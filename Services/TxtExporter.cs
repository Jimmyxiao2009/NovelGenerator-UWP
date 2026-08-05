using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using novelgen.Models;
using Windows.Storage;

namespace novelgen.Services
{
    public static class TxtExporter
    {
        /// <summary>
        /// 选出可导出的章节：优先用户已接受（Accepted）的；一章都没接受时退回所有已有正文的章节
        /// （批量生成后用户可能尚未逐章点接受，不应导出一本空书）。
        /// </summary>
        internal static List<Chapter> SelectExportable(Novel novel)
        {
            var chapters = novel?.Chapters ?? new List<Chapter>();
            var accepted = chapters.Where(c => c.Status == ChapterStatus.Accepted).OrderBy(c => c.Order).ToList();
            if (accepted.Count > 0) return accepted;
            return chapters
                .Where(c => !string.IsNullOrEmpty(c.PolishedContent) || !string.IsNullOrEmpty(c.Content))
                .OrderBy(c => c.Order)
                .ToList();
        }

        public static async Task WriteAsync(StorageFile file, Novel novel)
        {
            var chapters = SelectExportable(novel);
            if (chapters.Count == 0)
                throw new InvalidOperationException("没有可导出的章节（尚无已生成或已接受的章节）。");

            var title = string.IsNullOrWhiteSpace(novel.Title) ? "未命名小说" : novel.Title;
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine(new string('=', Math.Min(40, Math.Max(1, title.Length))));
            sb.AppendLine();

            if (novel.Composition != null)
            {
                if (!string.IsNullOrEmpty(novel.Composition.Description))
                {
                    sb.AppendLine("【简介】");
                    sb.AppendLine(novel.Composition.Description);
                    sb.AppendLine();
                }
                if (novel.Composition.Tags != null && novel.Composition.Tags.Length > 0)
                {
                    sb.AppendLine("【标签】" + string.Join("、", novel.Composition.Tags));
                    sb.AppendLine();
                }
            }

            sb.AppendLine("【目录】");
            foreach (var c in chapters)
                sb.AppendLine($"  第 {c.Order} 章  {c.Outline?.Title}");
            sb.AppendLine();
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();

            foreach (var c in chapters)
            {
                sb.AppendLine($"第 {c.Order} 章  {c.Outline?.Title}");
                sb.AppendLine();
                var body = c.PolishedContent ?? c.Content ?? "";
                sb.AppendLine(body);
                sb.AppendLine();
                sb.AppendLine(new string('-', 30));
                sb.AppendLine();
            }

            // 统一行尾为 \r\n（AI 正文常是裸 \n，与 AppendLine 的 \r\n 混杂），UTF-8 无 BOM
            var text = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var s = await file.OpenStreamForWriteAsync())
            {
                s.SetLength(0);
                await s.WriteAsync(bytes, 0, bytes.Length);
                await s.FlushAsync();
            }
        }
    }
}
