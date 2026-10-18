// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Stage of an ADSR envelope.</summary>
public enum EnvelopeStage
{
    /// <summary>Attack time.</summary>
    Attack,
    /// <summary>Decay time.</summary>
    Decay,
    /// <summary>Sustain level.</summary>
    Sustain,
    /// <summary>Release time.</summary>
    Release,
}

/// <summary>
/// Graphical ADSR editor: drag the peak (attack), the decay corner (decay + sustain), the sustain plateau
/// (sustain) or the end point (release). Values are normalized (times scaled by the plug-in) and each can be
/// bound to a parameter; double-click a point resets it.
/// </summary>
public class EnvelopeEditor : Control
{
    private enum Handle { None, Attack, Decay, Sustain, Release }

    private readonly BoundValue[] _stages = new BoundValue[4];
    private Handle _drag;
    private Handle _hover;
    private Point _last;
    private double _dragA, _dragB;
    private readonly Point[] _points = new Point[5];

    /// <summary>Creates an envelope editor.</summary>
    public EnvelopeEditor()
    {
        _stages[0] = new BoundValue(OnStageChanged, 0.15, 0.1);
        _stages[1] = new BoundValue(OnStageChanged, 0.3, 0.3);
        _stages[2] = new BoundValue(OnStageChanged, 0.6, 0.7);
        _stages[3] = new BoundValue(OnStageChanged, 0.4, 0.4);
    }

    /// <summary>Attack time (normalized).</summary>
    [Category("Value")]
    public double Attack { get => _stages[0].Value; set => _stages[0].SetByUser(value); }

    /// <summary>Decay time (normalized).</summary>
    [Category("Value")]
    public double Decay { get => _stages[1].Value; set => _stages[1].SetByUser(value); }

    /// <summary>Sustain level (normalized).</summary>
    [Category("Value")]
    public double Sustain { get => _stages[2].Value; set => _stages[2].SetByUser(value); }

    /// <summary>Release time (normalized).</summary>
    [Category("Value")]
    public double Release { get => _stages[3].Value; set => _stages[3].SetByUser(value); }

    /// <summary>Parameter bound to the attack time.</summary>
    [Category("Value")]
    public IParameter? AttackParameter { get => _stages[0].Parameter; set => _stages[0].Parameter = value; }

    /// <summary>Parameter bound to the decay time.</summary>
    [Category("Value")]
    public IParameter? DecayParameter { get => _stages[1].Parameter; set => _stages[1].Parameter = value; }

    /// <summary>Parameter bound to the sustain level.</summary>
    [Category("Value")]
    public IParameter? SustainParameter { get => _stages[2].Parameter; set => _stages[2].Parameter = value; }

    /// <summary>Parameter bound to the release time.</summary>
    [Category("Value")]
    public IParameter? ReleaseParameter { get => _stages[3].Parameter; set => _stages[3].Parameter = value; }

    /// <summary>Value of a stage (normalized).</summary>
    public double this[EnvelopeStage stage] { get => _stages[(int)stage].Value; set => _stages[(int)stage].SetByUser(value); }

    /// <summary>Sets the formatter of a stage's value text.</summary>
    public void SetFormatter(EnvelopeStage stage, Func<double, string>? formatter) => _stages[(int)stage].Formatter = formatter;

    /// <summary>Curve bend of the segments (0 = straight lines, 1 = strongly exponential).</summary>
    [Category("Appearance")]
    public float Curvature { get; set; } = 0.55f;

    /// <summary>Sensitivity multiplier while Shift is held.</summary>
    [Category("Behavior")]
    public float FineFactor { get; set; } = 0.1f;

    /// <summary>Raised when a stage changes.</summary>
    public event Action<EnvelopeEditor, EnvelopeStage, double>? StageChanged;

    /// <inheritdoc />
    public override string? ValueText
    {
        get
        {
            var h = _drag != Handle.None ? _drag : _hover;
            return h switch
            {
                Handle.Attack => "A " + _stages[0].Text,
                Handle.Decay => $"D {_stages[1].Text} · S {_stages[2].Text}",
                Handle.Sustain => "S " + _stages[2].Text,
                Handle.Release => "R " + _stages[3].Text,
                _ => $"A {_stages[0].Text}  D {_stages[1].Text}  S {_stages[2].Text}  R {_stages[3].Text}",
            };
        }
    }

    /// <inheritdoc />
    protected override Size DefaultSize => new(260f, 130f);

    private void OnStageChanged(BoundValue stage)
    {
        int index = Array.IndexOf(_stages, stage);
        if (index >= 0) StageChanged?.Invoke(this, (EnvelopeStage)index, stage.Value);
        InvalidateVisual();
    }

    private Rect PlotRect()
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        return Inner(widget);
    }

    private static Rect Inner(Rect widget) => widget.Inflate(-8f, -8f);

    private float SegmentWidth(Rect area) => area.Width / 3.4f;

    private void ComputePoints(Rect area)
    {
        float seg = SegmentWidth(area);
        float x = area.X;
        _points[0] = new Point(x, area.Bottom);
        x += seg * ToDisplay(Attack);
        _points[1] = new Point(x, area.Y);
        x += seg * ToDisplay(Decay);
        float sy = area.Bottom - area.Height * (float)Sustain;
        _points[2] = new Point(x, sy);
        x += seg * 0.4f;
        _points[3] = new Point(x, sy);
        x += seg * ToDisplay(Release);
        _points[4] = new Point(x, area.Bottom);
    }

    // Times are drawn on a square-root axis: short times get room, long ones still fit.
    private static float ToDisplay(double v) => MathF.Sqrt((float)Math.Clamp(v, 0.0, 1.0));

    private static double FromDisplay(double u) => Math.Clamp(u, 0.0, 1.0) * Math.Clamp(u, 0.0, 1.0);

    private PathGeometry CurvePath(bool closed, Rect area)
    {
        var path = new PathGeometry().MoveTo(_points[0]);
        float k = Math.Clamp(Curvature, 0f, 1f);
        // RC-style segments: fast change first, then a slow approach to the target.
        Segment(path, _points[0], _points[1], k);
        Segment(path, _points[1], _points[2], k);
        path.LineTo(_points[3]);
        Segment(path, _points[3], _points[4], k);
        if (closed)
        {
            path.LineTo(new Point(_points[4].X, area.Bottom)).LineTo(_points[0]).Close();
        }
        return path;
    }

    private static void Segment(PathGeometry path, Point a, Point b, float k)
    {
        // Quadratic control point pulled toward the (start x, target y) corner: exponential look.
        var corner = new Point(a.X, b.Y);
        var mid = Point.Lerp(a, b, 0.5f);
        path.QuadTo(Point.Lerp(mid, corner, k), b);
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = MathF.Min(4f, CornerRadiusFor(widget, style));
        dc.FillRoundedRect(widget, radius, style.Background);
        var area = Inner(widget);
        var g = p.Border.Fade(0.45f);
        for (int i = 1; i < 4; i++)
        {
            float y = MathF.Round(area.Y + area.Height * i / 4f) + 0.5f;
            dc.DrawLine(new Point(widget.X + 1f, y), new Point(widget.Right - 1f, y), g);
        }
        ComputePoints(area);
        // Stage separators.
        for (int i = 1; i <= 3; i++)
        {
            float x = MathF.Round(_points[i].X) + 0.5f;
            dc.DrawLine(new Point(x, widget.Y + 1f), new Point(x, widget.Bottom - 1f), g, 1f, StrokeStyle.Dashed);
        }
        dc.FillGeometry(CurvePath(closed: true, area), style.Accent.Fade(0.18f));
        dc.DrawGeometry(CurvePath(closed: false, area), style.Accent, 2f, StrokeStyle.Round);

        DrawHandle(dc, _points[1], Handle.Attack, style);
        DrawHandle(dc, _points[2], Handle.Decay, style);
        DrawHandle(dc, Point.Lerp(_points[2], _points[3], 0.5f), Handle.Sustain, style, small: true);
        DrawHandle(dc, _points[4], Handle.Release, style);
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame);
    }

    private void DrawHandle(DrawingContext dc, Point c, Handle handle, VectorStyle style, bool small = false)
    {
        bool hot = _hover == handle || _drag == handle;
        float r = small ? 3f : 4.5f;
        if (hot) dc.FillCircle(c, r + 4f, style.Accent.Fade(0.25f));
        dc.FillCircle(c, r, hot ? style.Accent : style.Background);
        dc.DrawCircle(c, r, style.Accent, 1.5f);
    }

    private Handle HitHandle(Point p)
    {
        ComputePoints(PlotRect());
        const float grab = 9f;
        if ((p - _points[1]).Length <= grab) return Handle.Attack;
        if ((p - _points[2]).Length <= grab) return Handle.Decay;
        if ((p - _points[4]).Length <= grab) return Handle.Release;
        if ((p - Point.Lerp(_points[2], _points[3], 0.5f)).Length <= grab || (p.X > _points[2].X && p.X < _points[3].X && MathF.Abs(p.Y - _points[2].Y) <= grab)) return Handle.Sustain;
        return Handle.None;
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        var h = HitHandle(e.Position);
        if (h == Handle.None) return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            foreach (var s in Stages(h)) s.SetByUser(s.DefaultValue);
            return;
        }
        _drag = h;
        _last = e.Position;
        var stages = Stages(h);
        // Time stages are dragged in display (square-root) space, levels linearly.
        _dragA = h == Handle.Sustain ? stages[0].Value : ToDisplay(stages[0].Value);
        _dragB = stages.Length > 1 ? stages[1].Value : 0;
        foreach (var s in stages) s.Begin();
        IsPressed = true;
        InvalidateVisual();
    }

    private BoundValue[] Stages(Handle h) => h switch
    {
        Handle.Attack => [_stages[0]],
        Handle.Decay => [_stages[1], _stages[2]],
        Handle.Sustain => [_stages[2]],
        Handle.Release => [_stages[3]],
        _ => [],
    };

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == Handle.None)
        {
            var hover = IsMouseOver ? HitHandle(e.Position) : Handle.None;
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover switch { Handle.None => StandardCursor.Default, Handle.Sustain => StandardCursor.SizeNS, Handle.Decay => StandardCursor.SizeAll, _ => StandardCursor.SizeWE };
                InvalidateVisual();
            }
            return;
        }
        var area = PlotRect();
        var d = e.Position - _last;
        _last = e.Position;
        float k = e.Shift ? FineFactor : 1f;
        double dx = d.X / MathF.Max(10f, SegmentWidth(area)) * k;
        double dy = -d.Y / MathF.Max(10f, area.Height) * k;
        switch (_drag)
        {
            case Handle.Attack:
                _dragA = Math.Clamp(_dragA + dx, 0.0, 1.0);
                _stages[0].Set(FromDisplay(_dragA));
                break;
            case Handle.Decay:
                _dragA = Math.Clamp(_dragA + dx, 0.0, 1.0);
                _dragB = Math.Clamp(_dragB + dy, 0.0, 1.0);
                _stages[1].Set(FromDisplay(_dragA));
                _stages[2].Set(_dragB);
                break;
            case Handle.Sustain:
                _dragA = Math.Clamp(_dragA + dy, 0.0, 1.0);
                _stages[2].Set(_dragA);
                break;
            case Handle.Release:
                _dragA = Math.Clamp(_dragA + dx, 0.0, 1.0);
                _stages[3].Set(FromDisplay(_dragA));
                break;
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = Handle.None;
    }

    private void EndDrag()
    {
        if (_drag == Handle.None) return;
        _drag = Handle.None;
        IsPressed = false;
        foreach (var s in _stages) s.End();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        foreach (var s in _stages) s.Attach();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        _drag = Handle.None;
        foreach (var s in _stages) s.Detach();
        base.OnDetached();
    }
}
