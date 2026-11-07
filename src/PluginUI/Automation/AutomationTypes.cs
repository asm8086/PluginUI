// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Automation;

/// <summary>
/// Role of an element as screen readers announce it ("button", "slider", "check box"…). Maps one to one onto the
/// UI Automation control types.
/// </summary>
public enum AutomationControlType
{
    /// <summary>Push button.</summary>
    Button,
    /// <summary>Check box (two or three states).</summary>
    CheckBox,
    /// <summary>Drop-down list.</summary>
    ComboBox,
    /// <summary>Text box.</summary>
    Edit,
    /// <summary>Link.</summary>
    Hyperlink,
    /// <summary>Picture.</summary>
    Image,
    /// <summary>Entry of a list.</summary>
    ListItem,
    /// <summary>List.</summary>
    List,
    /// <summary>Popup menu.</summary>
    Menu,
    /// <summary>Menu bar.</summary>
    MenuBar,
    /// <summary>Entry of a menu or of a menu bar.</summary>
    MenuItem,
    /// <summary>Progress bar or read-only level.</summary>
    ProgressBar,
    /// <summary>Option of a group of mutually exclusive options.</summary>
    RadioButton,
    /// <summary>Scroll bar.</summary>
    ScrollBar,
    /// <summary>Slider, knob, fader.</summary>
    Slider,
    /// <summary>Number box with up / down steps.</summary>
    Spinner,
    /// <summary>Status bar.</summary>
    StatusBar,
    /// <summary>Tab control.</summary>
    Tab,
    /// <summary>Tab of a tab control.</summary>
    TabItem,
    /// <summary>Static text.</summary>
    Text,
    /// <summary>Tool bar.</summary>
    ToolBar,
    /// <summary>Tooltip.</summary>
    ToolTip,
    /// <summary>Element with no standard role (graphs, meters, custom widgets).</summary>
    Custom,
    /// <summary>Group of related controls.</summary>
    Group,
    /// <summary>Window.</summary>
    Window,
    /// <summary>Pane (editor root, popups, dialogs).</summary>
    Pane,
    /// <summary>Separator.</summary>
    Separator,
    /// <summary>Tree of items that expand and collapse.</summary>
    Tree,
    /// <summary>Item of a tree.</summary>
    TreeItem,
}

/// <summary>Control patterns: the ways assistive technologies can act on an element.</summary>
public enum AutomationPattern
{
    /// <summary><see cref="IInvokePattern"/>.</summary>
    Invoke,
    /// <summary><see cref="ITogglePattern"/>.</summary>
    Toggle,
    /// <summary><see cref="IRangeValuePattern"/>.</summary>
    RangeValue,
    /// <summary><see cref="IValuePattern"/>.</summary>
    Value,
    /// <summary><see cref="IExpandCollapsePattern"/>.</summary>
    ExpandCollapse,
    /// <summary><see cref="ISelectionItemPattern"/>.</summary>
    SelectionItem,
    /// <summary><see cref="ISelectionPattern"/>.</summary>
    Selection,
}

/// <summary>State of a toggle.</summary>
public enum ToggleState
{
    /// <summary>Off / unchecked.</summary>
    Off = 0,
    /// <summary>On / checked.</summary>
    On = 1,
    /// <summary>Neither (mixed).</summary>
    Indeterminate = 2,
}

/// <summary>State of an element that shows or hides children (drop-downs, menus, collapsible groups).</summary>
public enum ExpandCollapseState
{
    /// <summary>Children hidden.</summary>
    Collapsed = 0,
    /// <summary>Children shown.</summary>
    Expanded = 1,
    /// <summary>Some children shown.</summary>
    PartiallyExpanded = 2,
    /// <summary>Nothing to expand.</summary>
    LeafNode = 3,
}

/// <summary>Orientation reported for sliders, lists, tool bars…</summary>
public enum AutomationOrientation
{
    /// <summary>Not applicable.</summary>
    None = 0,
    /// <summary>Horizontal.</summary>
    Horizontal = 1,
    /// <summary>Vertical.</summary>
    Vertical = 2,
}

/// <summary>Properties whose changes are reported to assistive technologies.</summary>
public enum AutomationProperty
{
    /// <summary><see cref="AutomationPeer.Name"/> (value: string).</summary>
    Name,
    /// <summary><see cref="AutomationPeer.IsEnabled"/> (value: bool).</summary>
    IsEnabled,
    /// <summary><see cref="IValuePattern.Value"/> (value: string).</summary>
    Value,
    /// <summary><see cref="IRangeValuePattern.Value"/> (value: double).</summary>
    RangeValue,
    /// <summary><see cref="ITogglePattern.ToggleState"/> (value: <see cref="Automation.ToggleState"/>).</summary>
    ToggleState,
    /// <summary><see cref="IExpandCollapsePattern.ExpandCollapseState"/> (value: <see cref="Automation.ExpandCollapseState"/>).</summary>
    ExpandCollapseState,
    /// <summary><see cref="ISelectionItemPattern.IsSelected"/> (value: bool).</summary>
    IsSelected,
    /// <summary><see cref="AutomationPeer.ItemStatus"/> (value: string).</summary>
    ItemStatus,
}

/// <summary>Events reported to assistive technologies besides focus and property changes.</summary>
public enum AutomationEvent
{
    /// <summary>A popup menu opened (raised on the menu).</summary>
    MenuOpened,
    /// <summary>A popup menu closed (raised on the menu, before it leaves the tree).</summary>
    MenuClosed,
    /// <summary>An element was invoked.</summary>
    Invoked,
    /// <summary>An element was selected among its siblings (raised on the selected item).</summary>
    ElementSelected,
    /// <summary>The text of a live region changed (status bars).</summary>
    LiveRegionChanged,
}

/// <summary>How urgently a screen reader should speak an announcement (<see cref="UIHost.Announce"/>).</summary>
public enum AnnouncementPriority
{
    /// <summary>Spoken after what is being read; a newer polite announcement replaces it.</summary>
    Polite,
    /// <summary>Interrupts what is being read.</summary>
    Important,
}
