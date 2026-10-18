// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Breakpoint of a <see cref="CurveEditor"/>: position (0..1), value (0..1) and curvature of the segment that
/// starts at this point (−1..1: negative = fast start, positive = slow start, 0 = straight).
/// </summary>
/// <param name="X">Position in [0, 1].</param>
/// <param name="Y">Value in [0, 1].</param>
/// <param name="Curvature">Curvature of the segment towards the next point, in [−1, 1].</param>
public readonly record struct CurvePoint(double X, double Y, double Curvature = 0.0);

/// <summary>
/// Multi-segment envelope / LFO shape editor (MSEG, as in Serum or Vital): draggable breakpoints joined by curved
/// segments, grid snapping, play cursor, and exact evaluation of the shape for the audio engine (<see cref="Evaluate(double)"/>).
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Drag a point to move it (the first and last points keep their position when
/// <see cref="LockEndpoints"/> is set); double-click on the background to add a point, on a point to delete it; drag
/// the small handle in the middle of a segment vertically to bend it. Points snap to the grid unless Shift is held
/// (<see cref="SnapToGrid"/>).</para>
/// <para><b>Curvature.</b> A segment with curvature c is <c>(e^(k·t) − 1) / (e^k − 1)</c> with <c>k = 8c</c>: smooth
/// exponential-like shapes, exactly reproduced by <see cref="Evaluate(double)"/>.</para>
/// <para><b>Audio thread.</b> Copy <see cref="Points"/> (for example into an immutable array published through a
/// volatile field) after each <see cref="CurveChanged"/>, and evaluate the copy with <see cref="Evaluate(IReadOnlyList{CurvePoint}, double)"/>.</para>
/// </remarks>
public class CurveEditor : Control
{
    private readonly List<CurvePoint> _points = [new(0.0, 0.0), new(0.5, 1.0), new(1.0, 0.0)];
    private int _dragPoint = -1;
    private int _dragSegment = -1;
    private double _dragStartCurvature;
    private float _dragStartY;
    private int _hoverPoint = -1;
    private int _hoverSegment = -1;
    private RefreshTicker? _ticker;
    private double _lastPlay = double.NaN;

    /// <summary>Creates a curve editor with a triangle shape.</summary>
    public CurveEditor()
    {
        Cursor = StandardCursor.Hand;
    }

    /// <summary>The breakpoints, sorted by X (read-only view; edit with <see cref="SetPoints"/> or the mouse).</summary>
    [Browsable(false)]
    public IReadOnlyList<CurvePoint> Points => _points;

    /// <summary>Vertical grid divisions (0 = none); also the snapping step.</summary>
    [Category("Behavior")]
    public int GridX { get; set; } = 8;

    /// <summary>Horizontal grid divisions (0 = none); also the snapping step.</summary>
    [Category("Behavior")]
    public int GridY { get; set; } = 4;

    /// <summary>Snaps dragged points to the grid (hold Shift to move freely).</summary>
    [Category("Behavior")]
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Keeps the first point at X = 0 and the last at X = 1.</summary>
    [Category("Behavior")]
    public bool LockEndpoints { get; set; } = true;

    /// <summary>Maximum number of points.</summary>
    [Category("Behavior")]
    public int MaxPoints { get; set; } = 64;

    /// <summary>Draws a center line and labels values from −1 to +1 (LFO shapes).</summary>
    [Category("Appearance")]
    public bool IsBipolar { get; set; }

    /// <summary>Fills the area below the curve.</summary>
    [Category("Appearance")]
    public bool Filled { get; set; } = true;

    /// <summary>Curve color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? CurveColor { get; set; }

    /// <summary>Play cursor position in [0, 1] (NaN = hidden).</summary>
    [Category("Value")]
    public double PlayPosition { get; set; } = double.NaN;

    /// <summary>Function polled every frame for the play cursor.</summary>
    [Browsable(false)]
    public Func<double>? PlayPositionSource { get; set; }

    /// <summary>Raised after the user changed the shape (or after <see cref="SetPoints"/>).</summary>
    public event Action<CurveEditor>? CurveChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(320f, 140f);

    /// <summary>Replaces all points (sorted by X; values clamped; at least two points).</summary>
    /// <param name="points">The new points.</param>
    /// <exception cref="ArgumentException">Fewer than two points.</exception>
    public void SetPoints(IEnumerable<CurvePoint> points)
    {
        var list = points.Select(p => new CurvePoint(Math.Clamp(p.X, 0.0, 1.0), Math.Clamp(p.Y, 0.0, 1.0), Math.Clamp(p.Curvature, -1.0, 1.0)))
            .OrderBy(p => p.X).ToList();
        if (list.Count < 2) throw new ArgumentException("A curve needs at least two points.", nameof(points));
        _points.Clear();
        _points.AddRange(list);
        Changed();
    }

    /// <summary>Value of the shape at <paramref name="x"/> (0..1).</summary>
    /// <param name="x">Position in [0, 1].</param>
    /// <returns>Value in [0, 1].</returns>
    public double Evaluate(double x) => Evaluate(_points, x);

    /// <summary>Value of a shape at <paramref name="x"/> (usable on a copy of <see cref="Points"/> on the audio thread).</summary>
    /// <param name="points">Points sorted by X.</param>
    /// <param name="x">Position in [0, 1].</param>
    /// <returns>Value in [0, 1] (0 for an empty list).</returns>
    public static double Evaluate(IReadOnlyList<CurvePoint> points, double x)
    {
        if (points.Count == 0) return 0.0;
        if (x <= points[0].X) return points[0].Y;
        for (int i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (x > b.X) continue;
            double span = b.X - a.X;
            double t = span <= 1e-12 ? 1.0 : (x - a.X) / span;
            return a.Y + (b.Y - a.Y) * Shape(t, a.Curvature);
        }
        return points[^1].Y;
    }

    /// <summary>Curvature warp of a normalized segment position.</summary>
    /// <param name="t">Position inside the segment, 0..1.</param>
    /// <param name="curvature">Curvature, −1..1.</param>
    /// <returns>Warped position, 0..1.</returns>
    public static double Shape(double t, double curvature)
    {
        double k = Math.Clamp(curvature, -1.0, 1.0) * 8.0;
        if (Math.Abs(k) < 1e-4) return t;
        return (Math.Exp(k * t) - 1.0) / (Math.Exp(k) - 1.0);
    }

    private void Changed()
    {
        CurveChanged?.Invoke(this);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- geometry

    private Rect Area(Rect widget) => widget.Deflate(6f);

    private Point ToScreen(CurvePoint p, Rect a) => new(a.X + (float)p.X * a.Width, a.Bottom - (float)p.Y * a.Height);

    private Point SegmentHandle(int i, Rect a)
    {
        var p0 = _points[i];
        var p1 = _points[i + 1];
        double x = (p0.X + p1.X) * 0.5;
        return ToScreen(new CurvePoint(x, Evaluate(x)), a);
    }

    private int HitPoint(Point p)
    {
        var a = Area(Bounds);
        for (int i = _points.Count - 1; i >= 0; i--)
        {
            if ((ToScreen(_points[i], a) - p).Length <= 7f) return i;
        }
        return -1;
    }

    private int HitSegment(Point p)
    {
        var a = Area(Bounds);
        for (int i = 0; i < _points.Count - 1; i++)
        {
            if ((SegmentHandle(i, a) - p).Length <= 6f) return i;
        }
        return -1;
    }

    private double Snap(double v, int divisions, bool free) => !SnapToGrid || free || divisions <= 0 ? v : Math.Round(v * divisions) / divisions;

    // ---------------------------------------------------------------- live updates

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, _ =>
        {
            if (PlayPositionSource is { } s) PlayPosition = s();
            if (!PlayPosition.Equals(_lastPlay)) { _lastPlay = PlayPosition; InvalidateVisual(); }
        }));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var a = Area(widget);
        var grid = p.Border.WithAlpha(0.5f);
        for (int i = 1; i < GridX; i++)
        {
            float x = MathF.Round(a.X + a.Width * i / GridX) + 0.5f;
            dc.DrawLine(new Point(x, a.Y), new Point(x, a.Bottom), grid, 1f);
        }
        for (int i = 1; i < GridY; i++)
        {
            float y = MathF.Round(a.Y + a.Height * i / GridY) + 0.5f;
            dc.DrawLine(new Point(a.X, y), new Point(a.Right, y), IsBipolar && i * 2 == GridY ? p.Border : grid, 1f);
        }
        var color = CurveColor ?? style.Accent;
        int n = Math.Max(32, (int)a.Width);
        var curve = new Point[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double x = i / (double)n;
            curve[i] = ToScreen(new CurvePoint(x, Evaluate(x)), a);
        }
        if (Filled)
        {
            var fill = new Point[n + 3];
            curve.CopyTo(fill, 0);
            float baseY = IsBipolar ? a.Center.Y : a.Bottom;
            fill[n + 1] = new Point(a.Right, baseY);
            fill[n + 2] = new Point(a.X, baseY);
            dc.FillPolygon(fill, color.WithAlpha(0.18f));
        }
        dc.DrawPolyline(curve, color, 2f, StrokeStyle.Round);

        for (int i = 0; i < _points.Count - 1; i++)
        {
            var h = SegmentHandle(i, a);
            bool hot = i == _hoverSegment || i == _dragSegment;
            dc.DrawCircle(h, hot ? 4f : 3f, color.WithAlpha(hot ? 1f : 0.6f), 1.2f);
        }
        for (int i = 0; i < _points.Count; i++)
        {
            var pt = ToScreen(_points[i], a);
            bool hot = i == _hoverPoint || i == _dragPoint;
            dc.FillCircle(pt, hot ? 5.5f : 4.5f, color);
            dc.DrawCircle(pt, hot ? 5.5f : 4.5f, p.Background, 1f);
        }
        if (!double.IsNaN(PlayPosition))
        {
            double x = Math.Clamp(PlayPosition, 0.0, 1.0);
            var pt = ToScreen(new CurvePoint(x, Evaluate(x)), a);
            dc.DrawLine(new Point(pt.X, a.Y), new Point(pt.X, a.Bottom), p.Text.WithAlpha(0.5f), 1f);
            dc.FillCircle(pt, 3.5f, p.Text);
        }
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        int point = HitPoint(e.Position);
        if (e.ClickCount == 2)
        {
            if (point > 0 && point < _points.Count - 1)
            {
                _points.RemoveAt(point);
                Changed();
            }
            else if (point < 0 && _points.Count < MaxPoints)
            {
                var a = Area(Bounds);
                double x = Snap(Math.Clamp((e.Position.X - a.X) / a.Width, 0.0, 1.0), GridX, e.Shift);
                double y = Snap(Math.Clamp((a.Bottom - e.Position.Y) / a.Height, 0.0, 1.0), GridY, e.Shift);
                int index = _points.FindIndex(q => q.X > x);
                if (index <= 0) index = _points.Count - 1;
                _points.Insert(index, new CurvePoint(x, y));
                Changed();
            }
            e.Handled = true;
            return;
        }
        if (point >= 0)
        {
            _dragPoint = point;
        }
        else if (HitSegment(e.Position) is int segment and >= 0)
        {
            _dragSegment = segment;
            _dragStartCurvature = _points[segment].Curvature;
            _dragStartY = e.Position.Y;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var a = Area(Bounds);
        if (_dragPoint >= 0)
        {
            int i = _dragPoint;
            double x = Snap(Math.Clamp((e.Position.X - a.X) / a.Width, 0.0, 1.0), GridX, e.Shift);
            double y = Snap(Math.Clamp((a.Bottom - e.Position.Y) / a.Height, 0.0, 1.0), GridY, e.Shift);
            double lo = i > 0 ? _points[i - 1].X : 0.0, hi = i < _points.Count - 1 ? _points[i + 1].X : 1.0;
            if (LockEndpoints && (i == 0 || i == _points.Count - 1)) x = i == 0 ? 0.0 : 1.0;
            else x = Math.Clamp(x, lo, hi);
            _points[i] = _points[i] with { X = x, Y = y };
            Changed();
            return;
        }
        if (_dragSegment >= 0)
        {
            int i = _dragSegment;
            // Dragging up bends the segment towards its upper side whatever its direction.
            double direction = _points[i + 1].Y >= _points[i].Y ? -1.0 : 1.0;
            double delta = (e.Position.Y - _dragStartY) / 80.0 * direction * (e.Shift ? 0.2 : 1.0);
            _points[i] = _points[i] with { Curvature = Math.Clamp(_dragStartCurvature + delta, -1.0, 1.0) };
            Changed();
            return;
        }
        int hp = HitPoint(e.Position), hs = hp >= 0 ? -1 : HitSegment(e.Position);
        if (hp != _hoverPoint || hs != _hoverSegment)
        {
            _hoverPoint = hp;
            _hoverSegment = hs;
            Cursor = hs >= 0 ? StandardCursor.SizeNS : StandardCursor.Hand;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragPoint = -1;
        _dragSegment = -1;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _dragPoint = -1;
        _dragSegment = -1;
    }
}
