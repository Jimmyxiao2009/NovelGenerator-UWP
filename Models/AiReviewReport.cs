using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    public enum ReviewType { Style, Pacing, Grammar, Character, Dialogue, Description, Other }
    public enum Severity   { Minor, Major, Critical }
    public enum IssueType  { NameInconsistency, TimelineConflict, RuleBreak, LocationError, AbilityContradiction, Other }

    [DataContract]
    public class ReviewNote
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public ReviewType Type { get; set; }
        [DataMember] public string Excerpt { get; set; } = "";            // 原文片段
        [DataMember] public string Suggestion { get; set; } = "";         // 修改建议
        [DataMember] public Severity Severity { get; set; } = Severity.Minor;
    }

    [DataContract]
    public class ConsistencyIssue
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public int    ChapterIndex { get; set; }
        [DataMember] public IssueType Type { get; set; }
        [DataMember] public Severity Severity { get; set; } = Severity.Minor;
        [DataMember] public string Description { get; set; } = "";
        [DataMember] public string Suggestion { get; set; } = "";
    }

    /// <summary>阶段⑥产物：AI 审查报告。</summary>
    [DataContract]
    public class AiReviewReport
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public int    QualityScore { get; set; }             // 1-10
        [DataMember] public string Summary { get; set; } = "";
        [DataMember] public List<ReviewNote>       Notes { get; set; } = new List<ReviewNote>();
        [DataMember] public List<ConsistencyIssue> Issues { get; set; } = new List<ConsistencyIssue>();
        [DataMember] public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
}
