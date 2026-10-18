// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// Progress indicator: determinate (0..1 with optional percentage text) or indeterminate (animated sweep).
/// Useful for preset loading, sample analysis or license activation.
/// </summary>
public class ProgressBar : Control
{
    private double _value;
    private bool _indeterminate;
    private double _phase;
    private SweepTicker? _ticker;

    /// <summary>Creates a progress bar.</summary>
    public ProgressBar()
    {
        Focusable = false;
    }

    /// <summary>Progress 0..1.</summary>
    [Category("Value")]
    public double Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(double.IsNaN(value) ? 0 : value, 0.0, 1.0);
            if (value == _value) return;
            _value = value;
            InvalidateVisual();
        }
    }

    /// <summary>Animated sweep instead of a known progress.</summary>
    [Category("Behavior")]
    public bool IsIndeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value) return;
            _indeterminate = value;
            if (value && Host is not null) StartTicker();
            InvalidateVisual();
        }
    }

    /// <summary>Shows the percentage (determinate mode).</summary>
    [Category("Appearance")]
    public bool ShowText { get; set; }

    /// <summary>Bar orientation.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <inheritdoc />
    protected override Size DefaultSize => Orientation == Orientation.Horizontal ? new Size(160f, ShowText ? 16f : 6f) : new Size(6f, 120f);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        float radius = MathF.Min(widget.Width, widget.Height) * 0.5f;
        dc.FillRoundedRect(widget, radius, Theme.Palette.Track.Darken(0.25f));
        var accent = style.Accent;

        if (_indeterminate)
        {
            using (dc.Clip(widget))
            {
                float length = (horizontal ? widget.Width : widget.Height) * 0.3f;
                float travel = (horizontal ? widget.Width : widget.Height) + length;
                float pos = (float)(_phase % 1.0) * travel - length;
                var r = horizontal
                    ? new Rect(widget.X + pos, widget.Y, length, widget.Height)
                    : new Rect(widget.X, widget.Bottom - pos - length, widget.Width, length);
                dc.FillRoundedRect(r, radius, accent);
            }
            return;
        }

        float f = (float)_value;
        if (f > 0f)
        {
            var fill = horizontal
                ? new Rect(widget.X, widget.Y, MathF.Max(widget.Height, widget.Width * f), widget.Height)
                : new Rect(widget.X, widget.Bottom - MathF.Max(widget.Width, widget.Height * f), widget.Width, MathF.Max(widget.Width, widget.Height * f));
            using (dc.Clip(widget))
            {
                dc.FillRoundedRect(fill, radius, horizontal ? Paint.Horizontal(accent.Darken(0.15f), accent) : Paint.Vertical(accent, accent.Darken(0.15f)));
            }
        }
        if (ShowText && horizontal)
        {
            var text = (_value * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
            dc.DrawText(text, widget, Theme.Typography.Small with { Align = HorizontalAlignment.Center, Weight = FontWeight.SemiBold }, Theme.Palette.Text);
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        if (_indeterminate) StartTicker();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private void StartTicker()
    {
        _ticker ??= new SweepTicker(this);
        Host?.AddTicker(_ticker);
    }

    private sealed class SweepTicker(ProgressBar owner) : ITickable
    {
        public bool Tick(double elapsedSeconds)
        {
            if (!owner._indeterminate || owner.Host is null) return false;
            owner._phase += elapsedSeconds * 0.7;
            owner.InvalidateVisual();
            return true;
        }
    }
}
