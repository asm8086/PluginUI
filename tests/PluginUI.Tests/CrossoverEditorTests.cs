// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>The crossover editor: response, splits, gains, solo / mute, keyboard and screen readers (no window).</summary>
public class CrossoverEditorTests
{
    private UIHost _host = null!;
    private Canvas _root = null!;

    // The graph of a 480 × 200 editor at (0, 0): 2 px border, 16 px of split handles above, 14 px of labels below.
    private static readonly Rect Area = new(2f, 18f, 476f, 168f);

    [SetUp]
    public void SetUp()
    {
        _root = new Canvas();
        _host = new UIHost(_root, new Size(800f, 600f));
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private CrossoverEditor Add(CrossoverEditor editor)
    {
        editor.SetBounds(0f, 0f, 480f, 200f);
        _root.Children.Add(editor);
        _host.UpdateLayout();
        return editor;
    }

    private static float X(double hz) => Area.X + MathF.Log((float)hz / 20f) / MathF.Log(1000f) * Area.Width;

    private static float Y(double db) => Area.Bottom - (float)((db + 24.0) / 48.0) * Area.Height;

    private void Drag(Point from, Point to, ModifierKeys modifiers = ModifierKeys.None)
    {
        _host.DispatchMouseDown(from, MouseButton.Left, 1, modifiers);
        for (int i = 1; i <= 4; i++) _host.DispatchMouseMove(Point.Lerp(from, to, i / 4f), modifiers);
        _host.DispatchMouseUp(to, MouseButton.Left, modifiers);
    }

    private void Click(Point p, int count = 1)
    {
        _host.DispatchMouseDown(p, MouseButton.Left, count, ModifierKeys.None);
        _host.DispatchMouseUp(p, MouseButton.Left, ModifierKeys.None);
    }

    private void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _host.DispatchKeyDown(new KeyEventArgs(key, modifiers));

    [Test]
    public void BandsFollowTheLinkwitzRileyResponse()
    {
        var editor = new CrossoverEditor();
        Assert.Multiple(() =>
        {
            Assert.That(editor.BandCount, Is.EqualTo(3));
            Assert.That(editor.Splits.Select(s => s.Frequency), Is.EqualTo(new[] { 200.0, 2000.0 }));
            Assert.That(editor.BandResponseDb(0, 200), Is.EqualTo(-6.0206).Within(1e-3), "Linkwitz-Riley: -6 dB at the split");
            Assert.That(editor.BandResponseDb(1, 200), Is.EqualTo(-6.0206).Within(1e-3));
            Assert.That(editor.BandResponseDb(0, 20), Is.EqualTo(0.0).Within(0.01));
            Assert.That(editor.BandResponseDb(0, 400), Is.EqualTo(-24.6).Within(0.2), "24 dB per octave above the split");
            Assert.That(editor.BandResponseDb(2, 20000), Is.EqualTo(0.0).Within(0.01));
        });
        editor.Bands[1].GainDb = 3.0;
        Assert.That(editor.BandResponseDb(1, 632), Is.EqualTo(3.0 - 0.173).Within(0.01), "the band gain adds to its response (both filters: -0.17 dB there)");
        editor.SlopeDbPerOctave = 12;
        Assert.That(editor.BandResponseDb(0, 400), Is.EqualTo(-13.98).Within(0.01), "LR2: 1 / (1 + 2²) one octave above");

        editor.BandCount = 4;
        Assert.Multiple(() =>
        {
            Assert.That(editor.Bands, Has.Count.EqualTo(4));
            Assert.That(editor.Splits.Select(s => s.Frequency), Is.EqualTo(new[] { 112.0, 632.0, 3557.0 }));
            Assert.That(editor.Bands[1].GainDb, Is.EqualTo(3.0), "existing bands are kept");
        });
        editor.BandCount = 20;
        Assert.That(editor.BandCount, Is.EqualTo(8));
    }

    [Test]
    public void SplitsDragWithGesturesAndKeepTheirSpacing()
    {
        var editor = Add(new CrossoverEditor());
        var low = new LocalParameter("Low split", scale: ParameterScale.Logarithmic(20, 20000), defaultValue: 200);
        editor.Splits[0].FrequencyParameter = low;
        int begins = 0, ends = 0;
        low.EditStarted += _ => begins++;
        low.EditEnded += _ => ends++;
        int changed = 0;
        editor.SplitChanged += (_, _) => changed++;

        // The handle above the graph.
        Drag(new Point(X(200), 9f), new Point(X(400), 9f));
        Assert.Multiple(() =>
        {
            Assert.That(editor.Splits[0].Frequency, Is.EqualTo(400).Within(4));
            Assert.That(low.IsEditing, Is.False);
            Assert.That((begins, ends), Is.EqualTo((1, 1)), "one complete gesture");
            Assert.That(changed, Is.GreaterThan(0));
        });

        // Anywhere on the line, towards the next split: it stops a third of an octave below it.
        Drag(new Point(X(400), Area.Center.Y), new Point(X(5000), Area.Center.Y));
        Assert.That(editor.Splits[0].Frequency, Is.EqualTo(2000 / Math.Pow(2, 0.333)).Within(1));
        Drag(new Point(X(editor.Splits[0].Frequency), Area.Center.Y), new Point(-50f, Area.Center.Y));
        Assert.That(editor.Splits[0].Frequency, Is.EqualTo(20).Within(0.01), "and at the edge of the display");
    }

    [Test]
    public void GainsSoloAndMuteAreEditedOnTheBands()
    {
        var editor = Add(new CrossoverEditor());
        var gain = new LocalParameter("Mid gain", -24, 24, 0);
        var solo = new LocalParameter("Mid solo", 0, 1, 0);
        editor.Bands[1].GainParameter = gain;
        editor.Bands[1].SoloParameter = solo;
        int begins = 0;
        gain.EditStarted += _ => begins++;

        float mid = X(632);
        Drag(new Point(mid, Y(0)), new Point(mid, Y(0) + 42f));
        Assert.Multiple(() =>
        {
            Assert.That(editor.Bands[1].GainDb, Is.EqualTo(-12).Within(0.3), "48 dB over the graph height");
            Assert.That(gain.IsEditing, Is.False);
            Assert.That(begins, Is.EqualTo(1));
        });
        Click(new Point(mid, Y(-12)), 2);
        Assert.That(editor.Bands[1].GainDb, Is.EqualTo(0).Within(1e-9), "double-click: 0 dB");

        // The S and M chips at the bottom of the band.
        float chipY = Area.Bottom - 15f - 4f + 7f;
        Click(new Point(mid - 8f, chipY));
        Assert.Multiple(() =>
        {
            Assert.That(editor.Bands[1].IsSolo, Is.True);
            Assert.That(solo.NormalizedValue, Is.EqualTo(1.0));
            Assert.That(editor.IsAudible(0), Is.False, "another band is soloed");
            Assert.That(editor.IsAudible(1), Is.True);
        });
        float lowBand = (Area.X + X(200)) * 0.5f;
        Click(new Point(lowBand + 8f, chipY));
        Assert.That(editor.Bands[0].IsMuted, Is.True);
        Click(new Point(mid - 8f, chipY));
        Assert.Multiple(() =>
        {
            Assert.That(editor.IsAudible(0), Is.False, "muted");
            Assert.That(editor.IsAudible(2), Is.True);
        });
    }

    [Test]
    public void TheKeyboardSelectsAndEditsAndScreenReadersFollow()
    {
        var editor = Add(new CrossoverEditor());
        editor.Focus();
        var peer = editor.GetAutomationPeer()!;
        var value = (IValuePattern)peer.GetPattern(AutomationPattern.Value)!;
        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Group));
            Assert.That(peer.IsKeyboardFocusable, Is.True);
            Assert.That(value.IsReadOnly, Is.True);
            Assert.That(value.Value, Is.EqualTo("200 Hz, 2.00 kHz").Or.EqualTo("200Hz, 2kHz").Or.Contains("200"), "the splits when nothing is selected");
        });

        Press(Key.Home);
        Assert.That(editor.SelectedBand, Is.EqualTo(0));
        Press(Key.PageDown);
        Assert.That(editor.SelectedSplit, Is.EqualTo(0));
        Press(Key.Right);
        Assert.That(editor.Splits[0].Frequency, Is.EqualTo(200 * Math.Pow(2, 1.0 / 12)).Within(1e-6), "a semitone up");
        Press(Key.Left, ModifierKeys.Shift);
        Assert.That(editor.Splits[0].Frequency, Is.EqualTo(200 * Math.Pow(2, 1.0 / 12 - 1.0 / 48)).Within(1e-6), "a quarter of it down");
        Assert.That(value.Value, Does.StartWith("Crossover 1: "));

        Press(Key.Right, ModifierKeys.Control);
        Assert.That(editor.SelectedBand, Is.EqualTo(1));
        Press(Key.Up);
        Press(Key.Up);
        Press(Key.S);
        Press(Key.M);
        Assert.Multiple(() =>
        {
            Assert.That(editor.Bands[1].GainDb, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(editor.Bands[1].IsSolo && editor.Bands[1].IsMuted, Is.True);
            Assert.That(value.Value, Is.EqualTo("Band 2: +2.0 dB, Solo, Mute"));
        });
        Press(Key.Delete);
        Assert.That(editor.Bands[1].GainDb, Is.EqualTo(0.0));
        Press(Key.End);
        Assert.That(editor.SelectedBand, Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => value.SetValue("1 kHz"));
    }
}
