// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public partial class MainWindow
{
    static readonly string RecoveryDir = Path.Combine(Pagjoro.Core.AppPaths.DataFolder, "recovery");
    static string RecoveryFile => Path.Combine(RecoveryDir, "unsaved.pdf");
    static string RecoveryNote => Path.Combine(RecoveryDir, "unsaved.json");
    DispatcherTimer? recoveryTimer;
    int lastRecoveryVersion = -1;

    /// <summary>A safety copy of unsaved changes is written every little while, so a crash or power cut does not lose the work.</summary>
    void StartRecovery()
    {
        recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        recoveryTimer.Tick += (_, _) => WriteRecovery();
        recoveryTimer.Start();
    }

    void WriteRecovery()
    {
        if (doc is null || !doc.IsModified) return;
        try
        {
            var bytes = doc.ToBytes(removeSecurity: true);
            if (bytes.Length == lastRecoveryVersion) return;   // nothing new since the last copy
            Directory.CreateDirectory(RecoveryDir);
            File.WriteAllBytes(RecoveryFile, bytes);
            File.WriteAllText(RecoveryNote, JsonSerializer.Serialize(new { original = doc.Path, saved = DateTime.Now }));
            lastRecoveryVersion = bytes.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PdfException) { /* a failed safety copy must never get in the way of the work */ }
    }

    void ClearRecovery()
    {
        lastRecoveryVersion = -1;
        try { File.Delete(RecoveryFile); File.Delete(RecoveryNote); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>On start: offers to bring back unsaved work from a session that ended without saving.</summary>
    bool OfferRecovery()
    {
        try
        {
            if (!File.Exists(RecoveryFile)) return false;
            string? original = null; var saved = File.GetLastWriteTime(RecoveryFile);
            try
            {
                using var j = JsonDocument.Parse(File.ReadAllText(RecoveryNote));
                if (j.RootElement.TryGetProperty("original", out var o) && o.ValueKind == JsonValueKind.String) original = o.GetString();
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
            var what = original is null ? "an unsaved document" : Path.GetFileName(original);
            if (!Dialogs.Confirm(this, "Recover Unsaved Work",
                $"Pagjoro found unsaved changes to {what} from {saved:g}, probably from a session that ended unexpectedly.\n\nRestore them now? (Cancel throws them away.)"))
            { ClearRecovery(); return false; }
            var file = PdfFile.FromBytes(File.ReadAllBytes(RecoveryFile), original);
            file.IsModified = true;
            undo.Clear(); redo.Clear();
            SetDocument(file);
            MessageStatus.Text = "Unsaved work restored. Use Save (or Save As) to keep it.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PdfException)
        {
            ClearRecovery();
            return false;
        }
    }
}
