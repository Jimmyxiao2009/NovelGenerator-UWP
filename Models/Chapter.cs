using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    public enum ChapterStatus
    {
        Pending,      // ⑤ 还没生成
        Drafting,     // ⑤ 生成中
        Drafted,      // ⑤ 完
        Polishing,    // ⑥ 润色中
        Polished,     // ⑥ 完
        AwaitingUser, // ⑦ 等用户审查
        Accepted,     // 用户接受
        Rejected,     // 用户驳回
        Revised       // 用户请求重做后已重做
    }

    /// <summary>阶段⑤+⑥+⑦ 合并容器：单章全生命周期数据。</summary>
    [DataContract]
    public class Chapter
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public int    Order { get; set; }                    // 全书序号
        [DataMember] public string VolumeId { get; set; } = "";
        [DataMember] public ChapterOutline Outline { get; set; }          // 来自骨架

        [DataMember] public string Content { get; set; } = "";           // ⑤ 原始草稿
        [DataMember] public string PolishedContent { get; set; } = "";   // ⑥ 润色后正文
        [DataMember] public AiReviewReport AiReview { get; set; }        // ⑥ 审查报告
        [DataMember] public UserReviewDecision UserDecision { get; set; }// ⑦ 决定

        [DataMember] public ChapterStatus Status { get; set; } = ChapterStatus.Pending;
        [DataMember] public int WordCount { get; set; }
        /// <summary>卷尾过渡段是否已追加（防止批量重跑时重复追加）。</summary>
        [DataMember] public bool BridgeApplied { get; set; }
        [DataMember] public DateTime DraftedAt { get; set; } = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        [DataMember] public DateTime PolishedAt { get; set; } = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        [DataMember] public DateTime DecidedAt { get; set; } = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
