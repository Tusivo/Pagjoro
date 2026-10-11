// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;
using PDFiumCore;

namespace Pagjoro.Core.Pdf;

/// <summary>
/// A PDF document held in memory. Opening a file reads it completely, so the original on disk is never locked and never changed until
/// <see cref="SaveTo(string)"/> is called. All access to the engine is serialised through <see cref="PdfEngine.Gate"/>.
/// </summary>
public sealed partial class PdfFile : IDisposable
{
    internal FpdfDocumentT Doc { get; private set; }
    GCHandle pin;
    byte[]? source;
    bool disposed;

    /// <summary>Where the document was read from (null for a new or combined document).</summary>
    public string? Path { get; private set; }

    /// <summary>True once something has been changed since the file was opened or last saved.</summary>
    public bool IsModified { get; set; }

    PdfFile(FpdfDocumentT doc, byte[]? data, GCHandle pin, string? path)
    {
        Doc = doc; source = data; this.pin = pin; Path = path;
    }

    // ---- opening and creating ---------------------------------------------------------------------------------------

    public static PdfFile Open(string path, string? password = null)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new PdfException($"Pagjoro could not read “{System.IO.Path.GetFileName(path)}”: {ex.Message}"); }
        return Open(data, password, path);
    }

    public static PdfFile Open(byte[] data, string? password = null, string? path = null)
    {
        if (data.Length < 8) throw new PdfException("This file is empty or too short to be a PDF.");
        PdfEngine.Start();
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        FpdfDocumentT? doc;
        lock (PdfEngine.Gate)
        {
            doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)data.Length, string.IsNullOrEmpty(password) ? null : password);
            if (doc is null)
            {
                var error = fpdfview.FPDF_GetLastError();
                pin.Free();
                throw error switch
                {
                    4 => new PdfPasswordRequiredException(!string.IsNullOrEmpty(password)),
                    3 => new PdfException("This file is not a valid PDF, or it is damaged."),
                    2 => new PdfException("The file could not be read."),
                    5 => new PdfException("This file uses a security setting that Pagjoro does not support."),
                    _ => new PdfException("The file could not be opened (error " + error + ")."),
                };
            }
        }
        return new PdfFile(doc, data, pin, path);
    }

    /// <summary>A new document with no pages.</summary>
    public static PdfFile CreateEmpty()
    {
        PdfEngine.Start();
        lock (PdfEngine.Gate)
        {
            var doc = fpdf_edit.FPDF_CreateNewDocument() ?? throw new PdfException("Pagjoro could not make a new document.");
            return new PdfFile(doc, null, default, null) { IsModified = true };
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (PdfEngine.Gate) fpdfview.FPDF_CloseDocument(Doc);
        if (pin.IsAllocated) pin.Free();
        source = null;
    }

    void Check() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <summary>Remembers where the document will be saved (used when pages were rearranged into a new document).</summary>
    public void AssignPath(string? path) => Path = path;

    /// <summary>True when the file is protected with a password or permissions.</summary>
    public bool IsEncrypted { get { Check(); lock (PdfEngine.Gate) return fpdfview.FPDF_GetSecurityHandlerRevision(Doc) != -1; } }

    /// <summary>What the file permits (as set by whoever protected it).</summary>
    public PdfPermissions Permissions
    {
        get
        {
            Check();
            if (!IsEncrypted) return PdfPermissions.All;
            uint bits; lock (PdfEngine.Gate) bits = fpdfview.FPDF_GetDocPermissions(Doc);
            return new PdfPermissions((bits & 4) != 0 || (bits & 2048) != 0, (bits & 8) != 0, (bits & 16) != 0, (bits & 32) != 0, (bits & 256) != 0, (bits & 1024) != 0);
        }
    }

    // ---- pages ------------------------------------------------------------------------------------------------------

    public int PageCount { get { Check(); lock (PdfEngine.Gate) return fpdfview.FPDF_GetPageCount(Doc); } }

    void ValidatePage(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index), "That page does not exist.");
    }

    /// <summary>The page's size in points as it is shown (rotation applied).</summary>
    public (double Width, double Height) GetPageSize(int index)
    {
        Check(); ValidatePage(index);
        lock (PdfEngine.Gate)
        {
            double w = 0, h = 0;
            fpdfview.FPDF_GetPageSizeByIndex(Doc, index, ref w, ref h);
            return (w, h);
        }
    }

    /// <summary>The page's rotation as stored: 0, 90, 180 or 270.</summary>
    public int GetPageRotation(int index)
    {
        Check(); ValidatePage(index);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, index);
            try { return fpdf_edit.FPDFPageGetRotation(page) * 90; }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>Turns a page clockwise by a multiple of 90 degrees (negative values turn it the other way).</summary>
    public void RotatePage(int index, int degrees)
    {
        Check(); ValidatePage(index);
        if (degrees % 90 != 0) throw new ArgumentException("Pages turn in steps of 90 degrees.", nameof(degrees));
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, index);
            try
            {
                var turns = ((fpdf_edit.FPDFPageGetRotation(page) + degrees / 90) % 4 + 4) % 4;
                fpdf_edit.FPDFPageSetRotation(page, turns);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        IsModified = true;
    }

    public void DeletePage(int index)
    {
        Check(); ValidatePage(index);
        if (PageCount == 1) throw new PdfException("A document needs at least one page, so the last page cannot be deleted.");
        lock (PdfEngine.Gate) fpdf_edit.FPDFPageDelete(Doc, index);
        IsModified = true;
    }

    /// <summary>Deletes several pages at once (indexes may be in any order and repeated).</summary>
    public void DeletePages(IEnumerable<int> indexes)
    {
        var list = indexes.Distinct().OrderByDescending(i => i).ToList();
        if (list.Count >= PageCount) throw new PdfException("A document needs at least one page, so every page cannot be deleted.");
        foreach (var i in list) DeletePage(i);
    }

    public void InsertBlankPage(int index, double widthPoints, double heightPoints)
    {
        Check();
        if (index < 0 || index > PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        lock (PdfEngine.Gate)
        {
            var page = fpdf_edit.FPDFPageNew(Doc, index, widthPoints, heightPoints) ?? throw new PdfException("Pagjoro could not add a page.");
            fpdfview.FPDF_ClosePage(page);
        }
        IsModified = true;
    }

    /// <summary>Copies pages of another document into this one, in the order given, placing them before page <paramref name="insertAt"/>.</summary>
    public void ImportPages(PdfFile from, IReadOnlyList<int> sourcePages, int insertAt)
    {
        Check(); from.Check();
        if (sourcePages.Count == 0) return;
        if (insertAt < 0 || insertAt > PageCount) throw new ArgumentOutOfRangeException(nameof(insertAt));
        var count = from.PageCount;
        if (sourcePages.Any(p => p < 0 || p >= count)) throw new ArgumentOutOfRangeException(nameof(sourcePages), "A page to copy does not exist.");
        var indexes = sourcePages.ToArray();
        lock (PdfEngine.Gate)
        {
            if (fpdf_ppo.FPDF_ImportPagesByIndex(Doc, from.Doc, ref indexes[0], (uint)indexes.Length, insertAt) == 0)
                throw new PdfException("Pagjoro could not copy those pages.");
        }
        IsModified = true;
    }

    /// <summary>A new document made of the given pages of this one, in that order.</summary>
    public PdfFile ExtractPages(IReadOnlyList<int> pages)
    {
        var result = CreateEmpty();
        try { result.ImportPages(this, pages, 0); return result; }
        catch { result.Dispose(); throw; }
    }

    // ---- looking at a page ------------------------------------------------------------------------------------------

    /// <summary>Draws a page into a bitmap of the given size in pixels (the whole page is scaled to fit).</summary>
    public PageImage Render(int index, int widthPx, int heightPx, bool annotations = true)
    {
        Check(); ValidatePage(index);
        if (widthPx < 1 || heightPx < 1 || (long)widthPx * heightPx > 120_000_000) throw new ArgumentOutOfRangeException(nameof(widthPx), "That picture size is not possible.");
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, index) ?? throw new PdfException("That page could not be loaded.");
            var bitmap = fpdfview.FPDFBitmapCreate(widthPx, heightPx, 1);
            try
            {
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, widthPx, heightPx, 0xFFFFFFFF);
                fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, widthPx, heightPx, 0, annotations ? 0x01 : 0);
                var stride = fpdfview.FPDFBitmapGetStride(bitmap);
                var pixels = new byte[stride * heightPx];
                Marshal.Copy(fpdfview.FPDFBitmapGetBuffer(bitmap), pixels, 0, pixels.Length);
                return new PageImage(widthPx, heightPx, stride, pixels);
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
                fpdfview.FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>Renders a page so that its longer side is about <paramref name="longSidePx"/> pixels.</summary>
    public PageImage RenderFit(int index, int longSidePx, bool annotations = true)
    {
        var (w, h) = GetPageSize(index);
        var scale = longSidePx / Math.Max(w, h);
        return Render(index, Math.Max(1, (int)Math.Round(w * scale)), Math.Max(1, (int)Math.Round(h * scale)), annotations);
    }

    // ---- text -------------------------------------------------------------------------------------------------------

    /// <summary>All the text of a page in reading order, as the engine finds it.</summary>
    public string GetPageText(int index)
    {
        Check(); ValidatePage(index);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, index);
            var text = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                var count = fpdf_text.FPDFTextCountChars(text);
                if (count <= 0) return "";
                var buffer = new ushort[count + 1];
                var written = fpdf_text.FPDFTextGetText(text, 0, count, ref buffer[0]);
                return written <= 1 ? "" : string.Create(written - 1, buffer, (span, b) => { for (var i = 0; i < span.Length; i++) span[i] = (char)b[i]; });
            }
            finally { fpdf_text.FPDFTextClosePage(text); fpdfview.FPDF_ClosePage(page); }
        }
    }

    static IReadOnlyList<PageRect> Rects(FpdfTextpageT text, int start, int count)
    {
        var n = fpdf_text.FPDFTextCountRects(text, start, count);
        var list = new List<PageRect>(Math.Max(n, 0));
        for (var i = 0; i < n; i++)
        {
            double l = 0, t = 0, r = 0, b = 0;
            if (fpdf_text.FPDFTextGetRect(text, i, ref l, ref t, ref r, ref b) != 0) list.Add(new PageRect(l, b, r, t));
        }
        return list;
    }

    /// <summary>Finds text on one page or on all pages (<paramref name="onlyPage"/> &lt; 0).</summary>
    public IReadOnlyList<TextMatch> Search(string needle, bool matchCase = false, bool wholeWord = false, int onlyPage = -1)
    {
        Check();
        var found = new List<TextMatch>();
        if (string.IsNullOrEmpty(needle)) return found;
        var pattern = PdfEngine.Utf16(needle);
        uint flags = (matchCase ? 1u : 0u) | (wholeWord ? 2u : 0u);
        var from = onlyPage >= 0 ? onlyPage : 0;
        var to = onlyPage >= 0 ? onlyPage : PageCount - 1;
        lock (PdfEngine.Gate)
        {
            for (var p = from; p <= to; p++)
            {
                var page = fpdfview.FPDF_LoadPage(Doc, p);
                var text = fpdf_text.FPDFTextLoadPage(page);
                var search = fpdf_text.FPDFTextFindStart(text, ref pattern[0], flags, 0);
                try
                {
                    while (search is not null && fpdf_text.FPDFTextFindNext(search) != 0)
                    {
                        var start = fpdf_text.FPDFTextGetSchResultIndex(search);
                        var length = fpdf_text.FPDFTextGetSchCount(search);
                        found.Add(new TextMatch(p, start, length, Rects(text, start, length)));
                    }
                }
                finally
                {
                    if (search is not null) fpdf_text.FPDFTextFindClose(search);
                    fpdf_text.FPDFTextClosePage(text); fpdfview.FPDF_ClosePage(page);
                }
            }
        }
        return found;
    }

    /// <summary>The index of the character under a page point, or -1 when there is none.</summary>
    public int CharIndexAt(int pageIndex, double x, double y, double tolerance = 4)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            var text = fpdf_text.FPDFTextLoadPage(page);
            try { return fpdf_text.FPDFTextGetCharIndexAtPos(text, x, y, tolerance, tolerance); }
            finally { fpdf_text.FPDFTextClosePage(text); fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>The boxes that cover characters [start, start+count) of a page, and the text itself.</summary>
    public (IReadOnlyList<PageRect> Rects, string Text) GetTextRange(int pageIndex, int start, int count)
    {
        Check(); ValidatePage(pageIndex);
        if (count <= 0) return ([], "");
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            var text = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                var buffer = new ushort[count + 1];
                var written = fpdf_text.FPDFTextGetText(text, start, count, ref buffer[0]);
                var s = written <= 1 ? "" : string.Create(written - 1, buffer, (span, b) => { for (var i = 0; i < span.Length; i++) span[i] = (char)b[i]; });
                return (Rects(text, start, count), s);
            }
            finally { fpdf_text.FPDFTextClosePage(text); fpdfview.FPDF_ClosePage(page); }
        }
    }

    /// <summary>The text found inside a rectangle of the page.</summary>
    public string GetTextIn(int pageIndex, PageRect area)
    {
        Check(); ValidatePage(pageIndex);
        var r = area.Normalized();
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            var text = fpdf_text.FPDFTextLoadPage(page);
            try
            {
                var buffer = new ushort[4096];
                var n = fpdf_text.FPDFTextGetBoundedText(text, r.Left, r.Top, r.Right, r.Bottom, ref buffer[0], buffer.Length - 1);
                return n <= 0 ? "" : string.Create(Math.Min(n, buffer.Length - 1), buffer, (span, b) => { for (var i = 0; i < span.Length; i++) span[i] = (char)b[i]; });
            }
            finally { fpdf_text.FPDFTextClosePage(text); fpdfview.FPDF_ClosePage(page); }
        }
    }

    // ---- structure --------------------------------------------------------------------------------------------------

    public IReadOnlyList<BookmarkInfo> GetBookmarks()
    {
        Check();
        var list = new List<BookmarkInfo>();
        lock (PdfEngine.Gate) Walk(null, 0, list, 0);
        return list;
    }

    void Walk(FpdfBookmarkT? parent, int level, List<BookmarkInfo> into, int guard)
    {
        if (level > 24 || into.Count > 20000) return;
        var child = fpdf_doc.FPDFBookmarkGetFirstChild(Doc, parent);
        var safety = 0;
        while (child is not null && safety++ < 20000)
        {
            var title = PdfEngine.ReadUtf16((buffer, length) => fpdf_doc.FPDFBookmarkGetTitle(child, buffer, length));
            var dest = fpdf_doc.FPDFBookmarkGetDest(Doc, child);
            var pageIndex = dest is null ? -1 : fpdf_doc.FPDFDestGetDestPageIndex(Doc, dest);
            if (pageIndex < 0)
            {
                var action = fpdf_doc.FPDFBookmarkGetAction(child);
                if (action is not null && fpdf_doc.FPDFActionGetType(action) == 1)
                {
                    var d = fpdf_doc.FPDFActionGetDest(Doc, action);
                    if (d is not null) pageIndex = fpdf_doc.FPDFDestGetDestPageIndex(Doc, d);
                }
            }
            into.Add(new BookmarkInfo(title, pageIndex, level));
            Walk(child, level + 1, into, guard + 1);
            child = fpdf_doc.FPDFBookmarkGetNextSibling(Doc, child);
        }
    }

    /// <summary>Title, Author, Subject, Keywords, Creator, Producer, CreationDate and ModDate, where the file has them.</summary>
    public IReadOnlyDictionary<string, string> GetMetadata()
    {
        Check();
        var result = new Dictionary<string, string>();
        lock (PdfEngine.Gate)
        {
            foreach (var key in new[] { "Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate" })
            {
                var value = PdfEngine.ReadUtf16((buffer, length) => fpdf_doc.FPDF_GetMetaText(Doc, key, buffer, length));
                if (value.Length > 0) result[key] = value;
            }
        }
        return result;
    }

    /// <summary>Where a click at a page point leads: a web address, a page, or nothing.</summary>
    public (string? Uri, int PageIndex) GetLinkAt(int pageIndex, double x, double y)
    {
        Check(); ValidatePage(pageIndex);
        lock (PdfEngine.Gate)
        {
            var page = fpdfview.FPDF_LoadPage(Doc, pageIndex);
            try
            {
                var link = fpdf_doc.FPDFLinkGetLinkAtPoint(page, x, y);
                if (link is null) return (null, -1);
                var dest = fpdf_doc.FPDFLinkGetDest(Doc, link);
                if (dest is not null)
                {
                    var target = fpdf_doc.FPDFDestGetDestPageIndex(Doc, dest);
                    if (target >= 0) return (null, target);
                }
                var action = fpdf_doc.FPDFLinkGetAction(link);
                if (action is null) return (null, -1);
                var type = fpdf_doc.FPDFActionGetType(action);
                if (type == 1)
                {
                    var d = fpdf_doc.FPDFActionGetDest(Doc, action);
                    return d is null ? (null, -1) : (null, fpdf_doc.FPDFDestGetDestPageIndex(Doc, d));
                }
                if (type == 3)
                {
                    var bytes = fpdf_doc.FPDFActionGetURIPath(Doc, action, IntPtr.Zero, 0);
                    if (bytes <= 1) return (null, -1);
                    var buffer = Marshal.AllocHGlobal((int)bytes);
                    try
                    {
                        fpdf_doc.FPDFActionGetURIPath(Doc, action, buffer, bytes);
                        return (Marshal.PtrToStringAnsi(buffer), -1);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                return (null, -1);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    // ---- saving -----------------------------------------------------------------------------------------------------

    /// <summary>The whole document as a PDF file, rewritten completely.</summary>
    public byte[] ToBytes(bool removeSecurity = false)
    {
        Check();
        using var stream = new MemoryStream();
        var writer = new FPDF_FILEWRITE_ { Version = 1 };
        writer.WriteBlock = (self, data, size) =>
        {
            var chunk = new byte[size];
            Marshal.Copy(data, chunk, 0, (int)size);
            stream.Write(chunk, 0, chunk.Length);
            return 1;
        };
        lock (PdfEngine.Gate)
        {
            if (fpdf_save.FPDF_SaveAsCopy(Doc, writer, removeSecurity ? 3u /* FPDF_REMOVE_SECURITY */ : 2u /* FPDF_NO_INCREMENTAL */) == 0)
                throw new PdfException("Pagjoro could not save the document.");
        }
        GC.KeepAlive(writer);
        return stream.ToArray();
    }

    /// <summary>Saves a copy. The file is written to a temporary name first, so a failure never damages an existing file.</summary>
    public void SaveTo(string path)
    {
        var bytes = ToBytes();
        var temp = path + ".pagjoro-saving";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw new PdfException($"Pagjoro could not save “{System.IO.Path.GetFileName(path)}”: {ex.Message}");
        }
        Path = path;
        IsModified = false;
    }

    /// <summary>Re-opens the saved bytes as a fresh document (used after steps that rewrite the file with another library).</summary>
    public static PdfFile FromBytes(byte[] bytes, string? path = null) => Open(bytes, null, path);

    internal static string Describe(string s) => s.Length > 40 ? s[..40] + "…" : s;
}
