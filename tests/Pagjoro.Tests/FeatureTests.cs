// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class FeatureTests
{
    [Fact]
    public void Headers_footers_and_page_numbers_fill_in_their_tokens()
    {
        using var f = PdfFileTests.Sample(3);
        PageDecorations.HeaderFooter(f, [0, 1, 2], new HeaderFooterOptions(TopCenter: "Confidential - {date}", BottomCenter: "Page {page} of {pages}"));
        var t = f.GetPageText(1);
        Assert.Contains("Page 2 of 3", t);
        Assert.Contains("Confidential - " + DateTime.Now.ToString("yyyy-MM-dd"), t);
        Assert.Contains("Page 3 of 3", f.GetPageText(2));
    }

    [Fact]
    public void Headers_land_in_the_right_place_even_on_a_turned_page()
    {
        using var f = PdfFileTests.Sample(1);
        f.RotatePage(0, 90);
        PageDecorations.HeaderFooter(f, [0], new HeaderFooterOptions(BottomCenter: "FOOT"));
        // the text must be visible on the turned page: render it and find dark pixels near the bottom-centre of the picture
        var img = f.Render(0, 792, 612);
        var dark = 0;
        for (var y = 612 - 60; y < 612; y++)
            for (var x = 300; x < 500; x++)
            {
                var o = y * img.Stride + x * 4;
                if (img.Pixels[o] < 100 && img.Pixels[o + 1] < 100 && img.Pixels[o + 2] < 100) dark++;
            }
        Assert.True(dark > 30, "the footer should appear along the bottom of the turned page");
    }

    [Fact]
    public void Watermarks_and_bates_numbers_are_added()
    {
        using var f = PdfFileTests.Sample(2);
        PageDecorations.Watermark(f, [0, 1], new WatermarkOptions("DRAFT"));
        Assert.Contains("DRAFT", f.GetPageText(0));
        var (first, last) = PageDecorations.Bates(f, [0, 1], new BatesOptions("ABC-", 41, 6));
        Assert.Equal("ABC-000041", first); Assert.Equal("ABC-000042", last);
        Assert.Contains("ABC-000042", f.GetPageText(1));
        Assert.Throws<PdfException>(() => PageDecorations.Watermark(f, [0], new WatermarkOptions("  ")));
    }

    [Fact]
    public void Patterns_find_private_numbers_and_the_matches_can_be_redacted()
    {
        using var f = PdfFileTests.Sample(2);
        var ssn = SensitivePatterns.Find(f, SensitivePatterns.Presets["Social Security numbers"]);
        Assert.Equal(2, ssn.Count);
        Assert.All(ssn, m => Assert.Equal("123-45-6789", m.Text));
        foreach (var m in ssn) f.Redact(m.PageIndex, m.Rects, PdfColor.Black);
        Assert.Empty(SensitivePatterns.Find(f, SensitivePatterns.Presets["Social Security numbers"]));
        Assert.Contains("quick brown fox", f.GetPageText(0));
        Assert.Throws<PdfException>(() => SensitivePatterns.Find(f, "(unclosed"));
    }

    [Fact]
    public void Luhn_accepts_real_card_checksums_only()
    {
        Assert.True(SensitivePatterns.Luhn("4539 1488 0343 6467"));
        Assert.True(SensitivePatterns.Luhn("4111-1111-1111-1111"));
        Assert.False(SensitivePatterns.Luhn("4111 1111 1111 1112"));
        Assert.False(SensitivePatterns.Luhn("1234"));
    }

    [Fact]
    public void Comparing_two_versions_reports_what_changed()
    {
        var c = DocumentComparer.ComparePage(0, 0, "The total is 100 dollars and due on Friday.", "The total is 250 dollars and due on Monday.");
        Assert.Equal(2, c.Added); Assert.Equal(2, c.Removed);
        Assert.Contains(c.Changes, w => w.Kind == ChangeKind.Removed && w.Text == "100");
        Assert.Contains(c.Changes, w => w.Kind == ChangeKind.Added && w.Text == "250");
        Assert.True(DocumentComparer.ComparePage(0, 0, "same words here", "same words here").Identical);

        using var a = PdfFileTests.Sample(2);
        using var b = PdfFileTests.Sample(3);
        var result = DocumentComparer.Compare(a, b);
        Assert.Equal(2, result.PagesA); Assert.Equal(3, result.PagesB);
        Assert.False(result.Identical);                     // a page was added
        Assert.True(result.TotalAdded > 0);
        using var same = PdfFileTests.Sample(2);
        Assert.True(DocumentComparer.Compare(a, same).Identical);
    }

    [Fact]
    public void Recent_files_keep_the_newest_first_and_forget_missing_ones()
    {
        var dir = Directory.CreateTempSubdirectory("pagjoro-recent-").FullName;
        try
        {
            var f1 = Path.Combine(dir, "a.pdf"); var f2 = Path.Combine(dir, "b.pdf");
            File.WriteAllText(f1, "x"); File.WriteAllText(f2, "x");
            var r = new Pagjoro.Core.RecentFiles(dir, 5);
            r.Add(f1); r.Add(f2); r.Add(f1);
            Assert.Equal([f1, f2], r.Items.ToArray());
            File.Delete(f2);
            Assert.Equal([f1], new Pagjoro.Core.RecentFiles(dir, 5).Items.ToArray());   // reloaded from disk; the missing file is gone
        }
        finally { Directory.Delete(dir, true); }
    }
}
