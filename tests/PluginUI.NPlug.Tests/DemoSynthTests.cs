// 2026/10/19 16:55:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using NPlug.Validator;
using PluginUI.DemoSynth.Dsp;
using PluginUI.DemoSynth.Plugin;

namespace PluginUI.NPlug.Tests;

/// <summary>Demo synth: DSP building blocks against closed forms, engine behavior, and the Steinberg validator.</summary>
public class DemoSynthTests
{
    private const double Fs = 48000.0;

    private static float[] Render(SynthEngine engine, int length, float[]? side = null)
    {
        var left = new float[length];
        var right = new float[length];
        const int Block = 256;
        for (int i = 0; i < length; i += Block)
        {
            int n = Math.Min(Block, length - i);
            engine.Render(left.AsSpan(i, n), right.AsSpan(i, n), side is null ? default : side.AsSpan(i, n));
        }
        Assert.That(right, Is.EqualTo(left), "centered voices: identical channels");
        return left;
    }

    private static double Rms(ReadOnlySpan<float> x)
    {
        double sum = 0.0;
        foreach (float v in x) sum += v * (double)v;
        return Math.Sqrt(sum / Math.Max(1, x.Length));
    }

    /// <summary>Fundamental frequency from the autocorrelation peak (parabolic interpolation of the lag).</summary>
    private static double EstimateFrequency(ReadOnlySpan<float> x, double minHz, double maxHz)
    {
        int minLag = (int)(Fs / maxHz), maxLag = (int)(Fs / minHz) + 1;
        int n = x.Length - maxLag - 1;
        double Correlation(ReadOnlySpan<float> s, int lag)
        {
            double sum = 0.0;
            for (int i = 0; i < n; i++) sum += s[i] * (double)s[i + lag];
            return sum;
        }
        int best = minLag;
        double bestValue = double.MinValue;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double c = Correlation(x, lag);
            if (c > bestValue) { bestValue = c; best = lag; }
        }
        double a = Correlation(x, best - 1), b = bestValue, d = Correlation(x, best + 1);
        double shift = 0.5 * (a - d) / (a - 2.0 * b + d);
        return Fs / (best + shift);
    }

    // ---------------------------------------------------------------- building blocks

    [Test]
    public void AdsrMeetsItsTimes()
    {
        var s = AdsrSettings.Create(attack: 0.01, decay: 0.1, sustain: 0.5, release: 0.2, Fs);
        var env = new Adsr();
        env.Trigger();
        int attackSamples = 0;
        while (env.Stage == AdsrStage.Attack) { env.Next(s); attackSamples++; }
        Assert.That(attackSamples, Is.EqualTo(480).Within(2), "0 → 1 in the attack time");
        for (int i = 0; i < (int)(0.1 * Fs); i++) env.Next(s);
        Assert.That(env.Level - 0.5, Is.EqualTo(0.5 * 1e-3).Within(1e-5), "decay: −60 dB of the distance to sustain");
        for (int i = 0; i < 48000; i++) env.Next(s);
        Assert.That(env.Stage, Is.EqualTo(AdsrStage.Sustain));
        env.Release();
        for (int i = 0; i < (int)(0.2 * Fs); i++) env.Next(s);
        Assert.That(env.Level, Is.EqualTo(0.5e-3).Within(1e-5), "release: −60 dB in the release time");
        for (int i = 0; i < 48000 && env.IsActive; i++) env.Next(s);
        Assert.That(env.IsActive, Is.False, "stops below −80 dB");
    }

    [TestCase(200.0, 1000.0, 0.0)]
    [TestCase(1000.0, 1000.0, 0.5)]
    [TestCase(5000.0, 1000.0, 0.2)]
    [TestCase(800.0, 1000.0, 0.95)]
    public void SvfMatchesItsTransferFunction(double frequency, double cutoff, double resonance)
    {
        var filter = new SvfLowPass();
        filter.SetCoefficients(cutoff, resonance, Fs);
        int n = 48000;
        double peak = 0.0;
        for (int i = 0; i < n; i++)
        {
            float y = filter.Process((float)Math.Sin(2.0 * Math.PI * frequency * i / Fs));
            if (i > n / 2) peak = Math.Max(peak, Math.Abs(y));
        }
        Assert.That(peak, Is.EqualTo(SvfLowPass.Magnitude(frequency, cutoff, resonance, Fs)).Within(0.5).Percent);
    }

    [Test]
    public void PolyBlepSawHasLessAliasingThanANaiveSaw()
    {
        // 3.7 kHz saw at 48 kHz: harmonics above 24 kHz fold back. Measure the energy that lands between harmonics.
        const double f0 = 3700.0;
        int n = 1 << 14;
        var blep = new float[n];
        var naive = new float[n];
        var osc = new PolyBlepOscillator();
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            blep[i] = osc.Next(f0 / Fs, 0f);
            naive[i] = (float)(2.0 * phase - 1.0);
            phase += f0 / Fs;
            if (phase >= 1.0) phase -= 1.0;
        }
        Assert.That(InharmonicEnergy(blep, f0), Is.LessThan(InharmonicEnergy(naive, f0) * 0.2));
        Assert.That(blep.Max(), Is.LessThan(1.2f));
        Assert.That(blep.Average(), Is.EqualTo(0.0).Within(0.01), "no DC");
    }

    private static double InharmonicEnergy(float[] x, double f0)
    {
        // Goertzel-free DFT on a coarse grid between harmonics (enough for a ratio).
        int n = x.Length;
        double total = 0.0;
        for (int k = 1; k < n / 2; k += 7)
        {
            double f = k * Fs / n;
            double harmonic = Math.Round(f / f0) * f0;
            if (Math.Abs(f - harmonic) < 150.0) continue;
            double re = 0, im = 0;
            for (int i = 0; i < n; i++)
            {
                double w = 2.0 * Math.PI * k * i / n;
                re += x[i] * Math.Cos(w);
                im -= x[i] * Math.Sin(w);
            }
            total += re * re + im * im;
        }
        return total;
    }

    // ---------------------------------------------------------------- engine

    [Test]
    public void NoteProducesItsPitchAndReleasesToSilence()
    {
        var activity = new NoteActivity();
        var engine = new SynthEngine(activity);
        engine.Setup(Fs);
        var settings = SynthSettings.Default;
        settings.DetuneCents = 0.0;
        settings.Cutoff = 20000.0;
        settings.FilterEnvOctaves = 0.0;
        engine.Apply(settings);
        engine.NoteOn(69, 1f);
        Assert.That(activity.IsOn(69), Is.True);
        var x = Render(engine, 48000);

        Assert.That(EstimateFrequency(x.AsSpan(24000, 8192), 200.0, 1000.0), Is.EqualTo(440.0).Within(0.5), "A4");
        Assert.That(Rms(x.AsSpan(24000)), Is.GreaterThan(0.01));

        engine.NoteOff(69);
        Assert.That(activity.IsOn(69), Is.False);
        Render(engine, 48000); // release 0.35 s
        Assert.That(engine.ActiveVoiceCount, Is.Zero);
        var tail = Render(engine, 1024);
        Assert.That(tail.Max(Math.Abs), Is.Zero);
    }

    [Test]
    public void PitchBendAndVoiceStealing()
    {
        var engine = new SynthEngine();
        engine.Setup(Fs);
        var settings = SynthSettings.Default;
        settings.DetuneCents = 0.0;
        settings.Cutoff = 20000.0;
        settings.FilterEnvOctaves = 0.0;
        settings.PitchBendSemitones = 12.0; // beyond the parameter range, exact octave for the test
        engine.Apply(settings);
        engine.NoteOn(57, 1f); // A3 bent up an octave → 440 Hz
        var x = Render(engine, 48000);
        Assert.That(EstimateFrequency(x.AsSpan(24000, 8192), 200.0, 1000.0), Is.EqualTo(440.0).Within(0.5), "A3 + 12 semitones");

        engine.Reset();
        for (int note = 40; note < 40 + SynthEngine.VoiceCount + 4; note++) engine.NoteOn(note, 0.8f);
        Assert.That(engine.ActiveVoiceCount, Is.EqualTo(SynthEngine.VoiceCount), "voices are stolen, never added");
        engine.AllNotesOff();
        Render(engine, 48000);
        Assert.That(engine.ActiveVoiceCount, Is.Zero);
    }

    [Test]
    public void NoteIdsAndRetriggers()
    {
        var engine = new SynthEngine();
        engine.Setup(Fs);
        engine.NoteOn(60, 1f, 0, noteId: 11);
        engine.NoteOn(64, 1f, 0, noteId: 12);
        engine.NoteOff(60, 0, noteId: 12); // id wins over pitch: releases E4
        Render(engine, 48000);
        Assert.That(engine.ActiveVoiceCount, Is.EqualTo(1));
        engine.NoteOn(60, 1f); // same note again: same voice
        Assert.That(engine.ActiveVoiceCount, Is.EqualTo(1));
        engine.NoteOn(62, 0f); // velocity 0 = note-off of a note that isn't playing: nothing happens
        Assert.That(engine.ActiveVoiceCount, Is.EqualTo(1));
    }

    [Test]
    public void SidechainDucksTheOutput()
    {
        float[] RenderWith(float duck, float[]? side)
        {
            var engine = new SynthEngine();
            engine.Setup(Fs);
            var settings = SynthSettings.Default;
            settings.Duck = duck;
            engine.Apply(settings);
            engine.NoteOn(60, 1f);
            return Render(engine, 24000, side);
        }
        var kick = new float[24000];
        for (int i = 0; i < kick.Length; i++) kick[i] = (float)(0.9 * Math.Sin(2.0 * Math.PI * 60.0 * i / Fs));
        double dry = Rms(RenderWith(0f, null).AsSpan(12000));
        double unducked = Rms(RenderWith(0f, kick).AsSpan(12000));
        double ducked = Rms(RenderWith(0.8f, kick).AsSpan(12000));
        Assert.That(unducked, Is.EqualTo(dry).Within(1e-9), "duck at 0 %: side-chain has no effect");
        Assert.That(ducked, Is.LessThan(dry * 0.4), "an 80 % duck under a loud side-chain");
    }

    [Test]
    public void ExtremeSettingsStayFinite()
    {
        var engine = new SynthEngine();
        engine.Setup(44100.0);
        var settings = SynthSettings.Default;
        settings.Resonance = 1.0;
        settings.Cutoff = 20000.0;
        settings.FilterEnvOctaves = 5.0;
        settings.AmpAttack = settings.FilterAttack = 0.001;
        settings.Volume = 2f;
        settings.ModWheel = 1.0;
        engine.Apply(settings);
        for (int note = 0; note < 128; note += 9) engine.NoteOn(note, 1f);
        var x = Render(engine, 44100);
        Assert.That(x.All(float.IsFinite), Is.True);
        Assert.That(x.Max(Math.Abs), Is.LessThan(20f));

        var mono = new float[512];
        engine.Render(mono, Span<float>.Empty); // mono output bus
        Assert.That(mono.All(float.IsFinite), Is.True);
    }

    // ---------------------------------------------------------------- VST3 validator

    [Test]
    public void SteinbergValidatorPasses()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        bool passed = AudioPluginValidator.Validate(DemoSynthPlugin.GetFactory().Export, output, errors);
        string text = output.ToString();
        TestContext.Out.WriteLine(text);
        Assert.That(errors.ToString().Trim(), Is.Empty);
        Assert.That(passed, Is.True);
        var summary = Regex.Match(text, @"Result: (\d+) tests passed, (\d+) tests failed");
        Assert.That(summary.Success, Is.True, "validator summary present");
        Assert.That(summary.Groups[2].Value, Is.EqualTo("0"));
    }
}
