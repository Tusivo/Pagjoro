// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class ProtectionTests
{
    static byte[] SampleBytes() { using var f = PdfFileTests.Sample(2); return f.ToBytes(); }

    [Fact]
    public void A_password_protects_a_file_and_can_be_removed_again()
    {
        var locked = PdfProtection.Encrypt(SampleBytes(), "open sesame", "owner secret", PdfPermissions.All);

        Assert.Throws<PdfPasswordRequiredException>(() => PdfFile.Open(locked));
        var wrong = Assert.Throws<PdfPasswordRequiredException>(() => PdfFile.Open(locked, "not it"));
        Assert.True(wrong.WrongPassword);

        using var opened = PdfFile.Open(locked, "open sesame");
        Assert.True(opened.IsEncrypted);
        Assert.Equal(2, opened.PageCount);
        Assert.Contains("Page 1 of", opened.GetPageText(0));

        // taking the password off: the new file opens with no password
        var plain = opened.ToBytes(removeSecurity: true);
        using var reopened = PdfFile.Open(plain);
        Assert.False(reopened.IsEncrypted);
        Assert.Contains("Page 2 of", reopened.GetPageText(1));
    }

    [Fact]
    public void Permissions_are_written_into_the_file()
    {
        var locked = PdfProtection.Encrypt(SampleBytes(), "", "owner secret", new PdfPermissions(Print: false, Modify: false, Copy: false, Annotate: true, FillForms: true, Assemble: false));
        using var f = PdfFile.Open(locked);       // no open password is needed, only restrictions apply
        Assert.True(f.IsEncrypted);
        var p = f.Permissions;
        Assert.False(p.Print); Assert.False(p.Modify); Assert.False(p.Copy); Assert.False(p.Assemble);
        Assert.True(p.Annotate); Assert.True(p.FillForms);
        Assert.Throws<PdfException>(() => PdfProtection.Encrypt(SampleBytes(), "", "", PdfPermissions.All));   // nothing to protect
    }

    [Fact]
    public void Information_can_be_set_and_sanitizing_removes_it()
    {
        var withInfo = PdfProtection.SetInformation(SampleBytes(), "Annual plan", "Jordan Smith", "Private", "budget, salary");
        using (var f = PdfFile.Open(withInfo))
        {
            var m = f.GetMetadata();
            Assert.Equal("Annual plan", m["Title"]); Assert.Equal("Jordan Smith", m["Author"]);
        }
        var (clean, report) = PdfProtection.Sanitize(withInfo);
        Assert.False(report.NothingFound);
        Assert.Contains(report.Removed, r => r.StartsWith("Document properties"));
        using (var f = PdfFile.Open(clean))
        {
            var m = f.GetMetadata();
            Assert.False(m.ContainsKey("Author")); Assert.False(m.ContainsKey("Title"));
            Assert.Contains("Page 1 of", f.GetPageText(0));          // the content is untouched
        }
    }

    [Fact]
    public void Recompressing_keeps_the_pages()
    {
        var bytes = SampleBytes();
        var smaller = PdfProtection.Recompress(bytes);
        using var f = PdfFile.Open(smaller);
        Assert.Equal(2, f.PageCount);
        Assert.Contains("lazy dog", f.GetPageText(0));
    }

    static X509Certificate2 SelfSigned(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    [Fact]
    public void A_signed_document_checks_out_and_a_change_is_noticed()
    {
        using var cert = SelfSigned("Jordan Smith");
        var signed = PdfSigning.Sign(SampleBytes(), cert, "I approve", "Concord", "jordan@example.org");
        var found = PdfSigning.Inspect(signed);
        Assert.Single(found);
        Assert.Equal("Jordan Smith", found[0].Signer);
        Assert.Equal("I approve", found[0].Reason);
        Assert.True(found[0].Intact);
        Assert.True(found[0].CoversWholeFile);
        Assert.False(found[0].CertificateTrusted);      // a certificate nobody vouched for

        // damage one byte inside the signed part
        var damaged = (byte[])signed.Clone();
        var i = Array.FindIndex(damaged, 600, b => b == (byte)'Q');   // somewhere inside the page content
        Assert.True(i > 0);
        damaged[i] = (byte)'Z';
        Assert.False(PdfSigning.Inspect(damaged)[0].Intact);

        // add something after signing: the signature is still genuine but no longer covers everything
        var extended = signed.Concat(System.Text.Encoding.ASCII.GetBytes("\n% added later\n")).ToArray();
        var ext = PdfSigning.Inspect(extended)[0];
        Assert.True(ext.Intact);
        Assert.False(ext.CoversWholeFile);
    }

    [Fact]
    public void A_visible_signature_can_be_placed_on_a_page()
    {
        using var cert = SelfSigned("Ada Lovelace");
        var signed = PdfSigning.Sign(SampleBytes(), cert, "Reviewed", "", "", 1, new PageRect(300, 40, 560, 100));
        Assert.True(PdfSigning.Inspect(signed)[0].Intact);
        using var f = PdfFile.Open(signed);
        Assert.Equal(2, f.PageCount);
    }

    [Fact]
    public void Page_ranges_are_read_the_way_people_write_them()
    {
        Assert.Equal([0, 1, 2, 4, 7, 8, 9], PageRanges.Parse("1-3, 5, 8-", 10));
        Assert.Equal([2, 0], PageRanges.Parse("3,1", 5));
        Assert.Equal([4, 3, 2], PageRanges.Parse("5-3", 5));
        Assert.Equal("1-3, 5, 8-10", PageRanges.Describe([0, 1, 2, 4, 7, 8, 9]));
        Assert.Throws<FormatException>(() => PageRanges.Parse("11", 10));
        Assert.Throws<FormatException>(() => PageRanges.Parse("abc", 10));
        Assert.Throws<FormatException>(() => PageRanges.Parse("", 10));
        var groups = PageRanges.ParseGroups("1-2, 3-4, 5", 5);
        Assert.Equal(3, groups.Count);
    }
}
