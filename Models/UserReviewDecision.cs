using System;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    public enum ReviewAction { Accept, Edit, RequestRevision, Reject }

    /// <summary>阶段④/⑦ 产物：用户审查决定。</summary>
    [DataContract]
    public class UserReviewDecision
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public ReviewAction Action { get; set; } = ReviewAction.Accept;
        /// <summary>Action=Edit 时填：用户编辑过的内容（覆盖原值）</summary>
        [DataMember] public string EditedContent { get; set; } = "";
        /// <summary>Action=RequestRevision 时填：给 AI 的反馈</summary>
        [DataMember] public string RevisionFeedback { get; set; } = "";
        [DataMember] public string Notes { get; set; } = "";
        [DataMember] public DateTime DecidedAt { get; set; } = DateTime.Now;
    }
}
