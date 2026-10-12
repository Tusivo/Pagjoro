// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Pagjoro.Core;

namespace Pagjoro.App;

public partial class MainWindow
{
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    static void TellWindowsAssociationsChanged() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    void OnDefaultApp(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !File.Exists(exe)) { Dialogs.Info(this, "Default PDF App", "Pagjoro could not find its own file.", error: true); return; }
        var registration = new DefaultAppRegistration();

        var w = new Window
        {
            Title = "Make Pagjoro the Default PDF App", Width = 620, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        var status = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        void Refresh()
        {
            status.Text = DefaultAppRegistration.IsCurrentDefault()
                ? "Pagjoro is already the program that opens your PDF files."
                : registration.IsRegistered(exe)
                    ? "Pagjoro is ready to be chosen. Windows still needs you to pick it (steps below)."
                    : "Pagjoro is not yet offered to Windows as a PDF program.";
        }
        panel.Children.Add(status);
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = "Windows does not let a program make itself the default: you have to choose it yourself. Pressing the button below tells Windows " +
                   "that Pagjoro can open PDF files (for your account only) and opens Windows' Default apps page. Then:",
        });
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 0, 10),
            Text = "1.  On the page that opens, type .pdf in the box \"Set a default for a file type or link type\".\n" +
                   "2.  Click the program shown next to .pdf.\n" +
                   "3.  Choose Pagjoro in the list, then press Set default.",
        });
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 14),
            Text = "Another way: right-click any PDF file in File Explorer, choose Open with, then Choose another app, pick Pagjoro, and tick \"Always use this app\".",
        });
        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 14),
            Text = "If you move Pagjoro.exe to another folder, open this window again so Windows learns the new place. " +
                   "\"Remove Entries\" takes Pagjoro out of the list again.",
        };
        panel.Children.Add(note);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var go = new Button { Content = "Tell Windows and Open Settings", IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        var remove = new Button { Content = "Remove Entries", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        go.Click += (_, _) =>
        {
            try
            {
                registration.Register(exe, AppInfo.Version);
                TellWindowsAssociationsChanged();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Dialogs.Info(w, "Default PDF App", "Windows did not allow Pagjoro to register itself: " + ex.Message + "\n\nUse the \"Open with\" way described in this window instead.", error: true);
                return;
            }
            Refresh();
            OpenUrl("ms-settings:defaultapps?registeredAppUser=Pagjoro");
        };
        remove.Click += (_, _) =>
        {
            try { registration.Unregister(exe); TellWindowsAssociationsChanged(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { Dialogs.Info(w, "Default PDF App", ex.Message, error: true); return; }
            Refresh();
            MessageStatus.Text = "Pagjoro was removed from Windows' list of PDF programs.";
        };
        row.Children.Add(go); row.Children.Add(remove); row.Children.Add(close);
        panel.Children.Add(row);
        w.Content = panel;
        Refresh();
        w.ShowDialog();
    }

    static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
