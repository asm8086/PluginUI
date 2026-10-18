// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Numerics;
using PluginUI.Dsp;

namespace PluginUI.Tests;

public class DspTests
{
    [Test]
    public void FftFindsAFullScaleSineAtZeroDecibels()
    {
        const int n = 2048;
        const double sampleRate = 48000.0;
        int bin = 100; // exact bin frequency: no leakage between bins
        double freq = bin * sampleRate / n;
        var samples = new float[n];
        for (int i = 0; i < n; i++) samples[i] = (float)Math.Sin(2 * Math.PI * freq * i / sampleRate);
        var fft = new Fft(n);
        var db = new float[n / 2];
        fft.MagnitudeSpectrum(samples, db, WindowFunction.Hann);
        int peak = Array.IndexOf(db, db.Max());
        Assert.Multiple(() =>
        {
            Assert.That(peak, Is.EqualTo(bin));
            Assert.That(db[peak], Is.EqualTo(0f).Within(0.1f));
            Assert.That(db[bin + 20], Is.LessThan(-80f));
        });
    }

    [Test]
    public void FftRejectsInvalidSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Fft(1000));
    }

    [Test]
    public void RingBufferKeepsTheLatestSamples()
    {
        var ring = new AudioRingBuffer(16);
        var block = Enumerable.Range(0, 40).Select(i => (float)i).ToArray();
        ring.Write(block.AsSpan(0, 10));
        ring.Write(block.AsSpan(10, 30));
        var latest = new float[4];
        Assert.That(ring.CopyLatest(latest), Is.EqualTo(4));
        Assert.That(latest, Is.EqualTo(new[] { 36f, 37f, 38f, 39f }));

        var read = new float[64];
        int count = ring.Read(read);
        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(16), "overrun: only the last capacity samples are readable");
            Assert.That(read[0], Is.EqualTo(24f));
            Assert.That(ring.Available, Is.Zero);
        });
    }

    [Test]
    public void BallisticsAttackInstantlyAndReleaseLinearly()
    {
        var b = new LevelBallistics { ReleaseDbPerSecond = 20f, HoldSeconds = 0.5f, FloorDb = -60f };
        b.Update(-6f, 0.01);
        Assert.That(b.LevelDb, Is.EqualTo(-6f));
        b.Update(-60f, 0.5);
        Assert.Multiple(() =>
        {
            Assert.That(b.LevelDb, Is.EqualTo(-16f).Within(1e-4));
            Assert.That(b.HoldDb, Is.EqualTo(-6f), "still holding");
        });
        b.Update(-60f, 1.0);
        Assert.That(b.HoldDb, Is.LessThan(-6f));
    }

    [Test]
    public void PeakValueKeepsTheMaximumUntilRead()
    {
        var p = new PeakValue();
        p.Push(0.2f);
        p.PushBlockPeak([0.1f, -0.7f, 0.3f]);
        p.Push(0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(p.ReadAndReset(), Is.EqualTo(0.7f));
            Assert.That(p.ReadAndReset(), Is.Zero);
            Assert.That(Decibels.FromGain(1f), Is.Zero);
            Assert.That(Decibels.ToGain(-20f), Is.EqualTo(0.1f).Within(1e-6));
        });
    }

    // ---- SIMD implementations checked against straightforward scalar references ----

    private static Complex[] NaiveDft(IReadOnlyList<Complex> x)
    {
        int n = x.Count;
        var result = new Complex[n];
        for (int k = 0; k < n; k++)
        {
            Complex sum = Complex.Zero;
            for (int j = 0; j < n; j++) sum += x[j] * Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * ((long)k * j % n) / n);
            result[k] = sum;
        }
        return result;
    }

    [TestCase(16)]
    [TestCase(32)]
    [TestCase(256)]
    [TestCase(2048)]
    public void FftForwardMatchesTheDefinition(int n)
    {
        var random = new Random(n);
        var data = Enumerable.Range(0, n).Select(_ => new Complex(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1)).ToArray();
        var expected = NaiveDft(data);
        new Fft(n).Forward(data);
        double maxError = data.Zip(expected, (a, b) => (a - b).Magnitude).Max();
        Assert.That(maxError, Is.LessThan(1e-10 * n));
    }

    [TestCase(16, WindowFunction.Rectangular)]
    [TestCase(64, WindowFunction.Hann)]
    [TestCase(1024, WindowFunction.BlackmanHarris)]
    public void RealInputSpectrumMatchesTheDefinition(int n, WindowFunction window)
    {
        var random = new Random(n);
        var samples = Enumerable.Range(0, n).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        var fft = new Fft(n);
        var w = fft.GetWindow(window).ToArray();
        double windowSum = w.Sum(v => (double)v);
        var spectrum = NaiveDft(samples.Select((s, i) => new Complex(s * w[i], 0.0)).ToArray());
        var db = new float[n / 2];
        fft.MagnitudeSpectrum(samples, db, window, floorDb: -200f);
        for (int k = 0; k < n / 2; k++)
        {
            double expectedDb = 20.0 * Math.Log10(Math.Max(1e-10, spectrum[k].Magnitude * 2.0 / windowSum));
            Assert.That(db[k], Is.EqualTo(expectedDb).Within(1e-3), $"bin {k}");
        }
    }

    [Test]
    public void SpectrumFloorAppliesToSilence()
    {
        var fft = new Fft(64);
        var db = new float[32];
        fft.MagnitudeSpectrum(new float[64], db, WindowFunction.Hann, floorDb: -120f);
        Assert.That(db, Is.All.EqualTo(-120f));
    }

    [Test]
    public void RingBufferWrapsAroundLikeAQueue()
    {
        var ring = new AudioRingBuffer(64);
        var reference = new List<float>();
        var random = new Random(7);
        float next = 0f;
        long readCursor = 0;
        for (int round = 0; round < 200; round++)
        {
            int count = random.Next(0, 100); // sometimes larger than the capacity
            var block = new float[count];
            for (int i = 0; i < count; i++) block[i] = next++;
            if (round % 3 == 0)
            {
                // Mono path: average of (v − 1, v + 1) = v.
                ring.WriteMono(block.Select(v => v - 1f).ToArray(), block.Select(v => v + 1f).ToArray());
            }
            else
            {
                ring.Write(block);
            }
            reference.AddRange(block);

            var latest = new float[random.Next(1, 33)];
            int copied = ring.CopyLatest(latest);
            Assert.That(latest.Take(copied), Is.EqualTo(reference.Skip(reference.Count - copied)), $"round {round}");

            if (round % 5 == 0)
            {
                var read = new float[random.Next(1, 80)];
                int n = ring.Read(read);
                readCursor = Math.Max(readCursor, reference.Count - ring.Capacity - 0L);
                readCursor = Math.Max(readCursor, (long)reference.Count - (n + ring.Available));
                Assert.That(read.Take(n), Is.EqualTo(reference.Skip((int)readCursor).Take(n)), $"read round {round}");
                readCursor += n;
            }
        }
    }

    [Test]
    public void VectorKernelsMatchScalarReferences()
    {
        var random = new Random(3);
        for (int n = 0; n <= 70; n++)
        {
            var x = Enumerable.Range(0, n).Select(_ => (float)(random.NextDouble() * 4 - 2)).ToArray();
            var y = Enumerable.Range(0, n).Select(_ => (float)(random.NextDouble() * 4 - 2)).ToArray();
            Assert.That(VectorOps.MaxAbs(x), Is.EqualTo(n == 0 ? 0f : x.Max(MathF.Abs)), $"MaxAbs n={n}");
            Assert.That(VectorOps.Max(x), Is.EqualTo(n == 0 ? float.NegativeInfinity : x.Max()), $"Max n={n}");
            var (lo, hi) = VectorOps.MinMax(x);
            if (n > 0) Assert.That((lo, hi), Is.EqualTo((x.Min(), x.Max())), $"MinMax n={n}");

            var average = new float[n];
            VectorOps.Average(x, y, average);
            Assert.That(average, Is.EqualTo(x.Zip(y, (a, b) => 0.5f * (a + b))), $"Average n={n}");

            var power = x.Select(v => v * v * 0.01f).ToArray();
            var db = (float[])power.Clone();
            VectorOps.PowerToDecibels(db, -60f);
            for (int i = 0; i < n; i++)
            {
                Assert.That(db[i], Is.EqualTo(Math.Max(-60.0, 10.0 * Math.Log10(power[i]))).Within(1e-4), $"dB n={n} i={i}");
            }
        }
        Assert.That(VectorOps.MaxAbs(Enumerable.Repeat(-0.25f, 33).ToArray()), Is.EqualTo(0.25f));
    }
}
