// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Base class of interactive widgets: vector style, optional skin (bitmap, film-strip, SVG, custom),
/// label, pressed/hover state and the label/value layout helpers shared by most controls.
/// </summary>
/// <remarks>
/// Rendering: if <see cref="Skin"/> is set it draws the widget, otherwise <see cref="RenderVector"/> does.
/// Labels and values are drawn by the control in both cases (see <see cref="VectorStyle.ShowLabel"/>).
/// </remarks>
public abstract class Control : UIElement
{
    private string? _label;
    private VectorStyle? _style;
    private IControlSkin? _skin;

    /// <summary>Creates a control.</summary>
    protected Control()
    {
        Focusable = true;
    }

    /// <summary>Caption drawn by the control (null = none).</summary>
    [Category("Text")]
    public string? Label { get => _label; set { _label = value; InvalidateVisual(); } }

    /// <summary>Per-instance style (null = theme style for this control type).</summary>
    [Category("Appearance")]
    public VectorStyle? Style { get => _style; set { _style = value; InvalidateVisual(); } }

    /// <summary>Style in effect.</summary>
    public VectorStyle EffectiveStyle => _style ?? Theme.StyleFor(this);

    /// <summary>Skin drawing the widget instead of the vector renderer (null = vector).</summary>
    [Category("Appearance")]
    public IControlSkin? Skin { get => _skin; set { _skin = value; InvalidateVisual(); } }

    /// <summary><c>true</c> while the primary button is held on the control.</summary>
    [Browsable(false)]
    public bool IsPressed { get; protected set; }

    /// <summary>Natural size used when no explicit size is given.</summary>
    protected virtual Size DefaultSize => new(64f, 24f);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available) => DefaultSize;

    /// <summary>State passed to skins.</summary>
    protected virtual SkinState GetSkinState(Rect widgetBounds) => new(widgetBounds, 0.0, 0, IsMouseOver, IsPressed, IsEffectivelyEnabled, IsFocused);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var style = EffectiveStyle;
        LayoutParts(style, out var label, out var widget, out var value);
        if (Skin is not null) Skin.Render(dc, this, GetSkinState(widget));
        else RenderVector(dc, widget, style);
        if (!label.IsEmpty) RenderLabel(dc, label, style);
        if (!value.IsEmpty) RenderValue(dc, value, style);
    }

    /// <summary>Vector rendering of the widget area.</summary>
    protected abstract void RenderVector(DrawingContext dc, Rect widget, VectorStyle style);

    /// <summary>Draws the label.</summary>
    protected virtual void RenderLabel(DrawingContext dc, Rect area, VectorStyle style)
        => dc.DrawText(Label, area, style.LabelText, style.LabelColor);

    /// <summary>Draws the value text (controls with a value override <see cref="ValueText"/>).</summary>
    protected virtual void RenderValue(DrawingContext dc, Rect area, VectorStyle style)
        => dc.DrawText(ValueText, area, style.ValueText, style.ValueColor);

    /// <summary>Text drawn in the value area (null = no value area).</summary>
    public virtual string? ValueText => null;

    /// <summary>
    /// Splits the bounds into label (top), widget and value (bottom) areas according to the style.
    /// Override to change the arrangement (e.g. label on the left for sliders).
    /// </summary>
    protected virtual void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        var r = Bounds;
        label = Rect.Empty;
        value = Rect.Empty;
        if (style.ShowLabel && !string.IsNullOrEmpty(Label))
        {
            float h = style.LabelText.Size * 1.5f;
            label = r.TakeTop(h);
            r = Rect.FromEdges(r.X, r.Y + h, r.Right, r.Bottom);
        }
        if (style.ShowValue && ValueText is not null)
        {
            float h = style.ValueText.Size * 1.5f;
            value = r.TakeBottom(h);
            r = Rect.FromEdges(r.X, r.Y, r.Right, r.Bottom - h);
        }
        if (style.WidgetFraction < 1f)
        {
            float f = Math.Clamp(style.WidgetFraction, 0.05f, 1f);
            r = Rect.FromCenter(r.Center, r.Width * f, r.Height * f);
        }
        widget = r;
    }

    /// <summary>Corner radius for a rectangle according to <see cref="VectorStyle.Roundness"/>.</summary>
    protected static float CornerRadiusFor(Rect r, VectorStyle style) => MathF.Min(r.Width, r.Height) * 0.5f * Math.Clamp(style.Roundness, 0f, 1f);

    /// <summary>Draws a standard raised box (shadow, fill, hover overlay, frame) used by buttons and boxes.</summary>
    protected void DrawBox(DrawingContext dc, Rect r, VectorStyle style, bool pressed, bool hovered, Color? fillOverride = null)
    {
        float radius = CornerRadiusFor(r, style);
        if (style.DrawShadow && !pressed) dc.DrawShadow(r, radius, style.ShadowBlur, new Point(0f, style.ShadowOffset), style.Shadow.Fade(0.6f));
        var fill = fillOverride ?? (pressed ? style.Pressed : style.Fill);
        dc.FillRoundedRect(r, radius, fill);
        if (hovered && !pressed) dc.FillRoundedRect(r, radius, style.Hover);
        if (style.DrawFrame) dc.DrawRoundedRect(r.Inflate(-style.FrameThickness * 0.5f), radius, style.Frame, style.FrameThickness);
    }

    /// <summary>Draws the keyboard focus ring.</summary>
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        if (IsFocused && Host?.ShowFocusRings == true)
        {
            var style = EffectiveStyle;
            dc.DrawRoundedRect(Bounds.Inflate(1.5f), CornerRadiusFor(Bounds, style) + 1.5f, Theme.Palette.Focus, Theme.Metrics.FocusWidth);
        }
    }
}
