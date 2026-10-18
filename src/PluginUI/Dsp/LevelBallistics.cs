// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Dsp;

/// <summary>Level conversions.</summary>
public static class Decibels
{
    /// <summary>Linear gain → dB (floored).</summary>
    public static float FromGain(float gain, float floorDb = -120f) => gain <= 0f ? floorDb : MathF.Max(floorDb, 20f * MathF.Log10(gain));

    /// <summary>dB → linear gain.</summary>
    public static float ToGain(float db) => MathF.Pow(10f, db / 20f);
}

/// <summary>
/// Meter ballistics on the UI side: instant attack / exponential release, and a peak-hold marker.
/// Feed it with the block peak (or RMS) computed on the audio thread and published through a volatile field.
/// </summary>
public sealed class LevelBallistics
{
    private float _level = float.NegativeInfinity;
    private float _hold = float.NegativeInfinity;
    private double _holdAge;

    /// <summary>Release speed in dB per second.</summary>
    public float ReleaseDbPerSecond { get; set; } = 24f;

    /// <summary>Attack time constant in seconds (0 = instant).</summary>
    public float AttackSeconds { get; set; }

    /// <summary>Peak-hold duration in seconds (0 = no hold).</summary>
    public float HoldSeconds { get; set; } = 1.5f;

    /// <summary>Floor in dB.</summary>
    public float FloorDb { get; set; } = -60f;

    /// <summary>Current displayed level (dB).</summary>
    public float LevelDb => float.IsNegativeInfinity(_level) ? FloorDb : _level;

    /// <summary>Current peak-hold value (dB).</summary>
    public float HoldDb => float.IsNegativeInfinity(_hold) ? FloorDb : _hold;

    /// <summary>Advances by <paramref name="dt"/> seconds with a new input level (dB). Returns <c>true</c> if the display changed.</summary>
    public bool Update(float inputDb, double dt)
    {
        float before = LevelDb, beforeHold = HoldDb;
        inputDb = MathF.Max(FloorDb, inputDb);
        if (float.IsNegativeInfinity(_level)) _level = FloorDb;
        if (inputDb >= _level)
        {
            _level = AttackSeconds <= 0f ? inputDb : _level + (inputDb - _level) * (1f - MathF.Exp((float)(-dt / AttackSeconds)));
        }
        else
        {
            _level = MathF.Max(inputDb, _level - ReleaseDbPerSecond * (float)dt);
        }

        if (inputDb >= _hold || float.IsNegativeInfinity(_hold))
        {
            _hold = inputDb;
            _holdAge = 0;
        }
        else
        {
            _holdAge += dt;
            if (_holdAge > HoldSeconds) _hold = MathF.Max(_level, _hold - ReleaseDbPerSecond * (float)dt);
        }
        return MathF.Abs(before - LevelDb) > 0.01f || MathF.Abs(beforeHold - HoldDb) > 0.01f;
    }

    /// <summary>Clears the level and the hold.</summary>
    public void Reset()
    {
        _level = float.NegativeInfinity;
        _hold = float.NegativeInfinity;
        _holdAge = 0;
    }
}

/// <summary>
/// Thread-safe single value published by the audio thread and read by the UI (meters, LEDs, gain reduction).
/// Stores the maximum since the last read so short peaks are never missed.
/// </summary>
/// <remarks>
/// Magnitudes only (values ≥ 0, e.g. linear peak, RMS, gain reduction in positive dB): the value resets to 0 on
/// each read. A <see cref="PeakValue"/> has a single reader: give each meter its own instance.
/// </remarks>
/// <example>
/// The processor publishes one peak per block and channel; the editor gives the values to a meter:
/// <code>
/// // Shared by the processor and the editor (e.g. through PluginUIProcessor.CreateSharedState).
/// public sealed class Meters
/// {
///     public PeakValue[] Output { get; } = [new PeakValue(), new PeakValue()];
/// }
///
/// // Audio thread, at the end of ProcessMain: lock-free, no allocation.
/// meters.Output[0].PushBlockPeak(left[..sampleCount]);
/// meters.Output[1].PushBlockPeak(right[..sampleCount]);
///
/// // Editor: the meter reads (and resets) the values at each frame.
/// var meter = new LevelMeter { Sources = meters.Output };
/// </code>
/// </example>
public sealed class PeakValue
{
    private int _bits; // float bits of the max since last read

    /// <summary>Audio thread: records a value (keeps the maximum until <see cref="ReadAndReset"/>).</summary>
    public void Push(float value)
    {
        int newBits = BitConverter.SingleToInt32Bits(value);
        int current = Volatile.Read(ref _bits);
        while (BitConverter.Int32BitsToSingle(current) < value)
        {
            int seen = Interlocked.CompareExchange(ref _bits, newBits, current);
            if (seen == current) return;
            current = seen;
        }
    }

    /// <summary>Audio thread: computes (vectorized) and records the absolute peak of a block.</summary>
    public void PushBlockPeak(ReadOnlySpan<float> samples) => Push(VectorOps.MaxAbs(samples));

    /// <summary>UI thread: returns the maximum since the previous call and resets it to 0.</summary>
    public float ReadAndReset() => BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _bits, 0));
}
