using System;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>每个 Gate 留下的可回滚快照。</summary>
    [DataContract]
    public class PipelineSnapshot
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        /// <summary>WorldSetting / Skeleton / Chapter3 / Composition / ...</summary>
        [DataMember] public string StageName { get; set; }
        /// <summary>该阶段产物的 JSON 序列化</summary>
        [DataMember] public string PayloadJson { get; set; } = "";
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        /// <summary>auto-before-gate1 / user-rollback / user-edit</summary>
        [DataMember] public string TriggerReason { get; set; } = "";
    }
}
