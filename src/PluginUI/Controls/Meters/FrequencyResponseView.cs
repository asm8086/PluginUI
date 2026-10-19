// 2026/10/19 22:45:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>Frequency axis of a <see cref="FrequencyResponseView"/>.</summary>
public enum FrequencyScale
{
    /// <summary>Logarithmic axis (equal width per octave) between <see cref="FrequencyResponseView.MinFrequency"/> and <see cref="FrequencyResponseView.MaxFrequency"/>.</summary>
    Logarithmic,

    /// <summary>Linear axis from 0 Hz to the Nyquist frequency.</summary>
    Linear,
}

/// <summary>Vertical axis a <see cref="ResponseCurve"/> is drawn against.</summary>
public enum ResponseAxis
{
    /// <summary>The left axis (magnitude, <see cref="FrequencyResponseView.LeftMin"/> … <see cref="FrequencyResponseView.LeftMax"/>).</summary>
    Left,

    /// <summary>The right axis, scaled automatically to the visible right-axis curves (phase, delays…).</summary>
    Right,
}

/// <summary>Tick labelling of the right axis.</summary>
public enum RightAxisTicks
{
    /// <summary>Round decimal steps (1, 2, 5 × 10ⁿ).</summary>
    Decimal,

    /// <summary>Multiples or fractions of π, for phases in radians.</summary>
    PiMultiples,
}

/// <summary>Decibel convention used to convert the analyzed spectrum.</summary>
public enum DecibelConvention
{
    /// <summary>dB = 20·log10(amplitude).</summary>
    TwentyLog10,

    /// <summary>dB = 6·log2(amplitude): exactly 6 dB per factor of two.</summary>
    SixLog2,
}

/// <summary>
/// One curve of a <see cref="FrequencyResponseView"/>: a function of the frequency (Hz) evaluated once per pixel column.
/// </summary>
public sealed class ResponseCurve
{
    /// <summary>Creates a curve.</summary>
    /// <param name="name">Name (for code and tooltips).</param>
    /// <param name="color">Stroke color.</param>
    /// <param name="evaluate">Value at a frequency in Hz, in the units of <paramref name="axis"/>; NaN leaves a gap.</param>
    /// <param name="axis">Vertical axis.</param>
    public ResponseCurve(string name, Color color, Func<double, double> evaluate, ResponseAxis axis = ResponseAxis.Left)
    {
        Name = name;
        Color = color;
        Evaluate = evaluate;
        Axis = axis;
    }

    /// <summary>Name of the curve.</summary>
    public string Name { get; set; }

    /// <summary>Stroke color.</summary>
    public Color Color { get; set; }

    /// <summary>Value at a frequency in Hz (NaN = gap).</summary>
    public Func<double, double> Evaluate { get; set; }

    /// <summary>Vertical axis of the curve.</summary>
    public ResponseAxis Axis { get; set; }

    /// <summary>Whether the curve is drawn.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Stroke thickness in DIPs.</summary>
    public float Thickness { get; set; } = 1.5f;

    /// <summary>Stroke style (dashes…), or <c>null</c> for a solid round-joined line.</summary>
    public StrokeStyle? Stroke { get; set; }

    /// <summary>Opacity of the area between the curve and the left-axis zero (0 = no fill). Left-axis curves only.</summary>
    public float FillOpacity { get; set; }
}

/// <summary>
/// A draggable point of a <see cref="FrequencyResponseView"/>, bound to parameters or holding its own values.
/// </summary>
/// <remarks>
/// <para>A node has either one frequency (<see cref="FrequencyParameter"/> / <see cref="Frequency"/>) or, when
/// <see cref="UsesBandEdges"/> is set, two band edges (<see cref="LowFrequencyParameter"/>,
/// <see cref="HighFrequencyParameter"/>): it then sits at their geometric centre, dragging it shifts both edges with a
/// constant ratio, the wheel changes the ratio around the centre and two small handles move each edge alone.</para>
/// <para>The vertical position is <see cref="Value"/> on the left axis; it is draggable only when
/// <see cref="IsValueEditable"/> is set (a gain), otherwise the node stays at <see cref="Value"/> (0 dB by default).</para>
/// <para>Parameter values are read with <see cref="IParameter.ToPlain"/> (Hz, dB) and written inside edit gestures.</para>
/// </remarks>
public sealed class ResponseNode
{
    private double _frequency = 1000.0, _low = 500.0, _high = 2000.0, _value;

    /// <summary>Short text drawn inside the node (for instance the band number).</summary>
    public string? Text { get; set; }

    /// <summary>Node color.</summary>
    public Color Color { get; set; } = Color.FromRgb(0x4FC3F7);

    /// <summary>Whether the node is drawn and can be hit.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Shows a warning ring (for instance when the requested design is not realizable).</summary>
    public bool ShowWarning { get; set; }

    /// <summary>The node is defined by two band edges instead of one frequency.</summary>
    public bool UsesBandEdges { get; set; }

    /// <summary>Frequency parameter (plain value in Hz), or <c>null</c>.</summary>
    public IParameter? FrequencyParameter { get; set; }

    /// <summary>Lower band-edge parameter (plain value in Hz), or <c>null</c>.</summary>
    public IParameter? LowFrequencyParameter { get; set; }

    /// <summary>Upper band-edge parameter (plain value in Hz), or <c>null</c>.</summary>
    public IParameter? HighFrequencyParameter { get; set; }

    /// <summary>Vertical value parameter (plain value in left-axis units), or <c>null</c>.</summary>
    public IParameter? ValueParameter { get; set; }

    /// <summary>The vertical position can be dragged.</summary>
    public bool IsValueEditable { get; set; }

    /// <summary>Parameter stepped by the mouse wheel over a single-frequency node (a width or Q), or <c>null</c>.</summary>
    public IParameter? WheelParameter { get; set; }

    /// <summary>Normalized step of <see cref="WheelParameter"/> per wheel notch (Shift divides it by 5).</summary>
    public double WheelStep { get; set; } = 0.02;

    /// <summary>Text shown while the node is hovered or dragged, or <c>null</c>.</summary>
    public Func<string>? Readout { get; set; }

    /// <summary>Free user data.</summary>
    public object? Tag { get; set; }

    /// <summary>Frequency in Hz: the geometric centre of the band edges when <see cref="UsesBandEdges"/> is set.</summary>
    public double Frequency
    {
        get => UsesBandEdges ? Math.Sqrt(LowFrequency * HighFrequency) : Read(FrequencyParameter, _frequency);
        set { if (FrequencyParameter is { } p) Write(p, value); else _frequency = value; }
    }

    /// <summary>Lower band edge in Hz.</summary>
    public double LowFrequency
    {
        get => Read(LowFrequencyParameter, _low);
        set { if (LowFrequencyParameter is { } p) Write(p, value); else _low = value; }
    }

    /// <summary>Upper band edge in Hz.</summary>
    public double HighFrequency
    {
        get => Read(HighFrequencyParameter, _high);
        set { if (HighFrequencyParameter is { } p) Write(p, value); else _high = value; }
    }

    /// <summary>Vertical value in left-axis units.</summary>
    public double Value
    {
        get => Read(ValueParameter, _value);
        set { if (ValueParameter is { } p) Write(p, value); else _value = value; }
    }

    internal IEnumerable<IParameter> DragParameters(bool frequencyOnly)
    {
        if (UsesBandEdges)
        {
            if (LowFrequencyParameter is { } l) yield return l;
            if (HighFrequencyParameter is { } h) yield return h;
        }
        else if (FrequencyParameter is { } f) yield return f;
        if (!frequencyOnly && IsValueEditable && ValueParameter is { } v) yield return v;
    }

    private static double Read(IParameter? p, double fallback) => p is null ? fallback : p.ToPlain(p.NormalizedValue);

    private static void Write(IParameter p, double plain)
    {
        double normalized = Math.Clamp(p.ToNormalized(plain), 0.0, 1.0);
        if (p.IsEditing) p.PerformEdit(normalized);
        else p.SetValue(normalized);
    }
}

/// <summary>
/// Frequency-response display: any number of curves on a logarithmic or linear frequency axis, a fixed left axis
/// (magnitude) and an auto-scaled right axis (phase, phase delay, group delay…), an optional analyzed input spectrum
/// and the same spectrum shaped by a response without a second FFT, and draggable nodes.
/// </summary>
/// <remarks>
/// <para><b>Curves.</b> The element does no filter design: each <see cref="ResponseCurve"/> supplies its values, so the
/// display is exact for any filter the consumer can evaluate.</para>
/// <para><b>Spectrum.</b> <see cref="SpectrumSource"/> is read sequentially (the view is its only reader); a Hann-windowed
/// FFT of <see cref="FftSize"/> points runs every <c>FftSize/2</c> new samples (50 % overlap) and each bin is smoothed
/// with an instant attack and an exponential release (<see cref="SpectrumReleaseSeconds"/>). A full-scale sine reads
/// 0 dB. The filtered spectrum adds <see cref="SpectrumFilter"/> (dB at a frequency) to every bin of the smoothed input
/// spectrum: the output spectrum of a linear filter, obtained from the input FFT alone.</para>
/// <para><b>Mouse.</b> Drag a node to change its frequency (and its value when editable; Shift = fine), drag a band-edge
/// handle to move one edge, turn the wheel over a node to change its band ratio or step its <see cref="ResponseNode.WheelParameter"/>.
/// Drags send complete edit gestures, all parameters of the node in parallel.</para>
/// </remarks>
/// <example>
/// <code>
/// var view = new FrequencyResponseView { SampleRate = 48000, SpectrumSource = shared.Input };
/// // Curves: any function of the frequency in Hz (here the magnitude in dB of the plug-in's own filter).
/// view.Curves.Add(new ResponseCurve("Band 1", Color.FromRgb(0xFF7043), f => band1.MagnitudeDb(f)));
/// view.Curves.Add(new ResponseCurve("Phase", Color.FromRgb(0xFFB300), f => cascade.Phase(f), ResponseAxis.Right));
/// view.RightAxisTicks = RightAxisTicks.PiMultiples;
/// view.SpectrumFilter = f => cascade.MagnitudeDb(f);   // output spectrum without a second FFT
/// // A gain node, and a band node defined by its two edges.
/// view.Nodes.Add(new ResponseNode { FrequencyParameter = Parameters[model.Freq], ValueParameter = Parameters[model.Gain], IsValueEditable = true, WheelParameter = Parameters[model.Q] });
/// view.Nodes.Add(new ResponseNode { UsesBandEdges = true, LowFrequencyParameter = Parameters[model.Low], HighFrequencyParameter = Parameters[model.High] });
/// view.ContentVersion = () => model.Shape.NormalizedValue;   // repaint when a design parameter changes
/// </code>
/// </example>
public class FrequencyResponseView : Control
{
    private const float NodeRadius = 7f;
    private const float HandleSize = 4f;
    private static readonly int[] Sides = [-1, 1];
    private static readonly int[] PiDivisors = [1, 2, 4];

    private int _hover = -1, _hoverHandle, _selected = -1, _dragging = -1, _dragHandle;
    private Point _dragOffset;
    private readonly List<IParameter> _dragParams = [];
    private RefreshTicker? _ticker;
    private long _drawnSpectrumFrames = -1;
    private double _version = double.NaN;

    // Spectrum analysis state.
    private Fft? _fft;
    private float[] _history = [];
    private int _historyFill;
    private float[] _frame = [];
    private float[] _bins = [];
    private float[] _smoothed = [];
    private float[] _readBuffer = new float[2048];
    private long _spectrumFrames;
    private AudioRingBuffer? _analyzedSource;

    /// <summary>Creates a frequency-response view.</summary>
    public FrequencyResponseView()
    {
        Cursor = StandardCursor.Arrow;
    }

    /// <summary>The curves, drawn in list order (later curves on top).</summary>
    [Browsable(false)]
    public IList<ResponseCurve> Curves { get; } = new List<ResponseCurve>();

    /// <summary>The draggable nodes.</summary>
    [Browsable(false)]
    public IList<ResponseNode> Nodes { get; } = new List<ResponseNode>();

    /// <summary>Sample rate in Hz (Nyquist limit of the linear axis, bin spacing of the spectrum).</summary>
    [Category("Data")]
    public double SampleRate { get; set; } = 48000.0;

    /// <summary>Frequency axis.</summary>
    [Category("Data")]
    public FrequencyScale FrequencyScale { get; set; } = FrequencyScale.Logarithmic;

    /// <summary>Lowest frequency of the logarithmic axis, in Hz.</summary>
    [Category("Data")]
    public double MinFrequency { get; set; } = 20.0;

    /// <summary>Highest frequency of the logarithmic axis, in Hz (limited to the Nyquist frequency).</summary>
    [Category("Data")]
    public double MaxFrequency { get; set; } = 20000.0;

    /// <summary>Bottom of the left axis.</summary>
    [Category("Data")]
    public double LeftMin { get; set; } = -24.0;

    /// <summary>Top of the left axis.</summary>
    [Category("Data")]
    public double LeftMax { get; set; } = 24.0;

    /// <summary>Grid step of the left axis.</summary>
    [Category("Data")]
    public double LeftStep { get; set; } = 6.0;

    /// <summary>Unit appended to the left-axis labels.</summary>
    [Category("Data")]
    public string LeftUnit { get; set; } = "dB";

    /// <summary>Unit shown at the top of the right axis.</summary>
    [Category("Data")]
    public string RightUnit { get; set; } = "";

    /// <summary>Tick labelling of the right axis.</summary>
    [Category("Data")]
    public RightAxisTicks RightAxisTicks { get; set; } = RightAxisTicks.Decimal;

    /// <summary>Color of the right-axis labels (null = the color of the first visible right-axis curve).</summary>
    [Category("Appearance")]
    public Color? RightAxisColor { get; set; }

    /// <summary>Smallest ratio between the band edges of a node.</summary>
    [Category("Behavior")]
    public double MinBandRatio { get; set; } = Math.Pow(2.0, 1.0 / 12.0);

    /// <summary>Largest ratio between the band edges of a node.</summary>
    [Category("Behavior")]
    public double MaxBandRatio { get; set; } = 1000.0;

    /// <summary>Audio analyzed for the spectra, or <c>null</c>. The view reads it sequentially (single reader).</summary>
    [Browsable(false)]
    public AudioRingBuffer? SpectrumSource { get; set; }

    /// <summary>FFT size of the analyzer (power of two, 64 to 32768).</summary>
    [Category("Data")]
    public int FftSize { get; set; } = 2048;

    /// <summary>Draws the analyzed input spectrum.</summary>
    [Category("Appearance")]
    public bool ShowInputSpectrum { get; set; } = true;

    /// <summary>Draws the input spectrum shaped by <see cref="SpectrumFilter"/>.</summary>
    [Category("Appearance")]
    public bool ShowFilteredSpectrum { get; set; } = true;

    /// <summary>Input spectrum color.</summary>
    [Category("Appearance")]
    public Color InputSpectrumColor { get; set; } = Color.FromRgb(0x78909C);

    /// <summary>Filtered spectrum color.</summary>
    [Category("Appearance")]
    public Color FilteredSpectrumColor { get; set; } = Color.FromRgb(0x26A69A);

    /// <summary>Gain in dB applied to the input spectrum at a frequency to obtain the filtered spectrum, or <c>null</c>.</summary>
    [Browsable(false)]
    public Func<double, double>? SpectrumFilter { get; set; }

    /// <summary>Spectrum level at the bottom of the plot.</summary>
    [Category("Data")]
    public double SpectrumMinDb { get; set; } = -120.0;

    /// <summary>Spectrum level at the top of the plot.</summary>
    [Category("Data")]
    public double SpectrumMaxDb { get; set; }

    /// <summary>Release time constant of the spectrum smoothing, in seconds (0 = none).</summary>
    [Category("Behavior")]
    public double SpectrumReleaseSeconds { get; set; } = 0.3;

    /// <summary>Decibel convention of the spectrum (the curves are drawn as supplied).</summary>
    [Category("Data")]
    public DecibelConvention DecibelConvention { get; set; } = DecibelConvention.TwentyLog10;

    /// <summary>
    /// Returns a value that changes whenever the curves change for a reason the view cannot see (a design parameter
    /// not bound to a node); polled every frame, the view repaints when it changes. Node parameters are tracked already.
    /// </summary>
    [Browsable(false)]
    public Func<double>? ContentVersion { get; set; }

    /// <summary>Selected node index (−1 = none).</summary>
    [Browsable(false)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = value < 0 || value >= Nodes.Count ? -1 : value;
            if (value == _selected) return;
            _selected = value;
            SelectionChanged?.Invoke(this);
            InvalidateVisual();
        }
    }

    /// <summary>Number of FFT frames analyzed so far (diagnostics and tests).</summary>
    [Browsable(false)]
    public long SpectrumFrameCount => _spectrumFrames;

    /// <summary>Raised after the user moved a node or changed it with the wheel.</summary>
    public event Action<FrequencyResponseView, ResponseNode>? NodeChanged;

    /// <summary>Raised on a wheel notch over a node (after the built-in handling), with the signed delta.</summary>
    public event Action<FrequencyResponseView, ResponseNode, float>? NodeWheel;

    /// <summary>Raised when a node is double-clicked.</summary>
    public event Action<FrequencyResponseView, ResponseNode>? NodeDoubleClicked;

    /// <summary>Raised when <see cref="SelectedIndex"/> changes.</summary>
    public event Action<FrequencyResponseView>? SelectionChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(640f, 300f);

    // ---------------------------------------------------------------- geometry

    /// <summary>Lowest displayed frequency in Hz.</summary>
    public double AxisMinFrequency => FrequencyScale == FrequencyScale.Linear ? 0.0 : Math.Max(1.0, Math.Min(MinFrequency, Nyquist * 0.5));

    /// <summary>Highest displayed frequency in Hz.</summary>
    public double AxisMaxFrequency => FrequencyScale == FrequencyScale.Linear ? Nyquist : Math.Max(AxisMinFrequency * 2.0, Math.Min(MaxFrequency, Nyquist));

    private double Nyquist => Math.Max(1.0, SampleRate * 0.5);

    /// <summary>Plot area inside the element bounds (room for the frequency labels below).</summary>
    /// <param name="widget">Element bounds.</param>
    /// <returns>The plot rectangle.</returns>
    public static Rect PlotArea(Rect widget) => widget.Deflate(new Thickness(2f, 2f, 2f, 15f));

    /// <summary>X coordinate of a frequency in a plot area.</summary>
    /// <param name="hz">Frequency in Hz.</param>
    /// <param name="area">Plot area.</param>
    /// <returns>The x coordinate.</returns>
    public float FrequencyToX(double hz, Rect area)
    {
        double lo = AxisMinFrequency, hi = AxisMaxFrequency;
        double t = FrequencyScale == FrequencyScale.Linear
            ? (hz - lo) / (hi - lo)
            : Math.Log(Math.Max(hz, 1e-9) / lo) / Math.Log(hi / lo);
        return area.X + (float)(t * area.Width);
    }

    /// <summary>Frequency at an x coordinate of a plot area.</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="area">Plot area.</param>
    /// <returns>The frequency in Hz.</returns>
    public double XToFrequency(float x, Rect area)
    {
        double lo = AxisMinFrequency, hi = AxisMaxFrequency;
        double t = (x - area.X) / Math.Max(1f, area.Width);
        return FrequencyScale == FrequencyScale.Linear ? lo + t * (hi - lo) : lo * Math.Pow(hi / lo, t);
    }

    private float LeftToY(double v, Rect area) => (float)(area.Bottom - (v - LeftMin) / Math.Max(1e-9, LeftMax - LeftMin) * area.Height);

    private double YToLeft(float y, Rect area) => LeftMin + (area.Bottom - y) / Math.Max(1f, area.Height) * (LeftMax - LeftMin);

    private Point NodePosition(ResponseNode node, Rect area)
    {
        double v = Math.Clamp(node.Value, LeftMin, LeftMax);
        return new Point(FrequencyToX(node.Frequency, area), LeftToY(v, area));
    }

    /// <summary>Index of the node at a point and the hit part: 0 = node body, −1 = lower edge, +1 = upper edge.</summary>
    private int HitNode(Point p, out int handle)
    {
        var area = PlotArea(Bounds);
        int best = -1;
        float bestDistance = 12f;
        handle = 0;
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (!node.IsVisible) continue;
            var pos = NodePosition(node, area);
            float d = (pos - p).Length;
            if (d <= bestDistance) { bestDistance = d; best = i; handle = 0; }
            if (!node.UsesBandEdges) continue;
            foreach (int side in Sides)
            {
                var hp = new Point(FrequencyToX(side < 0 ? node.LowFrequency : node.HighFrequency, area), pos.Y);
                float dh = (hp - p).Length;
                if (dh <= MathF.Min(bestDistance, 8f)) { bestDistance = dh; best = i; handle = side; }
            }
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
        AnalyzeSpectrum();
        double version = Nodes.Count * 1.0 + Curves.Count * 7.0 + (int)FrequencyScale * 3.0 + SampleRate;
        foreach (var n in Nodes)
            version = version * 1.0000001 + n.LowFrequency + n.HighFrequency * 3.0 + n.Frequency * 5.0 + n.Value * 11.0 + (n.IsVisible ? 13.0 : 0.0) + (n.ShowWarning ? 17.0 : 0.0);
        foreach (var c in Curves) version = version * 1.0000001 + (c.IsVisible ? 1.0 : 0.0);
        if (ContentVersion is { } cv) version += cv() * 19.0;
        bool spectrumChanged = SpectrumSource is not null && _spectrumFrames != _drawnSpectrumFrames;
        if (version != _version || spectrumChanged)
        {
            _version = version;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Reads the new samples of <see cref="SpectrumSource"/> and runs one FFT per half frame. Called every frame by the
    /// view; exposed for tests and for hosts that drive the view without a ticker.
    /// </summary>
    public void AnalyzeSpectrum()
    {
        var source = SpectrumSource;
        if (source is null) return;
        int size = Math.Clamp(FftSize, 64, 32768);
        if ((size & (size - 1)) != 0) size = 2048;
        if (_fft is null || _fft.Size != size || !ReferenceEquals(source, _analyzedSource))
        {
            _fft = new Fft(size);
            _history = new float[size];
            _frame = new float[size];
            _bins = new float[size / 2];
            _smoothed = new float[size / 2];
            Array.Fill(_smoothed, (float)SpectrumMinDb);
            _historyFill = 0;
            _pendingHop = 0;
            _analyzedSource = source;
        }

        int hop = size / 2;
        int framesThisCall = 0;
        while (framesThisCall < 16)
        {
            // Read up to the next hop boundary, so that an FFT runs exactly every `hop` samples.
            int want = Math.Min(hop - _pendingHop, _readBuffer.Length);
            int got = source.Read(_readBuffer.AsSpan(0, want));
            if (got <= 0) break;

            // Shift the history left and append (history keeps the last `size` samples).
            if (_historyFill + got > size)
            {
                int drop = _historyFill + got - size;
                Array.Copy(_history, drop, _history, 0, _historyFill - drop);
                _historyFill -= drop;
            }
            _readBuffer.AsSpan(0, got).CopyTo(_history.AsSpan(_historyFill));
            _historyFill += got;
            _pendingHop += got;

            if (_pendingHop >= hop)
            {
                _pendingHop = 0;
                framesThisCall++;
                if (_historyFill == size) RunFft(size, hop);
            }
        }
    }

    private int _pendingHop;

    private void RunFft(int size, int hop)
    {
        _history.AsSpan(0, size).CopyTo(_frame);
        _fft!.MagnitudeSpectrum(_frame, _bins, WindowFunction.Hann, -200f);
        // MagnitudeSpectrum returns 20·log10 dB; 6·log2(a) = 20·log10(a)·6/(20·log10 2).
        float convert = DecibelConvention == DecibelConvention.SixLog2 ? (float)(6.0 / (20.0 * Math.Log10(2.0))) : 1f;
        double tau = SpectrumReleaseSeconds;
        float release = tau <= 0.0 ? 1f : (float)(1.0 - Math.Exp(-hop / (Math.Max(1.0, SampleRate) * tau)));
        float floor = (float)SpectrumMinDb - 6f;
        for (int k = 0; k < _bins.Length; k++)
        {
            float db = MathF.Max(floor, _bins[k] * convert);
            float s = _smoothed[k];
            _smoothed[k] = db >= s ? db : s + (db - s) * release;
        }
        _spectrumFrames++;
    }

    /// <summary>Smoothed input spectrum level (dB) at a frequency, interpolated between bins; NaN before the first frame.</summary>
    /// <param name="hz">Frequency in Hz.</param>
    /// <returns>The level in dB.</returns>
    public double SpectrumLevel(double hz)
    {
        if (_spectrumFrames == 0 || _smoothed.Length == 0) return double.NaN;
        double binHz = SampleRate / (2.0 * _smoothed.Length);
        double k = hz / binHz;
        int k0 = Math.Clamp((int)Math.Floor(k), 0, _smoothed.Length - 1);
        int k1 = Math.Min(k0 + 1, _smoothed.Length - 1);
        double t = Math.Clamp(k - k0, 0.0, 1.0);
        return _smoothed[k0] + (_smoothed[k1] - _smoothed[k0]) * t;
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = MathF.Min(Theme.Metrics.CornerRadius, widget.Height * 0.2f);
        dc.FillRoundedRect(widget, radius, p.Background.Darken(0.3f));
        var area = PlotArea(widget);
        int columns = Math.Max(16, (int)area.Width);
        var freqs = new double[columns + 1];
        for (int i = 0; i <= columns; i++) freqs[i] = XToFrequency(area.X + area.Width * i / columns, area);

        DrawFrequencyGrid(dc, area, widget.Bottom, p);
        DrawLeftGrid(dc, area, p);

        dc.PushClip(area);
        if (SpectrumSource is not null && _spectrumFrames > 0)
        {
            if (ShowInputSpectrum) DrawSpectrum(dc, area, freqs, null, InputSpectrumColor, 0.18f);
            if (ShowFilteredSpectrum && SpectrumFilter is { } filter) DrawSpectrum(dc, area, freqs, filter, FilteredSpectrumColor, 0.10f);
            _drawnSpectrumFrames = _spectrumFrames;
        }

        // Right axis: evaluate the visible right curves first to scale the axis.
        var rightValues = new List<(ResponseCurve Curve, double[] Values)>();
        double rMin = double.PositiveInfinity, rMax = double.NegativeInfinity;
        foreach (var curve in Curves)
        {
            if (!curve.IsVisible || curve.Axis != ResponseAxis.Right) continue;
            var values = new double[columns + 1];
            for (int i = 0; i <= columns; i++)
            {
                double v = curve.Evaluate(freqs[i]);
                values[i] = v;
                if (double.IsFinite(v)) { rMin = Math.Min(rMin, v); rMax = Math.Max(rMax, v); }
            }
            rightValues.Add((curve, values));
        }
        (double lo, double hi, double step) = rightValues.Count > 0 ? RightScale(rMin, rMax) : (0.0, 0.0, 0.0);
        if (rightValues.Count > 0) DrawRightGrid(dc, area, lo, hi, step, p);

        var points = new Point[columns + 1];
        foreach (var curve in Curves)
        {
            if (!curve.IsVisible) continue;
            double[]? values = null;
            if (curve.Axis == ResponseAxis.Right)
            {
                foreach (var rv in rightValues) if (ReferenceEquals(rv.Curve, curve)) { values = rv.Values; break; }
            }
            else
            {
                values = new double[columns + 1];
                for (int i = 0; i <= columns; i++) values[i] = curve.Evaluate(freqs[i]);
            }
            if (values is null) continue;

            if (curve.Axis == ResponseAxis.Left && curve.FillOpacity > 0f)
            {
                var fill = new Point[columns + 3];
                float zero = LeftToY(Math.Clamp(0.0, LeftMin, LeftMax), area);
                for (int i = 0; i <= columns; i++)
                {
                    double v = double.IsFinite(values[i]) ? Math.Clamp(values[i], LeftMin - 1.0, LeftMax + 1.0) : 0.0;
                    fill[i] = new Point(area.X + area.Width * i / columns, LeftToY(v, area));
                }
                fill[columns + 1] = new Point(area.Right, zero);
                fill[columns + 2] = new Point(area.X, zero);
                dc.FillPolygon(fill, curve.Color.WithAlpha(curve.FillOpacity));
            }

            int count = 0;
            for (int i = 0; i <= columns; i++)
            {
                double v = values[i];
                if (!double.IsFinite(v))
                {
                    Flush(dc, points, ref count, curve);
                    continue;
                }
                float y = curve.Axis == ResponseAxis.Left
                    ? LeftToY(Math.Clamp(v, LeftMin - (LeftMax - LeftMin), LeftMax + (LeftMax - LeftMin)), area)
                    : (float)(area.Bottom - (v - lo) / Math.Max(1e-12, hi - lo) * area.Height);
                points[count++] = new Point(area.X + area.Width * i / columns, y);
            }
            Flush(dc, points, ref count, curve);
        }

        DrawNodes(dc, area, p);
        dc.PopClip();

        // Readout of the hovered / dragged / selected node.
        int show = _dragging >= 0 ? _dragging : _hover >= 0 ? _hover : _selected;
        if (show >= 0 && show < Nodes.Count && Nodes[show].IsVisible && Nodes[show].Readout is { } readout)
        {
            var text = readout();
            dc.DrawText(text, new Rect(area.X + 40f, area.Y + 3f, area.Width - 90f, 13f),
                Theme.Typography.Small with { Size = 10f, Align = HorizontalAlignment.Center }, Nodes[show].Color.Lighten(0.2f));
        }
    }

    private static void Flush(DrawingContext dc, Point[] points, ref int count, ResponseCurve curve)
    {
        if (count >= 2) dc.DrawPolyline(points.AsSpan(0, count), curve.Color, curve.Thickness, curve.Stroke ?? StrokeStyle.Round);
        count = 0;
    }

    private void DrawSpectrum(DrawingContext dc, Rect area, double[] freqs, Func<double, double>? filter, Color color, float fillAlpha)
    {
        int columns = freqs.Length - 1;
        var pts = new Point[columns + 3];
        double binHz = SampleRate / (2.0 * _smoothed.Length);
        double spMin = SpectrumMinDb, spMax = SpectrumMaxDb;
        for (int c = 0; c <= columns; c++)
        {
            // Highest bin inside the column, or the interpolated level when the column holds no bin.
            double fa = c == 0 ? freqs[0] : 0.5 * (freqs[c - 1] + freqs[c]);
            double fb = c == columns ? freqs[c] : 0.5 * (freqs[c] + freqs[c + 1]);
            int k0 = (int)Math.Ceiling(fa / binHz), k1 = (int)Math.Floor(fb / binHz);
            double db;
            if (k1 >= k0 && k0 < _smoothed.Length)
            {
                db = double.NegativeInfinity;
                for (int k = Math.Max(0, k0); k <= Math.Min(k1, _smoothed.Length - 1); k++)
                {
                    double v = _smoothed[k] + (filter is null ? 0.0 : filter(k * binHz));
                    if (v > db) db = v;
                }
            }
            else
            {
                db = SpectrumLevel(freqs[c]) + (filter is null ? 0.0 : filter(freqs[c]));
            }
            if (!double.IsFinite(db)) db = spMin;
            double t = Math.Clamp((db - spMin) / Math.Max(1e-9, spMax - spMin), 0.0, 1.0);
            pts[c] = new Point(area.X + area.Width * c / columns, (float)(area.Bottom - t * area.Height));
        }
        pts[columns + 1] = new Point(area.Right, area.Bottom);
        pts[columns + 2] = new Point(area.X, area.Bottom);
        dc.FillPolygon(pts, color.WithAlpha(fillAlpha));
        dc.DrawPolyline(pts.AsSpan(0, columns + 1), color.WithAlpha(0.8f), 1f, StrokeStyle.Round);
    }

    private void DrawFrequencyGrid(DrawingContext dc, Rect area, float labelBottom, Palette p)
    {
        var grid = p.Border.WithAlpha(0.55f);
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        double lo = AxisMinFrequency, hi = AxisMaxFrequency;
        foreach (double f in FrequencyTicks(lo, hi))
        {
            float x = MathF.Round(FrequencyToX(f, area)) + 0.5f;
            if (x < area.X || x > area.Right) continue;
            bool major = FrequencyScale == FrequencyScale.Linear || IsDecade(f);
            dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), major ? grid : grid.WithAlpha(0.3f), 1f);
            if (x - area.X < 10f || area.Right - x < 10f) continue;
            if (FrequencyScale == FrequencyScale.Logarithmic && !IsLabelled(f)) continue;
            dc.DrawText(FormatHz(f), new Rect(x - 18f, area.Bottom + 1f, 36f, MathF.Max(10f, labelBottom - area.Bottom - 1f)), label, p.TextDim);
        }
    }

    private static bool IsDecade(double f)
    {
        double m = f / Math.Pow(10.0, Math.Floor(Math.Log10(f) + 1e-9));
        return Math.Abs(m - 1.0) < 1e-6;
    }

    private static bool IsLabelled(double f)
    {
        double m = f / Math.Pow(10.0, Math.Floor(Math.Log10(f) + 1e-9));
        return Math.Abs(m - 1.0) < 1e-6 || Math.Abs(m - 2.0) < 1e-6 || Math.Abs(m - 5.0) < 1e-6;
    }

    /// <summary>Grid frequencies of the current axis: 1-2-…-9 × 10ⁿ (log) or round steps (linear).</summary>
    /// <param name="lo">Lowest frequency.</param>
    /// <param name="hi">Highest frequency.</param>
    /// <returns>The tick frequencies in Hz.</returns>
    public IEnumerable<double> FrequencyTicks(double lo, double hi)
    {
        if (FrequencyScale == FrequencyScale.Linear)
        {
            double step = NiceStep((hi - lo) / 8.0);
            for (double f = Math.Ceiling(lo / step) * step; f <= hi + 1e-9; f += step) yield return f;
            yield break;
        }
        for (double decade = Math.Pow(10.0, Math.Floor(Math.Log10(lo))); decade <= hi; decade *= 10.0)
        {
            for (int m = 1; m <= 9; m++)
            {
                double f = m * decade;
                if (f >= lo * 0.999 && f <= hi * 1.001) yield return f;
            }
        }
    }

    private void DrawLeftGrid(DrawingContext dc, Rect area, Palette p)
    {
        var grid = p.Border.WithAlpha(0.55f);
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Left, Ellipsis = false };
        double step = LeftStep > 0.0 ? LeftStep : NiceStep((LeftMax - LeftMin) / 8.0);
        for (double v = Math.Ceiling(LeftMin / step) * step; v <= LeftMax + 1e-9; v += step)
        {
            float y = MathF.Round(LeftToY(v, area)) + 0.5f;
            dc.DrawLine(new Point(area.X, y), new Point(area.Right, y), Math.Abs(v) < 1e-9 ? p.Border.Lighten(0.15f) : grid, 1f);
            if (y - area.Y < 6f || area.Bottom - y < 6f) continue;
            string text = (v > 0 ? "+" : "") + InvariantNumber.Format(v, "0.##");
            dc.DrawText(text, new Rect(area.X + 3f, y - 11f, 60f, 10f), label, p.TextDim.WithAlpha(0.85f));
        }
        if (!string.IsNullOrEmpty(LeftUnit))
            dc.DrawText(LeftUnit, new Rect(area.X + 3f, area.Y + 2f, 60f, 10f), label, p.TextDim.WithAlpha(0.85f));
    }

    private (double Lo, double Hi, double Step) RightScale(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max)) { min = -1.0; max = 1.0; }
        double unit = RightAxisTicks == RightAxisTicks.PiMultiples ? Math.PI : 1.0;
        double span = max - min;
        if (span < 1e-9 * Math.Max(1.0, Math.Abs(max)))
        {
            // Flat curve: centre it in a default span.
            double c = 0.5 * (min + max);
            min = c - unit;
            max = c + unit;
            span = max - min;
        }
        double step = NiceStep(span / unit / 6.0) * unit;
        double lo = Math.Floor(min / step) * step, hi = Math.Ceiling(max / step) * step;
        if (hi - lo < step) hi = lo + step;
        return (lo, hi, step);
    }

    private void DrawRightGrid(DrawingContext dc, Rect area, double lo, double hi, double step, Palette p)
    {
        Color color = RightAxisColor ?? FirstRightColor() ?? p.TextDim;
        var label = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Right, Ellipsis = false };
        for (double v = lo; v <= hi + step * 1e-6; v += step)
        {
            float y = (float)(area.Bottom - (v - lo) / Math.Max(1e-12, hi - lo) * area.Height);
            dc.DrawLine(new Point(area.Right - 5f, y), new Point(area.Right, y), color.WithAlpha(0.7f), 1f);
            if (y - area.Y < 6f || area.Bottom - y < 6f) continue;
            dc.DrawText(FormatRight(v, step), new Rect(area.Right - 66f, y - 11f, 60f, 10f), label, color.WithAlpha(0.9f));
        }
        if (!string.IsNullOrEmpty(RightUnit))
            dc.DrawText(RightUnit, new Rect(area.Right - 66f, area.Y + 2f, 60f, 10f), label, color.WithAlpha(0.9f));
    }

    private Color? FirstRightColor()
    {
        foreach (var c in Curves) if (c.IsVisible && c.Axis == ResponseAxis.Right) return c.Color;
        return null;
    }

    private string FormatRight(double v, double step)
    {
        if (RightAxisTicks != RightAxisTicks.PiMultiples)
            return InvariantNumber.Format(v, step >= 1.0 ? "0" : step >= 0.1 ? "0.0" : "0.00");
        double k = v / Math.PI;
        if (Math.Abs(k) < 1e-9) return "0";
        string sign = k < 0 ? "−" : "";
        k = Math.Abs(k);
        // Halves and quarters of π are written as fractions.
        foreach (int d in PiDivisors)
        {
            double n = k * d;
            if (Math.Abs(n - Math.Round(n)) < 1e-6)
            {
                int num = (int)Math.Round(n);
                string head = num == 1 ? "π" : InvariantNumber.Format(num, "0") + "π";
                return sign + (d == 1 ? head : head + "/" + InvariantNumber.Format(d, "0"));
            }
        }
        return sign + InvariantNumber.Format(k, "0.##") + "π";
    }

    private static double NiceStep(double raw)
    {
        if (!(raw > 0.0) || !double.IsFinite(raw)) return 1.0;
        double e = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
        double m = raw / e;
        double nice = m <= 1.0 ? 1.0 : m <= 2.0 ? 2.0 : m <= 2.5 ? 2.5 : m <= 5.0 ? 5.0 : 10.0;
        return nice * e;
    }

    /// <summary>Formats a frequency for an axis label ("50", "1.5k", "20k").</summary>
    /// <param name="hz">Frequency in Hz.</param>
    /// <returns>The label.</returns>
    public static string FormatHz(double hz) => hz >= 1000.0
        ? InvariantNumber.Format(hz / 1000.0, hz >= 10000.0 ? "0.#" : "0.##") + "k"
        : InvariantNumber.Format(hz, "0.#");

    private void DrawNodes(DrawingContext dc, Rect area, Palette p)
    {
        var text = Theme.Typography.Small with { Size = 9f, Align = HorizontalAlignment.Center, Ellipsis = false };
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (!node.IsVisible) continue;
            var pos = NodePosition(node, area);
            bool active = i == _selected || i == _hover || i == _dragging;
            var color = node.Color;

            if (node.UsesBandEdges)
            {
                float xl = FrequencyToX(node.LowFrequency, area), xh = FrequencyToX(node.HighFrequency, area);
                dc.DrawLine(new Point(xl, pos.Y), new Point(xh, pos.Y), color.WithAlpha(active ? 0.8f : 0.5f), 1f, StrokeStyle.Dashed);
                foreach (int side in Sides)
                {
                    float hx = side < 0 ? xl : xh;
                    bool hot = (i == _hover && _hoverHandle == side) || (i == _dragging && _dragHandle == side);
                    float s = hot ? HandleSize + 1.5f : HandleSize;
                    var r = new Rect(hx - s, pos.Y - s, 2f * s, 2f * s);
                    dc.FillRect(r, hot ? color : p.Background.WithAlpha(0.9f));
                    dc.DrawRect(r, color, 1f);
                }
            }

            float radius = active ? NodeRadius + 1f : NodeRadius;
            if (node.ShowWarning) dc.DrawCircle(pos, radius + 3f, p.Danger, 2f);
            dc.FillCircle(pos, radius, color);
            dc.DrawCircle(pos, radius, active ? color.Lighten(0.5f) : color.Lighten(0.25f), active ? 1.5f : 1f);
            if (!string.IsNullOrEmpty(node.Text))
                dc.DrawText(node.Text, new Rect(pos.X - radius, pos.Y - 6f, radius * 2f, 12f), text, color.Contrasting);
        }
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging >= 0 && _dragging < Nodes.Count)
        {
            DragTo(Nodes[_dragging], e.Position - _dragOffset, e.Shift);
            NodeChanged?.Invoke(this, Nodes[_dragging]);
            InvalidateVisual();
            return;
        }
        int hover = HitNode(e.Position, out int handle);
        if (hover != _hover || handle != _hoverHandle)
        {
            _hover = hover;
            _hoverHandle = handle;
            Cursor = hover < 0 ? StandardCursor.Arrow : handle != 0 ? StandardCursor.SizeWE : StandardCursor.Hand;
            InvalidateVisual();
        }
    }

    private void DragTo(ResponseNode node, Point target, bool fine)
    {
        var area = PlotArea(Bounds);
        if (_dragHandle != 0)
        {
            var current = new Point(FrequencyToX(_dragHandle < 0 ? node.LowFrequency : node.HighFrequency, area), target.Y);
            if (fine) target = current + (target - current) * 0.15f;
            double f = Math.Clamp(XToFrequency(target.X, area), Math.Max(1.0, AxisMinFrequency), AxisMaxFrequency);
            if (_dragHandle < 0)
                node.LowFrequency = Math.Clamp(f, Math.Max(1.0, node.HighFrequency / MaxBandRatio), node.HighFrequency / MinBandRatio);
            else
                node.HighFrequency = Math.Clamp(f, node.LowFrequency * MinBandRatio, Math.Min(AxisMaxFrequency, node.LowFrequency * MaxBandRatio));
            return;
        }

        if (fine)
        {
            var current = NodePosition(node, area);
            target = current + (target - current) * 0.15f;
        }
        double centre = Math.Clamp(XToFrequency(target.X, area), Math.Max(1.0, AxisMinFrequency), AxisMaxFrequency);
        if (node.UsesBandEdges)
        {
            double ratio = Math.Max(MinBandRatio, node.HighFrequency / Math.Max(1e-9, node.LowFrequency));
            double half = Math.Sqrt(ratio);
            double low = centre / half, high = centre * half;
            double minF = Math.Max(1.0, AxisMinFrequency), maxF = AxisMaxFrequency;
            if (low < minF) { low = minF; high = low * ratio; }
            if (high > maxF) { high = maxF; low = high / ratio; }
            node.LowFrequency = low;
            node.HighFrequency = high;
        }
        else
        {
            node.Frequency = centre;
        }
        if (node.IsValueEditable)
            node.Value = Math.Clamp(YToLeft(target.Y, area), LeftMin, LeftMax);
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (_hover >= 0) { _hover = -1; _hoverHandle = 0; InvalidateVisual(); }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left) return;
        int hit = HitNode(e.Position, out int handle);
        if (e.ClickCount == 2)
        {
            if (hit >= 0)
            {
                NodeDoubleClicked?.Invoke(this, Nodes[hit]);
                e.Handled = true;
            }
            return;
        }
        SelectedIndex = hit;
        if (hit < 0) return;
        var node = Nodes[hit];
        _dragging = hit;
        _dragHandle = handle;
        var area = PlotArea(Bounds);
        var anchor = handle == 0
            ? NodePosition(node, area)
            : new Point(FrequencyToX(handle < 0 ? node.LowFrequency : node.HighFrequency, area), NodePosition(node, area).Y);
        _dragOffset = e.Position - anchor;
        _dragParams.Clear();
        if (handle < 0 && node.LowFrequencyParameter is { } l) _dragParams.Add(l);
        else if (handle > 0 && node.HighFrequencyParameter is { } h) _dragParams.Add(h);
        else if (handle == 0) _dragParams.AddRange(node.DragParameters(frequencyOnly: false));
        foreach (var p in _dragParams) p.BeginEdit();
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
        foreach (var p in _dragParams) p.EndEdit();
        _dragParams.Clear();
        _dragging = -1;
        _dragHandle = 0;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        int index = HitNode(e.Position, out _);
        if (index < 0) index = _selected;
        if (index < 0 || index >= Nodes.Count || !Nodes[index].IsVisible) return;
        var node = Nodes[index];
        if (node.UsesBandEdges)
        {
            double ratio = node.HighFrequency / Math.Max(1e-9, node.LowFrequency);
            double factor = Math.Pow(e.Shift ? 1.02 : 1.12, -e.WheelDelta);
            double newRatio = Math.Clamp(ratio * factor, MinBandRatio, MaxBandRatio);
            double centre = node.Frequency, half = Math.Sqrt(newRatio);
            double low = Math.Max(Math.Max(1.0, AxisMinFrequency), centre / half);
            double high = Math.Min(AxisMaxFrequency, centre * half);
            Gesture(node.LowFrequencyParameter, node.HighFrequencyParameter, () => { node.LowFrequency = low; node.HighFrequency = high; });
        }
        else if (node.WheelParameter is { } wp)
        {
            double step = node.WheelStep * (e.Shift ? 0.2 : 1.0) * e.WheelDelta;
            Gesture(wp, null, () => wp.PerformEdit(Math.Clamp(wp.NormalizedValue + step, 0.0, 1.0)));
        }
        NodeWheel?.Invoke(this, node, e.WheelDelta);
        NodeChanged?.Invoke(this, node);
        e.Handled = true;
        InvalidateVisual();
    }

    private static void Gesture(IParameter? a, IParameter? b, Action edit)
    {
        a?.BeginEdit();
        b?.BeginEdit();
        edit();
        b?.EndEdit();
        a?.EndEdit();
    }
}
