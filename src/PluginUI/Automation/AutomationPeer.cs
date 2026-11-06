// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Automation;

/// <summary>
/// What assistive technologies (Narrator, NVDA, JAWS…) see of an element: its role, name, state and the patterns that
/// act on it. PluginUI builds the accessibility tree from peers and exposes it through Windows UI Automation.
/// </summary>
/// <remarks>
/// <para>Peers come from <see cref="UIElement.GetAutomationPeer"/>; an element returning <c>null</c> is transparent
/// (its children appear in its place). Items drawn by a control without being elements (options, tabs, menu entries,
/// list rows) are virtual peers: children of the control's peer.</para>
/// <para>A peer implements the pattern interfaces it supports (<see cref="IInvokePattern"/>, <see cref="IRangeValuePattern"/>…);
/// <see cref="GetPattern"/> returns them. Every member is called on the UI thread.</para>
/// <para>Coordinates are host DIPs, like <see cref="UIElement.Bounds"/>.</para>
/// </remarks>
public abstract class AutomationPeer
{
    private static int s_nextId;

    /// <summary>Creates a peer.</summary>
    protected AutomationPeer() => RuntimeId = Interlocked.Increment(ref s_nextId);

    /// <summary>Identifier unique in the process (UI Automation runtime id).</summary>
    public int RuntimeId { get; }

    /// <summary>Platform object wrapping this peer (the UI Automation provider on Windows).</summary>
    internal object? PlatformObject { get; set; }

    // ---------------------------------------------------------------- tree

    /// <summary>Host of the element (null when detached).</summary>
    public abstract UIHost? Host { get; }

    /// <summary><c>true</c> while the element is in the tree (attached and visible).</summary>
    public virtual bool IsAvailable => Host is not null;

    /// <summary>Parent peer (null for the root of the host).</summary>
    public abstract AutomationPeer? GetParent();

    /// <summary>Child peers, in reading order.</summary>
    public IReadOnlyList<AutomationPeer> GetChildren()
    {
        var children = new List<AutomationPeer>();
        if (IsAvailable) CollectChildren(children);
        return children;
    }

    /// <summary>Adds the children of this peer to <paramref name="children"/>.</summary>
    /// <param name="children">List to fill, in reading order.</param>
    protected virtual void CollectChildren(List<AutomationPeer> children)
    {
    }

    /// <summary>
    /// Deepest virtual descendant at <paramref name="point"/>, or this peer. The host finds the element under the
    /// point; this refines it into the items the element draws.
    /// </summary>
    /// <param name="point">Point in host DIPs.</param>
    public virtual AutomationPeer GetPeerFromPoint(Point point)
    {
        var children = GetChildren();
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (child is not ElementAutomationPeer && child.BoundingRectangle.Contains(point)) return child.GetPeerFromPoint(point);
        }
        return this;
    }

    /// <summary>
    /// Peer that holds the focus when this element has it: the element itself, or its current item (selected option,
    /// current list row).
    /// </summary>
    public virtual AutomationPeer GetFocusTarget() => this;

    // ---------------------------------------------------------------- properties

    /// <summary>Role.</summary>
    public abstract AutomationControlType ControlType { get; }

    /// <summary>Role as spoken, when it differs from the standard one ("knob", "dialog"); null = the standard name.</summary>
    public virtual string? LocalizedControlType => null;

    /// <summary>Accessible name ("Cutoff", "Save").</summary>
    public virtual string Name => string.Empty;

    /// <summary>Longer description (a tooltip).</summary>
    public virtual string? HelpText => null;

    /// <summary>Stable identifier for test tools (the element <see cref="UIElement.Name"/>).</summary>
    public virtual string? AutomationId => null;

    /// <summary>Framework class name.</summary>
    public virtual string? ClassName => null;

    /// <summary>Key that activates the element with Alt ("Alt+F").</summary>
    public virtual string? AccessKey => null;

    /// <summary>Shortcut of the element ("Ctrl+S").</summary>
    public virtual string? AcceleratorKey => null;

    /// <summary>Additional state ("MIDI learn", "Clipping").</summary>
    public virtual string? ItemStatus => null;

    /// <summary><c>true</c> if the element accepts input.</summary>
    public virtual bool IsEnabled => true;

    /// <summary><c>true</c> if the element can take the keyboard focus.</summary>
    public virtual bool IsKeyboardFocusable => false;

    /// <summary><c>true</c> if this peer holds the focus (see <see cref="UIHost.GetFocusedAutomationPeer"/>).</summary>
    public bool HasKeyboardFocus => Host?.GetFocusedAutomationPeer() is { } focused && ReferenceEquals(focused, this);

    /// <summary><c>true</c> if the element is a control a user interacts with or reads (not a layout container).</summary>
    public virtual bool IsControlElement => true;

    /// <summary><c>true</c> if the element carries information for the user.</summary>
    public virtual bool IsContentElement => IsControlElement;

    /// <summary><c>true</c> for a text box whose content must not be read.</summary>
    public virtual bool IsPassword => false;

    /// <summary><c>true</c> for a dialog (screen readers announce its content when it opens).</summary>
    public virtual bool IsDialog => false;

    /// <summary>Position among its siblings, from 1 (0 = not applicable).</summary>
    public virtual int PositionInSet => 0;

    /// <summary>Number of siblings of the same kind (0 = not applicable).</summary>
    public virtual int SizeOfSet => 0;

    /// <summary>Orientation of sliders, lists, bars.</summary>
    public virtual AutomationOrientation Orientation => AutomationOrientation.None;

    /// <summary>Bounds in host DIPs.</summary>
    public abstract Rect BoundingRectangle { get; }

    /// <summary><c>true</c> if the element is not visible on screen (scrolled out, zero size).</summary>
    public virtual bool IsOffscreen
    {
        get
        {
            var r = BoundingRectangle;
            if (r.Width <= 0f || r.Height <= 0f || Host is not { } host) return true;
            var size = host.LogicalSize;
            return !r.Intersects(new Rect(0f, 0f, size.Width, size.Height));
        }
    }

    // ---------------------------------------------------------------- patterns & actions

    /// <summary>Object implementing <paramref name="pattern"/>, or null when the element does not support it.</summary>
    /// <param name="pattern">Pattern asked for.</param>
    public virtual object? GetPattern(AutomationPattern pattern) => pattern switch
    {
        AutomationPattern.Invoke => this as IInvokePattern,
        AutomationPattern.Toggle => this as ITogglePattern,
        AutomationPattern.RangeValue => this as IRangeValuePattern,
        AutomationPattern.Value => this as IValuePattern,
        AutomationPattern.ExpandCollapse => this as IExpandCollapsePattern,
        AutomationPattern.SelectionItem => this as ISelectionItemPattern,
        AutomationPattern.Selection => this as ISelectionPattern,
        _ => null,
    };

    /// <summary>Gives the focus to the element (asked by an assistive technology).</summary>
    public virtual void SetFocus()
    {
    }

    // ---------------------------------------------------------------- events

    /// <summary>Reports a property change to assistive technologies.</summary>
    /// <param name="property">Property that changed.</param>
    /// <param name="oldValue">Previous value (null if unknown).</param>
    /// <param name="newValue">New value.</param>
    protected internal void RaisePropertyChanged(AutomationProperty property, object? oldValue, object? newValue)
        => Host?.RaiseAutomationPropertyChanged(this, property, oldValue, newValue);

    /// <summary>Reports an event to assistive technologies.</summary>
    /// <param name="kind">Event.</param>
    protected internal void RaiseEvent(AutomationEvent kind) => Host?.RaiseAutomationEvent(this, kind);

    /// <summary>
    /// Called when the state of the element changed (value, selection, text…): reports what assistive technologies
    /// need. Default: nothing.
    /// </summary>
    protected internal virtual void OnOwnerStateChanged()
    {
    }

    /// <summary>Throws when the element cannot be acted on now.</summary>
    /// <exception cref="InvalidOperationException">The element is disabled or no longer available.</exception>
    protected void ThrowIfNotEnabled()
    {
        if (!IsAvailable || !IsEnabled) throw new InvalidOperationException("The element is not enabled.");
    }

    /// <inheritdoc />
    public override string ToString() => $"{ControlType} '{Name}'";
}
