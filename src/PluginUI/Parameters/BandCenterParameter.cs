// 2026/11/06 02:49:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI;

/// <summary>
/// The centre frequency of a band given by two edge parameters, as a parameter of its own: a knob bound to it shows
/// the centre and moves both edges together, keeping their ratio. Only the two edges exist for the host.
/// </summary>
/// <remarks>
/// <para>The normalized scale is logarithmic between <see cref="MinFrequency"/> and <see cref="MaxFrequency"/>. The
/// edge ratio is read when an edit starts and kept for the whole gesture, so that touching a limit of the edges'
/// range does not change it for good. The edges are clamped to their own parameter ranges (the band then slides
/// along the limit with its ratio).</para>
/// </remarks>
/// <example>
/// <code>
/// var center = new BandCenterParameter(Parameters[model.Low], Parameters[model.High], BandCenters.Bilinear(() => SampleRate));
/// var knob = new Knob(center) { Label = "CENTER" };
/// </code>
/// </example>
public sealed class BandCenterParameter : IParameter
{
    private readonly double _logMin, _logSpan;
    private Action<IParameter>? _changed;
    private bool _subscribed;
    private double _gestureRatio = double.NaN;
    private int _editDepth;

    /// <summary>Creates the centre parameter.</summary>
    /// <param name="low">Lower-edge parameter (plain value in Hz).</param>
    /// <param name="high">Upper-edge parameter (plain value in Hz).</param>
    /// <param name="center">Centre rule (<see cref="BandCenters.Geometric"/> when null).</param>
    /// <param name="minFrequency">Lowest centre of the scale in Hz (default: the lower edge's minimum).</param>
    /// <param name="maxFrequency">Highest centre of the scale in Hz (default: the upper edge's maximum).</param>
    public BandCenterParameter(IParameter low, IParameter high, IBandCenter? center = null, double? minFrequency = null, double? maxFrequency = null)
    {
        Low = low ?? throw new ArgumentNullException(nameof(low));
        High = high ?? throw new ArgumentNullException(nameof(high));
        Centers = center ?? BandCenters.Geometric;
        MinFrequency = Math.Max(1e-6, minFrequency ?? low.ToPlain(0.0));
        MaxFrequency = Math.Max(MinFrequency * 1.000001, maxFrequency ?? high.ToPlain(1.0));
        _logMin = Math.Log(MinFrequency);
        _logSpan = Math.Log(MaxFrequency) - _logMin;
    }

    /// <summary>Lower-edge parameter.</summary>
    public IParameter Low { get; }

    /// <summary>Upper-edge parameter.</summary>
    public IParameter High { get; }

    /// <summary>Centre rule.</summary>
    public IBandCenter Centers { get; }

    /// <summary>Lowest centre of the scale in Hz.</summary>
    public double MinFrequency { get; }

    /// <summary>Highest centre of the scale in Hz.</summary>
    public double MaxFrequency { get; }

    /// <summary>Decimals of the display string below 1 kHz (above, 2 decimals up to 10 kHz, then 1).</summary>
    public int Decimals { get; set; }

    /// <summary>Centre of the current edges in Hz.</summary>
    public double Frequency => Centers.Center(Low.ToPlain(Low.NormalizedValue), High.ToPlain(High.NormalizedValue));

    /// <inheritdoc />
    public string Name { get; set; } = "Center";

    /// <inheritdoc />
    /// <remarks>Empty: the display string carries its unit, Hz or kHz ("250 Hz", "12.65 kHz").</remarks>
    public string Units => string.Empty;

    /// <inheritdoc />
    public int StepCount => 0;

    /// <inheritdoc />
    public double DefaultNormalizedValue => ToNormalized(Centers.Center(Low.ToPlain(Low.DefaultNormalizedValue), High.ToPlain(High.DefaultNormalizedValue)));

    /// <inheritdoc />
    public double NormalizedValue => ToNormalized(Frequency);

    /// <inheritdoc />
    public bool IsEditing => _editDepth > 0;

    /// <inheritdoc />
    public string ToDisplayString(double normalized)
    {
        double hz = ToPlain(normalized);
        if (hz >= 1000.0) return InvariantNumber.Format(hz / 1000.0, hz >= 10000.0 ? 1 : 2) + " kHz";
        return InvariantNumber.Format(hz, Math.Clamp(Decimals, 0, 6)) + " Hz";
    }

    /// <inheritdoc />
    public bool TryParse(string text, out double normalized)
    {
        string s = (text ?? string.Empty).Trim();
        double scale = 1.0;
        if (s.EndsWith("khz", StringComparison.OrdinalIgnoreCase)) { scale = 1000.0; s = s[..^3]; }
        else if (s.EndsWith("hz", StringComparison.OrdinalIgnoreCase)) s = s[..^2];
        else if (s.EndsWith('k') || s.EndsWith('K')) { scale = 1000.0; s = s[..^1]; }
        if (double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double hz) && hz > 0.0)
        {
            normalized = ToNormalized(hz * scale);
            return true;
        }
        normalized = NormalizedValue;
        return false;
    }

    /// <inheritdoc />
    public double ToPlain(double normalized) => Math.Exp(_logMin + Math.Clamp(normalized, 0.0, 1.0) * _logSpan);

    /// <inheritdoc />
    public double ToNormalized(double plain) => plain > 0.0 && double.IsFinite(plain) ? Math.Clamp((Math.Log(plain) - _logMin) / _logSpan, 0.0, 1.0) : 0.0;

    /// <inheritdoc />
    /// <remarks>Raised when either edge changes.</remarks>
    public event Action<IParameter>? Changed
    {
        add
        {
            _changed += value;
            if (_subscribed) return;
            Low.Changed += OnEdgeChanged;
            High.Changed += OnEdgeChanged;
            _subscribed = true;
        }
        remove
        {
            _changed -= value;
            if (_changed is not null || !_subscribed) return;
            Low.Changed -= OnEdgeChanged;
            High.Changed -= OnEdgeChanged;
            _subscribed = false;
        }
    }

    private void OnEdgeChanged(IParameter _) => _changed?.Invoke(this);

    /// <inheritdoc />
    public void BeginEdit()
    {
        if (_editDepth++ == 0) _gestureRatio = CurrentRatio();
        Low.BeginEdit();
        High.BeginEdit();
    }

    /// <inheritdoc />
    public void PerformEdit(double normalized)
    {
        double ratio = double.IsNaN(_gestureRatio) ? CurrentRatio() : _gestureRatio;
        var (low, high) = Centers.Edges(ToPlain(normalized), ratio);
        double minLow = Low.ToPlain(0.0), maxHigh = High.ToPlain(1.0);
        if (low < minLow) { low = minLow; high = low * ratio; }
        if (high > maxHigh) { high = maxHigh; low = Math.Max(minLow, high / ratio); }
        // Lower edge first when the band moves down, upper edge first when it moves up: the edges never cross.
        if (low < Low.ToPlain(Low.NormalizedValue))
        {
            Low.PerformEdit(Low.ToNormalized(low));
            High.PerformEdit(High.ToNormalized(high));
        }
        else
        {
            High.PerformEdit(High.ToNormalized(high));
            Low.PerformEdit(Low.ToNormalized(low));
        }
    }

    /// <inheritdoc />
    public void EndEdit()
    {
        High.EndEdit();
        Low.EndEdit();
        if (_editDepth > 0 && --_editDepth == 0) _gestureRatio = double.NaN;
    }

    private double CurrentRatio()
    {
        double low = Low.ToPlain(Low.NormalizedValue), high = High.ToPlain(High.NormalizedValue);
        return low > 0.0 && high > low ? high / low : 1.0;
    }
}
