// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Automation;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// A split frequency of a <see cref="CrossoverEditor"/>, stored here or read from a bound parameter (plain value in Hz).
/// </summary>
public sealed class CrossoverSplit
{
    private double _frequency;

    /// <summary>Creates a split.</summary>
    /// <param name="frequency">Frequency in Hz.</param>
    public CrossoverSplit(double frequency = 1000.0) => _frequency = frequency;

    /// <summary>Frequency in Hz.</summary>
    public double Frequency
    {
        get => FrequencyParameter is { } p ? p.ToPlain(p.NormalizedValue) : _frequency;
        set { if (FrequencyParameter is { } p) p.PerformEdit(p.ToNormalized(value)); else _frequency = value; }
    }

    /// <summary>Frequency parameter (plain value in Hz), or <c>null</c>.</summary>
    public IParameter? FrequencyParameter { get; set; }
}

/// <summary>
/// A band of a <see cref="CrossoverEditor"/>: output gain, solo and mute, stored here or read from bound parameters.
/// </summary>
/// <remarks>
/// <see cref="GainParameter"/> holds a plain value in dB; <see cref="SoloParameter"/> and <see cref="MuteParameter"/> are
/// on above 0.5.
/// </remarks>
public sealed class CrossoverBand
{
    private double _gain;
    private bool _solo, _mute;

    /// <summary>Creates a band.</summary>
    /// <param name="name">Name shown in the band (null = "Band n").</param>
    /// <param name="gainDb">Output gain in dB.</param>
    public CrossoverBand(string? name = null, double gainDb = 0.0)
    {
        Name = name;
        _gain = gainDb;
    }

    /// <summary>Name shown in the band (null = "Band n" in the language of the host).</summary>
    public string? Name { get; set; }

    /// <summary>Color of the band (null = automatic, from its index).</summary>
    public Color? Color { get; set; }

    /// <summary>Output gain in dB.</summary>
    public double GainDb
    {
        get => GainParameter is { } p ? p.ToPlain(p.NormalizedValue) : _gain;
        set { if (GainParameter is { } p) p.PerformEdit(p.ToNormalized(value)); else _gain = value; }
    }

    /// <summary><c>true</c> when the band is soloed.</summary>
    public bool IsSolo
    {
        get => SoloParameter is { } p ? p.NormalizedValue >= 0.5 : _solo;
        set { if (SoloParameter is { } p) Edit(p, value ? 1.0 : 0.0); else _solo = value; }
    }

    /// <summary><c>true</c> when the band is muted.</summary>
    public bool IsMuted
    {
        get => MuteParameter is { } p ? p.NormalizedValue >= 0.5 : _mute;
        set { if (MuteParameter is { } p) Edit(p, value ? 1.0 : 0.0); else _mute = value; }
    }

    /// <summary>Gain parameter (plain value in dB), or <c>null</c>.</summary>
    public IParameter? GainParameter { get; set; }

    /// <summary>Solo parameter, or <c>null</c>.</summary>
    public IParameter? SoloParameter { get; set; }

    /// <summary>Mute parameter, or <c>null</c>.</summary>
    public IParameter? MuteParameter { get; set; }

    private static void Edit(IParameter p, double normalized)
    {
        p.BeginEdit();
        p.PerformEdit(normalized);
        p.EndEdit();
    }
}

/// <summary>
/// Crossover editor of a multiband processor: the bands side by side on a logarithmic frequency axis, draggable split
/// frequencies, the output gain, solo and mute of each band, the Linkwitz-Riley response of each band and an optional
/// live spectrum behind.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Drag a split line (or its handle at the top) to move the frequency (Shift = fine); splits keep
/// <see cref="MinSpacingOctaves"/> between them. Drag a band's gain line up or down; double-click it for 0 dB. Click
/// the S and M chips of a band to solo or mute it.</para>
/// <para><b>Keyboard.</b> Page Up / Page Down (or Ctrl+Left / Ctrl+Right) select the next band or split, Home / End
/// the first or last; Left / Right move the selected split (Shift = fine), Up / Down change the gain of the selected
/// band, S and M toggle its solo and mute, Delete or Backspace resets its gain.</para>
/// <para><b>Parameters.</b> Bind the splits and bands to the plug-in parameters; every change is a complete edit
/// gesture and the display follows host automation every frame. Set <see cref="BandCount"/> (or fill
/// <see cref="Splits"/> and <see cref="Bands"/>) to choose the number of bands: there is always one more band than
/// splits.</para>
/// </remarks>
/// <example>
/// <code>
/// var crossover = new CrossoverEditor { BandCount = 4, SlopeDbPerOctave = 24, SpectrumSource = processor.InputScope };
/// for (int i = 0; i &lt; 3; i++) crossover.Splits[i].FrequencyParameter = Parameters[model.Split[i]];
/// for (int i = 0; i &lt; 4; i++)
/// {
///     crossover.Bands[i].GainParameter = Parameters[model.BandGain[i]];
///     crossover.Bands[i].SoloParameter = Parameters[model.BandSolo[i]];
///     crossover.Bands[i].MuteParameter = Parameters[model.BandMute[i]];
/// }
/// </code>
/// </example>
public class CrossoverEditor : Control
{
    private static readonly Color[] AutoColors =
    [
        Color.FromRgb(0x42A5F5), Color.FromRgb(0x26C6DA), Color.FromRgb(0x9CCC65), Color.FromRgb(0xFFCA28),
        Color.FromRgb(0xFF7043), Color.FromRgb(0xEC407A), Color.FromRgb(0xAB47BC), Color.FromRgb(0x8D6E63),
    ];

    private const float HandleHeight = 16f, ChipSize = 15f, LineTolerance = 5f, LabelHeight = 14f;

    // Selection: 2k = band k, 2k + 1 = split k; -1 = nothing.
    private int _selected = -1;
    private int _hover = -1;
    private int _dragSplit = -1, _dragBand = -1;
    private float _dragOffset;
    private readonly SpectrumUnderlay _underlay = new();
    private RefreshTicker? _ticker;
    private double _version;

    /// <summary>Creates a crossover editor with three bands (splits at 200 Hz and 2 kHz).</summary>
    public CrossoverEditor()
    {
        Cursor = StandardCursor.Hand;
        Focusable = true;
        BandCount = 3;
    }

    /// <summary>Split frequencies, from low to high (one fewer than <see cref="Bands"/>).</summary>
    [Category("Data")]
    public IList<CrossoverSplit> Splits { get; } = new List<CrossoverSplit>();

    /// <summary>Bands, from low to high (one more than <see cref="Splits"/>).</summary>
    [Category("Data")]
    public IList<CrossoverBand> Bands { get; } = new List<CrossoverBand>();

    /// <summary>
    /// Number of bands, 2 to 8. Setting it adds or removes bands and splits; new splits are spread evenly (on the
    /// logarithmic axis) between the existing ones.
    /// </summary>
    [Category("Data")]
    public int BandCount
    {
        get => Bands.Count;
        set
        {
            value = Math.Clamp(value, 2, 8);
            if (value == Bands.Count && Splits.Count == value - 1) return;
            while (Bands.Count > value) Bands.RemoveAt(Bands.Count - 1);
            while (Bands.Count < value) Bands.Add(new CrossoverBand());
            if (Splits.Count != value - 1)
            {
                Splits.Clear();
                double ratio = Math.Pow(MaxFrequency / MinFrequency, 1.0 / value);
                for (int i = 1; i < value; i++) Splits.Add(new CrossoverSplit(Math.Round(MinFrequency * Math.Pow(ratio, i))));
            }
            _selected = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Slope of the Linkwitz-Riley filters drawn, in dB per octave (12, 24, 36, 48…).</summary>
    [Category("Data")]
    public int SlopeDbPerOctave { get; set; } = 24;

    /// <summary>Sample rate of <see cref="SpectrumSource"/>, in Hz.</summary>
    [Category("Data")]
    public float SampleRate { get; set; } = 48000f;

    /// <summary>Lowest displayed frequency in Hz.</summary>
    [Category("Data")]
    public float MinFrequency { get; set; } = 20f;

    /// <summary>Highest displayed frequency in Hz.</summary>
    [Category("Data")]
    public float MaxFrequency { get; set; } = 20000f;

    /// <summary>Gain range of the bands, above and below 0 dB.</summary>
    [Category("Data")]
    public float GainRangeDb { get; set; } = 24f;

    /// <summary>Smallest distance between two splits, in octaves.</summary>
    [Category("Behavior")]
    public float MinSpacingOctaves { get; set; } = 0.333f;

    /// <summary>Shows the gain line of each band (off for a crossover without band gains).</summary>
    [Category("Behavior")]
    public bool ShowGains { get; set; } = true;

    /// <summary>Shows the solo and mute chips of each band.</summary>
    [Category("Behavior")]
    public bool ShowSoloMute { get; set; } = true;

    /// <summary>Draws the response of each band.</summary>
    [Category("Appearance")]
    public bool ShowBandCurves { get; set; } = true;

    /// <summary>Audio to analyze and draw behind the bands, or <c>null</c>.</summary>
    [Browsable(false)]
    public AudioRingBuffer? SpectrumSource { get; set; }

    /// <summary>Selected band (−1 when a split or nothing is selected).</summary>
    [Browsable(false)]
    public int SelectedBand => _selected >= 0 && _selected % 2 == 0 ? _selected / 2 : -1;

    /// <summary>Selected split (−1 when a band or nothing is selected).</summary>
    [Browsable(false)]
    public int SelectedSplit => _selected >= 0 && _selected % 2 == 1 ? _selected / 2 : -1;

    /// <summary>Raised after the user moved a split (its index).</summary>
    public event Action<CrossoverEditor, int>? SplitChanged;

    /// <summary>Raised after the user changed a band's gain, solo or mute (its index).</summary>
    public event Action<CrossoverEditor, int>? BandChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(480f, 200f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new CrossoverEditorAutomationPeer(this);

    // ---------------------------------------------------------------- response

    /// <summary>
    /// Magnitude in dB of band <paramref name="band"/> at <paramref name="frequency"/>: the Linkwitz-Riley high-pass of
    /// the split below and low-pass of the split above (<see cref="SlopeDbPerOctave"/>), plus the band gain. Mute and
    /// solo are not applied.
    /// </summary>
    public double BandResponseDb(int band, double frequency)
    {
        if (band < 0 || band >= Bands.Count) throw new ArgumentOutOfRangeException(nameof(band));
        // A Linkwitz-Riley filter is a Butterworth filter squared: |H| = 1 / (1 + (f / fc)^(2m)), m = slope / 12.
        double m2 = 2.0 * Math.Max(1, SlopeDbPerOctave / 12);
        double magnitude = 1.0;
        if (band > 0 && band - 1 < Splits.Count)
        {
            double r = Math.Pow(frequency / Splits[band - 1].Frequency, m2);
            magnitude *= r / (1.0 + r);
        }
        if (band < Splits.Count)
        {
            magnitude *= 1.0 / (1.0 + Math.Pow(frequency / Splits[band].Frequency, m2));
        }
        return 20.0 * Math.Log10(Math.Max(magnitude, 1e-12)) + Bands[band].GainDb;
    }

    /// <summary><c>true</c> when band <paramref name="band"/> is heard: not muted, and soloed when any band is.</summary>
    public bool IsAudible(int band)
    {
        var b = Bands[band];
        if (b.IsMuted) return false;
        foreach (var other in Bands) if (other.IsSolo) return b.IsSolo;
        return true;
    }

    private Color ColorOf(int band) => Bands[band].Color ?? AutoColors[band % AutoColors.Length];

    private string NameOf(int band) => Bands[band].Name is { Length: > 0 } name ? name : string.Format(CultureInfo.InvariantCulture, Strings.BandFormat, band + 1);

    // ---------------------------------------------------------------- geometry

    private Rect GraphArea(Rect widget) => widget.Deflate(new Thickness(2f, 2f + HandleHeight, 2f, LabelHeight));

    private float SplitX(int split, Rect area) => GraphHelpers.LogToX((float)Splits[split].Frequency, MinFrequency, MaxFrequency, area);

    private (float Left, float Right) BandSpan(int band, Rect area)
        => (band == 0 ? area.X : SplitX(band - 1, area), band >= Splits.Count ? area.Right : SplitX(band, area));

    private float GainY(int band, Rect area) => GraphHelpers.ValueToY((float)Bands[band].GainDb, -GainRangeDb, GainRangeDb, area);

    private Rect ChipRect(int band, Rect area, bool mute)
    {
        var (left, right) = BandSpan(band, area);
        float x = (left + right) * 0.5f + (mute ? 1f : -ChipSize - 1f);
        return new Rect(x, area.Bottom - ChipSize - 4f, ChipSize, ChipSize);
    }

    private enum HitKind { None, Split, Gain, Solo, Mute, Band }

    private (HitKind Kind, int Index) HitItem(Point p)
    {
        var area = GraphArea(Bounds);
        if (ShowSoloMute)
        {
            for (int b = 0; b < Bands.Count; b++)
            {
                if (ChipRect(b, area, false).Contains(p)) return (HitKind.Solo, b);
                if (ChipRect(b, area, true).Contains(p)) return (HitKind.Mute, b);
            }
        }
        int nearest = -1;
        float best = LineTolerance;
        for (int s = 0; s < Splits.Count; s++)
        {
            float d = MathF.Abs(SplitX(s, area) - p.X);
            if (d <= best)
            {
                best = d;
                nearest = s;
            }
        }
        if (nearest >= 0 && p.Y >= Bounds.Y && p.Y <= area.Bottom) return (HitKind.Split, nearest);
        if (p.X < area.X || p.X > area.Right || p.Y < area.Y || p.Y > area.Bottom) return (HitKind.None, -1);
        int band = BandAt(p.X, area);
        if (ShowGains && MathF.Abs(GainY(band, area) - p.Y) <= LineTolerance) return (HitKind.Gain, band);
        return (HitKind.Band, band);
    }

    private int BandAt(float x, Rect area)
    {
        for (int s = 0; s < Splits.Count; s++) if (x < SplitX(s, area)) return s;
        return Splits.Count;
    }

    // ---------------------------------------------------------------- live updates

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, Refresh));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        EndDrag();
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private void Refresh(double elapsed)
    {
        // Repaint when a split or a band changed from outside (automation, preset) or new audio arrived.
        double version = Bands.Count * 1000.0 + Splits.Count;
        foreach (var s in Splits) version = version * 31.0 + s.Frequency;
        foreach (var b in Bands) version = version * 31.0 + b.GainDb * 7.0 + (b.IsSolo ? 3.0 : 0.0) + (b.IsMuted ? 5.0 : 0.0);
        if (SpectrumSource is { } source) version += source.TotalWritten;
        if (version != _version)
        {
            _version = version;
            InvalidateVisual();
            NotifyAutomationStateChanged();
        }
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var area = GraphArea(widget);
        GraphHelpers.DrawFrequencyGrid(dc, Theme, area, MinFrequency, MaxFrequency, true, widget.Bottom);
        GraphHelpers.DrawLevelGrid(dc, Theme, area, -GainRangeDb, GainRangeDb, GainRangeDb > 12f ? 12f : 6f, true);
        dc.PushClip(area);
        if (SpectrumSource is { } source) _underlay.Render(dc, area, source, SampleRate, MinFrequency, MaxFrequency, p.TextDim.WithAlpha(0.12f));

        var small = Theme.Typography.Small with { Size = 10f, Ellipsis = true };
        var chipText = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        int n = Math.Max(32, (int)area.Width / 2);
        var points = new Point[n + 1];
        float floor = GraphHelpers.ValueToY(-GainRangeDb, -GainRangeDb, GainRangeDb, area);
        for (int b = 0; b < Bands.Count; b++)
        {
            var color = ColorOf(b);
            bool audible = IsAudible(b);
            bool active = _selected == 2 * b || _hover == 2 * b || _dragBand == b;
            var (left, right) = BandSpan(b, area);
            dc.FillRect(new Rect(left, area.Y, right - left, area.Height), color.WithAlpha(active ? 0.10f : 0.05f));

            if (ShowBandCurves)
            {
                for (int i = 0; i <= n; i++)
                {
                    float x = area.X + area.Width * i / n;
                    double db = BandResponseDb(b, GraphHelpers.XToLog(x, MinFrequency, MaxFrequency, area));
                    points[i] = new Point(x, Math.Min(floor, GraphHelpers.ValueToY((float)db, -GainRangeDb, GainRangeDb, area)));
                }
                var fill = new Point[n + 3];
                points.CopyTo(fill, 0);
                fill[n + 1] = new Point(area.Right, floor);
                fill[n + 2] = new Point(area.X, floor);
                dc.FillPolygon(fill, color.WithAlpha(audible ? (active ? 0.22f : 0.12f) : 0.04f));
                DrawAboveFloor(dc, points, floor, color.WithAlpha(audible ? 0.85f : 0.3f), active ? 1.6f : 1.1f);
            }

            if (ShowGains)
            {
                float y = GainY(b, area);
                float inset = MathF.Min(10f, (right - left) * 0.15f);
                dc.DrawLine(new Point(left + inset, y), new Point(right - inset, y), audible ? color : color.WithAlpha(0.4f), active ? 2.4f : 1.6f);
                dc.FillRoundedRect(new Rect((left + right) * 0.5f - 9f, y - 3f, 18f, 6f), 3f, audible ? color : color.WithAlpha(0.4f));
            }

            var nameRect = new Rect(left + 5f, area.Y + 3f, MathF.Max(0f, right - left - 10f), 12f);
            dc.DrawText(NameOf(b), nameRect, small, audible ? color : p.TextDim);
            if (ShowGains && Bands[b].GainDb != 0.0)
            {
                dc.DrawText(FormatDb(Bands[b].GainDb), new Rect(nameRect.X, nameRect.Y + 12f, nameRect.Width, 12f), small, p.TextDim);
            }

            if (ShowSoloMute && right - left >= 2f * ChipSize + 6f)
            {
                DrawChip(dc, ChipRect(b, area, false), "S", Bands[b].IsSolo, Color.FromRgb(0xFFCA28), chipText, p);
                DrawChip(dc, ChipRect(b, area, true), "M", Bands[b].IsMuted, Color.FromRgb(0xEF5350), chipText, p);
            }
        }
        dc.PopClip();

        // Split lines and their handles (over the top margin).
        for (int s = 0; s < Splits.Count; s++)
        {
            float x = SplitX(s, area);
            bool active = _selected == 2 * s + 1 || _hover == 2 * s + 1 || _dragSplit == s;
            var line = active ? style.Accent : p.Text.WithAlpha(0.55f);
            dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), line, active ? 2f : 1.2f);
            string label = GraphHelpers.FormatHz((float)Splits[s].Frequency) + "Hz";
            float w = MathF.Max(34f, label.Length * 6.2f);
            var handle = new Rect(Math.Clamp(x - w * 0.5f, widget.X + 2f, widget.Right - 2f - w), widget.Y + 2f, w, HandleHeight - 2f);
            dc.FillRoundedRect(handle, 4f, active ? style.Accent : p.Surface);
            dc.DrawRoundedRect(handle, 4f, line, 1f);
            dc.DrawText(label, handle, chipText, active ? style.Accent.Contrasting : p.Text);
        }

    }

    /// <summary>Draws the curve where it is above the bottom of the scale (not along the bottom edge).</summary>
    private static void DrawAboveFloor(DrawingContext dc, Point[] points, float floor, Color color, float width)
    {
        int start = -1;
        for (int i = 0; i <= points.Length; i++)
        {
            bool visible = i < points.Length && points[i].Y < floor - 0.5f;
            if (visible && start < 0) start = Math.Max(0, i - 1);
            if (!visible && start >= 0)
            {
                int end = Math.Min(points.Length - 1, i);
                if (end > start) dc.DrawPolyline(points[start..(end + 1)], color, width, StrokeStyle.Round);
                start = -1;
            }
        }
    }

    private static void DrawChip(DrawingContext dc, Rect r, string text, bool on, Color onColor, TextStyle style, Palette p)
    {
        dc.FillRoundedRect(r, 3f, on ? onColor : p.Background.WithAlpha(0.75f));
        dc.DrawRoundedRect(r, 3f, on ? onColor : p.TextDim.WithAlpha(0.6f), 1f);
        dc.DrawText(text, r, style, on ? onColor.Contrasting : p.TextDim);
    }

    private static string FormatDb(double db) => (db > 0 ? "+" : "") + InvariantNumber.Format(db, "0.0") + " dB";

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var area = GraphArea(Bounds);
        if (_dragSplit >= 0)
        {
            float target = e.Position.X - _dragOffset;
            if (e.Shift)
            {
                float current = SplitX(_dragSplit, area);
                target = current + (target - current) * 0.15f;
            }
            MoveSplit(_dragSplit, GraphHelpers.XToLog(target, MinFrequency, MaxFrequency, area));
            return;
        }
        if (_dragBand >= 0)
        {
            float target = e.Position.Y - _dragOffset;
            if (e.Shift)
            {
                float current = GainY(_dragBand, area);
                target = current + (target - current) * 0.15f;
            }
            SetGain(_dragBand, GraphHelpers.YToValue(target, -GainRangeDb, GainRangeDb, area));
            return;
        }
        var (kind, index) = HitItem(e.Position);
        int hover = kind switch
        {
            HitKind.Split => 2 * index + 1,
            HitKind.None => -1,
            _ => 2 * index,
        };
        Cursor = kind switch
        {
            HitKind.Split => StandardCursor.SizeWE,
            HitKind.Gain => StandardCursor.SizeNS,
            _ => StandardCursor.Hand,
        };
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (_hover >= 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left) return;
        var (kind, index) = HitItem(e.Position);
        var area = GraphArea(Bounds);
        switch (kind)
        {
            case HitKind.Solo:
                Select(2 * index);
                ToggleSolo(index);
                break;
            case HitKind.Mute:
                Select(2 * index);
                ToggleMute(index);
                break;
            case HitKind.Split:
                Select(2 * index + 1);
                _dragSplit = index;
                _dragOffset = e.Position.X - SplitX(index, area);
                Splits[index].FrequencyParameter?.BeginEdit();
                break;
            case HitKind.Gain when e.ClickCount == 2:
                Select(2 * index);
                ResetGain(index);
                break;
            case HitKind.Gain:
                Select(2 * index);
                _dragBand = index;
                _dragOffset = e.Position.Y - GainY(index, area);
                Bands[index].GainParameter?.BeginEdit();
                break;
            case HitKind.Band:
                Select(2 * index);
                break;
            default:
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left) EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragSplit >= 0 && _dragSplit < Splits.Count) Splits[_dragSplit].FrequencyParameter?.EndEdit();
        if (_dragBand >= 0 && _dragBand < Bands.Count) Bands[_dragBand].GainParameter?.EndEdit();
        if (_dragSplit < 0 && _dragBand < 0) return;
        _dragSplit = _dragBand = -1;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        int items = Bands.Count + Splits.Count;
        bool fine = e.Shift;
        switch (e.Key)
        {
            case Key.PageUp:
            case Key.Left when e.Control:
                Select(_selected <= 0 ? 0 : _selected - 1);
                break;
            case Key.PageDown:
            case Key.Right when e.Control:
                Select(_selected < 0 ? 0 : Math.Min(items - 1, _selected + 1));
                break;
            case Key.Home:
                Select(0);
                break;
            case Key.End:
                Select(items - 1);
                break;
            case Key.Left or Key.Right when SelectedSplit >= 0:
            {
                int s = SelectedSplit;
                double step = Math.Pow(2.0, (e.Key == Key.Right ? 1.0 : -1.0) / (fine ? 48.0 : 12.0));
                Edit(Splits[s].FrequencyParameter, () => MoveSplit(s, Splits[s].Frequency * step));
                break;
            }
            case Key.Up or Key.Down when SelectedBand >= 0 && ShowGains:
            {
                int b = SelectedBand;
                double step = (e.Key == Key.Up ? 1.0 : -1.0) * (fine ? 0.1 : 1.0);
                Edit(Bands[b].GainParameter, () => SetGain(b, Bands[b].GainDb + step));
                break;
            }
            case Key.S when SelectedBand >= 0 && ShowSoloMute:
                ToggleSolo(SelectedBand);
                break;
            case Key.M when SelectedBand >= 0 && ShowSoloMute:
                ToggleMute(SelectedBand);
                break;
            case Key.Delete or Key.Back when SelectedBand >= 0 && ShowGains:
                ResetGain(SelectedBand);
                break;
            default:
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- edits

    private void Select(int item)
    {
        if (item == _selected) return;
        _selected = item;
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private static void Edit(IParameter? parameter, Action change)
    {
        parameter?.BeginEdit();
        change();
        parameter?.EndEdit();
    }

    /// <summary>Moves a split, kept between its neighbors (<see cref="MinSpacingOctaves"/>) and the displayed range.</summary>
    private void MoveSplit(int split, double frequency)
    {
        double spacing = Math.Pow(2.0, Math.Max(0f, MinSpacingOctaves));
        double low = split > 0 ? Splits[split - 1].Frequency * spacing : MinFrequency;
        double high = split < Splits.Count - 1 ? Splits[split + 1].Frequency / spacing : MaxFrequency;
        if (high < low) high = low;
        frequency = Math.Clamp(frequency, low, high);
        if (Math.Abs(frequency - Splits[split].Frequency) < 1e-9) return;
        Splits[split].Frequency = frequency;
        SplitChanged?.Invoke(this, split);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void SetGain(int band, double db)
    {
        db = Math.Clamp(db, -GainRangeDb, GainRangeDb);
        if (Math.Abs(db - Bands[band].GainDb) < 1e-9) return;
        Bands[band].GainDb = db;
        BandChanged?.Invoke(this, band);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void ResetGain(int band) => Edit(Bands[band].GainParameter, () => SetGain(band, 0.0));

    private void ToggleSolo(int band)
    {
        Bands[band].IsSolo = !Bands[band].IsSolo;
        BandChanged?.Invoke(this, band);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void ToggleMute(int band)
    {
        Bands[band].IsMuted = !Bands[band].IsMuted;
        BandChanged?.Invoke(this, band);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary>What a screen reader says about the selected band or split.</summary>
    internal string DescribeSelection()
    {
        var t = Strings;
        if (SelectedSplit is var s and >= 0)
            return string.Format(CultureInfo.InvariantCulture, t.CrossoverFormat, s + 1) + ": " + GraphHelpers.FormatHz((float)Splits[s].Frequency) + "Hz";
        if (SelectedBand is var b and >= 0)
        {
            var band = Bands[b];
            string text = NameOf(b) + ": " + FormatDb(band.GainDb);
            if (band.IsSolo) text += ", " + t.Solo;
            if (band.IsMuted) text += ", " + t.Mute;
            return text;
        }
        return string.Join(", ", Splits.Select(x => GraphHelpers.FormatHz((float)x.Frequency) + "Hz"));
    }
}

/// <summary>
/// Peer of a <see cref="CrossoverEditor"/>: a group whose value is the selected band (gain, solo, mute) or split
/// frequency, or the list of splits when nothing is selected.
/// </summary>
/// <param name="owner">The editor.</param>
public class CrossoverEditorAutomationPeer(CrossoverEditor owner) : ElementAutomationPeer<CrossoverEditor>(owner), IValuePattern
{
    private string? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationCrossoverEditor;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => true;

    /// <inheritdoc />
    public string Value => Owner.DescribeSelection();

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(string value) => throw new InvalidOperationException("The crossover editor is edited with its keys.");

    /// <inheritdoc />
    public override object? GetPattern(AutomationPattern pattern) => pattern == AutomationPattern.Value ? this : base.GetPattern(pattern);

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        string text = Value;
        if (text == _reported) return;
        RaisePropertyChanged(AutomationProperty.Value, _reported, text);
        _reported = text;
    }
}
