// 2026/11/06 02:49:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

/// <summary><see cref="BandCenters"/> and <see cref="BandCenterParameter"/>.</summary>
public class BandCenterTests
{
    [Test]
    public void CentreIsDisplayedInHertzOrKilohertz()
    {
        var low = new LocalParameter("Low", defaultValue: 100.0, units: "Hz", scale: ParameterScale.Logarithmic(20.0, 20000.0));
        var high = new LocalParameter("High", defaultValue: 400.0, units: "Hz", scale: ParameterScale.Logarithmic(20.0, 20000.0));
        var centre = new BandCenterParameter(low, high);
        Assert.Multiple(() =>
        {
            Assert.That(centre.Units, Is.Empty, "the unit is in the text");
            Assert.That(centre.ToDisplayStringWithUnits(centre.NormalizedValue), Is.EqualTo("200 Hz"));
            Assert.That(centre.ToDisplayString(centre.ToNormalized(2500.0)), Is.EqualTo("2.50 kHz"));
            Assert.That(centre.ToDisplayString(centre.ToNormalized(12650.0)), Is.EqualTo("12.7 kHz"));
            Assert.That(centre.TryParse(centre.ToDisplayString(centre.ToNormalized(2500.0)), out double n) && Math.Abs(centre.ToPlain(n) - 2500.0) < 1e-6, Is.True, "the text reads back");
        });
    }

    [Test]
    public void BilinearCentreIsThePeakOfTheConstantinidesTransform()
    {
        var bilinear = BandCenters.Bilinear(() => 48000.0);
        double w1 = 2 * Math.PI * 10000.0 / 48000.0, w2 = 2 * Math.PI * 16000.0 / 48000.0;
        double expected = Math.Acos(Math.Cos((w1 + w2) / 2) / Math.Cos((w2 - w1) / 2)) * 48000.0 / (2 * Math.PI);
        Assert.Multiple(() =>
        {
            Assert.That(bilinear.Center(10000.0, 16000.0), Is.EqualTo(expected).Within(1e-9));
            Assert.That(bilinear.Center(10000.0, 16000.0), Is.EqualTo(13080.0).Within(10.0));
            Assert.That(bilinear.Center(100.0, 200.0), Is.EqualTo(Math.Sqrt(2.0) * 100.0).Within(0.01), "geometric at low frequencies");
            Assert.That(BandCenters.Geometric.Center(10000.0, 16000.0), Is.EqualTo(Math.Sqrt(1.6e8)).Within(1e-9));
        });
        foreach (double centre in new[] { 50.0, 1000.0, 9000.0, 15000.0 })
        {
            var (low, high) = bilinear.Edges(centre, 1.5);
            Assert.That(high / low, Is.EqualTo(1.5).Within(1e-12));
            Assert.That(bilinear.Center(low, high), Is.EqualTo(centre).Within(centre * 1e-10), $"{centre} Hz");
        }
        var (l, h) = bilinear.Edges(23000.0, 2.0);
        Assert.That(h, Is.LessThan(24000.0), "edges stay below Nyquist");
        Assert.That(h / l, Is.EqualTo(2.0).Within(1e-12));
    }

    [Test]
    public void TheCentreKnobMovesBothEdgesWithTheirRatio()
    {
        var scale = ParameterScale.Logarithmic(20, 20000);
        var low = new LocalParameter("Low", 20, 20000, 707, "Hz", scale);
        var high = new LocalParameter("High", 20, 20000, 1414, "Hz", scale);
        var center = new BandCenterParameter(low, high, BandCenters.Bilinear(() => 48000.0));
        int changes = 0;
        center.Changed += _ => changes++;
        Assert.That(center.Frequency, Is.EqualTo(1000.0).Within(5.0));
        center.SetValue(center.ToNormalized(12000.0));
        Assert.Multiple(() =>
        {
            Assert.That(center.Frequency, Is.EqualTo(12000.0).Within(1e-3));
            Assert.That(high.Value / low.Value, Is.EqualTo(2.0).Within(1e-6), "ratio kept");
            Assert.That(changes, Is.GreaterThan(0));
            Assert.That(low.IsEditing || high.IsEditing, Is.False, "gesture closed on both edges");
        });

        // At the top of the range the band slides along the limit, its ratio unchanged after the gesture.
        center.BeginEdit();
        center.PerformEdit(1.0);
        Assert.That(high.Value, Is.EqualTo(20000.0).Within(1e-6));
        center.PerformEdit(center.ToNormalized(1000.0));
        center.EndEdit();
        Assert.That(high.Value / low.Value, Is.EqualTo(2.0).Within(1e-6));
        Assert.That(center.TryParse("2.5 kHz", out double n), Is.True);
        Assert.That(center.ToPlain(n), Is.EqualTo(2500.0).Within(1e-9));
    }

    [Test]
    public void NodesSitAtTheirBandCentre()
    {
        var node = new ResponseNode { UsesBandEdges = true, LowFrequency = 10000.0, HighFrequency = 16000.0 };
        Assert.That(node.Frequency, Is.EqualTo(Math.Sqrt(1.6e8)).Within(1e-9), "geometric by default");
        node.BandCenter = BandCenters.Bilinear(() => 48000.0);
        Assert.That(node.Frequency, Is.EqualTo(13080.0).Within(10.0));
    }
}
