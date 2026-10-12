// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

/// <summary>One line of the list of marks (comments) in a document.</summary>
/// <param name="Words">For a highlight, underline or strike-through: the words it covers.</param>
/// <param name="Comment">The text typed into the mark (a note's words, or the comment on a highlight).</param>
public sealed record MarkSummaryItem(int Page, string Kind, string Words, string Comment);

public sealed partial class PdfFile
{
    /// <summary>The words that lie inside a rectangle of a page.</summary>
    public string GetTextInRect(int pageIndex, PageRect area)
    {
        Check(); ValidatePage(pageIndex);
        var r = area.Normalized();
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            var text = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                var buffer = new ushort[2048];
                var n = fpdf_text.FPDFTextGetBoundedText(text, r.Left, r.Top, r.Right, r.Bottom, ref buffer[0], buffer.Length - 1);
                return n <= 0 ? "" : new string(buffer.Take(n).Select(c => (char)c).ToArray()).Replace("\r\n", " ").Replace('\n', ' ').Trim();
            }
            finally { fpdf_text.FPDFTextClosePage(text); fpdfview.FPDF_ClosePage(page); }
        }
    }
}

/// <summary>A list of every mark in a document, for reviewers, as plain text or a spreadsheet.</summary>
public static class MarksSummary
{
    static string Describe(AnnotationKind k) => k switch
    {
        AnnotationKind.Highlight => "Highlight", AnnotationKind.Underline => "Underline", AnnotationKind.Strikeout => "Strike through",
        AnnotationKind.Squiggly => "Squiggly underline", AnnotationKind.Note => "Note", AnnotationKind.FreeText => "Text box",
        AnnotationKind.Square => "Box", AnnotationKind.Circle => "Oval", AnnotationKind.Ink => "Drawing", AnnotationKind.Stamp => "Stamp",
        AnnotationKind.Line => "Line", AnnotationKind.Polygon => "Shape", AnnotationKind.PolyLine => "Shape", AnnotationKind.Caret => "Insertion",
        _ => "Mark",
    };

    public static IReadOnlyList<MarkSummaryItem> Build(PdfFile file)
    {
        var skip = new HashSet<AnnotationKind> { AnnotationKind.Link, AnnotationKind.Popup, AnnotationKind.Widget };
        var items = new List<MarkSummaryItem>();
        for (var p = 0; p < file.PageCount; p++)
            foreach (var a in file.GetAnnotations(p).Where(a => !skip.Contains(a.Kind)))
            {
                var words = a.Kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.Strikeout or AnnotationKind.Squiggly
                    ? file.GetTextInRect(p, a.Rect) : "";
                items.Add(new MarkSummaryItem(p + 1, Describe(a.Kind), words, a.Contents.Trim()));
            }
        return items;
    }

    public static string ToText(IReadOnlyList<MarkSummaryItem> items, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Marks in " + title).AppendLine(new string('=', Math.Min(60, 9 + title.Length))).AppendLine();
        if (items.Count == 0) return sb.AppendLine("There are no marks in this document.").ToString();
        foreach (var group in items.GroupBy(i => i.Page))
        {
            sb.AppendLine($"Page {group.Key}");
            foreach (var i in group)
            {
                sb.Append("  - ").Append(i.Kind);
                if (i.Words.Length > 0) sb.Append(": \"").Append(i.Words).Append('"');
                sb.AppendLine();
                if (i.Comment.Length > 0) sb.Append("      ").AppendLine(i.Comment.Replace("\r\n", "\n").Replace("\n", "\n      "));
            }
            sb.AppendLine();
        }
        sb.AppendLine($"{items.Count} mark(s) on {items.Select(i => i.Page).Distinct().Count()} page(s).");
        return sb.ToString();
    }

    public static string ToCsv(IReadOnlyList<MarkSummaryItem> items)
    {
        static string Q(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder("Page,Type,Words marked,Comment\r\n");
        foreach (var i in items) sb.Append(i.Page).Append(',').Append(Q(i.Kind)).Append(',').Append(Q(i.Words)).Append(',').Append(Q(i.Comment)).Append("\r\n");
        return sb.ToString();
    }
}
