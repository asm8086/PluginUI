// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Geometry, drawing and drag logic of the slim overlay scrollbars used by <see cref="ScrollViewer"/>,
/// <see cref="ListBox"/> and <see cref="DataTable"/> (not an element: the owner forwards input to it).
/// </summary>
internal sealed class SlimScrollBar
{
    private float _dragStartPointer;
    private float _dragStartOffset;

    public SlimScrollBar(Orientation orientation) => Orientation = orientation;

    public Orientation Orientation { get; }

    /// <summary>Total content length (DIPs).</summary>
    [Category("Behavior")]
    public float Extent { get; set; }

    /// <summary>Visible length (DIPs).</summary>
    [Category("Behavior")]
    public float Viewport { get; set; }

    /// <summary>Current offset (DIPs), kept in [0, Extent − Viewport].</summary>
    [Category("Behavior")]
    public float Offset { get; set; }

    /// <summary>Track rectangle in host DIPs (set by the owner during layout or rendering).</summary>
    [Category("Appearance")]
    public Rect Track { get; set; }

    [Browsable(false)]
    public bool IsDragging { get; private set; }

    [Category("Behavior")]
    public bool IsHovered { get; set; }

    public float MaxOffset => MathF.Max(0f, Extent - Viewport);

    public bool IsNeeded => Extent > Viewport + 0.5f;

    public float Clamp(float offset) => Math.Clamp(offset, 0f, MaxOffset);

    public Rect ThumbRect
    {
        get
        {
            var t = Track;
            bool vertical = Orientation == Orientation.Vertical;
            float length = vertical ? t.Height : t.Width;
            if (!IsNeeded || length <= 0f) return Rect.Empty;
            float thumb = MathF.Max(MathF.Min(24f, length), length * Viewport / MathF.Max(1f, Extent));
            float pos = MaxOffset <= 0f ? 0f : (length - thumb) * Offset / MaxOffset;
            return vertical ? new Rect(t.X, t.Y + pos, t.Width, thumb) : new Rect(t.X + pos, t.Y, thumb, t.Height);
        }
    }

    /// <summary>Hit zone (the track, slightly widened for easy grabbing).</summary>
    public bool HitTest(Point p) => IsNeeded && (Orientation == Orientation.Vertical ? Track.Inflate(3f, 0f) : Track.Inflate(0f, 3f)).Contains(p);

    /// <summary>Starts a thumb drag, or pages toward the pointer when the track is clicked. Returns the new offset.</summary>
    public float Press(Point p)
    {
        float pointer = Orientation == Orientation.Vertical ? p.Y : p.X;
        var thumb = ThumbRect;
        bool onThumb = Orientation == Orientation.Vertical ? p.Y >= thumb.Y && p.Y < thumb.Bottom : p.X >= thumb.X && p.X < thumb.Right;
        if (!onThumb)
        {
            float thumbStart = Orientation == Orientation.Vertical ? thumb.Y : thumb.X;
            Offset = Clamp(Offset + (pointer < thumbStart ? -Viewport : Viewport) * 0.9f);
        }
        IsDragging = true;
        _dragStartPointer = pointer;
        _dragStartOffset = Offset;
        return Offset;
    }

    /// <summary>Moves the thumb with the pointer. Returns the new offset.</summary>
    public float Drag(Point p)
    {
        if (!IsDragging) return Offset;
        float pointer = Orientation == Orientation.Vertical ? p.Y : p.X;
        var t = Track;
        float length = Orientation == Orientation.Vertical ? t.Height : t.Width;
        var thumb = ThumbRect;
        float thumbLength = Orientation == Orientation.Vertical ? thumb.Height : thumb.Width;
        float travel = MathF.Max(1f, length - thumbLength);
        Offset = Clamp(_dragStartOffset + (pointer - _dragStartPointer) * MaxOffset / travel);
        return Offset;
    }

    public void Release() => IsDragging = false;

    /// <summary>Draws the bar (thin when idle, wider on hover or drag).</summary>
    public void Render(DrawingContext dc, Palette palette, float alpha = 1f)
    {
        if (!IsNeeded || alpha <= 0.01f) return;
        bool active = IsHovered || IsDragging;
        var t = Track;
        if (active) dc.FillRoundedRect(t, MathF.Min(t.Width, t.Height) * 0.5f, palette.Track.WithAlpha(0.5f * alpha));
        var thumb = ThumbRect;
        float inset = active ? 1f : 2f;
        thumb = Orientation == Orientation.Vertical ? thumb.Inflate(-inset, -1f) : thumb.Inflate(-1f, -inset);
        float r = MathF.Min(thumb.Width, thumb.Height) * 0.5f;
        var color = IsDragging ? palette.Text.WithAlpha(0.6f) : active ? palette.Text.WithAlpha(0.45f) : palette.TextDim.WithAlpha(0.35f);
        dc.FillRoundedRect(thumb, r, color.Fade(alpha));
    }
}
