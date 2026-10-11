// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class PdfFileTests
{
    /// <summary>A small test document made by Pagjoro itself: no outside files, nothing copyrighted.</summary>
    internal static PdfFile Sample(int pages = 3)
    {
        var file = PdfFile.CreateEmpty();
        for (var i = 0; i < pages; i++)
        {
            file.InsertBlankPage(i, 612, 792);
            file.AddText(i, $"Page {i + 1} of the sample document", 72, 700, 18, PdfColor.Black);
            file.AddText(i, "The quick brown fox jumps over the lazy dog.", 72, 660, 12, PdfColor.Black, "Times-Roman");
            file.AddText(i, "Account number 123-45-6789 is private.", 72, 620, 12, PdfColor.Black, "Courier");
        }
        // save and reopen so the tests exercise exactly what a real file does
        var bytes = file.ToBytes();
        file.Dispose();
        return PdfFile.FromBytes(bytes);
    }

    [Fact]
    public void A_new_document_round_trips_and_has_the_text_that_was_written()
    {
        using var f = Sample(3);
        Assert.Equal(3, f.PageCount);
        Assert.Contains("Page 2 of the sample document", f.GetPageText(1));
        var (w, h) = f.GetPageSize(0);
        Assert.Equal(612, w, 1); Assert.Equal(792, h, 1);
    }

    [Fact]
    public void Pages_render_with_ink_on_white()
    {
        using var f = Sample(1);
        var img = f.Render(0, 612, 792);
        Assert.Equal(612, img.Width); Assert.Equal(792 * img.Stride, img.Pixels.Length);
        Assert.Equal(255, img.Pixels[0]);                       // the corner is white paper
        var dark = 0;
        for (var i = 0; i < img.Pixels.Length; i += 4) if (img.Pixels[i] < 128 && img.Pixels[i + 1] < 128 && img.Pixels[i + 2] < 128) dark++;
        Assert.True(dark > 200, "the text should leave dark pixels on the page");
    }

    [Fact]
    public void Search_finds_text_with_its_boxes_and_respects_case_and_whole_words()
    {
        using var f = Sample(3);
        var hits = f.Search("lazy dog");
        Assert.Equal(3, hits.Count);
        Assert.All(hits, h => Assert.NotEmpty(h.Rects));
        Assert.Equal([0, 1, 2], hits.Select(h => h.PageIndex).ToArray());
        Assert.Empty(f.Search("LAZY DOG", matchCase: true));
        Assert.Single(f.Search("fox", onlyPage: 1));
        Assert.Empty(f.Search("ox", wholeWord: true));
        Assert.Empty(f.Search("no such words"));
        // a box lies where the text is: the top part of the page
        Assert.All(hits, h => Assert.True(h.Rects[0].Bottom > 600 && h.Rects[0].Top < 700));
    }

    [Fact]
    public void Rotating_deleting_and_extracting_pages_work()
    {
        using var f = Sample(4);
        f.RotatePage(1, 90);
        Assert.Equal(90, f.GetPageRotation(1));
        var (w, h) = f.GetPageSize(1);
        Assert.Equal(792, w, 1); Assert.Equal(612, h, 1);
        f.RotatePage(1, -90);
        Assert.Equal(0, f.GetPageRotation(1));

        f.DeletePage(0);
        Assert.Equal(3, f.PageCount);
        Assert.Contains("Page 2 of", f.GetPageText(0));

        using var part = f.ExtractPages([2, 0]);
        Assert.Equal(2, part.PageCount);
        Assert.Contains("Page 4 of", part.GetPageText(0));
        Assert.Contains("Page 2 of", part.GetPageText(1));

        Assert.Throws<PdfException>(() => { using var one = Sample(1); one.DeletePage(0); });
    }

    [Fact]
    public void Pages_from_another_file_are_merged_in_order()
    {
        using var a = Sample(2);
        using var b = Sample(3);
        a.ImportPages(b, [2, 0], 1);        // between a's pages
        Assert.Equal(4, a.PageCount);
        var texts = Enumerable.Range(0, 4).Select(i => a.GetPageText(i)).ToArray();
        Assert.Contains("Page 1 of", texts[0]);
        Assert.Contains("Page 3 of", texts[1]);
        Assert.Contains("Page 1 of", texts[2]);
        Assert.Contains("Page 2 of", texts[3]);
    }

    [Fact]
    public void Marks_are_added_listed_and_removed()
    {
        using var f = Sample(1);
        var hit = f.Search("quick brown fox")[0];
        f.AddMarkup(0, AnnotationKind.Highlight, hit.Rects, PdfColor.Yellow, "check this");
        f.AddNote(0, 400, 500, "A note", PdfColor.Blue);
        f.AddShape(0, AnnotationKind.Square, new PageRect(300, 300, 400, 380), PdfColor.Red);
        f.AddInk(0, [(100, 100), (120, 140), (160, 150), (200, 120)], PdfColor.Green);
        var saved = PdfFile.FromBytes(f.ToBytes());
        var marks = saved.GetAnnotations(0);
        Assert.Contains(marks, m => m.Kind == AnnotationKind.Highlight && m.Contents == "check this");
        Assert.Contains(marks, m => m.Kind == AnnotationKind.Note && m.Contents == "A note");
        Assert.Contains(marks, m => m.Kind == AnnotationKind.Square);
        Assert.Contains(marks, m => m.Kind == AnnotationKind.Ink);

        saved.RemoveAnnotation(0, marks.First(m => m.Kind == AnnotationKind.Note).Index);
        Assert.DoesNotContain(saved.GetAnnotations(0), m => m.Kind == AnnotationKind.Note);
        saved.Dispose();
    }

    [Fact]
    public void Redaction_removes_the_text_from_the_file_not_just_from_view()
    {
        using var f = Sample(1);
        var secret = f.Search("123-45-6789")[0];
        Assert.Contains("123-45-6789", f.GetPageText(0));
        var removed = f.Redact(0, secret.Rects, PdfColor.Black);
        Assert.True(removed >= 1);

        // save, reopen, and look again: the characters are gone from the content
        using var again = PdfFile.FromBytes(f.ToBytes());
        Assert.DoesNotContain("123-45-6789", again.GetPageText(0));
        Assert.Contains("quick brown fox", again.GetPageText(0));
        Assert.Empty(again.Search("123-45-6789"));
        // and the area is black
        var img = again.Render(0, 612, 792);
        var x = (int)secret.Rects[0].Left + 5; var y = 792 - (int)((secret.Rects[0].Bottom + secret.Rects[0].Top) / 2);
        var o = y * img.Stride + x * 4;
        Assert.True(img.Pixels[o] < 30 && img.Pixels[o + 1] < 30 && img.Pixels[o + 2] < 30, "the redacted area should be solid black");
    }

    [Fact]
    public void Text_and_images_can_be_added_and_the_crop_box_changed()
    {
        using var f = Sample(1);
        f.AddFilledRect(0, new PageRect(10, 10, 110, 60), PdfColor.Red);
        var pixels = new byte[10 * 10 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 0; pixels[i + 1] = 200; pixels[i + 2] = 0; pixels[i + 3] = 255; }
        f.AddImage(0, pixels, 10, 10, new PageRect(200, 10, 260, 70));
        var box = f.AddText(0, "STAMP", 300, 40, 20, PdfColor.Blue, "Helvetica-Bold", 0);
        Assert.True(box.Width > 40);
        f.SetCropBox(0, new PageRect(0, 0, 400, 400));
        var saved = PdfFile.FromBytes(f.ToBytes());
        Assert.Contains("STAMP", saved.GetPageText(0));
        var (w, h) = saved.GetPageSize(0);
        Assert.Equal(400, w, 1); Assert.Equal(400, h, 1);
        saved.Dispose();
    }

    [Fact]
    public void Bad_input_gives_plain_errors()
    {
        Assert.Throws<PdfException>(() => PdfFile.Open(new byte[] { 1, 2, 3 }));
        Assert.Throws<PdfException>(() => PdfFile.Open(System.Text.Encoding.ASCII.GetBytes("this is not a pdf file at all, only text")));
        Assert.Throws<PdfException>(() => PdfFile.Open(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".pdf")));
    }

    [Fact]
    public void Saving_writes_a_file_and_clears_the_modified_flag()
    {
        using var f = Sample(2);
        f.RotatePage(0, 90);
        Assert.True(f.IsModified);
        var path = Path.Combine(Path.GetTempPath(), "pagjoro-test-" + Guid.NewGuid() + ".pdf");
        try
        {
            f.SaveTo(path);
            Assert.False(f.IsModified);
            using var reopened = PdfFile.Open(path);
            Assert.Equal(2, reopened.PageCount);
            Assert.Equal(90, reopened.GetPageRotation(0));
        }
        finally { File.Delete(path); }
    }
}
