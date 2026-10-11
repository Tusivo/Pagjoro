// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class FontTests
{
    static string? Arial => InstalledFonts.Spec("Arial", false, false);

    [Fact]
    public void Installed_families_are_listed_with_their_styles()
    {
        if (Arial is null) return;   // the font is not on this machine
        var arial = InstalledFonts.Families.First(f => f.Name == "Arial");
        Assert.NotNull(arial.Bold);
        Assert.EndsWith("arialbd.ttf", InstalledFonts.Spec("Arial", true, false), StringComparison.OrdinalIgnoreCase);
        Assert.Null(InstalledFonts.Spec("No Such Family", false, false));
        Assert.True(InstalledFonts.IsSystem(Arial));
        Assert.False(InstalledFonts.IsSystem("Helvetica"));
    }

    [Fact]
    public void Letters_beyond_western_europe_can_be_written_with_a_system_font()
    {
        if (Arial is null) return;   // the font is not on this machine
        using var f = PdfFileTests.Sample(1);
        f.AddText(0, "Привет, мир! Καλημέρα κόσμε", 72, 500, 18, PdfColor.Black, Arial!);
        f.AddText(0, "Plain Latin with Ünïcödé: café", 72, 460, 14, PdfColor.Blue, InstalledFonts.Spec("Arial", true, false)!);

        using var again = PdfFile.FromBytes(f.ToBytes());
        var text = again.GetPageText(0);
        Assert.Contains("Привет", text);
        Assert.Contains("Καλημέρα", text);
        Assert.Contains("café", text);
        Assert.NotEmpty(again.Search("мир"));
        // the built-in font still turns what it cannot show into a question mark
        using var g = PdfFileTests.Sample(1);
        g.AddText(0, "Привет", 72, 500, 12, PdfColor.Black, "Helvetica");
        Assert.Contains("??????", PdfFile.FromBytes(g.ToBytes()).GetPageText(0));
    }

    [Fact]
    public void A_font_is_embedded_once_however_often_it_is_used()
    {
        if (Arial is null) return;   // the font is not on this machine
        using var one = PdfFileTests.Sample(1);
        one.AddText(0, "one", 72, 500, 12, PdfColor.Black, Arial!);
        var sizeOne = one.ToBytes().Length;
        using var many = PdfFileTests.Sample(1);
        for (var i = 0; i < 8; i++) many.AddText(0, "line " + i, 72, 500 - i * 20, 12, PdfColor.Black, Arial!);
        var sizeMany = many.ToBytes().Length;
        Assert.True(sizeMany < sizeOne + 20_000, $"eight lines ({sizeMany}) should not carry eight copies of the font (one line {sizeOne})");
    }

    [Fact]
    public void Existing_text_can_be_replaced_in_a_system_font()
    {
        if (InstalledFonts.Spec("Georgia", true, false) is null) return;   // the font is not on this machine
        using var f = PdfFileTests.Sample(1);
        var line = f.GetPageObjects(0).First(o => o.Text.Contains("quick brown fox"));
        f.ReplaceText(0, line.Index, "Новый текст на русском", InstalledFonts.Spec("Georgia", true, false));
        using var again = PdfFile.FromBytes(f.ToBytes());
        Assert.Contains("Новый текст", again.GetPageText(0));
    }

    [Fact]
    public void A_redaction_in_a_line_of_cyrillic_keeps_the_rest_of_the_line()
    {
        if (Arial is null) return;   // the font is not on this machine
        using var f = PdfFileTests.Sample(1);
        f.AddText(0, "Начало СЕКРЕТ конец строки", 72, 400, 14, PdfColor.Black, Arial!);
        var hit = f.Search("СЕКРЕТ").Single();
        var outcome = f.RedactPage(0, hit.Rects, PdfColor.Black);
        Assert.True(outcome.LinesTrimmed >= 1);
        using var again = PdfFile.FromBytes(f.ToBytes());
        var text = again.GetPageText(0);
        Assert.DoesNotContain("СЕКРЕТ", text);
        Assert.Contains("Начало", text);
        Assert.Contains("конец", text);
    }
}
