// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Device-independent vector path made of figures (move/line/curve/arc commands).
/// Converted to a Direct2D geometry when drawn.
/// </summary>
public sealed class PathGeometry
{
    internal enum CommandKind : byte { Move, Line, Quad, Cubic, Arc, Close }

    internal readonly record struct Command(CommandKind Kind, Point P1, Point P2, Point P3, Size Radius, float Rotation, bool LargeArc, bool Clockwise);

    internal readonly List<Command> Commands = new();

    /// <summary>Fill rule: <c>false</c> = alternate (even-odd), <c>true</c> = winding (non-zero).</summary>
    public bool Winding { get; set; }

    /// <summary><c>true</c> if no command was added.</summary>
    public bool IsEmpty => Commands.Count == 0;

    /// <summary>Starts a new figure.</summary>
    public PathGeometry MoveTo(Point p) { Commands.Add(new(CommandKind.Move, p, default, default, default, 0f, false, false)); return this; }

    /// <summary>Starts a new figure.</summary>
    public PathGeometry MoveTo(float x, float y) => MoveTo(new Point(x, y));

    /// <summary>Straight segment.</summary>
    public PathGeometry LineTo(Point p) { Commands.Add(new(CommandKind.Line, p, default, default, default, 0f, false, false)); return this; }

    /// <summary>Straight segment.</summary>
    public PathGeometry LineTo(float x, float y) => LineTo(new Point(x, y));

    /// <summary>Quadratic Bézier segment.</summary>
    public PathGeometry QuadTo(Point control, Point end) { Commands.Add(new(CommandKind.Quad, control, end, default, default, 0f, false, false)); return this; }

    /// <summary>Cubic Bézier segment.</summary>
    public PathGeometry CubicTo(Point control1, Point control2, Point end) { Commands.Add(new(CommandKind.Cubic, control1, control2, end, default, 0f, false, false)); return this; }

    /// <summary>Elliptic arc segment (SVG semantics).</summary>
    public PathGeometry ArcTo(Point end, Size radius, bool clockwise, bool largeArc = false, float rotationDegrees = 0f)
    {
        Commands.Add(new(CommandKind.Arc, end, default, default, radius, rotationDegrees, largeArc, clockwise));
        return this;
    }

    /// <summary>Closes the current figure.</summary>
    public PathGeometry Close() { Commands.Add(new(CommandKind.Close, default, default, default, default, 0f, false, false)); return this; }

    /// <summary>Removes every command.</summary>
    public void Clear() => Commands.Clear();

    /// <summary>
    /// Circular arc. Angles are in degrees, clockwise from 12 o'clock (knob convention).
    /// </summary>
    public static PathGeometry Arc(Point center, float radius, float startDegrees, float sweepDegrees)
    {
        var path = new PathGeometry();
        AppendArc(path, center, radius, startDegrees, sweepDegrees, moveToStart: true);
        return path;
    }

    /// <summary>Pie slice (closed). Angles in degrees, clockwise from 12 o'clock.</summary>
    public static PathGeometry Pie(Point center, float radius, float startDegrees, float sweepDegrees)
    {
        var path = new PathGeometry().MoveTo(center);
        path.LineTo(PointOnCircle(center, radius, startDegrees));
        AppendArc(path, center, radius, startDegrees, sweepDegrees, moveToStart: false);
        return path.Close();
    }

    /// <summary>Thick ring segment (closed annulus sector), handy for filled value arcs.</summary>
    public static PathGeometry RingSegment(Point center, float innerRadius, float outerRadius, float startDegrees, float sweepDegrees)
    {
        var path = new PathGeometry();
        AppendArc(path, center, outerRadius, startDegrees, sweepDegrees, moveToStart: true);
        path.LineTo(PointOnCircle(center, innerRadius, startDegrees + sweepDegrees));
        AppendArc(path, center, innerRadius, startDegrees + sweepDegrees, -sweepDegrees, moveToStart: false);
        return path.Close();
    }

    /// <summary>Closed polygon.</summary>
    public static PathGeometry Polygon(ReadOnlySpan<Point> points)
    {
        var path = new PathGeometry();
        if (points.Length == 0) return path;
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++) path.LineTo(points[i]);
        return path.Close();
    }

    /// <summary>Open polyline.</summary>
    public static PathGeometry Polyline(ReadOnlySpan<Point> points)
    {
        var path = new PathGeometry();
        if (points.Length == 0) return path;
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++) path.LineTo(points[i]);
        return path;
    }

    /// <summary>Rounded rectangle with individual corner radii (top-left, top-right, bottom-right, bottom-left).</summary>
    public static PathGeometry RoundedRect(Rect r, float tl, float tr, float br, float bl)
    {
        float max = MathF.Min(r.Width, r.Height) * 0.5f;
        tl = Math.Clamp(tl, 0f, max); tr = Math.Clamp(tr, 0f, max); br = Math.Clamp(br, 0f, max); bl = Math.Clamp(bl, 0f, max);
        var p = new PathGeometry().MoveTo(r.X + tl, r.Y).LineTo(r.Right - tr, r.Y);
        if (tr > 0f) AppendArc(p, new Point(r.Right - tr, r.Y + tr), tr, 0f, 90f, moveToStart: false);
        p.LineTo(r.Right, r.Bottom - br);
        if (br > 0f) AppendArc(p, new Point(r.Right - br, r.Bottom - br), br, 90f, 90f, moveToStart: false);
        p.LineTo(r.X + bl, r.Bottom);
        if (bl > 0f) AppendArc(p, new Point(r.X + bl, r.Bottom - bl), bl, 180f, 90f, moveToStart: false);
        p.LineTo(r.X, r.Y + tl);
        if (tl > 0f) AppendArc(p, new Point(r.X + tl, r.Y + tl), tl, 270f, 90f, moveToStart: false);
        return p.Close();
    }

    /// <summary>Point on a circle; angle in degrees clockwise from 12 o'clock.</summary>
    public static Point PointOnCircle(Point center, float radius, float degrees)
    {
        float rad = degrees * (MathF.PI / 180f);
        return new Point(center.X + radius * MathF.Sin(rad), center.Y - radius * MathF.Cos(rad));
    }

    /// <summary>
    /// Appends a circular arc as cubic Bézier segments of at most 90° (max radial error ≈ 0.03 % of the radius).
    /// Béziers render identically on every Direct2D implementation (Wine ignores <c>AddArc</c>).
    /// </summary>
    private static void AppendArc(PathGeometry path, Point center, float radius, float startDegrees, float sweepDegrees, bool moveToStart)
    {
        sweepDegrees = Math.Clamp(sweepDegrees, -360f, 360f);
        if (moveToStart) path.MoveTo(PointOnCircle(center, radius, startDegrees));
        if (MathF.Abs(sweepDegrees) < 0.01f) return;
        int segments = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweepDegrees) / 90f - 1e-4f));
        float step = sweepDegrees / segments;
        float k = 4f / 3f * MathF.Tan(step * (MathF.PI / 180f) / 4f) * radius;
        for (int i = 0; i < segments; i++)
        {
            float a0 = (startDegrees + step * i) * (MathF.PI / 180f);
            float a1 = (startDegrees + step * (i + 1)) * (MathF.PI / 180f);
            // Point on circle: (sin a, -cos a); clockwise tangent: (cos a, sin a).
            var p0 = new Point(center.X + radius * MathF.Sin(a0), center.Y - radius * MathF.Cos(a0));
            var p3 = new Point(center.X + radius * MathF.Sin(a1), center.Y - radius * MathF.Cos(a1));
            path.CubicTo(new Point(p0.X + k * MathF.Cos(a0), p0.Y + k * MathF.Sin(a0)),
                         new Point(p3.X - k * MathF.Cos(a1), p3.Y - k * MathF.Sin(a1)), p3);
        }
    }
}
