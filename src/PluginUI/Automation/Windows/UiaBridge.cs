// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TerraFX.Interop.Windows;

namespace PluginUI.Automation.Windows;

/// <summary>
/// Connects a host to Windows UI Automation: answers WM_GETOBJECT with the root provider and raises the events of
/// the peers. Created on the first request of a client (Narrator, NVDA, Accessibility Insights…): a host nobody
/// inspects pays nothing.
/// </summary>
internal sealed class UiaBridge(UIHost host) : IAutomationEventSink
{
    private readonly List<WeakReference<UiaProvider>> _providers = new();
    private long _listeningCheckedAt = long.MinValue;
    private bool _listening;
    private bool _notificationsUnsupported;

    public UIHost Host { get; } = host;

    /// <summary>Answers WM_GETOBJECT for <see cref="Uia.RootObjectId"/>.</summary>
    public unsafe nint ReturnRootProvider(nint hwnd, nuint wParam, nint lParam)
    {
        var root = UiaProvider.For(this, Host.RootAutomationPeer);
        void* pointer = ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(root);
        try
        {
            _listeningCheckedAt = long.MinValue; // a client is there: check again now
            return Uia.UiaReturnRawElementProvider(hwnd, wParam, lParam, (nint)pointer);
        }
        finally
        {
            ComInterfaceMarshaller<IRawElementProviderSimple>.Free(pointer);
        }
    }

    internal void Track(UiaProvider provider)
    {
        if (_providers.Count > 64 && _providers.Count % 64 == 0) _providers.RemoveAll(w => !w.TryGetTarget(out _));
        _providers.Add(new WeakReference<UiaProvider>(provider));
    }

    /// <summary>Releases the clients' references: called before the window is destroyed.</summary>
    public unsafe void Disconnect(nint hwnd)
    {
        // Snapshot: disconnecting may call back into the providers (and create new ones).
        var providers = new List<UiaProvider>();
        foreach (var weak in _providers)
        {
            if (weak.TryGetTarget(out var provider)) providers.Add(provider);
        }
        _providers.Clear();
        try
        {
            if (hwnd != 0) Uia.UiaReturnRawElementProvider(hwnd, 0, 0, 0);
            foreach (var provider in providers) WithPointer(provider, p => Uia.UiaDisconnectProvider(p));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // UI Automation unavailable: nothing to release
        }
        finally
        {
            foreach (var provider in providers)
            {
                if (ReferenceEquals(provider.Peer.PlatformObject, provider)) provider.Peer.PlatformObject = null;
            }
            _providers.Clear();
        }
    }

    private static unsafe void WithPointer(UiaProvider provider, Func<nint, int> call)
    {
        void* pointer = ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(provider);
        try
        {
            call((nint)pointer);
        }
        finally
        {
            ComInterfaceMarshaller<IRawElementProviderSimple>.Free(pointer);
        }
    }

    private void Raise(AutomationPeer peer, Func<nint, int> call)
    {
        if (!peer.IsAvailable && peer is not HostAutomationPeer) return;
        WithPointer(UiaProvider.For(this, peer), call);
    }

    // ---------------------------------------------------------------- IAutomationEventSink

    public bool IsListening
    {
        get
        {
            if (!Host.IsAttached) return false;
            long now = Environment.TickCount64;
            if (now - _listeningCheckedAt < 1000) return _listening;
            _listeningCheckedAt = now;
            try
            {
                _listening = Uia.UiaClientsAreListening();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _listening = false;
                _listeningCheckedAt = long.MaxValue / 2; // never again
            }
            return _listening;
        }
    }

    public void FocusChanged(AutomationPeer peer)
    {
        // Screen readers follow the focus of the window that has the keyboard focus only.
        if (!Host.HasKeyboardFocus) return;
        Raise(peer, p => Uia.UiaRaiseAutomationEvent(p, Uia.AutomationFocusChangedEventId));
    }

    public void PropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        int id = property switch
        {
            AutomationProperty.Name => Uia.NamePropertyId,
            AutomationProperty.IsEnabled => Uia.IsEnabledPropertyId,
            AutomationProperty.Value => Uia.ValueValuePropertyId,
            AutomationProperty.RangeValue => Uia.RangeValueValuePropertyId,
            AutomationProperty.ToggleState => Uia.ToggleStatePropertyId,
            AutomationProperty.ExpandCollapseState => Uia.ExpandCollapseStatePropertyId,
            AutomationProperty.IsSelected => Uia.SelectionItemIsSelectedPropertyId,
            AutomationProperty.ItemStatus => Uia.ItemStatusPropertyId,
            _ => 0,
        };
        if (id == 0) return;
        var before = ToVariant(oldValue);
        var after = ToVariant(newValue);
        try
        {
            Raise(peer, p => Uia.UiaRaiseAutomationPropertyChangedEvent(p, id, before, after));
        }
        finally
        {
            Variants.Clear(ref before);
            Variants.Clear(ref after);
        }
    }

    private static VARIANT ToVariant(object? value) => value switch
    {
        null => default,
        string s => Variants.String(s),
        bool b => Variants.Bool(b),
        double d => Variants.Double(d),
        int i => Variants.Int(i),
        Enum e => Variants.Int(Convert.ToInt32(e, System.Globalization.CultureInfo.InvariantCulture)),
        _ => default,
    };

    public void EventRaised(AutomationPeer peer, AutomationEvent kind)
    {
        int id = kind switch
        {
            AutomationEvent.MenuOpened => Uia.MenuOpenedEventId,
            AutomationEvent.MenuClosed => Uia.MenuClosedEventId,
            AutomationEvent.Invoked => Uia.InvokeInvokedEventId,
            AutomationEvent.ElementSelected => Uia.SelectionItemElementSelectedEventId,
            AutomationEvent.LiveRegionChanged => Uia.LiveRegionChangedEventId,
            _ => 0,
        };
        if (id == 0) return;
        if (kind == AutomationEvent.MenuClosed)
        {
            // The menu is leaving the tree: raise on its provider even though the peer is about to become unavailable.
            if (peer.PlatformObject is UiaProvider provider) WithPointer(provider, p => Uia.UiaRaiseAutomationEvent(p, id));
            return;
        }
        Raise(peer, p => Uia.UiaRaiseAutomationEvent(p, id));
    }

    public unsafe void StructureChanged(AutomationPeer peer)
    {
        int* runtimeId = stackalloc int[2];
        if (peer is HostAutomationPeer)
        {
            runtimeId[0] = Uia.HwndRuntimeId;
            runtimeId[1] = (int)Host.WindowHandle;
        }
        else
        {
            runtimeId[0] = Uia.AppendRuntimeId;
            runtimeId[1] = peer.RuntimeId;
        }
        nint id = (nint)runtimeId;
        Raise(peer, p => Uia.UiaRaiseStructureChangedEvent(p, Uia.StructureChildrenInvalidated, (int*)id, 2));
    }

    public void Announce(AutomationPeer source, string text, AnnouncementPriority priority)
    {
        if (_notificationsUnsupported) return;
        nint display = Marshal.StringToBSTR(text);
        nint activity = Marshal.StringToBSTR("PluginUI.Announcement");
        try
        {
            int processing = priority == AnnouncementPriority.Important ? Uia.NotificationProcessingImportantMostRecent : Uia.NotificationProcessingMostRecent;
            Raise(source, p => Uia.UiaRaiseNotificationEvent(p, Uia.NotificationKindOther, processing, display, activity));
        }
        catch (EntryPointNotFoundException)
        {
            _notificationsUnsupported = true; // before Windows 10 1709
        }
        finally
        {
            Marshal.FreeBSTR(display);
            Marshal.FreeBSTR(activity);
        }
    }
}
