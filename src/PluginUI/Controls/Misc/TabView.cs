// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Collections.ObjectModel;

namespace PluginUI;

/// <summary>One page of a <see cref="TabView"/>.</summary>
public sealed class TabPage
{
    /// <summary>Creates a page.</summary>
    public TabPage(string header, UIElement? content = null)
    {
        Header = header;
        Content = content;
    }

    /// <summary>Tab caption.</summary>
    [Category("Text")]
    public string Header { get; set; }

    /// <summary>Page content (attached to the host only while the page is selected).</summary>
    [Browsable(false)]
    public UIElement? Content { get; set; }

    /// <summary>Selectable when <c>true</c>.</summary>
    [Category("Behavior")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Tooltip of the tab.</summary>
    [Category("Text")]
    public string? ToolTip { get; set; }

    /// <summary>Free user data.</summary>
    [Browsable(false)]
    public object? Tag { get; set; }
}

/// <summary>Position of the tab strip.</summary>
public enum TabPlacement
{
    /// <summary>Tabs above the pages.</summary>
    Top,
    /// <summary>Tabs in a column on the left.</summary>
    Left,
}

/// <summary>
/// Tabbed pages: a tab strip (top or left) with an animated selection indicator, and the selected page's content.
/// Only the selected page is attached to the host (unselected pages cost nothing). Keyboard: Ctrl+Tab /
/// Ctrl+Shift+Tab from anywhere inside, arrows when the strip has the focus.
/// </summary>
public class TabView : Panel
{
    private int _selected = -1;
    private int _hover = -1;
    private float _indicatorPos, _indicatorLength;
    private bool _indicatorValid;
    private Animation? _indicatorAnimation;
    private readonly List<Rect> _tabRects = new();

    /// <summary>Creates a tab view.</summary>
    public TabView()
    {
        Pages = new TabPageCollection(this);
        Focusable = true;
        Padding = new Thickness(10f);
    }

    /// <summary>Pages.</summary>
    public Collection<TabPage> Pages { get; }

    /// <summary>Tab strip position.</summary>
    [Category("Layout")]
    public TabPlacement Placement { get; set; } = TabPlacement.Top;

    /// <summary>Height of the strip (top placement) or of each tab (left placement).</summary>
    [Category("Layout")]
    public float TabHeight { get; set; } = 30f;

    /// <summary>Width of the strip in left placement (0 = fit the captions).</summary>
    [Category("Layout")]
    public float StripWidth { get; set; }

    /// <summary>Stretches the tabs over the whole strip width (top placement).</summary>
    [Category("Layout")]
    public bool StretchTabs { get; set; }

    /// <summary>Draws a frame around the page area.</summary>
    [Category("Appearance")]
    public bool DrawFrame { get; set; } = true;

    /// <summary>Animates the selection indicator.</summary>
    [Category("Appearance")]
    public bool AnimateIndicator { get; set; } = true;

    /// <summary>Index of the selected page (-1 = none).</summary>
    [Category("Value")]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Pages.Count == 0 ? -1 : Math.Clamp(value, 0, Pages.Count - 1);
            if (value == _selected) return;
            int previous = _selected;
            _selected = value;
            Children.Clear();
            if (value >= 0 && Pages[value].Content is { } content) Children.Add(content);
            StartIndicatorAnimation();
            if (previous != value) SelectionChanged?.Invoke(this, value);
            InvalidateMeasure();
        }
    }

    /// <summary>Selected page.</summary>
    public TabPage? SelectedPage => _selected >= 0 && _selected < Pages.Count ? Pages[_selected] : null;

    /// <summary>Raised when the selected page changes.</summary>
    public event Action<TabView, int>? SelectionChanged;

    /// <summary>Adds a page (fluent).</summary>
    public TabView AddPage(string header, UIElement content)
    {
        Pages.Add(new TabPage(header, content));
        return this;
    }

    private sealed class TabPageCollection(TabView owner) : Collection<TabPage>
    {
        protected override void InsertItem(int index, TabPage item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.InsertItem(index, item);
            if (owner._selected < 0) owner.SelectedIndex = 0;
            else if (index <= owner._selected) owner._selected++;
            owner.InvalidateMeasure();
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            owner.OnPageRemoved(index);
        }

        protected override void SetItem(int index, TabPage item)
        {
            base.SetItem(index, item);
            if (index == owner._selected) owner.Reselect();
        }

        protected override void ClearItems()
        {
            base.ClearItems();
            owner._selected = -1;
            owner.Children.Clear();
            owner.InvalidateMeasure();
        }
    }

    private void OnPageRemoved(int index)
    {
        if (index < _selected) _selected--;
        else if (index == _selected) Reselect();
        InvalidateMeasure();
    }

    private void Reselect()
    {
        int target = Math.Min(_selected, Pages.Count - 1);
        _selected = -2; // force refresh
        SelectedIndex = target;
        if (Pages.Count == 0) { _selected = -1; Children.Clear(); }
    }

    // ---------------------------------------------------------------- layout

    private TextStyle TabStyle(bool selected) => Theme.Typography.Normal with { Weight = selected ? FontWeight.SemiBold : FontWeight.Normal, Align = HorizontalAlignment.Center };

    private float LeftStripWidth()
    {
        if (StripWidth > 0f) return StripWidth;
        float w = 60f;
        foreach (var page in Pages) w = MathF.Max(w, TextMeasurer.Measure(page.Header, TabStyle(true)).Width + 32f);
        return w;
    }

    private Rect PageArea(Rect bounds) => Placement == TabPlacement.Top
        ? Rect.FromEdges(bounds.X, bounds.Y + TabHeight, bounds.Right, bounds.Bottom)
        : Rect.FromEdges(bounds.X + LeftStripWidth(), bounds.Y, bounds.Right, bounds.Bottom);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        bool top = Placement == TabPlacement.Top;
        float stripW = top ? 0f : LeftStripWidth();
        float stripH = top ? TabHeight : 0f;
        var inner = new Size(MathF.Max(0f, available.Width - stripW - Padding.Horizontal), MathF.Max(0f, available.Height - stripH - Padding.Vertical));
        float w = 0f, h = 0f;
        foreach (var child in Children)
        {
            child.Measure(inner);
            w = MathF.Max(w, child.DesiredSize.Width);
            h = MathF.Max(h, child.DesiredSize.Height);
        }
        float tabsW = 0f;
        if (top) foreach (var page in Pages) tabsW += TextMeasurer.Measure(page.Header, TabStyle(true)).Width + 28f;
        return new Size(MathF.Max(w + Padding.Horizontal + stripW, tabsW), MathF.Max(h + Padding.Vertical + stripH, top ? 0f : Pages.Count * TabHeight));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var area = PageArea(bounds).Deflate(Padding);
        foreach (var child in Children) child.Arrange(area);
        ComputeTabRects(bounds);
        if (!_indicatorValid || _indicatorAnimation is null || _indicatorAnimation.IsFinished) SnapIndicator();
    }

    private void ComputeTabRects(Rect bounds)
    {
        _tabRects.Clear();
        if (Placement == TabPlacement.Top)
        {
            float x = bounds.X;
            float stretch = StretchTabs && Pages.Count > 0 ? bounds.Width / Pages.Count : 0f;
            foreach (var page in Pages)
            {
                float w = stretch > 0f ? stretch : TextMeasurer.Measure(page.Header, TabStyle(true)).Width + 28f;
                _tabRects.Add(new Rect(x, bounds.Y, w, TabHeight));
                x += w;
            }
        }
        else
        {
            float y = bounds.Y + 6f, w = LeftStripWidth();
            foreach (var _ in Pages)
            {
                _tabRects.Add(new Rect(bounds.X, y, w, TabHeight));
                y += TabHeight;
            }
        }
    }

    private (float Pos, float Length) IndicatorTarget()
    {
        if (_selected < 0 || _selected >= _tabRects.Count) return (0f, 0f);
        var r = _tabRects[_selected];
        return Placement == TabPlacement.Top ? (r.X + 8f, r.Width - 16f) : (r.Y + 6f, r.Height - 12f);
    }

    private void SnapIndicator()
    {
        (_indicatorPos, _indicatorLength) = IndicatorTarget();
        _indicatorValid = _tabRects.Count > 0;
    }

    private void StartIndicatorAnimation()
    {
        _indicatorAnimation?.Cancel();
        if (!AnimateIndicator || Host is null || !_indicatorValid) { _indicatorValid = false; return; }
        ComputeTabRects(Bounds);
        var (pos0, len0) = (_indicatorPos, _indicatorLength);
        var (pos1, len1) = IndicatorTarget();
        _indicatorAnimation = Host.Animator.Start(0.22, t =>
        {
            _indicatorPos = pos0 + (pos1 - pos0) * t;
            _indicatorLength = len0 + (len1 - len0) * t;
            InvalidateVisual();
        }, Easing.CubicOut);
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var b = Bounds;
        if (_tabRects.Count != Pages.Count) ComputeTabRects(b);
        var area = PageArea(b);
        float hair = 1f / dc.Scale;

        if (Background is not null) dc.FillRect(b, Background);
        if (DrawFrame)
        {
            dc.FillRoundedRect(area, 6f, p.Surface);
            dc.DrawRoundedRect(area.Inflate(-0.5f), 6f, p.Border, hair);
        }

        bool top = Placement == TabPlacement.Top;
        for (int i = 0; i < Pages.Count && i < _tabRects.Count; i++)
        {
            var page = Pages[i];
            var r = _tabRects[i];
            bool selected = i == _selected;
            if (i == _hover && !selected && page.IsEnabled) dc.FillRoundedRect(r.Inflate(-2f, -3f), 4f, p.Hover);
            var color = !page.IsEnabled ? p.TextDisabled : selected ? p.Text : p.TextDim;
            var textRect = top ? r : r.Deflate(new Thickness(14f, 0f, 8f, 0f));
            dc.DrawText(page.Header, textRect, TabStyle(selected) with { Align = top ? HorizontalAlignment.Center : HorizontalAlignment.Left }, color);
        }

        if (_selected >= 0 && _indicatorLength > 0f)
        {
            var indicator = top
                ? new Rect(_indicatorPos, b.Y + TabHeight - 3f, _indicatorLength, 2.5f)
                : new Rect(b.X + 3f, _indicatorPos, 2.5f, _indicatorLength);
            dc.FillRoundedRect(indicator, 1.25f, p.Accent);
        }

        if (IsFocused && Host?.ShowFocusRings == true && _selected >= 0 && _selected < _tabRects.Count)
        {
            dc.DrawRoundedRect(_tabRects[_selected].Inflate(-2f), 4f, p.Focus, Theme.Metrics.FocusWidth);
        }
    }

    // ---------------------------------------------------------------- input

    private int TabAt(Point point)
    {
        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_tabRects[i].Contains(point)) return i;
        }
        return -1;
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = Host?.HoveredElement == this ? TabAt(e.Position) : -1;
        if (hover != _hover)
        {
            _hover = hover;
            ToolTip = hover >= 0 ? Pages[hover].ToolTip : null;
            InvalidateVisual();
        }
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
        int index = TabAt(e.Position);
        if (index < 0 || !Pages[index].IsEnabled) return;
        SelectedIndex = index;
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Pages.Count == 0) return;
        bool strip = IsFocused;
        bool back = e.Shift;
        if (e.Key == Key.Tab && e.Control) { Step(back ? -1 : 1); e.Handled = true; return; }
        if (!strip) return;
        bool top = Placement == TabPlacement.Top;
        switch (e.Key)
        {
            case Key.Right when top: case Key.Down when !top: Step(1); e.Handled = true; break;
            case Key.Left when top: case Key.Up when !top: Step(-1); e.Handled = true; break;
            case Key.Home: SelectedIndex = 0; e.Handled = true; break;
            case Key.End: SelectedIndex = Pages.Count - 1; e.Handled = true; break;
        }
    }

    private void Step(int direction)
    {
        for (int k = 1; k <= Pages.Count; k++)
        {
            int i = ((_selected + direction * k) % Pages.Count + Pages.Count) % Pages.Count;
            if (Pages[i].IsEnabled)
            {
                SelectedIndex = i;
                return;
            }
        }
    }
}
