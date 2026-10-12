// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public enum ViewTool { Select, Highlight, Underline, Strikeout, Note, Draw, Rectangle, Ellipse, Redact, Text, Image, SignArea, TextField, CheckField, EditContent, Stamp, LinkArea }

/// <summary>
/// Shows a document as a vertical strip of pages and handles the mouse for selecting text, marking it and drawing. It lives inside a
/// ScrollViewer and paints only the pages that are in view, asking the engine for each one on a background thread.
/// </summary>
public sealed class DocumentView : FrameworkElement
{
    const double PageGap = 14, PageMargin = 18, Points = 96.0 / 72.0, MapScale = 4;

    sealed record Slot(double Top, double WidthPts, double HeightPts, double PairWidthPts = 0, double OffsetPts = 0);
    sealed record Cached(BitmapSource Image, int PixelWidth);

    PdfFile? doc;
    readonly List<Slot> slots = [];
    readonly ConcurrentDictionary<int, Cached> cache = new();
    readonly HashSet<int> pending = [];
    int version;
    double zoom = 1.0;
    double dpiScale = 1.0;
    ScrollViewer? scroller;

    // search
    IReadOnlyList<TextMatch> matches = [];
    int currentMatch = -1;

    // selection and drawing
    int selPage = -1, selStart, selCount;
    IReadOnlyList<PageRect> selRects = [];
    string selText = "";
    int dragPage = -1; bool dragging; Point dragFrom, dragTo;
    int dragAnchorChar = -1;
    List<Point>? inkPoints;
    readonly Dictionary<int, List<PageRect>> redactions = [];

    public ViewTool Tool { get; set; } = ViewTool.Select;

    /// <summary>Show pages two at a time, side by side, like an open book.</summary>
    public bool Facing { get; private set; }

    public void SetFacing(bool on)
    {
        if (Facing == on) return;
        Facing = on;
        var anchor = Math.Max(0, CurrentPage);
        version++; cache.Clear(); lock (pending) pending.Clear();
        BuildLayout();
        ScrollToPage(anchor);
        InvalidateVisual();
        ViewChanged?.Invoke();
    }
    public PdfColor MarkColor { get; set; } = PdfColor.Yellow;

    /// <summary>Thickness, in points, of freehand lines and of box and oval outlines.</summary>
    public double LineWidth { get; set; } = 2;
    public double Zoom => zoom;
    public int PageCount => doc?.PageCount ?? 0;
    public string SelectedText => selText;
    public bool HasSelection => selCount > 0;
    public int PendingRedactionCount => redactions.Values.Sum(l => l.Count);

    public event Action? ViewChanged;                 // zoom, page or layout changed
    public event Action? BeforeEdit;                  // a mark is about to be added (so the window can remember the state for Undo)
    public event Action? DocumentEdited;              // a mark was added by drawing
    public event Action<int, double, double>? NoteRequested;
    public event Action<int, double, double>? TextRequested;
    public event Action<ViewTool, int, PageRect>? AreaChosen;   // a box was dragged for adding a picture or placing a signature
    public event Action<string>? StatusMessage;
    public event Action<int, double, double>? StampRequested;
    public event Action<int, int>? MarkClicked;                         // page, index of a mark that was clicked
    public event Action<int, int, double, double>? MarkMoved;           // page, index, dx, dy (page points)
    public event Action<int, double, double>? ContentClicked;           // page, x, y: edit what is under this point
    public event Action<int, double, double, double, double>? ContentDragged;   // page, x, y, dx, dy

    (int Page, int Index, PageRect Rect, AnnotationKind Kind)? dragMark;
    (int Page, PageRect Rect)? shownMark;

    /// <summary>Outlines a mark so it can be seen which one is chosen (null clears it).</summary>
    public void ShowMark(int page, PageRect? rect)
    {
        shownMark = rect is { } r ? (page, r) : null;
        InvalidateVisual();
    }

    static bool IsMovableMark(AnnotationKind k) => k is AnnotationKind.Note or AnnotationKind.Square or AnnotationKind.Circle or AnnotationKind.FreeText or AnnotationKind.Stamp;

    AnnotationInfo? MarkAt(int page, double x, double y)
    {
        AnnotationInfo? best = null;
        foreach (var a in doc!.GetAnnotations(page))
        {
            if (a.Kind is AnnotationKind.Link or AnnotationKind.Widget or AnnotationKind.Popup) continue;
            var r = a.Rect;
            if (x >= r.Left - 2 && x <= r.Right + 2 && y >= r.Bottom - 2 && y <= r.Top + 2) best = a;   // the one drawn last is on top
        }
        return best;
    }

    public DocumentView()
    {
        ClipToBounds = false;
        SnapsToDevicePixels = true;
        Focusable = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    // ---- document and layout ----------------------------------------------------------------------------------------

    public void Attach(ScrollViewer sv)
    {
        scroller = sv;
        sv.ScrollChanged += (_, _) => { InvalidateVisual(); ViewChanged?.Invoke(); };
        sv.SizeChanged += (_, _) => { UpdateSize(); };
    }

    public void SetDocument(PdfFile? file)
    {
        doc = file;
        version++;
        cache.Clear(); lock (pending) pending.Clear();
        matches = []; currentMatch = -1; ClearSelection(); redactions.Clear();
        BuildLayout();
        scroller?.ScrollToVerticalOffset(0);
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>The document changed (pages added, removed, turned): layout and pictures are rebuilt.</summary>
    public void Refresh(bool keepPosition = true)
    {
        var offset = scroller?.VerticalOffset ?? 0;
        version++;
        cache.Clear(); lock (pending) pending.Clear();
        matches = []; currentMatch = -1; ClearSelection(); redactions.Clear();
        BuildLayout();
        if (keepPosition) scroller?.ScrollToVerticalOffset(Math.Min(offset, Math.Max(0, Height - (scroller?.ViewportHeight ?? 0))));
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>One page needs a fresh picture (a mark was added to it, for example).</summary>
    public void InvalidatePage(int page)
    {
        cache.TryRemove(page, out _);
        InvalidateVisual();
    }

    void BuildLayout()
    {
        slots.Clear();
        if (doc is null) { Width = 0; Height = 0; return; }
        double top = PageMargin;
        var count = doc.PageCount;
        const double pairGapPts = 10;
        for (var i = 0; i < count; i++)
        {
            var (w, h) = doc.GetViewSize(i);
            if (Facing && i + 1 < count)
            {
                // two pages side by side, tops aligned, the pair centered as one
                var (w2, h2) = doc.GetViewSize(i + 1);
                var pairWidth = w + pairGapPts + w2;
                slots.Add(new Slot(top, w, h, pairWidth, 0));
                slots.Add(new Slot(top, w2, h2, pairWidth, w + pairGapPts));
                top += Math.Max(h, h2) * Points * zoom + PageGap;
                i++;
                continue;
            }
            slots.Add(new Slot(top, w, h, w, 0));
            top += h * Points * zoom + PageGap;
        }
        Height = top - PageGap + PageMargin;
        UpdateSize();
    }

    void UpdateSize()
    {
        if (slots.Count == 0) { Width = Math.Max(0, scroller?.ViewportWidth ?? 0); return; }
        var widest = slots.Max(s => s.PairWidthPts) * Points * zoom + 2 * PageMargin;
        Width = Math.Max(widest, scroller?.ViewportWidth ?? 0);
        InvalidateVisual();
    }

    double PageLeft(Slot s) => (Width - s.PairWidthPts * Points * zoom) / 2 + s.OffsetPts * Points * zoom;
    double PageWidth(Slot s) => s.WidthPts * Points * zoom;
    double PageHeight(Slot s) => s.HeightPts * Points * zoom;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(Width) || double.IsNaN(Width) ? 0 : Width, double.IsNaN(Height) ? 0 : Height);

    // ---- zoom and navigation ----------------------------------------------------------------------------------------

    public void SetZoom(double value)
    {
        value = Math.Clamp(value, 0.1, 6.0);
        if (Math.Abs(value - zoom) < 0.0001 || scroller is null) return;
        var anchorPage = CurrentPage; var inside = 0.0;
        if (anchorPage >= 0 && anchorPage < slots.Count) inside = (scroller.VerticalOffset - slots[anchorPage].Top) / (slots[anchorPage].HeightPts * Points * zoom);
        zoom = value;
        cache.Clear(); lock (pending) pending.Clear(); version++;
        BuildLayout();
        if (anchorPage >= 0 && anchorPage < slots.Count) scroller.ScrollToVerticalOffset(slots[anchorPage].Top + inside * PageHeight(slots[anchorPage]));
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    public void FitWidth()
    {
        if (slots.Count == 0 || scroller is null) return;
        var widest = slots.Max(s => s.PairWidthPts) * Points;
        SetZoom((scroller.ViewportWidth - 2 * PageMargin - 4) / widest);
    }

    public void FitPage()
    {
        if (slots.Count == 0 || scroller is null) return;
        var page = Math.Max(0, CurrentPage); var s = slots[Math.Min(page, slots.Count - 1)];
        SetZoom(Math.Min((scroller.ViewportWidth - 2 * PageMargin - 4) / (s.PairWidthPts * Points), (scroller.ViewportHeight - 2 * PageMargin) / (s.HeightPts * Points)));
    }

    public int CurrentPage
    {
        get
        {
            if (scroller is null || slots.Count == 0) return -1;
            var probe = scroller.VerticalOffset + scroller.ViewportHeight / 3;
            for (var i = slots.Count - 1; i >= 0; i--) if (slots[i].Top <= probe + 1) return i;
            return 0;
        }
    }

    public void ScrollToPage(int page)
    {
        if (page < 0 || page >= slots.Count) return;
        scroller?.ScrollToVerticalOffset(Math.Max(0, slots[page].Top - PageMargin / 2));
    }

    // ---- search -----------------------------------------------------------------------------------------------------

    public void SetMatches(IReadOnlyList<TextMatch> found, int current = 0)
    {
        matches = found; currentMatch = found.Count == 0 ? -1 : Math.Clamp(current, 0, found.Count - 1);
        InvalidateVisual();
        if (currentMatch >= 0) RevealMatch(currentMatch);
    }

    public void ShowMatch(int index) { if (matches.Count == 0) return; currentMatch = ((index % matches.Count) + matches.Count) % matches.Count; InvalidateVisual(); RevealMatch(currentMatch); }
    public int CurrentMatchIndex => currentMatch;

    void RevealMatch(int index)
    {
        var m = matches[index];
        if (m.PageIndex >= slots.Count || scroller is null || doc is null) return;
        var s = slots[m.PageIndex];
        var rect = m.Rects.Count > 0 ? m.Rects[0] : default;
        double y = 0;
        if (m.Rects.Count > 0)
        {
            var dev = doc.RectsToDevice(m.PageIndex, (int)(PageWidth(s) * MapScale), (int)(PageHeight(s) * MapScale), [rect])[0];
            y = dev.Top / MapScale;
        }
        var target = s.Top + y;
        if (target < scroller.VerticalOffset + 20 || target > scroller.VerticalOffset + scroller.ViewportHeight - 60)
            scroller.ScrollToVerticalOffset(Math.Max(0, target - scroller.ViewportHeight / 3));
    }

    // ---- selection --------------------------------------------------------------------------------------------------

    public void ClearSelection()
    {
        selPage = -1; selCount = 0; selRects = []; selText = ""; dragAnchorChar = -1;
        InvalidateVisual();
    }

    public void SelectAllOnPage(int page)
    {
        if (doc is null || page < 0 || page >= slots.Count) return;
        var text = doc.GetPageText(page);
        if (text.Length == 0) return;
        SetSelection(page, 0, text.Length);
    }

    void SetSelection(int page, int start, int count)
    {
        if (doc is null) return;
        var (rects, text) = doc.GetTextRange(page, start, count);
        selPage = page; selStart = start; selCount = count; selRects = rects; selText = text;
        InvalidateVisual();
    }

    /// <summary>Applies the current text selection as a mark, then clears it.</summary>
    public bool MarkSelection(AnnotationKind kind, PdfColor color)
    {
        if (doc is null || selPage < 0 || selRects.Count == 0) return false;
        BeforeEdit?.Invoke();
        doc.AddMarkup(selPage, kind, selRects, color);
        var page = selPage;
        ClearSelection();
        InvalidatePage(page);
        DocumentEdited?.Invoke();
        return true;
    }

    // ---- redaction boxes --------------------------------------------------------------------------------------------

    public IReadOnlyDictionary<int, List<PageRect>> PendingRedactions => redactions;
    public void ClearPendingRedactions() { redactions.Clear(); InvalidateVisual(); }
    public void AddRedactionBoxes(int page, IEnumerable<PageRect> boxes)
    {
        if (!redactions.TryGetValue(page, out var list)) redactions[page] = list = [];
        list.AddRange(boxes);
        InvalidateVisual();
    }

    // ---- painting ---------------------------------------------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (doc is null || scroller is null) return;
        var top = scroller.VerticalOffset; var bottom = top + scroller.ViewportHeight;
        var paper = Brushes.White; var edge = new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1);
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var h = PageHeight(s);
            if (s.Top + h < top - 50) continue;
            if (s.Top > bottom + 50) break;
            var rect = new Rect(PageLeft(s), s.Top, PageWidth(s), h);
            dc.DrawRectangle(paper, edge, rect);
            var wantPx = Math.Max(1, (int)Math.Round(rect.Width * dpiScale));
            if (cache.TryGetValue(i, out var c))
            {
                dc.DrawImage(c.Image, rect);
                if (Math.Abs(c.PixelWidth - wantPx) > Math.Max(2, wantPx / 20)) RequestPage(i, wantPx, (int)Math.Round(rect.Height * dpiScale));
            }
            else RequestPage(i, wantPx, (int)Math.Round(rect.Height * dpiScale));

            DrawOverlays(dc, i, s, rect);
        }
        DrawAutoScrollMarker(dc);
    }

    void DrawOverlays(DrawingContext dc, int page, Slot s, Rect rect)
    {
        if (doc is null) return;
        var vw = (int)(rect.Width * MapScale); var vh = (int)(rect.Height * MapScale);
        if (vw < 1 || vh < 1) return;
        Rect Map((double Left, double Top, double Right, double Bottom) d) =>
            new(rect.Left + d.Left / MapScale, rect.Top + d.Top / MapScale, Math.Max(1, (d.Right - d.Left) / MapScale), Math.Max(1, (d.Bottom - d.Top) / MapScale));

        for (var m = 0; m < matches.Count; m++)
        {
            if (matches[m].PageIndex != page) continue;
            var brush = m == currentMatch ? new SolidColorBrush(Color.FromArgb(120, 255, 140, 0)) : new SolidColorBrush(Color.FromArgb(90, 255, 235, 59));
            foreach (var d in doc.RectsToDevice(page, vw, vh, matches[m].Rects)) dc.DrawRectangle(brush, null, Map(d));
        }
        if (selPage == page && selRects.Count > 0)
        {
            var brush = new SolidColorBrush(Color.FromArgb(90, 30, 120, 255));
            foreach (var d in doc.RectsToDevice(page, vw, vh, selRects)) dc.DrawRectangle(brush, null, Map(d));
        }
        if (shownMark is { } sm && sm.Page == page)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 215)), 2) { DashStyle = DashStyles.Dot };
            foreach (var d in doc.RectsToDevice(page, vw, vh, [new PageRect(sm.Rect.Left - 3, sm.Rect.Bottom - 3, sm.Rect.Right + 3, sm.Rect.Top + 3)])) dc.DrawRectangle(null, pen, Map(d));
        }
        if (redactions.TryGetValue(page, out var boxes))
        {
            var fill = new SolidColorBrush(Color.FromArgb(110, 220, 20, 20)); var pen = new Pen(Brushes.Red, 1.5);
            foreach (var d in doc.RectsToDevice(page, vw, vh, boxes)) dc.DrawRectangle(fill, pen, Map(d));
        }
        if (dragging && dragPage == page && Tool is ViewTool.Rectangle or ViewTool.Ellipse or ViewTool.Redact or ViewTool.Image or ViewTool.SignArea or ViewTool.TextField or ViewTool.CheckField or ViewTool.LinkArea)
        {
            var r = new Rect(dragFrom, dragTo);
            var pen = new Pen(Tool == ViewTool.Redact ? Brushes.Red : new SolidColorBrush(Color.FromRgb(220, 38, 38)), 1.5) { DashStyle = DashStyles.Dash };
            if (Tool == ViewTool.Ellipse) dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
            else dc.DrawRectangle(null, pen, r);
        }
        if (inkPoints is { Count: > 1 } && dragPage == page)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open()) { ctx.BeginFigure(inkPoints[0], false, false); ctx.PolyLineTo(inkPoints.Skip(1).ToList(), true, true); }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(MarkColor.R, MarkColor.G, MarkColor.B)), 2) { LineJoin = PenLineJoin.Round }, geo);
        }
    }

    void RequestPage(int index, int widthPx, int heightPx)
    {
        if (doc is null) return;
        lock (pending) { if (!pending.Add(index)) return; }
        var file = doc; var v = version;
        widthPx = Math.Clamp(widthPx, 8, 8000); heightPx = Math.Clamp(heightPx, 8, 8000);
        Task.Run(() =>
        {
            PageImage? img = null;
            try { img = file.Render(index, widthPx, heightPx); }
            catch (Exception) { /* the page stays blank; the status line is not the place for engine noise */ }
            Dispatcher.BeginInvoke(() =>
            {
                lock (pending) pending.Remove(index);
                if (v != version || img is null) return;
                var bmp = BitmapSource.Create(img.Width, img.Height, 96 * dpiScale, 96 * dpiScale, PixelFormats.Bgra32, null, img.Pixels, img.Stride);
                bmp.Freeze();
                cache[index] = new Cached(bmp, img.Width);
                TrimCache(index);
                InvalidateVisual();
            });
        });
    }

    void TrimCache(int keepNear)
    {
        if (cache.Count <= 24) return;
        foreach (var k in cache.Keys.OrderByDescending(k => Math.Abs(k - keepNear)).Take(cache.Count - 20)) cache.TryRemove(k, out _);
    }

    // ---- mouse ------------------------------------------------------------------------------------------------------

    (int Page, Rect PageRect)? PageAt(Point p)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var r = new Rect(PageLeft(s), s.Top, PageWidth(s), PageHeight(s));
            if (p.Y >= r.Top - PageGap / 2 && p.Y <= r.Bottom + PageGap / 2 && (!Facing || (p.X >= r.Left - PageGap / 2 && p.X <= r.Right + PageGap / 2))) return (i, r);
        }
        return null;
    }

    (double X, double Y) ToPagePoint(int page, Rect rect, Point p)
    {
        var vw = (int)(rect.Width * MapScale); var vh = (int)(rect.Height * MapScale);
        return doc!.DeviceToPage(page, vw, vh, (p.X - rect.Left) * MapScale, (p.Y - rect.Top) * MapScale);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (doc is null) return;
        var p = e.GetPosition(this);
        var hit = PageAt(p); if (hit is null) return;
        dragPage = hit.Value.Page; dragFrom = dragTo = p; dragging = true;
        CaptureMouse();
        switch (Tool)
        {
            case ViewTool.Select or ViewTool.Highlight or ViewTool.Underline or ViewTool.Strikeout:
                ClearSelection();
                var (x, y) = ToPagePoint(dragPage, hit.Value.PageRect, p);
                dragMark = null;
                if (Tool == ViewTool.Select && MarkAt(dragPage, x, y) is { } mk) { dragMark = (dragPage, mk.Index, mk.Rect, mk.Kind); dragAnchorChar = -1; }
                else dragAnchorChar = doc.CharIndexAt(dragPage, x, y, 6);
                break;
            case ViewTool.Draw: inkPoints = [p]; break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (doc is null) return;
        var p = e.GetPosition(this);
        if (!dragging || dragPage < 0)
        {
            UpdateCursor(p);
            return;
        }
        dragTo = p;
        var hit = PageAt(p);
        switch (Tool)
        {
            case ViewTool.Select or ViewTool.Highlight or ViewTool.Underline or ViewTool.Strikeout:
                if (dragAnchorChar >= 0 && hit is { } h && h.Page == dragPage)
                {
                    var (x, y) = ToPagePoint(dragPage, h.PageRect, p);
                    var end = doc.CharIndexAt(dragPage, x, y, 40);
                    if (end >= 0) SetSelection(dragPage, Math.Min(dragAnchorChar, end), Math.Abs(end - dragAnchorChar) + 1);
                }
                break;
            case ViewTool.Draw: inkPoints?.Add(p); InvalidateVisual(); break;
            default: InvalidateVisual(); break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!dragging) return;
        dragging = false; ReleaseMouseCapture();
        if (doc is null || dragPage < 0) return;
        var page = dragPage; var s = slots[page];
        var rect = new Rect(PageLeft(s), s.Top, PageWidth(s), PageHeight(s));
        var moved = (dragTo - dragFrom).Length > 4;
        var vw = (int)(rect.Width * MapScale); var vh = (int)(rect.Height * MapScale);
        try
        {
            switch (Tool)
            {
                case ViewTool.Select when moved && dragMark is { } dm && IsMovableMark(dm.Kind):
                    var from = ToPagePoint(page, rect, dragFrom); var to = ToPagePoint(page, rect, dragTo);
                    MarkMoved?.Invoke(dm.Page, dm.Index, to.X - from.X, to.Y - from.Y);
                    break;
                case ViewTool.Select when !moved && dragMark is { } clicked:
                    MarkClicked?.Invoke(clicked.Page, clicked.Index);
                    break;
                case ViewTool.EditContent when !moved:
                    var cp = ToPagePoint(page, rect, dragTo);
                    ContentClicked?.Invoke(page, cp.X, cp.Y);
                    break;
                case ViewTool.EditContent when moved:
                    var cf = ToPagePoint(page, rect, dragFrom); var ct = ToPagePoint(page, rect, dragTo);
                    ContentDragged?.Invoke(page, cf.X, cf.Y, ct.X - cf.X, ct.Y - cf.Y);
                    break;
                case ViewTool.Select when !moved:
                    var (x, y) = ToPagePoint(page, rect, dragTo);
                    var link = doc.GetLinkAt(page, x, y);
                    if (link.PageIndex >= 0) ScrollToPage(link.PageIndex);
                    else if (link.Uri is { Length: > 0 } uri && (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (MessageBox.Show($"Open this link in your web browser?\n\n{uri}", "Pagjoro", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    }
                    break;
                case ViewTool.Highlight or ViewTool.Underline or ViewTool.Strikeout when selCount > 0:
                    MarkSelection(Tool switch { ViewTool.Underline => AnnotationKind.Underline, ViewTool.Strikeout => AnnotationKind.Strikeout, _ => AnnotationKind.Highlight },
                        Tool == ViewTool.Highlight ? MarkColor : (MarkColor == PdfColor.Yellow ? PdfColor.Red : MarkColor));
                    break;
                case ViewTool.Note when !moved:
                    var np = ToPagePoint(page, rect, dragTo);
                    NoteRequested?.Invoke(page, np.X, np.Y);
                    break;
                case ViewTool.Stamp when !moved:
                    var sp = ToPagePoint(page, rect, dragTo);
                    StampRequested?.Invoke(page, sp.X, sp.Y);
                    break;
                case ViewTool.Text when !moved:
                    var tp = ToPagePoint(page, rect, dragTo);
                    TextRequested?.Invoke(page, tp.X, tp.Y);
                    break;
                case ViewTool.Image or ViewTool.SignArea or ViewTool.TextField or ViewTool.CheckField or ViewTool.LinkArea when moved:
                    var chosen = doc.DeviceRectToPage(page, vw, vh, (Math.Min(dragFrom.X, dragTo.X) - rect.Left) * MapScale, (Math.Min(dragFrom.Y, dragTo.Y) - rect.Top) * MapScale,
                        (Math.Max(dragFrom.X, dragTo.X) - rect.Left) * MapScale, (Math.Max(dragFrom.Y, dragTo.Y) - rect.Top) * MapScale);
                    AreaChosen?.Invoke(Tool, page, chosen);
                    break;
                case ViewTool.Draw when inkPoints is { Count: > 1 }:
                    var pts = inkPoints.Select(pt => ToPagePoint(page, rect, pt)).ToList();
                    BeforeEdit?.Invoke();
                    doc.AddInk(page, pts, MarkColor == PdfColor.Yellow ? PdfColor.Red : MarkColor, LineWidth);
                    InvalidatePage(page); DocumentEdited?.Invoke();
                    break;
                case ViewTool.Rectangle or ViewTool.Ellipse when moved:
                    var area = doc.DeviceRectToPage(page, vw, vh, (Math.Min(dragFrom.X, dragTo.X) - rect.Left) * MapScale, (Math.Min(dragFrom.Y, dragTo.Y) - rect.Top) * MapScale,
                        (Math.Max(dragFrom.X, dragTo.X) - rect.Left) * MapScale, (Math.Max(dragFrom.Y, dragTo.Y) - rect.Top) * MapScale);
                    BeforeEdit?.Invoke();
                    doc.AddShape(page, Tool == ViewTool.Rectangle ? AnnotationKind.Square : AnnotationKind.Circle, area, MarkColor == PdfColor.Yellow ? PdfColor.Red : MarkColor, null, LineWidth);
                    InvalidatePage(page); DocumentEdited?.Invoke();
                    break;
                case ViewTool.Redact when moved:
                    var box = doc.DeviceRectToPage(page, vw, vh, (Math.Min(dragFrom.X, dragTo.X) - rect.Left) * MapScale, (Math.Min(dragFrom.Y, dragTo.Y) - rect.Top) * MapScale,
                        (Math.Max(dragFrom.X, dragTo.X) - rect.Left) * MapScale, (Math.Max(dragFrom.Y, dragTo.Y) - rect.Top) * MapScale);
                    if (!redactions.TryGetValue(page, out var list)) redactions[page] = list = [];
                    list.Add(box);
                    StatusMessage?.Invoke($"{PendingRedactionCount} area(s) marked for redaction. Nothing is removed until you press Apply Redactions.");
                    break;
            }
        }
        catch (PdfException ex) { StatusMessage?.Invoke(ex.Message); }
        inkPoints = null; dragPage = -1;
        InvalidateVisual();
    }

    DateTime lastLinkProbe = DateTime.MinValue;

    void UpdateCursor(Point p)
    {
        Cursor = Tool switch
        {
            ViewTool.Select => Cursors.IBeam,
            ViewTool.Highlight or ViewTool.Underline or ViewTool.Strikeout => Cursors.IBeam,
            _ => Cursors.Cross,
        };
        if (Tool != ViewTool.Select || doc is null || (DateTime.UtcNow - lastLinkProbe).TotalMilliseconds < 90) return;
        lastLinkProbe = DateTime.UtcNow;
        var hit = PageAt(p); if (hit is null) return;
        try
        {
            var (x, y) = ToPagePoint(hit.Value.Page, hit.Value.PageRect, p);
            var link = doc.GetLinkAt(hit.Value.Page, x, y);
            if (link.Uri is not null || link.PageIndex >= 0) Cursor = Cursors.Hand;
        }
        catch (PdfException) { }
    }

    // ---- scrolling with the middle button ---------------------------------------------------------------------------
    //
    // Press the middle button (the wheel) and move the mouse away from where it was pressed: the page glides that way, faster the further
    // the mouse is from the starting point. Hold the button and let go to stop, or click it once to keep gliding and click again to stop.

    DispatcherTimer? glideTimer;
    bool gliding, glideHeld;
    Point glideStartScreen;      // where the button went down, in screen pixels
    Point glideMarkerContent;    // the same place, in the view's own coordinates (follows the scrolling)
    Point glideStartInScroller;
    DateTime glideStartedAt;
    bool glideMoved;

    public bool IsGliding => gliding;

    void StartGlide()
    {
        if (scroller is null || doc is null || gliding) return;
        gliding = true; glideHeld = true; glideMoved = false; glideStartedAt = DateTime.UtcNow;
        glideStartScreen = PointToScreen(Mouse.GetPosition(this));
        glideStartInScroller = scroller.PointFromScreen(glideStartScreen);
        Cursor = Cursors.ScrollAll;
        CaptureMouse();
        glideTimer ??= new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        glideTimer.Tick -= GlideTick; glideTimer.Tick += GlideTick;
        glideTimer.Start();
        InvalidateVisual();
    }

    /// <summary>Ends the gliding, if it is going.</summary>
    public void StopGlide()
    {
        if (!gliding) return;
        gliding = false; glideHeld = false;
        glideTimer?.Stop();
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = null;
        InvalidateVisual();
    }

    void GlideTick(object? sender, EventArgs e)
    {
        if (!gliding || scroller is null) return;
        var now = PointToScreen(Mouse.GetPosition(this));
        var scale = dpiScale <= 0 ? 1 : dpiScale;
        double dx = (now.X - glideStartScreen.X) / scale, dy = (now.Y - glideStartScreen.Y) / scale;
        // a small still zone around the starting point, then speed grows with distance
        const double dead = 8, gain = 0.13;
        double Speed(double d) => Math.Abs(d) <= dead ? 0 : Math.Sign(d) * (Math.Abs(d) - dead) * gain * (1 + Math.Abs(d) / 400);
        double vx = Speed(dx), vy = Speed(dy);
        if (Math.Abs(dx) > dead || Math.Abs(dy) > dead) glideMoved = true;
        if (vy != 0) scroller.ScrollToVerticalOffset(scroller.VerticalOffset + vy);
        if (vx != 0) scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + vx);
        glideMarkerContent = new Point(scroller.HorizontalOffset + glideStartInScroller.X, scroller.VerticalOffset + glideStartInScroller.Y);
        InvalidateVisual();
    }

    void DrawAutoScrollMarker(DrawingContext dc)
    {
        if (!gliding || scroller is null) return;
        var c = new Point(scroller.HorizontalOffset + glideStartInScroller.X, scroller.VerticalOffset + glideStartInScroller.Y);
        var fill = new SolidColorBrush(Color.FromArgb(215, 30, 30, 30));
        var ink = new Pen(Brushes.White, 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawEllipse(fill, new Pen(Brushes.White, 1.5), c, 15, 15);
        dc.DrawEllipse(Brushes.White, null, c, 2.2, 2.2);
        // four little arrows, one for each direction
        foreach (var (ux, uy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
        {
            var tip = new Point(c.X + ux * 10.5, c.Y + uy * 10.5);
            var back = new Point(c.X + ux * 6.5, c.Y + uy * 6.5);
            var side = new Vector(-uy, ux) * 2.8;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open()) { ctx.BeginFigure(back + side, false, false); ctx.LineTo(tip, true, true); ctx.LineTo(back - side, true, true); }
            geo.Freeze();
            dc.DrawGeometry(null, ink, geo);
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (gliding)
        {
            // any button stops the gliding; the click is not passed on to a tool
            StopGlide();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Middle && doc is not null)
        {
            StartGlide();
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (gliding && e.ChangedButton == MouseButton.Middle && glideHeld)
        {
            // held and moved: let go to stop. A quick click without moving keeps the gliding going until the next click.
            var quick = (DateTime.UtcNow - glideStartedAt).TotalMilliseconds < 350 && !glideMoved;
            if (quick) glideHeld = false; else StopGlide();
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseUp(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }
}
