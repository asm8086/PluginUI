// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI;

public sealed partial class UIHost
{
    private HostAutomationPeer? _rootPeer;
    private IAutomationEventSink? _automationSink;
    private AutomationPeer? _automationFocus;
    private int _automationBatch;
    private bool _automationFocusDirty;
    private bool _automationStructureDirty;
    private string? _automationName;

    /// <summary>
    /// Name of the editor or window read by screen readers when the focus enters it (typically the plug-in or
    /// application name). Null = none (the window title is used where there is one).
    /// </summary>
    public string? AutomationName
    {
        get => _automationName;
        set
        {
            if (_automationName == value) return;
            string? old = _automationName;
            _automationName = value;
            if (_rootPeer is { } root) RaiseAutomationPropertyChanged(root, AutomationProperty.Name, old ?? string.Empty, value ?? string.Empty);
        }
    }

    /// <summary>Root of the accessibility tree: the root element and the open popups.</summary>
    public AutomationPeer RootAutomationPeer => _rootPeer ??= new HostAutomationPeer(this);

    /// <summary>Receiver of automation events (the UI Automation bridge on Windows, a spy in tests).</summary>
    internal IAutomationEventSink? AutomationSink
    {
        get => _automationSink;
        set
        {
            _automationSink = value;
            _automationFocus = null;
        }
    }

    /// <summary><c>true</c> while an assistive technology listens: state changes are then reported.</summary>
    internal bool IsAutomationActive => _automationSink is { IsListening: true };

    /// <summary>Menu bar in keyboard mode (F10 / Alt), whose highlighted title holds the automation focus.</summary>
    internal MenuBar? MenuModeBar { get; set; }

    /// <summary>
    /// Peer holding the focus for assistive technologies: the highlighted entry of an open menu, the highlighted title
    /// of a menu bar in keyboard mode, or the focused element (its current item for lists and option groups).
    /// Null when nothing has the focus (the editor itself).
    /// </summary>
    public AutomationPeer? GetFocusedAutomationPeer()
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var popup = _popups[i];
            if (popup is MenuPopup menu && menu.GetAutomationPeer() is { } menuPeer) return menuPeer.GetFocusTarget();
            if (popup.CapturesKeyboard || popup.IsModal)
            {
                if (_focused is not null && popup.IsAncestorOf(_focused)) break;
                return PeerOrAncestor(popup);
            }
        }
        if (MenuModeBar is { } bar && bar.GetAutomationPeer() is { } barPeer && bar.Host == this) return barPeer.GetFocusTarget();
        return _focused is null ? null : PeerOrAncestor(_focused)?.GetFocusTarget();
    }

    /// <summary>Peer under <paramref name="point"/> (host DIPs), refined into the items of the element; null over nothing.</summary>
    internal AutomationPeer? AutomationPeerAt(Point point)
    {
        for (var e = HitAt(point); e is not null; e = e.Parent)
        {
            if (e.GetAutomationPeer() is { IsAvailable: true } peer) return peer.GetPeerFromPoint(point);
        }
        return null;
    }

    private static AutomationPeer? PeerOrAncestor(UIElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (e.GetAutomationPeer() is { } peer) return peer;
        }
        return null;
    }

    /// <summary>
    /// Asks screen readers to speak <paramref name="text"/> (an action completed, a value reset, an error), without
    /// moving the focus. Does nothing when no assistive technology listens. Uses UI Automation notifications
    /// (Windows 10 1709 and later).
    /// </summary>
    /// <param name="text">Text to speak.</param>
    /// <param name="priority">Whether it interrupts what is being read.</param>
    public void Announce(string text, AnnouncementPriority priority = AnnouncementPriority.Polite)
    {
        if (string.IsNullOrEmpty(text) || !IsAutomationActive) return;
        try
        {
            _automationSink!.Announce(GetFocusedAutomationPeer() ?? RootAutomationPeer, text, priority);
        }
        catch (Exception ex)
        {
            ReportException(ex, "Announce");
        }
    }

    // ---------------------------------------------------------------- event plumbing

    internal void RaiseAutomationPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        if (!IsAutomationActive || Equals(oldValue, newValue) && oldValue is not null) return;
        try
        {
            _automationSink!.PropertyChanged(peer, property, oldValue, newValue);
        }
        catch (Exception ex)
        {
            ReportException(ex, "Automation property event");
        }
    }

    internal void RaiseAutomationEvent(AutomationPeer peer, AutomationEvent kind)
    {
        if (!IsAutomationActive) return;
        try
        {
            _automationSink!.EventRaised(peer, kind);
        }
        catch (Exception ex)
        {
            ReportException(ex, "Automation event");
        }
    }

    /// <summary>Notes that elements were added, removed, shown or hidden; reported once, after the current input.</summary>
    internal void NotifyAutomationStructureChanged()
    {
        if (!IsAutomationActive || _automationStructureDirty) return;
        _automationStructureDirty = true;
        if (_automationBatch == 0) Post(FlushAutomationStructure);
    }

    private void FlushAutomationStructure()
    {
        if (!_automationStructureDirty) return;
        _automationStructureDirty = false;
        if (!IsAutomationActive) return;
        try
        {
            _automationSink!.StructureChanged(RootAutomationPeer);
        }
        catch (Exception ex)
        {
            ReportException(ex, "Automation structure event");
        }
    }

    /// <summary>
    /// Recomputes the automation focus and reports a change. Inside input dispatch the check runs once, when the
    /// input has been handled (a menu that opens and highlights its first entry reports only the entry).
    /// </summary>
    internal void UpdateAutomationFocus()
    {
        if (!IsAutomationActive) return;
        if (_automationBatch > 0)
        {
            _automationFocusDirty = true;
            return;
        }
        _automationFocusDirty = false;
        AutomationPeer? focus;
        try
        {
            focus = GetFocusedAutomationPeer();
        }
        catch (Exception ex)
        {
            ReportException(ex, "Automation focus");
            return;
        }
        if (ReferenceEquals(focus, _automationFocus)) return;
        _automationFocus = focus;
        try
        {
            _automationSink!.FocusChanged(focus ?? RootAutomationPeer);
        }
        catch (Exception ex)
        {
            ReportException(ex, "Automation focus event");
        }
    }

    /// <summary>Groups the automation notifications of one input event.</summary>
    internal AutomationBatch BeginAutomationBatch()
    {
        _automationBatch++;
        return new AutomationBatch(this);
    }

    internal readonly struct AutomationBatch(UIHost host) : IDisposable
    {
        public void Dispose() => host.EndAutomationBatch();
    }

    private void EndAutomationBatch()
    {
        if (--_automationBatch > 0) return;
        _automationBatch = 0;
        if (_automationStructureDirty) FlushAutomationStructure();
        if (_automationFocusDirty || IsAutomationActive) UpdateAutomationFocus();
    }

    private void OnPopupShownForAutomation(Popup popup)
    {
        if (!IsAutomationActive) return;
        NotifyAutomationStructureChanged();
        if (popup is MenuPopup && popup.GetAutomationPeer() is { } peer) RaiseAutomationEvent(peer, AutomationEvent.MenuOpened);
        UpdateAutomationFocus();
    }

    private void OnPopupClosingForAutomation(Popup popup)
    {
        if (!IsAutomationActive) return;
        if (popup is MenuPopup && popup.ExistingAutomationPeer is { } peer) RaiseAutomationEvent(peer, AutomationEvent.MenuClosed);
    }

    private void OnPopupsClosedForAutomation()
    {
        if (!IsAutomationActive) return;
        NotifyAutomationStructureChanged();
        UpdateAutomationFocus();
    }
}

/// <summary>Receiver of the events PluginUI reports to assistive technologies.</summary>
internal interface IAutomationEventSink
{
    /// <summary><c>true</c> while a client listens (events are dropped otherwise).</summary>
    bool IsListening { get; }

    /// <summary>The automation focus moved to <paramref name="peer"/>.</summary>
    void FocusChanged(AutomationPeer peer);

    /// <summary>A property of <paramref name="peer"/> changed.</summary>
    void PropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue);

    /// <summary>An event happened on <paramref name="peer"/>.</summary>
    void EventRaised(AutomationPeer peer, AutomationEvent kind);

    /// <summary>The children below <paramref name="peer"/> changed.</summary>
    void StructureChanged(AutomationPeer peer);

    /// <summary>Text to speak.</summary>
    void Announce(AutomationPeer source, string text, AnnouncementPriority priority);
}
