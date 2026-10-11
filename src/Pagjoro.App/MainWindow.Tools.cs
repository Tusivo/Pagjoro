// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Pagjoro.Core;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public partial class MainWindow
{
    Forms.SignChoice? pendingSign;

    void WireTools()
    {
        View.TextRequested += OnTextRequested;
        View.AreaChosen += OnAreaChosen;
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    IReadOnlyList<int>? PagesFromText(string text)
    {
        if (doc is null) return null;
        if (string.IsNullOrWhiteSpace(text)) return Enumerable.Range(0, doc.PageCount).ToList();
        return PageRanges.Parse(text, doc.PageCount);
    }

    void Mutate(Action<PdfFile> change, string message)
    {
        if (doc is null) return;
        PushUndo();
        try { change(doc); }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); undo.RemoveAt(undo.Count - 1); return; }
        StructureChanged(message);
    }

    static (byte[] Pixels, int Width, int Height, double DpiX, double DpiY) LoadPicture(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return (pixels, converted.PixelWidth, converted.PixelHeight, frame.DpiX > 1 ? frame.DpiX : 96, frame.DpiY > 1 ? frame.DpiY : 96);
    }

    const string PictureFilter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";

    string DefaultName(string suffix) =>
        (doc?.Path is null ? "Document" : Path.GetFileNameWithoutExtension(doc.Path)) + " (" + suffix + ").pdf";

    // ---- adding things to pages -------------------------------------------------------------------------------------

    void OnTextRequested(int page, double x, double y)
    {
        if (doc is null) return;
        var text = Forms.TextToAdd(this, out var size, out var font);
        if (text is null) return;
        PushUndo();
        doc.AddText(page, text, x, y - size * 0.8, size, PdfColor.Black, font);
        View.InvalidatePage(page); AfterEdit();
        MessageStatus.Text = "Text added.";
    }

    void OnAreaChosen(ViewTool tool, int page, PageRect area)
    {
        if (doc is null) return;
        if (tool == ViewTool.SignArea && pendingSign is { } choice)
        {
            pendingSign = null;
            View.Tool = ViewTool.Select; ToolSelect.IsChecked = true;
            SignNow(choice, page, area);
            return;
        }
        if (tool != ViewTool.Image) return;
        var d = new OpenFileDialog { Filter = PictureFilter, Title = "Choose a picture to place" };
        if (d.ShowDialog(this) != true) return;
        try
        {
            var (pixels, w, h, _, _) = LoadPicture(d.FileName);
            // keep the picture's shape inside the box that was dragged
            var scale = Math.Min(area.Width / w, area.Height / h);
            var pw = w * scale; var ph = h * scale;
            var fit = new PageRect(area.Left, area.Top - ph, area.Left + pw, area.Top);
            PushUndo();
            doc.AddImage(page, pixels, w, h, fit);
            View.InvalidatePage(page); AfterEdit();
            MessageStatus.Text = "Picture added.";
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or FileFormatException or PdfException or InvalidOperationException)
        {
            Dialogs.Info(this, "Add Picture", "That picture could not be used: " + ex.Message, error: true);
        }
    }

    void OnWatermark(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var choice = Forms.Watermark(this, doc.PageCount);
        if (choice is null) return;
        var pages = PagesFromText(choice.Pages)!;
        Mutate(f => PageDecorations.Watermark(f, pages, choice.Options), $"Watermark added to {pages.Count} page(s).");
    }

    void OnHeaderFooter(object sender, RoutedEventArgs e) => HeaderFooter(false);
    void OnPageNumbers(object sender, RoutedEventArgs e) => HeaderFooter(true);

    void HeaderFooter(bool numbersOnly)
    {
        if (doc is null) return;
        var choice = Forms.HeaderFooter(this, doc.PageCount, numbersOnly);
        if (choice is null) return;
        var pages = PagesFromText(choice.Pages)!;
        Mutate(f => PageDecorations.HeaderFooter(f, pages, choice.Options), $"Added to {pages.Count} page(s).");
    }

    void OnBates(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var options = Forms.Bates(this);
        if (options is null) return;
        var range = (First: "", Last: "");
        Mutate(f => range = PageDecorations.Bates(f, Enumerable.Range(0, f.PageCount), options), "");
        MessageStatus.Text = $"Numbered {range.First} to {range.Last}.";
    }

    void OnFlatten(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        if (!Dialogs.Confirm(this, "Flatten Marks", "This makes every comment, mark and form field part of the page, so they can no longer be changed or removed. You can undo it with Ctrl+Z until you close the file. Continue?")) return;
        Mutate(f => { for (var p = 0; p < f.PageCount; p++) f.Flatten(p); }, "Marks flattened.");
    }

    void OnImagesToPdf(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = PictureFilter, Multiselect = true, Title = "Choose the pictures, in page order" };
        if (d.ShowDialog(this) != true) return;
        var save = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "Pictures.pdf", Title = "Save the new document as" };
        if (save.ShowDialog(this) != true) return;
        try
        {
            using var made = PdfFile.CreateEmpty();
            var page = 0;
            foreach (var file in d.FileNames.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                var (pixels, w, h, dx, dy) = LoadPicture(file);
                var pw = w / dx * 72; var ph = h / dy * 72;
                var longer = Math.Max(pw, ph);
                if (longer > 792) { var s = 792 / longer; pw *= s; ph *= s; }   // no bigger than 11 inches on the long side
                if (longer < 144) { var s = 144 / longer; pw *= s; ph *= s; }
                made.InsertBlankPage(page, pw, ph);
                made.AddImage(page, pixels, w, h, new PageRect(0, 0, pw, ph));
                page++;
            }
            made.SaveTo(save.FileName);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or FileFormatException or PdfException or InvalidOperationException)
        {
            Dialogs.Info(this, "Pictures to PDF", "Pagjoro could not make the document: " + ex.Message, error: true);
            return;
        }
        MessageStatus.Text = $"Made {Path.GetFileName(save.FileName)} from {d.FileNames.Length} picture(s).";
        if (Dialogs.Confirm(this, "Pictures to PDF", "The document was made. Open it now?")) OpenPath(save.FileName);
    }

    // ---- protecting -------------------------------------------------------------------------------------------------

    void OnSetPassword(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var choice = Forms.SetPassword(this);
        if (choice is null) return;
        var save = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = DefaultName("protected"), Title = "Save the protected copy as" };
        if (save.ShowDialog(this) != true) return;
        try
        {
            var locked = PdfProtection.Encrypt(doc.ToBytes(removeSecurity: true), choice.OpenPassword, choice.OwnerPassword, choice.Permissions);
            File.WriteAllBytes(save.FileName, locked);
        }
        catch (Exception ex) when (ex is PdfException or IOException or UnauthorizedAccessException)
        {
            Dialogs.Info(this, "Pagjoro", ex.Message, error: true);
            return;
        }
        MessageStatus.Text = $"Saved a protected copy: {Path.GetFileName(save.FileName)}. The document on screen is unchanged.";
    }

    void OnRemovePassword(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        if (!doc.IsEncrypted) { Dialogs.Info(this, "Remove Password", "This file has no password or restrictions."); return; }
        var save = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = DefaultName("unprotected"), Title = "Save a copy without protection as" };
        if (save.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(save.FileName, doc.ToBytes(removeSecurity: true)); }
        catch (Exception ex) when (ex is PdfException or IOException or UnauthorizedAccessException) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        MessageStatus.Text = $"Saved {Path.GetFileName(save.FileName)} without a password.";
    }

    async void OnRedactPatterns(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var choice = Forms.Pattern(this);
        if (choice is null) return;
        var file = doc;
        MessageStatus.Text = "Looking...";
        IReadOnlyList<PatternMatch> found;
        try { found = await Task.Run(() => SensitivePatterns.Find(file, choice.Pattern, choice.CreditCardChecksum)); }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        if (found.Count == 0) { MessageStatus.Text = "Nothing found."; Dialogs.Info(this, "Find and Redact", $"No {choice.Label} were found in this document."); return; }
        var picked = Forms.PickMatches(this, choice.Label, found);
        if (picked is null || picked.Count == 0) { MessageStatus.Text = ""; return; }
        foreach (var g in picked.GroupBy(m => m.PageIndex)) View.AddRedactionBoxes(g.Key, g.SelectMany(m => m.Rects));
        SideTabs.SelectedIndex = 0;
        MessageStatus.Text = $"{picked.Count} item(s) marked in red. Press Apply Redactions to remove them for good.";
    }

    void OnSanitize(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        if (!Dialogs.Confirm(this, "Remove Hidden Data", "This takes out the document's properties (title, author and so on), XML metadata, scripts, embedded files and open actions. The pages stay as they are. If the file has a password it will be removed from the working copy. Continue?")) return;
        SanitizeReport report = new([]);
        try
        {
            var (bytes, r) = PdfProtection.Sanitize(doc.ToBytes(removeSecurity: true));
            report = r;
            PushUndo();
            ReplaceDocument(PdfFile.FromBytes(bytes), "Hidden data removed.");
        }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        Dialogs.Info(this, "Remove Hidden Data", report.NothingFound ? "No hidden data was found." : "Removed:\n\n• " + string.Join("\n• ", report.Removed) + "\n\nSave the file to keep the change.");
    }

    void OnCompress(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        try
        {
            var before = doc.ToBytes(removeSecurity: true);
            var after = PdfProtection.Recompress(before);
            if (after.Length >= before.Length) { Dialogs.Info(this, "Reduce File Size", "This file is already as small as Pagjoro can make it without losing quality."); return; }
            PushUndo();
            ReplaceDocument(PdfFile.FromBytes(after), $"File size reduced from {before.Length / 1024:N0} KB to {after.Length / 1024:N0} KB. Save to keep it.");
        }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); }
    }

    void OnEditInfo(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var m = doc.GetMetadata();
        string Get(string k) => m.TryGetValue(k, out var v) ? v : "";
        var f = new FormBuilder(this, "Document Information");
        var title = f.Text("Title", Get("Title")); var author = f.Text("Author", Get("Author"));
        var subject = f.Text("Subject", Get("Subject")); var keywords = f.Text("Keywords", Get("Keywords"));
        if (!f.Show("Save Information")) return;
        try
        {
            var bytes = PdfProtection.SetInformation(doc.ToBytes(removeSecurity: true), title.Text, author.Text, subject.Text, keywords.Text);
            PushUndo();
            ReplaceDocument(PdfFile.FromBytes(bytes), "Document information updated. Save to keep it.");
        }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); }
    }

    // ---- signing ----------------------------------------------------------------------------------------------------

    void OnSign(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var certs = PdfSigning.FindSigningCertificates();
        if (certs.Count == 0)
        {
            if (!Dialogs.Confirm(this, "Sign Document", "There is no signing certificate on this computer yet.\n\nPagjoro can make one for you now. People who receive your signed files will see that the file was not changed after you signed it and which certificate signed it, but they will be told their computer does not know the certificate unless they choose to trust it.\n\nMake one?")) return;
            var f = new FormBuilder(this, "Make a Signing Certificate");
            var name = f.Text("Your name, as it should appear", Environment.UserName);
            var email = f.Text("Email (optional)", "");
            if (!f.Show("Make Certificate")) return;
            try { PdfSigning.CreateSelfSigned(name.Text.Trim(), email.Text.Trim()); }
            catch (Exception ex) when (ex is PdfException or System.Security.Cryptography.CryptographicException) { Dialogs.Info(this, "Sign Document", ex.Message, error: true); return; }
            certs = PdfSigning.FindSigningCertificates();
            if (certs.Count == 0) return;
        }
        var choice = Forms.Sign(this, certs);
        if (choice is null) return;
        if (!choice.Visible) { SignNow(choice, 0, null); return; }
        pendingSign = choice;
        View.Tool = ViewTool.SignArea;
        foreach (var t in new[] { ToolSelect, ToolHighlight, ToolUnderline, ToolStrike, ToolNote, ToolDraw, ToolRect, ToolOval, ToolRedact, ToolText, ToolImage }) t.IsChecked = false;
        MessageStatus.Text = "Drag a box on the page where the signature should appear.";
    }

    void SignNow(Forms.SignChoice choice, int page, PageRect? area)
    {
        if (doc is null) return;
        var save = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = DefaultName("signed"), Title = "Save the signed copy as" };
        if (save.ShowDialog(this) != true) return;
        try
        {
            var signed = PdfSigning.Sign(doc.ToBytes(removeSecurity: true), choice.Certificate, choice.Reason, choice.Location, choice.Contact, page, area);
            File.WriteAllBytes(save.FileName, signed);
        }
        catch (Exception ex) when (ex is PdfException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Dialogs.Info(this, "Sign Document", "The document could not be signed: " + ex.Message, error: true);
            return;
        }
        MessageStatus.Text = $"Signed and saved as {Path.GetFileName(save.FileName)}.";
        if (Dialogs.Confirm(this, "Sign Document", "The signed copy was saved. Open it now to check it? (Any change you make to a signed file shows up as a broken signature.)")) OpenPath(save.FileName);
    }

    void OnCheckSignatures(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        if (doc.Path is null || !File.Exists(doc.Path)) { Dialogs.Info(this, "Check Signatures", "Save the file first. Signatures can only be checked on the file as it is on disk."); return; }
        IReadOnlyList<SignatureInfo> found;
        try { found = PdfSigning.Inspect(File.ReadAllBytes(doc.Path)); }
        catch (Exception ex) when (ex is PdfException or IOException) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        if (found.Count == 0) { Dialogs.Info(this, "Check Signatures", "This file has no digital signatures."); return; }
        var sb = new StringBuilder();
        if (doc.IsModified) sb.AppendLine("Note: you have unsaved changes. This is the check of the file on disk.\n");
        foreach (var s in found)
        {
            sb.AppendLine($"Signature {s.Index + 1}");
            sb.AppendLine("  " + s.Summary);
            if (s.Reason.Length > 0) sb.AppendLine("  Reason: " + s.Reason);
            if (s.SignedAt.Length > 0) sb.AppendLine("  Signed: " + s.SignedAt);
            sb.AppendLine();
        }
        Dialogs.Info(this, "Check Signatures", sb.ToString());
    }

    // ---- comparing --------------------------------------------------------------------------------------------------

    async void OnCompare(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var d = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Choose the file to compare with" };
        if (d.ShowDialog(this) != true) return;
        using var other = OpenWithPrompt(d.FileName, "to compare");
        if (other is null) return;
        var mine = doc;
        MessageStatus.Text = "Comparing...";
        ComparisonResult result;
        try { result = await Task.Run(() => DocumentComparer.Compare(mine, other)); }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        MessageStatus.Text = result.Identical ? "The documents are the same." : $"{result.TotalRemoved} removed, {result.TotalAdded} added.";
        Forms.ShowComparison(this, mine.Path is null ? "this document" : Path.GetFileName(mine.Path), Path.GetFileName(d.FileName), result);
    }
}
