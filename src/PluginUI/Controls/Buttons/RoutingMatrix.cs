// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Numerics;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>How many connections a <see cref="RoutingMatrix"/> allows.</summary>
public enum RoutingMode
{
    /// <summary>Any input to any number of outputs (a mixing patch bay).</summary>
    Free,
    /// <summary>Each output takes at most one input (a channel router, a pin connector).</summary>
    OneInputPerOutput,
    /// <summary>Each input goes to at most one output.</summary>
    OneOutputPerInput,
}

/// <summary>
/// Routing grid: inputs as rows, outputs as columns and a switch at each crossing, for channel routers, pin connectors,
/// send matrices and patch bays. Each crossing can be bound to a parameter.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Click a crossing to connect or disconnect it; drag across a row or a column to set several (the
/// first crossing decides). The row and the column under the mouse are highlighted.</para>
/// <para><b>Keyboard.</b> The arrows move the cursor, Home / End go to the first or last output of the row, Space or
/// Enter switches the crossing.</para>
/// <para><b>Screen readers.</b> Each crossing is a check box named after its input and output.</para>
/// </remarks>
/// <example>
/// <code>
/// var router = new RoutingMatrix { Mode = RoutingMode.OneInputPerOutput };
/// router.Inputs.AddRange(["In L", "In R", "Side L", "Side R"]);
/// router.Outputs.AddRange(["Out L", "Out R"]);
/// router.ConnectionChanged += (m, input, output, on) => engine.Route(input, output, on);
/// </code>
/// </example>
public class RoutingMatrix : Control
{
    private bool[,] _connections = new bool[0, 0];
    private int _row, _column;
    private int _hoverRow = -1, _hoverColumn = -1;
    private bool _painting;
    private bool _paintState;
    private int _paintRow = -1, _paintColumn = -1;

    /// <summary>Creates an empty matrix.</summary>
    public RoutingMatrix()
    {
        Focusable = true;
        Cursor = StandardCursor.Hand;
    }

    /// <summary>Names of the inputs (rows). After changing them, call <see cref="UIElement.InvalidateVisual"/>.</summary>
    [Browsable(false)]
    public List<string> Inputs { get; } = new();

    /// <summary>Names of the outputs (columns).</summary>
    [Browsable(false)]
    public List<string> Outputs { get; } = new();

    /// <summary>How many connections are allowed.</summary>
    [Category("Behavior")]
    public RoutingMode Mode { get; set; } = RoutingMode.Free;

    /// <summary>Largest size of a crossing in DIPs (the grid is centred when there is room left).</summary>
    [Category("Layout")]
    public float MaxCellSize { get; set; } = 30f;

    /// <summary>
    /// Parameter of a crossing (input, output), on above 0.5, or <c>null</c> to store the connection here. Every change is
    /// a complete edit gesture.
    /// </summary>
    [Browsable(false)]
    public Func<int, int, IParameter?>? ParameterOf { get; set; }

    /// <summary>Input of the keyboard cursor.</summary>
    [Browsable(false)]
    public int CurrentInput => _row;

    /// <summary>Output of the keyboard cursor.</summary>
    [Browsable(false)]
    public int CurrentOutput => _column;

    /// <summary>Raised after the user changed a crossing (input, output, connected), including the ones the mode cleared.</summary>
    public event Action<RoutingMatrix, int, int, bool>? ConnectionChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(320f, 220f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new RoutingMatrixAutomationPeer(this);

    /// <summary><c>true</c> when the input goes to the output.</summary>
    public bool IsConnected(int input, int output)
    {
        if (ParameterOf?.Invoke(input, output) is { } p) return p.NormalizedValue >= 0.5;
        return input >= 0 && output >= 0 && input < _connections.GetLength(0) && output < _connections.GetLength(1) && _connections[input, output];
    }

    /// <summary>Connects or disconnects a crossing (programmatic: no event; the mode is not applied).</summary>
    public void SetConnected(int input, int output, bool connected)
    {
        if (ParameterOf?.Invoke(input, output) is { } p)
        {
            p.BeginEdit();
            p.PerformEdit(connected ? 1.0 : 0.0);
            p.EndEdit();
        }
        else
        {
            EnsureStorage();
            _connections[input, output] = connected;
        }
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void EnsureStorage()
    {
        int rows = Inputs.Count, columns = Outputs.Count;
        if (_connections.GetLength(0) == rows && _connections.GetLength(1) == columns) return;
        var resized = new bool[rows, columns];
        for (int r = 0; r < Math.Min(rows, _connections.GetLength(0)); r++)
        {
            for (int c = 0; c < Math.Min(columns, _connections.GetLength(1)); c++) resized[r, c] = _connections[r, c];
        }
        _connections = resized;
    }

    // ---------------------------------------------------------------- geometry

    private TextStyle LabelStyle => Theme.Typography.Small with { Size = 10f, VerticalAlign = VerticalAlignment.Center, Ellipsis = true };

    /// <summary>Row labels on the left, column labels on top (turned when they are wider than a crossing), the grid.</summary>
    private void Layout(Rect widget, out Rect grid, out float cell, out float rowLabels, out float header, out bool turned)
    {
        var area = widget.Deflate(2f);
        var style = LabelStyle;
        float widestRow = 0f, widestColumn = 0f;
        foreach (var s in Inputs) widestRow = MathF.Max(widestRow, TextMeasurer.Measure(s, style).Width);
        foreach (var s in Outputs) widestColumn = MathF.Max(widestColumn, TextMeasurer.Measure(s, style).Width);
        rowLabels = MathF.Min(area.Width * 0.4f, widestRow + 12f);
        int rows = Math.Max(1, Inputs.Count), columns = Math.Max(1, Outputs.Count);
        float flatHeader = 18f;
        cell = MathF.Min(MaxCellSize, MathF.Min((area.Width - rowLabels) / columns, (area.Height - flatHeader) / rows));
        turned = widestColumn + 4f > cell;
        header = turned ? MathF.Min(area.Height * 0.4f, widestColumn + 10f) : flatHeader;
        if (turned) cell = MathF.Min(MaxCellSize, MathF.Min((area.Width - rowLabels) / columns, (area.Height - header) / rows));
        cell = MathF.Max(4f, cell);
        float gridWidth = cell * columns, gridHeight = cell * rows;
        float x = area.X + rowLabels + MathF.Max(0f, (area.Width - rowLabels - gridWidth) * 0.5f);
        float y = area.Y + header + MathF.Max(0f, (area.Height - header - gridHeight) * 0.5f);
        grid = new Rect(x, y, gridWidth, gridHeight);
    }

    /// <summary>Bounds of a crossing (host DIPs).</summary>
    internal Rect CellRect(int input, int output)
    {
        Layout(Bounds, out var grid, out float cell, out _, out _, out _);
        return new Rect(grid.X + output * cell, grid.Y + input * cell, cell, cell);
    }

    private (int Row, int Column) CellAt(Point p)
    {
        Layout(Bounds, out var grid, out float cell, out _, out _, out _);
        if (!grid.Contains(p)) return (-1, -1);
        int r = (int)((p.Y - grid.Y) / cell), c = (int)((p.X - grid.X) / cell);
        return r < Inputs.Count && c < Outputs.Count ? (r, c) : (-1, -1);
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        Layout(widget, out var grid, out float cell, out float rowLabels, out float header, out bool turned);
        var label = LabelStyle;
        int rows = Inputs.Count, columns = Outputs.Count;

        for (int r = 0; r < rows; r++)
        {
            var band = new Rect(grid.X, grid.Y + r * cell, grid.Width, cell);
            if (r % 2 == 0) dc.FillRect(band, p.Hover);
            if (r == _hoverRow) dc.FillRect(band, style.Accent.WithAlpha(0.08f));
            var text = Rect.FromEdges(grid.X - rowLabels, band.Y, grid.X - 6f, band.Bottom);
            dc.DrawText(Inputs[r], text, label with { Align = HorizontalAlignment.Right }, r == _hoverRow || r == _row && IsFocused ? p.Text : p.TextDim);
        }
        for (int c = 0; c < columns; c++)
        {
            var band = new Rect(grid.X + c * cell, grid.Y, cell, grid.Height);
            if (c == _hoverColumn) dc.FillRect(band, style.Accent.WithAlpha(0.08f));
            var color = c == _hoverColumn || c == _column && IsFocused ? p.Text : p.TextDim;
            if (turned)
            {
                // Turned a quarter left, reading upwards, ending just above its column.
                var anchor = new Point(band.Center.X, grid.Y - 4f);
                using (dc.Transform(Matrix3x2.CreateRotation(-MathF.PI / 2f, anchor.ToVector())))
                {
                    dc.DrawText(Outputs[c], new Rect(anchor.X, anchor.Y - cell * 0.5f, header - 6f, cell), label, color);
                }
            }
            else
            {
                dc.DrawText(Outputs[c], new Rect(band.X - 10f, grid.Y - header, cell + 20f, header - 2f), label with { Align = HorizontalAlignment.Center }, color);
            }
        }

        // Grid lines and the crossings.
        for (int r = 0; r <= rows; r++) dc.DrawLine(new Point(grid.X, grid.Y + r * cell), new Point(grid.Right, grid.Y + r * cell), p.Border.WithAlpha(0.6f), 1f);
        for (int c = 0; c <= columns; c++) dc.DrawLine(new Point(grid.X + c * cell, grid.Y), new Point(grid.X + c * cell, grid.Bottom), p.Border.WithAlpha(0.6f), 1f);
        float radius = MathF.Max(2f, cell * 0.28f);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                var center = new Point(grid.X + (c + 0.5f) * cell, grid.Y + (r + 0.5f) * cell);
                if (IsConnected(r, c))
                {
                    dc.FillCircle(center, radius, style.Accent);
                    dc.DrawCircle(center, radius, style.Accent.Lighten(0.25f), 1f);
                }
                else
                {
                    dc.FillCircle(center, MathF.Max(1.5f, radius * 0.25f), p.TextDim.WithAlpha(r == _hoverRow && c == _hoverColumn ? 0.9f : 0.4f));
                    if (r == _hoverRow && c == _hoverColumn) dc.DrawCircle(center, radius, style.Accent.WithAlpha(0.6f), 1f);
                }
                if (IsFocused && r == _row && c == _column && Host?.ShowFocusRings == true)
                {
                    dc.DrawRoundedRect(new Rect(grid.X + c * cell + 1.5f, grid.Y + r * cell + 1.5f, cell - 3f, cell - 3f), 3f, p.Focus, 1.2f);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        // Focus is shown on the current crossing instead of the default ring.
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (r, c) = CellAt(e.Position);
        if (_painting && r >= 0 && (r != _paintRow || c != _paintColumn))
        {
            _paintRow = r;
            _paintColumn = c;
            if (IsConnected(r, c) != _paintState) Toggle(r, c);
        }
        if (r != _hoverRow || c != _hoverColumn)
        {
            _hoverRow = r;
            _hoverColumn = c;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hoverRow = _hoverColumn = -1;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left) return;
        var (r, c) = CellAt(e.Position);
        if (r < 0) return;
        e.Handled = true;
        _row = r;
        _column = c;
        _paintState = !IsConnected(r, c);
        _painting = true;
        _paintRow = r;
        _paintColumn = c;
        Toggle(r, c);
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _painting = false;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _painting = false;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled || Inputs.Count == 0 || Outputs.Count == 0) return;
        switch (e.Key)
        {
            case Key.Up: _row = Math.Max(0, _row - 1); break;
            case Key.Down: _row = Math.Min(Inputs.Count - 1, _row + 1); break;
            case Key.Left: _column = Math.Max(0, _column - 1); break;
            case Key.Right: _column = Math.Min(Outputs.Count - 1, _column + 1); break;
            case Key.Home: _column = 0; break;
            case Key.End: _column = Outputs.Count - 1; break;
            case Key.Space or Key.Enter: Toggle(_row, _column); break;
            default: return;
        }
        _row = Math.Clamp(_row, 0, Inputs.Count - 1);
        _column = Math.Clamp(_column, 0, Outputs.Count - 1);
        e.Handled = true;
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary>Switches a crossing as a click does, applying <see cref="Mode"/>.</summary>
    internal void Toggle(int input, int output)
    {
        bool on = !IsConnected(input, output);
        if (on && Mode == RoutingMode.OneInputPerOutput)
        {
            for (int r = 0; r < Inputs.Count; r++) if (r != input && IsConnected(r, output)) Set(r, output, false);
        }
        else if (on && Mode == RoutingMode.OneOutputPerInput)
        {
            for (int c = 0; c < Outputs.Count; c++) if (c != output && IsConnected(input, c)) Set(input, c, false);
        }
        Set(input, output, on);
    }

    private void Set(int input, int output, bool on)
    {
        SetConnected(input, output, on);
        ConnectionChanged?.Invoke(this, input, output, on);
    }
}

/// <summary>Peer of a <see cref="RoutingMatrix"/>: a group of check boxes, one per crossing; the focus is on the current one.</summary>
/// <param name="owner">The matrix.</param>
public class RoutingMatrixAutomationPeer(RoutingMatrix owner) : ElementAutomationPeer<RoutingMatrix>(owner)
{
    private readonly Dictionary<(int, int), RoutingCellAutomationPeer> _cells = new();
    private (int, int) _reported = (-1, -1);

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationRoutingMatrix;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    /// <summary>Peer of a crossing.</summary>
    public RoutingCellAutomationPeer GetCell(int input, int output)
    {
        if (!_cells.TryGetValue((input, output), out var peer)) _cells[(input, output)] = peer = new RoutingCellAutomationPeer(this, input, output);
        return peer;
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        for (int r = 0; r < Owner.Inputs.Count; r++)
        {
            for (int c = 0; c < Owner.Outputs.Count; c++) children.Add(GetCell(r, c));
        }
    }

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
        => Owner.CurrentInput < Owner.Inputs.Count && Owner.CurrentOutput < Owner.Outputs.Count ? GetCell(Owner.CurrentInput, Owner.CurrentOutput) : this;

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        foreach (var cell in _cells.Values) cell.Report();
        var current = (Owner.CurrentInput, Owner.CurrentOutput);
        if (current == _reported) return;
        _reported = current;
        Host?.UpdateAutomationFocus();
    }
}

/// <summary>Peer of a crossing of a <see cref="RoutingMatrix"/>: a check box "input → output".</summary>
public sealed class RoutingCellAutomationPeer : ItemAutomationPeer, ITogglePattern
{
    private readonly RoutingMatrixAutomationPeer _matrix;
    private ToggleState? _reported;

    internal RoutingCellAutomationPeer(RoutingMatrixAutomationPeer matrix, int input, int output) : base(matrix)
    {
        _matrix = matrix;
        Input = input;
        Output = output;
    }

    /// <summary>Input (row).</summary>
    public int Input { get; }

    /// <summary>Output (column).</summary>
    public int Output { get; }

    private RoutingMatrix Matrix => _matrix.Owner;

    /// <inheritdoc />
    protected override bool Exists => Input < Matrix.Inputs.Count && Output < Matrix.Outputs.Count;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.CheckBox;

    /// <inheritdoc />
    public override string Name => Exists ? Matrix.Inputs[Input] + " → " + Matrix.Outputs[Output] : string.Empty;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => _matrix.IsEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Exists ? Matrix.CellRect(Input, Output) : Rect.Empty;

    /// <inheritdoc />
    public override int PositionInSet => Input * Matrix.Outputs.Count + Output + 1;

    /// <inheritdoc />
    public override int SizeOfSet => Matrix.Inputs.Count * Matrix.Outputs.Count;

    /// <inheritdoc />
    public ToggleState ToggleState => Matrix.IsConnected(Input, Output) ? ToggleState.On : ToggleState.Off;

    /// <inheritdoc />
    public void Toggle()
    {
        ThrowIfNotEnabled();
        Matrix.Toggle(Input, Output);
    }

    /// <inheritdoc />
    public override void SetFocus() => Matrix.Focus();

    internal void Report()
    {
        var state = ToggleState;
        if (state == _reported) return;
        if (_reported is not null) RaisePropertyChanged(AutomationProperty.ToggleState, _reported, state);
        _reported = state;
    }
}
