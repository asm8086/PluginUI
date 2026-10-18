// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Dsp;

namespace PluginUI.Gallery;

/// <summary>Language of the gallery texts: English by default, French with <c>--lang fr</c>.</summary>
internal static class L
{
    /// <summary><c>true</c> for the French texts.</summary>
    public static bool French { get; set; }

    /// <summary>The text in the current language.</summary>
    public static string T(string english, string french) => French ? french : english;
}

/// <summary>Layout helpers for the gallery pages.</summary>
internal static class Place
{
    /// <summary>Adds an element to a canvas at a position (NaN size = natural size).</summary>
    public static T At<T>(this Canvas canvas, T element, float x, float y, float width = float.NaN, float height = float.NaN) where T : UIElement
    {
        element.SetBounds(x, y, width, height);
        canvas.Children.Add(element);
        return element;
    }

    /// <summary>Section caption.</summary>
    public static Label Caption(this Canvas canvas, string text, float x, float y, float width = 300f)
        => canvas.At(new Label(text) { IsDim = true, TextStyle = new TextStyle("Segoe UI", 11f, FontWeight.SemiBold) }, x, y, width, 18f);
}

/// <summary>
/// Synthetic "audio thread" for the meters page: a ticker that writes a test signal into a ring buffer and
/// publishes block peaks, as a real processor would do from its process callback.
/// </summary>
internal sealed class DemoAudio : ITickable
{
    private const double SampleRate = 48000.0;
    private readonly float[] _left = new float[4096];
    private readonly float[] _right = new float[4096];
    private readonly Random _random = new(7);
    private double _time;

    public AudioRingBuffer Ring { get; } = new(16384);

    /// <summary>Left channel (goniometer, correlation).</summary>
    public AudioRingBuffer Left { get; } = new(16384);

    /// <summary>Right channel.</summary>
    public AudioRingBuffer Right { get; } = new(16384);

    /// <summary>EBU R128 measurement of the demo signal.</summary>
    public LoudnessAnalyzer Loudness { get; } = new();

    /// <summary>Input peak of the simulated compressor.</summary>
    public PeakValue CompressorInput { get; } = new();

    /// <summary>Gain reduction (dB) of the simulated compressor.</summary>
    public PeakValue GainReduction { get; } = new();

    /// <summary>Normalized play position of the simulated sampler.</summary>
    public double PlayPosition { get; private set; }

    /// <summary>Current step of the simulated sequencer.</summary>
    public int CurrentStep { get; private set; }

    private readonly List<PeakValue[]> _peakSets = new();
    private readonly List<PeakValue[]> _rmsSets = new();

    /// <summary>New stereo peak sources (one set per meter: a <see cref="PeakValue"/> has a single reader).</summary>
    public PeakValue[] NewPeakSources()
    {
        PeakValue[] set = [new(), new()];
        _peakSets.Add(set);
        return set;
    }

    /// <summary>New stereo RMS sources (one set per meter).</summary>
    public PeakValue[] NewRmsSources()
    {
        PeakValue[] set = [new(), new()];
        _rmsSets.Add(set);
        return set;
    }

    public PianoKeyboard? Keyboard { get; set; }

    public bool Tick(double elapsedSeconds)
    {
        Generate(Math.Clamp((int)(elapsedSeconds * SampleRate), 64, _left.Length));
        return true;
    }

    /// <summary>Generates a block (also used by the snapshot mode).</summary>
    public void Generate(int count)
    {
        // Slowly moving chord with an amplitude envelope and some noise.
        double env = 0.55 + 0.4 * Math.Sin(_time * 0.9);
        double f0 = 110.0 * Math.Pow(2.0, Math.Floor(_time / 2.0) % 4 / 12.0 * 3.0);
        double sumL = 0, sumR = 0;
        for (int i = 0; i < count; i++)
        {
            double t = _time + i / SampleRate;
            double s = Math.Sin(2 * Math.PI * f0 * t) * 0.5 + Math.Sin(2 * Math.PI * f0 * 1.5 * t) * 0.25
                     + Math.Sin(2 * Math.PI * f0 * 4.01 * t) * 0.12 + (_random.NextDouble() * 2 - 1) * 0.03;
            // Right channel: same chord plus a detuned voice and its own noise, so the stereo image is wide.
            double wide = Math.Sin(2 * Math.PI * f0 * 2.003 * t + 1.1) * 0.22 + (_random.NextDouble() * 2 - 1) * 0.04;
            float l = (float)(s * env), r = (float)((s * (0.8 + 0.2 * Math.Sin(t * 1.7)) + wide) * env);
            _left[i] = l;
            _right[i] = r;
            sumL += l * l;
            sumR += r * r;
        }
        _time += count / SampleRate;
        Ring.WriteMono(_left.AsSpan(0, count), _right.AsSpan(0, count));
        Left.Write(_left.AsSpan(0, count));
        Right.Write(_right.AsSpan(0, count));
        Loudness.Process(_left.AsSpan(0, count), _right.AsSpan(0, count));
        float peak = PluginUI.Dsp.Decibels.FromGain(MathF.Max(MathF.Abs(_left[0]), MathF.Abs(_left[count / 2])));
        CompressorInput.Push(MathF.Max(MathF.Abs(_left[0]), MathF.Abs(_left[count / 2])));
        GainReduction.Push((float)CompressorMath.GainReductionDb(peak, -18.0, 4.0, 6.0));
        PlayPosition = _time * 0.4 % 1.0;
        CurrentStep = (int)(_time * 4.0) % 16;
        foreach (var set in _peakSets)
        {
            set[0].PushBlockPeak(_left.AsSpan(0, count));
            set[1].PushBlockPeak(_right.AsSpan(0, count));
        }
        foreach (var set in _rmsSets)
        {
            set[0].Push((float)Math.Sqrt(sumL / count));
            set[1].Push((float)Math.Sqrt(sumR / count));
        }
        if (Keyboard is not null)
        {
            int root = 48 + (int)(Math.Floor(_time / 2.0) % 4) * 3;
            Keyboard.ClearNoteStates();
            Keyboard.SetNoteState(root, true);
            Keyboard.SetNoteState(root + 7, true);
            Keyboard.SetNoteState(root + 12, true);
        }
    }
}
