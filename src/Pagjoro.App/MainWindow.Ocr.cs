// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public partial class MainWindow
{
    async void OnRecognizeText(object sender, RoutedEventArgs e)
    {
        if (doc is null) { MessageStatus.Text = "Open a PDF first."; return; }
        var languages = TextRecognition.AvailableLanguages();
        if (languages.Count == 0)
        {
            Dialogs.Info(this, "Recognize Text",
                "Windows has no text recognition language installed for this account.\n\n" +
                "Open Windows Settings, Time & language, Language & region, and add a language (or use \"Language options\" on one you already have and install its optional features). Then try again.", error: true);
            return;
        }

        var f = new FormBuilder(this, "Recognize Text (OCR)", 520);
        f.Note("Makes scanned pages searchable and copyable by reading the words in the picture with the text recognition built into Windows. Nothing is uploaded. The page looks the same afterwards: the words are added as hidden text.");
        var which = f.Choice("Which pages", ["Only pages that have no text yet (recommended)", "Every page"]);
        var language = f.Choice("Language of the document", languages);
        if (!f.Show("Recognize")) return;

        var engine = TextRecognition.Create(languages[language.SelectedIndex]);
        if (engine is null) { Dialogs.Info(this, "Recognize Text", "Windows could not start text recognition for that language.", error: true); return; }

        var file = doc;
        var pages = new List<int>();
        for (var p = 0; p < file.PageCount; p++)
            if (which.SelectedIndex == 1 || file.GetPageText(p).Trim().Length == 0) pages.Add(p);
        if (pages.Count == 0) { Dialogs.Info(this, "Recognize Text", "Every page already has text, so there is nothing to recognize."); return; }

        PushUndo();
        var words = 0;
        int completed;
        try
        {
            completed = await ProgressWindow.RunAsync(this, "Recognize Text", pages.Count, async (i, _) =>
            {
                var n = await engine.RecognizePageAsync(file, pages[i]);
                words += n;
                return $"Page {pages[i] + 1} ({i + 1} of {pages.Count}): {n} words found";
            });
        }
        catch (Exception ex)
        {
            if (undo.Count > 0 && ReferenceEquals(doc, file)) { Restore(undo[^1]); undo.RemoveAt(undo.Count - 1); }
            Dialogs.Info(this, "Recognize Text", "Text recognition stopped: " + ex.Message, error: true);
            return;
        }
        View.Refresh(); AfterEdit();
        MessageStatus.Text = completed < pages.Count
            ? $"Stopped after {completed} of {pages.Count} page(s); {words} words were added. Save to keep them."
            : $"Recognized {words} words on {completed} page(s). Search and copy now work on them. Save to keep the change.";
    }
}
