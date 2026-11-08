// 2026/11/08 13:40:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>The node editor: cables, moves, selection, view, keyboard and screen readers (no window).</summary>
public class NodeEditorTests
{
    private UIHost _host = null!;
    private Canvas _root = null!;
    private NodeEditor _editor = null!;
    private NodeEditorNode _source = null!, _gain = null!, _output = null!, _keys = null!;

    [SetUp]
    public void SetUp()
    {
        _root = new Canvas();
        _host = new UIHost(_root, new Size(800f, 600f));
        _editor = new NodeEditor { SnapToGrid = true, GridSize = 16f };
        _editor.SetBounds(0f, 0f, 700f, 500f);
        _root.Children.Add(_editor);
        _host.UpdateLayout();

        _source = Node("Source", 32, 32, [], [new("out", "Out", "audio", 2)]);
        _gain = Node("Gain", 256, 32, [new("in", "In", "audio", 2)], [new("out", "Out", "audio", 2)]);
        _output = Node("Output", 480, 32, [new("in", "In", "audio", 2)], []);
        _keys = Node("Keys", 32, 192, [], [new("midi", "MIDI", "midi")]);
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private NodeEditorNode Node(string title, float x, float y, NodeEditorPort[] inputs, NodeEditorPort[] outputs)
    {
        var node = new NodeEditorNode(title) { X = x, Y = y };
        node.SetPorts(inputs, outputs);
        _editor.Nodes.Add(node);
        return node;
    }

    private Point At(NodeEditorPort port) => _editor.FromGraph(_editor.PortPosition(port));

    private Point Header(NodeEditorNode node) => _editor.FromGraph(new Point(node.X + 20f, node.Y + 10f));

    private void Drag(Point from, Point to, ModifierKeys modifiers = ModifierKeys.None, MouseButton button = MouseButton.Left)
    {
        _host.DispatchMouseDown(from, button, 1, modifiers);
        for (int i = 1; i <= 4; i++) _host.DispatchMouseMove(Point.Lerp(from, to, i / 4f), modifiers);
        _host.DispatchMouseUp(to, button, modifiers);
    }

    private void Click(Point p, int count = 1, MouseButton button = MouseButton.Left, ModifierKeys modifiers = ModifierKeys.None)
    {
        _host.DispatchMouseDown(p, button, count, modifiers);
        _host.DispatchMouseUp(p, button, modifiers);
    }

    private void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _host.DispatchKeyDown(new KeyEventArgs(key, modifiers));

    [Test]
    public void CablesAreDrawnBetweenCompatibleConnectorsOnly()
    {
        var requested = new List<string>();
        _editor.CableRequested += (_, o, i) => requested.Add($"{o} → {i}");
        Drag(At(_source.Outputs[0]), At(_gain.Inputs[0]));
        Drag(At(_output.Inputs[0]), At(_gain.Outputs[0]));
        Drag(At(_keys.Outputs[0]), At(_output.Inputs[0]));
        Drag(At(_source.Outputs[0]), _editor.FromGraph(new Point(300f, 400f)));
        Assert.That(requested, Is.EqualTo(new[] { "Source.Out → Gain.In", "Gain.Out → Output.In" }),
            "from an output or from an input; MIDI does not go to audio; nothing when dropped in the void");

        _editor.CanConnect = (o, i) => !ReferenceEquals(o.Node, i.Node);
        Drag(At(_gain.Outputs[0]), At(_gain.Inputs[0]));
        Assert.That(requested, Has.Count.EqualTo(2), "CanConnect decides");
    }

    [Test]
    public void ACableEndPulledOffItsInputIsMovedOrRemoved()
    {
        var cable = new NodeEditorCable(_source.Outputs[0], _gain.Inputs[0]);
        _editor.Cables.Add(cable);
        var events = new List<string>();
        _editor.CableRequested += (_, o, i) => events.Add($"add {o} → {i}");
        _editor.CableRemoveRequested += (_, c) => events.Add($"remove {c}");

        Drag(At(_gain.Inputs[0]), At(_gain.Inputs[0]) with { X = At(_gain.Inputs[0]).X + 2f });
        Assert.That(events, Is.Empty, "dropped back on its input");

        Drag(At(_gain.Inputs[0]), At(_output.Inputs[0]));
        Assert.That(events, Is.EqualTo(new[] { "remove Source.Out → Gain.In", "add Source.Out → Output.In" }));

        events.Clear();
        Drag(At(_gain.Inputs[0]), _editor.FromGraph(new Point(200f, 400f)));
        Assert.That(events, Is.EqualTo(new[] { "remove Source.Out → Gain.In" }));
    }

    [Test]
    public void NodesAreSelectedMovedOnTheGridAndOpened()
    {
        IReadOnlyList<NodeEditorNode>? moved = null;
        NodeEditorNode? opened = null;
        int selections = 0;
        _editor.NodesMoved += (_, nodes) => moved = nodes;
        _editor.NodeOpened += (_, node) => opened = node;
        _editor.SelectionChanged += _ => selections++;

        Click(Header(_gain));
        Assert.That(_editor.SelectedNodes, Is.EqualTo(new[] { _gain }));
        Click(Header(_source), modifiers: ModifierKeys.Control);
        Assert.That(_editor.SelectedNodes.Select(n => n.Title), Is.EquivalentTo(new[] { "Gain", "Source" }));

        Drag(Header(_gain), Header(_gain) with { X = Header(_gain).X + 37f, Y = Header(_gain).Y + 9f });
        Assert.Multiple(() =>
        {
            Assert.That((_gain.X, _gain.Y), Is.EqualTo((288f, 34f)), "on the 16-unit grid by its first connector (22 + 8 below the top)");
            Assert.That(_editor.PortPosition(_gain.Inputs[0]).Y % 16f, Is.Zero);
            Assert.That((_source.X, _source.Y), Is.EqualTo((64f, 34f)), "the selection moves together");
            Assert.That(moved, Is.EquivalentTo(new[] { _gain, _source }));
            Assert.That(_editor.Nodes[^1], Is.SameAs(_gain), "the dragged node comes to the front");
        });

        Click(Header(_output), count: 2);
        Assert.That(opened, Is.SameAs(_output));

        Drag(_editor.FromGraph(new Point(10f, 150f)), _editor.FromGraph(new Point(700f, 400f)));
        Assert.That(_editor.SelectedNodes, Is.EqualTo(new[] { _keys }), "the rectangle selects what it touches");
        Assert.That(selections, Is.GreaterThan(3));
    }

    [Test]
    public void CablesAreSelectedAndDeleted()
    {
        var cable = new NodeEditorCable(_source.Outputs[0], _gain.Inputs[0]) { IsFeedback = true, Label = "1 block" };
        _editor.Cables.Add(cable);
        var a = _editor.PortPosition(_source.Outputs[0]);
        var b = _editor.PortPosition(_gain.Inputs[0]);
        Click(_editor.FromGraph(new Point((a.X + b.X) / 2f, (a.Y + b.Y) / 2f)));
        Assert.That(_editor.SelectedCable, Is.SameAs(cable));
        var peer = (IValuePattern)_editor.GetAutomationPeer()!;
        Assert.That(peer.Value, Is.EqualTo("cable from Source.Out to Gain.In, feedback cable, delayed"));

        NodeEditorCable? removed = null;
        _editor.CableRemoveRequested += (_, c) => removed = c;
        Press(Key.Delete);
        Assert.That(removed, Is.SameAs(cable));
    }

    [Test]
    public void TheKeyboardSelectsMovesOpensAndDeletes()
    {
        _editor.Cables.Add(new NodeEditorCable(_source.Outputs[0], _gain.Inputs[0]));
        _editor.Cables.Add(new NodeEditorCable(_gain.Outputs[0], _output.Inputs[0]));
        _editor.Focus();
        Press(Key.Right, ModifierKeys.Control);
        Assert.That(_editor.SelectedNodes, Is.EqualTo(new[] { _source }), "left to right, then top to bottom");
        Press(Key.Right, ModifierKeys.Control);
        Assert.That(_editor.SelectedNodes, Is.EqualTo(new[] { _keys }));
        Press(Key.Right, ModifierKeys.Control);
        Assert.That(_editor.SelectedNodes, Is.EqualTo(new[] { _gain }));

        var peer = (IValuePattern)_editor.GetAutomationPeer()!;
        Assert.That(peer.Value, Is.EqualTo("Gain; inputs: In ← Source.Out; outputs: Out → Output.In"));

        Press(Key.Down, ModifierKeys.Shift);
        Assert.That(_gain.Y, Is.EqualTo(32f + 64f));

        NodeEditorNode? opened = null;
        _editor.NodeOpened += (_, n) => opened = n;
        Press(Key.Enter);
        Assert.That(opened, Is.SameAs(_gain));

        IReadOnlyList<NodeEditorNode>? deleted = null;
        _editor.DeleteRequested += (_, nodes, _) => deleted = nodes;
        Press(Key.A, ModifierKeys.Control);
        Assert.That(peer.Value, Is.EqualTo("4 of 4 modules selected"));
        Press(Key.Delete);
        Assert.That(deleted, Has.Count.EqualTo(4));

        Press(Key.Escape);
        Assert.That(peer.Value, Is.EqualTo("4 modules, 2 cables, nothing selected"));
    }

    [Test]
    public void TheViewZoomsAroundTheMouseAndFitsTheGraph()
    {
        var mouse = new Point(300f, 200f);
        var before = _editor.ToGraph(mouse);
        _host.DispatchWheel(mouse, 2f, false, ModifierKeys.Control);
        Assert.Multiple(() =>
        {
            Assert.That(_editor.Zoom, Is.EqualTo(1.15f * 1.15f).Within(1e-4f));
            Assert.That(_editor.ToGraph(mouse).X, Is.EqualTo(before.X).Within(1e-3f), "the point under the mouse stays");
            Assert.That(_editor.ToGraph(mouse).Y, Is.EqualTo(before.Y).Within(1e-3f));
        });

        var grabbed = _editor.ToGraph(new Point(400f, 400f));
        Drag(new Point(400f, 400f), new Point(350f, 380f), button: MouseButton.Middle);
        Assert.Multiple(() =>
        {
            Assert.That(_editor.ToGraph(new Point(350f, 380f)).X, Is.EqualTo(grabbed.X).Within(1e-3f), "the middle button drags the view");
            Assert.That(_editor.ToGraph(new Point(350f, 380f)).Y, Is.EqualTo(grabbed.Y).Within(1e-3f));
        });

        _editor.ZoomToFit();
        foreach (var node in _editor.Nodes)
        {
            var b = _editor.NodeBounds(node);
            var topLeft = _editor.FromGraph(new Point(b.X, b.Y));
            var bottomRight = _editor.FromGraph(new Point(b.Right, b.Bottom));
            Assert.That(_editor.Bounds.Contains(topLeft) && _editor.Bounds.Contains(bottomRight), Is.True, node.Title);
        }
    }

    [Test]
    public void SnappedNodesFaceEachOtherByTheirConnectors()
    {
        // Different headers (a subtitle, a meter): the first connectors still land on the same grid line.
        var a = Node("With subtitle", 32, 290, [], [new("out", "Out"), new("out2", "Out 2")]);
        a.Subtitle = "vendor";
        a.Level = () => 0.5f;
        var b = Node("Plain", 300, 301, [new("in", "In"), new("in2", "In 2")], []);
        _editor.Snap(a);
        _editor.Snap(b);
        Assert.Multiple(() =>
        {
            Assert.That(_editor.PortPosition(a.Outputs[0]).Y, Is.EqualTo(_editor.PortPosition(b.Inputs[0]).Y), "a straight cable");
            Assert.That(_editor.PortPosition(a.Outputs[1]).Y, Is.EqualTo(_editor.PortPosition(b.Inputs[1]).Y), "the rows are one grid step high");
            Assert.That(_editor.PortPosition(a.Outputs[0]).Y % 16f, Is.Zero);
        });
    }

    [Test]
    public void FitBeforeTheFirstRenderIsDoneAgainAtTheRealSize()
    {
        // A graph restored at start: the editor sits in a page that is not laid out yet.
        var page = new Canvas();
        var late = new NodeEditor();
        page.Children.Add(late);
        var node = new NodeEditorNode("Far") { X = 900, Y = -200 };
        node.SetPorts([new("in", "In")], []);
        late.Nodes.Add(node);
        late.ZoomToFit();
        late.SetBounds(0f, 0f, 700f, 500f);
        _root.Children.Remove(_editor);
        _root.Children.Add(page);
        _host.UpdateLayout();
        var b = late.NodeBounds(node);
        Assert.Multiple(() =>
        {
            Assert.That(late.Bounds.Contains(late.FromGraph(new Point(b.X, b.Y))), Is.True, "top left visible");
            Assert.That(late.Bounds.Contains(late.FromGraph(new Point(b.Right, b.Bottom))), Is.True, "bottom right visible");
        });
    }

    [Test]
    public void DraggingAnEmptyPlaceMovesTheViewWhenTheGraphDoesNotFit()
    {
        Node("Far", 1600, 900, [new("in", "In")], []);
        Assert.That(_editor.IsScrollable, Is.EqualTo((true, true)));
        var empty = new Point(400f, 400f);
        var grabbed = _editor.ToGraph(empty);
        Drag(empty, new Point(300f, 350f));
        Assert.Multiple(() =>
        {
            Assert.That(_editor.ToGraph(new Point(300f, 350f)).X, Is.EqualTo(grabbed.X).Within(1e-3f), "the graph follows the mouse");
            Assert.That(_editor.ToGraph(new Point(300f, 350f)).Y, Is.EqualTo(grabbed.Y).Within(1e-3f));
            Assert.That(_editor.SelectedNodes, Is.Empty);
        });

        _editor.ViewOffset = default;
        var keys = _editor.FromGraph(new Point(_keys.X + 20f, _keys.Y + 10f));
        Drag(new Point(keys.X - 30f, keys.Y + 60f), new Point(keys.X + 10f, keys.Y), ModifierKeys.Shift);
        Assert.Multiple(() =>
        {
            Assert.That(_editor.ViewOffset, Is.EqualTo(default(Point)), "Shift + drag does not move the view");
            Assert.That(_editor.SelectedNodes, Is.EqualTo(new[] { _keys }), "it selects with a rectangle");
        });
    }

    [Test]
    public void ScrollBarsAppearWhenTheGraphDoesNotFit()
    {
        Assert.That(_editor.IsScrollable, Is.EqualTo((false, false)), "everything fits at first");
        var far = Node("Far", 1600, 900, [new("in", "In")], []);
        Assert.That(_editor.IsScrollable, Is.EqualTo((true, true)));

        var start = _editor.ViewOffset;
        _host.DispatchWheel(new Point(300f, 200f), -1f, false, ModifierKeys.None);
        Assert.That(_editor.ViewOffset.Y, Is.EqualTo(start.Y + 40f).Within(1e-3f), "the wheel scrolls down");
        _host.DispatchWheel(new Point(300f, 200f), -1f, false, ModifierKeys.Shift);
        Assert.That(_editor.ViewOffset.X, Is.EqualTo(start.X + 40f).Within(1e-3f), "Shift+wheel scrolls sideways");
        Assert.That(_editor.Zoom, Is.EqualTo(1f), "without Ctrl, no zoom");

        // Dragging the horizontal thumb to the end shows the far node.
        _editor.ViewOffset = default;
        Drag(new Point(20f, 495f), new Point(700f, 495f));
        var right = _editor.ToGraph(new Point(700f, 0f)).X;
        Assert.That(right, Is.GreaterThanOrEqualTo(_editor.NodeBounds(far).Right), "the end of the bar reaches the last node");
    }

    [Test]
    public void TheMenuTellsWhatWasClicked()
    {
        NodeEditorMenuEventArgs? args = null;
        _editor.ContextMenuOpening += (_, e) =>
        {
            args = e;
            e.Items.Add(new MenuItem("Remove"));
        };
        Click(Header(_gain), button: MouseButton.Right);
        Assert.Multiple(() =>
        {
            Assert.That(args!.Node, Is.SameAs(_gain));
            Assert.That(_editor.SelectedNodes, Is.EqualTo(new[] { _gain }));
            Assert.That(_host.Popups, Has.Count.EqualTo(1));
        });
        _host.CloseAllPopups();
        Click(_editor.FromGraph(new Point(400f, 400f)), button: MouseButton.Right);
        Assert.Multiple(() =>
        {
            Assert.That(args!.Node, Is.Null);
            Assert.That(args.GraphPosition.X, Is.EqualTo(400f).Within(0.01f), "where to add a module");
        });
    }
}
