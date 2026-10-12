// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;

namespace Pagjoro.App;

/// <summary>
/// Where and how big the window opens, the same way Ledjoro does it: a window rather than full screen, but roomy: 1920 by 1080 pixels
/// on the screen the mouse is on, centred, and never larger than that screen's usable area.
/// </summary>
static class StartupBounds
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int Size; public RECT Monitor, Work; public int Flags; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, int flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    /// <summary>Sizes and places <paramref name="window"/> before it is shown. Falls back to the primary screen's work area.</summary>
    public static void Apply(Window window, int widthPx = 1920, int heightPx = 1080, int minWidth = 1040, int minHeight = 660, bool adjustFrame = true)
    {
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        double sx = scale.DpiScaleX, sy = scale.DpiScaleY;

        // the usable area (without the taskbar) of the screen the mouse is on, in pixels
        var work = new Rect(0, 0, SystemParameters.WorkArea.Width * sx, SystemParameters.WorkArea.Height * sy);
        work.X = SystemParameters.WorkArea.Left * sx; work.Y = SystemParameters.WorkArea.Top * sy;
        try
        {
            if (GetCursorPos(out var p))
            {
                var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(MonitorFromPoint(p, 2 /* nearest */), ref info))
                    work = new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { /* keep the primary screen */ }

        var minW = Math.Min(minWidth * sx, work.Width);
        var minH = Math.Min(minHeight * sy, work.Height);
        var w = Math.Min(work.Width, Math.Max(minW, widthPx));
        var h = Math.Min(work.Height, Math.Max(minH, heightPx));

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.MinWidth = minW / sx; window.MinHeight = minH / sy;
        window.Width = w / sx; window.Height = h / sy;
        window.Left = (work.X + (work.Width - w) / 2) / sx;
        window.Top = (work.Y + (work.Height - h) / 2) / sy;

        // Windows adds invisible resize borders around a window, so the visible frame comes out a little smaller than asked for.
        // Once the window exists, grow it by exactly that much so the visible window is the size that was meant (and recentre it).
        if (adjustFrame) window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (!GetWindowRect(hwnd, out var outer) || DwmGetWindowAttribute(hwnd, 9 /* extended frame bounds */, out var frame, Marshal.SizeOf<RECT>()) != 0) return;
                double extraW = (outer.Right - outer.Left) - (frame.Right - frame.Left), extraH = (outer.Bottom - outer.Top) - (frame.Bottom - frame.Top);
                if (extraW <= 0 && extraH <= 0) return;
                var nw = Math.Min(work.Width + extraW, w + extraW); var nh = Math.Min(work.Height + extraH, h + extraH);
                window.Width = nw / sx; window.Height = nh / sy;
                window.Left = (work.X + (work.Width - nw) / 2) / sx;
                window.Top = (work.Y + (work.Height - nh) / 2) / sy;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { /* keep the size as set */ }
        };
    }
}
