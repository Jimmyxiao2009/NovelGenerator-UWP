using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.AI.Generation
{
    /// <summary>
    /// 批量生成一卷中的所有章节。
    /// 每个章节由独立的 agent 并行生成，卷完成后由 connector agent 平滑过渡并修一致性问题。
    /// 失败语义：单章失败只标记该章并继续其余章节，阶段结束后统一抛错（已完成的章节保留，可断点续跑）；
    /// 用户取消通过 OperationCanceledException 正常上抛，不会被吞掉而误报成功。
    /// </summary>
    public class VolumeBatchGenerator
    {
        private readonly Stage3_ChapterGenerator _chapterGen = new Stage3_ChapterGenerator();
        private readonly Stage4_AiReviewPolisher _polisher = new Stage4_AiReviewPolisher();

        /// <summary>并行生成 + 连接 + 并行润色一卷的所有章节。</summary>
        public async Task GenerateVolumeAsync(Novel novel, int volumeIndex, StageContext ctx, int concurrency = 3)
        {
            var sk = novel.Skeleton;
            if (sk?.Volumes == null || volumeIndex >= sk.Volumes.Count)
                throw new InvalidOperationException("卷不存在");

            var vol = sk.Volumes[volumeIndex];
            var chapters = novel.Chapters.Where(c => c.VolumeId == vol.Id).OrderBy(c => c.Order).ToList();
            if (chapters.Count == 0) return;

            Report(ctx, "start", $"卷 {volumeIndex + 1}/{sk.Volumes.Count}「{vol.Title}」({chapters.Count}章)", 0, volumeIndex, sk.Volumes.Count);

            using (var semaphore = new SemaphoreSlim(concurrency))
            {
                var slots = new SlotPool(concurrency);
                int volCount = sk.Volumes.Count;

                // ── ① 并行生成 ─────────────────────────────────────────
                var toDraft = chapters.Where(c => string.IsNullOrEmpty(c.Content)).ToList();
                int draftTotal = Math.Max(1, toDraft.Count);
                int draftedCount = 0;

                var draftErrors = await RunPooledAsync(toDraft, semaphore, slots, ctx, async (ch, slot) =>
                {
                    await DraftChapterAsync(novel, ch, ctx, slot, volumeIndex, volCount,
                        () => (double)Interlocked.Increment(ref draftedCount) / draftTotal * 80);
                });
                ctx.Ct.ThrowIfCancellationRequested();
                ThrowIfAnyFailed(draftErrors, "起草");

                // ── ② 连接 ────────────────────────────────────────────
                Report(ctx, "connecting", $"卷{volumeIndex + 1} 连接 agent 过渡中…", 80, volumeIndex, volCount);
                await ConnectVolumeAsync(novel, vol, chapters, ctx);
                ctx.Ct.ThrowIfCancellationRequested();

                // ── ③ 并行润色 ─────────────────────────────────────────
                var toPolish = chapters.Where(c => string.IsNullOrEmpty(c.PolishedContent)).ToList();
                int polishTotal = Math.Max(1, toPolish.Count);
                int polishedCount = 0;

                var polishErrors = await RunPooledAsync(toPolish, semaphore, slots, ctx, async (ch, slot) =>
                {
                    await PolishChapterAsync(novel, ch, ctx, slot, volumeIndex, volCount,
                        () => 80 + (double)Interlocked.Increment(ref polishedCount) / polishTotal * 20);
                });
                ctx.Ct.ThrowIfCancellationRequested();
                ThrowIfAnyFailed(polishErrors, "润色");

                Report(ctx, "done", $"卷{volumeIndex + 1}「{vol.Title}」完成", 100, volumeIndex, volCount);
            }
        }

        /// <summary>
        /// 全书批量生成：所有卷的章节共用一个并发池一起起草，最大化 agent 利用率；
        /// 然后各卷并行做连接过渡；最后所有章节共用并发池一起润色。
        /// 相比逐卷串行，不会因为某一卷章节数少于并发数而让槽位空转。
        /// </summary>
        public async Task GenerateAllVolumesAsync(Novel novel, StageContext ctx, int concurrency = 3)
        {
            var sk = novel.Skeleton;
            if (sk?.Volumes == null || sk.Volumes.Count == 0)
                throw new InvalidOperationException("没有卷");

            int volCount = sk.Volumes.Count;

            // 收集所有有章节的卷，并让每张卷卡片进入「生成中」
            var volumes = new List<VolumeWork>();
            for (int vi = 0; vi < sk.Volumes.Count; vi++)
            {
                var vol = sk.Volumes[vi];
                var chs = novel.Chapters.Where(c => c.VolumeId == vol.Id).OrderBy(c => c.Order).ToList();
                if (chs.Count == 0) continue;
                volumes.Add(new VolumeWork { Index = vi, Volume = vol, Chapters = chs });
                Report(ctx, "start", $"卷 {vi + 1}「{vol.Title}」({chs.Count}章)", -1, vi, volCount);
            }
            if (volumes.Count == 0) return;

            var semaphore = new SemaphoreSlim(concurrency);
            var slots = new SlotPool(concurrency);
            try
            {

            // ── ① 全书并行起草 ───────────────────────────────────
            var toDraft = new List<ChapterWork>();
            foreach (var v in volumes)
                foreach (var c in v.Chapters)
                    if (string.IsNullOrEmpty(c.Content))
                        toDraft.Add(new ChapterWork { V = v, Ch = c });

            int draftTotal = Math.Max(1, toDraft.Count);
            int draftedCount = 0;

            var draftErrors = await RunPooledAsync(toDraft, semaphore, slots, ctx, async (w, slot) =>
            {
                await DraftChapterAsync(novel, w.Ch, ctx, slot, w.V.Index, volCount,
                    () => (double)Interlocked.Increment(ref draftedCount) / draftTotal * 70);
            });
            ctx.Ct.ThrowIfCancellationRequested();
            ThrowIfAnyFailed(draftErrors, "起草");

            // ── ② 各卷并行连接过渡 ───────────────────────────────
            var connectErrors = await RunPooledAsync(volumes, semaphore, slots, ctx, async (v, slot) =>
            {
                Report(ctx, "connecting", $"卷{v.Index + 1} 连接过渡中…", -1, v.Index, volCount);
                await ConnectVolumeAsync(novel, v.Volume, v.Chapters, ctx);
            }, useSlot: false);
            ctx.Ct.ThrowIfCancellationRequested();
            // 连接失败不阻断流程（保留原始草稿即可），但要让失败可见而非静默吞掉
            if (connectErrors != null && connectErrors.Count > 0)
            {
                Report(ctx, "error", $"有 {connectErrors.Count} 卷连接过渡失败（已保留原始草稿）：{connectErrors[0].Error.Message}", -1, -1, volCount);
                foreach (var ce in connectErrors)
                    System.Diagnostics.Debug.WriteLine($"[VolumeConnector] 连接失败: {ce.Error}");
            }
            Report(ctx, "progress", "连接完成，开始润色…", 80, -1, volCount);

            // ── ③ 全书并行润色 ───────────────────────────────────
            var toPolish = new List<ChapterWork>();
            foreach (var v in volumes)
                foreach (var c in v.Chapters)
                    if (string.IsNullOrEmpty(c.PolishedContent))
                        toPolish.Add(new ChapterWork { V = v, Ch = c });

            int polishTotal = Math.Max(1, toPolish.Count);
            int polishedCount = 0;

            // 每卷待润色计数：某卷全部润色完即标记「完成」
            var polishRemaining = new Dictionary<int, int>();
            foreach (var v in volumes)
            {
                int need = v.Chapters.Count(c => string.IsNullOrEmpty(c.PolishedContent));
                polishRemaining[v.Index] = need;
                if (need == 0) Report(ctx, "done", $"卷{v.Index + 1}「{v.Volume.Title}」完成", -1, v.Index, volCount);
            }

            var polishErrors = await RunPooledAsync(toPolish, semaphore, slots, ctx, async (w, slot) =>
            {
                await PolishChapterAsync(novel, w.Ch, ctx, slot, w.V.Index, volCount,
                    () => 80 + (double)Interlocked.Increment(ref polishedCount) / polishTotal * 20);

                int left;
                lock (polishRemaining) { left = --polishRemaining[w.V.Index]; }
                if (left == 0) Report(ctx, "done", $"卷{w.V.Index + 1}「{w.V.Volume.Title}」完成", -1, w.V.Index, volCount);
            });
            ctx.Ct.ThrowIfCancellationRequested();
            ThrowIfAnyFailed(polishErrors, "润色");

            Report(ctx, "alldone", "全部卷生成完毕", 100, -1, volCount);
            }
            finally
            {
                semaphore.Dispose();
            }
        }

        // ── 单章起草 / 润色（带状态回滚与互斥写入） ────────────────────
        private async Task DraftChapterAsync(Novel novel, Chapter ch, StageContext ctx,
            int slot, int volIdx, int volCount, Func<double> nextPercent)
        {
            Report(ctx, "drafting", $"第{ch.Order}章生成中…", -1, volIdx, volCount, ch.Order, slot, ch.Outline?.Title);
            lock (Core.DataManager.SyncRoot) { ch.Status = ChapterStatus.Drafting; }
            try
            {
                var client = new ApiClient(ctx.Profile);
                var content = await GenerateChapterInternal(novel, ch, client, ctx.Ct);
                // 与保存时的序列化快照互斥，避免序列化途中改写对象图
                lock (Core.DataManager.SyncRoot)
                {
                    ch.Content = content;
                    ch.WordCount = CountWords(content);
                    ch.DraftedAt = DateTime.Now;
                    ch.Status = ChapterStatus.Drafted;
                }
                Report(ctx, "drafted", $"第{ch.Order}章完成（{ch.WordCount}字）", nextPercent(), volIdx, volCount, ch.Order, slot, ch.Outline?.Title);
                ctx.OnChapterDone?.Invoke(ch);
            }
            catch
            {
                // 失败/取消的章节不能卡在 Drafting，恢复为可重试状态
                lock (Core.DataManager.SyncRoot)
                {
                    ch.Status = string.IsNullOrEmpty(ch.Content) ? ChapterStatus.Pending : ChapterStatus.Drafted;
                }
                throw;
            }
        }

        private async Task PolishChapterAsync(Novel novel, Chapter ch, StageContext ctx,
            int slot, int volIdx, int volCount, Func<double> nextPercent)
        {
            Report(ctx, "polishing", $"第{ch.Order}章润色中…", -1, volIdx, volCount, ch.Order, slot, ch.Outline?.Title);
            lock (Core.DataManager.SyncRoot) { ch.Status = ChapterStatus.Polishing; }
            try
            {
                var result = await _polisher.PolishAsync(novel, ch.Outline, ch.Content,
                    new StageContext { Profile = ctx.PolishProfile ?? ctx.Profile, Ct = ctx.Ct });
                lock (Core.DataManager.SyncRoot)
                {
                    ch.PolishedContent = result.PolishedText;
                    ch.AiReview = result.Report;
                    ch.Status = ChapterStatus.Polished;
                    ch.PolishedAt = DateTime.Now;
                }
                Report(ctx, "polished", $"第{ch.Order}章润色完成", nextPercent(), volIdx, volCount, ch.Order, slot, ch.Outline?.Title);
                ctx.OnChapterDone?.Invoke(ch);
            }
            catch
            {
                lock (Core.DataManager.SyncRoot)
                {
                    ch.Status = string.IsNullOrEmpty(ch.PolishedContent) ? ChapterStatus.Drafted : ChapterStatus.Polished;
                }
                throw;
            }
        }

        private static void ThrowIfAnyFailed(List<ChapterError> errors, string phase)
        {
            if (errors == null || errors.Count == 0) return;
            var orders = string.Join("、", errors.Select(e => e.Order > 0 ? $"第{e.Order}章" : "?").Distinct());
            throw new InvalidOperationException(
                $"{phase}阶段有 {errors.Count} 章失败（{orders}），其余章节已完成并保存，可重试继续。首个错误：{errors[0].Error.Message}",
                errors[0].Error);
        }

        /// <summary>把一批工作项放进共享并发池执行：先抢信号量，再借一个槽位作为 AgentId。
        /// 单项异常不再中止整批：收集后返回，由调用方决定如何上报。</summary>
        private static async Task<List<ChapterError>> RunPooledAsync<T>(
            IReadOnlyList<T> items, SemaphoreSlim semaphore, SlotPool slots, StageContext ctx,
            Func<T, int, Task> body, bool useSlot = true)
        {
            var errors = new ConcurrentQueue<ChapterError>();
            var tasks = new List<Task>(items.Count);
            foreach (var item in items)
            {
                var it = item;
                tasks.Add(Task.Run(async () =>
                {
                    try { await semaphore.WaitAsync(ctx.Ct); }
                    catch (OperationCanceledException) { return; }
                    int slot = useSlot ? slots.Take() : 0;
                    try
                    {
                        if (ctx.Ct.IsCancellationRequested) return;
                        await body(it, slot);
                    }
                    catch (OperationCanceledException) { /* 取消：调用方在 WhenAll 后统一 ThrowIfCancellationRequested */ }
                    catch (Exception ex)
                    {
                        int order = (it as ChapterWork)?.Ch?.Order ?? (it as Chapter)?.Order ?? 0;
                        errors.Enqueue(new ChapterError { Order = order, Error = ex });
                        Report(ctx, "error", order > 0 ? $"第{order}章失败: {ex.Message}" : $"失败: {ex.Message}", -1, -1, 0, order);
                    }
                    finally
                    {
                        if (useSlot) slots.Return(slot);
                        try { semaphore.Release(); } catch { }
                    }
                }));
            }
            await Task.WhenAll(tasks);
            return errors.ToList();
        }

        private sealed class ChapterError
        {
            public int Order;
            public Exception Error;
        }

        /// <summary>1..N 的槽位池，借出作为真实并发槽的 AgentId，用完归还。</summary>
        private sealed class SlotPool
        {
            private readonly ConcurrentQueue<int> _free = new ConcurrentQueue<int>();
            public SlotPool(int n) { for (int i = 1; i <= n; i++) _free.Enqueue(i); }
            public int Take() => _free.TryDequeue(out var id) ? id : 0;
            public void Return(int id) { if (id > 0) _free.Enqueue(id); }
        }

        private sealed class VolumeWork
        {
            public int Index;
            public VolumeOutline Volume;
            public List<Chapter> Chapters;
        }

        private sealed class ChapterWork
        {
            public VolumeWork V;
            public Chapter Ch;
        }

        private static void Report(StageContext ctx, string phase, string text, double percent, int volIdx, int volCount, int chOrder = 0, int agentId = 0, string chTitle = null)
        {
            ctx.Progress?.Report(new StageProgress
            {
                StageName = "VolumeBatch",
                Phase = phase,
                Text = text,
                Percent = percent,
                VolumeIndex = volIdx,
                VolumeCount = volCount,
                ChapterOrder = chOrder,
                AgentId = agentId,
                ChapterTitle = chTitle,
            });
        }

        // ── 连接 agent：读取卷内所有已生成草稿，修正过渡衔接 ──────────
        private async Task ConnectVolumeAsync(Novel novel, VolumeOutline vol, List<Chapter> chapters, StageContext ctx)
        {
            if (chapters.Count < 2) return;
            // 幂等：断点续跑时已接过桥的卷不再追加，避免章节尾部重复堆积过渡段
            if (chapters.All(c => c.BridgeApplied)) return;

            var connector = new ApiClient(ctx.Profile);
            var worldSummary = SummarizeWorld(novel.WorldSetting);
            var volumeContext = BuildVolumeContext(vol, chapters);

            var userPrompt = PromptRegistry.User_VolumeConnector(worldSummary, volumeContext);
            var raw = await connector.ChatJsonAsync(PromptRegistry.System_VolumeConnector, userPrompt, ctx.Ct);

            if (string.IsNullOrWhiteSpace(raw)) return;
            ApplyConnections(raw, chapters);
        }

        private string BuildVolumeContext(VolumeOutline vol, List<Chapter> chapters)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"卷标题：{vol.Title}");
            sb.AppendLine($"卷梗概：{vol.Synopsis}");
            sb.AppendLine($"高潮：{vol.Climax}");
            sb.AppendLine();
            for (int i = 0; i < chapters.Count; i++)
            {
                var ch = chapters[i];
                sb.AppendLine($"第 {ch.Order} 章「{ch.Outline?.Title}」");
                sb.AppendLine($"大纲：{ch.Outline?.Synopsis}");
                sb.AppendLine($"正文（前 300 字 + 后 200 字）：");
                sb.AppendLine(GetHead(ch.Content, 300));
                sb.AppendLine("…");
                sb.AppendLine(GetTail(ch.Content, 200));
                sb.AppendLine(new string('-', 40));
            }
            return sb.ToString();
        }

        private void ApplyConnections(string json, List<Chapter> chapters)
        {
            try
            {
                var jo = Windows.Data.Json.JsonObject.Parse(json);
                if (!jo.ContainsKey("transitions")) return;
                var arr = jo.GetNamedArray("transitions");
                lock (Core.DataManager.SyncRoot)
                {
                    foreach (var item in arr)
                    {
                        var t = item.GetObject();
                        var fromOrder = (int)t.GetNamedNumber("from", 0);
                        var bridge = t.GetNamedString("bridge", "");
                        if (string.IsNullOrEmpty(bridge)) continue;

                        // 给 from 章末追加过渡（幂等：每章只接一次桥）
                        var fromCh = chapters.FirstOrDefault(c => c.Order == fromOrder);
                        if (fromCh != null && !fromCh.BridgeApplied)
                        {
                            fromCh.Content = (fromCh.Content ?? "").TrimEnd() + "\n\n" + bridge;
                            fromCh.BridgeApplied = true;
                        }
                    }
                    // 无论 connector 是否给所有章节产出过渡，整卷标记已处理
                    foreach (var c in chapters) c.BridgeApplied = true;
                }
            }
            catch (Exception ex)
            {
                // connector 输出非法 JSON 时忽略（保留原始草稿），但记录以便诊断
                System.Diagnostics.Debug.WriteLine($"[VolumeConnector] invalid JSON: {ex.Message}");
            }
        }

        private async Task<string> GenerateChapterInternal(Novel novel, Chapter ch, ApiClient client, CancellationToken ct)
        {
            var worldSummary = SummarizeWorld(novel.WorldSetting);
            var outlineJson = SerializeOutline(ch.Outline);
            var prevSummary = SummarizePreviousChapters(novel, ch.Order);
            var rag = await PipelineHost.Retriever.RetrieveAsync(novel.WorldSetting,
                ch.Outline?.Title + " " + ch.Outline?.Synopsis + " " + string.Join(" ", ch.Outline?.KeyBeats ?? new string[0]),
                topK: 4);
            var user = PromptRegistry.User_Chapter(worldSummary, outlineJson, prevSummary, rag.ContextText, "");
            return await client.ChatAsync(Stage3_ChapterGenerator.BuildSystemPrompt(novel), user, ct);
        }

        private static string SummarizeWorld(WorldSetting ws)
        {
            if (ws == null) return "（无）";
            var sb = new StringBuilder();
            sb.AppendLine(ws.Synopsis);
            foreach (var c in ws.Characters ?? new List<Character>())
                sb.AppendLine($"- {c.Name}（{c.Role}）：{c.Personality}");
            foreach (var r in ws.Rules ?? new List<WorldRule>())
                sb.AppendLine($"- [规则] {r.Description}");
            return sb.ToString();
        }

        private static string SerializeOutline(ChapterOutline co)
        {
            if (co == null) return "（无）";
            var sb = new StringBuilder();
            sb.AppendLine($"标题：{co.Title}");
            sb.AppendLine($"梗概：{co.Synopsis}");
            sb.AppendLine($"氛围：{co.Mood}");
            sb.AppendLine("节拍：");
            foreach (var b in co.KeyBeats ?? new string[0]) sb.AppendLine("  - " + b);
            return sb.ToString();
        }

        private static string SummarizePreviousChapters(Novel novel, int currentOrder)
        {
            if (novel.Chapters == null || novel.Chapters.Count == 0) return "（无）";
            var sb = new StringBuilder();
            int n = 0;
            for (int i = novel.Chapters.Count - 1; i >= 0 && n < 2; i--)
            {
                var c = novel.Chapters[i];
                if (c.Order >= currentOrder) continue;
                if (c.Status == ChapterStatus.Accepted || c.Status == ChapterStatus.Polished || c.Status == ChapterStatus.Drafted)
                {
                    var txt = c.PolishedContent ?? c.Content;
                    sb.AppendLine($"第 {c.Order} 章 {c.Outline?.Title}（后 80 字）：{GetTail(txt, 80)}");
                    n++;
                }
            }
            return sb.ToString();
        }

        private static string GetHead(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > maxChars ? text.Substring(0, maxChars) + "…" : text;
        }

        private static string GetTail(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > maxChars ? "…" + text.Substring(text.Length - maxChars) : text;
        }

        private static int CountWords(string s) => Core.TokenEstimator.CountWords(s);
    }
}
