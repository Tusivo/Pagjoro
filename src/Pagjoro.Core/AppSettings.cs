// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace Pagjoro.Core;

/// <summary>Where the window was last, so it opens in the same place and size.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>The person's own choices (the colour theme and where the window was), kept in the program's data folder. Nothing in it leaves the computer.</summary>
public sealed class AppSettings
{
    public static readonly IReadOnlyList<string> Themes = ["dark", "light", "night"];

    readonly string file;

    public AppSettings(string? folder = null)
    {
        var dir = folder ?? AppPaths.DataFolder;
        file = Path.Combine(dir, "settings.json");
        try
        {
            if (!File.Exists(file)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String) Theme = Normalize(t.GetString());
            if (root.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.Object)
            {
                var placement = new WindowPlacement(w.GetProperty("left").GetDouble(), w.GetProperty("top").GetDouble(),
                    w.GetProperty("width").GetDouble(), w.GetProperty("height").GetDouble(),
                    w.TryGetProperty("maximized", out var m) && m.ValueKind == JsonValueKind.True);
                if (placement.Width >= 200 && placement.Height >= 150) Window = placement;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Theme = null; Window = null;
        }
    }

    /// <summary>"dark", "light" or "night"; null until the person has chosen (then the app follows Windows).</summary>
    public string? Theme { get; private set; }

    /// <summary>Where the window was when the program was last closed, or null.</summary>
    public WindowPlacement? Window { get; private set; }

    public static string? Normalize(string? name)
    {
        var n = name?.Trim().ToLowerInvariant();
        return n is not null && Themes.Contains(n) ? n : null;
    }

    public void SetTheme(string name)
    {
        Theme = Normalize(name) ?? throw new ArgumentException("Unknown theme.", nameof(name));
        Save();
    }

    public void SetWindow(WindowPlacement placement)
    {
        Window = placement;
        Save();
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                theme = Theme,
                window = Window is null ? null : new { left = Window.Left, top = Window.Top, width = Window.Width, height = Window.Height, maximized = Window.Maximized },
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the choice still applies for this session */ }
    }
}
