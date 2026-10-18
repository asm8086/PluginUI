// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// Scrolling spectrogram (waterfall): time runs from right to left, frequency on a logarithmic vertical axis, level
/// as color. Reads an <see cref="AudioRingBuffer"/> written by the audio thread and analyzes it with <see cref="Fft"/>.
/// </summary>
/// <remarks>
/// <para>The image is kept in a pixel buffer of the element's size (in DIPs) and scrolled by one column per analysis;
/// each analysis takes the latest <see cref="FftSize"/> samples (Hann window). <see cref="ColumnsPerSecond"/> sets the
/// scrolling speed.</para>
/// <para>The color map goes from transparent dark blue (below <see cref="MinDb"/>) through purple, red and orange to
/// pale yellow (at <see cref="MaxDb"/>).</para>
/// </remarks>
public class Spectrogram : Control
{
    private static readonly uint[] ColorMap = BuildColorMap();

    private Fft? _fft;
    private float[] _samples = [];
    private float[] _magnitudes = [];
    private byte[] _pixels = [];
    private int _imageWidth, _imageHeight;
    private ImageSource? _image;
    private bool _dirty;
    private double _accumulator;
    private long _lastWritten = -1;
    private RefreshTicker? _ticker;
    private int _fftSize = 2048;

    /// <summary>Creates a spectrogram.</summary>
    public Spectrogram()
    {
        Focusable = false;
    }

    /// <summary>Sample source.</summary>
    [Browsable(false)]
    public AudioRingBuffer? Source { get; set; }

    /// <summary>Sample rate of the source in Hz.</summary>
    [Category("Data")]
    public float SampleRate { get; set; } = 48000f;

    /// <summary>FFT size (power of two, 256 … 16384): frequency resolution vs time resolution.</summary>
    [Category("Data")]
    public int FftSize
    {
        get => _fftSize;
        set
        {
            int size = 256;
            while (size < Math.Clamp(value, 256, 16384)) size <<= 1;
            if (size == _fftSize) return;
            _fftSize = size;
            _fft = null;
        }
    }

    /// <summary>Scrolling speed in columns (analyses) per second.</summary>
    [Category("Data")]
    public float ColumnsPerSecond { get; set; } = 60f;

    /// <summary>Lowest displayed frequency in Hz.</summary>
    [Category("Data")]
    public float MinFrequency { get; set; } = 30f;

    /// <summary>Highest displayed frequency in Hz.</summary>
    [Category("Data")]
    public float MaxFrequency { get; set; } = 20000f;

    /// <summary>Level mapped to the darkest color (dBFS).</summary>
    [Category("Data")]
    public float MinDb { get; set; } = -100f;

    /// <summary>Level mapped to the brightest color (dBFS).</summary>
    [Category("Data")]
    public float MaxDb { get; set; } = -10f;

    /// <summary>Draws the frequency grid and labels over the image.</summary>
    [Category("Appearance")]
    public bool ShowGrid { get; set; } = true;

    /// <summary>Stops scrolling.</summary>
    [Category("Behavior")]
    public bool IsFrozen { get; set; }

    /// <inheritdoc />
    protected override Size DefaultSize => new(360f, 150f);

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, Advance));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        if (_image is not null) Host?.EvictImage(_image);
        _image = null;
        base.OnDetached();
    }

    /// <summary>Runs the analyses due since the previous frame (public for offscreen hosts and tests).</summary>
    /// <param name="elapsedSeconds">Time since the previous call.</param>
    public void Advance(double elapsedSeconds)
    {
        if (IsFrozen || Source is not { } source) return;
        _accumulator += elapsedSeconds * Math.Max(1f, ColumnsPerSecond);
        int columns = (int)Math.Min(_accumulator, 8.0); // never more than a few per frame
        if (columns <= 0) return;
        _accumulator -= columns;
        if (source.TotalWritten == _lastWritten) return; // no new audio: keep the image still
        _lastWritten = source.TotalWritten;
        EnsureBuffers();
        if (_imageWidth == 0) return;
        int n = _fft!.Size;
        if (source.CopyLatest(_samples.AsSpan(0, n)) < n) return;
        _fft.MagnitudeSpectrum(_samples, _magnitudes, WindowFunction.Hann, MinDb - 20f);
        for (int c = 0; c < columns; c++) ScrollAndDrawColumn();
        _dirty = true;
        InvalidateVisual();
    }

    private void EnsureBuffers()
    {
        int w = Math.Max(0, (int)MathF.Round(Bounds.Width)), h = Math.Max(0, (int)MathF.Round(Bounds.Height));
        if (w != _imageWidth || h != _imageHeight)
        {
            _imageWidth = w;
            _imageHeight = h;
            _pixels = new byte[w * h * 4];
        }
        if (_fft is null || _fft.Size != _fftSize)
        {
            _fft = new Fft(_fftSize);
            _samples = new float[_fftSize];
            _magnitudes = new float[_fftSize / 2];
        }
    }

    private void ScrollAndDrawColumn()
    {
        int w = _imageWidth, h = _imageHeight, stride = w * 4;
        for (int y = 0; y < h; y++) Buffer.BlockCopy(_pixels, y * stride + 4, _pixels, y * stride, stride - 4);
        float binHz = SampleRate / _fft!.Size;
        int bins = _magnitudes.Length;
        float range = MathF.Max(1f, MaxDb - MinDb);
        var area = new Rect(0f, 0f, w, h);
        for (int y = 0; y < h; y++)
        {
            // Band covered by this row; the loudest bin of the band (or the interpolated bin) gives the level.
            float f1 = Frequency(y, area), f0 = Frequency(y + 1, area);
            float b0 = f0 / binHz, b1 = f1 / binHz;
            float db;
            if (b1 - b0 < 1f)
            {
                float bc = Math.Clamp((b0 + b1) * 0.5f, 0f, bins - 1.001f);
                int i0 = (int)bc;
                db = _magnitudes[i0] + (_magnitudes[i0 + 1] - _magnitudes[i0]) * (bc - i0);
            }
            else
            {
                int i0 = Math.Clamp((int)b0, 0, bins - 1), i1 = Math.Clamp((int)MathF.Ceiling(b1), i0 + 1, bins);
                db = VectorOps.Max(_magnitudes.AsSpan(i0, i1 - i0));
            }
            int index = (int)(Math.Clamp((db - MinDb) / range, 0f, 1f) * 255f);
            uint bgra = ColorMap[index];
            int o = y * stride + stride - 4;
            _pixels[o] = (byte)bgra;
            _pixels[o + 1] = (byte)(bgra >> 8);
            _pixels[o + 2] = (byte)(bgra >> 16);
            _pixels[o + 3] = (byte)(bgra >> 24);
        }
    }

    private float Frequency(float y, Rect area)
    {
        float lo = MathF.Log(MathF.Max(1f, MinFrequency)), hi = MathF.Log(MathF.Max(MinFrequency + 1f, MaxFrequency));
        return MathF.Exp(lo + (area.Bottom - y) / MathF.Max(1f, area.Height) * (hi - lo));
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        GraphHelpers.DrawPanel(dc, Theme, widget);
        if (_dirty && _imageWidth > 0)
        {
            if (_image is not null) Host?.EvictImage(_image);
            _image = ImageSource.FromPixels(_imageWidth, _imageHeight, _pixels);
            _dirty = false;
        }
        if (_image is not null) dc.DrawImage(_image, new Rect(widget.X, widget.Y, _imageWidth, _imageHeight));
        if (!ShowGrid) return;
        var p = Theme.Palette;
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Left, Ellipsis = false };
        foreach (float f in (float[])[50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f])
        {
            if (f <= MinFrequency || f >= MaxFrequency) continue;
            float y = MathF.Round(GraphHelpers.LogToY(f, MinFrequency, MaxFrequency, widget)) + 0.5f;
            dc.DrawLine(new Point(widget.X, y), new Point(widget.X + 6f, y), p.Text.WithAlpha(0.6f), 1f);
            dc.DrawText(GraphHelpers.FormatHz(f), new Rect(widget.X + 8f, y - 6f, 40f, 11f), label, p.Text.WithAlpha(0.7f));
        }
    }

    private static uint[] BuildColorMap()
    {
        // (position, r, g, b, alpha) — premultiplied BGRA output.
        (float T, float R, float G, float B, float A)[] stops =
        [
            (0.00f, 0.02f, 0.02f, 0.10f, 0.0f),
            (0.20f, 0.10f, 0.05f, 0.35f, 0.85f),
            (0.45f, 0.50f, 0.10f, 0.55f, 1.0f),
            (0.65f, 0.85f, 0.20f, 0.30f, 1.0f),
            (0.85f, 0.98f, 0.55f, 0.10f, 1.0f),
            (1.00f, 1.00f, 0.97f, 0.70f, 1.0f),
        ];
        var map = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            int k = 0;
            while (k < stops.Length - 2 && t > stops[k + 1].T) k++;
            var (t0, r0, g0, b0, a0) = stops[k];
            var (t1, r1, g1, b1, a1) = stops[k + 1];
            float u = Math.Clamp((t - t0) / (t1 - t0), 0f, 1f);
            float a = a0 + (a1 - a0) * u;
            byte R = (byte)((r0 + (r1 - r0) * u) * a * 255f), G = (byte)((g0 + (g1 - g0) * u) * a * 255f), B = (byte)((b0 + (b1 - b0) * u) * a * 255f), A = (byte)(a * 255f);
            map[i] = B | ((uint)G << 8) | ((uint)R << 16) | ((uint)A << 24);
        }
        return map;
    }
}
