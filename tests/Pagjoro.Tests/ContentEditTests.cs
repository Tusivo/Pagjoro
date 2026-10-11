// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class ContentEditTests
{
    [Fact]
    public void Text_on_the_page_can_be_found_and_replaced()
    {
        using var f = PdfFileTests.Sample(1);
        var all = f.GetPageObjects(0);
        var fox = all.First(o => o.Kind == PageObjectKind.Text && o.Text.Contains("quick brown fox"));
        Assert.Equal("Times-Roman", fox.Font);
        Assert.InRange(fox.Size, 11.5, 12.5);

        var hit = f.ObjectAt(0, fox.Bounds.Left + 20, (fox.Bounds.Bottom + fox.Bounds.Top) / 2);
        Assert.NotNull(hit);
        Assert.Equal(fox.Index, hit!.Index);

        f.ReplaceText(0, fox.Index, "The slow green turtle naps under the warm sun.");
        using var again = PdfFile.FromBytes(f.ToBytes());
        var text = again.GetPageText(0);
        Assert.DoesNotContain("quick brown fox", text);
        Assert.Contains("slow green turtle", text);
        Assert.Contains("Account number", text);   // other lines untouched
        // it stayed on the same baseline
        var moved = again.GetPageObjects(0).First(o => o.Text.Contains("slow green turtle"));
        Assert.Equal(fox.Y, moved.Y, 1);
        Assert.Equal(fox.X, moved.X, 1);
    }

    [Fact]
    public void An_object_can_be_moved_and_deleted()
    {
        using var f = PdfFileTests.Sample(1);
        var line = f.GetPageObjects(0).First(o => o.Text.Contains("Account number"));
        f.MoveObject(0, line.Index, 50, -100);
        var moved = f.GetPageObjects(0).First(o => o.Text.Contains("Account number"));
        Assert.Equal(line.X + 50, moved.X, 1);
        Assert.Equal(line.Y - 100, moved.Y, 1);

        f.DeleteObject(0, moved.Index);
        Assert.DoesNotContain("Account number", f.GetPageText(0));
        Assert.Contains("quick brown fox", f.GetPageText(0));
    }

    [Fact]
    public void A_picture_can_be_swapped_and_marks_can_be_edited()
    {
        using var f = PdfFileTests.Sample(1);
        var red = new byte[10 * 10 * 4];
        for (var i = 0; i < red.Length; i += 4) { red[i + 2] = 255; red[i + 3] = 255; }
        f.AddImage(0, red, 10, 10, new PageRect(300, 300, 400, 400));
        var picture = f.GetPageObjects(0).First(o => o.Kind == PageObjectKind.Image);
        var green = new byte[10 * 10 * 4];
        for (var i = 0; i < green.Length; i += 4) { green[i + 1] = 255; green[i + 3] = 255; }
        f.ReplaceImage(0, picture.Index, green, 10, 10);
        var img = f.Render(0, 612, 792);
        var o = (792 - 350) * img.Stride + 350 * 4;
        Assert.True(img.Pixels[o + 1] > 200 && img.Pixels[o + 2] < 60, "the picture should now be green");

        f.AddNote(0, 100, 500, "first words", PdfColor.Yellow);
        var note = f.GetAnnotations(0).First(a => a.Kind == AnnotationKind.Note);
        f.SetAnnotationContents(0, note.Index, "edited words");
        f.SetAnnotationColor(0, note.Index, PdfColor.Blue);
        Assert.True(f.MoveAnnotation(0, note.Index, 30, -10));
        var after = f.GetAnnotations(0).First(a => a.Kind == AnnotationKind.Note);
        Assert.Equal("edited words", after.Contents);
        Assert.Equal(note.Rect.Left + 30, after.Rect.Left, 1);
        Assert.Equal(note.Rect.Top - 10, after.Rect.Top, 1);

        f.AddMarkup(0, AnnotationKind.Highlight, [new PageRect(72, 700, 200, 715)], PdfColor.Yellow);
        var highlight = f.GetAnnotations(0).First(a => a.Kind == AnnotationKind.Highlight);
        Assert.False(f.MoveAnnotation(0, highlight.Index, 5, 5), "highlights belong to their words and stay put");
    }
}
