// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// Live spectrum drawn behind a frequency display (equalizer, crossover): the latest audio of a ring buffer, 4096-point
/// FFT, one column per two pixels with the loudest bin, a +4.5 dB per octave tilt so that pink noise looks flat.
/// </summary>
internal sealed class SpectrumUnderlay
{
    private Fft? _fft;
    private float[] _samples = [];
    private float[] _spectrum = [];

    /// <summary>Fills the spectrum of <paramref name="source"/> over <paramref name="area"/> (nothing until enough audio arrived).</summary>
    public void Render(DrawingContext dc, Rect area, AudioRingBuffer source, float sampleRate, float minHz, float maxHz, Color color)
    {
        _fft ??= new Fft(4096);
        int size = _fft.Size;
        if (_samples.Length != size)
        {
            _samples = new float[size];
            _spectrum = new float[size / 2];
        }
        if (source.CopyLatest(_samples) < size) return;
        _fft.MagnitudeSpectrum(_samples, _spectrum, WindowFunction.Hann, -120f);
        int columns = Math.Max(16, (int)area.Width / 2);
        var points = new Point[columns + 2];
        float binHz = sampleRate / size;
        for (int c = 0; c < columns; c++)
        {
            float f0 = GraphHelpers.XToLog(area.X + area.Width * c / columns, minHz, maxHz, area);
            float f1 = GraphHelpers.XToLog(area.X + area.Width * (c + 1) / columns, minHz, maxHz, area);
            int i0 = Math.Clamp((int)(f0 / binHz), 0, _spectrum.Length - 1), i1 = Math.Clamp((int)MathF.Ceiling(f1 / binHz), i0 + 1, _spectrum.Length);
            float db = VectorOps.Max(_spectrum.AsSpan(i0, i1 - i0)) + 4.5f * MathF.Log2(MathF.Max(20f, (f0 + f1) * 0.5f) / 1000f);
            points[c] = new Point(area.X + area.Width * (c + 0.5f) / columns, GraphHelpers.ValueToY(db, -90f, 0f, area));
        }
        points[columns] = new Point(area.Right, area.Bottom);
        points[columns + 1] = new Point(area.X, area.Bottom);
        dc.FillPolygon(points, color);
    }
}
