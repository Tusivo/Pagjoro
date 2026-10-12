// SPDX-License-Identifier: AGPL-3.0-or-later
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

public sealed record StampPreset(string Name, string Text, PdfColor Color);



public sealed partial class PdfFile
{
    /// <summary>What the person chose to stamp: the words, a color, and whether today's date goes under them.</summary>
    public sealed record StampChoice(string Text, PdfColor Color, bool WithDate);

    /// <summary>The ready-made stamps offered in the stamp tool.</summary>
    public static readonly IReadOnlyList<StampPreset> StampPresets =
    [
        new("Approved", "APPROVED", new PdfColor(22, 130, 60)),
        new("Not approved", "NOT APPROVED", new PdfColor(190, 30, 30)),
        new("Draft", "DRAFT", new PdfColor(90, 90, 90)),
        new("Final", "FINAL", new PdfColor(30, 80, 190)),
        new("Confidential", "CONFIDENTIAL", new PdfColor(190, 30, 30)),
        new("For review", "FOR REVIEW", new PdfColor(200, 110, 0)),
        new("Reviewed", "REVIEWED", new PdfColor(22, 130, 60)),
        new("Void", "VOID", new PdfColor(190, 30, 30)),
        new("Received", "RECEIVED", new PdfColor(30, 80, 190)),
    ];

    /// <summary>
    /// Puts a rubber-stamp style label (bold capital words in a rounded-corner-free box, with an optional second line such as a date) on a page.
    /// (<paramref name="x"/>, <paramref name="y"/>) is the lower-left corner in page points. Returns the area it covers.
    /// </summary>
    public PageRect AddStamp(int pageIndex, string text, PdfColor color, double x, double y, double size = 24, double rotationDegrees = 0, string? secondLine = null)
    {
        Check(); ValidatePage(pageIndex);
        if (string.IsNullOrWhiteSpace(text)) throw new PdfException("Type the words for the stamp.");
        size = Math.Clamp(size, 6, 120);
        var main = Latin1(text.Trim());
        var second = string.IsNullOrWhiteSpace(secondLine) ? null : Latin1(secondLine.Trim());
        var (w1, h1) = MeasureText(main, size, "Helvetica-Bold");
        var small = size * 0.45;
        var (w2, h2) = second is null ? (0, 0) : MeasureText(second, small, "Helvetica");
        var pad = size * 0.35;
        var width = Math.Max(w1, w2) + pad * 2;
        var height = pad * 2 + h1 + (second is null ? 0 : h2 + pad * 0.5);

        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            try
            {
                var rad = rotationDegrees * Math.PI / 180;
                double c = Math.Cos(rad), s = Math.Sin(rad);

                var frame = fpdf_edit.FPDFPageObjCreateNewRect(0, 0, (float)width, (float)height) ?? throw new PdfException("Pagjoro could not add the stamp.");
                fpdf_edit.FPDFPageObjSetStrokeColor(frame, color.R, color.G, color.B, 255);
                fpdf_edit.FPDFPageObjSetStrokeWidth(frame, (float)Math.Max(1.5, size * 0.1));
                fpdf_edit.FPDFPathSetDrawMode(frame, 0, 1);   // no fill, outline only
                fpdf_edit.FPDFPageObjTransform(frame, c, s, -s, c, x, y);
                fpdf_edit.FPDFPageInsertObject(page, frame);

                void Place(string t, string font, double fontSize, double lx, double ly)
                {
                    var utf16 = PdfEngine.Utf16(t);
                    var obj = fpdf_edit.FPDFPageObjNewTextObj(Doc, font, (float)fontSize);
                    if (obj is null) return;
                    fpdf_edit.FPDFTextSetText(obj, ref utf16[0]);
                    fpdf_edit.FPDFPageObjSetFillColor(obj, color.R, color.G, color.B, 255);
                    // (lx, ly) is inside the stamp's own space; the stamp is then turned and moved as a whole
                    fpdf_edit.FPDFPageObjTransform(obj, c, s, -s, c, x + lx * c - ly * s, y + lx * s + ly * c);
                    fpdf_edit.FPDFPageInsertObject(page, obj);
                }
                var baseline1 = second is null ? pad + h1 * 0.2 : pad + h2 + pad * 0.5 + h1 * 0.2;
                Place(main, "Helvetica-Bold", size, (width - w1) / 2, baseline1);
                if (second is not null) Place(second, "Helvetica", small, (width - w2) / 2, pad + h2 * 0.2);
                fpdf_edit.FPDFPageGenerateContent(page);
                IsModified = true;

                var xs = new[] { 0.0, width, width, 0.0 }; var ys = new[] { 0.0, 0.0, height, height };
                var px = xs.Select((v, i) => x + v * c - ys[i] * s).ToArray();
                var py = xs.Select((v, i) => y + v * s + ys[i] * c).ToArray();
                return new PageRect(px.Min(), py.Min(), px.Max(), py.Max());
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    static string Latin1(string s) => new(s.Select(c => c < 0x20 || c > 0xFF ? '?' : c).ToArray());
}
