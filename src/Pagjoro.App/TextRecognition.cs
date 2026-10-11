// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices.WindowsRuntime;
using Pagjoro.Core.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Pagjoro.App;

/// <summary>
/// Reads the words in a picture of a page with the text recognition that is built into Windows (nothing is uploaded) and lays them
/// over the page as hidden text, so a scanned page becomes searchable and copyable.
/// </summary>
sealed class TextRecognition
{
    readonly OcrEngine engine;

    TextRecognition(OcrEngine engine) { this.engine = engine; }

    /// <summary>The languages the person has set up in Windows that can be recognized.</summary>
    public static IReadOnlyList<string> AvailableLanguages()
    {
        try { return OcrEngine.AvailableRecognizerLanguages.Select(l => l.DisplayName).ToList(); }
        catch (Exception) { return []; }
    }

    /// <summary>Starts the recognizer for the person's languages, or null when Windows has no recognition language installed.</summary>
    public static TextRecognition? Create(string? languageDisplayName = null)
    {
        try
        {
            OcrEngine? e = null;
            if (languageDisplayName is not null)
            {
                var lang = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.DisplayName == languageDisplayName);
                if (lang is not null) e = OcrEngine.TryCreateFromLanguage(lang);
            }
            e ??= OcrEngine.TryCreateFromUserProfileLanguages();
            e ??= OcrEngine.AvailableRecognizerLanguages.Count > 0 ? OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]) : null;
            return e is null ? null : new TextRecognition(e);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Recognizes one page of <paramref name="file"/> and adds the words as hidden text. Returns how many words were found.</summary>
    public async Task<int> RecognizePageAsync(PdfFile file, int pageIndex, int dpi = 300)
    {
        var (w, h) = file.GetPageSize(pageIndex);
        var maxSide = (int)Math.Min(OcrEngine.MaxImageDimension, 6000);
        var scale = Math.Min(dpi / 72.0, maxSide / Math.Max(w, h));
        var wpx = Math.Max(16, (int)Math.Round(w * scale)); var hpx = Math.Max(16, (int)Math.Round(h * scale));
        var image = await Task.Run(() => file.Render(pageIndex, wpx, hpx, annotations: false));

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(image.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);

        var words = new List<(string Text, PageRect Box)>();
        foreach (var line in result.Lines)
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                // picture pixels (origin top-left) to page points (origin bottom-left)
                var left = r.X / image.Width * w; var right = (r.X + r.Width) / image.Width * w;
                var top = h - r.Y / image.Height * h; var bottom = h - (r.Y + r.Height) / image.Height * h;
                words.Add((word.Text, new PageRect(left, bottom, right, top)));
            }
        await Task.Run(() => file.AddHiddenText(pageIndex, words));
        return words.Count;
    }
}
