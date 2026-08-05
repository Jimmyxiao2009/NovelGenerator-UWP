using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using novelgen.Models;
using Windows.Data.Json;

namespace novelgen.AI
{
    /// <summary>
    /// OpenAI 兼容 + Claude 原生 API 客户端。
    /// 结构仿 Lumina.Agent 的 PlaaApiClient 但更精简。
    /// 支持真 SSE 流式、瞬时错误自动重试（指数退避 + Retry-After）。
    /// </summary>
    public class ApiClient
    {
        // 全局共享 HttpClient：批量生成会创建数百个 ApiClient 实例，
        // 每实例一个 HttpClient 会耗尽套接字/句柄。
        // HttpClient.Timeout 约束的是【整个】操作（含读响应体），即使用 ResponseHeadersRead 也一样；
        // 长章节流式输出常需 60-180s，固定 300s 会在流读到一半时抛 TaskCanceledException 丢失正文。
        // 因此关闭全局超时（InfiniteTimeSpan），改为每次尝试用链接令牌强制单次超时，见 PerAttemptTimeout。
        private static readonly HttpClient _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // 单次请求的超时上限：仅约束一次尝试，重试会各自重新计时；不再约束整本书的总生成时长。
        private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(300);

        private const int MaxRetries = 3;

        public string ProviderType { get; set; } = "openai";
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "";
        public int MaxContextTokens { get; set; } = 32000;
        /// <summary>单次回复的最大输出 token（Claude 必填；2500 字中文章节约需 4000+）</summary>
        public int MaxOutputTokens { get; set; } = 8192;

        public ApiClient() { }
        public ApiClient(ApiProfile p)
        {
            ProviderType = p?.ProviderType ?? "openai";
            BaseUrl = p?.Url ?? "";
            ApiKey = p?.ApiKey ?? "";
            Model = p?.Model ?? "";
            MaxContextTokens = ModelContextLimits.GetEffectiveLimit(p);
            if (p != null && p.MaxOutputTokens > 0) MaxOutputTokens = p.MaxOutputTokens;
        }

        // ── 非流式 (包装流式回调) ─────────────────────────────────
        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            await StreamWithCallbackAsync(systemPrompt, userPrompt, t => sb.Append(t), ct);
            return sb.ToString();
        }

        // ── 流式（回调模式，兼容 C# 7.2）。带瞬时错误重试：
        //    仅在尚未向调用方吐出任何内容时才重试，避免内容重复。 ──────
        public async Task StreamWithCallbackAsync(
            string systemPrompt, string userPrompt,
            Action<string> onChunk, CancellationToken ct = default)
        {
            var url = ResolveChatUrl();
            string body;
            if (ProviderType == "claude")
            {
                body = ClaudeBody(systemPrompt, userPrompt, true);
            }
            else
            {
                body = OpenAIBody(systemPrompt, userPrompt, true);
            }

            for (int attempt = 0; ; attempt++)
            {
                bool emitted = false;
                try
                {
                    await StreamOnceAsync(url, body, t => { emitted = true; onChunk(t); }, ct);
                    return;
                }
                catch (Exception ex) when (!emitted && attempt < MaxRetries
                                           && !ct.IsCancellationRequested && IsTransient(ex))
                {
                    var delay = RetryDelay(ex, attempt);
                    System.Diagnostics.Debug.WriteLine(
                        $"[ApiClient] 瞬时错误（第 {attempt + 1} 次重试，{delay.TotalSeconds:0}s 后）: {ex.Message}");
                    await Task.Delay(delay, ct);
                }
            }
        }

        private async Task StreamOnceAsync(string url, string body, Action<string> onChunk, CancellationToken ct)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiClient] POST {url} model={Model} provider={ProviderType}");

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            AddAuthHeaders(req);

            // 每次尝试的超时：作为「空闲/读取超时」——每读到一段数据就重置计时，
            // 这样长流不会被误杀，但卡住不动的流仍会在 PerAttemptTimeout 后失败。
            // 链接调用方 ct：真用户取消时 linked.Token 也会取消，且 ct.IsCancellationRequested 为 true，
            // 上层重试过滤器据此区分「超时(可重试)」与「用户取消(不重试)」。
            using (var timeoutCts = new CancellationTokenSource(PerAttemptTimeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
            {
                var attemptCt = linked.Token;
                void ResetIdleTimeout()
                {
                    try { timeoutCts.CancelAfter(PerAttemptTimeout); } catch { /* 已释放则忽略 */ }
                }

                using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, attemptCt))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var err = await resp.Content.ReadAsStringAsync();
                        throw new ApiRequestException(
                            (int)resp.StatusCode, ParseRetryAfter(resp),
                            $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\nURL: {url}\nBody: {Truncate(err ?? "", 300)}");
                    }

                    var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
                    using (var stream = await resp.Content.ReadAsStreamAsync())
                    {
                        if (mediaType.IndexOf("event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // 真 SSE：逐行读取，逐 token 回调
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                string ln;
                                while ((ln = await reader.ReadLineAsync()) != null)
                                {
                                    attemptCt.ThrowIfCancellationRequested();
                                    ResetIdleTimeout();   // 收到一行就重置空闲计时
                                    var line = ln.TrimEnd('\r');
                                    if (!line.StartsWith("data:")) continue;
                                    var data = line.Substring(5).TrimStart(' ');
                                    if (data == "[DONE]") return;
                                    var token = ExtractDelta(data, ProviderType == "claude");
                                    if (!string.IsNullOrEmpty(token)) onChunk(token);
                                }
                            }
                            return;
                        }

                        // 非 SSE 响应（网关可能忽略 stream 参数）：整体读取后解析
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            var raw = await reader.ReadToEndAsync();
                            System.Diagnostics.Debug.WriteLine($"[ApiClient] non-SSE bodyLen={raw?.Length ?? 0} preview: {Truncate(raw, 400)}");
                            if (string.IsNullOrEmpty(raw)) return;

                            if (raw.StartsWith("data:") || raw.Contains("\ndata:"))
                            {
                                foreach (var l in raw.Split('\n'))
                                {
                                    attemptCt.ThrowIfCancellationRequested();
                                    var line = l.TrimEnd('\r');
                                    if (!line.StartsWith("data:")) continue;
                                    var data = line.Substring(5).TrimStart(' ');
                                    if (data == "[DONE]") return;
                                    var token = ExtractDelta(data, ProviderType == "claude");
                                    if (!string.IsNullOrEmpty(token)) onChunk(token);
                                }
                            }
                            else
                            {
                                var content = ExtractFullContent(raw, ProviderType == "claude");
                                if (!string.IsNullOrEmpty(content)) onChunk(content);
                            }
                        }
                    }
                }
            }
        }

        private static int ParseRetryAfter(HttpResponseMessage resp)
        {
            var ra = resp.Headers.RetryAfter;
            if (ra == null) return 0;
            if (ra.Delta.HasValue) return (int)ra.Delta.Value.TotalSeconds;
            if (ra.Date.HasValue) return (int)(ra.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            return 0;
        }

        private static bool IsTransient(Exception ex)
        {
            var are = ex as ApiRequestException;
            if (are != null) return are.IsTransient;
            if (ex is TaskCanceledException) return true;   // 请求超时（用户取消已在过滤器中排除）
            if (ex is HttpRequestException) return true;    // 网络瞬断 / DNS / 连接重置
            if (ex is IOException) return true;             // 流中断
            return false;
        }

        private static TimeSpan RetryDelay(Exception ex, int attempt)
        {
            var are = ex as ApiRequestException;
            if (are != null && are.RetryAfterSeconds > 0)
                return TimeSpan.FromSeconds(Math.Min(are.RetryAfterSeconds, 60));
            // 指数退避并封顶 60s：当前 MaxRetries=3 即 2/4/8s；即便日后调大 MaxRetries 也不会失控。
            return TimeSpan.FromSeconds(Math.Min(60, 2 * Math.Pow(2, attempt))); // 2, 4, 8, …（≤60）
        }

        private static string Truncate(string s, int n)
            => string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(0, n) + "…" : s);

        private static string ExtractFullContent(string json, bool isClaude)
        {
            // 优先正规 JSON 解析；失败再退回字符串扫描
            try
            {
                var jo = JsonObject.Parse(json);
                if (isClaude)
                {
                    var arr = jo.GetNamedArray("content", null);
                    if (arr == null) return null;
                    var sb = new StringBuilder();
                    foreach (var item in arr)
                    {
                        if (item.ValueType != JsonValueType.Object) continue;
                        var o = item.GetObject();
                        var v = o.GetNamedValue("text", null);
                        if (v != null && v.ValueType == JsonValueType.String) sb.Append(v.GetString());
                    }
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                else
                {
                    var choices = jo.GetNamedArray("choices", null);
                    if (choices == null || choices.Count == 0) return null;
                    var ch = choices.GetObjectAt(0);
                    var msg = ch.GetNamedObject("message", null);
                    if (msg != null)
                    {
                        var v = msg.GetNamedValue("content", null);
                        if (v != null && v.ValueType == JsonValueType.String) return v.GetString();
                    }
                    var t = ch.GetNamedValue("text", null);
                    if (t != null && t.ValueType == JsonValueType.String) return t.GetString();
                    return null;
                }
            }
            catch
            {
                return FallbackExtract(json, isClaude);
            }
        }

        // 字符串扫描回退（响应不是严格 JSON 时）
        private static string FallbackExtract(string json, bool isClaude)
        {
            try
            {
                var key = isClaude ? "\"text\"" : "\"content\"";
                var idx = json.IndexOf(key);
                if (idx < 0) return null;
                return ReadJsonStringValue(json, idx + key.Length);
            }
            catch { return null; }
        }

        // 从 json 的 fromPos 开始跳过 ":whitespace" 后读一个完整的 JSON 字符串值（含 \" \\ \n \r \t 解码）
        private static string ReadJsonStringValue(string json, int fromPos)
        {
            int i = fromPos;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n' || json[i] == '\r' || json[i] == ':')) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == '"') { sb.Append('"'); i += 2; continue; }
                    if (n == '\\') { sb.Append('\\'); i += 2; continue; }
                    if (n == '/') { sb.Append('/'); i += 2; continue; }
                    if (n == 'n') { sb.Append('\n'); i += 2; continue; }
                    if (n == 'r') { sb.Append('\r'); i += 2; continue; }
                    if (n == 't') { sb.Append('\t'); i += 2; continue; }
                    if (n == 'b') { sb.Append('\b'); i += 2; continue; }
                    if (n == 'f') { sb.Append('\f'); i += 2; continue; }
                    if (n == 'u' && i + 5 < json.Length)
                    {
                        int code;
                        if (int.TryParse(json.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                        {
                            sb.Append((char)code);
                            i += 6;
                            continue;
                        }
                    }
                    sb.Append(c); i++;
                }
                else if (c == '"')
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c); i++;
                }
            }
            return null;
        }

        // ── JSON 解析（一次性） ─────────────────────────────────────
        public async Task<string> ChatJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            // 在 user prompt 末尾要求 JSON 输出
            var augmented = userPrompt + "\n\n【硬性要求】只输出合法 JSON，不要任何解释或 Markdown 代码块。";
            var raw = await ChatAsync(systemPrompt, augmented, ct);
            return ExtractJsonPayload(raw);
        }

        // ── 连接测试：发一句 "ping"，用时与返回长度作为健康度 ───────────
        public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
        {
            var start = DateTime.UtcNow;
            try
            {
                var url = ResolveChatUrl();
                string body;
                if (ProviderType == "claude")
                {
                    body = ClaudeBody("", "ping", false);
                }
                else
                {
                    body = OpenAIBody("", "ping", false);
                }

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                AddAuthHeaders(req);

                // 全局 HttpClient 已关闭超时，这里用链接令牌为连接测试单独设上限。
                using (var timeoutCts = new CancellationTokenSource(PerAttemptTimeout))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, linked.Token))
                {
                    var elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                    if (!resp.IsSuccessStatusCode)
                    {
                        var errText = await resp.Content.ReadAsStringAsync();
                        return new ConnectionTestResult
                        {
                            Ok = false,
                            StatusCode = (int)resp.StatusCode,
                            Message = "HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase,
                            Detail = errText.Length > 200 ? errText.Substring(0, 200) : errText,
                            ElapsedMs = elapsed
                        };
                    }
                    var raw = await resp.Content.ReadAsStringAsync();
                    return new ConnectionTestResult
                    {
                        Ok = true,
                        StatusCode = (int)resp.StatusCode,
                        Message = "连接成功",
                        Detail = "模型: " + Model,
                        ElapsedMs = elapsed,
                        TokensApprox = EstimateTokens(raw)
                    };
                }
            }
            catch (Exception ex)
            {
                return new ConnectionTestResult
                {
                    Ok = false,
                    StatusCode = 0,
                    Message = ex.GetType().Name,
                    Detail = ex.Message,
                    ElapsedMs = (int)(DateTime.UtcNow - start).TotalMilliseconds
                };
            }
        }

        private static int EstimateTokens(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return Core.TokenEstimator.Estimate(s);
        }

        /// <summary>剥离 Markdown 代码围栏。容忍围栏前后混有说明文字（如"好的，以下是 JSON：```json …```"）。</summary>
        public static string StripCodeFence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            var fenceStart = s.IndexOf("```");
            if (fenceStart >= 0)
            {
                var contentStart = s.IndexOf('\n', fenceStart);
                if (contentStart > 0)
                {
                    var fenceEnd = s.IndexOf("```", contentStart);
                    if (fenceEnd > contentStart)
                        return s.Substring(contentStart + 1, fenceEnd - contentStart - 1).Trim();
                    return s.Substring(contentStart + 1).Trim(); // 只有起始围栏
                }
            }
            return s;
        }

        /// <summary>从 AI 回复中提取 JSON 载荷：剥围栏后，截取首个 '{'/'[' 到最后配对符号之间的内容。</summary>
        public static string ExtractJsonPayload(string s)
        {
            s = StripCodeFence(s);
            if (string.IsNullOrEmpty(s)) return s;
            if (s.StartsWith("{") || s.StartsWith("[")) return s;
            int objStart = s.IndexOf('{');
            int arrStart = s.IndexOf('[');
            int start = objStart < 0 ? arrStart : (arrStart < 0 ? objStart : Math.Min(objStart, arrStart));
            if (start < 0) return s;
            char close = s[start] == '{' ? '}' : ']';
            int end = s.LastIndexOf(close);
            if (end > start) return s.Substring(start, end - start + 1).Trim();
            return s;
        }

        // ── Internals ──────────────────────────────────────────────
        private string ResolveChatUrl()
        {
            var b = (BaseUrl ?? "").Trim();
            if (b.Length == 0)
                throw new InvalidOperationException("API Profile 的 URL 为空，请先在「API 设置」中配置。");
            b = b.TrimEnd('/');
            if (ProviderType == "claude")
            {
                if (b.EndsWith("/messages")) return b;
                if (b.EndsWith("/v1")) return b + "/messages";
                return b + "/v1/messages";
            }
            if (b.EndsWith("/chat/completions")) return b;
            if (b.EndsWith("/v1")) return b + "/chat/completions";
            return b + "/v1/chat/completions";
        }

        private void AddAuthHeaders(HttpRequestMessage req)
        {
            if (ProviderType == "claude")
            {
                req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            }
            else
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ApiKey);
            }
        }

        private string OpenAIBody(string sys, string user, bool stream)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"model\":\"{0}\",", Esc(Model));
            sb.Append(stream ? "\"stream\":true," : "\"stream\":false,");
            sb.Append("\"messages\":[");
            if (!string.IsNullOrEmpty(sys))
                sb.AppendFormat("{{\"role\":\"system\",\"content\":{0}}},", JsonString(sys));
            sb.AppendFormat("{{\"role\":\"user\",\"content\":{0}}}", JsonString(user));
            sb.Append("]}");
            return sb.ToString();
        }

        private string ClaudeBody(string sys, string user, bool stream)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"model\":\"{0}\",", Esc(Model));
            sb.AppendFormat("\"max_tokens\":{0},", MaxOutputTokens > 0 ? MaxOutputTokens : 8192);
            if (stream) sb.Append("\"stream\":true,");
            sb.Append("\"messages\":[");
            sb.AppendFormat("{{\"role\":\"user\",\"content\":{0}}}", JsonString(user));
            sb.Append("]");
            if (!string.IsNullOrEmpty(sys))
                sb.AppendFormat(",\"system\":{0}", JsonString(sys));
            sb.Append("}");
            return sb.ToString();
        }

        private static string Esc(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        private static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static string ExtractDelta(string data, bool isClaude)
        {
            try
            {
                var jo = JsonObject.Parse(data);
                if (isClaude)
                {
                    // content_block_delta: {"type":"content_block_delta","delta":{"type":"text_delta","text":"..."}}
                    var delta = jo.GetNamedObject("delta", null);
                    if (delta != null)
                    {
                        var v = delta.GetNamedValue("text", null);
                        if (v != null && v.ValueType == JsonValueType.String) return v.GetString();
                    }
                    return null;
                }
                else
                {
                    var choices = jo.GetNamedArray("choices", null);
                    if (choices == null || choices.Count == 0) return null;
                    var ch = choices.GetObjectAt(0);
                    // 兼容流式 delta 与非流式完整 message
                    var d = ch.GetNamedObject("delta", null);
                    if (d != null)
                    {
                        var v = d.GetNamedValue("content", null);
                        if (v != null && v.ValueType == JsonValueType.String) return v.GetString();
                    }
                    var msg = ch.GetNamedObject("message", null);
                    if (msg != null)
                    {
                        var v = msg.GetNamedValue("content", null);
                        if (v != null && v.ValueType == JsonValueType.String) return v.GetString();
                    }
                    var t = ch.GetNamedValue("text", null);
                    if (t != null && t.ValueType == JsonValueType.String) return t.GetString();
                    return null;
                }
            }
            catch { return null; }
        }
    }

    /// <summary>HTTP 层错误，携带状态码与 Retry-After，供重试逻辑判断。</summary>
    public class ApiRequestException : HttpRequestException
    {
        public int StatusCode { get; }
        public int RetryAfterSeconds { get; }
        public ApiRequestException(int statusCode, int retryAfterSeconds, string message) : base(message)
        {
            StatusCode = statusCode;
            RetryAfterSeconds = retryAfterSeconds;
        }
        public bool IsTransient
            => StatusCode == 408 || StatusCode == 429 || (StatusCode >= 500 && StatusCode <= 504);
    }

    /// <summary>连接测试结果。</summary>
    [DataContract]
    public class ConnectionTestResult
    {
        [DataMember] public bool Ok { get; set; }
        [DataMember] public int StatusCode { get; set; }
        [DataMember] public string Message { get; set; } = "";
        [DataMember] public string Detail { get; set; } = "";
        [DataMember] public int ElapsedMs { get; set; }
        [DataMember] public int TokensApprox { get; set; }
    }

    /// <summary>JSON 工具：DataContractJsonSerializer 之外的轻量反序列化。</summary>
    public static class JsonHelper
    {
        public static T Parse<T>(string json) where T : class
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                return (T)ser.ReadObject(ms);
        }
    }
}
