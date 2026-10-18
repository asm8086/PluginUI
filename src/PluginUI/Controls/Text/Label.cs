// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Static or real-time text: single line (with ellipsis) or wrapped multi-line text, aligned in its bounds.
/// Set <see cref="TextSource"/> to refresh the text on every frame (CPU load, voice count, tempo...).
/// </summary>
public class Label : UIElement
{
    private string? _text;
    private Func<string>? _textSource;
    private SourceTicker? _ticker;

    /// <summary>Creates an empty label.</summary>
    public Label() { }

    /// <summary>Creates a label with a text.</summary>
    public Label(string? text) => _text = text;

    /// <summary>Displayed text.</summary>
    [Category("Text")]
    public string? Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            // Auto-sized or wrapped labels depend on their text for layout; fixed-size ones only repaint.
            bool remeasure = float.IsNaN(Width) || (Wrap && float.IsNaN(Height));
            _text = value;
            if (remeasure) InvalidateMeasure(); else InvalidateVisual();
        }
    }

    /// <summary>
    /// Called on every frame while attached: its result replaces <see cref="Text"/> when it changes
    /// (real-time text). Null stops the refresh.
    /// </summary>
    [Browsable(false)]
    public Func<string>? TextSource
    {
        get => _textSource;
        set
        {
            _textSource = value;
            if (value is not null && Host is not null) StartTicker();
        }
    }

    /// <summary>Text style (null = theme normal text).</summary>
    [Category("Text")]
    public TextStyle? TextStyle { get; set; }

    /// <summary>Text color (null = theme text color, or dimmed text when <see cref="IsDim"/>).</summary>
    [Category("Appearance")]
    public Color? Foreground { get; set; }

    /// <summary>Uses the theme's secondary text color.</summary>
    [Category("Appearance")]
    public bool IsDim { get; set; }

    /// <summary>Wraps on word boundaries (multi-line).</summary>
    [Category("Behavior")]
    public bool Wrap { get; set; }

    /// <summary>Horizontal text alignment (null = from the text style).</summary>
    [Category("Layout")]
    public HorizontalAlignment? TextAlignment { get; set; }

    /// <summary>Vertical text alignment (null = from the text style).</summary>
    [Category("Layout")]
    public VerticalAlignment? VerticalTextAlignment { get; set; }

    /// <summary>Optional background fill.</summary>
    [Category("Appearance")]
    public Color? Background { get; set; }

    /// <summary>Corner radius of the background.</summary>
    [Category("Appearance")]
    public float CornerRadius { get; set; }

    /// <summary>Inner spacing around the text.</summary>
    [Category("Layout")]
    public Thickness Padding { get; set; }

    /// <summary>Text style in effect.</summary>
    protected TextStyle EffectiveTextStyle
    {
        get
        {
            var s = TextStyle ?? Theme.Typography.Normal;
            if (TextAlignment is { } h) s = s with { Align = h };
            if (VerticalTextAlignment is { } v) s = s with { VerticalAlign = v };
            if (Wrap) s = s with { Wrap = true, Ellipsis = false, VerticalAlign = VerticalTextAlignment ?? VerticalAlignment.Top };
            return s;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var style = EffectiveTextStyle;
        float maxWidth = Wrap ? MathF.Max(1f, available.Width - Padding.Horizontal) : float.PositiveInfinity;
        var size = TextMeasurer.Measure(_text ?? string.Empty, style with { Ellipsis = false }, maxWidth);
        if (string.IsNullOrEmpty(_text)) size = new Size(0f, TextMeasurer.Measure(" ", style).Height);
        return new Size(size.Width + Padding.Horizontal + 1f, size.Height + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        if (Background is { } bg)
        {
            if (CornerRadius > 0f) dc.FillRoundedRect(Bounds, CornerRadius, bg);
            else dc.FillRect(Bounds, bg);
        }
        var color = Foreground ?? (IsDim ? Theme.Palette.TextDim : Theme.Palette.Text);
        dc.DrawText(_text, Bounds.Deflate(Padding), EffectiveTextStyle, color);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        if (_textSource is not null) StartTicker();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private void StartTicker()
    {
        _ticker ??= new SourceTicker(this);
        _ticker.Refresh();
        Host?.AddTicker(_ticker);
    }

    private sealed class SourceTicker(Label owner) : ITickable
    {
        public void Refresh()
        {
            if (owner._textSource is { } source) owner.Text = source();
        }

        public bool Tick(double elapsedSeconds)
        {
            if (owner._textSource is null || owner.Host is null) return false;
            Refresh();
            return true;
        }
    }
}
