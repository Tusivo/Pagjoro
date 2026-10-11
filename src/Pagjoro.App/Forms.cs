// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

/// <summary>A tiny helper to lay out label-and-control rows in a dialog written in code.</summary>
sealed class FormBuilder
{
    readonly StackPanel panel = new() { Margin = new Thickness(20) };
    public Window Window { get; }

    public FormBuilder(Window? owner, string title, double width = 520)
    {
        Window = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        if (owner is not null) Window.Owner = owner;
        Window.Content = panel;
    }

    public FormBuilder Note(string text)
    {
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.85 });
        return this;
    }

    public TextBox Text(string label, string initial = "", double width = double.NaN)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
        var box = new TextBox { Text = initial };
        if (!double.IsNaN(width)) { box.Width = width; box.HorizontalAlignment = HorizontalAlignment.Left; }
        panel.Children.Add(box);
        return box;
    }

    public PasswordBox Password(string label)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
        var box = new PasswordBox();
        panel.Children.Add(box);
        return box;
    }

    public ComboBox Choice(string label, IEnumerable<string> items, int selected = 0)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
        var box = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = selected };
        panel.Children.Add(box);
        return box;
    }

    public CheckBox Check(string label, bool on = false)
    {
        var box = new CheckBox { Content = label, IsChecked = on, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(box);
        return box;
    }

    public T Add<T>(T element) where T : UIElement { panel.Children.Add(element); return element; }

    public bool Show(string ok, Func<bool>? validate = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var okButton = new Button { Content = ok, IsDefault = true, Padding = new Thickness(18, 6, 18, 6), MinWidth = 96 };
        okButton.Click += (_, _) => { if (validate is null || validate()) Window.DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
        row.Children.Add(okButton); row.Children.Add(cancel);
        panel.Children.Add(row);
        return Window.ShowDialog() == true;
    }
}

public static class Forms
{
    public sealed record WatermarkChoice(Pagjoro.Core.Pdf.WatermarkOptions Options, string Pages);

    public static WatermarkChoice? Watermark(Window? owner, int pageCount)
    {
        var f = new FormBuilder(owner, "Add a Watermark");
        f.Note("Large, light words across the middle of each page.");
        var text = f.Text("Words", "DRAFT");
        var size = f.Text("Letter size (points)", "72", 100);
        var angle = f.Choice("Direction", ["Diagonal (45 degrees)", "Straight across", "Diagonal the other way"]);
        var colour = f.Choice("Colour", ["Grey", "Red", "Blue"]);
        var pages = f.Text("Pages (leave empty for all)", "");
        WatermarkChoice? result = null;
        if (!f.Show("Add Watermark", () =>
        {
            if (string.IsNullOrWhiteSpace(text.Text)) { Dialogs.Info(f.Window, "Watermark", "Type the words for the watermark."); return false; }
            if (!double.TryParse(size.Text, out var s) || s < 8 || s > 400) { Dialogs.Info(f.Window, "Watermark", "Type a letter size between 8 and 400."); return false; }
            try { if (pages.Text.Trim().Length > 0) PageRanges.Parse(pages.Text, pageCount); }
            catch (FormatException ex) { Dialogs.Info(f.Window, "Watermark", ex.Message); return false; }
            var col = colour.SelectedIndex switch { 1 => new PdfColor(200, 30, 30), 2 => new PdfColor(30, 80, 200), _ => new PdfColor(110, 110, 110) };
            result = new WatermarkChoice(new Pagjoro.Core.Pdf.WatermarkOptions(text.Text.Trim(), s, col, angle.SelectedIndex switch { 1 => 0, 2 => -45, _ => 45 }), pages.Text.Trim());
            return true;
        })) return null;
        return result;
    }

    public sealed record HeaderFooterChoice(HeaderFooterOptions Options, string Pages);

    public static HeaderFooterChoice? HeaderFooter(Window? owner, int pageCount, bool pageNumbersOnly = false)
    {
        var f = new FormBuilder(owner, pageNumbersOnly ? "Page Numbers" : "Header and Footer", 560);
        f.Note("Type what should appear. {page} is the page number, {pages} the number of pages, {date} today's date and {file} the file name.");
        TextBox? tl = null, tc = null, tr = null;
        if (!pageNumbersOnly) { tl = f.Text("Top left"); tc = f.Text("Top centre"); tr = f.Text("Top right"); }
        var bl = f.Text("Bottom left");
        var bc = f.Text("Bottom centre", pageNumbersOnly ? "Page {page} of {pages}" : "");
        var br = f.Text("Bottom right");
        var size = f.Text("Letter size (points)", "10", 100);
        var pages = f.Text("Pages (leave empty for all)");
        HeaderFooterChoice? result = null;
        if (!f.Show("Add", () =>
        {
            if (!double.TryParse(size.Text, out var s) || s < 5 || s > 60) { Dialogs.Info(f.Window, "Header and Footer", "Type a letter size between 5 and 60."); return false; }
            try { if (pages.Text.Trim().Length > 0) PageRanges.Parse(pages.Text, pageCount); }
            catch (FormatException ex) { Dialogs.Info(f.Window, "Header and Footer", ex.Message); return false; }
            result = new HeaderFooterChoice(new HeaderFooterOptions(tl?.Text ?? "", tc?.Text ?? "", tr?.Text ?? "", bl.Text, bc.Text, br.Text, s), pages.Text.Trim());
            return result.Options is not { TopLeft: "", TopCenter: "", TopRight: "", BottomLeft: "", BottomCenter: "", BottomRight: "" } || Fail(f.Window);
        })) return null;
        return result;
    }

    static bool Fail(Window w) { Dialogs.Info(w, "Header and Footer", "Type something in at least one box."); return false; }

    public static BatesOptions? Bates(Window? owner)
    {
        var f = new FormBuilder(owner, "Bates Numbers");
        f.Note("Stamps a running number on every page, often used to identify pages of legal documents. Example: ABC-000001.");
        var prefix = f.Text("Letters before the number", "");
        var start = f.Text("First number", "1", 120);
        var digits = f.Text("Number of digits", "6", 120);
        var suffix = f.Text("Letters after the number", "");
        var corner = f.Choice("Where", ["Bottom right", "Bottom centre", "Bottom left", "Top right", "Top centre", "Top left"]);
        BatesOptions? result = null;
        if (!f.Show("Add Numbers", () =>
        {
            if (!int.TryParse(start.Text, out var s) || s < 0) { Dialogs.Info(f.Window, "Bates Numbers", "Type the first number, such as 1."); return false; }
            if (!int.TryParse(digits.Text, out var d) || d < 1 || d > 12) { Dialogs.Info(f.Window, "Bates Numbers", "Type how many digits, from 1 to 12."); return false; }
            var c = corner.SelectedIndex switch { 1 => PageCorner.BottomCenter, 2 => PageCorner.BottomLeft, 3 => PageCorner.TopRight, 4 => PageCorner.TopCenter, 5 => PageCorner.TopLeft, _ => PageCorner.BottomRight };
            result = new BatesOptions(prefix.Text.Trim(), s, d, suffix.Text.Trim(), c);
            return true;
        })) return null;
        return result;
    }

    public sealed record PasswordChoice(string OpenPassword, string OwnerPassword, PdfPermissions Permissions);

    public static PasswordChoice? SetPassword(Window? owner)
    {
        var f = new FormBuilder(owner, "Protect with a Password", 520);
        f.Note("A password to open the file keeps it private. You can also let people open it but stop printing, copying or changing it; that needs an owner password only you know. Keep your passwords safe: Pagjoro cannot recover them.");
        var open = f.Password("Password to open the file (leave empty for none)");
        var open2 = f.Password("Type it again");
        var owner2 = f.Password("Owner password, to change these settings later (optional)");
        var print = f.Check("Allow printing", true);
        var copy = f.Check("Allow copying text", true);
        var modify = f.Check("Allow changing the document", true);
        var annotate = f.Check("Allow adding comments", true);
        var assemble = f.Check("Allow inserting, deleting and turning pages", true);
        PasswordChoice? result = null;
        if (!f.Show("Protect", () =>
        {
            if (open.Password != open2.Password) { Dialogs.Info(f.Window, "Password", "The two passwords are different. Type the same one twice."); return false; }
            var perms = new PdfPermissions(print.IsChecked == true, modify.IsChecked == true, copy.IsChecked == true, annotate.IsChecked == true, true, assemble.IsChecked == true);
            if (open.Password.Length == 0 && !perms.IsRestricted) { Dialogs.Info(f.Window, "Password", "Type a password, or untick something to restrict."); return false; }
            if (perms.IsRestricted && owner2.Password.Length == 0 && open.Password.Length == 0)
            { Dialogs.Info(f.Window, "Password", "Restrictions need an owner password, so only you can change them. Type one."); return false; }
            result = new PasswordChoice(open.Password, owner2.Password, perms);
            return true;
        })) return null;
        return result;
    }

    public sealed record PatternChoice(string Pattern, bool CreditCardChecksum, string Label);

    public static PatternChoice? Pattern(Window? owner)
    {
        var f = new FormBuilder(owner, "Find and Redact Patterns", 540);
        f.Note("Find every place where something private appears, review the list, and remove the ones you choose.");
        var names = SensitivePatterns.Presets.Keys.Append("My own pattern (advanced)").ToList();
        var kind = f.Choice("What to look for", names);
        var custom = f.Text("Your pattern (a regular expression)", "");
        custom.IsEnabled = false;
        kind.SelectionChanged += (_, _) => custom.IsEnabled = kind.SelectedIndex == names.Count - 1;
        PatternChoice? result = null;
        if (!f.Show("Find", () =>
        {
            var i = kind.SelectedIndex;
            if (i == names.Count - 1)
            {
                if (custom.Text.Length == 0) { Dialogs.Info(f.Window, "Patterns", "Type the pattern to look for."); return false; }
                result = new PatternChoice(custom.Text, false, "your pattern");
            }
            else result = new PatternChoice(SensitivePatterns.Presets[names[i]], names[i] == "Credit card numbers", names[i]);
            return true;
        })) return null;
        return result;
    }

    /// <summary>Shows what was found with a tick box for each, and returns the ones to remove.</summary>
    public static List<PatternMatch>? PickMatches(Window? owner, string label, IReadOnlyList<PatternMatch> matches)
    {
        var w = new Window
        {
            Title = "Review what was found", Width = 620, Height = 480, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var dock = new DockPanel { Margin = new Thickness(20) };
        var head = new TextBlock { Text = $"{matches.Count} place(s) found for {label}. Untick any that should stay.", Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = new Button { Content = "Mark for Redaction", IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); dock.Children.Add(buttons);
        var list = new ListBox();
        var boxes = new List<(CheckBox, PatternMatch)>();
        foreach (var m in matches)
        {
            var cb = new CheckBox { IsChecked = true, Content = $"Page {m.PageIndex + 1}:  {m.Text}", Margin = new Thickness(2, 3, 2, 3) };
            boxes.Add((cb, m)); list.Items.Add(cb);
        }
        dock.Children.Add(list);
        w.Content = dock;
        ok.Click += (_, _) => w.DialogResult = true;
        return w.ShowDialog() == true ? boxes.Where(b => b.Item1.IsChecked == true).Select(b => b.Item2).ToList() : null;
    }

    public sealed record TextEditChoice(bool Delete, string Text, string Font, double Size, PdfColor Color);

    static (string Family, bool Bold, bool Italic) SplitFont(string font) =>
        (font.StartsWith("Times") ? "Serif" : font.StartsWith("Courier") ? "Fixed" : "Sans", font.Contains("Bold"), font.Contains("Italic") || font.Contains("Oblique"));

    public static string JoinFont(string family, bool bold, bool italic) => family switch
    {
        "Serif" => bold ? (italic ? "Times-BoldItalic" : "Times-Bold") : (italic ? "Times-Italic" : "Times-Roman"),
        "Fixed" => bold ? (italic ? "Courier-BoldOblique" : "Courier-Bold") : (italic ? "Courier-Oblique" : "Courier"),
        _ => bold ? (italic ? "Helvetica-BoldOblique" : "Helvetica-Bold") : (italic ? "Helvetica-Oblique" : "Helvetica"),
    };

    static readonly (string Name, PdfColor? Color)[] TextColours =
        [("Keep the current colour", null), ("Black", PdfColor.Black), ("Dark grey", new PdfColor(80, 80, 80)), ("Red", PdfColor.Red), ("Blue", PdfColor.Blue), ("Green", PdfColor.Green), ("White", PdfColor.White)];

    /// <summary>Change the words, font, size or colour of a piece of text on the page, or delete it.</summary>
    public static TextEditChoice? EditText(Window? owner, PageObjectInfo info)
    {
        var f = new FormBuilder(owner, "Edit Text", 560);
        f.Note("Change the words, then press Apply. The text is set in a standard font at the same place; letters outside the standard fonts become a question mark.");
        var text = f.Text("Text", info.Text);
        var (family, bold, italic) = SplitFont(info.Font);
        var fam = f.Choice("Font", ["Sans", "Serif", "Fixed width"], family == "Serif" ? 1 : family == "Fixed" ? 2 : 0);
        var boldBox = f.Check("Bold", bold);
        var italicBox = f.Check("Italic", italic);
        var size = f.Text("Letter size (points)", Math.Round(info.Size, 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 100);
        var colour = f.Choice("Colour", TextColours.Select(c => c.Name));
        var delete = f.Check("Delete this text from the page");
        TextEditChoice? result = null;
        if (!f.Show("Apply", () =>
        {
            if (delete.IsChecked != true && string.IsNullOrWhiteSpace(text.Text)) { Dialogs.Info(f.Window, "Edit Text", "Type the text, or tick Delete."); return false; }
            if (!double.TryParse(size.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) &&
                !double.TryParse(size.Text, out s)) { Dialogs.Info(f.Window, "Edit Text", "Type a letter size, such as 12."); return false; }
            if (s < 4 || s > 300) { Dialogs.Info(f.Window, "Edit Text", "Type a letter size between 4 and 300."); return false; }
            var chosen = TextColours[colour.SelectedIndex].Color ?? info.Color;
            var fontFamily = fam.SelectedIndex == 1 ? "Serif" : fam.SelectedIndex == 2 ? "Fixed" : "Sans";
            result = new TextEditChoice(delete.IsChecked == true, text.Text, JoinFont(fontFamily, boldBox.IsChecked == true, italicBox.IsChecked == true), s, chosen);
            return true;
        })) return null;
        return result;
    }

    public sealed record FormAnswer(Dictionary<string, string> Values, Dictionary<string, bool> Checks);

    /// <summary>One row per fillable field: a text box, a tick box or a list of choices.</summary>
    public static FormAnswer? FillForm(Window? owner, IReadOnlyList<FormField> fields)
    {
        var w = new Window
        {
            Title = "Fill In Form", Width = 560, Height = Math.Min(720, 220 + fields.Count * 62), MinHeight = 260, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var dock = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = new Button { Content = "Fill In", IsDefault = true, Padding = new Thickness(18, 6, 18, 6), MinWidth = 96 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); dock.Children.Add(buttons);
        var panel = new StackPanel();
        var texts = new List<(string Name, Func<string> Get)>();
        var checks = new List<(string Name, CheckBox Box)>();
        foreach (var f in fields)
        {
            if (f.Kind == FormFieldKind.CheckBox)
            {
                var cb = new CheckBox { Content = f.Name, IsChecked = f.Checked, Margin = new Thickness(0, 8, 0, 0) };
                checks.Add((f.Name, cb)); panel.Children.Add(cb);
                continue;
            }
            panel.Children.Add(new TextBlock { Text = f.Name, Margin = new Thickness(0, 8, 0, 3) });
            if (f.Options.Count > 0)
            {
                var combo = new ComboBox { ItemsSource = f.Options, SelectedItem = f.Options.Contains(f.Value) ? f.Value : null };
                texts.Add((f.Name, () => combo.SelectedItem as string ?? f.Value)); panel.Children.Add(combo);
            }
            else
            {
                var tb = new TextBox { Text = f.Value };
                texts.Add((f.Name, () => tb.Text)); panel.Children.Add(tb);
            }
        }
        dock.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        w.Content = dock;
        ok.Click += (_, _) => w.DialogResult = true;
        if (w.ShowDialog() != true) return null;
        return new FormAnswer(texts.ToDictionary(t => t.Name, t => t.Get()), checks.ToDictionary(c => c.Name, c => c.Box.IsChecked == true));
    }

    public sealed record SignChoice(X509Certificate2 Certificate, string Reason, string Location, string Contact, bool Visible, string? TimestampUrl = null);

    public static SignChoice? Sign(Window? owner, IReadOnlyList<X509Certificate2> certificates)
    {
        var f = new FormBuilder(owner, "Sign the Document", 560);
        f.Note("The document is signed with a certificate on this computer. After signing, any change to the file shows that it was altered. The signed copy is saved as a new file.");
        var names = certificates.Select(c => $"{c.GetNameInfo(X509NameType.SimpleName, false)}   (valid until {c.NotAfter:d})").ToList();
        var which = f.Choice("Sign as", names);
        var reason = f.Text("Reason (optional)", "");
        var location = f.Text("Place (optional)", "");
        var contact = f.Text("Contact (optional)", "");
        var visible = f.Check("Show a signature box on the page (you will drag where it goes)", true);
        var stamp = f.Check("Add a trusted time stamp (contacts a time stamping service; only a fingerprint of the signature is sent)");
        var stampUrl = f.Text("Time stamping service address", "http://timestamp.digicert.com");
        stampUrl.IsEnabled = false;
        stamp.Click += (_, _) => stampUrl.IsEnabled = stamp.IsChecked == true;
        SignChoice? result = null;
        if (!f.Show("Continue", () =>
        {
            if (which.SelectedIndex < 0) { Dialogs.Info(f.Window, "Sign", "Choose a certificate."); return false; }
            result = new SignChoice(certificates[which.SelectedIndex], reason.Text.Trim(), location.Text.Trim(), contact.Text.Trim(), visible.IsChecked == true, stamp.IsChecked == true ? stampUrl.Text.Trim() : null);
            return true;
        })) return null;
        return result;
    }

    public sealed record NewText(string Text, double Size, string Font, PdfColor Color);

    /// <summary>Asks what text to put on the page: words (several lines allowed), font, size and colour.</summary>
    public static NewText? TextToAdd(Window? owner)
    {
        var f = new FormBuilder(owner, "Add Text", 540);
        var text = f.Text("Text (press Enter for a new line)", "");
        text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.Height = 90; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var fam = f.Choice("Font", ["Sans", "Serif", "Fixed width"]);
        var bold = f.Check("Bold");
        var italic = f.Check("Italic");
        var sizeBox = f.Text("Letter size (points)", "12", 100);
        var colour = f.Choice("Colour", TextColours.Skip(1).Select(c => c.Name));
        NewText? result = null;
        if (!f.Show("Add", () =>
        {
            if (string.IsNullOrWhiteSpace(text.Text)) { Dialogs.Info(f.Window, "Add Text", "Type the text to add."); return false; }
            if (!double.TryParse(sizeBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && !double.TryParse(sizeBox.Text, out s))
            { Dialogs.Info(f.Window, "Add Text", "Type a letter size, such as 12."); return false; }
            if (s < 4 || s > 200) { Dialogs.Info(f.Window, "Add Text", "Type a letter size between 4 and 200."); return false; }
            var family = fam.SelectedIndex == 1 ? "Serif" : fam.SelectedIndex == 2 ? "Fixed" : "Sans";
            result = new NewText(text.Text.Replace("\r\n", "\n"), s, JoinFont(family, bold.IsChecked == true, italic.IsChecked == true), TextColours[colour.SelectedIndex + 1].Color!.Value);
            return true;
        })) return null;
        return result;
    }

    /// <summary>A window that shows the result of comparing two documents, page by page, with added words in green and removed words in red.</summary>
    public static void ShowComparison(Window? owner, string nameA, string nameB, ComparisonResult result)
    {
        var w = new Window { Title = "Compare Documents", Width = 900, Height = 640, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var dock = new DockPanel { Margin = new Thickness(16) };
        var summary = result.Identical
            ? "The two documents have the same words on every page."
            : $"{result.TotalRemoved} word(s) removed and {result.TotalAdded} added. {nameA} has {result.PagesA} page(s); {nameB} has {result.PagesB}.";
        var head = new TextBlock { Text = summary, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        var legend = new TextBlock { Margin = new Thickness(0, 0, 0, 10) };
        legend.Inlines.Add(new Run("Removed from the first: ") { Foreground = Brushes.Gray });
        legend.Inlines.Add(new Run("red, struck through") { Foreground = Brushes.IndianRed, TextDecorations = TextDecorations.Strikethrough });
        legend.Inlines.Add(new Run("     Added in the second: ") { Foreground = Brushes.Gray });
        legend.Inlines.Add(new Run("green, underlined") { Foreground = Brushes.SeaGreen, TextDecorations = TextDecorations.Underline });
        var top = new StackPanel(); top.Children.Add(head); top.Children.Add(legend);
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        var doc = new FlowDocument { PagePadding = new Thickness(8), FontSize = 14 };
        foreach (var page in result.Pages.Where(p => !p.Identical))
        {
            var title = page.PageA < 0 ? $"Page {page.PageB + 1} was added" : page.PageB < 0 ? $"Page {page.PageA + 1} was removed" : page.PageA == page.PageB ? $"Page {page.PageA + 1}" : $"Page {page.PageA + 1} / {page.PageB + 1}";
            var heading = new Paragraph(new Run(title) { FontWeight = FontWeights.Bold, FontSize = 16 }) { Margin = new Thickness(0, 12, 0, 4) };
            doc.Blocks.Add(heading);
            var p = new Paragraph();
            // show the changes with a little of the unchanged text around them
            var changes = page.Changes;
            for (var i = 0; i < changes.Count; i++)
            {
                var c = changes[i];
                if (c.Kind == ChangeKind.Same)
                {
                    var near = (i > 0 && changes[i - 1].Kind != ChangeKind.Same) || (i + 1 < changes.Count && changes[i + 1].Kind != ChangeKind.Same);
                    var close = Enumerable.Range(Math.Max(0, i - 6), Math.Min(13, changes.Count - Math.Max(0, i - 6))).Any(k => changes[k].Kind != ChangeKind.Same);
                    if (near || close) p.Inlines.Add(new Run(c.Text + " ") { Foreground = Brushes.Gray });
                    else if (i > 0 && p.Inlines.LastInline is Run lr && lr.Text != "… ") p.Inlines.Add(new Run("… ") { Foreground = Brushes.Gray });
                }
                else if (c.Kind == ChangeKind.Removed) p.Inlines.Add(new Run(c.Text + " ") { Foreground = Brushes.IndianRed, TextDecorations = TextDecorations.Strikethrough });
                else p.Inlines.Add(new Run(c.Text + " ") { Foreground = Brushes.SeaGreen, TextDecorations = TextDecorations.Underline });
            }
            doc.Blocks.Add(p);
        }
        dock.Children.Add(new FlowDocumentScrollViewer { Document = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        w.Content = dock;
        w.ShowDialog();
    }
}
