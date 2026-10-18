// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Shape of a <see cref="Led"/>.</summary>
public enum LedShape
{
    /// <summary>Round lens.</summary>
    Round,
    /// <summary>Rounded square.</summary>
    Square,
}

/// <summary>
/// Indicator LED with a lens highlight and a glow proportional to its brightness. Drive it with
/// <see cref="IsOn"/>, <see cref="Brightness"/>, or a <see cref="BrightnessSource"/> polled every frame
/// (signal present, clip, MIDI activity...). iPlug2 <c>ILEDControl</c>.
/// </summary>
public class Led : Control
{
    private float _brightness;
    private Func<float>? _source;
    private LedTicker? _ticker;

    /// <summary>Creates an LED.</summary>
    public Led()
    {
        Focusable = false;
    }

    /// <summary>On/off state (sets <see cref="Brightness"/> to 1 or 0).</summary>
    [Category("Value")]
    public bool IsOn { get => _brightness > 0.5f; set => Brightness = value ? 1f : 0f; }

    /// <summary>Brightness 0..1.</summary>
    [Category("Behavior")]
    public float Brightness
    {
        get => _brightness;
        set
        {
            value = Math.Clamp(float.IsNaN(value) ? 0f : value, 0f, 1f);
            if (MathF.Abs(value - _brightness) < 0.002f) return;
            _brightness = value;
            InvalidateVisual();
        }
    }

    /// <summary>Polled every frame while attached (audio-thread safe values only, e.g. a volatile field).</summary>
    [Browsable(false)]
    public Func<float>? BrightnessSource
    {
        get => _source;
        set
        {
            _source = value;
            if (value is not null && Host is not null) StartTicker();
        }
    }

    /// <summary>Decay of the brightness in units per second when the source drops (0 = follow instantly).</summary>
    [Category("Data")]
    public float DecayPerSecond { get; set; } = 6f;

    /// <summary>LED color (null = theme accent).</summary>
    [Category("Value")]
    public Color? Color { get; set; }

    /// <summary>Shape.</summary>
    [Category("Appearance")]
    public LedShape Shape { get; set; } = LedShape.Round;

    /// <summary>Draws a halo around a lit LED.</summary>
    [Category("Appearance")]
    public bool Glow { get; set; } = true;

    /// <inheritdoc />
    protected override Size DefaultSize => new(14f, 14f);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var color = Color ?? style.Accent;
        var off = Theme.Palette.Background.Darken(0.2f);
        var r = widget.CenteredSquare();
        float b = _brightness;
        var c = r.Center;
        float radius = r.Width * 0.5f;
        var lit = PluginUI.Color.Lerp(color.Darken(0.75f), color, b);

        if (Glow && b > 0.01f)
        {
            for (int i = 3; i >= 1; i--)
            {
                float grow = radius * 0.35f * i;
                var halo = color.WithAlpha(0.07f * b);
                if (Shape == LedShape.Round) dc.FillCircle(c, radius + grow, halo);
                else dc.FillRoundedRect(r.Inflate(grow), radius * 0.35f + grow, halo);
            }
        }

        if (Shape == LedShape.Round)
        {
            dc.FillCircle(c, radius, off);
            dc.FillCircle(c, radius - 1f, new RadialGradientPaint(new Point(0.5f, 0.45f), 0.55f, 0.55f,
                [new(0f, lit.Lighten(0.35f * b)), new(0.7f, lit), new(1f, lit.Darken(0.3f))]));
            dc.FillEllipse(new Point(c.X - radius * 0.22f, c.Y - radius * 0.3f), radius * 0.38f, radius * 0.24f, PluginUI.Color.White.WithAlpha(0.18f + 0.25f * b));
        }
        else
        {
            float cr = radius * 0.35f;
            dc.FillRoundedRect(r, cr, off);
            var inner = r.Inflate(-1f);
            dc.FillRoundedRect(inner, cr, Paint.Vertical(lit.Lighten(0.25f * b), lit.Darken(0.2f)));
            dc.FillRoundedRect(inner.TakeTop(inner.Height * 0.4f).Inflate(-1.5f, -1f), cr, PluginUI.Color.White.WithAlpha(0.08f + 0.15f * b));
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        if (_source is not null) StartTicker();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private void StartTicker()
    {
        _ticker ??= new LedTicker(this);
        Host?.AddTicker(_ticker);
    }

    private sealed class LedTicker(Led owner) : ITickable
    {
        public bool Tick(double elapsedSeconds)
        {
            if (owner._source is not { } source || owner.Host is null) return false;
            float target = Math.Clamp(source(), 0f, 1f);
            float current = owner._brightness;
            owner.Brightness = target >= current || owner.DecayPerSecond <= 0f
                ? target
                : MathF.Max(target, current - owner.DecayPerSecond * (float)elapsedSeconds);
            return true;
        }
    }
}
