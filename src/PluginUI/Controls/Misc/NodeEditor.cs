// 2026/11/08 13:40:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>An input or output of a <see cref="NodeEditorNode"/>.</summary>
public sealed class NodeEditorPort
{
    /// <summary>Creates a port.</summary>
    /// <param name="key">Identifier, unique among the node's inputs (or outputs).</param>
    /// <param name="name">Name shown next to the connector.</param>
    /// <param name="type">Kind of signal ("audio", "midi"…): by default a cable joins ports of the same type.</param>
    /// <param name="channels">Channel count shown on the connector (0 = none shown).</param>
    public NodeEditorPort(string key, string name, string type = "audio", int channels = 0)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Name = name ?? string.Empty;
        Type = type ?? string.Empty;
        Channels = channels;
    }

    /// <summary>Identifier.</summary>
    public string Key { get; }

    /// <summary>Name shown next to the connector.</summary>
    public string Name { get; set; }

    /// <summary>Kind of signal.</summary>
    public string Type { get; }

    /// <summary>Channel count shown on the connector (0 = none).</summary>
    public int Channels { get; set; }

    /// <summary>Color of the connector and its cables (null = the editor's color for <see cref="Type"/>).</summary>
    public Color? Color { get; set; }

    /// <summary>The node (set when the port is given to one).</summary>
    public NodeEditorNode? Node { get; internal set; }

    /// <summary><c>true</c> for an input, <c>false</c> for an output.</summary>
    public bool IsInput { get; internal set; }

    /// <summary>Your data.</summary>
    public object? Tag { get; set; }

    /// <inheritdoc />
    public override string ToString() => Node is null ? Name : $"{Node.Title}.{Name}";
}

/// <summary>A module of a <see cref="NodeEditor"/>: a title, inputs on the left, outputs on the right.</summary>
public class NodeEditorNode
{
    private NodeEditorPort[] _inputs = [];
    private NodeEditorPort[] _outputs = [];

    /// <summary>Creates a node.</summary>
    public NodeEditorNode(string title) => Title = title ?? string.Empty;

    /// <summary>Title in the header.</summary>
    public string Title { get; set; }

    /// <summary>Second line under the title (vendor, latency…; null = none).</summary>
    public string? Subtitle { get; set; }

    /// <summary>Short text at the right of the header ("BYPASS", "MISSING"; null = none).</summary>
    public string? Badge { get; set; }

    /// <summary>Position of the top-left corner in graph units.</summary>
    public float X { get; set; }

    /// <summary>Position of the top-left corner in graph units.</summary>
    public float Y { get; set; }

    /// <summary>Color of the header (null = the theme's raised surface).</summary>
    public Color? Color { get; set; }

    /// <summary>Drawn faded (bypassed, missing).</summary>
    public bool IsDimmed { get; set; }

    /// <summary>Shown with an error frame (the module failed).</summary>
    public bool HasError { get; set; }

    /// <summary>Level 0 … 1 polled every frame for the meter under the header (null = no meter).</summary>
    public Func<float>? Level { get; set; }

    /// <summary>Your data (the module the node stands for).</summary>
    public object? Tag { get; set; }

    /// <summary>Tooltip of the node.</summary>
    public string? ToolTip { get; set; }

    /// <summary>Inputs, top to bottom.</summary>
    public IReadOnlyList<NodeEditorPort> Inputs => _inputs;

    /// <summary>Outputs, top to bottom.</summary>
    public IReadOnlyList<NodeEditorPort> Outputs => _outputs;

    /// <summary>The input with this key, or null.</summary>
    public NodeEditorPort? FindInput(string key) => Array.Find(_inputs, p => p.Key == key);

    /// <summary>The output with this key, or null.</summary>
    public NodeEditorPort? FindOutput(string key) => Array.Find(_outputs, p => p.Key == key);

    /// <summary>Sets the ports.</summary>
    public void SetPorts(IEnumerable<NodeEditorPort> inputs, IEnumerable<NodeEditorPort> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        _inputs = [.. inputs];
        _outputs = [.. outputs];
        foreach (var p in _inputs)
        {
            p.Node = this;
            p.IsInput = true;
        }
        foreach (var p in _outputs)
        {
            p.Node = this;
            p.IsInput = false;
        }
    }

    /// <summary>Width in graph units (computed by the editor from the texts, again before each drawing).</summary>
    public float Width { get; internal set; } = 160f;

    internal bool IsMeasured { get; set; }

    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>A cable of a <see cref="NodeEditor"/>, from an output to an input.</summary>
public sealed class NodeEditorCable
{
    /// <summary>Creates a cable.</summary>
    public NodeEditorCable(NodeEditorPort output, NodeEditorPort input)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <summary>Where the cable starts.</summary>
    public NodeEditorPort Output { get; }

    /// <summary>Where the cable ends.</summary>
    public NodeEditorPort Input { get; }

    /// <summary>Drawn dashed: the cable that closes a loop (it delivers late).</summary>
    public bool IsFeedback { get; set; }

    /// <summary>Text at the middle of the cable ("1 block", "+32"; null = none).</summary>
    public string? Label { get; set; }

    /// <summary>Your data.</summary>
    public object? Tag { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Output} → {Input}";
}

/// <summary>Arguments of <see cref="NodeEditor.ContextMenuOpening"/>: what was clicked, and the menu to fill.</summary>
public sealed class NodeEditorMenuEventArgs : EventArgs
{
    internal NodeEditorMenuEventArgs(NodeEditorNode? node, NodeEditorPort? port, NodeEditorCable? cable, Point graphPosition)
    {
        Node = node;
        Port = port;
        Cable = cable;
        GraphPosition = graphPosition;
    }

    /// <summary>The node clicked (null = none).</summary>
    public NodeEditorNode? Node { get; }

    /// <summary>The connector clicked (null = none).</summary>
    public NodeEditorPort? Port { get; }

    /// <summary>The cable clicked (null = none).</summary>
    public NodeEditorCable? Cable { get; }

    /// <summary>Where, in graph units (to add a module there).</summary>
    public Point GraphPosition { get; }

    /// <summary>The menu shown when it is not empty.</summary>
    public IList<MenuItem> Items { get; } = new List<MenuItem>();
}

/// <summary>
/// Modular graph editor: nodes with inputs on the left and outputs on the right, joined by cables drawn as curves. For a
/// modular host, a patch bay, a routing or modulation view.
/// </summary>
/// <remarks>
/// <para><b>Model.</b> The editor shows <see cref="Nodes"/> and <see cref="Cables"/>; it changes only the node positions
/// and the selection. Everything else is a request: <see cref="CableRequested"/>, <see cref="CableRemoveRequested"/>,
/// <see cref="DeleteRequested"/>, <see cref="NodeOpened"/>, <see cref="ContextMenuOpening"/>. Apply it to your graph,
/// update the lists and call <see cref="UIElement.InvalidateVisual"/>.</para>
/// <para><b>Mouse.</b> Drag a node to move it (its selection with it); drag from a connector to draw a cable to a
/// compatible connector (they light up); drag the end of a cable away from its input to move or remove it. Click a cable
/// to select it. Drag on an empty area to select with a rectangle (Ctrl adds); when the graph does not fit (scroll bars
/// shown), that drag moves the view instead and Shift or Ctrl + drag selects. The wheel scrolls (Shift: sideways),
/// Ctrl+wheel zooms around the mouse; the middle button (or Alt with the left one) pans. Double-click a node to open it; right-click for its menu.</para>
/// <para><b>Keyboard.</b> Ctrl+Left / Ctrl+Right select the previous or next node (left to right), Ctrl+A all; arrows
/// move the selection (Shift = farther); Enter opens the node; Delete removes the selection; Home fits the graph in the
/// view, + and − zoom; Shift+F10 or the Menu key opens the menu of the selection.</para>
/// </remarks>
/// <example>
/// <code>
/// var view = new NodeEditor();
/// var gain = new NodeEditorNode("Gain") { X = 200, Y = 80 };
/// gain.SetPorts([new NodeEditorPort("in", "In", "audio", 2)], [new NodeEditorPort("out", "Out", "audio", 2)]);
/// view.Nodes.Add(gain);
/// view.CableRequested += (_, output, input) => { graph.Connect(...); Sync(); };
/// </code>
/// </example>
public class NodeEditor : Control
{
    private const float HeaderHeight = 22f, SubtitleHeight = 13f, MeterHeight = 4f, RowHeight = 16f, BottomPadding = 7f;
    private const float PortRadius = 4.5f, NarrowestNode = 150f, WidestNode = 320f;
    private const float MinZoom = 0.25f, MaxZoom = 2.5f;
    private static readonly StrokeStyle FeedbackStroke = new(LineCap.Flat, LineCap.Flat, LineJoin.Round, [3f, 2.5f]);

    private readonly List<NodeEditorNode> _nodes = [];
    private readonly List<NodeEditorCable> _cables = [];
    private readonly HashSet<NodeEditorNode> _selection = [];
    private NodeEditorCable? _selectedCable;
    private float _zoom = 1f;
    private Point _offset;
    private Rect _view;
    private bool _fitPending;

    private readonly SlimScrollBar _vbar = new(Orientation.Vertical);
    private readonly SlimScrollBar _hbar = new(Orientation.Horizontal);
    private SlimScrollBar? _barDrag;
    private Rect _barContent;

    private enum DragMode { None, Move, Pan, Band, Cable }

    private DragMode _drag;
    private Point _anchor, _mouse;
    private (NodeEditorNode Node, float X, float Y)[] _origin = [];
    private bool _moved;
    private Point _panOffset;
    private Rect _band;
    private NodeEditorPort? _fixedPort;
    private NodeEditorCable? _detached;
    private NodeEditorPort? _target;
    private RefreshTicker? _ticker;
    private float[] _levels = [];

    /// <summary>Creates an empty editor.</summary>
    public NodeEditor()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>The nodes (draw order: last on top). After changing the list from code, call <see cref="UIElement.InvalidateVisual"/>.</summary>
    [Browsable(false)]
    public IList<NodeEditorNode> Nodes => _nodes;

    /// <summary>The cables. After changing the list from code, call <see cref="UIElement.InvalidateVisual"/>.</summary>
    [Browsable(false)]
    public IList<NodeEditorCable> Cables => _cables;

    /// <summary>Colors of the port types (default: "audio" in the accent color, "midi" in the second accent color).</summary>
    [Browsable(false)]
    public IDictionary<string, Color> TypeColors { get; } = new Dictionary<string, Color>(StringComparer.Ordinal);

    /// <summary>
    /// Tells whether a cable may join an output to an input (null = same <see cref="NodeEditorPort.Type"/>). Only
    /// compatible connectors light up while a cable is drawn.
    /// </summary>
    [Browsable(false)]
    public Func<NodeEditorPort, NodeEditorPort, bool>? CanConnect { get; set; }

    /// <summary>Moves the nodes on a grid of <see cref="GridSize"/> graph units.</summary>
    [Category("Behavior")]
    public bool SnapToGrid { get; set; } = true;

    /// <summary>
    /// Grid step in graph units (default 16, the height of a row of connectors). Nodes snap so that their first connector
    /// lies on the grid: connectors of aligned nodes face each other and their cables are straight.
    /// </summary>
    [Category("Behavior")]
    public float GridSize { get; set; } = 16f;

    /// <summary>Text shown when there is no node.</summary>
    [Category("Appearance")]
    public string? EmptyText { get; set; }

    /// <summary>Zoom factor (0.25 … 2.5).</summary>
    [Category("Layout")]
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, MinZoom, MaxZoom);
            InvalidateVisual();
        }
    }

    /// <summary>Graph point at the top-left corner of the view.</summary>
    [Browsable(false)]
    public Point ViewOffset
    {
        get => _offset;
        set
        {
            _offset = value;
            InvalidateVisual();
        }
    }

    /// <summary>The graph point at the center of the view (where to add a module).</summary>
    [Browsable(false)]
    public Point ViewCenter
    {
        get
        {
            var v = View;
            return ToGraph(v.Center);
        }
    }

    /// <summary>Selected nodes, in list order.</summary>
    [Browsable(false)]
    public IReadOnlyList<NodeEditorNode> SelectedNodes => [.. _nodes.Where(_selection.Contains)];

    /// <summary>Selected cable (null = none).</summary>
    [Browsable(false)]
    public NodeEditorCable? SelectedCable => _selectedCable;

    /// <summary>The user drew a cable from <c>output</c> to <c>input</c> (an input may already have one: replace it if your graph allows one per input).</summary>
    public event Action<NodeEditor, NodeEditorPort, NodeEditorPort>? CableRequested;

    /// <summary>The user pulled a cable off its input, or deleted it.</summary>
    public event Action<NodeEditor, NodeEditorCable>? CableRemoveRequested;

    /// <summary>The user moved nodes (their <see cref="NodeEditorNode.X"/> and <see cref="NodeEditorNode.Y"/> are already updated).</summary>
    public event Action<NodeEditor, IReadOnlyList<NodeEditorNode>>? NodesMoved;

    /// <summary>The user double-clicked a node or pressed Enter on it.</summary>
    public event Action<NodeEditor, NodeEditorNode>? NodeOpened;

    /// <summary>The user pressed Delete: the selected nodes, or the selected cable.</summary>
    public event Action<NodeEditor, IReadOnlyList<NodeEditorNode>, NodeEditorCable?>? DeleteRequested;

    /// <summary>The user asks for a menu (right-click, Shift+F10): fill <see cref="NodeEditorMenuEventArgs.Items"/>.</summary>
    public event Action<NodeEditor, NodeEditorMenuEventArgs>? ContextMenuOpening;

    /// <summary>The selection changed.</summary>
    public event Action<NodeEditor>? SelectionChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(640f, 400f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new NodeEditorAutomationPeer(this);

    // ---------------------------------------------------------------- selection

    /// <summary><c>true</c> when <paramref name="node"/> is selected.</summary>
    public bool IsSelected(NodeEditorNode node) => _selection.Contains(node);

    /// <summary>Selects the given nodes only (and no cable).</summary>
    public void Select(IEnumerable<NodeEditorNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        _selection.Clear();
        foreach (var n in nodes)
        {
            if (_nodes.Contains(n)) _selection.Add(n);
        }
        _selectedCable = null;
        OnSelectionChanged();
    }

    /// <summary>Selects a cable only.</summary>
    public void SelectCable(NodeEditorCable? cable)
    {
        _selection.Clear();
        _selectedCable = cable is not null && _cables.Contains(cable) ? cable : null;
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    // ---------------------------------------------------------------- geometry

    private Rect View => _view.IsEmpty ? Bounds : _view;

    /// <summary>Graph point under a point of the window.</summary>
    public Point ToGraph(Point p)
    {
        var v = View;
        return new Point((p.X - v.X) / _zoom + _offset.X, (p.Y - v.Y) / _zoom + _offset.Y);
    }

    /// <summary>Window point of a graph point.</summary>
    public Point FromGraph(Point g)
    {
        var v = View;
        return new Point(v.X + (g.X - _offset.X) * _zoom, v.Y + (g.Y - _offset.Y) * _zoom);
    }

    /// <summary>Distance from the top of a node to the center of its first connector (first input and first output share it).</summary>
    private static float FirstConnector(NodeEditorNode n) => TopOfRows(n) + RowHeight / 2f;

    private (float X, float Y) Snapped(NodeEditorNode n, float x, float y)
    {
        if (GridSize <= 0f) return (x, y);
        float anchor = FirstConnector(n);
        return (MathF.Round(x / GridSize) * GridSize, MathF.Round((y + anchor) / GridSize) * GridSize - anchor);
    }

    /// <summary>Moves <paramref name="node"/> to the nearest grid position (its first connector on the grid).</summary>
    public void Snap(NodeEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        (node.X, node.Y) = Snapped(node, node.X, node.Y);
        InvalidateVisual();
    }

    private static float TopOfRows(NodeEditorNode n) => HeaderHeight + (n.Subtitle is null ? 0f : SubtitleHeight) + (n.Level is null ? 0f : MeterHeight + 2f);

    /// <summary>The node's rectangle in graph units.</summary>
    public Rect NodeBounds(NodeEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsMeasured) Measure(node);
        int rows = Math.Max(1, Math.Max(node.Inputs.Count, node.Outputs.Count));
        return new Rect(node.X, node.Y, node.Width, TopOfRows(node) + rows * RowHeight + BottomPadding);
    }

    /// <summary>Center of a connector in graph units.</summary>
    public Point PortPosition(NodeEditorPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        var n = port.Node ?? throw new ArgumentException("The port has no node.", nameof(port));
        if (!n.IsMeasured) Measure(n);
        var list = port.IsInput ? n.Inputs : n.Outputs;
        int index = 0;
        for (; index < list.Count; index++)
        {
            if (ReferenceEquals(list[index], port)) break;
        }
        return new Point(port.IsInput ? n.X : n.X + n.Width, n.Y + TopOfRows(n) + (index + 0.5f) * RowHeight);
    }

    private TextStyle TitleStyle => Theme.Typography.Small with { Size = 11.5f, Weight = FontWeight.SemiBold, VerticalAlign = VerticalAlignment.Center };

    private TextStyle PortStyle => Theme.Typography.Small with { Size = 10f, VerticalAlign = VerticalAlignment.Center };

    private TextStyle SmallStyle => Theme.Typography.Small with { Size = 9f, VerticalAlign = VerticalAlignment.Center, Ellipsis = false };

    /// <summary>Computes the node widths from their texts (before drawing and before handling the mouse).</summary>
    private void Layout()
    {
        foreach (var n in _nodes) Measure(n);
    }

    private void Measure(NodeEditorNode n)
    {
        var measurer = TextMeasurer;
        var port = PortStyle;
        var small = SmallStyle;
        float header = measurer.Measure(n.Title, TitleStyle).Width + 20f + (n.Badge is null ? 0f : measurer.Measure(n.Badge, small).Width + 14f);
        float sub = n.Subtitle is null ? 0f : measurer.Measure(n.Subtitle, small).Width + 20f;
        float ins = n.Inputs.Count == 0 ? 0f : n.Inputs.Max(p => measurer.Measure(PortText(p), port).Width);
        float outs = n.Outputs.Count == 0 ? 0f : n.Outputs.Max(p => measurer.Measure(PortText(p), port).Width);
        n.Width = Math.Clamp(MathF.Max(MathF.Max(header, sub), ins + outs + 44f), NarrowestNode, WidestNode);
        n.IsMeasured = true;
    }

    private static string PortText(NodeEditorPort p) => p.Channels > 0 ? $"{p.Name} ({p.Channels.ToString(CultureInfo.InvariantCulture)})" : p.Name;

    private Color TypeColor(string type, Palette p) => TypeColors.TryGetValue(type, out var c) ? c : type switch
    {
        "midi" => p.AccentAlt,
        "audio" => p.Accent,
        _ => p.TextDim,
    };

    private Color PortColor(NodeEditorPort port) => port.Color ?? TypeColor(port.Type, Theme.Palette);

    private bool Compatible(NodeEditorPort output, NodeEditorPort input)
        => !output.IsInput && input.IsInput && (CanConnect?.Invoke(output, input) ?? output.Type == input.Type);

    // ---------------------------------------------------------------- hit testing

    private NodeEditorNode? NodeAt(Point graph)
    {
        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            if (NodeBounds(_nodes[i]).Contains(graph)) return _nodes[i];
        }
        return null;
    }

    private NodeEditorPort? PortAt(Point graph)
    {
        float radius = MathF.Max(PortRadius + 3f, 9f / _zoom);
        NodeEditorPort? best = null;
        float bestDistance = radius;
        foreach (var n in _nodes)
        {
            var b = NodeBounds(n).Inflate(radius);
            if (!b.Contains(graph)) continue;
            foreach (var port in n.Inputs.Concat(n.Outputs))
            {
                float d = Distance(PortPosition(port), graph);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = port;
                }
            }
        }
        return best;
    }

    private NodeEditorCable? CableAt(Point graph)
    {
        float tolerance = MathF.Max(4f, 6f / _zoom);
        Span<Point> points = stackalloc Point[25];
        for (int i = _cables.Count - 1; i >= 0; i--)
        {
            var c = _cables[i];
            if (c.Output.Node is null || c.Input.Node is null) continue;
            Curve(PortPosition(c.Output), PortPosition(c.Input), points);
            for (int k = 1; k < points.Length; k++)
            {
                if (SegmentDistance(graph, points[k - 1], points[k]) <= tolerance) return c;
            }
        }
        return null;
    }

    private static float Distance(Point a, Point b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static float SegmentDistance(Point p, Point a, Point b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float length = dx * dx + dy * dy;
        float t = length <= 0f ? 0f : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0f, 1f);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    private static (Point C1, Point C2) Controls(Point from, Point to)
    {
        float dx = MathF.Max(40f, MathF.Abs(to.X - from.X) * 0.5f);
        return (new Point(from.X + dx, from.Y), new Point(to.X - dx, to.Y));
    }

    /// <summary>Samples the cable's curve (from an output, to an input).</summary>
    private static void Curve(Point from, Point to, Span<Point> points)
    {
        var (c1, c2) = Controls(from, to);
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / (float)(points.Length - 1), u = 1f - t;
            float a = u * u * u, b = 3f * u * u * t, c = 3f * u * t * t, d = t * t * t;
            points[i] = new Point(a * from.X + b * c1.X + c * c2.X + d * to.X, a * from.Y + b * c1.Y + c * c2.Y + d * to.Y);
        }
    }

    // ---------------------------------------------------------------- view

    /// <summary>What the scroll bars cover, in graph units: the nodes (with a margin) and the visible area.</summary>
    private Rect ScrollContent()
    {
        var v = View;
        var visible = new Rect(_offset.X, _offset.Y, v.Width / _zoom, v.Height / _zoom);
        if (_nodes.Count == 0) return visible;
        return _nodes.Select(NodeBounds).Aggregate((a, b) => a.Union(b)).Inflate(16f).Union(visible);
    }

    /// <summary>Places the scroll bars along the right and bottom edges (window pixels).</summary>
    private void UpdateBars()
    {
        var v = View;
        var content = _barDrag is not null ? _barContent : ScrollContent();
        _hbar.Extent = content.Width * _zoom;
        _hbar.Viewport = v.Width;
        _hbar.Offset = Math.Clamp((_offset.X - content.X) * _zoom, 0f, _hbar.MaxOffset);
        _vbar.Extent = content.Height * _zoom;
        _vbar.Viewport = v.Height;
        _vbar.Offset = Math.Clamp((_offset.Y - content.Y) * _zoom, 0f, _vbar.MaxOffset);
        _vbar.Track = new Rect(v.Right - 10f, v.Y + 2f, 9f, MathF.Max(0f, v.Height - 4f - (_hbar.IsNeeded ? 10f : 0f)));
        _hbar.Track = new Rect(v.X + 2f, v.Bottom - 10f, MathF.Max(0f, v.Width - 4f - (_vbar.IsNeeded ? 10f : 0f)), 9f);
    }

    /// <summary><c>true</c> when the graph does not fit in the view (scroll bars shown): horizontally, vertically.</summary>
    [Browsable(false)]
    public (bool Horizontal, bool Vertical) IsScrollable
    {
        get
        {
            UpdateBars();
            return (_hbar.IsNeeded, _vbar.IsNeeded);
        }
    }

    /// <summary>Zooms and scrolls so that every node is visible.</summary>
    /// <remarks>Called before the editor is first shown (its size is not known yet), the fit is done again when it is.</remarks>
    public void ZoomToFit()
    {
        _fitPending = _view.IsEmpty;
        Layout();
        var v = View;
        if (_nodes.Count == 0 || v.Width <= 0f || v.Height <= 0f)
        {
            _zoom = 1f;
            _offset = default;
            InvalidateVisual();
            return;
        }
        var all = _nodes.Select(NodeBounds).Aggregate((a, b) => a.Union(b)).Inflate(24f);
        _zoom = Math.Clamp(MathF.Min(v.Width / all.Width, v.Height / all.Height), MinZoom, 1.25f);
        _offset = new Point(all.Center.X - v.Width / 2f / _zoom, all.Center.Y - v.Height / 2f / _zoom);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        base.ArrangeOverride(bounds);
        // A fit asked before the editor had its size (a graph restored at start) is done again now.
        if (_fitPending && bounds.Width > 0f && bounds.Height > 0f)
        {
            ZoomToFit();
            _fitPending = false;
        }
    }

    /// <summary>Scrolls so that <paramref name="node"/> is visible.</summary>
    public void ScrollIntoView(NodeEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var v = View;
        var b = NodeBounds(node);
        var topLeft = ToGraph(new Point(v.X, v.Y));
        var bottomRight = ToGraph(new Point(v.Right, v.Bottom));
        float x = _offset.X, y = _offset.Y;
        if (b.X < topLeft.X) x -= topLeft.X - b.X + 16f;
        else if (b.Right > bottomRight.X) x += b.Right - bottomRight.X + 16f;
        if (b.Y < topLeft.Y) y -= topLeft.Y - b.Y + 16f;
        else if (b.Bottom > bottomRight.Y) y += b.Bottom - bottomRight.Y + 16f;
        ViewOffset = new Point(x, y);
    }

    private void ZoomAround(Point window, float factor)
    {
        var before = ToGraph(window);
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var v = View;
        _offset = new Point(before.X - (window.X - v.X) / _zoom, before.Y - (window.Y - v.Y) / _zoom);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- live meters

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, _ =>
        {
            if (_levels.Length != _nodes.Count) _levels = new float[_nodes.Count];
            bool changed = false;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Level is not { } level) continue;
                float value = Math.Clamp(level(), 0f, 1f);
                // Falls back slowly, like a peak meter.
                float shown = MathF.Max(value, _levels[i] * 0.85f);
                if (MathF.Abs(shown - _levels[i]) > 0.002f) changed = true;
                _levels[i] = shown < 0.002f ? 0f : shown;
            }
            if (changed) InvalidateVisual();
        }));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        CancelDrag();
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        _view = widget;
        if (_fitPending)
        {
            _fitPending = false;
            ZoomToFit();
        }
        Layout();
        var p = Theme.Palette;
        dc.FillRect(widget, p.Background);
        dc.PushClip(widget);
        DrawGrid(dc, widget, p);

        var transform = Matrix3x2.CreateTranslation(-_offset.X, -_offset.Y) * Matrix3x2.CreateScale(_zoom) * Matrix3x2.CreateTranslation(widget.X, widget.Y);
        dc.PushTransform(transform);

        Span<Point> points = stackalloc Point[33];
        foreach (var c in _cables)
        {
            if (c.Output.Node is null || c.Input.Node is null || ReferenceEquals(c, _detached)) continue;
            DrawCable(dc, c, PortPosition(c.Output), PortPosition(c.Input), points, p);
        }
        for (int i = 0; i < _nodes.Count; i++) DrawNode(dc, _nodes[i], i < _levels.Length ? _levels[i] : 0f, p);

        if (_drag == DragMode.Cable && _fixedPort?.Node is not null)
        {
            var fixedPoint = PortPosition(_fixedPort);
            var free = _target is not null ? PortPosition(_target) : ToGraph(_mouse);
            var (from, to) = _fixedPort.IsInput ? (free, fixedPoint) : (fixedPoint, free);
            Curve(from, to, points);
            dc.DrawPolyline(points, PortColor(_fixedPort).WithAlpha(0.9f), 2f / MathF.Sqrt(_zoom), StrokeStyle.Round);
        }
        dc.PopTransform();

        if (_drag == DragMode.Band) DrawBand(dc, p);
        UpdateBars();
        var mouse = Host?.MousePosition ?? default;
        _vbar.IsHovered = _barDrag == _vbar || (IsMouseOver && _vbar.HitTest(mouse));
        _hbar.IsHovered = _barDrag == _hbar || (IsMouseOver && _hbar.HitTest(mouse));
        float alpha = IsMouseOver || _barDrag is not null ? 1f : 0.6f;
        _vbar.Render(dc, p, alpha);
        _hbar.Render(dc, p, alpha);
        if (_nodes.Count == 0 && !string.IsNullOrEmpty(EmptyText))
        {
            dc.DrawText(EmptyText, widget.Deflate(24f), Theme.Typography.Normal with { Align = HorizontalAlignment.Center, Wrap = true }, p.TextDim);
        }
        dc.PopClip();
    }

    private void DrawGrid(DrawingContext dc, Rect widget, Palette p)
    {
        float step = GridSize * 4f * _zoom;
        if (step < 12f) step *= 4f;
        if (step < 4f) return;
        var line = p.Border.WithAlpha(0.35f);
        float startX = widget.X - ((_offset.X * _zoom) % step + step) % step;
        float startY = widget.Y - ((_offset.Y * _zoom) % step + step) % step;
        for (float x = startX; x <= widget.Right; x += step) dc.DrawLine(new Point(x, widget.Y), new Point(x, widget.Bottom), line, 1f);
        for (float y = startY; y <= widget.Bottom; y += step) dc.DrawLine(new Point(widget.X, y), new Point(widget.Right, y), line, 1f);
    }

    private void DrawBand(DrawingContext dc, Palette p)
    {
        var a = FromGraph(new Point(_band.X, _band.Y));
        var b = FromGraph(new Point(_band.Right, _band.Bottom));
        var r = Rect.FromEdges(a.X, a.Y, b.X, b.Y);
        dc.FillRect(r, p.Selection.WithAlpha(0.15f));
        dc.DrawRect(r, p.Focus, 1f);
    }

    private void DrawCable(DrawingContext dc, NodeEditorCable c, Point from, Point to, Span<Point> points, Palette p)
    {
        Curve(from, to, points);
        bool selected = ReferenceEquals(c, _selectedCable);
        var color = PortColor(c.Output);
        if (c.Output.Node is { IsDimmed: true } || c.Input.Node is { IsDimmed: true }) color = color.WithAlpha(0.45f);
        float width = selected ? 3.2f : 2f;
        if (selected) dc.DrawPolyline(points, color.WithAlpha(0.25f), width + 4f, StrokeStyle.Round);
        dc.DrawPolyline(points, color, width, c.IsFeedback ? FeedbackStroke : StrokeStyle.Round);
        if (!string.IsNullOrEmpty(c.Label))
        {
            var mid = points[points.Length / 2];
            var style = SmallStyle with { Align = HorizontalAlignment.Center };
            var size = dc.MeasureText(c.Label, style);
            var box = Rect.FromCenter(mid, size.Width + 8f, 13f);
            dc.FillRoundedRect(box, 6f, p.Surface);
            dc.DrawRoundedRect(box, 6f, color, 1f);
            dc.DrawText(c.Label, box, style, p.Text);
        }
    }

    private void DrawNode(DrawingContext dc, NodeEditorNode n, float level, Palette p)
    {
        var b = NodeBounds(n);
        bool selected = _selection.Contains(n);
        if (n.IsDimmed) dc.PushOpacity(0.55f);
        dc.DrawShadow(b, 6f, 8f, new Point(0f, 2f), p.Shadow);
        dc.FillRoundedRect(b, 6f, p.Surface);
        var header = new Rect(b.X, b.Y, b.Width, HeaderHeight);
        dc.PushRoundedClip(b, 6f);
        dc.FillRect(header, n.Color ?? p.SurfaceRaised);
        dc.PopClip();
        dc.DrawLine(new Point(b.X, header.Bottom), new Point(b.Right, header.Bottom), p.Border, 1f);

        var titleArea = Rect.FromEdges(b.X + 9f, b.Y, b.Right - 8f, header.Bottom);
        if (n.Badge is { } badge)
        {
            var small = SmallStyle with { Weight = FontWeight.SemiBold, Align = HorizontalAlignment.Center };
            float w = dc.MeasureText(badge, small).Width + 8f;
            var box = new Rect(b.Right - 7f - w, b.Y + 5f, w, 12f);
            dc.FillRoundedRect(box, 3f, n.HasError ? p.Danger : p.Warning.WithAlpha(0.85f));
            dc.DrawText(badge, box, small, Color.FromRgb(0x1C1D21));
            titleArea = Rect.FromEdges(titleArea.X, titleArea.Y, box.X - 4f, titleArea.Bottom);
        }
        dc.DrawText(n.Title, titleArea, TitleStyle, p.Text);
        float y = header.Bottom;
        if (n.Subtitle is { } subtitle)
        {
            dc.DrawText(subtitle, new Rect(b.X + 9f, y, b.Width - 18f, SubtitleHeight), SmallStyle with { Ellipsis = true }, p.TextDim);
            y += SubtitleHeight;
        }
        if (n.Level is not null)
        {
            var track = new Rect(b.X + 8f, y + 1f, b.Width - 16f, MeterHeight);
            dc.FillRoundedRect(track, 2f, p.Track);
            if (level > 0f)
            {
                float db = 20f * MathF.Log10(MathF.Max(level, 1e-6f));
                float fraction = Math.Clamp((db + 60f) / 60f, 0f, 1f);
                var color = level >= 0.999f ? p.Danger : db > -6f ? p.Warning : p.Success;
                dc.FillRoundedRect(new Rect(track.X, track.Y, track.Width * fraction, track.Height), 2f, color);
            }
        }

        var portStyle = PortStyle;
        foreach (var port in n.Inputs) DrawPort(dc, port, portStyle, p);
        foreach (var port in n.Outputs) DrawPort(dc, port, portStyle with { Align = HorizontalAlignment.Right }, p);

        var frame = n.HasError ? p.Danger : selected ? p.Focus : p.Border;
        dc.DrawRoundedRect(b, 6f, frame, selected || n.HasError ? 2f : 1f);
        if (n.IsDimmed) dc.PopOpacity();
    }

    private void DrawPort(DrawingContext dc, NodeEditorPort port, TextStyle style, Palette p)
    {
        var center = PortPosition(port);
        var color = PortColor(port);
        bool lit = _drag == DragMode.Cable && _fixedPort is not null && !ReferenceEquals(port, _fixedPort)
            && (_fixedPort.IsInput ? Compatible(port, _fixedPort) : Compatible(_fixedPort, port));
        bool connected = _cables.Any(c => !ReferenceEquals(c, _detached) && (ReferenceEquals(c.Input, port) || ReferenceEquals(c.Output, port)));
        if (ReferenceEquals(port, _target)) dc.FillCircle(center, PortRadius + 4f, color.WithAlpha(0.35f));
        else if (lit) dc.DrawCircle(center, PortRadius + 3f, color.WithAlpha(0.7f), 1.5f);
        dc.FillCircle(center, PortRadius, connected ? color : p.Background);
        dc.DrawCircle(center, PortRadius, color, 1.5f);
        var n = port.Node!;
        var text = port.IsInput
            ? new Rect(center.X + PortRadius + 5f, center.Y - RowHeight / 2f, n.Width / 2f - PortRadius - 6f, RowHeight)
            : new Rect(n.X + n.Width / 2f, center.Y - RowHeight / 2f, n.Width / 2f - PortRadius - 5f, RowHeight);
        dc.DrawText(PortText(port), text, style, p.TextDim);
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        Layout();
        var g = ToGraph(e.Position);
        _anchor = e.Position;
        _mouse = e.Position;
        Focus();

        if (e.Button == MouseButton.Left)
        {
            UpdateBars();
            var bar = _vbar.HitTest(e.Position) ? _vbar : _hbar.HitTest(e.Position) ? _hbar : null;
            if (bar is not null)
            {
                // The content is fixed for the whole drag, so that the thumb follows the mouse.
                _barContent = ScrollContent();
                _barDrag = bar;
                ScrollTo(bar, bar.Press(e.Position));
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }
        if (e.Button == MouseButton.Middle || (e.Button == MouseButton.Left && e.Alt))
        {
            _drag = DragMode.Pan;
            _panOffset = _offset;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (e.Button == MouseButton.Right)
        {
            var clickedPort = PortAt(g);
            var clickedNode = clickedPort?.Node ?? NodeAt(g);
            var clickedCable = clickedNode is null ? CableAt(g) : null;
            if (clickedNode is not null && !_selection.Contains(clickedNode))
            {
                _selection.Clear();
                _selection.Add(clickedNode);
                _selectedCable = null;
                OnSelectionChanged();
            }
            else if (clickedCable is not null)
            {
                SelectCable(clickedCable);
            }
            ShowMenu(clickedNode, clickedPort, clickedCable, g, e.Position);
            e.Handled = true;
            return;
        }
        if (e.Button != MouseButton.Left) return;
        e.Handled = true;

        if (PortAt(g) is { } hit)
        {
            BeginCable(hit);
            return;
        }
        if (NodeAt(g) is { } node)
        {
            if (e.ClickCount == 2)
            {
                NodeOpened?.Invoke(this, node);
                return;
            }
            if (e.Control)
            {
                if (!_selection.Remove(node)) _selection.Add(node);
                _selectedCable = null;
                OnSelectionChanged();
                return;
            }
            if (!_selection.Contains(node) || _selectedCable is not null)
            {
                _selection.Clear();
                _selection.Add(node);
                _selectedCable = null;
                OnSelectionChanged();
            }
            // Brought to the front.
            _nodes.Remove(node);
            _nodes.Add(node);
            _drag = DragMode.Move;
            _moved = false;
            _origin = [.. _nodes.Where(_selection.Contains).Select(n => (n, n.X, n.Y))];
            CaptureMouse();
            return;
        }
        if (CableAt(g) is { } cable)
        {
            SelectCable(cable);
            return;
        }
        if (!e.Control && (_selection.Count > 0 || _selectedCable is not null))
        {
            _selection.Clear();
            _selectedCable = null;
            OnSelectionChanged();
        }
        UpdateBars();
        if ((_hbar.IsNeeded || _vbar.IsNeeded) && !e.Shift && !e.Control)
        {
            // The graph does not fit: dragging an empty place moves the view (Shift or Ctrl + drag selects).
            _drag = DragMode.Pan;
            _panOffset = _offset;
            Cursor = StandardCursor.SizeAll;
            CaptureMouse();
            return;
        }
        _drag = DragMode.Band;
        _band = new Rect(g.X, g.Y, 0f, 0f);
        CaptureMouse();
    }

    private void BeginCable(NodeEditorPort port)
    {
        _detached = null;
        if (port.IsInput && _cables.Find(c => ReferenceEquals(c.Input, port)) is { } existing)
        {
            // Pulling the end of a cable off its input: the cable follows the mouse from its output.
            _detached = existing;
            _fixedPort = existing.Output;
        }
        else
        {
            _fixedPort = port;
        }
        _target = null;
        _drag = DragMode.Cable;
        CaptureMouse();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.Position;
        if (_barDrag is not null)
        {
            ScrollTo(_barDrag, _barDrag.Drag(e.Position));
            return;
        }
        switch (_drag)
        {
            case DragMode.Pan:
                ViewOffset = new Point(_panOffset.X - (e.Position.X - _anchor.X) / _zoom, _panOffset.Y - (e.Position.Y - _anchor.Y) / _zoom);
                return;
            case DragMode.Move:
            {
                float dx = (e.Position.X - _anchor.X) / _zoom, dy = (e.Position.Y - _anchor.Y) / _zoom;
                foreach (var (n, x, y) in _origin)
                {
                    float nx = x + dx, ny = y + dy;
                    if (SnapToGrid && GridSize > 0f && !e.Shift)
                    {
                        (nx, ny) = Snapped(n, nx, ny);
                    }
                    if (nx != n.X || ny != n.Y) _moved = true;
                    n.X = nx;
                    n.Y = ny;
                }
                InvalidateVisual();
                return;
            }
            case DragMode.Band:
            {
                var a = ToGraph(_anchor);
                var b = ToGraph(e.Position);
                _band = Rect.FromEdges(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
                bool changed = false;
                foreach (var n in _nodes)
                {
                    if (NodeBounds(n).Intersects(_band) && _selection.Add(n)) changed = true;
                }
                if (changed) OnSelectionChanged();
                InvalidateVisual();
                return;
            }
            case DragMode.Cable:
            {
                var port = PortAt(ToGraph(e.Position));
                bool usable = port is not null && _fixedPort is not null && !ReferenceEquals(port, _fixedPort)
                    && (_fixedPort.IsInput ? Compatible(port, _fixedPort) : Compatible(_fixedPort, port));
                _target = usable ? port : null;
                InvalidateVisual();
                return;
            }
        }
        var g = ToGraph(e.Position);
        bool overBar = _vbar.HitTest(e.Position) || _hbar.HitTest(e.Position);
        if (overBar != (_vbar.IsHovered || _hbar.IsHovered)) InvalidateVisual();
        Cursor = overBar ? StandardCursor.Arrow : PortAt(g) is not null ? StandardCursor.Hand : NodeAt(g) is not null ? StandardCursor.SizeAll : StandardCursor.Arrow;
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_barDrag is not null)
        {
            _barDrag.Release();
            _barDrag = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            return;
        }
        if (_drag == DragMode.None) return;
        var drag = _drag;
        _drag = DragMode.None;
        ReleaseMouseCapture();
        switch (drag)
        {
            case DragMode.Move:
                if (_moved) NodesMoved?.Invoke(this, [.. _origin.Select(o => o.Node)]);
                _origin = [];
                break;
            case DragMode.Cable:
                FinishCable();
                break;
        }
        InvalidateVisual();
    }

    private void FinishCable()
    {
        var fixedPort = _fixedPort;
        var target = _target;
        var detached = _detached;
        _fixedPort = null;
        _target = null;
        _detached = null;
        if (fixedPort is null) return;
        if (target is null)
        {
            if (detached is not null) CableRemoveRequested?.Invoke(this, detached);
            return;
        }
        var (output, input) = fixedPort.IsInput ? (target, fixedPort) : (fixedPort, target);
        if (detached is not null)
        {
            if (ReferenceEquals(detached.Input, input)) return; // dropped back where it was
            CableRemoveRequested?.Invoke(this, detached);
        }
        CableRequested?.Invoke(this, output, input);
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        CancelDrag();
    }

    private void ScrollTo(SlimScrollBar bar, float offset)
    {
        _offset = bar == _hbar ? new Point(_barContent.X + offset / _zoom, _offset.Y) : new Point(_offset.X, _barContent.Y + offset / _zoom);
        InvalidateVisual();
    }

    private void CancelDrag()
    {
        _barDrag?.Release();
        _barDrag = null;
        if (_drag == DragMode.Move)
        {
            foreach (var (n, x, y) in _origin)
            {
                n.X = x;
                n.Y = y;
            }
        }
        _drag = DragMode.None;
        _origin = [];
        _fixedPort = null;
        _target = null;
        _detached = null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.WheelDelta == 0f) return;
        if (e.Control)
        {
            ZoomAround(e.Position, MathF.Pow(1.15f, e.WheelDelta));
        }
        else if (e.IsHorizontalWheel)
        {
            ViewOffset = new Point(_offset.X + e.WheelDelta * 40f / _zoom, _offset.Y);
        }
        else if (e.Shift)
        {
            ViewOffset = new Point(_offset.X - e.WheelDelta * 40f / _zoom, _offset.Y);
        }
        else
        {
            ViewOffset = new Point(_offset.X, _offset.Y - e.WheelDelta * 40f / _zoom);
        }
        e.Handled = true;
    }

    private void ShowMenu(NodeEditorNode? node, NodeEditorPort? port, NodeEditorCable? cable, Point graph, Point window)
    {
        if (ContextMenuOpening is null) return;
        var args = new NodeEditorMenuEventArgs(node, port, cable, graph);
        ContextMenuOpening(this, args);
        if (args.Items.Count > 0) Host?.ShowMenu(args.Items, window);
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        var selected = SelectedNodes;
        switch (e.Key)
        {
            case Key.Escape when _drag != DragMode.None:
                ReleaseMouseCapture();
                CancelDrag();
                break;
            case Key.Escape when selected.Count > 0 || _selectedCable is not null:
                Select([]);
                break;
            case Key.A when e.Control:
                Select(_nodes);
                break;
            case Key.Left or Key.Right when e.Control:
            {
                if (_nodes.Count == 0) return;
                var ordered = _nodes.OrderBy(n => n.X).ThenBy(n => n.Y).ToList();
                int index = selected.Count > 0 ? ordered.IndexOf(e.Key == Key.Right ? selected[^1] : selected[0]) : -1;
                index = e.Key == Key.Right ? Math.Min(ordered.Count - 1, index + 1) : index < 0 ? 0 : Math.Max(0, index - 1);
                Select([ordered[index]]);
                ScrollIntoView(ordered[index]);
                break;
            }
            case Key.Left or Key.Right or Key.Up or Key.Down when selected.Count > 0:
            {
                float step = MathF.Max(1f, GridSize) * (e.Shift ? 4f : 1f);
                float dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0f;
                float dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0f;
                foreach (var n in selected)
                {
                    n.X += dx;
                    n.Y += dy;
                }
                NodesMoved?.Invoke(this, selected);
                ScrollIntoView(selected[^1]);
                break;
            }
            case Key.Enter when selected.Count == 1:
                NodeOpened?.Invoke(this, selected[0]);
                break;
            case Key.Delete or Key.Back when selected.Count > 0 || _selectedCable is not null:
                if (selected.Count > 0) DeleteRequested?.Invoke(this, selected, null);
                else if (_selectedCable is { } cable)
                {
                    DeleteRequested?.Invoke(this, [], cable);
                    CableRemoveRequested?.Invoke(this, cable);
                }
                break;
            case Key.Home:
                ZoomToFit();
                break;
            case Key.Add or Key.OemPlus:
                ZoomAround(View.Center, 1.25f);
                break;
            case Key.Subtract or Key.OemMinus:
                ZoomAround(View.Center, 0.8f);
                break;
            case Key.F10 when e.Shift:
            case Key.Apps:
            {
                var node = selected.Count > 0 ? selected[^1] : null;
                var at = node is not null ? new Point(node.X + node.Width / 2f, node.Y + HeaderHeight) : ViewCenter;
                ShowMenu(node, null, node is null ? _selectedCable : null, at, FromGraph(at));
                break;
            }
            default:
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override bool ClaimsKey(KeyEventArgs e)
        => e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.Delete or Key.Home || base.ClaimsKey(e);

    /// <inheritdoc />
    protected internal override string? GetToolTipText()
    {
        if (Host is null || _drag != DragMode.None) return ToolTip;
        var g = ToGraph(Host.MousePosition);
        if (PortAt(g) is { } port) return PortText(port);
        if (NodeAt(g) is { ToolTip: { } tip }) return tip;
        if (CableAt(g) is { } cable) return cable.Label is null ? cable.ToString() : $"{cable} ({cable.Label})";
        return ToolTip;
    }

    // ---------------------------------------------------------------- screen readers

    /// <summary>What a screen reader says: the selected node and its connections, the selected cable, or a count.</summary>
    internal string DescribeSelection()
    {
        var t = Strings;
        var selected = SelectedNodes;
        if (selected.Count == 1)
        {
            var n = selected[0];
            string Join(IEnumerable<string> parts)
            {
                var list = parts.ToList();
                return list.Count == 0 ? t.NodeNotConnected : string.Join(", ", list);
            }
            var ins = n.Inputs.Select(p => _cables.Find(c => ReferenceEquals(c.Input, p)) is { } c ? $"{p.Name} ← {c.Output}" : null).OfType<string>();
            var outs = n.Outputs.SelectMany(p => _cables.Where(c => ReferenceEquals(c.Output, p)).Select(c => $"{p.Name} → {c.Input}"));
            string text = n.Title + (n.Badge is null ? string.Empty : $" ({n.Badge})");
            if (n.Inputs.Count > 0) text += "; " + string.Format(CultureInfo.InvariantCulture, t.NodeInputsFormat, Join(ins));
            if (n.Outputs.Count > 0) text += "; " + string.Format(CultureInfo.InvariantCulture, t.NodeOutputsFormat, Join(outs));
            return text;
        }
        if (selected.Count > 1) return string.Format(CultureInfo.InvariantCulture, t.NodesSelectedFormat, selected.Count, _nodes.Count);
        if (_selectedCable is { } cable)
        {
            string text = string.Format(CultureInfo.InvariantCulture, t.CableFormat, cable.Output, cable.Input);
            return cable.IsFeedback ? text + ", " + t.FeedbackCable : text;
        }
        return string.Format(CultureInfo.InvariantCulture, t.NodeCountFormat, _nodes.Count, _cables.Count);
    }
}

/// <summary>Peer of a <see cref="NodeEditor"/>: a group whose value describes the selection (node and its cables, cable, count).</summary>
/// <param name="owner">The editor.</param>
public class NodeEditorAutomationPeer(NodeEditor owner) : ElementAutomationPeer<NodeEditor>(owner), IValuePattern
{
    private string? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationNodeEditor;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => true;

    /// <inheritdoc />
    public string Value => Owner.DescribeSelection();

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(string value) => throw new InvalidOperationException("The node editor is edited with its keys and menus.");

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
