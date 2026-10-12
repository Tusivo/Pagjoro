// SPDX-License-Identifier: AGPL-3.0-or-later
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

public sealed partial class PdfFile
{
    /// <summary>
    /// How the pages are turned on screen, in quarter turns clockwise (0 to 3). This is only a way of looking: it is not saved in the file and
    /// does not change the pages' own rotation. Rendering and the page/picture conversions below follow it.
    /// </summary>
    public int ViewRotation { get; set; }

    /// <summary>The page's size in points as it appears with the view turn applied.</summary>
    public (double Width, double Height) GetViewSize(int index)
    {
        var (w, h) = GetPageSize(index);
        return (ViewRotation & 1) == 1 ? (h, w) : (w, h);
    }

    /// <summary>
    /// Maps a point in page space (points, origin bottom-left) to a position in a picture of the page that is
    /// <paramref name="sizeX"/> by <paramref name="sizeY"/> pixels, taking the page's rotation and crop into account. The y axis points down.
    /// </summary>
    public (double X, double Y) PageToDevice(int pageIndex, int sizeX, int sizeY, double pageX, double pageY)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                int dx = 0, dy = 0;
                fpdfview.FPDF_PageToDevice(page, 0, 0, sizeX, sizeY, ViewRotation & 3, pageX, pageY, ref dx, ref dy);
                return (dx, dy);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>The reverse of <see cref="PageToDevice"/>.</summary>
    public (double X, double Y) DeviceToPage(int pageIndex, int sizeX, int sizeY, double deviceX, double deviceY)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                double px = 0, py = 0;
                fpdfview.FPDF_DeviceToPage(page, 0, 0, sizeX, sizeY, ViewRotation & 3, (int)Math.Round(deviceX), (int)Math.Round(deviceY), ref px, ref py);
                return (px, py);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Maps page rectangles into picture space: returns (left, top, right, bottom) in pixels for each, y pointing down.</summary>
    public IReadOnlyList<(double Left, double Top, double Right, double Bottom)> RectsToDevice(int pageIndex, int sizeX, int sizeY, IEnumerable<PageRect> rects)
    {
        Check(); ValidatePage(pageIndex);
        var result = new List<(double, double, double, double)>();
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                foreach (var r in rects)
                {
                    int x1 = 0, y1 = 0, x2 = 0, y2 = 0;
                    fpdfview.FPDF_PageToDevice(page, 0, 0, sizeX, sizeY, ViewRotation & 3, r.Left, r.Top, ref x1, ref y1);
                    fpdfview.FPDF_PageToDevice(page, 0, 0, sizeX, sizeY, ViewRotation & 3, r.Right, r.Bottom, ref x2, ref y2);
                    result.Add((Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)));
                }
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        return result;
    }

    /// <summary>Maps a rectangle drawn on a picture of the page back to page space.</summary>
    public PageRect DeviceRectToPage(int pageIndex, int sizeX, int sizeY, double left, double top, double right, double bottom)
    {
        var a = DeviceToPage(pageIndex, sizeX, sizeY, left, top);
        var b = DeviceToPage(pageIndex, sizeX, sizeY, right, bottom);
        return new PageRect(a.X, a.Y, b.X, b.Y).Normalized();
    }
}
