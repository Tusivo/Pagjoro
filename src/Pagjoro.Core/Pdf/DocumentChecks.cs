// SPDX-License-Identifier: AGPL-3.0-or-later
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Pagjoro.Core.Pdf;

public enum CheckStatus { Passed, Warning, Failed, Info }

/// <summary>One finding of a document check.</summary>
public sealed record CheckResult(CheckStatus Status, string Title, string Detail);

/// <summary>Checks a document for how well it works with screen readers, and for how safe it is to print or share.</summary>
public static class DocumentChecks
{
    /// <summary>Reading-access checks: title, language, tags, text, bookmarks, form labels and permissions.</summary>
    public static IReadOnlyList<CheckResult> Accessibility(byte[] pdf)
    {
        var results = new List<CheckResult>();
        using var doc = Open(pdf);
        var catalog = doc.Internals.Catalog;

        var title = doc.Info.Title;
        results.Add(string.IsNullOrWhiteSpace(title)
            ? new(CheckStatus.Failed, "Document title", "The document has no title, so a screen reader announces the file name instead. Set one in Review, Document Information.")
            : new(CheckStatus.Passed, "Document title", $"The title is \"{title}\"."));

        var prefs = catalog.Elements.GetDictionary("/ViewerPreferences");
        var showsTitle = prefs is not null && prefs.Elements.ContainsKey("/DisplayDocTitle") && prefs.Elements.GetBoolean("/DisplayDocTitle");
        results.Add(showsTitle
            ? new(CheckStatus.Passed, "Title shown in the window", "Readers show the title, not the file name, in the window title bar.")
            : new(CheckStatus.Warning, "Title shown in the window", "Readers show the file name instead of the title. Fix it in Review, Reading Options."));

        var lang = catalog.Elements.GetString("/Lang");
        results.Add(string.IsNullOrWhiteSpace(lang)
            ? new(CheckStatus.Failed, "Language", "No language is set, so a screen reader may pronounce words wrongly. Set it in Review, Reading Options.")
            : new(CheckStatus.Passed, "Language", $"The language is {lang}."));

        var mark = catalog.Elements.GetDictionary("/MarkInfo");
        var tagged = mark is not null && mark.Elements.ContainsKey("/Marked") && mark.Elements.GetBoolean("/Marked");
        var structure = catalog.Elements.ContainsKey("/StructTreeRoot");
        results.Add(tagged && structure
            ? new(CheckStatus.Passed, "Tagged structure", "The document is tagged, so headings, lists and reading order are available to assistive software.")
            : new(CheckStatus.Failed, "Tagged structure",
                "The document is not tagged. Screen readers have to guess the reading order. Pagjoro cannot add tags yet; re-export the file from the program that made it with tagging turned on."));

        // text on every page (a scanned page is only a picture)
        using var file = PdfFile.FromBytes(pdf);
        var noText = new List<int>();
        for (var p = 0; p < file.PageCount; p++) if (file.GetPageText(p).Trim().Length == 0) noText.Add(p + 1);
        if (noText.Count == 0) results.Add(new(CheckStatus.Passed, "Text on every page", "Every page has real text."));
        else results.Add(new(CheckStatus.Failed, "Text on every page",
            $"{noText.Count} page(s) have no text (pages {Summarize(noText)}); they are pictures to a screen reader. Use Review, Recognize Text (OCR)."));

        // outline for longer documents
        var bookmarks = file.GetBookmarks();
        if (file.PageCount >= 10)
            results.Add(bookmarks.Count > 0
                ? new(CheckStatus.Passed, "Bookmarks", $"The document has {bookmarks.Count} bookmark(s) for finding your way around.")
                : new(CheckStatus.Warning, "Bookmarks", "A document of this length should have bookmarks. Add them in Review, Bookmarks."));

        // form fields need names a screen reader can say
        var fields = PdfForms.Read(pdf);
        if (fields.Count > 0)
        {
            var unnamed = new List<string>();
            var form = doc.Internals.Catalog.Elements.GetDictionary("/AcroForm");
            var arr = form?.Elements.GetArray("/Fields");
            if (arr is not null)
                for (var i = 0; i < arr.Elements.Count; i++)
                {
                    var f = arr.Elements.GetDictionary(i);
                    if (f is not null && string.IsNullOrWhiteSpace(f.Elements.GetString("/TU"))) unnamed.Add(f.Elements.GetString("/T") ?? "(no name)");
                }
            results.Add(unnamed.Count == 0
                ? new(CheckStatus.Passed, "Form field descriptions", "Every form field has a description for screen readers.")
                : new(CheckStatus.Warning, "Form field descriptions", $"{unnamed.Count} form field(s) have no description (tool tip): {string.Join(", ", unnamed.Take(8))}{(unnamed.Count > 8 ? ", ..." : "")}."));
        }

        // can assistive software read it? an encrypted file may restrict copying and extraction
        results.Add(IsEncrypted(pdf)
            ? new(CheckStatus.Warning, "Permissions", "The document is encrypted. Its restrictions may block assistive software from extracting the text.")
            : new(CheckStatus.Passed, "Permissions", "Nothing in the permissions prevents assistive software from reading the text."));
        results.Add(new(CheckStatus.Info, "Not checked", "Pagjoro cannot yet check that pictures have alternative text, that colors have enough contrast, or that tables are marked up. A full check with a dedicated accessibility tool is still advised for official documents."));
        return results;
    }

    /// <summary>Print and sharing checks: fonts that are not embedded, encryption, unremoved hidden data, big pages.</summary>
    public static IReadOnlyList<CheckResult> Printing(byte[] pdf)
    {
        var results = new List<CheckResult>();
        using var doc = Open(pdf);
        var notEmbedded = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var standard14 = new HashSet<string>(PdfFile.StandardFonts.Select(f => f.Replace("-Roman", "")), StringComparer.OrdinalIgnoreCase) { "Symbol", "ZapfDingbats", "Times-Roman" };
        for (var i = 0; i < doc.PageCount; i++)
        {
            var res = doc.Pages[i].Elements.GetDictionary("/Resources");
            var fonts = res?.Elements.GetDictionary("/Font");
            if (fonts is null) continue;
            foreach (var key in fonts.Elements.Keys)
            {
                var font = fonts.Elements.GetDictionary(key);
                if (font is null) continue;
                var baseFont = (font.Elements.GetName("/BaseFont") ?? "").TrimStart('/');
                var plain = baseFont.Contains('+') ? baseFont[(baseFont.IndexOf('+') + 1)..] : baseFont;
                var desc = font.Elements.GetDictionary("/FontDescriptor");
                var subtype = font.Elements.GetName("/Subtype");
                if (subtype == "/Type0")
                {
                    var descendants = font.Elements.GetArray("/DescendantFonts");
                    desc = descendants?.Elements.GetDictionary(0)?.Elements.GetDictionary("/FontDescriptor");
                }
                if (subtype == "/Type3") continue;
                var embedded = desc is not null && (desc.Elements.ContainsKey("/FontFile") || desc.Elements.ContainsKey("/FontFile2") || desc.Elements.ContainsKey("/FontFile3"));
                if (!embedded && !standard14.Contains(plain)) notEmbedded.Add(plain.Length == 0 ? key : plain);
            }
        }
        results.Add(notEmbedded.Count == 0
            ? new(CheckStatus.Passed, "Fonts", "Every font is embedded or one of the standard fonts every reader has.")
            : new(CheckStatus.Warning, "Fonts", $"{notEmbedded.Count} font(s) are not embedded and may look different on other computers or printers: {string.Join(", ", notEmbedded.Take(8))}{(notEmbedded.Count > 8 ? ", ..." : "")}."));

        results.Add(IsEncrypted(pdf)
            ? new(CheckStatus.Warning, "Protection", "The document is protected with a password or restrictions, which printers and some services cannot handle.")
            : new(CheckStatus.Passed, "Protection", "The document has no password or restrictions."));

        var (_, report) = PdfProtection.Sanitize(pdf);
        results.Add(report.NothingFound
            ? new(CheckStatus.Passed, "Hidden data", "No scripts, attachments, metadata or hidden layers were found.")
            : new(CheckStatus.Warning, "Hidden data", "The file contains: " + string.Join("; ", report.Removed) + ". Use Protect, Remove Hidden Data before sharing."));

        var sizes = new HashSet<string>();
        for (var i = 0; i < doc.PageCount; i++)
        {
            var box = doc.Pages[i].MediaBox;
            sizes.Add($"{Math.Round(box.Width / 72 * 25.4)} x {Math.Round(box.Height / 72 * 25.4)} mm");
        }
        results.Add(sizes.Count <= 1
            ? new(CheckStatus.Passed, "Page size", sizes.Count == 1 ? "Every page is " + sizes.First() + "." : "")
            : new(CheckStatus.Info, "Page size", "The pages come in " + sizes.Count + " different sizes: " + string.Join("; ", sizes.Take(5)) + (sizes.Count > 5 ? "; ..." : "") + "."));
        return results;
    }

    static bool IsEncrypted(byte[] pdf) => System.Text.Encoding.Latin1.GetString(pdf, Math.Max(0, pdf.Length - 4096), Math.Min(4096, pdf.Length)).Contains("/Encrypt", StringComparison.Ordinal);

    static PdfDocument Open(byte[] pdf)
    {
        try { return PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import); }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotImplementedException or ArgumentException)
        { throw new PdfException("Pagjoro could not check this file (" + ex.Message + ")."); }
    }

    static string Summarize(List<int> pages) => pages.Count <= 8 ? string.Join(", ", pages) : string.Join(", ", pages.Take(8)) + ", ...";
}
