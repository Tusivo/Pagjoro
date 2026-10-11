// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PDFiumCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;

namespace Pagjoro.Core.Pdf;

/// <summary>One signature found in a document, and what could be checked about it.</summary>
public sealed record SignatureInfo(int Index, string Signer, string SignedAt, string Reason, bool Intact, bool CoversWholeFile, bool CertificateTrusted, string Summary, bool TimeStamped = false);

/// <summary>Signs documents with a certificate from the person's own store, and checks signatures in documents.</summary>
public static class PdfSigning
{
    // ---- signing ----------------------------------------------------------------------------------------------------

    /// <summary>Certificates the person has a private key for and that are valid now (Windows "Personal" store).</summary>
    public static IReadOnlyList<X509Certificate2> FindSigningCertificates()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var now = DateTime.Now;
        return store.Certificates.Cast<X509Certificate2>().Where(c => c.HasPrivateKey && c.NotBefore <= now && c.NotAfter >= now).OrderBy(c => c.GetNameInfo(X509NameType.SimpleName, false)).ToList();
    }

    /// <summary>
    /// Makes a certificate for signing, valid for several years, and puts it in the person's own Windows certificate store. Nobody else vouches
    /// for it, so people who receive signed files will see "not trusted" unless they choose to trust it; it still proves the file was not
    /// changed after signing and which certificate signed it.
    /// </summary>
    public static X509Certificate2 CreateSelfSigned(string name, string email = "", int years = 5)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new PdfException("Type the name the signature should show.");
        var subject = "CN=" + name.Replace(",", " ").Replace("=", " ").Trim() + (email.Length > 0 ? ", E=" + email.Replace(",", " ") : "");
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var temp = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(Math.Clamp(years, 1, 20)));
        var pfx = temp.Export(X509ContentType.Pfx, "pagjoro");
        var cert = X509CertificateLoader.LoadPkcs12(pfx, "pagjoro", X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        return cert;
    }
    sealed class CertificateSigner(X509Certificate2 certificate, string? timestampUrl = null) : IDigitalSigner
    {
        public string CertificateName => certificate.GetNameInfo(X509NameType.SimpleName, false);

        byte[] Cms(byte[] data, bool stamp = false)
        {
            var content = new ContentInfo(data);
            var signed = new SignedCms(content, detached: true);
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.WholeChain };
            signer.SignedAttributes.Add(new Pkcs9SigningTime());
            signed.ComputeSignature(signer);
            if (stamp && !string.IsNullOrWhiteSpace(timestampUrl))
            {
                var info = signed.SignerInfos[0];
                var token = Timestamp.Request(timestampUrl, info.GetSignature());
                info.AddUnsignedAttribute(new AsnEncodedData(new Oid(Timestamp.TokenOid), token));
            }
            return signed.Encode();
        }

        public Task<int> GetSignatureSizeAsync() => Task.FromResult(Cms(new byte[] { 1 }).Length + 2048 + (string.IsNullOrWhiteSpace(timestampUrl) ? 0 : 12288));

        public async Task<byte[]> GetSignatureAsync(Stream stream)
        {
            // the stream handed over is a window onto the file's signed parts: read it from the start, by hand
            var data = new byte[stream.Length];
            var read = 0;
            while (read < data.Length)
            {
                var n = await stream.ReadAsync(data.AsMemory(read));
                if (n <= 0) break;
                read += n;
            }
            if (read != data.Length) throw new PdfException("Pagjoro could not read the document to sign it.");
            return Cms(data, stamp: true);
        }
    }
    sealed class TextAppearance(string[] lines) : IAnnotationAppearanceHandler
    {
        public void DrawAppearance(XGraphics gfx, XRect rect)
        {
            gfx.DrawRectangle(new XPen(XColors.SeaGreen, 1), new XSolidBrush(XColor.FromArgb(235, 248, 245)), rect);
            var font = new XFont("Arial", 8);
            var y = rect.Y + 12;
            foreach (var line in lines)
            {
                gfx.DrawString(line, font, XBrushes.Black, new XPoint(rect.X + 5, y));
                y += 11;
            }
        }
    }

    /// <summary>
    /// Signs the document. When <paramref name="visibleArea"/> is given, a box with the signer's name, the date and the reason is drawn on that
    /// page. The result is a new file; any later change to it breaks the signature (that is what a signature is for).
    /// </summary>
    public static byte[] Sign(byte[] pdf, X509Certificate2 certificate, string reason = "", string location = "", string contact = "", int pageIndex = 0, PageRect? visibleArea = null, string? timestampUrl = null)
    {
        if (!certificate.HasPrivateKey) throw new PdfException("That certificate has no private key on this computer, so it cannot be used to sign.");
        PdfDocument doc;
        try { doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify); }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotImplementedException) { throw new PdfException("Pagjoro could not prepare this file for signing (" + ex.Message + ")."); }
        using (doc)
        {
            WindowsFonts.EnsureRegistered();
            var name = certificate.GetNameInfo(X509NameType.SimpleName, false);
            var index = Math.Clamp(pageIndex, 0, doc.PageCount - 1);
            var rectangle = new XRect(0, 0, 0, 0);
            IAnnotationAppearanceHandler? appearance = null;
            if (visibleArea is { } r)
            {
                var n = r.Normalized();
                rectangle = new XRect(n.Left, doc.Pages[index].Height.Point - n.Top, n.Width, n.Height);
                var lines = new List<string> { "Digitally signed by " + name, "Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
                if (reason.Length > 0) lines.Add("Reason: " + reason);
                if (location.Length > 0) lines.Add("Location: " + location);
                appearance = new TextAppearance([.. lines]);
            }
            var options = new DigitalSignatureOptions
            {
                ContactInfo = contact, Location = location, Reason = reason, AppName = "Pagjoro", PageIndex = index,
                Rectangle = rectangle, AppearanceHandler = appearance,
            };
            var handler = DigitalSignatureHandler.ForDocument(doc, new CertificateSigner(certificate, timestampUrl), options);
            using var ms = new MemoryStream();
            doc.Save(ms, false);
            GC.KeepAlive(handler);
            return ms.ToArray();
        }
    }

    // ---- checking ---------------------------------------------------------------------------------------------------

    /// <summary>Every signature in the document with the result of checking it. The file is not changed.</summary>
    public static IReadOnlyList<SignatureInfo> Inspect(byte[] pdf)
    {
        var result = new List<SignatureInfo>();
        using var file = PdfFile.Open(pdf);
        foreach (var raw in file.ReadSignatures())
        {
            var signer = "unknown"; var intact = false; var trusted = false; var stamped = false;
            var reasonText = raw.Reason;
            try
            {
                var content = TrimDer(raw.Contents);
                if (raw.ByteRange.Length >= 4 && raw.ByteRange[1] >= 0)
                {
                    var signedData = new byte[raw.ByteRange[1] + raw.ByteRange[3]];
                    Buffer.BlockCopy(pdf, raw.ByteRange[0], signedData, 0, raw.ByteRange[1]);
                    Buffer.BlockCopy(pdf, raw.ByteRange[2], signedData, raw.ByteRange[1], raw.ByteRange[3]);
                    var cms = new SignedCms(new ContentInfo(signedData), detached: true);
                    cms.Decode(content);
                    var cert = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
                    if (cms.SignerInfos.Count > 0)
                        foreach (var attr in cms.SignerInfos[0].UnsignedAttributes) if (attr.Oid?.Value == Timestamp.TokenOid) stamped = true;
                    if (cert is not null) signer = cert.GetNameInfo(X509NameType.SimpleName, false);
                    try { cms.CheckSignature(verifySignatureOnly: true); intact = true; } catch (CryptographicException) { intact = false; }
                    if (cert is not null)
                    {
                        using var chain = new X509Chain();
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
                        if (cms.Certificates.Count > 0) chain.ChainPolicy.ExtraStore.AddRange(cms.Certificates);
                        trusted = chain.Build(cert);
                    }
                }
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or IndexOutOfRangeException) { intact = false; }
            var whole = raw.ByteRange.Length >= 4 && (long)raw.ByteRange[2] + raw.ByteRange[3] == pdf.Length;
            var summary = !intact ? "The signature does not match the document: it was changed after signing, or the signature is damaged."
                : !whole ? "The signature is valid, but the document was changed after it was signed."
                : trusted ? $"Signed by {signer}. The document has not changed since."
                : $"Signed by {signer}. The document has not changed since, but this computer does not trust the signer's certificate.";
            if (stamped && intact) summary += " A trusted time stamp proves when it was signed.";
            result.Add(new SignatureInfo(raw.Index, signer, raw.Time, reasonText, intact, whole, trusted, summary, stamped));
        }
        return result;
    }

    /// <summary>A CMS blob padded with zeros: cut it to its real length using the DER length at its start.</summary>
    static byte[] TrimDer(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0x30) return data;
        int length, header;
        if ((data[1] & 0x80) == 0) { length = data[1]; header = 2; }
        else
        {
            var n = data[1] & 0x7F;
            if (n is < 1 or > 4 || data.Length < 2 + n) return data;
            length = 0;
            for (var i = 0; i < n; i++) length = (length << 8) | data[2 + i];
            header = 2 + n;
        }
        var total = header + length;
        return total <= data.Length ? data[..total] : data;
    }
}

public sealed record RawSignature(int Index, byte[] Contents, int[] ByteRange, string Reason, string Time, string SubFilter);

public sealed partial class PdfFile
{
    /// <summary>The raw signature records of the document (see <see cref="PdfSigning.Inspect"/> for the checked result).</summary>
    public unsafe IReadOnlyList<RawSignature> ReadSignatures()
    {
        Check();
        var list = new List<RawSignature>();
        lock (PdfEngine.Gate)
        {
            var count = fpdf_signature.FPDF_GetSignatureCount(Doc);
            for (var i = 0; i < count; i++)
            {
                var sig = fpdf_signature.FPDF_GetSignatureObject(Doc, i);
                if (sig is null) continue;
                // contents
                var size = fpdf_signature.FPDFSignatureObjGetContents(sig, IntPtr.Zero, 0);
                var contents = new byte[size];
                if (size > 0)
                {
                    var buffer = Marshal.AllocHGlobal((int)size);
                    try { fpdf_signature.FPDFSignatureObjGetContents(sig, buffer, size); Marshal.Copy(buffer, contents, 0, (int)size); }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                // byte range
                var dummy = 0;
                var n = (int)fpdf_signature.FPDFSignatureObjGetByteRange(sig, ref dummy, 0);
                var range = new int[Math.Max(n, 0)];
                if (n > 0) fpdf_signature.FPDFSignatureObjGetByteRange(sig, ref range[0], (uint)n);
                // reason (UTF-16), time and sub-filter (ASCII)
                var reason = PdfEngine.ReadUtf16((buf, len) => fpdf_signature.FPDFSignatureObjGetReason(sig, buf, len));
                var time = ReadAscii((p, len) => fpdf_signature.FPDFSignatureObjGetTime(sig, p, len));
                var sub = ReadAscii((p, len) => fpdf_signature.FPDFSignatureObjGetSubFilter(sig, p, len));
                list.Add(new RawSignature(i, contents, range, reason, time, sub));
            }
        }
        return list;
    }

    unsafe delegate uint AsciiCall(sbyte* buffer, uint length);

    static unsafe string ReadAscii(AsciiCall call)
    {
        var need = call(null, 0);
        if (need <= 1) return "";
        var bytes = new byte[need];
        fixed (byte* p = bytes) call((sbyte*)p, need);
        return System.Text.Encoding.ASCII.GetString(bytes, 0, (int)need - 1);
    }
}
