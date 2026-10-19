// 2026/10/19 20:27:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Dsp;

namespace PluginUI.Tests;

/// <summary>Interaction and analysis tests of <see cref="FrequencyResponseView"/> (simulated mouse, no window).</summary>
public class FrequencyResponseViewTests
{
    private UIHost _host = null!;
    private Canvas _root = null!;
    private FrequencyResponseView _view = null!;
    private Rect _area;

    [SetUp]
    public void SetUp()
    {
        _root = new Canvas();
        _host = new UIHost(_root, new Size(800f, 600f));
        _view = new FrequencyResponseView { SampleRate = 48000 };
        _view.SetBounds(0f, 0f, 644f, 317f);
        _root.Children.Add(_view);
        _host.UpdateLayout();
        _area = FrequencyResponseView.PlotArea(new Rect(0f, 0f, 644f, 317f));
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private void Drag(Point from, Point to, ModifierKeys modifiers = ModifierKeys.None, int steps = 4)
    {
        _host.DispatchMouseDown(from, MouseButton.Left, 1, modifiers);
        for (int i = 1; i <= steps; i++) _host.DispatchMouseMove(Point.Lerp(from, to, i / (float)steps), modifiers);
        _host.DispatchMouseUp(to, MouseButton.Left, modifiers);
    }

    private float Y(double db) => (float)(_area.Bottom - (db - _view.LeftMin) / (_view.LeftMax - _view.LeftMin) * _area.Height);

    private static LocalParameter Hz(string name, double value) =>
        new(name, scale: ParameterScale.Logarithmic(20, 20000), defaultValue: value);

    [Test]
    public void LogarithmicAndLinearAxesMapFrequenciesBothWays()
    {
        Assert.That(_view.FrequencyToX(20, _area), Is.EqualTo(_area.X).Within(1e-3));
        Assert.That(_view.FrequencyToX(20000, _area), Is.EqualTo(_area.Right).Within(1e-3));
        Assert.That(_view.XToFrequency(_view.FrequencyToX(632.5, _area), _area), Is.EqualTo(632.5).Within(1e-2));
        _view.FrequencyScale = FrequencyScale.Linear;
        Assert.Multiple(() =>
        {
            Assert.That(_view.AxisMinFrequency, Is.EqualTo(0.0));
            Assert.That(_view.AxisMaxFrequency, Is.EqualTo(24000.0));
            Assert.That(_view.FrequencyToX(12000, _area), Is.EqualTo(_area.X + _area.Width / 2f).Within(1e-3));
            Assert.That(_view.FrequencyTicks(0, 24000).Count(), Is.InRange(5, 14));
        });
    }

    [Test]
    public void DraggingABandNodeShiftsBothEdgesWithParallelGesturesAndAConstantRatio()
    {
        var low = Hz("Low", 500);
        var high = Hz("High", 2000);
        var gain = new LocalParameter("Gain", -24, 24, 6);
        var node = new ResponseNode
        {
            UsesBandEdges = true, LowFrequencyParameter = low, HighFrequencyParameter = high,
            ValueParameter = gain, IsValueEditable = true,
        };
        _view.Nodes.Add(node);
        bool allEditing = false;
        low.Changed += _ => allEditing |= low.IsEditing && high.IsEditing && gain.IsEditing;

        var start = new Point(_view.FrequencyToX(1000, _area), Y(6));
        Drag(start, start + new Point(_view.FrequencyToX(2000, _area) - start.X, Y(12) - Y(6)));
        Assert.Multiple(() =>
        {
            Assert.That(node.Frequency, Is.EqualTo(2000).Within(30));
            Assert.That(node.HighFrequency / node.LowFrequency, Is.EqualTo(4.0).Within(0.05));
            Assert.That(node.Value, Is.EqualTo(12).Within(0.3));
            Assert.That(allEditing, Is.True, "the three parameters are edited in parallel gestures");
            Assert.That(low.IsEditing || high.IsEditing || gain.IsEditing, Is.False, "gestures are closed");
        });
    }

    [Test]
    public void EdgeHandlesMoveOneEdgeOnly()
    {
        var node = new ResponseNode { UsesBandEdges = true, LowFrequency = 500, HighFrequency = 2000 };
        _view.Nodes.Add(node);
        var handle = new Point(_view.FrequencyToX(2000, _area), Y(0));
        Drag(handle, new Point(_view.FrequencyToX(4000, _area), Y(0)));
        Assert.Multiple(() =>
        {
            Assert.That(node.LowFrequency, Is.EqualTo(500));
            Assert.That(node.HighFrequency, Is.EqualTo(4000).Within(40));
        });

        // The low edge cannot cross the high edge (minimum ratio).
        var lowHandle = new Point(_view.FrequencyToX(500, _area), Y(0));
        Drag(lowHandle, new Point(_view.FrequencyToX(10000, _area), Y(0)));
        Assert.That(node.HighFrequency / node.LowFrequency, Is.EqualTo(_view.MinBandRatio).Within(1e-6));
    }

    [Test]
    public void NodesWithoutGainStayOnTheirValue()
    {
        var f = Hz("F", 1000);
        var node = new ResponseNode { FrequencyParameter = f };
        _view.Nodes.Add(node);
        var start = new Point(_view.FrequencyToX(1000, _area), Y(0));
        Drag(start, start + new Point(40f, -60f));
        Assert.Multiple(() =>
        {
            Assert.That(node.Value, Is.EqualTo(0.0));
            Assert.That(node.Frequency, Is.GreaterThan(1300));
        });
    }

    [Test]
    public void WheelChangesTheBandRatioAroundTheCentreOrStepsTheWheelParameter()
    {
        var band = new ResponseNode { UsesBandEdges = true, LowFrequency = 500, HighFrequency = 2000 };
        var width = new LocalParameter("Q", 0.1, 10, 1);
        var single = new ResponseNode { Frequency = 5000, WheelParameter = width };
        _view.Nodes.Add(band);
        _view.Nodes.Add(single);

        _host.DispatchWheel(new Point(_view.FrequencyToX(1000, _area), Y(0)), 1f, false, ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(band.Frequency, Is.EqualTo(1000).Within(1e-6));
            Assert.That(band.HighFrequency / band.LowFrequency, Is.LessThan(4.0), "wheel up narrows the band");
        });

        double before = width.NormalizedValue;
        _host.DispatchWheel(new Point(_view.FrequencyToX(5000, _area), Y(0)), 1f, false, ModifierKeys.None);
        Assert.That(width.NormalizedValue, Is.EqualTo(before + single.WheelStep).Within(1e-9));
    }

    [Test]
    public void HiddenNodesCannotBeHit()
    {
        var node = new ResponseNode { Frequency = 1000, IsVisible = false };
        _view.Nodes.Add(node);
        var p = new Point(_view.FrequencyToX(1000, _area), Y(0));
        Drag(p, p + new Point(50f, 0f));
        Assert.That(node.Frequency, Is.EqualTo(1000));
    }

    [Test]
    public void SpectrumOfAFullScaleSineReadsZeroDecibelsWithHalfFrameHops()
    {
        var ring = new AudioRingBuffer(16384);
        _view.SpectrumSource = ring;
        _view.FftSize = 2048;
        _view.SpectrumReleaseSeconds = 0.0;
        _view.DecibelConvention = DecibelConvention.SixLog2;

        // 1500 Hz is exactly bin 64 at 48 kHz / 2048: no scalloping loss.
        var block = new float[5120];
        for (int i = 0; i < block.Length; i++) block[i] = (float)Math.Sin(2.0 * Math.PI * 1500.0 * i / 48000.0);
        ring.Write(block);
        _view.AnalyzeSpectrum();

        Assert.Multiple(() =>
        {
            // 5120 samples = first full frame at 2048, then one frame every 1024 samples: 2048, 3072, 4096, 5120.
            Assert.That(_view.SpectrumFrameCount, Is.EqualTo(4));
            Assert.That(_view.SpectrumLevel(1500), Is.EqualTo(0.0).Within(0.05));
            Assert.That(_view.SpectrumLevel(6000), Is.LessThan(-80.0));
        });
    }

    [Test]
    public void ReleaseSmoothsDecreasingLevelsOnly()
    {
        var ring = new AudioRingBuffer(16384);
        _view.SpectrumSource = ring;
        _view.FftSize = 1024;
        _view.SpectrumReleaseSeconds = 1.0;
        var tone = new float[4096];
        for (int i = 0; i < tone.Length; i++) tone[i] = (float)Math.Sin(2.0 * Math.PI * 3000.0 * i / 48000.0);
        ring.Write(tone);
        _view.AnalyzeSpectrum();
        double loud = _view.SpectrumLevel(3000);
        ring.Write(new float[1024]);
        _view.AnalyzeSpectrum();
        double after = _view.SpectrumLevel(3000);
        Assert.Multiple(() =>
        {
            Assert.That(loud, Is.EqualTo(0.0).Within(0.1), "attack is instantaneous");
            Assert.That(after, Is.LessThan(loud).And.GreaterThan(loud - 10.0), "release is gradual");
        });
    }
}

/// <summary><see cref="DropDown.IsItemAvailable"/>: unavailable items are skipped by the wheel and the arrow keys.</summary>
public class DropDownAvailabilityTests
{
    [Test]
    public void WheelAndKeysSkipUnavailableItems()
    {
        var root = new Canvas();
        using var host = new UIHost(root, new Size(400f, 300f));
        var list = LocalParameter.FromList("Design", ["a", "b", "c", "d", "e", "f"]);
        var drop = new DropDown { Parameter = list, IsItemAvailable = i => i is 0 or 3 or 5 };
        drop.SetBounds(10f, 10f, 120f, 24f);
        root.Children.Add(drop);
        host.UpdateLayout();

        host.DispatchWheel(new Point(50f, 20f), -1f, false, ModifierKeys.None);   // wheel down = next item
        Assert.That(drop.SelectedIndex, Is.EqualTo(3));
        host.DispatchWheel(new Point(50f, 20f), -1f, false, ModifierKeys.None);
        Assert.That(drop.SelectedIndex, Is.EqualTo(5));
        host.DispatchWheel(new Point(50f, 20f), -1f, false, ModifierKeys.None);
        Assert.That(drop.SelectedIndex, Is.EqualTo(5), "no available item beyond the last one");
        host.DispatchWheel(new Point(50f, 20f), 1f, false, ModifierKeys.None);
        Assert.That(drop.SelectedIndex, Is.EqualTo(3));
    }
}
