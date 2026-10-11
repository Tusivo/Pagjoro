// SPDX-License-Identifier: AGPL-3.0-or-later
using Pagjoro.Core.Pdf;

namespace Pagjoro.Tests;

public class BatchTests
{
    sealed class FakeRecoder : IImageRecoder
    {
        public (int Width, int Height, int Channels, byte[] Pixels)? DecodeJpeg(byte[] jpeg) => null;
        public byte[] EncodeJpeg(byte[] pixels, int width, int height, int channels, int quality) => new byte[Math.Max(50, pixels.Length / 20)];
    }

    static BatchStep Step(string action, params (string, string)[] settings) => new(action, settings.ToDictionary(s => s.Item1, s => s.Item2));

    [Fact]
    public void A_sequence_is_applied_in_order_and_survives_saving()
    {
        byte[] input;
        using (var f = PdfFileTests.Sample(3)) input = f.ToBytes();
        var steps = new[]
        {
            Step("watermark", ("text", "INTERNAL")),
            Step("bates", ("prefix", "ABC-"), ("digits", "4")),
            Step("info", ("title", "Batch Title"), ("author", "Pagjoro")),
            Step("password", ("open", "secret"), ("owner", "boss"), ("modify", "no")),
        };
        var json = BatchRunner.Serialize(steps);
        var loaded = BatchRunner.Deserialize(json);
        Assert.Equal(steps.Length, loaded.Count);

        var output = BatchRunner.Run(input, loaded, null, "input.pdf");
        Assert.Throws<PdfPasswordRequiredException>(() => PdfFile.FromBytes(output));
        using var opened = PdfFile.Open(output, "secret");
        Assert.Equal(3, opened.PageCount);
        var text = opened.GetPageText(0);
        Assert.Contains("INTERNAL", text);
        Assert.Contains("ABC-0001", text);
        Assert.Contains("ABC-0003", opened.GetPageText(2));
    }

    [Fact]
    public void Unknown_steps_and_bad_files_are_refused()
    {
        Assert.Throws<PdfException>(() => BatchRunner.Deserialize("{\"steps\":[{\"action\":\"explode\"}]}"));
        Assert.Throws<PdfException>(() => BatchRunner.Deserialize("not json"));
        using var f = PdfFileTests.Sample(1);
        Assert.Throws<PdfException>(() => BatchRunner.Run(f.ToBytes(), [Step("explode")]));
    }

    [Fact]
    public void Form_data_goes_out_and_comes_back()
    {
        var bytes = PdfForms.AddTextField(PdfFileTests.Sample(1).ToBytes(), 0, new PageRect(72, 400, 300, 424), "full_name");
        bytes = PdfForms.AddCheckBox(bytes, 0, new PageRect(72, 360, 92, 380), "agree");
        bytes = PdfForms.Fill(bytes, new Dictionary<string, string> { ["full_name"] = "Ada, \"Countess\" Lovelace" }, new Dictionary<string, bool> { ["agree"] = true });

        var fields = PdfForms.Read(bytes);
        var json = FormData.ToJson(fields);
        var csv = FormData.ToCsv(fields);
        Assert.Contains("\"Ada, \"\"Countess\"\" Lovelace\"", csv);

        var blank = PdfForms.AddCheckBox(PdfForms.AddTextField(PdfFileTests.Sample(1).ToBytes(), 0, new PageRect(72, 400, 300, 424), "full_name"), 0, new PageRect(72, 360, 92, 380), "agree");
        var (values, checks) = FormData.FromJson(json);
        values["no_such_field"] = "ignored";
        var (filled, count) = FormData.Apply(blank, values, checks);
        Assert.Equal(2, count);
        var after = PdfForms.Read(filled);
        Assert.Equal("Ada, \"Countess\" Lovelace", after.First(f => f.Name == "full_name").Value);
        Assert.True(after.First(f => f.Name == "agree").Checked);
        Assert.Throws<PdfException>(() => FormData.FromJson("[1,2]"));
    }

    [Fact]
    public void The_optimizer_swaps_large_flate_pictures_for_smaller_ones_and_never_grows_a_file()
    {
        var w = 256; var h = 256;
        var pixels = new byte[w * h * 4];
        var rnd = new Random(7);
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)rnd.Next(256); pixels[i + 1] = (byte)rnd.Next(256); pixels[i + 2] = (byte)rnd.Next(256); pixels[i + 3] = 255; }
        byte[] input;
        using (var f = PdfFileTests.Sample(1)) { f.AddImage(0, pixels, w, h, new PageRect(100, 100, 400, 400)); input = f.ToBytes(); }

        var (output, report) = PdfOptimizer.Optimize(input, new OptimizeOptions(60, 150), new FakeRecoder());
        Assert.True(output.Length <= input.Length);
        if (report.PicturesChanged > 0) Assert.True(output.Length < input.Length);
        using var again = PdfFile.FromBytes(output);   // still a valid file
        Assert.Equal(1, again.PageCount);
    }
}
