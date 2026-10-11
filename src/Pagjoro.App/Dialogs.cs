// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Pagjoro.App;

/// <summary>Small windows built in code: a question, a password, a list of files to merge, the ways to split.</summary>
public static class Dialogs
{
    static Window Make(Window? owner, string title, double width = 460)
    {
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        if (owner is not null) w.Owner = owner;
        return w;
    }

    static StackPanel Buttons(Window w, string ok, bool cancel, Func<bool>? onOk = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var okButton = new Button { Content = ok, IsDefault = true, Padding = new Thickness(18, 6, 18, 6), MinWidth = 88 };
        okButton.Click += (_, _) => { if (onOk is null || onOk()) { w.DialogResult = true; } };
        row.Children.Add(okButton);
        if (cancel)
        {
            var c = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
            row.Children.Add(c);
        }
        return row;
    }

    public static void Info(Window? owner, string title, string message, bool error = false)
    {
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, error ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    public static bool Confirm(Window? owner, string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <summary>Yes, No or Cancel (used for "save changes first?").</summary>
    public static MessageBoxResult YesNoCancel(Window? owner, string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    public static string? Ask(Window? owner, string title, string message, string initial = "", bool password = false, bool multiline = false)
    {
        var w = Make(owner, title);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        Control input;
        if (password) input = new PasswordBox { Password = initial };
        else input = new TextBox { Text = initial, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 90 : 0, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled };
        panel.Children.Add(input);
        panel.Children.Add(Buttons(w, "OK", true));
        w.Content = panel;
        w.Loaded += (_, _) => { input.Focus(); if (input is TextBox tb) tb.SelectAll(); };
        return w.ShowDialog() == true ? (input is PasswordBox pb ? pb.Password : ((TextBox)input).Text) : null;
    }

    /// <summary>Choose several PDF files and put them in the order they should be joined.</summary>
    public static List<string>? MergeList(Window? owner, IEnumerable<string>? start = null)
    {
        var w = Make(owner, "Merge Files", 640);
        var files = new System.Collections.ObjectModel.ObservableCollection<string>(start ?? []);
        var panel = new DockPanel { Margin = new Thickness(20) };
        var info = new TextBlock { Text = "Add the files to join, then put them in order. They are joined in the order shown.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(info, Dock.Top); panel.Children.Add(info);
        var side = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(side, Dock.Right);
        var list = new ListBox { ItemsSource = files, Height = 260 };
        Button B(string text, Action act) { var b = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 6), MinWidth = 110 }; b.Click += (_, _) => act(); return b; }
        side.Children.Add(B("Add Files...", () =>
        {
            var d = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf|All files|*.*", Multiselect = true, Title = "Choose files to merge" };
            if (d.ShowDialog(w) == true) foreach (var f in d.FileNames) files.Add(f);
        }));
        side.Children.Add(B("Move Up", () => { var i = list.SelectedIndex; if (i > 0) { files.Move(i, i - 1); list.SelectedIndex = i - 1; } }));
        side.Children.Add(B("Move Down", () => { var i = list.SelectedIndex; if (i >= 0 && i < files.Count - 1) { files.Move(i, i + 1); list.SelectedIndex = i + 1; } }));
        side.Children.Add(B("Remove", () => { var i = list.SelectedIndex; if (i >= 0) files.RemoveAt(i); }));
        panel.Children.Add(side);
        var buttons = Buttons(w, "Merge...", true, () =>
        {
            if (files.Count < 2) { Info(w, "Merge Files", "Add at least two files to merge."); return false; }
            return true;
        });
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(list);
        w.Content = panel;
        return w.ShowDialog() == true ? files.ToList() : null;
    }

    public enum SplitMode { EveryN, Ranges, Single }

    public sealed record SplitChoice(SplitMode Mode, int EveryN, string Ranges);

    public static SplitChoice? Split(Window? owner, int pageCount)
    {
        var w = Make(owner, "Split Document", 500);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"This document has {pageCount} page{(pageCount == 1 ? "" : "s")}. How should it be split into separate files?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var every = new RadioButton { Content = "Every", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var n = new TextBox { Text = "1", Width = 56, Margin = new Thickness(8, 0, 8, 0) };
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row1.Children.Add(every); row1.Children.Add(n); row1.Children.Add(new TextBlock { Text = "page(s)", VerticalAlignment = VerticalAlignment.Center });
        var ranges = new RadioButton { Content = "These page ranges, one file each (for example 1-3, 4-6, 7-):" };
        var rangeBox = new TextBox { Margin = new Thickness(22, 4, 0, 8), IsEnabled = false };
        var single = new RadioButton { Content = "One file per page" };
        every.Checked += (_, _) => rangeBox.IsEnabled = false;
        ranges.Checked += (_, _) => { rangeBox.IsEnabled = true; rangeBox.Focus(); };
        single.Checked += (_, _) => rangeBox.IsEnabled = false;
        panel.Children.Add(row1); panel.Children.Add(ranges); panel.Children.Add(rangeBox); panel.Children.Add(single);
        SplitChoice? result = null;
        panel.Children.Add(Buttons(w, "Split...", true, () =>
        {
            if (ranges.IsChecked == true)
            {
                try { Pagjoro.Core.Pdf.PageRanges.ParseGroups(rangeBox.Text, pageCount); }
                catch (FormatException ex) { Info(w, "Split Document", ex.Message); return false; }
                result = new SplitChoice(SplitMode.Ranges, 0, rangeBox.Text);
            }
            else if (single.IsChecked == true) result = new SplitChoice(SplitMode.Single, 1, "");
            else
            {
                if (!int.TryParse(n.Text, out var k) || k < 1) { Info(w, "Split Document", "Type a whole number of pages, such as 5."); return false; }
                result = new SplitChoice(SplitMode.EveryN, k, "");
            }
            return true;
        }));
        w.Content = panel;
        return w.ShowDialog() == true ? result : null;
    }

    /// <summary>Margins to trim from each side, in points.</summary>
    public static (double Left, double Top, double Right, double Bottom)? Margins(Window? owner)
    {
        var w = Make(owner, "Crop Pages", 380);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "How much should be trimmed from each side? (1 inch is 72 points.)", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var boxes = new List<TextBox>();
        var i = 0;
        foreach (var label in new[] { "Left", "Top", "Right", "Bottom" })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = label, Margin = new Thickness(0, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(t, i);
            var b = new TextBox { Text = "36", Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(b, i); Grid.SetColumn(b, 1);
            grid.Children.Add(t); grid.Children.Add(b); boxes.Add(b); i++;
        }
        panel.Children.Add(grid);
        (double, double, double, double)? result = null;
        panel.Children.Add(Buttons(w, "Crop", true, () =>
        {
            var v = new double[4];
            for (var k = 0; k < 4; k++)
                if (!double.TryParse(boxes[k].Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out v[k]) || v[k] < 0)
                { Info(w, "Crop Pages", "Type each margin as a number of points, such as 36."); return false; }
            result = (v[0], v[1], v[2], v[3]);
            return true;
        }));
        w.Content = panel;
        return w.ShowDialog() == true ? result : null;
    }
}
