// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;

namespace Pagjoro.Core.Pdf;

/// <summary>One action in a saved sequence: a name and its settings, all as text so a sequence can be written to a file and read back.</summary>
public sealed record BatchStep(string Action, Dictionary<string, string> Settings)
{
    public string Get(string key, string fallback = "") => Settings.TryGetValue(key, out var v) ? v : fallback;
    public int GetInt(string key, int fallback) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    public double GetDouble(string key, double fallback) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    public bool GetBool(string key, bool fallback = false) => Settings.TryGetValue(key, out var v) ? v is "true" or "1" or "yes" : fallback;
}

/// <summary>A description of an action that can appear in a sequence, for building the list in the window.</summary>
public sealed record BatchAction(string Id, string Title, string Help, IReadOnlyList<BatchSetting> Settings);

public sealed record BatchSetting(string Key, string Label, string Default, IReadOnlyList<string>? Choices = null);

/// <summary>Runs a sequence of actions (a "batch") on files, and saves and loads sequences.</summary>
public static class BatchRunner
{
    public static readonly IReadOnlyList<BatchAction> Actions =
    [
        new("watermark", "Add a watermark", "Large light words across each page.",
            [new("text", "Words", "CONFIDENTIAL"), new("size", "Letter size", "64"), new("colour", "Colour", "Grey", ["Grey", "Red", "Blue"]), new("angle", "Direction", "Diagonal", ["Diagonal", "Straight across"])]),
        new("headerfooter", "Add a header or footer", "{page}, {pages}, {date} and {file} are filled in for each page.",
            [new("topleft", "Top left", ""), new("topcenter", "Top centre", ""), new("topright", "Top right", ""),
             new("bottomleft", "Bottom left", ""), new("bottomcenter", "Bottom centre", "Page {page} of {pages}"), new("bottomright", "Bottom right", ""), new("size", "Letter size", "10")]),
        new("bates", "Add Bates numbers", "A running number on every page.",
            [new("prefix", "Before the number", ""), new("start", "First number", "1"), new("digits", "Digits", "6"), new("suffix", "After the number", "")]),
        new("rotate", "Turn all pages", "Turns every page the same way.", [new("degrees", "Degrees clockwise", "90", ["90", "180", "270"])]),
        new("flatten", "Flatten marks and form fields", "Makes comments and fields part of the page.", []),
        new("sanitize", "Remove hidden data", "Scripts, attachments, metadata and hidden layers.", []),
        new("optimize", "Reduce file size", "Recompresses pictures and repacks the file.",
            [new("quality", "Picture quality (1-100)", "70"), new("maxdpi", "Largest picture detail (dpi, 0 keeps all)", "150")]),
        new("info", "Set document information", "Title, author, subject and keywords.",
            [new("title", "Title", ""), new("author", "Author", ""), new("subject", "Subject", ""), new("keywords", "Keywords", "")]),
        new("language", "Set the document language", "For screen readers, for example en-US.", [new("language", "Language code", "en-US"), new("showtitle", "Show the title in the window bar", "yes", ["yes", "no"])]),
        new("password", "Protect with a password", "Always keep this step last.",
            [new("open", "Password to open (optional)", ""), new("owner", "Owner password", ""), new("print", "Allow printing", "yes", ["yes", "no"]),
             new("copy", "Allow copying", "yes", ["yes", "no"]), new("modify", "Allow changing", "no", ["yes", "no"])]),
    ];

    /// <summary>Applies the steps in order to one document and returns the result.</summary>
    /// <param name="fileName">The name shown for {file} in headers and footers.</param>
    public static byte[] Run(byte[] pdf, IReadOnlyList<BatchStep> steps, IImageRecoder? recoder = null, string fileName = "")
    {
        var current = pdf;
        foreach (var step in steps) current = Apply(current, step, recoder, fileName);
        return current;
    }

    static byte[] Apply(byte[] pdf, BatchStep step, IImageRecoder? recoder, string fileName)
    {
        switch (step.Action)
        {
            case "watermark":
                {
                    using var f = PdfFile.FromBytes(pdf);
                    var colour = step.Get("colour", "Grey") switch { "Red" => new PdfColor(200, 30, 30), "Blue" => new PdfColor(30, 80, 200), _ => new PdfColor(110, 110, 110) };
                    var angle = step.Get("angle", "Diagonal") == "Straight across" ? 0 : 45;
                    PageDecorations.Watermark(f, Enumerable.Range(0, f.PageCount), new WatermarkOptions(step.Get("text", "CONFIDENTIAL"), step.GetDouble("size", 64), colour, angle));
                    return f.ToBytes();
                }
            case "headerfooter":
                {
                    using var f = PdfFile.FromBytes(pdf, fileName.Length > 0 ? fileName : null);
                    PageDecorations.HeaderFooter(f, Enumerable.Range(0, f.PageCount), new HeaderFooterOptions(
                        step.Get("topleft"), step.Get("topcenter"), step.Get("topright"), step.Get("bottomleft"), step.Get("bottomcenter"), step.Get("bottomright"), step.GetDouble("size", 10)));
                    return f.ToBytes();
                }
            case "bates":
                {
                    using var f = PdfFile.FromBytes(pdf);
                    PageDecorations.Bates(f, Enumerable.Range(0, f.PageCount), new BatesOptions(step.Get("prefix"), step.GetInt("start", 1), step.GetInt("digits", 6), step.Get("suffix")));
                    return f.ToBytes();
                }
            case "rotate":
                {
                    using var f = PdfFile.FromBytes(pdf);
                    for (var i = 0; i < f.PageCount; i++) f.RotatePage(i, step.GetInt("degrees", 90));
                    return f.ToBytes();
                }
            case "flatten":
                {
                    using var f = PdfFile.FromBytes(pdf);
                    for (var i = 0; i < f.PageCount; i++) f.Flatten(i);
                    return f.ToBytes();
                }
            case "sanitize": return PdfProtection.Sanitize(pdf).Pdf;
            case "optimize":
                if (recoder is null) return PdfProtection.Recompress(pdf);
                return PdfOptimizer.Optimize(pdf, new OptimizeOptions(Math.Clamp(step.GetInt("quality", 70), 1, 100), Math.Max(0, step.GetInt("maxdpi", 150))), recoder).Pdf;
            case "info":
                return PdfProtection.SetInformation(pdf, step.Get("title"), step.Get("author"), step.Get("subject"), step.Get("keywords"));
            case "language":
                return PdfStructure.SetReadingOptions(pdf, step.Get("language", "en-US"), step.GetBool("showtitle", true), null);
            case "password":
                return PdfProtection.Encrypt(pdf, step.Get("open"), step.Get("owner"),
                    new PdfPermissions(Print: step.GetBool("print", true), Modify: step.GetBool("modify"), Copy: step.GetBool("copy", true)));
            default:
                throw new PdfException("This sequence has a step Pagjoro does not know: " + step.Action);
        }
    }

    // ---- saving and loading a sequence -----------------------------------------------------------------------------------

    public static string Serialize(IReadOnlyList<BatchStep> steps) =>
        JsonSerializer.Serialize(new { pagjoroSequence = 1, steps = steps.Select(s => new { action = s.Action, settings = s.Settings }) }, new JsonSerializerOptions { WriteIndented = true });

    public static IReadOnlyList<BatchStep> Deserialize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array) throw new PdfException("That is not a Pagjoro sequence file.");
            var list = new List<BatchStep>();
            foreach (var s in steps.EnumerateArray())
            {
                var action = s.GetProperty("action").GetString() ?? "";
                if (Actions.All(a => a.Id != action)) throw new PdfException("The sequence uses a step Pagjoro does not know: " + action);
                var settings = new Dictionary<string, string>();
                if (s.TryGetProperty("settings", out var st) && st.ValueKind == JsonValueKind.Object)
                    foreach (var p in st.EnumerateObject()) settings[p.Name] = p.Value.GetString() ?? "";
                list.Add(new BatchStep(action, settings));
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new PdfException("That is not a Pagjoro sequence file.");
        }
    }
}
