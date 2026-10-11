// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Pagjoro.Core.Pdf;

/// <summary>
/// Gets a trusted time stamp (RFC 3161) for a signature from a time stamping authority the person chooses. Only a SHA-256 fingerprint of the
/// signature is sent, never the document.
/// </summary>
public static class Timestamp
{
    public const string TokenOid = "1.2.840.113549.1.9.16.2.14";
    const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>The request to send to the authority for a signature value.</summary>
    public static byte[] BuildRequest(byte[] signatureValue, byte[]? nonce = null)
    {
        var hash = SHA256.HashData(signatureValue);
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteInteger(1);                                   // version
            using (w.PushSequence())                             // messageImprint
            {
                using (w.PushSequence()) { w.WriteObjectIdentifier(Sha256Oid); w.WriteNull(); }
                w.WriteOctetString(hash);
            }
            w.WriteIntegerUnsigned(nonce ?? RandomNumberGenerator.GetBytes(8));
            w.WriteBoolean(true);                                // certReq: include the authority's certificate
        }
        return w.Encode();
    }

    /// <summary>The time stamp token (a CMS structure) from an authority's reply. Throws when the authority refused.</summary>
    public static byte[] ParseResponse(byte[] response)
    {
        try
        {
            var outer = new AsnReader(response, AsnEncodingRules.DER).ReadSequence();
            var status = outer.ReadSequence();
            var code = status.ReadInteger();
            if (code != 0 && code != 1)
                throw new PdfException("The time stamping service refused the request (status " + code + ").");
            if (!outer.HasData) throw new PdfException("The time stamping service sent no time stamp.");
            return outer.ReadEncodedValue().ToArray();
        }
        catch (AsnContentException) { throw new PdfException("The time stamping service sent a reply Pagjoro could not understand."); }
    }

    /// <summary>Asks the authority at <paramref name="url"/> to time stamp <paramref name="signatureValue"/>.</summary>
    public static byte[] Request(string url, byte[] signatureValue)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new PdfException("The time stamping address must start with http:// or https://.");
        var body = new ByteArrayContent(BuildRequest(signatureValue));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        try
        {
            using var reply = Http.PostAsync(uri, body).GetAwaiter().GetResult();
            if (!reply.IsSuccessStatusCode) throw new PdfException($"The time stamping service answered with an error ({(int)reply.StatusCode}).");
            return ParseResponse(reply.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
        }
        catch (HttpRequestException ex) { throw new PdfException("Pagjoro could not reach the time stamping service: " + ex.Message); }
        catch (TaskCanceledException) { throw new PdfException("The time stamping service did not answer in time."); }
    }
}
