// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using Pagjoro.Core;

namespace Pagjoro.App;

public partial class MainWindow : Window
{
    ReleaseInfo? found;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Name;
        NameText.Text = AppInfo.Name;
        VersionText.Text = "Version " + AppInfo.Version;
        LicenseText.Text = "Licence: " + AppInfo.License;
        StatusText.Text = AppInfo.IsPreRelease
            ? "Pagjoro is in early development. The foundations are in place; the PDF tools arrive phase by phase. It is not ready for real documents yet."
            : "Pagjoro is free software.";
        AboutBox.Text = ReadReadme();
    }

    /// <summary>The README built into the program, as plain text.</summary>
    static string ReadReadme()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("README.md");
        if (stream is null) return "The README is not included in this build.";
        using var reader = new StreamReader(stream);
        return PlainText.FromMarkdown(reader.ReadToEnd());
    }

    async void OnCheck(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false; InstallButton.Visibility = Visibility.Collapsed; found = null;
        UpdateText.Text = "Looking at GitHub …";
        try
        {
            found = await Updater.CheckAsync(AppInfo.Version);
            if (found is null) UpdateText.Text = $"You have the newest version ({AppInfo.Version}).";
            else
            {
                UpdateText.Text = $"Version {found.Version} is available (you have {AppInfo.Version}).";
                InstallButton.Content = "Download and Install " + found.Version;
                InstallButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) { UpdateText.Text = Updater.Friendly(ex); }
        finally { CheckButton.IsEnabled = true; }
    }

    async void OnInstall(object sender, RoutedEventArgs e)
    {
        var release = found;
        var exe = Environment.ProcessPath;
        if (release is null || exe is null) return;
        CheckButton.IsEnabled = false; InstallButton.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(p => UpdateText.Text = $"Downloading Pagjoro {release.Version} … {p}%");
            UpdateText.Text = $"Downloading Pagjoro {release.Version} … 0%";
            var file = await Updater.DownloadAsync(release, exe, progress);
            var backup = Updater.Install(file, exe, AppInfo.Version);
            InstallButton.Visibility = Visibility.Collapsed;
            UpdateText.Text = $"Pagjoro {release.Version} is installed. Your previous version was kept as {Path.GetFileName(backup)} in the same folder.";
            var again = MessageBox.Show(this, $"Version {release.Version} is ready. Close and reopen Pagjoro now to start using it?", AppInfo.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (again == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex) { UpdateText.Text = Updater.Friendly(ex); }
        finally { CheckButton.IsEnabled = true; InstallButton.IsEnabled = true; }
    }

    void OnLink(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show("Windows did not open the link. The address is " + e.Uri.AbsoluteUri, AppInfo.Name); }
        e.Handled = true;
    }
}
