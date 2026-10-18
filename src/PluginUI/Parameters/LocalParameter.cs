// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI;

/// <summary>
/// Mapping between a normalized value [0, 1] and a plain value.
/// </summary>
public abstract class ParameterScale
{
    /// <summary>Normalized → plain.</summary>
    public abstract double ToPlain(double normalized);

    /// <summary>Plain → normalized.</summary>
    public abstract double ToNormalized(double plain);

    /// <summary>Linear mapping.</summary>
    public static ParameterScale Linear(double min, double max) => new LinearScale(min, max);

    /// <summary>Logarithmic mapping (frequencies, times): equal travel = equal ratio. Requires 0 &lt; min &lt; max.</summary>
    public static ParameterScale Logarithmic(double min, double max) => new LogScale(min, max);

    /// <summary>Power-law mapping (exponent &gt; 1 gives more resolution near <paramref name="min"/>).</summary>
    public static ParameterScale Power(double min, double max, double exponent) => new PowerScale(min, max, exponent);

    private sealed class LinearScale(double min, double max) : ParameterScale
    {
        public override double ToPlain(double n) => min + Math.Clamp(n, 0.0, 1.0) * (max - min);
        public override double ToNormalized(double p) => max == min ? 0.0 : Math.Clamp((p - min) / (max - min), 0.0, 1.0);
    }

    private sealed class LogScale : ParameterScale
    {
        private readonly double _min, _max, _logMin, _logRatio;

        public LogScale(double min, double max)
        {
            if (min <= 0.0 || max <= min) throw new ArgumentException("Logarithmic scale requires 0 < min < max.");
            _min = min; _max = max; _logMin = Math.Log(min); _logRatio = Math.Log(max / min);
        }

        public override double ToPlain(double n) => Math.Exp(_logMin + Math.Clamp(n, 0.0, 1.0) * _logRatio);
        public override double ToNormalized(double p) => (Math.Log(Math.Clamp(p, _min, _max)) - _logMin) / _logRatio;
    }

    private sealed class PowerScale(double min, double max, double exponent) : ParameterScale
    {
        public override double ToPlain(double n) => min + Math.Pow(Math.Clamp(n, 0.0, 1.0), exponent) * (max - min);
        public override double ToNormalized(double p) => max == min ? 0.0 : Math.Pow(Math.Clamp((p - min) / (max - min), 0.0, 1.0), 1.0 / exponent);
    }
}

/// <summary>
/// Self-contained <see cref="IParameter"/> for values that are not plug-in parameters (UI state, standalone
/// applications, the gallery, tests).
/// </summary>
/// <example>
/// <code>
/// var gain = new LocalParameter("Gain", -24, 24, 0, "dB", decimals: 1);
/// var cutoff = new LocalParameter("Cutoff", 20, 20000, 1000, "Hz", ParameterScale.Logarithmic(20, 20000), decimals: 0);
/// var mode = new LocalParameter("Mode", 0, 2, 0, stepCount: 2, decimals: 0);
/// cutoff.Value = 2400;                                  // plain value
/// string text = cutoff.ToDisplayString(cutoff.NormalizedValue); // "2400" (the unit is in cutoff.Units)
/// </code>
/// </example>
public sealed class LocalParameter : IParameter
{
    private readonly ParameterScale _scale;
    private readonly string[]? _items;
    private double _value;
    private int _editDepth;

    /// <summary>Creates a continuous (or stepped) parameter.</summary>
    /// <param name="name">Display name.</param>
    /// <param name="min">Minimum plain value.</param>
    /// <param name="max">Maximum plain value.</param>
    /// <param name="defaultValue">Default plain value.</param>
    /// <param name="units">Unit label.</param>
    /// <param name="scale">Mapping (linear if null).</param>
    /// <param name="stepCount">0 = continuous; otherwise number of steps.</param>
    /// <param name="decimals">Decimals of the default formatter.</param>
    public LocalParameter(string name, double min = 0.0, double max = 1.0, double defaultValue = 0.0, string units = "",
        ParameterScale? scale = null, int stepCount = 0, int decimals = 2)
    {
        Name = name;
        Units = units;
        _scale = scale ?? ParameterScale.Linear(min, max);
        StepCount = Math.Max(0, stepCount);
        Decimals = decimals;
        DefaultNormalizedValue = Quantize(_scale.ToNormalized(defaultValue));
        _value = DefaultNormalizedValue;
    }

    private LocalParameter(string name, string[] items, int defaultIndex)
    {
        Name = name;
        Units = string.Empty;
        _items = items;
        _scale = ParameterScale.Linear(0, Math.Max(1, items.Length - 1));
        StepCount = Math.Max(1, items.Length - 1);
        DefaultNormalizedValue = items.Length <= 1 ? 0.0 : Math.Clamp(defaultIndex, 0, items.Length - 1) / (double)(items.Length - 1);
        _value = DefaultNormalizedValue;
    }

    /// <summary>Discrete parameter whose states are labeled by <paramref name="items"/>.</summary>
    public static LocalParameter FromList(string name, IReadOnlyList<string> items, int defaultIndex = 0)
        => new(name, items.ToArray(), defaultIndex);

    /// <summary>On/off parameter.</summary>
    public static LocalParameter Toggle(string name, bool defaultOn = false) => new(name, ["Off", "On"], defaultOn ? 1 : 0);

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Units { get; }

    /// <inheritdoc />
    public int StepCount { get; }

    /// <summary>Decimals of the default formatter.</summary>
    public int Decimals { get; set; }

    /// <summary>Custom formatter (plain value → text).</summary>
    public Func<double, string>? Formatter { get; set; }

    /// <summary>State labels for list parameters.</summary>
    public IReadOnlyList<string>? Items => _items;

    /// <inheritdoc />
    public double DefaultNormalizedValue { get; }

    /// <inheritdoc />
    public double NormalizedValue
    {
        get => _value;
        set => SetNormalized(value);
    }

    /// <summary>Current plain value.</summary>
    public double Value
    {
        get => _scale.ToPlain(_value);
        set => SetNormalized(_scale.ToNormalized(value));
    }

    /// <summary>Selected index for list/stepped parameters.</summary>
    public int SelectedIndex
    {
        get => StepCount <= 0 ? 0 : (int)Math.Round(_value * StepCount);
        set => SetNormalized(StepCount <= 0 ? 0.0 : Math.Clamp(value, 0, StepCount) / (double)StepCount);
    }

    /// <inheritdoc />
    public bool IsEditing => _editDepth > 0;

    /// <inheritdoc />
    public event Action<IParameter>? Changed;

    /// <summary>Raised by <see cref="BeginEdit"/>.</summary>
    public event Action<LocalParameter>? EditStarted;

    /// <summary>Raised by <see cref="EndEdit"/>.</summary>
    public event Action<LocalParameter>? EditEnded;

    /// <inheritdoc />
    public void BeginEdit()
    {
        _editDepth++;
        EditStarted?.Invoke(this);
    }

    /// <inheritdoc />
    public void PerformEdit(double normalized) => SetNormalized(normalized);

    /// <inheritdoc />
    public void EndEdit()
    {
        if (_editDepth > 0) _editDepth--;
        EditEnded?.Invoke(this);
    }

    /// <inheritdoc />
    public double ToPlain(double normalized) => _scale.ToPlain(Quantize(normalized));

    /// <inheritdoc />
    public double ToNormalized(double plain) => Quantize(_scale.ToNormalized(plain));

    /// <inheritdoc />
    public string ToDisplayString(double normalized)
    {
        if (_items is not null) return _items[Math.Clamp((int)Math.Round(Quantize(normalized) * StepCount), 0, _items.Length - 1)];
        double plain = ToPlain(normalized);
        if (Formatter is not null) return Formatter(plain);
        return StepCount > 0 && Decimals == 0
            ? InvariantNumber.Format(Math.Round(plain))
            : InvariantNumber.Format(plain, Decimals);
    }

    /// <inheritdoc />
    public bool TryParse(string text, out double normalized)
    {
        normalized = _value;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (_items is not null)
        {
            int index = Array.FindIndex(_items, i => string.Equals(i, text, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            normalized = StepCount == 0 ? 0.0 : index / (double)StepCount;
            return true;
        }
        if (!string.IsNullOrEmpty(Units) && text.EndsWith(Units, StringComparison.OrdinalIgnoreCase)) text = text[..^Units.Length].Trim();
        double multiplier = 1.0;
        if (text.EndsWith('k') || text.EndsWith('K')) { multiplier = 1000.0; text = text[..^1]; }
        // Always a decimal point, whatever the regional settings (see InvariantNumber).
        if (!InvariantNumber.TryParse(text, out double plain)) return false;
        normalized = ToNormalized(plain * multiplier);
        return true;
    }

    private double Quantize(double normalized)
    {
        normalized = Math.Clamp(normalized, 0.0, 1.0);
        return StepCount > 0 ? Math.Round(normalized * StepCount) / StepCount : normalized;
    }

    private void SetNormalized(double value)
    {
        value = Quantize(value);
        if (value == _value) return;
        _value = value;
        Changed?.Invoke(this);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} = {this.ToDisplayStringWithUnits(_value)}";
}
