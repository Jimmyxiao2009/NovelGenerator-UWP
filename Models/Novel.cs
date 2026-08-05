using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>一本书的根容器。所有流水线产物挂在它上面。</summary>
    [DataContract]
    public class Novel
    {
        // ── 基础 ─────────────────────────────────────────────────────
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; } = "未命名小说";
        [DataMember] public string Author { get; set; } = "";
        [DataMember] public string Genre { get; set; } = "通用";
        [DataMember] public string SubGenre { get; set; } = "";
        [DataMember] public string StylePresetId { get; set; } = "";
        [DataMember] public string ApiProfileId { get; set; } = "";       // 草稿/精修可分别用 AppSettings.DraftApiProfileId / PolishApiProfileId

        // ── ① 用户输入 ──────────────────────────────────────────────
        [DataMember] public string UserPrompt { get; set; } = "";          // 用户原始提示词
        [DataMember] public int    TargetChapterCount { get; set; } = 20;
        [DataMember] public int    TargetWordsPerChapter { get; set; } = 2500;

        // ── ①.b 世界观 / 骨架 自定义规模 ────────────────────────────
        [DataMember] public int    WorldCharacterCount { get; set; } = 3;   // 生成时要求的最少人物数
        [DataMember] public int    WorldLocationCount  { get; set; } = 2;
        [DataMember] public int    WorldFactionCount   { get; set; } = 2;
        [DataMember] public int    WorldRuleCount      { get; set; } = 3;
        [DataMember] public int    TargetVolumeCount   { get; set; } = 0;   // 0 = 由章节数自动推导

        // ── ② 世界设定 ──────────────────────────────────────────────
        [DataMember] public WorldSetting WorldSetting { get; set; }

        // ── ③ 故事骨架 ──────────────────────────────────────────────
        [DataMember] public StorySkeleton Skeleton { get; set; }

        // ── ⑤⑥⑦ 章节容器 ──────────────────────────────────────────
        [DataMember] public List<Chapter> Chapters { get; set; } = new List<Chapter>();

        // ── ⑧ 合并产物 ──────────────────────────────────────────────
        [DataMember] public Composition Composition { get; set; }

        // ── ⑨ 导出记录 ──────────────────────────────────────────────
        [DataMember] public ExportRecord LastExport { get; set; }

        // ── 流水线元数据 ────────────────────────────────────────────
        [DataMember] public PipelineState State { get; set; } = PipelineState.Draft;
        [DataMember] public List<PipelineSnapshot> Snapshots { get; set; } = new List<PipelineSnapshot>();
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        [DataMember] public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // 反序列化兜底：DCJS 不执行属性初始化器，旧版 JSON 缺成员时集合为 null
        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (Chapters == null) Chapters = new List<Chapter>();
            if (Snapshots == null) Snapshots = new List<PipelineSnapshot>();
            if (Title == null) Title = "未命名小说";
        }

        // ── 便利属性 ────────────────────────────────────────────────
        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case PipelineState.Draft: return "草稿";
                    case PipelineState.WorldSettingDraft: return "世界设定已生成";
                    case PipelineState.SkeletonDraft: return "骨架已生成";
                    case PipelineState.AwaitingReview1: return "等待 Gate1 审查";
                    case PipelineState.ChapterDrafting: return "分章生成中";
                    case PipelineState.ChapterPolishing: return "AI 审查润色中";
                    case PipelineState.AwaitingReview2: return "生成已完成";
                    case PipelineState.Composing: return "合并中";
                    case PipelineState.Composed: return "已合并";
                    case PipelineState.Exporting: return "导出中";
                    case PipelineState.Exported: return "已导出";
                    case PipelineState.Error: return "出错";
                    default: return State.ToString();
                }
            }
        }
    }
}
