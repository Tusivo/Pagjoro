// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

/// <summary>What a redaction actually did to a page.</summary>
/// <param name="MarksRemoved">Comments and other marks that touched an area, deleted.</param>
/// <param name="ObjectsRemoved">Whole page objects (lines that could not be split, shapes, shadings) deleted.</param>
/// <param name="LinesTrimmed">Text lines that were cut: the covered letters are gone and the rest was kept (re-set in a built-in font).</param>
/// <param name="PicturesMasked">Pictures where only the covered pixels were blanked.</param>
/// <param name="PageRasterized">True when the page had content that cannot be edited in place, so it was turned into a picture with the areas blacked out.</param>
public sealed record RedactionOutcome(int MarksRemoved, int ObjectsRemoved, int LinesTrimmed, int PicturesMasked, bool PageRasterized)
{
    public int Total => MarksRemoved + ObjectsRemoved + LinesTrimmed + PicturesMasked + (PageRasterized ? 1 : 0);
}

public sealed partial class PdfFile
{
    /// <summary>
    /// Permanently removes what lies under the given areas, then draws a solid block there. Only the covered letters of a text line are
    /// removed (the rest of the line is kept), only the covered pixels of a picture are blanked, and marks that touch an area are deleted.
    /// A page whose content cannot be edited in place (text inside an embedded form object) is turned into a picture with the areas blacked out.
    /// </summary>
    public RedactionOutcome RedactPage(int pageIndex, IReadOnlyList<PageRect> areas, PdfColor fill)
    {
        Check(); ValidatePage(pageIndex);
        if (areas.Count == 0) return new RedactionOutcome(0, 0, 0, 0, false);
        var boxes = areas.Select(a => a.Normalized()).ToList();
        int marks = 0, removed = 0, trimmed = 0, masked = 0; var raster = false;
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            var textPage = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                // marks first (highest index first so the numbering stays valid)
                for (var i = fpdf_annot.FPDFPageGetAnnotCount(page) - 1; i >= 0; i--)
                {
                    var annot = fpdf_annot.FPDFPageGetAnnot(page, i);
                    if (annot is null) continue;
                    var rect = new FS_RECTF_(); var hit = false;
                    try
                    {
                        var subtype = fpdf_annot.FPDFAnnotGetSubtype(annot);
                        if (subtype != (int)AnnotationKind.Link && fpdf_annot.FPDFAnnotGetRect(annot, rect) != 0)
                            hit = boxes.Any(b => b.Intersects(new PageRect(rect.Left, rect.Bottom, rect.Right, rect.Top)));
                    }
                    finally { fpdf_annot.FPDFPageCloseAnnot(annot); }
                    if (hit && fpdf_annot.FPDFPageRemoveAnnot(page, i) != 0) marks++;
                }

                string? pageText = null;
                var count = fpdf_edit.FPDFPageCountObjects(page);
                // the objects are visited from the end so deleting one does not disturb the numbering of the rest
                var retyped = new List<(string Text, string Font, double Size, PdfColor Color, double X, double Y, double Angle, double Width)>();
                for (var i = count - 1; i >= 0; i--)
                {
                    var obj = fpdf_edit.FPDFPageGetObject(page, i);
                    if (obj is null) continue;
                    float l = 0, b = 0, r = 0, t = 0;
                    if (fpdf_edit.FPDFPageObjGetBounds(obj, ref l, ref b, ref r, ref t) == 0) continue;
                    var ob = new PageRect(l, b, r, t);
                    if (!boxes.Any(x => x.Intersects(ob))) continue;
                    var type = fpdf_edit.FPDFPageObjGetType(obj);   // 1 text, 2 path, 3 image, 4 shading, 5 form
                    switch (type)
                    {
                        case 1:
                            if (textPage is not null && TrimTextObject(page, textPage, obj, ob, boxes, ref pageText, retyped)) { trimmed++; break; }
                            if (fpdf_edit.FPDFPageRemoveObject(page, obj) != 0) { fpdf_edit.FPDFPageObjDestroy(obj); removed++; }
                            break;
                        case 3:
                            if (MaskPicture(obj, boxes)) masked++;
                            else if (fpdf_edit.FPDFPageRemoveObject(page, obj) != 0) { fpdf_edit.FPDFPageObjDestroy(obj); removed++; }
                            break;
                        case 5:
                            // text and pictures inside an embedded form object cannot be edited one by one
                            if (fpdf_edit.FPDFFormObjCountObjects(obj) > 0) raster = true;
                            else if (fpdf_edit.FPDFPageRemoveObject(page, obj) != 0) { fpdf_edit.FPDFPageObjDestroy(obj); removed++; }
                            break;
                        default:
                            // a page-sized background (a shape covering most of the page) is kept, so only the objects inside are lost
                            if (type == 2 && IsPageSized(page, ob)) break;
                            if (fpdf_edit.FPDFPageRemoveObject(page, obj) != 0) { fpdf_edit.FPDFPageObjDestroy(obj); removed++; }
                            break;
                    }
                }

                if (raster) RasterizePage(page, boxes, fill);
                else
                {
                    // the retyped remainders of trimmed lines go back in first, then the solid blocks on top
                    foreach (var run in retyped) InsertRetypedRun(page, run);
                    foreach (var box in boxes)
                    {
                        var rect = fpdf_edit.FPDFPageObjCreateNewRect((float)box.Left, (float)box.Bottom, (float)box.Width, (float)box.Height);
                        fpdf_edit.FPDFPageObjSetFillColor(rect, fill.R, fill.G, fill.B, 255);
                        fpdf_edit.FPDFPathSetDrawMode(rect, 1, 0);
                        fpdf_edit.FPDFPageInsertObject(page, rect);
                    }
                }
                fpdf_edit.FPDFPageGenerateContent(page);
            }
            finally
            {
                if (textPage is not null) fpdf_text.FPDFTextClosePage(textPage);
                fpdfview.FPDF_ClosePage(page);
            }
        }
        IsModified = true;
        return new RedactionOutcome(marks, removed, trimmed, masked, raster);
    }

    // ---- text ---------------------------------------------------------------------------------------------------------

    static string ReadObjectText(FpdfPageobjectT obj, FpdfTextpageT textPage)
    {
        ushort probe = 0;
        var bytes = fpdf_edit.FPDFTextObjGetText(obj, textPage, ref probe, 0);
        if (bytes <= 2) return "";
        var buffer = new ushort[bytes / 2 + 1];
        fpdf_edit.FPDFTextObjGetText(obj, textPage, ref buffer[0], bytes);
        return new string(buffer.Take((int)(bytes / 2) - 1).Select(c => (char)c).ToArray());
    }

    /// <summary>
    /// Cuts the covered letters out of one text object and keeps the rest. Returns false when the letters cannot be located reliably, in
    /// which case the caller deletes the whole object. The object itself is removed and its remaining runs are queued for re-insertion.
    /// </summary>
    bool TrimTextObject(FpdfPageT page, FpdfTextpageT textPage, FpdfPageobjectT obj, PageRect bounds, List<PageRect> boxes, ref string? pageText,
        List<(string Text, string Font, double Size, PdfColor Color, double X, double Y, double Angle, double Width)> into)
    {
        var s = ReadObjectText(obj, textPage);
        if (s.Length == 0) return false;
        if (pageText is null)
        {
            var n = fpdf_text.FPDFTextCountChars(textPage);
            if (n <= 0) return false;
            var buffer = new ushort[n + 1];
            var written = fpdf_text.FPDFTextGetText(textPage, 0, n, ref buffer[0]);
            pageText = written <= 1 ? "" : new string(buffer.Take(written - 1).Select(c => (char)c).ToArray());
        }
        // locate this object's letters in the page text: the occurrence whose first letter lies inside the object's bounds
        var start = -1;
        for (var from = 0; (from = pageText.IndexOf(s, from, StringComparison.Ordinal)) >= 0; from++)
        {
            double cl = 0, cr = 0, cb = 0, ct = 0;
            if (fpdf_text.FPDFTextGetCharBox(textPage, from, ref cl, ref cr, ref cb, ref ct) == 0) continue;
            var cx = (cl + cr) / 2; var cy = (cb + ct) / 2;
            if (cx >= bounds.Left - 1 && cx <= bounds.Right + 1 && cy >= bounds.Bottom - 1 && cy <= bounds.Top + 1) { start = from; break; }
        }
        if (start < 0) return false;

        var keep = new bool[s.Length];
        var boxOf = new (double L, double R, double B, double T)[s.Length];
        for (var k = 0; k < s.Length; k++)
        {
            double cl = 0, cr = 0, cb = 0, ct = 0;
            if (fpdf_text.FPDFTextGetCharBox(textPage, start + k, ref cl, ref cr, ref cb, ref ct) == 0) return false;
            boxOf[k] = (cl, cr, cb, ct);
            keep[k] = !boxes.Any(x => Overlaps(x, cl, cr, cb, ct));
        }

        // the object's look: size and colour from the object; a built-in font of the same family and weight
        float fontSize = 0;
        fpdf_edit.FPDFTextObjGetFontSize(obj, ref fontSize);
        var measured = fpdf_text.FPDFTextGetFontSize(textPage, start);
        var size = measured > 0 ? measured : fontSize > 0 ? fontSize : 11;
        uint cr8 = 0, cg8 = 0, cb8 = 0, ca8 = 255;
        fpdf_edit.FPDFPageObjGetFillColor(obj, ref cr8, ref cg8, ref cb8, ref ca8);
        var color = new PdfColor((byte)cr8, (byte)cg8, (byte)cb8, (byte)(ca8 == 0 ? 255 : ca8));
        var font = StandardFontFor(fpdf_edit.FPDFTextObjGetFont(obj));

        var runs = new List<(string Text, string Font, double Size, PdfColor Color, double X, double Y, double Angle, double Width)>();
        var k0 = 0;
        while (k0 < s.Length)
        {
            if (!keep[k0]) { k0++; continue; }
            var k1 = k0;
            while (k1 + 1 < s.Length && keep[k1 + 1]) k1++;
            var piece = s.Substring(k0, k1 - k0 + 1);
            if (piece.Trim().Length > 0)
            {
                var runFont = font;
                if (piece.Any(c => c > 0xFF))
                {
                    runFont = InstalledFonts.Wide() ?? "";
                    if (runFont.Length == 0) return false;   // no font that can show these letters: remove the whole line instead
                }
                double ox = 0, oy = 0;
                fpdf_text.FPDFTextGetCharOrigin(textPage, start + k0, ref ox, ref oy);
                var angle = fpdf_text.FPDFTextGetCharAngle(textPage, start + k0);
                runs.Add((piece.TrimEnd(), runFont, size, color, ox, oy, angle * 180 / Math.PI, boxOf[k1].R - boxOf[k0].L));
            }
            k0 = k1 + 1;
        }

        if (fpdf_edit.FPDFPageRemoveObject(page, obj) == 0) return false;
        fpdf_edit.FPDFPageObjDestroy(obj);
        into.AddRange(runs);
        return true;
    }

    static bool Overlaps(PageRect box, double l, double r, double b, double t)
    {
        var w = r - l; var h = t - b;
        if (w <= 0 || h <= 0) return box.Left <= l && l <= box.Right && box.Bottom <= b && b <= box.Top;
        var ox = Math.Min(box.Right, r) - Math.Max(box.Left, l);
        var oy = Math.Min(box.Top, t) - Math.Max(box.Bottom, b);
        // a letter counts as covered when a fifth of it lies inside the area (so neighbours that merely touch are kept)
        return ox > 0 && oy > 0 && ox / w >= 0.2 && oy / h >= 0.2;
    }

    static unsafe string FontNameOf(FpdfFontT font)
    {
        var buffer = new sbyte[256];
        fixed (sbyte* p = buffer)
        {
            var n = fpdf_edit.FPDFFontGetFontName(font, p, (uint)buffer.Length);
            if (n <= 1) return "";
            return new string(buffer.Take((int)Math.Min(n, 255) - 1).Select(c => (char)(byte)c).ToArray());
        }
    }

    /// <summary>The built-in font that looks most like a font: its family (serif, sans, fixed width), weight and slant.</summary>
    static string StandardFontFor(FpdfFontT? font)
    {
        if (font is null) return "Helvetica";
        var flags = fpdf_edit.FPDFFontGetFlags(font);
        var weight = fpdf_edit.FPDFFontGetWeight(font);
        int angle = 0;
        fpdf_edit.FPDFFontGetItalicAngle(font, ref angle);
        var name = FontNameOf(font);
        bool Has(params string[] parts) => parts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
        var bold = weight >= 600 || (flags & 0x40000) != 0 || Has("Bold", "Black", "Heavy", "Semibold");
        var italic = (flags & 0x40) != 0 || angle != 0 || Has("Italic", "Oblique");
        var fixedWidth = (flags & 1) != 0 || Has("Courier", "Mono", "Consolas", "Typewriter", "Lucida Console");
        var serif = (flags & 2) != 0 || Has("Times", "Serif", "Georgia", "Garamond", "Palatino", "Cambria", "Minion", "Century", "Book", "Roman");
        if (Has("Sans", "Arial", "Helvetica", "Calibri", "Verdana", "Tahoma")) serif = false;
        if (fixedWidth) return bold ? (italic ? "Courier-BoldOblique" : "Courier-Bold") : (italic ? "Courier-Oblique" : "Courier");
        if (serif) return bold ? (italic ? "Times-BoldItalic" : "Times-Bold") : (italic ? "Times-Italic" : "Times-Roman");
        return bold ? (italic ? "Helvetica-BoldOblique" : "Helvetica-Bold") : (italic ? "Helvetica-Oblique" : "Helvetica");
    }
    void InsertRetypedRun(FpdfPageT page, (string Text, string Font, double Size, PdfColor Color, double X, double Y, double Angle, double Width) run)
    {
        var utf16 = PdfEngine.Utf16(run.Text);
        var obj = NewTextObject(run.Font, run.Size);
        if (obj is null) return;
        fpdf_edit.FPDFTextSetText(obj, ref utf16[0]);
        fpdf_edit.FPDFPageObjSetFillColor(obj, run.Color.R, run.Color.G, run.Color.B, run.Color.A);
        // keep the line's original length so the letters stay where they were
        float l = 0, b = 0, r = 0, t = 0;
        fpdf_edit.FPDFPageObjGetBounds(obj, ref l, ref b, ref r, ref t);
        var natural = r - l;
        var sx = natural > 0.5 && run.Width > 0.5 ? Math.Clamp(run.Width / natural, 0.5, 1.8) : 1.0;
        var rad = run.Angle * Math.PI / 180;
        fpdf_edit.FPDFPageObjTransform(obj, Math.Cos(rad) * sx, Math.Sin(rad) * sx, -Math.Sin(rad), Math.Cos(rad), run.X, run.Y);
        fpdf_edit.FPDFPageInsertObject(page, obj);
    }

    // ---- pictures -----------------------------------------------------------------------------------------------------

    /// <summary>Blanks the pixels of a picture that lie under the areas. Returns false if the picture cannot be read or rewritten.</summary>
    static bool MaskPicture(FpdfPageobjectT obj, List<PageRect> boxes)
    {
        var bitmap = fpdf_edit.FPDFImageObjGetBitmap(obj);
        if (bitmap is null) return false;
        try
        {
            var w = fpdfview.FPDFBitmapGetWidth(bitmap); var h = fpdfview.FPDFBitmapGetHeight(bitmap);
            var stride = fpdfview.FPDFBitmapGetStride(bitmap);
            var format = fpdfview.FPDFBitmapGetFormat(bitmap);   // 1 gray, 2 BGR, 3 BGRx, 4 BGRA
            var bpp = format switch { 1 => 1, 2 => 3, 3 => 4, 4 => 4, _ => 0 };
            var buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
            if (bpp == 0 || buffer == IntPtr.Zero || w <= 0 || h <= 0) return false;
            var m = new FS_MATRIX_();
            if (fpdf_edit.FPDFPageObjGetMatrix(obj, m) == 0) return false;
            double a = m.A, bb = m.B, c = m.C, d = m.D, e = m.E, f = m.F;
            var det = a * d - bb * c;
            if (Math.Abs(det) < 1e-9) return false;
            // only the part of the picture inside the areas needs visiting
            var row = new byte[stride];
            for (var py = 0; py < h; py++)
            {
                var v = 1.0 - (py + 0.5) / h;
                Marshal.Copy(buffer + py * stride, row, 0, stride);
                var changed = false;
                for (var px = 0; px < w; px++)
                {
                    var u = (px + 0.5) / w;
                    var x = a * u + c * v + e; var y = bb * u + d * v + f;
                    var inside = false;
                    foreach (var box in boxes) if (x >= box.Left && x <= box.Right && y >= box.Bottom && y <= box.Top) { inside = true; break; }
                    if (!inside) continue;
                    var o = px * bpp;
                    for (var k = 0; k < Math.Min(bpp, 3); k++) row[o + k] = 0;
                    if (bpp == 4) row[o + 3] = 255;
                    changed = true;
                }
                if (changed) Marshal.Copy(row, 0, buffer + py * stride, stride);
            }
            return fpdf_edit.FPDFImageObjSetBitmap(null, 0, obj, bitmap) != 0;
        }
        finally { fpdfview.FPDFBitmapDestroy(bitmap); }
    }

    // ---- pages that cannot be edited in place -------------------------------------------------------------------------

    /// <summary>Replaces the page's content with a picture of the page (areas blacked out), so nothing under them survives.</summary>
    void RasterizePage(FpdfPageT page, List<PageRect> boxes, PdfColor fill)
    {
        var rotation = fpdf_edit.FPDFPageGetRotation(page);
        fpdf_edit.FPDFPageSetRotation(page, 0);
        try
        {
            var pw = fpdfview.FPDF_GetPageWidthF(page); var ph = fpdfview.FPDF_GetPageHeightF(page);
            var scale = Math.Min(200.0 / 72.0, 8000.0 / Math.Max(pw, ph));
            var wpx = Math.Max(1, (int)Math.Ceiling(pw * scale)); var hpx = Math.Max(1, (int)Math.Ceiling(ph * scale));
            var bitmap = fpdfview.FPDFBitmapCreate(wpx, hpx, 0);
            try
            {
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, wpx, hpx, 0xFFFFFFFF);
                fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, wpx, hpx, 0, 0);
                var color = PdfEngine.Pack(fill.R, fill.G, fill.B);
                foreach (var box in boxes)
                {
                    int x1 = 0, y1 = 0, x2 = 0, y2 = 0;
                    fpdfview.FPDF_PageToDevice(page, 0, 0, wpx, hpx, 0, box.Left, box.Top, ref x1, ref y1);
                    fpdfview.FPDF_PageToDevice(page, 0, 0, wpx, hpx, 0, box.Right, box.Bottom, ref x2, ref y2);
                    var left = Math.Max(0, Math.Min(x1, x2)); var top = Math.Max(0, Math.Min(y1, y2));
                    var right = Math.Min(wpx, Math.Max(x1, x2)); var bottom = Math.Min(hpx, Math.Max(y1, y2));
                    if (right > left && bottom > top) fpdfview.FPDFBitmapFillRect(bitmap, left, top, right - left, bottom - top, color);
                }
                // the page's origin in page space (bottom-left corner of the visible area)
                double ox = 0, oy = 0;
                fpdfview.FPDF_DeviceToPage(page, 0, 0, wpx, hpx, 0, 0, hpx, ref ox, ref oy);

                for (var i = fpdf_edit.FPDFPageCountObjects(page) - 1; i >= 0; i--)
                {
                    var obj = fpdf_edit.FPDFPageGetObject(page, i);
                    if (obj is not null && fpdf_edit.FPDFPageRemoveObject(page, obj) != 0) fpdf_edit.FPDFPageObjDestroy(obj);
                }
                var image = fpdf_edit.FPDFPageObjNewImageObj(Doc) ?? throw new PdfException("Pagjoro could not rebuild the page.");
                if (fpdf_edit.FPDFImageObjSetBitmap(null, 0, image, bitmap) == 0) throw new PdfException("Pagjoro could not rebuild the page.");
                fpdf_edit.FPDFImageObjSetMatrix(image, pw, 0, 0, ph, ox, oy);
                fpdf_edit.FPDFPageInsertObject(page, image);
            }
            finally { fpdfview.FPDFBitmapDestroy(bitmap); }
        }
        finally { fpdf_edit.FPDFPageSetRotation(page, rotation); }
    }
}
