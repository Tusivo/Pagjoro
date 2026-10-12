// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

/// <summary>
/// The one place that starts the PDF engine (PDFium, BSD-3-Clause and Apache-2.0) and serializes access to it, because the engine is not thread safe.
/// Every call into the engine goes through <see cref="Gate"/>.
/// </summary>
public static class PdfEngine
{
    static readonly object startLock = new();
    static bool started;

    /// <summary>Held while any engine call runs.</summary>
    public static readonly object Gate = new();

    public static void Start()
    {
        lock (startLock)
        {
            if (started) return;
            lock (Gate) fpdfview.FPDF_InitLibrary();
            started = true;
        }
    }

    // ---- small helpers used throughout the wrapper -----------------------------------------------------------------

    /// <summary>Reads a UTF-16 string from an engine call that takes (buffer, length) and returns the byte length including the terminator.</summary>
    internal static string ReadUtf16(Func<IntPtr, uint, uint> call)
    {
        var bytes = call(IntPtr.Zero, 0);
        if (bytes <= 2) return "";
        var buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            call(buffer, bytes);
            return Marshal.PtrToStringUni(buffer, (int)(bytes / 2) - 1);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>A null-terminated UTF-16 buffer for engine calls that take a <c>ref ushort</c>.</summary>
    internal static ushort[] Utf16(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text);
        var result = new ushort[bytes.Length / 2 + 1];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    internal static uint Pack(byte r, byte g, byte b, byte a = 255) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
}

/// <summary>Why a file could not be opened, in words a person can act on.</summary>
public class PdfException(string message) : Exception(message);

/// <summary>The file is protected and needs a password (or the one given was wrong).</summary>
public sealed class PdfPasswordRequiredException(bool wrongPassword)
    : PdfException(wrongPassword ? "That password did not open the file. Check it and try again." : "This file is protected with a password. Enter the password to open it.")
{
    public bool WrongPassword { get; } = wrongPassword;
}

/// <summary>A rectangle in page space: points (1/72 inch), origin at the bottom-left of the page, so Top &gt; Bottom.</summary>
public readonly record struct PageRect(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;
    public double Height => Top - Bottom;
    public bool Intersects(PageRect o) => Left < o.Right && o.Left < Right && Bottom < o.Top && o.Bottom < Top;
    public PageRect Normalized() => new(Math.Min(Left, Right), Math.Min(Bottom, Top), Math.Max(Left, Right), Math.Max(Bottom, Top));
}

/// <summary>One place a search found the text.</summary>
public sealed record TextMatch(int PageIndex, int CharIndex, int Length, IReadOnlyList<PageRect> Rects);

/// <summary>An entry of the document outline.</summary>
public sealed record BookmarkInfo(string Title, int PageIndex, int Level);

/// <summary>A rendered page: 32-bit pixels in blue, green, red, alpha order, top row first.</summary>
public sealed record PageImage(int Width, int Height, int Stride, byte[] Pixels);
