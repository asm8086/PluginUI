// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>DataTable: column visibility and layout, context menus, accessibility.</summary>
public class DataTableTests
{
    private sealed record Row(string Name, string Vendor, int Inputs);

    private UIHost _host = null!;
    private DataTable _table = null!;
    private DataColumn _name = null!, _vendor = null!, _inputs = null!;

    [SetUp]
    public void SetUp()
    {
        var root = new Canvas();
        _host = new UIHost(root, new Size(500f, 300f));
        _name = new DataColumn("Name", o => ((Row)o).Name) { CanHide = false }.SortByText();
        _vendor = new DataColumn("Vendor", o => ((Row)o).Vendor, 120f) { Key = "vendor" }.SortByText();
        _inputs = new DataColumn("Inputs", o => ((Row)o).Inputs.ToString(System.Globalization.CultureInfo.InvariantCulture), 60f)
        {
            Comparison = (a, b) => ((Row)a).Inputs.CompareTo(((Row)b).Inputs),
        };
        _table = new DataTable { MultiSelect = true, Items = new List<Row> { new("Zeta", "B", 2), new("Alpha", "A", 6), new("Mid", "C", 1) } };
        _table.AddColumn(_name).AddColumn(_vendor).AddColumn(_inputs);
        _table.SetBounds(0f, 0f, 500f, 300f);
        root.Add(_table);
        _host.UpdateLayout();
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    // Header 26 DIPs high, rows 24 DIPs: row r is centered at y = 1 + 26 + 24 r + 12.
    private static Point RowPoint(int row) => new(20f, 39f + 24f * row);

    [Test]
    public void LayoutRoundTripsWidthsVisibilityAndSort()
    {
        _table.Sort(_inputs, descending: true);
        _vendor.Width = 150f;
        _table.SetColumnVisible(_inputs, false);
        string layout = _table.SaveLayout();

        _vendor.Width = 80f;
        _table.SetColumnVisible(_inputs, true);
        _table.Sort(null);
        _vendor.Header = "Marque"; // translated header: the key keeps the layout
        _table.RestoreLayout(layout);
        Assert.Multiple(() =>
        {
            Assert.That(_vendor.Width, Is.EqualTo(150f));
            Assert.That(_inputs.IsVisible, Is.False);
            Assert.That(_table.SortColumn, Is.SameAs(_inputs));
            Assert.That(_table.SortDescending, Is.True);
            Assert.That(_table.VisibleColumns, Is.EqualTo(new[] { _name, _vendor }));
        });
        _table.RestoreLayout("garbage;;x:y|nothing");
        Assert.That(_name.IsVisible, Is.True, "malformed layouts are ignored");
    }

    [Test]
    public void HeaderMenuHidesColumnsButNotTheLastOne()
    {
        int changes = 0;
        _table.ColumnsChanged += _ => changes++;
        _host.DispatchMouseDown(new Point(300f, 12f), MouseButton.Right, 1, ModifierKeys.None);
        _host.DispatchMouseUp(new Point(300f, 12f), MouseButton.Right, ModifierKeys.None);
        var menu = (MenuPopup)_host.Popups.Single();
        Assert.That(menu.Items.Select(i => (i.Text, i.IsChecked)), Is.EqualTo(new[] { ("Vendor", true), ("Inputs", true) }), "Name cannot be hidden");
        menu.Items[0].Invoke();
        Assert.Multiple(() =>
        {
            Assert.That(_vendor.IsVisible, Is.False);
            Assert.That(changes, Is.EqualTo(1));
        });
        _table.SetColumnVisible(_inputs, false);
        _name.CanHide = true;
        _table.SetColumnVisible(_name, false);
        Assert.That(_name.IsVisible, Is.True, "one column always stays");
    }

    [Test]
    public void RightClickSelectsTheRowAndShowsItsMenu()
    {
        IReadOnlyList<object>? asked = null;
        _table.RowContextMenu = selection =>
        {
            asked = selection;
            return [new MenuItem("Open")];
        };
        _host.DispatchMouseDown(RowPoint(1), MouseButton.Right, 1, ModifierKeys.None);
        _host.DispatchMouseUp(RowPoint(1), MouseButton.Right, ModifierKeys.None);
        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(new object[] { new Row("Alpha", "A", 6) }));
            Assert.That(((MenuPopup)_host.Popups.Single()).Items.Single().Text, Is.EqualTo("Open"));
        });
        _host.CloseAllPopups();

        // The menu key opens the menu of the current row.
        asked = null;
        _host.DispatchKeyDown(new KeyEventArgs(Key.Apps, ModifierKeys.None));
        Assert.That(asked, Is.EqualTo(new object[] { new Row("Alpha", "A", 6) }));
    }

    [Test]
    public void RowsAreAListForScreenReaders()
    {
        _table.SetColumnVisible(_inputs, false);
        _table.Sort(_name);
        var peer = _table.GetAutomationPeer()!;
        var rows = peer.GetChildren();
        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.List));
            Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Alpha, Vendor: A", "Mid, Vendor: C", "Zeta, Vendor: B" }), "view order, visible columns");
        });
        ((ISelectionItemPattern)rows[2].GetPattern(AutomationPattern.SelectionItem)!).Select();
        ((ISelectionItemPattern)rows[0].GetPattern(AutomationPattern.SelectionItem)!).AddToSelection();
        Assert.That(_table.SelectedItems.Select(i => ((Row)i).Name), Is.EquivalentTo(new[] { "Zeta", "Alpha" }));
        _table.Focus();
        Assert.That(_host.GetFocusedAutomationPeer(), Is.SameAs(rows[0]), "the focus is on the current row");
    }
}
