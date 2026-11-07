// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>The tree view and the preset browser: mouse, keyboard, filter, favorites, screen readers (no window).</summary>
public class TreeViewTests
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

    private T Add<T>(T element, float width, float height) where T : UIElement
    {
        element.SetBounds(0f, 0f, width, height);
        _root.Children.Add(element);
        _host.UpdateLayout();
        return element;
    }

    private void Click(Point p, int count = 1)
    {
        _host.DispatchMouseDown(p, MouseButton.Left, count, ModifierKeys.None);
        _host.DispatchMouseUp(p, MouseButton.Left, ModifierKeys.None);
    }

    private void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => _host.DispatchKeyDown(new KeyEventArgs(key, modifiers));

    // Rows of 24 px from y = 1; the expander of a level-L node is centred at x = 13 + 16 L.
    private static Point Row(int index, float x = 100f) => new(x, 1f + 24f * index + 12f);

    private static (TreeView Tree, TreeNode Bass, TreeNode Sub, TreeNode Reese, TreeNode Deep, TreeNode Keys, TreeNode Pads) Build()
    {
        var tree = new TreeView();
        var bass = tree.Nodes.Add("Bass");
        var sub = bass.Add("Sub");
        var reese = bass.Add("Reese");
        var deep = reese.Add("Deep");
        var keys = tree.Nodes.Add("Keys");
        var pads = tree.Nodes.Add("Pads");
        pads.Detail = "12";
        return (tree, bass, sub, reese, deep, keys, pads);
    }

    [Test]
    public void NodesExpandSelectAndActivateWithTheMouseAndTheKeyboard()
    {
        var (tree, bass, sub, reese, deep, keys, pads) = Build();
        Add(tree, 200f, 220f);
        var expanded = new List<string>();
        var activated = new List<string>();
        tree.ExpandedChanged += (_, n) => expanded.Add(n.Text + (n.IsExpanded ? "+" : "-"));
        tree.NodeActivated += (_, n) => activated.Add(n.Text);
        Assert.Multiple(() =>
        {
            Assert.That(tree.VisibleNodes, Is.EqualTo(new[] { bass, keys, pads }));
            Assert.That((deep.Level, deep.Parent, bass.Parent), Is.EqualTo((2, reese, (TreeNode?)null)));
        });

        Click(Row(0, 13f));
        Assert.Multiple(() =>
        {
            Assert.That(tree.VisibleNodes, Is.EqualTo(new[] { bass, sub, reese, keys, pads }), "the arrow expands");
            Assert.That(tree.SelectedNode, Is.Null, "without selecting");
            Assert.That(expanded, Is.EqualTo(new[] { "Bass+" }));
        });
        Click(Row(2));
        Assert.That(tree.SelectedNode, Is.SameAs(reese));

        tree.Focus();
        Press(Key.Right);
        Assert.That(reese.IsExpanded, Is.True, "Right expands");
        Press(Key.Right);
        Assert.That(tree.SelectedNode, Is.SameAs(deep), "then goes to the first child");
        Press(Key.Left);
        Assert.That(tree.SelectedNode, Is.SameAs(reese), "Left goes to the parent");
        Press(Key.Left);
        Assert.That(reese.IsExpanded, Is.False, "then collapses");
        Press(Key.Left);
        Press(Key.Down);
        Assert.That(tree.SelectedNode, Is.SameAs(sub));
        Press(Key.End);
        Assert.That(tree.SelectedNode, Is.SameAs(pads));
        Press(Key.Home);
        Assert.That(tree.SelectedNode, Is.SameAs(bass));
        Press(Key.K);
        Assert.That(tree.SelectedNode, Is.SameAs(keys), "type-ahead");
        Press(Key.Enter);
        Click(Row(4), 2);
        Assert.That(activated, Is.EqualTo(new[] { "Keys", "Pads" }));

        // Selecting a hidden node expands its ancestors; collapsing an ancestor moves the selection up.
        tree.SelectedNode = deep;
        Assert.That(reese.IsExpanded, Is.True);
        tree.SetExpanded(bass, false);
        Assert.That(tree.SelectedNode, Is.SameAs(bass));

        // A folder double-clicked toggles.
        Click(Row(0), 2);
        Assert.That(bass.IsExpanded, Is.True);
    }

    [Test]
    public void TheFilterKeepsTheAncestorsAndScreenReadersSeeATree()
    {
        var (tree, bass, sub, reese, deep, keys, pads) = Build();
        Add(tree, 200f, 220f);
        tree.Filter = "ee";
        Assert.That(tree.VisibleNodes, Is.EqualTo(new[] { bass, reese, deep }), "matches with their ancestors, open");
        Assert.That(tree.Matches(bass), Is.False);
        tree.Filter = null;
        Assert.That(tree.VisibleNodes, Is.EqualTo(new[] { bass, keys, pads }), "expanded states are kept");

        var peer = (TreeViewAutomationPeer)tree.GetAutomationPeer()!;
        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Tree));
            Assert.That(peer.GetChildren(), Has.Count.EqualTo(3));
            Assert.That(peer.GetItem(pads).Name, Is.EqualTo("Pads, 12"));
            Assert.That(peer.GetItem(pads).ExpandCollapseState, Is.EqualTo(ExpandCollapseState.LeafNode));
            Assert.That(peer.GetItem(bass).ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Collapsed));
            Assert.That((peer.GetItem(keys).PositionInSet, peer.GetItem(keys).SizeOfSet), Is.EqualTo((2, 3)));
        });
        peer.GetItem(bass).Expand();
        var reeseItem = peer.GetItem(reese);
        Assert.Multiple(() =>
        {
            Assert.That(bass.IsExpanded, Is.True);
            Assert.That(peer.GetItem(bass).GetChildren(), Is.EqualTo(new AutomationPeer[] { peer.GetItem(sub), reeseItem }));
            Assert.That(reeseItem.GetParent(), Is.SameAs(peer.GetItem(bass)));
            Assert.That(reeseItem.BoundingRectangle, Is.EqualTo(new Rect(1f, 49f, 198f, 24f)));
        });
        reeseItem.Select();
        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedNode, Is.SameAs(reese));
            Assert.That(peer.GetSelection(), Is.EqualTo(new AutomationPeer[] { reeseItem }));
            Assert.That(peer.GetFocusTarget(), Is.SameAs(reeseItem));
        });
        Assert.Throws<InvalidOperationException>(() => peer.GetItem(sub).Expand());

        // Moving a node to another parent removes it from the first one.
        keys.Children.Add(sub);
        Assert.Multiple(() =>
        {
            Assert.That(bass.Children, Is.EqualTo(new[] { reese }));
            Assert.That(sub.Parent, Is.SameAs(keys));
        });
        tree.Nodes.Remove(keys);
        tree.Refresh();
        Assert.That(keys.Parent, Is.Null);
    }

    private static PresetList Presets() => new(
    [
        new PresetEntry("Deep sub", "Bass/Sub"),
        new PresetEntry("Wobble", "Bass"),
        new PresetEntry("Glass", "Keys"),
        new PresetEntry("Warm pad", "Pads"),
        new PresetEntry("Init"),
    ]);

    [Test]
    public void ThePresetBrowserFiltersByCategoryAndSearchAndLoads()
    {
        var presets = Presets();
        var browser = Add(new PresetBrowser(presets), 460f, 300f);
        var tree = browser.Categories;
        Assert.Multiple(() =>
        {
            Assert.That(tree.Nodes.Select(n => n.Text + " " + n.Detail), Is.EqualTo(new[] { "All presets 5", "Favorites 0", "Bass 2", "Keys 1", "Pads 1" }));
            Assert.That(tree.Nodes[2].Children.Single().Text + " " + tree.Nodes[2].Children.Single().Detail, Is.EqualTo("Sub 1"));
            Assert.That(browser.VisiblePresets, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(browser.SearchBox.Placeholder, Is.EqualTo("Search presets"));
        });

        browser.SelectCategory("Bass");
        Assert.That(browser.VisiblePresets, Is.EqualTo(new[] { 0, 1 }));
        browser.SelectCategory("Bass/Sub");
        Assert.That(browser.VisiblePresets, Is.EqualTo(new[] { 0 }));
        browser.SelectCategory(null);
        browser.SearchText = "pad";
        Assert.That(browser.VisiblePresets, Is.EqualTo(new[] { 3 }));
        browser.SearchText = "sub deep";
        Assert.That(browser.VisiblePresets, Is.EqualTo(new[] { 0 }), "every word, in the name or the bank");
        browser.SearchText = "";

        int loaded = -1;
        browser.PresetLoaded += (_, index) => loaded = index;
        browser.PresetList.Focus();
        browser.PresetList.SelectedIndex = 2;
        Press(Key.Enter);
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(2));
            Assert.That(presets.CurrentIndex, Is.EqualTo(2));
        });
        browser.LoadOnSelect = true;
        Press(Key.Down);
        Assert.That(presets.CurrentIndex, Is.EqualTo(3), "loaded as soon as selected");

        // In the search box, Down goes to the list; Ctrl+F comes back.
        browser.SearchBox.Focus();
        browser.LoadOnSelect = false;
        Press(Key.Down);
        Assert.That(browser.PresetList.IsFocused, Is.True);
        Press(Key.F, ModifierKeys.Control);
        Assert.That(browser.SearchBox.IsFocused, Is.True);
    }

    [Test]
    public void FavoritesAreToggledWithTheStarAndCtrlD()
    {
        var presets = Presets();
        var browser = Add(new PresetBrowser(presets), 460f, 300f);
        int changes = 0;
        browser.FavoritesChanged += _ => changes++;

        // The list starts after the 170 px tree and a 6 px gap, below the 28 px search box and a 6 px gap.
        var list = browser.PresetList.Bounds;
        Assert.That((list.X, list.Y), Is.EqualTo((176f, 34f)));
        Click(new Point(list.X + 12f, list.Y + 1f + 26f * 2 + 13f));
        Assert.Multiple(() =>
        {
            Assert.That(browser.Favorites, Is.EquivalentTo(new[] { "Keys/Glass" }));
            Assert.That(browser.IsFavorite(2), Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(browser.Categories.Nodes[1].Detail, Is.EqualTo("1"));
            Assert.That(browser.PresetList.SelectedIndex, Is.EqualTo(-1), "the star does not select");
        });

        browser.PresetList.Focus();
        browser.PresetList.SelectedIndex = 4;
        Press(Key.D, ModifierKeys.Control);
        Assert.That(browser.Favorites, Is.EquivalentTo(new[] { "Keys/Glass", "Init" }));
        browser.SelectFavorites();
        Assert.That(browser.VisiblePresets, Is.EqualTo(new[] { 2, 4 }));

        presets.Load(2);
        var item = ((ListBoxAutomationPeer)browser.PresetList.GetAutomationPeer()!).GetItem(0);
        Assert.Multiple(() =>
        {
            Assert.That(item.Name, Is.EqualTo("Glass, Keys, favorite, loaded"));
            Assert.That(browser.PresetList.GetAutomationPeer()!.Name, Is.EqualTo("Presets"));
        });

        // Removing a favorite while showing the favorites hides it.
        browser.ToggleFavorite(4);
        Assert.That(browser.VisiblePresets, Is.EqualTo(new[] { 2 }));
        browser.ShowCategories = false;
        _host.UpdateLayout();
        Assert.That(browser.PresetList.Bounds.X, Is.EqualTo(0f));
    }
}
