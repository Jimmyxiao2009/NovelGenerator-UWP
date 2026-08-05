using System;
using System.Collections.Generic;
using System.Threading;
using novelgen.Models;

namespace novelgen.Pipeline
{
    /// <summary>一次阶段执行的输入/进度/取消上下文。</summary>
    public class StageContext
    {
        public ApiProfile Profile;
        public ApiProfile PolishProfile;
        public IProgress<StageProgress> Progress;
        public CancellationToken Ct;
        public int RetryCount;
        public Dictionary<string, object> Extra = new Dictionary<string, object>();
        /// <summary>单章完成时回调（用于增量保存进度）</summary>
        public Action<Chapter> OnChapterDone;
    }

    /// <summary>阶段进度事件。</summary>
    public class StageProgress
    {
        public string StageName;
        public string Phase;                   // start / drafting / drafted / connecting / polishing / polished / done
        public string Text;                    // 流式 token 或状态文本
        public object Payload;                 // 最终产物 / 报告
        public double Percent;
        // 批量生成专用
        public int VolumeIndex = -1;
        public int VolumeCount;
        public int ChapterOrder;
        public int AgentId;                    // 1-based
        public string ChapterTitle;
    }
}
