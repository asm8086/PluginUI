// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>A destination of a modulation (a parameter of the plug-in), optionally in a group shown as a sub-menu.</summary>
/// <param name="Name">Name shown.</param>
/// <param name="Group">Group (sub-menu) or <c>null</c>.</param>
/// <param name="Id">Application identifier (parameter id…).</param>
public sealed record ModulationTarget(string Name, string? Group = null, object? Id = null);

/// <summary>
/// A slot of a <see cref="ModulationMatrix"/>: source, target, bipolar amount and on / off, stored here or, for the amount
/// and the on / off state, read from bound parameters.
/// </summary>
public sealed class ModulationSlot
{
    private double _amount;
    private bool _enabled = true;

    /// <summary>Creates a slot.</summary>
    public ModulationSlot(ModulationSource? source = null, ModulationTarget? target = null, double amount = 0.0)
    {
        Source = source;
        Target = target;
        _amount = amount;
    }

    /// <summary>Source, or <c>null</c> when not chosen yet.</summary>
    public ModulationSource? Source { get; set; }

    /// <summary>Target, or <c>null</c> when not chosen yet.</summary>
    public ModulationTarget? Target { get; set; }

    /// <summary>Amount, −1 … +1.</summary>
    public double Amount
    {
        get => AmountParameter is { } p ? p.NormalizedValue * 2.0 - 1.0 : _amount;
        set
        {
            value = Math.Clamp(value, -1.0, 1.0);
            if (AmountParameter is { } p) p.PerformEdit((value + 1.0) * 0.5);
            else _amount = value;
        }
    }

    /// <summary><c>true</c> when the slot modulates (otherwise bypassed).</summary>
    public bool IsEnabled
    {
        get => EnabledParameter is { } p ? p.NormalizedValue >= 0.5 : _enabled;
        set
        {
            if (EnabledParameter is { } p)
            {
                p.BeginEdit();
                p.PerformEdit(value ? 1.0 : 0.0);
                p.EndEdit();
            }
            else _enabled = value;
        }
    }

    /// <summary>Amount parameter (normalized 0 … 1 for −1 … +1, 0.5 = no modulation), or <c>null</c>.</summary>
    public IParameter? AmountParameter { get; set; }

    /// <summary>On / off parameter (on above 0.5), or <c>null</c>.</summary>
    public IParameter? EnabledParameter { get; set; }
}

/// <summary>
/// Modulation matrix: one row per slot with an on / off switch, the source, the target, a bipolar amount and a delete
/// button, as in the matrix pages of software synthesizers. Sources dragged from a <see cref="ModulationSourceButton"/>
/// add a slot (or replace the source of the row they are dropped on).
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Click the source or the target to choose it from a menu (targets are grouped by
/// <see cref="ModulationTarget.Group"/>); drag the amount left or right (Shift = fine), double-click it for 0; click the
/// dot to switch the slot on or off and × to delete it; click the last row to add a slot.</para>
/// <para><b>Keyboard.</b> Up / Down choose the row, Left / Right the column; Enter or Space acts on the cell (switch,
/// menu, delete, add); Page Up / Page Down or + / − change the amount (Shift = fine); Insert adds a slot, Delete
/// removes the current one.</para>
/// <para><b>Parameters.</b> Bind <see cref="ModulationSlot.AmountParameter"/> and <see cref="ModulationSlot.EnabledParameter"/>
/// for automatable slots; every change is a complete edit gesture. Sources and targets are plug-in state: save
/// <see cref="Slots"/> after <see cref="SlotChanged"/>, <see cref="SlotAdded"/> and <see cref="SlotRemoved"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var matrix = new ModulationMatrix { MaxSlots = 16 };
/// matrix.Sources.Add(new ModulationSource("LFO 1", Color.FromRgb(0x4FC3F7)));
/// matrix.Targets.Add(new ModulationTarget("Cutoff", "Filter", ParamIds.Cutoff));
/// matrix.SlotChanged += (m, i) => engine.SetRoute(i, m.Slots[i]);
/// </code>
/// </example>
public class ModulationMatrix : Control
{
    private const float HeaderHeight = 20f;
    private const int ColumnCount = 5; // on, source, target, amount, delete

    private int _row = -1, _column = 1;
    private int _hoverRow = -1, _hoverColumn = -1;
    private int _dragRow = -1;
    private float _dragStartX;
    private double _dragStartAmount;
    private float _scroll;
    private int _dropRow = -2;

    /// <summary>Creates an empty matrix.</summary>
    public ModulationMatrix()
    {
        Focusable = true;
        Cursor = StandardCursor.Hand;
    }

    /// <summary>Sources offered in the source menu.</summary>
    [Browsable(false)]
    public IList<ModulationSource> Sources { get; } = new List<ModulationSource>();

    /// <summary>Targets offered in the target menu.</summary>
    [Browsable(false)]
    public IList<ModulationTarget> Targets { get; } = new List<ModulationTarget>();

    /// <summary>Slots, one per row.</summary>
    [Browsable(false)]
    public IList<ModulationSlot> Slots { get; } = new List<ModulationSlot>();

    /// <summary>Largest number of slots the user can add (1 … 256).</summary>
    [Category("Behavior")]
    public int MaxSlots { get; set; } = 16;

    /// <summary>Row height in DIPs.</summary>
    [Category("Layout")]
    public float RowHeight { get; set; } = 26f;

    /// <summary>Row of the keyboard cursor (−1 = none; <see cref="Slots"/>.Count = the add row).</summary>
    [Browsable(false)]
    public int CurrentRow => _row;

    /// <summary>Column of the keyboard cursor: 0 on / off, 1 source, 2 target, 3 amount, 4 delete.</summary>
    [Browsable(false)]
    public int CurrentColumn => _column;

    /// <summary>Raised after the user changed a slot (its index).</summary>
    public event Action<ModulationMatrix, int>? SlotChanged;

    /// <summary>Raised after the user added a slot (its index).</summary>
    public event Action<ModulationMatrix, int>? SlotAdded;

    /// <summary>Raised after the user removed a slot (its former index).</summary>
    public event Action<ModulationMatrix, int>? SlotRemoved;

    /// <inheritdoc />
    protected override Size DefaultSize => new(480f, 200f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new ModulationMatrixAutomationPeer(this);

    private bool CanAdd => Slots.Count < Math.Clamp(MaxSlots, 1, 256);

    private int RowCount => Slots.Count + (CanAdd ? 1 : 0);

    // ---------------------------------------------------------------- geometry

    private Rect Area => Bounds.Deflate(1f);

    private Rect RowsArea
    {
        get
        {
            var a = Area;
            return Rect.FromEdges(a.X, a.Y + HeaderHeight, a.Right, a.Bottom);
        }
    }

    private void ClampScroll() => _scroll = Math.Clamp(_scroll, 0f, MathF.Max(0f, RowCount * RowHeight - RowsArea.Height));

    private Rect RowRect(int row)
    {
        var rows = RowsArea;
        return new Rect(rows.X, rows.Y + row * RowHeight - _scroll, rows.Width, RowHeight);
    }

    private static Rect[] Columns(Rect row)
    {
        const float on = 24f, delete = 24f, arrow = 16f;
        float rest = MathF.Max(0f, row.Width - on - delete - arrow);
        float source = rest * 0.3f, target = rest * 0.34f, amount = rest - source - target;
        float x = row.X;
        var cOn = new Rect(x, row.Y, on, row.Height); x += on;
        var cSource = new Rect(x, row.Y, source, row.Height); x += source + arrow;
        var cTarget = new Rect(x, row.Y, target, row.Height); x += target;
        var cAmount = new Rect(x, row.Y, amount, row.Height); x += amount;
        var cDelete = new Rect(x, row.Y, delete, row.Height);
        return [cOn, cSource, cTarget, cAmount, cDelete];
    }

    private (int Row, int Column) CellAt(Point p)
    {
        var rows = RowsArea;
        if (!rows.Contains(p)) return (-1, -1);
        int row = (int)MathF.Floor((p.Y - rows.Y + _scroll) / RowHeight);
        if (row < 0 || row >= RowCount) return (-1, -1);
        var cols = Columns(RowRect(row));
        for (int c = 0; c < ColumnCount; c++) if (cols[c].Contains(p)) return (row, c);
        return (row, -1);
    }

    /// <summary>Bounds of a row (host DIPs, scrolled).</summary>
    internal Rect GetRowBounds(int row) => RowRect(row);

    private void EnsureVisible(int row)
    {
        var rows = RowsArea;
        float top = row * RowHeight, bottom = top + RowHeight;
        if (top < _scroll) _scroll = top;
        else if (bottom > _scroll + rows.Height) _scroll = bottom - rows.Height;
        ClampScroll();
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var t = Strings;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        ClampScroll();
        var area = Area;
        var header = new Rect(area.X, area.Y, area.Width, HeaderHeight);
        var headerCols = Columns(header);
        var caption = Theme.Typography.Small with { Size = 9.5f, VerticalAlign = VerticalAlignment.Center };
        dc.DrawText(t.ModulationSourceHeader.ToUpperInvariant(), headerCols[1].Deflate(new Thickness(6f, 0f, 0f, 0f)), caption, p.TextDim);
        dc.DrawText(t.ModulationTargetHeader.ToUpperInvariant(), headerCols[2].Deflate(new Thickness(6f, 0f, 0f, 0f)), caption, p.TextDim);
        dc.DrawText(t.ModulationAmountHeader.ToUpperInvariant(), headerCols[3], caption with { Align = HorizontalAlignment.Center }, p.TextDim);
        dc.DrawLine(new Point(area.X, header.Bottom - 0.5f), new Point(area.Right, header.Bottom - 0.5f), p.Border, 1f);

        var text = Theme.Typography.Normal with { VerticalAlign = VerticalAlignment.Center };
        var dim = text with { Size = text.Size - 1f };
        var rows = RowsArea;
        using (dc.Clip(rows))
        {
            for (int r = 0; r < RowCount; r++)
            {
                var row = RowRect(r);
                if (row.Bottom < rows.Y || row.Y > rows.Bottom) continue;
                bool current = r == _row && IsFocused;
                if (r % 2 == 1) dc.FillRect(row, p.Hover);
                if (r == _dropRow) dc.FillRect(row, style.Accent.WithAlpha(0.18f));
                var cols = Columns(row);
                if (r == Slots.Count)
                {
                    var add = Rect.FromEdges(cols[0].X + 6f, row.Y + 3f, row.Right - 6f, row.Bottom - 3f);
                    bool hot = _hoverRow == r || current;
                    dc.DrawRoundedRect(add, 4f, (hot ? style.Accent : p.Border).WithAlpha(hot ? 0.9f : 0.7f), 1f);
                    dc.DrawText("+  " + t.AddModulation, add, text with { Align = HorizontalAlignment.Center }, hot ? style.Accent : p.TextDim);
                    if (current && Host?.ShowFocusRings == true) dc.DrawRoundedRect(add.Inflate(1.5f), 5f, p.Focus, 1f);
                    continue;
                }
                var slot = Slots[r];
                var color = slot.Source?.Color ?? style.Accent;
                bool on = slot.IsEnabled;
                float alpha = on ? 1f : 0.45f;

                // On / off dot.
                var dot = cols[0].Center;
                if (on) dc.FillCircle(dot, 5f, color);
                dc.DrawCircle(dot, 5f, on ? color.Lighten(0.2f) : p.TextDim, 1.2f);

                // Source and target cells: text with a small menu arrow, highlighted on hover.
                for (int c = 1; c <= 2; c++)
                {
                    var cell = cols[c].Deflate(new Thickness(2f, 3f, 2f, 3f));
                    if (_hoverRow == r && _hoverColumn == c) dc.FillRoundedRect(cell, 4f, p.Hover);
                    string? name = c == 1 ? slot.Source?.Name : slot.Target?.Name;
                    if (c == 2 && slot.Target?.Group is { Length: > 0 } group && name is not null) name = group + " · " + name;
                    var textColor = name is null ? p.TextDim : (c == 1 ? color : p.Text).WithAlpha(alpha);
                    dc.DrawText(name ?? t.ModulationNone, Rect.FromEdges(cell.X + 6f, cell.Y, cell.Right - 14f, cell.Bottom), c == 1 ? text : dim, textColor);
                    var a = new Point(cell.Right - 8f, cell.Center.Y);
                    dc.DrawPolyline([new Point(a.X - 3f, a.Y - 1.5f), new Point(a.X, a.Y + 1.5f), new Point(a.X + 3f, a.Y - 1.5f)], p.TextDim, 1.2f, StrokeStyle.Round);
                }
                var arrowCenter = new Point(cols[1].Right + 8f, row.Center.Y);
                dc.DrawLine(new Point(arrowCenter.X - 5f, arrowCenter.Y), new Point(arrowCenter.X + 4f, arrowCenter.Y), p.TextDim, 1.2f);
                dc.DrawPolyline([new Point(arrowCenter.X + 1f, arrowCenter.Y - 3f), new Point(arrowCenter.X + 4f, arrowCenter.Y), new Point(arrowCenter.X + 1f, arrowCenter.Y + 3f)], p.TextDim, 1.2f, StrokeStyle.Round);

                // Bipolar amount bar.
                var track = cols[3].Deflate(new Thickness(6f, 7f, 6f, 7f));
                dc.FillRoundedRect(track, 3f, p.Track.WithAlpha(0.7f));
                float mid = track.Center.X;
                float x = mid + (float)slot.Amount * track.Width * 0.5f;
                dc.FillRect(Rect.FromEdges(MathF.Min(mid, x), track.Y, MathF.Max(mid, x), track.Bottom), color.WithAlpha(0.85f * alpha));
                dc.DrawLine(new Point(mid, track.Y - 3f), new Point(mid, track.Y), p.TextDim, 1f);
                dc.DrawLine(new Point(mid, track.Bottom), new Point(mid, track.Bottom + 3f), p.TextDim, 1f);
                dc.DrawText(FormatAmount(slot.Amount), track, Theme.Typography.Small with { Align = HorizontalAlignment.Center, VerticalAlign = VerticalAlignment.Center, Size = 9.5f }, p.Text.WithAlpha(alpha));

                // Delete.
                var del = cols[4].Center;
                var delColor = _hoverRow == r && _hoverColumn == 4 ? p.Danger : p.TextDim;
                dc.DrawLine(new Point(del.X - 4f, del.Y - 4f), new Point(del.X + 4f, del.Y + 4f), delColor, 1.4f);
                dc.DrawLine(new Point(del.X - 4f, del.Y + 4f), new Point(del.X + 4f, del.Y - 4f), delColor, 1.4f);

                if (current && Host?.ShowFocusRings == true && _column >= 0 && _column < ColumnCount)
                {
                    dc.DrawRoundedRect(cols[_column].Deflate(new Thickness(1f, 2f, 1f, 2f)), 4f, p.Focus, 1.2f);
                }
            }
        }
    }

    /// <summary>The amount as a signed percentage ("+35 %").</summary>
    internal static string FormatAmount(double amount)
    {
        int percent = (int)Math.Round(amount * 100.0);
        return (percent > 0 ? "+" : "") + percent.ToString(CultureInfo.InvariantCulture) + " %";
    }

    /// <inheritdoc />
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        // Focus is shown on the current cell instead of the default ring.
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragRow >= 0 && _dragRow < Slots.Count)
        {
            var track = Columns(RowRect(_dragRow))[3].Deflate(new Thickness(6f, 0f, 6f, 0f));
            double delta = (e.Position.X - _dragStartX) / MathF.Max(1f, track.Width) * 2.0 * (e.Shift ? 0.15 : 1.0);
            SetAmount(_dragRow, _dragStartAmount + delta);
            if (e.Shift)
            {
                _dragStartX = e.Position.X;
                _dragStartAmount = Slots[_dragRow].Amount;
            }
            return;
        }
        var (row, column) = CellAt(e.Position);
        Cursor = row >= 0 && row < Slots.Count && column == 3 ? StandardCursor.SizeWE : StandardCursor.Hand;
        if (row != _hoverRow || column != _hoverColumn)
        {
            _hoverRow = row;
            _hoverColumn = column;
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
        var (row, column) = CellAt(e.Position);
        if (row < 0) return;
        e.Handled = true;
        _row = row;
        if (row == Slots.Count)
        {
            AddSlot(new ModulationSlot());
            return;
        }
        if (column >= 0) _column = column;
        NotifyAutomationStateChanged();
        switch (column)
        {
            case 0:
                ToggleEnabled(row);
                break;
            case 1 or 2:
                OpenMenu(row, column);
                break;
            case 3 when e.ClickCount == 2:
                Edit(row, () => SetAmount(row, 0.0));
                break;
            case 3:
                _dragRow = row;
                _dragStartX = e.Position.X;
                _dragStartAmount = Slots[row].Amount;
                Slots[row].AmountParameter?.BeginEdit();
                break;
            case 4:
                RemoveSlot(row);
                break;
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragRow < 0) return;
        if (_dragRow < Slots.Count) Slots[_dragRow].AmountParameter?.EndEdit();
        _dragRow = -1;
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.IsHorizontalWheel) return;
        float before = _scroll;
        _scroll -= e.WheelDelta * RowHeight * 2f;
        ClampScroll();
        if (_scroll != before)
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    // ---------------------------------------------------------------- drag and drop of sources

    /// <inheritdoc />
    protected internal override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Data is not ModulationSource) return;
        var (row, _) = CellAt(e.Position);
        int drop = row >= 0 && row < Slots.Count ? row : -1;
        if (drop < 0 && !CanAdd) return;
        e.Accept = true;
        if (drop != _dropRow)
        {
            _dropRow = drop;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnDragLeave()
    {
        base.OnDragLeave();
        _dropRow = -2;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        int drop = _dropRow;
        _dropRow = -2;
        if (e.Data is not ModulationSource source) return;
        var (row, _) = CellAt(e.Position);
        if (row >= 0 && row < Slots.Count) drop = row;
        if (drop >= 0 && drop < Slots.Count)
        {
            Slots[drop].Source = source;
            _row = drop;
            Changed(drop);
        }
        else if (CanAdd)
        {
            AddSlot(new ModulationSlot(source));
        }
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        int rows = RowCount;
        if (rows == 0) return;
        bool slot = _row >= 0 && _row < Slots.Count;
        double step = e.Shift ? 0.01 : 0.1;
        switch (e.Key)
        {
            case Key.Down:
                _row = Math.Min(rows - 1, _row + 1);
                break;
            case Key.Up:
                _row = Math.Max(0, _row - 1);
                break;
            case Key.Home:
                _row = 0;
                break;
            case Key.End:
                _row = rows - 1;
                break;
            case Key.Left:
                _column = Math.Max(0, _column - 1);
                if (_row < 0) _row = 0;
                break;
            case Key.Right:
                _column = Math.Min(ColumnCount - 1, _column + 1);
                if (_row < 0) _row = 0;
                break;
            case Key.PageUp or Key.Add or Key.OemPlus when slot:
            {
                int r = _row;
                Edit(r, () => SetAmount(r, Slots[r].Amount + step));
                break;
            }
            case Key.PageDown or Key.Subtract or Key.OemMinus when slot:
            {
                int r = _row;
                Edit(r, () => SetAmount(r, Slots[r].Amount - step));
                break;
            }
            case Key.Enter or Key.Space when _row == Slots.Count:
                AddSlot(new ModulationSlot());
                break;
            case Key.Enter or Key.Space when slot:
                switch (_column)
                {
                    case 0: ToggleEnabled(_row); break;
                    case 1 or 2: OpenMenu(_row, _column); break;
                    case 3: { int r = _row; Edit(r, () => SetAmount(r, 0.0)); break; }
                    case 4: RemoveSlot(_row); break;
                }
                break;
            case Key.Insert when CanAdd:
                AddSlot(new ModulationSlot());
                break;
            case Key.Delete when slot:
                RemoveSlot(_row);
                break;
            default:
                return;
        }
        if (_row >= 0) EnsureVisible(_row);
        e.Handled = true;
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    // ---------------------------------------------------------------- edits

    private void OpenMenu(int row, int column)
    {
        if (Host is not { } host || row < 0 || row >= Slots.Count) return;
        var slot = Slots[row];
        var items = new List<MenuItem>();
        if (column == 1)
        {
            foreach (var source in Sources)
            {
                var s = source;
                items.Add(new MenuItem(s.Name, () => { slot.Source = s; Changed(row); }) { IsChecked = Equals(slot.Source, s) });
            }
        }
        else
        {
            var groups = new Dictionary<string, MenuItem>();
            foreach (var target in Targets)
            {
                var tg = target;
                var item = new MenuItem(tg.Name, () => { slot.Target = tg; Changed(row); }) { IsChecked = Equals(slot.Target, tg) };
                if (tg.Group is { Length: > 0 } group)
                {
                    if (!groups.TryGetValue(group, out var parent))
                    {
                        groups[group] = parent = new MenuItem(group);
                        items.Add(parent);
                    }
                    parent.Items.Add(item);
                    if (item.IsChecked) parent.IsChecked = true;
                }
                else items.Add(item);
            }
        }
        if (items.Count == 0) return;
        host.ShowMenu(items, Columns(RowRect(row))[column], PopupPlacement.Below, Columns(RowRect(row))[column].Width);
    }

    /// <summary>Chooses a source or a target as the menus do (screen readers, tests).</summary>
    internal void Choose(int row, ModulationSource? source, ModulationTarget? target)
    {
        if (source is not null) Slots[row].Source = source;
        if (target is not null) Slots[row].Target = target;
        Changed(row);
    }

    private static void Edit(IParameter? parameter, Action change)
    {
        parameter?.BeginEdit();
        change();
        parameter?.EndEdit();
    }

    private void Edit(int row, Action change) => Edit(Slots[row].AmountParameter, change);

    private void SetAmount(int row, double amount)
    {
        amount = Math.Clamp(amount, -1.0, 1.0);
        if (Math.Abs(Slots[row].Amount - amount) < 1e-9) return;
        Slots[row].Amount = amount;
        Changed(row);
    }

    private void ToggleEnabled(int row)
    {
        Slots[row].IsEnabled = !Slots[row].IsEnabled;
        Changed(row);
    }

    private void AddSlot(ModulationSlot slot)
    {
        if (!CanAdd) return;
        Slots.Add(slot);
        _row = Slots.Count - 1;
        _column = slot.Source is null ? 1 : 2;
        EnsureVisible(_row);
        SlotAdded?.Invoke(this, _row);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void RemoveSlot(int row)
    {
        Slots.RemoveAt(row);
        _row = Math.Min(_row, RowCount - 1);
        ClampScroll();
        SlotRemoved?.Invoke(this, row);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void Changed(int row)
    {
        SlotChanged?.Invoke(this, row);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary>What a screen reader says about the current row and column.</summary>
    internal string DescribeCurrent()
    {
        var t = Strings;
        if (_row == Slots.Count) return t.AddModulation;
        if (_row < 0 || _row >= Slots.Count) return string.Format(CultureInfo.InvariantCulture, t.ModulationSlotCountFormat, Slots.Count);
        var slot = Slots[_row];
        string column = _column switch
        {
            0 => slot.IsEnabled ? t.ModulationOn : t.ModulationOff,
            1 => t.ModulationSourceHeader,
            2 => t.ModulationTargetHeader,
            3 => t.ModulationAmountHeader,
            _ => t.Delete,
        };
        return string.Format(CultureInfo.InvariantCulture, t.ModulationSlotFormat, _row + 1) + ": " + (slot.Source?.Name ?? t.ModulationNone) + " → "
            + (slot.Target?.Name ?? t.ModulationNone) + ", " + FormatAmount(slot.Amount) + (slot.IsEnabled ? "" : ", " + t.ModulationOff) + " (" + column + ")";
    }
}

/// <summary>Peer of a <see cref="ModulationMatrix"/>: a group whose value describes the current slot and column.</summary>
/// <param name="owner">The matrix.</param>
public class ModulationMatrixAutomationPeer(ModulationMatrix owner) : ElementAutomationPeer<ModulationMatrix>(owner), IValuePattern
{
    private string? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationModulationMatrix;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => true;

    /// <inheritdoc />
    public string Value => Owner.DescribeCurrent();

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(string value) => throw new InvalidOperationException("The modulation matrix is edited with its keys.");

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
