// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// EBU R128 loudness meter: momentary and short-term bars against a target line, integrated loudness, loudness range
/// and sample peak, read from a <see cref="LoudnessAnalyzer"/> fed by the audio thread.
/// </summary>
/// <remarks>
/// Values are shown in LUFS (absolute) or in LU relative to <see cref="TargetLufs"/> (<see cref="ShowRelative"/>).
/// Double-click resets the measurement (thread-safe, through <see cref="LoudnessAnalyzer.RequestReset"/>).
/// Common targets: −23 LUFS (EBU R128 broadcast), −24 (ATSC A/85), −14 (most streaming services), −16 (podcasts).
/// </remarks>
public class LoudnessMeter : Control
{
    private RefreshTicker? _ticker;
    private double _m = double.NegativeInfinity, _s = double.NegativeInfinity, _i = double.NegativeInfinity, _lra, _peakDb = double.NegativeInfinity;
    private double _sinceIntegrated;

    /// <summary>Creates a loudness meter.</summary>
    public LoudnessMeter()
    {
        Focusable = false;
    }

    /// <summary>The analyzer (fed by the audio thread).</summary>
    [Browsable(false)]
    public LoudnessAnalyzer? Analyzer { get; set; }

    /// <summary>Target loudness in LUFS (drawn as a line; reference of relative mode).</summary>
    [Category("Value")]
    public float TargetLufs { get; set; } = -23f;

    /// <summary>Tolerance around the target (green zone), in LU.</summary>
    [Category("Value")]
    public float ToleranceLu { get; set; } = 1f;

    /// <summary>Bottom of the bars in LUFS.</summary>
    [Category("Data")]
    public float MinLufs { get; set; } = -60f;

    /// <summary>Top of the bars in LUFS.</summary>
    [Category("Data")]
    public float MaxLufs { get; set; } = 0f;

    /// <summary>Shows values in LU relative to the target instead of LUFS.</summary>
    [Category("Appearance")]
    public bool ShowRelative { get; set; }

    /// <summary>Shows the loudness range and the peak below the integrated value.</summary>
    [Category("Appearance")]
    public bool ShowDetails { get; set; } = true;

    /// <summary>Last momentary loudness read (LUFS).</summary>
    [Browsable(false)]
    public double Momentary => _m;

    /// <summary>Last short-term loudness read (LUFS).</summary>
    [Browsable(false)]
    public double ShortTerm => _s;

    /// <summary>Last integrated loudness read (LUFS).</summary>
    [Browsable(false)]
    public double Integrated => _i;

    /// <inheritdoc />
    protected override Size DefaultSize => new(170f, 170f);

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, Refresh));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    /// <summary>Reads the analyzer (every frame; integrated loudness and range 4 times per second).</summary>
    /// <param name="elapsedSeconds">Time since the previous call.</param>
    public void Refresh(double elapsedSeconds)
    {
        if (Analyzer is not { } a) return;
        double m = a.MomentaryLufs, s = a.ShortTermLufs, peak = a.SamplePeak > 0f ? 20.0 * Math.Log10(a.SamplePeak) : double.NegativeInfinity;
        _sinceIntegrated += elapsedSeconds;
        bool changed = !Same(m, _m) || !Same(s, _s) || !Same(peak, _peakDb);
        if (_sinceIntegrated >= 0.25)
        {
            _sinceIntegrated = 0.0;
            double i = a.IntegratedLufs, lra = a.LoudnessRange;
            changed |= !Same(i, _i) || !Same(lra, _lra);
            _i = i;
            _lra = lra;
        }
        _m = m;
        _s = s;
        _peakDb = peak;
        if (changed) InvalidateVisual();
    }

    private static bool Same(double a, double b) => a == b || Math.Abs(a - b) < 0.01;

    private string Format(double lufs)
    {
        if (double.IsNegativeInfinity(lufs) || double.IsNaN(lufs)) return "—";
        return ShowRelative ? (lufs - TargetLufs >= 0 ? "+" : "") + InvariantNumber.Format(lufs - TargetLufs, "0.0") : InvariantNumber.Format(lufs, "0.0");
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var inner = widget.Deflate(6f);
        float barW = MathF.Min(14f, inner.Width * 0.1f);
        var scaleArea = new Rect(inner.X + 20f, inner.Y + 4f, barW * 2f + 3f, inner.Height - 8f);
        float Y(double lufs) => GraphHelpers.ValueToY((float)lufs, MinLufs, MaxLufs, scaleArea);

        // Scale labels and target zone.
        var small = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Right, Ellipsis = false };
        for (float v = MathF.Ceiling(MinLufs / 6f) * 6f; v <= MaxLufs; v += 6f)
        {
            float y = Y(v);
            dc.DrawText(InvariantNumber.Format(ShowRelative ? v - TargetLufs : v, "0"), new Rect(inner.X - 2f, y - 6f, 20f, 11f), small, p.TextDim);
        }
        var zone = Rect.FromEdges(scaleArea.X - 2f, Y(TargetLufs + ToleranceLu), scaleArea.Right + 2f, Y(TargetLufs - ToleranceLu));
        dc.FillRect(zone, p.Success.WithAlpha(0.18f));
        var mBar = new Rect(scaleArea.X, scaleArea.Y, barW, scaleArea.Height);
        var sBar = new Rect(scaleArea.X + barW + 3f, scaleArea.Y, barW, scaleArea.Height);
        DrawBar(dc, mBar, _m, Y, p);
        DrawBar(dc, sBar, _s, Y, p);
        float ty = MathF.Round(Y(TargetLufs)) + 0.5f;
        dc.DrawLine(new Point(scaleArea.X - 4f, ty), new Point(scaleArea.Right + 4f, ty), p.Text, 1.5f);
        var tiny = Theme.Typography.Small with { Size = 8f, Align = HorizontalAlignment.Center, Ellipsis = false };
        dc.DrawText("M", new Rect(mBar.X, widget.Bottom - 12f, mBar.Width, 10f), tiny, p.TextDim);
        dc.DrawText("S", new Rect(sBar.X, widget.Bottom - 12f, sBar.Width, 10f), tiny, p.TextDim);

        // Numbers.
        var t = Strings;
        float x = scaleArea.Right + 12f;
        var text = new Rect(x, inner.Y, inner.Right - x, inner.Height);
        string unit = ShowRelative ? "LU" : "LUFS";
        var caption = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Left };
        var big = Theme.Typography.Title with { Size = MathF.Min(26f, text.Width * 0.24f), Align = HorizontalAlignment.Left };
        dc.DrawText(t.Integrated.ToUpperInvariant() + " · " + unit, new Rect(text.X, text.Y, text.Width, 12f), caption, p.TextDim);
        var iColor = double.IsNegativeInfinity(_i) ? p.TextDim : Math.Abs(_i - TargetLufs) <= ToleranceLu ? p.Success : _i > TargetLufs ? p.Warning : p.Text;
        dc.DrawText(Format(_i), new Rect(text.X, text.Y + 12f, text.Width, big.Size * 1.3f), big, iColor);
        float y0 = text.Y + 14f + big.Size * 1.35f;
        var line = Theme.Typography.Small with { Size = 10f, Align = HorizontalAlignment.Left };
        void Row(string label, string value)
        {
            dc.DrawText(label, new Rect(text.X, y0, text.Width * 0.58f, 13f), line, p.TextDim);
            dc.DrawText(value, new Rect(text.X + text.Width * 0.58f, y0, text.Width * 0.42f, 13f), line with { Align = HorizontalAlignment.Right }, p.Text);
            y0 += 14f;
        }
        Row(t.ShortTerm, Format(_s));
        Row(t.Momentary, Format(_m));
        if (ShowDetails)
        {
            Row(t.LoudnessRange, InvariantNumber.Format(_lra, "0.0") + " LU");
            Row(t.Peak, double.IsNegativeInfinity(_peakDb) ? "—" : InvariantNumber.Format(_peakDb, "0.0") + " dB");
        }
    }

    private void DrawBar(DrawingContext dc, Rect bar, double lufs, Func<double, float> y, Palette p)
    {
        dc.FillRect(bar, p.Background.Darken(0.25f));
        if (double.IsNegativeInfinity(lufs) || lufs <= MinLufs) return;
        float top = y(Math.Min(lufs, MaxLufs));
        var color = lufs > TargetLufs + ToleranceLu ? p.Warning : lufs >= TargetLufs - ToleranceLu ? p.Success : Theme.Palette.Accent;
        dc.FillRect(Rect.FromEdges(bar.X, top, bar.Right, bar.Bottom), color.WithAlpha(0.9f));
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || e.ClickCount != 2) return;
        Analyzer?.RequestReset();
        _i = _m = _s = _peakDb = double.NegativeInfinity;
        _lra = 0.0;
        InvalidateVisual();
        e.Handled = true;
    }
}
