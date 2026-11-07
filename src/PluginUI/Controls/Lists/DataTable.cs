// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Collections;
using System.Globalization;
using System.Text;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>Column of a <see cref="DataTable"/>.</summary>
public sealed class DataColumn
{
    /// <summary>Creates a column.</summary>
    /// <param name="header">Header caption.</param>
    /// <param name="cellText">Text of the cell for a row item.</param>
    /// <param name="width">Width in DIPs (NaN = proportional share of the remaining space, see <see cref="Star"/>).</param>
    public DataColumn(string header, Func<object, string> cellText, float width = float.NaN)
    {
        Header = header;
        CellText = cellText ?? throw new ArgumentNullException(nameof(cellText));
        Width = width;
    }

    /// <summary>Header caption.</summary>
    [Category("Text")]
    public string Header { get; set; }

    /// <summary>
    /// Stable identifier used by <see cref="DataTable.SaveLayout"/> (defaults to the header; set it when the header is
    /// translated, so that a saved layout survives a change of language).
    /// </summary>
    [Category("Behavior")]
    public string Key { get => _key ?? Header; set => _key = value; }

    private string? _key;

    /// <summary>Shown in the table. Hidden columns keep their width, sort and editing settings.</summary>
    [Category("Appearance")]
    public bool IsVisible { get; set; } = true;

    /// <summary>The user can hide the column from the header menu (right click on the header).</summary>
    [Category("Behavior")]
    public bool CanHide { get; set; } = true;

    /// <summary>Cell text for a row item.</summary>
    [Browsable(false)]
    public Func<object, string> CellText { get; set; }

    /// <summary>Width in DIPs; NaN for a proportional column. Set by the user when resizing.</summary>
    [Category("Layout")]
    public float Width { get; set; }

    /// <summary>Weight of a proportional column.</summary>
    [Category("Appearance")]
    public float Star { get; set; } = 1f;

    /// <summary>Minimum width.</summary>
    [Category("Layout")]
    public float MinWidth { get; set; } = 36f;

    /// <summary>Cell (and header) alignment.</summary>
    [Category("Layout")]
    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Sort comparison (null = not sortable).</summary>
    [Browsable(false)]
    public Comparison<object>? Comparison { get; set; }

    /// <summary>Called with the new text after an in-place edit (null = read-only column).</summary>
    [Browsable(false)]
    public Action<object, string>? CommitEdit { get; set; }

    /// <summary>Optional per-cell text color.</summary>
    [Browsable(false)]
    public Func<object, Color?>? CellColor { get; set; }

    /// <summary>
    /// Draws the cell instead of its text (icons, stars, bars): drawing context, cell bounds (padding removed), row item and
    /// the text color. <see cref="CellText"/> still gives the sort, the automatic width and what screen readers say.
    /// </summary>
    [Browsable(false)]
    public Action<DrawingContext, Rect, object, Color>? CellRenderer { get; set; }

    /// <summary>Lets the user resize the column.</summary>
    [Category("Behavior")]
    public bool IsResizable { get; set; } = true;

    /// <summary><c>true</c> if cells can be edited in place.</summary>
    public bool IsEditable => CommitEdit is not null;

    /// <summary>Makes the column sortable by its text (ordinal, case-insensitive) — fluent.</summary>
    public DataColumn SortByText()
    {
        Comparison = (a, b) => string.Compare(CellText(a), CellText(b), StringComparison.OrdinalIgnoreCase);
        return this;
    }
}

/// <summary>
/// Virtualized multi-column table (preset browsers, sample lists, modulation matrices): sortable headers with arrows,
/// resizable columns, striped rows, single or extended selection, keyboard navigation, horizontal and vertical
/// scrolling, and in-place editing of editable columns (double-click or F2).
/// </summary>
/// <remarks>Call <see cref="Refresh"/> after changing the <see cref="Items"/> list.</remarks>
public class DataTable : Control
{
    private readonly SelectionModel _selection = new();
    private readonly SlimScrollBar _vbar = new(Orientation.Vertical);
    private readonly SlimScrollBar _hbar = new(Orientation.Horizontal);
    private IList? _items;
    private int[] _order = [];
    private bool _orderDirty = true;
    private float[] _colX = [], _colW = [];
    private float _scrollX, _scrollY;
    private int _hoverRow = -1;
    private int _resizeColumn = -1;
    private float _resizeStartX, _resizeStartWidth;
    private SlimScrollBar? _barDrag;
    private int _pressedHeader = -1;
    private int _pendingScrollRow = -1;

    /// <summary>Creates an empty table.</summary>
    public DataTable() { }

    /// <summary>Columns.</summary>
    public List<DataColumn> Columns { get; } = new();

    /// <summary>Row items.</summary>
    [Category("Value")]
    public IList? Items
    {
        get => _items;
        set
        {
            _items = value;
            _scrollY = 0f;
            _orderDirty = true;
            if (_selection.Clear()) RaiseSelectionChanged();
            InvalidateVisual();
        }
    }

    /// <summary>Row height.</summary>
    [Category("Layout")]
    public float RowHeight { get; set; } = 24f;

    /// <summary>Header height (0 = no header).</summary>
    [Category("Layout")]
    public float HeaderHeight { get; set; } = 26f;

    /// <summary>Alternate row shading.</summary>
    [Category("Appearance")]
    public bool Striped { get; set; } = true;

    /// <summary>Vertical lines between columns.</summary>
    [Category("Appearance")]
    public bool ShowGridLines { get; set; }

    /// <summary>Extended selection (Ctrl / Shift).</summary>
    [Category("Behavior")]
    public bool MultiSelect { get => _selection.MultiSelect; set => _selection.MultiSelect = value; }

    /// <summary>Text shown when there are no rows.</summary>
    [Category("Text")]
    public string? EmptyText { get; set; }

    /// <summary>Sort column (null = items order).</summary>
    [Browsable(false)]
    public DataColumn? SortColumn { get; private set; }

    /// <summary>Descending sort.</summary>
    [Browsable(false)]
    public bool SortDescending { get; private set; }

    /// <summary>Number of rows.</summary>
    public int RowCount => _items?.Count ?? 0;

    /// <summary>Raised when the selection changes.</summary>
    public event Action<DataTable>? SelectionChanged;

    /// <summary>Raised on double-click (non-editable cell) or Enter with the row item.</summary>
    public event Action<DataTable, object>? RowActivated;

    /// <summary>Raised when the sort changes.</summary>
    public event Action<DataTable>? SortChanged;

    /// <summary>Raised when the user resizes, shows or hides a column (to save the layout).</summary>
    public event Action<DataTable>? ColumnsChanged;

    /// <summary>
    /// Entries of the context menu of the selected rows (right click on a row, the menu key or Shift+F10), or null for
    /// no menu. The argument is the selection, in view order.
    /// </summary>
    [Browsable(false)]
    public Func<IReadOnlyList<object>, IList<MenuItem>?>? RowContextMenu { get; set; }

    /// <summary>Right click on the header shows a menu to show or hide the columns that <see cref="DataColumn.CanHide"/>.</summary>
    [Category("Behavior")]
    public bool ShowColumnMenu { get; set; } = true;

    /// <summary>Columns shown, in order.</summary>
    public IEnumerable<DataColumn> VisibleColumns => Columns.Where(c => c.IsVisible);

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this);
        NotifyAutomationStateChanged();
    }

    /// <summary>Selected row items (in view order).</summary>
    public IEnumerable<object> SelectedItems
    {
        get
        {
            EnsureOrder();
            foreach (int v in _selection.Selected)
            {
                if (v < _order.Length) yield return _items![_order[v]]!;
            }
        }
    }

    /// <summary>First selected item (null = none).</summary>
    [Browsable(false)]
    public object? SelectedItem
    {
        get
        {
            EnsureOrder();
            int v = _selection.First;
            return v >= 0 && v < _order.Length ? _items![_order[v]] : null;
        }
        set
        {
            EnsureOrder();
            int view = -1;
            if (value is not null && _items is not null)
            {
                for (int v = 0; v < _order.Length; v++)
                {
                    if (Equals(_items[_order[v]], value)) { view = v; break; }
                }
            }
            if (_selection.Set(view)) RaiseSelectionChanged();
            if (view >= 0) ScrollRowIntoView(view);
            InvalidateVisual();
        }
    }

    /// <summary>Adds a column (fluent).</summary>
    public DataTable AddColumn(DataColumn column)
    {
        Columns.Add(column);
        InvalidateVisual();
        return this;
    }

    /// <summary>Sorts by a column (null = items order).</summary>
    public void Sort(DataColumn? column, bool descending = false)
    {
        SortColumn = column?.Comparison is null ? null : column;
        SortDescending = descending;
        _orderDirty = true;
        if (_selection.Clear()) RaiseSelectionChanged();
        SortChanged?.Invoke(this);
        NotifyAutomationStateChanged();
        InvalidateVisual();
    }

    /// <summary>Re-reads the items (after adding, removing or editing rows).</summary>
    public void Refresh()
    {
        _orderDirty = true;
        if (_selection.Clamp(RowCount)) RaiseSelectionChanged();
        InvalidateVisual();
    }

    private void EnsureOrder()
    {
        int count = RowCount;
        if (!_orderDirty && _order.Length == count) return;
        if (_order.Length != count) _order = new int[count];
        for (int i = 0; i < count; i++) _order[i] = i;
        if (SortColumn?.Comparison is { } comparison && _items is not null)
        {
            var items = _items;
            int sign = SortDescending ? -1 : 1;
            // Stable: ties keep the items order.
            Array.Sort(_order, (a, b) =>
            {
                int c = comparison(items[a]!, items[b]!) * sign;
                return c != 0 ? c : a.CompareTo(b);
            });
        }
        _orderDirty = false;
    }

    // ---------------------------------------------------------------- geometry

    private Rect Inner => Bounds.Inflate(-1f);

    private Rect HeaderArea => Inner.TakeTop(HeaderHeight);

    private Rect RowsArea => Rect.FromEdges(Inner.X, Inner.Y + HeaderHeight, Inner.Right, Inner.Bottom);

    private float TotalColumnsWidth => _colW.Length == 0 ? 0f : _colX[^1] + _colW[^1] - Inner.X;

    private void LayoutColumns()
    {
        int n = Columns.Count;
        if (_colX.Length != n)
        {
            _colX = new float[n];
            _colW = new float[n];
        }
        float available = Inner.Width;
        float fixedSum = 0f, stars = 0f;
        foreach (var c in Columns)
        {
            if (!c.IsVisible) continue;
            if (float.IsNaN(c.Width)) stars += MathF.Max(0.01f, c.Star);
            else fixedSum += MathF.Max(c.MinWidth, c.Width);
        }
        float remaining = MathF.Max(0f, available - fixedSum);
        float x = Inner.X;
        for (int i = 0; i < n; i++)
        {
            var c = Columns[i];
            float w = !c.IsVisible ? 0f : float.IsNaN(c.Width) ? MathF.Max(c.MinWidth, remaining * MathF.Max(0.01f, c.Star) / MathF.Max(0.01f, stars)) : MathF.Max(c.MinWidth, c.Width);
            _colX[i] = x;
            _colW[i] = w;
            x += w;
        }
    }

    private void UpdateBars()
    {
        var rows = RowsArea;
        _hbar.Extent = TotalColumnsWidth;
        _hbar.Viewport = Inner.Width;
        _hbar.Offset = _scrollX;
        float hGap = _hbar.IsNeeded ? 10f : 0f;
        _vbar.Extent = RowCount * RowHeight;
        _vbar.Viewport = MathF.Max(0f, rows.Height - hGap);
        _vbar.Offset = _scrollY;
        _vbar.Track = new Rect(rows.Right - 10f, rows.Y + 2f, 9f, MathF.Max(0f, rows.Height - 4f - hGap));
        _hbar.Track = new Rect(Inner.X + 2f, Inner.Bottom - 10f, MathF.Max(0f, Inner.Width - 4f - (_vbar.IsNeeded ? 10f : 0f)), 9f);
    }

    private void ClampScroll()
    {
        _scrollX = Math.Clamp(_scrollX, 0f, _hbar.MaxOffset);
        _scrollY = Math.Clamp(_scrollY, 0f, _vbar.MaxOffset);
        _hbar.Offset = _scrollX;
        _vbar.Offset = _scrollY;
    }

    private void Prepare()
    {
        EnsureOrder();
        LayoutColumns();
        UpdateBars();
        ClampScroll();
    }

    private int RowAt(Point p)
    {
        var rows = RowsArea;
        if (!rows.Contains(p)) return -1;
        int view = (int)MathF.Floor((p.Y - rows.Y + _scrollY) / RowHeight);
        return view >= 0 && view < RowCount ? view : -1;
    }

    private int ColumnAt(float x)
    {
        float cx = x + _scrollX;
        for (int i = 0; i < _colX.Length; i++)
        {
            if (cx >= _colX[i] && cx < _colX[i] + _colW[i]) return i;
        }
        return -1;
    }

    private int ResizeHandleAt(Point p)
    {
        if (!HeaderArea.Contains(p)) return -1;
        for (int i = 0; i < _colX.Length; i++)
        {
            if (_colW[i] <= 0f) continue;
            float edge = _colX[i] + _colW[i] - _scrollX;
            if (MathF.Abs(p.X - edge) <= 4f && Columns[i].IsResizable) return i;
        }
        return -1;
    }

    private int LastVisibleColumn => Columns.FindLastIndex(c => c.IsVisible);

    private Rect CellRect(int view, int column)
    {
        var rows = RowsArea;
        float y = rows.Y + view * RowHeight - _scrollY;
        return new Rect(_colX[column] - _scrollX, y, _colW[column], RowHeight);
    }

    /// <summary>Scrolls so that a row (view index) is visible.</summary>
    public void ScrollRowIntoView(int view)
    {
        if (view < 0) return;
        if (RowsArea.Height <= 0f)
        {
            _pendingScrollRow = view; // not laid out yet
            return;
        }
        Prepare();
        float top = view * RowHeight, bottom = top + RowHeight;
        if (top < _scrollY) _scrollY = top;
        else if (bottom > _scrollY + _vbar.Viewport) _scrollY = bottom - _vbar.Viewport;
        ClampScroll();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override Size DefaultSize => new(360f, 200f);

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    /// <inheritdoc />
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        // Focus is shown on the frame and the current row.
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        const float radius = 6f;
        if (_pendingScrollRow >= 0 && RowsArea.Height > 0f)
        {
            int pending = _pendingScrollRow;
            _pendingScrollRow = -1;
            ScrollRowIntoView(pending);
        }
        Prepare();
        dc.FillRoundedRect(widget, radius, style.Background);
        var inner = Inner;
        var rows = RowsArea;
        var cellStyle = Theme.Typography.Normal;
        float hair = 1f / dc.Scale;

        using (dc.PushRoundedClipScope(inner, radius - 1f))
        {
            // Rows.
            using (dc.Clip(rows))
            {
                int count = RowCount;
                if (count == 0 && !string.IsNullOrEmpty(EmptyText)) dc.DrawText(EmptyText, rows, cellStyle with { Align = HorizontalAlignment.Center }, p.TextDisabled);
                int first = Math.Max(0, (int)(_scrollY / RowHeight));
                int last = Math.Min(count - 1, (int)((_scrollY + rows.Height) / RowHeight));
                float rowRight = MathF.Min(rows.Right, inner.X + TotalColumnsWidth - _scrollX);
                rowRight = MathF.Max(rowRight, rows.Right);
                for (int v = first; v <= last; v++)
                {
                    float y = rows.Y + v * RowHeight - _scrollY;
                    var row = Rect.FromEdges(rows.X, y, rowRight, y + RowHeight);
                    bool selected = _selection.IsSelected(v);
                    if (selected) dc.FillRect(row, IsFocused ? p.Selection : p.Selection.Fade(0.6f));
                    else if (v == _hoverRow) dc.FillRect(row, p.Hover);
                    else if (Striped && (v & 1) == 1) dc.FillRect(row, p.Text.WithAlpha(0.025f));
                    if (v == _selection.Current && IsFocused && Host?.ShowFocusRings == true) dc.DrawRect(row.Inflate(-0.5f), p.Focus, 1f);

                    object item = _items![_order[v]]!;
                    for (int c = 0; c < Columns.Count; c++)
                    {
                        if (_colW[c] <= 0f) continue;
                        var cell = CellRect(v, c);
                        if (cell.Right < rows.X || cell.X > rows.Right) continue;
                        var column = Columns[c];
                        var color = column.CellColor?.Invoke(item) ?? (selected ? p.Text : p.Text.WithAlpha(0.88f));
                        if (column.CellRenderer is { } renderer)
                        {
                            using (dc.Clip(cell)) renderer(dc, cell.Deflate(new Thickness(8f, 0f)), item, color);
                        }
                        else
                        {
                            dc.DrawText(column.CellText(item), cell.Deflate(new Thickness(8f, 0f)), cellStyle with { Align = column.Alignment }, color);
                        }
                    }
                }
                if (ShowGridLines)
                {
                    int lastGrid = LastVisibleColumn;
                    for (int c = 0; c < lastGrid; c++)
                    {
                        if (_colW[c] <= 0f) continue;
                        float x = MathF.Round((_colX[c] + _colW[c] - _scrollX) * dc.Scale) / dc.Scale + hair * 0.5f;
                        dc.DrawLine(new Point(x, rows.Y), new Point(x, rows.Bottom), p.Border.WithAlpha(0.6f), hair);
                    }
                }
            }

            // Header.
            if (HeaderHeight > 0f)
            {
                var header = HeaderArea;
                dc.FillRect(header, p.SurfaceRaised);
                var headerStyle = Theme.Typography.Small with { Weight = FontWeight.SemiBold };
                using (dc.Clip(header))
                {
                    int lastHeader = LastVisibleColumn;
                    for (int c = 0; c < Columns.Count; c++)
                    {
                        if (_colW[c] <= 0f) continue;
                        var column = Columns[c];
                        var r = new Rect(_colX[c] - _scrollX, header.Y, _colW[c], header.Height);
                        if (c == _pressedHeader) dc.FillRect(r, p.Hover);
                        bool sorted = ReferenceEquals(column, SortColumn);
                        var textRect = r.Deflate(new Thickness(8f, 0f, sorted ? 20f : 8f, 0f));
                        dc.DrawText(column.Header, textRect, headerStyle with { Align = column.Alignment }, sorted ? p.Text : p.TextDim);
                        if (sorted)
                        {
                            var a = new Point(r.Right - 12f, r.Center.Y);
                            ReadOnlySpan<Point> arrow = SortDescending
                                ? [new(a.X - 3.5f, a.Y - 1.5f), new(a.X + 3.5f, a.Y - 1.5f), new(a.X, a.Y + 2.5f)]
                                : [new(a.X - 3.5f, a.Y + 2f), new(a.X + 3.5f, a.Y + 2f), new(a.X, a.Y - 2f)];
                            dc.FillPolygon(arrow, p.Accent);
                        }
                        if (c < lastHeader)
                        {
                            float x = MathF.Round(r.Right * dc.Scale) / dc.Scale - hair * 0.5f;
                            dc.DrawLine(new Point(x, r.Y + 6f), new Point(x, r.Bottom - 6f), c == _resizeColumn ? p.Accent : p.Border, c == _resizeColumn ? 2f : hair);
                        }
                    }
                }
                float by = header.Bottom - hair * 0.5f;
                dc.DrawLine(new Point(header.X, by), new Point(header.Right, by), p.Border, hair);
            }

            var mouse = Host?.MousePosition ?? default;
            _vbar.IsHovered = _barDrag == _vbar || (IsMouseOver && _vbar.HitTest(mouse));
            _hbar.IsHovered = _barDrag == _hbar || (IsMouseOver && _hbar.HitTest(mouse));
            float alpha = IsMouseOver || _barDrag is not null ? 1f : 0.6f;
            _vbar.Render(dc, p, alpha);
            _hbar.Render(dc, p, alpha);
        }
        dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, IsFocused ? style.Accent.WithAlpha(0.7f) : style.Frame, 1f);
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_barDrag is not null)
        {
            float offset = _barDrag.Drag(e.Position);
            if (_barDrag == _vbar) _scrollY = offset; else _scrollX = offset;
            InvalidateVisual();
            return;
        }
        if (_resizeColumn >= 0 && IsMouseCaptured)
        {
            var column = Columns[_resizeColumn];
            column.Width = MathF.Max(column.MinWidth, _resizeStartWidth + e.Position.X - _resizeStartX);
            InvalidateVisual();
            return;
        }
        Prepare();
        Cursor = ResizeHandleAt(e.Position) >= 0 ? StandardCursor.SizeWE : StandardCursor.Default;
        int hover = _vbar.HitTest(e.Position) || _hbar.HitTest(e.Position) ? -1 : RowAt(e.Position);
        if (hover != _hoverRow) _hoverRow = hover;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hoverRow = -1;
        Cursor = StandardCursor.Default;
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled) return;
        if (e.Button == MouseButton.Right)
        {
            // The row under the mouse joins the selection (or replaces it) before its menu opens.
            Prepare();
            int row = RowAt(e.Position);
            if (row >= 0 && !_selection.IsSelected(row))
            {
                if (_selection.Set(row)) RaiseSelectionChanged();
                InvalidateVisual();
            }
            Focus();
            return;
        }
        if (e.Button != MouseButton.Left) return;
        e.Handled = true;
        Prepare();
        if (_vbar.HitTest(e.Position)) { _barDrag = _vbar; _scrollY = _vbar.Press(e.Position); InvalidateVisual(); return; }
        if (_hbar.HitTest(e.Position)) { _barDrag = _hbar; _scrollX = _hbar.Press(e.Position); InvalidateVisual(); return; }

        int resize = ResizeHandleAt(e.Position);
        if (resize >= 0)
        {
            if (e.ClickCount == 2)
            {
                AutoFitColumn(resize);
                return;
            }
            _resizeColumn = resize;
            _resizeStartX = e.Position.X;
            _resizeStartWidth = _colW[resize];
            return;
        }

        if (HeaderArea.Contains(e.Position))
        {
            _pressedHeader = ColumnAt(e.Position.X);
            InvalidateVisual();
            return;
        }

        int view = RowAt(e.Position);
        if (view < 0) return;
        int col = ColumnAt(e.Position.X);
        if (e.ClickCount == 2)
        {
            if (col >= 0 && Columns[col].IsEditable) BeginEdit(view, col);
            else RowActivated?.Invoke(this, _items![_order[view]]!);
            return;
        }
        if (_selection.Click(view, e.Control, e.Shift)) RaiseSelectionChanged();
        else NotifyAutomationStateChanged();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Right && !e.Handled)
        {
            Prepare();
            if (HeaderArea.Contains(e.Position)) e.Handled = ShowColumnMenuAt(e.Position);
            else if (RowAt(e.Position) >= 0) e.Handled = ShowRowMenuAt(e.Position);
            return;
        }
        if (_pressedHeader >= 0 && HeaderArea.Contains(e.Position) && ColumnAt(e.Position.X) == _pressedHeader)
        {
            var column = Columns[_pressedHeader];
            if (column.Comparison is not null)
            {
                if (!ReferenceEquals(SortColumn, column)) Sort(column, false);
                else if (!SortDescending) Sort(column, true);
                else Sort(null);
            }
        }
        EndInteraction();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndInteraction();
    }

    private void EndInteraction()
    {
        _barDrag?.Release();
        _barDrag = null;
        if (_resizeColumn >= 0) ColumnsChanged?.Invoke(this);
        _resizeColumn = -1;
        _pressedHeader = -1;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled) return;
        Prepare();
        float x = _scrollX, y = _scrollY;
        if (e.IsHorizontalWheel) _scrollX += e.WheelDelta * 40f;
        else if (e.Shift) _scrollX -= e.WheelDelta * 40f;
        else _scrollY -= e.WheelDelta * RowHeight * 3f;
        ClampScroll();
        if (x != _scrollX || y != _scrollY)
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    private void AutoFitColumn(int c)
    {
        var column = Columns[c];
        var style = Theme.Typography.Normal;
        float w = TextMeasurer.Measure(column.Header, Theme.Typography.Small with { Weight = FontWeight.SemiBold }).Width + 28f;
        int count = Math.Min(RowCount, 2000); // bounded cost
        for (int i = 0; i < count; i++) w = MathF.Max(w, TextMeasurer.Measure(column.CellText(_items![i]!), style).Width + 18f);
        column.Width = MathF.Max(column.MinWidth, w);
        ColumnsChanged?.Invoke(this);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- editing

    /// <summary>Starts editing a cell (view row index, column index) if the column is editable.</summary>
    public void BeginEdit(int viewRow, int column)
    {
        if (Host is null || column < 0 || column >= Columns.Count || viewRow < 0 || viewRow >= RowCount) return;
        var col = Columns[column];
        if (col.CommitEdit is not { } commit) return;
        ScrollRowIntoView(viewRow);
        Prepare();
        object item = _items![_order[viewRow]]!;
        var cell = CellRect(viewRow, column).Intersect(RowsArea);
        if (cell.IsEmpty) return;
        Host.BeginTextEdit(cell, col.CellText(item), text =>
        {
            commit(item, text);
            Refresh();
        }, () => Focus(), Theme.Typography.Normal with { Align = col.Alignment });
    }

    // ---------------------------------------------------------------- menus

    private bool ShowRowMenuAt(Point at)
    {
        if (Host is null || RowContextMenu is null) return false;
        var selection = SelectedItems.ToList();
        if (selection.Count == 0) return false;
        var items = RowContextMenu(selection);
        if (items is not { Count: > 0 }) return false;
        Host.ShowMenu(items, at);
        return true;
    }

    private bool ShowColumnMenuAt(Point at)
    {
        if (Host is null || !ShowColumnMenu) return false;
        var items = GetColumnMenuItems();
        if (items.Count == 0) return false;
        Host.ShowMenu(items, at);
        return true;
    }

    /// <summary>Entries of the header menu: one checked entry per column that can be hidden.</summary>
    public IList<MenuItem> GetColumnMenuItems()
    {
        var items = new List<MenuItem>();
        foreach (var column in Columns)
        {
            if (!column.CanHide || string.IsNullOrEmpty(column.Header)) continue;
            var c = column;
            items.Add(new MenuItem(c.Header.Replace("&", "&&", StringComparison.Ordinal), () => SetColumnVisible(c, !c.IsVisible))
            {
                IsChecked = c.IsVisible,
                // The last visible column stays.
                IsEnabled = !c.IsVisible || Columns.Count(x => x.IsVisible) > 1,
            });
        }
        return items;
    }

    /// <summary>Shows or hides a column.</summary>
    public void SetColumnVisible(DataColumn column, bool visible)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (column.IsVisible == visible) return;
        if (!visible && Columns.Count(c => c.IsVisible) <= 1) return;
        column.IsVisible = visible;
        ColumnsChanged?.Invoke(this);
        NotifyAutomationStateChanged();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- layout persistence

    /// <summary>
    /// Text describing the column widths, visibility and the sort, for <see cref="RestoreLayout"/> (saved with the
    /// application settings).
    /// </summary>
    public string SaveLayout()
    {
        var sb = new StringBuilder();
        foreach (var c in Columns)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(Uri.EscapeDataString(c.Key)).Append(':')
              .Append(float.IsNaN(c.Width) ? "*" : c.Width.ToString("0.#", CultureInfo.InvariantCulture)).Append(':')
              .Append(c.IsVisible ? '1' : '0');
        }
        sb.Append('|');
        if (SortColumn is not null) sb.Append(Uri.EscapeDataString(SortColumn.Key)).Append(':').Append(SortDescending ? 'd' : 'a');
        return sb.ToString();
    }

    /// <summary>Applies a layout saved by <see cref="SaveLayout"/>; unknown columns and malformed parts are ignored.</summary>
    public void RestoreLayout(string? layout)
    {
        if (string.IsNullOrEmpty(layout)) return;
        string[] halves = layout.Split('|');
        foreach (string part in halves[0].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = part.Split(':');
            if (f.Length < 3) continue;
            var column = Columns.Find(c => c.Key == Uri.UnescapeDataString(f[0]));
            if (column is null) continue;
            if (f[1] == "*") column.Width = float.NaN;
            else if (float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float w) && w > 0f) column.Width = w;
            column.IsVisible = f[2] != "0" || !column.CanHide;
        }
        if (!Columns.Exists(c => c.IsVisible) && Columns.Count > 0) Columns[0].IsVisible = true;
        if (halves.Length > 1)
        {
            string[] f = halves[1].Split(':');
            var column = f.Length == 2 ? Columns.Find(c => c.Key == Uri.UnescapeDataString(f[0])) : null;
            Sort(column, f.Length == 2 && f[1] == "d");
        }
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- accessibility

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new DataTableAutomationPeer(this);

    /// <summary>Row of the keyboard cursor (view index, -1 = none).</summary>
    internal int CurrentRow => _selection.Current;

    /// <summary><c>true</c> if a row (view index) is selected.</summary>
    internal bool IsRowSelected(int view) => _selection.IsSelected(view);

    /// <summary>Selected rows (view indices).</summary>
    internal IReadOnlyCollection<int> SelectedRows => _selection.Selected;

    /// <summary>Spoken text of a row (view index): the first visible column, then "Header: value" for the others.</summary>
    internal string GetRowText(int view)
    {
        EnsureOrder();
        if (view < 0 || view >= _order.Length) return string.Empty;
        object item = _items![_order[view]]!;
        var sb = new StringBuilder();
        foreach (var c in Columns)
        {
            if (!c.IsVisible) continue;
            string text = c.CellText(item);
            if (string.IsNullOrEmpty(text)) continue;
            if (sb.Length == 0) sb.Append(text);
            else sb.Append(", ").Append(Mnemonics.Strip(c.Header)).Append(": ").Append(text);
        }
        return sb.ToString();
    }

    /// <summary>Bounds of a row (view index), scrolled (may lie outside the table).</summary>
    internal Rect GetRowBounds(int view)
    {
        var rows = RowsArea;
        return new Rect(rows.X, rows.Y + view * RowHeight - _scrollY, rows.Width, RowHeight);
    }

    /// <summary>Changes the selection on behalf of an assistive technology.</summary>
    internal void SelectFromAutomation(int view, ListSelectionChange change)
    {
        if (view < 0 || view >= RowCount) return;
        bool changed = change switch
        {
            ListSelectionChange.Add => !_selection.IsSelected(view) && _selection.Toggle(view),
            ListSelectionChange.Remove => _selection.IsSelected(view) && (MultiSelect ? _selection.Toggle(view) : _selection.Clear()),
            _ => _selection.Set(view),
        };
        ScrollRowIntoView(view);
        if (changed) RaiseSelectionChanged();
        else NotifyAutomationStateChanged();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || RowCount == 0) return;
        Prepare();
        int current = _selection.Current;
        int page = Math.Max(1, (int)(_vbar.Viewport / RowHeight) - 1);
        int target = e.Key switch
        {
            Key.Down => current + 1,
            Key.Up => current < 0 ? 0 : current - 1,
            Key.PageDown => current + page,
            Key.PageUp => current - page,
            Key.Home => 0,
            Key.End => RowCount - 1,
            _ => int.MinValue,
        };
        if (target != int.MinValue)
        {
            target = Math.Clamp(target, 0, RowCount - 1);
            if (_selection.MoveTo(target, e.Shift)) RaiseSelectionChanged();
            else NotifyAutomationStateChanged();
            ScrollRowIntoView(target);
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.A when e.Control:
                if (_selection.SelectAll(RowCount)) RaiseSelectionChanged();
                InvalidateVisual();
                e.Handled = MultiSelect;
                break;
            case Key.Enter when current >= 0:
                RowActivated?.Invoke(this, _items![_order[current]]!);
                e.Handled = true;
                break;
            case Key.Apps when current >= 0:
            case Key.F10 when e.Shift && current >= 0:
                if (!_selection.IsSelected(current) && _selection.Set(current)) RaiseSelectionChanged();
                var rowRect = CellRect(current, 0);
                e.Handled = ShowRowMenuAt(new Point(MathF.Max(RowsArea.X, rowRect.X) + 16f, MathF.Min(rowRect.Bottom, RowsArea.Bottom)));
                break;
            case Key.F2 when current >= 0:
                int editable = Columns.FindIndex(c => c.IsEditable && c.IsVisible);
                if (editable >= 0) BeginEdit(current, editable);
                e.Handled = true;
                break;
            case Key.Left:
                _scrollX -= 40f; ClampScroll(); InvalidateVisual(); e.Handled = _hbar.IsNeeded;
                break;
            case Key.Right:
                _scrollX += 40f; ClampScroll(); InvalidateVisual(); e.Handled = _hbar.IsNeeded;
                break;
        }
    }
}

internal static class DrawingContextListExtensions
{
    /// <summary>Rounded clip as a disposable scope.</summary>
    public static RoundedClipScope PushRoundedClipScope(this DrawingContext dc, Rect rect, float radius)
    {
        dc.PushRoundedClip(rect, radius);
        return new RoundedClipScope(dc);
    }

    public readonly struct RoundedClipScope(DrawingContext dc) : IDisposable
    {
        public void Dispose() => dc.PopClip();
    }
}
