// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Row of bars edited by drawing across them (step sequencer, harmonic levels, wave tables, graphic EQ).
/// Drag paints values with line interpolation so fast strokes never skip bars; double-click resets a bar,
/// Ctrl+drag draws straight lines from the press point. Values are local or bound to a list of parameters.
/// </summary>
public class MultiSlider : Control
{
    private BoundValue[] _bars = [];
    private int _highlight = -1;
    private int _hover = -1;
    private bool _drawing;
    private Point _last;
    private Point _start;
    private double[] _lineBase = [];
    private int _steps;
    private bool _bipolar;

    /// <summary>Creates a multi-slider with <paramref name="count"/> bars.</summary>
    public MultiSlider(int count = 16)
    {
        Count = count;
        Cursor = StandardCursor.Cross;
    }

    /// <summary>Number of bars (setting it keeps existing local values, drops parameter bindings).</summary>
    [Category("Behavior")]
    public int Count
    {
        get => _bars.Length;
        set
        {
            value = Math.Clamp(value, 1, 4096);
            if (value == _bars.Length && _bars.All(b => b.Parameter is null)) return;
            var old = _bars;
            foreach (var b in old) b.Detach();
            _bars = new BoundValue[value];
            for (int i = 0; i < value; i++)
            {
                _bars[i] = new BoundValue(OnBarChanged, i < old.Length && old[i].Parameter is null ? old[i].Value : DefaultValue, DefaultValue) { StepCount = _steps };
                if (Host is not null) _bars[i].Attach();
            }
            InvalidateVisual();
        }
    }

    /// <summary>Binds one parameter per bar (replaces <see cref="Count"/>).</summary>
    public void BindParameters(IReadOnlyList<IParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var b in _bars) b.Detach();
        _bars = new BoundValue[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            _bars[i] = new BoundValue(OnBarChanged) { Parameter = parameters[i] };
            if (Host is not null) _bars[i].Attach();
        }
        InvalidateVisual();
    }

    /// <summary>Parameter bound to a bar (null when local).</summary>
    public IParameter? GetParameter(int index) => _bars[index].Parameter;

    /// <summary>Value of a bar (normalized).</summary>
    public double this[int index]
    {
        get => _bars[index].Value;
        set => _bars[index].SetByUser(value);
    }

    /// <summary>Copies all values (normalized).</summary>
    public double[] GetValues() => _bars.Select(b => b.Value).ToArray();

    /// <summary>Sets all values (normalized) at once.</summary>
    public void SetValues(ReadOnlySpan<double> values)
    {
        for (int i = 0; i < Math.Min(values.Length, _bars.Length); i++) _bars[i].SetByUser(values[i]);
    }

    /// <summary>Default value of unbound bars (reset).</summary>
    [Category("Value")]
    public double DefaultValue { get; set; }

    /// <summary>Discrete steps for unbound bars (0 = continuous); a grid is drawn when &gt; 0.</summary>
    [Category("Value")]
    public int StepCount
    {
        get => _steps;
        set
        {
            _steps = Math.Max(0, value);
            foreach (var b in _bars) b.StepCount = _steps;
            InvalidateVisual();
        }
    }

    /// <summary>Bars grow up and down from the middle.</summary>
    [Category("Value")]
    public bool IsBipolar { get => _bipolar; set { _bipolar = value; InvalidateVisual(); } }

    /// <summary>Gap between bars in DIPs.</summary>
    [Category("Layout")]
    public float BarSpacing { get; set; } = 2f;

    /// <summary>Highlighted bar (playhead of a sequencer; −1 = none).</summary>
    [Category("Value")]
    public int HighlightedIndex { get => _highlight; set { if (_highlight != value) { _highlight = value; InvalidateVisual(); } } }

    /// <summary>Raised when a bar changes (index, normalized value).</summary>
    public event Action<MultiSlider, int, double>? ValueChanged;

    /// <summary>Raised when a drawing gesture starts.</summary>
    public event Action<MultiSlider>? GestureStarted;

    /// <summary>Raised when a drawing gesture ends.</summary>
    public event Action<MultiSlider>? GestureEnded;

    /// <inheritdoc />
    public override string? ValueText
    {
        get
        {
            int i = _hover;
            return i >= 0 && i < _bars.Length ? $"{i + 1}: {_bars[i].Text}" : null;
        }
    }

    /// <inheritdoc />
    protected override Size DefaultSize => new(260f, 120f);

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        // Reserve the value line permanently so the layout does not jump while hovering.
        var r = Bounds;
        label = Rect.Empty;
        value = Rect.Empty;
        if (style.ShowLabel && !string.IsNullOrEmpty(Label))
        {
            float h = style.LabelText.Size * 1.5f;
            label = r.TakeTop(h);
            r = Rect.FromEdges(r.X, r.Y + h, r.Right, r.Bottom);
        }
        if (style.ShowValue)
        {
            float h = style.ValueText.Size * 1.5f;
            value = r.TakeBottom(h);
            r = Rect.FromEdges(r.X, r.Y, r.Right, r.Bottom - h);
        }
        widget = r;
    }

    private void OnBarChanged(BoundValue bar)
    {
        int index = Array.IndexOf(_bars, bar);
        if (index >= 0) ValueChanged?.Invoke(this, index, bar.Value);
        InvalidateVisual();
    }

    private Rect WidgetRect()
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        return widget.Inflate(-2f);
    }

    private int IndexAtX(Rect area, float x) => Math.Clamp((int)((x - area.X) / MathF.Max(1e-3f, area.Width) * _bars.Length), 0, _bars.Length - 1);

    private static double ValueAtY(Rect area, float y) => Math.Clamp((area.Bottom - y) / MathF.Max(1f, area.Height), 0f, 1f);

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = MathF.Min(4f, CornerRadiusFor(widget, style));
        dc.FillRoundedRect(widget, radius, style.Background);
        var area = widget.Inflate(-2f);
        int n = _bars.Length;
        if (n == 0) return;

        if (_steps > 0 && _steps <= 32)
        {
            var g = p.Border.Fade(0.45f);
            for (int s = 1; s < _steps; s++)
            {
                float y = MathF.Round(area.Bottom - area.Height * s / _steps) + 0.5f;
                dc.DrawLine(new Point(area.X, y), new Point(area.Right, y), g);
            }
        }
        if (_bipolar)
        {
            float y = MathF.Round(area.Center.Y) + 0.5f;
            dc.DrawLine(new Point(area.X, y), new Point(area.Right, y), p.Border);
        }

        float slot = area.Width / n;
        float gap = MathF.Min(BarSpacing, slot * 0.4f);
        for (int i = 0; i < n; i++)
        {
            float x0 = area.X + slot * i + gap * 0.5f;
            float w = MathF.Max(1f, slot - gap);
            float v = (float)_bars[i].Value;
            float yv = area.Bottom - area.Height * v;
            float y0 = _bipolar ? area.Center.Y : area.Bottom;
            var bar = Rect.FromEdges(x0, MathF.Min(yv, y0), x0 + w, MathF.Max(yv, y0));
            var color = i == _highlight ? style.Accent.Lighten(0.25f) : i == _hover ? style.Accent.Lighten(0.1f) : style.Accent.Fade(0.85f);
            if (i == _highlight) dc.FillRect(new Rect(x0, area.Y, w, area.Height), style.Accent.Fade(0.12f));
            if (bar.Height > 0f) dc.FillRect(bar, color.Fade(0.55f));
            // Bright cap at the value.
            dc.FillRect(new Rect(x0, yv - 1f, w, 2f), color);
        }
        if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, style.Frame);
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled || _bars.Length == 0) return;
        e.Handled = true;
        var area = WidgetRect();
        int index = IndexAtX(area, e.Position.X);
        if (e.ClickCount == 2)
        {
            _bars[index].SetByUser(_bars[index].DefaultValue);
            return;
        }
        _drawing = true;
        IsPressed = true;
        _start = _last = e.Position;
        _lineBase = GetValues();
        GestureStarted?.Invoke(this);
        PaintBars(area, index, index, ValueAtY(area, e.Position.Y), ValueAtY(area, e.Position.Y));
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var area = WidgetRect();
        int hover = IsMouseOver || _drawing ? IndexAtX(area, e.Position.X) : -1;
        if (hover != _hover) { _hover = hover; InvalidateVisual(); }
        if (!_drawing) return;
        if (e.Control)
        {
            // Straight line from the press point: restore then draw the line.
            for (int i = 0; i < _bars.Length && i < _lineBase.Length; i++) { _bars[i].Begin(); _bars[i].Set(_lineBase[i]); }
            Line(area, _start, e.Position);
        }
        else
        {
            Line(area, _last, e.Position);
        }
        _last = e.Position;
    }

    private void Line(Rect area, Point from, Point to)
    {
        int i0 = IndexAtX(area, from.X), i1 = IndexAtX(area, to.X);
        PaintBars(area, i0, i1, ValueAtY(area, from.Y), ValueAtY(area, to.Y));
    }

    private void PaintBars(Rect area, int i0, int i1, double v0, double v1)
    {
        int lo = Math.Min(i0, i1), hi = Math.Max(i0, i1);
        for (int i = lo; i <= hi; i++)
        {
            double t = hi == lo ? 1.0 : (i - i0) / (double)(i1 - i0);
            var bar = _bars[i];
            bar.Begin();
            bar.Set(v0 + (v1 - v0) * t);
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrawing();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrawing();
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (!_drawing) _hover = -1;
    }

    private void EndDrawing()
    {
        if (!_drawing) return;
        _drawing = false;
        IsPressed = false;
        foreach (var b in _bars) b.End();
        GestureEnded?.Invoke(this);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled || _hover < 0 || _hover >= _bars.Length) return;
        var bar = _bars[_hover];
        double step = bar.StepCount > 0 ? Math.Sign(e.WheelDelta) / (double)bar.StepCount : e.WheelDelta * (e.Shift ? 0.002 : 0.02);
        bar.SetByUser(bar.Value + step);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        foreach (var b in _bars) b.Attach();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        _drawing = false;
        foreach (var b in _bars) b.Detach();
        base.OnDetached();
    }
}
