using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.AI.Generation;

namespace novelgen.Pipeline
{
    /// <summary>
    /// 单本小说的流水线状态机主类。
    /// 持有 <see cref="Novel"/>，暴露 9 个阶段的入口方法，
    /// 抛 4 类事件供 UI 订阅。
    /// 不直接做 AI 调用——所有 AI 工作委派给 <see cref="AI.Generation"/> 下的 Stage 服务。
    /// </summary>
    public class NovelPipeline : IPipelineEvents
    {
        public Novel Novel { get; private set; }

        // ── 事件 ─────────────────────────────────────────────────────
        public event EventHandler<PipelineStateChangedEventArgs> StateChanged;
        public event EventHandler<PipelineErrorEventArgs>        ErrorOccurred;
        public event EventHandler<ChapterStreamEventArgs>        ChapterStream;
        public event EventHandler<PipelineLogEventArgs>          Log;

        // ── 子服务（由宿主在 Initialize 时注入） ─────────────────────
        public Stage1_WorldSettingGenerator  WorldGen    { get; set; }
        public Stage2_SkeletonGenerator      SkeletonGen { get; set; }
        public Stage3_ChapterGenerator       ChapterGen  { get; set; }
        public Stage4_AiReviewPolisher       Polisher    { get; set; }
        public Stage5_Composer               Composer    { get; set; }
        public Stage6_Exporter               Exporter    { get; set; }
        public VolumeBatchGenerator          VolumeGenerator { get; set; } = new VolumeBatchGenerator();

        public NovelPipeline(Novel novel)
        {
            Novel = novel ?? new Novel();
        }

        public PipelineState State
        {
            get => Novel.State;
            private set => TransitionTo(value, "set");
        }

        public void TransitionTo(PipelineState newState, string reason = "")
        {
            var old = Novel.State;
            if (old == newState) return;
            Novel.State = newState;
            RaiseLog("info", $"State: {old} → {newState} ({reason})");
            StateChanged?.Invoke(this, new PipelineStateChangedEventArgs
            {
                OldState = old,
                NewState = newState,
                Novel = Novel,
                Reason = reason,
            });
        }

        public void RaiseLog(string level, string msg)
            => Log?.Invoke(this, new PipelineLogEventArgs { Level = level, Message = msg, Time = DateTime.Now });

        public void RaiseError(string stage, Exception ex)
        {
            // 走 TransitionTo 而非直接改字段，保证 StateChanged 订阅者能看到 Error 转换
            TransitionTo(PipelineState.Error, $"[{stage}] {ex.GetType().Name}");
            RaiseLog("error", $"[{stage}] {ex.Message}");
            ErrorOccurred?.Invoke(this, new PipelineErrorEventArgs
            {
                State = Novel.State,
                StageName = stage,
                Exception = ex,
            });
        }

        public void RaiseChapterStream(ChapterStreamEventArgs e)
            => ChapterStream?.Invoke(this, e);

        // ── ① 初始化 ────────────────────────────────────────────────
        /// <summary>书名占位符：用户未手填、AI 也未起名时的默认标题。</summary>
        public const string UntitledPlaceholder = "未命名小说";

        public static NovelPipeline Create(UserPromptLike prompt, string genre, string subGenre, string presetId, string title = null)
        {
            var novel = new Novel
            {
                // 标题独立于描述：用户手填则用其值，否则留占位符，待 ② 世界设定生成时由 AI 起名
                Title = string.IsNullOrWhiteSpace(title) ? UntitledPlaceholder : title.Trim(),
                Genre = genre,
                SubGenre = subGenre,
                StylePresetId = presetId,
                UserPrompt = prompt?.Text ?? "",
                State = PipelineState.Draft,
            };
            return new NovelPipeline(novel);
        }

        // ── ② 世界设定 ──────────────────────────────────────────────
        public async Task<WorldSetting> GenerateWorldSettingAsync(StageContext ctx)
        {
            TransitionTo(PipelineState.WorldSettingDraft, "进入②");
            try
            {
                var ws = await WorldGen.GenerateAsync(Novel, ctx);
                Novel.WorldSetting = ws;
                // AI 返回为空 / 非合法 JSON：不能当成有效世界设定继续推进（否则骨架会基于垃圾数据生成）
                if (ws == null || ws.ParseFailed)
                    throw new FormatException("世界设定生成失败：AI 返回为空或不是合法 JSON。" +
                        (ws != null && !string.IsNullOrEmpty(ws.Synopsis) ? "\n" + ws.Synopsis : ""));
                // 用户未手填标题时，采用 AI 起的书名（绝不回退到「截取描述」）
                if (string.IsNullOrWhiteSpace(Novel.Title) || Novel.Title == UntitledPlaceholder)
                {
                    if (!string.IsNullOrWhiteSpace(ws.Title)) Novel.Title = ws.Title.Trim();
                }
                await Core.DataManager.SaveNovelAsync(Novel);
                RaiseLog("info", "世界设定生成完毕");
                return ws;
            }
            catch (OperationCanceledException) { throw; } // 用户取消不是错误
            catch (Exception ex)
            {
                RaiseError("WorldSetting", ex);
                throw;
            }
        }

        // ── ③ 故事骨架 ──────────────────────────────────────────────
        public async Task<StorySkeleton> GenerateStorySkeletonAsync(StageContext ctx)
        {
            if (Novel.WorldSetting == null)
                throw new InvalidOperationException("WorldSetting 未生成，无法生成骨架");
            TransitionTo(PipelineState.SkeletonDraft, "进入③");
            try
            {
                var sk = await SkeletonGen.GenerateAsync(Novel, ctx);
                // AI 返回为空 / 非合法 JSON / 0 卷：不能当成有效骨架（否则生成 0 章的空书）
                if (sk == null || sk.ParseFailed)
                    throw new FormatException("故事骨架生成失败：AI 返回为空、不是合法 JSON 或未产出任何卷。");
                Novel.Skeleton = sk;
                // 把骨架中的章节占位放进 Chapters
                Novel.Chapters = new System.Collections.Generic.List<Chapter>();
                int order = 0;
                foreach (var vol in sk.Volumes)
                {
                    foreach (var co in vol.Chapters)
                    {
                        Novel.Chapters.Add(new Chapter
                        {
                            Order = ++order,
                            VolumeId = vol.Id,
                            Outline = co,
                            Status = ChapterStatus.Pending,
                        });
                    }
                }
                await Core.DataManager.SaveNovelAsync(Novel);
                RaiseLog("info", $"骨架生成完毕，共 {Novel.Chapters.Count} 章");
                return sk;
            }
            catch (OperationCanceledException) { throw; } // 用户取消不是错误
            catch (Exception ex)
            {
                RaiseError("Skeleton", ex);
                throw;
            }
        }

        // ── ④ Gate #1 ───────────────────────────────────────────────
        public async Task<PipelineSnapshot> SubmitReview1Async(UserReviewDecision decision)
        {
            // 自动留快照
            var snap = await CreateSnapshotAsync("WorldSetting+Skeleton", "auto-before-gate1");
            switch (decision.Action)
            {
                case ReviewAction.Accept:
                    TransitionTo(PipelineState.ChapterDrafting, "Gate1 Accept");
                    break;
                case ReviewAction.RequestRevision:
                    if (Novel.Skeleton == null)
                        TransitionTo(PipelineState.WorldSettingDraft, "Gate1 RequestRevision→世界");
                    else
                        TransitionTo(PipelineState.SkeletonDraft, "Gate1 RequestRevision→骨架");
                    break;
                case ReviewAction.Reject:
                    TransitionTo(PipelineState.WorldSettingDraft, "Gate1 Reject");
                    break;
                case ReviewAction.Edit:
                    // 用户已编辑世界/骨架（页面层处理具体编辑），这里仅推进
                    TransitionTo(PipelineState.ChapterDrafting, "Gate1 Edit→通过");
                    break;
            }
            await Core.DataManager.SaveNovelAsync(Novel);
            return snap;
        }

        // ── ⑤⑥ 批量生成：一卷内所有章节并行生成 + 连接 + 并行润色 ───
        public async Task GenerateVolumeBatchAsync(int volumeIndex, StageContext ctx, int concurrency = 3)
        {
            if (VolumeGenerator == null) VolumeGenerator = new VolumeBatchGenerator();
            TransitionTo(PipelineState.ChapterDrafting, $"开始批生成卷 {volumeIndex + 1}");

            try
            {
                await VolumeGenerator.GenerateVolumeAsync(Novel, volumeIndex, ctx, concurrency);
            }
            catch (OperationCanceledException)
            {
                // 取消时保留已完成的章节，但不把半成品状态报告为"完毕"
                await Core.DataManager.SaveNovelAsync(Novel);
                throw;
            }
            catch (Exception ex)
            {
                RaiseError($"Volume{volumeIndex + 1}", ex);
                throw;
            }
            await Core.DataManager.SaveNovelAsync(Novel);
            RaiseLog("info", $"卷 {volumeIndex + 1} 批生成完毕");
        }

        /// <summary>
        /// 全书批量生成：所有卷的章节共用一个并发池一起起草/润色，最大化 agent 利用率
        /// （不会因为单卷章节数少于并发数而让槽位空转）。进度通过 ctx.Progress 上报。
        /// </summary>
        public async Task GenerateAllVolumesBatchAsync(StageContext ctx, int concurrency = 3)
        {
            var vols = Novel.Skeleton?.Volumes;
            if (vols == null || vols.Count == 0)
                throw new InvalidOperationException("没有卷，请先生成骨架");

            if (VolumeGenerator == null) VolumeGenerator = new VolumeBatchGenerator();
            TransitionTo(PipelineState.ChapterDrafting, "开始全书批量生成");

            try
            {
                await VolumeGenerator.GenerateAllVolumesAsync(Novel, ctx, concurrency);
            }
            catch (OperationCanceledException)
            {
                // 取消时保留已完成的章节
                await Core.DataManager.SaveNovelAsync(Novel);
                throw;
            }
            catch (Exception ex)
            {
                RaiseError("AllVolumes", ex);
                throw;
            }

            await Core.DataManager.SaveNovelAsync(Novel);
            RaiseLog("info", "全部卷批生成完毕");
            TransitionTo(PipelineState.AwaitingReview2, "全部卷批生成完毕");
        }

        // ── ⑤⑥ 单章生成+润色（流式） ───────────────────────────────
        public async Task<Chapter> GenerateAndPolishChapterAsync(int chapterIndex, StageContext ctx)
        {
            if (Novel.Chapters == null || chapterIndex < 0 || chapterIndex >= Novel.Chapters.Count)
                throw new ArgumentOutOfRangeException(nameof(chapterIndex));
            var ch = Novel.Chapters[chapterIndex];
            TransitionTo(PipelineState.ChapterDrafting, $"开始生成第 {ch.Order} 章");

            try
            {
                // ⑤ 生成
                ch.Status = ChapterStatus.Drafting;
                ch.Content = await ChapterGen.GenerateAsync(Novel, ch.Outline, ctx);
                ch.WordCount = CountWords(ch.Content);
                ch.DraftedAt = DateTime.Now;
                ch.Status = ChapterStatus.Drafted;

                RaiseChapterStream(new ChapterStreamEventArgs
                {
                    ChapterId = ch.Id, ChapterIndex = ch.Order, Phase = "drafted", Chapter = ch,
                });

                // ⑥ 润色
                TransitionTo(PipelineState.ChapterPolishing, $"开始润色第 {ch.Order} 章");
                ch.Status = ChapterStatus.Polishing;
                var polishResult = await Polisher.PolishAsync(Novel, ch.Outline, ch.Content, ctx);
                ch.PolishedContent = polishResult.PolishedText;
                ch.AiReview = polishResult.Report;
                ch.Status = ChapterStatus.Polished;
                ch.PolishedAt = DateTime.Now;

                RaiseChapterStream(new ChapterStreamEventArgs
                {
                    ChapterId = ch.Id, ChapterIndex = ch.Order, Phase = "polished",
                    Chapter = ch, Report = ch.AiReview,
                });

                await Core.DataManager.SaveNovelAsync(Novel);
                return ch;
            }
            catch (OperationCanceledException) { throw; } // 用户取消不是错误
            catch (Exception ex)
            {
                RaiseError($"Chapter{ch.Order}", ex);
                throw;
            }
        }

        // ── ⑦ Gate #2 ───────────────────────────────────────────────
        public async Task SubmitReview2Async(int chapterIndex, UserReviewDecision decision)
        {
            if (Novel.Chapters == null || chapterIndex < 0 || chapterIndex >= Novel.Chapters.Count)
                throw new ArgumentOutOfRangeException(nameof(chapterIndex));
            var ch = Novel.Chapters[chapterIndex];
            ch.UserDecision = decision;
            ch.DecidedAt = DateTime.Now;

            switch (decision.Action)
            {
                case ReviewAction.Accept:
                    ch.Status = ChapterStatus.Accepted;
                    break;
                case ReviewAction.Edit:
                    if (!string.IsNullOrEmpty(decision.EditedContent))
                        ch.PolishedContent = decision.EditedContent;
                    ch.Status = ChapterStatus.Accepted;
                    break;
                case ReviewAction.RequestRevision:
                    ch.Status = ChapterStatus.Revised;
                    break;
                case ReviewAction.Reject:
                    ch.Status = ChapterStatus.Rejected;
                    break;
            }
            TransitionTo(PipelineState.AwaitingReview2, $"第 {ch.Order} 章：{decision.Action}");
            await Core.DataManager.SaveNovelAsync(Novel);
        }

        public async Task<Chapter> RequestChapterRevisionAsync(int chapterIndex, StageContext ctx)
        {
            if (Novel.Chapters == null || chapterIndex < 0 || chapterIndex >= Novel.Chapters.Count)
                throw new ArgumentOutOfRangeException(nameof(chapterIndex));
            var ch = Novel.Chapters[chapterIndex];
            var feedback = ch.UserDecision?.RevisionFeedback ?? "请改进";
            try
            {
                ch.Status = ChapterStatus.Drafting;
                ch.Content = await ChapterGen.RegenerateAsync(Novel, ch.Outline, ch.Content, feedback, ctx);
                ch.WordCount = CountWords(ch.Content);
                ch.DraftedAt = DateTime.Now;
                ch.Status = ChapterStatus.Drafted;

                var polishResult = await Polisher.PolishAsync(Novel, ch.Outline, ch.Content, ctx);
                ch.PolishedContent = polishResult.PolishedText;
                ch.AiReview = polishResult.Report;
                ch.Status = ChapterStatus.Polished;
                ch.PolishedAt = DateTime.Now;
                await Core.DataManager.SaveNovelAsync(Novel);
                return ch;
            }
            catch (OperationCanceledException)
            {
                // 取消后章节不能卡在 Drafting/Polishing，恢复为可重试状态
                ch.Status = string.IsNullOrEmpty(ch.PolishedContent) ? ChapterStatus.Drafted : ChapterStatus.Polished;
                throw;
            }
            catch (Exception ex)
            {
                ch.Status = string.IsNullOrEmpty(ch.PolishedContent) ? ChapterStatus.Drafted : ChapterStatus.Polished;
                RaiseError($"Revision{ch.Order}", ex);
                throw;
            }
        }

        // ── ⑧ 合并 ──────────────────────────────────────────────────
        public async Task<Composition> ComposeAsync(StageContext ctx)
        {
            TransitionTo(PipelineState.Composing, "进入⑧");
            try
            {
                var comp = await Composer.ComposeAsync(Novel, ctx);
                Novel.Composition = comp;
                TransitionTo(PipelineState.Composed, "合并完成");
                await Core.DataManager.SaveNovelAsync(Novel);
                return comp;
            }
            catch (Exception ex)
            {
                RaiseError("Compose", ex);
                throw;
            }
        }

        // ── ⑨ 导出 ──────────────────────────────────────────────────
        public async Task<ExportRecord> ExportAsync(string format, StageContext ctx)
        {
            if (Novel.Composition == null) throw new InvalidOperationException("尚未合并");
            TransitionTo(PipelineState.Exporting, $"导出 {format}");
            try
            {
                var rec = await Exporter.ExportAsync(Novel, format, ctx);
                if (rec == null)
                {
                    // 用户在文件选择器中取消了，没有写出任何文件
                    TransitionTo(PipelineState.Composed, "导出取消");
                    return null;
                }
                Novel.LastExport = rec;
                TransitionTo(PipelineState.Exported, "导出完成");
                await Core.DataManager.SaveNovelAsync(Novel);
                return rec;
            }
            catch (OperationCanceledException)
            {
                TransitionTo(PipelineState.Composed, "导出取消");
                throw;
            }
            catch (Exception ex)
            {
                RaiseError("Export", ex);
                throw;
            }
        }

        // ── 快照 / 回滚 ─────────────────────────────────────────────
        public async Task<PipelineSnapshot> CreateSnapshotAsync(string stageName, string reason)
        {
            var snap = new PipelineSnapshot
            {
                StageName = stageName,
                TriggerReason = reason,
                PayloadJson = SerializeNovel(),
            };
            Novel.Snapshots.Add(snap);
            if (Novel.Snapshots.Count > 20) Novel.Snapshots.RemoveAt(0);
            await Core.DataManager.SaveNovelAsync(Novel);
            return snap;
        }

        public async Task RollbackToSnapshotAsync(string snapshotId)
        {
            var snap = Novel.Snapshots.Find(s => s.Id == snapshotId);
            if (snap == null) throw new InvalidOperationException("找不到快照");
            RaiseLog("info", $"回滚到 {snap.StageName}");

            // 真正还原快照里的数据（旧版快照 PayloadJson 可能为空，则只回滚状态）
            if (!string.IsNullOrEmpty(snap.PayloadJson))
            {
                try
                {
                    Novel restored;
                    var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Novel));
                    using (var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(snap.PayloadJson)))
                        restored = (Novel)ser.ReadObject(ms);
                    if (restored != null)
                    {
                        restored.Snapshots = Novel.Snapshots; // 保留完整快照历史（含回滚点之后的）
                        Novel = restored;
                    }
                }
                catch (Exception ex)
                {
                    RaiseLog("error", $"快照数据还原失败，仅回滚状态: {ex.Message}");
                }
            }

            switch (snap.StageName)
            {
                case "WorldSetting+Skeleton":
                case "WorldSetting":
                    TransitionTo(PipelineState.WorldSettingDraft, "rollback");
                    break;
                case "Skeleton":
                    TransitionTo(PipelineState.SkeletonDraft, "rollback");
                    break;
                default:
                    TransitionTo(PipelineState.AwaitingReview2, "rollback");
                    break;
            }
            await Core.DataManager.SaveNovelAsync(Novel);
        }

        // ── 工具 ────────────────────────────────────────────────────
        private static int CountWords(string s) => Core.TokenEstimator.CountWords(s);

        private string SerializeNovel()
        {
            try
            {
                var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Novel));
                using (var ms = new System.IO.MemoryStream())
                {
                    ser.WriteObject(ms, Novel);
                    ms.Position = 0;
                    return new System.IO.StreamReader(ms).ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                RaiseLog("error", $"序列化 Novel 失败: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NovelPipeline] SerializeNovel 失败: {ex}");
                return "";
            }
        }
    }

    /// <summary>Wizard 用的最小提示词容器（避免 Wizard 引用 Models.UserPrompt 之外的东西）。</summary>
    public class UserPromptLike
    {
        public string Text;
        public string Genre;
        public string SubGenre;
        public string PresetId;
    }
}
