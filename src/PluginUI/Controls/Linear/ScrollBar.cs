// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Scroll bar over a content range: <see cref="Value"/> is the first visible position in
/// [<see cref="Minimum"/>, <see cref="Maximum"/> − <see cref="ViewportSize"/>]. Thumb drag, track paging with
/// auto-repeat, optional arrow buttons, mouse wheel. Used by scroll viewers, lists and tables.
/// </summary>
public class ScrollBar : Control
{
    private enum Part { None, Thumb, TrackBefore, TrackAfter, ArrowBefore, ArrowAfter }

    private Orientation _orientation;
    private double _minimum;
    private double _maximum = 100.0;
    private double _viewport = 10.0;
    private double _value;
    private double? _smallChange;
    private double? _largeChange;
    private Part _pressed;
    private Part _hover;
    private float _grabOffset;
    private Repeater? _repeater;

    /// <summary>Creates a vertical scroll bar.</summary>
    public ScrollBar() : this(Orientation.Vertical) { }

    /// <summary>Creates a scroll bar.</summary>
    public ScrollBar(Orientation orientation)
    {
        _orientation = orientation;
        Focusable = false;
    }

    /// <summary>Direction.</summary>
    [Category("Layout")]
    public Orientation Orientation { get => _orientation; set { _orientation = value; InvalidateMeasure(); } }

    /// <summary>Start of the content range.</summary>
    [Category("Value")]
    public double Minimum { get => _minimum; set { _minimum = value; Coerce(); } }

    /// <summary>End of the content range (total content extent).</summary>
    [Category("Value")]
    public double Maximum { get => _maximum; set { _maximum = value; Coerce(); } }

    /// <summary>Visible extent (thumb size).</summary>
    [Category("Value")]
    public double ViewportSize { get => _viewport; set { _viewport = Math.Max(0.0, value); Coerce(); } }

    /// <summary>First visible position.</summary>
    [Category("Value")]
    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Clamp(value, _minimum, MaxValue);
            if (v == _value) return;
            _value = v;
            InvalidateVisual();
        }
    }

    /// <summary>Arrow / wheel step (default 10 % of the viewport).</summary>
    [Category("Value")]
    public double SmallChange { get => _smallChange ?? Math.Max(1e-9, _viewport * 0.1); set => _smallChange = value; }

    /// <summary>Page step (default: the viewport).</summary>
    [Category("Value")]
    public double LargeChange { get => _largeChange ?? Math.Max(1e-9, _viewport); set => _largeChange = value; }

    /// <summary>Shows arrow buttons at both ends.</summary>
    [Category("Appearance")]
    public bool ShowArrows { get; set; }

    /// <summary>Draws nothing (and ignores input) when everything fits in the viewport.</summary>
    [Category("Behavior")]
    public bool AutoHide { get; set; } = true;

    /// <summary>Largest valid <see cref="Value"/>.</summary>
    public double MaxValue => Math.Max(_minimum, _maximum - _viewport);

    /// <summary><c>true</c> when the content is larger than the viewport.</summary>
    public bool IsScrollable => _maximum - _minimum > _viewport + 1e-9;

    /// <summary>Raised when the user scrolls (new value).</summary>
    public event Action<ScrollBar, double>? Scrolled;

    /// <inheritdoc />
    protected override Size DefaultSize => _orientation == Orientation.Vertical ? new(12f, 100f) : new(100f, 12f);

    /// <summary>Scrolls by a delta as the user would (raises <see cref="Scrolled"/>).</summary>
    public void ScrollBy(double delta) => ScrollTo(_value + delta);

    /// <summary>Scrolls to a position as the user would (raises <see cref="Scrolled"/>).</summary>
    public void ScrollTo(double value)
    {
        double before = _value;
        Value = value;
        if (_value != before) Scrolled?.Invoke(this, _value);
    }

    private void Coerce()
    {
        _value = Math.Clamp(_value, _minimum, MaxValue);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- geometry

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    private bool Vertical => _orientation == Orientation.Vertical;

    private float ArrowLength => ShowArrows ? (Vertical ? Bounds.Width : Bounds.Height) : 0f;

    private Rect TrackRect
    {
        get
        {
            var r = Bounds;
            float a = ArrowLength;
            return Vertical ? Rect.FromEdges(r.X, r.Y + a, r.Right, r.Bottom - a) : Rect.FromEdges(r.X + a, r.Y, r.Right - a, r.Bottom);
        }
    }

    private Rect ThumbRect
    {
        get
        {
            var t = TrackRect;
            double extent = Math.Max(1e-9, _maximum - _minimum);
            float length = Vertical ? t.Height : t.Width;
            float thumb = MathF.Max(MathF.Min(18f, length), (float)(length * Math.Min(1.0, _viewport / extent)));
            double range = MaxValue - _minimum;
            float pos = range <= 0 ? 0f : (float)((_value - _minimum) / range) * (length - thumb);
            return Vertical ? new Rect(t.X, t.Y + pos, t.Width, thumb) : new Rect(t.X + pos, t.Y, thumb, t.Height);
        }
    }

    private Part HitPart(Point p)
    {
        if (!Bounds.Contains(p)) return Part.None;
        var t = TrackRect;
        if (ShowArrows)
        {
            if (Vertical ? p.Y < t.Y : p.X < t.X) return Part.ArrowBefore;
            if (Vertical ? p.Y >= t.Bottom : p.X >= t.Right) return Part.ArrowAfter;
        }
        var th = ThumbRect;
        if (th.Contains(p)) return Part.Thumb;
        return (Vertical ? p.Y < th.Y : p.X < th.X) ? Part.TrackBefore : Part.TrackAfter;
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        if (AutoHide && !IsScrollable) return;
        var p = Theme.Palette;
        var t = TrackRect;
        float cross = Vertical ? t.Width : t.Height;
        bool hot = IsMouseOver || _pressed != Part.None;
        // Thin when idle, full width when hovered (modern overlay style).
        float inset = hot ? cross * 0.15f : cross * 0.3f;
        var track = Vertical ? t.Inflate(-inset, 0f) : t.Inflate(0f, -inset);
        float r = (Vertical ? track.Width : track.Height) * 0.5f;
        if (hot || ShowArrows) dc.FillRoundedRect(track, r, p.Track.Fade(hot ? 0.6f : 0.35f));
        var th = ThumbRect;
        th = Vertical ? th.Inflate(-inset, -1f) : th.Inflate(-1f, -inset);
        var color = _pressed == Part.Thumb ? p.TextDim : _hover == Part.Thumb ? p.TextDim.Fade(0.85f) : p.TextDim.Fade(hot ? 0.6f : 0.45f);
        dc.FillRoundedRect(th, r, color);

        if (ShowArrows)
        {
            DrawArrow(dc, Vertical ? new Rect(widget.X, widget.Y, widget.Width, ArrowLength) : new Rect(widget.X, widget.Y, ArrowLength, widget.Height), before: true, _hover == Part.ArrowBefore || _pressed == Part.ArrowBefore);
            DrawArrow(dc, Vertical ? new Rect(widget.X, widget.Bottom - ArrowLength, widget.Width, ArrowLength) : new Rect(widget.Right - ArrowLength, widget.Y, ArrowLength, widget.Height), before: false, _hover == Part.ArrowAfter || _pressed == Part.ArrowAfter);
        }
    }

    private void DrawArrow(DrawingContext dc, Rect r, bool before, bool hot)
    {
        var c = r.Center;
        float s = MathF.Min(r.Width, r.Height) * 0.22f;
        var color = hot ? Theme.Palette.Text : Theme.Palette.TextDim;
        ReadOnlySpan<Point> tri = Vertical
            ? before ? [new(c.X - s, c.Y + s * 0.5f), new(c.X + s, c.Y + s * 0.5f), new(c.X, c.Y - s * 0.6f)]
                     : [new(c.X - s, c.Y - s * 0.5f), new(c.X + s, c.Y - s * 0.5f), new(c.X, c.Y + s * 0.6f)]
            : before ? [new(c.X + s * 0.5f, c.Y - s), new(c.X + s * 0.5f, c.Y + s), new(c.X - s * 0.6f, c.Y)]
                     : [new(c.X - s * 0.5f, c.Y - s), new(c.X - s * 0.5f, c.Y + s), new(c.X + s * 0.6f, c.Y)];
        dc.FillPolygon(tri, color);
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled || (AutoHide && !IsScrollable)) return;
        e.Handled = true;
        _pressed = HitPart(e.Position);
        switch (_pressed)
        {
            case Part.Thumb:
                var th = ThumbRect;
                _grabOffset = Vertical ? e.Position.Y - th.Y : e.Position.X - th.X;
                break;
            case Part.TrackBefore or Part.TrackAfter or Part.ArrowBefore or Part.ArrowAfter:
                Step();
                _repeater ??= new Repeater(this);
                _repeater.Start();
                Host?.AddTicker(_repeater);
                break;
        }
        IsPressed = true;
        InvalidateVisual();
    }

    private void Step()
    {
        switch (_pressed)
        {
            case Part.ArrowBefore: ScrollBy(-SmallChange); break;
            case Part.ArrowAfter: ScrollBy(SmallChange); break;
            case Part.TrackBefore or Part.TrackAfter:
            {
                // Stop paging once the thumb reaches the pointer.
                var th = ThumbRect;
                var mouse = Host?.MousePosition ?? th.Center;
                bool before = Vertical ? mouse.Y < th.Y : mouse.X < th.X;
                bool after = Vertical ? mouse.Y >= th.Bottom : mouse.X >= th.Right;
                if (_pressed == Part.TrackBefore && before) ScrollBy(-LargeChange);
                if (_pressed == Part.TrackAfter && after) ScrollBy(LargeChange);
                break;
            }
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressed == Part.Thumb)
        {
            var t = TrackRect;
            var th = ThumbRect;
            float length = (Vertical ? t.Height : t.Width) - (Vertical ? th.Height : th.Width);
            if (length <= 0f) return;
            float pos = (Vertical ? e.Position.Y - t.Y : e.Position.X - t.X) - _grabOffset;
            ScrollTo(_minimum + Math.Clamp(pos / length, 0f, 1f) * (MaxValue - _minimum));
            return;
        }
        var hover = _pressed == Part.None && IsMouseOver ? HitPart(e.Position) : Part.None;
        if (hover != _hover) { _hover = hover; InvalidateVisual(); }
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
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = Part.None;
    }

    private void Release()
    {
        if (_pressed == Part.None) return;
        _pressed = Part.None;
        IsPressed = false;
        if (_repeater is not null) Host?.RemoveTicker(_repeater);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsScrollable) return;
        double before = _value;
        ScrollBy(-e.WheelDelta * SmallChange * 3.0);
        e.Handled = _value != before;
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        Release();
        base.OnDetached();
    }

    private sealed class Repeater(ScrollBar owner) : ITickable
    {
        private double _elapsed;
        private double _next;

        public void Start()
        {
            _elapsed = 0;
            _next = 0.35;
        }

        public bool Tick(double elapsedSeconds)
        {
            if (owner._pressed is Part.None or Part.Thumb) return false;
            _elapsed += elapsedSeconds;
            while (_elapsed >= _next)
            {
                owner.Step();
                _next += 0.05;
            }
            return true;
        }
    }
}
