// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Collections;

namespace PluginUI;

/// <summary>State of one row handed to a custom <see cref="ListBox.ItemRenderer"/>.</summary>
/// <param name="Index">Row index.</param>
/// <param name="IsSelected">Row selected.</param>
/// <param name="IsHovered">Mouse over the row.</param>
/// <param name="IsCurrent">Keyboard cursor row (drawn with a focus outline when the list is focused).</param>
/// <param name="IsEnabled">List enabled.</param>
public readonly record struct ListItemState(int Index, bool IsSelected, bool IsHovered, bool IsCurrent, bool IsEnabled);

/// <summary>
/// Virtualized list of items (presets, files, samples...): only visible rows are drawn, so lists of any length cost the
/// same per frame. Single or extended selection (Ctrl / Shift), keyboard navigation with type-ahead, wheel and slim
/// scrollbar, double-click / Enter activation and optional drag reordering.
/// </summary>
/// <remarks>
/// <see cref="Items"/> accepts any <see cref="IList"/> (e.g. <c>List&lt;string&gt;</c>). After changing the list
/// itself, call <see cref="Refresh"/>.
/// </remarks>
public class ListBox : Control
{
    private readonly SelectionModel _selection = new();
    private readonly SlimScrollBar _bar = new(Orientation.Vertical);
    private IList? _items;
    private float _scroll;
    private int _hover = -1;
    private bool _barDragging;
    private int _pressIndex = -1;
    private Point _pressPosition;
    private int _dropIndex = -1;
    private string _typeAhead = string.Empty;
    private long _typeAheadTime;
    private int _pendingScrollIndex = -1;

    private sealed record ReorderData(ListBox Source, int Index);

    /// <summary>Creates an empty list.</summary>
    public ListBox() { }

    /// <summary>Creates a list over <paramref name="items"/>.</summary>
    public ListBox(IList items) => _items = items;

    /// <summary>Items (any list; strings are shown as is).</summary>
    [Category("Value")]
    public IList? Items
    {
        get => _items;
        set
        {
            _items = value;
            _scroll = 0f;
            _hover = -1;
            if (_selection.Clear()) SelectionChanged?.Invoke(this);
            InvalidateVisual();
        }
    }

    /// <summary>Text of an item (default: <see cref="object.ToString"/>).</summary>
    [Browsable(false)]
    public Func<object, string>? ItemText { get; set; }

    /// <summary>Custom row renderer (replaces the default text row; selection/hover backgrounds are still drawn).</summary>
    [Browsable(false)]
    public Action<DrawingContext, Rect, object, ListItemState>? ItemRenderer { get; set; }

    /// <summary>Row height in DIPs.</summary>
    [Category("Layout")]
    public float ItemHeight { get; set; } = 24f;

    /// <summary>Allows several selected rows (Ctrl+click, Shift+click, Shift+arrows, Ctrl+A).</summary>
    [Category("Behavior")]
    public bool MultiSelect { get => _selection.MultiSelect; set => _selection.MultiSelect = value; }

    /// <summary>Lets the user reorder rows by dragging (the <see cref="Items"/> list must be writable).</summary>
    [Category("Behavior")]
    public bool AllowReorder { get; set; }

    /// <summary>Text shown when the list is empty.</summary>
    [Category("Text")]
    public string? EmptyText { get; set; }

    /// <summary>Draws alternate row backgrounds.</summary>
    [Category("Appearance")]
    public bool Striped { get; set; }

    /// <summary>Draws the frame and background.</summary>
    [Category("Appearance")]
    public bool DrawBackground { get; set; } = true;

    /// <summary>Number of items.</summary>
    public int Count => _items?.Count ?? 0;

    /// <summary>First selected index (-1 = none); setting it selects only that row and scrolls it into view.</summary>
    [Category("Value")]
    public int SelectedIndex
    {
        get => _selection.First;
        set
        {
            value = value < 0 || value >= Count ? -1 : value;
            if (_selection.Set(value)) SelectionChanged?.Invoke(this);
            if (value >= 0) ScrollIntoView(value);
            InvalidateVisual();
        }
    }

    /// <summary>Selected indices in ascending order.</summary>
    public IReadOnlyCollection<int> SelectedIndices => _selection.Selected;

    /// <summary>First selected item.</summary>
    public object? SelectedItem => SelectedIndex >= 0 ? _items![SelectedIndex] : null;

    /// <summary>Raised when the selection changes.</summary>
    public event Action<ListBox>? SelectionChanged;

    /// <summary>Raised on double-click or Enter.</summary>
    public event Action<ListBox, int>? ItemActivated;

    /// <summary>Raised after a drag reorder moved an item (from, to).</summary>
    public event Action<ListBox, int, int>? ItemMoved;

    /// <summary>Re-reads the list after it changed (clamps the selection and the scroll position).</summary>
    public void Refresh()
    {
        if (_selection.Clamp(Count)) SelectionChanged?.Invoke(this);
        ClampScroll();
        InvalidateVisual();
    }

    /// <summary>Scrolls so that the row is visible.</summary>
    public void ScrollIntoView(int index)
    {
        if (index < 0) return;
        var rows = RowsArea;
        if (rows.Height <= 0f)
        {
            _pendingScrollIndex = index; // not laid out yet: apply at the first frame
            return;
        }
        float top = index * ItemHeight, bottom = top + ItemHeight;
        if (top < _scroll) _scroll = top;
        else if (bottom > _scroll + rows.Height) _scroll = bottom - rows.Height;
        ClampScroll();
        InvalidateVisual();
    }

    /// <summary>Text displayed for an item.</summary>
    protected string TextOf(object? item) => item is null ? string.Empty : ItemText?.Invoke(item) ?? item.ToString() ?? string.Empty;

    /// <inheritdoc />
    protected override Size DefaultSize => new(180f, 160f);

    private Rect RowsArea => Bounds.Inflate(-1f);

    private void ClampScroll() => _scroll = Math.Clamp(_scroll, 0f, MathF.Max(0f, Count * ItemHeight - RowsArea.Height));

    private int IndexAt(Point p)
    {
        var rows = RowsArea;
        if (!rows.Contains(p)) return -1;
        int index = (int)MathF.Floor((p.Y - rows.Y + _scroll) / ItemHeight);
        return index >= 0 && index < Count ? index : -1;
    }

    private void UpdateBar()
    {
        var rows = RowsArea;
        _bar.Extent = Count * ItemHeight;
        _bar.Viewport = rows.Height;
        _bar.Offset = _scroll;
        _bar.Track = new Rect(rows.Right - 10f, rows.Y + 2f, 9f, MathF.Max(0f, rows.Height - 4f));
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = 6f;
        if (DrawBackground) dc.FillRoundedRect(widget, radius, style.Background);
        if (_pendingScrollIndex >= 0 && RowsArea.Height > 0f)
        {
            int pending = _pendingScrollIndex;
            _pendingScrollIndex = -1;
            ScrollIntoView(pending);
        }
        ClampScroll();
        UpdateBar();
        var rows = RowsArea;
        int count = Count;
        var text = Theme.Typography.Normal;

        using (dc.Clip(rows))
        {
            if (count == 0 && !string.IsNullOrEmpty(EmptyText))
            {
                dc.DrawText(EmptyText, rows, text with { Align = HorizontalAlignment.Center }, p.TextDisabled);
            }
            int first = Math.Max(0, (int)(_scroll / ItemHeight));
            int last = Math.Min(count - 1, (int)((_scroll + rows.Height) / ItemHeight));
            float right = rows.Right - (_bar.IsNeeded ? 10f : 0f);
            for (int i = first; i <= last; i++)
            {
                var row = Rect.FromEdges(rows.X, rows.Y + i * ItemHeight - _scroll, right, rows.Y + (i + 1) * ItemHeight - _scroll);
                var state = new ListItemState(i, _selection.IsSelected(i), i == _hover, i == _selection.Current, IsEffectivelyEnabled);
                if (Striped && (i & 1) == 1) dc.FillRect(row, p.Hover);
                var bg = row.Inflate(-2f, -1f);
                if (state.IsSelected) dc.FillRoundedRect(bg, 4f, IsFocused ? p.Selection : p.Selection.Fade(0.6f));
                else if (state.IsHovered) dc.FillRoundedRect(bg, 4f, p.Hover);
                if (state.IsCurrent && IsFocused && Host?.ShowFocusRings == true) dc.DrawRoundedRect(bg.Inflate(-0.5f), 4f, p.Focus, 1f);
                object item = _items![i]!;
                if (ItemRenderer is not null) ItemRenderer(dc, row, item, state);
                else dc.DrawText(TextOf(item), row.Deflate(new Thickness(10f, 0f, 6f, 0f)), text, state.IsSelected ? p.Text : p.Text.WithAlpha(0.88f));
            }
            if (_dropIndex >= 0)
            {
                float y = rows.Y + _dropIndex * ItemHeight - _scroll;
                dc.DrawLine(new Point(rows.X + 4f, y), new Point(right - 4f, y), p.Accent, 2f, StrokeStyle.Round);
                dc.FillCircle(new Point(rows.X + 4f, y), 3f, p.Accent);
            }
        }
        _bar.IsHovered = _barDragging || (IsMouseOver && _bar.HitTest(Host?.MousePosition ?? default));
        _bar.Render(dc, p, IsMouseOver || _barDragging ? 1f : 0.6f);
        if (DrawBackground) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, IsFocused ? style.Accent.WithAlpha(0.7f) : style.Frame, 1f);
    }

    /// <inheritdoc />
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        // Focus is shown on the frame and the current row instead of the default ring.
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_barDragging)
        {
            _scroll = _bar.Drag(e.Position);
            InvalidateVisual();
            return;
        }
        if (_pressIndex >= 0 && AllowReorder && IsMouseCaptured && Host is { IsDragging: false } host && (e.Position - _pressPosition).Length > 4f)
        {
            int index = _pressIndex;
            _pressIndex = -1;
            host.BeginDrag(this, new ReorderData(this, index), _ => { _dropIndex = -1; InvalidateVisual(); });
            return;
        }
        int hover = _bar.HitTest(e.Position) ? -1 : IndexAt(e.Position);
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
        else if (_bar.IsNeeded) InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = -1;
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left) return;
        e.Handled = true;
        UpdateBar();
        if (_bar.HitTest(e.Position))
        {
            _scroll = _bar.Press(e.Position);
            _barDragging = true;
            InvalidateVisual();
            return;
        }
        int index = IndexAt(e.Position);
        if (index < 0) return;
        if (e.ClickCount == 2)
        {
            ItemActivated?.Invoke(this, index);
            return;
        }
        if (_selection.Click(index, e.Control, e.Shift)) SelectionChanged?.Invoke(this);
        _pressIndex = index;
        _pressPosition = e.Position;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_barDragging) _bar.Release();
        _barDragging = false;
        _pressIndex = -1;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _bar.Release();
        _barDragging = false;
        _pressIndex = -1;
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.IsHorizontalWheel) return;
        float before = _scroll;
        _scroll -= e.WheelDelta * ItemHeight * 3f;
        ClampScroll();
        if (_scroll != before)
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    // ---------------------------------------------------------------- drag reorder

    /// <inheritdoc />
    protected internal override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Data is not ReorderData data || !ReferenceEquals(data.Source, this) || _items is null || _items.IsReadOnly || _items.IsFixedSize) return;
        var rows = RowsArea;
        int drop = (int)MathF.Round((e.Position.Y - rows.Y + _scroll) / ItemHeight);
        drop = Math.Clamp(drop, 0, Count);
        // Auto-scroll near the edges.
        if (e.Position.Y < rows.Y + ItemHeight * 0.5f) _scroll -= ItemHeight * 0.5f;
        else if (e.Position.Y > rows.Bottom - ItemHeight * 0.5f) _scroll += ItemHeight * 0.5f;
        ClampScroll();
        if (drop != _dropIndex)
        {
            _dropIndex = drop;
        }
        InvalidateVisual();
        e.Accept = true;
    }

    /// <inheritdoc />
    protected internal override void OnDragLeave()
    {
        base.OnDragLeave();
        _dropIndex = -1;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        int drop = _dropIndex;
        _dropIndex = -1;
        if (e.Data is not ReorderData data || _items is null || drop < 0) return;
        int from = data.Index;
        int to = drop > from ? drop - 1 : drop;
        if (to == from || from >= _items.Count) return;
        object? item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        _selection.OnItemMoved(from, to);
        ItemMoved?.Invoke(this, from, to);
        SelectionChanged?.Invoke(this);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Count == 0) return;
        int current = Math.Max(_selection.Current, -1);
        int page = Math.Max(1, (int)(RowsArea.Height / ItemHeight) - 1);
        int target = e.Key switch
        {
            Key.Down => current + 1,
            Key.Up => current < 0 ? 0 : current - 1,
            Key.PageDown => current + page,
            Key.PageUp => current - page,
            Key.Home => 0,
            Key.End => Count - 1,
            _ => int.MinValue,
        };
        if (target != int.MinValue)
        {
            target = Math.Clamp(target, 0, Count - 1);
            if (_selection.MoveTo(target, e.Shift)) SelectionChanged?.Invoke(this);
            ScrollIntoView(target);
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.A when e.Control:
                if (_selection.SelectAll(Count)) SelectionChanged?.Invoke(this);
                e.Handled = MultiSelect;
                InvalidateVisual();
                return;
            case Key.Enter when current >= 0:
                ItemActivated?.Invoke(this, current);
                e.Handled = true;
                return;
            case Key.Space when current >= 0 && MultiSelect:
                if (_selection.Toggle(current)) SelectionChanged?.Invoke(this);
                InvalidateVisual();
                e.Handled = true;
                return;
        }
        if (!e.Control && (e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9))
        {
            TypeAhead((char)e.Key);
            e.Handled = true;
        }
    }

    private void TypeAhead(char c)
    {
        long now = Environment.TickCount64;
        _typeAhead = now - _typeAheadTime > 900 ? c.ToString() : _typeAhead + c;
        _typeAheadTime = now;
        int start = Math.Max(0, _selection.Current + (_typeAhead.Length == 1 ? 1 : 0));
        for (int k = 0; k < Count; k++)
        {
            int i = (start + k) % Count;
            if (TextOf(_items![i]).StartsWith(_typeAhead, StringComparison.OrdinalIgnoreCase))
            {
                if (_selection.MoveTo(i, false)) SelectionChanged?.Invoke(this);
                ScrollIntoView(i);
                return;
            }
        }
    }
}
