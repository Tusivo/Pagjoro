// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pagjoro.Core;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

/// <summary>Small conveniences: reopening where you left off, remembering the window, turning the view, line thickness and the marks list.</summary>
public partial class MainWindow
{
    readonly ViewMemory viewMemory = new();

    // ---- where you left off -----------------------------------------------------------------------------------------

    void RememberView()
    {
        if (doc?.Path is not { } path || View.PageCount == 0) return;
        try { viewMemory.Remember(path, new ViewState(Math.Max(0, View.CurrentPage), View.Zoom, doc.ViewRotation, View.Facing)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    /// <summary>After a file was opened: go back to the page, zoom and view it was left at.</summary>
    void RestoreView(string path)
    {
        ViewState? state;
        try { state = viewMemory.Get(path); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return; }
        if (state is null || doc is null) return;
        var file = doc;
        // the window has not measured the new document yet; do it once layout has settled
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(doc, file)) return;
            if (state.Rotation != 0) { file.ViewRotation = state.Rotation & 3; View.Refresh(); RebuildSidePanes(); }
            if (state.Facing) { FacingItem.IsChecked = true; View.SetFacing(true); }
            View.SetZoom(state.Zoom);
            View.ScrollToPage(Math.Min(state.Page, Math.Max(0, file.PageCount - 1)));
            MessageStatus.Text = $"Opened {Path.GetFileName(path)} where you left off (page {Math.Min(state.Page, file.PageCount - 1) + 1}).";
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    // ---- where the window was ---------------------------------------------------------------------------------------

    /// <summary>A saved window position, if it still lands on a screen that is connected.</summary>
    static WindowPlacement? UsableWindow(WindowPlacement? saved)
    {
        if (saved is null) return null;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var window = new Rect(saved.Left, saved.Top, saved.Width, saved.Height);
        var overlap = Rect.Intersect(screen, window);
        // enough of the title bar must be reachable to drag the window back
        return !overlap.IsEmpty && overlap.Width >= 200 && overlap.Height >= 100 ? saved : null;
    }

    void PlaceWindow(WindowPlacement p)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left; Top = p.Top; Width = Math.Max(MinWidth, p.Width); Height = Math.Max(MinHeight, p.Height);
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    void RememberWindow()
    {
        try
        {
            var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (b.Width < 200 || b.Height < 150 || double.IsNaN(b.Left) || double.IsNaN(b.Top)) return;
            settings.SetWindow(new WindowPlacement(b.Left, b.Top, b.Width, b.Height, WindowState == WindowState.Maximized));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { }
    }

    // ---- turning the view -------------------------------------------------------------------------------------------

    void TurnView(int quarterTurns)
    {
        if (doc is null) return;
        var page = Math.Max(0, View.CurrentPage);
        doc.ViewRotation = (doc.ViewRotation + quarterTurns) & 3;
        View.Refresh();
        RebuildSidePanes();
        View.FitWidth();   // a turned page has a different width, so fit it again
        View.ScrollToPage(page);
        MessageStatus.Text = doc.ViewRotation == 0 ? "The view is back to normal." : $"View turned {doc.ViewRotation * 90} degrees. The file itself is not changed; use Pages, Rotate to turn the pages.";
    }

    void OnRotateViewRight(object sender, RoutedEventArgs e) => TurnView(1);
    void OnRotateViewLeft(object sender, RoutedEventArgs e) => TurnView(3);

    // ---- line thickness ---------------------------------------------------------------------------------------------

    void OnLineWidth(object sender, SelectionChangedEventArgs e)
    {
        if (View is null || LineBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)) View.LineWidth = w;
    }

    // ---- the marks list for reviewers ---------------------------------------------------------------------------------

    void OnExportMarks(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        IReadOnlyList<MarkSummaryItem> items;
        try { items = MarksSummary.Build(doc); }
        catch (PdfException ex) { Dialogs.Info(this, "Export Marks List", ex.Message, error: true); return; }
        if (items.Count == 0) { Dialogs.Info(this, "Export Marks List", "There are no marks in this document yet."); return; }
        var save = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt|Spreadsheet (*.csv)|*.csv", FileName = BaseName() + " - marks", Title = "Save the list of marks as",
        };
        if (save.ShowDialog(this) != true) return;
        try
        {
            var name = doc.Path is null ? "this document" : Path.GetFileName(doc.Path);
            var csv = save.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(save.FileName, csv ? MarksSummary.ToCsv(items) : MarksSummary.ToText(items, name), new UTF8Encoding(csv));
            MessageStatus.Text = $"Saved {items.Count} mark(s) to {Path.GetFileName(save.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Dialogs.Info(this, "Export Marks List", ex.Message, error: true); }
    }
}
