// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>How <see cref="HistoryGraph"/> draws its values.</summary>
public enum HistoryGraphMode
{
    /// <summary>Line through the values.</summary>
    Line,
    /// <summary>Line with the area below filled (or above, for <see cref="HistoryGraph.FillFromTop"/>).</summary>
    Filled,
    /// <summary>One bar per value.</summary>
    Bars,
}

/// <summary>
/// Rolling graph of a value over time, newest on the right (iPlug2 <c>IVDisplayControl</c>): gain reduction history,
/// envelope follower, LFO output, CPU load… Values come from <see cref="Push"/> (UI thread), from a lock-free
/// <see cref="PeakValue"/> published by the audio thread (<see cref="PeakSource"/>), or from a function polled every
/// frame (<see cref="ValueSource"/>).
/// </summary>
/// <example>
/// <code>
/// // Audio thread: _grPeak.Push(gainReductionDb);   UI:
/// var history = new HistoryGraph { PeakSource = processor.GainReduction, Minimum = 0, Maximum = 24, FillFromTop = true,
///                                  Mode = HistoryGraphMode.Filled, Unit = " dB" };
/// </code>
/// </example>
public class HistoryGraph : Control
{
    private float[] _values = new float[512];
    private int _head;      // index of the next write
    private int _count;
    private double _accumulator;
    private HistoryTicker? _ticker;

    /// <summary>Creates a history graph.</summary>
    public HistoryGraph()
    {
        Focusable = false;
    }

    /// <summary>Lock-free value published by the audio thread; its maximum since the previous frame is plotted.</summary>
    [Browsable(false)]
    public PeakValue? PeakSource { get; set; }

    /// <summary>Function polled once per sample period (UI thread).</summary>
    [Browsable(false)]
    public Func<float>? ValueSource { get; set; }

    /// <summary>Values plotted per second when polling a source (default 30).</summary>
    [Category("Data")]
    public float SamplesPerSecond { get; set; } = 30f;

    /// <summary>Number of values kept (and spread across the width).</summary>
    [Category("Data")]
    public int Capacity
    {
        get => _values.Length;
        set
        {
            value = Math.Clamp(value, 8, 65536);
            if (value == _values.Length) return;
            var old = Snapshot();
            _values = new float[value];
            _head = 0;
            _count = 0;
            foreach (float v in old.AsSpan(Math.Max(0, old.Length - value))) Append(v);
            InvalidateVisual();
        }
    }

    /// <summary>Value at the bottom of the graph.</summary>
    [Category("Value")]
    public float Minimum { get; set; }

    /// <summary>Value at the top of the graph.</summary>
    [Category("Value")]
    public float Maximum { get; set; } = 1f;

    /// <summary>Drawing mode.</summary>
    [Category("Appearance")]
    public HistoryGraphMode Mode { get; set; } = HistoryGraphMode.Filled;

    /// <summary>Fills (and draws bars) from the top instead of the bottom, e.g. for gain reduction.</summary>
    [Category("Appearance")]
    public bool FillFromTop { get; set; }

    /// <summary>Trace color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? TraceColor { get; set; }

    /// <summary>Line thickness.</summary>
    [Category("Appearance")]
    public float LineThickness { get; set; } = 1.5f;

    /// <summary>Draws horizontal grid lines (4 divisions).</summary>
    [Category("Appearance")]
    public bool ShowGrid { get; set; } = true;

    /// <summary>Shows the latest value in the top-right corner.</summary>
    [Category("Appearance")]
    public bool ShowCurrentValue { get; set; } = true;

    /// <summary>Unit appended to the displayed value (e.g. <c>" dB"</c>).</summary>
    [Category("Text")]
    public string? Unit { get; set; }

    /// <summary>Decimals of the displayed value.</summary>
    [Category("Text")]
    public int Decimals { get; set; } = 1;

    /// <summary>Stops scrolling (values pushed meanwhile are ignored).</summary>
    [Category("Behavior")]
    public bool IsFrozen { get; set; }

    /// <summary>Number of values currently stored.</summary>
    [Browsable(false)]
    public int Count => _count;

    /// <summary>Most recent value (0 when empty).</summary>
    [Browsable(false)]
    public float Latest => _count == 0 ? 0f : _values[(_head - 1 + _values.Length) % _values.Length];

    /// <inheritdoc />
    protected override Size DefaultSize => new(240f, 80f);

    /// <summary>Appends a value (UI thread).</summary>
    /// <param name="value">The value (NaN is ignored).</param>
    public void Push(float value)
    {
        if (IsFrozen || float.IsNaN(value)) return;
        Append(value);
        InvalidateVisual();
    }

    /// <summary>Removes every value.</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        InvalidateVisual();
    }

    /// <summary>Copy of the stored values, oldest first.</summary>
    /// <returns>A new array of <see cref="Count"/> values.</returns>
    public float[] Snapshot()
    {
        var result = new float[_count];
        int start = (_head - _count + _values.Length) % _values.Length;
        for (int i = 0; i < _count; i++) result[i] = _values[(start + i) % _values.Length];
        return result;
    }

    private void Append(float value)
    {
        _values[_head] = value;
        _head = (_head + 1) % _values.Length;
        if (_count < _values.Length) _count++;
    }

    /// <summary>Advances the polling clock (called every frame; public for offscreen hosts and tests).</summary>
    /// <param name="elapsedSeconds">Time since the previous call.</param>
    public void Advance(double elapsedSeconds)
    {
        if (IsFrozen || (PeakSource is null && ValueSource is null)) return;
        _accumulator += elapsedSeconds * Math.Max(0.1f, SamplesPerSecond);
        int steps = (int)Math.Min(_accumulator, _values.Length);
        if (steps <= 0) return;
        _accumulator -= steps;
        float v = PeakSource?.ReadAndReset() ?? ValueSource!.Invoke();
        for (int i = 0; i < steps; i++) Append(v);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new HistoryTicker(this));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private sealed class HistoryTicker(HistoryGraph owner) : ITickable
    {
        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            owner.Advance(elapsedSeconds);
            return true;
        }
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var area = widget.Deflate(new Thickness(2f, 3f));
        if (ShowGrid)
        {
            var grid = p.Border.WithAlpha(0.5f);
            for (int i = 1; i < 4; i++)
            {
                float y = MathF.Round(area.Y + area.Height * i / 4f) + 0.5f;
                dc.DrawLine(new Point(area.X, y), new Point(area.Right, y), grid, 1f);
            }
        }
        var color = TraceColor ?? style.Accent;
        int n = _count, capacity = _values.Length;
        if (n > 0)
        {
            float step = area.Width / Math.Max(1, capacity - 1);
            int start = (_head - n + capacity) % capacity;
            float x0 = area.Right - (n - 1) * step;
            float baseY = FillFromTop ? area.Y : area.Bottom;
            float Y(float v) => GraphHelpers.ValueToY(v, Minimum, Maximum, area);
            dc.PushClip(area);
            if (Mode == HistoryGraphMode.Bars)
            {
                float w = MathF.Max(1f, step - 1f);
                for (int i = 0; i < n; i++)
                {
                    float x = x0 + i * step, y = Y(_values[(start + i) % capacity]);
                    dc.FillRect(Rect.FromEdges(x - w * 0.5f, MathF.Min(y, baseY), x + w * 0.5f, MathF.Max(y, baseY)), color.WithAlpha(0.85f));
                }
            }
            else
            {
                var points = new Point[n];
                for (int i = 0; i < n; i++) points[i] = new Point(x0 + i * step, Y(_values[(start + i) % capacity]));
                if (Mode == HistoryGraphMode.Filled && n > 1)
                {
                    var poly = new Point[n + 2];
                    points.CopyTo(poly, 0);
                    poly[n] = new Point(points[^1].X, baseY);
                    poly[n + 1] = new Point(points[0].X, baseY);
                    dc.FillPolygon(poly, color.WithAlpha(0.25f));
                }
                if (n > 1) dc.DrawPolyline(points, color, LineThickness, StrokeStyle.Round);
            }
            dc.PopClip();
        }
        if (ShowCurrentValue && n > 0)
        {
            var text = InvariantNumber.Format(Latest, Math.Clamp(Decimals, 0, 6)) + (Unit ?? string.Empty);
            dc.DrawText(text, new Rect(area.X, area.Y + 1f, area.Width - 3f, 12f), Theme.Typography.Small with { Size = 10f, Align = HorizontalAlignment.Right }, p.TextDim);
        }
    }
}
