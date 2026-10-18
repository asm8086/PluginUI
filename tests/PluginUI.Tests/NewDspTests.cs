// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Numerics;
using PluginUI.Dsp;

namespace PluginUI.Tests;

/// <summary>DSP behind the new controls, checked against references (BS.1770, RBJ cookbook, brute force).</summary>
public class NewDspTests
{
    private static float[] Sine(double frequency, double amplitude, double sampleRate, int length)
    {
        var x = new float[length];
        for (int i = 0; i < length; i++) x[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / sampleRate));
        return x;
    }

    // ---------------------------------------------------------------- loudness

    [Test]
    public void KWeightingMatchesTheBs1770CoefficientsAt48kHz()
    {
        var (preB, preA, rlbB, rlbA) = LoudnessAnalyzer.KWeighting(48000.0);
        Assert.Multiple(() =>
        {
            Assert.That(preB, Is.EqualTo(new[] { 1.53512485958697, -2.69169618940638, 1.19839281085285 }).Within(1e-9));
            Assert.That(preA, Is.EqualTo(new[] { 1.0, -1.69065929318241, 0.73248077421585 }).Within(1e-9));
            Assert.That(rlbB, Is.EqualTo(new[] { 1.0, -2.0, 1.0 }));
            Assert.That(rlbA, Is.EqualTo(new[] { 1.0, -1.99004745483398, 0.99007225036621 }).Within(1e-9));
        });
    }

    [TestCase(48000.0)]
    [TestCase(44100.0)]
    [TestCase(96000.0)]
    public void SineAtMinus23DbfsReadsMinus23Lufs(double sampleRate)
    {
        var analyzer = new LoudnessAnalyzer();
        analyzer.Configure(sampleRate, 2);
        // EBU Tech 3341 test signal: 997 Hz sine at −23 dBFS on both channels. The −0.691 offset of BS.1770 makes a
        // full-scale 997 Hz sine on both channels read 0 LUFS, so this one must read −23 LUFS.
        double amplitude = Math.Pow(10.0, -23.0 / 20.0);
        var block = Sine(997.0, amplitude, sampleRate, (int)sampleRate * 20);
        for (int i = 0; i < block.Length; i += 512)
        {
            int n = Math.Min(512, block.Length - i);
            analyzer.Process(block.AsSpan(i, n), block.AsSpan(i, n));
        }
        Assert.Multiple(() =>
        {
            Assert.That(analyzer.MomentaryLufs, Is.EqualTo(-23.0).Within(0.1));
            Assert.That(analyzer.ShortTermLufs, Is.EqualTo(-23.0).Within(0.1));
            Assert.That(analyzer.IntegratedLufs, Is.EqualTo(-23.0).Within(0.1));
            Assert.That(analyzer.LoudnessRange, Is.LessThan(0.5));
            Assert.That(20.0 * Math.Log10(analyzer.SamplePeak), Is.EqualTo(-23.0).Within(0.05));
        });
    }

    [Test]
    public void IntegratedLoudnessIgnoresSilenceAndLoudnessRangeMeasuresSteps()
    {
        const double fs = 48000.0;
        var analyzer = new LoudnessAnalyzer();
        analyzer.Configure(fs, 2);
        void Feed(double db, double seconds)
        {
            var x = Sine(997.0, Math.Pow(10.0, db / 20.0), fs, (int)(fs * seconds));
            for (int i = 0; i < x.Length; i += 1024)
            {
                int n = Math.Min(1024, x.Length - i);
                analyzer.Process(x.AsSpan(i, n), x.AsSpan(i, n));
            }
        }
        Feed(-20.0, 20.0);
        Feed(-200.0, 20.0);  // silence: below the absolute gate
        Feed(-30.0, 20.0);
        // Integrated: mean energy of −20 and −30 LUFS blocks (equal time), the −30 blocks pass the relative gate
        // (mean − 10 LU ≈ −32.4), so I = 10·log10((10^-2 + 10^-3)/2) ≈ −22.6 LUFS.
        Assert.That(analyzer.IntegratedLufs, Is.EqualTo(10.0 * Math.Log10((Math.Pow(10, -2.0) + Math.Pow(10, -3.0)) / 2.0)).Within(0.3));
        Assert.That(analyzer.LoudnessRange, Is.EqualTo(10.0).Within(1.0));

        analyzer.RequestReset();
        analyzer.Process(new float[16], new float[16]);
        Assert.That(analyzer.IntegratedLufs, Is.EqualTo(double.NegativeInfinity), "reset requested from another thread");
    }

    // ---------------------------------------------------------------- equalizer

    private static double DirectMagnitudeDb(BiquadDesign.Coefficients c, double f, double fs)
    {
        var z1 = Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * f / fs);
        var z2 = z1 * z1;
        var h = (c.B0 + c.B1 * z1 + c.B2 * z2) / (1.0 + c.A1 * z1 + c.A2 * z2);
        return 20.0 * Math.Log10(h.Magnitude);
    }

    [Test]
    public void BiquadMagnitudeMatchesTheTransferFunction()
    {
        const double fs = 48000.0;
        foreach (var type in Enum.GetValues<EqBandType>())
        {
            var c = BiquadDesign.Design(type, 1200.0, 6.0, 1.4, fs);
            foreach (double f in (double[])[20, 100, 700, 1200, 3000, 10000, 20000])
            {
                double expected = DirectMagnitudeDb(c, f, fs);
                if (expected < -120) continue; // deep notch: both are "very low"
                Assert.That(BiquadDesign.MagnitudeDb(c, f, fs), Is.EqualTo(expected).Within(1e-6), $"{type} at {f} Hz");
            }
        }
        var bell = BiquadDesign.Design(EqBandType.Bell, 1000.0, -9.0, 2.0, fs);
        Assert.That(BiquadDesign.MagnitudeDb(bell, 1000.0, fs), Is.EqualTo(-9.0).Within(1e-9), "bell gain at its center");
        var low = BiquadDesign.Design(EqBandType.LowShelf, 200.0, 6.0, 0.7071, fs);
        Assert.That(BiquadDesign.MagnitudeDb(low, 10.0, fs), Is.EqualTo(6.0).Within(0.05), "shelf plateau");
        var cut = BiquadDesign.Design(EqBandType.LowCut, 1000.0, 0.0, 0.7071, fs);
        Assert.That(BiquadDesign.MagnitudeDb(cut, 1000.0, fs), Is.EqualTo(-3.01).Within(0.02), "Butterworth corner");
    }

    // ---------------------------------------------------------------- stereo, compressor

    [Test]
    public void CorrelationAndBalance()
    {
        var x = Sine(440.0, 0.5, 48000.0, 4800);
        var inverted = x.Select(v => -v).ToArray();
        var random = new Random(1);
        var a = Enumerable.Range(0, 48000).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        var b = Enumerable.Range(0, 48000).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(StereoAnalysis.Correlation(x, x), Is.EqualTo(1.0).Within(1e-6));
            Assert.That(StereoAnalysis.Correlation(x, inverted), Is.EqualTo(-1.0).Within(1e-6));
            Assert.That(Math.Abs(StereoAnalysis.Correlation(a, b)), Is.LessThan(0.03));
            Assert.That(StereoAnalysis.Correlation(new float[100], new float[100]), Is.Zero);
            Assert.That(StereoAnalysis.Balance(x, new float[x.Length]), Is.EqualTo(-1.0).Within(1e-9));
            Assert.That(StereoAnalysis.Balance(x, x), Is.EqualTo(0.0).Within(1e-9));
        });
    }

    [Test]
    public void CompressorCurveIsContinuousAndMonotonic()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompressorMath.OutputDb(-40, -20, 4, 0), Is.EqualTo(-40));
            Assert.That(CompressorMath.OutputDb(-8, -20, 4, 0), Is.EqualTo(-17));
            Assert.That(CompressorMath.OutputDb(-8, -20, 1, 6), Is.EqualTo(-8).Within(1e-12), "ratio 1 = no compression");
            Assert.That(CompressorMath.OutputDb(-23, -20, 4, 6), Is.EqualTo(-23).Within(1e-12), "knee start");
            Assert.That(CompressorMath.OutputDb(-17, -20, 4, 6), Is.EqualTo(-20 + 3.0 / 4).Within(1e-12), "knee end");
            Assert.That(CompressorMath.GainReductionDb(0, -20, 4, 0), Is.EqualTo(15).Within(1e-12));
        });
        double previous = double.NegativeInfinity;
        for (double x = -60; x <= 6; x += 0.05)
        {
            double y = CompressorMath.OutputDb(x, -24, 8, 10);
            Assert.That(y, Is.GreaterThanOrEqualTo(previous - 1e-12));
            previous = y;
        }
    }

    // ---------------------------------------------------------------- WAV, peaks

    private static byte[] Wav(int format, int bits, int channels, int rate, Action<BinaryWriter> writeSamples)
    {
        using var data = new MemoryStream();
        using (var w = new BinaryWriter(data, System.Text.Encoding.ASCII, leaveOpen: true)) writeSamples(w);
        using var file = new MemoryStream();
        using var o = new BinaryWriter(file);
        o.Write("RIFF"u8); o.Write(36 + (int)data.Length); o.Write("WAVE"u8);
        o.Write("fmt "u8); o.Write(16); o.Write((short)format); o.Write((short)channels); o.Write(rate);
        o.Write(rate * channels * bits / 8); o.Write((short)(channels * bits / 8)); o.Write((short)bits);
        o.Write("LIST"u8); o.Write(4); o.Write("INFO"u8); // an unknown chunk must be skipped
        o.Write("data"u8); o.Write((int)data.Length); o.Write(data.ToArray());
        o.Flush();
        return file.ToArray();
    }

    [Test]
    public void WavReaderDecodesPcmAndFloat()
    {
        var pcm16 = WavReader.Read(Wav(1, 16, 2, 44100, w => { w.Write((short)16384); w.Write((short)-32768); w.Write((short)0); w.Write((short)32767); }));
        Assert.Multiple(() =>
        {
            Assert.That(pcm16.SampleRate, Is.EqualTo(44100));
            Assert.That(pcm16.Channels.Length, Is.EqualTo(2));
            Assert.That(pcm16.Length, Is.EqualTo(2));
            Assert.That(pcm16.Channels[0], Is.EqualTo(new[] { 0.5f, 0f }));
            Assert.That(pcm16.Channels[1][0], Is.EqualTo(-1f));
        });
        var pcm24 = WavReader.Read(Wav(1, 24, 1, 48000, w => { w.Write((byte)0); w.Write((byte)0); w.Write((byte)0xC0); })); // -0.5
        Assert.That(pcm24.Channels[0][0], Is.EqualTo(-0.5f));
        var f32 = WavReader.Read(Wav(3, 32, 1, 96000, w => { w.Write(0.25f); w.Write(-0.75f); }));
        Assert.That(f32.Channels[0], Is.EqualTo(new[] { 0.25f, -0.75f }));
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new byte[20]));
    }

    [Test]
    public void WaveformPeaksMatchBruteForce()
    {
        var random = new Random(5);
        var samples = Enumerable.Range(0, 200_000).Select(i => (float)Math.Sin(i * 0.001) * 0.5f + (float)(random.NextDouble() - 0.5) * 0.2f).ToArray();
        var peaks = new WaveformPeaks(samples);
        for (int k = 0; k < 300; k++)
        {
            int a = random.Next(samples.Length), length = random.Next(1, 120_000);
            int b = Math.Min(samples.Length, a + length);
            var (lo, hi) = peaks.Range(a, b);
            var slice = samples.AsSpan(a, b - a).ToArray();
            Assert.That((lo, hi), Is.EqualTo((slice.Min(), slice.Max())), $"range {a}..{b}");
        }
        Assert.That(peaks.Range(10, 10), Is.EqualTo((0f, 0f)));
    }
}
