// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Text measurement service (available during layout through <see cref="UIElement.TextMeasurer"/>).
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Size of the laid-out text in DIPs.</summary>
    Size Measure(string text, TextStyle style, float maxWidth = float.PositiveInfinity);

    /// <summary>Horizontal position of the caret placed before character <paramref name="index"/> (single line).</summary>
    float CaretX(string text, TextStyle style, int index);

    /// <summary>Index of the caret position closest to <paramref name="x"/> (single line).</summary>
    int HitTest(string text, TextStyle style, float x);
}

/// <summary>
/// Approximate measurer used when no renderer is available (unit tests, layout before attachment).
/// </summary>
public sealed class FallbackTextMeasurer : ITextMeasurer
{
    /// <summary>Shared instance.</summary>
    public static FallbackTextMeasurer Instance { get; } = new();

    private static float CharWidth(TextStyle style) => style.Size * (style.Weight >= FontWeight.SemiBold ? 0.58f : 0.53f);

    /// <inheritdoc />
    public Size Measure(string text, TextStyle style, float maxWidth = float.PositiveInfinity)
    {
        if (string.IsNullOrEmpty(text)) return new Size(0f, style.Size * 1.33f);
        float w = text.Length * CharWidth(style);
        float lineHeight = style.Size * 1.33f;
        if (style.Wrap && w > maxWidth && maxWidth > 0f)
        {
            int lines = (int)MathF.Ceiling(w / maxWidth);
            return new Size(maxWidth, lines * lineHeight);
        }
        return new Size(w, lineHeight);
    }

    /// <inheritdoc />
    public float CaretX(string text, TextStyle style, int index) => Math.Clamp(index, 0, text.Length) * CharWidth(style);

    /// <inheritdoc />
    public int HitTest(string text, TextStyle style, float x) => Math.Clamp((int)MathF.Round(x / CharWidth(style)), 0, text.Length);
}
