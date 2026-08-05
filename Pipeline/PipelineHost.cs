using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.AI.Generation;
using novelgen.AI.RAG;

namespace novelgen.Pipeline
{
    /// <summary>App 级单例：保存当前正在编辑的 NovelPipeline + 子服务实例。</summary>
    public static class PipelineHost
    {
        public static NovelPipeline Current { get; private set; }
        public static WorldbookRetriever Retriever { get; private set; }

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            Retriever = new WorldbookRetriever();
            _initialized = true;
        }

        public static void Start(NovelPipeline pipeline)
        {
            Current = pipeline;
            // 注入子服务（生成器可在这里换实现）
            pipeline.WorldGen    = new Stage1_WorldSettingGenerator();
            pipeline.SkeletonGen = new Stage2_SkeletonGenerator();
            pipeline.ChapterGen  = new Stage3_ChapterGenerator();
            pipeline.Polisher    = new Stage4_AiReviewPolisher();
            pipeline.Composer    = new Stage5_Composer();
            pipeline.Exporter    = new Stage6_Exporter();
        }

        public static async Task<NovelPipeline> ResumeAsync(string novelId)
        {
            var n = await Core.DataManager.LoadNovelAsync(novelId);
            if (n == null) return null;

            bool repaired = RepairState(n);

            var p = new NovelPipeline(n);
            Start(p);
            if (repaired)
                await Core.DataManager.SaveNovelAsync(n); // 把修复后的状态写回，避免每次启动都"带病"
            return p;
        }

        /// <summary>
        /// 崩溃 / 强杀 / 上次出错后，把持久化下来的瞬态状态修复为可继续的状态。
        /// 返回是否做了修复。
        /// </summary>
        private static bool RepairState(Novel n)
        {
            bool changed = false;

            // 冻结在"进行中"的章节恢复为可重试状态
            if (n.Chapters != null)
            {
                foreach (var c in n.Chapters)
                {
                    if (c.Status == ChapterStatus.Drafting)
                    {
                        c.Status = string.IsNullOrEmpty(c.Content) ? ChapterStatus.Pending : ChapterStatus.Drafted;
                        changed = true;
                    }
                    else if (c.Status == ChapterStatus.Polishing)
                    {
                        c.Status = string.IsNullOrEmpty(c.PolishedContent) ? ChapterStatus.Drafted : ChapterStatus.Polished;
                        changed = true;
                    }
                }
            }

            var old = n.State;
            switch (n.State)
            {
                case PipelineState.Composing:
                    n.State = PipelineState.AwaitingReview2;
                    break;
                case PipelineState.Exporting:
                    n.State = PipelineState.Composed;
                    break;
                case PipelineState.Error:
                    // 按实际已有数据推断最后一个一致状态，让流水线能走出 Error
                    n.State = InferStateFromData(n);
                    break;
            }
            return changed || n.State != old;
        }

        private static PipelineState InferStateFromData(Novel n)
        {
            if (n.Composition != null) return PipelineState.Composed;
            if (n.WorldSetting == null) return PipelineState.Draft;
            if (n.Skeleton == null) return PipelineState.WorldSettingDraft;
            if (n.Chapters == null || n.Chapters.Count == 0) return PipelineState.AwaitingReview1;

            bool anyWork = false, allAccepted = true;
            foreach (var c in n.Chapters)
            {
                if (!string.IsNullOrEmpty(c.Content)) anyWork = true;
                if (c.Status != ChapterStatus.Accepted) allAccepted = false;
            }
            if (allAccepted) return PipelineState.AwaitingReview2;
            if (anyWork) return PipelineState.ChapterDrafting;
            return PipelineState.AwaitingReview1;
        }

        public static void Clear() => Current = null;
    }
}
