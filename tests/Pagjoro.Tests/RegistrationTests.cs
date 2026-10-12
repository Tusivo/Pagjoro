// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Win32;
using Pagjoro.Core;

namespace Pagjoro.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class RegistrationTests
{
    [Fact]
    public void Entries_are_written_found_and_removed_again()
    {
        if (!OperatingSystem.IsWindows()) return;
        var scratchName = @"Software\PagjoroTest-" + Guid.NewGuid().ToString("N");
        using var scratch = Registry.CurrentUser.CreateSubKey(scratchName);
        var exe = Path.Combine(Path.GetTempPath(), "Pagjoro-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(exe, "not a real program");
        try
        {
            var reg = new DefaultAppRegistration(scratch);
            Assert.False(reg.IsRegistered(exe));
            reg.Register(exe, "9.9.9");
            Assert.True(reg.IsRegistered(exe));

            using (var cap = scratch.OpenSubKey(@"Software\Pagjoro\Capabilities\FileAssociations"))
                Assert.Equal(DefaultAppRegistration.ProgId, cap!.GetValue(".pdf"));
            using (var registered = scratch.OpenSubKey(@"Software\RegisteredApplications"))
                Assert.Equal(@"Software\Pagjoro\Capabilities", registered!.GetValue("Pagjoro"));
            using (var cmd = scratch.OpenSubKey(@"Software\Classes\" + DefaultAppRegistration.ProgId + @"\shell\open\command"))
                Assert.Equal($"\"{exe}\" \"%1\"", cmd!.GetValue(""));   // the file to open is handed over as the first argument
            using (var with = scratch.OpenSubKey(@"Software\Classes\.pdf\OpenWithProgids"))
                Assert.Contains(DefaultAppRegistration.ProgId, with!.GetValueNames());

            reg.Register(exe, "9.9.9");   // twice is harmless
            reg.Unregister(exe);
            Assert.False(reg.IsRegistered(exe));
            Assert.Null(scratch.OpenSubKey(@"Software\Pagjoro"));
            using var after = scratch.OpenSubKey(@"Software\Classes\.pdf\OpenWithProgids");
            Assert.DoesNotContain(DefaultAppRegistration.ProgId, after?.GetValueNames() ?? []);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(scratchName, throwOnMissingSubKey: false);
            File.Delete(exe);
        }
    }

    [Fact]
    public void A_missing_program_file_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        var scratchName = @"Software\PagjoroTest-" + Guid.NewGuid().ToString("N");
        using var scratch = Registry.CurrentUser.CreateSubKey(scratchName);
        try { Assert.Throws<FileNotFoundException>(() => new DefaultAppRegistration(scratch).Register(@"C:\no\such\Pagjoro.exe", "1")); }
        finally { Registry.CurrentUser.DeleteSubKeyTree(scratchName, throwOnMissingSubKey: false); }
    }

    [Fact]
    public void Settings_and_recent_files_live_in_the_chosen_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pagjoro-data-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings(folder);
            settings.SetTheme("night");
            var recent = new RecentFiles(folder);
            var file = Path.Combine(folder, "x.pdf"); File.WriteAllText(file, "x");
            recent.Add(file);
            Assert.True(File.Exists(Path.Combine(folder, "settings.json")));
            Assert.True(File.Exists(Path.Combine(folder, "recent.json")));
            Assert.Equal("night", new AppSettings(folder).Theme);
            Assert.Single(new RecentFiles(folder).Items);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_test_run_never_creates_a_data_folder_beside_the_test_host()
    {
        // the portable folder is only used when the program itself is Pagjoro.exe
        var host = Environment.ProcessPath!;
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(host)!, AppPaths.FolderName)));
        Assert.False(AppPaths.IsPortable);
    }
}
