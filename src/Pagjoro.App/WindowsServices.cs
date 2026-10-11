// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

/// <summary>JPEG encoding and decoding with the picture codecs that come with Windows.</summary>
sealed class WpfImageRecoder : IImageRecoder
{
    public (int Width, int Height, int Channels, byte[] Pixels)? DecodeJpeg(byte[] jpeg)
    {
        try
        {
            var decoder = new JpegBitmapDecoder(new MemoryStream(jpeg), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var gray = frame.Format == PixelFormats.Gray8;
            BitmapSource source = gray ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
            var channels = gray ? 1 : 3;
            var stride = source.PixelWidth * channels;
            var raw = new byte[stride * source.PixelHeight];
            source.CopyPixels(raw, stride, 0);
            if (!gray)   // BGR to RGB
                for (var i = 0; i + 2 < raw.Length; i += 3) (raw[i], raw[i + 2]) = (raw[i + 2], raw[i]);
            return (source.PixelWidth, source.PixelHeight, channels, raw);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or IOException) { return null; }
    }

    public byte[] EncodeJpeg(byte[] pixels, int width, int height, int channels, int quality)
    {
        BitmapSource bmp;
        if (channels == 1) bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        else
        {
            var bgr = new byte[pixels.Length];
            for (var i = 0; i + 2 < pixels.Length; i += 3) { bgr[i] = pixels[i + 2]; bgr[i + 1] = pixels[i + 1]; bgr[i + 2] = pixels[i]; }
            bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, bgr, width * 3);
        }
        var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>Turns Word, Excel and PowerPoint files (and other office formats) into PDF with whatever is installed on this computer.</summary>
static class OfficeConverter
{
    public static readonly string[] Extensions = [".doc", ".docx", ".rtf", ".odt", ".txt", ".xls", ".xlsx", ".ods", ".csv", ".ppt", ".pptx", ".odp"];

    public static string FileFilter =>
        "Office documents|" + string.Join(";", Extensions.Select(e => "*" + e)) + "|All files|*.*";

    /// <summary>Converts <paramref name="source"/> to <paramref name="target"/>. Throws with a plain explanation when nothing installed can do it.</summary>
    public static async Task ConvertAsync(string source, string target)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        Exception? officeError = null;
        var progId = ext switch
        {
            ".doc" or ".docx" or ".rtf" or ".txt" or ".odt" => "Word.Application",
            ".xls" or ".xlsx" or ".csv" or ".ods" => "Excel.Application",
            ".ppt" or ".pptx" or ".odp" => "PowerPoint.Application",
            _ => null,
        };
        if (progId is not null && Type.GetTypeFromProgID(progId) is not null)
        {
            try { await Task.Run(() => ViaOffice(progId, source, target)); if (File.Exists(target)) return; }
            catch (Exception ex) { officeError = ex; }
        }
        var soffice = FindLibreOffice();
        if (soffice is not null)
        {
            await ViaLibreOffice(soffice, source, target);
            if (File.Exists(target)) return;
        }
        if (officeError is not null) throw new InvalidOperationException("Microsoft Office could not convert that file: " + officeError.Message);
        throw new InvalidOperationException(
            "Pagjoro needs Microsoft Office (Word, Excel or PowerPoint) or the free LibreOffice installed on this computer to turn this kind of file into a PDF. " +
            "Neither was found. Install one of them, or print the document to \"Microsoft Print to PDF\" from the program that opens it.");
    }

    static void ViaOffice(string progId, string source, string target)
    {
        dynamic? app = null;
        try
        {
            app = Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;
            switch (progId)
            {
                case "Word.Application":
                    {
                        app.Visible = false; app.DisplayAlerts = 0;
                        dynamic d = app.Documents.Open(source, false, true);
                        try { d.ExportAsFixedFormat(target, 17); } finally { d.Close(false); }
                        break;
                    }
                case "Excel.Application":
                    {
                        app.Visible = false; app.DisplayAlerts = false;
                        dynamic wb = app.Workbooks.Open(source, 0, true);
                        try { wb.ExportAsFixedFormat(0, target); } finally { wb.Close(false); }
                        break;
                    }
                default:
                    {
                        dynamic p = app.Presentations.Open(source, true, false, false);
                        try { p.SaveAs(target, 32); } finally { p.Close(); }
                        break;
                    }
            }
        }
        finally
        {
            try { app?.Quit(); } catch (Exception) { /* already gone */ }
            if (app is not null) { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(app); } catch (Exception) { } }
        }
    }

    static string? FindLibreOffice()
    {
        string[] candidates =
        [
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    static async Task ViaLibreOffice(string soffice, string source, string target)
    {
        var temp = Path.Combine(Path.GetTempPath(), "pagjoro-office-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var psi = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "--headless", "--convert-to", "pdf", "--outdir", temp, source }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("LibreOffice did not start.");
            if (!await Task.Run(() => p.WaitForExit(180_000))) { try { p.Kill(true); } catch (Exception) { } throw new InvalidOperationException("LibreOffice took too long."); }
            var made = Path.Combine(temp, Path.GetFileNameWithoutExtension(source) + ".pdf");
            if (File.Exists(made)) File.Copy(made, target, true);
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }
}

/// <summary>Scanning with the Windows Image Acquisition service that every scanner driver supports.</summary>
static class Scanner
{
    /// <summary>Shows the scanner's own dialog and returns the picture file path, or null if nothing was scanned.</summary>
    public static string? Acquire()
    {
        var type = Type.GetTypeFromProgID("WIA.CommonDialog")
            ?? throw new InvalidOperationException("Windows image acquisition is not available on this computer.");
        dynamic dialog = Activator.CreateInstance(type)!;
        // device type 1 = scanner, intent 1 = colour, bias 0x20000 = maximise quality, JPEG format
        dynamic? image;
        try { image = dialog.ShowAcquireImage(1, 1, 0x20000, "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}", false, true, false); }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80210015)
        { throw new InvalidOperationException("No scanner was found. Connect and switch on a scanner, then try again."); }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80210064 || (uint)ex.HResult == 0x80210001)
        { return null; }   // cancelled
        if (image is null) return null;
        var path = Path.Combine(Path.GetTempPath(), "pagjoro-scan-" + Guid.NewGuid().ToString("N") + ".jpg");
        image.SaveFile(path);
        return path;
    }
}
