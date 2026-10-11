// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;

namespace Pagjoro.Core.Pdf;

/// <summary>Saves what is typed in a form to a file (JSON or CSV) and fills a form from such a file.</summary>
public static class FormData
{
    public static string ToJson(IReadOnlyList<FormField> fields)
    {
        var map = new Dictionary<string, object>();
        foreach (var f in fields)
        {
            if (f.Kind is FormFieldKind.Signature or FormFieldKind.Button) continue;
            map[f.Name] = f.Kind == FormFieldKind.CheckBox ? f.Checked : f.Value;
        }
        return JsonSerializer.Serialize(new { pagjoroForm = 1, fields = map }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>One header row of field names and one row of values.</summary>
    public static string ToCsv(IReadOnlyList<FormField> fields)
    {
        var use = fields.Where(f => f.Kind is not (FormFieldKind.Signature or FormFieldKind.Button)).ToList();
        static string Q(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder();
        sb.AppendJoin(',', use.Select(f => Q(f.Name))).Append("\r\n");
        sb.AppendJoin(',', use.Select(f => Q(f.Kind == FormFieldKind.CheckBox ? (f.Checked ? "true" : "false") : f.Value))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Values and tick-box states from a JSON file made by <see cref="ToJson"/> (or a plain object of names and values).</summary>
    public static (Dictionary<string, string> Values, Dictionary<string, bool> Checks) FromJson(string json)
    {
        var values = new Dictionary<string, string>(); var checks = new Dictionary<string, bool>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("fields", out var inner) && inner.ValueKind == JsonValueKind.Object) root = inner;
            if (root.ValueKind != JsonValueKind.Object) throw new PdfException("That is not a form data file.");
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name == "pagjoroForm") continue;
                switch (p.Value.ValueKind)
                {
                    case JsonValueKind.True: checks[p.Name] = true; break;
                    case JsonValueKind.False: checks[p.Name] = false; break;
                    case JsonValueKind.String: values[p.Name] = p.Value.GetString() ?? ""; break;
                    case JsonValueKind.Number: values[p.Name] = p.Value.GetRawText(); break;
                }
            }
        }
        catch (JsonException) { throw new PdfException("That is not a form data file."); }
        return (values, checks);
    }

    /// <summary>Applies loaded data to a document, ignoring names the form does not have. Returns the document and how many fields were set.</summary>
    public static (byte[] Pdf, int FieldsSet) Apply(byte[] pdf, Dictionary<string, string> values, Dictionary<string, bool> checks)
    {
        var fields = PdfForms.Read(pdf);
        var names = fields.ToDictionary(f => f.Name, f => f.Kind, StringComparer.Ordinal);
        var v = values.Where(kv => names.TryGetValue(kv.Key, out var k) && k != FormFieldKind.CheckBox).ToDictionary(kv => kv.Key, kv => kv.Value);
        var c = checks.Where(kv => names.TryGetValue(kv.Key, out var k) && k == FormFieldKind.CheckBox).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (v.Count + c.Count == 0) return (pdf, 0);
        return (PdfForms.Fill(pdf, v, c), v.Count + c.Count);
    }
}
