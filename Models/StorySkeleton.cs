using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>伏笔。</summary>
    [DataContract]
    public class Foreshadowing
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Description { get; set; }
        [DataMember] public int PlantedAtChapter { get; set; } = -1;
        [DataMember] public int ExpectedPayoffChapter { get; set; } = -1;
        [DataMember] public int PaidOffAtChapter { get; set; } = -1;
        [DataMember] public string Status { get; set; } = "Planted";   // Planted/PaidOff/Dropped
        [DataMember] public string Notes { get; set; } = "";
    }

    [DataContract]
    public class ChapterOutline
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; }
        [DataMember] public string Synopsis { get; set; } = "";
        [DataMember] public string Mood { get; set; } = "";
        [DataMember] public string[] KeyBeats { get; set; } = new string[0];
        [DataMember] public string[] CharacterIds { get; set; } = new string[0];
        [DataMember] public string LocationId { get; set; } = "";
    }

    [DataContract]
    public class VolumeOutline
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; }
        [DataMember] public string Synopsis { get; set; } = "";
        [DataMember] public string PlotGoal { get; set; } = "";
        [DataMember] public string Climax { get; set; } = "";
        [DataMember] public List<ChapterOutline> Chapters { get; set; } = new List<ChapterOutline>();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (Chapters == null) Chapters = new List<ChapterOutline>();
        }
    }

    /// <summary>阶段③产物：故事骨架。</summary>
    [DataContract]
    public class StorySkeleton
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Logline { get; set; } = "";       // 一句话故事
        [DataMember] public string ThreeAct { get; set; } = "";       // 起承转合
        [DataMember] public List<VolumeOutline> Volumes { get; set; } = new List<VolumeOutline>();
        [DataMember] public List<Foreshadowing> PlantedForeshadowings { get; set; } = new List<Foreshadowing>();
        [DataMember] public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>解析期瞬时标志：AI 返回非合法 JSON 或为空时为 true。
        /// 不持久化（不标 [DataMember]）；仅供生成流水线判断是否应进入 Error 状态，避免把解析失败当成有效骨架继续推进。</summary>
        public bool ParseFailed { get; set; }

        // 反序列化兜底：DCJS 不执行属性初始化器
        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (Volumes == null) Volumes = new List<VolumeOutline>();
            if (PlantedForeshadowings == null) PlantedForeshadowings = new List<Foreshadowing>();
        }

        public int TotalChapters
        {
            get
            {
                int n = 0;
                if (Volumes != null) foreach (var v in Volumes) n += v.Chapters?.Count ?? 0;
                return n;
            }
        }
    }
}
