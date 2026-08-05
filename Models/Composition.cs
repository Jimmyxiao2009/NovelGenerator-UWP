using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>阶段⑧产物：合并后的整书元数据 + TOC。</summary>
    [DataContract]
    public class Composition
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; } = "";
        [DataMember] public string Author { get; set; } = "";
        [DataMember] public string Description { get; set; } = "";
        [DataMember] public string[] Tags { get; set; } = new string[0];
        [DataMember] public string TableOfContents { get; set; } = "";     // 自动生成的目录文本
        [DataMember] public string CombinedContent { get; set; } = "";    // 全部 Accepted 章节合并
        [DataMember] public int TotalChapters { get; set; }
        [DataMember] public int TotalWords { get; set; }
        [DataMember] public DateTime ComposedAt { get; set; } = DateTime.Now;
    }
}
