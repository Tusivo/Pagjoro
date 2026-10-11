// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pagjoro.Core;

namespace Pagjoro.App;

public partial class MainWindow
{
    bool updatingUi;
    static readonly string[] ZoomLevels = ["25%", "50%", "75%", "100%", "125%", "150%", "200%", "300%", "400%"];

    void InitMenusAndToolbar()
    {
        ZoomBox.ItemsSource = ZoomLevels;
        RebuildRecentMenu();
    }

    void RebuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        var items = recent.Items;
        if (items.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(none yet)", IsEnabled = false });
            return;
        }
        foreach (var path in items)
        {
            var item = new MenuItem { Header = Path.GetFileName(path).Replace("_", "__"), ToolTip = path };
            var p = path;
            item.Click += (_, _) => OpenPath(p);
            RecentMenu.Items.Add(item);
        }
        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear List" };
        clear.Click += (_, _) => { recent.Clear(); RebuildRecentMenu(); ShowRecent(); };
        RecentMenu.Items.Add(clear);
    }

    // ---- keeping the toolbar in step with the document --------------------------------------------------------------

    void SyncToolbar()
    {
        updatingUi = true;
        try
        {
            ZoomBox.Text = doc is null ? "" : $"{Math.Round(View.Zoom * 100)}%";
            PageOf.Text = doc is null ? "/ 0" : "/ " + View.PageCount;
            if (!PageBox.IsKeyboardFocusWithin) PageBox.Text = doc is null ? "" : (Math.Max(0, View.CurrentPage) + 1).ToString();
        }
        finally { updatingUi = false; }
    }

    void OnZoomPicked(object sender, SelectionChangedEventArgs e)
    {
        if (updatingUi || ZoomBox.SelectedItem is not string s) return;
        ApplyZoomText(s);
    }

    void OnZoomKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyZoomText(ZoomBox.Text);
        View.Focus();
    }

    void ApplyZoomText(string text)
    {
        if (doc is null) return;
        var t = text.Trim().TrimEnd('%').Trim();
        if (double.TryParse(t, out var pct) && pct >= 10 && pct <= 600) View.SetZoom(pct / 100);
        else { MessageStatus.Text = "Type a zoom between 10% and 600%."; SyncToolbar(); }
    }

    void OnPageBoxKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || doc is null) return;
        if (int.TryParse(PageBox.Text, out var n) && n >= 1 && n <= doc.PageCount) { View.ScrollToPage(n - 1); View.Focus(); }
        else { MessageStatus.Text = $"Type a page number from 1 to {doc.PageCount}."; SyncToolbar(); }
    }

    // ---- tools chosen from the menu ---------------------------------------------------------------------------------

    void OnToolMenu(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag }) SelectTool(tag);
    }

    void OnExit(object sender, RoutedEventArgs e) => Close();

    // ---- help -------------------------------------------------------------------------------------------------------

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show("Windows did not open the link. The address is " + url, AppInfo.Name); }
    }

    void OnReportProblem(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.Repository + "/issues/new/choose");

    void OnRequestFeature(object sender, RoutedEventArgs e)
    {
        var body = "What would you like Pagjoro to do, and how would it help?\r\n\r\n\r\n(Please do not include private documents.)\r\n\r\nPagjoro " + AppInfo.Version;
        OpenUrl($"mailto:{AppInfo.ContactEmail}?subject={Uri.EscapeDataString("Pagjoro feature request")}&body={Uri.EscapeDataString(body)}");
    }

    void OnSupport(object sender, RoutedEventArgs e) => OpenUrl("https://buymeacoffee.com/tusivo");

    void OnAbout(object sender, RoutedEventArgs e)
    {
        var w = new Window
        {
            Title = "About Pagjoro", Width = 760, Height = 600, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var dock = new DockPanel { Margin = new Thickness(24) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock { Text = AppInfo.Name, FontSize = 30, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock { Text = "Version " + AppInfo.Version + (AppInfo.IsPreRelease ? "  (early development: not ready for real documents yet)" : ""), Opacity = 0.8 });
        head.Children.Add(new TextBlock { Text = "Licence: " + AppInfo.License, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 0) });
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button Link(string text, string url) { var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 0) }; b.Click += (_, _) => OpenUrl(url); return b; }
        buttons.Children.Add(Link("Source Code", AppInfo.Repository));
        buttons.Children.Add(Link("Website", AppInfo.Website));
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new TextBox { Text = ReadReadme(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        w.Content = dock;
        w.ShowDialog();
    }

    static string ReadReadme()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("README.md");
        if (stream is null) return "The README is not included in this build.";
        using var reader = new StreamReader(stream);
        return PlainText.FromMarkdown(reader.ReadToEnd());
    }

    async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        MessageStatus.Text = "Looking for updates...";
        try
        {
            var release = await Updater.CheckAsync(AppInfo.Version);
            if (release is null)
            {
                MessageStatus.Text = "";
                Dialogs.Info(this, "Check for Updates", $"You have the newest version ({AppInfo.Version}).");
                return;
            }
            var notes = release.Notes.Trim();
            if (notes.Length > 500) notes = notes[..500].TrimEnd() + " …";
            if (!Dialogs.Confirm(this, "Check for Updates",
                $"Version {release.Version} is available (you have {AppInfo.Version}).\n\n{(notes.Length > 0 ? PlainText.FromMarkdown(notes) + "\n\n" : "")}" +
                "Download and install it now? Pagjoro keeps your current copy as a backup in the same folder, and only installs an update that carries Pagjoro's release signature.")) { MessageStatus.Text = ""; return; }
            var exe = Environment.ProcessPath ?? throw new UpdateException("Pagjoro could not find its own file.");
            var progress = new Progress<int>(p => MessageStatus.Text = $"Downloading Pagjoro {release.Version} ... {p}%");
            var file = await Updater.DownloadAsync(release, exe, progress);
            var backup = Updater.Install(file, exe, AppInfo.Version);
            MessageStatus.Text = $"Installed version {release.Version}.";
            if (Dialogs.Confirm(this, "Check for Updates", $"Version {release.Version} is installed. Your previous version was kept as {Path.GetFileName(backup)}.\n\nClose and reopen Pagjoro now?"))
            {
                if (!ConfirmClose()) return;
                doc?.AssignPath(doc.Path);   // nothing to do; the document is closed with the window
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageStatus.Text = "";
            Dialogs.Info(this, "Check for Updates", Updater.Friendly(ex), error: true);
        }
    }
}
