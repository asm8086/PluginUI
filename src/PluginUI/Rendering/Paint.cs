// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Describes how a shape is filled: solid color, linear or radial gradient.
/// </summary>
/// <remarks>
/// Gradient coordinates are relative to the bounds of the filled shape (0..1) unless
/// <c>RelativeToBounds</c> is <c>false</c>, in which case they are absolute DIPs.
/// </remarks>
public abstract record Paint
{
    /// <summary>Implicit conversion from a color.</summary>
    public static implicit operator Paint(Color color) => new SolidPaint(color);

    /// <summary>Top-to-bottom two-color gradient.</summary>
    public static LinearGradientPaint Vertical(Color top, Color bottom) => new(new Point(0f, 0f), new Point(0f, 1f), [new(0f, top), new(1f, bottom)]);

    /// <summary>Left-to-right two-color gradient.</summary>
    public static LinearGradientPaint Horizontal(Color left, Color right) => new(new Point(0f, 0f), new Point(1f, 0f), [new(0f, left), new(1f, right)]);

    /// <summary>Radial gradient from the center color to the edge color.</summary>
    public static RadialGradientPaint Radial(Color center, Color edge) => new(new Point(0.5f, 0.5f), 0.5f, 0.5f, [new(0f, center), new(1f, edge)]);
}

/// <summary>Solid color fill.</summary>
public sealed record SolidPaint(Color Color) : Paint;

/// <summary>Color stop of a gradient.</summary>
public readonly record struct GradientStop(float Offset, Color Color);

/// <summary>Linear gradient between two points.</summary>
public sealed record LinearGradientPaint(Point Start, Point End, GradientStop[] Stops, bool RelativeToBounds = true) : Paint;

/// <summary>Radial (elliptic) gradient.</summary>
public sealed record RadialGradientPaint(Point Center, float RadiusX, float RadiusY, GradientStop[] Stops, Point OriginOffset = default, bool RelativeToBounds = true) : Paint;

/// <summary>Shape of the end of a line.</summary>
public enum LineCap
{
    /// <summary>Square end flush with the end point.</summary>
    Flat,
    /// <summary>Square end extending half the thickness.</summary>
    Square,
    /// <summary>Rounded end.</summary>
    Round,
}

/// <summary>Shape of the joint between two segments.</summary>
public enum LineJoin
{
    /// <summary>Sharp corner.</summary>
    Miter,
    /// <summary>Beveled corner.</summary>
    Bevel,
    /// <summary>Rounded corner.</summary>
    Round,
}

/// <summary>
/// Stroke options (caps, joins, dashes). Instances are cached by value by the renderer.
/// </summary>
public sealed record StrokeStyle(LineCap StartCap = LineCap.Flat, LineCap EndCap = LineCap.Flat, LineJoin Join = LineJoin.Miter, float[]? Dashes = null, float DashOffset = 0f)
{
    /// <summary>Plain stroke with flat caps.</summary>
    public static StrokeStyle Solid { get; } = new();

    /// <summary>Round caps and joins (arcs, knob pointers, curves).</summary>
    public static StrokeStyle Round { get; } = new(LineCap.Round, LineCap.Round, LineJoin.Round);

    /// <summary>Dashed line (dash and gap lengths are in stroke-width units).</summary>
    public static StrokeStyle Dashed { get; } = new(Dashes: [4f, 3f]);

    /// <summary>Dotted line.</summary>
    public static StrokeStyle Dotted { get; } = new(LineCap.Round, LineCap.Round, LineJoin.Round, [0f, 2f]);

    /// <inheritdoc />
    public bool Equals(StrokeStyle? other)
        => other is not null && StartCap == other.StartCap && EndCap == other.EndCap && Join == other.Join && DashOffset == other.DashOffset
           && (Dashes is null ? other.Dashes is null : other.Dashes is not null && Dashes.AsSpan().SequenceEqual(other.Dashes));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StartCap); hash.Add(EndCap); hash.Add(Join); hash.Add(DashOffset);
        if (Dashes is not null) foreach (var d in Dashes) hash.Add(d);
        return hash.ToHashCode();
    }
}
