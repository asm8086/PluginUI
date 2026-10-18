// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Dsp;

/// <summary>Filter shapes of an equalizer band (<see cref="BiquadDesign"/>, <see cref="ParametricEqEditor"/>).</summary>
public enum EqBandType
{
    /// <summary>Peaking (bell) filter: boost or cut around the frequency, width set by Q.</summary>
    Bell,
    /// <summary>Low shelf: boost or cut below the frequency.</summary>
    LowShelf,
    /// <summary>High shelf: boost or cut above the frequency.</summary>
    HighShelf,
    /// <summary>Low cut (high-pass, 12 dB/octave): removes frequencies below the cutoff; gain ignored.</summary>
    LowCut,
    /// <summary>High cut (low-pass, 12 dB/octave): removes frequencies above the cutoff; gain ignored.</summary>
    HighCut,
    /// <summary>Notch: narrow rejection at the frequency; gain ignored.</summary>
    Notch,
    /// <summary>Band pass (constant 0 dB peak gain); gain ignored.</summary>
    BandPass,
}

/// <summary>
/// Second-order filter coefficients from the RBJ "Audio EQ Cookbook" and their exact magnitude response,
/// used to draw equalizer curves that match a biquad implementation sample for sample.
/// </summary>
/// <remarks>
/// Coefficients are normalized (a0 = 1). <see cref="MagnitudeDb"/> evaluates |H(e^jω)| of the digital filter, so the
/// curve shows the real response near Nyquist (cramping of bells and shelves) instead of the analog prototype.
/// </remarks>
public static class BiquadDesign
{
    /// <summary>Normalized biquad coefficients: y = b0·x + b1·x₁ + b2·x₂ − a1·y₁ − a2·y₂.</summary>
    /// <param name="B0">Feed-forward coefficient b0.</param>
    /// <param name="B1">Feed-forward coefficient b1.</param>
    /// <param name="B2">Feed-forward coefficient b2.</param>
    /// <param name="A1">Feedback coefficient a1.</param>
    /// <param name="A2">Feedback coefficient a2.</param>
    public readonly record struct Coefficients(double B0, double B1, double B2, double A1, double A2)
    {
        /// <summary>The identity filter (output = input).</summary>
        public static Coefficients Identity => new(1.0, 0.0, 0.0, 0.0, 0.0);
    }

    /// <summary>Designs one band.</summary>
    /// <param name="type">Filter shape.</param>
    /// <param name="frequency">Center / corner frequency in Hz (clamped to 1 Hz … 0.499 × sample rate).</param>
    /// <param name="gainDb">Gain in dB (bells and shelves only).</param>
    /// <param name="q">Quality factor (bandwidth of bells, notches and band passes; resonance of cuts; slope of shelves, 0.7071 = maximally steep without overshoot).</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>The normalized coefficients.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    public static Coefficients Design(EqBandType type, double frequency, double gainDb, double q, double sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        double f = Math.Clamp(frequency, 1.0, 0.499 * sampleRate);
        q = Math.Max(q, 1e-3);
        double w0 = 2.0 * Math.PI * f / sampleRate;
        double cos = Math.Cos(w0), sin = Math.Sin(w0);
        double alpha = sin / (2.0 * q);
        double a = Math.Pow(10.0, gainDb / 40.0);
        double b0, b1, b2, a0, a1, a2;
        switch (type)
        {
            case EqBandType.LowShelf:
            {
                double s = 2.0 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * cos + s);
                b1 = 2 * a * ((a - 1) - (a + 1) * cos);
                b2 = a * ((a + 1) - (a - 1) * cos - s);
                a0 = (a + 1) + (a - 1) * cos + s;
                a1 = -2 * ((a - 1) + (a + 1) * cos);
                a2 = (a + 1) + (a - 1) * cos - s;
                break;
            }
            case EqBandType.HighShelf:
            {
                double s = 2.0 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * cos + s);
                b1 = -2 * a * ((a - 1) + (a + 1) * cos);
                b2 = a * ((a + 1) + (a - 1) * cos - s);
                a0 = (a + 1) - (a - 1) * cos + s;
                a1 = 2 * ((a - 1) - (a + 1) * cos);
                a2 = (a + 1) - (a - 1) * cos - s;
                break;
            }
            case EqBandType.LowCut:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqBandType.HighCut:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqBandType.Notch:
                b0 = 1; b1 = -2 * cos; b2 = 1;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqBandType.BandPass:
                b0 = alpha; b1 = 0; b2 = -alpha;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            default: // Bell
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
        }
        return new Coefficients(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>Magnitude response in dB of a biquad at a frequency.</summary>
    /// <param name="c">Normalized coefficients.</param>
    /// <param name="frequency">Frequency in Hz.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>20·log10 |H(e^jω)|, floored at −300 dB.</returns>
    public static double MagnitudeDb(in Coefficients c, double frequency, double sampleRate)
    {
        // |H|² = |b0 + b1 z⁻¹ + b2 z⁻²|² / |1 + a1 z⁻¹ + a2 z⁻²|², written with φ = sin²(ω/2) for numerical accuracy
        // at low frequencies (Robert Bristow-Johnson's formulation).
        double w = 2.0 * Math.PI * frequency / sampleRate;
        double phi = Math.Sin(w / 2.0);
        phi *= phi;
        double b0 = c.B0, b1 = c.B1, b2 = c.B2, a1 = c.A1, a2 = c.A2;
        double num = Square(b0 + b1 + b2) - 4.0 * (b0 * b1 + 4.0 * b0 * b2 + b1 * b2) * phi + 16.0 * b0 * b2 * phi * phi;
        double den = Square(1.0 + a1 + a2) - 4.0 * (a1 + 4.0 * a2 + a1 * a2) * phi + 16.0 * a2 * phi * phi;
        double ratio = Math.Max(num, 0.0) / Math.Max(den, 1e-300);
        return ratio <= 1e-30 ? -300.0 : 10.0 * Math.Log10(ratio);
    }

    private static double Square(double x) => x * x;
}
