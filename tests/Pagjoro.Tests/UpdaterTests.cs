// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core;

namespace Pagjoro.Tests;

public class UpdaterTests
{
    // A signature made by scripts/release-sign.js (in Node) with a throwaway key. The program (in .NET) must accept exactly this and nothing else.
    const string PublicKey = "Ux++bB6zA1pSKCQhVWgLKnTpDHd9CJ7jn2Q8E0q3sNWQG7oYUkRAi7rq+ZyUp7H3DaXE6dOJeGUi10k7m/SZMg==";
    const string Sha = "3a311faa59b0bae41cb73dfbe2aaa1c140938d76f7e825f73321923bea681dd9";
    const string Signature = """{"format":1,"version":"7.7.7-test.1","sha256":"3a311faa59b0bae41cb73dfbe2aaa1c140938d76f7e825f73321923bea681dd9","signature":"nkHZq8/cNkjtKiUFw84UD+Q7aFVjEwz6q4v//Bn3JdhH0VLYIRVKwcKTUXY8GdUmqtHv+XYq5smIHJkUY1cHVg=="}""";

    static readonly byte[] Key = Convert.FromBase64String(PublicKey);

    [Fact]
    public void Versions_sort_the_way_people_expect()
    {
        Assert.True(Updater.Compare("0.1.0-alpha.1", "v0.1.0-alpha.2") < 0);
        Assert.True(Updater.Compare("0.1.0-alpha.2", "0.1.0-beta.1") < 0);
        Assert.True(Updater.Compare("0.1.0-beta.1", "0.1.0") < 0);
        Assert.True(Updater.Compare("0.1.0", "0.10.0") < 0);
        Assert.Equal(0, Updater.Compare("v1.0.0", "1.0.0"));
        Assert.True(Updater.Compare("0.1.0-alpha.10", "0.1.0-alpha.9") > 0);
    }

    [Fact]
    public void A_signature_made_in_Node_verifies_in_dotnet()
    {
        Updater.VerifySignature(Signature, Sha, "v7.7.7-test.1", Key);
    }

    [Theory]
    [InlineData("other key")]
    [InlineData("other version")]
    [InlineData("other file")]
    [InlineData("damaged signature")]
    [InlineData("unreadable")]
    [InlineData("no key")]
    public void Anything_else_is_refused(string what)
    {
        var key = Key; var sha = Sha; var version = "7.7.7-test.1"; var sig = Signature;
        switch (what)
        {
            case "other key": key = (byte[])Key.Clone(); key[5] ^= 1; break;
            case "other version": version = "7.7.8-test.1"; break;
            case "other file": sha = Sha.Replace('3', '4'); break;
            case "damaged signature": sig = Signature.Replace("nkHZ", "nkHY"); break;
            case "unreadable": sig = "{broken"; break;
            case "no key": Check(null); return;
        }
        Check(key);

        void Check(byte[]? k) => Assert.Throws<UpdateException>(() => Updater.VerifySignature(sig, sha, version, k));
    }

    const string Base = "https://github.com/Tusivo/Pagjoro/releases/download/";

    static string Asset(string tag, string name, long size = 5_000_000) =>
        $$"""{"name":"{{name}}","size":{{size}},"browser_download_url":"{{Base}}{{tag}}/{{name}}"}""";

    [Fact]
    public void The_newest_signed_release_is_picked()
    {
        var json = "[" + string.Join(",",
            // oldest, signed
            $$"""{"tag_name":"v0.1.0-alpha.1","draft":false,"name":"a","assets":[{{Asset("v0.1.0-alpha.1", "Pagjoro.exe")}},{{Asset("v0.1.0-alpha.1", "Pagjoro.exe.sig", 300)}}]}""",
            // newer, signed, with a digest and notes
            $$"""{"tag_name":"v0.1.0-alpha.2","draft":false,"name":"b","body":"notes","assets":[{"name":"Pagjoro.exe","size":6000000,"digest":"sha256:ABC","browser_download_url":"{{Base}}v0.1.0-alpha.2/Pagjoro.exe"},{{Asset("v0.1.0-alpha.2", "Pagjoro.exe.sig", 300)}}]}""",
            // newest but unsigned: never offered
            $$"""{"tag_name":"v5.0.0","draft":false,"assets":[{{Asset("v5.0.0", "Pagjoro.exe")}}]}""",
            // a draft
            $$"""{"tag_name":"v9.9.9","draft":true,"assets":[{{Asset("v9.9.9", "Pagjoro.exe")}},{{Asset("v9.9.9", "Pagjoro.exe.sig", 300)}}]}""",
            // files from somewhere else
            """{"tag_name":"v8.0.0","draft":false,"assets":[{"name":"Pagjoro.exe","size":1,"browser_download_url":"https://evil.example/Pagjoro.exe"},{"name":"Pagjoro.exe.sig","size":1,"browser_download_url":"https://evil.example/Pagjoro.exe.sig"}]}""")
            + "]";

        var pick = Updater.Pick(json);
        Assert.NotNull(pick);
        Assert.Equal("0.1.0-alpha.2", pick!.Version);
        Assert.Equal("abc", pick.Sha256);
        Assert.Equal("notes", pick.Notes);
        Assert.EndsWith("v0.1.0-alpha.2/Pagjoro.exe.sig", pick.SigUrl);
    }

    [Fact]
    public void Install_keeps_a_backup_and_never_overwrites_an_earlier_one()
    {
        var dir = Directory.CreateTempSubdirectory("pagjoro-upd-").FullName;
        try
        {
            var exe = Path.Combine(dir, "Pagjoro.exe"); var download = Path.Combine(dir, "Pagjoro.update.download");
            File.WriteAllBytes(exe, [1, 2, 3]);
            File.WriteAllBytes(download, new byte[10]);
            var backup = Updater.Install(download, exe, "0.1.0-alpha.1");
            Assert.Equal("Pagjoro-0.1.0-alpha.1-backup.exe", Path.GetFileName(backup));
            Assert.Equal(3, File.ReadAllBytes(backup).Length);
            Assert.Equal(10, File.ReadAllBytes(exe).Length);
            Assert.False(File.Exists(download));

            File.WriteAllBytes(download, new byte[20]);
            var second = Updater.Install(download, exe, "0.1.0-alpha.1");
            Assert.Equal("Pagjoro-0.1.0-alpha.1-backup (2).exe", Path.GetFileName(second));
            Assert.True(File.Exists(backup));

            // a missing new file leaves the program where it was
            Assert.Throws<UpdateException>(() => Updater.Install(Path.Combine(dir, "missing.download"), exe, "0.1.1"));
            Assert.True(File.Exists(exe));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_file_that_is_too_small_or_not_a_program_is_refused()
    {
        var dir = Directory.CreateTempSubdirectory("pagjoro-file-").FullName;
        try
        {
            var f = Path.Combine(dir, "x.download");
            var release = new ReleaseInfo("v1", "1.0.0", "t", "", "", "Pagjoro.exe", Base + "v1/Pagjoro.exe", Base + "v1/Pagjoro.exe.sig", 0, "");
            File.WriteAllBytes(f, new byte[100]);
            Assert.Throws<UpdateException>(() => Updater.VerifyFile(f, release));

            var big = new byte[1_100_000]; big[0] = (byte)'M'; big[1] = (byte)'Z';
            File.WriteAllBytes(f, big);
            Updater.VerifyFile(f, release);
            Assert.Throws<UpdateException>(() => Updater.VerifyFile(f, release with { Size = big.Length + 1 }));

            big[0] = (byte)'X';
            File.WriteAllBytes(f, big);
            Assert.Throws<UpdateException>(() => Updater.VerifyFile(f, release));
        }
        finally { Directory.Delete(dir, true); }
    }
}
