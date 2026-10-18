// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Shape of a slider handle.</summary>
public enum SliderHandle
{
    /// <summary>Rounded rectangle cap (fader).</summary>
    Cap,
    /// <summary>Circle.</summary>
    Round,
    /// <summary>Thin line across the track.</summary>
    Line,
    /// <summary>No handle: the filled track shows the value.</summary>
    None,
}

/// <summary>
/// Linear fader, vertical or horizontal. Relative drag (or jump to the click with <see cref="JumpToClick"/>),
/// Shift = fine, double-click/Ctrl+click resets, Alt+click types a value, wheel and arrow keys step.
/// Bitmap looks: <see cref="SliderHandleSkin"/> or a <see cref="FilmStripSkin"/>.
/// </summary>
public class Slider : ValueControl
{
    private Orientation _orientation;
    private SliderHandle _handle = SliderHandle.Cap;
    private bool _bipolar;
    private bool? _sideLabels;
    private RotaryLinearHelpers.DragAccumulator _drag;

    /// <summary>Creates a vertical slider.</summary>
    public Slider() : this(Orientation.Vertical) { }

    /// <summary>Creates a slider with an orientation.</summary>
    public Slider(Orientation orientation)
    {
        _orientation = orientation;
        Cursor = orientation == Orientation.Vertical ? StandardCursor.SizeNS : StandardCursor.SizeWE;
    }

    /// <summary>Direction of travel.</summary>
    [Category("Layout")]
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            Cursor = value == Orientation.Vertical ? StandardCursor.SizeNS : StandardCursor.SizeWE;
            InvalidateMeasure();
        }
    }

    /// <summary>Handle shape.</summary>
    [Category("Appearance")]
    public SliderHandle Handle { get => _handle; set { _handle = value; InvalidateVisual(); } }

    /// <summary>Fills from <see cref="BipolarCenter"/> instead of the minimum.</summary>
    [Category("Value")]
    public bool IsBipolar { get => _bipolar; set { _bipolar = value; InvalidateVisual(); } }

    /// <summary>Origin of the bipolar fill (normalized).</summary>
    [Category("Value")]
    public double BipolarCenter { get; set; } = 0.5;

    /// <summary>A click on the track moves the handle there before dragging.</summary>
    [Category("Behavior")]
    public bool JumpToClick { get; set; }

    /// <summary>Draws tick marks along the track (0 = none; discrete values use their step count when −1).</summary>
    [Category("Appearance")]
    public int TickCount { get; set; } = -1;

    /// <summary>
    /// Label left and value right of the track (horizontal sliders default to <c>true</c>), instead of above/below.
    /// </summary>
    [Category("Layout")]
    public bool SideLabels { get => _sideLabels ?? _orientation == Orientation.Horizontal; set { _sideLabels = value; InvalidateVisual(); } }

    /// <summary>Width of the side label column.</summary>
    [Category("Layout")]
    public float LabelWidth { get; set; } = 56f;

    /// <summary>Width of the side value column.</summary>
    [Category("Layout")]
    public float ValueWidth { get; set; } = 52f;

    /// <summary>Handle length along the track in DIPs (0 = automatic).</summary>
    [Category("Layout")]
    public float HandleLength { get; set; }

    /// <summary>
    /// Allows typing the value in a text box (default <c>true</c>). The actions that open it are chosen by
    /// <see cref="ValueControl.TextEntryTriggers"/>: by default a double-click on the value text, Alt+click,
    /// a right double-click or Enter.
    /// </summary>
    [Category("Behavior")]
    public bool AllowTextEntry { get; set; } = true;

    /// <inheritdoc />
    protected override bool SupportsTextEntry => AllowTextEntry;

    /// <summary>Text → normalized value parser for typed values (default: parameter parser, or percent).</summary>
    [Browsable(false)]
    public Func<string, double?>? Parser { get; set; }

    /// <inheritdoc />
    protected override Size DefaultSize => _orientation == Orientation.Vertical ? new(44f, 160f) : new(220f, 28f);

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        if (!SideLabels)
        {
            base.LayoutParts(style, out label, out widget, out value);
            return;
        }
        var r = Bounds;
        label = Rect.Empty;
        value = Rect.Empty;
        if (style.ShowLabel && !string.IsNullOrEmpty(Label))
        {
            label = r.TakeLeft(LabelWidth);
            r = Rect.FromEdges(r.X + LabelWidth + 4f, r.Y, r.Right, r.Bottom);
        }
        if (style.ShowValue && ValueText is not null)
        {
            value = r.TakeRight(ValueWidth);
            r = Rect.FromEdges(r.X, r.Y, r.Right - ValueWidth - 4f, r.Bottom);
        }
        widget = r;
    }

    /// <inheritdoc />
    protected override void RenderLabel(DrawingContext dc, Rect area, VectorStyle style)
        => dc.DrawText(Label, area, SideLabels ? style.LabelText with { Align = HorizontalAlignment.Left } : style.LabelText, style.LabelColor);

    /// <inheritdoc />
    protected override void RenderValue(DrawingContext dc, Rect area, VectorStyle style)
        => dc.DrawText(ValueText, area, SideLabels ? style.ValueText with { Align = HorizontalAlignment.Right } : style.ValueText, style.ValueColor);

    // ---------------------------------------------------------------- geometry

    private float AutoHandleLength(Rect widget)
    {
        if (HandleLength > 0f) return HandleLength;
        float cross = _orientation == Orientation.Vertical ? widget.Width : widget.Height;
        return _handle switch
        {
            SliderHandle.Round => MathF.Min(cross, 16f),
            SliderHandle.Line => 4f,
            SliderHandle.None => 0f,
            _ => _orientation == Orientation.Vertical ? MathF.Min(28f, MathF.Max(14f, cross * 0.45f)) : MathF.Min(cross, 14f),
        };
    }

    /// <summary>Track segment (start = value 0, end = value 1) inside a widget rectangle.</summary>
    public (Point Start, Point End) GetTrack(Rect widget)
    {
        float half = AutoHandleLength(widget) * 0.5f + 1f;
        var c = widget.Center;
        return _orientation == Orientation.Vertical
            ? (new Point(c.X, widget.Bottom - half), new Point(c.X, widget.Y + half))
            : (new Point(widget.X + half, c.Y), new Point(widget.Right - half, c.Y));
    }

    /// <summary>Point of the track at a normalized value.</summary>
    public Point PointAt(Rect widget, double normalized)
    {
        var (a, b) = GetTrack(widget);
        return Point.Lerp(a, b, (float)Math.Clamp(normalized, 0.0, 1.0));
    }

    /// <summary>Handle rectangle of a given size centered on the value position (used by skins).</summary>
    public Rect GetHandleRect(Rect widget, Size handleSize, double normalized) => Rect.FromCenter(PointAt(widget, normalized), handleSize.Width, handleSize.Height);

    private double ValueAt(Rect widget, Point p)
    {
        var (a, b) = GetTrack(widget);
        float length = _orientation == Orientation.Vertical ? a.Y - b.Y : b.X - a.X;
        if (length <= 0f) return Value;
        return Math.Clamp(_orientation == Orientation.Vertical ? (a.Y - p.Y) / length : (p.X - a.X) / length, 0f, 1f);
    }

    private Rect WidgetRect()
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        return widget;
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        bool vertical = _orientation == Orientation.Vertical;
        var (a, b) = GetTrack(widget);
        float thick = _handle == SliderHandle.None
            ? (vertical ? widget.Width : widget.Height) * 0.5f
            : MathF.Max(2f, style.TrackThickness);
        float value = (float)Value;
        var pos = Point.Lerp(a, b, value);
        var origin = Point.Lerp(a, b, _bipolar ? (float)BipolarCenter : 0f);
        bool hot = IsMouseOver || IsPressed;

        // Ticks.
        int ticks = TickCount >= 0 ? TickCount : StepCount > 0 && StepCount <= 24 ? StepCount + 1 : 0;
        if (ticks >= 2)
        {
            var tc = style.LabelColor.Fade(0.5f);
            float off = (vertical ? widget.Width : widget.Height) * 0.5f - 1f;
            for (int i = 0; i < ticks; i++)
            {
                var t = Point.Lerp(a, b, i / (float)(ticks - 1));
                if (vertical)
                {
                    dc.DrawLine(new Point(t.X - off, t.Y), new Point(t.X - off + 4f, t.Y), tc);
                    dc.DrawLine(new Point(t.X + off - 4f, t.Y), new Point(t.X + off, t.Y), tc);
                }
                else
                {
                    dc.DrawLine(new Point(t.X, t.Y - off), new Point(t.X, t.Y - off + 4f), tc);
                    dc.DrawLine(new Point(t.X, t.Y + off - 4f), new Point(t.X, t.Y + off), tc);
                }
            }
        }

        // Track and fill.
        var track = vertical ? Rect.FromEdges(a.X - thick * 0.5f, b.Y - 1f, a.X + thick * 0.5f, a.Y + 1f) : Rect.FromEdges(a.X - 1f, a.Y - thick * 0.5f, b.X + 1f, a.Y + thick * 0.5f);
        float tr = MathF.Min(thick * 0.5f, 4f);
        dc.FillRoundedRect(track, tr, p.Track);
        var fill = vertical
            ? Rect.FromEdges(track.X, MathF.Min(origin.Y, pos.Y), track.Right, MathF.Max(origin.Y, pos.Y))
            : Rect.FromEdges(MathF.Min(origin.X, pos.X), track.Y, MathF.Max(origin.X, pos.X), track.Bottom);
        if (!_bipolar)
        {
            fill = vertical ? Rect.FromEdges(track.X, pos.Y, track.Right, track.Bottom) : Rect.FromEdges(track.X, track.Y, pos.X, track.Bottom);
        }
        if (fill.Width > 0f && fill.Height > 0f) dc.FillRoundedRect(fill, tr, hot && _handle == SliderHandle.None ? style.Accent.Lighten(0.1f) : style.Accent);
        if (_bipolar)
        {
            dc.DrawLine(vertical ? new Point(track.X - 3f, origin.Y) : new Point(origin.X, track.Y - 3f),
                vertical ? new Point(track.Right + 3f, origin.Y) : new Point(origin.X, track.Bottom + 3f), style.LabelColor.Fade(0.7f));
        }

        // Handle.
        float len = AutoHandleLength(widget);
        float cross = vertical ? widget.Width : widget.Height;
        switch (_handle)
        {
            case SliderHandle.Round:
            {
                float r = len * 0.5f;
                if (style.DrawShadow) dc.DrawShadow(Rect.FromCenter(pos, len, len), r, style.ShadowBlur * 0.6f, new Point(0f, style.ShadowOffset * 0.5f), style.Shadow);
                dc.FillCircle(pos, r, IsPressed ? style.Pressed : style.Fill);
                if (hot) dc.FillCircle(pos, r, style.Hover);
                dc.DrawCircle(pos, r - 0.5f, style.Frame);
                dc.FillCircle(pos, MathF.Max(1.5f, r * 0.35f), style.Accent);
                break;
            }
            case SliderHandle.Line:
            {
                float w = cross * 0.8f;
                var line = vertical ? Rect.FromCenter(pos, w, 3f) : Rect.FromCenter(pos, 3f, w);
                dc.FillRoundedRect(line, 1.5f, hot ? style.ValueColor : style.ValueColor.Fade(0.85f));
                break;
            }
            case SliderHandle.Cap:
            {
                float w = vertical ? MathF.Min(cross, MathF.Max(18f, cross * 0.8f)) : len;
                float h = vertical ? len : MathF.Min(cross, MathF.Max(16f, cross * 0.85f));
                var cap = Rect.FromCenter(pos, w, h);
                float r = MathF.Min(3f, MathF.Min(w, h) * 0.3f);
                if (style.DrawShadow) dc.DrawShadow(cap, r, style.ShadowBlur * 0.6f, new Point(0f, style.ShadowOffset * 0.6f), style.Shadow);
                var f = IsPressed ? style.Pressed : style.Fill;
                dc.FillRoundedRect(cap, r, Paint.Vertical(f.Lighten(0.08f), f.Darken(0.06f)));
                if (hot) dc.FillRoundedRect(cap, r, style.Hover);
                if (style.DrawFrame) dc.DrawRoundedRect(cap.Inflate(-0.5f), r, style.Frame);
                // Center grip line.
                var g = vertical ? new Rect(cap.X + 3f, pos.Y - 0.75f, cap.Width - 6f, 1.5f) : new Rect(pos.X - 0.75f, cap.Y + 3f, 1.5f, cap.Height - 6f);
                dc.FillRect(g, style.Accent);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (TryHandleTextEntry(e)) return;
        if (e.Button != MouseButton.Left) return;
        if (TryHandleReset(e)) return;
        IsPressed = true;
        BeginGesture();
        var widget = WidgetRect();
        double start = Value;
        if (JumpToClick)
        {
            var handlePos = PointAt(widget, start);
            float len = MathF.Max(8f, AutoHandleLength(widget));
            float distance = _orientation == Orientation.Vertical ? MathF.Abs(e.Position.Y - handlePos.Y) : MathF.Abs(e.Position.X - handlePos.X);
            if (distance > len * 0.5f)
            {
                start = ValueAt(widget, e.Position);
                UpdateGesture(start);
            }
        }
        _drag.Start(e.Position, start);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsPressed) return;
        var (a, b) = GetTrack(WidgetRect());
        float length = MathF.Max(10f, _orientation == Orientation.Vertical ? a.Y - b.Y : b.X - a.X);
        double factor = 1.0 / length * (e.Shift ? FineFactor : 1f);
        UpdateGesture(_orientation == Orientation.Vertical ? _drag.Move(e.Position, 0f, -1f, factor) : _drag.Move(e.Position, 1f, 0f, factor));
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsPressed || e.Button != MouseButton.Left) return;
        IsPressed = false;
        EndGesture();
        InvalidateVisual();
    }

    /// <summary>Opens the in-place text box over the value text (or over the slider when the value is hidden).</summary>
    public override void BeginTextEntry()
    {
        if (!AllowTextEntry) return;
        LayoutParts(EffectiveStyle, out _, out var widget, out var valueArea);
        var area = valueArea.IsEmpty ? Rect.FromCenter(widget.Center, 60f, 20f) : valueArea;
        RotaryLinearHelpers.BeginTextEntry(this, area, ValueText ?? string.Empty, Parameter, Parser, SetValueByUser);
    }
}

/// <summary>
/// Bitmap slider skin: optional track image stretched over the widget area and a handle image moved along the
/// track of a <see cref="Slider"/> (or of any control, horizontally/vertically by <see cref="Orientation"/>).
/// </summary>
public sealed class SliderHandleSkin(ImageSource handle, ImageSource? track = null) : IControlSkin
{
    /// <summary>Handle image.</summary>
    public ImageSource HandleImage { get; } = handle;

    /// <summary>Track image (null = vector track from the theme).</summary>
    public ImageSource? TrackImage { get; } = track;

    /// <summary>Orientation used for controls other than <see cref="Slider"/>.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; init; } = Orientation.Vertical;

    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state)
    {
        var r = state.Bounds;
        var size = HandleImage.Size;
        if (TrackImage is not null) dc.DrawImage(TrackImage, ImageLayout.Fit(TrackImage.Size, r, ImageFit.Contain));
        Rect handleRect;
        if (control is Slider slider)
        {
            if (TrackImage is null)
            {
                var (a, b) = slider.GetTrack(r);
                dc.DrawLine(a, b, control.Theme.Palette.Track, 4f, StrokeStyle.Round);
            }
            handleRect = slider.GetHandleRect(r, size, state.Value);
        }
        else
        {
            bool vertical = Orientation == Orientation.Vertical;
            float t = (float)Math.Clamp(state.Value, 0.0, 1.0);
            var c = vertical
                ? new Point(r.Center.X, r.Bottom - size.Height * 0.5f - (r.Height - size.Height) * t)
                : new Point(r.X + size.Width * 0.5f + (r.Width - size.Width) * t, r.Center.Y);
            handleRect = Rect.FromCenter(c, size.Width, size.Height);
        }
        dc.DrawImage(HandleImage, handleRect);
    }
}
