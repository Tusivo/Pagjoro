// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Pagjoro.Core;

/// <summary>
/// Where Pagjoro keeps its own files (settings, the recent files list, the safety copy of unsaved work). Like Ledjoro, it uses a folder called
/// <c>Pagjoro-Data</c> next to <c>Pagjoro.exe</c>, so the program and its settings can be copied together. When that folder cannot be written
/// (a program installed in a protected folder, a read-only drive) the person's own application-data folder is used instead.
/// </summary>
public static class AppPaths
{
    public const string FolderName = "Pagjoro-Data";

    static string? resolved;

    /// <summary>The folder Pagjoro keeps its files in. It exists and is writable when this returns.</summary>
    public static string DataFolder => resolved ??= Resolve();

    /// <summary>True when <see cref="DataFolder"/> is the one beside the program.</summary>
    public static bool IsPortable => string.Equals(Path.GetFileName(DataFolder), FolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>For tests: choose the folder directly.</summary>
    internal static void UseFolder(string? folder) => resolved = folder;

    /// <summary>The folder where earlier versions kept their files.</summary>
    public static string LegacyFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pagjoro");

    static string Resolve()
    {
        var exe = Environment.ProcessPath;
        // only the program itself uses a folder beside it; a test run or a development tool must not scatter folders next to dotnet.exe
        if (exe is not null && string.Equals(Path.GetFileNameWithoutExtension(exe), "Pagjoro", StringComparison.OrdinalIgnoreCase))
        {
            var beside = Path.Combine(Path.GetDirectoryName(exe)!, FolderName);
            if (TryPrepare(beside))
            {
                MigrateFromLegacy(beside);
                return beside;
            }
        }
        var legacy = LegacyFolder;
        TryPrepare(legacy);
        return legacy;
    }

    static bool TryPrepare(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    /// <summary>The first time the portable folder is used, bring over the settings and the recent files of earlier versions.</summary>
    static void MigrateFromLegacy(string target)
    {
        try
        {
            var legacy = LegacyFolder;
            if (!Directory.Exists(legacy)) return;
            foreach (var name in new[] { "settings.json", "recent.json" })
            {
                var from = Path.Combine(legacy, name); var to = Path.Combine(target, name);
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* starting fresh is fine */ }
    }
}
