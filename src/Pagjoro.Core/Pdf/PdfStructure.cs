// SPDX-License-Identifier: AGPL-3.0-or-later
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Pagjoro.Core.Pdf;

/// <summary>A file that is embedded in a PDF.</summary>
public sealed record AttachmentInfo(string Name, long Size);

/// <summary>How one run of pages is numbered. Style: "decimal", "roman", "ROMAN", "letters", "LETTERS" or "none".</summary>
public sealed record PageLabelRange(int FirstPage, string Style, string Prefix, int StartNumber);

/// <summary>Document-level structure: the bookmark tree, page numbering labels, embedded files, links and the language.</summary>
public static class PdfStructure
{
    static PdfDocument Load(byte[] pdf)
    {
        try { return PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify); }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotImplementedException or ArgumentException)
        { throw new PdfException("Pagjoro could not read this file's structure (" + ex.Message + ")."); }
    }

    static byte[] Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    // ---- bookmarks ----------------------------------------------------------------------------------------------------

    /// <summary>Replaces the document's bookmarks with the given list (Level 0 are top-level entries, Level 1 sit beneath the entry before them, and so on).</summary>
    public static byte[] SetBookmarks(byte[] pdf, IReadOnlyList<BookmarkInfo> bookmarks)
    {
        using var doc = Load(pdf);
        doc.Outlines.Clear();
        var stack = new List<PdfOutline>();
        foreach (var b in bookmarks)
        {
            if (b.PageIndex < 0 || b.PageIndex >= doc.PageCount) throw new PdfException($"The bookmark \"{b.Title}\" points to a page that does not exist.");
            var level = Math.Clamp(b.Level, 0, stack.Count);
            while (stack.Count > level) stack.RemoveAt(stack.Count - 1);
            var page = doc.Pages[b.PageIndex];
            var entry = stack.Count == 0 ? doc.Outlines.Add(b.Title, page, true) : stack[^1].Outlines.Add(b.Title, page, true);
            stack.Add(entry);
        }
        return Save(doc);
    }

    // ---- page labels --------------------------------------------------------------------------------------------------

    static string StyleCode(string style) => style switch
    {
        "roman" => "r", "ROMAN" => "R", "letters" => "a", "LETTERS" => "A", "decimal" => "D", _ => "",
    };

    /// <summary>Sets how pages are numbered (for example the first four in small roman numerals, then 1, 2, 3...).</summary>
    public static byte[] SetPageLabels(byte[] pdf, IReadOnlyList<PageLabelRange> ranges)
    {
        using var doc = Load(pdf);
        var catalog = doc.Internals.Catalog;
        if (ranges.Count == 0) { catalog.Elements.Remove("/PageLabels"); return Save(doc); }
        if (ranges.Any(r => r.FirstPage < 0 || r.FirstPage >= doc.PageCount)) throw new PdfException("A page-number range starts on a page that does not exist.");
        var nums = new PdfArray(doc);
        foreach (var r in ranges.OrderBy(r => r.FirstPage))
        {
            nums.Elements.Add(new PdfInteger(r.FirstPage));
            var d = new PdfDictionary(doc);
            var code = StyleCode(r.Style);
            if (code.Length > 0) d.Elements.SetName("/S", "/" + code);
            if (r.Prefix.Length > 0) d.Elements.SetString("/P", r.Prefix);
            if (r.StartNumber != 1) d.Elements.SetInteger("/St", r.StartNumber);
            nums.Elements.Add(d);
        }
        var labels = new PdfDictionary(doc);
        labels.Elements["/Nums"] = nums;
        catalog.Elements["/PageLabels"] = labels;
        return Save(doc);
    }

    /// <summary>The label of each page as a reader would show it ("iv", "12", "A-3"...).</summary>
    public static IReadOnlyList<string> PageLabels(byte[] pdf)
    {
        using var doc = Load(pdf);
        var result = new string[doc.PageCount];
        var ranges = ReadLabelRanges(doc);
        for (var i = 0; i < doc.PageCount; i++)
        {
            var r = ranges.LastOrDefault(x => x.FirstPage <= i);
            if (r is null) { result[i] = (i + 1).ToString(); continue; }
            var n = r.StartNumber + (i - r.FirstPage);
            var body = r.Style switch
            {
                "roman" => Roman(n).ToLowerInvariant(), "ROMAN" => Roman(n), "letters" => Letters(n).ToLowerInvariant(), "LETTERS" => Letters(n),
                "decimal" => n.ToString(), _ => "",
            };
            result[i] = r.Prefix + body;
        }
        return result;
    }

    /// <summary>The page-numbering ranges as they are stored.</summary>
    public static IReadOnlyList<PageLabelRange> LabelRanges(byte[] pdf)
    {
        using var doc = Load(pdf);
        return ReadLabelRanges(doc);
    }

    static List<PageLabelRange> ReadLabelRanges(PdfDocument doc)
    {
        var list = new List<PageLabelRange>();
        var labels = doc.Internals.Catalog.Elements.GetDictionary("/PageLabels");
        var nums = labels?.Elements.GetArray("/Nums");
        if (nums is null) return list;
        for (var i = 0; i + 1 < nums.Elements.Count; i += 2)
        {
            var first = nums.Elements.GetInteger(i);
            var d = nums.Elements.GetDictionary(i + 1);
            if (d is null) continue;
            var s = d.Elements.GetName("/S");
            var style = s switch { "/r" => "roman", "/R" => "ROMAN", "/a" => "letters", "/A" => "LETTERS", "/D" => "decimal", _ => "none" };
            var st = d.Elements.ContainsKey("/St") ? d.Elements.GetInteger("/St") : 1;
            list.Add(new PageLabelRange(first, style, d.Elements.GetString("/P") ?? "", st));
        }
        return list;
    }

    static string Roman(int n)
    {
        if (n <= 0) return n.ToString();
        var parts = new (int V, string S)[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var sb = new System.Text.StringBuilder();
        foreach (var (v, s) in parts) while (n >= v) { sb.Append(s); n -= v; }
        return sb.ToString();
    }

    static string Letters(int n)
    {
        if (n <= 0) return n.ToString();
        var s = ""; var letter = (char)('A' + (n - 1) % 26); var count = (n - 1) / 26 + 1;
        for (var i = 0; i < count; i++) s += letter;
        return s;
    }

    // ---- embedded files -----------------------------------------------------------------------------------------------

    public static IReadOnlyList<AttachmentInfo> Attachments(byte[] pdf)
    {
        using var doc = Load(pdf);
        var list = new List<AttachmentInfo>();
        foreach (var (name, spec) in EmbeddedSpecs(doc))
        {
            long size = 0;
            var stream = EmbeddedStream(spec);
            if (stream?.Stream is { } s) size = s.Length;
            list.Add(new AttachmentInfo(name, size));
        }
        return list;
    }

    static IEnumerable<(string Name, PdfDictionary Spec)> EmbeddedSpecs(PdfDocument doc)
    {
        var names = doc.Internals.Catalog.Elements.GetDictionary("/Names");
        var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
        var arr = embedded?.Elements.GetArray("/Names");
        if (arr is null) yield break;
        for (var i = 0; i + 1 < arr.Elements.Count; i += 2)
        {
            var name = arr.Elements.GetString(i);
            var spec = arr.Elements.GetDictionary(i + 1);
            if (spec is not null) yield return (name, spec);
        }
    }

    static PdfDictionary? EmbeddedStream(PdfDictionary spec)
    {
        var ef = spec.Elements.GetDictionary("/EF");
        return ef?.Elements.GetDictionary("/F") ?? ef?.Elements.GetDictionary("/UF");
    }

    /// <summary>The bytes of an embedded file.</summary>
    public static byte[] ExtractAttachment(byte[] pdf, string name)
    {
        using var doc = Load(pdf);
        foreach (var (n, spec) in EmbeddedSpecs(doc))
        {
            if (!string.Equals(n, name, StringComparison.Ordinal)) continue;
            var stream = EmbeddedStream(spec) ?? throw new PdfException("That attachment has no data.");
            return stream.Stream?.UnfilteredValue ?? throw new PdfException("That attachment has no data.");
        }
        throw new PdfException("There is no attachment called " + name + ".");
    }

    /// <summary>Adds a file to the document (an existing attachment of the same name is replaced).</summary>
    public static byte[] AddAttachment(byte[] pdf, string name, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new PdfException("Give the attachment a name.");
        using var doc = Load(pdf);
        var kept = EmbeddedSpecs(doc).Where(e => e.Name != name).ToList();

        var stream = new PdfDictionary(doc);
        stream.Elements.SetName("/Type", "/EmbeddedFile");
        var p = new PdfDictionary(doc); p.Elements.SetInteger("/Size", data.Length);
        stream.Elements["/Params"] = p;
        stream.CreateStream(data);
        doc.Internals.AddObject(stream);

        var spec = new PdfDictionary(doc);
        spec.Elements.SetName("/Type", "/Filespec");
        spec.Elements.SetString("/F", name);
        spec.Elements.SetString("/UF", name);
        var ef = new PdfDictionary(doc); ef.Elements["/F"] = stream.Reference!;
        spec.Elements["/EF"] = ef;
        doc.Internals.AddObject(spec);

        kept.Add((name, spec));
        WriteEmbedded(doc, kept);
        return Save(doc);
    }

    public static byte[] RemoveAttachment(byte[] pdf, string name)
    {
        using var doc = Load(pdf);
        var all = EmbeddedSpecs(doc).ToList();
        if (all.All(e => e.Name != name)) throw new PdfException("There is no attachment called " + name + ".");
        WriteEmbedded(doc, all.Where(e => e.Name != name).ToList());
        return Save(doc);
    }

    static void WriteEmbedded(PdfDocument doc, List<(string Name, PdfDictionary Spec)> items)
    {
        var catalog = doc.Internals.Catalog;
        var names = catalog.Elements.GetDictionary("/Names");
        if (names is null) { names = new PdfDictionary(doc); catalog.Elements["/Names"] = names; }
        if (items.Count == 0) { names.Elements.Remove("/EmbeddedFiles"); return; }
        var arr = new PdfArray(doc);
        foreach (var (n, spec) in items.OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            arr.Elements.Add(new PdfString(n));
            arr.Elements.Add(spec.Reference ?? (PdfItem)spec);
        }
        var tree = new PdfDictionary(doc);
        tree.Elements["/Names"] = arr;
        names.Elements["/EmbeddedFiles"] = tree;
    }

    // ---- links and document properties --------------------------------------------------------------------------------

    /// <summary>Makes an area of a page a clickable link to a web address.</summary>
    public static byte[] AddWebLink(byte[] pdf, int pageIndex, PageRect area, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
            throw new PdfException("Type a web address that starts with http://, https:// or mailto:.");
        using var doc = Load(pdf);
        if (pageIndex < 0 || pageIndex >= doc.PageCount) throw new PdfException("That page does not exist.");
        var r = area.Normalized();
        doc.Pages[pageIndex].AddWebLink(new PdfRectangle(new XPoint(r.Left, r.Bottom), new XPoint(r.Right, r.Top)), uri.ToString());
        return Save(doc);
    }

    /// <summary>Makes an area of a page a clickable link to another page of the same document.</summary>
    public static byte[] AddPageLink(byte[] pdf, int pageIndex, PageRect area, int targetPageIndex)
    {
        using var doc = Load(pdf);
        if (pageIndex < 0 || pageIndex >= doc.PageCount || targetPageIndex < 0 || targetPageIndex >= doc.PageCount) throw new PdfException("That page does not exist.");
        var r = area.Normalized();
        doc.Pages[pageIndex].AddDocumentLink(new PdfRectangle(new XPoint(r.Left, r.Bottom), new XPoint(r.Right, r.Top)), targetPageIndex + 1);
        return Save(doc);
    }

    /// <summary>Sets the language, shows the title in the window title bar of readers, and marks the document as structured text.</summary>
    public static byte[] SetReadingOptions(byte[] pdf, string? language, bool showTitle, string? title)
    {
        using var doc = Load(pdf);
        var catalog = doc.Internals.Catalog;
        if (!string.IsNullOrWhiteSpace(language)) catalog.Elements.SetString("/Lang", language.Trim());
        var prefs = catalog.Elements.GetDictionary("/ViewerPreferences");
        if (prefs is null) { prefs = new PdfDictionary(doc); catalog.Elements["/ViewerPreferences"] = prefs; }
        prefs.Elements.SetBoolean("/DisplayDocTitle", showTitle);
        if (!string.IsNullOrWhiteSpace(title)) doc.Info.Title = title.Trim();
        return Save(doc);
    }
}
