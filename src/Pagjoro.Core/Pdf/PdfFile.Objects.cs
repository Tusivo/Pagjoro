// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

public enum PageObjectKind { Other = 0, Text = 1, Path = 2, Image = 3, Shading = 4, Form = 5 }

/// <summary>One thing drawn on a page: a run of text, a picture, a shape.</summary>
/// <param name="Index">Its place in the page's content (changes when objects are added or removed).</param>
/// <param name="Text">The words, for text objects.</param>
/// <param name="Font">The nearest built-in font family and weight, for text objects.</param>
/// <param name="Size">Letter size in points, for text objects.</param>
/// <param name="X">Where a text object starts (baseline), in page points.</param>
/// <param name="Y">Where a text object starts (baseline), in page points.</param>
public sealed record PageObjectInfo(int Index, PageObjectKind Kind, PageRect Bounds, string Text, string Font, double Size, PdfColor Color, double X, double Y);

public sealed partial class PdfFile
{
    // ---- looking at what a page is made of ----------------------------------------------------------------------------

    /// <summary>Every text and picture object of a page (shapes and backgrounds are left out), in drawing order.</summary>
    public IReadOnlyList<PageObjectInfo> GetPageObjects(int pageIndex)
    {
        Check(); ValidatePage(pageIndex);
        var list = new List<PageObjectInfo>();
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            var textPage = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                var count = fpdf_edit.FPDFPageCountObjects(page);
                for (var i = 0; i < count; i++)
                {
                    var obj = fpdf_edit.FPDFPageGetObject(page, i);
                    if (obj is null) continue;
                    var info = Describe(obj, i, textPage);
                    if (info is not null && info.Kind is PageObjectKind.Text or PageObjectKind.Image) list.Add(info);
                }
            }
            finally
            {
                if (textPage is not null) fpdf_text.FPDFTextClosePage(textPage);
                fpdfview.FPDF_ClosePage(page);
            }
        }
        return list;
    }

    /// <summary>The topmost text or picture under a point of the page, or null.</summary>
    public PageObjectInfo? ObjectAt(int pageIndex, double x, double y, double tolerance = 2)
    {
        var all = GetPageObjects(pageIndex);
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var b = all[i].Bounds;
            if (x >= b.Left - tolerance && x <= b.Right + tolerance && y >= b.Bottom - tolerance && y <= b.Top + tolerance) return all[i];
        }
        return null;
    }

    PageObjectInfo? Describe(FpdfPageobjectT obj, int index, FpdfTextpageT? textPage)
    {
        float l = 0, b = 0, r = 0, t = 0;
        if (fpdf_edit.FPDFPageObjGetBounds(obj, ref l, ref b, ref r, ref t) == 0) return null;
        var kind = fpdf_edit.FPDFPageObjGetType(obj) switch { 1 => PageObjectKind.Text, 2 => PageObjectKind.Path, 3 => PageObjectKind.Image, 4 => PageObjectKind.Shading, 5 => PageObjectKind.Form, _ => PageObjectKind.Other };
        var bounds = new PageRect(l, b, r, t);
        if (kind != PageObjectKind.Text) return new PageObjectInfo(index, kind, bounds, "", "", 0, PdfColor.Black, l, b);
        var text = textPage is null ? "" : ReadObjectText(obj, textPage);
        var m = new FS_MATRIX_();
        fpdf_edit.FPDFPageObjGetMatrix(obj, m);
        float rawSize = 0;
        fpdf_edit.FPDFTextObjGetFontSize(obj, ref rawSize);
        var scale = Math.Sqrt((double)m.C * m.C + (double)m.D * m.D);
        var size = rawSize > 0 ? rawSize * (scale > 0.0001 ? scale : 1) : 11;
        uint cr = 0, cg = 0, cb = 0, ca = 255;
        fpdf_edit.FPDFPageObjGetFillColor(obj, ref cr, ref cg, ref cb, ref ca);
        return new PageObjectInfo(index, kind, bounds, text, StandardFontFor(fpdf_edit.FPDFTextObjGetFont(obj)), size,
            new PdfColor((byte)cr, (byte)cg, (byte)cb, 255), m.E, m.F);
    }

    // ---- changing it --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces the words of a text object. The object is re-set in a built-in font (the original font may not contain the new letters),
    /// at the same place and angle. Letters outside the built-in fonts are shown as "?". Returns the index of the new object.
    /// </summary>
    public int ReplaceText(int pageIndex, int objectIndex, string newText, string? font = null, double? size = null, PdfColor? color = null)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            var textPage = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                var obj = fpdf_edit.FPDFPageGetObject(page, objectIndex);
                if (obj is null || fpdf_edit.FPDFPageObjGetType(obj) != 1) throw new PdfException("That is no longer a piece of text on the page.");
                var info = Describe(obj, objectIndex, textPage)!;
                var m = new FS_MATRIX_();
                fpdf_edit.FPDFPageObjGetMatrix(obj, m);
                var angle = Math.Atan2(m.B, m.A);
                var useFont = font is not null && (StandardFonts.Contains(font) || InstalledFonts.IsSystem(font)) ? font : info.Font;
                var useSize = size ?? info.Size;
                var useColor = color ?? info.Color;
                if (fpdf_edit.FPDFPageRemoveObject(page, obj) == 0) throw new PdfException("That text could not be changed.");
                fpdf_edit.FPDFPageObjDestroy(obj);

                var safe = FitToFont(newText, useFont);
                if (safe.Trim().Length > 0)
                {
                    var utf16 = PdfEngine.Utf16(safe);
                    var neu = NewTextObject(useFont, useSize) ?? throw new PdfException("Pagjoro could not add the text.");
                    fpdf_edit.FPDFTextSetText(neu, ref utf16[0]);
                    fpdf_edit.FPDFPageObjSetFillColor(neu, useColor.R, useColor.G, useColor.B, 255);
                    fpdf_edit.FPDFPageObjTransform(neu, Math.Cos(angle), Math.Sin(angle), -Math.Sin(angle), Math.Cos(angle), m.E, m.F);
                    fpdf_edit.FPDFPageInsertObject(page, neu);
                }
                fpdf_edit.FPDFPageGenerateContent(page);
                IsModified = true;
                return fpdf_edit.FPDFPageCountObjects(page) - 1;
            }
            finally
            {
                if (textPage is not null) fpdf_text.FPDFTextClosePage(textPage);
                fpdfview.FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>Deletes one text or picture object from a page.</summary>
    public void DeleteObject(int pageIndex, int objectIndex)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            try
            {
                var obj = fpdf_edit.FPDFPageGetObject(page, objectIndex) ?? throw new PdfException("That is no longer on the page.");
                if (fpdf_edit.FPDFPageRemoveObject(page, obj) == 0) throw new PdfException("That could not be deleted.");
                fpdf_edit.FPDFPageObjDestroy(obj);
                fpdf_edit.FPDFPageGenerateContent(page);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Moves a text or picture object by the given distance, in page points (up and right are positive).</summary>
    public void MoveObject(int pageIndex, int objectIndex, double dx, double dy)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            try
            {
                var obj = fpdf_edit.FPDFPageGetObject(page, objectIndex) ?? throw new PdfException("That is no longer on the page.");
                fpdf_edit.FPDFPageObjTransform(obj, 1, 0, 0, 1, dx, dy);
                fpdf_edit.FPDFPageGenerateContent(page);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Swaps the pixels of a picture on the page for a new picture (blue, green, red, alpha), keeping its place and size.</summary>
    public void ReplaceImage(int pageIndex, int objectIndex, byte[] bgra, int widthPx, int heightPx)
    {
        Check(); ValidatePage(pageIndex);
        if (bgra.Length < widthPx * heightPx * 4) throw new ArgumentException("The picture data is too short.", nameof(bgra));
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            lock (PdfEngine.Gate)
            {
                var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
                try
                {
                    var obj = fpdf_edit.FPDFPageGetObject(page, objectIndex);
                    if (obj is null || fpdf_edit.FPDFPageObjGetType(obj) != 3) throw new PdfException("That is no longer a picture on the page.");
                    var bitmap = fpdfview.FPDFBitmapCreateEx(widthPx, heightPx, 4, handle.AddrOfPinnedObject(), widthPx * 4);
                    try
                    {
                        if (fpdf_edit.FPDFImageObjSetBitmap(null, 0, obj, bitmap) == 0) throw new PdfException("Pagjoro could not use that picture.");
                    }
                    finally { fpdfview.FPDFBitmapDestroy(bitmap); }
                    fpdf_edit.FPDFPageGenerateContent(page);
                    IsModified = true;
                }
                finally { fpdfview.FPDF_ClosePage(page); }
            }
        }
        finally { handle.Free(); }
    }

    // ---- hidden text (for scanned pages) -------------------------------------------------------------------------------

    /// <summary>
    /// Adds words that are not drawn but can be searched, selected and copied, laid over the place where the same words appear in a
    /// picture of a scanned page. Used by text recognition (OCR).
    /// </summary>
    public void AddHiddenText(int pageIndex, IReadOnlyList<(string Text, PageRect Box)> words)
    {
        Check(); ValidatePage(pageIndex);
        if (words.Count == 0) return;
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            try
            {
                foreach (var (text, box) in words)
                {
                    var b = box.Normalized();
                    var safe = new string(text.Select(c => c < 0x20 ? ' ' : c > 0xFF ? '?' : c).ToArray()).Trim();
                    if (safe.Length == 0 || b.Width < 0.5 || b.Height < 0.5) continue;
                    var size = Math.Clamp(b.Height * 0.85, 3, 200);
                    var utf16 = PdfEngine.Utf16(safe);
                    var obj = fpdf_edit.FPDFPageObjNewTextObj(Doc, "Helvetica", (float)size);
                    if (obj is null) continue;
                    fpdf_edit.FPDFTextSetText(obj, ref utf16[0]);
                    fpdf_edit.FPDFTextObjSetTextRenderMode(obj, (FPDF_TEXT_RENDERMODE)3);   // 3: invisible
                    float l = 0, bt = 0, r = 0, t = 0;
                    fpdf_edit.FPDFPageObjGetBounds(obj, ref l, ref bt, ref r, ref t);
                    var natural = r - l;
                    var sx = natural > 0.1 ? Math.Clamp(b.Width / natural, 0.2, 5.0) : 1.0;
                    fpdf_edit.FPDFPageObjTransform(obj, sx, 0, 0, 1, b.Left, b.Bottom + b.Height * 0.2);
                    fpdf_edit.FPDFPageInsertObject(page, obj);
                }
                fpdf_edit.FPDFPageGenerateContent(page);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }
    // ---- marks (annotations) ------------------------------------------------------------------------------------------

    /// <summary>Changes the text of a mark (the words of a note, or the comment of a highlight).</summary>
    public void SetAnnotationContents(int pageIndex, int annotationIndex, string text)
    {
        WithAnnotation(pageIndex, annotationIndex, annot =>
        {
            var c = PdfEngine.Utf16(text);
            if (fpdf_annot.FPDFAnnotSetStringValue(annot, "Contents", ref c[0]) == 0) throw new PdfException("That mark could not be changed.");
        });
    }

    /// <summary>Changes the colour of a mark.</summary>
    public void SetAnnotationColor(int pageIndex, int annotationIndex, PdfColor color)
    {
        WithAnnotation(pageIndex, annotationIndex, annot =>
        {
            if (fpdf_annot.FPDFAnnotSetColor(annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_Color, color.R, color.G, color.B, 255) == 0)
                throw new PdfException("That mark could not be recoloured.");
        });
    }

    /// <summary>Moves a note, box, oval or stamp. Text marks (highlights and the like) belong to the words they cover and cannot be moved.</summary>
    public bool MoveAnnotation(int pageIndex, int annotationIndex, double dx, double dy)
    {
        var moved = false;
        WithAnnotation(pageIndex, annotationIndex, annot =>
        {
            var subtype = (AnnotationKind)fpdf_annot.FPDFAnnotGetSubtype(annot);
            if (subtype is not (AnnotationKind.Note or AnnotationKind.Square or AnnotationKind.Circle or AnnotationKind.FreeText or AnnotationKind.Stamp)) return;
            var r = new FS_RECTF_();
            if (fpdf_annot.FPDFAnnotGetRect(annot, r) == 0) return;
            var moved2 = new PageRect(r.Left + dx, r.Bottom + dy, r.Right + dx, r.Top + dy);
            moved = fpdf_annot.FPDFAnnotSetRect(annot, ToRect(moved2)) != 0;
        });
        return moved;
    }

    void WithAnnotation(int pageIndex, int annotationIndex, Action<FpdfAnnotationT> action)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex) ?? throw new PdfException("That page could not be loaded.");
            try
            {
                var annot = fpdf_annot.FPDFPageGetAnnot(page, annotationIndex) ?? throw new PdfException("That mark is no longer on the page.");
                try { action(annot); }
                finally { fpdf_annot.FPDFPageCloseAnnot(annot); }
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        IsModified = true;
    }
}
