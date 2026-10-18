// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Point in device-independent pixels (DIPs, 1/96 inch).
/// </summary>
public readonly record struct Point(float X, float Y)
{
    /// <summary>The origin.</summary>
    public static Point Zero => default;

    /// <summary>Adds two points component-wise.</summary>
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtracts two points component-wise.</summary>
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Scales a point.</summary>
    public static Point operator *(Point a, float s) => new(a.X * s, a.Y * s);

    /// <summary>Euclidean length of the vector.</summary>
    public float Length => MathF.Sqrt(X * X + Y * Y);

    /// <summary>Linear interpolation between two points.</summary>
    public static Point Lerp(Point a, Point b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}

/// <summary>
/// Size in DIPs.
/// </summary>
public readonly record struct Size(float Width, float Height)
{
    /// <summary>Empty size.</summary>
    public static Size Zero => default;

    /// <summary>Unbounded size, used as measure constraint.</summary>
    public static Size Infinite => new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary><c>true</c> if either dimension is zero or negative.</summary>
    public bool IsEmpty => Width <= 0f || Height <= 0f;
}

/// <summary>
/// Axis-aligned rectangle in DIPs.
/// </summary>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    /// <summary>Empty rectangle at the origin.</summary>
    public static Rect Empty => default;

    /// <summary>Creates a rectangle from its edges.</summary>
    public static Rect FromEdges(float left, float top, float right, float bottom) => new(left, top, MathF.Max(0f, right - left), MathF.Max(0f, bottom - top));

    /// <summary>Creates a rectangle centered on a point.</summary>
    public static Rect FromCenter(Point center, float width, float height) => new(center.X - width * 0.5f, center.Y - height * 0.5f, width, height);

    /// <summary>Left edge.</summary>
    public float Left => X;

    /// <summary>Top edge.</summary>
    public float Top => Y;

    /// <summary>Right edge.</summary>
    public float Right => X + Width;

    /// <summary>Bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>Top-left corner.</summary>
    public Point Location => new(X, Y);

    /// <summary>Size of the rectangle.</summary>
    public Size Size => new(Width, Height);

    /// <summary>Center point.</summary>
    public Point Center => new(X + Width * 0.5f, Y + Height * 0.5f);

    /// <summary><c>true</c> if the rectangle has no area.</summary>
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    /// <summary>Returns <c>true</c> if the point lies inside the rectangle (right/bottom edges excluded).</summary>
    public bool Contains(Point p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    /// <summary>Returns <c>true</c> if the rectangles overlap.</summary>
    public bool Intersects(Rect other) => other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;

    /// <summary>Intersection of two rectangles (empty if they do not overlap).</summary>
    public Rect Intersect(Rect other)
    {
        float l = MathF.Max(X, other.X), t = MathF.Max(Y, other.Y);
        float r = MathF.Min(Right, other.Right), b = MathF.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? Empty : new Rect(l, t, r - l, b - t);
    }

    /// <summary>Smallest rectangle containing both rectangles.</summary>
    public Rect Union(Rect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(MathF.Min(X, other.X), MathF.Min(Y, other.Y), MathF.Max(Right, other.Right), MathF.Max(Bottom, other.Bottom));
    }

    /// <summary>Grows (positive) or shrinks (negative) the rectangle on every side.</summary>
    public Rect Inflate(float dx, float dy) => new(X - dx, Y - dy, MathF.Max(0f, Width + 2f * dx), MathF.Max(0f, Height + 2f * dy));

    /// <summary>Grows the rectangle uniformly.</summary>
    public Rect Inflate(float d) => Inflate(d, d);

    /// <summary>Removes a thickness from the rectangle (padding, border...).</summary>
    public Rect Deflate(Thickness t) => new(X + t.Left, Y + t.Top, MathF.Max(0f, Width - t.Horizontal), MathF.Max(0f, Height - t.Vertical));

    /// <summary>Adds a thickness around the rectangle.</summary>
    public Rect Inflate(Thickness t) => new(X - t.Left, Y - t.Top, Width + t.Horizontal, Height + t.Vertical);

    /// <summary>Moves the rectangle.</summary>
    public Rect Offset(float dx, float dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Largest square centered in the rectangle.</summary>
    public Rect CenteredSquare()
    {
        float s = MathF.Min(Width, Height);
        return new Rect(X + (Width - s) * 0.5f, Y + (Height - s) * 0.5f, s, s);
    }

    /// <summary>Splits off a horizontal band from the top.</summary>
    public Rect TakeTop(float height) => new(X, Y, Width, MathF.Min(height, Height));

    /// <summary>Splits off a horizontal band from the bottom.</summary>
    public Rect TakeBottom(float height) { float h = MathF.Min(height, Height); return new(X, Bottom - h, Width, h); }

    /// <summary>Splits off a vertical band from the left.</summary>
    public Rect TakeLeft(float width) => new(X, Y, MathF.Min(width, Width), Height);

    /// <summary>Splits off a vertical band from the right.</summary>
    public Rect TakeRight(float width) { float w = MathF.Min(width, Width); return new(Right - w, Y, w, Height); }

    /// <summary>Rounds the edges to whole pixels for a given scale (crisp 1-pixel lines).</summary>
    public Rect Snap(float scale)
    {
        if (scale <= 0f) return this;
        float l = MathF.Round(X * scale) / scale, t = MathF.Round(Y * scale) / scale;
        float r = MathF.Round(Right * scale) / scale, b = MathF.Round(Bottom * scale) / scale;
        return FromEdges(l, t, r, b);
    }
}

/// <summary>
/// Thickness of a frame (margin, padding, border) for each side, in DIPs.
/// </summary>
public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Same thickness on every side.</summary>
    public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }

    /// <summary>Horizontal and vertical thickness.</summary>
    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }

    /// <summary>No thickness.</summary>
    public static Thickness Zero => default;

    /// <summary>Left + right.</summary>
    public float Horizontal => Left + Right;

    /// <summary>Top + bottom.</summary>
    public float Vertical => Top + Bottom;

    /// <summary>Implicit conversion from a uniform value.</summary>
    public static implicit operator Thickness(float uniform) => new(uniform);
}

/// <summary>Horizontal or vertical orientation.</summary>
public enum Orientation
{
    /// <summary>Left to right.</summary>
    Horizontal,
    /// <summary>Top to bottom.</summary>
    Vertical,
}

/// <summary>Horizontal alignment of an element within its layout slot, or of text.</summary>
public enum HorizontalAlignment
{
    /// <summary>Fills the slot.</summary>
    Stretch,
    /// <summary>Left aligned.</summary>
    Left,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Right aligned.</summary>
    Right,
}

/// <summary>Vertical alignment of an element within its layout slot, or of text.</summary>
public enum VerticalAlignment
{
    /// <summary>Fills the slot.</summary>
    Stretch,
    /// <summary>Top aligned.</summary>
    Top,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Bottom aligned.</summary>
    Bottom,
}

/// <summary>Side of a rectangle (docking, popup placement, tab strip...).</summary>
public enum Side
{
    /// <summary>Left side.</summary>
    Left,
    /// <summary>Top side.</summary>
    Top,
    /// <summary>Right side.</summary>
    Right,
    /// <summary>Bottom side.</summary>
    Bottom,
}
