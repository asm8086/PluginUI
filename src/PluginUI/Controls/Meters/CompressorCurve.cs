// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// Static transfer curve of a compressor / limiter (input level → output level, in dB) with threshold, ratio and soft
/// knee, the unity line, and a moving dot showing the current input level on the curve. The threshold and the ratio
/// can be edited by dragging, optionally bound to parameters.
/// </summary>
/// <remarks>
/// <para><b>Editing.</b> Drag the threshold handle (on the curve at the threshold) to move the threshold; drag anywhere
/// above the threshold vertically to change the ratio (up = gentler, down = harder; Shift = fine). Double-click
/// resets both to their defaults. Bound parameters receive complete begin / perform / end gestures.</para>
/// <para><b>Parameters.</b> <see cref="ThresholdParameter"/>, <see cref="RatioParameter"/> and <see cref="KneeParameter"/>
/// are read through <see cref="IParameter.ToPlain"/>: their plain values must be in dB, ratio (e.g. 4 for 4:1) and dB.
/// Unbound, the <see cref="ThresholdDb"/>, <see cref="Ratio"/> and <see cref="KneeDb"/> properties are used and
/// <see cref="CurveChanged"/> is raised on user edits.</para>
/// <para>The curve is computed by <see cref="CompressorMath.OutputDb"/> (quadratic soft knee).</para>
/// </remarks>
public class CompressorCurve : Control
{
    private enum DragPart { None, Threshold, Ratio }

    private float _threshold = -18f;
    private float _ratio = 4f;
    private float _knee = 6f;
    private DragPart _drag;
    private Point _dragStart;
    private float _dragStartThreshold, _dragStartRatio;
    private readonly LevelBallistics _input = new() { ReleaseDbPerSecond = 40f, HoldSeconds = 0f, FloorDb = -120f };
    private RefreshTicker? _ticker;

    /// <summary>Creates a compressor curve.</summary>
    public CompressorCurve()
    {
        Cursor = StandardCursor.Hand;
    }

    /// <summary>Threshold in dB (used when <see cref="ThresholdParameter"/> is <c>null</c>).</summary>
    [Category("Value")]
    public float ThresholdDb { get => ThresholdParameter is { } p ? (float)p.ToPlain(p.NormalizedValue) : _threshold; set { _threshold = value; InvalidateVisual(); } }

    /// <summary>Ratio, ≥ 1 (used when <see cref="RatioParameter"/> is <c>null</c>); use a large value (e.g. 100) for a limiter.</summary>
    [Category("Value")]
    public float Ratio { get => RatioParameter is { } p ? (float)p.ToPlain(p.NormalizedValue) : _ratio; set { _ratio = MathF.Max(1f, value); InvalidateVisual(); } }

    /// <summary>Knee width in dB (used when <see cref="KneeParameter"/> is <c>null</c>).</summary>
    [Category("Value")]
    public float KneeDb { get => KneeParameter is { } p ? (float)p.ToPlain(p.NormalizedValue) : _knee; set { _knee = MathF.Max(0f, value); InvalidateVisual(); } }

    /// <summary>Make-up gain in dB added to the drawn output.</summary>
    [Category("Value")]
    public float MakeupDb { get; set; }

    /// <summary>Threshold parameter (plain value in dB), or <c>null</c>.</summary>
    [Category("Value")]
    public IParameter? ThresholdParameter { get; set; }

    /// <summary>Ratio parameter (plain value = ratio), or <c>null</c>.</summary>
    [Category("Value")]
    public IParameter? RatioParameter { get; set; }

    /// <summary>Knee parameter (plain value in dB), or <c>null</c>.</summary>
    [Category("Value")]
    public IParameter? KneeParameter { get; set; }

    /// <summary>Lowest level of both axes in dB.</summary>
    [Category("Data")]
    public float MinDb { get; set; } = -60f;

    /// <summary>Highest level of both axes in dB.</summary>
    [Category("Data")]
    public float MaxDb { get; set; } = 6f;

    /// <summary>Lowest threshold reachable by dragging (unbound).</summary>
    [Category("Behavior")]
    public float MinThresholdDb { get; set; } = -60f;

    /// <summary>Highest ratio reachable by dragging (unbound).</summary>
    [Category("Behavior")]
    public float MaxRatio { get; set; } = 30f;

    /// <summary>Allows editing threshold and ratio with the mouse.</summary>
    [Category("Behavior")]
    public bool IsEditable { get; set; } = true;

    /// <summary>Input peak level (linear) published by the audio thread, shown as a dot on the curve.</summary>
    [Browsable(false)]
    public PeakValue? InputLevelSource { get; set; }

    /// <summary>Curve color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? CurveColor { get; set; }

    /// <summary>Raised after the user changed the threshold or the ratio of an unbound curve.</summary>
    public event Action<CompressorCurve>? CurveChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(160f, 160f);

    /// <summary>Output level in dB for an input level, with the current settings and make-up gain.</summary>
    /// <param name="inputDb">Input level in dB.</param>
    /// <returns>Output level in dB.</returns>
    public double OutputDb(double inputDb) => CompressorMath.OutputDb(inputDb, ThresholdDb, Ratio, KneeDb) + MakeupDb;

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, UpdateInput));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private void UpdateInput(double elapsed)
    {
        if (InputLevelSource is not { } source) return;
        float peak = source.ReadAndReset();
        if (_input.Update(Decibels.FromGain(peak, _input.FloorDb), elapsed)) InvalidateVisual();
    }

    private Rect GraphArea(Rect widget) => widget.CenteredSquare().Deflate(new Thickness(22f, 4f, 4f, 16f));

    private float X(float db, Rect a) => a.X + (db - MinDb) / MathF.Max(1f, MaxDb - MinDb) * a.Width;

    private float Y(float db, Rect a) => GraphHelpers.ValueToY(db, MinDb, MaxDb, a);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget.CenteredSquare());
        var a = GraphArea(widget);
        // Grid every 12 dB on both axes.
        var grid = p.Border.WithAlpha(0.5f);
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Right, Ellipsis = false };
        for (float db = MathF.Ceiling(MinDb / 12f) * 12f; db <= MaxDb; db += 12f)
        {
            float x = MathF.Round(X(db, a)) + 0.5f, y = MathF.Round(Y(db, a)) + 0.5f;
            dc.DrawLine(new Point(x, a.Y), new Point(x, a.Bottom), grid, 1f);
            dc.DrawLine(new Point(a.X, y), new Point(a.Right, y), grid, 1f);
            dc.DrawText(InvariantNumber.Format(db, "0"), new Rect(a.X - 22f, y - 6f, 19f, 11f), label, p.TextDim);
            dc.DrawText(InvariantNumber.Format(db, "0"), new Rect(x - 12f, a.Bottom + 2f, 24f, 11f), label with { Align = HorizontalAlignment.Center }, p.TextDim);
        }
        dc.PushClip(a);
        dc.DrawLine(new Point(X(MinDb, a), Y(MinDb, a)), new Point(X(MaxDb, a), Y(MaxDb, a)), p.TextDim.WithAlpha(0.45f), 1f, StrokeStyle.Dashed);

        // Transfer curve.
        int n = Math.Max(16, (int)a.Width);
        var points = new Point[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float inDb = MinDb + (MaxDb - MinDb) * i / n;
            points[i] = new Point(X(inDb, a), Y((float)OutputDb(inDb), a));
        }
        var color = CurveColor ?? style.Accent;
        var fill = new Point[n + 3];
        points.CopyTo(fill, 0);
        fill[n + 1] = new Point(a.Right, a.Bottom);
        fill[n + 2] = new Point(a.X, a.Bottom);
        dc.FillPolygon(fill, color.WithAlpha(0.12f));
        dc.DrawPolyline(points, color, 2f, StrokeStyle.Round);

        // Threshold handle.
        float t = ThresholdDb;
        var handle = new Point(X(t, a), Y((float)OutputDb(t), a));
        dc.DrawLine(new Point(handle.X, a.Y), new Point(handle.X, a.Bottom), color.WithAlpha(0.35f), 1f, StrokeStyle.Dashed);
        if (IsEditable) dc.FillCircle(handle, _drag == DragPart.Threshold ? 5.5f : 4.5f, color);

        // Current input level.
        if (InputLevelSource is not null && _input.LevelDb > MinDb + 0.5f)
        {
            float level = _input.LevelDb;
            var dot = new Point(X(level, a), Y((float)OutputDb(level), a));
            dc.FillCircle(dot, 4f, p.Text);
            dc.DrawCircle(dot, 6f, p.Text.WithAlpha(0.4f), 1.5f);
        }
        dc.PopClip();

        var info = $"{InvariantNumber.Format(t, "0.0")} dB · {(Ratio >= 50f ? "∞" : InvariantNumber.Format(Ratio, "0.#"))}:1";
        dc.DrawText(info, new Rect(a.X + 4f, a.Y + 2f, a.Width - 8f, 12f), Theme.Typography.Small with { Size = 10f, Align = HorizontalAlignment.Left }, p.TextDim);
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEditable || !IsEffectivelyEnabled) return;
        var a = GraphArea(Bounds);
        if (e.ClickCount == 2)
        {
            ResetToDefaults();
            e.Handled = true;
            return;
        }
        var handle = new Point(X(ThresholdDb, a), Y((float)OutputDb(ThresholdDb), a));
        _drag = (e.Position - handle).Length <= 10f || e.Position.X < handle.X ? DragPart.Threshold : DragPart.Ratio;
        _dragStart = e.Position;
        _dragStartThreshold = ThresholdDb;
        _dragStartRatio = Ratio;
        if (_drag == DragPart.Threshold) ThresholdParameter?.BeginEdit();
        else RatioParameter?.BeginEdit();
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == DragPart.None) return;
        var a = GraphArea(Bounds);
        float fine = e.Shift ? 0.1f : 1f;
        float dbPerPixel = (MaxDb - MinDb) / MathF.Max(1f, a.Width);
        if (_drag == DragPart.Threshold)
        {
            float delta = ((e.Position.X - _dragStart.X) - (e.Position.Y - _dragStart.Y)) * 0.5f * dbPerPixel * fine;
            SetThreshold(_dragStartThreshold + delta);
        }
        else
        {
            // Up = gentler ratio; logarithmic so that 1:1 … 30:1 is reachable smoothly.
            float octaves = (e.Position.Y - _dragStart.Y) / 60f * fine;
            SetRatio(_dragStartRatio * MathF.Pow(2f, octaves));
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == DragPart.None || e.Button != MouseButton.Left) return;
        EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrag();
    }

    private void EndDrag()
    {
        if (_drag == DragPart.Threshold) ThresholdParameter?.EndEdit();
        else if (_drag == DragPart.Ratio) RatioParameter?.EndEdit();
        _drag = DragPart.None;
        InvalidateVisual();
    }

    private void SetThreshold(float db)
    {
        if (ThresholdParameter is { } p) p.PerformEdit(p.ToNormalized(db));
        else
        {
            _threshold = Math.Clamp(db, MinThresholdDb, MaxDb);
            CurveChanged?.Invoke(this);
        }
        InvalidateVisual();
    }

    private void SetRatio(float ratio)
    {
        if (RatioParameter is { } p) p.PerformEdit(p.ToNormalized(ratio));
        else
        {
            _ratio = Math.Clamp(ratio, 1f, MaxRatio);
            CurveChanged?.Invoke(this);
        }
        InvalidateVisual();
    }

    private void ResetToDefaults()
    {
        foreach (var p in new[] { ThresholdParameter, RatioParameter })
        {
            if (p is null) continue;
            p.BeginEdit();
            p.PerformEdit(p.DefaultNormalizedValue);
            p.EndEdit();
        }
        if (ThresholdParameter is null) _threshold = -18f;
        if (RatioParameter is null) _ratio = 4f;
        if (ThresholdParameter is null || RatioParameter is null) CurveChanged?.Invoke(this);
        InvalidateVisual();
    }
}
