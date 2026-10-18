// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Slider with two thumbs selecting a range (key range, frequency band, loop points). Drag a thumb, or the band
/// between them to move both; Shift = fine; double-click a thumb resets it. Each end can be bound to a parameter.
/// </summary>
public class RangeSlider : Control
{
    private enum Part { None, Low, High, Band }

    private readonly BoundValue _low;
    private readonly BoundValue _high;
    private Orientation _orientation = Orientation.Horizontal;
    private Part _drag;
    private Part _hover;
    private Point _last;
    private double _dragLow, _dragHigh;

    /// <summary>Creates a range slider (0 – 1).</summary>
    public RangeSlider()
    {
        _low = new BoundValue(OnValueChanged, 0.25, 0.0);
        _high = new BoundValue(OnValueChanged, 0.75, 1.0);
    }

    /// <summary>Direction of travel.</summary>
    [Category("Layout")]
    public Orientation Orientation { get => _orientation; set { _orientation = value; InvalidateMeasure(); } }

    /// <summary>Lower end (normalized).</summary>
    [Category("Value")]
    public double Low { get => _low.Value; set => SetRange(value, Math.Max(value + MinimumGap, High), inGesture: false); }

    /// <summary>Upper end (normalized).</summary>
    [Category("Value")]
    public double High { get => _high.Value; set => SetRange(Math.Min(Low, value - MinimumGap), value, inGesture: false); }

    /// <summary>Parameter bound to the lower end.</summary>
    [Category("Value")]
    public IParameter? LowParameter { get => _low.Parameter; set { _low.Parameter = value; if (Label is null && value is not null) Label = value.Name; } }

    /// <summary>Parameter bound to the upper end.</summary>
    [Category("Value")]
    public IParameter? HighParameter { get => _high.Parameter; set => _high.Parameter = value; }

    /// <summary>Minimum distance between the ends (normalized).</summary>
    [Category("Value")]
    public double MinimumGap { get; set; }

    /// <summary>Default of the lower end (reset).</summary>
    [Category("Value")]
    public double DefaultLow { get => _low.DefaultValue; set => _low.DefaultValue = value; }

    /// <summary>Default of the upper end (reset).</summary>
    [Category("Value")]
    public double DefaultHigh { get => _high.DefaultValue; set => _high.DefaultValue = value; }

    /// <summary>Discrete steps of both ends when unbound (0 = continuous).</summary>
    [Category("Value")]
    public int StepCount { get => _low.StepCount; set { _low.StepCount = value; _high.StepCount = value; } }

    /// <summary>Sensitivity multiplier while Shift is held.</summary>
    [Category("Behavior")]
    public float FineFactor { get; set; } = 0.1f;

    /// <summary>Formatter of a normalized end value (default: parameter text, or percent).</summary>
    [Browsable(false)]
    public Func<double, string>? Formatter { get => _low.Formatter; set { _low.Formatter = value; _high.Formatter = value; } }

    /// <summary>Raised when either end changes.</summary>
    public event Action<RangeSlider, double, double>? RangeChanged;

    /// <inheritdoc />
    public override string? ValueText
    {
        get
        {
            string lo = _low.Text, hi = _high.Text;
            // "20 % – 70 %" → "20 – 70 %" when both share the unit.
            int a = lo.LastIndexOf(' '), b = hi.LastIndexOf(' ');
            if (a > 0 && b > 0 && lo[a..] == hi[b..] && !char.IsDigit(lo[^1])) return $"{lo[..a]} – {hi}";
            return $"{lo} – {hi}";
        }
    }

    /// <inheritdoc />
    protected override Size DefaultSize => _orientation == Orientation.Horizontal ? new(220f, 44f) : new(44f, 180f);

    private void OnValueChanged(BoundValue value)
    {
        RangeChanged?.Invoke(this, Low, High);
        InvalidateVisual();
    }

    private void SetRange(double low, double high, bool inGesture)
    {
        double gap = Math.Clamp(MinimumGap, 0.0, 1.0);
        low = Math.Clamp(low, 0.0, 1.0 - gap);
        high = Math.Clamp(high, low + gap, 1.0);
        if (inGesture)
        {
            _low.Set(low);
            _high.Set(high);
        }
        else
        {
            _low.SetByUser(low);
            _high.SetByUser(high);
        }
    }

    // ---------------------------------------------------------------- geometry

    private const float ThumbSize = 14f;

    private (Point A, Point B) Track(Rect widget)
    {
        var c = widget.Center;
        float half = ThumbSize * 0.5f + 1f;
        return _orientation == Orientation.Horizontal
            ? (new Point(widget.X + half, c.Y), new Point(widget.Right - half, c.Y))
            : (new Point(c.X, widget.Bottom - half), new Point(c.X, widget.Y + half));
    }

    private Rect WidgetRect()
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        return widget;
    }

    private Part HitPart(Point p)
    {
        var w = WidgetRect();
        var (a, b) = Track(w);
        var lo = Point.Lerp(a, b, (float)Low);
        var hi = Point.Lerp(a, b, (float)High);
        float dl = (p - lo).Length, dh = (p - hi).Length;
        float grab = ThumbSize * 0.75f;
        if (dl <= grab || dh <= grab) return dl < dh || (dl == dh && (_orientation == Orientation.Horizontal ? p.X < lo.X : p.Y > lo.Y)) ? Part.Low : Part.High;
        bool between = _orientation == Orientation.Horizontal ? p.X > lo.X && p.X < hi.X : p.Y < lo.Y && p.Y > hi.Y;
        return between ? Part.Band : Part.None;
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var (a, b) = Track(widget);
        bool horizontal = _orientation == Orientation.Horizontal;
        float t = MathF.Max(2f, style.TrackThickness);
        var track = horizontal ? Rect.FromEdges(a.X, a.Y - t * 0.5f, b.X, a.Y + t * 0.5f) : Rect.FromEdges(a.X - t * 0.5f, b.Y, a.X + t * 0.5f, a.Y);
        dc.FillRoundedRect(track, t * 0.5f, p.Track);
        var lo = Point.Lerp(a, b, (float)Low);
        var hi = Point.Lerp(a, b, (float)High);
        var band = horizontal ? Rect.FromEdges(lo.X, track.Y, hi.X, track.Bottom) : Rect.FromEdges(track.X, hi.Y, track.Right, lo.Y);
        var accent = _hover == Part.Band || _drag == Part.Band ? style.Accent.Lighten(0.12f) : style.Accent;
        dc.FillRect(band, accent);
        DrawThumb(dc, lo, style, Part.Low);
        DrawThumb(dc, hi, style, Part.High);
    }

    private void DrawThumb(DrawingContext dc, Point c, VectorStyle style, Part part)
    {
        float r = ThumbSize * 0.5f;
        bool hot = _hover == part || _drag == part;
        if (style.DrawShadow) dc.DrawShadow(Rect.FromCenter(c, ThumbSize, ThumbSize), r, style.ShadowBlur * 0.5f, new Point(0f, style.ShadowOffset * 0.5f), style.Shadow);
        dc.FillCircle(c, r, _drag == part ? style.Pressed : style.Fill);
        if (hot) dc.FillCircle(c, r, style.Hover);
        dc.DrawCircle(c, r - 0.5f, hot ? style.Accent : style.Frame);
        dc.FillCircle(c, 2.5f, style.Accent);
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        var part = HitPart(e.Position);
        if (part == Part.None)
        {
            // Click on the track: move the nearest end there.
            var w = WidgetRect();
            var (a, b) = Track(w);
            float len = _orientation == Orientation.Horizontal ? b.X - a.X : a.Y - b.Y;
            double v = len <= 0f ? 0 : Math.Clamp(_orientation == Orientation.Horizontal ? (e.Position.X - a.X) / len : (a.Y - e.Position.Y) / len, 0f, 1f);
            part = Math.Abs(v - Low) <= Math.Abs(v - High) ? Part.Low : Part.High;
            if (part == Part.Low) SetRange(v, High, inGesture: false); else SetRange(Low, v, inGesture: false);
        }
        if (e.ClickCount == 2 && part is Part.Low or Part.High)
        {
            if (part == Part.Low) SetRange(DefaultLow, High, inGesture: false); else SetRange(Low, DefaultHigh, inGesture: false);
            e.Handled = true;
            return;
        }
        _drag = part;
        _last = e.Position;
        _dragLow = Low;
        _dragHigh = High;
        if (part is Part.Low or Part.Band) _low.Begin();
        if (part is Part.High or Part.Band) _high.Begin();
        IsPressed = true;
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == Part.None)
        {
            var hover = IsMouseOver ? HitPart(e.Position) : Part.None;
            if (hover != _hover) { _hover = hover; InvalidateVisual(); }
            Cursor = hover == Part.Band ? StandardCursor.SizeAll : hover == Part.None ? StandardCursor.Default : _orientation == Orientation.Horizontal ? StandardCursor.SizeWE : StandardCursor.SizeNS;
            return;
        }
        var (a, b) = Track(WidgetRect());
        float len = MathF.Max(10f, _orientation == Orientation.Horizontal ? b.X - a.X : a.Y - b.Y);
        var d = e.Position - _last;
        _last = e.Position;
        double delta = (_orientation == Orientation.Horizontal ? d.X : -d.Y) / len * (e.Shift ? FineFactor : 1f);
        double gap = Math.Clamp(MinimumGap, 0.0, 1.0);
        switch (_drag)
        {
            case Part.Low:
                _dragLow = Math.Clamp(_dragLow + delta, 0.0, 1.0);
                _low.Set(Math.Min(_dragLow, High - gap));
                break;
            case Part.High:
                _dragHigh = Math.Clamp(_dragHigh + delta, 0.0, 1.0);
                _high.Set(Math.Max(_dragHigh, Low + gap));
                break;
            case Part.Band:
            {
                double width = _dragHigh - _dragLow;
                _dragLow = Math.Clamp(_dragLow + delta, 0.0, 1.0 - width);
                _dragHigh = _dragLow + width;
                _low.Set(_dragLow);
                _high.Set(_dragHigh);
                break;
            }
        }
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

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = Part.None;
    }

    private void EndDrag()
    {
        if (_drag == Part.None) return;
        _drag = Part.None;
        IsPressed = false;
        _low.End();
        _high.End();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        double step = 0.02 * e.WheelDelta * (e.Shift ? FineFactor : 1f);
        double width = High - Low;
        double low = Math.Clamp(Low + step, 0.0, 1.0 - width);
        SetRange(low, low + width, inGesture: false);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _low.Attach();
        _high.Attach();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        _drag = Part.None;
        _low.Detach();
        _high.Detach();
        base.OnDetached();
    }
}
