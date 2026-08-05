using System;
using System.Text;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;

namespace novelgen.AI.Generation
{
    /// <summary>阶段⑤：基于大纲 + RAG 召回写章节正文（流式）。</summary>
    public class Stage3_ChapterGenerator
    {
        public async Task<string> GenerateAsync(Novel novel, ChapterOutline outline, StageContext ctx)
        {
            return await GenerateInternal(novel, outline, "", "", ctx);
        }

        public async Task<string> RegenerateAsync(Novel novel, ChapterOutline outline, string oldDraft, string feedback, StageContext ctx)
        {
            return await GenerateInternal(novel, outline, oldDraft ?? "", feedback ?? "", ctx);
        }

        private async Task<string> GenerateInternal(Novel novel, ChapterOutline outline, string prevDraft, string feedback, StageContext ctx)
        {
            var client = new ApiClient(ctx.Profile);
            var worldSummary = SummarizeWorld(novel.WorldSetting);
            var outlineJson = SerializeOutline(outline);
            var prevSummary = SummarizePrevious(novel);
            var rag = await PipelineHost.Retriever.RetrieveAsync(novel.WorldSetting,
                outline?.Title + " " + outline?.Synopsis + " " + string.Join(" ", outline?.KeyBeats ?? new string[0]),
                topK: 4);

            var extra = "";
            if (!string.IsNullOrEmpty(feedback)) extra = "【重做反馈】" + feedback;
            if (!string.IsNullOrEmpty(prevDraft)) extra += "\n【上一稿】" + Truncate(prevDraft, 400);

            var user = PromptRegistry.User_Chapter(worldSummary, outlineJson, prevSummary, rag.ContextText, extra);
            var sb = new StringBuilder();
            await client.StreamWithCallbackAsync(BuildSystemPrompt(novel), user, token =>
            {
                sb.Append(token);
                ctx.Progress?.Report(new StageProgress { StageName = "Chapter", Phase = "chunk", Text = token });
            }, ctx.Ct);
            return sb.ToString();
        }

        /// <summary>章节系统提示词：风格预设的 SystemPrompt 注入在通用提示词之前。</summary>
        internal static string BuildSystemPrompt(Novel novel)
        {
            var preset = GenrePresets.FindById(novel, null);
            var sys = PromptRegistry.System_Chapter;
            if (!string.IsNullOrEmpty(preset?.SystemPrompt))
                sys = preset.SystemPrompt + "\n\n" + sys;
            return sys;
        }

        private static string SummarizeWorld(WorldSetting ws)
        {
            if (ws == null) return "（无）";
            var sb = new StringBuilder();
            sb.AppendLine(ws.Synopsis);
            foreach (var c in ws.Characters ?? new System.Collections.Generic.List<Character>()) sb.AppendLine($"- {c.Name}（{c.Role}）：{c.Personality}");
            foreach (var r in ws.Rules ?? new System.Collections.Generic.List<WorldRule>()) sb.AppendLine($"- [规则] {r.Description}");
            return sb.ToString();
        }

        private static string SerializeOutline(ChapterOutline co)
        {
            if (co == null) return "（无）";
            var sb = new StringBuilder();
            sb.AppendLine($"标题：{co.Title}");
            sb.AppendLine($"梗概：{co.Synopsis}");
            sb.AppendLine($"氛围：{co.Mood}");
            sb.AppendLine("节拍：");
            foreach (var b in co.KeyBeats ?? new string[0]) sb.AppendLine("  - " + b);
            return sb.ToString();
        }

        private static string SummarizePrevious(Novel novel)
        {
            if (novel.Chapters == null || novel.Chapters.Count == 0) return "（无）";
            var sb = new StringBuilder();
            int n = 0;
            for (int i = novel.Chapters.Count - 1; i >= 0 && n < 3; i--)
            {
                var c = novel.Chapters[i];
                if (c.Status == ChapterStatus.Accepted || c.Status == ChapterStatus.Polished)
                {
                    sb.AppendLine($"第 {c.Order} 章 {c.Outline?.Title}：{Truncate(c.PolishedContent ?? c.Content, 200)}");
                    n++;
                }
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int n)
            => string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(0, n) + "…" : s);
    }
}
