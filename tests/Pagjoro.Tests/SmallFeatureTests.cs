// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core;
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class SmallFeatureTests
{
    static string TempDir() => Path.Combine(Path.GetTempPath(), "pagjoro-small-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_page_and_zoom_of_a_file_are_remembered_between_runs()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "Report.pdf");
            var memory = new ViewMemory(dir);
            Assert.Null(memory.Get(path));
            memory.Remember(path, new ViewState(7, 1.5, 1, true));
            var again = new ViewMemory(dir).Get(path.ToUpperInvariant());
            Assert.Equal(new ViewState(7, 1.5, 1, true), again);

            memory.Remember(path, new ViewState(9, 2.0));
            Assert.Equal(9, new ViewMemory(dir).Get(path)!.Page);
            memory.Remember(path, new ViewState(-1, 1));          // nonsense is ignored
            memory.Remember(path, new ViewState(3, double.NaN));
            Assert.Equal(9, new ViewMemory(dir).Get(path)!.Page);

            File.WriteAllText(Path.Combine(dir, "view-state.json"), "{ not json");
            Assert.Null(new ViewMemory(dir).Get(path));            // a damaged file is simply forgotten
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Only_the_newest_hundred_files_are_remembered()
    {
        var dir = TempDir();
        try
        {
            var memory = new ViewMemory(dir);
            for (var i = 0; i < 120; i++) memory.Remember(Path.Combine(dir, $"f{i}.pdf"), new ViewState(i, 1));
            var again = new ViewMemory(dir);
            Assert.Null(again.Get(Path.Combine(dir, "f0.pdf")));
            Assert.NotNull(again.Get(Path.Combine(dir, "f119.pdf")));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void The_window_position_is_kept_next_to_the_theme()
    {
        var dir = TempDir();
        try
        {
            var settings = new AppSettings(dir);
            settings.SetTheme("night");
            settings.SetWindow(new WindowPlacement(120, 80, 1500, 900, true));
            var again = new AppSettings(dir);
            Assert.Equal("night", again.Theme);                                   // saving the window did not lose the theme
            Assert.Equal(new WindowPlacement(120, 80, 1500, 900, true), again.Window);
            again.SetTheme("light");
            Assert.Equal(1500, new AppSettings(dir).Window!.Width);               // and the other way round
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"theme\":\"dark\",\"window\":{\"left\":1,\"top\":1,\"width\":5,\"height\":5}}");
            Assert.Null(new AppSettings(dir).Window);                             // a tiny window is ignored
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void The_marks_list_has_the_words_marked_and_the_comments()
    {
        using var f = PdfFileTests.Sample(2);
        var hit = f.Search("quick brown fox")[0];
        f.AddMarkup(hit.PageIndex, AnnotationKind.Highlight, hit.Rects, PdfColor.Yellow, "check this wording");
        f.AddNote(1, 100, 500, "Ask legal.\nSecond line", PdfColor.Orange);
        f.AddShape(0, AnnotationKind.Square, new PageRect(300, 300, 400, 380), PdfColor.Pink);

        var items = MarksSummary.Build(f);
        Assert.Equal(3, items.Count);
        var highlight = items.Single(i => i.Kind == "Highlight");
        Assert.Contains("quick brown fox", highlight.Words);
        Assert.Equal("check this wording", highlight.Comment);
        Assert.Equal(2, items.Single(i => i.Kind == "Note").Page);

        var text = MarksSummary.ToText(items, "Sample.pdf");
        Assert.Contains("Page 1", text);
        Assert.Contains("\"quick brown fox", text);
        Assert.Contains("Ask legal.", text);
        var csv = MarksSummary.ToCsv(items);
        Assert.StartsWith("Page,Type,Words marked,Comment", csv);
        Assert.Contains("\"Ask legal.\nSecond line\"", csv);   // a comment with a line break stays one cell
        Assert.Contains("There are no marks", MarksSummary.ToText([], "x.pdf"));
    }

    [Fact]
    public void Turning_the_view_swaps_the_page_size_and_the_picture_but_not_the_file()
    {
        using var f = PdfFileTests.Sample(1);
        var (w, h) = f.GetPageSize(0);
        Assert.Equal((w, h), f.GetViewSize(0));
        var before = f.Render(0, 306, 396);
        Assert.Equal(396, before.Height);

        f.ViewRotation = 1;
        var (vw, vh) = f.GetViewSize(0);
        Assert.Equal((h, w), (vw, vh));
        var turned = f.Render(0, 396, 306);
        Assert.Equal(396, turned.Width);
        // the stored page rotation is untouched, so a saved copy is not turned
        Assert.Equal(0, f.GetPageRotation(0));
        using var saved = PdfFile.FromBytes(f.ToBytes());
        Assert.Equal((w, h), saved.GetPageSize(0));

        // a point at the top-left of the page lands at the top-right of a view turned clockwise
        var topLeft = f.PageToDevice(0, 396, 306, 0, h);
        Assert.True(topLeft.X > 300, $"top-left of the page should be at the right edge, was {topLeft.X}");
        Assert.True(topLeft.Y < 5);
        var back = f.DeviceToPage(0, 396, 306, topLeft.X, topLeft.Y);
        Assert.Equal(0, back.X, 1);
        Assert.Equal(h, back.Y, 1);
    }
}
