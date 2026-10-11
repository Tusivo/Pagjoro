// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Pagjoro.Core.Pdf;

/// <summary>Reads page lists the way people write them: "1-3, 5, 8-" (pages are numbered from 1; "8-" means page 8 to the end).</summary>
public static class PageRanges
{
    /// <summary>One group per comma-separated part, as zero-based page indexes in the order written (so "3,1" keeps that order).</summary>
    public static IReadOnlyList<IReadOnlyList<int>> ParseGroups(string text, int pageCount)
    {
        if (pageCount < 1) throw new FormatException("The document has no pages.");
        var groups = new List<IReadOnlyList<int>>();
        foreach (var raw in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.Replace('–', '-').Replace('—', '-');
            int from, to;
            var dash = part.IndexOf('-');
            if (dash < 0)
            {
                from = to = Number(part, pageCount, raw);
            }
            else
            {
                var a = part[..dash].Trim(); var b = part[(dash + 1)..].Trim();
                from = a.Length == 0 ? 1 : Number(a, pageCount, raw);
                to = b.Length == 0 ? pageCount : Number(b, pageCount, raw);
            }
            var pages = new List<int>();
            if (from <= to) for (var p = from; p <= to; p++) pages.Add(p - 1);
            else for (var p = from; p >= to; p--) pages.Add(p - 1);
            groups.Add(pages);
        }
        if (groups.Count == 0) throw new FormatException("Type the pages you want, for example 1-3, 5, 8-");
        return groups;
    }

    /// <summary>All the pages named, in the order written, each at most once.</summary>
    public static IReadOnlyList<int> Parse(string text, int pageCount) =>
        ParseGroups(text, pageCount).SelectMany(g => g).Distinct().ToList();

    static int Number(string s, int pageCount, string context)
    {
        if (!int.TryParse(s, out var n)) throw new FormatException($"“{context}” is not a page number.");
        if (n < 1 || n > pageCount) throw new FormatException($"Page {n} does not exist; this document has {pageCount} page{(pageCount == 1 ? "" : "s")}.");
        return n;
    }

    /// <summary>"1-3, 5, 7-9" for a set of zero-based page indexes.</summary>
    public static string Describe(IEnumerable<int> zeroBased)
    {
        var pages = zeroBased.Distinct().OrderBy(p => p).Select(p => p + 1).ToList();
        if (pages.Count == 0) return "";
        var parts = new List<string>(); var start = pages[0]; var prev = start;
        void Flush() => parts.Add(start == prev ? start.ToString() : $"{start}-{prev}");
        foreach (var p in pages.Skip(1))
        {
            if (p == prev + 1) { prev = p; continue; }
            Flush(); start = prev = p;
        }
        Flush();
        return string.Join(", ", parts);
    }
}
