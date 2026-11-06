// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;
using System.Text;

namespace PluginUI.Host.Audio;

/// <summary>How a response is measured.</summary>
public enum MeasurementMethod
{
    /// <summary>Impulse, then FFT: fast, exact for linear plug-ins (filters, EQs).</summary>
    Impulse,
    /// <summary>One sine per frequency, gain and phase of the fundamental: slower, meaningful for non-linear plug-ins.</summary>
    SteppedSine,
}

/// <summary>Decibel convention of the results.</summary>
public enum DecibelConvention
{
    /// <summary>dB = 20·log10(gain).</summary>
    TwentyLog10,
    /// <summary>dB = 6·log2(gain) (6 dB per doubling exactly).</summary>
    SixLog2,
}

/// <summary>Options of a response measurement.</summary>
public sealed class ResponseOptions
{
    /// <summary>Sample rate (default 48 kHz).</summary>
    public double SampleRate { get; set; } = 48000;

    /// <summary>Method (default <see cref="MeasurementMethod.Impulse"/>).</summary>
    public MeasurementMethod Method { get; set; } = MeasurementMethod.Impulse;

    /// <summary>Lowest frequency (default 20 Hz).</summary>
    public double MinFrequency { get; set; } = 20;

    /// <summary>Highest frequency (default 20 kHz, capped below Nyquist).</summary>
    public double MaxFrequency { get; set; } = 20000;

    /// <summary>Number of frequencies, logarithmically spaced (default 200).</summary>
    public int Points { get; set; } = 200;

    /// <summary>Level of the test signal in dBFS (default −20).</summary>
    public double LevelDb { get; set; } = -20;

    /// <summary>Impulse response length in samples (power of two, default 65536).</summary>
    public int ImpulseLength { get; set; } = 65536;

    /// <summary>Input channel that receives the test signal (default 0; the others stay silent).</summary>
    public int InputChannel { get; set; }

    /// <summary>Removes the latency the plug-in reports from the phase (default <c>true</c>).</summary>
    public bool CompensateLatency { get; set; } = true;

    /// <summary>Decibel convention of <see cref="ResponsePoint.MagnitudeDb"/>.</summary>
    public DecibelConvention Decibels { get; set; } = DecibelConvention.TwentyLog10;
}

/// <summary>Gain and phase of one output at one frequency.</summary>
/// <param name="Frequency">Frequency in Hz.</param>
/// <param name="MagnitudeDb">Gain in dB.</param>
/// <param name="PhaseDegrees">Phase in degrees (−180..180).</param>
public readonly record struct ResponsePoint(double Frequency, double MagnitudeDb, double PhaseDegrees);

/// <summary>Measured response: one curve per output channel, and the impulse response when measured by impulse.</summary>
/// <param name="Channels">Curves, one per output channel.</param>
/// <param name="ImpulseResponse">Impulse response (null for stepped sine).</param>
/// <param name="LatencySamples">Latency reported by the plug-in.</param>
public sealed record FrequencyResponse(IReadOnlyList<ResponsePoint[]> Channels, AudioBuffer? ImpulseResponse, int LatencySamples)
{
    /// <summary>CSV: frequency, then magnitude and phase of each channel. Invariant culture.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder("frequency_hz");
        for (int c = 0; c < Channels.Count; c++) sb.Append(CultureInfo.InvariantCulture, $",magnitude_db_{c + 1},phase_deg_{c + 1}");
        sb.AppendLine();
        if (Channels.Count == 0) return sb.ToString();
        for (int i = 0; i < Channels[0].Length; i++)
        {
            sb.Append(Channels[0][i].Frequency.ToString("0.###", CultureInfo.InvariantCulture));
            foreach (var channel in Channels)
                sb.Append(CultureInfo.InvariantCulture, $",{channel[i].MagnitudeDb:0.####},{channel[i].PhaseDegrees:0.###}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

/// <summary>Measures the frequency response of a plug-in.</summary>
public static class ResponseMeasurer
{
    /// <summary>Measures <paramref name="plugin"/> with its current parameters.</summary>
    public static FrequencyResponse Measure(PluginInstance plugin, ResponseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        options ??= new ResponseOptions();
        if (plugin.InputChannelCount == 0) throw new InvalidOperationException("The plug-in has no audio input to measure.");
        double nyquist = options.SampleRate / 2;
        double fMax = Math.Min(options.MaxFrequency, nyquist * 0.999);
        double fMin = Math.Clamp(options.MinFrequency, 1e-3, fMax);
        int points = Math.Max(2, options.Points);
        var frequencies = Enumerable.Range(0, points).Select(i => fMin * Math.Pow(fMax / fMin, i / (double)(points - 1))).ToArray();
        return options.Method == MeasurementMethod.Impulse
            ? MeasureImpulse(plugin, options, frequencies)
            : MeasureSines(plugin, options, frequencies);
    }

    private static double ToDb(double gain, DecibelConvention convention)
    {
        if (gain <= 0) return double.NegativeInfinity;
        return convention == DecibelConvention.SixLog2 ? 6.0 * Math.Log2(gain) : 20.0 * Math.Log10(gain);
    }

    private static double Amplitude(ResponseOptions options) => Math.Pow(10.0, options.LevelDb / 20.0);

    private static FrequencyResponse MeasureImpulse(PluginInstance plugin, ResponseOptions options, double[] frequencies)
    {
        int n = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(256, options.ImpulseLength));
        double amplitude = Amplitude(options);
        var input = AudioBuffer.Create(options.SampleRate, Math.Max(1, plugin.InputChannelCount), n);
        int channel = Math.Clamp(options.InputChannel, 0, input.ChannelCount - 1);
        input.Channels[channel][0] = (float)amplitude; // one channel only: the others stay silent

        var ir = OfflineRenderer.Render([plugin], input, options: new RenderOptions { TailSeconds = 0, CompensateLatency = options.CompensateLatency, BlockSize = 512 });
        int latency = plugin.LatencySamples;
        var curves = new List<ResponsePoint[]>();
        for (int c = 0; c < ir.ChannelCount; c++)
        {
            var spectrum = new Complex[n];
            for (int i = 0; i < n; i++) spectrum[i] = ir.Channels[c][i] / amplitude;
            Fft.Forward(spectrum);
            var curve = new ResponsePoint[frequencies.Length];
            for (int k = 0; k < frequencies.Length; k++)
            {
                // Linear interpolation of the complex spectrum between bins.
                double bin = frequencies[k] / options.SampleRate * n;
                int b0 = (int)Math.Floor(bin);
                double t = bin - b0;
                var h = spectrum[Math.Min(b0, n / 2)] * (1 - t) + spectrum[Math.Min(b0 + 1, n / 2)] * t;
                curve[k] = new ResponsePoint(frequencies[k], ToDb(h.Magnitude, options.Decibels), h.Phase * 180.0 / Math.PI);
            }
            curves.Add(curve);
        }
        return new FrequencyResponse(curves, ir, latency);
    }

    private static FrequencyResponse MeasureSines(PluginInstance plugin, ResponseOptions options, double[] frequencies)
    {
        double rate = options.SampleRate, amplitude = Amplitude(options);
        int channel = Math.Clamp(options.InputChannel, 0, plugin.InputChannelCount - 1);
        int outs = plugin.OutputChannelCount;
        var curves = Enumerable.Range(0, outs).Select(_ => new ResponsePoint[frequencies.Length]).ToArray();
        int latency = plugin.LatencySamples;
        for (int k = 0; k < frequencies.Length; k++)
        {
            double f = frequencies[k];
            // Settle for 50 ms (at least 4 periods), then analyse a whole number of periods (≥ 100 ms, ≥ 8 periods).
            int periods = (int)Math.Ceiling(Math.Max(8, 0.1 * f));
            int analysed = (int)Math.Round(periods * rate / f);
            int settle = (int)Math.Max(0.05 * rate, 4 * rate / f) + latency;
            int length = settle + analysed;
            var input = AudioBuffer.Create(rate, plugin.InputChannelCount, length);
            double w = 2 * Math.PI * f / rate;
            for (int i = 0; i < length; i++) input.Channels[channel][i] = (float)(amplitude * Math.Sin(w * i));
            var output = OfflineRenderer.Render([plugin], input, options: new RenderOptions { TailSeconds = 0, CompensateLatency = options.CompensateLatency });
            // Correlate the analysed part with the input sine: complex gain of the fundamental.
            for (int c = 0; c < outs; c++)
            {
                double re = 0, im = 0;
                for (int i = settle; i < length; i++)
                {
                    double y = output.Channels[c][i];
                    re += y * Math.Sin(w * i);
                    im += y * Math.Cos(w * i);
                }
                double scale = 2.0 / (analysed * amplitude);
                var h = new Complex(re * scale, im * scale);
                curves[c][k] = new ResponsePoint(f, ToDb(h.Magnitude, options.Decibels), h.Phase * 180.0 / Math.PI);
            }
        }
        return new FrequencyResponse(curves, null, latency);
    }
}

/// <summary>In-place radix-2 FFT.</summary>
public static class Fft
{
    /// <summary>Forward transform (length: power of two).</summary>
    public static void Forward(Complex[] data)
    {
        int n = data.Length;
        if (n == 0 || (n & (n - 1)) != 0) throw new ArgumentException("The length must be a power of two.", nameof(data));
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            var wl = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += length)
            {
                var w = Complex.One;
                for (int j = 0; j < length / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + length / 2] * w;
                    data[i + j] = u + v;
                    data[i + j + length / 2] = u - v;
                    w *= wl;
                }
            }
        }
    }
}
