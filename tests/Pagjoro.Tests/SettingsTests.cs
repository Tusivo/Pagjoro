// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core;

namespace Pagjoro.Tests;

public class SettingsTests
{
    static string TempDir() => Path.Combine(Path.GetTempPath(), "pagjoro-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_theme_choice_is_remembered_and_unknown_values_are_ignored()
    {
        var dir = TempDir();
        try
        {
            Assert.Null(new AppSettings(dir).Theme);
            new AppSettings(dir).SetTheme("night");
            Assert.Equal("night", new AppSettings(dir).Theme);
            Assert.Throws<ArgumentException>(() => new AppSettings(dir).SetTheme("neon"));
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"theme\":\"neon\"}");
            Assert.Null(new AppSettings(dir).Theme);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "not json at all");
            Assert.Null(new AppSettings(dir).Theme);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
