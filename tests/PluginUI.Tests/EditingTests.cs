// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

public class EditingTests
{
    [Test]
    public void TextBoxTypingAndSelection()
    {
        var root = new Canvas();
        var box = new TextBox();
        box.SetBounds(0f, 0f, 200f, 24f);
        root.Add(box);
        var host = new UIHost(root, new Size(300f, 100f));
        host.UpdateLayout();
        string? committed = null;
        box.Committed += (_, t) => committed = t;

        host.Focus(box);
        host.DispatchTextInput("Hello");
        host.DispatchTextInput(" world");
        Assert.That(box.Text, Is.EqualTo("Hello world"));

        host.DispatchKeyDown(new KeyEventArgs(Key.Left, ModifierKeys.Control | ModifierKeys.Shift)); // select "world"
        Assert.That(box.SelectedText, Is.EqualTo("world"));
        host.DispatchTextInput("there");
        host.DispatchKeyDown(new KeyEventArgs(Key.Back, ModifierKeys.None));
        Assert.That(box.Text, Is.EqualTo("Hello ther"));

        host.DispatchKeyDown(new KeyEventArgs(Key.A, ModifierKeys.Control));
        Assert.That(box.SelectionLength, Is.EqualTo(10));
        host.DispatchKeyDown(new KeyEventArgs(Key.Enter, ModifierKeys.None));
        Assert.That(committed, Is.EqualTo("Hello ther"));
    }

    [Test]
    public void TextBoxFilterAndMaxLength()
    {
        var box = new TextBox { MaxLength = 4, CharacterFilter = char.IsDigit };
        var host = new UIHost(box, new Size(100f, 24f));
        host.Focus(box);
        host.DispatchTextInput("1a2b3c4d5");
        Assert.That(box.Text, Is.EqualTo("1234"));
    }

    [Test]
    public void BeginTextEditCommitsOnEnterAndCancelsOnEscape()
    {
        var root = new Canvas();
        var host = new UIHost(root, new Size(300f, 100f));
        string? result = null;
        bool cancelled = false;

        var popup = host.BeginTextEdit(new Rect(10f, 10f, 80f, 20f), "12", t => result = t);
        host.ProcessPostedActions(); // focus is posted
        host.DispatchKeyDown(new KeyEventArgs(Key.End, ModifierKeys.None));
        host.DispatchTextInput("3");
        host.DispatchKeyDown(new KeyEventArgs(Key.Enter, ModifierKeys.None));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("123"));
            Assert.That(popup.IsOpen, Is.False);
        });

        host.BeginTextEdit(new Rect(10f, 10f, 80f, 20f), "x", t => result = t, () => cancelled = true);
        host.ProcessPostedActions();
        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None));
        Assert.Multiple(() =>
        {
            Assert.That(cancelled, Is.True);
            Assert.That(result, Is.EqualTo("123"));
        });
    }

    [Test]
    public void LiveEditMovesResizesUndoesAndGeneratesCode()
    {
        var root = new Canvas();
        var probe = new Probe { Name = "gainKnob" };
        probe.SetBounds(20f, 20f, 40f, 40f);
        root.Add(probe);
        var host = new UIHost(root, new Size(300f, 200f));
        host.UpdateLayout();

        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.E, ModifierKeys.Control | ModifierKeys.Shift)), Is.True);
        Assert.That(host.IsLiveEditEnabled, Is.True);
        var editor = host.LiveEditor;
        editor.GridSize = 4f;

        host.DispatchMouseDown(new Point(30f, 30f), MouseButton.Left, 1, ModifierKeys.None);
        Assert.That(editor.Selected, Is.SameAs(probe));
        host.DispatchMouseMove(new Point(51f, 39f), ModifierKeys.None); // +21,+9 → snapped to +20,+8
        host.DispatchMouseUp(new Point(51f, 39f), MouseButton.Left, ModifierKeys.None);
        Assert.That((probe.Left, probe.Top), Is.EqualTo((40f, 28f)));

        host.UpdateLayout();
        // Bottom-right handle: +11 x +7, snapped to the 4-DIP grid: +12 x +8.
        host.DispatchMouseDown(new Point(80f, 68f), MouseButton.Left, 1, ModifierKeys.None);
        host.DispatchMouseMove(new Point(91f, 75f), ModifierKeys.None);
        host.DispatchMouseUp(new Point(91f, 75f), MouseButton.Left, ModifierKeys.None);
        Assert.That((probe.Width, probe.Height), Is.EqualTo((52f, 48f)));

        host.DispatchKeyDown(new KeyEventArgs(Key.Right, ModifierKeys.Shift));
        Assert.That(probe.Left, Is.EqualTo(50f));

        string code = editor.GenerateCode();
        Assert.That(code, Does.Contain("gainKnob.SetBounds(50f, 28f, 52f, 48f);"));

        Assert.That(editor.Undo(), Is.True);
        Assert.That(probe.Left, Is.EqualTo(40f));

        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None)); // deselect
        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None)); // exit
        Assert.That(host.IsLiveEditEnabled, Is.False);
    }

    [Test]
    public void LiveEditOutsideCanvasEditsMargins()
    {
        var root = new StackPanel();
        var probe = new Probe { Name = "row" };
        root.Add(probe);
        var host = new UIHost(root, new Size(200f, 100f)) { IsLiveEditEnabled = true };
        host.LiveEditor.GridSize = 0f;
        host.UpdateLayout();
        host.DispatchMouseDown(new Point(5f, 5f), MouseButton.Left, 1, ModifierKeys.None);
        host.DispatchMouseMove(new Point(15f, 8f), ModifierKeys.None);
        host.DispatchMouseUp(new Point(15f, 8f), MouseButton.Left, ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(probe.Margin.Left, Is.EqualTo(10f));
            Assert.That(probe.Margin.Top, Is.EqualTo(3f));
            Assert.That(host.LiveEditor.GenerateCode(), Does.Contain("row.Margin = new Thickness(10f, 3f, 0f, 0f);"), "stretched axes keep their far margin");
        });
    }
}
