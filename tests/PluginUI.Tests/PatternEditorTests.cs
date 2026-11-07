// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>The pattern editor (mini piano roll) and the drum pads: mouse, keyboard, screen readers (no window).</summary>
public class PatternEditorTests
{
    private UIHost _host = null!;
    private Canvas _root = null!;

    // A 560 × 260 editor at (0, 0): 1 px border, 34 px keyboard, 14 px ruler, 48 px velocity lane below a 4 px gap.
    // Grid: x 35 … 559 (16 beats), y 15 … 207 (24 rows of 8 px, C3 = 48 at the bottom).
    private static float X(double beat) => 35f + (float)(beat / 16.0) * 524f;

    private static float Y(int pitch) => 15f + (48 + 24 - 1 - pitch) * 8f + 4f;

    [SetUp]
    public void SetUp()
    {
        _root = new Canvas();
        _host = new UIHost(_root, new Size(800f, 600f));
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private T Add<T>(T element, float width, float height) where T : UIElement
    {
        element.SetBounds(0f, 0f, width, height);
        _root.Children.Add(element);
        _host.UpdateLayout();
        return element;
    }

    private void Drag(Point from, Point to, ModifierKeys modifiers = ModifierKeys.None, MouseButton button = MouseButton.Left)
    {
        _host.DispatchMouseDown(from, button, 1, modifiers);
        for (int i = 1; i <= 4; i++) _host.DispatchMouseMove(Point.Lerp(from, to, i / 4f), modifiers);
        _host.DispatchMouseUp(to, button, modifiers);
    }

    private void Click(Point p, int count = 1, MouseButton button = MouseButton.Left, ModifierKeys modifiers = ModifierKeys.None)
    {
        _host.DispatchMouseDown(p, button, count, modifiers);
        _host.DispatchMouseUp(p, button, modifiers);
    }

    private void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _host.DispatchKeyDown(new KeyEventArgs(key, modifiers));

    private void Release(Key key) => _host.DispatchKeyUp(new KeyEventArgs(key, ModifierKeys.None));

    [Test]
    public void NotesAreAddedMovedResizedAndDeletedWithTheMouse()
    {
        var roll = Add(new PatternEditor(), 560f, 260f);
        int changes = 0;
        var heard = new List<string>();
        roll.PatternChanged += _ => changes++;
        roll.PreviewNoteOn += (_, pitch, _) => heard.Add("on " + pitch);
        roll.PreviewNoteOff += (_, pitch) => heard.Add("off " + pitch);

        Click(new Point(X(1.1), Y(60)));
        Assert.That(roll.Notes, Has.Count.EqualTo(1));
        var note = roll.Notes[0];
        Assert.Multiple(() =>
        {
            Assert.That((note.Pitch, note.Start, note.Length), Is.EqualTo((60, 1.0, 0.25)), "on the grid cell under the mouse, one step long");
            Assert.That(note.Velocity, Is.EqualTo(0.8f));
            Assert.That(roll.IsSelected(note), Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(heard, Is.EqualTo(new[] { "on 60", "off 60" }), "the new note is heard while the mouse is down");
        });

        Drag(new Point(X(1.05), Y(60)), new Point(X(3.1), Y(64)));
        Assert.Multiple(() =>
        {
            Assert.That((note.Pitch, note.Start, note.Length), Is.EqualTo((64, 3.0, 0.25)), "moved on the grid");
            Assert.That(changes, Is.EqualTo(2), "one change per gesture");
        });

        Drag(new Point(X(3.24), Y(64)), new Point(X(4.01), Y(64)));
        Assert.That(note.Length, Is.EqualTo(1.0), "the right edge sets the length");
        Drag(new Point(X(3.1), Y(64)), new Point(X(3.2), Y(64)), ModifierKeys.Alt);
        Assert.That(note.Start, Is.EqualTo(3.0 + 0.1).Within(0.01), "Alt: off the grid");

        // Drawing a longer note: drag right from an empty cell; the next note gets the same length.
        Drag(new Point(X(8.1), Y(55)), new Point(X(9.9), Y(55)));
        var second = roll.Notes[1];
        Assert.That((second.Start, second.Length), Is.EqualTo((8.0, 2.0)));
        Click(new Point(X(12.1), Y(50)));
        Assert.That(roll.Notes[2].Length, Is.EqualTo(2.0));

        Click(new Point(X(12.5), Y(50)), 2);
        Assert.That(roll.Notes, Has.Count.EqualTo(2), "double-click deletes");
        Click(new Point(X(8.5), Y(55)), button: MouseButton.Right);
        Assert.That(roll.Notes, Is.EqualTo(new[] { note }), "right-click deletes");
    }

    [Test]
    public void SelectionVelocityLaneAndScrolling()
    {
        var roll = Add(new PatternEditor(), 560f, 260f);
        var a = new PatternNote(60, 0, 1);
        var b = new PatternNote(62, 2, 1);
        var c = new PatternNote(67, 4, 1);
        roll.SetNotes([a, b, c]);
        Assert.That(roll.Notes[0], Is.Not.SameAs(a), "SetNotes copies");
        a = roll.Notes[0]; b = roll.Notes[1]; c = roll.Notes[2];

        // Shift+drag: rectangle selection over the first two notes.
        Drag(new Point(X(0.5) - 10f, Y(64)), new Point(X(2.5), Y(58)), ModifierKeys.Shift);
        Assert.That(roll.SelectedNotes, Is.EqualTo(new[] { a, b }));
        Click(new Point(X(4.5), Y(67)), modifiers: ModifierKeys.Control);
        Assert.That(roll.SelectedNotes, Is.EqualTo(new[] { a, b, c }), "Ctrl+click adds");
        Click(new Point(X(0.5), Y(60)), modifiers: ModifierKeys.Control);
        Assert.That(roll.SelectedNotes, Is.EqualTo(new[] { b, c }), "and removes");

        // Moving one of the selected notes moves them all.
        Drag(new Point(X(2.5), Y(62)), new Point(X(3.5), Y(63)));
        Assert.That((b.Start, b.Pitch, c.Start, c.Pitch), Is.EqualTo((3.0, 63, 5.0, 68)));

        // The velocity lane changes only the selected notes under the mouse.
        float laneBottom = 259f;
        _host.DispatchMouseDown(new Point(X(3.05), laneBottom - 12f), MouseButton.Left, 1, ModifierKeys.None);
        _host.DispatchMouseMove(new Point(X(5.05), laneBottom - 12f), ModifierKeys.None);
        _host.DispatchMouseUp(new Point(X(5.05), laneBottom - 12f), MouseButton.Left, ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(a.Velocity, Is.EqualTo(0.8f), "not selected");
            Assert.That(b.Velocity, Is.EqualTo(12f / 46f).Within(0.01f));
            Assert.That(c.Velocity, Is.EqualTo(12f / 46f).Within(0.01f));
        });

        // Right-click on a selected note deletes the selection.
        Click(new Point(X(3.5), Y(63)), button: MouseButton.Right);
        Assert.That(roll.Notes, Is.EqualTo(new[] { a }));

        // The wheel scrolls the pitches: two semitones, an octave with Shift.
        _host.DispatchWheel(new Point(X(8), Y(60)), 1f, false, ModifierKeys.None);
        Assert.That(roll.LowestVisibleNote, Is.EqualTo(50));
        _host.DispatchWheel(new Point(X(8), Y(60)), -1f, false, ModifierKeys.Shift);
        Assert.That(roll.LowestVisibleNote, Is.EqualTo(38));

        // The keyboard on the left plays the pitch under the mouse.
        var heard = new List<int>();
        roll.PreviewNoteOn += (_, pitch, _) => heard.Add(pitch);
        roll.LowestVisibleNote = 48;
        Drag(new Point(10f, Y(60)), new Point(10f, Y(62)));
        Assert.That(heard, Is.EqualTo(new[] { 60, 61, 62 }));

        roll.PlayPositionSource = () => 2.5;
        _host.StepFrame(1.0 / 60.0);
        Assert.That(roll.PlayPosition, Is.EqualTo(2.5));
    }

    [Test]
    public void TheKeyboardEditsTheSelectionAndScreenReadersFollow()
    {
        var roll = Add(new PatternEditor(), 560f, 260f);
        roll.SetNotes([new PatternNote(60, 0, 0.5), new PatternNote(64, 1, 0.5), new PatternNote(67, 2, 0.5)]);
        roll.Focus();
        var peer = roll.GetAutomationPeer()!;
        var value = (IValuePattern)peer.GetPattern(AutomationPattern.Value)!;
        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Group));
            Assert.That(peer.LocalizedControlType, Is.EqualTo("pattern editor"));
            Assert.That(value.IsReadOnly, Is.True);
            Assert.That(value.Value, Is.EqualTo("3 notes, none selected"));
        });

        Press(Key.Home);
        Press(Key.Right, ModifierKeys.Control);
        var e = roll.SelectedNotes.Single();
        Assert.That(e.Pitch, Is.EqualTo(64));
        Assert.That(value.Value, Is.EqualTo("E4, bar 1, beat 2, length 0.5 beats, velocity 80 %"));

        Press(Key.Right);
        Press(Key.Right, ModifierKeys.Shift);
        Press(Key.Up);
        Press(Key.Up, ModifierKeys.Control);
        Assert.That((e.Start, e.Length, e.Pitch, e.Velocity), Is.EqualTo((1.25, 0.75, 65, 0.85f)));
        Press(Key.Up, ModifierKeys.Shift);
        Assert.Multiple(() =>
        {
            Assert.That(e.Pitch, Is.EqualTo(77));
            Assert.That(roll.LowestVisibleNote, Is.EqualTo(77 - 23), "scrolled to show it");
        });

        Press(Key.D, ModifierKeys.Control);
        var copy = roll.SelectedNotes.Single();
        Assert.That((copy.Pitch, copy.Start, copy.Length), Is.EqualTo((77, 2.0, 0.75)), "duplicated after itself, on the grid");
        Press(Key.Insert);
        var inserted = roll.SelectedNotes.Single();
        Assert.That((inserted.Pitch, inserted.Start, inserted.Length), Is.EqualTo((77, 2.75, 0.75)));
        Press(Key.Delete);
        Assert.That(roll.Notes, Has.Count.EqualTo(4));

        Press(Key.A, ModifierKeys.Control);
        Assert.That(value.Value, Is.EqualTo("4 of 4 notes selected"));
        Press(Key.Left);
        Assert.That(roll.Notes.Min(n => n.Start), Is.EqualTo(0.0), "the selection stays inside the pattern");
        Press(Key.Escape);
        Assert.That(roll.SelectedNotes, Is.Empty);
        Press(Key.End);
        Assert.That(roll.SelectedNotes.Single().Start, Is.EqualTo(2.0));
        Assert.That(roll.GetNotes().Select(n => n.Start), Is.Ordered, "copies in time order");
        Assert.Throws<InvalidOperationException>(() => value.SetValue("C4"));
    }

    [Test]
    public void DrumPadsPlaySelectAndLight()
    {
        var pads = Add(new DrumPads(), 260f, 260f);
        var played = new List<string>();
        pads.PadPressed += (_, pad, velocity) => played.Add(FormattableString.Invariant($"+{pad}:{velocity:0.00}"));
        pads.PadReleased += (_, pad) => played.Add($"-{pad}");

        // 4 × 4 pads 59.5 px wide with 6 px gaps from (2, 2); pad 0 at the bottom left.
        var r0 = pads.PadRect(0);
        Assert.Multiple(() =>
        {
            Assert.That(pads.Pads.Select(p => p.Note), Is.EqualTo(Enumerable.Range(36, 16)));
            Assert.That(r0.X, Is.EqualTo(2f).Within(0.01f));
            Assert.That(r0.Bottom, Is.EqualTo(258f).Within(0.01f));
            Assert.That(pads.PadAt(new Point(10f, 10f)), Is.EqualTo(12), "the top left pad is the 13th");
        });

        Click(new Point(r0.Center.X, r0.Y + 1f));
        Click(new Point(r0.Center.X, r0.Bottom - 1f));
        Assert.That(played, Is.EqualTo(new[] { "+0:0.99", "-0", "+0:0.26", "-0" }), "louder at the top of the pad");
        pads.VelocitySensitive = false;
        played.Clear();
        Click(pads.PadRect(5).Center);
        Assert.Multiple(() =>
        {
            Assert.That(played, Is.EqualTo(new[] { "+5:0.80", "-5" }));
            Assert.That(pads.SelectedPad, Is.EqualTo(5));
        });

        // Keyboard: arrows move on the screen, Space plays, 1 2 3 4 is the top row.
        pads.Focus();
        played.Clear();
        Press(Key.Up);
        Assert.That(pads.SelectedPad, Is.EqualTo(9), "up on the screen");
        Press(Key.Space);
        Assert.That(pads.IsPadHeld(9), Is.True);
        Release(Key.Space);
        Press(Key.D1);
        Press(Key.V);
        Release(Key.D1);
        Release(Key.V);
        Assert.That(played, Is.EqualTo(new[] { "+9:0.80", "-9", "+12:0.80", "+3:0.80", "-12", "-3" }));

        // Flash from another thread: the pad glows at the next frame.
        var t = new Thread(() => pads.Flash(38, 1f));
        t.Start();
        t.Join();
        _host.StepFrame(1.0 / 60.0);
        Assert.That(pads.IsPadHeld(2), Is.False, "a flash is not a press");

        // Screen readers: a group of buttons, the focus on the selected pad; invoking a pad plays it.
        var peer = pads.GetAutomationPeer()!;
        pads.Pads[0].Name = "Kick";
        var pad0 = (DrumPadAutomationPeer)((DrumPadsAutomationPeer)peer).GetPad(0);
        Assert.Multiple(() =>
        {
            Assert.That(peer.LocalizedControlType, Is.EqualTo("drum pads"));
            Assert.That(peer.GetChildren(), Has.Count.EqualTo(16));
            Assert.That(pad0.Name, Is.EqualTo("Kick, C2"));
            Assert.That(((DrumPadAutomationPeer)peer.GetFocusTarget()).Index, Is.EqualTo(3));
            Assert.That(((DrumPadAutomationPeer)((DrumPadsAutomationPeer)peer).GetPad(1)).Name, Is.EqualTo("Pad 2, C#2"));
        });
        played.Clear();
        pad0.Invoke();
        Assert.Multiple(() =>
        {
            Assert.That(played, Is.EqualTo(new[] { "+0:0.80", "-0" }));
            Assert.That(pads.SelectedPad, Is.EqualTo(0));
        });

        pads.Columns = 2;
        pads.Rows = 2;
        Assert.That(pads.Pads, Has.Count.EqualTo(4));
    }
}
