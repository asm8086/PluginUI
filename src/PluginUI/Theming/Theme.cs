// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Color palette of a theme.
/// </summary>
public sealed record Palette(
    Color Background,
    Color Surface,
    Color SurfaceRaised,
    Color Border,
    Color Track,
    Color Accent,
    Color AccentAlt,
    Color Text,
    Color TextDim,
    Color TextDisabled,
    Color Warning,
    Color Danger,
    Color Success,
    Color Shadow,
    Color Focus,
    Color Selection,
    Color Hover)
{
    /// <summary>Dark studio palette (default).</summary>
    public static Palette Dark { get; } = new(
        Background: Color.FromRgb(0x1C1D21),
        Surface: Color.FromRgb(0x26282E),
        SurfaceRaised: Color.FromRgb(0x32353D),
        Border: Color.FromRgb(0x3B3E47),
        Track: Color.FromRgb(0x43464F),
        Accent: Color.FromRgb(0x4FC3F7),
        AccentAlt: Color.FromRgb(0xFFB74D),
        Text: Color.FromRgb(0xE8E8EA),
        TextDim: Color.FromRgb(0x9BA0A8),
        TextDisabled: Color.FromRgb(0x5E626B),
        Warning: Color.FromRgb(0xFFCA28),
        Danger: Color.FromRgb(0xEF5350),
        Success: Color.FromRgb(0x66BB6A),
        Shadow: Color.FromRgb(0x000000, 0.45f),
        Focus: Color.FromRgb(0x4FC3F7, 0.8f),
        Selection: Color.FromRgb(0x4FC3F7, 0.30f),
        Hover: Color.FromRgb(0xFFFFFF, 0.06f));

    /// <summary>Light palette.</summary>
    public static Palette Light { get; } = new(
        Background: Color.FromRgb(0xEEF0F3),
        Surface: Color.FromRgb(0xFFFFFF),
        SurfaceRaised: Color.FromRgb(0xF6F7F9),
        Border: Color.FromRgb(0xC9CDD4),
        Track: Color.FromRgb(0xD5D9DF),
        Accent: Color.FromRgb(0x1E88E5),
        AccentAlt: Color.FromRgb(0xF57C00),
        Text: Color.FromRgb(0x1F2328),
        TextDim: Color.FromRgb(0x5E6670),
        TextDisabled: Color.FromRgb(0xA4AAB2),
        Warning: Color.FromRgb(0xF9A825),
        Danger: Color.FromRgb(0xE53935),
        Success: Color.FromRgb(0x43A047),
        Shadow: Color.FromRgb(0x000000, 0.18f),
        Focus: Color.FromRgb(0x1E88E5, 0.8f),
        Selection: Color.FromRgb(0x1E88E5, 0.25f),
        Hover: Color.FromRgb(0x000000, 0.05f));
}

/// <summary>Font families and sizes of a theme.</summary>
public sealed record Typography(
    string Family = "Segoe UI",
    string MonospaceFamily = "Consolas",
    float SmallSize = 10.5f,
    float NormalSize = 12f,
    float LargeSize = 14f,
    float TitleSize = 18f)
{
    /// <summary>Normal text style.</summary>
    public TextStyle Normal => new(Family, NormalSize);

    /// <summary>Small text style.</summary>
    public TextStyle Small => new(Family, SmallSize);

    /// <summary>Large text style.</summary>
    public TextStyle Large => new(Family, LargeSize);

    /// <summary>Title text style.</summary>
    public TextStyle Title => new(Family, TitleSize, FontWeight.SemiBold);

    /// <summary>Monospace text style.</summary>
    public TextStyle Monospace => new(MonospaceFamily, NormalSize);
}

/// <summary>Dimensions of a theme.</summary>
public sealed record Metrics(
    float CornerRadius = 4f,
    float Spacing = 8f,
    float StrokeWidth = 1f,
    float FocusWidth = 1.5f,
    float TrackWidth = 4f,
    float ShadowBlur = 6f,
    float ShadowOffsetY = 2f,
    float ControlHeight = 24f);

/// <summary>
/// Visual parameters of vector-drawn controls (the "IVStyle" of iPlug2): what to show and which colors to use.
/// Every control resolves its style as <c>control.Style ?? theme.StyleFor(control)</c>; derive variants with <c>with</c>.
/// </summary>
public sealed record VectorStyle(
    Color Background,
    Color Fill,
    Color Pressed,
    Color Frame,
    Color Accent,
    Color LabelColor,
    Color ValueColor,
    Color Shadow,
    Color Hover,
    TextStyle LabelText,
    TextStyle ValueText,
    bool ShowLabel = true,
    bool ShowValue = true,
    bool DrawFrame = true,
    bool DrawShadow = true,
    float Roundness = 0.3f,
    float FrameThickness = 1f,
    float TrackThickness = 4f,
    float ShadowBlur = 5f,
    float ShadowOffset = 2f,
    float WidgetFraction = 1f)
{
    /// <summary>Returns a variant with another accent color.</summary>
    public VectorStyle WithAccent(Color accent) => this with { Accent = accent };

    /// <summary>Returns a variant without label and value texts.</summary>
    public VectorStyle Bare() => this with { ShowLabel = false, ShowValue = false };
}

/// <summary>
/// Complete theme: palette, typography, metrics, the default vector style and optional per-control-type styles.
/// </summary>
public sealed record Theme(string Name, Palette Palette, Typography Typography, Metrics Metrics)
{
    private VectorStyle? _defaultStyle;
    private Dictionary<Type, VectorStyle>? _typeStyles;

    /// <summary>Dark theme (default).</summary>
    public static Theme Dark { get; } = new("Dark", Palette.Dark, new Typography(), new Metrics());

    /// <summary>Light theme.</summary>
    public static Theme Light { get; } = new("Light", Palette.Light, new Typography(), new Metrics());

    /// <summary>Default vector style derived from the palette.</summary>
    public VectorStyle DefaultStyle
    {
        get => _defaultStyle ??= new VectorStyle(
            Background: Palette.Surface,
            Fill: Palette.SurfaceRaised,
            Pressed: Palette.SurfaceRaised.Lighten(0.08f),
            Frame: Palette.Border,
            Accent: Palette.Accent,
            LabelColor: Palette.TextDim,
            ValueColor: Palette.Text,
            Shadow: Palette.Shadow,
            Hover: Palette.Hover,
            LabelText: new TextStyle(Typography.Family, Typography.SmallSize, FontWeight.SemiBold, Align: HorizontalAlignment.Center),
            ValueText: new TextStyle(Typography.Family, Typography.SmallSize, Align: HorizontalAlignment.Center),
            Roundness: 0.3f,
            FrameThickness: Metrics.StrokeWidth,
            TrackThickness: Metrics.TrackWidth,
            ShadowBlur: Metrics.ShadowBlur,
            ShadowOffset: Metrics.ShadowOffsetY);
        init => _defaultStyle = value;
    }

    /// <summary>Returns a copy of the theme with another accent color.</summary>
    public Theme WithAccent(Color accent) => this with
    {
        Palette = Palette with { Accent = accent, Focus = accent.WithAlpha(0.8f), Selection = accent.WithAlpha(0.3f) },
        _defaultStyle = null,
        _typeStyles = _typeStyles is null ? null : new Dictionary<Type, VectorStyle>(_typeStyles),
    };

    /// <summary>Returns a copy of the theme where controls of type <typeparamref name="T"/> (and subclasses) use <paramref name="style"/>.</summary>
    public Theme WithStyle<T>(VectorStyle style) where T : Control
    {
        var styles = _typeStyles is null ? new Dictionary<Type, VectorStyle>() : new Dictionary<Type, VectorStyle>(_typeStyles);
        styles[typeof(T)] = style;
        return this with { _typeStyles = styles };
    }

    /// <summary>Style for a control: the most specific type override, or <see cref="DefaultStyle"/>.</summary>
    public VectorStyle StyleFor(Control control)
    {
        if (_typeStyles is not null)
        {
            for (var t = control.GetType(); t is not null && t != typeof(object); t = t.BaseType)
            {
                if (_typeStyles.TryGetValue(t, out var style)) return style;
            }
        }
        return DefaultStyle;
    }
}
