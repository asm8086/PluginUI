// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// Oscilloscope reading an <see cref="AudioRingBuffer"/> written by the audio thread: window length, rising
/// zero-crossing trigger, gain, grid, glowing or filled trace. Dense windows are drawn as min/max columns.
/// iPlug2 <c>IVScopeControl</c>.
/// </summary>
public class Oscilloscope : Control
{
    private float[] _buffer = [];
    private Point[] _points = [];
    private Point[] _fill = [];
    private int _pointCount;
    private long _lastWritten = -1;
    private ScopeTicker? _ticker;
    private int _windowSamples = 1024;

    /// <summary>Creates a scope.</summary>
    public Oscilloscope()
    {
        Focusable = false;
    }

    /// <summary>Sample source (null = flat line).</summary>
    [Browsable(false)]
    public AudioRingBuffer? Source { get; set; }

    /// <summary>Number of samples across the width.</summary>
    [Category("Data")]
    public int WindowSamples { get => _windowSamples; set { _windowSamples = Math.Clamp(value, 16, 65536); _lastWritten = -1; } }

    /// <summary>Aligns the trace on a rising zero crossing (stable display of periodic signals).</summary>
    [Category("Data")]
    public bool Trigger { get; set; } = true;

    /// <summary>Trigger level.</summary>
    [Category("Data")]
    public float TriggerLevel { get; set; }

    /// <summary>Vertical gain (1 = ±1.0 full height).</summary>
    [Category("Value")]
    public float Gain { get; set; } = 1f;

    /// <summary>Draws the grid.</summary>
    [Category("Appearance")]
    public bool ShowGrid { get; set; } = true;

    /// <summary>Fills between the trace and the center line.</summary>
    [Category("Appearance")]
    public bool Filled { get; set; }

    /// <summary>Draws a soft glow under the trace.</summary>
    [Category("Appearance")]
    public bool Glow { get; set; } = true;

    /// <summary>Trace color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? TraceColor { get; set; }

    /// <summary>Trace thickness.</summary>
    [Category("Appearance")]
    public float LineThickness { get; set; } = 1.5f;

    /// <summary>Stops updating (holds the current trace).</summary>
    [Category("Behavior")]
    public bool IsFrozen { get; set; }

    /// <inheritdoc />
    protected override Size DefaultSize => new(240f, 110f);

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _ticker ??= new ScopeTicker(this);
        Host!.AddTicker(_ticker);
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private sealed class ScopeTicker(Oscilloscope owner) : ITickable
    {
        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            if (!owner.IsFrozen && owner.Source is { } s && s.TotalWritten != owner._lastWritten) owner.InvalidateVisual();
            return true;
        }
    }

    private void Capture(Rect area)
    {
        int window = _windowSamples;
        int columns = Math.Max(2, (int)MathF.Ceiling(area.Width));
        bool dense = window > columns * 2;
        int needed = dense ? columns * 2 : window;
        if (_points.Length < needed) _points = new Point[needed];
        if (_fill.Length < needed + 2) _fill = new Point[needed + 2];

        var source = Source;
        int capacity = Trigger ? window * 2 : window;
        if (_buffer.Length < capacity) _buffer = new float[capacity];
        int n = 0;
        if (source is not null && !IsFrozen)
        {
            _lastWritten = source.TotalWritten;
            n = source.CopyLatest(_buffer.AsSpan(0, Math.Min(capacity, source.Capacity)));
        }
        else if (IsFrozen && _pointCount > 0)
        {
            return;
        }

        int start = Math.Max(0, n - window);
        if (Trigger && n > window)
        {
            // Latest rising crossing that still leaves a full window after it.
            for (int i = n - window - 1; i >= 1; i--)
            {
                if (_buffer[i - 1] <= TriggerLevel && _buffer[i] > TriggerLevel)
                {
                    start = i;
                    break;
                }
            }
        }

        float mid = area.Center.Y, half = area.Height * 0.5f * Gain;
        int available = Math.Max(0, Math.Min(window, n - start));
        float Y(float s) => Math.Clamp(mid - s * half, area.Y, area.Bottom);

        if (available == 0)
        {
            _points[0] = new Point(area.X, mid);
            _points[1] = new Point(area.Right, mid);
            _pointCount = 2;
            return;
        }

        if (dense)
        {
            int count = 0;
            for (int c = 0; c < columns; c++)
            {
                int a = start + (int)((long)c * window / columns);
                int b = start + (int)((long)(c + 1) * window / columns);
                b = Math.Min(Math.Max(b, a + 1), start + available);
                if (a >= start + available) break;
                var (lo, hi) = VectorOps.MinMax(_buffer.AsSpan(a, b - a));
                float x = area.X + c * area.Width / (columns - 1);
                // Alternate order keeps the polyline continuous.
                if ((c & 1) == 0) { _points[count++] = new Point(x, Y(lo)); _points[count++] = new Point(x, Y(hi)); }
                else { _points[count++] = new Point(x, Y(hi)); _points[count++] = new Point(x, Y(lo)); }
            }
            _pointCount = count;
        }
        else
        {
            for (int i = 0; i < available; i++)
            {
                float x = area.X + i * area.Width / Math.Max(1, window - 1);
                _points[i] = new Point(x, Y(_buffer[start + i]));
            }
            _pointCount = available;
        }
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = MathF.Min(Theme.Metrics.CornerRadius, widget.Height * 0.2f);
        dc.FillRoundedRect(widget, radius, p.Background.Darken(0.3f));
        var area = widget.Deflate(new Thickness(2f, 4f));

        if (ShowGrid)
        {
            var grid = p.Border.WithAlpha(0.5f);
            for (int i = 1; i < 8; i++)
            {
                float x = MathF.Round(area.X + area.Width * i / 8f) + 0.5f;
                dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), grid, 1f);
            }
            for (int i = 1; i < 4; i++)
            {
                float y = MathF.Round(area.Y + area.Height * i / 4f) + 0.5f;
                dc.DrawLine(new Point(area.X, y), new Point(area.Right, y), i == 2 ? p.Border : grid, 1f);
            }
        }

        Capture(area);
        var color = TraceColor ?? style.Accent;
        var points = new ReadOnlySpan<Point>(_points, 0, _pointCount);
        using (dc.Clip(widget.Inflate(-1f)))
        {
            if (Filled && _pointCount > 1)
            {
                _fill[0] = new Point(points[0].X, area.Center.Y);
                points.CopyTo(_fill.AsSpan(1));
                _fill[_pointCount + 1] = new Point(points[^1].X, area.Center.Y);
                dc.FillPolygon(new ReadOnlySpan<Point>(_fill, 0, _pointCount + 2), color.WithAlpha(0.18f));
            }
            if (Glow) dc.DrawPolyline(points, color.WithAlpha(0.18f), LineThickness * 3.5f, StrokeStyle.Round);
            dc.DrawPolyline(points, color, LineThickness, StrokeStyle.Round);
        }
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame, style.FrameThickness);
    }
}
