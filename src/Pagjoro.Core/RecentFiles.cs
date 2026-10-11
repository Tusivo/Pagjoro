// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace Pagjoro.Core;

/// <summary>The list of recently opened files, kept in the person's own application-data folder. Nothing in it leaves the computer.</summary>
public sealed class RecentFiles
{
    readonly string file;
    readonly int max;
    List<string> items = [];

    public RecentFiles(string? folder = null, int max = 10)
    {
        this.max = max;
        var dir = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pagjoro");
        file = Path.Combine(dir, "recent.json");
        try
        {
            if (File.Exists(file)) items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { items = []; }
    }

    /// <summary>Newest first; files that no longer exist are left out.</summary>
    public IReadOnlyList<string> Items => items.Where(File.Exists).ToList();

    public void Add(string path)
    {
        var full = Path.GetFullPath(path);
        items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, full);
        if (items.Count > max) items.RemoveRange(max, items.Count - max);
        Save();
    }

    public void Clear() { items.Clear(); Save(); }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(items));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a list of recent files is a convenience, not worth an error */ }
    }
}
