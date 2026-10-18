// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Two panes separated by a draggable splitter. <see cref="Orientation.Horizontal"/> places the panes side by side
/// (vertical splitter), <see cref="Orientation.Vertical"/> stacks them. Double-click the splitter to restore
/// <see cref="DefaultRatio"/>.
/// </summary>
/// <remarks>Set the panes through <see cref="First"/> and <see cref="Second"/> (do not add children directly).</remarks>
public class SplitView : Panel
{
    private readonly SplitterThumb _thumb;
    private UIElement? _first, _second;
    private Orientation _orientation = Orientation.Horizontal;
    private float _ratio = 0.5f;

    /// <summary>Creates a split view.</summary>
    public SplitView()
    {
        _thumb = new SplitterThumb(this);
        Children.Add(_thumb);
    }

    /// <summary>Creates a split view with two panes.</summary>
    public SplitView(UIElement first, UIElement second, Orientation orientation = Orientation.Horizontal) : this()
    {
        Orientation = orientation;
        First = first;
        Second = second;
    }

    /// <summary>Left (or top) pane.</summary>
    [Category("Value")]
    public UIElement? First
    {
        get => _first;
        set
        {
            if (ReferenceEquals(_first, value)) return;
            if (_first is not null) Children.Remove(_first);
            _first = value;
            if (value is not null) Children.Insert(0, value);
        }
    }

    /// <summary>Right (or bottom) pane.</summary>
    [Category("Value")]
    public UIElement? Second
    {
        get => _second;
        set
        {
            if (ReferenceEquals(_second, value)) return;
            if (_second is not null) Children.Remove(_second);
            _second = value;
            if (value is not null) Children.Insert(_first is null ? 0 : 1, value);
        }
    }

    /// <summary>Panes side by side (<see cref="Orientation.Horizontal"/>) or stacked.</summary>
    [Category("Layout")]
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            _thumb.Cursor = value == Orientation.Horizontal ? StandardCursor.SizeWE : StandardCursor.SizeNS;
            InvalidateMeasure();
        }
    }

    /// <summary>Share of the space given to the first pane (0..1).</summary>
    [Category("Layout")]
    public float Ratio
    {
        get => _ratio;
        set
        {
            value = Math.Clamp(value, 0f, 1f);
            if (value == _ratio) return;
            _ratio = value;
            RatioChanged?.Invoke(this, value);
            InvalidateMeasure();
        }
    }

    /// <summary>Ratio restored by a double-click on the splitter.</summary>
    [Category("Layout")]
    public float DefaultRatio { get; set; } = 0.5f;

    /// <summary>Minimum size of the first pane in DIPs.</summary>
    [Category("Layout")]
    public float MinFirst { get; set; } = 40f;

    /// <summary>Minimum size of the second pane in DIPs.</summary>
    [Category("Layout")]
    public float MinSecond { get; set; } = 40f;

    /// <summary>Thickness of the splitter (grab zone) in DIPs.</summary>
    [Category("Layout")]
    public float SplitterThickness { get; set; } = 6f;

    /// <summary>Raised when the ratio changes.</summary>
    public event Action<SplitView, float>? RatioChanged;

    private bool Horizontal => _orientation == Orientation.Horizontal;

    private float Available(Rect inner) => MathF.Max(0f, (Horizontal ? inner.Width : inner.Height) - SplitterThickness);

    private float FirstLength(float available)
    {
        if (!float.IsFinite(available)) return MinFirst;
        float max = MathF.Max(MinFirst, available - MinSecond);
        return Math.Clamp(available * _ratio, MathF.Min(MinFirst, available), MathF.Min(max, available));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var inner = new Size(MathF.Max(0f, available.Width - Padding.Horizontal), MathF.Max(0f, available.Height - Padding.Vertical));
        float avail = MathF.Max(0f, (Horizontal ? inner.Width : inner.Height) - SplitterThickness);
        float firstSize = FirstLength(avail);
        float secondSize = MathF.Max(0f, avail - firstSize);
        Size Slot(float main) => Horizontal ? new Size(main, inner.Height) : new Size(inner.Width, main);
        _first?.Measure(Slot(firstSize));
        _second?.Measure(Slot(secondSize));
        _thumb.Measure(inner);
        var a = _first?.DesiredSize ?? Size.Zero;
        var b = _second?.DesiredSize ?? Size.Zero;
        var size = Horizontal
            ? new Size(a.Width + b.Width + SplitterThickness, MathF.Max(a.Height, b.Height))
            : new Size(MathF.Max(a.Width, b.Width), a.Height + b.Height + SplitterThickness);
        return new Size(size.Width + Padding.Horizontal, size.Height + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var r = bounds.Deflate(Padding);
        float first = FirstLength(Available(r));
        if (Horizontal)
        {
            _first?.Arrange(new Rect(r.X, r.Y, first, r.Height));
            var splitter = new Rect(r.X + first, r.Y, SplitterThickness, r.Height);
            _thumb.Arrange(splitter);
            _second?.Arrange(Rect.FromEdges(splitter.Right, r.Y, r.Right, r.Bottom));
        }
        else
        {
            _first?.Arrange(new Rect(r.X, r.Y, r.Width, first));
            var splitter = new Rect(r.X, r.Y + first, r.Width, SplitterThickness);
            _thumb.Arrange(splitter);
            _second?.Arrange(Rect.FromEdges(r.X, splitter.Bottom, r.Right, r.Bottom));
        }
    }

    /// <summary>The splitter: drawn above the panes, owns the drag.</summary>
    private sealed class SplitterThumb : UIElement
    {
        private readonly SplitView _owner;
        private bool _dragging;
        private float _startPointer, _startFirst;

        public SplitterThumb(SplitView owner)
        {
            _owner = owner;
            Cursor = StandardCursor.SizeWE;
        }

        public override bool HitTest(Point point) => Bounds.Inflate(2f).Contains(point);

        protected override void OnRender(DrawingContext dc)
        {
            var p = Theme.Palette;
            bool hot = _dragging || IsMouseOver;
            var c = Bounds.Center;
            float hair = 1f / dc.Scale;
            if (_owner.Horizontal)
            {
                float x = MathF.Round(c.X * dc.Scale) / dc.Scale + hair * 0.5f;
                dc.DrawLine(new Point(x, Bounds.Y), new Point(x, Bounds.Bottom), hot ? p.Accent : p.Border, hot ? 2f : hair);
                for (int i = -1; i <= 1; i++) dc.FillCircle(new Point(c.X, c.Y + i * 5f), 1.3f, hot ? p.Accent : p.TextDim);
            }
            else
            {
                float y = MathF.Round(c.Y * dc.Scale) / dc.Scale + hair * 0.5f;
                dc.DrawLine(new Point(Bounds.X, y), new Point(Bounds.Right, y), hot ? p.Accent : p.Border, hot ? 2f : hair);
                for (int i = -1; i <= 1; i++) dc.FillCircle(new Point(c.X + i * 5f, c.Y), 1.3f, hot ? p.Accent : p.TextDim);
            }
        }

        protected internal override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left) return;
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                _owner.Ratio = _owner.DefaultRatio;
                return;
            }
            _dragging = true;
            _startPointer = _owner.Horizontal ? e.Position.X : e.Position.Y;
            var r = _owner.Bounds.Deflate(_owner.Padding);
            _startFirst = _owner.FirstLength(_owner.Available(r));
        }

        protected internal override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            var r = _owner.Bounds.Deflate(_owner.Padding);
            float available = MathF.Max(1f, _owner.Available(r));
            float pointer = _owner.Horizontal ? e.Position.X : e.Position.Y;
            float first = Math.Clamp(_startFirst + pointer - _startPointer, MathF.Min(_owner.MinFirst, available), MathF.Max(_owner.MinFirst, available - _owner.MinSecond));
            _owner.Ratio = first / available;
        }

        protected internal override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            InvalidateVisual();
        }

        protected internal override void OnCaptureLost()
        {
            base.OnCaptureLost();
            _dragging = false;
        }
    }
}
