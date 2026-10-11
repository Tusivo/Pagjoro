// SPDX-License-Identifier: AGPL-3.0-or-later
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Pagjoro.Core.Pdf;

/// <summary>What a person who opens a protected file is allowed to do.</summary>
public sealed record PdfPermissions(bool Print = true, bool Modify = true, bool Copy = true, bool Annotate = true, bool FillForms = true, bool Assemble = true)
{
    public static readonly PdfPermissions All = new();
    public bool IsRestricted => !(Print && Modify && Copy && Annotate && FillForms && Assemble);
}

/// <summary>What <see cref="PdfProtection.Sanitize"/> found and removed.</summary>
public sealed record SanitizeReport(IReadOnlyList<string> Removed)
{
    public bool NothingFound => Removed.Count == 0;
}

/// <summary>
/// Whole-file operations that rewrite the document's structure: passwords and permissions, document information, removing hidden data,
/// and smaller files. They take a document's bytes and return new bytes, leaving the input alone.
/// </summary>
public static class PdfProtection
{
    static PdfDocument Load(byte[] pdf)
    {
        try { return PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify); }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotImplementedException or ArgumentException)
        { throw new PdfException("Pagjoro could not rewrite this file's structure (" + ex.Message + ")."); }
    }

    static byte[] Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    /// <summary>Locks a document with an open password and/or restrictions. Use bytes from <c>PdfFile.ToBytes(removeSecurity: true)</c>.</summary>
    public static byte[] Encrypt(byte[] pdf, string openPassword, string ownerPassword, PdfPermissions permissions)
    {
        if (string.IsNullOrEmpty(openPassword) && !permissions.IsRestricted)
            throw new PdfException("Choose a password to open the file, or restrict something, otherwise there is nothing to protect.");
        if (string.IsNullOrEmpty(ownerPassword)) ownerPassword = openPassword.Length > 0 ? openPassword + "·owner" : Guid.NewGuid().ToString("N");
        using var doc = Load(pdf);
        doc.SecurityHandler.SetEncryptionToV5(true);
        var s = doc.SecuritySettings;
        s.UserPassword = openPassword;
        s.OwnerPassword = ownerPassword;
        s.PermitPrint = permissions.Print; s.PermitFullQualityPrint = permissions.Print;
        s.PermitModifyDocument = permissions.Modify;
        s.PermitExtractContent = permissions.Copy;
        s.PermitAnnotations = permissions.Annotate;
        s.PermitFormsFill = permissions.FillForms;
        s.PermitAssembleDocument = permissions.Assemble;
        return Save(doc);
    }

    /// <summary>Sets the Title, Author, Subject and Keywords shown in the document's properties.</summary>
    public static byte[] SetInformation(byte[] pdf, string title, string author, string subject, string keywords)
    {
        using var doc = Load(pdf);
        doc.Info.Title = title; doc.Info.Author = author; doc.Info.Subject = subject; doc.Info.Keywords = keywords;
        return Save(doc);
    }

    /// <summary>
    /// Removes information that is easy to overlook: the document properties, XML metadata, scripts, embedded files, document-open actions
    /// and page actions. Returns the new bytes and a list of what was found.
    /// </summary>
    public static (byte[] Pdf, SanitizeReport Report) Sanitize(byte[] pdf)
    {
        using var doc = Load(pdf);
        var removed = new List<string>();
        var info = doc.Info;
        if (!string.IsNullOrEmpty(info.Title) || !string.IsNullOrEmpty(info.Author) || !string.IsNullOrEmpty(info.Subject) || !string.IsNullOrEmpty(info.Keywords))
        {
            removed.Add("Document properties (title, author, subject, keywords)");
            info.Title = ""; info.Author = ""; info.Subject = ""; info.Keywords = "";
        }
        if (!string.IsNullOrEmpty(info.Creator)) { removed.Add("Name of the program that created the file"); info.Creator = ""; }
        var catalog = doc.Internals.Catalog;
        void Drop(string key, string what)
        {
            if (catalog.Elements.ContainsKey(key)) { catalog.Elements.Remove(key); removed.Add(what); }
        }
        Drop("/Metadata", "XML metadata");
        Drop("/OpenAction", "An action that runs when the document opens");
        Drop("/AA", "Document-level actions");
        if (catalog.Elements.GetDictionary("/Names") is { } names)
        {
            if (names.Elements.ContainsKey("/JavaScript")) { names.Elements.Remove("/JavaScript"); removed.Add("Document scripts"); }
            if (names.Elements.ContainsKey("/EmbeddedFiles")) { names.Elements.Remove("/EmbeddedFiles"); removed.Add("Embedded files"); }
        }
        var pageActions = 0;
        foreach (var page in doc.Pages)
            if (page.Elements.ContainsKey("/AA")) { page.Elements.Remove("/AA"); pageActions++; }
        if (pageActions > 0) removed.Add($"Actions on {pageActions} page(s)");
        return (Save(doc), new SanitizeReport(removed));
    }

    /// <summary>Rewrites the file with the strongest lossless compression of its streams.</summary>
    public static byte[] Recompress(byte[] pdf)
    {
        using var doc = Load(pdf);
        doc.Options.CompressContentStreams = true;
        doc.Options.NoCompression = false;
        doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        doc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;
        return Save(doc);
    }
}
