// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace Pagjoro.Core;

/// <summary>Where a person was in a document: the page, the zoom and how the pages were shown.</summary>
public sealed record ViewState(int Page, double Zoom, int Rotation = 0, bool Facing = false);

/// <summary>
/// Remembers, for the last documents opened, the page and zoom they were left at, so reopening a file picks up where it was left. Kept in the
/// program's data folder; only file paths and numbers are stored, never anything from inside a document.
/// </summary>
public sealed class ViewMemory
{
    const int MaxEntries = 100;
    readonly string file;
    readonly List<(string Path, ViewState State)> items = [];

    public ViewMemory(string? folder = null)
    {
        file = Path.Combine(folder ?? AppPaths.DataFolder, "view-state.json");
        try
        {
            if (!File.Exists(file)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var path = e.GetProperty("path").GetString();
                if (string.IsNullOrEmpty(path)) continue;
                items.Add((path, new ViewState(
                    e.GetProperty("page").GetInt32(), e.GetProperty("zoom").GetDouble(),
                    e.TryGetProperty("rotation", out var r) ? r.GetInt32() : 0, e.TryGetProperty("facing", out var f) && f.GetBoolean())));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            items.Clear();
        }
    }

    static string Key(string path) => Path.GetFullPath(path);

    public ViewState? Get(string path)
    {
        var key = Key(path);
        foreach (var (p, s) in items) if (string.Equals(p, key, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    public void Remember(string path, ViewState state)
    {
        if (state.Page < 0 || double.IsNaN(state.Zoom) || state.Zoom < 0.05 || state.Zoom > 10) return;
        var key = Key(path);
        items.RemoveAll(i => string.Equals(i.Path, key, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, (key, state with { Rotation = state.Rotation & 3 }));
        if (items.Count > MaxEntries) items.RemoveRange(MaxEntries, items.Count - MaxEntries);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(items.Select(i => new
            {
                path = i.Path, page = i.State.Page, zoom = Math.Round(i.State.Zoom, 3), rotation = i.State.Rotation, facing = i.State.Facing,
            })));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* losing a remembered page is not worth an error */ }
    }
}
