// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;

namespace Pagjoro.Core;

/// <summary>What the program is called, which version it is, and where its source lives. One place, so the window, the installer and the documents agree.</summary>
public static class AppInfo
{
    public const string Name = "Pagjoro";
    public const string Tagline = "Free, open-source PDF tools that run on your own computer.";
    public const string License = "GNU Affero General Public License 3.0 or later";
    public const string Repository = "https://github.com/Tusivo/Pagjoro";
    public const string Website = "https://tusivo.com/apps/Pagjoro/";
    public const string ContactEmail = "info@tusivo.com";

    /// <summary>The version as written in <c>Directory.Build.props</c>, without the build hash (for example <c>0.1.0-alpha.1</c>).</summary>
    public static string Version
    {
        get
        {
            var attribute = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var text = attribute?.InformationalVersion ?? typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var plus = text.IndexOf('+');
            return plus > 0 ? text[..plus] : text;
        }
    }

    /// <summary>True for versions that are not yet meant for real documents (alpha and beta).</summary>
    public static bool IsPreRelease => Version.Contains('-');
}
