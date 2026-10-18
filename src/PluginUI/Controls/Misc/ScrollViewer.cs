// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Scrollbar visibility policy of a <see cref="ScrollViewer"/>.</summary>
public enum ScrollBarVisibility
{
    /// <summary>No scrolling along this axis: the content is constrained to the viewport.</summary>
    Disabled,
    /// <summary>Scrollable; the overlay bar appears while hovering when the content overflows.</summary>
    Auto,
    /// <summary>Scrollable; the overlay bar stays visible when the content overflows.</summary>
    Visible,
}

/// <summary>
/// Scrolls a content larger than its viewport. The content is measured with an unbounded size along the scrollable
/// axes and arranged at an offset; slim overlay scrollbars (drag, track click) fade in while hovering.
/// Wheel scrolls vertically (Shift+wheel or tilt: horizontally); unused wheel events bubble to outer scrollers.
/// </summary>
/// <remarks>
/// Set the scrolled element through <see cref="Content"/> (do not add children directly).
/// Embedded native windows (<see cref="NativeWindowHost"/>) are not clipped by the viewport.
/// </remarks>
public class ScrollViewer : Panel
{
    private readonly ScrollBarsOverlay _overlay;
    private UIElement? _content;
    private float _offsetX, _offsetY;
    private Size _extent;
    private Rect _viewport;
    private float _barAlpha;
    private Animation? _fade;

    /// <summary>Creates a scroll viewer.</summary>
    public ScrollViewer()
    {
        ClipToBounds = true;
        _overlay = new ScrollBarsOverlay(this);
        Children.Add(_overlay);
    }

    /// <summary>Creates a scroll viewer around <paramref name="content"/>.</summary>
    public ScrollViewer(UIElement content) : this() => Content = content;

    /// <summary>Scrolled element.</summary>
    [Browsable(false)]
    public UIElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;
            if (_content is not null) Children.Remove(_content);
            _content = value;
            if (value is not null) Children.Insert(0, value);
            _offsetX = _offsetY = 0f;
        }
    }

    /// <summary>Vertical scrolling policy.</summary>
    [Category("Behavior")]
    public ScrollBarVisibility VerticalScrollBar { get; set; } = ScrollBarVisibility.Auto;

    /// <summary>Horizontal scrolling policy.</summary>
    [Category("Behavior")]
    public ScrollBarVisibility HorizontalScrollBar { get; set; } = ScrollBarVisibility.Disabled;

    /// <summary>DIPs scrolled per wheel notch.</summary>
    [Category("Behavior")]
    public float WheelStep { get; set; } = 48f;

    /// <summary>Horizontal offset in DIPs.</summary>
    public float OffsetX => _offsetX;

    /// <summary>Vertical offset in DIPs.</summary>
    public float OffsetY => _offsetY;

    /// <summary>Size of the content (as last measured).</summary>
    public Size ExtentSize => _extent;

    /// <summary>Size of the visible area.</summary>
    public Size ViewportSize => _viewport.Size;

    /// <summary>Maximum horizontal offset.</summary>
    public float MaxOffsetX => HorizontalScrollBar == ScrollBarVisibility.Disabled ? 0f : MathF.Max(0f, _extent.Width - _viewport.Width);

    /// <summary>Maximum vertical offset.</summary>
    public float MaxOffsetY => VerticalScrollBar == ScrollBarVisibility.Disabled ? 0f : MathF.Max(0f, _extent.Height - _viewport.Height);

    /// <summary>Raised when the offset changes.</summary>
    public event Action<ScrollViewer>? ScrollChanged;

    /// <summary>Scrolls to an offset (clamped).</summary>
    public void ScrollTo(float offsetX, float offsetY)
    {
        offsetX = Math.Clamp(offsetX, 0f, MaxOffsetX);
        offsetY = Math.Clamp(offsetY, 0f, MaxOffsetY);
        if (offsetX == _offsetX && offsetY == _offsetY) return;
        _offsetX = offsetX;
        _offsetY = offsetY;
        ArrangeContent();
        ScrollChanged?.Invoke(this);
        InvalidateVisual();
    }

    /// <summary>Scrolls by a delta.</summary>
    public void ScrollBy(float dx, float dy) => ScrollTo(_offsetX + dx, _offsetY + dy);

    /// <summary>Scrolls the minimum amount that makes <paramref name="element"/> (a descendant) fully visible.</summary>
    public void ScrollIntoView(UIElement element)
    {
        if (_content is null || !_content.IsAncestorOf(element)) return;
        var r = element.Bounds;
        var v = _viewport;
        float dx = 0f, dy = 0f;
        if (r.Right > v.Right) dx = r.Right - v.Right;
        if (r.X + -dx < v.X) dx = r.X - v.X;
        if (r.Bottom > v.Bottom) dy = r.Bottom - v.Bottom;
        if (r.Y - dy < v.Y) dy = r.Y - v.Y;
        ScrollBy(dx, dy);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float innerW = MathF.Max(0f, available.Width - Padding.Horizontal);
        float innerH = MathF.Max(0f, available.Height - Padding.Vertical);
        if (_content is not null)
        {
            _content.Measure(new Size(
                HorizontalScrollBar == ScrollBarVisibility.Disabled ? innerW : float.PositiveInfinity,
                VerticalScrollBar == ScrollBarVisibility.Disabled ? innerH : float.PositiveInfinity));
            _extent = _content.DesiredSize;
        }
        else
        {
            _extent = Size.Zero;
        }
        _overlay.Measure(available);
        return new Size(MathF.Min(_extent.Width, innerW) + Padding.Horizontal, MathF.Min(_extent.Height, innerH) + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        _viewport = bounds.Deflate(Padding);
        _offsetX = Math.Clamp(_offsetX, 0f, MaxOffsetX);
        _offsetY = Math.Clamp(_offsetY, 0f, MaxOffsetY);
        ArrangeContent();
        _overlay.Arrange(_viewport);
    }

    private void ArrangeContent()
    {
        if (_content is null) return;
        float w = HorizontalScrollBar == ScrollBarVisibility.Disabled ? _viewport.Width : MathF.Max(_extent.Width, _viewport.Width);
        float h = VerticalScrollBar == ScrollBarVisibility.Disabled ? _viewport.Height : MathF.Max(_extent.Height, _viewport.Height);
        _content.Arrange(new Rect(_viewport.X - _offsetX, _viewport.Y - _offsetY, w, h));
        _overlay.UpdateTracks();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled) return;
        float x = _offsetX, y = _offsetY;
        bool horizontal = e.IsHorizontalWheel || (e.Shift && MaxOffsetX > 0f) || (MaxOffsetY <= 0f && MaxOffsetX > 0f);
        if (horizontal) ScrollBy(e.IsHorizontalWheel ? e.WheelDelta * WheelStep : -e.WheelDelta * WheelStep, 0f);
        else ScrollBy(0f, -e.WheelDelta * WheelStep);
        // Unchanged (at the end): let an outer scroller take it.
        if (x != _offsetX || y != _offsetY) e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.PageDown: ScrollBy(0f, _viewport.Height * 0.9f); e.Handled = true; break;
            case Key.PageUp: ScrollBy(0f, -_viewport.Height * 0.9f); e.Handled = true; break;
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseEnter()
    {
        base.OnMouseEnter();
        FadeBars(1f);
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (!_overlay.IsDragging) FadeBars(0f);
    }

    private void FadeBars(float target)
    {
        _fade?.Cancel();
        if (Host is null) { _barAlpha = target; return; }
        _fade = Host.Animator.Animate(_barAlpha, target, target > _barAlpha ? 0.15 : 0.6, v => { _barAlpha = v; InvalidateVisual(); }, Easing.QuadOut);
    }

    private float AlphaFor(ScrollBarVisibility visibility) => visibility == ScrollBarVisibility.Visible ? 1f : _barAlpha;

    /// <summary>Child drawn above the content: the two overlay bars and their input.</summary>
    private sealed class ScrollBarsOverlay : UIElement
    {
        private const float Thickness = 10f;
        private readonly ScrollViewer _owner;
        private readonly SlimScrollBar _v = new(Orientation.Vertical);
        private readonly SlimScrollBar _h = new(Orientation.Horizontal);
        private SlimScrollBar? _dragging;

        public ScrollBarsOverlay(ScrollViewer owner) => _owner = owner;

        public bool IsDragging => _dragging is not null;

        public void UpdateTracks()
        {
            var v = _owner._viewport;
            _v.Extent = _owner.VerticalScrollBar == ScrollBarVisibility.Disabled ? 0f : _owner._extent.Height;
            _v.Viewport = v.Height;
            _v.Offset = _owner._offsetY;
            _h.Extent = _owner.HorizontalScrollBar == ScrollBarVisibility.Disabled ? 0f : _owner._extent.Width;
            _h.Viewport = v.Width;
            _h.Offset = _owner._offsetX;
            float hGap = _h.IsNeeded ? Thickness : 0f, vGap = _v.IsNeeded ? Thickness : 0f;
            _v.Track = new Rect(v.Right - Thickness - 1f, v.Y + 2f, Thickness, MathF.Max(0f, v.Height - 4f - hGap));
            _h.Track = new Rect(v.X + 2f, v.Bottom - Thickness - 1f, MathF.Max(0f, v.Width - 4f - vGap), Thickness);
        }

        protected override void ArrangeOverride(Rect bounds) => UpdateTracks();

        public override bool HitTest(Point point) => _v.HitTest(point) || _h.HitTest(point);

        protected override void OnRender(DrawingContext dc)
        {
            var p = Theme.Palette;
            _v.Render(dc, p, _owner.AlphaFor(_owner.VerticalScrollBar));
            _h.Render(dc, p, _owner.AlphaFor(_owner.HorizontalScrollBar));
        }

        protected internal override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging is not null)
            {
                float offset = _dragging.Drag(e.Position);
                if (_dragging == _v) _owner.ScrollTo(_owner._offsetX, offset);
                else _owner.ScrollTo(offset, _owner._offsetY);
                return;
            }
            bool v = _v.HitTest(e.Position), h = !v && _h.HitTest(e.Position);
            if (v != _v.IsHovered || h != _h.IsHovered)
            {
                _v.IsHovered = v;
                _h.IsHovered = h;
                InvalidateVisual();
            }
        }

        protected internal override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _v.IsHovered = _h.IsHovered = false;
        }

        protected internal override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left) return;
            var bar = _v.HitTest(e.Position) ? _v : _h.HitTest(e.Position) ? _h : null;
            if (bar is null) return;
            _dragging = bar;
            float offset = bar.Press(e.Position);
            if (bar == _v) _owner.ScrollTo(_owner._offsetX, offset);
            else _owner.ScrollTo(offset, _owner._offsetY);
            e.Handled = true;
        }

        protected internal override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndDrag();
        }

        protected internal override void OnCaptureLost()
        {
            base.OnCaptureLost();
            EndDrag();
        }

        private void EndDrag()
        {
            _dragging?.Release();
            _dragging = null;
            if (!_owner.IsMouseOver) _owner.FadeBars(0f);
            InvalidateVisual();
        }
    }
}
