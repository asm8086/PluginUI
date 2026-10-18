// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// Seven-segment LED/LCD display drawn with vectors: digits, minus sign, a few letters and decimal points.
/// Shows <see cref="Text"/>, <see cref="Number"/> or the value of a bound <see cref="Parameter"/>.
/// Look-alike of VSTGUI <c>CSpecialDigit</c> without bitmaps.
/// </summary>
public class SegmentDisplay : Control
{
    // Segment bits: a=1 (top), b=2 (top right), c=4 (bottom right), d=8 (bottom), e=16 (bottom left), f=32 (top left), g=64 (middle).
    private static readonly Dictionary<char, byte> Glyphs = new()
    {
        ['0'] = 0x3F, ['1'] = 0x06, ['2'] = 0x5B, ['3'] = 0x4F, ['4'] = 0x66, ['5'] = 0x6D, ['6'] = 0x7D, ['7'] = 0x07,
        ['8'] = 0x7F, ['9'] = 0x6F, ['-'] = 0x40, ['_'] = 0x08, [' '] = 0x00, ['A'] = 0x77, ['B'] = 0x7C, ['C'] = 0x39,
        ['D'] = 0x5E, ['E'] = 0x79, ['F'] = 0x71, ['G'] = 0x3D, ['H'] = 0x76, ['I'] = 0x06, ['J'] = 0x1E, ['L'] = 0x38,
        ['N'] = 0x54, ['O'] = 0x5C, ['P'] = 0x73, ['R'] = 0x50, ['S'] = 0x6D, ['T'] = 0x78, ['U'] = 0x3E, ['Y'] = 0x6E,
        ['°'] = 0x63, ['='] = 0x48,
    };

    private readonly List<(byte Segments, bool Dot)> _cells = new();
    private string _text = string.Empty;
    private int _digitCount = 4;
    private IParameter? _parameter;
    private bool _subscribed;

    /// <summary>Creates a display.</summary>
    public SegmentDisplay()
    {
        Focusable = false;
        Rebuild();
    }

    /// <summary>Displayed text (digits, '-', '.', space and the letters A–Y that 7 segments can show).</summary>
    [Category("Text")]
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;
            _text = value;
            Rebuild();
            InvalidateVisual();
        }
    }

    /// <summary>Shows a number with <see cref="Decimals"/> decimals.</summary>
    [Category("Value")]
    public double Number
    {
        get => double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        set => Text = value.ToString("F" + Math.Clamp(Decimals, 0, 6), CultureInfo.InvariantCulture);
    }

    /// <summary>Decimals used by <see cref="Number"/>.</summary>
    [Category("Text")]
    public int Decimals { get; set; }

    /// <summary>Number of digit cells (text is right-aligned and truncated to the right-most cells).</summary>
    [Category("Appearance")]
    public int DigitCount { get => _digitCount; set { _digitCount = Math.Clamp(value, 1, 32); Rebuild(); InvalidateMeasure(); } }

    /// <summary>Lit segment color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? LitColor { get; set; }

    /// <summary>Unlit segment color (null = lit color at low alpha, the "ghost" segments of real displays).</summary>
    [Category("Appearance")]
    public Color? UnlitColor { get; set; }

    /// <summary>Background of the display window (null = theme background).</summary>
    [Category("Appearance")]
    public Color? DisplayBackground { get; set; }

    /// <summary>Italic slant (0 = upright, 0.1 is typical).</summary>
    [Category("Appearance")]
    public float Slant { get; set; } = 0.08f;

    /// <summary>Segment thickness relative to the digit height.</summary>
    [Category("Layout")]
    public float SegmentThickness { get; set; } = 0.11f;

    /// <summary>Draws a soft glow around lit segments.</summary>
    [Category("Appearance")]
    public bool Glow { get; set; } = true;

    /// <summary>Bound parameter: the display shows its value text (units excluded).</summary>
    [Category("Value")]
    public IParameter? Parameter
    {
        get => _parameter;
        set
        {
            if (ReferenceEquals(_parameter, value)) return;
            Unsubscribe();
            _parameter = value;
            if (Host is not null) Subscribe();
            if (value is not null) Text = value.ToDisplayString(value.NormalizedValue);
        }
    }

    /// <inheritdoc />
    protected override Size DefaultSize => new(_digitCount * 22f + 12f, 40f);

    private void Rebuild()
    {
        _cells.Clear();
        foreach (char raw in _text)
        {
            char c = char.ToUpperInvariant(raw);
            if ((c == '.' || c == ',') && _cells.Count > 0 && !_cells[^1].Dot)
            {
                _cells[^1] = (_cells[^1].Segments, true);
                continue;
            }
            if (c == '.' || c == ',')
            {
                _cells.Add((0, true));
                continue;
            }
            _cells.Add((Glyphs.TryGetValue(c, out var g) ? g : (byte)0x00, false));
        }
        if (_cells.Count > _digitCount) _cells.RemoveRange(0, _cells.Count - _digitCount);
        while (_cells.Count < _digitCount) _cells.Insert(0, (0, false));
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = MathF.Min(Theme.Metrics.CornerRadius, widget.Height * 0.25f);
        dc.FillRoundedRect(widget, radius, DisplayBackground ?? p.Background.Darken(0.25f));
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame, style.FrameThickness);

        var lit = LitColor ?? style.Accent;
        var unlit = UnlitColor ?? lit.WithAlpha(0.07f);
        var inner = widget.Deflate(new Thickness(MathF.Max(4f, widget.Height * 0.14f), widget.Height * 0.14f));
        float cellW = inner.Width / _digitCount;
        float h = inner.Height;
        float w = MathF.Min(cellW * 0.78f, h * 0.55f);
        float t = MathF.Max(1.2f, h * SegmentThickness);

        for (int i = 0; i < _cells.Count; i++)
        {
            var (segments, dot) = _cells[i];
            float x0 = inner.X + i * cellW + (cellW - w) * 0.5f - w * 0.1f;
            for (int s = 0; s < 7; s++)
            {
                bool on = (segments & (1 << s)) != 0;
                DrawSegment(dc, s, x0, inner.Y, w, h, t, inner.Center.Y, on ? lit : unlit, on && Glow ? lit : (Color?)null);
            }
            var dotCenter = Skew(new Point(x0 + w + t * 0.9f, inner.Bottom - t * 0.5f), inner.Center.Y);
            if (dot && Glow) dc.FillCircle(dotCenter, t * 1.1f, lit.WithAlpha(0.18f));
            dc.FillCircle(dotCenter, t * 0.55f, dot ? lit : unlit);
        }
    }

    private Point Skew(Point p, float centerY) => new(p.X + (centerY - p.Y) * Slant, p.Y);

    private void DrawSegment(DrawingContext dc, int segment, float x, float y, float w, float h, float t, float cy, Color color, Color? glow)
    {
        float gap = t * 0.18f, half = t * 0.5f, mid = y + h * 0.5f;
        Span<Point> pts = stackalloc Point[6];
        bool horizontal = segment is 0 or 3 or 6;
        if (horizontal)
        {
            float sy = segment == 0 ? y + half : segment == 3 ? y + h - half : mid;
            float a = x + gap + half * 0.4f, b = x + w - gap - half * 0.4f;
            pts[0] = new(a, sy); pts[1] = new(a + half, sy - half); pts[2] = new(b - half, sy - half);
            pts[3] = new(b, sy); pts[4] = new(b - half, sy + half); pts[5] = new(a + half, sy + half);
        }
        else
        {
            float sx = segment is 1 or 2 ? x + w - half : x + half;
            bool top = segment is 1 or 5;
            float a = top ? y + half + gap : mid + gap, b = top ? mid - gap : y + h - half - gap;
            pts[0] = new(sx, a); pts[1] = new(sx + half, a + half); pts[2] = new(sx + half, b - half);
            pts[3] = new(sx, b); pts[4] = new(sx - half, b - half); pts[5] = new(sx - half, a + half);
        }
        for (int i = 0; i < 6; i++) pts[i] = Skew(pts[i], cy);
        if (glow is { } g)
        {
            Span<Point> ring = stackalloc Point[7];
            pts.CopyTo(ring);
            ring[6] = pts[0];
            dc.DrawPolyline(ring, g.WithAlpha(0.16f), t * 0.9f, StrokeStyle.Round);
        }
        dc.FillPolygon(pts, color);
    }

    // ---------------------------------------------------------------- binding

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Subscribe();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        Unsubscribe();
        base.OnDetached();
    }

    private void Subscribe()
    {
        if (_subscribed || _parameter is null) return;
        _parameter.Changed += OnParameterChanged;
        _subscribed = true;
        OnParameterChanged(_parameter);
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _parameter is null) return;
        _parameter.Changed -= OnParameterChanged;
        _subscribed = false;
    }

    private void OnParameterChanged(IParameter parameter) => Text = parameter.ToDisplayString(parameter.NormalizedValue);
}
