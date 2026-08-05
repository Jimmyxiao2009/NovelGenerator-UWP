using System;
using System.Text;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;
using novelgen.Services;
using Windows.Data.Json;

namespace novelgen.AI.Generation
{
    /// <summary>阶段⑧：合并可导出的章节（优先 Accepted，无 Accepted 时退回所有已生成正文），生成书简介 / 标签 / TOC。</summary>
    public class Stage5_Composer
    {
        public async Task<Composition> ComposeAsync(Novel novel, StageContext ctx)
        {
            var client = new ApiClient(ctx.PolishProfile ?? ctx.Profile);

            // 与导出器一致的章节选择：用户尚未逐章点接受时（批量生成后常见）也能合并出非空书。
            var chapters = TxtExporter.SelectExportable(novel);

            var sb = new StringBuilder();
            foreach (var c in chapters)
            {
                var body = c.PolishedContent ?? c.Content ?? "";
                sb.AppendLine($"- 第 {c.Order} 章 {c.Outline?.Title}（{body.Length} 字）");
            }

            var worldSynopsis = novel.WorldSetting?.Synopsis ?? "";
            var user = PromptRegistry.User_Compose(novel.Title, worldSynopsis, sb.ToString());

            string raw = await client.ChatJsonAsync(PromptRegistry.System_Compose, user, ctx.Ct);

            var comp = new Composition
            {
                Title = novel.Title,
                Author = novel.Author,
                Description = "",
                Tags = new string[0],
                TotalChapters = 0,
                TotalWords = 0,
            };

            int chCount = 0, words = 0;
            var tocSb = new StringBuilder();
            tocSb.AppendLine(novel.Title);
            tocSb.AppendLine("作者：" + (string.IsNullOrEmpty(novel.Author) ? "未知" : novel.Author));
            tocSb.AppendLine();

            var combinedSb = new StringBuilder();
            combinedSb.AppendLine(novel.Title);
            combinedSb.AppendLine();

            foreach (var c in chapters)
            {
                chCount++;
                var title = $"第 {c.Order} 章 {c.Outline?.Title}";
                tocSb.AppendLine(title);
                combinedSb.AppendLine(title);
                combinedSb.AppendLine();
                var body = c.PolishedContent ?? c.Content ?? "";
                combinedSb.AppendLine(body);
                combinedSb.AppendLine();
                words += CountWords(body);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var jo = JsonObject.Parse(raw);
                    comp.Description = jo.GetNamedString("description", "");
                    if (jo.ContainsKey("tags"))
                    {
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var t in jo.GetNamedArray("tags")) list.Add(t.GetString());
                        comp.Tags = list.ToArray();
                    }
                    var aiToc = jo.GetNamedString("tableOfContents", "");
                    if (!string.IsNullOrEmpty(aiToc)) tocSb.AppendLine().AppendLine(aiToc);
                }
            }
            catch { /* 解析失败时使用自动 TOC */ }

            comp.TableOfContents = tocSb.ToString();
            comp.CombinedContent = combinedSb.ToString();
            comp.TotalChapters = chCount;
            comp.TotalWords = words;
            return comp;
        }

        private static int CountWords(string s) => Core.TokenEstimator.CountWords(s);
    }
}
