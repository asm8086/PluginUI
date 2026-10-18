// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Dsp;

/// <summary>
/// Loudness measurement according to ITU-R BS.1770-4 / EBU R128: momentary (400 ms), short-term (3 s) and
/// integrated (gated) loudness in LUFS, loudness range (LRA, EBU Tech 3342) and sample peak.
/// Feeds <see cref="LoudnessMeter"/>.
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> Call <see cref="Process(ReadOnlySpan{float}, ReadOnlySpan{float})"/> on the audio thread (no allocation, no lock, wait-free); read
/// the results from any thread. <see cref="Configure"/> and <see cref="Reset"/> must not run concurrently with
/// <see cref="Process(ReadOnlySpan{float}, ReadOnlySpan{float})"/> (call them from <c>OnActivate</c> / <c>SetupProcessing</c> or while processing is stopped).</para>
/// <para><b>Algorithm.</b> Each channel goes through the K-weighting filter (high-shelf pre-filter + RLB high-pass,
/// coefficients computed for the actual sample rate), mean squares are accumulated in 100 ms sub-blocks, and the
/// momentary / short-term values are sliding sums of 4 / 30 sub-blocks. Integrated loudness and LRA use histograms
/// with 0.1 LU resolution (absolute gate −70 LUFS, relative gates −10 LU and −20 LU), so memory is constant whatever
/// the duration. Channel weights: 1.0 for front channels (pass <c>1.41</c> for surround channels with
/// <see cref="Configure"/>).</para>
/// <para>Reference values: a 997 Hz sine at −23 dBFS on both channels of a stereo signal reads −23.0 LUFS.</para>
/// </remarks>
/// <example>
/// <code>
/// // Processor (audio thread):
/// readonly LoudnessAnalyzer _loudness = new();
/// protected override void OnActivate(bool active) { if (active) _loudness.Configure(ProcessSetupData.SampleRate, 2); }
/// protected override void ProcessMain(in AudioProcessData data)
/// {
///     ... // after processing:
///     _loudness.Process(outLeft, outRight);
/// }
///
/// // Editor (UI thread): new LoudnessMeter { Analyzer = processorLoudness }
/// </code>
/// </example>
public sealed class LoudnessAnalyzer
{
    private const int SubBlocksMomentary = 4;   // 400 ms
    private const int SubBlocksShortTerm = 30;  // 3 s
    private const double HistogramMinLufs = -70.0;
    private const double HistogramStep = 0.1;
    private const int HistogramBins = 1000;     // -70 .. +30 LUFS

    private int _channels = 2;
    private double[] _weights = [1.0, 1.0];
    private Biquad[] _pre = [];
    private Biquad[] _rlb = [];
    private int _subBlockLength;
    private int _subBlockFill;
    private double _subBlockSum;                // weighted sum of squares in the current 100 ms
    private readonly double[] _ring = new double[SubBlocksShortTerm];
    private int _ringIndex;
    private int _ringCount;
    private readonly int[] _momentaryHistogram = new int[HistogramBins];
    private readonly int[] _shortTermHistogram = new int[HistogramBins];
    private int _shortTermHop;

    // Published values (written by the audio thread, read by the UI).
    private double _momentary = double.NegativeInfinity;
    private double _shortTerm = double.NegativeInfinity;
    private int _peakBits;
    private int _resetRequested;

    /// <summary>Creates an analyzer for stereo at 48 kHz (call <see cref="Configure"/> with the real settings).</summary>
    public LoudnessAnalyzer() => Configure(48000.0, 2);

    /// <summary>Sample rate in Hz.</summary>
    public double SampleRate { get; private set; }

    /// <summary>Number of channels.</summary>
    public int ChannelCount => _channels;

    /// <summary>
    /// Prepares the filters for a sample rate and a channel layout, and resets all measurements.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz (8 kHz to 384 kHz).</param>
    /// <param name="channelCount">Number of channels (1 to 8).</param>
    /// <param name="channelWeights">Weight of each channel (BS.1770: 1.0 for L, R, C; 1.41 for Ls, Rs; 0 to ignore LFE), or <c>null</c> for 1.0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> or <paramref name="channelCount"/> is out of range.</exception>
    public void Configure(double sampleRate, int channelCount, ReadOnlySpan<double> channelWeights = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleRate, 384000.0);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channelCount, 8);
        SampleRate = sampleRate;
        _channels = channelCount;
        _weights = new double[channelCount];
        for (int c = 0; c < channelCount; c++) _weights[c] = c < channelWeights.Length ? channelWeights[c] : 1.0;
        _pre = new Biquad[channelCount];
        _rlb = new Biquad[channelCount];
        var (preB, preA, rlbB, rlbA) = KWeighting(sampleRate);
        for (int c = 0; c < channelCount; c++)
        {
            _pre[c] = new Biquad(preB, preA);
            _rlb[c] = new Biquad(rlbB, rlbA);
        }
        _subBlockLength = Math.Max(1, (int)Math.Round(sampleRate * 0.1));
        Reset();
    }

    /// <summary>Clears all measurements (integrated loudness, LRA, peak) and the filter states.</summary>
    public void Reset()
    {
        for (int c = 0; c < _channels; c++)
        {
            _pre[c].Reset();
            _rlb[c].Reset();
        }
        _subBlockFill = 0;
        _subBlockSum = 0.0;
        Array.Clear(_ring);
        _ringIndex = 0;
        _ringCount = 0;
        _shortTermHop = 0;
        Array.Clear(_momentaryHistogram);
        Array.Clear(_shortTermHistogram);
        Volatile.Write(ref _momentary, double.NegativeInfinity);
        Volatile.Write(ref _shortTerm, double.NegativeInfinity);
        Volatile.Write(ref _peakBits, 0);
    }

    /// <summary>
    /// Thread-safe reset request (e.g. from a "reset" button of the UI): the measurements are cleared by the audio
    /// thread at the beginning of the next <see cref="Process(ReadOnlySpan{float}, ReadOnlySpan{float})"/> call.
    /// </summary>
    public void RequestReset() => Volatile.Write(ref _resetRequested, 1);

    /// <summary>Audio thread: analyzes a stereo block (or the first two channels).</summary>
    /// <param name="left">Left channel samples.</param>
    /// <param name="right">Right channel samples (same length as <paramref name="left"/>).</param>
    /// <exception cref="ArgumentException">The spans have different lengths.</exception>
    public void Process(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length) throw new ArgumentException("The channels must have the same length.");
        if (_channels == 1)
        {
            ProcessCore(left, default, 1);
            return;
        }
        ProcessCore(left, right, 2);
    }

    /// <summary>Audio thread: analyzes a mono block (or the first channel of the configuration).</summary>
    /// <param name="mono">Samples.</param>
    public void Process(ReadOnlySpan<float> mono) => ProcessCore(mono, default, 1);

    private void ProcessCore(ReadOnlySpan<float> a, ReadOnlySpan<float> b, int count)
    {
        if (Interlocked.Exchange(ref _resetRequested, 0) != 0) Reset();
        float peak = BitConverter.Int32BitsToSingle(Volatile.Read(ref _peakBits));
        double wa = _weights[0], wb = count > 1 && _channels > 1 ? _weights[1] : 0.0;
        ref var preA = ref _pre[0];
        ref var rlbA = ref _rlb[0];
        for (int i = 0; i < a.Length; i++)
        {
            double xa = a[i];
            double ya = rlbA.Process(preA.Process(xa));
            double sum = wa * ya * ya;
            float pa = MathF.Abs(a[i]);
            if (pa > peak) peak = pa;
            if (wb != 0.0)
            {
                double xb = b[i];
                double yb = _rlb[1].Process(_pre[1].Process(xb));
                sum += wb * yb * yb;
                float pb = MathF.Abs(b[i]);
                if (pb > peak) peak = pb;
            }
            _subBlockSum += sum;
            if (++_subBlockFill == _subBlockLength) CompleteSubBlock();
        }
        for (int c = 0; c < _channels; c++)
        {
            _pre[c].FlushDenormals();
            _rlb[c].FlushDenormals();
        }
        Volatile.Write(ref _peakBits, BitConverter.SingleToInt32Bits(peak));
    }

    private void CompleteSubBlock()
    {
        _ring[_ringIndex] = _subBlockSum / _subBlockLength;
        _ringIndex = (_ringIndex + 1) % SubBlocksShortTerm;
        if (_ringCount < SubBlocksShortTerm) _ringCount++;
        _subBlockFill = 0;
        _subBlockSum = 0.0;

        if (_ringCount >= SubBlocksMomentary)
        {
            double m = ToLufs(Mean(SubBlocksMomentary));
            Volatile.Write(ref _momentary, m);
            AddToHistogram(_momentaryHistogram, m);
        }
        if (_ringCount >= SubBlocksShortTerm)
        {
            double s = ToLufs(Mean(SubBlocksShortTerm));
            Volatile.Write(ref _shortTerm, s);
            // LRA uses short-term values every 1 s (EBU Tech 3342 recommends ≥ 10 Hz; 1 s keeps the histogram light
            // and gives the same statistics for program material).
            if (++_shortTermHop >= 10)
            {
                _shortTermHop = 0;
                AddToHistogram(_shortTermHistogram, s);
            }
        }
    }

    private double Mean(int subBlocks)
    {
        double sum = 0.0;
        for (int k = 1; k <= subBlocks; k++) sum += _ring[(_ringIndex - k + SubBlocksShortTerm) % SubBlocksShortTerm];
        return sum / subBlocks;
    }

    private static double ToLufs(double weightedMeanSquare) => weightedMeanSquare <= 1e-20 ? double.NegativeInfinity : -0.691 + 10.0 * Math.Log10(weightedMeanSquare);

    private static void AddToHistogram(int[] histogram, double lufs)
    {
        if (!(lufs >= HistogramMinLufs)) return; // absolute gate (also rejects -∞ and NaN)
        int bin = Math.Min(HistogramBins - 1, (int)((lufs - HistogramMinLufs) / HistogramStep));
        Interlocked.Increment(ref histogram[bin]);
    }

    private static double BinCenter(int bin) => HistogramMinLufs + (bin + 0.5) * HistogramStep;

    /// <summary>Momentary loudness (400 ms window) in LUFS; −∞ before 400 ms of signal or during digital silence.</summary>
    public double MomentaryLufs => Volatile.Read(ref _momentary);

    /// <summary>Short-term loudness (3 s window) in LUFS; −∞ before 3 s of signal.</summary>
    public double ShortTermLufs => Volatile.Read(ref _shortTerm);

    /// <summary>Largest absolute sample value since the last <see cref="Reset"/> (linear, 1.0 = 0 dBFS).</summary>
    public float SamplePeak => BitConverter.Int32BitsToSingle(Volatile.Read(ref _peakBits));

    /// <summary>
    /// Integrated (program) loudness in LUFS since the last <see cref="Reset"/>, with the absolute (−70 LUFS) and
    /// relative (−10 LU) gates of BS.1770-4; −∞ while no block passes the gates. Computed on demand (O(1000)).
    /// </summary>
    public double IntegratedLufs
    {
        get
        {
            // First pass: mean energy of blocks above the absolute gate.
            double energy = 0.0;
            long count = 0;
            for (int i = 0; i < HistogramBins; i++)
            {
                int n = Volatile.Read(ref _momentaryHistogram[i]);
                if (n == 0) continue;
                energy += n * Math.Pow(10.0, (BinCenter(i) + 0.691) / 10.0);
                count += n;
            }
            if (count == 0) return double.NegativeInfinity;
            double relativeGate = -0.691 + 10.0 * Math.Log10(energy / count) - 10.0;
            // Second pass: blocks above the relative gate.
            energy = 0.0;
            count = 0;
            for (int i = 0; i < HistogramBins; i++)
            {
                if (BinCenter(i) < relativeGate) continue;
                int n = Volatile.Read(ref _momentaryHistogram[i]);
                if (n == 0) continue;
                energy += n * Math.Pow(10.0, (BinCenter(i) + 0.691) / 10.0);
                count += n;
            }
            return count == 0 ? double.NegativeInfinity : -0.691 + 10.0 * Math.Log10(energy / count);
        }
    }

    /// <summary>
    /// Loudness range (LRA) in LU (EBU Tech 3342): spread between the 10th and 95th percentiles of the short-term
    /// loudness, after absolute (−70 LUFS) and relative (−20 LU) gating; 0 while too little signal was measured.
    /// </summary>
    public double LoudnessRange
    {
        get
        {
            double energy = 0.0;
            long count = 0;
            for (int i = 0; i < HistogramBins; i++)
            {
                int n = Volatile.Read(ref _shortTermHistogram[i]);
                if (n == 0) continue;
                energy += n * Math.Pow(10.0, (BinCenter(i) + 0.691) / 10.0);
                count += n;
            }
            if (count < 2) return 0.0;
            double gate = -0.691 + 10.0 * Math.Log10(energy / count) - 20.0;
            long kept = 0;
            for (int i = 0; i < HistogramBins; i++) if (BinCenter(i) >= gate) kept += Volatile.Read(ref _shortTermHistogram[i]);
            if (kept < 2) return 0.0;
            double Percentile(double p)
            {
                long target = (long)Math.Ceiling(p * kept);
                long seen = 0;
                for (int i = 0; i < HistogramBins; i++)
                {
                    if (BinCenter(i) < gate) continue;
                    seen += Volatile.Read(ref _shortTermHistogram[i]);
                    if (seen >= target) return BinCenter(i);
                }
                return BinCenter(HistogramBins - 1);
            }
            return Math.Max(0.0, Percentile(0.95) - Percentile(0.10));
        }
    }

    /// <summary>
    /// K-weighting coefficients (BS.1770 pre-filter and RLB high-pass) for any sample rate, from the analog
    /// prototypes through the bilinear transform; at 48 kHz they match the coefficients tabulated in BS.1770.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>Numerator and denominator (a0 = 1) of both sections.</returns>
    public static (double[] PreB, double[] PreA, double[] RlbB, double[] RlbA) KWeighting(double sampleRate)
    {
        double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
        double k = Math.Tan(Math.PI * f0 / sampleRate);
        double vh = Math.Pow(10.0, g / 20.0);
        double vb = Math.Pow(vh, 0.4996667741545416);
        double a0 = 1.0 + k / q + k * k;
        double[] preB = [(vh + vb * k / q + k * k) / a0, 2.0 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0];
        double[] preA = [1.0, 2.0 * (k * k - 1.0) / a0, (1.0 - k / q + k * k) / a0];

        f0 = 38.13547087602444;
        q = 0.5003270373238773;
        k = Math.Tan(Math.PI * f0 / sampleRate);
        double d = 1.0 + k / q + k * k;
        double[] rlbB = [1.0, -2.0, 1.0];
        double[] rlbA = [1.0, 2.0 * (k * k - 1.0) / d, (1.0 - k / q + k * k) / d];
        return (preB, preA, rlbB, rlbA);
    }

    private struct Biquad(double[] b, double[] a)
    {
        private readonly double _b0 = b[0], _b1 = b[1], _b2 = b[2], _a1 = a[1], _a2 = a[2];
        private double _z1, _z2;

        public double Process(double x)
        {
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }

        public void Reset() => _z1 = _z2 = 0.0;

        public void FlushDenormals()
        {
            if (Math.Abs(_z1) < 1e-25) _z1 = 0.0;
            if (Math.Abs(_z2) < 1e-25) _z2 = 0.0;
        }
    }
}
