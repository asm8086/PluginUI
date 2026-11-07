// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;
using PluginUI.Dsp;

namespace PluginUI.Tests;

/// <summary>The YIN pitch detector, the tuner and the wavetable view (no window).</summary>
public class TunerWavetableTests
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

    private T Add<T>(T element, float width, float height) where T : UIElement
    {
        element.SetBounds(0f, 0f, width, height);
        _root.Children.Add(element);
        _host.UpdateLayout();
        return element;
    }

    private static float[] Tone(double frequency, int count, double sampleRate = 48000.0, int harmonics = 1)
    {
        var x = new float[count];
        for (int i = 0; i < count; i++)
        {
            double v = 0.0;
            for (int k = 1; k <= harmonics; k++) v += Math.Sin(2.0 * Math.PI * k * frequency * i / sampleRate) / k;
            x[i] = (float)(0.5 * v);
        }
        return x;
    }

    [Test]
    public void YinFindsTheFundamental()
    {
        var yin = new PitchDetector(48000, 40, 2000);
        Assert.That((yin.WindowSize, yin.MinLag), Is.EqualTo((2400, 24)));
        Assert.Multiple(() =>
        {
            Assert.That(yin.Detect(Tone(440.0, 4096)).Frequency, Is.EqualTo(440.0).Within(0.2));
            Assert.That(yin.Detect(Tone(82.41, 4096, harmonics: 12)).Frequency, Is.EqualTo(82.41).Within(0.1), "a low E with harmonics");
            Assert.That(yin.Detect(Tone(1318.5, 4096)).Frequency, Is.EqualTo(1318.5).Within(1.0));
            Assert.That(yin.Detect(Tone(440.0, 4096)).Clarity, Is.GreaterThan(0.95));
            Assert.That(yin.Detect(new float[4096]).HasPitch, Is.False, "silence");
        });
        var random = new Random(3);
        var noise = new float[4096];
        for (int i = 0; i < noise.Length; i++) noise[i] = (float)(random.NextDouble() * 2.0 - 1.0) * 0.5f;
        var estimate = yin.Detect(noise);
        Assert.That(!estimate.HasPitch || estimate.Clarity < 0.8, Is.True, "noise has no clear pitch");
        Assert.Throws<ArgumentException>(() => yin.Detect(new float[100]));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PitchDetector(48000, 500, 100));
    }

    [Test]
    public void TheTunerShowsTheNoteAndTheDeviation()
    {
        var ring = new AudioRingBuffer(16384);
        var tuner = Add(new Tuner { Source = ring, SampleRate = 48000f }, 260f, 150f);
        var value = (IValuePattern)tuner.GetAutomationPeer()!.GetPattern(AutomationPattern.Value)!;
        Assert.That(value.Value, Is.EqualTo("no note"));

        // 446 Hz is A4 + 23.4 cents.
        ring.Write(Tone(446.0, 4096));
        _host.StepFrame(1.0 / 60.0);
        Assert.Multiple(() =>
        {
            Assert.That(tuner.Note, Is.EqualTo(69));
            Assert.That(tuner.Cents, Is.EqualTo(23.4).Within(0.5));
            Assert.That(tuner.IsInTune, Is.False);
            Assert.That(value.Value, Is.EqualTo("A4, +23 cents"));
            Assert.That(tuner.GetAutomationPeer()!.LocalizedControlType, Is.EqualTo("tuner"));
        });

        tuner.SetFrequency(440.5);
        Assert.That(value.Value, Is.EqualTo("A4, +2 cents, in tune"));
        tuner.ReferenceHz = 432.0;
        Assert.That(tuner.Cents, Is.EqualTo(1200.0 * Math.Log2(440.5 / 432.0)).Within(1e-6), "relative to the reference");

        // No new audio: the note stays for the hold time, then goes.
        _host.StepFrame(0.5);
        Assert.That(tuner.Note, Is.EqualTo(69));
        _host.StepFrame(0.6);
        Assert.Multiple(() =>
        {
            Assert.That(tuner.Note, Is.EqualTo(-1));
            Assert.That(value.Value, Is.EqualTo("no note"));
        });

        // A frequency measured by the plug-in.
        tuner.Source = null;
        tuner.ReferenceHz = 440.0;
        tuner.FrequencySource = () => 277.18;
        tuner.UseFlats = true;
        _host.StepFrame(1.0 / 60.0);
        Assert.That(tuner.NoteName(tuner.Note), Is.EqualTo("D♭4"));
        tuner.UseFlats = false;
        tuner.MiddleCOctave = 3;
        Assert.That(tuner.NoteName(tuner.Note), Is.EqualTo("C#3"));
        var reference = new LocalParameter("Reference", 400, 480, 440, "Hz");
        tuner.ReferenceParameter = reference;
        tuner.ReferenceHz = 415.0;
        Assert.That(reference.ToPlain(reference.NormalizedValue), Is.EqualTo(415.0).Within(1e-9));
    }

    [Test]
    public void TheWavetableViewScansTheFrames()
    {
        var frames = WavetableView.CreateMorph(8, 64, 8);
        Assert.Multiple(() =>
        {
            Assert.That(frames[0][16], Is.EqualTo(1f).Within(1e-5f), "the first frame is a sine");
            Assert.That(frames[7].Max(MathF.Abs), Is.EqualTo(1f).Within(1e-5f), "normalized");
        });
        var position = new LocalParameter("Position", 0, 1, 0.5);
        var view = Add(new WavetableView { Parameter = position, Label = "" }, 280f, 180f);
        view.SetFrames(frames);
        var current = view.CurrentFrame;
        Assert.Multiple(() =>
        {
            Assert.That(current[10], Is.EqualTo((frames[3][10] + frames[4][10]) * 0.5f).Within(1e-6f), "between frames 4 and 5");
            Assert.That(view.ValueText, Is.EqualTo("5 / 8"));
            Assert.That((view.FrameCount, view.FrameLength), Is.EqualTo((8, 64)));
        });

        int gestures = 0;
        position.EditStarted += _ => gestures++;
        _host.DispatchMouseDown(new Point(140f, 120f), MouseButton.Left, 1, ModifierKeys.None);
        _host.DispatchMouseMove(new Point(140f, 70f), ModifierKeys.None);
        _host.DispatchMouseUp(new Point(140f, 70f), MouseButton.Left, ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(position.NormalizedValue, Is.EqualTo(0.75).Within(1e-6), "50 DIPs up of 200");
            Assert.That(gestures, Is.EqualTo(1));
            Assert.That(view.GetAutomationPeer()!.ControlType, Is.EqualTo(AutomationControlType.Slider));
        });
        view.Display = WavetableDisplay.Single;
        Assert.Throws<ArgumentException>(() => view.SetFrames([new float[8], new float[9]]));
    }
}
