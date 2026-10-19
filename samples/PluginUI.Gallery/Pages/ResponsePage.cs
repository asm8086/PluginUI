// 2026/10/19 22:34:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Numerics;
using PluginUI.Dsp;

namespace PluginUI.Gallery.Pages;

/// <summary>
/// FrequencyResponseView: three RBJ biquads supplied as curves (the element designs nothing itself), their cascade,
/// phase or group delay on the right axis, the demo signal's spectrum and the same spectrum shaped by the cascade.
/// </summary>
internal static class ResponsePage
{
    private const double Fs = 48000.0;

    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();
        c.Caption(L.T("FREQUENCY RESPONSE (DRAG THE POINTS, WHEEL = WIDTH, SQUARE HANDLES = BAND EDGES)",
                      "RÉPONSE EN FRÉQUENCE (GLISSER LES POINTS, MOLETTE = LARGEUR, POIGNÉES CARRÉES = BORDS DE BANDE)"), 20, 12, 850);

        // Widths stepped by the wheel.
        var lowCutQ = new LocalParameter("Low cut Q", scale: ParameterScale.Logarithmic(0.3, 6.0), defaultValue: 0.7071);
        var bellQ = new LocalParameter("Bell Q", scale: ParameterScale.Logarithmic(0.2, 12.0), defaultValue: 1.4);

        var lowCut = new ResponseNode { Text = "1", Color = Color.FromRgb(0xFF7043), Frequency = 60.0, WheelParameter = lowCutQ };
        var bell = new ResponseNode { Text = "2", Color = Color.FromRgb(0x26C6DA), Frequency = 900.0, Value = -6.0, IsValueEditable = true, WheelParameter = bellQ };
        // Third band defined by its two edges: a bell whose Q follows the edge ratio.
        var band = new ResponseNode { Text = "3", Color = Color.FromRgb(0x9CCC65), UsesBandEdges = true, LowFrequency = 3000.0, HighFrequency = 9000.0, Value = 5.0, IsValueEditable = true };

        BiquadDesign.Coefficients LowCut() => BiquadDesign.Design(EqBandType.LowCut, lowCut.Frequency, 0.0, lowCutQ.PlainValue(), Fs);
        BiquadDesign.Coefficients Bell() => BiquadDesign.Design(EqBandType.Bell, bell.Frequency, bell.Value, bellQ.PlainValue(), Fs);
        BiquadDesign.Coefficients Band()
        {
            double r = band.HighFrequency / band.LowFrequency;
            return BiquadDesign.Design(EqBandType.Bell, band.Frequency, band.Value, Math.Sqrt(r) / (r - 1.0), Fs);
        }

        double Total(double hz) => Db(Response(LowCut(), hz) * Response(Bell(), hz) * Response(Band(), hz));

        var view = c.At(new FrequencyResponseView
        {
            SampleRate = Fs,
            LeftMin = -18.0, LeftMax = 18.0, LeftStep = 6.0,
            SpectrumSource = audio.Ring,
            SpectrumFilter = Total,
            RightAxisColor = Color.FromRgb(0xFFB300),
            ContentVersion = () => lowCutQ.NormalizedValue + 3.0 * bellQ.NormalizedValue,
        }, 20, 36, 850, 410);

        view.Curves.Add(new ResponseCurve("Low cut", lowCut.Color, f => Db(Response(LowCut(), f))) { Thickness = 1.2f, FillOpacity = 0.06f });
        view.Curves.Add(new ResponseCurve("Bell", bell.Color, f => Db(Response(Bell(), f))) { Thickness = 1.2f, FillOpacity = 0.06f });
        view.Curves.Add(new ResponseCurve("Band", band.Color, f => Db(Response(Band(), f))) { Thickness = 1.2f, FillOpacity = 0.06f });
        view.Curves.Add(new ResponseCurve("Cascade", Color.FromRgb(0xF5F5F5), Total) { Thickness = 2.2f });
        var right = new ResponseCurve("Phase", Color.FromRgb(0xFFB300), f => Phase(f, LowCut(), Bell(), Band()), ResponseAxis.Right)
        {
            Thickness = 1.3f,
            Stroke = StrokeStyle.Dashed,
        };
        view.Curves.Add(right);
        view.Nodes.Add(lowCut);
        view.Nodes.Add(bell);
        view.Nodes.Add(band);
        view.RightAxisTicks = RightAxisTicks.PiMultiples;
        view.RightUnit = "rad";

        // Display options.
        c.Caption(L.T("FREQUENCY AXIS", "AXE DES FRÉQUENCES"), 20, 460, 150);
        var axis = c.At(new DropDown(L.T("Logarithmic", "Logarithmique"), L.T("Linear", "Linéaire")), 20, 482, 150, 26);
        axis.ValueChanged += (_, _) => view.FrequencyScale = axis.SelectedIndex == 0 ? FrequencyScale.Logarithmic : FrequencyScale.Linear;

        c.Caption(L.T("RIGHT AXIS", "AXE DROIT"), 190, 460, 150);
        var rightAxis = c.At(new DropDown(L.T("None", "Aucun"), L.T("Phase", "Phase"), L.T("Group delay", "Retard de groupe")) { SelectedIndex = 1 }, 190, 482, 170, 26);
        rightAxis.ValueChanged += (_, _) =>
        {
            int i = rightAxis.SelectedIndex;
            right.IsVisible = i != 0;
            right.Evaluate = i == 2 ? f => GroupDelay(f, LowCut(), Bell(), Band()) : f => Phase(f, LowCut(), Bell(), Band());
            view.RightAxisTicks = i == 1 ? RightAxisTicks.PiMultiples : RightAxisTicks.Decimal;
            view.RightUnit = i == 1 ? "rad" : i == 2 ? L.T("samples", "échantillons") : "";
            view.InvalidateVisual();
        };

        c.Caption(L.T("SPECTRA", "SPECTRES"), 380, 460, 200);
        var input = c.At(new ToggleButton(L.T("Input", "Entrée")) { OnColor = view.InputSpectrumColor }, 380, 482, 90, 26);
        input.IsOn = true;
        input.ValueChanged += (_, _) => view.ShowInputSpectrum = input.IsOn;
        var output = c.At(new ToggleButton(L.T("Filtered", "Filtré")) { OnColor = view.FilteredSpectrumColor, ToolTip = L.T("Input spectrum shaped by the cascade, no second FFT", "Spectre d'entrée façonné par la cascade, sans seconde FFT") }, 476, 482, 90, 26);
        output.IsOn = true;
        output.ValueChanged += (_, _) => view.ShowFilteredSpectrum = output.IsOn;
        return c;
    }

    private static Complex Response(in BiquadDesign.Coefficients k, double hz)
    {
        Complex x = Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * hz / Fs), x2 = x * x;
        return (k.B0 + k.B1 * x + k.B2 * x2) / (1.0 + k.A1 * x + k.A2 * x2);
    }

    private static double Db(Complex h) => 20.0 * Math.Log10(Math.Max(h.Magnitude, 1e-12));

    // Unwrapped phase of stable minimum-phase biquads: the sum of the principal arguments of 1 − r·e^(−iω) for each
    // zero and pole r inside the unit circle is continuous (each factor has a positive real part).
    private static double Phase(double hz, params BiquadDesign.Coefficients[] sections)
    {
        double w = 2.0 * Math.PI * hz / Fs, phi = 0.0;
        Complex e = Complex.FromPolarCoordinates(1.0, -w);
        foreach (var k in sections)
        {
            foreach (var r in Roots(1.0, k.B1 / k.B0, k.B2 / k.B0)) phi += FactorPhase(r, e, w);
            foreach (var r in Roots(1.0, k.A1, k.A2)) phi -= FactorPhase(r, e, w);
        }
        return phi;
    }

    private static double GroupDelay(double hz, params BiquadDesign.Coefficients[] sections)
    {
        double w = 2.0 * Math.PI * hz / Fs, tau = 0.0;
        Complex e = Complex.FromPolarCoordinates(1.0, -w);
        foreach (var k in sections)
        {
            foreach (var r in Roots(1.0, k.B1 / k.B0, k.B2 / k.B0)) tau -= (r * e / (1.0 - r * e)).Real;
            foreach (var r in Roots(1.0, k.A1, k.A2)) tau += (r * e / (1.0 - r * e)).Real;
        }
        return tau;
    }

    private static double FactorPhase(Complex r, Complex e, double w) => r.Magnitude <= 1.0
        ? (1.0 - r * e).Phase
        : (-r).Phase - w + (1.0 - Complex.Conjugate(e) / r).Phase;

    // Roots in z of z² + b·z + c.
    private static Complex[] Roots(double a, double b, double c)
    {
        Complex sq = Complex.Sqrt(b * b - 4.0 * a * c);
        return [(-b + sq) / (2.0 * a), (-b - sq) / (2.0 * a)];
    }
}
