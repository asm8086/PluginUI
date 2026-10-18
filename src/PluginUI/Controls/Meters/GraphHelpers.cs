// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI;

/// <summary>Axis mapping and grid drawing shared by the frequency / level graphs (EQ, spectrogram, compressor…).</summary>
internal static class GraphHelpers
{
    private static readonly float[] FrequencyLines = [20f, 50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f];

    public static float LogToX(float hz, float minHz, float maxHz, Rect area)
    {
        float lo = MathF.Log(MathF.Max(1f, minHz)), hi = MathF.Log(MathF.Max(minHz + 1f, maxHz));
        return area.X + (MathF.Log(MathF.Max(1f, hz)) - lo) / (hi - lo) * area.Width;
    }

    public static float XToLog(float x, float minHz, float maxHz, Rect area)
    {
        float lo = MathF.Log(MathF.Max(1f, minHz)), hi = MathF.Log(MathF.Max(minHz + 1f, maxHz));
        return MathF.Exp(lo + (x - area.X) / MathF.Max(1f, area.Width) * (hi - lo));
    }

    public static float LogToY(float hz, float minHz, float maxHz, Rect area)
    {
        float lo = MathF.Log(MathF.Max(1f, minHz)), hi = MathF.Log(MathF.Max(minHz + 1f, maxHz));
        return area.Bottom - (MathF.Log(MathF.Max(1f, hz)) - lo) / (hi - lo) * area.Height;
    }

    public static float ValueToY(float value, float min, float max, Rect area)
        => area.Bottom - Math.Clamp((value - min) / MathF.Max(1e-6f, max - min), 0f, 1f) * area.Height;

    public static float YToValue(float y, float min, float max, Rect area)
        => min + (area.Bottom - y) / MathF.Max(1f, area.Height) * (max - min);

    public static string FormatHz(float hz) => hz >= 1000f
        ? InvariantNumber.Format(hz / 1000f, hz >= 10000f ? "0" : "0.#") + "k"
        : InvariantNumber.Format(hz, "0");

    /// <summary>Vertical frequency lines (and labels below the area when <paramref name="labels"/> is set).</summary>
    public static void DrawFrequencyGrid(DrawingContext dc, Theme theme, Rect area, float minHz, float maxHz, bool labels, float labelBottom)
    {
        var p = theme.Palette;
        var grid = p.Border.WithAlpha(0.55f);
        var style = theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        foreach (float f in FrequencyLines)
        {
            if (f < minHz || f > maxHz) continue;
            float x = MathF.Round(LogToX(f, minHz, maxHz, area)) + 0.5f;
            dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), grid, 1f);
            if (!labels || f <= minHz * 1.01f || f >= maxHz * 0.99f) continue;
            dc.DrawText(FormatHz(f), new Rect(x - 16f, area.Bottom + 1f, 32f, MathF.Max(10f, labelBottom - area.Bottom - 1f)), style, p.TextDim);
        }
    }

    /// <summary>Horizontal level lines every <paramref name="step"/> (0 line emphasized), with labels inside the area.</summary>
    public static void DrawLevelGrid(DrawingContext dc, Theme theme, Rect area, float min, float max, float step, bool labels, string suffix = "")
    {
        var p = theme.Palette;
        var grid = p.Border.WithAlpha(0.55f);
        var style = theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Left, Ellipsis = false };
        if (step <= 0f) return;
        for (float v = MathF.Ceiling(min / step) * step; v <= max + 1e-3f; v += step)
        {
            float y = MathF.Round(ValueToY(v, min, max, area)) + 0.5f;
            dc.DrawLine(new Point(area.X, y), new Point(area.Right, y), MathF.Abs(v) < 1e-3f ? p.Border : grid, 1f);
            if (!labels || y - area.Y < 6f || area.Bottom - y < 6f) continue;
            string text = (v > 0f ? "+" : "") + InvariantNumber.Format(v, "0") + suffix;
            dc.DrawText(text, new Rect(area.X + 3f, y - 11f, 40f, 10f), style, p.TextDim.WithAlpha(0.8f));
        }
    }

    /// <summary>Dark rounded background used by the analysis displays.</summary>
    public static void DrawPanel(DrawingContext dc, Theme theme, Rect widget)
    {
        float radius = MathF.Min(theme.Metrics.CornerRadius, widget.Height * 0.2f);
        dc.FillRoundedRect(widget, radius, theme.Palette.Background.Darken(0.3f));
    }

    /// <summary>Invariant-culture string with the given format (shortcut).</summary>
    public static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
