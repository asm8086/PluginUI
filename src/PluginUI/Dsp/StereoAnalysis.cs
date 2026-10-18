// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.InteropServices;

namespace PluginUI.Dsp;

/// <summary>Stereo image measurements used by <see cref="CorrelationMeter"/> and <see cref="Goniometer"/>.</summary>
public static class StereoAnalysis
{
    /// <summary>
    /// Phase correlation of two channels over a block: Σ(L·R) / √(ΣL² · ΣR²), from +1 (mono, in phase) through 0
    /// (unrelated / wide) to −1 (out of phase, cancels in mono). Returns 0 for silence. Vectorized.
    /// </summary>
    /// <param name="left">Left samples.</param>
    /// <param name="right">Right samples (same length).</param>
    /// <returns>The correlation coefficient in [−1, 1].</returns>
    /// <exception cref="ArgumentException">The spans have different lengths.</exception>
    public static double Correlation(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length) throw new ArgumentException("The channels must have the same length.");
        var (lr, ll, rr) = Sums(left, right);
        double denominator = Math.Sqrt(ll * rr);
        return denominator < 1e-12 ? 0.0 : Math.Clamp(lr / denominator, -1.0, 1.0);
    }

    /// <summary>
    /// Stereo balance of a block from the channel energies: −1 = left only, 0 = centered, +1 = right only.
    /// </summary>
    /// <param name="left">Left samples.</param>
    /// <param name="right">Right samples (same length).</param>
    /// <returns>The balance in [−1, 1] (0 for silence).</returns>
    /// <exception cref="ArgumentException">The spans have different lengths.</exception>
    public static double Balance(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length) throw new ArgumentException("The channels must have the same length.");
        var (_, ll, rr) = Sums(left, right);
        double total = ll + rr;
        return total < 1e-12 ? 0.0 : (rr - ll) / total;
    }

    private static (double LR, double LL, double RR) Sums(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        int n = left.Length, i = 0;
        double lr = 0.0, ll = 0.0, rr = 0.0;
        if (Vector.IsHardwareAccelerated && n >= Vector<float>.Count)
        {
            ref float l0 = ref MemoryMarshal.GetReference(left);
            ref float r0 = ref MemoryMarshal.GetReference(right);
            var vlr = Vector<float>.Zero;
            var vll = Vector<float>.Zero;
            var vrr = Vector<float>.Zero;
            // Float accumulation in blocks keeps the speed of SIMD; partial sums are folded into doubles regularly.
            int blockEnd;
            while (i <= n - Vector<float>.Count)
            {
                blockEnd = Math.Min(n - Vector<float>.Count, i + 4096);
                for (; i <= blockEnd; i += Vector<float>.Count)
                {
                    var l = Vector.LoadUnsafe(ref l0, (nuint)i);
                    var r = Vector.LoadUnsafe(ref r0, (nuint)i);
                    vlr += l * r;
                    vll += l * l;
                    vrr += r * r;
                }
                lr += Vector.Sum(vlr); ll += Vector.Sum(vll); rr += Vector.Sum(vrr);
                vlr = vll = vrr = Vector<float>.Zero;
            }
        }
        for (; i < n; i++)
        {
            double l = left[i], r = right[i];
            lr += l * r;
            ll += l * l;
            rr += r * r;
        }
        return (lr, ll, rr);
    }
}

/// <summary>Static characteristic of a feed-forward compressor (drawn by <see cref="CompressorCurve"/>).</summary>
public static class CompressorMath
{
    /// <summary>
    /// Output level of a compressor with a quadratic soft knee (Giannoulis, Massberg &amp; Reiss, JAES 2012):
    /// below <c>threshold − knee/2</c> the signal is unchanged, above <c>threshold + knee/2</c> it rises by
    /// <c>1/ratio</c> dB per dB, and the knee joins both lines smoothly.
    /// </summary>
    /// <param name="inputDb">Input level in dB.</param>
    /// <param name="thresholdDb">Threshold in dB.</param>
    /// <param name="ratio">Ratio (1 = no compression, ∞ = limiter); values below 1 are treated as 1.</param>
    /// <param name="kneeDb">Knee width in dB (0 = hard knee).</param>
    /// <returns>The output level in dB (before make-up gain).</returns>
    public static double OutputDb(double inputDb, double thresholdDb, double ratio, double kneeDb)
    {
        ratio = Math.Max(1.0, ratio);
        double slope = 1.0 / ratio - 1.0;
        double over = inputDb - thresholdDb;
        if (kneeDb > 0.0 && Math.Abs(over) <= kneeDb / 2.0)
        {
            double x = over + kneeDb / 2.0;
            return inputDb + slope * x * x / (2.0 * kneeDb);
        }
        return over > 0.0 ? thresholdDb + over / ratio : inputDb;
    }

    /// <summary>Gain reduction in dB (≥ 0) for an input level: <c>input − output</c>.</summary>
    /// <param name="inputDb">Input level in dB.</param>
    /// <param name="thresholdDb">Threshold in dB.</param>
    /// <param name="ratio">Ratio.</param>
    /// <param name="kneeDb">Knee width in dB.</param>
    /// <returns>The gain reduction in dB.</returns>
    public static double GainReductionDb(double inputDb, double thresholdDb, double ratio, double kneeDb)
        => inputDb - OutputDb(inputDb, thresholdDb, ratio, kneeDb);
}
