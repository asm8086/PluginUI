// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Step sequencer: a lane of step values (bars drawn with the mouse) above a lane of gates (on / off), with beat
/// grouping, an active length and a play cursor. Each step can be bound to parameters.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Drag across the value lane to draw values (fast movements are interpolated so no step is
/// skipped), double-click a bar to reset it to <see cref="DefaultStepValue"/>; click or drag across the gate lane to
/// switch gates (the first gate clicked decides on or off for the whole drag).</para>
/// <para><b>Parameters.</b> When <see cref="ValueParameters"/> / <see cref="GateParameters"/> are set (one per step), the
/// steps read and write them; each parameter touched during a drag gets a single gesture that ends with the drag.</para>
/// </remarks>
/// <example>
/// <code>
/// var seq = new StepSequencer { StepCount = 16, IsBipolar = true };
/// seq.CurrentStepSource = () => processor.CurrentStep; // −1 when stopped
/// seq.StepChanged += (s, i) => pattern[i] = s.GetValue(i);
/// </code>
/// </example>
public class StepSequencer : Control
{
    private double[] _values = new double[16];
    private bool[] _gates = new bool[16];
    private int _length = 16;
    private bool _painting;
    private bool _gatePainting;
    private bool _gateState;
    private int _lastStep = -1;
    private double _lastValue;
    private readonly HashSet<IParameter> _touched = [];
    private RefreshTicker? _ticker;
    private int _lastPlay = int.MinValue;

    /// <summary>Creates a 16-step sequencer with all gates on.</summary>
    public StepSequencer()
    {
        Array.Fill(_gates, true);
        Array.Fill(_values, 0.5);
    }

    /// <summary>Number of steps (1 … 64).</summary>
    [Category("Data")]
    public int StepCount
    {
        get => _values.Length;
        set
        {
            value = Math.Clamp(value, 1, 64);
            if (value == _values.Length) return;
            int old = _values.Length;
            Array.Resize(ref _values, value);
            Array.Resize(ref _gates, value);
            for (int i = old; i < value; i++) { _values[i] = DefaultStepValue; _gates[i] = true; }
            _length = Math.Min(_length == old ? value : _length, value);
            InvalidateVisual();
        }
    }

    /// <summary>Number of active steps (steps beyond are dimmed).</summary>
    [Category("Data")]
    public int Length { get => _length; set { _length = Math.Clamp(value, 1, StepCount); InvalidateVisual(); } }

    /// <summary>Steps per beat group (alternate background shading; 0 = none).</summary>
    [Category("Appearance")]
    public int GroupSize { get; set; } = 4;

    /// <summary>Draws values from the center line (−1..+1) instead of from the bottom.</summary>
    [Category("Value")]
    public bool IsBipolar { get; set; }

    /// <summary>Value restored by a double-click.</summary>
    [Category("Value")]
    public double DefaultStepValue { get; set; } = 0.5;

    /// <summary>Number of value steps (0 = continuous; e.g. 24 for semitones over two octaves).</summary>
    [Category("Value")]
    public int ValueSteps { get; set; }

    /// <summary>Shows the gate lane.</summary>
    [Category("Appearance")]
    public bool ShowGates { get; set; } = true;

    /// <summary>Step being played (−1 = none).</summary>
    [Category("Value")]
    public int CurrentStep { get; set; } = -1;

    /// <summary>Function polled every frame for the playing step (e.g. a volatile field written by the audio thread).</summary>
    [Browsable(false)]
    public Func<int>? CurrentStepSource { get; set; }

    /// <summary>One parameter per step for the values, or <c>null</c>.</summary>
    [Browsable(false)]
    public IReadOnlyList<IParameter>? ValueParameters { get; set; }

    /// <summary>One parameter per step for the gates (on above 0.5), or <c>null</c>.</summary>
    [Browsable(false)]
    public IReadOnlyList<IParameter>? GateParameters { get; set; }

    /// <summary>Raised after the user changed a step value (argument: step index).</summary>
    public event Action<StepSequencer, int>? StepChanged;

    /// <summary>Raised after the user switched a gate (argument: step index).</summary>
    public event Action<StepSequencer, int>? GateChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(400f, 120f);

    /// <summary>Value of a step (normalized).</summary>
    /// <param name="step">Step index.</param>
    /// <returns>The value in [0, 1].</returns>
    public double GetValue(int step) => ValueParameter(step)?.NormalizedValue ?? _values[step];

    /// <summary>Sets a step value (programmatic: bound parameters receive a complete gesture).</summary>
    /// <param name="step">Step index.</param>
    /// <param name="value">Value in [0, 1].</param>
    public void SetValue(int step, double value)
    {
        value = Quantize(value);
        if (ValueParameter(step) is { } p) p.SetValue(value);
        else _values[step] = value;
        InvalidateVisual();
    }

    /// <summary>Gate of a step.</summary>
    /// <param name="step">Step index.</param>
    /// <returns><c>true</c> if the step plays.</returns>
    public bool GetGate(int step) => GateParameter(step) is { } p ? p.NormalizedValue >= 0.5 : _gates[step];

    /// <summary>Sets a gate (programmatic).</summary>
    /// <param name="step">Step index.</param>
    /// <param name="on">New state.</param>
    public void SetGate(int step, bool on)
    {
        if (GateParameter(step) is { } p) p.SetValue(on ? 1.0 : 0.0);
        else _gates[step] = on;
        InvalidateVisual();
    }

    private IParameter? ValueParameter(int step) => ValueParameters is { } list && step < list.Count ? list[step] : null;

    private IParameter? GateParameter(int step) => GateParameters is { } list && step < list.Count ? list[step] : null;

    private double Quantize(double v)
    {
        v = Math.Clamp(v, 0.0, 1.0);
        return ValueSteps > 0 ? Math.Round(v * ValueSteps) / ValueSteps : v;
    }

    // ---------------------------------------------------------------- geometry

    private void Lanes(Rect widget, out Rect values, out Rect gates)
    {
        var r = widget.Deflate(2f);
        float gateH = ShowGates ? MathF.Min(18f, r.Height * 0.2f) : 0f;
        values = new Rect(r.X, r.Y, r.Width, r.Height - gateH - (ShowGates ? 4f : 0f));
        gates = new Rect(r.X, r.Bottom - gateH, r.Width, gateH);
    }

    private int StepAt(float x, Rect lane) => Math.Clamp((int)((x - lane.X) / MathF.Max(1f, lane.Width) * StepCount), 0, StepCount - 1);

    // ---------------------------------------------------------------- live updates

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, _ =>
        {
            if (CurrentStepSource is { } s) CurrentStep = s();
            if (CurrentStep != _lastPlay) { _lastPlay = CurrentStep; InvalidateVisual(); }
        }));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        EndPaint();
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        Lanes(widget, out var lane, out var gateLane);
        int n = StepCount;
        float w = lane.Width / n;
        var accent = style.Accent;
        for (int i = 0; i < n; i++)
        {
            var cell = new Rect(lane.X + i * w, lane.Y, w, lane.Height);
            bool active = i < _length;
            var background = GroupSize > 0 && i / GroupSize % 2 == 1 ? p.Surface.Lighten(0.04f) : p.Surface;
            dc.FillRect(cell.Deflate(new Thickness(0.5f, 0f)), background.WithAlpha(active ? 1f : 0.5f));
            if (i == CurrentStep) dc.FillRect(cell.Deflate(new Thickness(0.5f, 0f)), accent.WithAlpha(0.15f));
            double v = GetValue(i);
            bool gate = GetGate(i);
            float baseY = IsBipolar ? lane.Center.Y : lane.Bottom;
            float y = lane.Bottom - (float)v * lane.Height;
            var bar = Rect.FromEdges(cell.X + 2f, MathF.Min(y, baseY), cell.Right - 2f, MathF.Max(y, baseY));
            var barColor = (gate ? accent : p.TextDim).WithAlpha(active ? (i == CurrentStep ? 1f : 0.8f) : 0.3f);
            dc.FillRect(bar, barColor);
            dc.FillRect(new Rect(bar.X, y - 1f, bar.Width, 2f), barColor.Lighten(0.3f));
            if (ShowGates)
            {
                var g = new Rect(lane.X + i * w + 2f, gateLane.Y, w - 4f, gateLane.Height);
                float radius = MathF.Min(3f, g.Height * 0.3f);
                dc.FillRoundedRect(g, radius, gate ? accent.WithAlpha(active ? 0.9f : 0.35f) : p.Track.WithAlpha(active ? 1f : 0.5f));
                if (i == CurrentStep) dc.DrawRoundedRect(g, radius, p.Text, 1.5f);
            }
        }
        if (IsBipolar) dc.DrawLine(new Point(lane.X, lane.Center.Y), new Point(lane.Right, lane.Center.Y), p.Border, 1f);
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        Lanes(Bounds, out var lane, out var gateLane);
        if (ShowGates && e.Position.Y >= gateLane.Y - 2f)
        {
            int step = StepAt(e.Position.X, gateLane);
            _gateState = !GetGate(step);
            _gatePainting = true;
            PaintGate(step);
            e.Handled = true;
            return;
        }
        int s = StepAt(e.Position.X, lane);
        if (e.ClickCount == 2)
        {
            WriteValue(s, DefaultStepValue);
            EndPaint();
            e.Handled = true;
            return;
        }
        _painting = true;
        _lastStep = s;
        _lastValue = ValueAt(e.Position.Y, lane);
        WriteValue(s, _lastValue);
        e.Handled = true;
    }

    private double ValueAt(float y, Rect lane) => Math.Clamp((lane.Bottom - y) / MathF.Max(1f, lane.Height), 0.0, 1.0);

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Lanes(Bounds, out var lane, out var gateLane);
        if (_gatePainting)
        {
            PaintGate(StepAt(e.Position.X, gateLane));
            return;
        }
        if (!_painting) return;
        int s = StepAt(e.Position.X, lane);
        double v = ValueAt(e.Position.Y, lane);
        // Interpolate across the steps skipped by a fast movement.
        int from = _lastStep, to = s;
        int dir = Math.Sign(to - from);
        for (int i = from; ; i += dir)
        {
            double t = to == from ? 1.0 : (i - from) / (double)(to - from);
            WriteValue(i, _lastValue + (v - _lastValue) * t);
            if (i == to || dir == 0) break;
        }
        _lastStep = s;
        _lastValue = v;
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left) EndPaint();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndPaint();
    }

    private void WriteValue(int step, double value)
    {
        value = Quantize(value);
        if (ValueParameter(step) is { } p)
        {
            if (_touched.Add(p)) p.BeginEdit();
            if (p.NormalizedValue != value) p.PerformEdit(value);
        }
        else
        {
            if (_values[step] == value) return;
            _values[step] = value;
        }
        StepChanged?.Invoke(this, step);
        InvalidateVisual();
    }

    private void PaintGate(int step)
    {
        if (GetGate(step) == _gateState) return;
        if (GateParameter(step) is { } p)
        {
            if (_touched.Add(p)) p.BeginEdit();
            p.PerformEdit(_gateState ? 1.0 : 0.0);
        }
        else
        {
            _gates[step] = _gateState;
        }
        GateChanged?.Invoke(this, step);
        InvalidateVisual();
    }

    private void EndPaint()
    {
        _painting = false;
        _gatePainting = false;
        foreach (var p in _touched) p.EndEdit();
        _touched.Clear();
    }
}
