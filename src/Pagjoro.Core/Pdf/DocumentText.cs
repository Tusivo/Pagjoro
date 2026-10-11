// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Pagjoro.Core.Pdf;

/// <summary>One line of text on a page, found by grouping the text objects that share a baseline.</summary>
public sealed record TextLine(int PageIndex, string Text, double Size, bool Bold, double Left, double Baseline, IReadOnlyList<(double Left, double Right, string Text)> Cells);

/// <summary>A paragraph (lines that belong together) or a heading.</summary>
public sealed record TextParagraph(int PageIndex, string Text, double Size, bool Bold, int HeadingLevel);

/// <summary>Reads the text of a document back as lines and paragraphs, and writes it out as HTML, Word and spreadsheet files.</summary>
public static class DocumentText
{
    /// <summary>The lines of one page from top to bottom.</summary>
    public static IReadOnlyList<TextLine> Lines(PdfFile file, int pageIndex)
    {
        var runs = file.GetPageObjects(pageIndex).Where(o => o.Kind == PageObjectKind.Text && o.Text.Trim().Length > 0).ToList();
        var lines = new List<TextLine>();
        foreach (var group in GroupByBaseline(runs))
        {
            var ordered = group.OrderBy(r => r.Bounds.Left).ToList();
            var size = ordered.Max(r => r.Size);
            var sb = new StringBuilder();
            var cells = new List<(double, double, string)>();
            PageObjectInfo? prev = null;
            foreach (var r in ordered)
            {
                var text = r.Text.Replace('\r', ' ').Replace('\n', ' ');
                if (prev is not null)
                {
                    var gap = r.Bounds.Left - prev.Bounds.Right;
                    if (gap > size * 0.15 && !sb.ToString().EndsWith(' ') && !text.StartsWith(' ')) sb.Append(' ');
                }
                sb.Append(text);
                cells.Add((r.Bounds.Left, r.Bounds.Right, text.Trim()));
                prev = r;
            }
            var bold = ordered.Count(r => r.Font.Contains("Bold")) * 2 >= ordered.Count;
            lines.Add(new TextLine(pageIndex, sb.ToString().Trim(), size, bold, ordered[0].Bounds.Left, group.Average(r => r.Y), cells));
        }
        return lines.Where(l => l.Text.Length > 0).OrderByDescending(l => l.Baseline).ToList();
    }

    static IEnumerable<List<PageObjectInfo>> GroupByBaseline(List<PageObjectInfo> runs)
    {
        var groups = new List<List<PageObjectInfo>>();
        foreach (var r in runs.OrderByDescending(r => r.Y))
        {
            var g = groups.FirstOrDefault(g => Math.Abs(g[0].Y - r.Y) <= Math.Max(1.5, r.Size * 0.35));
            if (g is null) groups.Add([r]); else g.Add(r);
        }
        return groups;
    }

    /// <summary>The page as headings and paragraphs, in reading order.</summary>
    public static IReadOnlyList<TextParagraph> Blocks(PdfFile file, int pageIndex, double bodySize)
    {
        var lines = Lines(file, pageIndex);
        var blocks = new List<TextParagraph>();
        StringBuilder? current = null; double cSize = 0; var cBold = false; TextLine? last = null;
        void Flush()
        {
            if (current is null || current.Length == 0) return;
            var level = cSize >= bodySize * 1.6 ? 1 : cSize >= bodySize * 1.3 ? 2 : cBold && cSize >= bodySize * 1.05 && current.Length < 90 ? 3 : 0;
            blocks.Add(new TextParagraph(pageIndex, current.ToString().Trim(), cSize, cBold, level));
            current = null;
        }
        foreach (var line in lines)
        {
            var newBlock = last is null || Math.Abs(line.Size - last.Size) > 0.6 || (last.Baseline - line.Baseline) > Math.Max(line.Size, last.Size) * 1.7 || line.Bold != last.Bold;
            if (newBlock) { Flush(); current = new StringBuilder(); cSize = line.Size; cBold = line.Bold; }
            else if (current!.Length > 0 && !current.ToString().EndsWith('-')) current.Append(' ');
            else if (current.ToString().EndsWith('-')) current.Length--;   // a word broken at the end of the line
            current!.Append(line.Text);
            last = line;
        }
        Flush();
        return blocks;
    }

    /// <summary>The most common letter size in the document (the size of ordinary body text).</summary>
    public static double BodySize(PdfFile file)
    {
        var sizes = new Dictionary<double, int>();
        for (var p = 0; p < Math.Min(file.PageCount, 20); p++)
            foreach (var o in file.GetPageObjects(p).Where(o => o.Kind == PageObjectKind.Text))
            {
                var key = Math.Round(o.Size * 2) / 2;
                sizes[key] = sizes.GetValueOrDefault(key) + Math.Max(1, o.Text.Length);
            }
        return sizes.Count == 0 ? 11 : sizes.OrderByDescending(kv => kv.Value).First().Key;
    }

    // ---- HTML ---------------------------------------------------------------------------------------------------------

    public static string ToHtml(PdfFile file, string title)
    {
        var body = BodySize(file);
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(SecurityElement.Escape(title)).Append("</title>\n");
        sb.Append("<style>body{font:16px/1.6 system-ui,Segoe UI,Arial,sans-serif;max-width:46em;margin:2em auto;padding:0 1em}hr{margin:2em 0;border:0;border-top:1px solid #bbb}</style>\n</head>\n<body>\n");
        for (var p = 0; p < file.PageCount; p++)
        {
            if (p > 0) sb.Append("<hr>\n");
            sb.Append("<section id=\"page-").Append(p + 1).Append("\">\n");
            foreach (var b in Blocks(file, p, body))
            {
                var tag = b.HeadingLevel is >= 1 and <= 3 ? "h" + b.HeadingLevel : "p";
                sb.Append('<').Append(tag).Append('>').Append(SecurityElement.Escape(b.Text)).Append("</").Append(tag).Append(">\n");
            }
            sb.Append("</section>\n");
        }
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    // ---- Word (.docx) -------------------------------------------------------------------------------------------------

    public static byte[] ToDocx(PdfFile file, string title)
    {
        var body = BodySize(file);
        var doc = new StringBuilder();
        doc.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        doc.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        for (var p = 0; p < file.PageCount; p++)
        {
            foreach (var b in Blocks(file, p, body))
            {
                var halfPoints = Math.Clamp((int)Math.Round(b.Size * 2), 8, 144);
                doc.Append("<w:p><w:pPr>");
                if (b.HeadingLevel is >= 1 and <= 3) doc.Append("<w:pStyle w:val=\"Heading").Append(b.HeadingLevel).Append("\"/>");
                doc.Append("<w:spacing w:after=\"120\"/></w:pPr><w:r><w:rPr>");
                if (b.Bold || b.HeadingLevel > 0) doc.Append("<w:b/>");
                doc.Append("<w:sz w:val=\"").Append(halfPoints).Append("\"/></w:rPr><w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(b.Text)).Append("</w:t></w:r></w:p>");
            }
            if (p < file.PageCount - 1) doc.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        }
        doc.Append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>");
        doc.Append("</w:body></w:document>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml", doc.ToString());
        }
        return ms.ToArray();
    }

    // ---- tables: CSV and Excel (.xlsx) ----------------------------------------------------------------------------------

    /// <summary>Rows of cells for the whole document: a line is split into cells where there is a wide gap between runs of text.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Rows(PdfFile file)
    {
        var rows = new List<IReadOnlyList<string>>();
        for (var p = 0; p < file.PageCount; p++)
        {
            foreach (var line in Lines(file, p))
            {
                var cells = new List<string>();
                var sb = new StringBuilder(); double prevRight = double.NaN;
                foreach (var (left, right, text) in line.Cells)
                {
                    if (!double.IsNaN(prevRight) && left - prevRight > line.Size * 1.2) { cells.Add(sb.ToString().Trim()); sb.Clear(); }
                    else if (sb.Length > 0) sb.Append(' ');
                    sb.Append(text); prevRight = right;
                }
                cells.Add(sb.ToString().Trim());
                rows.Add(cells);
            }
        }
        return rows;
    }

    public static string ToCsv(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendJoin(',', row.Select(c => c.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + c.Replace("\"", "\"\"") + "\"" : c)).Append("\r\n");
        return sb.ToString();
    }

    public static byte[] ToXlsx(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        static string Col(int i) { var s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append("<row r=\"").Append(r + 1).Append("\">");
            for (var c = 0; c < rows[r].Count; c++)
            {
                var text = rows[r][c];
                if (text.Length == 0) continue;
                var reference = Col(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                if (double.TryParse(text.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && text.Length < 16 && !text.StartsWith('0') || text == "0")
                    sheet.Append("<c r=\"").Append(reference).Append("\"><v>").Append(number.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                else
                    sheet.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(SecurityElement.Escape(text)).Append("</t></is></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Pages\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }
}
