// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// On/off button (bypass, enable, mode toggles). Toggles on press; the "on" state is drawn with the accent color,
/// or with a glowing LED when <see cref="ShowLed"/> is set. Text can differ between states.
/// </summary>
public class ToggleButton : ValueControl
{
    /// <summary>Creates a toggle button.</summary>
    public ToggleButton()
    {
        StepCount = 1;
    }

    /// <summary>Creates a toggle button with a caption.</summary>
    public ToggleButton(string text) : this() => Text = text;

    /// <summary>Caption used for both states when <see cref="OnText"/>/<see cref="OffText"/> are not set.</summary>
    [Category("Text")]
    public string? Text { get; set; }

    /// <summary>Caption in the on state.</summary>
    [Category("Text")]
    public string? OnText { get; set; }

    /// <summary>Caption in the off state.</summary>
    [Category("Text")]
    public string? OffText { get; set; }

    /// <summary>Draws an LED left of the caption instead of filling the button with the accent color.</summary>
    [Category("Appearance")]
    public bool ShowLed { get; set; }

    /// <summary>LED / on color (null = style accent).</summary>
    [Category("Appearance")]
    public Color? OnColor { get; set; }

    /// <summary>Text style (null = theme normal text, centered).</summary>
    [Category("Text")]
    public TextStyle? TextStyle { get; set; }

    /// <summary><c>true</c> in the on state.</summary>
    [Category("Value")]
    public bool IsOn
    {
        get => Value >= 0.5;
        set => Value = value ? 1.0 : 0.0;
    }

    /// <inheritdoc />
    public override string? ValueText => null;

    /// <summary>Caption for the current state.</summary>
    protected string? CurrentText
    {
        get
        {
            var t = IsOn ? OnText ?? Text : OffText ?? Text;
            if (t is null && Parameter is not null && Label is null) t = Parameter.ToDisplayString(Value);
            return t;
        }
    }

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            var ts = EffectiveTextStyle;
            float w = MathF.Max(MeasureCaption(OnText ?? Text, ts), MeasureCaption(OffText ?? Text, ts)) + (ShowLed ? 34f : 24f);
            float h = Theme.Metrics.ControlHeight;
            if (EffectiveStyle.ShowLabel && !string.IsNullOrEmpty(Label)) h += EffectiveStyle.LabelText.Size * 1.5f;
            return new Size(MathF.Max(48f, w), h);
        }
    }

    private float MeasureCaption(string? text, TextStyle ts) => string.IsNullOrEmpty(text) ? 0f : TextMeasurer.Measure(text, ts).Width;

    private TextStyle EffectiveTextStyle => TextStyle ?? Theme.Typography.Normal with { Align = HorizontalAlignment.Center, Weight = FontWeight.Medium };

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        bool on = IsOn;
        var accent = OnColor ?? style.Accent;
        float radius = CornerRadiusFor(widget, style);
        var p = Theme.Palette;
        Color fg;
        if (ShowLed)
        {
            DrawBox(dc, widget, style, IsPressed, IsMouseOver);
            fg = IsEffectivelyEnabled ? (on ? p.Text : p.TextDim) : p.TextDisabled;
            var led = new Point(widget.X + 12f, widget.Center.Y);
            Glyphs.Led(dc, led, 3.5f, accent, on, p.Track);
            dc.DrawText(CurrentText, Rect.FromEdges(widget.X + 22f, widget.Y, widget.Right - 6f, widget.Bottom), EffectiveTextStyle, fg);
            return;
        }

        if (on)
        {
            if (style.DrawShadow) dc.DrawShadow(widget, radius, style.ShadowBlur, new Point(0f, style.ShadowOffset), accent.WithAlpha(0.25f));
            dc.FillRoundedRect(widget, radius, IsPressed ? accent.Darken(0.15f) : IsMouseOver ? accent.Lighten(0.08f) : accent);
            fg = accent.Contrasting;
        }
        else
        {
            DrawBox(dc, widget, style, IsPressed, IsMouseOver);
            fg = p.TextDim;
        }
        if (!IsEffectivelyEnabled) fg = p.TextDisabled;
        dc.DrawText(CurrentText, widget.Deflate(new Thickness(4f, 0f)), EffectiveTextStyle, fg);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        IsPressed = true;
        SetValueByUser(IsOn ? 0.0 : 1.0);
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        IsPressed = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter && IsEffectivelyEnabled)
        {
            e.Handled = true;
            if (!e.IsRepeat) SetValueByUser(IsOn ? 0.0 : 1.0);
            return;
        }
        base.OnKeyDown(e);
    }
}
