// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;

namespace Pagjoro.App;

/// <summary>A small window with a progress bar and a Cancel button for work that takes a while (text recognition, batch jobs).</summary>
public static class ProgressWindow
{
    /// <summary>
    /// Runs <paramref name="step"/> for each of <paramref name="total"/> items, in order, showing the text it returns. Returns how many steps
    /// completed; fewer than <paramref name="total"/> means it was cancelled or <paramref name="step"/> threw (the exception is rethrown after
    /// the window closes).
    /// </summary>
    public static async Task<int> RunAsync(Window? owner, string title, int total, Func<int, CancellationToken, Task<string>> step)
    {
        var cts = new CancellationTokenSource();
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Height = 18, Margin = new Thickness(0, 10, 0, 0) };
        var label = new TextBlock { Text = "Starting...", TextWrapping = TextWrapping.Wrap };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(18, 5, 18, 5), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(label); panel.Children.Add(bar); panel.Children.Add(cancel);
        var w = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Content = panel,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        if (owner is not null) w.Owner = owner;
        var done = 0;
        Exception? failure = null;
        cancel.Click += (_, _) => { cts.Cancel(); cancel.IsEnabled = false; label.Text = "Stopping after the current item..."; };
        w.Closing += (_, e) => { if (done < total && !cts.IsCancellationRequested) { cts.Cancel(); } };
        if (owner is not null) owner.IsEnabled = false;
        w.Show();
        try
        {
            for (var i = 0; i < total && !cts.IsCancellationRequested; i++)
            {
                try { label.Text = await step(i, cts.Token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { failure = ex; break; }
                done = i + 1; bar.Value = done;
            }
        }
        finally
        {
            if (owner is not null) owner.IsEnabled = true;
            w.Close();
            owner?.Activate();
        }
        if (failure is not null) throw failure;
        return done;
    }
}
