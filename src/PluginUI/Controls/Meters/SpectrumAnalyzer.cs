// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>Display mode of a <see cref="SpectrumAnalyzer"/>.</summary>
public enum SpectrumMode
{
    /// <summary>Continuous curve with a filled area below.</summary>
    Line,
    /// <summary>Fractional-octave bars.</summary>
    Bars,
}

/// <summary>
/// Real-time FFT spectrum analyzer reading an <see cref="AudioRingBuffer"/>: logarithmic frequency axis, dB range,
/// per-column attack/release smoothing, peak-hold curve, slope compensation (dB/octave around 1 kHz),
/// line or fractional-octave bar display, labeled grid. iPlug2 <c>IVSpectrumAnalyzerControl</c>.
/// </summary>
/// <remarks>The FFT runs on the UI thread at frame rate, only when new audio arrived. Buffers are preallocated.</remarks>
public class SpectrumAnalyzer : Control
{
    private static readonly float[] FrequencyLines = [20f, 50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f];

    private Fft? _fft;
    private float[] _samples = [];
    private float[] _magnitudes = [];
    private float[] _columns = [];
    private float[] _smoothed = [];
    private float[] _peaks = [];
    private Point[] _points = [];
    private Point[] _fill = [];
    private long _lastWritten = -1;
    private double _pendingDt;
    private bool _dataValid;
    private int _fftSize = 2048;
    private AnalyzerTicker? _ticker;
    private LinearGradientPaint? _fillPaint;
    private Rect _fillPaintRect;
    private LinearGradientPaint? _barPaint;
    private Rect _barPaintRect;
    private bool _settled;
    private Color _paintColor;

    /// <summary>Creates an analyzer.</summary>
    public SpectrumAnalyzer()
    {
        Focusable = false;
    }

    /// <summary>Sample source.</summary>
    [Browsable(false)]
    public AudioRingBuffer? Source { get; set; }

    /// <summary>FFT size (power of two, 256..16384).</summary>
    [Category("Data")]
    public int FftSize
    {
        get => _fftSize;
        set
        {
            int v = 256;
            while (v < value && v < 16384) v <<= 1;
            if (v == _fftSize) return;
            _fftSize = v;
            _fft = null;
        }
    }

    /// <summary>Sample rate of the source in Hz.</summary>
    [Category("Data")]
    public float SampleRate { get; set; } = 48000f;

    /// <summary>Lowest displayed frequency.</summary>
    [Category("Data")]
    public float MinFrequency { get; set; } = 20f;

    /// <summary>Highest displayed frequency.</summary>
    [Category("Data")]
    public float MaxFrequency { get; set; } = 20000f;

    /// <summary>Bottom of the dB range.</summary>
    [Category("Data")]
    public float MinDb { get; set; } = -90f;

    /// <summary>Top of the dB range.</summary>
    [Category("Data")]
    public float MaxDb { get; set; } = 6f;

    /// <summary>Tilt added to the spectrum in dB per octave around 1 kHz (4.5 makes pink noise look flat).</summary>
    [Category("Data")]
    public float SlopeDbPerOctave { get; set; } = 4.5f;

    /// <summary>Attack time constant in seconds.</summary>
    [Category("Data")]
    public float AttackSeconds { get; set; } = 0.01f;

    /// <summary>Release speed in dB per second.</summary>
    [Category("Data")]
    public float ReleaseDbPerSecond { get; set; } = 40f;

    /// <summary>Draws a slowly decaying peak curve.</summary>
    [Category("Appearance")]
    public bool ShowPeakHold { get; set; }

    /// <summary>Peak curve decay in dB per second.</summary>
    [Category("Data")]
    public float PeakDecayDbPerSecond { get; set; } = 8f;

    /// <summary>Line or bars.</summary>
    [Category("Data")]
    public SpectrumMode Mode { get; set; } = SpectrumMode.Line;

    /// <summary>Bars per octave in <see cref="SpectrumMode.Bars"/> (1, 2, 3, 6...).</summary>
    [Category("Data")]
    public int BandsPerOctave { get; set; } = 3;

    /// <summary>Analysis window.</summary>
    [Category("Data")]
    public WindowFunction Window { get; set; } = WindowFunction.Hann;

    /// <summary>Draws the frequency/dB grid and labels.</summary>
    [Category("Appearance")]
    public bool ShowGrid { get; set; } = true;

    /// <summary>Curve color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? CurveColor { get; set; }

    /// <summary>Stops updating.</summary>
    [Category("Behavior")]
    public bool IsFrozen { get; set; }

    /// <inheritdoc />
    protected override Size DefaultSize => new(360f, 150f);

    /// <summary>X position of a frequency inside an area (log axis).</summary>
    public float FrequencyToX(float hz, Rect area)
    {
        float lo = MathF.Log(MathF.Max(1f, MinFrequency)), hi = MathF.Log(MathF.Max(MinFrequency + 1f, MaxFrequency));
        return area.X + (MathF.Log(MathF.Max(1f, hz)) - lo) / (hi - lo) * area.Width;
    }

    /// <summary>Frequency at an X position inside an area.</summary>
    public float XToFrequency(float x, Rect area)
    {
        float lo = MathF.Log(MathF.Max(1f, MinFrequency)), hi = MathF.Log(MathF.Max(MinFrequency + 1f, MaxFrequency));
        return MathF.Exp(lo + (x - area.X) / MathF.Max(1f, area.Width) * (hi - lo));
    }

    private float DbToY(float db, Rect area) => area.Bottom - Math.Clamp((db - MinDb) / MathF.Max(0.1f, MaxDb - MinDb), 0f, 1f) * area.Height;

    // ---------------------------------------------------------------- analysis

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _ticker ??= new AnalyzerTicker(this);
        Host!.AddTicker(_ticker);
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private sealed class AnalyzerTicker(SpectrumAnalyzer owner) : ITickable
    {
        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            owner._pendingDt += elapsedSeconds;
            bool fresh = owner.Source is { } s && s.TotalWritten != owner._lastWritten;
            if (!owner.IsFrozen && (fresh || !owner._settled)) owner.InvalidateVisual();
            return true;
        }
    }

    private void Analyze(int columns, Rect area)
    {
        if (_fft is null || _fft.Size != _fftSize)
        {
            _fft = new Fft(_fftSize);
            _samples = new float[_fftSize];
            _magnitudes = new float[_fftSize / 2];
            _lastWritten = -1;
        }
        if (_columns.Length != columns)
        {
            _columns = new float[columns];
            _smoothed = new float[columns];
            _peaks = new float[columns];
            Array.Fill(_smoothed, MinDb);
            Array.Fill(_peaks, MinDb);
            _points = new Point[columns];
            _fill = new Point[columns + 2];
        }

        double dt = Math.Min(_pendingDt, 0.1);
        _pendingDt = 0;
        var source = Source;
        if (IsFrozen) return;
        if (source is not null && source.TotalWritten >= _fftSize && source.TotalWritten != _lastWritten)
        {
            _lastWritten = source.TotalWritten;
            source.CopyLatest(_samples);
            _fft.MagnitudeSpectrum(_samples, _magnitudes, Window, MinDb - 40f);
            MapToColumns(columns, area);
            _dataValid = true;
        }
        else if (!_dataValid)
        {
            Array.Fill(_columns, MinDb - 20f);
        }

        float attack = AttackSeconds <= 0f || dt <= 0 ? 1f : 1f - MathF.Exp((float)(-dt / AttackSeconds));
        float release = ReleaseDbPerSecond * (float)dt;
        float peakDecay = PeakDecayDbPerSecond * (float)dt;
        if (dt <= 0) { attack = 1f; release = float.MaxValue; peakDecay = ShowPeakHold ? 0f : float.MaxValue; }
        bool settled = true;
        for (int i = 0; i < columns; i++)
        {
            float target = _columns[i];
            float s = _smoothed[i];
            s = target > s ? s + (target - s) * attack : MathF.Max(target, s - release);
            _smoothed[i] = s;
            _peaks[i] = s >= _peaks[i] ? s : MathF.Max(s, _peaks[i] - peakDecay);
            settled &= MathF.Abs(s - target) < 0.05f && (!ShowPeakHold || _peaks[i] - s < 0.05f);
        }
        _settled = settled;
    }

    private void MapToColumns(int columns, Rect area)
    {
        float binHz = SampleRate / _fftSize;
        int bins = _magnitudes.Length;
        for (int c = 0; c < columns; c++)
        {
            float f0 = XToFrequency(area.X + c * area.Width / columns, area);
            float f1 = XToFrequency(area.X + (c + 1) * area.Width / columns, area);
            float b0 = f0 / binHz, b1 = f1 / binHz;
            float db;
            if (b1 - b0 < 1f)
            {
                // Fewer bins than pixels: interpolate between neighboring bins.
                float bc = Math.Clamp((b0 + b1) * 0.5f, 0f, bins - 1.001f);
                int i0 = (int)bc;
                float t = bc - i0;
                db = _magnitudes[i0] + (_magnitudes[Math.Min(bins - 1, i0 + 1)] - _magnitudes[i0]) * t;
            }
            else
            {
                int i0 = Math.Clamp((int)MathF.Floor(b0), 0, bins - 1), i1 = Math.Clamp((int)MathF.Ceiling(b1), i0 + 1, bins);
                db = VectorOps.Max(_magnitudes.AsSpan(i0, i1 - i0));
            }
            float fc = MathF.Sqrt(MathF.Max(1f, f0) * MathF.Max(1f, f1));
            _columns[c] = db + SlopeDbPerOctave * MathF.Log2(fc / 1000f);
        }
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = MathF.Min(Theme.Metrics.CornerRadius, widget.Height * 0.2f);
        dc.FillRoundedRect(widget, radius, p.Background.Darken(0.3f));
        var area = widget.Deflate(new Thickness(1f, 6f, 1f, ShowGrid ? 14f : 2f));
        int columns = Math.Max(8, (int)MathF.Ceiling(area.Width));
        Analyze(columns, area);

        if (ShowGrid) RenderGrid(dc, widget, area, p);

        var color = CurveColor ?? style.Accent;
        using (dc.Clip(Rect.FromEdges(widget.X + 1f, widget.Y + 1f, widget.Right - 1f, area.Bottom)))
        {
            if (Mode == SpectrumMode.Bars) RenderBars(dc, area, color);
            else RenderLine(dc, area, color);
        }
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame, style.FrameThickness);
    }

    private void RenderLine(DrawingContext dc, Rect area, Color color)
    {
        int n = _smoothed.Length;
        for (int i = 0; i < n; i++) _points[i] = new Point(area.X + (i + 0.5f) * area.Width / n, DbToY(_smoothed[i], area));
        _fill[0] = new Point(area.X, area.Bottom);
        Array.Copy(_points, 0, _fill, 1, n);
        _fill[n + 1] = new Point(area.Right, area.Bottom);
        if (_fillPaint is null || _fillPaintRect != area || _paintColor != color)
        {
            _fillPaint = new LinearGradientPaint(new Point(area.X, area.Y), new Point(area.X, area.Bottom),
                [new(0f, color.WithAlpha(0.42f)), new(1f, color.WithAlpha(0.03f))], RelativeToBounds: false);
            _fillPaintRect = area;
            _barPaint = null;
            _paintColor = color;
        }
        dc.FillPolygon(_fill, _fillPaint);
        dc.DrawPolyline(_points, color, 1.5f, StrokeStyle.Round);

        if (ShowPeakHold)
        {
            for (int i = 0; i < n; i++) _points[i] = new Point(area.X + (i + 0.5f) * area.Width / n, DbToY(_peaks[i], area));
            dc.DrawPolyline(_points, Theme.Palette.Text.WithAlpha(0.45f), 1f, StrokeStyle.Round);
        }
    }

    private void RenderBars(DrawingContext dc, Rect area, Color color)
    {
        int bands = Math.Max(1, BandsPerOctave);
        float ratio = MathF.Pow(2f, 1f / bands);
        int n = _smoothed.Length;
        float gap = MathF.Max(1f, 6f / bands);
        // Bands centered on ISO-like frequencies around 1 kHz.
        float start = 1000f;
        while (start / ratio >= MinFrequency) start /= ratio;
        for (float fc = start; fc <= MaxFrequency; fc *= ratio)
        {
            float lo = fc / MathF.Sqrt(ratio), hi = fc * MathF.Sqrt(ratio);
            float x0 = MathF.Max(area.X, FrequencyToX(lo, area)), x1 = MathF.Min(area.Right, FrequencyToX(hi, area));
            int c0 = Math.Clamp((int)((x0 - area.X) / area.Width * n), 0, n - 1);
            int c1 = Math.Clamp((int)MathF.Ceiling((x1 - area.X) / area.Width * n), c0 + 1, n);
            float level = float.MinValue, peak = float.MinValue;
            for (int c = c0; c < c1; c++) { level = MathF.Max(level, _smoothed[c]); peak = MathF.Max(peak, _peaks[c]); }
            var bar = Rect.FromEdges(x0 + gap * 0.5f, DbToY(level, area), x1 - gap * 0.5f, area.Bottom);
            if (bar.Width < 0.5f) continue;
            dc.FillRect(bar, BarPaint(area, color));
            if (ShowPeakHold) dc.FillRect(new Rect(bar.X, DbToY(peak, area) - 1f, bar.Width, 2f), Theme.Palette.Text.WithAlpha(0.6f));
        }
    }

    private LinearGradientPaint BarPaint(Rect area, Color color)
    {
        if (_barPaint is null || _barPaintRect != area || _paintColor != color)
        {
            _barPaint = new LinearGradientPaint(new Point(area.X, area.Y), new Point(area.X, area.Bottom),
                [new(0f, color.Lighten(0.15f)), new(1f, color.Darken(0.5f))], RelativeToBounds: false);
            _barPaintRect = area;
            _fillPaint = null;
            _paintColor = color;
        }
        return _barPaint;
    }

    private void RenderGrid(DrawingContext dc, Rect widget, Rect area, Palette p)
    {
        var grid = p.Border.WithAlpha(0.55f);
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        foreach (float f in FrequencyLines)
        {
            if (f < MinFrequency || f > MaxFrequency) continue;
            float x = MathF.Round(FrequencyToX(f, area)) + 0.5f;
            dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), grid, 1f);
            if (f <= MinFrequency * 1.01f || f >= MaxFrequency * 0.99f) continue;
            string text = f >= 1000f ? (f / 1000f).ToString("0", CultureInfo.InvariantCulture) + "k" : f.ToString("0", CultureInfo.InvariantCulture);
            dc.DrawText(text, new Rect(x - 16f, area.Bottom + 1f, 32f, widget.Bottom - area.Bottom - 1f), label, p.TextDim);
        }
        float step = (MaxDb - MinDb) > 60f ? 12f : 6f;
        var dbLabel = label with { Align = HorizontalAlignment.Left };
        for (float db = MathF.Ceiling(MinDb / step) * step; db <= MaxDb; db += step)
        {
            float y = MathF.Round(DbToY(db, area)) + 0.5f;
            dc.DrawLine(new Point(area.X, y), new Point(area.Right, y), db == 0f ? p.Border : grid, 1f);
            if (y - area.Y < 6f || area.Bottom - y < 6f) continue;
            dc.DrawText(db.ToString("0", CultureInfo.InvariantCulture), new Rect(area.X + 3f, y - 11f, 30f, 10f), dbLabel, p.TextDim.WithAlpha(0.8f));
        }
    }
}
