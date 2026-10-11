// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class FormTests
{
    static byte[] Blank() { using var f = PdfFileTests.Sample(1); return f.ToBytes(); }

    static int DarkPixels(byte[] pdf, PageRect area)
    {
        using var f = PdfFile.Open(pdf);
        var img = f.Render(0, 612, 792);
        int dark = 0;
        for (var y = (int)(792 - area.Top); y < (int)(792 - area.Bottom); y++)
            for (var x = (int)area.Left; x < (int)area.Right; x++)
            {
                var o = y * img.Stride + x * 4;
                // ink: clearly darker than the pale field background (viewers may tint text slightly)
                if (img.Pixels[o + 2] < 140) dark++;
            }
        return dark;
    }

    [Fact]
    public void Fields_can_be_added_read_and_filled()
    {
        var box = new PageRect(72, 400, 300, 424);
        var bytes = PdfForms.AddTextField(Blank(), 0, box, "full_name");
        bytes = PdfForms.AddCheckBox(bytes, 0, new PageRect(72, 360, 92, 380), "agree");

        var fields = PdfForms.Read(bytes);
        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, f => f.Name == "full_name" && f.Kind == FormFieldKind.Text && f.Value == "");
        Assert.Contains(fields, f => f.Name == "agree" && f.Kind == FormFieldKind.CheckBox && !f.Checked);

        var before = DarkPixels(bytes, box);
        var filled = PdfForms.Fill(bytes, new Dictionary<string, string> { ["full_name"] = "Jordan Smith" }, new Dictionary<string, bool> { ["agree"] = true });
        var after = PdfForms.Read(filled);
        Assert.Equal("Jordan Smith", after.First(f => f.Name == "full_name").Value);
        Assert.True(after.First(f => f.Name == "agree").Checked);
        // the page draws the typed words: there is more ink inside the box than before
        var inked = DarkPixels(filled, box);
        Assert.True(inked > before + 20, $"the typed text should be visible in the field (before {before}, after {inked})");
        // the rest of the document is untouched
        using var f = PdfFile.Open(filled);
        Assert.Contains("quick brown fox", f.GetPageText(0));
    }

    [Fact]
    public void A_document_without_a_form_says_so_and_bad_requests_are_refused()
    {
        Assert.Empty(PdfForms.Read(Blank()));
        Assert.Throws<PdfException>(() => PdfForms.Fill(Blank(), new Dictionary<string, string>(), new Dictionary<string, bool>()));
        Assert.Throws<PdfException>(() => PdfForms.AddTextField(Blank(), 0, new PageRect(10, 10, 12, 12), "tiny"));
        Assert.Throws<PdfException>(() => PdfForms.AddTextField(Blank(), 0, new PageRect(10, 10, 200, 40), " "));
        Assert.Throws<PdfException>(() => PdfForms.AddTextField(Blank(), 5, new PageRect(10, 10, 200, 40), "x"));
    }
}
