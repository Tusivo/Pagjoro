// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

public enum PageCorner { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

public sealed record WatermarkOptions(string Text, double FontSize = 64, PdfColor? Color = null, double AngleDegrees = 45, string Font = "Helvetica-Bold", double Opacity = 0.25);

public sealed record HeaderFooterOptions(string TopLeft = "", string TopCenter = "", string TopRight = "", string BottomLeft = "", string BottomCenter = "", string BottomRight = "",
    double FontSize = 10, string Font = "Helvetica", PdfColor? Color = null, double Margin = 30);

public sealed record BatesOptions(string Prefix = "", int Start = 1, int Digits = 6, string Suffix = "", PageCorner Corner = PageCorner.BottomRight, double FontSize = 10);

public sealed partial class PdfFile
{
    /// <summary>The area of a page that is shown (its crop box, or the whole page when none is set), in unrotated page space.</summary>
    public PageRect GetVisibleBox(int pageIndex)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                float l = 0, b = 0, r = 0, t = 0;
                if (fpdf_transformpage.FPDFPageGetCropBox(page, ref l, ref b, ref r, ref t) != 0 && r > l && t > b) return new PageRect(l, b, r, t);
                fpdf_transformpage.FPDFPageGetMediaBox(page, ref l, ref b, ref r, ref t);
                return r > l && t > b ? new PageRect(l, b, r, t) : new PageRect(0, 0, 612, 792);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>
    /// Maps a point on the page <i>as it is seen</i> (origin at the bottom-left of the shown page, y up) to the page's own coordinate space,
    /// so text lands in the same place on turned pages too.
    /// </summary>
    internal (double X, double Y, double AngleOffset) SeenToPage(int pageIndex, double seenX, double seenY)
    {
        var box = GetVisibleBox(pageIndex);
        var rotation = GetPageRotation(pageIndex);
        double w = box.Width, h = box.Height;
        var (px, py) = rotation switch
        {
            90 => (w - seenY, seenX),
            180 => (w - seenX, h - seenY),
            270 => (seenY, h - seenX),
            _ => (seenX, seenY),
        };
        return (box.Left + px, box.Bottom + py, rotation);
    }

    /// <summary>The page's size as it is seen (width, height in points).</summary>
    internal (double W, double H) SeenSize(int pageIndex)
    {
        var box = GetVisibleBox(pageIndex);
        return GetPageRotation(pageIndex) is 90 or 270 ? (box.Height, box.Width) : (box.Width, box.Height);
    }

    /// <summary>Draws text on a page at a place given as seen by the reader (so it is upright whatever the page rotation).</summary>
    public PageRect AddTextSeen(int pageIndex, string text, double seenX, double seenY, double fontSize, PdfColor color, string font = "Helvetica", double angleDegrees = 0)
    {
        var (x, y, rot) = SeenToPage(pageIndex, seenX, seenY);
        return AddText(pageIndex, text, x, y, fontSize, color, font, angleDegrees + rot);
    }
}

/// <summary>Watermarks, headers and footers, page numbers and Bates numbers.</summary>
public static class PageDecorations
{
    static string Fill(string template, int page, int pages, string fileName) =>
        Regex.Replace(template, @"\{(page|pages|date|time|file)\}", m => m.Groups[1].Value switch
        {
            "page" => (page + 1).ToString(),
            "pages" => pages.ToString(),
            "date" => DateTime.Now.ToString("yyyy-MM-dd"),
            "time" => DateTime.Now.ToString("HH:mm"),
            _ => fileName,
        }, RegexOptions.IgnoreCase);

    /// <summary>Puts large, light text across the middle of each page.</summary>
    public static void Watermark(PdfFile file, IEnumerable<int> pages, WatermarkOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Text)) throw new PdfException("Type the words for the watermark.");
        var color = o.Color ?? new PdfColor(120, 120, 120);
        color = color with { A = (byte)Math.Clamp((int)Math.Round(o.Opacity * 255), 5, 255) };
        var (tw, th) = file.MeasureText(o.Text, o.FontSize, o.Font);
        var angle = o.AngleDegrees * Math.PI / 180;
        foreach (var p in pages)
        {
            var (w, h) = file.SeenSize(p);
            // the text is centered on the page: move back by half its length along its own direction
            var cx = w / 2 - Math.Cos(angle) * tw / 2 + Math.Sin(angle) * th / 4;
            var cy = h / 2 - Math.Sin(angle) * tw / 2 - Math.Cos(angle) * th / 4;
            file.AddTextSeen(p, o.Text, cx, cy, o.FontSize, color, o.Font, o.AngleDegrees);
        }
    }

    static (double X, double Y) Spot(PageCorner corner, double pageW, double pageH, double textW, double fontSize, double margin) => corner switch
    {
        PageCorner.TopLeft => (margin, pageH - margin - fontSize * 0.8),
        PageCorner.TopCenter => ((pageW - textW) / 2, pageH - margin - fontSize * 0.8),
        PageCorner.TopRight => (pageW - margin - textW, pageH - margin - fontSize * 0.8),
        PageCorner.BottomLeft => (margin, margin),
        PageCorner.BottomCenter => ((pageW - textW) / 2, margin),
        _ => (pageW - margin - textW, margin),
    };

    /// <summary>Adds the header and footer texts to each page. {page}, {pages}, {date}, {time} and {file} are filled in.</summary>
    public static void HeaderFooter(PdfFile file, IEnumerable<int> pages, HeaderFooterOptions o)
    {
        var color = o.Color ?? PdfColor.Black;
        var list = pages.ToList();
        var total = file.PageCount;
        var name = file.Path is null ? "" : System.IO.Path.GetFileName(file.Path);
        var slots = new (PageCorner Corner, string Text)[]
        {
            (PageCorner.TopLeft, o.TopLeft), (PageCorner.TopCenter, o.TopCenter), (PageCorner.TopRight, o.TopRight),
            (PageCorner.BottomLeft, o.BottomLeft), (PageCorner.BottomCenter, o.BottomCenter), (PageCorner.BottomRight, o.BottomRight),
        };
        foreach (var p in list)
        {
            var (w, h) = file.SeenSize(p);
            foreach (var (corner, template) in slots)
            {
                if (string.IsNullOrEmpty(template)) continue;
                var text = Fill(template, p, total, name);
                var (tw, _) = file.MeasureText(text, o.FontSize, o.Font);
                var (x, y) = Spot(corner, w, h, tw, o.FontSize, o.Margin);
                file.AddTextSeen(p, text, x, y, o.FontSize, color, o.Font);
            }
        }
    }

    /// <summary>Stamps a running number on every page (used to identify pages of legal documents). Returns the first and last numbers used.</summary>
    public static (string First, string Last) Bates(PdfFile file, IEnumerable<int> pages, BatesOptions o)
    {
        var list = pages.ToList();
        var number = o.Start; string first = "", last = "";
        foreach (var p in list)
        {
            var label = o.Prefix + number.ToString().PadLeft(Math.Max(1, o.Digits), '0') + o.Suffix;
            if (first.Length == 0) first = label;
            last = label;
            var (w, h) = file.SeenSize(p);
            var (tw, _) = file.MeasureText(label, o.FontSize, "Courier");
            var (x, y) = Spot(o.Corner, w, h, tw, o.FontSize, 30);
            file.AddTextSeen(p, label, x, y, o.FontSize, PdfColor.Black, "Courier");
            number++;
        }
        return (first, last);
    }
}
