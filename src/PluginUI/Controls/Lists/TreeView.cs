// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using System.ComponentModel;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>A node of a <see cref="TreeView"/>: text, optional detail and color, children, expanded state.</summary>
public sealed class TreeNode
{
    /// <summary>Creates a node.</summary>
    /// <param name="text">Text shown.</param>
    /// <param name="tag">Application data.</param>
    public TreeNode(string text, object? tag = null)
    {
        Text = text;
        Tag = tag;
        Children = new TreeNodeCollection(this);
    }

    /// <summary>Text shown.</summary>
    public string Text { get; set; }

    /// <summary>Secondary text shown dimmed on the right (a count, an author…), or <c>null</c>.</summary>
    public string? Detail { get; set; }

    /// <summary>Color of a small dot before the text, or <c>null</c>.</summary>
    public Color? Color { get; set; }

    /// <summary>Application data.</summary>
    public object? Tag { get; set; }

    /// <summary><c>true</c> when the children are shown.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>Shows an expander even without children (children loaded when expanded, see <see cref="TreeView.Expanding"/>).</summary>
    public bool IsFolder { get; set; }

    /// <summary>Child nodes.</summary>
    public TreeNodeCollection Children { get; }

    /// <summary>Parent node (<c>null</c> for a top-level node).</summary>
    public TreeNode? Parent { get; internal set; }

    /// <summary>Depth: 0 for a top-level node.</summary>
    public int Level => Parent is null ? 0 : Parent.Level + 1;

    /// <summary><c>true</c> when the node has children or is a <see cref="IsFolder">folder</see>.</summary>
    public bool HasChildren => Children.Count > 0 || IsFolder;

    /// <summary>Adds a child node and returns it.</summary>
    /// <param name="text">Text shown.</param>
    /// <param name="tag">Application data.</param>
    public TreeNode Add(string text, object? tag = null)
    {
        var node = new TreeNode(text, tag);
        Children.Add(node);
        return node;
    }

    /// <summary>The node and all its descendants, depth first.</summary>
    public IEnumerable<TreeNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var n in child.SelfAndDescendants()) yield return n;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>Children of a <see cref="TreeNode"/> (or the top-level nodes of a <see cref="TreeView"/>): keeps <see cref="TreeNode.Parent"/> up to date.</summary>
public sealed class TreeNodeCollection : Collection<TreeNode>
{
    private readonly TreeNode? _owner;

    internal TreeNodeCollection(TreeNode? owner) => _owner = owner;

    /// <summary>Adds a node and returns it.</summary>
    /// <param name="text">Text shown.</param>
    /// <param name="tag">Application data.</param>
    public TreeNode Add(string text, object? tag = null)
    {
        var node = new TreeNode(text, tag);
        Add(node);
        return node;
    }

    /// <inheritdoc />
    protected override void InsertItem(int index, TreeNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Parent?.Children.Remove(item);
        item.Parent = _owner;
        base.InsertItem(index, item);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, TreeNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        this[index].Parent = null;
        item.Parent = _owner;
        base.SetItem(index, item);
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        this[index].Parent = null;
        base.RemoveItem(index);
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        foreach (var n in this) n.Parent = null;
        base.ClearItems();
    }
}

/// <summary>
/// Virtualized tree (preset banks and categories, sample folders, modulation sources…): expanders, indentation, one
/// selected node, keyboard navigation with type-ahead, a text filter that keeps the ancestors of matching nodes, wheel
/// and slim scrollbar.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Click a node to select it, its arrow (or double-click a folder) to expand or collapse it,
/// double-click a leaf to activate it.</para>
/// <para><b>Keyboard.</b> Up / Down, Page Up / Page Down, Home / End move the selection; Right expands a folder or goes
/// to its first child, Left collapses it or goes to the parent; Enter activates a leaf or toggles a folder; letters
/// jump to the next node starting with them.</para>
/// <para>After changing the nodes, call <see cref="Refresh"/>. Screen readers see a tree of tree items that expand and
/// collapse.</para>
/// </remarks>
/// <example>
/// <code>
/// var tree = new TreeView();
/// var bass = tree.Nodes.Add("Bass");
/// bass.Add("Sub").Detail = "12";
/// tree.NodeActivated += (_, node) => LoadFolder(node.Tag);
/// </code>
/// </example>
public class TreeView : Control
{
    private readonly SlimScrollBar _bar = new(Orientation.Vertical);
    private readonly List<TreeNode> _rows = new();
    private TreeNode? _selected;
    private TreeNode? _hover;
    private float _scroll;
    private bool _barDragging;
    private string? _filter;
    private string _typeAhead = string.Empty;
    private long _typeAheadTime;
    private TreeNode? _pendingScroll;

    /// <summary>Creates an empty tree.</summary>
    public TreeView()
    {
        Focusable = true;
        Nodes = new TreeNodeCollection(null);
    }

    /// <summary>Top-level nodes.</summary>
    [Browsable(false)]
    public TreeNodeCollection Nodes { get; }

    /// <summary>Row height in DIPs.</summary>
    [Category("Layout")]
    public float ItemHeight { get; set; } = 24f;

    /// <summary>Indentation per level in DIPs.</summary>
    [Category("Layout")]
    public float Indent { get; set; } = 16f;

    /// <summary>Draws the frame and background.</summary>
    [Category("Appearance")]
    public bool DrawBackground { get; set; } = true;

    /// <summary>Text shown when no node is visible.</summary>
    [Category("Text")]
    public string? EmptyText { get; set; }

    /// <summary>
    /// Shows only the nodes whose text contains this text (case ignored), with their ancestors, expanded; <c>null</c> or
    /// empty shows everything.
    /// </summary>
    [Category("Behavior")]
    public string? Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value;
            _scroll = 0f;
            Refresh();
        }
    }

    /// <summary>Selected node; setting it expands its ancestors and scrolls it into view.</summary>
    [Browsable(false)]
    public TreeNode? SelectedNode
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (value is not null)
            {
                for (var p = value.Parent; p is not null; p = p.Parent) p.IsExpanded = true;
            }
            _selected = value;
            BuildRows();
            if (value is not null) ScrollIntoView(value);
            SelectionChanged?.Invoke(this);
            InvalidateVisual();
            NotifyAutomationStateChanged();
        }
    }

    /// <summary>Rows currently shown (expanded and matching the filter), top to bottom.</summary>
    [Browsable(false)]
    public IReadOnlyList<TreeNode> VisibleNodes
    {
        get
        {
            BuildRows();
            return _rows;
        }
    }

    /// <summary>Raised when the selection changes.</summary>
    public event Action<TreeView>? SelectionChanged;

    /// <summary>Raised on double-click or Enter on a leaf.</summary>
    public event Action<TreeView, TreeNode>? NodeActivated;

    /// <summary>Raised before a node expands (load its children here when it is a <see cref="TreeNode.IsFolder"/>).</summary>
    public event Action<TreeView, TreeNode>? Expanding;

    /// <summary>Raised after a node expanded or collapsed.</summary>
    public event Action<TreeView, TreeNode>? ExpandedChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(200f, 220f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new TreeViewAutomationPeer(this);

    /// <summary>All nodes, depth first.</summary>
    public IEnumerable<TreeNode> AllNodes() => Nodes.SelectMany(n => n.SelfAndDescendants());

    /// <summary>Re-reads the nodes after they changed (drops a selection that is no longer in the tree).</summary>
    public void Refresh()
    {
        if (_selected is not null && !AllNodes().Contains(_selected))
        {
            _selected = null;
            SelectionChanged?.Invoke(this);
        }
        BuildRows();
        ClampScroll();
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary>Expands or collapses a node.</summary>
    /// <param name="node">Node.</param>
    /// <param name="expanded">New state.</param>
    public void SetExpanded(TreeNode node, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsExpanded == expanded || !node.HasChildren && expanded) return;
        if (expanded) Expanding?.Invoke(this, node);
        node.IsExpanded = expanded;
        if (!expanded && _selected is not null && IsAncestor(node, _selected)) SelectedNode = node;
        BuildRows();
        ClampScroll();
        ExpandedChanged?.Invoke(this, node);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary>Expands every node.</summary>
    public void ExpandAll()
    {
        foreach (var n in AllNodes()) if (n.HasChildren) n.IsExpanded = true;
        Refresh();
    }

    /// <summary>Scrolls so that the node's row is visible (expanding its ancestors).</summary>
    public void ScrollIntoView(TreeNode node)
    {
        for (var p = node.Parent; p is not null; p = p.Parent) p.IsExpanded = true;
        BuildRows();
        int index = _rows.IndexOf(node);
        if (index < 0) return;
        var rows = RowsArea;
        if (rows.Height <= 0f)
        {
            _pendingScroll = node;
            return;
        }
        float top = index * ItemHeight, bottom = top + ItemHeight;
        if (top < _scroll) _scroll = top;
        else if (bottom > _scroll + rows.Height) _scroll = bottom - rows.Height;
        ClampScroll();
        InvalidateVisual();
    }

    private static bool IsAncestor(TreeNode ancestor, TreeNode node)
    {
        for (var p = node.Parent; p is not null; p = p.Parent) if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

    private bool Filtering => !string.IsNullOrEmpty(_filter);

    /// <summary><c>true</c> when the node matches the filter (or there is none).</summary>
    public bool Matches(TreeNode node) => !Filtering || node.Text.Contains(_filter!, StringComparison.OrdinalIgnoreCase);

    private bool Shown(TreeNode node) => !Filtering || node.SelfAndDescendants().Any(Matches);

    /// <summary><c>true</c> when the children of the node are shown (expanded, or kept open by the filter).</summary>
    internal bool IsOpen(TreeNode node) => node.Children.Count > 0 && (node.IsExpanded || Filtering);

    private void BuildRows()
    {
        _rows.Clear();
        void Add(TreeNodeCollection nodes)
        {
            foreach (var n in nodes)
            {
                if (!Shown(n)) continue;
                _rows.Add(n);
                if (IsOpen(n)) Add(n.Children);
            }
        }
        Add(Nodes);
    }

    // ---------------------------------------------------------------- geometry

    private Rect RowsArea => Bounds.Inflate(-1f);

    private void ClampScroll() => _scroll = Math.Clamp(_scroll, 0f, MathF.Max(0f, _rows.Count * ItemHeight - RowsArea.Height));

    /// <summary>Bounds of a node's row (host DIPs, scrolled), or empty when it is not shown.</summary>
    internal Rect GetRowBounds(TreeNode node)
    {
        int index = _rows.IndexOf(node);
        if (index < 0) return Rect.Empty;
        var rows = RowsArea;
        return new Rect(rows.X, rows.Y + index * ItemHeight - _scroll, rows.Width, ItemHeight);
    }

    private Rect ExpanderRect(TreeNode node, Rect row) => new(row.X + 4f + node.Level * Indent, row.Y, 16f, row.Height);

    private int IndexAt(Point p)
    {
        var rows = RowsArea;
        if (!rows.Contains(p)) return -1;
        int index = (int)MathF.Floor((p.Y - rows.Y + _scroll) / ItemHeight);
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    private void UpdateBar()
    {
        var rows = RowsArea;
        _bar.Extent = _rows.Count * ItemHeight;
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
        BuildRows();
        if (_pendingScroll is { } pending && RowsArea.Height > 0f)
        {
            _pendingScroll = null;
            ScrollIntoView(pending);
        }
        ClampScroll();
        UpdateBar();
        if (DrawBackground) dc.FillRoundedRect(widget, 6f, style.Background);
        var rows = RowsArea;
        var text = Theme.Typography.Normal;
        var detailStyle = Theme.Typography.Small with { Align = HorizontalAlignment.Right };
        using (dc.Clip(rows))
        {
            if (_rows.Count == 0 && !string.IsNullOrEmpty(EmptyText))
            {
                dc.DrawText(EmptyText, rows, text with { Align = HorizontalAlignment.Center }, p.TextDisabled);
            }
            int first = Math.Max(0, (int)(_scroll / ItemHeight));
            int last = Math.Min(_rows.Count - 1, (int)((_scroll + rows.Height) / ItemHeight));
            float right = rows.Right - (_bar.IsNeeded ? 10f : 0f);
            for (int i = first; i <= last; i++)
            {
                var node = _rows[i];
                var row = Rect.FromEdges(rows.X, rows.Y + i * ItemHeight - _scroll, right, rows.Y + (i + 1) * ItemHeight - _scroll);
                var bg = row.Inflate(-2f, -1f);
                bool selected = ReferenceEquals(node, _selected);
                if (selected) dc.FillRoundedRect(bg, 4f, IsFocused ? p.Selection : p.Selection.Fade(0.6f));
                else if (ReferenceEquals(node, _hover)) dc.FillRoundedRect(bg, 4f, p.Hover);
                if (selected && IsFocused && Host?.ShowFocusRings == true) dc.DrawRoundedRect(bg.Inflate(-0.5f), 4f, p.Focus, 1f);

                var expander = ExpanderRect(node, row);
                if (node.HasChildren)
                {
                    var c = expander.Center;
                    var color = p.TextDim;
                    if (IsOpen(node) || node.IsExpanded)
                        dc.DrawPolyline([new Point(c.X - 4f, c.Y - 2f), new Point(c.X, c.Y + 2f), new Point(c.X + 4f, c.Y - 2f)], color, 1.5f, StrokeStyle.Round);
                    else
                        dc.DrawPolyline([new Point(c.X - 2f, c.Y - 4f), new Point(c.X + 2f, c.Y), new Point(c.X - 2f, c.Y + 4f)], color, 1.5f, StrokeStyle.Round);
                }
                float x = expander.Right + 2f;
                if (node.Color is { } dot)
                {
                    dc.FillCircle(new Point(x + 4f, row.Center.Y), 4f, dot);
                    x += 13f;
                }
                float detailWidth = 0f;
                if (node.Detail is { Length: > 0 } detail)
                {
                    detailWidth = MathF.Min(row.Width * 0.4f, TextMeasurer.Measure(detail, detailStyle).Width + 8f);
                    dc.DrawText(detail, Rect.FromEdges(right - detailWidth - 6f, row.Y, right - 6f, row.Bottom), detailStyle, p.TextDim);
                }
                bool dim = Filtering && !Matches(node);
                var textStyle = Filtering && !dim ? text with { Weight = FontWeight.SemiBold } : text;
                dc.DrawText(node.Text, Rect.FromEdges(x, row.Y, right - detailWidth - 8f, row.Bottom), textStyle, dim ? p.TextDim : selected ? p.Text : p.Text.WithAlpha(0.88f));
            }
        }
        _bar.IsHovered = _barDragging || (IsMouseOver && _bar.HitTest(Host?.MousePosition ?? default));
        _bar.Render(dc, p, IsMouseOver || _barDragging ? 1f : 0.6f);
        if (DrawBackground) dc.DrawRoundedRect(widget.Inflate(-0.5f), 6f, IsFocused ? style.Accent.WithAlpha(0.7f) : style.Frame, 1f);
    }

    /// <inheritdoc />
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        // Focus is shown on the frame and the selected row instead of the default ring.
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
        BuildRows();
        int index = _bar.HitTest(e.Position) ? -1 : IndexAt(e.Position);
        var hover = index >= 0 ? _rows[index] : null;
        if (!ReferenceEquals(hover, _hover))
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left) return;
        e.Handled = true;
        BuildRows();
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
        var node = _rows[index];
        var row = GetRowBounds(node);
        if (node.HasChildren && ExpanderRect(node, row).Inflate(2f, 0f).Contains(e.Position))
        {
            SetExpanded(node, !node.IsExpanded);
            return;
        }
        SelectedNode = node;
        if (e.ClickCount == 2) Activate(node);
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_barDragging) _bar.Release();
        _barDragging = false;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _bar.Release();
        _barDragging = false;
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

    private void Activate(TreeNode node)
    {
        if (node.HasChildren) SetExpanded(node, !node.IsExpanded);
        else NodeActivated?.Invoke(this, node);
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        BuildRows();
        if (_rows.Count == 0) return;
        int current = _selected is null ? -1 : _rows.IndexOf(_selected);
        int page = Math.Max(1, (int)(RowsArea.Height / ItemHeight) - 1);
        int target = e.Key switch
        {
            Key.Down => current + 1,
            Key.Up => current < 0 ? 0 : current - 1,
            Key.PageDown => current + page,
            Key.PageUp => current - page,
            Key.Home => 0,
            Key.End => _rows.Count - 1,
            _ => int.MinValue,
        };
        if (target != int.MinValue)
        {
            SelectedNode = _rows[Math.Clamp(target, 0, _rows.Count - 1)];
            e.Handled = true;
            return;
        }
        var node = _selected;
        switch (e.Key)
        {
            case Key.Right when node is not null && node.HasChildren:
                if (!IsOpen(node) && !node.IsExpanded) SetExpanded(node, true);
                else if (node.Children.Count > 0 && _rows.IndexOf(node) + 1 < _rows.Count && ReferenceEquals(_rows[_rows.IndexOf(node) + 1].Parent, node)) SelectedNode = _rows[_rows.IndexOf(node) + 1];
                e.Handled = true;
                return;
            case Key.Left when node is not null:
                if (node.IsExpanded && node.HasChildren) SetExpanded(node, false);
                else if (node.Parent is { } parent) SelectedNode = parent;
                e.Handled = true;
                return;
            case Key.Enter when node is not null:
                Activate(node);
                e.Handled = true;
                return;
        }
        if (!e.Control && (e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9))
        {
            TypeAhead((char)e.Key, current);
            e.Handled = true;
        }
    }

    private void TypeAhead(char c, int current)
    {
        long now = Environment.TickCount64;
        _typeAhead = now - _typeAheadTime > 900 ? c.ToString() : _typeAhead + c;
        _typeAheadTime = now;
        int start = Math.Max(0, current + (_typeAhead.Length == 1 ? 1 : 0));
        for (int k = 0; k < _rows.Count; k++)
        {
            int i = (start + k) % _rows.Count;
            if (_rows[i].Text.StartsWith(_typeAhead, StringComparison.OrdinalIgnoreCase))
            {
                SelectedNode = _rows[i];
                return;
            }
        }
    }
}

/// <summary>Peer of a <see cref="TreeView"/>: a tree whose children are the top-level tree items shown.</summary>
/// <param name="owner">The tree.</param>
public class TreeViewAutomationPeer(TreeView owner) : ElementAutomationPeer<TreeView>(owner), ISelectionPattern
{
    private readonly Dictionary<TreeNode, TreeItemAutomationPeer> _items = new();
    private TreeNode? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Tree;

    /// <summary>Peer of a node.</summary>
    /// <param name="node">Node of the tree.</param>
    public TreeItemAutomationPeer GetItem(TreeNode node)
    {
        if (!_items.TryGetValue(node, out var peer)) _items[node] = peer = new TreeItemAutomationPeer(this, node);
        return peer;
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        foreach (var n in Owner.VisibleNodes) if (n.Parent is null) children.Add(GetItem(n));
    }

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget() => Owner.SelectedNode is { } node && Owner.VisibleNodes.Contains(node) ? GetItem(node) : this;

    /// <inheritdoc />
    public IReadOnlyList<AutomationPeer> GetSelection() => Owner.SelectedNode is { } node ? [GetItem(node)] : [];

    /// <inheritdoc />
    public bool CanSelectMultiple => false;

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        var selected = Owner.SelectedNode;
        if (!ReferenceEquals(selected, _reported))
        {
            if (_reported is not null && _items.TryGetValue(_reported, out var old)) old.RaisePropertyChanged(AutomationProperty.IsSelected, true, false);
            _reported = selected;
            if (selected is not null)
            {
                var item = GetItem(selected);
                item.RaisePropertyChanged(AutomationProperty.IsSelected, false, true);
                item.RaiseEvent(AutomationEvent.ElementSelected);
            }
        }
        foreach (var peer in _items.Values) peer.ReportExpandCollapse();
        Host?.UpdateAutomationFocus();
    }
}

/// <summary>Peer of a node of a <see cref="TreeView"/>: a tree item that expands, collapses and can be selected.</summary>
public sealed class TreeItemAutomationPeer : ItemAutomationPeer, IExpandCollapsePattern, ISelectionItemPattern
{
    private readonly TreeViewAutomationPeer _tree;
    private ExpandCollapseState? _reportedState;

    internal TreeItemAutomationPeer(TreeViewAutomationPeer tree, TreeNode node) : base(tree)
    {
        _tree = tree;
        Node = node;
    }

    /// <summary>The node.</summary>
    public TreeNode Node { get; }

    private TreeView Tree => _tree.Owner;

    /// <inheritdoc />
    protected override bool Exists => Tree.VisibleNodes.Contains(Node);

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.TreeItem;

    /// <inheritdoc />
    public override string Name => Node.Detail is { Length: > 0 } detail ? Node.Text + ", " + detail : Node.Text;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => _tree.IsEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Tree.GetRowBounds(Node);

    private IList<TreeNode> Siblings => Node.Parent is { } parent ? parent.Children : Tree.Nodes;

    /// <inheritdoc />
    public override int PositionInSet => Siblings.IndexOf(Node) + 1;

    /// <inheritdoc />
    public override int SizeOfSet => Siblings.Count;

    /// <inheritdoc />
    public override AutomationPeer? GetParent() => Node.Parent is { } parent ? _tree.GetItem(parent) : _tree;

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        if (!Tree.IsOpen(Node)) return;
        foreach (var child in Node.Children) if (Tree.VisibleNodes.Contains(child)) children.Add(_tree.GetItem(child));
    }

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState => !Node.HasChildren ? ExpandCollapseState.LeafNode
        : Tree.IsOpen(Node) ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <inheritdoc />
    public void Expand()
    {
        ThrowIfNotEnabled();
        if (!Node.HasChildren) throw new InvalidOperationException("The node has no children.");
        Tree.SetExpanded(Node, true);
    }

    /// <inheritdoc />
    public void Collapse()
    {
        ThrowIfNotEnabled();
        if (!Node.HasChildren) throw new InvalidOperationException("The node has no children.");
        Tree.SetExpanded(Node, false);
    }

    /// <inheritdoc />
    public bool IsSelected => ReferenceEquals(Tree.SelectedNode, Node);

    /// <inheritdoc />
    public AutomationPeer? SelectionContainer => _tree;

    /// <inheritdoc />
    public void Select()
    {
        ThrowIfNotEnabled();
        Tree.SelectedNode = Node;
    }

    /// <inheritdoc />
    public void AddToSelection() => Select();

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        if (IsSelected) Tree.SelectedNode = null;
    }

    /// <inheritdoc />
    public override void SetFocus()
    {
        Tree.SelectedNode = Node;
        Tree.Focus();
    }

    internal void ReportExpandCollapse()
    {
        var state = ExpandCollapseState;
        if (state == _reportedState) return;
        if (_reportedState is not null) RaisePropertyChanged(AutomationProperty.ExpandCollapseState, _reportedState, state);
        _reportedState = state;
    }
}
