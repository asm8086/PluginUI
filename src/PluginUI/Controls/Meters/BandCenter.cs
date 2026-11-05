// 2026/11/05 22:35:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// The centre of a band given by two edge frequencies, and the edges of a band of a given edge ratio around a centre:
/// where a <see cref="ResponseNode"/> with band edges sits, and what a centre knob (<see cref="BandCenterParameter"/>)
/// shows and sets.
/// </summary>
public interface IBandCenter
{
    /// <summary>Centre of the band.</summary>
    /// <param name="low">Lower edge in Hz.</param>
    /// <param name="high">Upper edge in Hz.</param>
    /// <returns>The centre in Hz.</returns>
    double Center(double low, double high);

    /// <summary>The edges of a band of edge ratio <paramref name="ratio"/> (high / low) whose centre is <paramref name="center"/>.</summary>
    /// <param name="center">Centre in Hz.</param>
    /// <param name="ratio">Edge ratio, ≥ 1.</param>
    /// <returns>The edges in Hz.</returns>
    (double Low, double High) Edges(double center, double ratio);
}

/// <summary>Band centres of common filter designs.</summary>
public static class BandCenters
{
    /// <summary>Geometric centre √(low·high): analog band filters, and the default of <see cref="ResponseNode"/>.</summary>
    public static IBandCenter Geometric { get; } = new GeometricBandCenter();

    /// <summary>
    /// Centre of the band filters designed by the bilinear transform with prewarped edges (Butterworth, Chebyshev,
    /// elliptic band-pass, band-stop and peaking; Constantinides transformation): the frequency of the peak or the
    /// notch, cos ω0 = cos((ω1 + ω2)/2) / cos((ω2 − ω1)/2). Equal to the geometric centre at low frequencies, above it
    /// by several percent near Nyquist (10 – 16 kHz at 48 kHz: 13.08 kHz instead of 12.65 kHz).
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz, read at each call (it can change).</param>
    /// <returns>The centre rule.</returns>
    public static IBandCenter Bilinear(Func<double> sampleRate) => new BilinearBandCenter(sampleRate ?? throw new ArgumentNullException(nameof(sampleRate)));

    private sealed class GeometricBandCenter : IBandCenter
    {
        public double Center(double low, double high) => Math.Sqrt(low * high);

        public (double Low, double High) Edges(double center, double ratio)
        {
            double half = Math.Sqrt(Math.Max(1.0, ratio));
            return (center / half, center * half);
        }
    }

    private sealed class BilinearBandCenter(Func<double> sampleRate) : IBandCenter
    {
        // Edges are kept strictly below Nyquist.
        private const double MaxFraction = 0.4999;

        public double Center(double low, double high)
        {
            double fs = sampleRate();
            if (!(fs > 0.0) || !double.IsFinite(fs)) return Math.Sqrt(low * high);
            double w1 = 2.0 * Math.PI * Math.Clamp(low, 0.0, MaxFraction * fs) / fs;
            double w2 = 2.0 * Math.PI * Math.Clamp(high, 0.0, MaxFraction * fs) / fs;
            if (w2 < w1) (w1, w2) = (w2, w1);
            double c = Math.Cos(0.5 * (w1 + w2)) / Math.Cos(0.5 * (w2 - w1));
            return Math.Acos(Math.Clamp(c, -1.0, 1.0)) * fs / (2.0 * Math.PI);
        }

        public (double Low, double High) Edges(double center, double ratio)
        {
            double fs = sampleRate();
            ratio = Math.Max(1.0, ratio);
            if (!(fs > 0.0) || !double.IsFinite(fs)) return Geometric.Edges(center, ratio);
            double maxF = MaxFraction * fs;
            // The centre lies between the edges and grows with the lower edge: bisection on log(low) in
            // [center/ratio, min(center, maxF/ratio)].
            double lo = Math.Log(Math.Max(1e-9, center / ratio));
            double hi = Math.Log(Math.Max(1e-9, Math.Min(center, maxF / ratio)));
            if (hi <= lo) { double l = Math.Exp(hi); return (l, l * ratio); }
            if (Center(Math.Exp(hi), Math.Exp(hi) * ratio) <= center) { double l = Math.Exp(hi); return (l, l * ratio); }
            for (int i = 0; i < 64 && hi - lo > 1e-13; i++)
            {
                double mid = 0.5 * (lo + hi);
                double l = Math.Exp(mid);
                if (Center(l, l * ratio) < center) lo = mid; else hi = mid;
            }
            double low = Math.Exp(0.5 * (lo + hi));
            return (low, low * ratio);
        }
    }
}
