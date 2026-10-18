// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Clickable link text: accent colored, underlined on hover, hand cursor. Opens <see cref="Url"/> in the default
/// browser (or raises <see cref="Clicked"/> only when no URL is set). iPlug2 <c>IURLControl</c>.
/// </summary>
public class Hyperlink : Control
{
    private string _text = string.Empty;
    private bool _visited;

    /// <summary>Creates a link.</summary>
    public Hyperlink()
    {
        Cursor = StandardCursor.Hand;
    }

    /// <summary>Creates a link with a text and a URL.</summary>
    public Hyperlink(string text, string? url = null) : this()
    {
        _text = text ?? string.Empty;
        Url = url;
    }

    /// <summary>Displayed text.</summary>
    [Category("Text")]
    public string Text { get => _text; set { _text = value ?? string.Empty; InvalidateMeasure(); } }

    /// <summary>Target opened with <see cref="Shell.Open"/> (null = only raise <see cref="Clicked"/>).</summary>
    [Category("Text")]
    public string? Url { get; set; }

    /// <summary>Text style (null = theme normal text).</summary>
    [Category("Text")]
    public TextStyle? TextStyle { get; set; }

    /// <summary>Link color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? LinkColor { get; set; }

    /// <summary>Color after the link was opened once (null = same as <see cref="LinkColor"/>).</summary>
    [Category("Appearance")]
    public Color? VisitedColor { get; set; }

    /// <summary>Underlines the text permanently (otherwise only on hover).</summary>
    [Category("Appearance")]
    public bool AlwaysUnderline { get; set; }

    /// <summary>Raised when the link is activated (click, Enter or Space).</summary>
    public event Action<Hyperlink>? Clicked;

    private TextStyle EffectiveTextStyle => (TextStyle ?? Theme.Typography.Normal) with { Wrap = false };

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var s = TextMeasurer.Measure(_text, EffectiveTextStyle with { Ellipsis = false });
        return new Size(s.Width + 2f, MathF.Max(s.Height, 16f));
    }

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var ts = EffectiveTextStyle;
        var baseColor = LinkColor ?? style.Accent;
        var color = !IsEffectivelyEnabled ? Theme.Palette.TextDisabled
            : IsPressed ? baseColor.Darken(0.2f)
            : _visited && VisitedColor is { } v ? v
            : IsMouseOver ? baseColor.Lighten(0.15f) : baseColor;
        dc.DrawText(_text, widget, ts, color);
        if ((IsMouseOver || AlwaysUnderline) && IsEffectivelyEnabled)
        {
            var size = TextMeasurer.Measure(_text, ts with { Ellipsis = false });
            float w = MathF.Min(size.Width, widget.Width);
            float x = ts.Align switch
            {
                HorizontalAlignment.Center => widget.Center.X - w * 0.5f,
                HorizontalAlignment.Right => widget.Right - w,
                _ => widget.X,
            };
            float y = MathF.Round(widget.Center.Y + size.Height * 0.42f) + 0.5f;
            dc.DrawLine(new Point(x, y), new Point(x + w, y), color, 1f);
        }
    }

    /// <summary>Opens the link and raises <see cref="Clicked"/>.</summary>
    public void Activate()
    {
        _visited = true;
        Clicked?.Invoke(this);
        if (!string.IsNullOrEmpty(Url)) Shell.Open(Url);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        IsPressed = true;
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsPressed) return;
        IsPressed = false;
        if (Bounds.Contains(e.Position)) Activate();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        IsPressed = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (e.Key is Key.Enter or Key.Space)
        {
            Activate();
            e.Handled = true;
        }
    }
}
