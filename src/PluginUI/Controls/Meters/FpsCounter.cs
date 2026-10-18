// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// Debug overlay showing the repaint rate (frames per second) and the average frame interval.
/// It forces a continuous repaint while attached, so remove it from release builds. iPlug2 <c>IFPSDisplayControl</c>.
/// </summary>
public class FpsCounter : UIElement
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double[] _frameTimes = new double[64];
    private int _frameIndex;
    private int _frameCount;
    private double _lastFrame = -1;
    private string _text = "-- fps";
    private double _lastTextUpdate;
    private FpsTicker? _ticker;

    /// <summary>Creates the counter.</summary>
    public FpsCounter()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Measured frames per second.</summary>
    [Browsable(false)]
    public double FramesPerSecond { get; private set; }

    /// <summary>Text color (null = theme success color).</summary>
    [Category("Appearance")]
    public Color? Foreground { get; set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available) => new(96f, 18f);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_lastFrame >= 0)
        {
            _frameTimes[_frameIndex] = now - _lastFrame;
            _frameIndex = (_frameIndex + 1) % _frameTimes.Length;
            _frameCount = Math.Min(_frameCount + 1, _frameTimes.Length);
        }
        _lastFrame = now;
        if (now - _lastTextUpdate > 0.25 && _frameCount > 0)
        {
            double sum = 0;
            for (int i = 0; i < _frameCount; i++) sum += _frameTimes[i];
            double avg = sum / _frameCount;
            FramesPerSecond = avg > 0 ? 1.0 / avg : 0;
            _text = string.Create(CultureInfo.InvariantCulture, $"{FramesPerSecond:0} fps  {avg * 1000:0.0} ms");
            _lastTextUpdate = now;
        }

        var p = Theme.Palette;
        dc.FillRoundedRect(Bounds, 3f, p.Background.WithAlpha(0.75f));
        dc.DrawText(_text, Bounds.Deflate(new Thickness(5f, 0f)), Theme.Typography.Monospace with { Size = 10f, Ellipsis = false }, Foreground ?? p.Success);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _ticker ??= new FpsTicker(this);
        Host!.AddTicker(_ticker);
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private sealed class FpsTicker(FpsCounter owner) : ITickable
    {
        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            owner.InvalidateVisual();
            return true;
        }
    }
}
