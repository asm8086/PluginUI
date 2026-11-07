// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>The spin box, the timeline ruler and the toasts (no window).</summary>
public class TimelineToastTests
{
    private UIHost _host = null!;
    private Canvas _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = new Canvas();
        _host = new UIHost(_root, new Size(800f, 600f));
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private T Add<T>(T element, float x, float y, float width, float height) where T : UIElement
    {
        element.SetBounds(x, y, width, height);
        _root.Children.Add(element);
        _host.UpdateLayout();
        return element;
    }

    private void Click(Point p, int count = 1)
    {
        _host.DispatchMouseDown(p, MouseButton.Left, count, ModifierKeys.None);
        _host.DispatchMouseUp(p, MouseButton.Left, ModifierKeys.None);
    }

    private void Drag(Point from, Point to)
    {
        _host.DispatchMouseDown(from, MouseButton.Left, 1, ModifierKeys.None);
        for (int i = 1; i <= 4; i++) _host.DispatchMouseMove(Point.Lerp(from, to, i / 4f), ModifierKeys.None);
        _host.DispatchMouseUp(to, MouseButton.Left, ModifierKeys.None);
    }

    private void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _host.DispatchKeyDown(new KeyEventArgs(key, modifiers));

    [Test]
    public void TheSpinBoxStepsInPlainUnitsAndRepeatsFaster()
    {
        var delay = new LocalParameter("Delay", 0, 1000, 100, "ms");
        var spin = Add(new NumericUpDown { Parameter = delay, PlainStep = 1.0, Label = "" }, 0f, 0f, 80f, 24f);
        double Ms() => delay.ToPlain(delay.NormalizedValue);

        Click(new Point(71f, 5f));
        Assert.That(Ms(), Is.EqualTo(101).Within(1e-6), "▲ on the top half of the right side");
        spin.Focus();
        Press(Key.Up);
        Press(Key.PageDown);
        Assert.That(Ms(), Is.EqualTo(92).Within(1e-6), "Up: one step, Page Down: ten");

        // Held: repeats after 0.4 s every 0.06 s, ten times faster after twenty repeats.
        _host.DispatchMouseDown(new Point(71f, 18f), MouseButton.Left, 1, ModifierKeys.None);
        _host.StepFrame(0.5);
        Assert.That(Ms(), Is.EqualTo(92 - 1 - 2).Within(1e-6));
        _host.StepFrame(1.5);
        _host.DispatchMouseUp(new Point(71f, 18f), MouseButton.Left, ModifierKeys.None);
        Assert.That(Ms(), Is.LessThan(89 - 20).And.GreaterThanOrEqualTo(0), "accelerated");

        var voices = LocalParameter.FromList("Voices", ["1", "2", "4", "8", "16"], 2);
        var stepped = Add(new NumericUpDown { Parameter = voices, Label = "" }, 0f, 40f, 80f, 24f);
        Click(new Point(71f, 45f));
        Assert.Multiple(() =>
        {
            Assert.That(voices.ToDisplayString(voices.NormalizedValue), Is.EqualTo("8"), "one position of a stepped parameter");
            Assert.That(stepped.GetAutomationPeer()!.ControlType, Is.EqualTo(AutomationControlType.Spinner));
            Assert.That(spin.StepFrom(delay.ToNormalized(999.5), 3), Is.EqualTo(1.0), "clamped");
        });
    }

    // A 560 × 44 ruler at (0, 0) showing 64 beats: 8.71875 DIPs per beat from x = 1; labels every 2 bars, snapping to 2 beats.
    private static float X(double beat) => 1f + (float)beat * 8.71875f;

    [Test]
    public void TheTimelineSeeksLoopsAndZooms()
    {
        var ruler = Add(new TimelineRuler(), 0f, 0f, 560f, 44f);
        var start = new LocalParameter("Loop start", 0, 64, 4);
        var end = new LocalParameter("Loop end", 0, 64, 8);
        int gestures = 0;
        start.EditStarted += _ => gestures++;
        ruler.LoopStartParameter = start;
        ruler.LoopEndParameter = end;
        var requested = new List<double>();
        ruler.PositionRequested += (_, beat) => requested.Add(beat);

        Click(new Point(X(10.3), 30f));
        Assert.That((requested.Last(), ruler.PlayPosition), Is.EqualTo((10.0, 10.0)), "snapped to the grid");

        Drag(new Point(X(20.1), 6f), new Point(X(30.1), 6f));
        Assert.That((ruler.LoopStart, ruler.LoopEnd), Is.EqualTo((20.0, 30.0)), "a new loop drawn in the strip");
        Drag(new Point(X(25), 6f), new Point(X(29), 6f));
        Assert.That((ruler.LoopStart, ruler.LoopEnd), Is.EqualTo((24.0, 34.0)), "moved");
        Drag(new Point(X(34) - 1f, 6f), new Point(X(40), 6f));
        Assert.Multiple(() =>
        {
            Assert.That((ruler.LoopStart, ruler.LoopEnd), Is.EqualTo((24.0, 40.0)), "the end edge");
            Assert.That(end.ToPlain(end.NormalizedValue), Is.EqualTo(40.0).Within(1e-9));
            Assert.That(gestures, Is.EqualTo(3), "one gesture per drag");
            Assert.That(start.IsEditing, Is.False);
        });
        Click(new Point(X(30), 6f), 2);
        Assert.That(ruler.IsLoopEnabled, Is.False, "double-click in the strip");

        ruler.Focus();
        Press(Key.Right);
        Assert.That(requested.Last(), Is.EqualTo(11.0));
        Press(Key.Right, ModifierKeys.Shift);
        Assert.That(requested.Last(), Is.EqualTo(12.0), "to the next bar");
        Press(Key.Left);
        Press(Key.L);
        Assert.Multiple(() =>
        {
            Assert.That(requested.Last(), Is.EqualTo(11.0));
            Assert.That(ruler.IsLoopEnabled, Is.True);
        });

        _host.DispatchWheel(new Point(X(32), 30f), 1f, false, ModifierKeys.Control);
        Assert.Multiple(() =>
        {
            Assert.That(ruler.ViewLength, Is.EqualTo(51.2).Within(1e-9));
            Assert.That(ruler.ViewStart, Is.EqualTo(32.0 - 0.5 * 51.2).Within(1e-3), "around the mouse");
        });

        var value = (IValuePattern)ruler.GetAutomationPeer()!.GetPattern(AutomationPattern.Value)!;
        Assert.Multiple(() =>
        {
            Assert.That(value.Value, Is.EqualTo("Position 3.4, loop 7.1 to 11.1"));
            Assert.That(ruler.FormatPosition(10.5), Is.EqualTo("3.3.3"));
        });
        ruler.Format = TimelineFormat.Seconds;
        Assert.That((ruler.FormatPosition(10), ruler.FormatPosition(10.25), ruler.FormatPosition(130)), Is.EqualTo(("0:05", "0:05.1", "1:05")));
    }

    [Test]
    public void ToastsStackFadeOutAndCarryActions()
    {
        var below = Add(new Button { Text = "Below" }, 0f, 0f, 400f, 300f);
        var toasts = Add(new ToastStack(), 0f, 0f, 400f, 300f);
        var saved = toasts.Show("Preset saved", ToastKind.Success);
        _host.StepFrame(0.2);
        var bounds = toasts.ToastBounds(saved);
        Assert.Multiple(() =>
        {
            Assert.That((bounds.Right, bounds.Bottom), Is.EqualTo((388f, 288f)), "bottom right corner");
            Assert.That(_host.HitAt(new Point(20f, 20f)), Is.SameAs(below), "only the toasts take the mouse");
            Assert.That(_host.HitAt(bounds.Center), Is.SameAs(toasts));
        });

        // The timer pauses while the mouse is over the toast.
        _host.DispatchMouseMove(bounds.Center, ModifierKeys.None);
        _host.StepFrame(5.0);
        Assert.That(saved.IsDismissed, Is.False);
        _host.DispatchMouseMove(new Point(20f, 20f), ModifierKeys.None);
        _host.StepFrame(5.0);
        _host.StepFrame(0.3);
        Assert.Multiple(() =>
        {
            Assert.That(saved.IsDismissed, Is.True);
            Assert.That(toasts.Toasts, Is.Empty, "faded out and removed");
        });

        bool undone = false;
        var deleted = toasts.Show("3 notes deleted", actionText: "Undo", action: () => undone = true);
        var sticky = toasts.Show("Sample rate changed", ToastKind.Warning, TimeSpan.Zero);
        _host.StepFrame(0.2);
        Click(toasts.ActionBounds(deleted).Center);
        Assert.Multiple(() =>
        {
            Assert.That(undone, Is.True);
            Assert.That(deleted.IsDismissed, Is.True);
        });
        var close = toasts.ToastBounds(sticky);
        Click(new Point(close.Right - 16f, close.Center.Y));
        Assert.That(sticky.IsDismissed, Is.True, "× closes");

        for (int i = 0; i < 6; i++) toasts.Show("Message " + i);
        Assert.That(toasts.Toasts.Count(t => !t.IsDismissed), Is.EqualTo(4), "the oldest close first");

        var thread = new Thread(() => toasts.Post("From the audio thread", ToastKind.Error));
        thread.Start();
        thread.Join();
        _host.StepFrame(0.016);
        Assert.That(toasts.Toasts.Last().Message, Is.EqualTo("From the audio thread"));

        bool shown = false;
        var withAction = toasts.Show("Update available", actionText: "Show", action: () => shown = true);
        var peer = (ToastStackAutomationPeer)toasts.GetAutomationPeer()!;
        var item = peer.GetToast(withAction);
        Assert.Multiple(() =>
        {
            Assert.That(peer.LocalizedControlType, Is.EqualTo("notification area"));
            Assert.That(peer.GetChildren(), Has.Count.EqualTo(4));
            Assert.That(item.ControlType, Is.EqualTo(AutomationControlType.Button));
            Assert.That(item.Name, Is.EqualTo("Update available, Show"));
            Assert.That(peer.GetToast(toasts.Toasts.First(t => !t.IsDismissed)).GetPattern(AutomationPattern.Invoke), Is.Null);
        });
        item.Invoke();
        Assert.That(shown, Is.True);
    }
}
