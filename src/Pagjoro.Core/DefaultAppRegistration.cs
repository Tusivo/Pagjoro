// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Pagjoro.Core;

/// <summary>
/// Lets Windows know that Pagjoro can open PDF files, so it appears in the "Default apps" list and in "Open with". Windows does not allow a
/// program to make itself the default (the person has to choose it there), so this only adds the entries that make the choice possible.
/// Everything is written under the person's own account, never machine-wide, and can be removed again.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DefaultAppRegistration
{
    public const string ProgId = "Pagjoro.PDF";
    const string AppKey = "Pagjoro";

    readonly RegistryKey root;

    /// <param name="root">Where the entries are written. The person's account (<c>Registry.CurrentUser</c>) in the program; a scratch key in tests.</param>
    public DefaultAppRegistration(RegistryKey? root = null) => this.root = root ?? Registry.CurrentUser;

    /// <summary>Writes the entries for the program at <paramref name="exePath"/>. Safe to call again (for example after the program was moved).</summary>
    public void Register(string exePath, string version)
    {
        if (!File.Exists(exePath)) throw new FileNotFoundException("Pagjoro could not find its own file.", exePath);
        var open = $"\"{exePath}\" \"%1\"";
        var exeName = Path.GetFileName(exePath);

        // the program, as "Open with" shows it
        using (var app = root.CreateSubKey($@"Software\Classes\Applications\{exeName}"))
        {
            app.SetValue("FriendlyAppName", "Pagjoro");
            using (var cmd = app.CreateSubKey(@"shell\open\command")) cmd.SetValue("", open);
            using (var icon = app.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exePath}\",0");
            using (var types = app.CreateSubKey("SupportedTypes")) types.SetValue(".pdf", "");
        }

        // how a PDF is described and opened
        using (var prog = root.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue("", "PDF document");
            prog.SetValue("FriendlyTypeName", "PDF document (Pagjoro)");
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exePath}\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue("", open);
        }
        using (var with = root.CreateSubKey(@"Software\Classes\.pdf\OpenWithProgids")) with.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

        // what the "Default apps" page lists
        using (var cap = root.CreateSubKey($@"Software\{AppKey}\Capabilities"))
        {
            cap.SetValue("ApplicationName", "Pagjoro");
            cap.SetValue("ApplicationDescription", $"Free PDF tools that run on your own computer (version {version}).");
            using var assoc = cap.CreateSubKey("FileAssociations");
            assoc.SetValue(".pdf", ProgId);
        }
        using (var registered = root.CreateSubKey(@"Software\RegisteredApplications")) registered.SetValue(AppKey, $@"Software\{AppKey}\Capabilities");
    }

    /// <summary>Removes everything <see cref="Register"/> wrote. It does not change which program the person has chosen for PDFs.</summary>
    public void Unregister(string exePath)
    {
        var exeName = Path.GetFileName(exePath);
        using (var registered = root.OpenSubKey(@"Software\RegisteredApplications", writable: true)) registered?.DeleteValue(AppKey, throwOnMissingValue: false);
        root.DeleteSubKeyTree($@"Software\{AppKey}", throwOnMissingSubKey: false);
        root.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        root.DeleteSubKeyTree($@"Software\Classes\Applications\{exeName}", throwOnMissingSubKey: false);
        using (var with = root.OpenSubKey(@"Software\Classes\.pdf\OpenWithProgids", writable: true)) with?.DeleteValue(ProgId, throwOnMissingValue: false);
    }

    /// <summary>True when the entries are there and point at <paramref name="exePath"/>.</summary>
    public bool IsRegistered(string exePath)
    {
        using var cmd = root.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        var value = cmd?.GetValue("") as string;
        return value is not null && value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The program Windows currently opens PDFs with for this person, as the registered name (such as "AcroExch.Document.DC" or "Pagjoro.PDF"), or null.</summary>
    public static string? CurrentDefaultProgId()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice");
        return key?.GetValue("ProgId") as string;
    }

    /// <summary>True when the person has already chosen Pagjoro for PDF files.</summary>
    public static bool IsCurrentDefault() => string.Equals(CurrentDefaultProgId(), ProgId, StringComparison.OrdinalIgnoreCase);
}
