// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public partial class MainWindow
{
    /// <summary>Edit the settings of one step in a sequence. Returns null when cancelled.</summary>
    BatchStep? EditStep(Window owner, BatchAction action, BatchStep? existing)
    {
        var f = new FormBuilder(owner, action.Title, 520);
        f.Note(action.Help);
        var boxes = new Dictionary<string, Func<string>>();
        foreach (var s in action.Settings)
        {
            var current = existing is not null && existing.Settings.TryGetValue(s.Key, out var v) ? v : s.Default;
            if (s.Choices is { Count: > 0 })
            {
                var combo = f.Choice(s.Label, s.Choices, Math.Max(0, s.Choices.ToList().IndexOf(current)));
                boxes[s.Key] = () => combo.SelectedItem as string ?? s.Default;
            }
            else
            {
                var tb = f.Text(s.Label, current);
                boxes[s.Key] = () => tb.Text;
            }
        }
        if (action.Settings.Count == 0) f.Note("This step has no settings.");
        if (!f.Show("OK")) return null;
        return new BatchStep(action.Id, boxes.ToDictionary(kv => kv.Key, kv => kv.Value()));
    }

    static string DescribeStep(BatchStep step)
    {
        var a = BatchRunner.Actions.FirstOrDefault(x => x.Id == step.Action);
        if (a is null) return step.Action;
        var detail = string.Join(", ", step.Settings.Where(kv => kv.Key is not "open" and not "owner" && !string.IsNullOrWhiteSpace(kv.Value)).Take(3).Select(kv => kv.Value));
        return detail.Length == 0 ? a.Title : $"{a.Title}  ({detail})";
    }

    void OnBatch(object sender, RoutedEventArgs e)
    {
        var files = new List<string>();
        var steps = new List<BatchStep>();
        var w = new Window { Title = "Batch Process Files", Width = 820, Height = 640, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // left: files
        var fileHead = new System.Windows.Controls.TextBlock { Text = "1. The files to process", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        var fileList = new ListBox { SelectionMode = SelectionMode.Extended };
        var fileButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        Button Mini(string text, Action act, Panel into) { var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) }; b.Click += (_, _) => act(); into.Children.Add(b); return b; }
        void RefillFiles() { fileList.Items.Clear(); foreach (var f in files) fileList.Items.Add(f); }
        Mini("Add files...", () =>
        {
            var d = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Multiselect = true, Title = "Choose the PDF files" };
            if (d.ShowDialog(w) != true) return;
            foreach (var f in d.FileNames) if (!files.Contains(f)) files.Add(f);
            RefillFiles();
        }, fileButtons);
        Mini("Remove", () => { foreach (var s in fileList.SelectedItems.Cast<string>().ToList()) files.Remove(s); RefillFiles(); }, fileButtons);
        Mini("Clear", () => { files.Clear(); RefillFiles(); }, fileButtons);
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); left.RowDefinitions.Add(new RowDefinition()); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(fileHead); Grid.SetRow(fileList, 1); left.Children.Add(fileList); Grid.SetRow(fileButtons, 2); left.Children.Add(fileButtons);
        Grid.SetRow(left, 0); Grid.SetRowSpan(left, 2); Grid.SetColumn(left, 0); root.Children.Add(left);

        // right: steps
        var stepHead = new System.Windows.Controls.TextBlock { Text = "2. What to do to each file, in order", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        var stepList = new ListBox();
        var stepButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void RefillSteps(int sel = -1) { stepList.Items.Clear(); foreach (var s in steps) stepList.Items.Add(DescribeStep(s)); if (sel >= 0 && sel < steps.Count) stepList.SelectedIndex = sel; }
        var actionBox = new ComboBox { ItemsSource = BatchRunner.Actions.Select(a => a.Title).ToList(), SelectedIndex = 0, MinWidth = 220, Margin = new Thickness(0, 0, 6, 6) };
        stepButtons.Children.Add(actionBox);
        Mini("Add", () =>
        {
            var a = BatchRunner.Actions[Math.Max(0, actionBox.SelectedIndex)];
            var s = EditStep(w, a, null);
            if (s is null) return;
            steps.Add(s); RefillSteps(steps.Count - 1);
        }, stepButtons);
        Mini("Change...", () =>
        {
            var i = stepList.SelectedIndex; if (i < 0) return;
            var a = BatchRunner.Actions.First(x => x.Id == steps[i].Action);
            var s = EditStep(w, a, steps[i]); if (s is null) return;
            steps[i] = s; RefillSteps(i);
        }, stepButtons);
        Mini("Remove", () => { var i = stepList.SelectedIndex; if (i < 0) return; steps.RemoveAt(i); RefillSteps(Math.Min(i, steps.Count - 1)); }, stepButtons);
        Mini("Up", () => { var i = stepList.SelectedIndex; if (i > 0) { (steps[i], steps[i - 1]) = (steps[i - 1], steps[i]); RefillSteps(i - 1); } }, stepButtons);
        Mini("Down", () => { var i = stepList.SelectedIndex; if (i >= 0 && i < steps.Count - 1) { (steps[i], steps[i + 1]) = (steps[i + 1], steps[i]); RefillSteps(i + 1); } }, stepButtons);
        var seqButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        Mini("Save this sequence...", () =>
        {
            var d = new SaveFileDialog { Filter = "Pagjoro sequence (*.pagjoro-sequence.json)|*.json", FileName = "My sequence.pagjoro-sequence.json", Title = "Save the sequence" };
            if (d.ShowDialog(w) != true) return;
            try { File.WriteAllText(d.FileName, BatchRunner.Serialize(steps)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Dialogs.Info(w, "Batch", ex.Message, error: true); }
        }, seqButtons);
        Mini("Load a sequence...", () =>
        {
            var d = new OpenFileDialog { Filter = "Pagjoro sequence (*.json)|*.json", Title = "Choose a saved sequence" };
            if (d.ShowDialog(w) != true) return;
            try { var loaded = BatchRunner.Deserialize(File.ReadAllText(d.FileName)); steps.Clear(); steps.AddRange(loaded); RefillSteps(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PdfException) { Dialogs.Info(w, "Batch", ex.Message, error: true); }
        }, seqButtons);
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.Children.Add(stepHead); Grid.SetRow(stepList, 1); right.Children.Add(stepList);
        Grid.SetRow(stepButtons, 2); right.Children.Add(stepButtons); Grid.SetRow(seqButtons, 3); right.Children.Add(seqButtons);
        Grid.SetRow(right, 0); Grid.SetRowSpan(right, 2); Grid.SetColumn(right, 2); root.Children.Add(right);

        // bottom: output
        var outPanel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        outPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "3. Where the results go (the originals are never changed)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var folderRow = new DockPanel();
        var folderBox = new TextBox { Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        var browse = new Button { Content = "Browse...", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) => { var d = new OpenFolderDialog { Title = "Choose the folder for the results" }; if (d.ShowDialog(w) == true) folderBox.Text = d.FolderName; };
        folderRow.Children.Add(browse); folderRow.Children.Add(folderBox);
        outPanel.Children.Add(folderRow);
        var suffixRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        suffixRow.Children.Add(new System.Windows.Controls.TextBlock { Text = "Add these words to each file name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var suffix = new TextBox { Text = " (processed)", Width = 200 };
        suffixRow.Children.Add(suffix);
        outPanel.Children.Add(suffixRow);
        Grid.SetRow(outPanel, 2); Grid.SetColumnSpan(outPanel, 3); root.Children.Add(outPanel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var run = new Button { Content = "Run", IsDefault = true, Padding = new Thickness(22, 6, 22, 6) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(run); buttons.Children.Add(close);
        Grid.SetRow(buttons, 3); Grid.SetColumnSpan(buttons, 3); root.Children.Add(buttons);
        w.Content = root;

        run.Click += async (_, _) =>
        {
            if (files.Count == 0) { Dialogs.Info(w, "Batch", "Add at least one file."); return; }
            if (steps.Count == 0) { Dialogs.Info(w, "Batch", "Add at least one step."); return; }
            var folder = folderBox.Text.Trim();
            if (!Directory.Exists(folder)) { Dialogs.Info(w, "Batch", "Choose a folder that exists for the results."); return; }
            if (suffix.Text.Trim().Length == 0 && files.Any(f => string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase)))
            { Dialogs.Info(w, "Batch", "With no words added to the names, the results would replace the originals in the same folder. Add some words or choose another folder."); return; }
            var failures = new List<string>();
            var snapshot = steps.ToList();
            var done = 0;
            try
            {
                done = await ProgressWindow.RunAsync(w, "Batch Process Files", files.Count, async (i, _) =>
                {
                    var input = files[i];
                    var name = Path.GetFileName(input);
                    await Task.Run(() =>
                    {
                        try
                        {
                            byte[] bytes;
                            using (var f = PdfFile.Open(input)) bytes = f.ToBytes(removeSecurity: true);
                            var result = BatchRunner.Run(bytes, snapshot, recoder, name);
                            var target = Path.Combine(folder, Path.GetFileNameWithoutExtension(input) + suffix.Text + ".pdf");
                            File.WriteAllBytes(target, result);
                        }
                        catch (Exception ex) when (ex is PdfException or IOException or UnauthorizedAccessException) { failures.Add($"{name}: {ex.Message}"); }
                    });
                    return $"{name} ({i + 1} of {files.Count})";
                });
            }
            catch (Exception ex) { Dialogs.Info(w, "Batch", "The batch stopped: " + ex.Message, error: true); return; }
            var summary = $"{done - failures.Count} of {files.Count} file(s) processed into {folder}.";
            if (failures.Count > 0) summary += "\n\nThese could not be processed:\n" + string.Join("\n", failures.Take(10));
            if (done < files.Count) summary += "\n\nThe batch was stopped before the end.";
            Dialogs.Info(w, "Batch Process Files", summary, error: failures.Count > 0);
        };
        w.ShowDialog();
    }

    // ---- reduce file size, with choices -----------------------------------------------------------------------------------

    void OnCompress(object sender, RoutedEventArgs e)
    {
        if (doc is null) return;
        var f = new FormBuilder(this, "Reduce File Size", 520);
        f.Note("Repacks the file and recompresses its pictures. Text and drawings are not changed. Pictures lose some detail at lower quality.");
        var level = f.Choice("How much", ["Light: keep pictures nearly as they are", "Balanced (recommended)", "Smallest: pictures are visibly softer"], 1);
        var meta = f.Check("Also remove the file's descriptive data (XMP metadata)");
        if (!f.Show("Reduce")) return;
        var (quality, dpi) = level.SelectedIndex switch { 0 => (85, 220), 2 => (50, 110), _ => (70, 150) };
        try
        {
            var before = doc.ToBytes(removeSecurity: true);
            var (after, report) = PdfOptimizer.Optimize(before, new OptimizeOptions(quality, dpi, true, meta.IsChecked == true), recoder);
            if (after.Length >= before.Length) { Dialogs.Info(this, "Reduce File Size", "This file is already as small as Pagjoro can make it."); return; }
            PushUndo();
            ReplaceDocument(PdfFile.FromBytes(after),
                $"File size reduced from {before.Length / 1024:N0} KB to {after.Length / 1024:N0} KB ({report.PicturesChanged} picture(s) recompressed). Save to keep it.");
        }
        catch (PdfException ex) { Dialogs.Info(this, "Pagjoro", ex.Message, error: true); }
    }
}
