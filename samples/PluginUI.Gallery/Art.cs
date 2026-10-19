// 2026/10/19 17:23:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery;

/// <summary>
/// Procedurally generated bitmaps and SVG for the skins page (a real plug-in would load PNG/SVG files or
/// embedded resources with <see cref="ImageSource.FromFile"/> / <see cref="ImageSource.FromResource"/>).
/// </summary>
internal static class Art
{
    /// <summary>Small SVG knob rotor (rendered natively on Windows 10 1703+).</summary>
    public const string RotorSvg =
        "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 64 64'>" +
        "<defs><radialGradient id='g' cx='40%' cy='35%' r='70%'><stop offset='0' stop-color='#6b7280'/><stop offset='1' stop-color='#1f2329'/></radialGradient></defs>" +
        "<circle cx='32' cy='32' r='26' fill='url(#g)' stroke='#0d0f12' stroke-width='2'/>" +
        "<rect x='30' y='9' width='4' height='16' rx='2' fill='#ffb74d'/></svg>";

    /// <summary>SVG logo.</summary>
    public const string LogoSvg =
        "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 64 64'>" +
        "<circle cx='32' cy='32' r='29' fill='#4fc3f7'/><path d='M14 38 Q24 14 32 32 T50 26' fill='none' stroke='#14171c' stroke-width='5' stroke-linecap='round'/></svg>";

    private static void Put(byte[] px, int stride, int x, int y, float r, float g, float b, float a)
    {
        int o = (y * stride + x) * 4;
        a = Math.Clamp(a, 0f, 1f);
        px[o] = (byte)(Math.Clamp(b, 0f, 1f) * a * 255f);
        px[o + 1] = (byte)(Math.Clamp(g, 0f, 1f) * a * 255f);
        px[o + 2] = (byte)(Math.Clamp(r, 0f, 1f) * a * 255f);
        px[o + 3] = (byte)(a * 255f);
    }

    /// <summary>Gradient disc.</summary>
    public static ImageSource Disc(int n)
    {
        var px = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f, d = MathF.Sqrt(dx * dx + dy * dy) / (n / 2f);
                Put(px, n, x, y, x / (float)n, 0.7f, 1f - y / (float)n, (1f - d) * 8f);
            }
        }
        return ImageSource.FromPixels(n, n, px);
    }

    /// <summary>Vertical film-strip of a brushed-metal knob with an orange indicator (2× pixel density).</summary>
    public static FilmStrip KnobStrip(int frames = 61, int size = 128)
    {
        var px = new byte[size * size * frames * 4];
        float c = size / 2f - 0.5f, radius = size * 0.42f;
        for (int f = 0; f < frames; f++)
        {
            float angle = (-135f + 270f * f / (frames - 1)) * MathF.PI / 180f;
            float ix = MathF.Sin(angle), iy = -MathF.Cos(angle);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c, d = MathF.Sqrt(dx * dx + dy * dy);
                    float edge = radius + 0.5f - d;
                    if (edge <= -1f) continue;
                    float a = Math.Clamp(edge, 0f, 1f);
                    float shade = 0.32f + 0.18f * (-(dx + dy) / (size * 0.6f)) + 0.04f * MathF.Sin(MathF.Atan2(dy, dx) * 24f);
                    float ring = d > radius * 0.86f ? 0.08f : 0f;
                    float r = shade + ring, g = shade + ring, b = shade + ring + 0.02f;
                    // Indicator: distance from the radius segment at the current angle.
                    float t = Math.Clamp(dx * ix + dy * iy, radius * 0.35f, radius * 0.8f);
                    float ex = dx - ix * t, ey = dy - iy * t;
                    float line = Math.Clamp(size * 0.035f - MathF.Sqrt(ex * ex + ey * ey), 0f, 1f);
                    r = r + (1.0f - r) * line; g = g + (0.6f - g) * line; b = b + (0.15f - b) * line;
                    Put(px, size, x, f * size + y, r, g, b, a);
                }
            }
        }
        return new FilmStrip(ImageSource.FromPixels(size, size * frames, px, pixelsPerDip: 2f), frames);
    }

    /// <summary>
    /// LED-bar meter film strip (frame 0 = silent, last frame = full scale): green, amber and red segments, unlit
    /// segments still visible, 2× density.
    /// </summary>
    public static FilmStrip MeterStrip(int frames = 25, int width = 24, int height = 192)
    {
        const int segments = 24;
        var px = new byte[width * height * frames * 4];
        float pitch = height / (float)segments;
        for (int f = 0; f < frames; f++)
        {
            int lit = (int)MathF.Round(segments * f / (float)(frames - 1));
            for (int s = 0; s < segments; s++)
            {
                float t = s / (float)(segments - 1);
                float r = t > 0.85f ? 1.0f : t > 0.65f ? 1.0f : 0.24f;
                float g = t > 0.85f ? 0.32f : t > 0.65f ? 0.72f : 0.84f;
                float b = t > 0.85f ? 0.32f : t > 0.65f ? 0.30f : 0.55f;
                float k = s < lit ? 1f : 0.16f;
                int top = (int)MathF.Round(height - (s + 1) * pitch) + 1;
                int bottom = (int)MathF.Round(height - s * pitch) - 1;
                for (int y = top; y < bottom; y++)
                {
                    for (int x = 2; x < width - 2; x++) Put(px, width, x, f * height + y, r * k, g * k, b * k, 1f);
                }
            }
        }
        return new FilmStrip(ImageSource.FromPixels(width, height * frames, px, pixelsPerDip: 2f), frames);
    }

    /// <summary>Square push-button image (off / on), 2× density.</summary>
    public static ImageSource PadButton(bool on, int size = 96)
    {
        var px = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float m = 10f, rr = 14f;
                float qx = MathF.Max(MathF.Abs(x - size / 2f + 0.5f) - (size / 2f - m - rr), 0f);
                float qy = MathF.Max(MathF.Abs(y - size / 2f + 0.5f) - (size / 2f - m - rr), 0f);
                float edge = rr - MathF.Sqrt(qx * qx + qy * qy);
                if (edge <= -1f) continue;
                float v = 1f - y / (float)size * 0.35f;
                if (on) Put(px, size, x, y, 1f * v, 0.45f * v, 0.1f * v, Math.Clamp(edge, 0f, 1f));
                else Put(px, size, x, y, 0.22f * v, 0.24f * v, 0.27f * v, Math.Clamp(edge, 0f, 1f));
            }
        }
        return ImageSource.FromPixels(size, size, px, pixelsPerDip: 2f);
    }

    /// <summary>Nine-slice frame (rounded, bevelled), 1× density with 12-pixel insets.</summary>
    public static NineSlice Frame(bool pressed)
    {
        const int n = 40;
        var px = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float qx = MathF.Max(MathF.Abs(x - n / 2f + 0.5f) - (n / 2f - 10f), 0f);
                float qy = MathF.Max(MathF.Abs(y - n / 2f + 0.5f) - (n / 2f - 10f), 0f);
                float edge = 9.5f - MathF.Sqrt(qx * qx + qy * qy);
                if (edge <= -1f) continue;
                float bevel = edge < 2f ? (y < n / 2 ^ pressed ? 0.55f : 0.12f) : (pressed ? 0.2f : 0.3f);
                Put(px, n, x, y, bevel, bevel * 1.02f, bevel * 1.1f, Math.Clamp(edge, 0f, 1f));
            }
        }
        return new NineSlice(ImageSource.FromPixels(n, n, px), new Thickness(12f));
    }

    /// <summary>Bitmap knob rotor for <see cref="ImageRotarySkin"/>: shaded cap with a white pointer, 2× density.</summary>
    public static ImageSource BitmapRotor(int size = 128)
    {
        var px = new byte[size * size * 4];
        float c = size / 2f, radius = size * 0.42f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c + 0.5f, dy = y - c + 0.5f, d = MathF.Sqrt(dx * dx + dy * dy);
                float edge = radius - d;
                if (edge <= -1f) continue;
                // Knurled rim and radial shading (the light rotates with the knob, typical of rotated bitmaps).
                float angle = MathF.Atan2(dy, dx);
                float knurl = d > radius * 0.86f ? 0.08f * MathF.Sin(angle * 36f) : 0f;
                float shade = 0.32f + 0.18f * (1f - d / radius) - dy / size * 0.25f + knurl;
                bool pointer = MathF.Abs(dx) < size * 0.025f && dy < -radius * 0.25f && dy > -radius * 0.85f;
                if (pointer) Put(px, size, x, y, 0.95f, 0.95f, 0.95f, 1f);
                else Put(px, size, x, y, shade, shade * 1.02f, shade * 1.08f, Math.Clamp(edge, 0f, 1f));
            }
        }
        return ImageSource.FromPixels(size, size, px, pixelsPerDip: 2f);
    }

    /// <summary>Characters of <see cref="PixelFont"/>, in atlas order.</summary>
    public const string PixelFontCharacters = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ.:-+%/";

    // 5 × 7 glyphs, one string per row ('#' = lit), in PixelFontCharacters order.
    private static readonly string[] Glyphs =
    [
        ".....,.....,.....,.....,.....,.....,.....",
        ".###.,#...#,#..##,#.#.#,##..#,#...#,.###.", "..#..,.##..,..#..,..#..,..#..,..#..,.###.",
        ".###.,#...#,....#,...#.,..#..,.#...,#####", "#####,...#.,..#..,...#.,....#,#...#,.###.",
        "...#.,..##.,.#.#.,#..#.,#####,...#.,...#.", "#####,#....,####.,....#,....#,#...#,.###.",
        "..##.,.#...,#....,####.,#...#,#...#,.###.", "#####,....#,...#.,..#..,.#...,.#...,.#...",
        ".###.,#...#,#...#,.###.,#...#,#...#,.###.", ".###.,#...#,#...#,.####,....#,...#.,.##..",
        ".###.,#...#,#...#,#####,#...#,#...#,#...#", "####.,#...#,#...#,####.,#...#,#...#,####.",
        ".###.,#...#,#....,#....,#....,#...#,.###.", "###..,#..#.,#...#,#...#,#...#,#..#.,###..",
        "#####,#....,#....,####.,#....,#....,#####", "#####,#....,#....,####.,#....,#....,#....",
        ".###.,#...#,#....,#.###,#...#,#...#,.####", "#...#,#...#,#...#,#####,#...#,#...#,#...#",
        ".###.,..#..,..#..,..#..,..#..,..#..,.###.", "..###,...#.,...#.,...#.,...#.,#..#.,.##..",
        "#...#,#..#.,#.#..,##...,#.#..,#..#.,#...#", "#....,#....,#....,#....,#....,#....,#####",
        "#...#,##.##,#.#.#,#.#.#,#...#,#...#,#...#", "#...#,#...#,##..#,#.#.#,#..##,#...#,#...#",
        ".###.,#...#,#...#,#...#,#...#,#...#,.###.", "####.,#...#,#...#,####.,#....,#....,#....",
        ".###.,#...#,#...#,#...#,#.#.#,#..#.,.##.#", "####.,#...#,#...#,####.,#.#..,#..#.,#...#",
        ".####,#....,#....,.###.,....#,....#,####.", "#####,..#..,..#..,..#..,..#..,..#..,..#..",
        "#...#,#...#,#...#,#...#,#...#,#...#,.###.", "#...#,#...#,#...#,#...#,#...#,.#.#.,..#..",
        "#...#,#...#,#...#,#.#.#,#.#.#,#.#.#,.#.#.", "#...#,#...#,.#.#.,..#..,.#.#.,#...#,#...#",
        "#...#,#...#,#...#,.#.#.,..#..,..#..,..#..", "#####,....#,...#.,..#..,.#...,#....,#####",
        ".....,.....,.....,.....,.....,.##..,.##..", ".....,.##..,.##..,.....,.##..,.##..,.....",
        ".....,.....,.....,#####,.....,.....,.....", ".....,..#..,..#..,#####,..#..,..#..,.....",
        "##...,##..#,...#.,..#..,.#...,#..##,...##", ".....,....#,...#.,..#..,.#...,#....,.....",
    ];

    /// <summary>
    /// 5 × 7 LCD pixel font for <see cref="BitmapText"/>: cells of 6 × 8 pixels (one pixel of spacing), 16 cells per
    /// row, colored like a backlit display.
    /// </summary>
    public static BitmapFont PixelFont(Color color)
    {
        const int cw = 6, ch = 8, columns = 16;
        int rows = (Glyphs.Length + columns - 1) / columns;
        int w = cw * columns, h = ch * rows;
        var px = new byte[w * h * 4];
        for (int g = 0; g < Glyphs.Length; g++)
        {
            var lines = Glyphs[g].Split(',');
            int ox = g % columns * cw, oy = g / columns * ch;
            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    if (lines[y][x] == '#') Put(px, w, ox + x, oy + y, color.R, color.G, color.B, 1f);
                }
            }
        }
        return new BitmapFont(ImageSource.FromPixels(w, h, px), cw, ch, characters: PixelFontCharacters);
    }
}
