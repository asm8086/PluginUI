// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>How a <see cref="WavetableView"/> draws the table.</summary>
public enum WavetableDisplay
{
    /// <summary>Every frame stacked in depth, the current one highlighted (as in wavetable synthesizers).</summary>
    Stack3D,
    /// <summary>The current frame only, large.</summary>
    Single,
}

/// <summary>
/// Wavetable display and position control: the frames of a wavetable (one cycle each) stacked in depth or the current one
/// alone, the frame at the position interpolated between its neighbours. Drag up or down to scan the table; the position
/// is the value of the control (bind it to the position parameter).
/// </summary>
/// <remarks>
/// It is a <see cref="ValueControl"/>: wheel, arrows, double-click or Ctrl+click to reset, edit gestures and the slider
/// peer for screen readers come with it. Give the frames with <see cref="SetFrames"/> (copied; any number of samples
/// per frame, all frames the same length), or build a table with <see cref="CreateMorph"/>.
/// </remarks>
/// <example>
/// <code>
/// var table = new WavetableView { Parameter = Parameters[model.WavePosition] };
/// table.SetFrames(WavetableView.CreateMorph(64, 256, harmonics: 24));
/// </code>
/// </example>
public class WavetableView : ValueControl
{
    private float[][] _frames = [];
    private float[] _current = [];
    private RotaryLinearHelpers.DragAccumulator _drag;

    /// <summary>Creates an empty view (call <see cref="SetFrames"/>).</summary>
    public WavetableView()
    {
        Cursor = StandardCursor.SizeNS;
        Focusable = true;
        Formatter = v => FrameCount == 0 ? "—" : string.Format(CultureInfo.InvariantCulture, "{0} / {1}", FrameAt(v) + 1, FrameCount);
    }

    /// <summary>How the table is drawn.</summary>
    [Category("Appearance")]
    public WavetableDisplay Display { get; set; } = WavetableDisplay.Stack3D;

    /// <summary>Largest number of frames drawn in the 3D view (they are picked evenly; the current frame is always drawn).</summary>
    [Category("Appearance")]
    public int MaxDrawnFrames { get; set; } = 48;

    /// <summary>Fills under the current frame.</summary>
    [Category("Appearance")]
    public bool FillCurrent { get; set; } = true;

    /// <summary>Number of frames.</summary>
    [Browsable(false)]
    public int FrameCount => _frames.Length;

    /// <summary>Samples per frame.</summary>
    [Browsable(false)]
    public int FrameLength => _frames.Length == 0 ? 0 : _frames[0].Length;

    /// <summary>The frame at the current position, interpolated between its neighbours (a copy of the internal buffer).</summary>
    [Browsable(false)]
    public float[] CurrentFrame
    {
        get
        {
            UpdateCurrent();
            return (float[])_current.Clone();
        }
    }

    /// <inheritdoc />
    protected override Size DefaultSize => new(280f, 180f);

    /// <summary>Replaces the table (the frames are copied).</summary>
    /// <param name="frames">Frames, one cycle each, all the same length (at least 2 samples).</param>
    public void SetFrames(IEnumerable<float[]> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var copy = frames.Select(f => (float[])f.Clone()).ToArray();
        if (copy.Length > 0 && (copy[0].Length < 2 || copy.Any(f => f.Length != copy[0].Length)))
            throw new ArgumentException("Every frame needs the same length, at least 2 samples.", nameof(frames));
        _frames = copy;
        _current = copy.Length > 0 ? new float[copy[0].Length] : [];
        InvalidateVisual();
    }

    /// <summary>
    /// A demonstration table: from a sine (first frame) to a band-limited saw (last frame), the upper harmonics fading in
    /// along the table.
    /// </summary>
    /// <param name="frameCount">Number of frames.</param>
    /// <param name="frameLength">Samples per frame.</param>
    /// <param name="harmonics">Harmonics of the last frame.</param>
    public static float[][] CreateMorph(int frameCount = 64, int frameLength = 256, int harmonics = 24)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameLength, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(harmonics, 1);
        var frames = new float[frameCount][];
        for (int f = 0; f < frameCount; f++)
        {
            double t = frameCount == 1 ? 1.0 : f / (double)(frameCount - 1);
            var frame = new float[frameLength];
            double peak = 0.0;
            var values = new double[frameLength];
            for (int k = 1; k <= harmonics; k++)
            {
                // Harmonic k fades in once the morph passes (k − 1) / harmonics.
                double weight = k == 1 ? 1.0 : Math.Clamp(t * harmonics - (k - 1) + 1.0, 0.0, 1.0);
                if (weight <= 0.0) continue;
                double amplitude = weight / k;
                for (int i = 0; i < frameLength; i++) values[i] += amplitude * Math.Sin(2.0 * Math.PI * k * i / frameLength);
            }
            foreach (double v in values) peak = Math.Max(peak, Math.Abs(v));
            for (int i = 0; i < frameLength; i++) frame[i] = (float)(values[i] / Math.Max(peak, 1e-9));
            frames[f] = frame;
        }
        return frames;
    }

    private int FrameAt(double normalized) => FrameCount <= 1 ? 0 : (int)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * (FrameCount - 1));

    private void UpdateCurrent()
    {
        if (_frames.Length == 0) return;
        double position = Math.Clamp(Value, 0.0, 1.0) * (_frames.Length - 1);
        int a = (int)Math.Floor(position);
        int b = Math.Min(a + 1, _frames.Length - 1);
        float t = (float)(position - a);
        var fa = _frames[a];
        var fb = _frames[b];
        for (int i = 0; i < _current.Length; i++) _current[i] = fa[i] + (fb[i] - fa[i]) * t;
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var area = widget.Deflate(8f);
        if (_frames.Length == 0)
        {
            dc.DrawText("—", area, Theme.Typography.Normal with { Align = HorizontalAlignment.Center }, p.TextDim);
            return;
        }
        UpdateCurrent();
        var accent = style.Accent;
        if (Display == WavetableDisplay.Single || _frames.Length == 1)
        {
            dc.DrawLine(new Point(area.X, area.Center.Y), new Point(area.Right, area.Center.Y), p.Border, 1f);
            DrawFrame(dc, _current, area, accent, 2f, FillCurrent ? accent.WithAlpha(0.18f) : null, area.Center.Y);
            return;
        }

        // Oblique projection: the first frame at the front left, the last at the back right.
        float depthX = area.Width * 0.22f, depthY = area.Height * 0.38f;
        var front = new Rect(area.X, area.Y + depthY, area.Width - depthX, area.Height - depthY);
        int count = _frames.Length;
        int drawn = Math.Clamp(MaxDrawnFrames, 2, count);
        float current = (float)(Math.Clamp(Value, 0.0, 1.0) * (count - 1));
        var background = p.Background;
        // Back to front, each frame hiding what is behind it.
        for (int k = drawn - 1; k >= 0; k--)
        {
            int f = (int)Math.Round(k * (count - 1) / (double)(drawn - 1));
            float depth = f / (float)(count - 1);
            var r = front.Offset(depthX * depth, -depthY * depth);
            float distance = MathF.Abs(f - current) / count;
            var line = accent.WithAlpha(Math.Clamp(0.55f - distance * 1.6f, 0.12f, 0.55f));
            DrawFrame(dc, _frames[f], r, line, 1f, background.WithAlpha(0.35f), r.Bottom);
        }
        float currentDepth = current / (count - 1);
        var cr = front.Offset(depthX * currentDepth, -depthY * currentDepth);
        DrawFrame(dc, _current, cr, accent.Lighten(0.15f), 2.2f, FillCurrent ? accent.WithAlpha(0.2f) : null, cr.Center.Y);
    }

    private static void DrawFrame(DrawingContext dc, float[] frame, Rect r, Color line, float width, Color? fill, float baseline)
    {
        int n = Math.Min(frame.Length, Math.Max(16, (int)r.Width));
        var points = new Point[n];
        for (int i = 0; i < n; i++)
        {
            int index = (int)((long)i * (frame.Length - 1) / Math.Max(1, n - 1));
            points[i] = new Point(r.X + r.Width * i / (n - 1), r.Center.Y - Math.Clamp(frame[index], -1f, 1f) * r.Height * 0.45f);
        }
        if (fill is { } f)
        {
            var polygon = new Point[n + 2];
            points.CopyTo(polygon, 0);
            polygon[n] = new Point(r.Right, baseline);
            polygon[n + 1] = new Point(r.X, baseline);
            dc.FillPolygon(polygon, f);
        }
        dc.DrawPolyline(points, line, width, StrokeStyle.Round);
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left) return;
        if (TryHandleReset(e)) return;
        IsPressed = true;
        BeginGesture();
        _drag.Start(e.Position, Value);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsPressed) return;
        double factor = 1.0 / MathF.Max(10f, DragSensitivity) * (e.Shift ? FineFactor : 1f);
        UpdateGesture(_drag.Move(e.Position, 0f, -1f, factor));
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
}
