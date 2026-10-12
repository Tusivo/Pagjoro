// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Pagjoro.Core.Pdf;

namespace Pagjoro.App;

public partial class MainWindow
{
    (int Page, int Index)? chosenMark;

    void WireEditing()
    {
        View.MarkClicked += OnMarkClicked;
        View.MarkMoved += OnMarkMoved;
        View.ContentClicked += OnContentClicked;
        View.ContentDragged += OnContentDragged;
        PreviewKeyDown += OnWindowKeyDown;
    }

    // ---- marks ------------------------------------------------------------------------------------------------------

    void OnMarkClicked(int page, int index)
    {
        if (doc is null) return;
        chosenMark = (page, index);
        var info = doc.GetAnnotations(page).FirstOrDefault(a => a.Index == index);
        if (info is null) return;
        View.ShowMark(page, info.Rect);
        RefreshMarks();
        if (MarksList.ItemsSource is IEnumerable<MarkItem> items)
        {
            var item = items.FirstOrDefault(m => m.Page == page && m.Index == index);
            if (item is not null) MarksList.SelectedItem = item;
        }
        MessageStatus.Text = "Mark chosen. Press Delete to remove it, or use the Marks tab to edit it. Drag a note or shape to move it.";
    }

    void OnMarkMoved(int page, int index, double dx, double dy)
    {
        if (doc is null) return;
        PushUndo();
        try { doc.MoveAnnotation(page, index, dx, dy); }
        catch (PdfException ex) { undo.RemoveAt(undo.Count - 1); Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        View.InvalidatePage(page); AfterEdit();
        var info = doc.GetAnnotations(page).FirstOrDefault(a => a.Index == index);
        if (info is not null) View.ShowMark(page, info.Rect);
        chosenMark = (page, index);
    }

    (int Page, int Index)? CurrentMark()
    {
        if (MarksList.SelectedItem is MarkItem m) return (m.Page, m.Index);
        return chosenMark;
    }

    void OnEditMark(object sender, RoutedEventArgs e)
    {
        if (doc is null || CurrentMark() is not { } mk) { MessageStatus.Text = "Choose a mark first (click it on the page or in the list)."; return; }
        var info = doc.GetAnnotations(mk.Page).FirstOrDefault(a => a.Index == mk.Index);
        if (info is null) return;
        var text = Dialogs.Ask(this, "Edit Mark", "The words of this mark:", info.Contents, multiline: true);
        if (text is null) return;
        PushUndo();
        try { doc.SetAnnotationContents(mk.Page, mk.Index, text); }
        catch (PdfException ex) { undo.RemoveAt(undo.Count - 1); Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        View.InvalidatePage(mk.Page); AfterEdit(); RefreshMarks();
    }

    void OnRecolorMark(object sender, RoutedEventArgs e)
    {
        if (doc is null || CurrentMark() is not { } mk) { MessageStatus.Text = "Choose a mark first (click it on the page or in the list)."; return; }
        PushUndo();
        try { doc.SetAnnotationColor(mk.Page, mk.Index, View.MarkColor); }
        catch (PdfException ex) { undo.RemoveAt(undo.Count - 1); Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        View.InvalidatePage(mk.Page); AfterEdit();
        MessageStatus.Text = "Color changed to the one chosen in the toolbar.";
    }

    void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (View.IsGliding && e.Key == Key.Escape) { View.StopGlide(); e.Handled = true; return; }
        if (doc is null) return;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox or System.Windows.Controls.ComboBox;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        // viewing shortcuts (left alone while a text box has the keyboard)
        if (!typing)
        {
            switch (e.Key)
            {
                case Key.Add or Key.OemPlus when ctrl: View.SetZoom(View.Zoom * 1.25); e.Handled = true; return;
                case Key.Subtract or Key.OemMinus when ctrl: View.SetZoom(View.Zoom / 1.25); e.Handled = true; return;
                case Key.D0 or Key.NumPad0 when ctrl: View.FitPage(); e.Handled = true; return;
                case Key.D1 or Key.NumPad1 when ctrl: View.SetZoom(1.0); e.Handled = true; return;
                case Key.D2 or Key.NumPad2 when ctrl: View.FitWidth(); e.Handled = true; return;
                case Key.R when ctrl && shift: OnRotateViewRight(this, e); e.Handled = true; return;
                case Key.L when ctrl && shift: OnRotateViewLeft(this, e); e.Handled = true; return;
                case Key.G when ctrl: OnGoToPage(this, e); e.Handled = true; return;
                case Key.PageDown when !ctrl && !Thumbs.IsKeyboardFocusWithin: View.ScrollToPage(Math.Min(View.PageCount - 1, View.CurrentPage + 1)); e.Handled = true; return;
                case Key.PageUp when !ctrl && !Thumbs.IsKeyboardFocusWithin: View.ScrollToPage(Math.Max(0, View.CurrentPage - 1)); e.Handled = true; return;
                case Key.Home when ctrl: View.ScrollToPage(0); e.Handled = true; return;
                case Key.End when ctrl: View.ScrollToPage(View.PageCount - 1); e.Handled = true; return;
                case Key.F3: if (shift) OnSearchPrev(this, e); else OnSearchNext(this, e); e.Handled = true; return;
                case Key.Escape: SelectTool("Select"); View.ShowMark(0, null); chosenMark = null; View.ClearSelection(); e.Handled = true; return;
            }
        }
        if (e.Key != Key.Delete) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox or System.Windows.Controls.ComboBox) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.ListBoxItem && Thumbs.IsKeyboardFocusWithin) return;   // Delete there means delete pages, handled elsewhere
        if (chosenMark is not { } mk) return;
        e.Handled = true;
        PushUndo();
        try { doc.RemoveAnnotation(mk.Page, mk.Index); }
        catch (PdfException ex) { undo.RemoveAt(undo.Count - 1); Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        chosenMark = null; View.ShowMark(mk.Page, null);
        View.InvalidatePage(mk.Page); AfterEdit(); RefreshMarks();
        MessageStatus.Text = "Mark removed.";
    }

    // ---- text and pictures on the page ----------------------------------------------------------------------------------

    void OnContentClicked(int page, double x, double y)
    {
        if (doc is null) return;
        var obj = doc.ObjectAt(page, x, y);
        if (obj is null) { MessageStatus.Text = "There is no text or picture there. (Scanned pages are pictures: use Review, Recognize Text first.)"; return; }
        if (obj.Kind == PageObjectKind.Text)
        {
            var choice = Forms.EditText(this, obj);
            if (choice is null) return;
            PushUndo();
            try
            {
                if (choice.Delete) { doc.DeleteObject(page, obj.Index); MessageStatus.Text = "Text deleted."; }
                else { doc.ReplaceText(page, obj.Index, choice.Text, choice.Font, choice.Size, choice.Color); MessageStatus.Text = "Text changed."; }
            }
            catch (PdfException ex) { undo.RemoveAt(undo.Count - 1); Dialogs.Info(this, "Edit Text", ex.Message, error: true); return; }
            View.InvalidatePage(page); AfterEdit();
            return;
        }
        // a picture
        var pick = Dialogs.YesNoCancel(this, "Picture",
            "Yes: choose a different picture to put in its place.\nNo: delete this picture from the page.\nCancel: leave it.");
        if (pick == MessageBoxResult.Cancel) return;
        try
        {
            if (pick == MessageBoxResult.Yes)
            {
                var d = new OpenFileDialog { Filter = PictureFilter, Title = "Choose the new picture" };
                if (d.ShowDialog(this) != true) return;
                var (pixels, w, h, _, _) = LoadPicture(d.FileName);
                PushUndo();
                doc.ReplaceImage(page, obj.Index, pixels, w, h);
                MessageStatus.Text = "Picture replaced.";
            }
            else
            {
                PushUndo();
                doc.DeleteObject(page, obj.Index);
                MessageStatus.Text = "Picture deleted.";
            }
            View.InvalidatePage(page); AfterEdit();
        }
        catch (Exception ex) when (ex is PdfException or NotSupportedException or IOException or FileFormatException or InvalidOperationException)
        {
            if (undo.Count > 0) undo.RemoveAt(undo.Count - 1);
            Dialogs.Info(this, "Picture", ex.Message, error: true);
        }
    }

    void OnContentDragged(int page, double x, double y, double dx, double dy)
    {
        if (doc is null) return;
        var obj = doc.ObjectAt(page, x, y);
        if (obj is null) return;
        PushUndo();
        try { doc.MoveObject(page, obj.Index, dx, dy); }
        catch (PdfException ex) { undo.RemoveAt(undo.Count - 1); Dialogs.Info(this, "Pagjoro", ex.Message, error: true); return; }
        View.InvalidatePage(page); AfterEdit();
        MessageStatus.Text = "Moved.";
    }
}
