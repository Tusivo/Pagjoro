// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;

namespace Pagjoro.Core;

/// <summary>Turns the project's Markdown (the README shown on the About page) into readable plain text.</summary>
public static partial class PlainText
{
    public static string FromMarkdown(string markdown)
    {
        var sb = new StringBuilder();
        var inCode = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; continue; }
            if (inCode) { sb.AppendLine("    " + line); continue; }
            if (line.TrimStart().StartsWith('|')) continue;                        // tables are not readable as plain text
            if (ImageLine().IsMatch(line)) continue;                              // pictures are for the web page
            line = Heading().Replace(line, "");
            line = Quote().Replace(line, "");
            line = Link().Replace(line, "$1");                                    // keep the words, lose the address
            line = line.Replace("**", "").Replace("`", "");
            line = Bullet().Replace(line, "$1•  ");
            sb.AppendLine(line);
        }
        return BlankRuns().Replace(sb.ToString(), "\n\n").Trim();
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*")] private static partial Regex Heading();
    [GeneratedRegex(@"^\s*>\s?")] private static partial Regex Quote();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")] private static partial Regex Link();
    [GeneratedRegex(@"^(\s*)[-*]\s+")] private static partial Regex Bullet();
    [GeneratedRegex(@"^\s*!\[[^\]]*\]\([^)]*\)\s*$")] private static partial Regex ImageLine();
    [GeneratedRegex("\n{3,}")] private static partial Regex BlankRuns();
}
