using System;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>阶段⑨产物：导出记录。</summary>
    [DataContract]
    public class ExportRecord
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Format { get; set; } = "";             // epub / txt
        [DataMember] public string FilePath { get; set; } = "";
        [DataMember] public long   FileSizeBytes { get; set; }
        [DataMember] public int    ChapterCount { get; set; }
        [DataMember] public int    WordCount { get; set; }
        [DataMember] public DateTime ExportedAt { get; set; } = DateTime.Now;
    }
}
