// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Automation;

// Control patterns, implemented by automation peers. Every method is called on the UI thread and may throw
// InvalidOperationException when the element cannot do it now (disabled, read-only); the platform layer reports it.

/// <summary>Element that does one action when activated (buttons, links, menu entries).</summary>
public interface IInvokePattern
{
    /// <summary>Does the action, as a click or Enter would.</summary>
    void Invoke();
}

/// <summary>Element that cycles through states (check boxes, toggle buttons, switches).</summary>
public interface ITogglePattern
{
    /// <summary>Current state.</summary>
    ToggleState ToggleState { get; }

    /// <summary>Goes to the next state, as a click would.</summary>
    void Toggle();
}

/// <summary>Element whose value lies in a numeric range (knobs, sliders, progress bars).</summary>
public interface IRangeValuePattern
{
    /// <summary>Current value, in <see cref="Minimum"/>..<see cref="Maximum"/>.</summary>
    double Value { get; }

    /// <summary>Lowest value.</summary>
    double Minimum { get; }

    /// <summary>Highest value.</summary>
    double Maximum { get; }

    /// <summary>Change of an arrow key.</summary>
    double SmallChange { get; }

    /// <summary>Change of Page Up / Page Down.</summary>
    double LargeChange { get; }

    /// <summary><c>true</c> if <see cref="SetValue"/> is refused.</summary>
    bool IsReadOnly { get; }

    /// <summary>Sets the value as a complete user edit.</summary>
    /// <param name="value">New value, in <see cref="Minimum"/>..<see cref="Maximum"/>.</param>
    void SetValue(double value);
}

/// <summary>Element with a text value (text boxes, drop-downs, the formatted value of a knob).</summary>
public interface IValuePattern
{
    /// <summary>Value as text ("-6.0 dB", "Hall", the content of a text box).</summary>
    string Value { get; }

    /// <summary><c>true</c> if <see cref="SetValue"/> is refused.</summary>
    bool IsReadOnly { get; }

    /// <summary>Sets the value from text (parsed like a typed entry).</summary>
    /// <param name="value">Text to apply.</param>
    void SetValue(string value);
}

/// <summary>Element that shows or hides content (drop-downs, menu entries with a sub-menu, collapsible groups).</summary>
public interface IExpandCollapsePattern
{
    /// <summary>Current state.</summary>
    ExpandCollapseState ExpandCollapseState { get; }

    /// <summary>Shows the content.</summary>
    void Expand();

    /// <summary>Hides the content.</summary>
    void Collapse();
}

/// <summary>Item that can be selected in a container (options, tabs, list entries).</summary>
public interface ISelectionItemPattern
{
    /// <summary><c>true</c> if the item is selected.</summary>
    bool IsSelected { get; }

    /// <summary>Container implementing <see cref="ISelectionPattern"/>.</summary>
    AutomationPeer? SelectionContainer { get; }

    /// <summary>Selects this item only.</summary>
    void Select();

    /// <summary>Adds this item to the selection (multiple selection containers).</summary>
    void AddToSelection();

    /// <summary>Removes this item from the selection.</summary>
    void RemoveFromSelection();
}

/// <summary>Container of selectable items.</summary>
public interface ISelectionPattern
{
    /// <summary>Selected items.</summary>
    IReadOnlyList<AutomationPeer> GetSelection();

    /// <summary><c>true</c> if several items can be selected.</summary>
    bool CanSelectMultiple { get; }

    /// <summary><c>true</c> if one item is always selected.</summary>
    bool IsSelectionRequired { get; }
}
