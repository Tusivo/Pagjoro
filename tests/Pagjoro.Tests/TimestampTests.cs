// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class TimestampTests
{
    [Fact]
    public void The_request_carries_a_sha256_fingerprint_and_the_reply_is_unpacked()
    {
        var signature = new byte[] { 1, 2, 3, 4, 5 };
        var request = Timestamp.BuildRequest(signature, [9, 9, 9, 9]);
        var r = new AsnReader(request, AsnEncodingRules.DER).ReadSequence();
        Assert.Equal(1, (int)r.ReadInteger());
        var imprint = r.ReadSequence();
        imprint.ReadSequence();   // the algorithm
        Assert.Equal(SHA256.HashData(signature), imprint.ReadOctetString());

        // a "granted" reply wrapping a token
        var token = new byte[] { 0x30, 0x03, 0x02, 0x01, 0x07 };
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            using (w.PushSequence()) w.WriteInteger(0);
            w.WriteEncodedValue(token);
        }
        Assert.Equal(token, Timestamp.ParseResponse(w.Encode()));

        // a "rejected" reply
        var bad = new AsnWriter(AsnEncodingRules.DER);
        using (bad.PushSequence()) using (bad.PushSequence()) bad.WriteInteger(2);
        Assert.Throws<PdfException>(() => Timestamp.ParseResponse(bad.Encode()));
        Assert.Throws<PdfException>(() => Timestamp.ParseResponse([1, 2, 3]));
    }

    [Fact]
    public void A_signature_can_carry_a_time_stamp_from_a_service()
    {
        // a stand-in time stamping service on this computer: it answers every request with a "granted" reply and a made-up token
        byte[] token;
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var req = new CertificateRequest("CN=Test TSA", key, HashAlgorithmName.SHA256);
            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var cms = new SignedCms(new ContentInfo([1, 2, 3]));
            cms.ComputeSignature(new CmsSigner(cert));
            token = cms.Encode();
        }
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence()) { using (w.PushSequence()) w.WriteInteger(0); w.WriteEncodedValue(token); }
        var reply = w.Encode();

        var port = Random.Shared.Next(20000, 40000);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/tsa/");
        listener.Start();
        var served = Task.Run(async () =>
        {
            for (var i = 0; i < 4; i++)
            {
                var ctx = await listener.GetContextAsync();
                ctx.Response.ContentType = "application/timestamp-reply";
                await ctx.Response.OutputStream.WriteAsync(reply);
                ctx.Response.Close();
            }
        });

        try
        {
            using var signer = PdfSigning.CreateSelfSigned("Timestamp Tester");
            byte[] input;
            using (var f = PdfFileTests.Sample(1)) input = f.ToBytes();
            var signed = PdfSigning.Sign(input, signer, "test", "", "", 0, new PageRect(72, 72, 300, 140), $"http://127.0.0.1:{port}/tsa/");
            var info = PdfSigning.Inspect(signed).Single();
            Assert.True(info.Intact);
            Assert.True(info.TimeStamped);
            Assert.Contains("time stamp", info.Summary);

            var plain = PdfSigning.Sign(input, signer, "test", "", "", 0, null, null);
            Assert.False(PdfSigning.Inspect(plain).Single().TimeStamped);
        }
        finally { listener.Stop(); }
        Assert.Throws<PdfException>(() => Timestamp.Request("ftp://example.org/", [1]));
    }
}
