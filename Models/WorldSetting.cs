using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>阶段②产物：世界设定。</summary>
    [DataContract]
    public class WorldSetting
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; } = "";          // AI 起的书名（用户未手填标题时采用）
        [DataMember] public string Synopsis { get; set; } = "";
        [DataMember] public string[] Themes { get; set; } = new string[0];
        [DataMember] public List<Character>  Characters { get; set; } = new List<Character>();
        [DataMember] public List<Faction>    Factions   { get; set; } = new List<Faction>();
        [DataMember] public List<Location>   Locations  { get; set; } = new List<Location>();
        [DataMember] public List<Item>       Items      { get; set; } = new List<Item>();
        [DataMember] public List<WorldRule>  Rules      { get; set; } = new List<WorldRule>();
        [DataMember] public List<WorldEvent> Timeline   { get; set; } = new List<WorldEvent>();
        [DataMember] public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>解析期瞬时标志：AI 返回非合法 JSON 或为空时为 true。
        /// 不持久化（不标 [DataMember]）；仅供生成流水线判断是否应进入 Error 状态，避免把解析失败当成有效内容继续推进。</summary>
        public bool ParseFailed { get; set; }

        // 反序列化兜底：DCJS 不执行属性初始化器
        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (Themes == null) Themes = new string[0];
            if (Characters == null) Characters = new List<Character>();
            if (Factions == null) Factions = new List<Faction>();
            if (Locations == null) Locations = new List<Location>();
            if (Items == null) Items = new List<Item>();
            if (Rules == null) Rules = new List<WorldRule>();
            if (Timeline == null) Timeline = new List<WorldEvent>();
        }

        public int CharacterCount => Characters?.Count ?? 0;
        public int RuleCount => Rules?.Count ?? 0;
    }
}
