// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Momentary rocker (−1 / 0 / +1): pressing one half raises <see cref="Stepped"/> and springs back on release.
/// Bound to a parameter, each step moves it by one discrete step (or <see cref="ContinuousStep"/>).
/// Typical uses: preset increment/decrement, octave shift, fine tuning.
/// </summary>
public class RockerSwitch : Control
{
    private int _state;
    private RepeatTicker? _repeat;
    private IParameter? _parameter;
    private IParameter? _subscribed;

    /// <summary>Creates a rocker switch.</summary>
    public RockerSwitch() { }

    /// <summary>Rocker direction: vertical (up = +1) or horizontal (right = +1).</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Optional parameter incremented / decremented by the rocker.</summary>
    [Category("Value")]
    public IParameter? Parameter
    {
        get => _parameter;
        set
        {
            if (ReferenceEquals(_parameter, value)) return;
            Unsubscribe();
            _parameter = value;
            if (Host is not null) Subscribe();
            InvalidateVisual();
        }
    }

    /// <summary>Normalized increment for continuous parameters.</summary>
    [Category("Value")]
    public double ContinuousStep { get; set; } = 0.01;

    /// <summary>Wraps around at the ends (discrete parameters).</summary>
    [Category("Behavior")]
    public bool Wrap { get; set; }

    /// <summary>Repeats the step while held.</summary>
    [Category("Behavior")]
    public bool RepeatWhileHeld { get; set; } = true;

    /// <summary>Current position: −1, 0 or +1.</summary>
    public int State => _state;

    /// <summary>Raised with +1 or −1 on every step (including auto-repeat).</summary>
    public event Action<RockerSwitch, int>? Stepped;

    /// <inheritdoc />
    protected override Size DefaultSize => Orientation == Orientation.Vertical
        ? new Size(24f, 44f + ControlSizing.LabelAndValueHeight(this))
        : new Size(48f, 22f + ControlSizing.LabelAndValueHeight(this));

    /// <inheritdoc />
    public override string? ValueText => Parameter is null ? null : Parameter.ToDisplayStringWithUnits(Parameter.NormalizedValue);

    /// <inheritdoc />
    protected override SkinState GetSkinState(Rect widgetBounds)
        => new(widgetBounds, (_state + 1) * 0.5, _state + 1, IsMouseOver, IsPressed, IsEffectivelyEnabled, IsFocused);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.4f) });
        bool vertical = Orientation == Orientation.Vertical;
        if (style.DrawShadow) dc.DrawShadow(widget, radius, style.ShadowBlur, new Point(0f, style.ShadowOffset), style.Shadow.Fade(0.6f));
        dc.FillRoundedRect(widget, radius, style.Fill);

        Rect plus = vertical ? widget.TakeTop(widget.Height * 0.5f) : widget.TakeRight(widget.Width * 0.5f);
        Rect minus = vertical ? widget.TakeBottom(widget.Height * 0.5f) : widget.TakeLeft(widget.Width * 0.5f);
        using (dc.Clip(widget))
        {
            // The pressed half sinks (darker, inner shadow), the other one tilts up (highlight).
            if (_state != 0)
            {
                var down = _state > 0 ? plus : minus;
                var up = _state > 0 ? minus : plus;
                dc.FillRect(down, style.Pressed.Darken(0.25f));
                dc.FillRect(up, Paint.Vertical(style.Fill.Lighten(0.1f), style.Fill));
            }
            else
            {
                dc.FillRect(widget, Paint.Vertical(style.Fill.Lighten(0.06f), style.Fill.Darken(0.06f)));
            }
        }
        if (IsMouseOver && IsEffectivelyEnabled) dc.FillRoundedRect(widget, radius, style.Hover);
        if (vertical)
        {
            float y = MathF.Round(widget.Center.Y) + 0.5f;
            dc.DrawLine(new Point(widget.X + 3f, y), new Point(widget.Right - 3f, y), Color.Black.WithAlpha(0.4f));
        }
        else
        {
            float x = MathF.Round(widget.Center.X) + 0.5f;
            dc.DrawLine(new Point(x, widget.Y + 3f), new Point(x, widget.Bottom - 3f), Color.Black.WithAlpha(0.4f));
        }
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame, style.FrameThickness);

        var fg = IsEffectivelyEnabled ? p.Text : p.TextDisabled;
        float g = MathF.Min(10f, MathF.Min(plus.Width, plus.Height) * 0.55f);
        if (vertical)
        {
            Glyphs.Chevron(dc, plus.Center, g, _state > 0 ? style.Accent : fg, up: true);
            Glyphs.Chevron(dc, minus.Center, g, _state < 0 ? style.Accent : fg);
        }
        else
        {
            Glyphs.ChevronSide(dc, plus.Center, g, _state > 0 ? style.Accent : fg, right: true);
            Glyphs.ChevronSide(dc, minus.Center, g, _state < 0 ? style.Accent : fg, right: false);
        }
    }

    private int DirectionAt(Point point)
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        return Orientation == Orientation.Vertical
            ? (point.Y < widget.Center.Y ? 1 : -1)
            : (point.X >= widget.Center.X ? 1 : -1);
    }

    /// <summary>Performs one step in a direction (+1 / −1), as if the user pressed the rocker.</summary>
    public void Step(int direction)
    {
        direction = Math.Sign(direction);
        if (direction == 0) return;
        if (Parameter is { } param)
        {
            double delta = param.StepCount > 0 ? 1.0 / param.StepCount : ContinuousStep;
            double v = param.NormalizedValue + delta * direction;
            if (Wrap && param.StepCount > 0)
            {
                if (v > 1.0 + 1e-9) v = 0.0;
                else if (v < -1e-9) v = 1.0;
            }
            param.SetValue(Math.Clamp(v, 0.0, 1.0));
        }
        Stepped?.Invoke(this, direction);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        IsPressed = true;
        _state = DirectionAt(e.Position);
        Step(_state);
        if (RepeatWhileHeld)
        {
            _repeat ??= new RepeatTicker(() => { if (_state != 0) Step(_state); });
            _repeat.Start(Host);
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Release();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        Release();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        Release();
        Unsubscribe();
        base.OnDetached();
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed is not null || _parameter is null) return;
        _parameter.Changed += OnParameterChanged;
        _subscribed = _parameter;
    }

    private void Unsubscribe()
    {
        if (_subscribed is null) return;
        _subscribed.Changed -= OnParameterChanged;
        _subscribed = null;
    }

    private void OnParameterChanged(IParameter parameter) => InvalidateVisual();

    private void Release()
    {
        _repeat?.Stop(Host);
        if (_state == 0 && !IsPressed) return;
        _state = 0;
        IsPressed = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        switch (e.Key)
        {
            case Key.Up or Key.Right: Step(1); e.Handled = true; break;
            case Key.Down or Key.Left: Step(-1); e.Handled = true; break;
        }
    }
}
