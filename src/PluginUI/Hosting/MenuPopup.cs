// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Custom-drawn popup menu with check marks, shortcuts, separators, sub-menus, keyboard navigation and scrolling.
/// Usually shown through <see cref="UIHost.ShowMenu(IList{MenuItem}, Rect, PopupPlacement, float, Action?)"/>.
/// </summary>
public class MenuPopup : Popup
{
    private const float ItemHeight = 24f;
    private const float SeparatorHeight = 9f;
    private const float Pad = 4f;
    private const float CheckColumn = 22f;
    private const float ArrowColumn = 18f;

    private int _highlight = -1;
    private float _scroll;
    private MenuPopup? _child;

    /// <summary>Creates a menu.</summary>
    public MenuPopup(IList<MenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
        CapturesKeyboard = true;
        Placement = PopupPlacement.AtPoint;
    }

    /// <summary>Entries.</summary>
    public IList<MenuItem> Items { get; }

    /// <summary>Menu that opened this sub-menu (null for a root menu).</summary>
    public MenuPopup? ParentMenu { get; private set; }

    /// <summary>Raised on the root menu when an entry is chosen.</summary>
    public event Action<MenuPopup, MenuItem>? ItemChosen;

    /// <summary>
    /// Raised on the root menu when Left (-1) or Right (+1) asks for the neighbouring menu of a menu bar: Left in the
    /// root menu, Right on an entry without sub-menu. Without handler the keys do nothing there.
    /// </summary>
    public event Action<MenuPopup, int>? NavigateSibling;

    /// <summary>Underlines the mnemonic characters (menus opened from the keyboard). Sub-menus inherit it.</summary>
    public bool ShowMnemonics { get; set; }

    /// <summary>Index of the highlighted entry (-1 = none).</summary>
    public int HighlightedIndex
    {
        get => _highlight;
        set
        {
            value = Math.Clamp(value, -1, Items.Count - 1);
            if (_highlight == value) return;
            _highlight = value;
            EnsureVisible(value);
            InvalidateVisual();
        }
    }

    private TextStyle ItemStyle => Theme.Typography.Normal with { Ellipsis = false };

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float textWidth = 60f, shortcutWidth = 0f, height = Pad * 2f;
        bool anySubmenu = false;
        foreach (var item in Items)
        {
            if (item.IsSeparator) { height += SeparatorHeight; continue; }
            height += ItemHeight;
            textWidth = MathF.Max(textWidth, TextMeasurer.Measure(item.DisplayText, ItemStyle).Width);
            if (item.GetShortcutText(Host?.Strings) is { Length: > 0 } s) shortcutWidth = MathF.Max(shortcutWidth, TextMeasurer.Measure(s, ItemStyle).Width + 24f);
            anySubmenu |= item.HasSubmenu;
        }
        float width = Pad * 2f + CheckColumn + textWidth + shortcutWidth + (anySubmenu ? ArrowColumn : 10f);
        return new Size(MathF.Min(width, MathF.Max(80f, available.Width)), height);
    }

    private float ContentHeight
    {
        get
        {
            float h = Pad * 2f;
            foreach (var item in Items) h += item.IsSeparator ? SeparatorHeight : ItemHeight;
            return h;
        }
    }

    private Rect ItemRect(int index)
    {
        float y = Bounds.Y + Pad - _scroll;
        for (int i = 0; i < index; i++) y += Items[i].IsSeparator ? SeparatorHeight : ItemHeight;
        return new Rect(Bounds.X + Pad, y, Bounds.Width - Pad * 2f, Items[index].IsSeparator ? SeparatorHeight : ItemHeight);
    }

    private int IndexAt(Point p)
    {
        if (!Bounds.Contains(p)) return -1;
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemRect(i).Contains(p)) return Items[i].IsSeparator ? -1 : i;
        }
        return -1;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || Bounds.Height <= 0f) return; // not laid out yet: the first layout shows the top
        var r = ItemRect(index);
        if (r.Y < Bounds.Y + Pad) _scroll -= Bounds.Y + Pad - r.Y;
        else if (r.Bottom > Bounds.Bottom - Pad) _scroll += r.Bottom - (Bounds.Bottom - Pad);
        ClampScroll();
    }

    private void ClampScroll() => _scroll = Math.Clamp(_scroll, 0f, MathF.Max(0f, ContentHeight - Bounds.Height));

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds;
        float radius = Theme.Metrics.CornerRadius;
        dc.DrawShadow(r, radius, 10f, new Point(0f, 3f), p.Shadow);
        dc.FillRoundedRect(r, radius, p.SurfaceRaised);
        dc.DrawRoundedRect(r.Inflate(-0.5f), radius, p.Border);

        using var clip = dc.Clip(r.Inflate(-1f));
        var style = ItemStyle;
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var ir = ItemRect(i);
            if (ir.Bottom < r.Y || ir.Y > r.Bottom) continue;
            if (item.IsSeparator)
            {
                float y = MathF.Round(ir.Center.Y) + 0.5f;
                dc.DrawLine(new Point(ir.X + 6f, y), new Point(ir.Right - 6f, y), p.Border);
                continue;
            }
            bool highlighted = i == _highlight && item.IsEnabled;
            if (highlighted) dc.FillRoundedRect(ir, MathF.Max(0f, radius - 2f), p.Selection);
            var color = !item.IsEnabled ? p.TextDisabled : p.Text;
            if (item.IsChecked)
            {
                var c = new Point(ir.X + CheckColumn * 0.5f, ir.Center.Y);
                ReadOnlySpan<Point> tick = [new(c.X - 4f, c.Y), new(c.X - 1f, c.Y + 3f), new(c.X + 4f, c.Y - 3.5f)];
                dc.DrawPolyline(tick, highlighted ? p.Text : p.Accent, 1.6f, StrokeStyle.Round);
            }
            var textArea = Rect.FromEdges(ir.X + CheckColumn, ir.Y, ir.Right - (item.HasSubmenu ? ArrowColumn : 6f), ir.Bottom);
            string text = Mnemonics.Parse(item.Text, out int mnemonic);
            dc.DrawText(text, textArea, style, color);
            if (ShowMnemonics && mnemonic >= 0) DrawMnemonic(dc, TextMeasurer, text, mnemonic, textArea, style, color);
            if (item.GetShortcutText(Host?.Strings) is { Length: > 0 } shortcut)
            {
                dc.DrawText(shortcut, textArea, style with { Align = HorizontalAlignment.Right }, !item.IsEnabled ? p.TextDisabled : p.TextDim);
            }
            if (item.HasSubmenu)
            {
                var a = new Point(ir.Right - ArrowColumn * 0.5f, ir.Center.Y);
                ReadOnlySpan<Point> arrow = [new(a.X - 2f, a.Y - 4f), new(a.X + 2.5f, a.Y), new(a.X - 2f, a.Y + 4f)];
                dc.FillPolygon(arrow, color);
            }
        }

        if (ContentHeight > r.Height + 0.5f)
        {
            // Scroll indicator.
            float trackH = r.Height - 8f;
            float thumbH = MathF.Max(16f, trackH * r.Height / ContentHeight);
            float t = _scroll / MathF.Max(1f, ContentHeight - r.Height);
            dc.FillRoundedRect(new Rect(r.Right - 4f, r.Y + 4f + (trackH - thumbH) * t, 2f, thumbH), 1f, p.TextDim.WithAlpha(0.5f));
        }
    }

    /// <summary>Underlines character <paramref name="index"/> of a left-aligned, vertically centred single line.</summary>
    internal static void DrawMnemonic(DrawingContext dc, ITextMeasurer measurer, string text, int index, Rect area, TextStyle style, Color color)
    {
        var left = style with { Align = HorizontalAlignment.Left };
        // Prefix widths rather than caret positions: same result, and robust where hit-testing is approximate.
        float x0 = index == 0 ? 0f : measurer.Measure(text[..index], left).Width;
        float x1 = measurer.Measure(text[..(index + 1)], left).Width;
        float x = style.Align switch
        {
            HorizontalAlignment.Center => area.X + (area.Width - measurer.Measure(text, left).Width) * 0.5f,
            HorizontalAlignment.Right => area.Right - measurer.Measure(text, left).Width,
            _ => area.X,
        };
        float y = MathF.Round(area.Center.Y + style.Size * 0.58f) + 0.5f;
        dc.DrawLine(new Point(x + x0, y), new Point(x + x1, y), color);
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Position);
        if (index < 0) return;
        HighlightedIndex = index;
        var item = Items[index];
        if (item.HasSubmenu && item.IsEnabled) OpenSubmenu(index, focusFirst: false);
        else CloseSubmenu();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        e.Handled = true;
        int index = IndexAt(e.Position);
        if (index >= 0) Activate(index);
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll -= e.WheelDelta * ItemHeight * 3f;
        ClampScroll();
        InvalidateVisual();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (_child is null) HighlightedIndex = -1;
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: HighlightedIndex = Next(_highlight, +1); e.Handled = true; return;
            case Key.Up: HighlightedIndex = Next(_highlight < 0 ? Items.Count : _highlight, -1); e.Handled = true; return;
            case Key.Home: HighlightedIndex = Next(-1, +1); e.Handled = true; return;
            case Key.End: HighlightedIndex = Next(Items.Count, -1); e.Handled = true; return;
            case Key.Enter or Key.Space:
                if (_highlight >= 0) Activate(_highlight);
                e.Handled = true;
                return;
            case Key.Right:
                if (_highlight >= 0 && Items[_highlight].HasSubmenu && Items[_highlight].IsEnabled) OpenSubmenu(_highlight, focusFirst: true);
                else Root.NavigateSibling?.Invoke(Root, +1);
                e.Handled = true;
                return;
            case Key.Left when ParentMenu is not null:
                Close();
                e.Handled = true;
                return;
            case Key.Left:
                NavigateSibling?.Invoke(this, -1);
                e.Handled = true;
                return;
        }
        // Mnemonics: a unique match runs (or opens) its entry; otherwise, and for entries without mnemonic, the first
        // letter selects the next matching entry.
        if (e.Modifiers is ModifierKeys.None or ModifierKeys.Shift or ModifierKeys.Alt && e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9)
        {
            char c = (char)e.Key;
            int first = -1, count = 0;
            for (int k = 1; k <= Items.Count; k++)
            {
                int i = (Math.Max(_highlight, -1) + k) % Items.Count;
                var it = Items[i];
                if (it.IsSeparator || !it.IsEnabled) continue;
                char m = it.Mnemonic;
                string plain = it.DisplayText;
                bool match = m != '\0' ? m == c : plain.Length > 0 && char.ToUpperInvariant(plain[0]) == c;
                if (!match) continue;
                if (first < 0) first = i;
                count++;
            }
            if (first >= 0)
            {
                HighlightedIndex = first;
                if (count == 1 && Items[first].Mnemonic == c) Activate(first);
            }
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private int Next(int from, int direction)
    {
        for (int k = 1; k <= Items.Count; k++)
        {
            int i = ((from + direction * k) % Items.Count + Items.Count) % Items.Count;
            if (!Items[i].IsSeparator && Items[i].IsEnabled) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------- activation

    private void Activate(int index)
    {
        var item = Items[index];
        if (!item.IsEnabled || item.IsSeparator) return;
        if (item.HasSubmenu)
        {
            OpenSubmenu(index, focusFirst: true);
            return;
        }
        var root = Root;
        var host = Host;
        root.Close();
        try
        {
            item.Invoke();
            root.ItemChosen?.Invoke(root, item);
        }
        finally
        {
            host?.Invalidate();
        }
    }

    private MenuPopup Root
    {
        get
        {
            var root = this;
            while (root.ParentMenu is not null) root = root.ParentMenu;
            return root;
        }
    }

    private void OpenSubmenu(int index, bool focusFirst)
    {
        var item = Items[index];
        if (_child is { IsOpen: true } && ReferenceEquals(_child.Items, item.Items))
        {
            if (focusFirst && _child._highlight < 0) _child.HighlightedIndex = _child.Next(-1, +1);
            return;
        }
        CloseSubmenu();
        if (Host is null) return;
        var sub = new MenuPopup(item.Items)
        {
            ParentMenu = this,
            ShowMnemonics = ShowMnemonics,
            Anchor = ItemRect(index).Inflate(Pad, Pad),
            Placement = PopupPlacement.Right,
        };
        _child = sub;
        sub.Closed += _ => { if (ReferenceEquals(_child, sub)) _child = null; };
        Host.ShowPopup(sub);
        if (focusFirst) sub.HighlightedIndex = sub.Next(-1, +1);
    }

    private void CloseSubmenu()
    {
        var child = _child;
        _child = null;
        child?.Close();
    }
}
