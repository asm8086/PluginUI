// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Two-dimensional pad editing X and Y at once (filter cutoff/resonance, vector mixing, panning).
/// Click jumps the handle, drag moves it (Shift = fine, relative), double-click resets both axes.
/// Each axis can be bound to a parameter; both gestures run together.
/// </summary>
public class XYPad : Control
{
    private readonly BoundValue _x;
    private readonly BoundValue _y;
    private Point _last;
    private double _dragX, _dragY;

    /// <summary>Creates a pad centered at (0.5, 0.5).</summary>
    public XYPad()
    {
        _x = new BoundValue(OnAxisChanged, 0.5, 0.5);
        _y = new BoundValue(OnAxisChanged, 0.5, 0.5);
        Cursor = StandardCursor.Cross;
    }

    /// <summary>Horizontal value (normalized, 0 = left).</summary>
    [Category("Value")]
    public double X { get => _x.Value; set => _x.SetByUser(value); }

    /// <summary>Vertical value (normalized, 0 = bottom).</summary>
    [Category("Value")]
    public double Y { get => _y.Value; set => _y.SetByUser(value); }

    /// <summary>Parameter bound to X.</summary>
    [Category("Value")]
    public IParameter? XParameter { get => _x.Parameter; set => _x.Parameter = value; }

    /// <summary>Parameter bound to Y.</summary>
    [Category("Value")]
    public IParameter? YParameter { get => _y.Parameter; set => _y.Parameter = value; }

    /// <summary>Default X (reset).</summary>
    [Category("Value")]
    public double DefaultX { get => _x.DefaultValue; set => _x.DefaultValue = value; }

    /// <summary>Default Y (reset).</summary>
    [Category("Value")]
    public double DefaultY { get => _y.DefaultValue; set => _y.DefaultValue = value; }

    /// <summary>Grid divisions per axis (0 = no grid).</summary>
    [Category("Appearance")]
    public int GridDivisions { get; set; } = 4;

    /// <summary>Draws crosshair lines through the handle.</summary>
    [Category("Appearance")]
    public bool ShowCrosshair { get; set; } = true;

    /// <summary>Handle radius in DIPs.</summary>
    [Category("Layout")]
    public float HandleRadius { get; set; } = 7f;

    /// <summary>Sensitivity multiplier while Shift is held.</summary>
    [Category("Behavior")]
    public float FineFactor { get; set; } = 0.1f;

    /// <summary>Formatter of the X axis value.</summary>
    [Browsable(false)]
    public Func<double, string>? XFormatter { get => _x.Formatter; set => _x.Formatter = value; }

    /// <summary>Formatter of the Y axis value.</summary>
    [Browsable(false)]
    public Func<double, string>? YFormatter { get => _y.Formatter; set => _y.Formatter = value; }

    /// <summary>Raised when X or Y changes.</summary>
    public event Action<XYPad, double, double>? PositionChanged;

    /// <inheritdoc />
    public override string? ValueText => $"{_x.Text} · {_y.Text}";

    /// <inheritdoc />
    protected override Size DefaultSize => new(160f, 190f);

    private void OnAxisChanged(BoundValue axis)
    {
        PositionChanged?.Invoke(this, X, Y);
        InvalidateVisual();
    }

    private Rect PadRect()
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        return widget;
    }

    private Rect Inner(Rect pad) => pad.Inflate(-HandleRadius - 1f);

    /// <summary>Handle center for the current values inside a widget rectangle.</summary>
    public Point HandlePosition(Rect widget)
    {
        var r = Inner(widget);
        return new Point(r.X + r.Width * (float)X, r.Bottom - r.Height * (float)Y);
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.08f) });
        dc.FillRoundedRect(widget, radius, style.Background);
        var inner = Inner(widget);
        if (GridDivisions > 1)
        {
            var g = p.Border.Fade(0.6f);
            for (int i = 1; i < GridDivisions; i++)
            {
                float x = MathF.Round(inner.X + inner.Width * i / GridDivisions) + 0.5f;
                float y = MathF.Round(inner.Y + inner.Height * i / GridDivisions) + 0.5f;
                dc.DrawLine(new Point(x, widget.Y + 1f), new Point(x, widget.Bottom - 1f), g);
                dc.DrawLine(new Point(widget.X + 1f, y), new Point(widget.Right - 1f, y), g);
            }
        }
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame);

        var h = HandlePosition(widget);
        if (ShowCrosshair)
        {
            var cross = style.Accent.Fade(0.45f);
            dc.DrawLine(new Point(widget.X + 1f, h.Y), new Point(widget.Right - 1f, h.Y), cross);
            dc.DrawLine(new Point(h.X, widget.Y + 1f), new Point(h.X, widget.Bottom - 1f), cross);
        }
        float r = HandleRadius;
        dc.FillCircle(h, r + 4f, style.Accent.Fade(IsPressed ? 0.3f : IsMouseOver ? 0.2f : 0.12f));
        if (style.DrawShadow) dc.DrawShadow(Rect.FromCenter(h, r * 2f, r * 2f), r, style.ShadowBlur * 0.5f, new Point(0f, style.ShadowOffset * 0.5f), style.Shadow);
        dc.FillCircle(h, r, style.Accent);
        dc.DrawCircle(h, r - 0.75f, style.Accent.Lighten(0.35f), 1.5f);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        if (e.ClickCount == 2 || e.Control)
        {
            _x.SetByUser(DefaultX);
            _y.SetByUser(DefaultY);
            return;
        }
        IsPressed = true;
        _x.Begin();
        _y.Begin();
        var inner = Inner(PadRect());
        if (!e.Shift && inner.Width > 0f && inner.Height > 0f)
        {
            _x.Set((e.Position.X - inner.X) / inner.Width);
            _y.Set((inner.Bottom - e.Position.Y) / inner.Height);
        }
        _dragX = X;
        _dragY = Y;
        _last = e.Position;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsPressed) return;
        var inner = Inner(PadRect());
        var d = e.Position - _last;
        _last = e.Position;
        float k = e.Shift ? FineFactor : 1f;
        _dragX = Math.Clamp(_dragX + d.X / MathF.Max(10f, inner.Width) * k, 0.0, 1.0);
        _dragY = Math.Clamp(_dragY - d.Y / MathF.Max(10f, inner.Height) * k, 0.0, 1.0);
        _x.Set(_dragX);
        _y.Set(_dragY);
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrag();
    }

    private void EndDrag()
    {
        if (!IsPressed) return;
        IsPressed = false;
        _x.End();
        _y.End();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        double step = e.Shift ? 0.002 : 0.02;
        switch (e.Key)
        {
            case Key.Left: _x.SetByUser(X - step); e.Handled = true; break;
            case Key.Right: _x.SetByUser(X + step); e.Handled = true; break;
            case Key.Up: _y.SetByUser(Y + step); e.Handled = true; break;
            case Key.Down: _y.SetByUser(Y - step); e.Handled = true; break;
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _x.Attach();
        _y.Attach();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        IsPressed = false;
        _x.Detach();
        _y.Detach();
        base.OnDetached();
    }
}
