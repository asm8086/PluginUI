// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>
/// One band of a <see cref="ParametricEqEditor"/>: shape, frequency, gain, Q and on/off state, either stored in the
/// band or read from bound parameters.
/// </summary>
/// <remarks>
/// When a parameter is set, the band reads and writes it instead of its own field: <see cref="FrequencyParameter"/>
/// (plain value in Hz), <see cref="GainParameter"/> (dB), <see cref="QParameter"/> (Q), <see cref="EnabledParameter"/>
/// (on above 0.5) and <see cref="TypeParameter"/> (list parameter whose step index is the <see cref="EqBandType"/>).
/// </remarks>
public sealed class EqBand
{
    private EqBandType _type;
    private double _frequency = 1000.0, _gain, _q = 0.7071;
    private bool _enabled = true;

    /// <summary>Creates a band.</summary>
    /// <param name="type">Shape.</param>
    /// <param name="frequency">Frequency in Hz.</param>
    /// <param name="gainDb">Gain in dB.</param>
    /// <param name="q">Quality factor.</param>
    public EqBand(EqBandType type = EqBandType.Bell, double frequency = 1000.0, double gainDb = 0.0, double q = 0.7071)
    {
        _type = type;
        _frequency = frequency;
        _gain = gainDb;
        _q = q;
    }

    /// <summary>Shape of the band.</summary>
    public EqBandType Type
    {
        get => TypeParameter is { } p ? (EqBandType)Math.Clamp((int)Math.Round(p.NormalizedValue * Math.Max(1, p.StepCount)), 0, 6) : _type;
        set { if (TypeParameter is { } p) Edit(p, p.StepCount <= 0 ? 0 : (int)value / (double)p.StepCount); else _type = value; }
    }

    /// <summary>Frequency in Hz.</summary>
    public double Frequency
    {
        get => FrequencyParameter is { } p ? p.ToPlain(p.NormalizedValue) : _frequency;
        set { if (FrequencyParameter is { } p) p.PerformEdit(p.ToNormalized(value)); else _frequency = value; }
    }

    /// <summary>Gain in dB (bells and shelves).</summary>
    public double GainDb
    {
        get => GainParameter is { } p ? p.ToPlain(p.NormalizedValue) : _gain;
        set { if (GainParameter is { } p) p.PerformEdit(p.ToNormalized(value)); else _gain = value; }
    }

    /// <summary>Quality factor.</summary>
    public double Q
    {
        get => QParameter is { } p ? p.ToPlain(p.NormalizedValue) : _q;
        set { if (QParameter is { } p) p.PerformEdit(p.ToNormalized(value)); else _q = Math.Max(0.05, value); }
    }

    /// <summary><c>false</c> when the band is bypassed.</summary>
    public bool IsEnabled
    {
        get => EnabledParameter is { } p ? p.NormalizedValue >= 0.5 : _enabled;
        set { if (EnabledParameter is { } p) Edit(p, value ? 1.0 : 0.0); else _enabled = value; }
    }

    /// <summary>Frequency parameter (plain value in Hz), or <c>null</c>.</summary>
    public IParameter? FrequencyParameter { get; set; }

    /// <summary>Gain parameter (plain value in dB), or <c>null</c>.</summary>
    public IParameter? GainParameter { get; set; }

    /// <summary>Q parameter (plain value = Q), or <c>null</c>.</summary>
    public IParameter? QParameter { get; set; }

    /// <summary>On/off parameter, or <c>null</c>.</summary>
    public IParameter? EnabledParameter { get; set; }

    /// <summary>Shape parameter (step index = <see cref="EqBandType"/> value), or <c>null</c>.</summary>
    public IParameter? TypeParameter { get; set; }

    /// <summary>Color of the node and of the band curve (null = automatic, from the band index).</summary>
    public Color? Color { get; set; }

    /// <summary><c>true</c> if the gain is meaningful for the current shape (bells and shelves).</summary>
    public bool HasGain => Type is EqBandType.Bell or EqBandType.LowShelf or EqBandType.HighShelf;

    /// <summary>Coefficients of the band at a sample rate (identity when bypassed).</summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>The biquad coefficients.</returns>
    public BiquadDesign.Coefficients Coefficients(double sampleRate)
        => IsEnabled ? BiquadDesign.Design(Type, Frequency, HasGain ? GainDb : 0.0, Q, sampleRate) : BiquadDesign.Coefficients.Identity;

    internal IEnumerable<IParameter> Parameters()
    {
        if (FrequencyParameter is { } f) yield return f;
        if (GainParameter is { } g) yield return g;
        if (QParameter is { } q) yield return q;
        if (EnabledParameter is { } e) yield return e;
        if (TypeParameter is { } t) yield return t;
    }

    private static void Edit(IParameter p, double normalized)
    {
        p.BeginEdit();
        p.PerformEdit(normalized);
        p.EndEdit();
    }
}

/// <summary>
/// Interactive parametric equalizer display (FabFilter-style): frequency response of all bands, draggable band nodes,
/// Q on the mouse wheel, per-band shape menu, and an optional live spectrum behind the curve.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Drag a node to change frequency (horizontal) and gain (vertical; Shift = fine), turn the wheel
/// over a node to change Q, double-click a node to bypass it, right-click it for its shape, bypass and delete
/// commands. With <see cref="AllowAddBands"/>, double-clicking an empty area adds a bell band there.</para>
/// <para><b>Accuracy.</b> The curve is the exact magnitude of the digital RBJ biquads at <see cref="SampleRate"/>
/// (<see cref="BiquadDesign"/>), so it matches a processor using the same formulas.</para>
/// <para><b>Parameters.</b> Bind each band to the plugin parameters through <see cref="EqBand"/>; drags send complete
/// edit gestures, frequency and gain in parallel. The display follows host automation every frame.</para>
/// </remarks>
/// <example>
/// <code>
/// var eq = new ParametricEqEditor { SampleRate = 48000 };
/// eq.Bands.Add(new EqBand(EqBandType.LowCut, 40, 0, 0.7071) { FrequencyParameter = Parameters[model.LowCut] });
/// eq.Bands.Add(new EqBand(EqBandType.Bell, 1000, 3, 1.2)
/// {
///     FrequencyParameter = Parameters[model.MidFreq], GainParameter = Parameters[model.MidGain], QParameter = Parameters[model.MidQ],
/// });
/// eq.SpectrumSource = processor.OutputScope; // optional analyzer underlay
/// </code>
/// </example>
public class ParametricEqEditor : Control
{
    private static readonly Color[] AutoColors =
    [
        Color.FromRgb(0xFF7043), Color.FromRgb(0xFFCA28), Color.FromRgb(0x9CCC65), Color.FromRgb(0x26C6DA),
        Color.FromRgb(0x5C6BC0), Color.FromRgb(0xAB47BC), Color.FromRgb(0xEC407A), Color.FromRgb(0x8D6E63),
    ];

    private int _hover = -1;
    private int _selected = -1;
    private int _dragging = -1;
    private Point _dragOffset;
    private Fft? _fft;
    private float[] _spectrumSamples = [];
    private float[] _spectrum = [];
    private RefreshTicker? _ticker;
    private double _version;

    /// <summary>Creates an equalizer editor.</summary>
    public ParametricEqEditor()
    {
        Cursor = StandardCursor.Hand;
    }

    /// <summary>The bands (add, remove or replace freely; the display updates on the next frame).</summary>
    [Category("Data")]
    public IList<EqBand> Bands { get; } = new List<EqBand>();

    /// <summary>Sample rate used to compute the digital response, in Hz.</summary>
    [Category("Data")]
    public float SampleRate { get; set; } = 48000f;

    /// <summary>Lowest displayed frequency in Hz.</summary>
    [Category("Data")]
    public float MinFrequency { get; set; } = 20f;

    /// <summary>Highest displayed frequency in Hz.</summary>
    [Category("Data")]
    public float MaxFrequency { get; set; } = 20000f;

    /// <summary>Gain range above and below 0 dB.</summary>
    [Category("Data")]
    public float GainRangeDb { get; set; } = 18f;

    /// <summary>Lowest Q reachable with the wheel.</summary>
    [Category("Behavior")]
    public float MinQ { get; set; } = 0.1f;

    /// <summary>Highest Q reachable with the wheel.</summary>
    [Category("Behavior")]
    public float MaxQ { get; set; } = 18f;

    /// <summary>Double-click on an empty area adds a band; the band menu offers "Delete".</summary>
    [Category("Behavior")]
    public bool AllowAddBands { get; set; }

    /// <summary>Maximum number of bands when <see cref="AllowAddBands"/> is set.</summary>
    [Category("Behavior")]
    public int MaxBands { get; set; } = 8;

    /// <summary>Draws each band's own curve below the total response.</summary>
    [Category("Appearance")]
    public bool ShowBandCurves { get; set; } = true;

    /// <summary>Total response color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? CurveColor { get; set; }

    /// <summary>Audio to analyze and draw behind the curve, or <c>null</c>.</summary>
    [Browsable(false)]
    public AudioRingBuffer? SpectrumSource { get; set; }

    /// <summary>Selected band index (−1 = none).</summary>
    [Browsable(false)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = value < 0 || value >= Bands.Count ? -1 : value;
            if (value == _selected) return;
            _selected = value;
            SelectionChanged?.Invoke(this);
            InvalidateVisual();
        }
    }

    /// <summary>Raised after the user changed a band.</summary>
    public event Action<ParametricEqEditor, EqBand>? BandChanged;

    /// <summary>Raised after the user added a band (<see cref="AllowAddBands"/>).</summary>
    public event Action<ParametricEqEditor, EqBand>? BandAdded;

    /// <summary>Raised after the user deleted a band.</summary>
    public event Action<ParametricEqEditor, EqBand>? BandRemoved;

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event Action<ParametricEqEditor>? SelectionChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(420f, 200f);

    /// <summary>Total response in dB of the enabled bands at a frequency.</summary>
    /// <param name="frequency">Frequency in Hz.</param>
    /// <returns>Sum of the band magnitudes in dB.</returns>
    public double ResponseDb(double frequency)
    {
        double db = 0.0;
        foreach (var band in Bands)
        {
            if (!band.IsEnabled) continue;
            var c = band.Coefficients(SampleRate);
            db += BiquadDesign.MagnitudeDb(c, frequency, SampleRate);
        }
        return db;
    }

    private Color ColorOf(int index) => Bands[index].Color ?? AutoColors[index % AutoColors.Length];

    private Rect GraphArea(Rect widget) => widget.Deflate(new Thickness(2f, 2f, 2f, 14f));

    private Point NodePosition(EqBand band, Rect area)
    {
        float x = GraphHelpers.LogToX((float)band.Frequency, MinFrequency, MaxFrequency, area);
        float gain = band.HasGain ? (float)band.GainDb : 0f;
        return new Point(x, GraphHelpers.ValueToY(gain, -GainRangeDb, GainRangeDb, area));
    }

    private int HitNode(Point p)
    {
        var area = GraphArea(Bounds);
        int best = -1;
        float bestDistance = 12f;
        for (int i = 0; i < Bands.Count; i++)
        {
            float d = (NodePosition(Bands[i], area) - p).Length;
            if (d <= bestDistance) { bestDistance = d; best = i; }
        }
        return best;
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
        // Repaint when a band changed from outside (automation, preset) or new audio arrived.
        double version = Bands.Count;
        foreach (var b in Bands) version = version * 31.0 + b.Frequency + b.GainDb * 7.0 + b.Q * 13.0 + (int)b.Type * 17.0 + (b.IsEnabled ? 1.0 : 0.0);
        if (SpectrumSource is { } s) version += s.TotalWritten;
        if (version != _version)
        {
            _version = version;
            InvalidateVisual();
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
        float step = GainRangeDb > 12f ? 6f : 3f;
        GraphHelpers.DrawLevelGrid(dc, Theme, area, -GainRangeDb, GainRangeDb, step, true);
        dc.PushClip(area);
        if (SpectrumSource is not null) RenderSpectrum(dc, area, p);

        int n = Math.Max(32, (int)area.Width / 2);
        var total = new double[n + 1];
        var freq = new double[n + 1];
        for (int i = 0; i <= n; i++) freq[i] = GraphHelpers.XToLog(area.X + area.Width * i / n, MinFrequency, MaxFrequency, area);
        var points = new Point[n + 1];
        for (int b = 0; b < Bands.Count; b++)
        {
            var band = Bands[b];
            if (!band.IsEnabled) continue;
            var c = band.Coefficients(SampleRate);
            for (int i = 0; i <= n; i++)
            {
                double db = BiquadDesign.MagnitudeDb(c, freq[i], SampleRate);
                total[i] += db;
                points[i] = new Point(area.X + area.Width * i / n, GraphHelpers.ValueToY((float)db, -GainRangeDb, GainRangeDb, area));
            }
            if (ShowBandCurves)
            {
                var color = ColorOf(b);
                bool active = b == _selected || b == _hover || b == _dragging;
                var fill = new Point[n + 3];
                points.CopyTo(fill, 0);
                float zero = GraphHelpers.ValueToY(0f, -GainRangeDb, GainRangeDb, area);
                fill[n + 1] = new Point(area.Right, zero);
                fill[n + 2] = new Point(area.X, zero);
                dc.FillPolygon(fill, color.WithAlpha(active ? 0.22f : 0.10f));
                dc.DrawPolyline(points, color.WithAlpha(active ? 0.9f : 0.45f), active ? 1.5f : 1f);
            }
        }
        for (int i = 0; i <= n; i++) points[i] = new Point(area.X + area.Width * i / n, GraphHelpers.ValueToY((float)total[i], -GainRangeDb, GainRangeDb, area));
        dc.DrawPolyline(points, CurveColor ?? style.Accent, 2.2f, StrokeStyle.Round);

        // Nodes.
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        for (int b = 0; b < Bands.Count; b++)
        {
            var band = Bands[b];
            var pos = NodePosition(band, area);
            var color = ColorOf(b);
            bool active = b == _selected || b == _hover || b == _dragging;
            float r = active ? 8f : 7f;
            if (band.IsEnabled) dc.FillCircle(pos, r, color);
            else dc.FillCircle(pos, r, p.Background.WithAlpha(0.8f));
            dc.DrawCircle(pos, r, band.IsEnabled ? color.Lighten(0.3f) : color, band.IsEnabled ? 1f : 1.5f);
            dc.DrawText(InvariantNumber.Format(b + 1, "0"), new Rect(pos.X - r, pos.Y - 6f, r * 2f, 12f), label, band.IsEnabled ? color.Contrasting : color);
        }
        dc.PopClip();

        // Readout of the hovered / selected band.
        int show = _dragging >= 0 ? _dragging : _hover >= 0 ? _hover : _selected;
        if (show >= 0 && show < Bands.Count)
        {
            var band = Bands[show];
            string text = GraphHelpers.FormatHz((float)band.Frequency) + "Hz"
                + (band.HasGain ? "  " + (band.GainDb >= 0 ? "+" : "") + InvariantNumber.Format(band.GainDb, "0.0") + " dB" : "")
                + "  Q " + InvariantNumber.Format(band.Q, "0.00") + "  " + TypeName(band.Type);
            dc.DrawText(text, new Rect(area.X + 6f, area.Y + 3f, area.Width - 12f, 12f), Theme.Typography.Small with { Size = 10f, Align = HorizontalAlignment.Right }, ColorOf(show));
        }
    }

    private void RenderSpectrum(DrawingContext dc, Rect area, Palette p)
    {
        _fft ??= new Fft(4096);
        int size = _fft.Size;
        if (_spectrumSamples.Length != size) { _spectrumSamples = new float[size]; _spectrum = new float[size / 2]; }
        if (SpectrumSource!.CopyLatest(_spectrumSamples) < size) return;
        _fft.MagnitudeSpectrum(_spectrumSamples, _spectrum, WindowFunction.Hann, -120f);
        int columns = Math.Max(16, (int)area.Width / 2);
        var points = new Point[columns + 2];
        float binHz = SampleRate / size;
        for (int c = 0; c < columns; c++)
        {
            float f0 = GraphHelpers.XToLog(area.X + area.Width * c / columns, MinFrequency, MaxFrequency, area);
            float f1 = GraphHelpers.XToLog(area.X + area.Width * (c + 1) / columns, MinFrequency, MaxFrequency, area);
            int i0 = Math.Clamp((int)(f0 / binHz), 0, _spectrum.Length - 1), i1 = Math.Clamp((int)MathF.Ceiling(f1 / binHz), i0 + 1, _spectrum.Length);
            float db = VectorOps.Max(_spectrum.AsSpan(i0, i1 - i0)) + 4.5f * MathF.Log2(MathF.Max(20f, (f0 + f1) * 0.5f) / 1000f); // +4.5 dB/oct tilt
            points[c] = new Point(area.X + area.Width * (c + 0.5f) / columns, GraphHelpers.ValueToY(db, -90f, 0f, area));
        }
        points[columns] = new Point(area.Right, area.Bottom);
        points[columns + 1] = new Point(area.X, area.Bottom);
        dc.FillPolygon(points, p.TextDim.WithAlpha(0.12f));
    }

    private string TypeName(EqBandType type)
    {
        var t = Strings;
        return type switch
        {
            EqBandType.LowShelf => t.BandLowShelf,
            EqBandType.HighShelf => t.BandHighShelf,
            EqBandType.LowCut => t.BandLowCut,
            EqBandType.HighCut => t.BandHighCut,
            EqBandType.Notch => t.BandNotch,
            EqBandType.BandPass => t.BandBandPass,
            _ => t.BandBell,
        };
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging >= 0)
        {
            var band = Bands[_dragging];
            var area = GraphArea(Bounds);
            var target = e.Position - _dragOffset;
            if (e.Shift)
            {
                var current = NodePosition(band, area);
                target = current + (target - current) * 0.15f;
            }
            band.Frequency = Math.Clamp(GraphHelpers.XToLog(target.X, MinFrequency, MaxFrequency, area), MinFrequency, MaxFrequency);
            if (band.HasGain) band.GainDb = Math.Clamp(GraphHelpers.YToValue(target.Y, -GainRangeDb, GainRangeDb, area), -GainRangeDb, GainRangeDb);
            BandChanged?.Invoke(this, band);
            InvalidateVisual();
            return;
        }
        int hover = HitNode(e.Position);
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
        if (_hover >= 0) { _hover = -1; InvalidateVisual(); }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        int hit = HitNode(e.Position);
        if (e.Button == MouseButton.Right)
        {
            if (hit < 0) return;
            SelectedIndex = hit;
            ShowBandMenu(hit, e.Position);
            e.Handled = true;
            return;
        }
        if (e.Button != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            if (hit >= 0)
            {
                var band = Bands[hit];
                band.IsEnabled = !band.IsEnabled;
                BandChanged?.Invoke(this, band);
            }
            else if (AllowAddBands && Bands.Count < MaxBands)
            {
                var area = GraphArea(Bounds);
                var band = new EqBand(EqBandType.Bell,
                    Math.Clamp(GraphHelpers.XToLog(e.Position.X, MinFrequency, MaxFrequency, area), MinFrequency, MaxFrequency),
                    Math.Clamp(GraphHelpers.YToValue(e.Position.Y, -GainRangeDb, GainRangeDb, area), -GainRangeDb, GainRangeDb), 1.0);
                Bands.Add(band);
                SelectedIndex = Bands.Count - 1;
                BandAdded?.Invoke(this, band);
            }
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        SelectedIndex = hit;
        if (hit < 0) return;
        _dragging = hit;
        _dragOffset = e.Position - NodePosition(Bands[hit], GraphArea(Bounds));
        Bands[hit].FrequencyParameter?.BeginEdit();
        if (Bands[hit].HasGain) Bands[hit].GainParameter?.BeginEdit();
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
        if (_dragging < 0) return;
        if (_dragging < Bands.Count)
        {
            var band = Bands[_dragging];
            band.FrequencyParameter?.EndEdit();
            if (band.HasGain) band.GainParameter?.EndEdit();
        }
        _dragging = -1;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        int index = HitNode(e.Position);
        if (index < 0) index = _selected;
        if (index < 0) return;
        var band = Bands[index];
        double factor = Math.Pow(e.Shift ? 1.03 : 1.15, e.WheelDelta);
        band.QParameter?.BeginEdit();
        band.Q = Math.Clamp(band.Q * factor, MinQ, MaxQ);
        band.QParameter?.EndEdit();
        BandChanged?.Invoke(this, band);
        e.Handled = true;
        InvalidateVisual();
    }

    private void ShowBandMenu(int index, Point at)
    {
        if (Host is null) return;
        var band = Bands[index];
        var t = Strings;
        var items = new List<MenuItem>();
        foreach (var type in Enum.GetValues<EqBandType>())
        {
            var captured = type;
            items.Add(new MenuItem(TypeName(type), () => { band.Type = captured; BandChanged?.Invoke(this, band); InvalidateVisual(); }) { IsChecked = band.Type == type });
        }
        items.Add(MenuItem.Separator());
        items.Add(new MenuItem(t.Bypass, () => { band.IsEnabled = !band.IsEnabled; BandChanged?.Invoke(this, band); InvalidateVisual(); }) { IsChecked = !band.IsEnabled });
        if (AllowAddBands)
        {
            items.Add(new MenuItem(t.DeleteBand, () =>
            {
                Bands.Remove(band);
                SelectedIndex = -1;
                _hover = -1;
                BandRemoved?.Invoke(this, band);
                InvalidateVisual();
            }));
        }
        Host.ShowMenu(items, at);
    }
}
