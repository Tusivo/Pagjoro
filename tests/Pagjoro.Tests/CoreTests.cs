// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using Pagjoro.Core;

namespace Pagjoro.Tests;

public class CoreTests
{
    [Fact]
    public void Version_is_a_semantic_version_without_a_build_hash()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", AppInfo.Version);
        Assert.DoesNotContain('+', AppInfo.Version);
    }

    [Fact]
    public void Name_and_links_are_set()
    {
        Assert.Equal("Pagjoro", AppInfo.Name);
        Assert.StartsWith("https://github.com/Tusivo/", AppInfo.Repository);
        Assert.Equal("info@tusivo.com", AppInfo.ContactEmail);
    }

    [Fact]
    public void Markdown_becomes_readable_plain_text()
    {
        var md = "# Title\n\n> **Note:** see [the docs](https://example.org/x).\n\n![pic](a.png)\n*caption*\n\n- one\n- two\n\n| a | b |\n|---|---|\n\n`code`";
        var text = PlainText.FromMarkdown(md);
        Assert.StartsWith("Title", text);
        Assert.Contains("Note: see the docs.", text);
        Assert.DoesNotContain("https://example.org", text);
        Assert.DoesNotContain("a.png", text);
        Assert.DoesNotContain("| a |", text);
        Assert.Contains("•  one", text);
        Assert.Contains("code", text);
    }
}

/// <summary>
/// The clean-room policy (LEGAL.md) says the product must not use another company's names or marks. This test keeps the source code honest:
/// it fails if a trademark we have promised not to use shows up in the program's own files.
/// </summary>
public class PolicyTests
{
    static readonly string[] Forbidden = { "Acrobat", "Adobe", "Sensei" };

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pagjoro.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root (Pagjoro.sln).");
    }

    [Fact]
    public void Program_source_does_not_use_other_companies_marks()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".cs" or ".xaml" or ".csproj" or ".json" or ".resx")) continue;
            var text = File.ReadAllText(file);
            foreach (var word in Forbidden)
                if (Regex.IsMatch(text, @"\b" + word + @"\b", RegexOptions.IgnoreCase)) offenders.Add($"{Path.GetRelativePath(src, file)}: {word}");
        }
        Assert.True(offenders.Count == 0, "These files use a name the clean-room policy forbids: " + string.Join("; ", offenders));
    }

    [Fact]
    public void Policy_documents_exist()
    {
        var root = RepoRoot();
        foreach (var name in new[] { "README.md", "LEGAL.md", "SECURITY.md", "THIRD-PARTY-NOTICES.md" })
            Assert.True(File.Exists(Path.Combine(root, name)), name + " is missing");
    }
}
