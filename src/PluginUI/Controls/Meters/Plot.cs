// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>A curve of a <see cref="Plot"/>: a function sampled across the width, or explicit points.</summary>
public sealed class PlotSeries
{
    internal Point[] Buffer = [];
    internal Point[] FillBuffer = [];

    /// <summary>Creates a series from a function y = f(x).</summary>
    public PlotSeries(Func<double, double> function, Color? color = null)
    {
        Function = function;
        Color = color;
    }

    /// <summary>Creates a series from points (data coordinates).</summary>
    public PlotSeries(IReadOnlyList<Point> points, Color? color = null)
    {
        Points = points;
        Color = color;
    }

    /// <summary>Function (null when <see cref="Points"/> is used).</summary>
    [Browsable(false)]
    public Func<double, double>? Function { get; set; }

    /// <summary>Points in data coordinates (null when <see cref="Function"/> is used).</summary>
    [Category("Data")]
    public IReadOnlyList<Point>? Points { get; set; }

    /// <summary>Color (null = theme accent).</summary>
    [Category("Value")]
    public Color? Color { get; set; }

    /// <summary>Line thickness.</summary>
    [Category("Appearance")]
    public float Thickness { get; set; } = 1.6f;

    /// <summary>Fills between the curve and the <see cref="Plot.FillBaseline"/>.</summary>
    [Category("Appearance")]
    public bool Fill { get; set; }

    /// <summary>Hidden series are not drawn.</summary>
    [Category("Appearance")]
    public bool IsVisible { get; set; } = true;

    /// <summary>Optional legend name.</summary>
    [Category("Behavior")]
    public string? Name { get; set; }
}

/// <summary>
/// Function / data plot with axes, grid and labels: linear or logarithmic X, several series, filled curves.
/// Typical uses: EQ and filter response, compressor transfer curve, envelope or LFO shapes. iPlug2 <c>IVPlotControl</c>.
/// </summary>
/// <remarks>Call <see cref="UIElement.InvalidateVisual"/> after changing what a function depends on.</remarks>
public class Plot : Control
{
    /// <summary>Creates a plot.</summary>
    public Plot()
    {
        Focusable = false;
    }

    /// <summary>Curves, drawn in order.</summary>
    public List<PlotSeries> Series { get; } = new();

    /// <summary>Left of the X range.</summary>
    [Category("Data")]
    public double XMin { get; set; }

    /// <summary>Right of the X range.</summary>
    [Category("Data")]
    public double XMax { get; set; } = 1.0;

    /// <summary>Bottom of the Y range.</summary>
    [Category("Data")]
    public double YMin { get; set; } = -1.0;

    /// <summary>Top of the Y range.</summary>
    [Category("Data")]
    public double YMax { get; set; } = 1.0;

    /// <summary>Logarithmic X axis (frequency plots; XMin must be &gt; 0).</summary>
    [Category("Data")]
    public bool LogX { get; set; }

    /// <summary>Y value the fills go to (default: 0 clamped into the range).</summary>
    [Category("Appearance")]
    public double? FillBaseline { get; set; }

    /// <summary>Vertical grid lines in data units (null = automatic).</summary>
    [Category("Appearance")]
    public IReadOnlyList<double>? XGrid { get; set; }

    /// <summary>Horizontal grid lines in data units (null = automatic).</summary>
    [Category("Appearance")]
    public IReadOnlyList<double>? YGrid { get; set; }

    /// <summary>Draws the grid.</summary>
    [Category("Appearance")]
    public bool ShowGrid { get; set; } = true;

    /// <summary>Draws axis labels.</summary>
    [Category("Appearance")]
    public bool ShowAxisLabels { get; set; } = true;

    /// <summary>Formats X labels (default: compact numbers, "k" above 1000).</summary>
    [Browsable(false)]
    public Func<double, string>? XLabelFormatter { get; set; }

    /// <summary>Formats Y labels.</summary>
    [Browsable(false)]
    public Func<double, string>? YLabelFormatter { get; set; }

    /// <summary>Draws the background panel.</summary>
    [Category("Appearance")]
    public bool ShowBackground { get; set; } = true;

    /// <summary>Adds a function series (fluent).</summary>
    public Plot Add(Func<double, double> function, Color? color = null, bool fill = false)
    {
        Series.Add(new PlotSeries(function, color) { Fill = fill });
        InvalidateVisual();
        return this;
    }

    /// <inheritdoc />
    protected override Size DefaultSize => new(300f, 150f);

    /// <summary>Plot area (inside the labels) for the current bounds.</summary>
    public Rect PlotArea
    {
        get
        {
            LayoutParts(EffectiveStyle, out _, out var widget, out _);
            return AreaFor(widget);
        }
    }

    private Rect AreaFor(Rect widget) => ShowAxisLabels ? widget.Deflate(new Thickness(30f, 6f, 6f, 15f)) : widget.Deflate(4f);

    /// <summary>Data X → screen X.</summary>
    public float DataToX(double x, Rect area)
    {
        double t = LogX
            ? (Math.Log(Math.Max(1e-12, x)) - Math.Log(Math.Max(1e-12, XMin))) / (Math.Log(Math.Max(1e-12, XMax)) - Math.Log(Math.Max(1e-12, XMin)))
            : (x - XMin) / (XMax - XMin);
        return (float)(area.X + t * area.Width);
    }

    /// <summary>Screen X → data X.</summary>
    public double XToData(float x, Rect area)
    {
        double t = (x - area.X) / Math.Max(1e-6, area.Width);
        return LogX ? Math.Exp(Math.Log(Math.Max(1e-12, XMin)) + t * (Math.Log(Math.Max(1e-12, XMax)) - Math.Log(Math.Max(1e-12, XMin)))) : XMin + t * (XMax - XMin);
    }

    /// <summary>Data Y → screen Y.</summary>
    public float DataToY(double y, Rect area) => (float)(area.Bottom - (y - YMin) / (YMax - YMin) * area.Height);

    /// <summary>Screen Y → data Y.</summary>
    public double YToData(float y, Rect area) => YMin + (area.Bottom - y) / Math.Max(1e-6, area.Height) * (YMax - YMin);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = MathF.Min(Theme.Metrics.CornerRadius, widget.Height * 0.2f);
        if (ShowBackground) dc.FillRoundedRect(widget, radius, p.Background.Darken(0.3f));
        var area = AreaFor(widget);
        if (area.IsEmpty) return;
        if (ShowGrid) RenderGrid(dc, area, p);

        double baseline = Math.Clamp(FillBaseline ?? 0.0, Math.Min(YMin, YMax), Math.Max(YMin, YMax));
        float baseY = DataToY(baseline, area);
        using (dc.Clip(area.Inflate(1f)))
        {
            foreach (var series in Series)
            {
                if (!series.IsVisible) continue;
                int n = Sample(series, area);
                if (n < 2) continue;
                var color = series.Color ?? style.Accent;
                if (series.Fill)
                {
                    if (series.FillBuffer.Length < n + 2) series.FillBuffer = new Point[n + 2];
                    series.FillBuffer[0] = new Point(series.Buffer[0].X, baseY);
                    Array.Copy(series.Buffer, 0, series.FillBuffer, 1, n);
                    series.FillBuffer[n + 1] = new Point(series.Buffer[n - 1].X, baseY);
                    dc.FillPolygon(new ReadOnlySpan<Point>(series.FillBuffer, 0, n + 2), color.WithAlpha(0.2f));
                }
                dc.DrawPolyline(new ReadOnlySpan<Point>(series.Buffer, 0, n), color, series.Thickness, StrokeStyle.Round);
            }
        }
        if (ShowBackground && style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame, style.FrameThickness);
    }

    private int Sample(PlotSeries series, Rect area)
    {
        if (series.Function is { } f)
        {
            int n = Math.Max(2, (int)MathF.Ceiling(area.Width) + 1);
            if (series.Buffer.Length < n) series.Buffer = new Point[n];
            for (int i = 0; i < n; i++)
            {
                float x = area.X + i * area.Width / (n - 1);
                double y = f(XToData(x, area));
                if (!double.IsFinite(y)) y = y > 0 ? YMax * 10 : YMin * 10;
                float sy = Math.Clamp(DataToY(y, area), area.Y - area.Height, area.Bottom + area.Height);
                series.Buffer[i] = new Point(x, sy);
            }
            return n;
        }
        if (series.Points is { } pts)
        {
            if (series.Buffer.Length < pts.Count) series.Buffer = new Point[pts.Count];
            for (int i = 0; i < pts.Count; i++) series.Buffer[i] = new Point(DataToX(pts[i].X, area), DataToY(pts[i].Y, area));
            return pts.Count;
        }
        return 0;
    }

    private void RenderGrid(DrawingContext dc, Rect area, Palette p)
    {
        var grid = p.Border.WithAlpha(0.55f);
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        foreach (double x in XGrid ?? AutoXGrid())
        {
            float sx = MathF.Round(DataToX(x, area)) + 0.5f;
            if (sx < area.X - 0.5f || sx > area.Right + 0.5f) continue;
            dc.DrawLine(new Point(sx, area.Y), new Point(sx, area.Bottom), grid, 1f);
            if (ShowAxisLabels) dc.DrawText(FormatX(x), new Rect(sx - 18f, area.Bottom + 1f, 36f, 13f), label, p.TextDim);
        }
        foreach (double y in YGrid ?? AutoYGrid())
        {
            float sy = MathF.Round(DataToY(y, area)) + 0.5f;
            if (sy < area.Y - 0.5f || sy > area.Bottom + 0.5f) continue;
            dc.DrawLine(new Point(area.X, sy), new Point(area.Right, sy), Math.Abs(y) < 1e-9 ? p.Border.Lighten(0.1f) : grid, 1f);
            if (ShowAxisLabels) dc.DrawText(FormatY(y), new Rect(area.X - 30f, sy - 6f, 27f, 12f), label with { Align = HorizontalAlignment.Right }, p.TextDim);
        }
        dc.DrawRect(area, grid, 1f);
    }

    private string FormatX(double x)
    {
        if (XLabelFormatter is not null) return XLabelFormatter(x);
        return Math.Abs(x) >= 1000 ? (x / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k" : x.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private string FormatY(double y) => YLabelFormatter?.Invoke(y) ?? y.ToString("0.##", CultureInfo.InvariantCulture);

    private IEnumerable<double> AutoXGrid()
    {
        if (LogX)
        {
            for (double decade = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1e-12, XMin)))); decade <= XMax; decade *= 10)
            {
                foreach (double m in (double[])[1, 2, 5])
                {
                    double v = decade * m;
                    if (v >= XMin * 0.999 && v <= XMax * 1.001) yield return v;
                }
            }
            yield break;
        }
        foreach (double v in NiceTicks(XMin, XMax, 6)) yield return v;
    }

    private IEnumerable<double> AutoYGrid() => NiceTicks(YMin, YMax, 5);

    /// <summary>"Nice" tick values (1, 2, 5 × 10ⁿ) covering a range.</summary>
    public static IEnumerable<double> NiceTicks(double min, double max, int approximateCount)
    {
        if (max < min) (min, max) = (max, min);
        double range = max - min;
        if (range <= 0 || !double.IsFinite(range)) yield break;
        double raw = range / Math.Max(1, approximateCount);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step = raw / magnitude < 1.5 ? magnitude : raw / magnitude < 3.5 ? 2 * magnitude : raw / magnitude < 7.5 ? 5 * magnitude : 10 * magnitude;
        for (double v = Math.Ceiling(min / step) * step; v <= max + step * 1e-6; v += step)
        {
            yield return Math.Abs(v) < step * 1e-9 ? 0.0 : v;
        }
    }
}
