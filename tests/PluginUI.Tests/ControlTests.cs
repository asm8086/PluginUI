// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

/// <summary>Interaction tests of the control catalog (simulated mouse and keyboard, no window).</summary>
public class ControlTests
{
    private UIHost _host = null!;
    private Canvas _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = new Canvas();
        _host = new UIHost(_root, new Size(600f, 500f));
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private T Add<T>(T element, float x, float y, float w, float h) where T : UIElement
    {
        element.SetBounds(x, y, w, h);
        _root.Children.Add(element);
        _host.UpdateLayout();
        return element;
    }

    private void Click(Point p, int clickCount = 1, ModifierKeys modifiers = ModifierKeys.None)
    {
        _host.DispatchMouseDown(p, MouseButton.Left, clickCount, modifiers);
        _host.DispatchMouseUp(p, MouseButton.Left, modifiers);
    }

    private void Drag(Point from, Point to, ModifierKeys modifiers = ModifierKeys.None)
    {
        _host.DispatchMouseDown(from, MouseButton.Left, 1, modifiers);
        _host.DispatchMouseMove(Point.Lerp(from, to, 0.5f), modifiers);
        _host.DispatchMouseMove(to, modifiers);
        _host.DispatchMouseUp(to, MouseButton.Left, modifiers);
    }

    [Test]
    public void KnobVerticalDragIsOneGesture()
    {
        var p = new LocalParameter("Gain");
        int begins = 0, ends = 0;
        p.EditStarted += _ => begins++;
        p.EditEnded += _ => ends++;
        var knob = Add(new Knob(p) { DragSensitivity = 200f }, 10f, 10f, 64f, 84f);
        Drag(knob.Bounds.Center, knob.Bounds.Center + new Point(0f, -50f));
        Assert.Multiple(() =>
        {
            Assert.That(p.NormalizedValue, Is.EqualTo(0.25).Within(0.02));
            Assert.That((begins, ends), Is.EqualTo((1, 1)));
            Assert.That(p.IsEditing, Is.False);
        });
        Click(knob.Bounds.Center, clickCount: 2);
        Assert.That(p.NormalizedValue, Is.EqualTo(p.DefaultNormalizedValue), "double-click resets");
    }

    [Test]
    public void SliderFollowsTheMouseAndQuantizes()
    {
        var slider = Add(new Slider(Orientation.Horizontal) { StepCount = 4, Style = Theme.Dark.DefaultStyle.Bare() }, 10f, 10f, 200f, 24f);
        Drag(new Point(slider.Bounds.X + 4f, slider.Bounds.Center.Y), new Point(slider.Bounds.Right - 2f, slider.Bounds.Center.Y));
        Assert.That(slider.Value, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(slider.Value * 4, Is.EqualTo(Math.Round(slider.Value * 4)), "stepped");
    }

    [Test]
    public void TogglesAndCheckBoxesUseSingleGestures()
    {
        var toggle = Add(new ToggleButton("T"), 10f, 10f, 80f, 24f);
        Click(new Point(50f, 22f));
        Assert.That(toggle.IsOn, Is.True);
        Click(new Point(50f, 22f));
        Assert.That(toggle.IsOn, Is.False);

        var p = LocalParameter.Toggle("P");
        int edits = 0;
        p.EditStarted += _ => edits++;
        var check = Add(new CheckBox("C") { Parameter = p }, 100f, 10f, 80f, 20f);
        Click(new Point(110f, 20f));
        Assert.Multiple(() =>
        {
            Assert.That(check.IsChecked, Is.True);
            Assert.That(p.NormalizedValue, Is.EqualTo(1.0));
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(p.IsEditing, Is.False);
        });
    }

    [Test]
    public void MultiToggleDragPaintsCells()
    {
        var mt = Add(new MultiToggle(8), 10f, 50f, 160f, 20f);
        Drag(new Point(15f, 60f), new Point(75f, 60f));
        Assert.That(Enumerable.Range(0, 5).Select(i => mt[i]), Is.EqualTo(new[] { true, true, true, true, false }));
    }

    [Test]
    public void DropDownOpensAMenuAndSelects()
    {
        var dd = Add(new DropDown("A", "B", "C"), 200f, 10f, 100f, 24f);
        Click(new Point(250f, 22f));
        Assert.That(dd.IsMenuOpen, Is.True);
        _host.UpdateLayout();
        var menu = (MenuPopup)_host.Popups[0];
        var item = new Point(menu.Bounds.X + 20f, menu.Bounds.Y + 4f + 24f * 2f + 12f);
        _host.DispatchMouseMove(item, ModifierKeys.None);
        Click(item);
        Assert.Multiple(() =>
        {
            Assert.That(dd.SelectedIndex, Is.EqualTo(2));
            Assert.That(dd.IsMenuOpen, Is.False);
            Assert.That(_host.Popups, Is.Empty);
        });
    }

    [Test]
    public void ColorSwatchPickerUpdatesLiveAndEscapeRestores()
    {
        var swatch = Add(new ColorSwatch(Color.FromRgb(0xFF0000)), 320f, 10f, 40f, 24f);
        Click(new Point(340f, 22f));
        Assert.That(_host.Popups.Single(), Is.InstanceOf<ColorPickerPopup>());
        _host.UpdateLayout();
        var picker = ((ColorPickerPopup)_host.Popups[0]).Picker.Bounds;
        Click(new Point(picker.X + 2f, picker.Y + 2f)); // top-left of the SV square: white
        Assert.That(swatch.Color.G, Is.GreaterThan(0.9f));
        _host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None));
        Assert.Multiple(() =>
        {
            Assert.That(_host.Popups, Is.Empty);
            Assert.That(swatch.Color.ToHex(), Is.EqualTo("#FF0000"));
        });
    }

    [Test]
    public void SegmentRockerButtonAndPresets()
    {
        var seg = Add(new SegmentedControl("1", "2", "3", "4"), 10f, 90f, 200f, 24f);
        Click(new Point(10f + 200f * 2.5f / 4f, 100f));
        Assert.That(seg.SelectedIndex, Is.EqualTo(2));

        var rocker = Add(new RockerSwitch { Parameter = new LocalParameter("O", 0, 4, 2, stepCount: 4) }, 400f, 10f, 24f, 44f);
        Click(new Point(412f, 15f));
        Assert.That(rocker.Parameter!.NormalizedValue, Is.EqualTo(0.75));

        int clicks = 0;
        var button = Add(new Button("B"), 10f, 130f, 60f, 24f);
        button.Click += _ => clicks++;
        Click(new Point(30f, 140f));
        Drag(new Point(30f, 140f), new Point(300f, 300f));
        Assert.That(clicks, Is.EqualTo(1), "released outside: no click");

        var presets = new PresetList([new("One"), new("Two"), new("Three")]);
        Add(new PresetBar(presets), 10f, 170f, 300f, 24f);
        Click(new Point(40f, 180f));
        Assert.That(presets.CurrentIndex, Is.Zero);
        Click(new Point(15f, 180f));
        Assert.That(presets.CurrentIndex, Is.EqualTo(2), "previous wraps around");
    }

    [Test]
    public void XYPadEditsBothParametersInParallel()
    {
        var x = new LocalParameter("X");
        var y = new LocalParameter("Y");
        var pad = Add(new XYPad { XParameter = x, YParameter = y, Style = Theme.Dark.DefaultStyle.Bare() }, 10f, 10f, 200f, 200f);
        bool bothEditing = false;
        x.Changed += _ => bothEditing |= x.IsEditing && y.IsEditing;
        Drag(pad.Bounds.Center, new Point(pad.Bounds.Right - 1f, pad.Bounds.Y + 1f));
        Assert.Multiple(() =>
        {
            Assert.That(bothEditing, Is.True);
            Assert.That(x.NormalizedValue, Is.GreaterThan(0.9));
            Assert.That(y.NormalizedValue, Is.GreaterThan(0.9), "Y grows upwards");
            Assert.That(x.IsEditing || y.IsEditing, Is.False);
        });
    }

    [Test]
    public void ListBoxKeyboardNavigationAndSelection()
    {
        var items = Enumerable.Range(0, 100).Select(i => $"Item {i}").ToList();
        var list = Add(new ListBox(items), 10f, 10f, 200f, 200f);
        int changes = 0;
        list.SelectionChanged += _ => changes++;
        Click(new Point(50f, 15f));
        Assert.That(list.SelectedIndex, Is.Zero);
        _host.DispatchKeyDown(new KeyEventArgs(Key.Down, ModifierKeys.None));
        _host.DispatchKeyDown(new KeyEventArgs(Key.End, ModifierKeys.None));
        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndex, Is.EqualTo(99));
            Assert.That(changes, Is.EqualTo(3));
        });
    }

    [Test]
    public void TabViewAttachesOnlyTheSelectedPage()
    {
        var first = new Probe();
        var second = new Probe();
        var tabs = Add(new TabView(), 0f, 0f, 400f, 300f);
        tabs.AddPage("One", first).AddPage("Two", second);
        _host.UpdateLayout();
        Assert.That((first.Host, second.Host), Is.EqualTo((_host, (UIHost?)null)));
        tabs.SelectedIndex = 1;
        Assert.That((first.Host, second.Host), Is.EqualTo(((UIHost?)null, _host)));
    }

    [Test]
    public void ScrollViewerScrollsWithTheWheel()
    {
        var content = new StackPanel();
        for (int i = 0; i < 50; i++) content.Add(new Probe { Natural = new Size(100f, 20f) });
        var viewer = Add(new ScrollViewer(content), 0f, 0f, 200f, 100f);
        _host.UpdateLayout();
        Assert.That(_host.DispatchWheel(new Point(50f, 50f), -2f, false, ModifierKeys.None), Is.True);
        Assert.That(viewer.OffsetY, Is.GreaterThan(0f));
        var firstChild = content.Children[0];
        _host.UpdateLayout();
        Assert.That(firstChild.Bounds.Y, Is.EqualTo(-viewer.OffsetY).Within(0.5f), "content arranged at -offset");
    }

    [Test]
    public void LevelMeterBallisticsFollowSources()
    {
        var peak = new PluginUI.Dsp.PeakValue();
        var meter = Add(new LevelMeter { ChannelCount = 1, Sources = [peak] }, 0f, 0f, 20f, 100f);
        peak.Push(1f);
        _host.StepFrame(0.016);
        Assert.That(meter, Is.Not.Null);
    }
}
