// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pagjoro.Core;

namespace Pagjoro.App;

public partial class MainWindow
{
    readonly AppSettings settings = new();
    string theme = "dark";

    static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    // Night: a deep navy palette with a soft blue accent (a calm, low-glare look for evenings)
    static readonly Brush NightBg = B("#011627"), NightSurface = B("#0B253A"), NightSoft = B("#10304A"), NightInk = B("#D6DEEB"), NightBackdrop = B("#06111C");

    void InitTheme()
    {
        var chosen = settings.Theme;
        if (chosen is null)
        {
            // first run: follow Windows until the person chooses
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            chosen = key?.GetValue("AppsUseLightTheme") is int v && v == 1 ? "light" : "dark";
        }
        ApplyTheme(chosen);
    }

    void OnThemePicked(object sender, SelectionChangedEventArgs e)
    {
        if (updatingUi || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string name }) return;
        settings.SetTheme(name);
        ApplyTheme(name);
    }

    void ApplyTheme(string name)
    {
        theme = name;
        var app = Application.Current;
        app.ThemeMode = name == "light" ? ThemeMode.Light : ThemeMode.Dark;
        IconExtension.Dark = name != "light";

        updatingUi = true;
        try { foreach (ComboBoxItem item in ThemeBox.Items) if ((string)item.Tag == name) ThemeBox.SelectedItem = item; }
        finally { updatingUi = false; }

        var night = name == "night";
        Resources["MenuPopupBrush"] = night ? B("#0B253A") : name == "light" ? B("#FFFFFF") : B("#2B2B2B");
        void Paint(Control c, Brush? bg, Brush? fg = null)
        {
            if (bg is null) c.ClearValue(Control.BackgroundProperty); else c.Background = bg;
            if (fg is null) c.ClearValue(Control.ForegroundProperty); else c.Foreground = fg;
        }
        Paint(this, night ? NightBg : null, night ? NightInk : null);
        Paint(MainMenu, night ? NightBg : null, night ? NightInk : null);
        MenuRow.Background = night ? NightBg : Brushes.Transparent;
        Tray.Background = night ? NightSurface : Brushes.Transparent;
        foreach (var bar in Tray.ToolBars) Paint(bar, night ? NightSurface : null, night ? NightInk : null);
        Paint(Status, night ? NightSurface : null, night ? NightInk : null);
        Paint(SideTabs, night ? NightBg : null, night ? NightInk : null);
        foreach (var list in new Control[] { Thumbs, Outline, MarksList }) Paint(list, night ? NightBg : null, night ? NightInk : null);
        Scroller.Background = night ? NightBackdrop : B("#FF7A7F85");
    }
}
