// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public partial class MainWindow
{
    PdfFile.StampChoice? pendingStamp;
    readonly WpfImageRecoder recoder = new();

    void WireMore()
    {
        View.StampRequested += OnStampRequested;
        BuildStampMenu();
    }

    /// <summary>Applies a changed copy of the document (structure edits that work on bytes), keeping Undo.</summary>
    void ApplyBytes(Func<byte[], byte[]> change, string message)
    {
        if (doc is null) return;
        try
        {
            var bytes = change(doc.ToBytes(removeSecurity: true));
            PushUndo();
            ReplaceDocument(PdfFile.FromBytes(bytes), message);
        }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); }
    }

    string BaseName() => doc?.Path is null ? "Document" : Path.GetFileNameWithoutExtension(doc.Path);

    void OnToggleFacing(object sender, RoutedEventArgs e)
    {
        View.SetFacing(FacingItem.IsChecked);
        MessageStatus.Text = FacingItem.IsChecked ? "Pages are shown two at a time." : "Pages are shown one at a time.";
    }
    // ---- stamps -----------------------------------------------------------------------------------------------------

    void BuildStampMenu()
    {
        StampMenu.Items.Clear();
        foreach (var preset in PdfFile.StampPresets)
        {
            var p = preset;
            var item = new MenuItem { Header = p.Name };
            item.Click += (_, _) => ChooseStamp(new PdfFile.StampChoice(p.Text, p.Color, true));
            StampMenu.Items.Add(item);
        }
        StampMenu.Items.Add(new Separator());
        var custom = new MenuItem { Header = "Your own words..." };
        custom.Click += (_, _) =>
        {
            var text = Dialogs.Ask(this, "Stamp", "The words for the stamp (short, in capitals works best):", "");
            if (!string.IsNullOrWhiteSpace(text)) ChooseStamp(new PdfFile.StampChoice(text.Trim().ToUpperInvariant(), new PdfColor(30, 80, 190), true));
        };
        StampMenu.Items.Add(custom);
    }

    void ChooseStamp(PdfFile.StampChoice choice)
    {
        if (doc is null) { MessageStatus.Text = "Open a PDF first."; return; }
        pendingStamp = choice;
        SelectTool("Stamp");
    }

    void OnStampRequested(int page, double x, double y)
    {
        if (doc is null || pendingStamp is not { } stamp) return;
        PushUndo();
        try
        {
            var second = stamp.WithDate ? DateTime.Now.ToString("yyyy-MM-dd") : null;
            doc.AddStamp(page, stamp.Text, stamp.Color, x, y, 26, 0, second);
        }
        catch (PdfException ex) { undo.RemoveAt(undo.Count - 1); Dialogs.Info(this, "Stamp", ex.Message, error: true); return; }
        View.InvalidatePage(page); AfterEdit();
        MessageStatus.Text = "Stamp placed. Click again to place another, or choose another tool.";
    }

    // ---- links ------------------------------------------------------------------------------------------------------

    void AddLink(int page, PageRect area)
    {
        if (doc is null) return;
        var answer = Dialogs.Ask(this, "Add a Link",
            "Where should the link go? Type a web address (https://...) or a page number of this document:", "https://");
        if (string.IsNullOrWhiteSpace(answer)) return;
        answer = answer.Trim();
        ApplyBytes(b => int.TryParse(answer, out var n) && n >= 1 && n <= doc.PageCount
                ? PdfStructure.AddPageLink(b, page, area, n - 1)
                : PdfStructure.AddWebLink(b, page, area, answer.Contains("://") || answer.StartsWith("mailto:") ? answer : "https://" + answer),
            "Link added. Save to keep it.");
        SelectTool("Select");
    }

    // ---- checks -----------------------------------------------------------------------------------------------------

    void ShowChecks(string title, string intro, IReadOnlyList<CheckResult> results)
    {
        var w = new Window { Title = title, Width = 760, Height = 600, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var dock = new DockPanel { Margin = new Thickness(18) };
        var head = new System.Windows.Controls.TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var copy = new Button { Content = "Copy as Text", Padding = new Thickness(14, 5, 14, 5) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(string.Join("\r\n\r\n", results.Select(r => $"[{r.Status}] {r.Title}\r\n{r.Detail}"))); MessageStatus.Text = "Copied."; }
            catch (System.Runtime.InteropServices.COMException) { MessageStatus.Text = "Windows would not let Pagjoro use the clipboard just now."; }
        };
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(copy); buttons.Children.Add(close); dock.Children.Add(buttons);

        var panel = new StackPanel();
        foreach (var r in results)
        {
            var (glyph, colour) = r.Status switch
            {
                CheckStatus.Passed => ("✔", Color.FromRgb(22, 130, 60)),
                CheckStatus.Warning => ("⚠", Color.FromRgb(200, 120, 0)),
                CheckStatus.Failed => ("✖", Color.FromRgb(200, 30, 30)),
                _ => ("ℹ", Color.FromRgb(60, 110, 200)),
            };
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = glyph, FontSize = 20, Foreground = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Top });
            var text = new StackPanel();
            text.Children.Add(new System.Windows.Controls.TextBlock { Text = r.Title, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new System.Windows.Controls.TextBlock { Text = r.Detail, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
            Grid.SetColumn(text, 1); row.Children.Add(text);
            panel.Children.Add(row);
        }
        dock.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        w.Content = dock;
        w.ShowDialog();
    }

    void OnAccessibilityCheck(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        try
        {
            var results = DocumentChecks.Accessibility(doc.ToBytes(removeSecurity: true));
            var failed = results.Count(r => r.Status == CheckStatus.Failed); var warn = results.Count(r => r.Status == CheckStatus.Warning);
            ShowChecks("Accessibility Check", failed + warn == 0 ? "Nothing needs attention." : $"{failed} problem(s) and {warn} thing(s) to look at, from the checks Pagjoro can do.", results);
        }
        catch (PdfException ex) { Dialogs.Info(this, "Accessibility Check", ex.Message, error: true); }
    }

    void OnPrintCheck(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        try { ShowChecks("Print and Sharing Check", "How safe this file is to print professionally or pass on.", DocumentChecks.Printing(doc.ToBytes(removeSecurity: true))); }
        catch (PdfException ex) { Dialogs.Info(this, "Print Check", ex.Message, error: true); }
    }

    void OnReadingOptions(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var m = doc.GetMetadata();
        var f = new FormBuilder(this, "Reading Options");
        f.Note("Helps screen readers and the window title of other PDF programs.");
        var title = f.Text("Document title", m.TryGetValue("Title", out var t) ? t : "");
        var lang = f.Text("Language code (for example en-US, fr-FR, de-DE)", "en-US", 140);
        var show = f.Check("Show the title instead of the file name in the window title bar", true);
        if (!f.Show("Apply")) return;
        ApplyBytes(b => PdfStructure.SetReadingOptions(b, lang.Text, show.IsChecked == true, title.Text), "Reading options set. Save to keep them.");
    }

    // ---- bookmarks --------------------------------------------------------------------------------------------------

    void OnBookmarks(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var items = doc.GetBookmarks().ToList();
        var w = new Window { Title = "Bookmarks", Width = 600, Height = 520, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var dock = new DockPanel { Margin = new Thickness(16) };
        var list = new ListBox();
        void Refill(int select = -1)
        {
            list.Items.Clear();
            foreach (var b in items) list.Items.Add(new string(' ', b.Level * 4) + b.Title + "    (page " + (b.PageIndex + 1) + ")");
            if (select >= 0 && select < items.Count) list.SelectedIndex = select;
        }
        var side = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 150 };
        DockPanel.SetDock(side, Dock.Right);
        Button Btn(string text, Action act) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 5, 8, 5) }; b.Click += (_, _) => act(); side.Children.Add(b); return b; }
        Btn("Add (this page)", () =>
        {
            var title = Dialogs.Ask(w, "Add Bookmark", "Name of the bookmark (it will point to the page you are viewing):", "");
            if (string.IsNullOrWhiteSpace(title)) return;
            var at = list.SelectedIndex >= 0 ? list.SelectedIndex + 1 : items.Count;
            var level = list.SelectedIndex >= 0 ? items[list.SelectedIndex].Level : 0;
            items.Insert(at, new BookmarkInfo(title.Trim(), Math.Max(0, View.CurrentPage), level));
            Refill(at);
        });
        Btn("Rename...", () =>
        {
            if (list.SelectedIndex < 0) return;
            var i = list.SelectedIndex;
            var title = Dialogs.Ask(w, "Rename Bookmark", "New name:", items[i].Title);
            if (string.IsNullOrWhiteSpace(title)) return;
            items[i] = items[i] with { Title = title.Trim() }; Refill(i);
        });
        Btn("Set page...", () =>
        {
            if (list.SelectedIndex < 0) return;
            var i = list.SelectedIndex;
            var s = Dialogs.Ask(w, "Bookmark Page", $"Page number (1 to {doc.PageCount}):", (items[i].PageIndex + 1).ToString());
            if (int.TryParse(s, out var n) && n >= 1 && n <= doc.PageCount) { items[i] = items[i] with { PageIndex = n - 1 }; Refill(i); }
        });
        Btn("Delete", () => { if (list.SelectedIndex < 0) return; var i = list.SelectedIndex; items.RemoveAt(i); Refill(Math.Min(i, items.Count - 1)); });
        Btn("Move up", () => { var i = list.SelectedIndex; if (i > 0) { (items[i], items[i - 1]) = (items[i - 1], items[i]); Refill(i - 1); } });
        Btn("Move down", () => { var i = list.SelectedIndex; if (i >= 0 && i < items.Count - 1) { (items[i], items[i + 1]) = (items[i + 1], items[i]); Refill(i + 1); } });
        Btn("Indent", () => { var i = list.SelectedIndex; if (i > 0 && items[i].Level <= items[i - 1].Level) { items[i] = items[i] with { Level = items[i].Level + 1 }; Refill(i); } });
        Btn("Outdent", () => { var i = list.SelectedIndex; if (i >= 0 && items[i].Level > 0) { items[i] = items[i] with { Level = items[i].Level - 1 }; Refill(i); } });
        Btn("Remove all", () => { items.Clear(); Refill(); });
        dock.Children.Add(side);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel); dock.Children.Add(buttons);
        dock.Children.Add(list);
        w.Content = dock;
        Refill();
        if (w.ShowDialog() != true) return;
        ApplyBytes(b => PdfStructure.SetBookmarks(b, items), "Bookmarks updated. Save to keep them.");
        RebuildSidePanes();
    }

    // ---- page numbering -----------------------------------------------------------------------------------------------

    void OnPageLabels(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var bytes = doc.ToBytes(removeSecurity: true);
        IReadOnlyList<PageLabelRange> existing;
        try { existing = PdfStructure.LabelRanges(bytes); } catch (PdfException ex) { Dialogs.Info(this, "Page Numbering", ex.Message, error: true); return; }
        var f = new FormBuilder(this, "Page Numbering", 540);
        f.Note("Choose how a run of pages is numbered when it is shown by a PDF reader (for example the first pages i, ii, iii and then 1, 2, 3). Existing: " +
               (existing.Count == 0 ? "none (pages are numbered 1, 2, 3...)." : string.Join("; ", existing.Select(r => $"from page {r.FirstPage + 1}: {r.Prefix}{r.Style}"))));
        var from = f.Text("Starting at page", (Math.Max(0, View.CurrentPage) + 1).ToString(), 100);
        var style = f.Choice("Style", ["1, 2, 3", "i, ii, iii", "I, II, III", "a, b, c", "A, B, C", "No number (only the prefix)", "Remove all page numbering"]);
        var prefix = f.Text("Words before the number (optional, for example A-)", "", 160);
        var start = f.Text("First number in this run", "1", 100);
        var replace = f.Check("Replace the existing numbering instead of adding to it");
        if (!f.Show("Apply", () =>
        {
            if (style.SelectedIndex == 6) return true;
            if (!int.TryParse(from.Text, out var p) || p < 1 || p > doc.PageCount) { Dialogs.Info(f.Window, "Page Numbering", $"Type a page from 1 to {doc.PageCount}."); return false; }
            if (!int.TryParse(start.Text, out var s) || s < 1) { Dialogs.Info(f.Window, "Page Numbering", "Type the first number, such as 1."); return false; }
            return true;
        })) return;
        if (style.SelectedIndex == 6) { ApplyBytes(b => PdfStructure.SetPageLabels(b, []), "Page numbering removed. Save to keep it."); return; }
        var styleName = style.SelectedIndex switch { 1 => "roman", 2 => "ROMAN", 3 => "letters", 4 => "LETTERS", 5 => "none", _ => "decimal" };
        var range = new PageLabelRange(int.Parse(from.Text) - 1, styleName, prefix.Text, int.Parse(start.Text));
        var all = replace.IsChecked == true ? [] : existing.Where(r => r.FirstPage != range.FirstPage).ToList();
        all.Add(range);
        ApplyBytes(b => PdfStructure.SetPageLabels(b, all), "Page numbering set. Save to keep it.");
    }

    // ---- attachments ------------------------------------------------------------------------------------------------

    void OnAttachments(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var bytes = doc.ToBytes(removeSecurity: true);
        var changed = false;
        var w = new Window { Title = "Attachments", Width = 560, Height = 420, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var dock = new DockPanel { Margin = new Thickness(16) };
        var list = new ListBox();
        void Refill()
        {
            list.Items.Clear();
            foreach (var a in PdfStructure.Attachments(bytes)) list.Items.Add($"{a.Name}    ({a.Size / 1024.0:N1} KB)");
        }
        var side = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 150 };
        DockPanel.SetDock(side, Dock.Right);
        Button Btn(string text, Action act) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 5, 8, 5) }; b.Click += (_, _) => act(); side.Children.Add(b); return b; }
        string? Selected() => list.SelectedIndex < 0 ? null : PdfStructure.Attachments(bytes)[list.SelectedIndex].Name;
        Btn("Attach a file...", () =>
        {
            var d = new OpenFileDialog { Title = "Choose the file to attach" };
            if (d.ShowDialog(w) != true) return;
            try { bytes = PdfStructure.AddAttachment(bytes, Path.GetFileName(d.FileName), File.ReadAllBytes(d.FileName)); changed = true; Refill(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PdfException) { Dialogs.Info(w, "Attachments", ex.Message, error: true); }
        });
        Btn("Save a copy...", () =>
        {
            if (Selected() is not { } name) return;
            var d = new SaveFileDialog { FileName = name, Title = "Save the attachment as" };
            if (d.ShowDialog(w) != true) return;
            try { File.WriteAllBytes(d.FileName, PdfStructure.ExtractAttachment(bytes, name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PdfException) { Dialogs.Info(w, "Attachments", ex.Message, error: true); }
        });
        Btn("Remove", () =>
        {
            if (Selected() is not { } name) return;
            try { bytes = PdfStructure.RemoveAttachment(bytes, name); changed = true; Refill(); }
            catch (PdfException ex) { Dialogs.Info(w, "Attachments", ex.Message, error: true); }
        });
        dock.Children.Add(side);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel); dock.Children.Add(buttons);
        dock.Children.Add(list);
        w.Content = dock;
        Refill();
        if (w.ShowDialog() != true || !changed) return;
        var result = bytes;
        ApplyBytes(_ => result, "Attachments updated. Save to keep them.");
    }

    // ---- exporting ----------------------------------------------------------------------------------------------------

    async Task ExportAsync(string title, string filter, string extension, Func<PdfFile, byte[]> make, string doneMessage)
    {
        if (doc is null) return;
        var save = new SaveFileDialog { Filter = filter, FileName = BaseName() + extension, Title = title };
        if (save.ShowDialog(this) != true) return;
        var file = doc;
        MessageStatus.Text = "Exporting...";
        try
        {
            var data = await Task.Run(() => make(file));
            await File.WriteAllBytesAsync(save.FileName, data);
            MessageStatus.Text = doneMessage + " " + Path.GetFileName(save.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PdfException)
        {
            MessageStatus.Text = "";
            Dialogs.Info(this, title, "The file could not be written: " + ex.Message, error: true);
        }
    }

    async void OnExportHtml(object sender, RoutedEventArgs e) =>
        await ExportAsync("Export as a Web Page", "Web page (*.html)|*.html", ".html", f => Encoding.UTF8.GetBytes(DocumentText.ToHtml(f, BaseName())), "Saved");

    async void OnExportWord(object sender, RoutedEventArgs e) =>
        await ExportAsync("Export as a Word Document", "Word document (*.docx)|*.docx", ".docx", f => DocumentText.ToDocx(f, BaseName()), "Saved");

    async void OnExportExcel(object sender, RoutedEventArgs e) =>
        await ExportAsync("Export as an Excel Workbook", "Excel workbook (*.xlsx)|*.xlsx", ".xlsx", f => DocumentText.ToXlsx(DocumentText.Rows(f)), "Saved");

    async void OnExportCsv(object sender, RoutedEventArgs e) =>
        await ExportAsync("Export as CSV", "Comma separated values (*.csv)|*.csv", ".csv", f => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(DocumentText.ToCsv(DocumentText.Rows(f)))).ToArray(), "Saved");

    // ---- Office files and scanning ---------------------------------------------------------------------------------------

    async void OnOfficeToPdf(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = OfficeConverter.FileFilter, Title = "Choose the document to turn into a PDF" };
        if (d.ShowDialog(this) != true) return;
        var save = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = Path.GetFileNameWithoutExtension(d.FileName) + ".pdf", Title = "Save the PDF as" };
        if (save.ShowDialog(this) != true) return;
        MessageStatus.Text = "Converting " + Path.GetFileName(d.FileName) + " (this can take a little while)...";
        IsEnabled = false;
        try { await OfficeConverter.ConvertAsync(d.FileName, save.FileName); }
        catch (Exception ex)
        {
            IsEnabled = true; MessageStatus.Text = "";
            Dialogs.Info(this, "Office File to PDF", ex.Message, error: true);
            return;
        }
        IsEnabled = true;
        MessageStatus.Text = "Made " + Path.GetFileName(save.FileName);
        if (Dialogs.Confirm(this, "Office File to PDF", "The PDF was made. Open it now?")) OpenPath(save.FileName);
    }

    void OnScan(object sender, RoutedEventArgs e)
    {
        var pictures = new List<string>();
        try
        {
            while (true)
            {
                var path = Scanner.Acquire();
                if (path is null) break;
                pictures.Add(path);
                if (!Dialogs.Confirm(this, "Scan to PDF", $"{pictures.Count} page(s) scanned.\n\nPress OK to scan another page, or Cancel to finish.")) break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Dialogs.Info(this, "Scan to PDF", ex.Message, error: true);
            if (pictures.Count == 0) return;
        }
        if (pictures.Count == 0) return;
        try
        {
            if (!ConfirmClose()) return;
            var made = PdfFile.CreateEmpty();
            for (var i = 0; i < pictures.Count; i++)
            {
                var (pixels, w, h, dx, dy) = LoadPicture(pictures[i]);
                double pw = w / dx * 72, ph = h / dy * 72;
                made.InsertBlankPage(i, pw, ph);
                made.AddImage(i, pixels, w, h, new PageRect(0, 0, pw, ph));
            }
            var bytes = made.ToBytes(); made.Dispose();
            var file = PdfFile.FromBytes(bytes);
            file.IsModified = true;
            undo.Clear(); redo.Clear();
            SetDocument(file);
            MessageStatus.Text = $"Scanned {pictures.Count} page(s). Use Review, Recognize Text to make them searchable, then Save.";
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or FileFormatException or PdfException or InvalidOperationException)
        {
            Dialogs.Info(this, "Scan to PDF", "Pagjoro could not make the document: " + ex.Message, error: true);
        }
        finally
        {
            foreach (var p in pictures) { try { File.Delete(p); } catch (IOException) { } }
        }
    }

    // ---- form data ----------------------------------------------------------------------------------------------------

    void OnExportFormData(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        IReadOnlyList<FormField> fields;
        try { fields = PdfForms.Read(doc.ToBytes(removeSecurity: true)); }
        catch (PdfException ex) { Dialogs.Info(this, "Forms", ex.Message, error: true); return; }
        if (fields.Count == 0) { Dialogs.Info(this, "Export Form Data", "This document has no form fields."); return; }
        var save = new SaveFileDialog { Filter = "Form data (*.json)|*.json|Spreadsheet row (*.csv)|*.csv", FileName = BaseName() + " - form data", Title = "Save the form data as" };
        if (save.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(save.FileName, save.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? FormData.ToCsv(fields) : FormData.ToJson(fields), new UTF8Encoding(false));
            MessageStatus.Text = $"Saved {fields.Count} field(s) to {Path.GetFileName(save.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Dialogs.Info(this, "Export Form Data", ex.Message, error: true); }
    }

    void OnImportFormData(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var d = new OpenFileDialog { Filter = "Form data (*.json)|*.json", Title = "Choose the form data file" };
        if (d.ShowDialog(this) != true) return;
        try
        {
            var (values, checks) = FormData.FromJson(File.ReadAllText(d.FileName));
            var applied = 0;
            ApplyBytes(b => { var (pdf, n) = FormData.Apply(b, values, checks); applied = n; return pdf; }, "Form data loaded. Save to keep it.");
            if (applied == 0) MessageStatus.Text = "None of the names in that file match a field in this form.";
            else MessageStatus.Text = $"{applied} field(s) filled in from the file. Save to keep it.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PdfException)
        {
            Dialogs.Info(this, "Import Form Data", ex.Message, error: true);
        }
    }
}
