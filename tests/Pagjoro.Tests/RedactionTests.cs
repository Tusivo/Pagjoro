// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class RedactionTests
{
    [Fact]
    public void Only_the_covered_letters_of_a_line_are_removed()
    {
        using var f = PdfFileTests.Sample(1);
        var secret = f.Search("123-45-6789")[0];
        var outcome = f.RedactPage(0, secret.Rects, PdfColor.Black);
        Assert.True(outcome.LinesTrimmed >= 1, "the line should be trimmed, not thrown away");
        Assert.False(outcome.PageRasterized);

        using var again = PdfFile.FromBytes(f.ToBytes());
        var text = again.GetPageText(0);
        Assert.DoesNotContain("123-45-6789", text);
        Assert.DoesNotContain("6789", text);
        Assert.Contains("Account number", text);        // the start of the same line survives
        Assert.Contains("is private", text);            // and so does the end
        Assert.Contains("quick brown fox", text);       // other lines are untouched
    }

    [Fact]
    public void A_picture_keeps_its_uncovered_pixels()
    {
        using var f = PdfFileTests.Sample(1);
        var pixels = new byte[20 * 20 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 255; pixels[i + 3] = 255; }   // red
        f.AddImage(0, pixels, 20, 20, new PageRect(100, 300, 200, 400));
        using var withImage = PdfFile.FromBytes(f.ToBytes());

        var outcome = withImage.RedactPage(0, [new PageRect(100, 300, 150, 400)], PdfColor.Black);
        Assert.True(outcome.PicturesMasked >= 1);

        using var again = PdfFile.FromBytes(withImage.ToBytes());
        var img = again.Render(0, 612, 792);
        int Px(int x, int yPage) { var y = 792 - yPage; var o = y * img.Stride + x * 4; return img.Pixels[o + 2]; }   // red channel
        Assert.True(Px(125, 350) < 40, "the covered half is black");
        Assert.True(Px(175, 350) > 200, "the uncovered half is still the picture");
    }

    [Fact]
    public void A_box_over_nothing_changes_nothing_but_the_block()
    {
        using var f = PdfFileTests.Sample(1);
        var before = f.GetPageText(0);
        var outcome = f.RedactPage(0, [new PageRect(400, 10, 500, 30)], PdfColor.Black);
        Assert.Equal(0, outcome.MarksRemoved + outcome.ObjectsRemoved + outcome.LinesTrimmed + outcome.PicturesMasked);
        Assert.Equal(before, f.GetPageText(0));
    }
}
