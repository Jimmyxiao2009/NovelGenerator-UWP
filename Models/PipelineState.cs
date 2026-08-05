namespace novelgen.Models
{
    /// <summary>流水线状态枚举（数据形状，归 Models 管，Pipeline 仅消费）。</summary>
    public enum PipelineState
    {
        Draft,                // ① 已建书，等用户输入
        WorldSettingDraft,    // ② 完
        SkeletonDraft,        // ③ 完
        AwaitingReview1,      // ④ Gate1
        ChapterDrafting,      // ⑤ 进行中
        ChapterPolishing,     // ⑥ 进行中
        AwaitingReview2,      // ⑦ Gate2（按章循环）
        Composing,            // ⑧ 进行中
        Composed,             // ⑧ 完
        Exporting,            // ⑨ 进行中
        Exported,             // ⑨ 完
        Error
    }
}
