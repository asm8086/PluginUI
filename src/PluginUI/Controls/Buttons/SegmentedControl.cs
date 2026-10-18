// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Row (or column) of mutually exclusive segments — radio buttons / tab switch in one shape. The selection
/// highlight slides between segments. Segment texts come from <see cref="Items"/> or the parameter's state labels.
/// </summary>
public class SegmentedControl : ValueControl
{
    private IReadOnlyList<string>? _items;
    private float _highlight = -1f;
    private Animation? _animation;
    private int _hoverIndex = -1;

    /// <summary>Creates a segmented control.</summary>
    public SegmentedControl() { }

    /// <summary>Creates a segmented control with local items.</summary>
    public SegmentedControl(params string[] items) => Items = items;

    /// <summary>Segment texts (null = parameter state labels).</summary>
    [Category("Value")]
    public IReadOnlyList<string>? Items
    {
        get => _items;
        set
        {
            _items = value;
            if (value is { Count: > 1 } && Parameter is null) StepCount = value.Count - 1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Segments laid out horizontally or vertically.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Selected segment index.</summary>
    [Category("Value")]
    public int SelectedIndex
    {
        get => StateIndex;
        set => Value = Count <= 1 ? 0.0 : Math.Clamp(value, 0, Count - 1) / (double)(Count - 1);
    }

    /// <summary>Segment labels in effect.</summary>
    public IReadOnlyList<string> Labels => StateLabels.For(this, _items);

    /// <summary>Number of segments.</summary>
    public int Count => Math.Max(Labels.Count, StepCount + 1);

    /// <inheritdoc />
    public override string? ValueText => null;

    private TextStyle SegmentStyle => Theme.Typography.Small with { Align = HorizontalAlignment.Center, Weight = FontWeight.SemiBold };

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            var labels = Labels;
            float maxW = 24f;
            foreach (var l in labels) maxW = MathF.Max(maxW, TextMeasurer.Measure(l, SegmentStyle).Width + 18f);
            int n = Math.Max(1, Count);
            float extra = EffectiveStyle.ShowLabel && !string.IsNullOrEmpty(Label) ? EffectiveStyle.LabelText.Size * 1.5f : 0f;
            return Orientation == Orientation.Horizontal
                ? new Size(maxW * n + 4f, Theme.Metrics.ControlHeight + extra)
                : new Size(maxW + 4f, 22f * n + 4f + extra);
        }
    }

    private Rect SegmentRect(Rect track, float index)
    {
        int n = Math.Max(1, Count);
        var inner = track.Inflate(-2f);
        if (Orientation == Orientation.Horizontal)
        {
            float w = inner.Width / n;
            return new Rect(inner.X + w * index, inner.Y, w, inner.Height);
        }
        float h = inner.Height / n;
        return new Rect(inner.X, inner.Y + h * index, inner.Width, h);
    }

    private Rect TrackRect
    {
        get
        {
            LayoutParts(EffectiveStyle, out _, out var widget, out _);
            return widget;
        }
    }

    private int IndexAt(Point p)
    {
        var inner = TrackRect.Inflate(-2f);
        int n = Math.Max(1, Count);
        float t = Orientation == Orientation.Horizontal ? (p.X - inner.X) / MathF.Max(1f, inner.Width) : (p.Y - inner.Y) / MathF.Max(1f, inner.Height);
        return Math.Clamp((int)(t * n), 0, n - 1);
    }

    /// <inheritdoc />
    protected override void OnValueChanged()
    {
        base.OnValueChanged();
        float target = StateIndex;
        _animation?.Cancel();
        if (Host is not { IsAttached: true } || _highlight < 0f) { _highlight = target; return; }
        float from = _highlight;
        _animation = Host.Animator.Animate(from, target, 0.18, v => { _highlight = v; InvalidateVisual(); }, Easing.CubicOut);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _highlight = StateIndex;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var labels = Labels;
        int n = Math.Max(1, Count);
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.5f) });
        dc.FillRoundedRect(widget, radius, style.Background);
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame, style.FrameThickness);

        // Dividers between unselected segments.
        int selected = StateIndex;
        for (int i = 1; i < n; i++)
        {
            if (i == selected || i == selected + 1) continue;
            var a = SegmentRect(widget, i);
            if (Orientation == Orientation.Horizontal)
            {
                float x = MathF.Round(a.X) + 0.5f;
                dc.DrawLine(new Point(x, a.Y + 5f), new Point(x, a.Bottom - 5f), style.Frame);
            }
            else
            {
                float y = MathF.Round(a.Y) + 0.5f;
                dc.DrawLine(new Point(a.X + 5f, y), new Point(a.Right - 5f, y), style.Frame);
            }
        }

        if (_hoverIndex >= 0 && _hoverIndex != selected && IsMouseOver && IsEffectivelyEnabled)
        {
            var hr = SegmentRect(widget, _hoverIndex).Inflate(-1f);
            dc.FillRoundedRect(hr, MathF.Max(0f, radius - 2f), style.Hover);
        }

        float hl = _highlight < 0f ? selected : _highlight;
        var sel = SegmentRect(widget, hl);
        var accent = IsEffectivelyEnabled ? style.Accent : p.TextDisabled;
        if (style.DrawShadow) dc.DrawShadow(sel, MathF.Max(0f, radius - 2f), 3f, new Point(0f, 1f), style.Shadow.Fade(0.5f));
        dc.FillRoundedRect(sel, MathF.Max(0f, radius - 2f), IsPressed ? accent.Darken(0.12f) : accent);

        var ts = SegmentStyle;
        for (int i = 0; i < n; i++)
        {
            var r = SegmentRect(widget, i);
            string text = i < labels.Count ? labels[i] : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Text color blends with the sliding highlight overlap.
            float overlap = Math.Clamp(1f - MathF.Abs(hl - i), 0f, 1f);
            var fg = Color.Lerp(IsEffectivelyEnabled ? p.TextDim : p.TextDisabled, accent.Contrasting, overlap);
            dc.DrawText(text, r.Deflate(new Thickness(3f, 0f)), ts, fg);
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        IsPressed = true;
        BeginGesture();
        UpdateGesture(Count <= 1 ? 0.0 : IndexAt(e.Position) / (double)(Count - 1));
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Position);
        if (IsMouseCaptured && IsPressed)
        {
            UpdateGesture(Count <= 1 ? 0.0 : index / (double)(Count - 1));
        }
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        IsPressed = false;
        EndGesture();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hoverIndex = -1;
    }
}
