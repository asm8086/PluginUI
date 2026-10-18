// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Endless rotary encoder: turns without limits and reports steps through <see cref="Turned"/> (preset browsing,
/// list scrolling, fine tuning). Optionally drives a parameter by <see cref="StepSize"/> per step, with wrap-around.
/// </summary>
public class EndlessKnob : Control
{
    private float _angle;
    private float _pixelAccumulator;
    private Point _last;
    private IParameter? _parameter;
    private bool _subscribed;
    private bool _editing;

    /// <summary>Creates an encoder.</summary>
    public EndlessKnob()
    {
        Cursor = StandardCursor.SizeNS;
    }

    /// <summary>Raised for every step turned (positive = clockwise / up).</summary>
    public event Action<EndlessKnob, int>? Turned;

    /// <summary>Steps per full visual revolution.</summary>
    [Category("Data")]
    public int StepsPerRevolution { get; set; } = 24;

    /// <summary>Mouse travel (DIPs) for one step.</summary>
    [Category("Behavior")]
    public float PixelsPerStep { get; set; } = 8f;

    /// <summary>Number of grip ridges drawn on the body.</summary>
    [Category("Appearance")]
    public int RidgeCount { get; set; } = 16;

    /// <summary>Optional parameter changed by <see cref="StepSize"/> per step.</summary>
    [Category("Value")]
    public IParameter? Parameter
    {
        get => _parameter;
        set
        {
            if (ReferenceEquals(_parameter, value)) return;
            EndEdit();
            Unsubscribe();
            _parameter = value;
            if (Host is not null) Subscribe();
            if (Label is null && value is not null) Label = value.Name;
            InvalidateVisual();
        }
    }

    /// <summary>Normalized change per step (0 = one parameter step, or 1 %).</summary>
    [Category("Value")]
    public double StepSize { get; set; }

    /// <summary>Wraps from 1 to 0 (and back) instead of stopping at the ends.</summary>
    [Category("Behavior")]
    public bool Wrap { get; set; }

    /// <summary>Current visual angle in degrees.</summary>
    public float Angle => _angle;

    /// <inheritdoc />
    public override string? ValueText => _parameter?.ToDisplayStringWithUnits(_parameter.NormalizedValue);

    /// <inheritdoc />
    protected override Size DefaultSize => new(56f, 76f);

    /// <summary>Applies <paramref name="steps"/> as if turned by the user.</summary>
    public void Turn(int steps)
    {
        if (steps == 0) return;
        _angle = (_angle + steps * 360f / Math.Max(1, StepsPerRevolution)) % 360f;
        if (_parameter is not null)
        {
            double step = StepSize > 0 ? StepSize : _parameter.StepCount > 0 ? 1.0 / _parameter.StepCount : 0.01;
            double v = _parameter.NormalizedValue + steps * step;
            if (Wrap)
            {
                v %= 1.0 + (_parameter.StepCount > 0 ? step : 0.0);
                if (v < 0) v += 1.0 + (_parameter.StepCount > 0 ? step : 0.0);
            }
            v = Math.Clamp(v, 0.0, 1.0);
            if (_parameter.StepCount > 0) v = Math.Round(v * _parameter.StepCount) / _parameter.StepCount;
            if (_editing) _parameter.PerformEdit(v);
            else _parameter.SetValue(v);
        }
        Turned?.Invoke(this, steps);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var square = widget.CenteredSquare();
        float r = square.Width * 0.5f - 2f;
        if (r < 4f) return;
        var c = square.Center;
        if (style.DrawShadow) dc.DrawShadow(Rect.FromCenter(c, r * 2f, r * 2f), r, style.ShadowBlur, new Point(0f, style.ShadowOffset), style.Shadow);
        var fill = IsPressed ? style.Pressed : style.Fill;
        dc.FillCircle(c, r, Paint.Vertical(fill.Lighten(0.07f), fill.Darken(0.07f)));
        if (IsMouseOver || IsPressed) dc.FillCircle(c, r, style.Hover);

        // Grip ridges rotate with the angle.
        var ridge = style.Frame.Lighten(0.12f);
        for (int i = 0; i < RidgeCount; i++)
        {
            float a = _angle + 360f * i / RidgeCount;
            dc.DrawLine(PathGeometry.PointOnCircle(c, r * 0.80f, a), PathGeometry.PointOnCircle(c, r * 0.96f, a), ridge, MathF.Max(1f, r * 0.05f), StrokeStyle.Round);
        }
        if (style.DrawFrame) dc.DrawCircle(c, r - style.FrameThickness * 0.5f, style.Frame, style.FrameThickness);
        dc.FillCircle(c, r * 0.62f, fill.Darken(0.04f));
        dc.DrawCircle(c, r * 0.62f, style.Frame.Fade(0.6f), 1f);
        dc.FillCircle(PathGeometry.PointOnCircle(c, r * 0.45f, _angle), MathF.Max(1.8f, r * 0.09f), style.Accent);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        IsPressed = true;
        _last = e.Position;
        _pixelAccumulator = 0f;
        if (_parameter is not null && !_editing)
        {
            _editing = true;
            _parameter.BeginEdit();
        }
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsPressed) return;
        var d = e.Position - _last;
        _last = e.Position;
        _pixelAccumulator += (d.X - d.Y) * (e.Shift ? 0.25f : 1f);
        float pps = MathF.Max(1f, PixelsPerStep);
        int steps = (int)(_pixelAccumulator / pps);
        if (steps != 0)
        {
            _pixelAccumulator -= steps * pps;
            Turn(steps);
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsPressed) return;
        IsPressed = false;
        EndEdit();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        IsPressed = false;
        EndEdit();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        Turn(Math.Sign(e.WheelDelta));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Up or Key.Right: Turn(1); e.Handled = true; break;
            case Key.Down or Key.Left: Turn(-1); e.Handled = true; break;
            case Key.PageUp: Turn(10); e.Handled = true; break;
            case Key.PageDown: Turn(-10); e.Handled = true; break;
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Subscribe();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        EndEdit();
        Unsubscribe();
        base.OnDetached();
    }

    private void EndEdit()
    {
        if (!_editing) return;
        _editing = false;
        _parameter?.EndEdit();
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

    private void OnParameterChanged(IParameter parameter) => InvalidateVisual();
}
