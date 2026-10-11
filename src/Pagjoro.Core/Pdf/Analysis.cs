// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace Pagjoro.Core.Pdf;

/// <summary>A piece of text that matched a pattern, with where it is on the page.</summary>
public sealed record PatternMatch(int PageIndex, string Text, IReadOnlyList<PageRect> Rects);

/// <summary>Ready-made patterns for things people usually have to remove from documents, and the engine that finds them.</summary>
public static partial class SensitivePatterns
{
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        ["Social Security numbers"] = @"\b\d{3}-\d{2}-\d{4}\b",
        ["Email addresses"] = @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        ["Phone numbers"] = @"(?<!\d)(?:\+?1[\s.\-]?)?\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4}(?!\d)",
        ["Credit card numbers"] = @"\b(?:\d[ \-]?){13,19}\b",
        ["Dates (numeric)"] = @"\b\d{1,2}/\d{1,2}/\d{2,4}\b|\b\d{4}-\d{2}-\d{2}\b",
        ["Web addresses"] = @"https?://[^\s<>\""]+",
    };

    /// <summary>All places in the document where the pattern matches. Credit card candidates are kept only when they pass the checksum.</summary>
    public static IReadOnlyList<PatternMatch> Find(PdfFile file, string pattern, bool creditCardChecksum = false, bool ignoreCase = true)
    {
        Regex regex;
        try { regex = new Regex(pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(2)); }
        catch (ArgumentException ex) { throw new PdfException("That pattern is not valid: " + ex.Message); }
        var found = new List<PatternMatch>();
        for (var p = 0; p < file.PageCount; p++)
        {
            var text = file.GetPageText(p);
            try
            {
                foreach (Match m in regex.Matches(text))
                {
                    if (m.Length == 0) continue;
                    if (creditCardChecksum && !Luhn(m.Value)) continue;
                    var (rects, shown) = file.GetTextRange(p, m.Index, m.Length);
                    if (rects.Count > 0) found.Add(new PatternMatch(p, shown, rects));
                }
            }
            catch (RegexMatchTimeoutException) { throw new PdfException("That pattern is too complicated to run on this document. Try a simpler one."); }
        }
        return found;
    }

    public static bool Luhn(string digits)
    {
        var ds = digits.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (ds.Length is < 13 or > 19) return false;
        var sum = 0; var alt = false;
        for (var i = ds.Length - 1; i >= 0; i--)
        {
            var d = ds[i];
            if (alt) { d *= 2; if (d > 9) d -= 9; }
            sum += d; alt = !alt;
        }
        return sum % 10 == 0;
    }
}

public enum ChangeKind { Same, Added, Removed }

public sealed record WordChange(ChangeKind Kind, string Text);

public sealed record PageComparison(int PageA, int PageB, int Added, int Removed, IReadOnlyList<WordChange> Changes)
{
    public bool Identical => Added == 0 && Removed == 0;
}

public sealed record ComparisonResult(int PagesA, int PagesB, IReadOnlyList<PageComparison> Pages)
{
    public int TotalAdded => Pages.Sum(p => p.Added);
    public int TotalRemoved => Pages.Sum(p => p.Removed);
    public bool Identical => PagesA == PagesB && Pages.All(p => p.Identical);
}

/// <summary>Compares the words of two documents page by page.</summary>
public static partial class DocumentComparer
{
    [GeneratedRegex(@"\S+")] private static partial Regex Word();

    public static ComparisonResult Compare(PdfFile a, PdfFile b)
    {
        var pages = new List<PageComparison>();
        var n = Math.Min(a.PageCount, b.PageCount);
        for (var i = 0; i < n; i++) pages.Add(ComparePage(i, i, a.GetPageText(i), b.GetPageText(i)));
        for (var i = n; i < a.PageCount; i++) pages.Add(ComparePage(i, -1, a.GetPageText(i), ""));
        for (var i = n; i < b.PageCount; i++) pages.Add(ComparePage(-1, i, "", b.GetPageText(i)));
        return new ComparisonResult(a.PageCount, b.PageCount, pages);
    }

    public static PageComparison ComparePage(int pageA, int pageB, string textA, string textB)
    {
        var x = Word().Matches(textA).Select(m => m.Value).ToArray();
        var y = Word().Matches(textB).Select(m => m.Value).ToArray();
        var changes = new List<WordChange>();
        int added = 0, removed = 0;
        // trim the common start and end first: documents are usually mostly alike
        var start = 0;
        while (start < x.Length && start < y.Length && x[start] == y[start]) start++;
        var endX = x.Length; var endY = y.Length;
        while (endX > start && endY > start && x[endX - 1] == y[endY - 1]) { endX--; endY--; }
        for (var i = 0; i < start; i++) changes.Add(new WordChange(ChangeKind.Same, x[i]));
        var midX = x[start..endX]; var midY = y[start..endY];
        if ((long)midX.Length * midY.Length > 6_000_000)
        {
            // too large for a word-by-word match: report the middle as one removal and one addition
            foreach (var w in midX) { changes.Add(new WordChange(ChangeKind.Removed, w)); removed++; }
            foreach (var w in midY) { changes.Add(new WordChange(ChangeKind.Added, w)); added++; }
        }
        else
        {
            var lcs = new int[midX.Length + 1, midY.Length + 1];
            for (var i = midX.Length - 1; i >= 0; i--)
                for (var j = midY.Length - 1; j >= 0; j--)
                    lcs[i, j] = midX[i] == midY[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int p = 0, q = 0;
            while (p < midX.Length && q < midY.Length)
            {
                if (midX[p] == midY[q]) { changes.Add(new WordChange(ChangeKind.Same, midX[p])); p++; q++; }
                else if (lcs[p + 1, q] >= lcs[p, q + 1]) { changes.Add(new WordChange(ChangeKind.Removed, midX[p++])); removed++; }
                else { changes.Add(new WordChange(ChangeKind.Added, midY[q++])); added++; }
            }
            while (p < midX.Length) { changes.Add(new WordChange(ChangeKind.Removed, midX[p++])); removed++; }
            while (q < midY.Length) { changes.Add(new WordChange(ChangeKind.Added, midY[q++])); added++; }
        }
        for (var i = endX; i < x.Length; i++) changes.Add(new WordChange(ChangeKind.Same, x[i]));
        return new PageComparison(pageA, pageB, added, removed, changes);
    }
}
