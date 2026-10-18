// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Momentary push button with text and/or icon (bitmap, SVG or vector callback). Raises <see cref="Click"/> on release
/// over the button (or repeatedly while held when <see cref="RepeatWhileHeld"/>), and can drive a parameter as a
/// momentary switch (1 while pressed, 0 when released).
/// </summary>
public class Button : Control
{
    private string? _text;
    private RepeatTicker? _repeat;
    private bool _gestureOpen;
    private bool _inside = true;

    /// <summary>Creates a button.</summary>
    public Button() { }

    /// <summary>Creates a button with a caption and an optional click action.</summary>
    public Button(string text, Action? clicked = null)
    {
        _text = text;
        Clicked = clicked;
    }

    /// <summary>Caption drawn inside the button.</summary>
    [Category("Text")]
    public string? Text { get => _text; set { _text = value; InvalidateMeasure(); InvalidateVisual(); } }

    /// <summary>Bitmap icon (drawn left of the text, or centered without text).</summary>
    [Category("Appearance")]
    public ImageSource? Image { get; set; }

    /// <summary>SVG icon.</summary>
    [Category("Appearance")]
    public SvgImage? Svg { get; set; }

    /// <summary>Vector icon callback: draws into the icon rectangle with the given foreground color.</summary>
    [Browsable(false)]
    public Action<DrawingContext, Rect, Color>? IconRenderer { get; set; }

    /// <summary>Icon size in DIPs.</summary>
    [Category("Layout")]
    public float IconSize { get; set; } = 16f;

    /// <summary>Visual variant.</summary>
    [Category("Appearance")]
    public ButtonAppearance Appearance { get; set; } = ButtonAppearance.Normal;

    /// <summary>Text style (null = theme normal text, centered, medium weight).</summary>
    [Category("Text")]
    public TextStyle? TextStyle { get; set; }

    /// <summary>Optional parameter driven as a momentary switch (1 while pressed).</summary>
    [Category("Value")]
    public IParameter? Parameter { get; set; }

    /// <summary>Fires <see cref="Click"/> on press, then repeatedly while held.</summary>
    [Category("Behavior")]
    public bool RepeatWhileHeld { get; set; }

    /// <summary>Delay before auto-repeat starts (seconds).</summary>
    [Category("Behavior")]
    public double RepeatDelay { get; set; } = 0.4;

    /// <summary>Auto-repeat interval (seconds).</summary>
    [Category("Behavior")]
    public double RepeatInterval { get; set; } = 0.08;

    /// <summary>Raised when the button is clicked.</summary>
    public event Action<Button>? Click;

    /// <summary>Action invoked on click (command style).</summary>
    [Browsable(false)]
    public Action? Clicked { get; set; }

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            float w = 24f;
            if (!string.IsNullOrEmpty(_text)) w = TextMeasurer.Measure(_text, EffectiveTextStyle).Width + 24f;
            if (HasIcon) w += IconSize + (string.IsNullOrEmpty(_text) ? 0f : 6f);
            return new Size(MathF.Max(w, 28f), Theme.Metrics.ControlHeight + ControlSizing.LabelAndValueHeight(this));
        }
    }

    private bool HasIcon => Image is not null || Svg is not null || IconRenderer is not null;

    private TextStyle EffectiveTextStyle => TextStyle ?? Theme.Typography.Normal with { Align = HorizontalAlignment.Center, Weight = FontWeight.Medium };

    /// <summary>Raises the click (also callable from code).</summary>
    public void PerformClick()
    {
        Clicked?.Invoke();
        Click?.Invoke(this);
        OnClick();
    }

    /// <summary>Called on every click (after the events).</summary>
    protected virtual void OnClick() { }

    /// <summary>Foreground color for the current state.</summary>
    protected Color ForegroundColor(VectorStyle style)
    {
        if (!IsEffectivelyEnabled) return Theme.Palette.TextDisabled;
        return Appearance == ButtonAppearance.Accent ? style.Accent.Contrasting : Theme.Palette.Text;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var r = widget;
        float radius = CornerRadiusFor(r, style);
        bool pressed = IsPressed && _inside;
        switch (Appearance)
        {
            case ButtonAppearance.Accent:
                if (style.DrawShadow && !pressed) dc.DrawShadow(r, radius, style.ShadowBlur, new Point(0f, style.ShadowOffset), style.Shadow.Fade(0.6f));
                dc.FillRoundedRect(r, radius, pressed ? style.Accent.Darken(0.18f) : IsMouseOver ? style.Accent.Lighten(0.1f) : style.Accent);
                break;
            case ButtonAppearance.Flat:
                if (pressed) dc.FillRoundedRect(r, radius, style.Pressed);
                else if (IsMouseOver) dc.FillRoundedRect(r, radius, style.Fill);
                break;
            case ButtonAppearance.Outline:
                if (pressed) dc.FillRoundedRect(r, radius, style.Pressed);
                else if (IsMouseOver) dc.FillRoundedRect(r, radius, style.Hover);
                dc.DrawRoundedRect(r.Inflate(-0.5f), radius, IsMouseOver ? style.Accent : style.Frame, 1f);
                break;
            default:
                DrawBox(dc, r, style, pressed, IsMouseOver && _inside);
                break;
        }
        RenderContent(dc, pressed ? r.Offset(0f, 0.5f) : r, ForegroundColor(style));
    }

    /// <summary>Draws the text and icon over a <see cref="Control.Skin"/> (bitmap frames without baked-in text).</summary>
    [Category("Appearance")]
    public bool ShowContentOverSkin { get; set; } = true;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Skin is null || !ShowContentOverSkin) return;
        var style = EffectiveStyle;
        LayoutParts(style, out _, out var widget, out _);
        bool pressed = IsPressed && _inside;
        RenderContent(dc, pressed ? widget.Offset(0f, 0.5f) : widget, ForegroundColor(style));
    }

    /// <summary>Draws icon and text centered in <paramref name="r"/>.</summary>
    protected virtual void RenderContent(DrawingContext dc, Rect r, Color foreground)
    {
        var ts = EffectiveTextStyle;
        bool hasText = !string.IsNullOrEmpty(_text);
        float textW = hasText ? MathF.Min(TextMeasurer.Measure(_text!, ts).Width, r.Width - 8f) : 0f;
        float total = textW + (HasIcon ? IconSize + (hasText ? 6f : 0f) : 0f);
        float x = r.Center.X - total * 0.5f;
        if (HasIcon)
        {
            var icon = new Rect(x, r.Center.Y - IconSize * 0.5f, IconSize, IconSize);
            if (Image is not null) dc.DrawImage(Image, ImageLayout.Fit(Image.Size, icon, ImageFit.Contain), IsEffectivelyEnabled ? 1f : 0.5f);
            else if (Svg is not null) dc.DrawSvg(Svg, icon, 0f, IsEffectivelyEnabled ? 1f : 0.5f);
            else IconRenderer!(dc, icon, foreground);
            x += IconSize + 6f;
        }
        if (hasText)
        {
            var tr = HasIcon ? new Rect(x, r.Y, r.Right - x - 4f, r.Height) : r.Deflate(new Thickness(4f, 0f));
            dc.DrawText(_text, tr, HasIcon ? ts with { Align = HorizontalAlignment.Left } : ts, foreground);
        }
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        Press();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButton.Left || !IsPressed) return;
        e.Handled = true;
        Release(fire: Bounds.Contains(e.Position));
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && IsPressed)
        {
            bool inside = Bounds.Contains(e.Position);
            if (inside != _inside) { _inside = inside; InvalidateVisual(); }
        }
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        if (IsPressed) Release(fire: false);
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            if (!e.IsRepeat) PerformClick();
        }
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (IsPressed) Release(fire: false);
        base.OnDetached();
    }

    private void Press()
    {
        IsPressed = true;
        _inside = true;
        if (Parameter is not null && !_gestureOpen)
        {
            _gestureOpen = true;
            Parameter.BeginEdit();
            Parameter.PerformEdit(1.0);
        }
        if (RepeatWhileHeld)
        {
            PerformClick();
            _repeat ??= new RepeatTicker(() => { if (_inside) PerformClick(); });
            _repeat.Delay = RepeatDelay;
            _repeat.Interval = RepeatInterval;
            _repeat.Start(Host);
        }
        InvalidateVisual();
    }

    private void Release(bool fire)
    {
        IsPressed = false;
        _repeat?.Stop(Host);
        if (_gestureOpen)
        {
            _gestureOpen = false;
            Parameter?.PerformEdit(0.0);
            Parameter?.EndEdit();
        }
        if (fire && !RepeatWhileHeld) PerformClick();
        InvalidateVisual();
    }
}
