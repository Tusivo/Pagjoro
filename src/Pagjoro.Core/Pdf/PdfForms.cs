// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace Pagjoro.Core.Pdf;

public enum FormFieldKind { Text, CheckBox, RadioButton, ComboBox, ListBox, Button, Signature, Other }

/// <summary>A fillable field of a form.</summary>
public sealed record FormField(string Name, FormFieldKind Kind, string Value, bool Checked, bool ReadOnly, IReadOnlyList<string> Options);

/// <summary>Reading, filling and making interactive forms (AcroForm fields).</summary>
public static class PdfForms
{
    static PdfDocument Load(byte[] pdf)
    {
        try { return PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify); }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotImplementedException or ArgumentException)
        { throw new PdfException("Pagjoro could not read this file's form (" + ex.Message + ")."); }
    }

    static byte[] Save(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    static IEnumerable<PdfAcroField> Leaves(PdfAcroField.PdfAcroFieldCollection fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (f.HasKids && f.Fields.Count > 0 && f is not PdfRadioButtonField && f is not PdfCheckBoxField)
                foreach (var k in Leaves(f.Fields)) yield return k;
            else yield return f;
        }
    }

    static string? FullName(PdfAcroField f)
    {
        var parts = new List<string>();
        PdfDictionary? d = f;
        for (var depth = 0; d is not null && depth < 12; depth++)
        {
            var t = d.Elements.GetString("/T");
            if (!string.IsNullOrEmpty(t)) parts.Insert(0, t);
            d = d.Elements.GetDictionary("/Parent");
        }
        return parts.Count == 0 ? null : string.Join(".", parts);
    }

    /// <summary>All fields in the form, in the order they are defined.</summary>
    public static IReadOnlyList<FormField> Read(byte[] pdf)
    {
        WindowsFonts.EnsureRegistered();
        using var doc = Load(pdf);
        var result = new List<FormField>();
        if (!doc.Internals.Catalog.Elements.ContainsKey("/AcroForm")) return result;
        var form = doc.AcroForm;
        foreach (var f in Leaves(form.Fields))
        {
            var name = FullName(f) ?? f.Name;
            var ro = f.ReadOnly;
            switch (f)
            {
                case PdfTextField t: result.Add(new FormField(name, FormFieldKind.Text, t.Text ?? "", false, ro, [])); break;
                case PdfCheckBoxField c: result.Add(new FormField(name, FormFieldKind.CheckBox, "", c.Checked, ro, [])); break;
                case PdfRadioButtonField r: result.Add(new FormField(name, FormFieldKind.RadioButton, r.Value?.ToString() ?? "", false, ro, OptionsOf(r))); break;
                case PdfComboBoxField cb: result.Add(new FormField(name, FormFieldKind.ComboBox, cb.Value?.ToString() ?? "", false, ro, OptionsOf(cb))); break;
                case PdfListBoxField lb: result.Add(new FormField(name, FormFieldKind.ListBox, lb.Value?.ToString() ?? "", false, ro, OptionsOf(lb))); break;
                case PdfSignatureField: result.Add(new FormField(name, FormFieldKind.Signature, "", false, ro, [])); break;
                case PdfPushButtonField: result.Add(new FormField(name, FormFieldKind.Button, "", false, ro, [])); break;
                default: result.Add(new FormField(name, FormFieldKind.Other, f.Value?.ToString() ?? "", false, ro, [])); break;
            }
        }
        return result;
    }

    static IReadOnlyList<string> OptionsOf(PdfAcroField f)
    {
        var list = new List<string>();
        var opt = f.Elements.GetArray("/Opt");
        if (opt is null) return list;
        for (var i = 0; i < opt.Elements.Count; i++)
        {
            switch (opt.Elements[i])
            {
                case PdfString s: list.Add(s.Value); break;
                case PdfArray pair when pair.Elements.Count > 1 && pair.Elements[1] is PdfString label: list.Add(label.Value); break;
            }
        }
        return list;
    }

    /// <summary>Sets values: text and choice fields from <paramref name="values"/>, check boxes from <paramref name="checks"/>.</summary>
    public static byte[] Fill(byte[] pdf, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, bool> checks)
    {
        WindowsFonts.EnsureRegistered();
        using var doc = Load(pdf);
        if (!doc.Internals.Catalog.Elements.ContainsKey("/AcroForm")) throw new PdfException("This document has no form fields to fill in.");
        var form = doc.AcroForm;
        foreach (var f in Leaves(form.Fields))
        {
            var name = FullName(f) ?? f.Name;
            if (f.ReadOnly) continue;
            switch (f)
            {
                case PdfTextField t when values.TryGetValue(name, out var v):
                    t.Text = v;
                    SetAppearance(doc, t, v);
                    break;
                case PdfCheckBoxField c when checks.TryGetValue(name, out var on):
                    c.Checked = on;
                    break;
                case PdfComboBoxField cb when values.TryGetValue(name, out var cv): cb.Value = new PdfString(cv); break;
                case PdfRadioButtonField or PdfListBoxField when values.TryGetValue(name, out var rv): f.Value = new PdfString(rv); break;
            }
        }
        // appearances are written above, so readers need not regenerate them
        form.Elements.Remove("/NeedAppearances");
        return Save(doc);
    }

    // ---- making fields ----------------------------------------------------------------------------------------------

    /// <summary>Adds a text box the person can type in.</summary>
    public static byte[] AddTextField(byte[] pdf, int pageIndex, PageRect area, string name, string initialValue = "")
    {
        using var doc = Load(pdf);
        var widget = NewWidget(doc, pageIndex, area, "/Tx", name);
        widget.Elements.SetString("/DA", "/Helv 11 Tf 0 g");
        widget.Elements.SetString("/V", initialValue);
        SetAppearance(doc, widget, initialValue, area.Normalized().Width, area.Normalized().Height);
        return Save(doc);
    }

    /// <summary>Adds a check box.</summary>
    public static byte[] AddCheckBox(byte[] pdf, int pageIndex, PageRect area, string name, bool isChecked = false)
    {
        using var doc = Load(pdf);
        var w = NewWidget(doc, pageIndex, area, "/Btn", name);
        var r = area.Normalized();
        var size = Math.Min(r.Width, r.Height);
        w.Elements.SetName("/AS", isChecked ? "/Yes" : "/Off");
        w.Elements.SetName("/V", isChecked ? "/Yes" : "/Off");
        var off = FormStream(doc, r.Width, r.Height, Box(r.Width, r.Height, 0.5));
        var on = FormStream(doc, r.Width, r.Height, Box(r.Width, r.Height, 0.5) + $" 0 0 0 RG 2 w {F(size * 0.2)} {F(size * 0.5)} m {F(size * 0.42)} {F(size * 0.22)} l {F(size * 0.82)} {F(size * 0.8)} l S");
        var n = new PdfDictionary(doc);
        n.Elements["/Off"] = off.Reference!;
        n.Elements["/Yes"] = on.Reference!;
        var ap = new PdfDictionary(doc);
        ap.Elements["/N"] = n;
        w.Elements["/AP"] = ap;
        return Save(doc);
    }

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static string Box(double w, double h, double line) =>
        $"q 0.93 0.96 1 rg 0 0 {F(w)} {F(h)} re f 0.2 0.3 0.5 RG {F(line)} w {F(line / 2)} {F(line / 2)} {F(w - line)} {F(h - line)} re S Q";

    static PdfDictionary FormStream(PdfDocument doc, double w, double h, string content)
    {
        var form = new PdfDictionary(doc);
        form.Elements.SetName("/Type", "/XObject");
        form.Elements.SetName("/Subtype", "/Form");
        form.Elements.SetRectangle("/BBox", new PdfRectangle(new XPoint(0, 0), new XPoint(w, h)));
        form.Elements["/Resources"] = FontResources(doc);
        form.CreateStream(Encoding.Latin1.GetBytes(content));
        doc.Internals.AddObject(form);
        return form;
    }

    static PdfDictionary FontResources(PdfDocument doc)
    {
        var font = new PdfDictionary(doc);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        var fonts = new PdfDictionary(doc);
        fonts.Elements["/Helv"] = font;
        var res = new PdfDictionary(doc);
        res.Elements["/Font"] = fonts;
        return res;
    }

    static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 32 || c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static void SetAppearance(PdfDocument doc, PdfDictionary field, string value, double? w = null, double? h = null)
    {
        var rect = field.Elements.GetRectangle("/Rect");
        var width = w ?? rect.Width; var height = h ?? rect.Height;
        var text = value.Length == 0 ? "" : $"/Tx BMC q BT /Helv 11 Tf 0 g 3 {F(Math.Max(2, (height - 11) / 2 + 2))} Td ({Escape(value)}) Tj ET Q EMC";
        var stream = FormStream(doc, width, height, Box(width, height, 0.75) + " " + text);
        var ap = new PdfDictionary(doc);
        ap.Elements["/N"] = stream.Reference!;
        field.Elements["/AP"] = ap;
    }

    static PdfDictionary NewWidget(PdfDocument doc, int pageIndex, PageRect area, string fieldType, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new PdfException("Give the field a name.");
        if (pageIndex < 0 || pageIndex >= doc.PageCount) throw new PdfException("That page does not exist.");
        var r = area.Normalized();
        if (r.Width < 6 || r.Height < 6) throw new PdfException("The box is too small. Drag a larger area.");
        var page = doc.Pages[pageIndex];
        var widget = new PdfDictionary(doc);
        widget.Elements.SetName("/Type", "/Annot");
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements.SetName("/FT", fieldType);
        widget.Elements.SetString("/T", name.Trim());
        widget.Elements.SetRectangle("/Rect", new PdfRectangle(new XPoint(r.Left, r.Bottom), new XPoint(r.Right, r.Top)));
        widget.Elements.SetInteger("/F", 4);
        doc.Internals.AddObject(widget);
        widget.Elements["/P"] = page.Reference!;

        var annots = page.Elements.GetArray("/Annots");
        if (annots is null) { annots = new PdfArray(doc); page.Elements["/Annots"] = annots; }
        annots.Elements.Add(widget.Reference!);

        var catalog = doc.Internals.Catalog;
        var form = catalog.Elements.GetDictionary("/AcroForm");
        if (form is null)
        {
            form = new PdfDictionary(doc);
            doc.Internals.AddObject(form);
            catalog.Elements["/AcroForm"] = form.Reference!;
            form.Elements["/Fields"] = new PdfArray(doc);
            form.Elements["/DR"] = FontResources(doc);
            form.Elements.SetString("/DA", "/Helv 0 Tf 0 g");
        }
        var fields = form.Elements.GetArray("/Fields");
        if (fields is null) { fields = new PdfArray(doc); form.Elements["/Fields"] = fields; }
        fields.Elements.Add(widget.Reference!);
        return widget;
    }
}
