// 2026/11/05 22:35:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// A view of another <see cref="IParameter"/> through a remapping of its normalized value, optionally driving
/// other parameters in the same gesture. The host still sees the inner parameter only: automation, presets and undo
/// are unchanged.
/// </summary>
/// <remarks>
/// <para>Uses: a knob that covers only part of a parameter's range, or a range with a hole (two ranges end to end);
/// a control that keeps two parameters linked (the followers receive a value computed from the edited one, inside
/// the same begin/perform/end gesture).</para>
/// <para><see cref="FromInner"/> must be the inverse of <see cref="ToInner"/> on the reachable values; values of the
/// inner parameter outside the mapped range are shown clamped.</para>
/// </remarks>
/// <example>
/// <code>
/// // Feedback knob over −0.999 … −0.5 and 0.5 … 0.999 of a −1 … 1 parameter.
/// var fb = MappedParameter.Ranges(feedback, (-0.999, -0.5), (0.5, 0.999));
/// // Blend knob that keeps feedback = −blend.
/// var blend = new MappedParameter(blendParameter).Link(feedback, n => 1.0 - n);
/// </code>
/// </example>
public sealed class MappedParameter : IParameter
{
    private readonly List<(IParameter Target, Func<double, double> Map)> _links = [];
    private Action<IParameter>? _changed;
    private bool _subscribed;

    /// <summary>Wraps a parameter.</summary>
    /// <param name="inner">The parameter actually edited.</param>
    /// <param name="toInner">Outer normalized → inner normalized (identity when null).</param>
    /// <param name="fromInner">Inner normalized → outer normalized (identity when null).</param>
    public MappedParameter(IParameter inner, Func<double, double>? toInner = null, Func<double, double>? fromInner = null)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ToInner = toInner ?? (n => n);
        FromInner = fromInner ?? (n => n);
    }

    /// <summary>
    /// A parameter covering several plain-value ranges of <paramref name="inner"/> end to end: the outer travel is
    /// split evenly between them (in the inner normalized scale), and the gaps between the ranges are never reached.
    /// </summary>
    /// <param name="inner">The parameter actually edited.</param>
    /// <param name="ranges">Plain ranges, in increasing order.</param>
    /// <returns>The mapped parameter.</returns>
    /// <exception cref="ArgumentException">No range, or ranges out of order.</exception>
    public static MappedParameter Ranges(IParameter inner, params (double Min, double Max)[] ranges)
    {
        if (ranges is null || ranges.Length == 0) throw new ArgumentException("At least one range is needed.", nameof(ranges));
        var lo = new double[ranges.Length];
        var hi = new double[ranges.Length];
        for (int i = 0; i < ranges.Length; i++)
        {
            lo[i] = inner.ToNormalized(ranges[i].Min);
            hi[i] = inner.ToNormalized(ranges[i].Max);
            if (!(hi[i] >= lo[i]) || (i > 0 && lo[i] < hi[i - 1]))
                throw new ArgumentException("The ranges must be increasing and must not overlap.", nameof(ranges));
        }
        int count = ranges.Length;
        double ToInnerMap(double n)
        {
            double t = Math.Clamp(n, 0.0, 1.0) * count;
            int k = Math.Min((int)t, count - 1);
            return lo[k] + (t - k) * (hi[k] - lo[k]);
        }
        double FromInnerMap(double v)
        {
            // The range containing v, or the nearest one (gaps snap to the closer end).
            int best = 0;
            double bestDistance = double.PositiveInfinity;
            for (int k = 0; k < count; k++)
            {
                double distance = v < lo[k] ? lo[k] - v : v > hi[k] ? v - hi[k] : 0.0;
                if (distance < bestDistance) { bestDistance = distance; best = k; }
            }
            double width = hi[best] - lo[best];
            double local = width <= 0.0 ? 0.0 : Math.Clamp((v - lo[best]) / width, 0.0, 1.0);
            return (best + local) / count;
        }
        return new MappedParameter(inner, ToInnerMap, FromInnerMap);
    }

    /// <summary>The parameter actually edited.</summary>
    public IParameter Inner { get; }

    /// <summary>Outer normalized → inner normalized.</summary>
    public Func<double, double> ToInner { get; }

    /// <summary>Inner normalized → outer normalized.</summary>
    public Func<double, double> FromInner { get; }

    /// <summary>Name shown instead of the inner one (null = inner name).</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Adds a follower: each edit also sets <paramref name="target"/> to <paramref name="map"/>(inner normalized
    /// value), in the same gesture.
    /// </summary>
    /// <param name="target">Parameter kept linked.</param>
    /// <param name="map">Inner normalized value of this parameter → normalized value of the follower.</param>
    /// <returns>This instance.</returns>
    public MappedParameter Link(IParameter target, Func<double, double> map)
    {
        _links.Add((target ?? throw new ArgumentNullException(nameof(target)), map ?? throw new ArgumentNullException(nameof(map))));
        return this;
    }

    /// <inheritdoc />
    public string Name => DisplayName ?? Inner.Name;

    /// <inheritdoc />
    public string Units => Inner.Units;

    /// <inheritdoc />
    public int StepCount => Inner.StepCount;

    /// <inheritdoc />
    public double DefaultNormalizedValue => Clamp01(FromInner(Inner.DefaultNormalizedValue));

    /// <inheritdoc />
    public double NormalizedValue => Clamp01(FromInner(Inner.NormalizedValue));

    /// <inheritdoc />
    public bool IsEditing => Inner.IsEditing;

    /// <inheritdoc />
    public string ToDisplayString(double normalized) => Inner.ToDisplayString(ToInner(Clamp01(normalized)));

    /// <inheritdoc />
    public bool TryParse(string text, out double normalized)
    {
        if (Inner.TryParse(text, out double inner))
        {
            normalized = Clamp01(FromInner(inner));
            return true;
        }
        normalized = NormalizedValue;
        return false;
    }

    /// <inheritdoc />
    public double ToPlain(double normalized) => Inner.ToPlain(ToInner(Clamp01(normalized)));

    /// <inheritdoc />
    public double ToNormalized(double plain) => Clamp01(FromInner(Inner.ToNormalized(plain)));

    /// <inheritdoc />
    /// <remarks>Raised when the inner parameter changes.</remarks>
    public event Action<IParameter>? Changed
    {
        add
        {
            _changed += value;
            if (!_subscribed)
            {
                Inner.Changed += OnInnerChanged;
                _subscribed = true;
            }
        }
        remove
        {
            _changed -= value;
            if (_changed is null && _subscribed)
            {
                Inner.Changed -= OnInnerChanged;
                _subscribed = false;
            }
        }
    }

    private void OnInnerChanged(IParameter _) => _changed?.Invoke(this);

    /// <inheritdoc />
    public void BeginEdit()
    {
        Inner.BeginEdit();
        foreach (var (target, _) in _links) target.BeginEdit();
    }

    /// <inheritdoc />
    public void PerformEdit(double normalized)
    {
        double inner = Clamp01(ToInner(Clamp01(normalized)));
        Inner.PerformEdit(inner);
        foreach (var (target, map) in _links) target.PerformEdit(Clamp01(map(inner)));
    }

    /// <inheritdoc />
    public void EndEdit()
    {
        foreach (var (target, _) in _links) target.EndEdit();
        Inner.EndEdit();
    }

    private static double Clamp01(double v) => double.IsNaN(v) ? 0.0 : Math.Clamp(v, 0.0, 1.0);
}
