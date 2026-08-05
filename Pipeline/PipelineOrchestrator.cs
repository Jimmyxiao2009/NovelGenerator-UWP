using System;
using System.Threading.Tasks;
using novelgen.Models;

namespace novelgen.Pipeline
{
    /// <summary>
    /// 跨阶段协调：把 ②→③ 串起来、把 ⑤⑥ 串起来、对接 PipelineHost。
    /// </summary>
    public static class PipelineOrchestrator
    {
        /// <summary>从 Draft 状态一次性跑完 ②③，到达 Gate1 前。
        /// （Gate1 前的自动快照由 SubmitReview1Async 统一负责，这里不再重复留快照。）</summary>
        public static async Task RunWorldAndSkeletonAsync(NovelPipeline pipeline, StageContext ctx)
        {
            if (pipeline.Novel.WorldSetting == null)
                await pipeline.GenerateWorldSettingAsync(ctx);
            if (pipeline.Novel.Skeleton == null)
                await pipeline.GenerateStorySkeletonAsync(ctx);
            pipeline.TransitionTo(PipelineState.AwaitingReview1, "等待 Gate1");
        }

        /// <summary>Gate1 通过后批量生成全书。</summary>
        public static async Task RunBatchAllVolumesAsync(NovelPipeline pipeline, StageContext ctx, int concurrency = 3)
        {
            await pipeline.GenerateAllVolumesBatchAsync(ctx, concurrency);
        }

        /// <summary>该章是否仍需处理（含崩溃/取消后冻结在 Drafting/Polishing 的章节）。</summary>
        private static bool IsPending(Chapter c)
        {
            return c.Status == ChapterStatus.Pending
                || c.Status == ChapterStatus.Drafting
                || c.Status == ChapterStatus.Drafted
                || c.Status == ChapterStatus.Polishing
                || c.Status == ChapterStatus.Polished
                || c.Status == ChapterStatus.AwaitingUser
                || c.Status == ChapterStatus.Revised;
        }

        /// <summary>取下一个待处理的章节。</summary>
        public static Chapter GetNextPendingChapter(NovelPipeline pipeline)
        {
            if (pipeline.Novel.Chapters == null) return null;
            foreach (var c in pipeline.Novel.Chapters)
                if (IsPending(c)) return c;
            return null;
        }

        public static int GetNextPendingIndex(NovelPipeline pipeline)
        {
            if (pipeline.Novel.Chapters == null) return -1;
            for (int i = 0; i < pipeline.Novel.Chapters.Count; i++)
                if (IsPending(pipeline.Novel.Chapters[i])) return i;
            return -1;
        }

        public static bool AllChaptersAccepted(NovelPipeline pipeline)
        {
            if (pipeline.Novel.Chapters == null || pipeline.Novel.Chapters.Count == 0) return false;
            foreach (var c in pipeline.Novel.Chapters)
                if (c.Status != ChapterStatus.Accepted) return false;
            return true;
        }
    }
}
