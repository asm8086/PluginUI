// 2026/10/24 14:21:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Visual variants of push buttons.</summary>
public enum ButtonAppearance
{
    /// <summary>Raised box (shadow, fill, frame).</summary>
    Normal,
    /// <summary>Filled with the accent color (primary action).</summary>
    Accent,
    /// <summary>No background until hovered (toolbars).</summary>
    Flat,
    /// <summary>Frame only.</summary>
    Outline,
}

/// <summary>Icon drawn by a <see cref="ToggleButton"/> instead of (or left of) its caption.</summary>
public enum ButtonGlyph
{
    /// <summary>No icon.</summary>
    None,
    /// <summary>One period of a sine wave (show/hide a curve, oscillator, signal).</summary>
    Wave,
}

/// <summary>Small vector glyphs shared by buttons, menus and pickers.</summary>
public static class Glyphs
{
    /// <summary>Draws a chevron pointing down (or up when <paramref name="up"/>) centered at <paramref name="c"/>.</summary>
    public static void Chevron(DrawingContext dc, Point c, float size, Color color, bool up = false, float thickness = 1.5f)
    {
        float w = size * 0.5f, h = size * 0.28f * (up ? -1f : 1f);
        ReadOnlySpan<Point> pts = [new(c.X - w, c.Y - h), new(c.X, c.Y + h), new(c.X + w, c.Y - h)];
        dc.DrawPolyline(pts, color, thickness, StrokeStyle.Round);
    }

    /// <summary>Draws a chevron pointing left or right.</summary>
    public static void ChevronSide(DrawingContext dc, Point c, float size, Color color, bool right, float thickness = 1.5f)
    {
        float h = size * 0.5f, w = size * 0.28f * (right ? 1f : -1f);
        ReadOnlySpan<Point> pts = [new(c.X - w, c.Y - h), new(c.X + w, c.Y), new(c.X - w, c.Y + h)];
        dc.DrawPolyline(pts, color, thickness, StrokeStyle.Round);
    }

    /// <summary>Draws a check mark fitting a square of side <paramref name="size"/> centered at <paramref name="c"/>.</summary>
    public static void Check(DrawingContext dc, Point c, float size, Color color, float thickness = 1.8f)
    {
        float s = size * 0.5f;
        ReadOnlySpan<Point> pts = [new(c.X - s * 0.75f, c.Y + s * 0.02f), new(c.X - s * 0.2f, c.Y + s * 0.55f), new(c.X + s * 0.8f, c.Y - s * 0.6f)];
        dc.DrawPolyline(pts, color, thickness, StrokeStyle.Round);
    }

    /// <summary>Draws three horizontal bars (hamburger menu).</summary>
    public static void Hamburger(DrawingContext dc, Point c, float size, Color color, float thickness = 1.5f)
    {
        float w = size * 0.5f, g = size * 0.3f;
        for (int i = -1; i <= 1; i++) dc.DrawLine(new Point(c.X - w, c.Y + i * g), new Point(c.X + w, c.Y + i * g), color, thickness, StrokeStyle.Round);
    }

    /// <summary>Draws three dots (ellipsis / "more" menu).</summary>
    public static void Ellipsis(DrawingContext dc, Point c, float size, Color color)
    {
        float g = size * 0.32f, r = MathF.Max(1.1f, size * 0.09f);
        for (int i = -1; i <= 1; i++) dc.FillCircle(new Point(c.X + i * g, c.Y), r, color);
    }

    /// <summary>Draws a filled triangle pointing up or down.</summary>
    public static void Triangle(DrawingContext dc, Point c, float size, Color color, bool up)
    {
        float w = size * 0.5f, h = size * 0.35f * (up ? -1f : 1f);
        ReadOnlySpan<Point> pts = [new(c.X - w, c.Y - h), new(c.X + w, c.Y - h), new(c.X, c.Y + h)];
        dc.FillPolygon(pts, color);
    }

    /// <summary>Draws one period of a sine wave fitting a square of side <paramref name="size"/> centered at <paramref name="c"/>.</summary>
    public static void Wave(DrawingContext dc, Point c, float size, Color color, float thickness = 1.5f)
    {
        // Filled as a ribbon (offset along the normals) rather than stroked: no join artefacts at small sizes.
        const int Steps = 24;
        Span<Point> ribbon = stackalloc Point[2 * (Steps + 1)];
        float w = size * 0.5f, h = size * 0.3f, half = 0.5f * thickness;
        for (int i = 0; i <= Steps; i++)
        {
            float t = (float)i / Steps;
            float x = c.X - w + 2f * w * t, y = c.Y - h * MathF.Sin(2f * MathF.PI * t);
            // Tangent (2w, −2πh·cos 2πt), normalized; the normal is the tangent turned by 90°.
            float tx = 2f * w, ty = -2f * MathF.PI * h * MathF.Cos(2f * MathF.PI * t);
            float len = MathF.Sqrt(tx * tx + ty * ty);
            float nx = -ty / len * half, ny = tx / len * half;
            ribbon[i] = new Point(x + nx, y + ny);
            ribbon[2 * Steps + 1 - i] = new Point(x - nx, y - ny);
        }
        dc.FillPolygon(ribbon, color);
    }

    /// <summary>Draws <paramref name="glyph"/> fitting a square of side <paramref name="size"/> centered at <paramref name="c"/>.</summary>
    public static void Draw(DrawingContext dc, ButtonGlyph glyph, Point c, float size, Color color)
    {
        switch (glyph)
        {
            case ButtonGlyph.Wave: Wave(dc, c, size, color); break;
        }
    }

    /// <summary>Draws a glowing LED dot.</summary>
    public static void Led(DrawingContext dc, Point c, float radius, Color color, bool on, Color offColor)
    {
        if (on)
        {
            dc.FillCircle(c, radius * 2.2f, Paint.Radial(color.WithAlpha(0.35f), color.WithAlpha(0f)));
            dc.FillCircle(c, radius, color);
            dc.FillCircle(new Point(c.X - radius * 0.3f, c.Y - radius * 0.3f), radius * 0.35f, Color.White.WithAlpha(0.55f));
        }
        else
        {
            dc.FillCircle(c, radius, offColor);
            dc.DrawCircle(c, radius - 0.5f, Color.Black.WithAlpha(0.35f), 1f);
        }
    }

    /// <summary>Draws a checkerboard (behind translucent colors).</summary>
    public static void Checkerboard(DrawingContext dc, Rect r, float cell = 5f)
    {
        dc.FillRect(r, Color.FromRgb(0xCCCCCC));
        var dark = Color.FromRgb(0x999999);
        using var clip = dc.Clip(r);
        int cols = (int)MathF.Ceiling(r.Width / cell), rows = (int)MathF.Ceiling(r.Height / cell);
        for (int y = 0; y < rows; y++)
        {
            for (int x = (y & 1); x < cols; x += 2) dc.FillRect(new Rect(r.X + x * cell, r.Y + y * cell, cell, cell), dark);
        }
    }
}

/// <summary>
/// Auto-repeat helper for buttons held down: fires once after <see cref="Delay"/>, then every <see cref="Interval"/>.
/// </summary>
internal sealed class RepeatTicker(Action fire) : ITickable
{
    private double _elapsed;
    private bool _started;

    [Category("Behavior")]
    public double Delay { get; set; } = 0.4;

    [Category("Data")]
    public double Interval { get; set; } = 0.08;

    [Browsable(false)]
    public bool IsRunning { get; private set; }

    public void Start(UIHost? host)
    {
        _elapsed = 0;
        _started = false;
        IsRunning = true;
        host?.AddTicker(this);
    }

    public void Stop(UIHost? host)
    {
        IsRunning = false;
        host?.RemoveTicker(this);
    }

    public bool Tick(double elapsedSeconds)
    {
        if (!IsRunning) return false;
        _elapsed += elapsedSeconds;
        double threshold = _started ? Interval : Delay;
        while (_elapsed >= threshold && IsRunning)
        {
            _elapsed -= threshold;
            _started = true;
            threshold = Interval;
            fire();
        }
        return IsRunning;
    }
}

/// <summary>Helpers to obtain state labels of discrete values.</summary>
internal static class StateLabels
{
    /// <summary>Labels for each state of a value control: explicit items, list parameter items, or step texts.</summary>
    public static IReadOnlyList<string> For(ValueControl control, IReadOnlyList<string>? items)
    {
        if (items is { Count: > 0 }) return items;
        if (control.Parameter is LocalParameter { Items: { Count: > 0 } list }) return list;
        int steps = control.StepCount;
        if (steps <= 0) return [];
        var labels = new string[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double n = i / (double)steps;
            labels[i] = control.Parameter is { } p ? p.ToDisplayString(n) : control.FormatValue(n);
        }
        return labels;
    }
}

/// <summary>Size helpers for controls whose natural size must include the label and value areas.</summary>
internal static class ControlSizing
{
    /// <summary>Extra height taken by the label (top) and value (bottom) areas of <paramref name="control"/>.</summary>
    public static float LabelAndValueHeight(Control control)
    {
        var style = control.EffectiveStyle;
        float h = 0f;
        if (style.ShowLabel && !string.IsNullOrEmpty(control.Label)) h += style.LabelText.Size * 1.5f;
        if (style.ShowValue && control.ValueText is not null) h += style.ValueText.Size * 1.5f;
        return h;
    }
}
