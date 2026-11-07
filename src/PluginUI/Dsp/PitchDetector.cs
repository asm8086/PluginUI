// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Dsp;

/// <summary>Result of a <see cref="PitchDetector"/>: the fundamental frequency and how periodic the signal is.</summary>
/// <param name="Frequency">Fundamental frequency in Hz (0 when none was found).</param>
/// <param name="Clarity">Periodicity, 0 (noise) to 1 (pure periodic signal).</param>
public readonly record struct PitchEstimate(double Frequency, double Clarity)
{
    /// <summary><c>true</c> when a pitch was found.</summary>
    public bool HasPitch => Frequency > 0.0;
}

/// <summary>
/// Monophonic pitch detection with the YIN algorithm (A. de Cheveigné and H. Kawahara, "YIN, a fundamental frequency
/// estimator for speech and music", JASA 111(4), 2002): difference function, cumulative mean normalization, absolute
/// threshold and parabolic interpolation of the period. For tuners and pitch displays; allocation-free after
/// construction.
/// </summary>
/// <example>
/// <code>
/// var yin = new PitchDetector(48000, minFrequency: 40, maxFrequency: 2000);
/// float[] block = new float[yin.WindowSize];
/// ring.CopyLatest(block);
/// var pitch = yin.Detect(block);
/// if (pitch.HasPitch) Console.WriteLine($"{pitch.Frequency:0.0} Hz");
/// </code>
/// </example>
public sealed class PitchDetector
{
    private readonly float[] _difference;

    /// <summary>Creates a detector.</summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="minFrequency">Lowest pitch looked for in Hz (sets the window: two periods of it).</param>
    /// <param name="maxFrequency">Highest pitch looked for in Hz.</param>
    public PitchDetector(double sampleRate, double minFrequency = 40.0, double maxFrequency = 2000.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0.0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minFrequency, 0.0);
        if (maxFrequency <= minFrequency) throw new ArgumentOutOfRangeException(nameof(maxFrequency), "The highest frequency must be above the lowest.");
        SampleRate = sampleRate;
        MinFrequency = minFrequency;
        MaxFrequency = Math.Min(maxFrequency, sampleRate / 4.0);
        MaxLag = (int)Math.Ceiling(sampleRate / minFrequency);
        MinLag = Math.Max(2, (int)Math.Floor(sampleRate / MaxFrequency));
        WindowSize = 2 * MaxLag;
        _difference = new float[MaxLag + 2];
    }

    /// <summary>Sample rate in Hz.</summary>
    public double SampleRate { get; }

    /// <summary>Lowest pitch looked for in Hz.</summary>
    public double MinFrequency { get; }

    /// <summary>Highest pitch looked for in Hz.</summary>
    public double MaxFrequency { get; }

    /// <summary>Samples <see cref="Detect"/> needs: two periods of the lowest pitch.</summary>
    public int WindowSize { get; }

    /// <summary>Longest period in samples.</summary>
    public int MaxLag { get; }

    /// <summary>Shortest period in samples.</summary>
    public int MinLag { get; }

    /// <summary>YIN threshold on the normalized difference (default 0.15; lower = stricter).</summary>
    public double Threshold { get; set; } = 0.15;

    /// <summary>Signals quieter than this RMS level are not analyzed (default −60 dBFS).</summary>
    public double SilenceRms { get; set; } = 0.001;

    /// <summary>Estimates the pitch of the last <see cref="WindowSize"/> samples of <paramref name="samples"/>.</summary>
    /// <param name="samples">At least <see cref="WindowSize"/> samples, oldest first.</param>
    /// <returns>The estimate (frequency 0 when the signal is silent or not periodic enough).</returns>
    public PitchEstimate Detect(ReadOnlySpan<float> samples)
    {
        if (samples.Length < WindowSize) throw new ArgumentException($"At least {WindowSize} samples are needed.", nameof(samples));
        var x = samples[^WindowSize..];
        int w = MaxLag; // integration window
        double energy = 0.0;
        for (int i = 0; i < WindowSize; i++) energy += x[i] * (double)x[i];
        if (Math.Sqrt(energy / WindowSize) < SilenceRms) return default;

        // Difference function d(τ) = Σ (x[j] − x[j + τ])², then the cumulative mean normalized difference d'(τ).
        var d = _difference;
        d[0] = 1f;
        double running = 0.0;
        for (int tau = 1; tau <= MaxLag; tau++)
        {
            double sum = 0.0;
            for (int j = 0; j < w; j++)
            {
                double diff = x[j] - x[j + tau];
                sum += diff * diff;
            }
            running += sum;
            d[tau] = running > 0.0 ? (float)(sum * tau / running) : 1f;
        }

        // First dip below the threshold, followed to its local minimum; otherwise the global minimum.
        int best = -1;
        for (int tau = MinLag; tau < MaxLag; tau++)
        {
            if (d[tau] < Threshold)
            {
                while (tau + 1 < MaxLag && d[tau + 1] < d[tau]) tau++;
                best = tau;
                break;
            }
        }
        if (best < 0)
        {
            float min = float.MaxValue;
            for (int tau = MinLag; tau < MaxLag; tau++)
            {
                if (d[tau] < min)
                {
                    min = d[tau];
                    best = tau;
                }
            }
            if (best < 0 || min > 0.5f) return default;
        }

        // Parabolic interpolation around the minimum for a sub-sample period.
        double period = best;
        if (best > 1 && best < MaxLag)
        {
            double a = d[best - 1], b = d[best], c = d[best + 1];
            double denominator = a - 2.0 * b + c;
            if (Math.Abs(denominator) > 1e-12) period = best + 0.5 * (a - c) / denominator;
        }
        double frequency = SampleRate / period;
        if (frequency < MinFrequency * 0.97 || frequency > MaxFrequency * 1.03) return default;
        return new PitchEstimate(frequency, Math.Clamp(1.0 - d[best], 0.0, 1.0));
    }
}
