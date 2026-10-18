// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using System.Globalization;
using NPlug;

namespace PluginUI.DemoSynth.Plugin;

/// <summary>
/// Continuous parameter with a logarithmic mapping between the normalized value [0, 1] and the plain range [min, max].
/// Suited for frequencies and quality factors, where equal knob travel should mean equal ratios.
/// </summary>
public sealed class LogRangeParameter : AudioParameter
{
    private readonly double _logMin;
    private readonly double _logRatio;

    /// <summary>
    /// Creates a new logarithmic parameter.
    /// </summary>
    /// <param name="title">Title displayed by the host.</param>
    /// <param name="units">Units displayed by the host.</param>
    /// <param name="minValue">Minimum plain value (must be &gt; 0).</param>
    /// <param name="maxValue">Maximum plain value (must be &gt; <paramref name="minValue"/>).</param>
    /// <param name="defaultPlainValue">Default plain value.</param>
    /// <param name="decimals">Number of decimals used when converting to a string.</param>
    public LogRangeParameter(string title, string units, double minValue, double maxValue, double defaultPlainValue, int decimals = 2)
        : base(title, units, defaultNormalizedValue: Normalize(minValue, maxValue, defaultPlainValue))
    {
        if (minValue <= 0.0 || maxValue <= minValue)
            throw new ArgumentException($"Invalid range [{minValue}, {maxValue}] for a logarithmic parameter.");

        MinValue = minValue;
        MaxValue = maxValue;
        Decimals = decimals;
        _logMin = Math.Log(minValue);
        _logRatio = Math.Log(maxValue / minValue);
        NormalizedValue = DefaultNormalizedValue;
    }

    /// <summary>Minimum plain value.</summary>
    public double MinValue { get; }

    /// <summary>Maximum plain value.</summary>
    public double MaxValue { get; }

    /// <summary>Number of decimals used by <see cref="ToString(double)"/>.</summary>
    public int Decimals { get; }

    /// <summary>
    /// Gets the current plain value (allocation-free, safe for the audio thread).
    /// </summary>
    public double Value => ToPlain(NormalizedValue);

    /// <inheritdoc />
    public override double ToPlain(double normalizedValue)
        => Math.Exp(_logMin + Math.Clamp(normalizedValue, 0.0, 1.0) * _logRatio);

    /// <inheritdoc />
    public override double ToNormalized(double plainValue)
        => (Math.Log(Math.Clamp(plainValue, MinValue, MaxValue)) - _logMin) / _logRatio;

    /// <inheritdoc />
    public override string ToString(double valueNormalized)
        => ToPlain(valueNormalized).ToString("F" + Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override double FromString(string plainValueAsString)
    {
        return double.TryParse(plainValueAsString, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? ToNormalized(value)
            : NormalizedValue;
    }

    private static double Normalize(double minValue, double maxValue, double plainValue)
    {
        if (minValue <= 0.0 || maxValue <= minValue) return 0.0;
        double clamped = Math.Clamp(plainValue, minValue, maxValue);
        return Math.Log(clamped / minValue) / Math.Log(maxValue / minValue);
    }
}
