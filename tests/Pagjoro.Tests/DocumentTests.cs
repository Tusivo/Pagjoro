// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Xml.Linq;
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class DocumentTests
{
    [Fact]
    public void Lines_and_paragraphs_are_read_back_in_order()
    {
        using var f = PdfFileTests.Sample(2);
        var lines = DocumentText.Lines(f, 0);
        Assert.Equal(3, lines.Count);
        Assert.Contains("Page 1 of the sample document", lines[0].Text);   // top to bottom
        Assert.Contains("Account number", lines[2].Text);

        var blocks = DocumentText.Blocks(f, 0, DocumentText.BodySize(f));
        Assert.Equal(3, blocks.Count);
        Assert.True(blocks[0].HeadingLevel >= 1, "the 18-point first line is a heading");
        Assert.Equal(0, blocks[1].HeadingLevel);
    }

    [Fact]
    public void Html_word_and_spreadsheet_exports_hold_the_text()
    {
        using var f = PdfFileTests.Sample(2);
        var html = DocumentText.ToHtml(f, "Sample <test>");
        Assert.Contains("<title>Sample &lt;test&gt;</title>", html);
        Assert.Contains("quick brown fox", html);
        Assert.Contains("page-2", html);

        var docx = DocumentText.ToDocx(f, "Sample");
        using (var zip = new ZipArchive(new MemoryStream(docx)))
        {
            var xml = XDocument.Load(zip.GetEntry("word/document.xml")!.Open());   // well-formed
            Assert.Contains("quick brown fox", xml.ToString());
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        }

        var rows = DocumentText.Rows(f);
        var csv = DocumentText.ToCsv(rows);
        Assert.Contains("quick brown fox", csv);
        var xlsx = DocumentText.ToXlsx([["Name", "Amount"], ["Rent", "1200"], ["Note, with comma", ""]]);
        using var xz = new ZipArchive(new MemoryStream(xlsx));
        var sheet = XDocument.Load(xz.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ToString();
        Assert.Contains("1200", sheet);
        Assert.Contains("Note, with comma", sheet);
    }

    [Fact]
    public void Bookmarks_page_labels_and_attachments_round_trip()
    {
        byte[] bytes;
        using (var f = PdfFileTests.Sample(6)) bytes = f.ToBytes();

        bytes = PdfStructure.SetBookmarks(bytes, [new("Intro", 0, 0), new("Details", 1, 1), new("More", 2, 1), new("End", 5, 0)]);
        using (var f = PdfFile.FromBytes(bytes))
        {
            var marks = f.GetBookmarks();
            Assert.Equal(["Intro", "Details", "More", "End"], marks.Select(m => m.Title).ToArray());
            Assert.Equal([0, 1, 1, 0], marks.Select(m => m.Level).ToArray());
            Assert.Equal(5, marks[3].PageIndex);
        }

        bytes = PdfStructure.SetPageLabels(bytes, [new(0, "roman", "", 1), new(2, "decimal", "", 1), new(5, "decimal", "App-", 1)]);
        Assert.Equal(["i", "ii", "1", "2", "3", "App-1"], PdfStructure.PageLabels(bytes));

        var data = System.Text.Encoding.UTF8.GetBytes("receipt contents");
        bytes = PdfStructure.AddAttachment(bytes, "receipt.txt", data);
        var list = PdfStructure.Attachments(bytes);
        Assert.Single(list);
        Assert.Equal("receipt.txt", list[0].Name);
        Assert.Equal(data, PdfStructure.ExtractAttachment(bytes, "receipt.txt"));
        bytes = PdfStructure.RemoveAttachment(bytes, "receipt.txt");
        Assert.Empty(PdfStructure.Attachments(bytes));
        Assert.Throws<PdfException>(() => PdfStructure.ExtractAttachment(bytes, "receipt.txt"));
        using var again = PdfFile.FromBytes(bytes);
        Assert.Equal(6, again.PageCount);
    }

    [Fact]
    public void Links_can_be_added_and_reading_options_set()
    {
        byte[] bytes;
        using (var f = PdfFileTests.Sample(3)) bytes = f.ToBytes();
        bytes = PdfStructure.AddWebLink(bytes, 0, new PageRect(72, 600, 300, 640), "https://tusivo.com/");
        bytes = PdfStructure.AddPageLink(bytes, 0, new PageRect(72, 500, 300, 540), 2);
        using (var f = PdfFile.FromBytes(bytes))
        {
            Assert.Equal("https://tusivo.com/", f.GetLinkAt(0, 100, 620).Uri);
            Assert.Equal(2, f.GetLinkAt(0, 100, 520).PageIndex);
        }
        Assert.Throws<PdfException>(() => PdfStructure.AddWebLink(bytes, 0, new PageRect(0, 0, 10, 10), "javascript:alert(1)"));

        bytes = PdfStructure.SetReadingOptions(bytes, "en-US", true, "Quarterly Review");
        var results = DocumentChecks.Accessibility(bytes);
        Assert.Contains(results, r => r.Title == "Document title" && r.Status == CheckStatus.Passed);
        Assert.Contains(results, r => r.Title == "Language" && r.Status == CheckStatus.Passed);
        Assert.Contains(results, r => r.Title == "Tagged structure" && r.Status == CheckStatus.Failed);
        Assert.Contains(results, r => r.Title == "Text on every page" && r.Status == CheckStatus.Passed);
    }

    [Fact]
    public void Scanned_pages_and_unembedded_fonts_are_reported()
    {
        byte[] bytes;
        using (var f = PdfFile.CreateEmpty())
        {
            f.InsertBlankPage(0, 612, 792);   // a page with no text at all
            bytes = f.ToBytes();
        }
        var results = DocumentChecks.Accessibility(bytes);
        Assert.Contains(results, r => r.Title == "Text on every page" && r.Status == CheckStatus.Failed && r.Detail.Contains("OCR"));
        Assert.Contains(results, r => r.Title == "Document title" && r.Status == CheckStatus.Failed);

        byte[] withText;
        using (var f = PdfFileTests.Sample(1)) withText = f.ToBytes();
        var print = DocumentChecks.Printing(withText);
        Assert.Contains(print, r => r.Title == "Fonts");
        Assert.Contains(print, r => r.Title == "Protection" && r.Status == CheckStatus.Passed);
    }

    [Fact]
    public void Stamps_land_on_the_page_and_can_be_turned()
    {
        using var f = PdfFileTests.Sample(1);
        var area = f.AddStamp(0, "APPROVED", new PdfColor(22, 130, 60), 300, 400, 28, 0, "2026-10-11");
        Assert.True(area.Width > 100 && area.Height > 30);
        Assert.Contains("APPROVED", f.GetPageText(0));
        Assert.Contains("2026-10-11", f.GetPageText(0));
        var turned = f.AddStamp(0, "DRAFT", PdfColor.Red, 100, 200, 30, 20);
        Assert.True(turned.Height > turned.Width * 0.25);
        Assert.Throws<PdfException>(() => f.AddStamp(0, "  ", PdfColor.Red, 0, 0));
    }

    [Fact]
    public void Hidden_text_can_be_searched_but_leaves_the_page_looking_the_same()
    {
        using var f = PdfFile.CreateEmpty();
        f.InsertBlankPage(0, 612, 792);
        var before = f.Render(0, 612, 792).Pixels.ToArray();
        f.AddHiddenText(0, [("Invoice", new PageRect(100, 600, 200, 620)), ("42-B", new PageRect(220, 600, 270, 620))]);
        Assert.Contains("Invoice", f.GetPageText(0));
        Assert.Single(f.Search("42-B"));
        Assert.Equal(before, f.Render(0, 612, 792).Pixels);
        var hit = f.Search("Invoice")[0].Rects[0];
        Assert.InRange(hit.Left, 95, 105);
    }
}
