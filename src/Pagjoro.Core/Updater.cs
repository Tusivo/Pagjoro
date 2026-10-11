// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pagjoro.Core;

/// <summary>A published release that carries a program file and its signature file.</summary>
public sealed record ReleaseInfo(string Tag, string Version, string Title, string Notes, string Page, string AssetName, string AssetUrl, string SigUrl, long Size, string Sha256);

/// <summary>A plain-language reason an update was not offered or not installed.</summary>
public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Looks for a newer Pagjoro among this project's GitHub releases (only when the person asks), downloads it, checks that it was signed with the
/// release key built into this program, keeps the running program as a backup in the same folder and puts the new one in its place.
/// A release without a signature file is never offered, and a download whose signature does not verify is thrown away.
/// </summary>
public static partial class Updater
{
    public const string Repo = "Tusivo/Pagjoro";
    const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    // ---- version order (0.1.0-alpha.1 < 0.1.0-beta.1 < 0.1.0 < 0.10.0) --------------------------------------------------

    public static string Clean(string? version)
    {
        var v = (version ?? "").Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    public static bool IsValid(string? version) => VersionPattern().IsMatch(Clean(version));

    public static int Compare(string a, string b)
    {
        a = Clean(a); b = Clean(b);
        string pa = "", pb = "";
        var i = a.IndexOf('-'); var j = b.IndexOf('-');
        if (i >= 0) { pa = a[(i + 1)..]; a = a[..i]; }
        if (j >= 0) { pb = b[(j + 1)..]; b = b[..j]; }
        var na = a.Split('.').Select(x => long.TryParse(x, out var n) ? n : 0).ToArray();
        var nb = b.Split('.').Select(x => long.TryParse(x, out var n) ? n : 0).ToArray();
        for (var k = 0; k < Math.Max(na.Length, nb.Length); k++)
        {
            long x = k < na.Length ? na[k] : 0, y = k < nb.Length ? nb[k] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        if (pa == pb) return 0;
        if (pa.Length == 0) return 1;      // a release beats its own pre-releases
        if (pb.Length == 0) return -1;
        var ia = pa.Split('.'); var ib = pb.Split('.');
        for (var k = 0; k < Math.Max(ia.Length, ib.Length); k++)
        {
            if (k >= ia.Length) return -1;
            if (k >= ib.Length) return 1;
            var nx = long.TryParse(ia[k], out var x); var ny = long.TryParse(ib[k], out var y);
            var c = nx && ny ? x.CompareTo(y) : nx ? -1 : ny ? 1 : string.CompareOrdinal(ia[k], ib[k]);
            if (c != 0) return c < 0 ? -1 : 1;
        }
        return 0;
    }

    // ---- the release list -------------------------------------------------------------------------------------------

    /// <summary>The newest published release that has a Pagjoro .exe and a matching .sig file attached (drafts and files from elsewhere are ignored).</summary>
    public static ReleaseInfo? Pick(string releasesJson)
    {
        using var doc = JsonDocument.Parse(releasesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        ReleaseInfo? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            var tag = Str(r, "tag_name");
            if (!IsValid(tag) || !r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;

            JsonElement? exe = null, sig = null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = Str(a, "name"); var url = Str(a, "browser_download_url");
                if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                if (name.StartsWith("Pagjoro", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && (exe is null || name.Equals("Pagjoro.exe", StringComparison.OrdinalIgnoreCase))) exe = a;
            }
            if (exe is null) continue;
            var sigName = Str(exe.Value, "name") + ".sig";
            foreach (var a in assets.EnumerateArray())
            {
                var url = Str(a, "browser_download_url");
                if (Str(a, "name").Equals(sigName, StringComparison.OrdinalIgnoreCase) && url.StartsWith(DownloadPrefix, StringComparison.Ordinal)
                    && a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) && n is > 0 and < 16384) sig = a;
            }
            if (sig is null) continue;

            var size = exe.Value.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;
            var digest = Str(exe.Value, "digest");
            var info = new ReleaseInfo(tag, Clean(tag), Str(r, "name") is { Length: > 0 } t ? t : tag, Str(r, "body"), Str(r, "html_url"),
                Str(exe.Value, "name"), Str(exe.Value, "browser_download_url"), Str(sig.Value, "browser_download_url"), size,
                digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : "");
            if (best is null || Compare(info.Version, best.Version) > 0) best = info;
        }
        return best;
    }

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // ---- the release signature --------------------------------------------------------------------------------------

    /// <summary>The release signing public key built into this program (64 bytes: X then Y of an ECDSA P-256 key), or null when this build has none.</summary>
    public static byte[]? PublicKey()
    {
        try
        {
            using var stream = typeof(Updater).Assembly.GetManifestResourceStream("release-key.txt");
            if (stream is null) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var base64 = string.Concat(reader.ReadToEnd().Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0 && !l.StartsWith('#')));
            var raw = Convert.FromBase64String(base64);
            return raw.Length == 64 ? raw : null;
        }
        catch (Exception) { return null; }
    }

    public static byte[] SignedMessage(string version, string sha256) => Encoding.UTF8.GetBytes($"pagjoro-release-v1\n{Clean(version)}\n{sha256}\n");

    /// <summary>
    /// Passes silently when <paramref name="signatureFile"/> is a signature, made with the release key, for exactly this version and this file (by SHA-256).
    /// Throws <see cref="UpdateException"/> saying why when it is not. The version is part of what was signed, so an old signed program cannot be passed off as a new one.
    /// </summary>
    public static void VerifySignature(string signatureFile, string sha256, string version, byte[]? publicKey)
    {
        if (publicKey is null || publicKey.Length != 64)
            throw new UpdateException("This copy of Pagjoro does not carry the release signing key, so it cannot check that an update is genuine. Download the newest Pagjoro.exe from the releases page instead.");
        JsonElement j;
        try { j = JsonDocument.Parse(signatureFile).RootElement; }
        catch (JsonException) { throw new UpdateException("The update's signature file could not be read, so the update was thrown away."); }
        if (j.ValueKind != JsonValueKind.Object || !j.TryGetProperty("format", out var f) || !f.TryGetInt32(out var format) || format != 1)
            throw new UpdateException("The update's signature is in a format this version of Pagjoro does not know. Download the newest Pagjoro.exe from the releases page instead.");
        if (!string.Equals(Str(j, "version"), Clean(version), StringComparison.Ordinal))
            throw new UpdateException("The update's signature is for a different version, so the update was thrown away.");
        if (!string.Equals(Str(j, "sha256").ToLowerInvariant(), sha256, StringComparison.Ordinal))
            throw new UpdateException("The update's signature is for a different file than the one downloaded, so the update was thrown away.");
        byte[] signature;
        try { signature = Convert.FromBase64String(Str(j, "signature")); } catch (FormatException) { signature = []; }
        var ok = false;
        if (signature.Length == 64)
        {
            try
            {
                var parameters = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publicKey[..32], Y = publicKey[32..] } };
                using var ec = ECDsa.Create(parameters);
                ok = ec.VerifyData(SignedMessage(version, sha256), signature, HashAlgorithmName.SHA256);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or PlatformNotSupportedException) { ok = false; }   // a key that is not a point on the curve is just "not signed"
        }
        if (!ok) throw new UpdateException("The update is not signed with Pagjoro's release key, so it was thrown away. If you reached this by checking for updates on a normal release, please report it on the project's issues page.");
    }

    // ---- check, download, install -----------------------------------------------------------------------------------

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static HttpClient NewClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Pagjoro/" + AppInfo.Version.Replace(' ', '-'));
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>The newest signed release when it is newer than <paramref name="current"/>, otherwise null (this is already the newest).</summary>
    public static async Task<ReleaseInfo?> CheckAsync(string current, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        if (PublicKey() is null)
            throw new UpdateException("This copy of Pagjoro does not carry the release signing key, so it cannot check that an update is genuine. Download the newest Pagjoro.exe from the releases page instead.");
        using var client = handler is null ? NewClient(TimeSpan.FromSeconds(20)) : new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Pagjoro/" + AppInfo.Version);
        var response = await client.GetAsync($"https://api.github.com/repos/{Repo}/releases?per_page=30", cancel);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new UpdateException("No releases have been published for Pagjoro yet.");
        if (!response.IsSuccessStatusCode) throw new UpdateException($"GitHub answered with an error ({(int)response.StatusCode}). Try again in a few minutes.");
        var best = Pick(await response.Content.ReadAsStringAsync(cancel));
        if (best is null) throw new UpdateException("No signed release of Pagjoro was found. Download the newest Pagjoro.exe from the releases page instead.");
        return Compare(best.Version, current) > 0 ? best : null;
    }

    /// <summary>Checks the size and the Windows program header of a downloaded file. Throws <see cref="UpdateException"/> when anything is off.</summary>
    public static void VerifyFile(string path, ReleaseInfo release)
    {
        var info = new FileInfo(path);
        if (release.Size > 0 && info.Length != release.Size) throw new UpdateException($"The download is incomplete ({info.Length} of {release.Size} bytes).");
        if (info.Length < 1024 * 1024) throw new UpdateException("The downloaded file is too small to be Pagjoro.");
        Span<byte> head = stackalloc byte[2];
        using (var f = File.OpenRead(path)) f.ReadExactly(head);
        if (head[0] != (byte)'M' || head[1] != (byte)'Z') throw new UpdateException("The downloaded file is not a Windows program.");
    }

    /// <summary>Downloads the release next to the running program (same drive, so the swap is a rename), then verifies size, header and signature. Returns the file.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, string exePath, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        if (!release.AssetUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !release.SigUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal))
            throw new UpdateException("The update is not hosted where Pagjoro expects it.");
        var tmp = Path.Combine(Path.GetDirectoryName(exePath)!, "Pagjoro.update.download");
        try
        {
            using (var client = NewClient(TimeSpan.FromMinutes(10)))
            {
                using var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
                if (!response.IsSuccessStatusCode) throw new UpdateException($"GitHub would not send the file ({(int)response.StatusCode}).");
                var total = response.Content.Headers.ContentLength ?? release.Size;
                long got = 0; var last = -1;
                await using var src = await response.Content.ReadAsStreamAsync(cancel);
                await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920]; int n;
                while ((n = await src.ReadAsync(buffer, cancel)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), cancel); got += n;
                    var pct = total > 0 ? (int)(got * 100 / total) : 0;
                    if (pct != last) { last = pct; progress?.Report(pct); }
                }
            }
            VerifyFile(tmp, release);
            string signatureFile;
            using (var client = NewClient(TimeSpan.FromSeconds(60)))
            {
                var response = await client.GetAsync(release.SigUrl, cancel);
                if (!response.IsSuccessStatusCode) throw new UpdateException($"GitHub would not send the update's signature ({(int)response.StatusCode}), so the update was not installed.");
                signatureFile = await response.Content.ReadAsStringAsync(cancel);
            }
            VerifySignature(signatureFile, Sha256Of(tmp), release.Version, PublicKey());
            return tmp;
        }
        catch (Exception)
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>
    /// Keeps the current program as "Pagjoro-&lt;version&gt;-backup.exe" in the same folder and puts the new file in its place. A running program can be renamed
    /// (not overwritten), which is what makes this possible. If anything fails the old one is put back. Returns the backup's path.
    /// </summary>
    public static string Install(string newFile, string exePath, string currentVersion)
    {
        var dir = Path.GetDirectoryName(exePath)!;
        var backup = Path.Combine(dir, $"Pagjoro-{Clean(currentVersion)}-backup.exe");
        for (var n = 2; File.Exists(backup); n++) backup = Path.Combine(dir, $"Pagjoro-{Clean(currentVersion)}-backup ({n}).exe");
        try { File.Move(exePath, backup); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new UpdateException($"Pagjoro could not make a backup of itself in {dir} ({ex.Message}). Nothing was changed."); }
        try { File.Move(newFile, exePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (!File.Exists(exePath)) File.Move(backup, exePath); } catch (IOException) { }
            throw new UpdateException($"Pagjoro could not put the new version in place ({ex.Message}). The old version was left as it was.");
        }
        return backup;
    }

    /// <summary>A sentence for the window.</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        UpdateException => ex.Message,
        TaskCanceledException => "GitHub did not answer in time. Check your internet connection and try again.",
        HttpRequestException => "Pagjoro could not reach GitHub. Check your internet connection and try again.",
        _ => "Something went wrong while checking for updates: " + ex.Message,
    };

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$")] private static partial Regex VersionPattern();
}
