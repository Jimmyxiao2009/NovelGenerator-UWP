using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using novelgen.Models;
using Windows.Storage;

namespace novelgen.Services
{
    /// <summary>最小化 EPUB 3.0 导出器（无图片、无字体）。</summary>
    public static class EpubExporter
    {
        public static async Task WriteAsync(StorageFile file, Novel novel)
        {
            var chapters = TxtExporter.SelectExportable(novel);
            if (chapters.Count == 0)
                throw new InvalidOperationException("没有可导出的章节（尚无已生成或已接受的章节）。");

            var bookTitle = string.IsNullOrWhiteSpace(novel.Title) ? "未命名小说" : novel.Title;
            // 标识符复用 Novel.Id：同一本书多次导出保持同一 UUID（阅读器据此去重/同步）
            var uid = "urn:uuid:" + (string.IsNullOrEmpty(novel.Id) ? Guid.NewGuid().ToString() : novel.Id);

            // 1. 先写到临时文件，再复制到目标（EPUB 必须先建立 mimetype 不压缩的 ZIP）
            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".epub");
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
                {
                    // mimetype 必须是不压缩存储
                    var mt = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
                    using (var s = mt.Open())
                    using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                        w.Write("application/epub+zip");

                    // META-INF/container.xml
                    WriteEntry(zip, "META-INF/container.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                        "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");

                    int idx = 0;
                    var manifest = new StringBuilder();
                    var spine = new StringBuilder();
                    var ncx = new StringBuilder();
                    var navList = new StringBuilder();
                    ncx.Append("<navMap>");

                    foreach (var c in chapters)
                    {
                        idx++;
                        var fname = $"chapter{idx}.xhtml";
                        // 章节号用全书原始序号 Order，与正文中的前后章引用保持一致
                        var title = EscapeXml($"第 {c.Order} 章 {c.Outline?.Title}");
                        var body = EscapeXml(c.PolishedContent ?? c.Content ?? "");
                        var xhtml =
                            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                            "<!DOCTYPE html>" +
                            "<html xmlns=\"http://www.w3.org/1999/xhtml\">" +
                            "<head><title>" + title + "</title></head>" +
                            "<body><h1>" + title + "</h1>" +
                            body.Replace("\n", "<br/>") +
                            "</body></html>";
                        WriteEntry(zip, "OEBPS/" + fname, xhtml);

                        manifest.Append($"<item id=\"ch{idx}\" href=\"{fname}\" media-type=\"application/xhtml+xml\"/>");
                        spine.Append($"<itemref idref=\"ch{idx}\"/>");
                        ncx.Append($"<navPoint id=\"np{idx}\" playOrder=\"{idx}\"><navLabel><text>{title}</text></navLabel><content src=\"{fname}\"/></navPoint>");
                        navList.Append($"<li><a href=\"{fname}\">{title}</a></li>");
                    }
                    ncx.Append("</navMap>");

                    // EPUB 3 必需的 XHTML 导航文档（properties="nav"）
                    var navDoc =
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<!DOCTYPE html>" +
                        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">" +
                        "<head><title>目录</title></head>" +
                        "<body><nav epub:type=\"toc\" id=\"toc\"><h1>目录</h1><ol>" +
                        navList.ToString() +
                        "</ol></nav></body></html>";
                    WriteEntry(zip, "OEBPS/nav.xhtml", navDoc);

                    var modified = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                    var opf =
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">" +
                        $"<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
                        $"<dc:title>{EscapeXml(bookTitle)}</dc:title>" +
                        $"<dc:creator>{EscapeXml(novel.Author ?? "")}</dc:creator>" +
                        $"<dc:language>zh-CN</dc:language>" +
                        $"<dc:identifier id=\"bookid\">{uid}</dc:identifier>" +
                        $"<meta property=\"dcterms:modified\">{modified}</meta>" +
                        "</metadata>" +
                        "<manifest>" +
                        "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>" +
                        "<item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>" +
                        manifest.ToString() +
                        "</manifest>" +
                        "<spine toc=\"ncx\">" + spine.ToString() + "</spine>" +
                        "</package>";
                    WriteEntry(zip, "OEBPS/content.opf", opf);

                    var ncxDoc =
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<!DOCTYPE ncx PUBLIC \"-//NISO//DTD ncx 2005-1//EN\" \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">" +
                        "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">" +
                        $"<head><meta name=\"dtb:uid\" content=\"{uid}\"/><meta name=\"dtb:depth\" content=\"1\"/></head>" +
                        "<docTitle><text>" + EscapeXml(bookTitle) + "</text></docTitle>" +
                        ncx.ToString() +
                        "</ncx>";
                    WriteEntry(zip, "OEBPS/toc.ncx", ncxDoc);
                }

                // 复制到目标：必须经 StorageFile 的代理流写入。
                // 直接 new FileStream(file.Path) 会因 UWP 沙箱（无 broadFileSystemAccess）抛 UnauthorizedAccessException。
                using (var src = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                using (var dst = await file.OpenStreamForWriteAsync())
                {
                    dst.SetLength(0);
                    await src.CopyToAsync(dst);
                    await dst.FlushAsync();
                }
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static void WriteEntry(ZipArchive zip, string path, string content)
        {
            var e = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (var s = e.Open())
            using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                w.Write(content);
        }

        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
