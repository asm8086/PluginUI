// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// One normalized value of a multi-value control (range slider, XY pad, envelope, multi-slider): either stored
/// locally or bound to an <see cref="IParameter"/>, with the gesture protocol and change notification.
/// Reusable by custom controls that edit several parameters at once.
/// </summary>
/// <remarks>
/// Call <see cref="Attach"/> from <c>OnAttached</c> and <see cref="Detach"/> from <c>OnDetached</c> so the binding
/// follows the host lifetime (no leaked event subscriptions, no gesture left open).
/// </remarks>
public sealed class BoundValue
{
    private readonly Action<BoundValue> _changed;
    private IParameter? _parameter;
    private double _local;
    private double _default;
    private int _localSteps;
    private bool _attached;
    private bool _subscribed;
    private bool _editing;

    /// <summary>Creates a value; <paramref name="changed"/> is called after every change (user, code or parameter).</summary>
    public BoundValue(Action<BoundValue> changed, double initial = 0.0, double defaultValue = 0.0)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _local = Math.Clamp(initial, 0.0, 1.0);
        _default = Math.Clamp(defaultValue, 0.0, 1.0);
    }

    /// <summary>Bound parameter (null = local value).</summary>
    [Category("Value")]
    public IParameter? Parameter
    {
        get => _parameter;
        set
        {
            if (ReferenceEquals(_parameter, value)) return;
            if (_editing) End();
            Unsubscribe();
            _parameter = value;
            if (_attached) Subscribe();
            _changed(this);
        }
    }

    /// <summary>Current normalized value.</summary>
    public double Value => _parameter?.NormalizedValue ?? _local;

    /// <summary>Default normalized value (parameter default when bound and not overridden).</summary>
    [Category("Value")]
    public double DefaultValue { get => _parameter?.DefaultNormalizedValue ?? _default; set => _default = Math.Clamp(value, 0.0, 1.0); }

    /// <summary>Discrete steps (parameter's when bound).</summary>
    [Category("Value")]
    public int StepCount { get => _parameter?.StepCount ?? _localSteps; set => _localSteps = Math.Max(0, value); }

    /// <summary><c>true</c> during a gesture.</summary>
    public bool IsEditing => _editing;

    /// <summary>Optional formatter used by <see cref="Format"/> (normalized → text).</summary>
    [Browsable(false)]
    public Func<double, string>? Formatter { get; set; }

    /// <summary>Rounds to the step grid and clamps.</summary>
    public double Quantize(double normalized)
    {
        normalized = Math.Clamp(normalized, 0.0, 1.0);
        int steps = StepCount;
        return steps > 0 ? Math.Round(normalized * steps) / steps : normalized;
    }

    /// <summary>Text of a normalized value (formatter, parameter text with units, or a percentage).</summary>
    public string Format(double normalized)
    {
        if (Formatter is not null) return Formatter(normalized);
        if (_parameter is not null) return _parameter.ToDisplayStringWithUnits(normalized);
        return (normalized * 100.0).ToString("F0", CultureInfo.InvariantCulture) + " %";
    }

    /// <summary>Text of the current value.</summary>
    public string Text => Format(Value);

    /// <summary>Starts a gesture.</summary>
    public void Begin()
    {
        if (_editing) return;
        _editing = true;
        _parameter?.BeginEdit();
    }

    /// <summary>Changes the value (inside a gesture: performEdit; outside: a complete single-step edit).</summary>
    public void Set(double normalized)
    {
        normalized = Quantize(normalized);
        if (_parameter is not null)
        {
            if (_parameter.NormalizedValue == normalized) return;
            if (_editing) _parameter.PerformEdit(normalized);
            else _parameter.SetValue(normalized);
            if (!_subscribed) _changed(this);
            return;
        }
        if (_local == normalized) return;
        _local = normalized;
        _changed(this);
    }

    /// <summary>Ends the gesture.</summary>
    public void End()
    {
        if (!_editing) return;
        _editing = false;
        _parameter?.EndEdit();
    }

    /// <summary>Complete single-step edit (reset, wheel, keyboard).</summary>
    public void SetByUser(double normalized)
    {
        bool owns = !_editing;
        if (owns) Begin();
        Set(normalized);
        if (owns) End();
    }

    /// <summary>Subscribes to the parameter (call from the control's <c>OnAttached</c>).</summary>
    public void Attach()
    {
        _attached = true;
        Subscribe();
    }

    /// <summary>Ends any gesture and unsubscribes (call from the control's <c>OnDetached</c>).</summary>
    public void Detach()
    {
        End();
        Unsubscribe();
        _attached = false;
    }

    private void Subscribe()
    {
        if (_subscribed || _parameter is null) return;
        _parameter.Changed += OnParameterChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _parameter is null) return;
        _parameter.Changed -= OnParameterChanged;
        _subscribed = false;
    }

    private void OnParameterChanged(IParameter parameter) => _changed(this);
}

/// <summary>Shared helpers of the rotary and linear controls.</summary>
internal static class RotaryLinearHelpers
{
    /// <summary>
    /// Parses a percentage typed by the user (<c>"50"</c>, <c>"50 %"</c>, <c>"12.5%"</c>) into a normalized value
    /// clamped to [0, 1]. The decimal separator is always a point (see <see cref="InvariantNumber"/>).
    /// </summary>
    /// <param name="text">The typed text.</param>
    /// <returns>The normalized value, or <c>null</c> if the text is not a number.</returns>
    public static double? ParsePercent(string text)
    {
        var s = text.Trim().TrimEnd('%');
        if (!InvariantNumber.TryParse(s, out double v)) return null;
        return Math.Clamp(v / 100.0, 0.0, 1.0);
    }

    /// <summary>
    /// Opens an in-place text editor to type a value. The text is parsed by <paramref name="parser"/>, then by the
    /// parameter (<see cref="IParameter.TryParse"/>), then as a percentage for unbound controls. Text that cannot be
    /// parsed keeps the editor open with a red frame.
    /// </summary>
    /// <param name="owner">The control being edited (must be attached to a host).</param>
    /// <param name="area">Area covered by the editor (enlarged to at least 48 × 20 DIP).</param>
    /// <param name="current">Initial text.</param>
    /// <param name="parameter">Bound parameter, or <c>null</c>.</param>
    /// <param name="parser">Custom text → normalized value parser, or <c>null</c>.</param>
    /// <param name="apply">Applies the normalized value (as a user gesture).</param>
    public static void BeginTextEntry(UIElement owner, Rect area, string current, IParameter? parameter, Func<string, double?>? parser, Action<double> apply)
    {
        var host = owner.Host;
        if (host is null) return;
        if (area.Width < 48f) area = Rect.FromCenter(area.Center, 48f, area.Height);
        if (area.Height < 20f) area = Rect.FromCenter(area.Center, area.Width, 20f);
        var style = owner.Theme.Typography.Small with { Align = HorizontalAlignment.Center };
        host.BeginTextEdit(area, current, text =>
        {
            double? value = parser?.Invoke(text);
            if (value is null && parameter is not null && parameter.TryParse(text, out double n)) value = n;
            value ??= parser is null && parameter is null ? ParsePercent(text) : null;
            if (value is not { } v || double.IsNaN(v)) return false;
            apply(Math.Clamp(v, 0.0, 1.0));
            return true;
        }, style: style);
    }

    /// <summary>
    /// Circular arc built from cubic Béziers (≤ 90° each, max error ≈ 0.03 %). Angles in degrees clockwise from
    /// 12 o'clock. Used instead of D2D arc segments, which some renderers (Wine) do not support.
    /// </summary>
    public static PathGeometry BezierArc(Point center, float radius, float startDegrees, float sweepDegrees)
        => PathGeometry.Arc(center, radius, startDegrees, Math.Clamp(sweepDegrees, -360f, 360f));

    /// <summary>Strokes an arc (see <see cref="BezierArc"/>).</summary>
    public static void StrokeArc(DrawingContext dc, Point center, float radius, float startDegrees, float sweepDegrees, Color color, float thickness, StrokeStyle? style = null)
    {
        if (MathF.Abs(sweepDegrees) < 0.05f || color.IsTransparent) return;
        dc.DrawGeometry(BezierArc(center, radius, startDegrees, sweepDegrees), color, thickness, style ?? StrokeStyle.Round);
    }

    /// <summary>Pointer drag accumulator with fine mode that never jumps when Shift is toggled mid-drag.</summary>
    public struct DragAccumulator
    {
        private Point _last;
        public double Value;

        public void Start(Point position, double value)
        {
            _last = position;
            Value = value;
        }

        /// <summary>Adds the movement since the previous call: value += (dx·kx + dy·ky) × factor.</summary>
        public double Move(Point position, float kx, float ky, double factor)
        {
            var delta = position - _last;
            _last = position;
            Value = Math.Clamp(Value + (delta.X * kx + delta.Y * ky) * factor, 0.0, 1.0);
            return Value;
        }
    }
}
