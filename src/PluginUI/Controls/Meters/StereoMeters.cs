// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// Goniometer (vector scope): plots the stereo signal as a cloud of points rotated by 45°, so that mono material is a
/// vertical line, wide material a broad cloud, and out-of-phase material a horizontal line. Reads two
/// <see cref="AudioRingBuffer"/> written by the audio thread (one per channel).
/// </summary>
/// <remarks>
/// Each point is (S, M) = ((R − L)/√2, (L + R)/√2), scaled by <see cref="Gain"/>. The most recent
/// <see cref="PointCount"/> samples are drawn, older ones fainter, which gives a short persistence without
/// accumulating an image.
/// </remarks>
/// <example>
/// <code>
/// // Processor: _left.Write(outL); _right.Write(outR);   Editor:
/// root.Add(new Goniometer { LeftSource = processor.ScopeLeft, RightSource = processor.ScopeRight });
/// </code>
/// </example>
public class Goniometer : Control
{
    private float[] _left = [];
    private float[] _right = [];
    private long _lastWritten = -1;
    private RefreshTicker? _ticker;

    /// <summary>Creates a goniometer.</summary>
    public Goniometer()
    {
        Focusable = false;
    }

    /// <summary>Left channel samples.</summary>
    [Browsable(false)]
    public AudioRingBuffer? LeftSource { get; set; }

    /// <summary>Right channel samples.</summary>
    [Browsable(false)]
    public AudioRingBuffer? RightSource { get; set; }

    /// <summary>Number of recent samples drawn.</summary>
    [Category("Data")]
    public int PointCount { get; set; } = 2048;

    /// <summary>Zoom factor (1 = full scale reaches the edge).</summary>
    [Category("Value")]
    public float Gain { get; set; } = 1f;

    /// <summary>Draws the L / R / M / S axes and labels.</summary>
    [Category("Appearance")]
    public bool ShowAxes { get; set; } = true;

    /// <summary>Point color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? TraceColor { get; set; }

    /// <summary>Size of a point in DIPs.</summary>
    [Category("Appearance")]
    public float PointSize { get; set; } = 1.3f;

    /// <inheritdoc />
    protected override Size DefaultSize => new(160f, 160f);

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, () => LeftSource?.TotalWritten ?? -1));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var square = widget.CenteredSquare();
        GraphHelpers.DrawPanel(dc, Theme, square);
        var c = square.Center;
        float r = square.Width * 0.5f - 6f;
        if (ShowAxes)
        {
            var axis = p.Border.WithAlpha(0.7f);
            dc.DrawLine(new Point(c.X, c.Y - r), new Point(c.X, c.Y + r), axis, 1f);                       // M
            dc.DrawLine(new Point(c.X - r, c.Y), new Point(c.X + r, c.Y), axis.Fade(0.6f), 1f);            // S
            float d = r * 0.7071f;
            dc.DrawLine(new Point(c.X - d, c.Y - d), new Point(c.X + d, c.Y + d), axis.Fade(0.6f), 1f);    // L
            dc.DrawLine(new Point(c.X + d, c.Y - d), new Point(c.X - d, c.Y + d), axis.Fade(0.6f), 1f);    // R
            var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
            dc.DrawText("M", new Rect(c.X - 8f, c.Y - r - 1f, 16f, 10f), label, p.TextDim);
            dc.DrawText("L", new Rect(c.X - d - 10f, c.Y - d - 10f, 12f, 10f), label, p.TextDim);
            dc.DrawText("R", new Rect(c.X + d - 2f, c.Y - d - 10f, 12f, 10f), label, p.TextDim);
        }
        if (LeftSource is not { } ls || RightSource is not { } rs) return;
        int n = Math.Clamp(PointCount, 16, Math.Min(ls.Capacity, rs.Capacity));
        if (_left.Length < n) { _left = new float[n]; _right = new float[n]; }
        _lastWritten = ls.TotalWritten;
        n = Math.Min(ls.CopyLatest(_left.AsSpan(0, n)), rs.CopyLatest(_right.AsSpan(0, n)));
        var color = TraceColor ?? style.Accent;
        float k = r * Gain * 0.7071f, size = MathF.Max(0.6f, PointSize);
        dc.PushClip(square.Deflate(2f));
        const int Groups = 4; // older samples fainter
        for (int g = 0; g < Groups; g++)
        {
            var groupColor = color.WithAlpha(0.18f + 0.6f * (g + 1) / Groups);
            int from = n * g / Groups, to = n * (g + 1) / Groups;
            for (int i = from; i < to; i++)
            {
                float l = _left[i], rr = _right[i];
                float x = c.X + (rr - l) * k, y = c.Y - (l + rr) * k;
                dc.FillRect(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), groupColor);
            }
        }
        dc.PopClip();
    }
}

/// <summary>
/// Phase correlation meter: a horizontal bar from −1 (out of phase) through 0 to +1 (mono), computed from two
/// <see cref="AudioRingBuffer"/> over <see cref="WindowMilliseconds"/> and smoothed. Red on the left half warns that the
/// mix will lose content when summed to mono.
/// </summary>
public class CorrelationMeter : Control
{
    private float[] _left = [];
    private float[] _right = [];
    private double _smoothed;
    private RefreshTicker? _ticker;

    /// <summary>Creates a correlation meter.</summary>
    public CorrelationMeter()
    {
        Focusable = false;
    }

    /// <summary>Left channel samples.</summary>
    [Browsable(false)]
    public AudioRingBuffer? LeftSource { get; set; }

    /// <summary>Right channel samples.</summary>
    [Browsable(false)]
    public AudioRingBuffer? RightSource { get; set; }

    /// <summary>Sample rate of the sources, used to convert the window to samples.</summary>
    [Category("Data")]
    public float SampleRate { get; set; } = 48000f;

    /// <summary>Analysis window in milliseconds.</summary>
    [Category("Data")]
    public float WindowMilliseconds { get; set; } = 200f;

    /// <summary>Smoothing time constant in seconds (0 = none).</summary>
    [Category("Data")]
    public float SmoothingSeconds { get; set; } = 0.15f;

    /// <summary>Current (smoothed) correlation in [−1, 1].</summary>
    [Browsable(false)]
    public double Correlation => _smoothed;

    /// <inheritdoc />
    protected override Size DefaultSize => new(200f, 22f);

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, Update));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    /// <summary>Recomputes the correlation from the sources (called every frame).</summary>
    /// <param name="elapsedSeconds">Time since the previous update.</param>
    public void Update(double elapsedSeconds)
    {
        if (LeftSource is not { } ls || RightSource is not { } rs) return;
        int n = (int)Math.Clamp(SampleRate * WindowMilliseconds / 1000f, 64, Math.Min(ls.Capacity, rs.Capacity) / 2);
        if (_left.Length < n) { _left = new float[n]; _right = new float[n]; }
        n = Math.Min(ls.CopyLatest(_left.AsSpan(0, n)), rs.CopyLatest(_right.AsSpan(0, n)));
        double target = StereoAnalysis.Correlation(_left.AsSpan(0, n), _right.AsSpan(0, n));
        double k = SmoothingSeconds <= 0f ? 1.0 : 1.0 - Math.Exp(-elapsedSeconds / SmoothingSeconds);
        double next = _smoothed + (target - _smoothed) * k;
        if (Math.Abs(next - _smoothed) > 1e-4) InvalidateVisual();
        _smoothed = next;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var bar = widget.Deflate(new Thickness(14f, 3f));
        float radius = MathF.Min(3f, bar.Height * 0.5f);
        dc.FillRoundedRect(bar, radius, p.Background.Darken(0.3f));
        float cx = bar.Center.X, x = cx + (float)_smoothed * bar.Width * 0.5f;
        var color = _smoothed < 0.0 ? Color.Lerp(p.Warning, p.Danger, (float)Math.Min(1.0, -_smoothed * 2.0)) : p.Success;
        dc.FillRoundedRect(Rect.FromEdges(MathF.Min(cx, x), bar.Y + 1f, MathF.Max(cx, x), bar.Bottom - 1f), radius, color.WithAlpha(0.85f));
        dc.DrawLine(new Point(cx, bar.Y - 1f), new Point(cx, bar.Bottom + 1f), p.TextDim, 1f);
        dc.FillRect(new Rect(x - 1.5f, bar.Y - 1f, 3f, bar.Height + 2f), p.Text);
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        dc.DrawText("−1", new Rect(widget.X, bar.Y, 14f, bar.Height), label, p.TextDim);
        dc.DrawText("+1", new Rect(widget.Right - 14f, bar.Y, 14f, bar.Height), label, p.TextDim);
    }
}

/// <summary>Per-frame callback that repaints when a source advanced, or runs an update function.</summary>
internal sealed class RefreshTicker(UIElement owner, Func<long>? version, Action<double>? update = null) : ITickable
{
    private long _last = long.MinValue;

    public bool Tick(double elapsedSeconds)
    {
        if (owner.Host is null) return false;
        update?.Invoke(elapsedSeconds);
        if (version is not null)
        {
            long v = version();
            if (v != _last) { _last = v; owner.InvalidateVisual(); }
        }
        return true;
    }
}
