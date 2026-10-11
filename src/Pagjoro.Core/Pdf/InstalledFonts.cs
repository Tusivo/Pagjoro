// SPDX-License-Identifier: AGPL-3.0-or-later
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

/// <summary>A font family installed in Windows that Pagjoro can put on a page.</summary>
public sealed record FontFamilyInfo(string Name, string Regular, string? Bold, string? Italic, string? BoldItalic);

/// <summary>
/// The fonts of Windows that can be used for text on a page, besides the twelve built-in PDF fonts. A choice is passed around as a text
/// spec: a built-in font name such as "Helvetica-Bold", or "ttf:" and the path of a font file.
/// </summary>
public static class InstalledFonts
{
    public const string Prefix = "ttf:";

    // display name, then the file names of regular, bold, italic, bold italic (only TrueType outlines, which every PDF reader can embed)
    static readonly (string Name, string Regular, string? Bold, string? Italic, string? BoldItalic)[] Known =
    [
        ("Arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        ("Calibri", "calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf"),
        ("Cambria", "cambria.ttc", "cambriab.ttf", "cambriai.ttf", "cambriaz.ttf"),
        ("Candara", "Candara.ttf", "Candarab.ttf", "Candarai.ttf", "Candaraz.ttf"),
        ("Comic Sans MS", "comic.ttf", "comicbd.ttf", "comici.ttf", "comicz.ttf"),
        ("Consolas", "consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"),
        ("Constantia", "constan.ttf", "constanb.ttf", "constani.ttf", "constanz.ttf"),
        ("Corbel", "corbel.ttf", "corbelb.ttf", "corbeli.ttf", "corbelz.ttf"),
        ("Courier New", "cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
        ("Franklin Gothic Medium", "framd.ttf", null, "framdit.ttf", null),
        ("Georgia", "georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf"),
        ("Impact", "impact.ttf", null, null, null),
        ("Lucida Console", "lucon.ttf", null, null, null),
        ("Palatino Linotype", "pala.ttf", "palab.ttf", "palai.ttf", "palabi.ttf"),
        ("Segoe UI", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        ("Tahoma", "tahoma.ttf", "tahomabd.ttf", null, null),
        ("Times New Roman", "times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
        ("Trebuchet MS", "trebuc.ttf", "trebucbd.ttf", "trebucit.ttf", "trebucbi.ttf"),
        ("Verdana", "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
    ];

    static IReadOnlyList<FontFamilyInfo>? cache;
    static string? folderOverride;

    /// <summary>For tests: look for fonts in another folder.</summary>
    internal static void UseFolder(string? folder) { folderOverride = folder; cache = null; }

    static string Folder => folderOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    /// <summary>The families that are installed, in alphabetical order.</summary>
    public static IReadOnlyList<FontFamilyInfo> Families
    {
        get
        {
            if (cache is not null) return cache;
            var list = new List<FontFamilyInfo>();
            var dir = Folder;
            string? Path(string? file) { if (file is null) return null; var p = System.IO.Path.Combine(dir, file); return File.Exists(p) ? p : null; }
            foreach (var (name, regular, bold, italic, boldItalic) in Known)
            {
                var r = Path(regular);
                if (r is null) continue;
                list.Add(new FontFamilyInfo(name, r, Path(bold), Path(italic), Path(boldItalic)));
            }
            return cache = list;
        }
    }

    /// <summary>The spec for a family, weight and slant; falls back to the nearest style the family has. Null if the family is not installed.</summary>
    public static string? Spec(string familyName, bool bold, bool italic)
    {
        var f = Families.FirstOrDefault(x => string.Equals(x.Name, familyName, StringComparison.OrdinalIgnoreCase));
        if (f is null) return null;
        var path = (bold, italic) switch
        {
            (true, true) => f.BoldItalic ?? f.Bold ?? f.Italic ?? f.Regular,
            (true, false) => f.Bold ?? f.Regular,
            (false, true) => f.Italic ?? f.Regular,
            _ => f.Regular,
        };
        return Prefix + path;
    }

    /// <summary>A font that covers a wide range of letters (Latin, Greek, Cyrillic), used when text goes beyond the built-in fonts.</summary>
    public static string? Wide() => Spec("Arial", false, false) ?? Spec("Segoe UI", false, false) ?? Spec("Verdana", false, false);

    public static bool IsSystem(string? spec) => spec is not null && spec.StartsWith(Prefix, StringComparison.Ordinal);
}

public sealed partial class PdfFile
{
    readonly Dictionary<string, FpdfFontT> loadedFonts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Makes a text object in a built-in font or a font file ("ttf:" and a path). A font file is embedded in the document once, however many
    /// pieces of text use it. Caller holds the engine gate.
    /// </summary>
    FpdfPageobjectT? NewTextObject(string fontSpec, double size)
    {
        if (!InstalledFonts.IsSystem(fontSpec))
            return fpdf_edit.FPDFPageObjNewTextObj(Doc, StandardFonts.Contains(fontSpec) ? fontSpec : "Helvetica", (float)size);
        if (!loadedFonts.TryGetValue(fontSpec, out var font))
        {
            var path = fontSpec[InstalledFonts.Prefix.Length..];
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new PdfException("That font could not be read: " + System.IO.Path.GetFileName(path)); }
            unsafe
            {
                fixed (byte* p = data)
                {
                    // 2 = TrueType; the last argument asks for a font that maps every letter, not just the Western European ones
                    font = fpdf_edit.FPDFTextLoadFont(Doc, p, (uint)data.Length, 2, 1)
                        ?? throw new PdfException("That font could not be used: " + System.IO.Path.GetFileName(path));
                }
            }
            loadedFonts[fontSpec] = font;
        }
        return fpdf_edit.FPDFPageObjCreateTextObj(Doc, font, (float)size);
    }

    void CloseFonts()
    {
        foreach (var f in loadedFonts.Values) { try { fpdf_edit.FPDFFontClose(f); } catch (Exception) { /* closing at exit */ } }
        loadedFonts.Clear();
    }

    /// <summary>Replaces anything the font cannot show: control characters always, and letters beyond Latin-1 when the font is a built-in one.</summary>
    static string FitToFont(string text, string fontSpec) =>
        InstalledFonts.IsSystem(fontSpec)
            ? new string(text.Select(c => c < 0x20 ? ' ' : c).ToArray())
            : new string(text.Select(c => c < 0x20 || c > 0xFF ? '?' : c).ToArray());
}
