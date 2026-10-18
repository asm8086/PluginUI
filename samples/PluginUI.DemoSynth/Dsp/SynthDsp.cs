// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace PluginUI.DemoSynth.Dsp;

/// <summary>
/// Band-limited sawtooth / square oscillator (PolyBLEP: the discontinuities are smoothed by a two-sample polynomial
/// residual, which removes most of the audible aliasing for a few operations per sample).
/// </summary>
public struct PolyBlepOscillator
{
    private double _phase;

    /// <summary>Phase in [0, 1).</summary>
    public readonly double Phase => _phase;

    /// <summary>Restarts the waveform at a given phase.</summary>
    /// <param name="phase">Phase in [0, 1).</param>
    public void Reset(double phase = 0.0) => _phase = phase - Math.Floor(phase);

    /// <summary>Next sample.</summary>
    /// <param name="increment">Frequency / sample rate, in (0, 0.5).</param>
    /// <param name="shape">0 = sawtooth, 1 = square, crossfade in between.</param>
    /// <returns>The sample, in about [−1, 1].</returns>
    public float Next(double increment, float shape)
    {
        double t = _phase;
        double saw = 2.0 * t - 1.0 - Blep(t, increment);
        double half = t + 0.5;
        if (half >= 1.0) half -= 1.0;
        double square = (t < 0.5 ? 1.0 : -1.0) + Blep(t, increment) - Blep(half, increment);
        _phase = t + increment;
        if (_phase >= 1.0) _phase -= 1.0;
        return (float)(saw + (square - saw) * shape);
    }

    /// <summary>Polynomial band-limited step residual around a discontinuity at phase 0.</summary>
    /// <param name="t">Phase in [0, 1).</param>
    /// <param name="dt">Phase increment.</param>
    /// <returns>The correction to subtract from a rising (add to a falling) step.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Blep(double t, double dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1.0;
        }
        if (t > 1.0 - dt)
        {
            t = (t - 1.0) / dt;
            return t * t + t + t + 1.0;
        }
        return 0.0;
    }
}

/// <summary>Stage of an <see cref="Adsr"/> envelope.</summary>
public enum AdsrStage
{
    /// <summary>Silent, finished.</summary>
    Idle,

    /// <summary>Rising towards 1.</summary>
    Attack,

    /// <summary>Falling towards the sustain level.</summary>
    Decay,

    /// <summary>Holding the sustain level.</summary>
    Sustain,

    /// <summary>Falling towards 0 after the note-off.</summary>
    Release,
}

/// <summary>Per-sample coefficients of an <see cref="Adsr"/>, computed once per block from the times.</summary>
public struct AdsrSettings
{
    /// <summary>Attack: one-pole coefficient towards <see cref="AttackTarget"/>.</summary>
    public double AttackCoefficient;

    /// <summary>Decay multiplier per sample (towards the sustain level).</summary>
    public double DecayCoefficient;

    /// <summary>Release multiplier per sample.</summary>
    public double ReleaseCoefficient;

    /// <summary>Sustain level in [0, 1].</summary>
    public double Sustain;

    /// <summary>Overshoot target of the attack (analog-style curve that reaches 1 in the attack time).</summary>
    public const double AttackTarget = 1.3;

    /// <summary>Level below which a releasing envelope stops (−80 dB).</summary>
    public const double SilenceLevel = 1e-4;

    /// <summary>Computes the coefficients.</summary>
    /// <param name="attack">Attack time in seconds.</param>
    /// <param name="decay">Decay time in seconds (−60 dB of the distance to the sustain level).</param>
    /// <param name="sustain">Sustain level in [0, 1].</param>
    /// <param name="release">Release time in seconds (−60 dB).</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>The settings.</returns>
    public static AdsrSettings Create(double attack, double decay, double sustain, double release, double sampleRate)
    {
        const double Ln1000 = 6.907755278982137; // 60 dB
        double fs = Math.Max(1.0, sampleRate);
        // 1 − exp(−r/fs) with r chosen so that 0 → 1 takes exactly 'attack' seconds towards the 1.3 target.
        double attackRate = Math.Log(AttackTarget / (AttackTarget - 1.0)) / Math.Max(attack, 1e-4);
        return new AdsrSettings
        {
            AttackCoefficient = 1.0 - Math.Exp(-attackRate / fs),
            DecayCoefficient = Math.Exp(-Ln1000 / (Math.Max(decay, 1e-4) * fs)),
            ReleaseCoefficient = Math.Exp(-Ln1000 / (Math.Max(release, 1e-4) * fs)),
            Sustain = Math.Clamp(sustain, 0.0, 1.0),
        };
    }
}

/// <summary>Attack / decay / sustain / release envelope with exponential segments (one multiply-add per sample).</summary>
public struct Adsr
{
    private double _level;

    /// <summary>Current stage.</summary>
    public AdsrStage Stage { readonly get; private set; }

    /// <summary>Current level in [0, 1].</summary>
    public readonly double Level => _level;

    /// <summary><c>true</c> unless the envelope finished.</summary>
    public readonly bool IsActive => Stage != AdsrStage.Idle;

    /// <summary>Starts (or restarts from the current level, without a click).</summary>
    public void Trigger() => Stage = AdsrStage.Attack;

    /// <summary>Enters the release stage.</summary>
    public void Release()
    {
        if (Stage != AdsrStage.Idle) Stage = AdsrStage.Release;
    }

    /// <summary>Stops immediately.</summary>
    public void Reset()
    {
        _level = 0.0;
        Stage = AdsrStage.Idle;
    }

    /// <summary>Next level.</summary>
    /// <param name="s">Coefficients.</param>
    /// <returns>The level.</returns>
    public float Next(in AdsrSettings s)
    {
        switch (Stage)
        {
            case AdsrStage.Attack:
                _level += (AdsrSettings.AttackTarget - _level) * s.AttackCoefficient;
                if (_level >= 1.0)
                {
                    _level = 1.0;
                    Stage = AdsrStage.Decay;
                }
                break;
            case AdsrStage.Decay:
                _level = s.Sustain + (_level - s.Sustain) * s.DecayCoefficient;
                if (Math.Abs(_level - s.Sustain) < 1e-5)
                {
                    _level = s.Sustain;
                    Stage = AdsrStage.Sustain;
                }
                break;
            case AdsrStage.Sustain:
                _level = s.Sustain; // follows sustain changes
                break;
            case AdsrStage.Release:
                _level *= s.ReleaseCoefficient;
                if (_level < AdsrSettings.SilenceLevel) Reset();
                break;
        }
        return (float)_level;
    }
}

/// <summary>
/// Zero-delay-feedback state-variable low-pass filter (topology-preserving transform, A. Simper): stable under fast
/// cutoff modulation, resonance up to self-oscillation edge.
/// </summary>
public struct SvfLowPass
{
    private double _ic1, _ic2;
    private double _a1, _a2, _a3;

    /// <summary>Clears the state.</summary>
    public void Reset() => _ic1 = _ic2 = 0.0;

    /// <summary>Sets cutoff and resonance.</summary>
    /// <param name="cutoff">Cutoff in Hz (clamped below Nyquist).</param>
    /// <param name="resonance">Resonance in [0, 1] (Q from 0.5 to 25).</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    public void SetCoefficients(double cutoff, double resonance, double sampleRate)
    {
        double fc = Math.Clamp(cutoff, 10.0, sampleRate * 0.49);
        double g = Math.Tan(Math.PI * fc / sampleRate);
        double k = 2.0 - 1.96 * Math.Clamp(resonance, 0.0, 1.0); // 1/Q
        _a1 = 1.0 / (1.0 + g * (g + k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    /// <summary>Filters one sample.</summary>
    /// <param name="x">Input.</param>
    /// <returns>Low-pass output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float x)
    {
        double v3 = x - _ic2;
        double v1 = _a1 * _ic1 + _a2 * v3;
        double v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2.0 * v1 - _ic1;
        _ic2 = 2.0 * v2 - _ic2;
        return (float)v2;
    }

    /// <summary>Exact magnitude response of the analog prototype after the bilinear transform (for tests).</summary>
    /// <param name="frequency">Frequency in Hz.</param>
    /// <param name="cutoff">Cutoff in Hz.</param>
    /// <param name="resonance">Resonance in [0, 1].</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>|H| (linear).</returns>
    public static double Magnitude(double frequency, double cutoff, double resonance, double sampleRate)
    {
        double g = Math.Tan(Math.PI * cutoff / sampleRate);
        double w = Math.Tan(Math.PI * frequency / sampleRate) / g; // prewarped, normalized to the cutoff
        double k = 2.0 - 1.96 * Math.Clamp(resonance, 0.0, 1.0);
        return 1.0 / Math.Sqrt((1.0 - w * w) * (1.0 - w * w) + k * k * w * w);
    }
}

/// <summary>Peak envelope follower (instant-ish attack, exponential release) for side-chain detection.</summary>
public struct EnvelopeFollower
{
    private double _envelope;
    private double _attack, _release;

    /// <summary>Current envelope.</summary>
    public readonly double Envelope => _envelope;

    /// <summary>Sets the time constants.</summary>
    /// <param name="attackSeconds">Attack time constant.</param>
    /// <param name="releaseSeconds">Release time constant.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    public void Setup(double attackSeconds, double releaseSeconds, double sampleRate)
    {
        double fs = Math.Max(1.0, sampleRate);
        _attack = 1.0 - Math.Exp(-1.0 / (Math.Max(attackSeconds, 1e-5) * fs));
        _release = 1.0 - Math.Exp(-1.0 / (Math.Max(releaseSeconds, 1e-5) * fs));
    }

    /// <summary>Clears the envelope.</summary>
    public void Reset() => _envelope = 0.0;

    /// <summary>Feeds one rectified sample.</summary>
    /// <param name="level">|x|.</param>
    /// <returns>The envelope.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Process(double level)
    {
        _envelope += (level - _envelope) * (level > _envelope ? _attack : _release);
        return _envelope;
    }
}
