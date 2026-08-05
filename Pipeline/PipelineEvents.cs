using System;
using novelgen.Models;

namespace novelgen.Pipeline
{
    public class PipelineStateChangedEventArgs : EventArgs
    {
        public PipelineState OldState;
        public PipelineState NewState;
        public Novel Novel;
        public string Reason;
    }

    public class PipelineErrorEventArgs : EventArgs
    {
        public PipelineState State;
        public string StageName;
        public Exception Exception;
    }

    public class ChapterStreamEventArgs : EventArgs
    {
        public string ChapterId;
        public int ChapterIndex;
        public string Phase;                   // drafting / polishing / report
        public string Text;                    // 流式 token
        public Chapter Chapter;                // 最终结果
        public AiReviewReport Report;
    }

    public class PipelineLogEventArgs : EventArgs
    {
        public string Level;                   // info / warn / error
        public string Message;
        public DateTime Time;
    }

    /// <summary>状态机抛给 UI / 编排层的事件契约。</summary>
    public interface IPipelineEvents
    {
        event EventHandler<PipelineStateChangedEventArgs> StateChanged;
        event EventHandler<PipelineErrorEventArgs>        ErrorOccurred;
        event EventHandler<ChapterStreamEventArgs>        ChapterStream;
        event EventHandler<PipelineLogEventArgs>          Log;
    }
}
