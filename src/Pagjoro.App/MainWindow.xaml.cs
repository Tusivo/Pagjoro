// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;
using Pagjoro.Core;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public sealed class ThumbItem(PdfFile? source, int index, double aspect) : INotifyPropertyChanged
{
    BitmapSource? image; bool requested;
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Index { get; set; } = index;
    public string Label => (Index + 1).ToString();
    public double ThumbHeight { get; } = Math.Clamp(150 * aspect, 60, 400);
    public BitmapSource? Image
    {
        get
        {
            if (!requested && source is not null)
            {
                requested = true;
                var file = source; var i = Index;
                Task.Run(() =>
                {
                    try
                    {
                        var img = file.RenderFit(i, 300);
                        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Pixels, img.Stride);
                        bmp.Freeze();
                        Application.Current?.Dispatcher.BeginInvoke(() => { image = bmp; PropertyChanged?.Invoke(this, new(nameof(Image))); });
                    }
                    catch (Exception) { /* leave the picture blank */ }
                });
            }
            return image;
        }
    }
}

public sealed record OutlineItem(string Title, int Page, Thickness Indent);
public sealed record MarkItem(int Page, int Index, string Title, string Detail);
public sealed record RecentItem(string Path, string Name, string Folder);

public partial class MainWindow : Window
{
    PdfFile? doc;
    readonly List<byte[]> undo = [];
    readonly List<byte[]> redo = [];
    readonly RecentFiles recent = new();
    readonly ObservableCollection<ThumbItem> thumbs = [];
    IReadOnlyList<TextMatch> found = [];
    Point dragStart;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Name;
        VersionStatus.Text = "Pagjoro " + AppInfo.Version;
        Thumbs.ItemsSource = thumbs;
        View.Attach(Scroller);
        View.ViewChanged += OnViewChanged;
        View.BeforeEdit += PushUndo;
        View.DocumentEdited += () => { AfterEdit(); RefreshMarks(); };
        View.NoteRequested += OnNoteRequested;
        View.StatusMessage += s => MessageStatus.Text = s;
        WireTools();
        InitMenusAndToolbar();
        InitTheme();
        ShowRecent();
        Loaded += (_, _) =>
        {
            var arg = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
            if (arg is not null) OpenPath(arg);
        };
        UpdateEnabled();
    }

    // ---- state ------------------------------------------------------------------------------------------------------

    void HasDocument(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = doc is not null;

    void UpdateEnabled()
    {
        var has = doc is not null;
        Welcome.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        View.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateTitle()
    {
        if (doc is null) { Title = AppInfo.Name; return; }
        var name = doc.Path is null ? "Untitled" : Path.GetFileName(doc.Path);
        Title = (doc.IsModified ? "● " : "") + name + " - " + AppInfo.Name;
    }

    void OnViewChanged()
    {
        if (doc is null) { PageStatus.Text = ""; ZoomStatus.Text = ""; SyncToolbar(); return; }
        PageStatus.Text = $"Page {Math.Max(0, View.CurrentPage) + 1} of {View.PageCount}";
        SyncToolbar();
        ZoomStatus.Text = $"Zoom {Math.Round(View.Zoom * 100)}%";
    }

    void SetDocument(PdfFile? file, bool keepPosition = false)
    {
        var offset = Scroller.VerticalOffset;
        var old = doc;
        doc = file;
        View.SetDocument(file);
        if (keepPosition) { Scroller.UpdateLayout(); Scroller.ScrollToVerticalOffset(offset); }
        old?.Dispose();
        RebuildSidePanes();
        UpdateEnabled(); UpdateTitle(); OnViewChanged();
        if (file is not null && !keepPosition) Dispatcher.BeginInvoke(View.FitWidth, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void RebuildSidePanes()
    {
        thumbs.Clear();
        if (doc is null) { Outline.ItemsSource = null; MarksList.ItemsSource = null; return; }
        for (var i = 0; i < doc.PageCount; i++)
        {
            var (w, h) = doc.GetPageSize(i);
            thumbs.Add(new ThumbItem(doc, i, h / w));
        }
        Outline.ItemsSource = doc.GetBookmarks().Select(b => new OutlineItem(b.Title.Length == 0 ? "(no title)" : b.Title, b.PageIndex, new Thickness(b.Level * 14, 3, 0, 3))).ToList();
        RefreshMarks();
    }

    void RefreshMarks()
    {
        if (doc is null) return;
        var list = new List<MarkItem>();
        var kinds = new HashSet<AnnotationKind> { AnnotationKind.Link, AnnotationKind.Popup, AnnotationKind.Widget };
        for (var p = 0; p < doc.PageCount; p++)
            foreach (var a in doc.GetAnnotations(p).Where(a => !kinds.Contains(a.Kind)))
                list.Add(new MarkItem(p, a.Index, $"{Describe(a.Kind)}, page {p + 1}", a.Contents));
        MarksList.ItemsSource = list;
    }

    static string Describe(AnnotationKind k) => k switch
    {
        AnnotationKind.Highlight => "Highlight", AnnotationKind.Underline => "Underline", AnnotationKind.Strikeout => "Strike through", AnnotationKind.Squiggly => "Squiggly underline",
        AnnotationKind.Note => "Note", AnnotationKind.Ink => "Drawing", AnnotationKind.Square => "Rectangle", AnnotationKind.Circle => "Oval", AnnotationKind.FreeText => "Text box",
        AnnotationKind.Stamp => "Stamp", AnnotationKind.Line => "Line", _ => "Mark",
    };

    // ---- undo -------------------------------------------------------------------------------------------------------

    void PushUndo()
    {
        if (doc is null) return;
        undo.Add(doc.ToBytes()); redo.Clear();
        while (undo.Count > 12 || undo.Sum(b => (long)b.Length) > 600_000_000) undo.RemoveAt(0);
    }

    void AfterEdit() { UpdateTitle(); OnViewChanged(); }

    void OnUndo(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        if (undo.Count == 0) { MessageStatus.Text = "There is nothing to undo."; return; }
        redo.Add(doc.ToBytes());
        var bytes = undo[^1]; undo.RemoveAt(undo.Count - 1);
        Restore(bytes);
    }

    void OnRedo(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        if (redo.Count == 0) { MessageStatus.Text = "There is nothing to redo."; return; }
        undo.Add(doc.ToBytes());
        var bytes = redo[^1]; redo.RemoveAt(redo.Count - 1);
        Restore(bytes);
    }

    void Restore(byte[] bytes)
    {
        var path = doc?.Path;
        var file = PdfFile.FromBytes(bytes, path);
        file.IsModified = true;
        SetDocument(file, keepPosition: true);
        UpdateTitle();
    }

    // ---- opening, saving --------------------------------------------------------------------------------------------

    bool ConfirmClose()
    {
        if (doc is null || !doc.IsModified) return true;
        var name = doc.Path is null ? "this document" : Path.GetFileName(doc.Path);
        switch (Dialogs.YesNoCancel(this, "Pagjoro", $"Save changes to {name} before closing?"))
        {
            case MessageBoxResult.Yes: return Save();
            case MessageBoxResult.No: return true;
            default: return false;
        }
    }

    void OnOpen(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf|All files|*.*", Title = "Open a PDF" };
        if (d.ShowDialog(this) == true) OpenPath(d.FileName);
    }

    void OnRecent(object sender, RequestNavigateEventArgs e) { OpenPath(e.Uri.OriginalString); e.Handled = true; }

    void ShowRecent()
    {
        var items = recent.Items.Select(p => new RecentItem(p, Path.GetFileName(p), "  " + Path.GetDirectoryName(p))).ToList();
        RecentList.ItemsSource = items;
        RecentTitle.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildRecentMenu();
    }

    /// <summary>Opens a file, asking for the password when it needs one. Returns null if the person gave up.</summary>
    PdfFile? OpenWithPrompt(string path, string? purpose = null)
    {
        string? password = null;
        while (true)
        {
            try { return PdfFile.Open(path, password); }
            catch (PdfPasswordRequiredException ex)
            {
                password = Dialogs.Ask(this, "Password needed", $"{Path.GetFileName(path)}{(purpose is null ? "" : " (" + purpose + ")")}\n\n{ex.Message}", "", password: true);
                if (password is null) return null;
            }
            catch (PdfException ex)
            {
                Dialogs.Info(this, "Pagjoro", ex.Message, error: true);
                return null;
            }
        }
    }

    void OpenPath(string path)
    {
        if (!ConfirmClose()) return;
        var file = OpenWithPrompt(path);
        if (file is null) return;
        undo.Clear(); redo.Clear();
        SetDocument(file);
        recent.Add(path); ShowRecent();
        MessageStatus.Text = $"Opened {Path.GetFileName(path)}";
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var pdfs = files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pdfs.Count == 0) { MessageStatus.Text = "That is not a PDF file."; return; }
        if (pdfs.Count == 1) { OpenPath(pdfs[0]); return; }
        if (Dialogs.Confirm(this, "Pagjoro", $"You dropped {pdfs.Count} files. Merge them into one document?")) MergeFiles(pdfs);
        else OpenPath(pdfs[0]);
    }

    bool Save()
    {
        if (doc is null) return true;
        if (doc.Path is null) return SaveAs();
        try { doc.SaveTo(doc.Path); UpdateTitle(); MessageStatus.Text = "Saved."; return true; }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return false; }
    }

    bool SaveAs()
    {
        if (doc is null) return true;
        var d = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = doc.Path is null ? "Document.pdf" : Path.GetFileName(doc.Path), Title = "Save a copy" };
        if (d.ShowDialog(this) != true) return false;
        try { doc.SaveTo(d.FileName); recent.Add(d.FileName); ShowRecent(); UpdateTitle(); MessageStatus.Text = "Saved."; return true; }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return false; }
    }

    void OnSave(object sender, RoutedEventArgs e) => Save();
    void OnSaveAs(object sender, RoutedEventArgs e) => SaveAs();
    void OnCloseFile(object sender, RoutedEventArgs e) { if (ConfirmClose()) { undo.Clear(); redo.Clear(); SetDocument(null); } }
    void OnClosing(object? sender, CancelEventArgs e) { if (!ConfirmClose()) e.Cancel = true; }

    void OnProperties(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"File: {doc.Path ?? "(not saved yet)"}");
        sb.AppendLine($"Pages: {doc.PageCount}");
        var (w, h) = doc.GetPageSize(0);
        sb.AppendLine($"First page size: {w / 72:0.##} x {h / 72:0.##} inches");
        foreach (var kv in doc.GetMetadata()) sb.AppendLine($"{kv.Key}: {kv.Value}");
        Dialogs.Info(this, "Document Properties", sb.ToString());
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!View.HasSelection) { MessageStatus.Text = "Select some text first."; return; }
        try { Clipboard.SetText(View.SelectedText); MessageStatus.Text = "Copied."; } catch (System.Runtime.InteropServices.COMException) { MessageStatus.Text = "Windows would not let Pagjoro use the clipboard just now. Try again."; }
    }

    // ---- view -------------------------------------------------------------------------------------------------------

    void OnZoomIn(object sender, RoutedEventArgs e) => View.SetZoom(View.Zoom * 1.25);
    void OnZoomOut(object sender, RoutedEventArgs e) => View.SetZoom(View.Zoom / 1.25);
    void OnFitWidth(object sender, RoutedEventArgs e) => View.FitWidth();
    void OnFitPage(object sender, RoutedEventArgs e) => View.FitPage();
    void OnActualSize(object sender, RoutedEventArgs e) => View.SetZoom(1.0);
    void OnPrevPage(object sender, RoutedEventArgs e) => View.ScrollToPage(Math.Max(0, View.CurrentPage - 1));
    void OnNextPage(object sender, RoutedEventArgs e) => View.ScrollToPage(Math.Min(View.PageCount - 1, View.CurrentPage + 1));

    void OnGoToPage(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var s = Dialogs.Ask(this, "Go to Page", $"Page number (1 to {doc.PageCount}):", (View.CurrentPage + 1).ToString());
        if (s is not null && int.TryParse(s, out var n) && n >= 1 && n <= doc.PageCount) View.ScrollToPage(n - 1);
    }

    void OnThumbSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Thumbs.SelectedItems.Count == 1 && Thumbs.SelectedItem is ThumbItem t && Thumbs.IsKeyboardFocusWithin || Mouse.LeftButton == MouseButtonState.Pressed)
            if (Thumbs.SelectedItem is ThumbItem item) View.ScrollToPage(item.Index);
    }

    void OnOutlineSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Outline.SelectedItem is OutlineItem o && o.Page >= 0) View.ScrollToPage(o.Page);
    }

    // ---- search -----------------------------------------------------------------------------------------------------

    void OnFocusSearch(object sender, RoutedEventArgs e)
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Focus(); SearchBox.SelectAll();
    }

    void OnCloseSearch(object sender, RoutedEventArgs e)
    {
        SearchBar.Visibility = Visibility.Collapsed;
        View.SetMatches([]);
        SearchCount.Text = "";
    }

    void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { if (found.Count > 0 && SearchBox.Text == lastQuery) View.ShowMatch(View.CurrentMatchIndex + 1); else OnSearch(sender, e); }
        else if (e.Key == Key.Escape) OnCloseSearch(sender, e);
    }

    string lastQuery = "";

    async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var file = doc; var q = SearchBox.Text;
        if (q.Length == 0) { View.SetMatches([]); SearchCount.Text = ""; return; }
        SearchCount.Text = "Searching...";
        var matchCase = CaseBox.IsChecked == true; var word = WordBox.IsChecked == true;
        var result = await Task.Run(() => file.Search(q, matchCase, word));
        if (file != doc) return;
        found = result; lastQuery = q;
        View.SetMatches(result, 0);
        SearchCount.Text = result.Count == 0 ? "Not found" : $"{result.Count} found";
    }

    void OnSearchNext(object sender, RoutedEventArgs e) { if (found.Count > 0) View.ShowMatch(View.CurrentMatchIndex + 1); }
    void OnSearchPrev(object sender, RoutedEventArgs e) { if (found.Count > 0) View.ShowMatch(View.CurrentMatchIndex - 1); }

    // ---- commenting -------------------------------------------------------------------------------------------------

    void OnTool(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }) SelectTool(tag);
    }

    void SelectTool(string tag)
    {
        foreach (var t in new[] { ToolSelect, ToolHighlight, ToolUnderline, ToolStrike, ToolNote, ToolDraw, ToolRect, ToolOval, ToolRedact, ToolText, ToolImage, ToolField, ToolCheck }) t.IsChecked = (string)t.Tag == tag;
        View.Tool = Enum.Parse<ViewTool>(tag);
        MessageStatus.Text = View.Tool switch
        {
            ViewTool.Select => "Drag across text to select it. Ctrl+C copies it.",
            ViewTool.Highlight or ViewTool.Underline or ViewTool.Strikeout => "Drag across text to mark it.",
            ViewTool.Note => "Click on the page where the note should go.",
            ViewTool.Draw => "Drag to draw a line.",
            ViewTool.Rectangle or ViewTool.Ellipse => "Drag to draw the shape.",
            ViewTool.Redact => "Drag a box over what must be removed, then press Apply Redactions.",
            ViewTool.Text => "Click on the page where the text should go.",
            ViewTool.Image => "Drag a box where the picture should go.",
            ViewTool.TextField => "Drag a box where the text field should go.",
            ViewTool.CheckField => "Drag a small box where the check box should go.",
            _ => "",
        };
    }

    void OnColor(object sender, SelectionChangedEventArgs e)
    {
        if (View is null || ColorBox.SelectedItem is not ComboBoxItem item) return;
        View.MarkColor = (string)item.Tag switch { "Green" => PdfColor.Green, "Blue" => PdfColor.Blue, "Red" => PdfColor.Red, _ => PdfColor.Yellow };
    }

    void OnMarkSelection(object sender, RoutedEventArgs e)
    {
        if (!View.HasSelection) { MessageStatus.Text = "Select some text first, then press Mark Selection."; return; }
        View.MarkSelection(AnnotationKind.Highlight, View.MarkColor);
    }

    void OnNoteRequested(int page, double x, double y)
    {
        if (doc is null) return;
        var text = Dialogs.Ask(this, "Add a Note", "What should the note say?", "", multiline: true);
        if (string.IsNullOrWhiteSpace(text)) return;
        PushUndo();
        doc.AddNote(page, x, y, text.Trim(), View.MarkColor);
        View.InvalidatePage(page); AfterEdit(); RefreshMarks();
    }

    void OnShowMarks(object sender, RoutedEventArgs e) { SideTabs.SelectedIndex = 2; RefreshMarks(); }

    void OnMarkSelected(object sender, SelectionChangedEventArgs e)
    {
        if (MarksList.SelectedItem is MarkItem m) View.ScrollToPage(m.Page);
    }

    void OnRemoveMark(object sender, RoutedEventArgs e)
    {
        if (doc is null || MarksList.SelectedItem is not MarkItem m) { MessageStatus.Text = "Choose a mark in the list first."; return; }
        PushUndo();
        try { doc.RemoveAnnotation(m.Page, m.Index); }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        View.InvalidatePage(m.Page); AfterEdit(); RefreshMarks();
    }

    // ---- pages ------------------------------------------------------------------------------------------------------

    List<int> SelectedPages()
    {
        var sel = Thumbs.SelectedItems.OfType<ThumbItem>().Select(t => t.Index).OrderBy(i => i).ToList();
        if (sel.Count == 0 && doc is not null && View.CurrentPage >= 0) sel.Add(View.CurrentPage);
        return sel;
    }

    void StructureChanged(string message)
    {
        View.Refresh();
        RebuildSidePanes();
        AfterEdit();
        MessageStatus.Text = message;
    }

    void ReplaceDocument(PdfFile replacement, string message)
    {
        var path = doc?.Path;
        replacement.IsModified = true;
        SetDocument(replacement, keepPosition: true);
        typeof(PdfFile).GetProperty(nameof(PdfFile.Path))!.SetValue(replacement, path);
        UpdateTitle();
        MessageStatus.Text = message;
    }

    void Rotate(int degrees)
    {
        if (doc is null) return;
        var pages = SelectedPages();
        PushUndo();
        foreach (var p in pages) doc.RotatePage(p, degrees);
        StructureChanged($"Turned {pages.Count} page{(pages.Count == 1 ? "" : "s")}.");
        foreach (var p in pages) if (p < Thumbs.Items.Count) Thumbs.SelectedItems.Add(thumbs[p]);
    }

    void OnRotateLeft(object sender, RoutedEventArgs e) => Rotate(-90);
    void OnRotateRight(object sender, RoutedEventArgs e) => Rotate(90);

    void OnDeletePages(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var pages = SelectedPages();
        if (pages.Count >= doc.PageCount) { Dialogs.Info(this, "Pagjoro", "A document needs at least one page. Choose fewer pages to delete."); return; }
        if (!Dialogs.Confirm(this, "Delete Pages", $"Delete page{(pages.Count == 1 ? "" : "s")} {PageRanges.Describe(pages)}? You can undo this with Ctrl+Z.")) return;
        PushUndo();
        doc.DeletePages(pages);
        StructureChanged($"Deleted {pages.Count} page{(pages.Count == 1 ? "" : "s")}.");
    }

    void MovePages(int by)
    {
        if (doc is null) return;
        var pages = SelectedPages();
        var order = Enumerable.Range(0, doc.PageCount).ToList();
        if (by < 0)
        {
            if (pages[0] == 0) return;
            foreach (var p in pages) { var i = order.IndexOf(p); (order[i - 1], order[i]) = (order[i], order[i - 1]); }
        }
        else
        {
            if (pages[^1] == doc.PageCount - 1) return;
            foreach (var p in Enumerable.Reverse(pages)) { var i = order.IndexOf(p); (order[i + 1], order[i]) = (order[i], order[i + 1]); }
        }
        PushUndo();
        ReplaceDocument(doc.ExtractPages(order), "Moved.");
        foreach (var p in pages) { var np = Math.Clamp(p + by, 0, thumbs.Count - 1); Thumbs.SelectedItems.Add(thumbs[np]); }
    }

    void OnMoveUp(object sender, RoutedEventArgs e) => MovePages(-1);
    void OnMoveDown(object sender, RoutedEventArgs e) => MovePages(1);

    void OnThumbDragStart(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Thumbs.SelectedItems.Count == 0) { dragStart = e.GetPosition(null); return; }
        var now = e.GetPosition(null);
        if (Math.Abs(now.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Thumbs, d) is null) return;
        DragDrop.DoDragDrop(Thumbs, new DataObject("pagjoro-pages", SelectedPages().ToArray()), DragDropEffects.Move);
    }

    void OnThumbDrop(object sender, DragEventArgs e)
    {
        if (doc is null || e.Data.GetData("pagjoro-pages") is not int[] moving) return;
        if (e.OriginalSource is not DependencyObject d || ItemsControl.ContainerFromElement(Thumbs, d) is not ListBoxItem container) return;
        var target = Thumbs.ItemContainerGenerator.IndexFromContainer(container);
        var order = Enumerable.Range(0, doc.PageCount).Where(i => !moving.Contains(i)).ToList();
        var insertAt = order.Count(i => i < target) + (target > moving.Min() ? 1 : 0);
        order.InsertRange(Math.Clamp(insertAt, 0, order.Count), moving.OrderBy(i => i));
        PushUndo();
        ReplaceDocument(doc.ExtractPages(order), "Moved.");
    }

    void OnExtractPages(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var pages = SelectedPages();
        var d = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "Extracted pages.pdf", Title = $"Save pages {PageRanges.Describe(pages)} as a new file" };
        if (d.ShowDialog(this) != true) return;
        try { using var part = doc.ExtractPages(pages); part.SaveTo(d.FileName); MessageStatus.Text = $"Saved {pages.Count} page(s) to {Path.GetFileName(d.FileName)}."; }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); }
    }

    void OnInsertBlank(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var after = Math.Max(0, View.CurrentPage);
        var (w, h) = doc.GetPageSize(after);
        PushUndo();
        doc.InsertBlankPage(after + 1, w, h);
        StructureChanged($"Added a blank page after page {after + 1}.");
    }

    void OnInsertFromFile(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var d = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Insert pages from" };
        if (d.ShowDialog(this) != true) return;
        using var other = OpenWithPrompt(d.FileName);
        if (other is null) return;
        var text = Dialogs.Ask(this, "Insert Pages", $"Which pages of {Path.GetFileName(d.FileName)} (it has {other.PageCount})? Leave empty for all, or type for example 1-3, 5.", "");
        if (text is null) return;
        IReadOnlyList<int> pages;
        try { pages = string.IsNullOrWhiteSpace(text) ? Enumerable.Range(0, other.PageCount).ToList() : PageRanges.Parse(text, other.PageCount); }
        catch (FormatException ex) { Dialogs.Info(this, "Insert Pages", ex.Message); return; }
        var after = Math.Max(0, View.CurrentPage) + 1;
        PushUndo();
        try { doc.ImportPages(other, pages, after); }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        StructureChanged($"Inserted {pages.Count} page(s) after page {after}.");
    }

    void OnMerge(object sender, RoutedEventArgs e)
    {
        var files = Dialogs.MergeList(this, doc?.Path is { } p ? [p] : null);
        if (files is not null) MergeFiles(files);
    }

    void MergeFiles(IReadOnlyList<string> paths)
    {
        var d = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "Merged.pdf", Title = "Save the merged document as" };
        if (d.ShowDialog(this) != true) return;
        try
        {
            using var merged = PdfFile.CreateEmpty();
            foreach (var path in paths)
            {
                using var part = OpenWithPrompt(path, "to merge");
                if (part is null) return;
                merged.ImportPages(part, Enumerable.Range(0, part.PageCount).ToList(), merged.PageCount);
            }
            merged.SaveTo(d.FileName);
        }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        MessageStatus.Text = $"Merged {paths.Count} files into {Path.GetFileName(d.FileName)}.";
        if (Dialogs.Confirm(this, "Merge Files", "Merged. Open the new document now?")) OpenPath(d.FileName);
    }

    void OnSplit(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var choice = Dialogs.Split(this, doc.PageCount);
        if (choice is null) return;
        var folder = new OpenFolderDialog { Title = "Choose the folder for the new files" };
        if (folder.ShowDialog(this) != true) return;
        var groups = new List<IReadOnlyList<int>>();
        switch (choice.Mode)
        {
            case Dialogs.SplitMode.Ranges: groups.AddRange(PageRanges.ParseGroups(choice.Ranges, doc.PageCount)); break;
            default:
                var step = choice.Mode == Dialogs.SplitMode.Single ? 1 : choice.EveryN;
                for (var i = 0; i < doc.PageCount; i += step) groups.Add(Enumerable.Range(i, Math.Min(step, doc.PageCount - i)).ToList());
                break;
        }
        var baseName = doc.Path is null ? "Document" : Path.GetFileNameWithoutExtension(doc.Path);
        try
        {
            var n = 1;
            foreach (var g in groups)
            {
                using var part = doc.ExtractPages(g);
                part.SaveTo(Path.Combine(folder.FolderName, $"{baseName} - part {n++}.pdf"));
            }
        }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        MessageStatus.Text = $"Made {groups.Count} files in {folder.FolderName}.";
        if (Dialogs.Confirm(this, "Split Document", $"Made {groups.Count} files. Open the folder?")) Process.Start(new ProcessStartInfo(folder.FolderName) { UseShellExecute = true });
    }

    void OnCrop(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var m = Dialogs.Margins(this);
        if (m is null) return;
        var pages = SelectedPages();
        PushUndo();
        try
        {
            foreach (var p in pages)
            {
                var box = doc.GetMediaBox(p);
                doc.SetCropBox(p, new PageRect(box.Left + m.Value.Left, box.Bottom + m.Value.Bottom, box.Right - m.Value.Right, box.Top - m.Value.Top));
            }
        }
        catch (ArgumentException) { Dialogs.Info(this, "Crop Pages", "Those margins leave nothing of the page. Use smaller numbers."); return; }
        StructureChanged($"Cropped {pages.Count} page(s).");
    }

    // ---- redaction --------------------------------------------------------------------------------------------------

    void OnClearRedactions(object sender, RoutedEventArgs e) { View.ClearPendingRedactions(); MessageStatus.Text = "Cleared."; }

    void OnApplyRedactions(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        if (View.PendingRedactionCount == 0) { Dialogs.Info(this, "Redaction", "Nothing is marked yet. Use Mark for Redaction, then drag a box over what must be removed."); return; }
        if (!Dialogs.Confirm(this, "Apply Redactions",
            $"This permanently removes everything under the {View.PendingRedactionCount} marked area(s) and puts a solid black block there.\n\n" +
            "Any line of text or picture that touches a marked area is removed entirely, not just the part inside the box. After you save, the removed content cannot be recovered from the file.\n\nContinue?")) return;
        PushUndo();
        var removed = 0;
        foreach (var (page, boxes) in View.PendingRedactions.ToList()) removed += doc.Redact(page, boxes, PdfColor.Black);
        View.ClearPendingRedactions();
        StructureChanged($"Redaction applied. {removed} item(s) removed. Save the file to keep the change.");
    }

    // ---- converting and printing ------------------------------------------------------------------------------------

    async void OnExportImages(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var folder = new OpenFolderDialog { Title = "Choose the folder for the picture files" };
        if (folder.ShowDialog(this) != true) return;
        var file = doc; var dir = folder.FolderName;
        var baseName = file.Path is null ? "Page" : Path.GetFileNameWithoutExtension(file.Path);
        try
        {
            await Task.Run(() =>
            {
                for (var i = 0; i < file.PageCount; i++)
                {
                    var (w, h) = file.GetPageSize(i);
                    var img = file.Render(i, (int)Math.Round(w / 72 * 150), (int)Math.Round(h / 72 * 150));
                    var bmp = BitmapSource.Create(img.Width, img.Height, 150, 150, PixelFormats.Bgra32, null, img.Pixels, img.Stride);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = File.Create(Path.Combine(dir, $"{baseName} - page {i + 1}.png"));
                    enc.Save(fs);
                }
            });
            MessageStatus.Text = $"Saved {file.PageCount} picture(s) in {dir}.";
        }
        catch (Exception ex) when (ex is IOException or PdfException or UnauthorizedAccessException) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); }
    }

    void OnExportText(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var d = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = (doc.Path is null ? "Document" : Path.GetFileNameWithoutExtension(doc.Path)) + ".txt", Title = "Save the text as" };
        if (d.ShowDialog(this) != true) return;
        try
        {
            var sb = new StringBuilder();
            for (var i = 0; i < doc.PageCount; i++) { sb.AppendLine($"--- Page {i + 1} ---"); sb.AppendLine(doc.GetPageText(i)); sb.AppendLine(); }
            File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
            MessageStatus.Text = "Text saved.";
        }
        catch (IOException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); }
    }

    void OnPrint(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var dialog = new PrintDialog { MinPage = 1, MaxPage = (uint)doc.PageCount, UserPageRangeEnabled = true };
        if (dialog.ShowDialog() != true) return;
        var from = 0; var to = doc.PageCount - 1;
        if (dialog.PageRangeSelection == PageRangeSelection.UserPages) { from = dialog.PageRange.PageFrom - 1; to = dialog.PageRange.PageTo - 1; }
        var pageW = dialog.PrintableAreaWidth; var pageH = dialog.PrintableAreaHeight;
        var fixedDoc = new FixedDocument();
        for (var i = Math.Max(0, from); i <= Math.Min(to, doc.PageCount - 1); i++)
        {
            var (w, h) = doc.GetPageSize(i);
            var scale = Math.Min(pageW / (w * 96 / 72), pageH / (h * 96 / 72));
            var dipW = w * 96 / 72 * scale; var dipH = h * 96 / 72 * scale;
            var img = doc.Render(i, (int)(dipW * 300 / 96), (int)(dipH * 300 / 96));
            var bmp = BitmapSource.Create(img.Width, img.Height, 300, 300, PixelFormats.Bgra32, null, img.Pixels, img.Stride);
            var fp = new FixedPage { Width = pageW, Height = pageH };
            var image = new Image { Source = bmp, Width = dipW, Height = dipH };
            fp.Children.Add(image);
            var pc = new PageContent(); ((System.Windows.Markup.IAddChild)pc).AddChild(fp);
            fixedDoc.Pages.Add(pc);
        }
        try { dialog.PrintDocument(fixedDoc.DocumentPaginator, doc.Path is null ? "Pagjoro document" : Path.GetFileName(doc.Path)); MessageStatus.Text = "Sent to the printer."; }
        catch (PrintQueueException ex) { Dialogs.Info(this, "Pagjoro", "The printer reported a problem: " + ex.Message, error: true); }
    }
}
