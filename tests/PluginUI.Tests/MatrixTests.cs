// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>The modulation matrix and the routing matrix: mouse, keyboard, drops, parameters, screen readers (no window).</summary>
public class MatrixTests
{
    private UIHost _host = null!;
    private Canvas _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = new Canvas();
        _host = new UIHost(_root, new Size(800f, 600f));
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private T Add<T>(T element, float x, float y, float width, float height) where T : UIElement
    {
        element.SetBounds(x, y, width, height);
        _root.Children.Add(element);
        _host.UpdateLayout();
        return element;
    }

    private void Click(Point p, int count = 1)
    {
        _host.DispatchMouseDown(p, MouseButton.Left, count, ModifierKeys.None);
        _host.DispatchMouseUp(p, MouseButton.Left, ModifierKeys.None);
    }

    private void Drag(Point from, Point to)
    {
        _host.DispatchMouseDown(from, MouseButton.Left, 1, ModifierKeys.None);
        for (int i = 1; i <= 4; i++) _host.DispatchMouseMove(Point.Lerp(from, to, i / 4f), ModifierKeys.None);
        _host.DispatchMouseUp(to, MouseButton.Left, ModifierKeys.None);
    }

    private void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _host.DispatchKeyDown(new KeyEventArgs(key, modifiers));

    // A 480 × 200 matrix at (0, 0): rows of 26 px from y = 21; columns on 1–25, source 25–149, target 165–306,
    // amount 306–455 (track 312–449), delete 455–479.
    private static float RowY(int row) => 21f + 26f * row + 13f;

    [Test]
    public void SlotsAreAddedEditedAndRemovedWithTheMouse()
    {
        var matrix = Add(new ModulationMatrix(), 0f, 0f, 480f, 200f);
        var lfo = new ModulationSource("LFO 1", Color.FromRgb(0x4FC3F7));
        var cutoff = new ModulationTarget("Cutoff", "Filter");
        matrix.Sources.Add(lfo);
        matrix.Targets.Add(cutoff);
        var events = new List<string>();
        matrix.SlotAdded += (_, i) => events.Add("+" + i);
        matrix.SlotChanged += (_, i) => events.Add("~" + i);
        matrix.SlotRemoved += (_, i) => events.Add("-" + i);

        Click(new Point(240f, RowY(0)));
        Assert.That(matrix.Slots, Has.Count.EqualTo(1), "the last row adds a slot");
        matrix.Choose(0, lfo, cutoff);
        var amount = new LocalParameter("Amount", 0, 1, 0.5);
        int gestures = 0;
        amount.EditStarted += _ => gestures++;
        matrix.Slots[0].AmountParameter = amount;

        Drag(new Point(380.5f, RowY(0)), new Point(380.5f + 34.26f, RowY(0)));
        Assert.Multiple(() =>
        {
            Assert.That(matrix.Slots[0].Amount, Is.EqualTo(0.5).Within(0.01), "the full track is −1 … +1");
            Assert.That(amount.NormalizedValue, Is.EqualTo(0.75).Within(0.005));
            Assert.That(gestures, Is.EqualTo(1));
            Assert.That(amount.IsEditing, Is.False);
        });
        Click(new Point(380.5f, RowY(0)), 2);
        Assert.That(matrix.Slots[0].Amount, Is.EqualTo(0.0).Within(1e-9), "double-click: no modulation");

        Click(new Point(13f, RowY(0)));
        Assert.That(matrix.Slots[0].IsEnabled, Is.False, "the dot switches the slot");
        Assert.That(ModulationMatrix.FormatAmount(-0.354), Is.EqualTo("-35 %"));

        // A source dropped on the add row adds a slot with it; on a slot it replaces its source.
        var lfo2 = new ModulationSource("LFO 2");
        var chip = Add(new ModulationSourceButton { Source = lfo2 }, 600f, 300f, 80f, 24f);
        _host.DispatchMouseDown(new Point(610f, 310f), MouseButton.Left, 1, ModifierKeys.None);
        _host.BeginDrag(chip, lfo2, _ => { });
        _host.DispatchMouseMove(new Point(240f, RowY(1)), ModifierKeys.None);
        _host.DispatchMouseUp(new Point(240f, RowY(1)), MouseButton.Left, ModifierKeys.None);
        Assert.That(matrix.Slots.Select(s => s.Source), Is.EqualTo(new[] { lfo, lfo2 }));
        _host.DispatchMouseDown(new Point(610f, 310f), MouseButton.Left, 1, ModifierKeys.None);
        _host.BeginDrag(chip, lfo2, _ => { });
        _host.DispatchMouseMove(new Point(240f, RowY(0)), ModifierKeys.None);
        _host.DispatchMouseUp(new Point(240f, RowY(0)), MouseButton.Left, ModifierKeys.None);
        Assert.That(matrix.Slots[0].Source, Is.SameAs(lfo2));

        Click(new Point(467f, RowY(1)));
        Assert.Multiple(() =>
        {
            Assert.That(matrix.Slots, Has.Count.EqualTo(1), "× deletes");
            Assert.That(events.Where(e => e[0] != '~'), Is.EqualTo(new[] { "+0", "+1", "-1" }));
            Assert.That(events.Count(e => e == "~0"), Is.GreaterThanOrEqualTo(5), "choice, amount, reset, switch, drop");
        });

        matrix.MaxSlots = 1;
        Click(new Point(240f, RowY(1)));
        Assert.That(matrix.Slots, Has.Count.EqualTo(1), "no add row when full");
    }

    [Test]
    public void TheModulationMatrixWorksFromTheKeyboard()
    {
        var matrix = Add(new ModulationMatrix(), 0f, 0f, 480f, 200f);
        matrix.Sources.Add(new ModulationSource("Env 2"));
        matrix.Slots.Add(new ModulationSlot(matrix.Sources[0], new ModulationTarget("Pitch"), 0.25));
        matrix.Focus();
        var peer = matrix.GetAutomationPeer()!;
        var value = (IValuePattern)peer.GetPattern(AutomationPattern.Value)!;
        Assert.That(value.Value, Is.EqualTo("1 slots"));

        Press(Key.Down);
        Assert.That(value.Value, Is.EqualTo("Slot 1: Env 2 → Pitch, +25 % (Source)"));
        Press(Key.PageUp);
        Press(Key.PageUp, ModifierKeys.Shift);
        Assert.That(matrix.Slots[0].Amount, Is.EqualTo(0.36).Within(1e-9));
        Press(Key.Subtract);
        Assert.That(matrix.Slots[0].Amount, Is.EqualTo(0.26).Within(1e-9));
        Press(Key.Left);
        Press(Key.Space);
        Assert.That(value.Value, Is.EqualTo("Slot 1: Env 2 → Pitch, +26 %, off (off)"));
        Press(Key.Insert);
        Assert.Multiple(() =>
        {
            Assert.That(matrix.Slots, Has.Count.EqualTo(2));
            Assert.That((matrix.CurrentRow, matrix.CurrentColumn), Is.EqualTo((1, 1)), "on the source of the new slot");
            Assert.That(value.Value, Is.EqualTo("Slot 2: (none) → (none), 0 % (Source)"));
        });
        Press(Key.Delete);
        Press(Key.End);
        Assert.Multiple(() =>
        {
            Assert.That(matrix.Slots, Has.Count.EqualTo(1));
            Assert.That(value.Value, Is.EqualTo("Add modulation"));
            Assert.That(peer.LocalizedControlType, Is.EqualTo("modulation matrix"));
        });
        Press(Key.Enter);
        Assert.That(matrix.Slots, Has.Count.EqualTo(2));
    }

    [Test]
    public void TheRoutingMatrixConnectsInputsToOutputs()
    {
        var router = Add(new RoutingMatrix(), 0f, 0f, 320f, 220f);
        router.Inputs.AddRange(["In L", "In R", "Side L", "Side R"]);
        router.Outputs.AddRange(["Out L", "Out R"]);
        var changes = new List<string>();
        router.ConnectionChanged += (_, i, o, on) => changes.Add($"{i}{o}{(on ? "+" : "-")}");

        Click(router.CellRect(0, 0).Center);
        Click(router.CellRect(1, 1).Center);
        Assert.That((router.IsConnected(0, 0), router.IsConnected(1, 1), router.IsConnected(0, 1)), Is.EqualTo((true, true, false)));

        // Dragging down a column sets every crossing like the first one.
        Drag(router.CellRect(0, 1).Center, router.CellRect(3, 1).Center);
        Assert.That(Enumerable.Range(0, 4).Select(i => router.IsConnected(i, 1)), Is.EqualTo(new[] { true, true, true, true }));

        // One input per output: connecting a crossing clears the others of its column.
        router.Mode = RoutingMode.OneInputPerOutput;
        changes.Clear();
        Click(router.CellRect(2, 0).Center);
        Assert.Multiple(() =>
        {
            Assert.That(router.IsConnected(0, 0), Is.False);
            Assert.That(router.IsConnected(2, 0), Is.True);
            Assert.That(changes, Is.EqualTo(new[] { "00-", "20+" }));
        });
        router.Mode = RoutingMode.OneOutputPerInput;
        Click(router.CellRect(2, 1).Center);
        Assert.That((router.IsConnected(2, 0), router.IsConnected(2, 1)), Is.EqualTo((true, false)), "switching off is not affected");
        Click(router.CellRect(2, 1).Center);
        Assert.That((router.IsConnected(2, 0), router.IsConnected(2, 1)), Is.EqualTo((false, true)), "one output per input");

        // Parameters, keyboard and screen readers.
        var p = new LocalParameter("Side R to Out L", 0, 1, 0);
        int gestures = 0;
        p.EditStarted += _ => gestures++;
        router.ParameterOf = (i, o) => i == 3 && o == 0 ? p : null;
        router.Focus();
        Press(Key.Down);
        Press(Key.Down);
        Press(Key.Down);
        Press(Key.Home);
        Press(Key.Space);
        Assert.Multiple(() =>
        {
            Assert.That(p.NormalizedValue, Is.EqualTo(1.0));
            Assert.That(gestures, Is.EqualTo(1));
            Assert.That((router.CurrentInput, router.CurrentOutput), Is.EqualTo((3, 0)));
        });
        var peer = (RoutingMatrixAutomationPeer)router.GetAutomationPeer()!;
        var cell = peer.GetCell(3, 0);
        Assert.Multiple(() =>
        {
            Assert.That(peer.GetChildren(), Has.Count.EqualTo(8));
            Assert.That(peer.GetFocusTarget(), Is.SameAs(cell));
            Assert.That(cell.Name, Is.EqualTo("Side R → Out L"));
            Assert.That(cell.ControlType, Is.EqualTo(AutomationControlType.CheckBox));
            Assert.That(cell.ToggleState, Is.EqualTo(ToggleState.On));
            Assert.That(cell.BoundingRectangle, Is.EqualTo(router.CellRect(3, 0)));
        });
        cell.Toggle();
        Assert.That(p.NormalizedValue, Is.EqualTo(0.0));
    }
}
