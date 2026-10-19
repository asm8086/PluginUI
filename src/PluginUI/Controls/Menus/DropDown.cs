// 2026/10/19 20:27:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Drop-down list (combo box) for a discrete value: shows the selected item and a chevron, opens a menu with a
/// check mark on the current item. Items come from <see cref="Items"/> or the parameter's state labels; names such as
/// <c>"Bass/Sub"</c> are grouped into sub-menus when <see cref="SubmenuSeparator"/> is set.
/// <see cref="IsItemAvailable"/> hides the items that do not apply in the current context (the menu omits them and the
/// wheel and arrow keys skip them), without changing the indices of the others.
/// </summary>
public class DropDown : ValueControl
{
    private IReadOnlyList<string>? _items;
    private bool _menuOpen;

    /// <summary>Creates a drop-down.</summary>
    public DropDown() { }

    /// <summary>Creates a drop-down with local items.</summary>
    public DropDown(params string[] items) => Items = items;

    /// <summary>Item texts (null = parameter state labels).</summary>
    [Category("Value")]
    public IReadOnlyList<string>? Items
    {
        get => _items;
        set
        {
            _items = value;
            if (value is { Count: > 1 } && Parameter is null) StepCount = value.Count - 1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Character splitting item names into sub-menu groups (e.g. '/'), or null for a flat menu.</summary>
    [Category("Behavior")]
    public char? SubmenuSeparator { get; set; }

    /// <summary>
    /// Tells whether the item at an index can be chosen (null = all items). Unavailable items are left out of the menu
    /// and skipped by the wheel and the arrow keys; the selected item stays displayed even when it is unavailable.
    /// </summary>
    [Browsable(false)]
    public Func<int, bool>? IsItemAvailable { get; set; }

    /// <summary>Text shown when there are no items.</summary>
    [Category("Text")]
    public string? Placeholder { get; set; }

    /// <summary>Item labels in effect.</summary>
    public IReadOnlyList<string> Labels => StateLabels.For(this, _items);

    /// <summary>Number of items.</summary>
    public int Count => Labels.Count;

    /// <summary>Selected item index.</summary>
    [Category("Value")]
    public int SelectedIndex
    {
        get => Count == 0 ? -1 : Math.Clamp(StateIndex, 0, Count - 1);
        set => Value = Count <= 1 ? 0.0 : Math.Clamp(value, 0, Count - 1) / (double)(Count - 1);
    }

    /// <summary>Selected item text (null when empty).</summary>
    public string? SelectedText => SelectedIndex >= 0 ? DisplayName(Labels[SelectedIndex]) : null;

    /// <summary><c>true</c> while the menu is open.</summary>
    public bool IsMenuOpen => _menuOpen;

    /// <inheritdoc />
    public override string? ValueText => null;

    private TextStyle ItemStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Left };

    private string DisplayName(string item)
        => SubmenuSeparator is char sep && item.LastIndexOf(sep) is int i and >= 0 ? item[(i + 1)..] : item;

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            float w = 60f;
            foreach (var l in Labels) w = MathF.Max(w, TextMeasurer.Measure(DisplayName(l), ItemStyle).Width);
            float h = Theme.Metrics.ControlHeight;
            if (EffectiveStyle.ShowLabel && !string.IsNullOrEmpty(Label)) h += EffectiveStyle.LabelText.Size * 1.5f;
            return new Size(MathF.Min(260f, w + 36f), h);
        }
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        bool enabled = IsEffectivelyEnabled;
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.35f) });
        dc.FillRoundedRect(widget, radius, _menuOpen ? style.Pressed : style.Background);
        if (IsMouseOver && enabled && !_menuOpen) dc.FillRoundedRect(widget, radius, style.Hover);
        var frame = _menuOpen ? style.Accent : style.Frame;
        if (style.DrawFrame || _menuOpen) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, frame, _menuOpen ? 1.5f : style.FrameThickness);

        var text = SelectedText ?? Placeholder;
        var color = !enabled ? p.TextDisabled : SelectedText is null ? p.TextDim : p.Text;
        dc.DrawText(text, Rect.FromEdges(widget.X + 8f, widget.Y, widget.Right - 22f, widget.Bottom), ItemStyle, color);
        var chev = new Point(widget.Right - 12f, widget.Center.Y);
        Glyphs.Chevron(dc, chev, 8f, enabled ? (_menuOpen ? style.Accent : p.TextDim) : p.TextDisabled, up: _menuOpen);
    }

    /// <summary>Opens the menu.</summary>
    public void OpenMenu()
    {
        if (Host is null || Count == 0 || _menuOpen) return;
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        _menuOpen = true;
        InvalidateVisual();
        Host.ShowMenu(BuildMenu(), widget, PopupPlacement.Below, widget.Width, () =>
        {
            _menuOpen = false;
            InvalidateVisual();
        });
    }

    /// <summary>Builds the menu entries (override to add custom entries).</summary>
    protected virtual IList<MenuItem> BuildMenu()
    {
        var labels = Labels;
        int selected = SelectedIndex;
        var root = new List<MenuItem>();
        var groups = new Dictionary<string, MenuItem>();
        for (int i = 0; i < labels.Count; i++)
        {
            if (IsItemAvailable is { } available && !available(i)) continue;
            int index = i;
            string label = labels[i];
            var item = new MenuItem(DisplayName(label), () => SelectByUser(index)) { IsChecked = i == selected };
            if (SubmenuSeparator is char sep && label.LastIndexOf(sep) is int cut and > 0)
            {
                string group = label[..cut];
                if (!groups.TryGetValue(group, out var parent))
                {
                    parent = new MenuItem(group);
                    groups[group] = parent;
                    root.Add(parent);
                }
                parent.Items.Add(item);
                if (item.IsChecked) parent.IsChecked = true;
            }
            else
            {
                root.Add(item);
            }
        }
        return root;
    }

    private void SelectByUser(int index)
    {
        if (Count == 0) return;
        SetValueByUser(Count <= 1 ? 0.0 : Math.Clamp(index, 0, Count - 1) / (double)(Count - 1));
    }

    // Moves to the next available item in a direction; stays put when there is none.
    private void StepByUser(int direction)
    {
        int count = Count;
        for (int i = SelectedIndex + direction; i >= 0 && i < count; i += direction)
        {
            if (IsItemAvailable is { } available && !available(i)) continue;
            SelectByUser(i);
            return;
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        OpenMenu();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        if (!IsEffectivelyEnabled || Count == 0) return;
        // Wheel up = previous item, like native combo boxes.
        StepByUser(-Math.Sign(e.WheelDelta));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        if (IsEffectivelyEnabled && Count > 0)
        {
            switch (e.Key)
            {
                case Key.Down or Key.Right: StepByUser(1); e.Handled = true; return;
                case Key.Up or Key.Left: StepByUser(-1); e.Handled = true; return;
                case Key.Enter or Key.Space or Key.F4: OpenMenu(); e.Handled = true; return;
            }
        }
        base.OnKeyDown(e);
    }
}
