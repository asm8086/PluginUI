// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Row or grid of on/off cells, each bound to its own parameter (or to a local state array): step-sequencer gate
/// rows, channel enables, harmonic switches. Press a cell to flip it and drag across the others to paint the same
/// state. <see cref="PlayPosition"/> highlights the current step.
/// </summary>
public class MultiToggle : Control
{
    private bool[] _local;
    private IReadOnlyList<IParameter>? _parameters;
    private readonly HashSet<int> _editing = new();
    private bool _paintState;
    private int _lastCell = -1;
    private int _hover = -1;
    private int _playPosition = -1;
    private bool _subscribed;

    /// <summary>Creates a multi-toggle with <paramref name="count"/> local cells.</summary>
    public MultiToggle(int count = 16)
    {
        _local = new bool[Math.Max(1, count)];
    }

    /// <summary>Creates a multi-toggle bound to parameters (one cell per parameter).</summary>
    public MultiToggle(IReadOnlyList<IParameter> parameters) : this(parameters.Count) => Parameters = parameters;

    /// <summary>Bound parameters (null = local states).</summary>
    [Browsable(false)]
    public IReadOnlyList<IParameter>? Parameters
    {
        get => _parameters;
        set
        {
            Unsubscribe();
            EndAllEdits();
            _parameters = value;
            if (value is not null && value.Count != _local.Length) _local = new bool[Math.Max(1, value.Count)];
            if (Host is not null) Subscribe();
            InvalidateVisual();
        }
    }

    /// <summary>Number of cells.</summary>
    [Category("Behavior")]
    public int Count
    {
        get => _parameters?.Count ?? _local.Length;
        set
        {
            if (_parameters is not null) throw new InvalidOperationException("Count follows the bound parameters.");
            Array.Resize(ref _local, Math.Max(1, value));
            InvalidateVisual();
        }
    }

    /// <summary>Columns per row (0 = single row).</summary>
    [Category("Behavior")]
    public int Columns { get; set; }

    /// <summary>Space between cells in DIPs.</summary>
    [Category("Layout")]
    public float CellSpacing { get; set; } = 3f;

    /// <summary>Highlights every n-th column more strongly (beats in a 16-step row). 0 = off.</summary>
    [Category("Layout")]
    public int GroupSize { get; set; } = 4;

    /// <summary>Index of the playing step (−1 = none).</summary>
    [Category("Value")]
    public int PlayPosition
    {
        get => _playPosition;
        set
        {
            if (_playPosition == value) return;
            _playPosition = value;
            InvalidateVisual();
        }
    }

    /// <summary>Raised when the user changes a cell.</summary>
    public event Action<MultiToggle, int, bool>? CellChanged;

    /// <summary>State of a cell.</summary>
    public bool this[int index]
    {
        get => _parameters is { } ps ? ps[index].NormalizedValue >= 0.5 : _local[index];
        set
        {
            if (_parameters is { } ps) ps[index].SetValue(value ? 1.0 : 0.0);
            else _local[index] = value;
            InvalidateVisual();
        }
    }

    private int ColumnCount => Columns > 0 ? Math.Min(Columns, Count) : Count;

    private int RowCount => (int)Math.Ceiling(Count / (double)ColumnCount);

    /// <inheritdoc />
    protected override Size DefaultSize => new(ColumnCount * 18f + (ColumnCount - 1) * CellSpacing, RowCount * 18f + (RowCount - 1) * CellSpacing + ControlSizing.LabelAndValueHeight(this));

    private Rect CellRect(Rect widget, int index)
    {
        int cols = ColumnCount, rows = RowCount;
        float w = (widget.Width - (cols - 1) * CellSpacing) / cols;
        float h = (widget.Height - (rows - 1) * CellSpacing) / rows;
        int r = index / cols, c = index % cols;
        return new Rect(widget.X + c * (w + CellSpacing), widget.Y + r * (h + CellSpacing), w, h);
    }

    private int CellAt(Point p)
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        int cols = ColumnCount, rows = RowCount;
        float cw = (widget.Width + CellSpacing) / cols, ch = (widget.Height + CellSpacing) / rows;
        int c = (int)MathF.Floor((p.X - widget.X) / cw), r = (int)MathF.Floor((p.Y - widget.Y) / ch);
        if (c < 0 || r < 0 || c >= cols || r >= rows) return -1;
        int index = r * cols + c;
        return index < Count ? index : -1;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        bool enabled = IsEffectivelyEnabled;
        var accent = enabled ? style.Accent : p.TextDisabled;
        int cols = ColumnCount;
        for (int i = 0; i < Count; i++)
        {
            var r = CellRect(widget, i);
            float radius = MathF.Min(3f, MathF.Min(r.Width, r.Height) * 0.25f);
            bool on = this[i];
            bool groupStart = GroupSize > 0 && (i % cols) / GroupSize % 2 == 1;
            var off = groupStart ? style.Background.Lighten(0.05f) : style.Background;
            dc.FillRoundedRect(r, radius, on ? accent : off);
            if (on) dc.FillRoundedRect(r.TakeTop(r.Height * 0.45f).Inflate(-1.5f, -1f), radius, Color.White.WithAlpha(0.12f));
            dc.DrawRoundedRect(r.Inflate(-0.5f), radius, on ? accent.Darken(0.25f) : style.Frame, 1f);
            if (i == _hover && IsMouseOver && enabled) dc.FillRoundedRect(r, radius, style.Hover);
            if (i == _playPosition)
            {
                dc.DrawRoundedRect(r.Inflate(1f), radius + 1f, p.AccentAlt, 1.5f);
            }
        }
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        int cell = CellAt(e.Position);
        if (cell < 0) return;
        e.Handled = true;
        IsPressed = true;
        _paintState = !this[cell];
        _lastCell = -1;
        PaintCell(cell);
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int cell = CellAt(e.Position);
        if (IsPressed && IsMouseCaptured && cell >= 0)
        {
            // Fill the cells skipped by fast moves (same row only).
            int from = _lastCell;
            if (from >= 0 && from / ColumnCount == cell / ColumnCount)
            {
                int step = Math.Sign(cell - from);
                for (int i = from + step; i != cell; i += step) { _lastCell = -1; PaintCell(i); }
            }
            PaintCell(cell);
        }
        if (cell != _hover)
        {
            _hover = cell;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        IsPressed = false;
        EndAllEdits();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        IsPressed = false;
        EndAllEdits();
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = -1;
    }

    private void PaintCell(int cell)
    {
        if (cell == _lastCell) return;
        _lastCell = cell;
        if (this[cell] == _paintState) return;
        if (_parameters is { } ps)
        {
            var param = ps[cell];
            if (_editing.Add(cell)) param.BeginEdit();
            param.PerformEdit(_paintState ? 1.0 : 0.0);
        }
        else
        {
            _local[cell] = _paintState;
        }
        CellChanged?.Invoke(this, cell, _paintState);
        InvalidateVisual();
    }

    private void EndAllEdits()
    {
        if (_parameters is { } ps)
        {
            foreach (int i in _editing)
            {
                if (i < ps.Count) ps[i].EndEdit();
            }
        }
        _editing.Clear();
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
        EndAllEdits();
        Unsubscribe();
        base.OnDetached();
    }

    private void Subscribe()
    {
        if (_subscribed || _parameters is null) return;
        foreach (var p in _parameters) p.Changed += OnParameterChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _parameters is null) return;
        foreach (var p in _parameters) p.Changed -= OnParameterChanged;
        _subscribed = false;
    }

    private void OnParameterChanged(IParameter parameter) => InvalidateVisual();

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (e.Key is Key.Left or Key.Right)
        {
            _hover = Math.Clamp((_hover < 0 ? 0 : _hover) + (e.Key == Key.Right ? 1 : -1), 0, Count - 1);
            e.Handled = true;
            InvalidateVisual();
        }
        else if (e.Key is Key.Space or Key.Enter && _hover >= 0)
        {
            bool state = !this[_hover];
            this[_hover] = state;
            CellChanged?.Invoke(this, _hover, state);
            e.Handled = true;
        }
    }
}
