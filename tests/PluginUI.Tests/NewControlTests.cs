// 2026/10/19 17:23:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Dsp;

namespace PluginUI.Tests;

/// <summary>Interaction tests of the controls added in step 3 (simulated mouse and keyboard, no window).</summary>
public class NewControlTests
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

    private T Add<T>(T element, float x, float y, float w, float h) where T : UIElement
    {
        element.SetBounds(x, y, w, h);
        _root.Children.Add(element);
        _host.UpdateLayout();
        return element;
    }

    private void Drag(Point from, Point to, ModifierKeys modifiers = ModifierKeys.None, int steps = 4)
    {
        _host.DispatchMouseDown(from, MouseButton.Left, 1, modifiers);
        for (int i = 1; i <= steps; i++) _host.DispatchMouseMove(Point.Lerp(from, to, i / (float)steps), modifiers);
        _host.DispatchMouseUp(to, MouseButton.Left, modifiers);
    }

    private void DoubleClick(Point p)
    {
        _host.DispatchMouseDown(p, MouseButton.Left, 1, ModifierKeys.None);
        _host.DispatchMouseUp(p, MouseButton.Left, ModifierKeys.None);
        _host.DispatchMouseDown(p, MouseButton.Left, 2, ModifierKeys.None);
        _host.DispatchMouseUp(p, MouseButton.Left, ModifierKeys.None);
    }

    private static (int Begins, int Ends) CountGestures(LocalParameter p, List<int> counters)
    {
        counters.AddRange([0, 0]);
        p.EditStarted += _ => counters[0]++;
        p.EditEnded += _ => counters[1]++;
        return (0, 0);
    }

    [Test]
    public void PitchWheelSpringsBackInsideOneGesture()
    {
        var p = new LocalParameter("Bend", -1, 1, 0);
        var counts = new List<int>();
        CountGestures(p, counts);
        var wheel = Add(new PitchWheel { Parameter = p, Style = Theme.Dark.DefaultStyle.Bare() }, 10f, 10f, 30f, 120f);
        _host.DispatchMouseDown(wheel.Bounds.Center, MouseButton.Left, 1, ModifierKeys.None);
        _host.DispatchMouseMove(wheel.Bounds.Center - new Point(0f, 60f), ModifierKeys.None);
        Assert.That(p.NormalizedValue, Is.GreaterThan(0.8), "dragged up");
        _host.DispatchMouseUp(wheel.Bounds.Center - new Point(0f, 60f), MouseButton.Left, ModifierKeys.None);
        Assert.That(p.IsEditing, Is.True, "the return animation is part of the gesture");
        wheel.FinishReturn();
        Assert.Multiple(() =>
        {
            Assert.That(p.NormalizedValue, Is.EqualTo(0.5).Within(1e-9));
            Assert.That((counts[0], counts[1]), Is.EqualTo((1, 1)));
        });

        var mod = Add(new ModWheel(), 60f, 10f, 30f, 120f);
        Drag(mod.Bounds.Center, mod.Bounds.Center - new Point(0f, 40f));
        Assert.That(mod.Value, Is.GreaterThan(0.2), "mod wheel stays");
    }

    [Test]
    public void StepSequencerPaintsInterpolatedValuesAndGates()
    {
        var seq = Add(new StepSequencer { StepCount = 8, Style = Theme.Dark.DefaultStyle.Bare() }, 0f, 0f, 400f, 120f);
        // Value lane: from step 0 at the top to step 7 at the bottom in one fast movement.
        Drag(new Point(10f, 6f), new Point(390f, 90f), steps: 1);
        Assert.Multiple(() =>
        {
            Assert.That(seq.GetValue(0), Is.GreaterThan(0.9));
            Assert.That(seq.GetValue(7), Is.LessThan(0.15));
            for (int i = 1; i < 8; i++) Assert.That(seq.GetValue(i), Is.LessThanOrEqualTo(seq.GetValue(i - 1)), $"step {i} interpolated");
        });
        // Gate lane: drag switches every touched gate to the opposite of the first one.
        Drag(new Point(10f, 112f), new Point(160f, 112f));
        Assert.That(Enumerable.Range(0, 8).Select(seq.GetGate), Is.EqualTo(new[] { false, false, false, false, true, true, true, true }));

        var parameters = Enumerable.Range(0, 8).Select(i => new LocalParameter($"S{i}")).ToArray();
        int begins = 0, ends = 0;
        foreach (var p in parameters) { p.EditStarted += _ => begins++; p.EditEnded += _ => ends++; }
        seq.ValueParameters = parameters;
        Drag(new Point(10f, 50f), new Point(140f, 30f), steps: 6);
        Assert.Multiple(() =>
        {
            Assert.That(begins, Is.EqualTo(ends));
            Assert.That(begins, Is.InRange(3, 4), "one gesture per parameter touched");
            Assert.That(parameters.Any(p => p.IsEditing), Is.False);
        });
    }

    [Test]
    public void CurveEditorAddsMovesBendsAndEvaluates()
    {
        var curve = Add(new CurveEditor { GridX = 4, GridY = 4 }, 0f, 0f, 212f, 112f); // inner area 200 × 100
        Assert.That(curve.Evaluate(0.25), Is.EqualTo(0.5).Within(1e-12), "triangle");
        DoubleClick(new Point(6f + 150f, 6f + 25f)); // x = 0.75, y = 0.75 → snapped
        Assert.That(curve.Points.Count, Is.EqualTo(4));
        Assert.That(curve.Points[2], Is.EqualTo(new CurvePoint(0.75, 0.75)));
        Drag(new Point(6f + 150f, 6f + 25f), new Point(6f + 150f, 6f + 50f));
        Assert.That(curve.Points[2].Y, Is.EqualTo(0.5).Within(1e-12));
        DoubleClick(new Point(6f + 150f, 6f + 50f));
        Assert.That(curve.Points.Count, Is.EqualTo(3), "double-click on a point deletes it");
        DoubleClick(new Point(6f, 106f));
        Assert.That(curve.Points.Count, Is.EqualTo(3), "endpoints cannot be deleted");
        Assert.Multiple(() =>
        {
            Assert.That(CurveEditor.Shape(0.5, 0.0), Is.EqualTo(0.5));
            Assert.That(CurveEditor.Shape(0.5, 0.8), Is.LessThan(0.2), "positive curvature starts slowly");
            Assert.That(CurveEditor.Shape(0.5, -0.8), Is.GreaterThan(0.8));
            Assert.That(CurveEditor.Shape(1.0, 0.7), Is.EqualTo(1.0).Within(1e-12));
        });
    }

    [Test]
    public void EqualizerDragsBandsWithParallelGestures()
    {
        var freq = new LocalParameter("Freq", scale: ParameterScale.Logarithmic(20, 20000), defaultValue: 1000);
        var gain = new LocalParameter("Gain", -18, 18, 0);
        var eq = Add(new ParametricEqEditor(), 0f, 0f, 404f, 216f);
        var band = new EqBand(EqBandType.Bell, 1000, 0, 1) { FrequencyParameter = freq, GainParameter = gain };
        eq.Bands.Add(band);
        eq.Bands.Add(new EqBand(EqBandType.HighShelf, 8000, -4, 0.7071));
        _host.UpdateLayout();
        double expected = BiquadDesign.MagnitudeDb(eq.Bands[1].Coefficients(48000), 12000, 48000);
        Assert.That(eq.ResponseDb(12000), Is.EqualTo(expected).Within(1e-9), "bell at 0 dB contributes nothing");

        var area = new Rect(2f, 2f, 400f, 200f);
        float x = area.X + MathF.Log(1000f / 20f) / MathF.Log(1000f) * area.Width;
        var node = new Point(x, area.Center.Y);
        bool bothEditing = false;
        freq.Changed += _ => bothEditing |= freq.IsEditing && gain.IsEditing;
        Drag(node, node + new Point(40f, -50f));
        Assert.Multiple(() =>
        {
            Assert.That(band.Frequency, Is.GreaterThan(1500));
            Assert.That(band.GainDb, Is.EqualTo(9.0).Within(0.6));
            Assert.That(bothEditing, Is.True);
            Assert.That(freq.IsEditing || gain.IsEditing, Is.False);
        });
        double q = band.Q;
        _host.DispatchWheel(new Point(GraphX(eq, band.Frequency, area), GraphY(band.GainDb, area)), 1f, false, ModifierKeys.None);
        Assert.That(band.Q, Is.GreaterThan(q));
        DoubleClick(new Point(GraphX(eq, band.Frequency, area), GraphY(band.GainDb, area)));
        Assert.That(band.IsEnabled, Is.False, "double-click on a node bypasses the band");
    }

    private static float GraphX(ParametricEqEditor eq, double hz, Rect area) => area.X + MathF.Log((float)hz / eq.MinFrequency) / MathF.Log(eq.MaxFrequency / eq.MinFrequency) * area.Width;

    private static float GraphY(double db, Rect area) => area.Bottom - (float)((db + 18.0) / 36.0) * area.Height;

    [Test]
    public void ModulationSourcesDropOnAcceptingControlsOnly()
    {
        var source = new ModulationSource("LFO 1", Id: 1);
        var chip = Add(new ModulationSourceButton { Source = source }, 10f, 10f, 80f, 22f);
        var target = Add(new Knob { AcceptsModulation = true }, 200f, 10f, 64f, 84f);
        var other = Add(new Knob(), 300f, 10f, 64f, 84f);
        ModulationSource? dropped = null;
        target.ModulationDropped += (_, s) => dropped = s;
        other.ModulationDropped += (_, _) => Assert.Fail("not accepting");
        Drag(chip.Bounds.Center, target.Bounds.Center);
        Assert.That(dropped, Is.SameAs(source));
        Assert.That(target.IsModulationDropTarget, Is.False, "highlight cleared");
        bool? completed = null;
        chip.DragCompleted += (_, ok) => completed = ok;
        Drag(chip.Bounds.Center, other.Bounds.Center);
        Assert.That(completed, Is.False);
    }

    [Test]
    public void MidiLearnFromTheContextMenu()
    {
        var p = new LocalParameter("Cutoff");
        var knob = Add(new Knob(p), 10f, 10f, 64f, 84f);
        var service = new MidiLearnService();
        _host.MidiLearn = service;
        Assert.That(_host.GetParameterMenuItems(p).Select(i => i.Text), Is.EqualTo(new[] { "MIDI Learn" }));

        _host.DispatchMouseDown(knob.Bounds.Center, MouseButton.Right, 1, ModifierKeys.None);
        _host.DispatchMouseUp(knob.Bounds.Center, MouseButton.Right, ModifierKeys.None);
        Assert.That(_host.Popups.OfType<MenuPopup>().Count(), Is.EqualTo(1), "own menu when no DAW menu handler");
        _host.CloseAllPopups();

        _host.GetParameterMenuItems(p)[0].Action!();
        Assert.That(knob.IsMidiLearning, Is.True);
        Assert.That(service.ProcessController(0, 74, 0.0), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(knob.IsMidiLearning, Is.False);
            Assert.That(service.GetAssignment(p), Is.EqualTo("CC 74 · Ch 1"));
            Assert.That(_host.GetParameterMenuItems(p).Select(i => i.Text), Is.EqualTo(new[] { "MIDI Learn", "Forget MIDI assignment (CC 74 · Ch 1)" }));
        });
        service.ProcessController(0, 74, 0.75);
        Assert.That(p.NormalizedValue, Is.EqualTo(0.75).Within(1e-9));
        Assert.That(p.IsEditing, Is.False);
        _host.Language = UILanguages.French;
        Assert.That(_host.GetParameterMenuItems(p)[0].Text, Is.EqualTo("Apprentissage MIDI"));
        service.Forget(p);
        Assert.That(service.GetAssignment(p), Is.Null);
    }

    [Test]
    public void ABCompareSwitchesAndCopies()
    {
        var a = new LocalParameter("A");
        var b = new LocalParameter("B");
        var bar = Add(new ABCompareBar { Parameters = [a, b] }, 0f, 0f, 120f, 24f);
        a.SetValue(0.2);
        bar.Select(true);
        Assert.That(a.NormalizedValue, Is.EqualTo(0.2), "B starts as a copy of A");
        a.SetValue(0.9);
        bar.Select(false);
        Assert.That(a.NormalizedValue, Is.EqualTo(0.2));
        bar.Select(true);
        Assert.That(a.NormalizedValue, Is.EqualTo(0.9));
        bar.CopyToOther();
        bar.Select(false);
        Assert.That(a.NormalizedValue, Is.EqualTo(0.9), "B copied to A");
    }

    [Test]
    public void HistoryGraphKeepsTheLatestValues()
    {
        var graph = new HistoryGraph { Capacity = 8 };
        for (int i = 0; i < 12; i++) graph.Push(i);
        Assert.That(graph.Snapshot(), Is.EqualTo(new float[] { 4, 5, 6, 7, 8, 9, 10, 11 }));
        graph.Capacity = 16;
        Assert.That(graph.Snapshot(), Is.EqualTo(new float[] { 4, 5, 6, 7, 8, 9, 10, 11 }));
        float v = 0f;
        graph.ValueSource = () => v += 1f;
        graph.SamplesPerSecond = 10f;
        graph.Advance(0.35); // 3 samples due → same polled value repeated
        Assert.That(graph.Count, Is.EqualTo(11));
        Assert.That(graph.Latest, Is.EqualTo(1f));
    }

    [Test]
    public void LayeredPanelStacksFullSizeLayers()
    {
        var layers = Add(new LayeredPanel(), 10f, 10f, 200f, 100f);
        var back = new Probe();
        var front = new Probe();
        layers.Add(back, front);
        _host.UpdateLayout();
        Assert.That((back.Bounds, front.Bounds), Is.EqualTo((new Rect(10f, 10f, 200f, 100f), new Rect(10f, 10f, 200f, 100f))));
        layers.SendToBack(front);
        Assert.That(layers.Children, Is.EqualTo(new UIElement[] { front, back }));
        layers.BringToFront(front);
        Assert.That(layers.Children[^1], Is.SameAs(front));
        Assert.Throws<ArgumentException>(() => layers.BringToFront(new Probe()));
    }

    [Test]
    public void BitmapFontMapsCharactersToCells()
    {
        var atlas = ImageSource.FromPixels(16 * 8, 6 * 12, new byte[16 * 8 * 6 * 12 * 4]);
        var font = new BitmapFont(atlas, 8, 12);
        Assert.Multiple(() =>
        {
            Assert.That(font.GlyphPixels(' '), Is.EqualTo(new Rect(0f, 0f, 8f, 12f)));
            Assert.That(font.GlyphPixels('A'), Is.EqualTo(new Rect(1 * 8f, 2 * 12f, 8f, 12f))); // 'A' = 65 → cell 33
            Assert.That(font.GlyphPixels('é'), Is.EqualTo(font.GlyphPixels(' ')), "fallback");
            Assert.That(font.GlyphSize, Is.EqualTo(new Size(8f, 12f)));
        });
        var custom = new BitmapFont(atlas, 8, 12, characters: "0123456789");
        Assert.That(custom.GlyphPixels('3'), Is.EqualTo(new Rect(24f, 0f, 8f, 12f)));
    }

    [Test]
    public void WaveformMarkersAreConstrainedAndBound()
    {
        var start = new LocalParameter("Start");
        var view = Add(new WaveformView { StartParameter = start }, 0f, 0f, 402f, 104f);
        view.Audio = new AudioData([Enumerable.Range(0, 1000).Select(i => MathF.Sin(i * 0.05f)).ToArray()], 48000);
        view.MoveMarker(WaveformMarker.End, 0.6);
        view.MoveMarker(WaveformMarker.Start, 0.8);
        Assert.Multiple(() =>
        {
            Assert.That(view.EndPosition, Is.EqualTo(0.6));
            Assert.That(view.StartPosition, Is.EqualTo(0.6), "start cannot pass the end");
            Assert.That(start.NormalizedValue, Is.EqualTo(0.6));
        });
        // Drag the end marker (x = 1 + 0.6 × 400) to the right.
        float x = 1f + 0.6f * 400f * 999f / 1000f;
        Drag(new Point(x, 50f), new Point(x + 100f, 50f));
        Assert.That(view.EndPosition, Is.EqualTo(0.85).Within(0.01));
        view.Zoom(0.5, 500);
        Assert.That(view.ViewLength, Is.EqualTo(500).Within(1e-9));
        DoubleClick(new Point(100f, 50f));
        Assert.That(view.ViewLength, Is.EqualTo(1000));
    }

    [Test]
    public void SpectrogramShowsASineAtItsFrequency()
    {
        var ring = new AudioRingBuffer(16384);
        var spectrogram = Add(new Spectrogram { Source = ring, FftSize = 2048, MinFrequency = 100f, MaxFrequency = 10000f }, 0f, 0f, 100f, 100f);
        var block = new float[4096];
        for (int i = 0; i < block.Length; i++) block[i] = 0.5f * MathF.Sin(2f * MathF.PI * 1000f * i / 48000f);
        ring.Write(block);
        spectrogram.Advance(1.0 / 60.0);
        var pixels = (byte[])typeof(Spectrogram).GetField("_pixels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(spectrogram)!;
        int Brightness(int row) => pixels[row * 100 * 4 + 99 * 4 + 2]; // red channel of the newest column
        int sineRow = (int)MathF.Round(100f - MathF.Log(1000f / 100f) / MathF.Log(100f) * 100f); // 1 kHz on a 100 Hz … 10 kHz log axis
        Assert.That(Brightness(sineRow), Is.GreaterThan(Brightness(10) + 50).And.GreaterThan(Brightness(90) + 50));
    }

    [Test]
    public void CorrelationMeterFollowsItsSources()
    {
        var left = new AudioRingBuffer(16384);
        var right = new AudioRingBuffer(16384);
        var meter = new CorrelationMeter { LeftSource = left, RightSource = right, SmoothingSeconds = 0f };
        var x = new float[8192];
        for (int i = 0; i < x.Length; i++) x[i] = MathF.Sin(i * 0.01f);
        left.Write(x);
        right.Write(x.Select(v => -v).ToArray());
        meter.Update(0.016);
        Assert.That(meter.Correlation, Is.EqualTo(-1.0).Within(1e-6));
    }

    [Test]
    public void FrenchTranslationIsComplete()
    {
        var same = typeof(UIStrings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.Name != nameof(UIStrings.LanguageCode))
            .Where(p => (string?)p.GetValue(UIStrings.French) == (string?)p.GetValue(UIStrings.English))
            .Select(p => p.Name).ToArray();
        Assert.That(same, Is.EquivalentTo(new[] { nameof(UIStrings.Ok), nameof(UIStrings.PresetPlaceholder), nameof(UIStrings.VersionFormat) }));
    }
    [Test]
    public void ViewSwitcherShowsTheViewOfTheParameterValue()
    {
        var type = LocalParameter.FromList("Effect", ["Chorus", "Delay", "Reverb"], 1);
        var views = new[] { new Probe(), new Probe(), new Probe() };
        var switcher = Add(new ViewSwitcher { Parameter = type }, 10f, 10f, 200f, 100f);
        int raised = -1;
        switcher.SelectionChanged += (_, index) => raised = index;
        switcher.Add(views);
        _host.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(views.Select(v => v.IsVisible), Is.EqualTo(new[] { false, true, false }));
            Assert.That(switcher.SelectedView, Is.SameAs(views[1]));
            Assert.That(views[1].Bounds, Is.EqualTo(new Rect(10f, 10f, 200f, 100f)), "full content area");
            Assert.That(_host.HitAt(new Point(50f, 50f)), Is.SameAs(views[1]), "hidden views are not hit-tested");
        });

        type.SetValue(1.0); // host automation, preset...
        Assert.That((switcher.SelectedIndex, raised), Is.EqualTo((2, 2)));
        Assert.That(views.Select(v => v.IsVisible), Is.EqualTo(new[] { false, false, true }));

        switcher.SelectedIndex = 0; // edits the parameter
        Assert.That((type.NormalizedValue, raised, views[0].IsVisible), Is.EqualTo((0.0, 0, true)));
    }

    [Test]
    public void ViewSwitcherMapsContinuousValuesAndWorksWithoutParameter()
    {
        var switcher = Add(new ViewSwitcher(), 0f, 0f, 100f, 100f);
        Assert.That(switcher.SelectedIndex, Is.EqualTo(-1), "no view");
        var a = new Probe();
        var b = new Probe();
        switcher.Add(a, b);
        _host.UpdateLayout();
        Assert.That((switcher.SelectedIndex, a.IsVisible, b.IsVisible), Is.EqualTo((0, true, false)));
        switcher.SelectedIndex = 5; // clamped to the last view
        _host.UpdateLayout();
        Assert.That((switcher.SelectedIndex, a.IsVisible, b.IsVisible), Is.EqualTo((1, false, true)));

        var mix = new LocalParameter("Mix");
        switcher.Parameter = mix;
        var c = new Probe();
        switcher.Add(c);
        _host.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(switcher.IndexForValue(0.0, 3), Is.EqualTo(0));
            Assert.That(switcher.IndexForValue(0.3, 3), Is.EqualTo(1));
            Assert.That(switcher.IndexForValue(0.8, 3), Is.EqualTo(2));
        });
        mix.SetValue(0.6);
        Assert.That((switcher.SelectedView, a.IsVisible, b.IsVisible, c.IsVisible), Is.EqualTo((b, false, true, false)));
    }

    [Test]
    public void LevelMeterSkinGetsEachChannelLevelAndClipState()
    {
        var meter = Add(new LevelMeter { InputUnit = LevelUnit.Decibels, MinDb = -60f, MaxDb = 0f, ShowScale = false, Skin = new NullSkin() }, 0f, 0f, 40f, 100f);
        meter.SetLevels(-30f, 3f);
        var states = meter.ChannelSkinStates(new Rect(0f, 0f, 40f, 100f), out var scale);
        Assert.Multiple(() =>
        {
            Assert.That(states, Has.Length.EqualTo(2));
            Assert.That(states[0].Value, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(states[1].Value, Is.EqualTo(1.0));
            Assert.That((states[0].StateIndex, states[1].StateIndex), Is.EqualTo((0, 1)), "clip latched on the second channel");
            Assert.That(states[0].Bounds, Is.EqualTo(new Rect(0f, 0f, 19f, 100f)), "no clip strip with a skin");
            Assert.That(states[1].Bounds, Is.EqualTo(new Rect(21f, 0f, 19f, 100f)));
            Assert.That(scale.IsEmpty, Is.True);
        });
    }

    private sealed class NullSkin : IControlSkin
    {
        public void Render(DrawingContext dc, Control control, in SkinState state) { }
    }
}
