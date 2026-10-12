// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Pagjoro.Core.Pdf;

/// <summary>Turns pixels into JPEG data and back. The window supplies this (Core has no picture codecs of its own).</summary>
public interface IImageRecoder
{
    /// <summary>Decodes JPEG data to raw pixels: width, height, channels (1 gray, 3 RGB) and the bytes row by row.</summary>
    (int Width, int Height, int Channels, byte[] Pixels)? DecodeJpeg(byte[] jpeg);

    /// <summary>Encodes raw pixels (1 or 3 channels, 8 bits each) as a JPEG at the given quality (1 to 100).</summary>
    byte[] EncodeJpeg(byte[] pixels, int width, int height, int channels, int quality);
}

/// <param name="JpegQuality">1 to 100. Lower is smaller and blurrier.</param>
/// <param name="MaxDpi">Pictures with more detail than this (judged by how big they appear on the page) are scaled down. 0 keeps every pixel.</param>
public sealed record OptimizeOptions(int JpegQuality = 70, int MaxDpi = 150, bool RecompressFlate = true, bool DropMetadata = false);

public sealed record OptimizeReport(int PicturesChanged, long BytesBefore, long BytesAfter);

/// <summary>Makes a file smaller by recompressing and reducing its pictures, then repacking its streams.</summary>
public static class PdfOptimizer
{
    public static (byte[] Pdf, OptimizeReport Report) Optimize(byte[] pdf, OptimizeOptions options, IImageRecoder recoder)
    {
        PdfDocument doc;
        try { doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify); }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotImplementedException or ArgumentException)
        { throw new PdfException("Pagjoro could not read this file to shrink it (" + ex.Message + ")."); }
        var changed = 0;
        using (doc)
        {
            foreach (var item in doc.Internals.GetAllObjects())
            {
                if (item is not PdfDictionary d || d.Stream is null) continue;
                if (d.Elements.GetName("/Subtype") != "/Image") continue;
                if (d.Elements.ContainsKey("/SMask") || d.Elements.ContainsKey("/Mask")) continue;     // transparency: left alone
                if (d.Elements.GetInteger("/BitsPerComponent") != 8) continue;
                var cs = d.Elements.GetName("/ColorSpace");
                var channels = cs == "/DeviceRGB" ? 3 : cs == "/DeviceGray" ? 1 : 0;
                if (channels == 0) continue;
                int w = d.Elements.GetInteger("/Width"), h = d.Elements.GetInteger("/Height");
                if (w <= 0 || h <= 0) continue;

                var filter = FilterName(d);
                byte[]? jpeg = null; int pw = w, ph = h;
                if (filter == "/DCTDecode")
                {
                    var raw = d.Stream.Value;
                    var decoded = recoder.DecodeJpeg(raw);
                    if (decoded is null || decoded.Value.Channels != channels) continue;
                    var (dw, dh, ch, pixels) = decoded.Value;
                    (pixels, pw, ph) = Scale(pixels, dw, dh, ch, options.MaxDpi);
                    jpeg = recoder.EncodeJpeg(pixels, pw, ph, ch, options.JpegQuality);
                    if (jpeg.Length >= raw.Length * 0.97 && pw == w && ph == h) continue;
                }
                else if (options.RecompressFlate && filter == "/FlateDecode" && !d.Elements.ContainsKey("/DecodeParms") && !d.Elements.ContainsKey("/Decode"))
                {
                    byte[] pixels;
                    try
                    {
                        using var zin = new ZLibStream(new MemoryStream(d.Stream.Value), CompressionMode.Decompress);
                        using var buf = new MemoryStream(); zin.CopyTo(buf); pixels = buf.ToArray();
                    }
                    catch (InvalidDataException) { continue; }
                    if (pixels.Length != w * h * channels) continue;
                    // only photographic pictures gain from JPEG; flat graphics (few colors) are better left lossless
                    if (LooksFlat(pixels, channels)) continue;
                    (pixels, pw, ph) = Scale(pixels, w, h, channels, options.MaxDpi);
                    jpeg = recoder.EncodeJpeg(pixels, pw, ph, channels, options.JpegQuality);
                    if (jpeg.Length >= d.Stream.Value.Length * 0.9) continue;
                }
                if (jpeg is null) continue;

                d.Elements.Remove("/DecodeParms");
                d.Stream.Value = jpeg;
                d.Elements.SetName("/Filter", "/DCTDecode");
                d.Elements.SetInteger("/Width", pw);
                d.Elements.SetInteger("/Height", ph);
                d.Elements.SetInteger("/Length", jpeg.Length);
                changed++;
            }
            if (options.DropMetadata) doc.Internals.Catalog.Elements.Remove("/Metadata");
            using var ms = new MemoryStream();
            doc.Options.CompressContentStreams = true;
            doc.Options.NoCompression = false;
            doc.Save(ms, false);
            var result = ms.ToArray();
            // never hand back a larger file than the one given
            return result.Length < pdf.Length ? (result, new OptimizeReport(changed, pdf.Length, result.Length)) : (pdf, new OptimizeReport(0, pdf.Length, pdf.Length));
        }
    }

    static string? FilterName(PdfDictionary d)
    {
        var f = d.Elements["/Filter"];
        if (f is PdfName n) return n.Value;
        if (f is PdfArray a && a.Elements.Count == 1 && a.Elements[0] is PdfName n0) return n0.Value;
        return null;
    }

    /// <summary>Scales a picture down by whole steps when it holds more than <paramref name="maxDpi"/> would need for a letter-sized page.</summary>
    static (byte[] Pixels, int Width, int Height) Scale(byte[] pixels, int w, int h, int ch, int maxDpi)
    {
        if (maxDpi <= 0) return (pixels, w, h);
        // without per-page placement we assume the picture may fill a letter page's width (8.5 in)
        var maxWidth = (int)(maxDpi * 8.5);
        if (w <= maxWidth) return (pixels, w, h);
        var factor = (int)Math.Ceiling((double)w / maxWidth);
        var nw = Math.Max(1, w / factor); var nh = Math.Max(1, h / factor);
        var result = new byte[nw * nh * ch];
        for (var y = 0; y < nh; y++)
            for (var x = 0; x < nw; x++)
                for (var c = 0; c < ch; c++)
                {
                    var sum = 0; var n = 0;
                    for (var dy = 0; dy < factor && y * factor + dy < h; dy++)
                        for (var dx = 0; dx < factor && x * factor + dx < w; dx++) { sum += pixels[((y * factor + dy) * w + x * factor + dx) * ch + c]; n++; }
                    result[(y * nw + x) * ch + c] = (byte)(sum / Math.Max(1, n));
                }
        return (result, nw, nh);
    }

    static bool LooksFlat(byte[] pixels, int ch)
    {
        var seen = new HashSet<int>();
        var step = Math.Max(1, pixels.Length / ch / 4000);
        for (var i = 0; i + ch <= pixels.Length; i += step * ch)
        {
            var key = ch == 1 ? pixels[i] : pixels[i] << 16 | pixels[i + 1] << 8 | pixels[i + 2];
            seen.Add(key);
            if (seen.Count > 400) return false;
        }
        return true;
    }
}
