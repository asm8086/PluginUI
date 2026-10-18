// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Collections;

namespace PluginUI;

/// <summary>
/// Element with children, an optional background/border and padding. Base class of every layout container.
/// </summary>
/// <remarks>
/// The default layout stacks every child over the whole content area (like a single-cell grid).
/// Derive and override <see cref="UIElement.MeasureOverride"/> / <see cref="UIElement.ArrangeOverride"/> to
/// implement a custom layout (see <see cref="StackPanel"/> for a short example).
/// </remarks>
public class Panel : UIElement
{
    private Thickness _padding;

    /// <summary>Creates an empty panel.</summary>
    public Panel()
    {
        Children = new ElementCollection(this);
    }

    /// <summary>Children, drawn in order (last = topmost).</summary>
    public ElementCollection Children { get; }

    /// <inheritdoc />
    public override IReadOnlyList<UIElement> VisualChildren => Children;

    /// <summary>Background fill (null = transparent).</summary>
    [Category("Appearance")]
    public Paint? Background { get; set; }

    /// <summary>Border color (transparent = none).</summary>
    [Category("Appearance")]
    public Color BorderColor { get; set; } = Color.Transparent;

    /// <summary>Border thickness in DIPs.</summary>
    [Category("Appearance")]
    public float BorderThickness { get; set; } = 1f;

    /// <summary>Corner radius of the background and border.</summary>
    [Category("Appearance")]
    public float CornerRadius { get; set; }

    /// <summary>Drop shadow color (transparent = none).</summary>
    [Category("Appearance")]
    public Color ShadowColor { get; set; } = Color.Transparent;

    /// <summary>Drop shadow blur radius.</summary>
    [Category("Appearance")]
    public float ShadowBlur { get; set; } = 8f;

    /// <summary>Drop shadow offset.</summary>
    [Category("Appearance")]
    public Point ShadowOffset { get; set; } = new(0f, 2f);

    /// <summary>Inner spacing between the border and the children.</summary>
    [Category("Layout")]
    public Thickness Padding { get => _padding; set { if (_padding != value) { _padding = value; InvalidateMeasure(); } } }

    /// <summary>Content area (bounds minus padding).</summary>
    protected Rect ContentBounds => Bounds.Deflate(_padding);

    /// <summary>Adds children (fluent).</summary>
    public Panel Add(params UIElement[] elements)
    {
        foreach (var e in elements) Children.Add(e);
        return this;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var inner = new Size(Math.Max(0f, available.Width - _padding.Horizontal), Math.Max(0f, available.Height - _padding.Vertical));
        float w = 0f, h = 0f;
        foreach (var child in Children)
        {
            child.Measure(inner);
            w = MathF.Max(w, child.DesiredSize.Width);
            h = MathF.Max(h, child.DesiredSize.Height);
        }
        return new Size(w + _padding.Horizontal, h + _padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var inner = bounds.Deflate(_padding);
        foreach (var child in Children) child.Arrange(inner);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        if (!ShadowColor.IsTransparent) dc.DrawShadow(Bounds, CornerRadius, ShadowBlur, ShadowOffset, ShadowColor);
        if (Background is not null)
        {
            if (CornerRadius > 0f) dc.FillRoundedRect(Bounds, CornerRadius, Background);
            else dc.FillRect(Bounds, Background);
        }
        if (!BorderColor.IsTransparent && BorderThickness > 0f)
        {
            var r = Bounds.Inflate(-BorderThickness * 0.5f);
            if (CornerRadius > 0f) dc.DrawRoundedRect(r, CornerRadius, BorderColor, BorderThickness);
            else dc.DrawRect(r, BorderColor, BorderThickness);
        }
    }
}

/// <summary>
/// Panel holding a single <see cref="Content"/> element.
/// </summary>
public class ContentPanel : Panel
{
    /// <summary>The single child (null = empty).</summary>
    [Browsable(false)]
    public UIElement? Content
    {
        get => Children.Count > 0 ? Children[0] : null;
        set
        {
            Children.Clear();
            if (value is not null) Children.Add(value);
        }
    }
}

/// <summary>
/// Children collection of a <see cref="Panel"/>: maintains parent links, host attachment and layout invalidation.
/// </summary>
public sealed class ElementCollection : IList<UIElement>, IReadOnlyList<UIElement>
{
    private readonly UIElement _owner;
    private readonly List<UIElement> _items = new();

    internal ElementCollection(UIElement owner) => _owner = owner;

    /// <inheritdoc cref="IList{T}.this" />
    public UIElement this[int index]
    {
        get => _items[index];
        set
        {
            Detach(_items[index]);
            _items[index] = value;
            Attach(value);
        }
    }

    /// <inheritdoc cref="ICollection{T}.Count" />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void Add(UIElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        Attach(item);
    }

    /// <inheritdoc />
    public void Insert(int index, UIElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        Attach(item);
    }

    /// <inheritdoc />
    public bool Remove(UIElement item)
    {
        if (!_items.Remove(item)) return false;
        Detach(item);
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        Detach(item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        var items = _items.ToArray();
        _items.Clear();
        foreach (var item in items) Detach(item);
    }

    /// <summary>Moves a child to the end (drawn on top).</summary>
    public void BringToFront(UIElement item)
    {
        if (_items.Remove(item)) _items.Add(item);
        _owner.InvalidateVisual();
    }

    /// <inheritdoc />
    public bool Contains(UIElement item) => _items.Contains(item);

    /// <inheritdoc />
    public int IndexOf(UIElement item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void CopyTo(UIElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<UIElement> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    private void Attach(UIElement item)
    {
        if (item.Parent is Panel previous && !ReferenceEquals(previous, _owner)) previous.Children.Remove(item);
        item.Parent = _owner;
        if (_owner.Host is not null) item.AttachTo(_owner.Host);
        _owner.InvalidateMeasure();
    }

    private void Detach(UIElement item)
    {
        item.DetachFromHost();
        if (ReferenceEquals(item.Parent, _owner)) item.Parent = null;
        _owner.InvalidateMeasure();
    }
}

/// <summary>
/// Menu entry used by context menus, drop-downs and menu buttons.
/// </summary>
public sealed class MenuItem
{
    /// <summary>Creates an entry.</summary>
    public MenuItem(string text, Action? action = null)
    {
        Text = text;
        Action = action;
    }

    private MenuItem() => Text = string.Empty;

    /// <summary>A separator line.</summary>
    public static MenuItem Separator() => new() { IsSeparator = true };

    /// <summary>Displayed text.</summary>
    [Category("Text")]
    public string Text { get; set; }

    /// <summary>Shortcut hint displayed on the right (informative only).</summary>
    [Category("Text")]
    public string? ShortcutText { get; set; }

    /// <summary>Invoked when the entry is chosen.</summary>
    [Browsable(false)]
    public Action? Action { get; set; }

    /// <summary>Shows a check mark.</summary>
    [Category("Value")]
    public bool IsChecked { get; set; }

    /// <summary>Selectable when <c>true</c>.</summary>
    [Category("Behavior")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Separator entry.</summary>
    [Category("Behavior")]
    public bool IsSeparator { get; private init; }

    /// <summary>Sub-menu entries (non-empty = this entry opens a sub-menu).</summary>
    public List<MenuItem> Items { get; } = new();

    /// <summary>Free user data.</summary>
    [Browsable(false)]
    public object? Tag { get; set; }

    /// <summary><c>true</c> if the entry opens a sub-menu.</summary>
    public bool HasSubmenu => Items.Count > 0;
}
