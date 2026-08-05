using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;
using novelgen.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace novelgen.AI.Generation
{
    /// <summary>阶段⑨：导出为 TXT / EPUB。</summary>
    public class Stage6_Exporter
    {
        public async Task<ExportRecord> ExportAsync(Novel novel, string format, StageContext ctx)
        {
            format = (format ?? "txt").ToLower();
            string ext = format == "epub" ? ".epub" : ".txt";
            var picker = new FileSavePicker { SuggestedFileName = SanitizeFileName(novel.Title) };
            picker.FileTypeChoices.Add(format == "epub" ? "EPUB" : "TXT", new[] { ext });
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var file = await picker.PickSaveFileAsync();
            if (file == null) return null;

            // DeferUpdates：写云同步位置（OneDrive 等）时避免半截文件被同步走
            CachedFileManager.DeferUpdates(file);
            try
            {
                if (format == "epub")
                {
                    await EpubExporter.WriteAsync(file, novel);
                }
                else
                {
                    await TxtExporter.WriteAsync(file, novel);
                }
            }
            finally
            {
                await CachedFileManager.CompleteUpdatesAsync(file);
            }

            int chCount = 0, words = 0;
            foreach (var c in TxtExporter.SelectExportable(novel))
            {
                chCount++;
                words += CountWords(c.PolishedContent ?? c.Content ?? "");
            }

            var props = await file.GetBasicPropertiesAsync();
            return new ExportRecord
            {
                Format = format,
                FilePath = file.Path,
                FileSizeBytes = (long)props.Size,
                ChapterCount = chCount,
                WordCount = words,
            };
        }

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "novel";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.TrimEnd('.', ' ');
            if (s.Length == 0) return "novel";
            // Windows 设备保留名（CON、NUL、COM1…）不能作为文件名
            if (ReservedNames.Contains(s.ToUpperInvariant())) s = "_" + s;
            return s;
        }

        private static int CountWords(string s) => Core.TokenEstimator.CountWords(s);
    }
}
