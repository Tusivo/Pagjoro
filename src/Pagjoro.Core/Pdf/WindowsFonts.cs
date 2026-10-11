// SPDX-License-Identifier: AGPL-3.0-or-later
using PdfSharp.Fonts;

namespace Pagjoro.Core.Pdf;

/// <summary>
/// Lets the structure library use the fonts installed in Windows (only needed where it draws text itself, such as the box of a visible
/// signature). The fonts are read from the person's own Fonts folder and are never copied anywhere.
/// </summary>
static class WindowsFonts
{
    sealed class Resolver : IFontResolver
    {
        static string Dir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        static readonly Dictionary<string, string[]> Files = new(StringComparer.OrdinalIgnoreCase)
        {
            ["arial"] = ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
            ["segoe ui"] = ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"],
            ["times new roman"] = ["times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"],
            ["courier new"] = ["cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"],
        };

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var family = Files.ContainsKey(familyName) && File.Exists(Path.Combine(Dir, Files[familyName][0])) ? familyName : "segoe ui";
            if (!File.Exists(Path.Combine(Dir, Files[family][0]))) family = "arial";
            var index = (isBold ? 1 : 0) + (isItalic ? 2 : 0);
            return new FontResolverInfo(family + "|" + index);
        }

        public byte[]? GetFont(string faceName)
        {
            var parts = faceName.Split('|');
            var file = Path.Combine(Dir, Files[parts[0]][int.Parse(parts[1])]);
            if (!File.Exists(file)) file = Path.Combine(Dir, Files[parts[0]][0]);
            return File.ReadAllBytes(file);
        }
    }

    static bool done;

    public static void EnsureRegistered()
    {
        if (done) return;
        done = true;
        try { GlobalFontSettings.FontResolver ??= new Resolver(); }
        catch (InvalidOperationException) { /* a resolver was already set elsewhere */ }
    }
}
