using System;
using System.Text;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;
using Windows.Data.Json;

namespace novelgen.AI.Generation
{
    public class PolishResult
    {
        public string PolishedText;
        public AiReviewReport Report;
    }

    /// <summary>阶段⑥：审查 + 润色。</summary>
    public class Stage4_AiReviewPolisher
    {
        public async Task<PolishResult> PolishAsync(Novel novel, ChapterOutline outline, string draft, StageContext ctx)
        {
            var client = new ApiClient(ctx.PolishProfile ?? ctx.Profile);
            var worldSummary = SummarizeWorld(novel.WorldSetting);
            var outlineJson = SerializeOutline(outline);
            var prevSummary = SummarizePrevious(novel);
            var user = PromptRegistry.User_Polish(worldSummary, outlineJson, prevSummary, draft);

            string raw = await client.ChatAsync(PromptRegistry.System_Polish, user, ctx.Ct);
            return Parse(raw);
        }

        public static PolishResult Parse(string raw)
        {
            var result = new PolishResult();
            if (string.IsNullOrEmpty(raw))
            {
                result.PolishedText = "";
                result.Report = new AiReviewReport { QualityScore = 0, Summary = "（空响应）" };
                return result;
            }

            // 切分 ===POLISHED=== ... ===END=== 和 ===REPORT=== ... ===END===
            string polished = ExtractSection(raw, "===POLISHED===");
            string reportJson = ExtractSection(raw, "===REPORT===");
            result.PolishedText = string.IsNullOrEmpty(polished) ? raw : polished;
            result.Report = ParseReport(reportJson);
            return result;
        }

        private static string ExtractSection(string raw, string marker)
        {
            int start = raw.IndexOf(marker);
            if (start < 0) return null;
            start += marker.Length;
            int end = raw.IndexOf("===END===", start);
            if (end < 0)
            {
                // 模型漏写 ===END=== 时，用下一个 ===XXX=== 标记兜底，
                // 避免把 REPORT 的 JSON 混进润色后的正文
                end = raw.IndexOf("===", start);
                if (end < 0) end = raw.Length;
            }
            return raw.Substring(start, end - start).Trim();
        }

        public static AiReviewReport ParseReport(string json)
        {
            var r = new AiReviewReport();
            if (string.IsNullOrWhiteSpace(json)) { r.Summary = "（未生成报告）"; return r; }
            try
            {
                // 模型可能把报告包在 ```json 围栏里
                var jo = JsonObject.Parse(ApiClient.ExtractJsonPayload(json));
                r.QualityScore = (int)jo.GetNamedNumber("qualityScore", 0);
                r.Summary = jo.GetNamedString("summary", "");
                if (jo.ContainsKey("notes"))
                {
                    foreach (var v in jo.GetNamedArray("notes"))
                    {
                        var n = v.GetObject();
                        r.Notes.Add(new ReviewNote
                        {
                            Type = ParseEnum<ReviewType>(n.GetNamedString("type", "Other")),
                            Excerpt = n.GetNamedString("excerpt", ""),
                            Suggestion = n.GetNamedString("suggestion", ""),
                            Severity = ParseEnum<Severity>(n.GetNamedString("severity", "Minor")),
                        });
                    }
                }
                if (jo.ContainsKey("issues"))
                {
                    foreach (var v in jo.GetNamedArray("issues"))
                    {
                        var n = v.GetObject();
                        r.Issues.Add(new ConsistencyIssue
                        {
                            Type = ParseEnum<IssueType>(n.GetNamedString("type", "Other")),
                            Severity = ParseEnum<Severity>(n.GetNamedString("severity", "Minor")),
                            Description = n.GetNamedString("description", ""),
                            Suggestion = n.GetNamedString("suggestion", ""),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                r.Summary = "（报告解析失败）" + ex.Message;
            }
            return r;
        }

        private static T ParseEnum<T>(string s) where T : struct
        {
            T def = default(T);
            if (string.IsNullOrEmpty(s)) return def;
            T v;
            return Enum.TryParse<T>(s, true, out v) ? v : def;
        }

        private static string SummarizeWorld(WorldSetting ws)
        {
            if (ws == null) return "（无）";
            var sb = new StringBuilder();
            sb.AppendLine(ws.Synopsis);
            foreach (var c in ws.Characters ?? new System.Collections.Generic.List<Character>()) sb.AppendLine($"- {c.Name}：{c.Personality} | 目标：{c.Goal}");
            foreach (var ru in ws.Rules ?? new System.Collections.Generic.List<WorldRule>()) sb.AppendLine($"- [规则] {ru.Description}");
            return sb.ToString();
        }

        private static string SerializeOutline(ChapterOutline co)
        {
            if (co == null) return "（无）";
            return $"标题：{co.Title}\n梗概：{co.Synopsis}\n节拍：{string.Join(" / ", co.KeyBeats ?? new string[0])}";
        }

        private static string SummarizePrevious(Novel novel)
        {
            if (novel.Chapters == null) return "（无）";
            var sb = new StringBuilder();
            int n = 0;
            for (int i = novel.Chapters.Count - 1; i >= 0 && n < 2; i--)
            {
                var c = novel.Chapters[i];
                if (c.Status == ChapterStatus.Accepted || c.Status == ChapterStatus.Polished)
                {
                    var src = c.PolishedContent ?? c.Content ?? "";
                    sb.AppendLine($"第 {c.Order} 章 {c.Outline?.Title}：{(src.Length > 200 ? src.Substring(0, 200) + "…" : src)}");
                    n++;
                }
            }
            return sb.ToString();
        }
    }
}
