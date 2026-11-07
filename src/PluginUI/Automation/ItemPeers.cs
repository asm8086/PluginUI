// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Automation;

/// <summary>
/// Base of the peers of items a control draws without being elements (options, tabs, list rows, menu entries,
/// status bar fields). The parent is the peer of the control.
/// </summary>
/// <param name="container">Peer of the control drawing the item.</param>
public abstract class ItemAutomationPeer(AutomationPeer container) : AutomationPeer
{
    /// <summary>Peer of the control drawing the item.</summary>
    public AutomationPeer Container { get; } = container;

    /// <inheritdoc />
    public override UIHost? Host => Container.Host;

    /// <inheritdoc />
    public override bool IsAvailable => Container.IsAvailable && Exists;

    /// <summary><c>true</c> while the item still exists in its control.</summary>
    protected abstract bool Exists { get; }

    /// <inheritdoc />
    public override bool IsEnabled => Container.IsEnabled;

    /// <inheritdoc />
    public override AutomationPeer? GetParent() => Container;

    /// <inheritdoc />
    public override string? ClassName => GetType().Name;

    /// <inheritdoc />
    public override void SetFocus() => Container.SetFocus();
}

// =====================================================================================
// Options: RadioGroup, SegmentedControl, multi-position Switch
// =====================================================================================

/// <summary>Value control choosing one option among several, drawn by the control.</summary>
internal interface IAutomationOptions
{
    int OptionCount { get; }
    int SelectedOption { get; }
    string GetOptionText(int index);
    Rect GetOptionBounds(int index);
}

/// <summary>
/// Peer of a control choosing one option among several (<see cref="RadioGroup"/>, <see cref="SegmentedControl"/>,
/// <see cref="Switch"/> with more than two positions): a group of radio buttons. The focus is on the selected option.
/// </summary>
/// <param name="owner">The control.</param>
public class OptionsAutomationPeer(ValueControl owner) : ElementAutomationPeer<ValueControl>(owner), ISelectionPattern
{
    private readonly List<OptionAutomationPeer> _options = new();
    private int _reportedSelection = -2;

    private IAutomationOptions Options => (IAutomationOptions)Owner;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    protected override string? GetNameCore() => base.GetNameCore() ?? Owner.Parameter?.Name;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    internal int OptionCount => Owner is IAutomationOptions o ? o.OptionCount : 0;

    internal int SelectedOption => Owner is IAutomationOptions o ? o.SelectedOption : -1;

    internal string GetOptionText(int index) => Options.GetOptionText(index);

    internal Rect GetOptionBounds(int index) => Options.GetOptionBounds(index);

    internal void Select(int index)
    {
        ThrowIfNotEnabled();
        int n = OptionCount;
        if (index < 0 || index >= n) throw new InvalidOperationException("The option no longer exists.");
        Owner.SetValueFromAutomation(n <= 1 ? 0.0 : index / (double)(n - 1));
    }

    /// <summary>Peer of option <paramref name="index"/>.</summary>
    /// <param name="index">Option index.</param>
    public AutomationPeer GetOption(int index)
    {
        while (_options.Count <= index) _options.Add(new OptionAutomationPeer(this, _options.Count));
        return _options[index];
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        for (int i = 0; i < OptionCount; i++) children.Add(GetOption(i));
    }

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
    {
        int selected = SelectedOption;
        return selected >= 0 && selected < OptionCount ? GetOption(selected) : this;
    }

    /// <inheritdoc />
    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        int selected = SelectedOption;
        return selected >= 0 && selected < OptionCount ? [GetOption(selected)] : [];
    }

    /// <inheritdoc />
    public bool CanSelectMultiple => false;

    /// <inheritdoc />
    public bool IsSelectionRequired => true;

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        int selected = SelectedOption;
        if (selected == _reportedSelection) return;
        if (_reportedSelection >= 0 && _reportedSelection < _options.Count) _options[_reportedSelection].RaisePropertyChanged(AutomationProperty.IsSelected, true, false);
        _reportedSelection = selected;
        if (selected >= 0 && selected < OptionCount)
        {
            var option = GetOption(selected);
            option.RaisePropertyChanged(AutomationProperty.IsSelected, false, true);
            option.RaiseEvent(AutomationEvent.ElementSelected);
        }
        Host?.UpdateAutomationFocus();
    }
}

/// <summary>Peer of one option of an <see cref="OptionsAutomationPeer"/>: a radio button.</summary>
public sealed class OptionAutomationPeer : ItemAutomationPeer, ISelectionItemPattern
{
    private readonly OptionsAutomationPeer _group;

    internal OptionAutomationPeer(OptionsAutomationPeer group, int index) : base(group)
    {
        _group = group;
        Index = index;
    }

    /// <summary>Option index.</summary>
    public int Index { get; }

    /// <inheritdoc />
    protected override bool Exists => Index < _group.OptionCount;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.RadioButton;

    /// <inheritdoc />
    public override string Name => Exists ? _group.GetOptionText(Index) : string.Empty;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => _group.IsEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Exists ? _group.GetOptionBounds(Index) : Rect.Empty;

    /// <inheritdoc />
    public override int PositionInSet => Index + 1;

    /// <inheritdoc />
    public override int SizeOfSet => _group.OptionCount;

    /// <inheritdoc />
    public bool IsSelected => _group.SelectedOption == Index;

    /// <inheritdoc />
    public AutomationPeer? SelectionContainer => _group;

    /// <inheritdoc />
    public void Select() => _group.Select(Index);

    /// <inheritdoc />
    public void AddToSelection() => Select();

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        if (IsSelected) throw new InvalidOperationException("One option is always selected.");
    }

    /// <inheritdoc />
    public override void SetFocus() => _group.Owner.Focus();
}

// =====================================================================================
// Drop-downs and menu buttons
// =====================================================================================

/// <summary>Peer of a <see cref="DropDown"/>: a combo box with the selected entry as value.</summary>
/// <param name="owner">The drop-down.</param>
public class DropDownAutomationPeer(DropDown owner) : ElementAutomationPeer<DropDown>(owner), IExpandCollapsePattern, IValuePattern
{
    private string? _reportedText;
    private ExpandCollapseState? _reportedState;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.ComboBox;

    /// <inheritdoc />
    protected override string? GetNameCore() => base.GetNameCore() ?? Owner.Parameter?.Name;

    /// <inheritdoc />
    public override string? ItemStatus => Owner.IsMidiLearning ? Strings.MidiLearnWaiting : null;

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState => Owner.IsMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <inheritdoc />
    public void Expand()
    {
        ThrowIfNotEnabled();
        Owner.OpenMenu();
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (Owner.IsMenuOpen) Owner.Host?.CloseAllPopups();
    }

    /// <inheritdoc />
    public string Value => Owner.SelectedText ?? Owner.Placeholder ?? string.Empty;

    /// <inheritdoc />
    public bool IsReadOnly => !Owner.IsEffectivelyEnabled;

    /// <inheritdoc />
    public void SetValue(string value)
    {
        ThrowIfNotEnabled();
        if (!Owner.SelectByText(value ?? string.Empty)) throw new ArgumentException("No entry has this text.", nameof(value));
    }

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        string text = Value;
        if (text != _reportedText)
        {
            RaisePropertyChanged(AutomationProperty.Value, _reportedText, text);
            _reportedText = text;
        }
        var state = ExpandCollapseState;
        if (state != _reportedState)
        {
            RaisePropertyChanged(AutomationProperty.ExpandCollapseState, _reportedState, state);
            _reportedState = state;
        }
    }
}

/// <summary>Peer of a <see cref="MenuButton"/>: a button that opens a menu.</summary>
/// <param name="owner">The menu button.</param>
public class MenuButtonAutomationPeer(MenuButton owner) : ElementAutomationPeer<MenuButton>(owner), IExpandCollapsePattern
{
    private ExpandCollapseState? _reportedState;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Button;

    /// <inheritdoc />
    protected override string? GetNameCore()
        => Owner.Text is { Length: > 0 } text ? text : base.GetNameCore() ?? (string.IsNullOrEmpty(Owner.GetToolTipText()) ? Strings.AutomationMenu : null);

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState => Owner.IsMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <inheritdoc />
    public void Expand()
    {
        ThrowIfNotEnabled();
        Owner.OpenMenu();
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (Owner.IsMenuOpen) Owner.Host?.CloseAllPopups();
    }

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        var state = ExpandCollapseState;
        if (state == _reportedState) return;
        RaisePropertyChanged(AutomationProperty.ExpandCollapseState, _reportedState, state);
        _reportedState = state;
    }
}

// =====================================================================================
// Tabs
// =====================================================================================

/// <summary>Peer of a <see cref="TabView"/>: a tab control whose tabs come first, then the content of the page.</summary>
/// <param name="owner">The tab view.</param>
public class TabViewAutomationPeer(TabView owner) : ElementAutomationPeer<TabView>(owner), ISelectionPattern
{
    private readonly List<TabItemAutomationPeer> _tabs = new();
    private int _reportedSelection = -2;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Tab;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    /// <summary>Peer of tab <paramref name="index"/>.</summary>
    /// <param name="index">Page index.</param>
    public AutomationPeer GetTab(int index)
    {
        while (_tabs.Count <= index) _tabs.Add(new TabItemAutomationPeer(this, _tabs.Count));
        return _tabs[index];
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        for (int i = 0; i < Owner.Pages.Count; i++) children.Add(GetTab(i));
        base.CollectChildren(children);
    }

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
    {
        int selected = Owner.SelectedIndex;
        return selected >= 0 && selected < Owner.Pages.Count ? GetTab(selected) : this;
    }

    /// <inheritdoc />
    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        int selected = Owner.SelectedIndex;
        return selected >= 0 && selected < Owner.Pages.Count ? [GetTab(selected)] : [];
    }

    /// <inheritdoc />
    public bool CanSelectMultiple => false;

    /// <inheritdoc />
    public bool IsSelectionRequired => true;

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        int selected = Owner.SelectedIndex;
        if (selected == _reportedSelection) return;
        if (_reportedSelection >= 0 && _reportedSelection < _tabs.Count) _tabs[_reportedSelection].RaisePropertyChanged(AutomationProperty.IsSelected, true, false);
        _reportedSelection = selected;
        if (selected >= 0 && selected < Owner.Pages.Count)
        {
            var tab = GetTab(selected);
            tab.RaisePropertyChanged(AutomationProperty.IsSelected, false, true);
            tab.RaiseEvent(AutomationEvent.ElementSelected);
        }
        Host?.NotifyAutomationStructureChanged();
        Host?.UpdateAutomationFocus();
    }
}

/// <summary>Peer of one tab of a <see cref="TabView"/>.</summary>
public sealed class TabItemAutomationPeer : ItemAutomationPeer, ISelectionItemPattern
{
    private readonly TabViewAutomationPeer _tabs;

    internal TabItemAutomationPeer(TabViewAutomationPeer tabs, int index) : base(tabs)
    {
        _tabs = tabs;
        Index = index;
    }

    /// <summary>Page index.</summary>
    public int Index { get; }

    private TabView View => _tabs.Owner;

    /// <inheritdoc />
    protected override bool Exists => Index < View.Pages.Count;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.TabItem;

    /// <inheritdoc />
    public override string Name => Exists ? View.Pages[Index].Header : string.Empty;

    /// <inheritdoc />
    public override string? HelpText => Exists ? View.Pages[Index].ToolTip : null;

    /// <inheritdoc />
    public override bool IsEnabled => _tabs.IsEnabled && Exists && View.Pages[Index].IsEnabled;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => IsEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Exists ? View.GetTabBounds(Index) : Rect.Empty;

    /// <inheritdoc />
    public override int PositionInSet => Index + 1;

    /// <inheritdoc />
    public override int SizeOfSet => View.Pages.Count;

    /// <inheritdoc />
    public bool IsSelected => View.SelectedIndex == Index;

    /// <inheritdoc />
    public AutomationPeer? SelectionContainer => _tabs;

    /// <inheritdoc />
    public void Select()
    {
        ThrowIfNotEnabled();
        View.SelectedIndex = Index;
    }

    /// <inheritdoc />
    public void AddToSelection() => Select();

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        if (IsSelected) throw new InvalidOperationException("One tab is always selected.");
    }

    /// <inheritdoc />
    public override void SetFocus() => View.Focus();
}

// =====================================================================================
// Lists
// =====================================================================================

/// <summary>Peer of a <see cref="ListBox"/>: a list of selectable entries; the focus is on the current entry.</summary>
/// <param name="owner">The list box.</param>
public class ListBoxAutomationPeer(ListBox owner) : ElementAutomationPeer<ListBox>(owner), ISelectionPattern
{
    private readonly List<ListItemAutomationPeer> _items = new();
    private int _reportedCurrent = -2;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.List;

    /// <inheritdoc />
    public override AutomationOrientation Orientation => AutomationOrientation.Vertical;

    /// <summary>Peer of entry <paramref name="index"/>.</summary>
    /// <param name="index">Entry index.</param>
    public AutomationPeer GetItem(int index)
    {
        while (_items.Count <= index) _items.Add(new ListItemAutomationPeer(this, _items.Count));
        return _items[index];
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        for (int i = 0; i < Owner.Count; i++) children.Add(GetItem(i));
    }

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
    {
        int current = Owner.CurrentIndex;
        return current >= 0 && current < Owner.Count ? GetItem(current) : this;
    }

    /// <inheritdoc />
    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        var result = new List<AutomationPeer>();
        foreach (int i in Owner.SelectedIndices.Order())
        {
            if (i >= 0 && i < Owner.Count) result.Add(GetItem(i));
        }
        return result;
    }

    /// <inheritdoc />
    public bool CanSelectMultiple => Owner.MultiSelect;

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        int current = Owner.CurrentIndex;
        if (current != _reportedCurrent)
        {
            _reportedCurrent = current;
            if (current >= 0 && current < Owner.Count && Owner.SelectedIndices.Contains(current)) GetItem(current).RaiseEvent(AutomationEvent.ElementSelected);
        }
        Host?.UpdateAutomationFocus();
    }
}

/// <summary>Peer of one entry of a <see cref="ListBox"/>.</summary>
public sealed class ListItemAutomationPeer : ItemAutomationPeer, ISelectionItemPattern
{
    private readonly ListBoxAutomationPeer _list;

    internal ListItemAutomationPeer(ListBoxAutomationPeer list, int index) : base(list)
    {
        _list = list;
        Index = index;
    }

    /// <summary>Entry index.</summary>
    public int Index { get; }

    private ListBox List => _list.Owner;

    /// <inheritdoc />
    protected override bool Exists => Index < List.Count;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.ListItem;

    /// <inheritdoc />
    public override string Name => Exists ? List.GetItemText(Index) : string.Empty;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => IsEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Exists ? List.GetRowBounds(Index) : Rect.Empty;

    /// <inheritdoc />
    public override bool IsOffscreen => !Exists || !List.GetRowBounds(Index).Intersects(List.Bounds) || base.IsOffscreen;

    /// <inheritdoc />
    public override int PositionInSet => Index + 1;

    /// <inheritdoc />
    public override int SizeOfSet => List.Count;

    /// <inheritdoc />
    public bool IsSelected => List.SelectedIndices.Contains(Index);

    /// <inheritdoc />
    public AutomationPeer? SelectionContainer => _list;

    /// <inheritdoc />
    public void Select()
    {
        ThrowIfNotEnabled();
        List.SelectFromAutomation(Index, ListSelectionChange.Replace);
    }

    /// <inheritdoc />
    public void AddToSelection()
    {
        ThrowIfNotEnabled();
        List.SelectFromAutomation(Index, List.MultiSelect ? ListSelectionChange.Add : ListSelectionChange.Replace);
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        ThrowIfNotEnabled();
        List.SelectFromAutomation(Index, ListSelectionChange.Remove);
    }

    /// <inheritdoc />
    public override void SetFocus() => List.Focus();
}

/// <summary>
/// Peer of a <see cref="DataTable"/>: a list whose entries are the rows (in view order); each row reads its first visible
/// column, then "Header: value" for the others. The focus is on the current row.
/// </summary>
/// <param name="owner">The table.</param>
public class DataTableAutomationPeer(DataTable owner) : ElementAutomationPeer<DataTable>(owner), ISelectionPattern
{
    private readonly List<DataRowAutomationPeer> _rows = new();
    private int _reportedCurrent = -2;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.List;

    /// <inheritdoc />
    public override AutomationOrientation Orientation => AutomationOrientation.Vertical;

    /// <summary>Peer of row <paramref name="index"/> (view order).</summary>
    /// <param name="index">Row index.</param>
    public AutomationPeer GetRow(int index)
    {
        while (_rows.Count <= index) _rows.Add(new DataRowAutomationPeer(this, _rows.Count));
        return _rows[index];
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        for (int i = 0; i < Owner.RowCount; i++) children.Add(GetRow(i));
    }

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
    {
        int current = Owner.CurrentRow;
        return current >= 0 && current < Owner.RowCount ? GetRow(current) : this;
    }

    /// <inheritdoc />
    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        var result = new List<AutomationPeer>();
        foreach (int i in Owner.SelectedRows.Order())
        {
            if (i >= 0 && i < Owner.RowCount) result.Add(GetRow(i));
        }
        return result;
    }

    /// <inheritdoc />
    public bool CanSelectMultiple => Owner.MultiSelect;

    /// <inheritdoc />
    public bool IsSelectionRequired => false;

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        int current = Owner.CurrentRow;
        if (current != _reportedCurrent)
        {
            _reportedCurrent = current;
            if (current >= 0 && current < Owner.RowCount && Owner.IsRowSelected(current)) GetRow(current).RaiseEvent(AutomationEvent.ElementSelected);
        }
        Host?.UpdateAutomationFocus();
    }
}

/// <summary>Peer of one row of a <see cref="DataTable"/>.</summary>
public sealed class DataRowAutomationPeer : ItemAutomationPeer, ISelectionItemPattern
{
    private readonly DataTableAutomationPeer _table;

    internal DataRowAutomationPeer(DataTableAutomationPeer table, int index) : base(table)
    {
        _table = table;
        Index = index;
    }

    /// <summary>Row index (view order).</summary>
    public int Index { get; }

    private DataTable Table => _table.Owner;

    /// <inheritdoc />
    protected override bool Exists => Index < Table.RowCount;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.ListItem;

    /// <inheritdoc />
    public override string Name => Exists ? Table.GetRowText(Index) : string.Empty;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => IsEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Exists ? Table.GetRowBounds(Index) : Rect.Empty;

    /// <inheritdoc />
    public override bool IsOffscreen => !Exists || !Table.GetRowBounds(Index).Intersects(Table.Bounds) || base.IsOffscreen;

    /// <inheritdoc />
    public override int PositionInSet => Index + 1;

    /// <inheritdoc />
    public override int SizeOfSet => Table.RowCount;

    /// <inheritdoc />
    public bool IsSelected => Table.IsRowSelected(Index);

    /// <inheritdoc />
    public AutomationPeer? SelectionContainer => _table;

    /// <inheritdoc />
    public void Select()
    {
        ThrowIfNotEnabled();
        Table.SelectFromAutomation(Index, ListSelectionChange.Replace);
    }

    /// <inheritdoc />
    public void AddToSelection()
    {
        ThrowIfNotEnabled();
        Table.SelectFromAutomation(Index, Table.MultiSelect ? ListSelectionChange.Add : ListSelectionChange.Replace);
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        ThrowIfNotEnabled();
        Table.SelectFromAutomation(Index, ListSelectionChange.Remove);
    }

    /// <inheritdoc />
    public override void SetFocus() => Table.Focus();
}

/// <summary>How <see cref="ListBox"/> selection changes from automation.</summary>
internal enum ListSelectionChange
{
    Replace,
    Add,
    Remove,
}

// =====================================================================================
// Menus
// =====================================================================================

/// <summary>Control showing menu entries: a <see cref="MenuBar"/> or a <see cref="MenuPopup"/>.</summary>
internal interface IMenuItemOwner
{
    AutomationPeer Peer { get; }
    IList<MenuItem> Items { get; }
    bool IsMenuBar { get; }
    Rect GetItemBounds(int index);
    bool IsExpanded(int index);
    void Expand(int index);
    void Collapse(int index);
    void Invoke(int index);
}

/// <summary>Peer of a <see cref="MenuBar"/>: its titles are menu items that expand into menus.</summary>
/// <param name="owner">The menu bar.</param>
public class MenuBarAutomationPeer(MenuBar owner) : ElementAutomationPeer<MenuBar>(owner), IMenuItemOwner
{
    private readonly MenuItemPeerCache _cache = new();

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.MenuBar;

    /// <inheritdoc />
    public override AutomationOrientation Orientation => AutomationOrientation.Horizontal;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    AutomationPeer IMenuItemOwner.Peer => this;
    IList<MenuItem> IMenuItemOwner.Items => Owner.Items;
    bool IMenuItemOwner.IsMenuBar => true;
    Rect IMenuItemOwner.GetItemBounds(int index) => Owner.ItemRect(index);
    bool IMenuItemOwner.IsExpanded(int index) => Owner.OpenIndex == index;
    void IMenuItemOwner.Expand(int index) => Owner.OpenMenu(index, fromKeyboard: true);
    void IMenuItemOwner.Collapse(int index)
    {
        if (Owner.OpenIndex == index) Owner.CloseMenu();
    }
    void IMenuItemOwner.Invoke(int index) => Owner.OpenMenu(index);

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children) => _cache.Collect(this, children);

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
    {
        int index = Owner.KeyboardIndex;
        return Owner.IsInMenuMode && index >= 0 && index < Owner.Items.Count ? _cache.Get(this, Owner.Items[index]) : this;
    }
}

/// <summary>Peer of a <see cref="MenuPopup"/>: a menu whose highlighted entry holds the focus.</summary>
/// <param name="owner">The menu.</param>
public class MenuAutomationPeer(MenuPopup owner) : ElementAutomationPeer<MenuPopup>(owner), IMenuItemOwner
{
    private readonly MenuItemPeerCache _cache = new();

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Menu;

    /// <inheritdoc />
    public override AutomationOrientation Orientation => AutomationOrientation.Vertical;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    AutomationPeer IMenuItemOwner.Peer => this;
    IList<MenuItem> IMenuItemOwner.Items => Owner.Items;
    bool IMenuItemOwner.IsMenuBar => false;
    Rect IMenuItemOwner.GetItemBounds(int index) => Owner.GetItemBounds(index);
    bool IMenuItemOwner.IsExpanded(int index) => Owner.IsSubmenuOpen(index);
    void IMenuItemOwner.Expand(int index) => Owner.OpenSubmenuFromAutomation(index);
    void IMenuItemOwner.Collapse(int index)
    {
        if (Owner.IsSubmenuOpen(index)) Owner.CloseSubmenuFromAutomation();
    }
    void IMenuItemOwner.Invoke(int index) => Owner.ActivateFromAutomation(index);

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children) => _cache.Collect(this, children);

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
    {
        int index = Owner.HighlightedIndex;
        return index >= 0 && index < Owner.Items.Count ? _cache.Get(this, Owner.Items[index]) : this;
    }
}

/// <summary>Peers of the entries of a menu, one per <see cref="MenuItem"/>.</summary>
internal sealed class MenuItemPeerCache
{
    private readonly Dictionary<MenuItem, MenuItemAutomationPeer> _peers = new(ReferenceEqualityComparer.Instance);

    public MenuItemAutomationPeer Get(IMenuItemOwner owner, MenuItem item)
    {
        if (!_peers.TryGetValue(item, out var peer))
        {
            peer = new MenuItemAutomationPeer(owner, item);
            _peers[item] = peer;
        }
        return peer;
    }

    public void Collect(IMenuItemOwner owner, List<AutomationPeer> children)
    {
        var items = owner.Items;
        if (_peers.Count > items.Count * 2 + 8)
        {
            // Rebuilt menus (recent files): forget the entries that left.
            var alive = new HashSet<MenuItem>(items, ReferenceEqualityComparer.Instance);
            foreach (var gone in _peers.Keys.Where(k => !alive.Contains(k)).ToList()) _peers.Remove(gone);
        }
        foreach (var item in items) children.Add(Get(owner, item));
    }
}

/// <summary>
/// Peer of an entry of a menu or of a menu bar: invoked when it runs a command, expanded when it opens a sub-menu,
/// toggled when it carries a check mark.
/// </summary>
public sealed class MenuItemAutomationPeer : ItemAutomationPeer, IInvokePattern, IExpandCollapsePattern, ITogglePattern
{
    private readonly IMenuItemOwner _owner;

    internal MenuItemAutomationPeer(IMenuItemOwner owner, MenuItem item) : base(owner.Peer)
    {
        _owner = owner;
        Item = item;
    }

    /// <summary>The entry.</summary>
    public MenuItem Item { get; }

    private int Index => _owner.Items.IndexOf(Item);

    /// <inheritdoc />
    protected override bool Exists => Index >= 0;

    /// <inheritdoc />
    public override AutomationControlType ControlType => Item.IsSeparator ? AutomationControlType.Separator : AutomationControlType.MenuItem;

    /// <inheritdoc />
    public override string Name => Item.IsSeparator ? string.Empty : Item.DisplayText;

    /// <inheritdoc />
    public override string? HelpText => Item.Description;

    /// <inheritdoc />
    public override string? AccessKey
    {
        get
        {
            char m = Item.Mnemonic;
            if (m == '\0' || Item.IsSeparator) return null;
            if (!_owner.IsMenuBar) return m.ToString();
            var strings = Host?.Strings ?? UIStrings.ForLanguage(UIHost.DefaultLanguage);
            return strings.KeyName(Key.Alt) + "+" + m;
        }
    }

    /// <inheritdoc />
    public override string? AcceleratorKey => Item.GetShortcutText(Host?.Strings);

    /// <inheritdoc />
    public override bool IsEnabled => Container.IsEnabled && Item.IsEnabled && !Item.IsSeparator;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => IsEnabled;

    /// <inheritdoc />
    public override bool IsContentElement => !Item.IsSeparator;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Index is int i and >= 0 ? _owner.GetItemBounds(i) : Rect.Empty;

    /// <inheritdoc />
    public override int PositionInSet
    {
        get
        {
            if (Item.IsSeparator) return 0;
            int position = 0;
            foreach (var item in _owner.Items)
            {
                if (item.IsSeparator) continue;
                position++;
                if (ReferenceEquals(item, Item)) return position;
            }
            return 0;
        }
    }

    /// <inheritdoc />
    public override int SizeOfSet => Item.IsSeparator ? 0 : _owner.Items.Count(i => !i.IsSeparator);

    private bool IsCheckable => Item.IsChecked || Item.Command?.IsCheckable == true;

    /// <inheritdoc />
    public override object? GetPattern(AutomationPattern pattern) => Item.IsSeparator ? null : pattern switch
    {
        AutomationPattern.Invoke => Item.HasSubmenu ? null : this,
        AutomationPattern.ExpandCollapse => Item.HasSubmenu ? this : null,
        AutomationPattern.Toggle => IsCheckable && !Item.HasSubmenu ? this : null,
        _ => null,
    };

    /// <inheritdoc />
    public void Invoke()
    {
        ThrowIfNotEnabled();
        _owner.Invoke(Index);
    }

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState
        => !Item.HasSubmenu ? ExpandCollapseState.LeafNode : Index is int i and >= 0 && _owner.IsExpanded(i) ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <inheritdoc />
    public void Expand()
    {
        ThrowIfNotEnabled();
        if (!Item.HasSubmenu) throw new InvalidOperationException("The entry has no sub-menu.");
        _owner.Expand(Index);
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (Index is int i and >= 0) _owner.Collapse(i);
    }

    /// <inheritdoc />
    public ToggleState ToggleState => Item.IsChecked ? ToggleState.On : ToggleState.Off;

    /// <inheritdoc />
    public void Toggle() => Invoke();

    /// <inheritdoc />
    public override void SetFocus()
    {
    }
}

// =====================================================================================
// Status bar
// =====================================================================================

/// <summary>Peer of a <see cref="StatusBar"/>: a status bar whose fields are texts.</summary>
/// <param name="owner">The status bar.</param>
public class StatusBarAutomationPeer(StatusBar owner) : ElementAutomationPeer<StatusBar>(owner)
{
    private readonly List<StatusFieldAutomationPeer> _fields = new();

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.StatusBar;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        var segments = Owner.Segments;
        while (_fields.Count < segments.Count) _fields.Add(new StatusFieldAutomationPeer(this, _fields.Count));
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].IsVisible) children.Add(_fields[i]);
        }
    }
}

/// <summary>Peer of one field of a <see cref="StatusBar"/>.</summary>
public sealed class StatusFieldAutomationPeer : ItemAutomationPeer
{
    private readonly StatusBarAutomationPeer _bar;

    internal StatusFieldAutomationPeer(StatusBarAutomationPeer bar, int index) : base(bar)
    {
        _bar = bar;
        Index = index;
    }

    /// <summary>Field index.</summary>
    public int Index { get; }

    private StatusBar Bar => _bar.Owner;

    /// <inheritdoc />
    protected override bool Exists => Index < Bar.Segments.Count && Bar.Segments[Index].IsVisible;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Text;

    /// <inheritdoc />
    public override string Name => Exists ? Bar.DisplayedText(Index) : string.Empty;

    /// <inheritdoc />
    public override Rect BoundingRectangle
    {
        get
        {
            if (!Exists) return Rect.Empty;
            var bounds = Bar.SegmentBounds();
            return Index < bounds.Count ? bounds[Index] : Rect.Empty;
        }
    }
}
