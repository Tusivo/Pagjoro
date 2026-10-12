// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace Pagjoro.Core;

/// <summary>The person's own choices (for now the colour theme), kept in their application-data folder. Nothing in it leaves the computer.</summary>
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
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("theme", out var t) && t.GetString() is { } name)
                    Theme = Normalize(name);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Theme = null; }
    }

    /// <summary>"dark", "light" or "night"; null until the person has chosen (then the app follows Windows).</summary>
    public string? Theme { get; private set; }

    public static string? Normalize(string? name)
    {
        var n = name?.Trim().ToLowerInvariant();
        return n is not null && Themes.Contains(n) ? n : null;
    }

    public void SetTheme(string name)
    {
        Theme = Normalize(name) ?? throw new ArgumentException("Unknown theme.", nameof(name));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(new { theme = Theme }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the choice still applies for this session */ }
    }
}
