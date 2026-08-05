using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>小说类型（玄幻 / 都市 / 科幻 / …）。用于匹配 StylePreset。</summary>
    [DataContract]
    public class Genre
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; }
        [DataMember] public string Description { get; set; }
        [DataMember] public List<string> SubGenres { get; set; } = new List<string>();
    }

    /// <summary>风格预设：system prompt + 各阶段 prompt 模板 + 节奏参数。</summary>
    [DataContract]
    public class StylePreset
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; } = "通用";
        [DataMember] public string Genre { get; set; } = "通用";
        [DataMember] public string SubGenre { get; set; } = "";
        [DataMember] public string Description { get; set; } = "";

        [DataMember] public string SystemPrompt { get; set; } = "你是一位经验丰富的小说家。";
        [DataMember] public string WorldSettingTemplate { get; set; } = "";
        [DataMember] public string SkeletonTemplate { get; set; } = "";
        [DataMember] public string ChapterTemplate { get; set; } = "";
        [DataMember] public string PolishTemplate { get; set; } = "";
        [DataMember] public string ComposeTemplate { get; set; } = "";

        [DataMember] public int    TargetWordsPerChapter { get; set; } = 2500;
        [DataMember] public int    HookDensity { get; set; } = 3;            // 章末钩子密度 1-5
        [DataMember] public string DialogueStyle { get; set; } = "";          // 对话风格
        [DataMember] public string[] AvoidWords { get; set; } = new string[0];
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    [DataContract]
    public class GenreCatalog
    {
        [DataMember] public List<Genre>      Genres      { get; set; } = new List<Genre>();
        [DataMember] public List<StylePreset> Presets     { get; set; } = new List<StylePreset>();
    }
}
