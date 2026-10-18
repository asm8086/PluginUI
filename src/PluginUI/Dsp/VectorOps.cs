// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PluginUI.Dsp;

/// <summary>
/// SIMD kernels shared by the display DSP, written with <see cref="Vector{T}"/> so that the width follows the target:
/// 8 floats / 4 doubles with AVX2 (JIT on a recent CPU, or NativeAOT with <c>IlcInstructionSet=x86-64-v3</c>),
/// 4 floats / 2 doubles with SSE (default NativeAOT baseline). No allocation: usable on the audio thread.
/// </summary>
/// <remarks>
/// Reductions (<see cref="Max"/>, <see cref="MaxAbs"/>, <see cref="MinMax"/>) handle the remainder with one
/// overlapping vector load instead of a scalar tail (the result is the same since max/min are idempotent).
/// </remarks>
internal static class VectorOps
{
    private static bool UseVectors(int length) => Vector.IsHardwareAccelerated && length >= Vector<float>.Count;

    /// <summary>Largest absolute value (0 for an empty span).</summary>
    public static float MaxAbs(ReadOnlySpan<float> x)
    {
        if (!UseVectors(x.Length))
        {
            float peak = 0f;
            foreach (float s in x) peak = MathF.Max(peak, MathF.Abs(s));
            return peak;
        }

        ref float r = ref MemoryMarshal.GetReference(x);
        int width = Vector<float>.Count;
        int last = x.Length - width;
        // Two accumulators hide the latency of the max instruction.
        var acc0 = Vector<float>.Zero;
        var acc1 = Vector<float>.Zero;
        int i = 0;
        for (; i <= last - width; i += 2 * width)
        {
            acc0 = Vector.Max(acc0, Vector.Abs(Vector.LoadUnsafe(ref r, (nuint)i)));
            acc1 = Vector.Max(acc1, Vector.Abs(Vector.LoadUnsafe(ref r, (nuint)(i + width))));
        }
        for (; i < last; i += width) acc0 = Vector.Max(acc0, Vector.Abs(Vector.LoadUnsafe(ref r, (nuint)i)));
        acc0 = Vector.Max(acc0, Vector.Abs(Vector.LoadUnsafe(ref r, (nuint)last)));
        return HorizontalMax(Vector.Max(acc0, acc1));
    }

    /// <summary>Largest value (<see cref="float.NegativeInfinity"/> for an empty span).</summary>
    public static float Max(ReadOnlySpan<float> x)
    {
        if (!UseVectors(x.Length))
        {
            float max = float.NegativeInfinity;
            foreach (float s in x) max = MathF.Max(max, s);
            return max;
        }

        ref float r = ref MemoryMarshal.GetReference(x);
        int width = Vector<float>.Count;
        int last = x.Length - width;
        var acc = Vector.LoadUnsafe(ref r, (nuint)last);
        for (int i = 0; i < last; i += width) acc = Vector.Max(acc, Vector.LoadUnsafe(ref r, (nuint)i));
        return HorizontalMax(acc);
    }

    /// <summary>Smallest and largest values (<c>(+∞, −∞)</c> for an empty span).</summary>
    public static (float Min, float Max) MinMax(ReadOnlySpan<float> x)
    {
        if (!UseVectors(x.Length))
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            foreach (float s in x)
            {
                if (s < lo) lo = s;
                if (s > hi) hi = s;
            }
            return (lo, hi);
        }

        ref float r = ref MemoryMarshal.GetReference(x);
        int width = Vector<float>.Count;
        int last = x.Length - width;
        var vmin = Vector.LoadUnsafe(ref r, (nuint)last);
        var vmax = vmin;
        for (int i = 0; i < last; i += width)
        {
            var v = Vector.LoadUnsafe(ref r, (nuint)i);
            vmin = Vector.Min(vmin, v);
            vmax = Vector.Max(vmax, v);
        }
        float min = vmin[0], max = vmax[0];
        for (int k = 1; k < width; k++)
        {
            min = MathF.Min(min, vmin[k]);
            max = MathF.Max(max, vmax[k]);
        }
        return (min, max);
    }

    /// <summary><c>destination[i] = (left[i] + right[i]) / 2</c> (stereo to mono). The spans must have the same length.</summary>
    public static void Average(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> destination)
    {
        int n = destination.Length;
        if (left.Length != n || right.Length != n) throw new ArgumentException("The spans must have the same length.");
        int i = 0;
        if (UseVectors(n))
        {
            ref float l = ref MemoryMarshal.GetReference(left);
            ref float rr = ref MemoryMarshal.GetReference(right);
            ref float d = ref MemoryMarshal.GetReference(destination);
            var half = new Vector<float>(0.5f);
            for (int last = n - Vector<float>.Count; i <= last; i += Vector<float>.Count)
            {
                ((Vector.LoadUnsafe(ref l, (nuint)i) + Vector.LoadUnsafe(ref rr, (nuint)i)) * half).StoreUnsafe(ref d, (nuint)i);
            }
        }
        for (; i < n; i++) destination[i] = 0.5f * (left[i] + right[i]);
    }

    /// <summary>
    /// In place: power (linear, ≥ 0) → decibels, <c>max(floorDb, 10·log10(p))</c>. Zero, negative or denormal-small
    /// powers give <paramref name="floorDb"/>.
    /// </summary>
    public static void PowerToDecibels(Span<float> values, float floorDb)
    {
        const float TenOverLn10 = 4.342944819032518f; // 10 / ln(10)
        int n = values.Length;
        int i = 0;
        if (UseVectors(n))
        {
            ref float r = ref MemoryMarshal.GetReference(values);
            var k = new Vector<float>(TenOverLn10);
            var floor = new Vector<float>(floorDb);
            var tiny = new Vector<float>(float.Epsilon);
            for (int last = n - Vector<float>.Count; i <= last; i += Vector<float>.Count)
            {
                // max(p, tiny) avoids log(0) = −∞ and log(negative) = NaN; the floor then applies.
                var p = Vector.Max(Vector.LoadUnsafe(ref r, (nuint)i), tiny);
                Vector.Max(floor, Vector.Log(p) * k).StoreUnsafe(ref r, (nuint)i);
            }
        }
        for (; i < n; i++) values[i] = MathF.Max(floorDb, TenOverLn10 * MathF.Log(MathF.Max(values[i], float.Epsilon)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HorizontalMax(Vector<float> v)
    {
        float max = v[0];
        for (int k = 1; k < Vector<float>.Count; k++) max = MathF.Max(max, v[k]);
        return max;
    }
}
