// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Toggle switch: a pill with a sliding knob (animated), with an optional caption on the right
/// (or <see cref="OnText"/>/<see cref="OffText"/> state captions).
/// </summary>
public class SlideSwitch : ValueControl
{
    private float _position;
    private Animation? _animation;

    /// <summary>Creates a slide switch.</summary>
    public SlideSwitch()
    {
        StepCount = 1;
    }

    /// <summary>Creates a slide switch with a caption.</summary>
    public SlideSwitch(string text, bool isOn = false) : this()
    {
        Text = text;
        IsOn = isOn;
    }

    /// <summary>Caption drawn right of the switch.</summary>
    [Category("Text")]
    public string? Text { get; set; }

    /// <summary>Caption in the on state (replaces <see cref="Text"/>).</summary>
    [Category("Text")]
    public string? OnText { get; set; }

    /// <summary>Caption in the off state (replaces <see cref="Text"/>).</summary>
    [Category("Text")]
    public string? OffText { get; set; }

    /// <summary>Height of the pill in DIPs.</summary>
    [Category("Layout")]
    public float TrackHeight { get; set; } = 18f;

    /// <summary><c>true</c> in the on state.</summary>
    [Category("Value")]
    public bool IsOn
    {
        get => Value >= 0.5;
        set => Value = value ? 1.0 : 0.0;
    }

    /// <inheritdoc />
    public override string? ValueText => null;

    private string? Caption => (IsOn ? OnText : OffText) ?? Text;

    private TextStyle CaptionStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Left };

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            float w = TrackHeight * 1.9f;
            float tw = 0f;
            foreach (var t in new[] { Text, OnText, OffText })
            {
                if (!string.IsNullOrEmpty(t)) tw = MathF.Max(tw, TextMeasurer.Measure(t, CaptionStyle).Width);
            }
            if (tw > 0f) w += 8f + tw;
            float h = TrackHeight + 4f;
            if (EffectiveStyle.ShowLabel && !string.IsNullOrEmpty(Label)) h += EffectiveStyle.LabelText.Size * 1.5f;
            return new Size(MathF.Ceiling(w) + 2f, h);
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _position = IsOn ? 1f : 0f;
    }

    /// <inheritdoc />
    protected override void OnValueChanged()
    {
        base.OnValueChanged();
        float target = IsOn ? 1f : 0f;
        _animation?.Cancel();
        if (Host is not { IsAttached: true }) { _position = target; return; }
        _animation = Host.Animator.Animate(_position, target, 0.16, v => { _position = v; InvalidateVisual(); }, Easing.CubicOut);
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float h = MathF.Min(TrackHeight, widget.Height);
        var pill = new Rect(widget.X + 1f, widget.Center.Y - h * 0.5f, h * 1.9f, h);
        bool enabled = IsEffectivelyEnabled;
        var onColor = enabled ? style.Accent : p.TextDisabled;
        dc.FillRoundedRect(pill, h * 0.5f, Color.Lerp(p.Track, onColor, _position));
        dc.DrawRoundedRect(pill.Inflate(-0.5f), h * 0.5f, Color.Black.WithAlpha(0.25f), 1f);
        float knobR = h * 0.5f - 2.5f;
        float x = pill.X + h * 0.5f + (pill.Width - h) * _position;
        var c = new Point(x, pill.Center.Y);
        dc.FillCircle(new Point(c.X, c.Y + 1f), knobR + 0.5f, Color.Black.WithAlpha(0.3f));
        dc.FillCircle(c, knobR, IsPressed ? Color.FromRgb(0xD8D8DC) : Color.FromRgb(0xF4F4F6));
        if (IsMouseOver && enabled) dc.FillCircle(c, knobR + 4f, style.Hover);
        if (!string.IsNullOrEmpty(Caption))
        {
            dc.DrawText(Caption, Rect.FromEdges(pill.Right + 8f, widget.Y, widget.Right, widget.Bottom), CaptionStyle, enabled ? p.Text : p.TextDisabled);
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        IsPressed = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsPressed) return;
        IsPressed = false;
        if (Bounds.Contains(e.Position)) SetValueByUser(IsOn ? 0.0 : 1.0);
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
