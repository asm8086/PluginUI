// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

public class HostTests
{
    private static (UIHost Host, Canvas Root) CreateHost(float width = 300f, float height = 200f)
    {
        var root = new Canvas();
        var host = new UIHost(root, new Size(width, height));
        return (host, root);
    }

    [Test]
    public void DragPerformsOneGestureWithImplicitCapture()
    {
        var (host, root) = CreateHost();
        var parameter = new LocalParameter("Gain");
        var slider = new TestSlider { Parameter = parameter };
        slider.SetBounds(10f, 10f, 40f, 100f);
        root.Add(slider);
        host.UpdateLayout();

        var log = new List<string>();
        parameter.EditStarted += _ => log.Add("begin");
        parameter.EditEnded += _ => log.Add("end");

        host.DispatchMouseDown(new Point(20f, 100f), MouseButton.Left, 1, ModifierKeys.None);
        Assert.That(host.CapturedElement, Is.SameAs(slider));
        host.DispatchMouseMove(new Point(250f, 50f), ModifierKeys.None); // outside: still captured
        host.DispatchMouseUp(new Point(250f, 50f), MouseButton.Left, ModifierKeys.None);

        Assert.Multiple(() =>
        {
            Assert.That(parameter.NormalizedValue, Is.EqualTo(50.0 / 200.0).Within(1e-9));
            Assert.That(log, Is.EqualTo(new[] { "begin", "end" }));
            Assert.That(host.CapturedElement, Is.Null);
            Assert.That(parameter.IsEditing, Is.False);
            Assert.That(host.FocusedElement, Is.SameAs(slider), "controls are focusable and take focus on click");
        });
    }

    [Test]
    public void DoubleClickResetsToDefault()
    {
        var (host, root) = CreateHost();
        var parameter = new LocalParameter("Gain", 0, 1, 0.75);
        parameter.NormalizedValue = 0.1;
        var slider = new TestSlider { Parameter = parameter };
        slider.SetBounds(0f, 0f, 40f, 100f);
        root.Add(slider);
        host.UpdateLayout();
        host.DispatchMouseDown(new Point(5f, 5f), MouseButton.Left, 2, ModifierKeys.None);
        host.DispatchMouseUp(new Point(5f, 5f), MouseButton.Left, ModifierKeys.None);
        Assert.That(parameter.NormalizedValue, Is.EqualTo(0.75).Within(1e-9));
    }

    [Test]
    public void ExternalParameterChangesRaiseValueChanged()
    {
        var (host, root) = CreateHost();
        var parameter = new LocalParameter("Gain");
        var slider = new TestSlider { Parameter = parameter };
        root.Add(slider);
        int changes = 0;
        slider.ValueChanged += (_, _) => changes++;
        parameter.NormalizedValue = 0.3; // automation / preset
        root.Children.Remove(slider);
        parameter.NormalizedValue = 0.6; // detached: no longer subscribed
        Assert.That(changes, Is.EqualTo(1));
        GC.KeepAlive(host);
    }

    [Test]
    public void HoverEnterLeaveFollowTheMouse()
    {
        var (host, root) = CreateHost();
        var panel = new Panel();
        panel.SetBounds(0f, 0f, 100f, 100f);
        var probe = new Probe();
        probe.SetBounds(10f, 10f, 20f, 20f);
        panel.Add(probe);
        root.Add(panel);
        host.UpdateLayout();

        host.DispatchMouseMove(new Point(15f, 15f), ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(probe.IsMouseOver, Is.True);
            Assert.That(panel.IsMouseOver, Is.True, "ancestors are hovered too");
        });
        host.DispatchMouseMove(new Point(50f, 50f), ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(probe.IsMouseOver, Is.False);
            Assert.That(panel.IsMouseOver, Is.True);
            Assert.That(probe.Log, Is.EqualTo(new[] { "enter", "leave" }));
        });
    }

    [Test]
    public void TabMovesFocusInTreeOrderAndKeysGoToFocusedElement()
    {
        var (host, root) = CreateHost();
        var a = new Probe { Focusable = true };
        var b = new Probe { Focusable = true };
        var disabled = new Probe { Focusable = true, IsEnabled = false };
        var c = new Probe { Focusable = true };
        root.Add(a, b, disabled, c);
        host.UpdateLayout();

        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.Tab, ModifierKeys.None)), Is.True);
        Assert.That(host.FocusedElement, Is.SameAs(a));
        host.DispatchKeyDown(new KeyEventArgs(Key.Tab, ModifierKeys.None));
        host.DispatchKeyDown(new KeyEventArgs(Key.Tab, ModifierKeys.None));
        Assert.That(host.FocusedElement, Is.SameAs(c), "disabled elements are skipped");
        host.DispatchKeyDown(new KeyEventArgs(Key.Tab, ModifierKeys.Shift));
        Assert.That(host.FocusedElement, Is.SameAs(b));
        Assert.That(host.ShowFocusRings, Is.True);

        bool handled = host.DispatchKeyDown(new KeyEventArgs(Key.Q, ModifierKeys.None));
        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False, "unhandled keys go back to the DAW");
            Assert.That(b.Log, Does.Contain("key:Q"));
        });
    }

    [Test]
    public void ArrowKeysEditAFocusedValueControl()
    {
        var (host, root) = CreateHost();
        var parameter = new LocalParameter("Mix");
        var slider = new TestSlider { Parameter = parameter, WheelStep = 0.1 };
        root.Add(slider);
        host.UpdateLayout();
        host.Focus(slider);
        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.Up, ModifierKeys.None)), Is.True);
        Assert.That(parameter.NormalizedValue, Is.EqualTo(0.1).Within(1e-9));
        host.DispatchKeyDown(new KeyEventArgs(Key.End, ModifierKeys.None));
        Assert.That(parameter.NormalizedValue, Is.EqualTo(1.0));
    }

    [Test]
    public void WheelOutsideControlsIsNotHandled()
    {
        var (host, root) = CreateHost();
        var slider = new TestSlider();
        slider.SetBounds(0f, 0f, 40f, 100f);
        root.Add(slider);
        host.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(host.DispatchWheel(new Point(200f, 150f), 1f, false, ModifierKeys.None), Is.False);
            Assert.That(host.DispatchWheel(new Point(10f, 10f), 1f, false, ModifierKeys.None), Is.True);
            Assert.That(slider.Value, Is.EqualTo(slider.WheelStep).Within(1e-9));
        });
    }

    [Test]
    public void LightDismissPopupClosesOnOutsideClickAndConsumesIt()
    {
        var (host, root) = CreateHost();
        var slider = new TestSlider();
        slider.SetBounds(0f, 0f, 40f, 100f);
        root.Add(slider);
        var popup = new Popup { Anchor = new Rect(100f, 20f, 50f, 20f), Content = new Probe { Natural = new Size(60f, 40f) } };
        int closed = 0;
        popup.Closed += _ => closed++;
        host.ShowPopup(popup);
        host.UpdateLayout();
        Assert.That(popup.Bounds, Is.EqualTo(new Rect(100f, 40f, 60f, 40f)), "below the anchor");

        host.DispatchMouseDown(new Point(10f, 10f), MouseButton.Left, 1, ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(slider.MouseDowns, Is.Zero, "the dismissing click is consumed");
        });
    }

    [Test]
    public void PopupFlipsAboveWhenThereIsNoRoomBelow()
    {
        var (host, _) = CreateHost(200f, 100f);
        var popup = new Popup { Anchor = new Rect(10f, 70f, 50f, 20f), Content = new Probe { Natural = new Size(40f, 40f) } };
        host.ShowPopup(popup);
        host.UpdateLayout();
        Assert.That(popup.Bounds, Is.EqualTo(new Rect(10f, 30f, 40f, 40f)));
    }

    [Test]
    public void RightClickShowsTheContextMenuAndItemsRunTheirAction()
    {
        var (host, root) = CreateHost();
        var probe = new Probe();
        probe.SetBounds(0f, 0f, 50f, 50f);
        bool ran = false;
        probe.ContextMenu = [new MenuItem("Reset", () => ran = true), MenuItem.Separator(), new MenuItem("Other")];
        root.Add(probe);
        host.UpdateLayout();
        host.DispatchMouseDown(new Point(10f, 10f), MouseButton.Right, 1, ModifierKeys.None);
        host.DispatchMouseUp(new Point(10f, 10f), MouseButton.Right, ModifierKeys.None);
        Assert.That(host.Popups, Has.Count.EqualTo(1));
        var menu = (MenuPopup)host.Popups[0];
        host.UpdateLayout();

        host.DispatchKeyDown(new KeyEventArgs(Key.Down, ModifierKeys.None));
        Assert.That(menu.HighlightedIndex, Is.EqualTo(0));
        host.DispatchKeyDown(new KeyEventArgs(Key.Enter, ModifierKeys.None));
        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.True);
            Assert.That(host.Popups, Is.Empty);
        });
    }

    [Test]
    public void ParameterContextMenuHookIsCalledForBoundControls()
    {
        var (host, root) = CreateHost();
        var parameter = new LocalParameter("Gain");
        var slider = new TestSlider { Parameter = parameter };
        slider.SetBounds(0f, 0f, 40f, 100f);
        root.Add(slider);
        host.UpdateLayout();
        IParameter? requested = null;
        host.ParameterContextMenuRequested = (p, _) => { requested = p; return true; };
        host.DispatchMouseDown(new Point(10f, 10f), MouseButton.Right, 1, ModifierKeys.None);
        host.DispatchMouseUp(new Point(10f, 10f), MouseButton.Right, ModifierKeys.None);
        Assert.That(requested, Is.SameAs(parameter));
    }

    [Test]
    public void ValueBubbleFollowsTheGesture()
    {
        var (host, root) = CreateHost();
        var slider = new TestSlider { ShowValueBubble = true };
        slider.SetBounds(100f, 100f, 40f, 80f);
        root.Add(slider);
        host.UpdateLayout();
        host.DispatchMouseDown(new Point(110f, 150f), MouseButton.Left, 1, ModifierKeys.None);
        host.DispatchMouseMove(new Point(110f, 130f), ModifierKeys.None);
        host.UpdateLayout();
        var bubble = root.Host!.HitAt(new Point(120f, 90f));
        Assert.That(bubble, Is.Null.Or.Not.InstanceOf<Popup>(), "the bubble is not hit-testable");
        host.DispatchMouseUp(new Point(110f, 130f), MouseButton.Left, ModifierKeys.None);
        Assert.That(slider.Value, Is.EqualTo(0.1).Within(1e-6));
    }

    [Test]
    public void DetachingTheCapturedElementReleasesCapture()
    {
        var (host, root) = CreateHost();
        var parameter = new LocalParameter("Gain");
        var slider = new TestSlider { Parameter = parameter };
        slider.SetBounds(0f, 0f, 40f, 100f);
        root.Add(slider);
        host.UpdateLayout();
        host.DispatchMouseDown(new Point(10f, 50f), MouseButton.Left, 1, ModifierKeys.None);
        root.Children.Remove(slider);
        Assert.Multiple(() =>
        {
            Assert.That(host.CapturedElement, Is.Null);
            Assert.That(parameter.IsEditing, Is.False, "the gesture is closed when the control leaves the tree");
        });
    }

    [Test]
    public void ResizeConstraintsFollowTheSizingMode()
    {
        var host = new UIHost(new Panel(), new Size(400f, 200f), SizingMode.ScaleToFit) { MinSize = new Size(200f, 100f), MaxSize = new Size(800f, 400f) };
        int w = 1000, h = 100;
        host.ConstrainPixelSize(ref w, ref h);
        Assert.Multiple(() =>
        {
            Assert.That((w, h), Is.EqualTo((800, 400)), "aspect ratio kept, clamped to the maximum");
            host.SetPixelSize(600, 300);
            Assert.That(host.Scale, Is.EqualTo(1.5f).Within(1e-6));
            Assert.That(host.LogicalSize, Is.EqualTo(new Size(400f, 200f)));
        });

        var fixedHost = new UIHost(new Panel(), new Size(400f, 200f));
        int fw = 10, fh = 10;
        fixedHost.ConstrainPixelSize(ref fw, ref fh);
        Assert.That((fw, fh), Is.EqualTo((400, 200)));
    }

    [Test]
    public void DpiChangeAsksTheContainerForTheScaledSize()
    {
        var host = new UIHost(new Panel(), new Size(400f, 200f));
        (int, int)? requested = null;
        host.ResizeRequestHandler = (w, h) => { requested = (w, h); host.SetPixelSize(w, h); return true; };
        host.SetDpiScale(1.5f);
        Assert.Multiple(() =>
        {
            Assert.That(requested, Is.EqualTo((600, 300)));
            Assert.That(host.Scale, Is.EqualTo(1.5f));
        });

        host.ResizeRequestHandler = (_, _) => false; // DAW refuses
        host.SetDpiScale(2f);
        Assert.That(host.DpiScale, Is.EqualTo(1.5f), "scale restored when the resize is refused");
    }

    [Test]
    public void ZoomRequestsANewSize()
    {
        var host = new UIHost(new Panel(), new Size(400f, 200f));
        host.Zoom = 1.25f;
        Assert.That(host.PixelSize, Is.EqualTo((500, 250)));
    }

    [Test]
    public void PostedActionsRunInOrder()
    {
        var (host, _) = CreateHost();
        var log = new List<int>();
        var t = Task.Run(() => { host.Post(() => log.Add(1)); host.Post(() => log.Add(2)); });
        t.Wait();
        host.ProcessPostedActions();
        Assert.That(log, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void AnimatorAndTickersAdvanceWithStepFrame()
    {
        var (host, root) = CreateHost();
        var probe = new Probe();
        root.Add(probe);
        bool done = false;
        host.Animator.Fade(probe, 0f, 0.1, () => done = true);
        int ticks = 0;
        host.AddTicker(new CountingTicker(() => ++ticks < 3));
        for (int i = 0; i < 10; i++) host.StepFrame(0.02);
        Assert.Multiple(() =>
        {
            Assert.That(probe.Opacity, Is.EqualTo(0f));
            Assert.That(done, Is.True);
            Assert.That(host.Animator.IsRunning, Is.False);
            Assert.That(ticks, Is.EqualTo(3), "a ticker returning false is removed");
        });
    }

    private sealed class CountingTicker(Func<bool> tick) : ITickable
    {
        public bool Tick(double elapsedSeconds) => tick();
    }

    [Test]
    public void InternalDragAndDropDeliversToTheAcceptingElement()
    {
        var (host, root) = CreateHost();
        var source = new Probe();
        source.SetBounds(0f, 0f, 20f, 20f);
        var target = new DropTarget();
        target.SetBounds(100f, 0f, 50f, 50f);
        root.Add(source, target);
        host.UpdateLayout();
        bool? completed = null;
        host.DispatchMouseDown(new Point(5f, 5f), MouseButton.Left, 1, ModifierKeys.None);
        host.BeginDrag(source, "payload", ok => completed = ok);
        host.DispatchMouseMove(new Point(120f, 10f), ModifierKeys.None);
        host.DispatchMouseUp(new Point(120f, 10f), MouseButton.Left, ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(target.Dropped, Is.EqualTo("payload"));
            Assert.That(completed, Is.True);
            Assert.That(host.IsDragging, Is.False);
        });
    }

    private sealed class DropTarget : UIElement
    {
        public object? Dropped { get; private set; }

        protected internal override void OnDragOver(DragEventArgs e) => e.Accept = e.Data is string;

        protected internal override void OnDrop(DragEventArgs e) => Dropped = e.Data;
    }
}
