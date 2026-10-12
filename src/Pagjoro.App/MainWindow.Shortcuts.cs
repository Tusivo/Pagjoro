// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;

namespace Pagjoro.App;

public partial class MainWindow
{
    static readonly (string Group, (string Keys, string Does)[] Items)[] ShortcutList =
    [
        ("Files", [("Ctrl+O", "Open a PDF"), ("Ctrl+S", "Save"), ("Ctrl+Shift+S", "Save As"), ("Ctrl+P", "Print")]),
        ("Editing", [("Ctrl+Z", "Undo"), ("Ctrl+Y", "Redo"), ("Ctrl+C", "Copy the selected text"), ("Delete", "Remove the chosen mark"), ("Esc", "Back to the Select tool")]),
        ("Finding", [("Ctrl+F", "Find"), ("F3", "Next match"), ("Shift+F3", "Previous match")]),
        ("Zoom", [("Ctrl+plus", "Zoom in"), ("Ctrl+minus", "Zoom out"), ("Ctrl+0", "Fit the whole page"), ("Ctrl+1", "Actual size"), ("Ctrl+2", "Fit the page width"), ("Ctrl+mouse wheel", "Zoom in or out")]),
        ("Moving around", [("Page Up / Page Down", "Previous / next page"), ("Ctrl+Home / Ctrl+End", "First / last page"), ("Ctrl+G", "Go to a page"),
            ("Middle button", "Press and move the mouse to glide that way, faster the further you move. Click once to keep gliding, click again to stop.")]),
        ("Turning the view", [("Ctrl+Shift+R", "Rotate the view clockwise"), ("Ctrl+Shift+L", "Rotate the view counterclockwise")]),
    ];

    void OnShortcuts(object sender, RoutedEventArgs e)
    {
        var w = new Window
        {
            Title = "Keyboard Shortcuts", Width = 640, Height = 760, MinWidth = 480, MinHeight = 360, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var dock = new DockPanel { Margin = new Thickness(24, 20, 24, 16) };
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, Padding = new Thickness(18, 5, 18, 5), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom); dock.Children.Add(close);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        foreach (var (group, items) in ShortcutList)
        {
            panel.Children.Add(new TextBlock { Text = group, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 16, 0, 6) });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < items.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var keys = new Border
                {
                    Background = System.Windows.Media.Brushes.Transparent, BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 2, 12, 2), HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = items[i].Keys, FontFamily = new System.Windows.Media.FontFamily("Segoe UI") },
                };
                Grid.SetRow(keys, i); grid.Children.Add(keys);
                var does = new TextBlock { Text = items[i].Does, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(does, i); Grid.SetColumn(does, 1); grid.Children.Add(does);
            }
            panel.Children.Add(grid);
        }
        dock.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        w.Content = dock;
        w.ShowDialog();
    }
}
