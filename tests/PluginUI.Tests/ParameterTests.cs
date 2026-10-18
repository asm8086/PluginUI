// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

public class ParameterTests
{
    [Test]
    public void LogarithmicScaleRoundTrips()
    {
        var p = new LocalParameter("Cutoff", 20, 20000, 1000, "Hz", ParameterScale.Logarithmic(20, 20000), decimals: 0);
        Assert.Multiple(() =>
        {
            Assert.That(p.ToPlain(0.0), Is.EqualTo(20).Within(1e-9));
            Assert.That(p.ToPlain(1.0), Is.EqualTo(20000).Within(1e-6));
            Assert.That(p.ToPlain(0.5), Is.EqualTo(Math.Sqrt(20 * 20000)).Within(1e-6), "geometric mean at mid course");
            Assert.That(p.Value, Is.EqualTo(1000).Within(1e-6));
            Assert.That(p.ToDisplayStringWithUnits(p.NormalizedValue), Is.EqualTo("1000 Hz"));
        });
    }

    [TestCase("440", 440.0)]
    [TestCase("440 Hz", 440.0)]
    [TestCase("2k", 2000.0)]
    [TestCase("2kHz", 2000.0)]
    public void ParsesUserText(string text, double expected)
    {
        var p = new LocalParameter("Cutoff", 20, 20000, 1000, "Hz", ParameterScale.Logarithmic(20, 20000));
        Assert.That(p.TryParse(text, out double n), Is.True);
        Assert.That(p.ToPlain(n), Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void SteppedValuesAreQuantized()
    {
        var p = new LocalParameter("Voices", 1, 8, 4, stepCount: 7, decimals: 0);
        p.NormalizedValue = 0.52;
        Assert.Multiple(() =>
        {
            Assert.That(p.SelectedIndex, Is.EqualTo(4));
            Assert.That(p.NormalizedValue, Is.EqualTo(4.0 / 7.0).Within(1e-12));
            Assert.That(p.ToDisplayString(p.NormalizedValue), Is.EqualTo("5"));
        });
    }

    [Test]
    public void ListParameterUsesItems()
    {
        var p = LocalParameter.FromList("Mode", ["Low", "Band", "High"], 1);
        Assert.Multiple(() =>
        {
            Assert.That(p.StepCount, Is.EqualTo(2));
            Assert.That(p.ToDisplayString(p.NormalizedValue), Is.EqualTo("Band"));
            Assert.That(p.TryParse("high", out double n), Is.True);
            Assert.That(n, Is.EqualTo(1.0));
            Assert.That(p.TryParse("none", out _), Is.False);
        });
    }

    [Test]
    public void SetValueWrapsASingleGesture()
    {
        var p = new LocalParameter("Gain");
        var log = new List<string>();
        p.EditStarted += _ => log.Add("begin");
        p.Changed += _ => log.Add("change");
        p.EditEnded += _ => log.Add("end");
        p.SetValue(0.25);
        Assert.That(log, Is.EqualTo(new[] { "begin", "change", "end" }));
        Assert.That(p.IsEditing, Is.False);
    }
}
