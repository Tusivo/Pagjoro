// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

/// <summary>An RGB color with an opacity from 0 (clear) to 255 (solid).</summary>
public readonly record struct PdfColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly PdfColor Black = new(0, 0, 0);
    public static readonly PdfColor White = new(255, 255, 255);
    public static readonly PdfColor Yellow = new(255, 235, 59);
    public static readonly PdfColor Red = new(220, 38, 38);
    public static readonly PdfColor Blue = new(37, 99, 235);
    public static readonly PdfColor Green = new(22, 163, 74);
    public static readonly PdfColor Orange = new(251, 140, 0);
    public static readonly PdfColor Pink = new(236, 64, 122);
    public static readonly PdfColor Purple = new(142, 36, 170);
}

public enum AnnotationKind
{
    Other = 0, Note = 1, Link = 2, FreeText = 3, Line = 4, Square = 5, Circle = 6, Polygon = 7, PolyLine = 8, Highlight = 9, Underline = 10,
    Squiggly = 11, Strikeout = 12, Stamp = 13, Caret = 14, Ink = 15, Popup = 16, FileAttachment = 17, Widget = 20,
}

public sealed record AnnotationInfo(int Index, AnnotationKind Kind, PageRect Rect, string Contents);

public sealed partial class PdfFile
{
    // ---- drawing on a page ------------------------------------------------------------------------------------------

    /// <summary>The standard fonts every PDF reader has built in.</summary>
    public static readonly IReadOnlyList<string> StandardFonts =
        ["Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique", "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
         "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique"];

    /// <summary>
    /// Writes a line of text onto a page. (<paramref name="x"/>, <paramref name="y"/>) is the baseline start in page points. The built-in fonts
    /// cover Western European letters; characters outside them are shown as "?".
    /// </summary>
    public PageRect AddText(int pageIndex, string text, double x, double y, double fontSize, PdfColor color, string font = "Helvetica", double rotationDegrees = 0)
    {
        Check(); ValidatePage(pageIndex);
        if (!InstalledFonts.IsSystem(font) && !StandardFonts.Contains(font)) font = "Helvetica";
        var utf16 = PdfEngine.Utf16(FitToFont(text, font));
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var obj = NewTextObject(font, fontSize) ?? throw new PdfException("Pagjoro could not add text.");
                fpdf_edit.FPDFTextSetText(obj, ref utf16[0]);
                fpdf_edit.FPDFPageObjSetFillColor(obj, color.R, color.G, color.B, color.A);
                var r = rotationDegrees * Math.PI / 180;
                fpdf_edit.FPDFPageObjTransform(obj, Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), x, y);
                fpdf_edit.FPDFPageInsertObject(page, obj);
                float l = 0, b = 0, rt = 0, t = 0;
                fpdf_edit.FPDFPageObjGetBounds(obj, ref l, ref b, ref rt, ref t);
                fpdf_edit.FPDFPageGenerateContent(page);
                IsModified = true;
                return new PageRect(l, b, rt, t);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>How big a line of text would be (width and height in points), without drawing it.</summary>
    public (double Width, double Height) MeasureText(string text, double fontSize, string font = "Helvetica")
    {
        Check();
        if (!InstalledFonts.IsSystem(font) && !StandardFonts.Contains(font)) font = "Helvetica";
        var utf16 = PdfEngine.Utf16(FitToFont(text, font));
        lock (PdfEngine.Gate)
        {
            var obj = NewTextObject(font, fontSize);
            if (obj is null) return (0, 0);
            try
            {
                fpdf_edit.FPDFTextSetText(obj, ref utf16[0]);
                float l = 0, b = 0, r = 0, t = 0;
                fpdf_edit.FPDFPageObjGetBounds(obj, ref l, ref b, ref r, ref t);
                return (r - l, t - b);
            }
            finally { fpdf_edit.FPDFPageObjDestroy(obj); }
        }
    }

    /// <summary>Draws a filled rectangle (for example a solid block over something to hide, or a background).</summary>
    public void AddFilledRect(int pageIndex, PageRect rect, PdfColor color)
    {
        Check(); ValidatePage(pageIndex);
        var r = rect.Normalized();
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var obj = fpdf_edit.FPDFPageObjCreateNewRect((float)r.Left, (float)r.Bottom, (float)r.Width, (float)r.Height) ?? throw new PdfException("Pagjoro could not add a shape.");
                fpdf_edit.FPDFPageObjSetFillColor(obj, color.R, color.G, color.B, color.A);
                fpdf_edit.FPDFPathSetDrawMode(obj, 1 /* fill */, 0);
                fpdf_edit.FPDFPageInsertObject(page, obj);
                fpdf_edit.FPDFPageGenerateContent(page);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Places a picture (blue, green, red, alpha pixels) into a rectangle of the page.</summary>
    public void AddImage(int pageIndex, byte[] bgra, int widthPx, int heightPx, PageRect rect)
    {
        Check(); ValidatePage(pageIndex);
        if (bgra.Length < widthPx * heightPx * 4) throw new ArgumentException("The picture data is too short.", nameof(bgra));
        var r = rect.Normalized();
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            lock (PdfEngine.Gate)
            {
                var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
                try
                {
                    var bitmap = fpdfview.FPDFBitmapCreateEx(widthPx, heightPx, 4 /* BGRA */, handle.AddrOfPinnedObject(), widthPx * 4);
                    var obj = fpdf_edit.FPDFPageObjNewImageObj(Doc) ?? throw new PdfException("Pagjoro could not add a picture.");
                    if (fpdf_edit.FPDFImageObjSetBitmap(null, 0, obj, bitmap) == 0) throw new PdfException("Pagjoro could not use that picture.");
                    fpdf_edit.FPDFImageObjSetMatrix(obj, r.Width, 0, 0, r.Height, r.Left, r.Bottom);
                    fpdf_edit.FPDFPageInsertObject(page, obj);
                    fpdf_edit.FPDFPageGenerateContent(page);
                    fpdfview.FPDFBitmapDestroy(bitmap);
                    IsModified = true;
                }
                finally { fpdfview.FPDF_ClosePage(page); }
            }
        }
        finally { handle.Free(); }
    }

    /// <summary>Sets the visible area of a page (the "crop box"), in page points.</summary>
    public void SetCropBox(int pageIndex, PageRect box)
    {
        Check(); ValidatePage(pageIndex);
        var r = box.Normalized();
        if (r.Width < 1 || r.Height < 1) throw new ArgumentException("The crop area is too small.", nameof(box));
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try { fpdf_transformpage.FPDFPageSetCropBox(page, (float)r.Left, (float)r.Bottom, (float)r.Right, (float)r.Top); }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        IsModified = true;
    }

    /// <summary>The page's full area (media box) in points, before any crop.</summary>
    public PageRect GetMediaBox(int pageIndex)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                float l = 0, b = 0, r = 0, t = 0;
                fpdf_transformpage.FPDFPageGetMediaBox(page, ref l, ref b, ref r, ref t);
                return new PageRect(l, b, r, t);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    // ---- annotations ------------------------------------------------------------------------------------------------

    static FS_RECTF_ ToRect(PageRect r) => new() { Left = (float)r.Left, Bottom = (float)r.Bottom, Right = (float)r.Right, Top = (float)r.Top };

    /// <summary>Adds a highlight, underline, squiggly underline or strike-through over the given rectangles (usually search or selection boxes).</summary>
    public void AddMarkup(int pageIndex, AnnotationKind kind, IReadOnlyList<PageRect> areas, PdfColor color, string contents = "")
    {
        Check(); ValidatePage(pageIndex);
        if (kind is not (AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.Squiggly or AnnotationKind.Strikeout))
            throw new ArgumentException("That is not a text mark.", nameof(kind));
        if (areas.Count == 0) return;
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var annot = fpdf_annot.FPDFPageCreateAnnot(page, (int)kind) ?? throw new PdfException("Pagjoro could not add the mark.");
                double l = double.MaxValue, b = double.MaxValue, r = double.MinValue, t = double.MinValue;
                foreach (var a in areas)
                {
                    var n = a.Normalized();
                    var q = new FS_QUADPOINTSF { X1 = (float)n.Left, Y1 = (float)n.Top, X2 = (float)n.Right, Y2 = (float)n.Top, X3 = (float)n.Left, Y3 = (float)n.Bottom, X4 = (float)n.Right, Y4 = (float)n.Bottom };
                    fpdf_annot.FPDFAnnotAppendAttachmentPoints(annot, q);
                    l = Math.Min(l, n.Left); b = Math.Min(b, n.Bottom); r = Math.Max(r, n.Right); t = Math.Max(t, n.Top);
                }
                fpdf_annot.FPDFAnnotSetRect(annot, ToRect(new PageRect(l, b, r, t)));
                fpdf_annot.FPDFAnnotSetColor(annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_Color, color.R, color.G, color.B, color.A == 255 ? (uint)255 : color.A);
                fpdf_annot.FPDFAnnotSetFlags(annot, 4 /* print */);
                if (contents.Length > 0) { var c = PdfEngine.Utf16(contents); fpdf_annot.FPDFAnnotSetStringValue(annot, "Contents", ref c[0]); }
                fpdf_annot.FPDFPageCloseAnnot(annot);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Adds a rectangle or ellipse outline, or a filled box, as an annotation.</summary>
    public void AddShape(int pageIndex, AnnotationKind kind, PageRect area, PdfColor stroke, PdfColor? fill = null, double lineWidth = 1.5, string contents = "")
    {
        Check(); ValidatePage(pageIndex);
        if (kind is not (AnnotationKind.Square or AnnotationKind.Circle)) throw new ArgumentException("That is not a shape.", nameof(kind));
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var annot = fpdf_annot.FPDFPageCreateAnnot(page, (int)kind) ?? throw new PdfException("Pagjoro could not add the shape.");
                fpdf_annot.FPDFAnnotSetRect(annot, ToRect(area.Normalized()));
                fpdf_annot.FPDFAnnotSetColor(annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_Color, stroke.R, stroke.G, stroke.B, 255);
                if (fill is { } f) fpdf_annot.FPDFAnnotSetColor(annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_InteriorColor, f.R, f.G, f.B, f.A);
                fpdf_annot.FPDFAnnotSetBorder(annot, 0, 0, (float)lineWidth);
                fpdf_annot.FPDFAnnotSetFlags(annot, 4);
                if (contents.Length > 0) { var c = PdfEngine.Utf16(contents); fpdf_annot.FPDFAnnotSetStringValue(annot, "Contents", ref c[0]); }
                fpdf_annot.FPDFPageCloseAnnot(annot);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Adds a freehand line made of the given points.</summary>
    public void AddInk(int pageIndex, IReadOnlyList<(double X, double Y)> points, PdfColor color, double lineWidth = 2)
    {
        Check(); ValidatePage(pageIndex);
        if (points.Count < 2) return;
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var annot = fpdf_annot.FPDFPageCreateAnnot(page, (int)AnnotationKind.Ink) ?? throw new PdfException("Pagjoro could not add the drawing.");
                var box = new PageRect(points.Min(p => p.X) - lineWidth, points.Min(p => p.Y) - lineWidth, points.Max(p => p.X) + lineWidth, points.Max(p => p.Y) + lineWidth);
                fpdf_annot.FPDFAnnotSetRect(annot, ToRect(box));
                fpdf_annot.FPDFAnnotSetColor(annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_Color, color.R, color.G, color.B, 255);
                fpdf_annot.FPDFAnnotSetBorder(annot, 0, 0, (float)lineWidth);
                var buffer = Marshal.AllocHGlobal(points.Count * 8);
                try
                {
                    var floats = new float[points.Count * 2];
                    for (var i = 0; i < points.Count; i++) { floats[i * 2] = (float)points[i].X; floats[i * 2 + 1] = (float)points[i].Y; }
                    Marshal.Copy(floats, 0, buffer, floats.Length);
                    var first = WrapPoints(buffer);
                    fpdf_annot.FPDFAnnotAddInkStroke(annot, first, (ulong)points.Count);
                }
                finally { Marshal.FreeHGlobal(buffer); }
                fpdf_annot.FPDFAnnotSetFlags(annot, 4);
                fpdf_annot.FPDFPageCloseAnnot(annot);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Adds a sticky note whose text shows when the icon is opened.</summary>
    public void AddNote(int pageIndex, double x, double y, string text, PdfColor color)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var annot = fpdf_annot.FPDFPageCreateAnnot(page, (int)AnnotationKind.Note) ?? throw new PdfException("Pagjoro could not add the note.");
                fpdf_annot.FPDFAnnotSetRect(annot, ToRect(new PageRect(x, y - 20, x + 20, y)));
                fpdf_annot.FPDFAnnotSetColor(annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_Color, color.R, color.G, color.B, 255);
                var c = PdfEngine.Utf16(text); fpdf_annot.FPDFAnnotSetStringValue(annot, "Contents", ref c[0]);
                fpdf_annot.FPDFAnnotSetFlags(annot, 4);
                fpdf_annot.FPDFPageCloseAnnot(annot);
                IsModified = true;
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    public IReadOnlyList<AnnotationInfo> GetAnnotations(int pageIndex)
    {
        Check(); ValidatePage(pageIndex);
        var list = new List<AnnotationInfo>();
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var count = fpdf_annot.FPDFPageGetAnnotCount(page);
                for (var i = 0; i < count; i++)
                {
                    var annot = fpdf_annot.FPDFPageGetAnnot(page, i);
                    if (annot is null) continue;
                    try
                    {
                        var subtype = fpdf_annot.FPDFAnnotGetSubtype(annot);
                        var rect = new FS_RECTF_();
                        fpdf_annot.FPDFAnnotGetRect(annot, rect);
                        var contents = ReadAnnotString(annot, "Contents");
                        list.Add(new AnnotationInfo(i, Enum.IsDefined(typeof(AnnotationKind), subtype) ? (AnnotationKind)subtype : AnnotationKind.Other,
                            new PageRect(rect.Left, rect.Bottom, rect.Right, rect.Top), contents));
                    }
                    finally { fpdf_annot.FPDFPageCloseAnnot(annot); }
                }
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        return list;
    }

    /// <summary>The engine\u2019s wrapper type has no public way to point at memory we filled ourselves, so the (non-public) constructor is used.</summary>
    static unsafe FS_POINTF_ WrapPoints(IntPtr memory)
    {
        var ctor = typeof(FS_POINTF_).GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [typeof(void*), typeof(bool)], null)
            ?? throw new PdfException("This version of the PDF engine cannot draw freehand lines.");
        return (FS_POINTF_)ctor.Invoke([System.Reflection.Pointer.Box((void*)memory, typeof(void*)), false]);
    }

    static string ReadAnnotString(FpdfAnnotationT annot, string key)
    {
        var bytes = fpdf_annot.FPDFAnnotGetStringValue(annot, key, ref MemoryMarshal.GetArrayDataReference(new ushort[1]), 0);
        if (bytes <= 2) return "";
        var buffer = new ushort[bytes / 2 + 1];
        fpdf_annot.FPDFAnnotGetStringValue(annot, key, ref buffer[0], bytes);
        return new string(buffer.TakeWhile(c => c != 0).Select(c => (char)c).ToArray());
    }

    public void RemoveAnnotation(int pageIndex, int annotationIndex)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                if (fpdf_annot.FPDFPageRemoveAnnot(page, annotationIndex) == 0) throw new PdfException("That mark could not be removed.");
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        IsModified = true;
    }

    /// <summary>Makes every mark and form field part of the page itself, so it can no longer be changed or removed.</summary>
    public void Flatten(int pageIndex)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try { fpdf_flatten.FPDFPageFlatten(page, 0); }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        IsModified = true;
    }

    // ---- redaction --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Permanently removes what lies under the given areas and draws a solid block there. See <see cref="RedactPage"/> for exactly what is
    /// removed. Returns how many marks, lines, pictures and objects were changed.
    /// </summary>
    public int Redact(int pageIndex, IReadOnlyList<PageRect> areas, PdfColor fill) => RedactPage(pageIndex, areas, fill).Total;

    static bool IsPageSized(FpdfPageT page, PageRect box)
    {
        var w = fpdfview.FPDF_GetPageWidthF(page); var h = fpdfview.FPDF_GetPageHeightF(page);
        return box.Width >= w * 0.9 && box.Height >= h * 0.9;
    }
}
