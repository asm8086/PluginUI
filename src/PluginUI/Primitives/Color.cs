// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI;

/// <summary>
/// Straight (non-premultiplied) RGBA color, components in [0, 1].
/// </summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    /// <summary>Fully transparent black.</summary>
    public static Color Transparent => new(0f, 0f, 0f, 0f);

    /// <summary>Opaque black.</summary>
    public static Color Black => new(0f, 0f, 0f);

    /// <summary>Opaque white.</summary>
    public static Color White => new(1f, 1f, 1f);

    /// <summary><c>true</c> if the color is fully transparent.</summary>
    public bool IsTransparent => A <= 0f;

    /// <summary>Creates a color from a 0xRRGGBB value.</summary>
    public static Color FromRgb(uint rgb, float alpha = 1f)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    /// <summary>Creates a color from 8-bit components.</summary>
    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>
    /// Parses "#RGB", "#RRGGBB" or "#RRGGBBAA" (the leading '#' is optional).
    /// </summary>
    public static Color Parse(string text)
    {
        if (!TryParse(text, out var color)) throw new FormatException($"Invalid color '{text}'.");
        return color;
    }

    /// <summary>Tries to parse a hexadecimal color string.</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length != 6 && s.Length != 8) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        color = s.Length == 6 ? FromRgb(v) : new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
        return true;
    }

    /// <summary>Formats as "#RRGGBB" or "#RRGGBBAA" when not opaque.</summary>
    public string ToHex()
    {
        static int Byte(float v) => (int)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
        return A >= 1f
            ? string.Create(CultureInfo.InvariantCulture, $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}{Byte(A):X2}");
    }

    /// <summary>Returns the color with another alpha.</summary>
    public Color WithAlpha(float alpha) => this with { A = Math.Clamp(alpha, 0f, 1f) };

    /// <summary>Multiplies the alpha by a factor.</summary>
    public Color Fade(float factor) => this with { A = Math.Clamp(A * factor, 0f, 1f) };

    /// <summary>Linear interpolation between two colors.</summary>
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }

    /// <summary>Mixes towards white by <paramref name="amount"/> (0..1).</summary>
    public Color Lighten(float amount) => Lerp(this, White.WithAlpha(A), amount);

    /// <summary>Mixes towards black by <paramref name="amount"/> (0..1).</summary>
    public Color Darken(float amount) => Lerp(this, Black.WithAlpha(A), amount);

    /// <summary>Relative luminance (sRGB approximation).</summary>
    public float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;

    /// <summary>Black or white, whichever reads best on this color.</summary>
    public Color Contrasting => Luminance > 0.55f ? Black : White;

    /// <summary>Converts to hue (degrees 0..360), saturation and value (0..1).</summary>
    public (float H, float S, float V) ToHsv()
    {
        float max = MathF.Max(R, MathF.Max(G, B)), min = MathF.Min(R, MathF.Min(G, B));
        float d = max - min, h = 0f;
        if (d > 1e-6f)
        {
            if (max == R) h = 60f * (((G - B) / d) % 6f);
            else if (max == G) h = 60f * (((B - R) / d) + 2f);
            else h = 60f * (((R - G) / d) + 4f);
        }
        if (h < 0f) h += 360f;
        return (h, max <= 0f ? 0f : d / max, max);
    }

    /// <summary>Creates a color from hue (degrees), saturation and value (0..1).</summary>
    public static Color FromHsv(float h, float s, float v, float a = 1f)
    {
        h = ((h % 360f) + 360f) % 360f;
        s = Math.Clamp(s, 0f, 1f); v = Math.Clamp(v, 0f, 1f);
        float c = v * s, x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f)), m = v - c;
        (float r, float g, float b) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return new Color(r + m, g + m, b + m, a);
    }

    /// <inheritdoc />
    public override string ToString() => ToHex();
}
