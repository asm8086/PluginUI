// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Automation.Windows;

/// <summary>
/// UI Automation provider of one peer. Every call runs on the UI thread of the host (marshalled when UI Automation
/// calls from another thread) and fails with UIA_E_ELEMENTNOTAVAILABLE once the element left the tree.
/// </summary>
/// <remarks>One object implements every pattern interface; <see cref="GetPatternProvider"/> hands it out only for
/// the patterns the peer supports.</remarks>
[GeneratedComClass]
internal partial class UiaProvider :
    IRawElementProviderSimple, IRawElementProviderFragment,
    IInvokeProvider, IToggleProvider, IRangeValueProvider, IValueProvider, IExpandCollapseProvider,
    ISelectionItemProvider, ISelectionProvider
{
    protected UiaProvider(UiaBridge bridge, AutomationPeer peer)
    {
        Bridge = bridge;
        Peer = peer;
    }

    public UiaBridge Bridge { get; }

    public AutomationPeer Peer { get; }

    /// <summary>Provider of a peer (created on first use, then cached on the peer).</summary>
    public static UiaProvider For(UiaBridge bridge, AutomationPeer peer)
    {
        if (peer.PlatformObject is UiaProvider existing && ReferenceEquals(existing.Bridge, bridge)) return existing;
        var provider = ReferenceEquals(peer, bridge.Host.RootAutomationPeer) ? new UiaRootProvider(bridge, peer) : new UiaProvider(bridge, peer);
        peer.PlatformObject = provider;
        bridge.Track(provider);
        return provider;
    }

    protected bool IsRoot => this is UiaRootProvider;

    private static COMException NotAvailable() => new("The element is no longer available.", Uia.E_ELEMENTNOTAVAILABLE);

    /// <summary>Runs <paramref name="call"/> on the UI thread with a live peer.</summary>
    protected T Call<T>(Func<AutomationPeer, T> call) => Bridge.Host.RunOnUIThread(() =>
    {
        if (!IsRoot && !Peer.IsAvailable) throw NotAvailable();
        return call(Peer);
    });

    /// <summary>Runs an action asked by an assistive technology (reported as one input event).</summary>
    protected void Act(Action<AutomationPeer> action) => Bridge.Host.RunOnUIThread(() =>
    {
        if (!IsRoot && !Peer.IsAvailable) throw NotAvailable();
        using var batch = Bridge.Host.BeginAutomationBatch();
        action(Peer);
        return 0;
    });

    private T Pattern<T>(AutomationPattern pattern, AutomationPeer peer) where T : class
        => peer.GetPattern(pattern) as T ?? throw new InvalidOperationException("The element does not support this pattern.");

    private static int Bool(bool value) => value ? 1 : 0;

    // ---------------------------------------------------------------- IRawElementProviderSimple

    public int get_ProviderOptions() => Uia.ProviderOptions_ServerSideProvider | Uia.ProviderOptions_UseComThreading;

    public unsafe nint GetPatternProvider(int patternId)
    {
        if (Uia.Pattern(patternId) is not { } pattern) return 0;
        bool supported = Call(p => p.GetPattern(pattern) is not null);
        return supported ? (nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(this) : 0;
    }

    public unsafe int GetPropertyValue(int propertyId, VARIANT* value)
    {
        if (value is null) return E.E_POINTER;
        *value = default;
        try
        {
            *value = Call(peer => PropertyValue(peer, propertyId));
            return S.S_OK;
        }
        catch (Exception ex)
        {
            return ex.HResult;
        }
    }

    private static VARIANT PropertyValue(AutomationPeer peer, int id)
    {
        switch (id)
        {
            case Uia.ControlTypePropertyId: return Variants.Int(Uia.ControlTypeId(peer.ControlType));
            case Uia.LocalizedControlTypePropertyId: return Variants.Text(peer.LocalizedControlType);
            case Uia.NamePropertyId: return Variants.String(peer.Name ?? string.Empty);
            case Uia.AcceleratorKeyPropertyId: return Variants.Text(peer.AcceleratorKey);
            case Uia.AccessKeyPropertyId: return Variants.Text(peer.AccessKey);
            case Uia.HasKeyboardFocusPropertyId: return Variants.Bool(peer.HasKeyboardFocus && peer.Host?.HasKeyboardFocus == true);
            case Uia.IsKeyboardFocusablePropertyId: return Variants.Bool(peer.IsKeyboardFocusable);
            case Uia.IsEnabledPropertyId: return Variants.Bool(peer.IsEnabled);
            case Uia.AutomationIdPropertyId: return Variants.Text(peer.AutomationId);
            case Uia.ClassNamePropertyId: return Variants.Text(peer.ClassName);
            case Uia.HelpTextPropertyId: return Variants.Text(peer.HelpText);
            case Uia.IsControlElementPropertyId: return Variants.Bool(peer.IsControlElement);
            case Uia.IsContentElementPropertyId: return Variants.Bool(peer.IsContentElement);
            case Uia.IsPasswordPropertyId: return Variants.Bool(peer.IsPassword);
            case Uia.IsOffscreenPropertyId: return Variants.Bool(peer.IsOffscreen);
            case Uia.OrientationPropertyId: return Variants.Int((int)peer.Orientation);
            case Uia.FrameworkIdPropertyId: return Variants.String("PluginUI");
            case Uia.ItemStatusPropertyId: return Variants.Text(peer.ItemStatus);
            case Uia.IsDialogPropertyId: return peer.IsDialog ? Variants.Bool(true) : default;
            case Uia.PositionInSetPropertyId: return peer.PositionInSet > 0 ? Variants.Int(peer.PositionInSet) : default;
            case Uia.SizeOfSetPropertyId: return peer.SizeOfSet > 0 ? Variants.Int(peer.SizeOfSet) : default;
            case Uia.IsInvokePatternAvailablePropertyId: return Variants.Bool(peer.GetPattern(AutomationPattern.Invoke) is not null);
            case Uia.IsTogglePatternAvailablePropertyId: return Variants.Bool(peer.GetPattern(AutomationPattern.Toggle) is not null);
            case Uia.IsRangeValuePatternAvailablePropertyId: return Variants.Bool(peer.GetPattern(AutomationPattern.RangeValue) is not null);
            case Uia.IsValuePatternAvailablePropertyId: return Variants.Bool(peer.GetPattern(AutomationPattern.Value) is not null);
            case Uia.IsExpandCollapsePatternAvailablePropertyId: return Variants.Bool(peer.GetPattern(AutomationPattern.ExpandCollapse) is not null);
            case Uia.IsSelectionItemPatternAvailablePropertyId: return Variants.Bool(peer.GetPattern(AutomationPattern.SelectionItem) is not null);
            default: return default; // VT_EMPTY: UI Automation uses its default (or the window's provider for the root)
        }
    }

    public virtual nint get_HostRawElementProvider() => 0;

    // ---------------------------------------------------------------- IRawElementProviderFragment

    public IRawElementProviderFragment? Navigate(int direction) => Call(peer =>
    {
        AutomationPeer? target = null;
        switch (direction)
        {
            case Uia.NavigateParent:
                target = IsRoot ? null : peer.GetParent();
                break;
            case Uia.NavigateNextSibling:
            case Uia.NavigatePreviousSibling:
                if (!IsRoot && peer.GetParent() is { } parent)
                {
                    var siblings = parent.GetChildren();
                    int index = IndexOf(siblings, peer);
                    int next = index < 0 ? -1 : index + (direction == Uia.NavigateNextSibling ? 1 : -1);
                    target = next >= 0 && next < siblings.Count ? siblings[next] : null;
                }
                break;
            case Uia.NavigateFirstChild:
            case Uia.NavigateLastChild:
            {
                var children = peer.GetChildren();
                if (children.Count > 0) target = direction == Uia.NavigateFirstChild ? children[0] : children[^1];
                break;
            }
        }
        return target is null ? null : (IRawElementProviderFragment)For(Bridge, target);
    });

    private static int IndexOf(IReadOnlyList<AutomationPeer> list, AutomationPeer peer)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], peer)) return i;
        }
        return -1;
    }

    public unsafe nint GetRuntimeId()
    {
        if (IsRoot) return 0; // the window provides it
        int id = Peer.RuntimeId;
        var array = SafeArrayCreateVector((ushort)VARENUM.VT_I4, 0, 2);
        if (array is null) throw new OutOfMemoryException();
        int first = Uia.AppendRuntimeId;
        int index = 0;
        SafeArrayPutElement(array, &index, &first);
        index = 1;
        SafeArrayPutElement(array, &index, &id);
        return (nint)array;
    }

    public UiaRect get_BoundingRectangle() => Call(peer =>
    {
        var r = peer.BoundingRectangle;
        if (r.Width <= 0f || r.Height <= 0f || Bridge.Host is not { IsAttached: true } host) return default;
        var (left, top) = host.DipToScreen(new Point(r.X, r.Y));
        var (right, bottom) = host.DipToScreen(new Point(r.Right, r.Bottom));
        return new UiaRect { Left = left, Top = top, Width = right - left, Height = bottom - top };
    });

    public nint GetEmbeddedFragmentRoots() => 0;

    public void SetFocus() => Act(peer => peer.SetFocus());

    public IRawElementProviderFragmentRoot? get_FragmentRoot() => Bridge.Host.RunOnUIThread(() => (IRawElementProviderFragmentRoot)For(Bridge, Bridge.Host.RootAutomationPeer));

    // ---------------------------------------------------------------- patterns

    public void Invoke() => Act(peer => Pattern<IInvokePattern>(AutomationPattern.Invoke, peer).Invoke());

    public void Toggle() => Act(peer => Pattern<ITogglePattern>(AutomationPattern.Toggle, peer).Toggle());

    public int get_ToggleState() => Call(peer => (int)Pattern<ITogglePattern>(AutomationPattern.Toggle, peer).ToggleState);

    void IRangeValueProvider.SetValue(double val) => Act(peer => Pattern<IRangeValuePattern>(AutomationPattern.RangeValue, peer).SetValue(val));

    double IRangeValueProvider.get_Value() => Call(peer => Pattern<IRangeValuePattern>(AutomationPattern.RangeValue, peer).Value);

    int IRangeValueProvider.get_IsReadOnly() => Call(peer => Bool(Pattern<IRangeValuePattern>(AutomationPattern.RangeValue, peer).IsReadOnly));

    public double get_Maximum() => Call(peer => Pattern<IRangeValuePattern>(AutomationPattern.RangeValue, peer).Maximum);

    public double get_Minimum() => Call(peer => Pattern<IRangeValuePattern>(AutomationPattern.RangeValue, peer).Minimum);

    public double get_LargeChange() => Call(peer => Pattern<IRangeValuePattern>(AutomationPattern.RangeValue, peer).LargeChange);

    public double get_SmallChange() => Call(peer => Pattern<IRangeValuePattern>(AutomationPattern.RangeValue, peer).SmallChange);

    void IValueProvider.SetValue(string val) => Act(peer => Pattern<IValuePattern>(AutomationPattern.Value, peer).SetValue(val));

    string IValueProvider.get_Value() => Call(peer => Pattern<IValuePattern>(AutomationPattern.Value, peer).Value ?? string.Empty);

    int IValueProvider.get_IsReadOnly() => Call(peer => Bool(Pattern<IValuePattern>(AutomationPattern.Value, peer).IsReadOnly));

    public void Expand() => Act(peer => Pattern<IExpandCollapsePattern>(AutomationPattern.ExpandCollapse, peer).Expand());

    public void Collapse() => Act(peer => Pattern<IExpandCollapsePattern>(AutomationPattern.ExpandCollapse, peer).Collapse());

    public int get_ExpandCollapseState() => Call(peer => (int)Pattern<IExpandCollapsePattern>(AutomationPattern.ExpandCollapse, peer).ExpandCollapseState);

    public void Select() => Act(peer => Pattern<ISelectionItemPattern>(AutomationPattern.SelectionItem, peer).Select());

    public void AddToSelection() => Act(peer => Pattern<ISelectionItemPattern>(AutomationPattern.SelectionItem, peer).AddToSelection());

    public void RemoveFromSelection() => Act(peer => Pattern<ISelectionItemPattern>(AutomationPattern.SelectionItem, peer).RemoveFromSelection());

    public int get_IsSelected() => Call(peer => Bool(Pattern<ISelectionItemPattern>(AutomationPattern.SelectionItem, peer).IsSelected));

    public IRawElementProviderSimple? get_SelectionContainer() => Call(peer =>
        Pattern<ISelectionItemPattern>(AutomationPattern.SelectionItem, peer).SelectionContainer is { } container ? (IRawElementProviderSimple)For(Bridge, container) : null);

    public unsafe nint GetSelection()
    {
        var selection = Call(peer => Pattern<ISelectionPattern>(AutomationPattern.Selection, peer).GetSelection().Select(p => For(Bridge, p)).ToList());
        var array = SafeArrayCreateVector((ushort)VARENUM.VT_UNKNOWN, 0, (uint)selection.Count);
        if (array is null) throw new OutOfMemoryException();
        for (int i = 0; i < selection.Count; i++)
        {
            void* unknown = ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(selection[i]);
            try
            {
                int index = i;
                SafeArrayPutElement(array, &index, unknown); // AddRefs
            }
            finally
            {
                ComInterfaceMarshaller<IRawElementProviderSimple>.Free(unknown);
            }
        }
        return (nint)array;
    }

    public int get_CanSelectMultiple() => Call(peer => Bool(Pattern<ISelectionPattern>(AutomationPattern.Selection, peer).CanSelectMultiple));

    public int get_IsSelectionRequired() => Call(peer => Bool(Pattern<ISelectionPattern>(AutomationPattern.Selection, peer).IsSelectionRequired));
}

/// <summary>Provider of the root of a host: also the fragment root, hosted in the window.</summary>
[GeneratedComClass]
internal sealed partial class UiaRootProvider : UiaProvider, IRawElementProviderFragmentRoot
{
    public UiaRootProvider(UiaBridge bridge, AutomationPeer peer) : base(bridge, peer)
    {
    }

    public override nint get_HostRawElementProvider()
    {
        nint hwnd = Bridge.Host.WindowHandle;
        if (hwnd == 0) return 0;
        return Uia.UiaHostProviderFromHwnd(hwnd, out nint provider) >= 0 ? provider : 0;
    }

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y) => Call(_ =>
    {
        var host = Bridge.Host;
        var point = host.ScreenToDip((int)Math.Floor(x), (int)Math.Floor(y));
        var peer = host.AutomationPeerAt(point);
        return (IRawElementProviderFragment)For(Bridge, peer ?? host.RootAutomationPeer);
    });

    public IRawElementProviderFragment? GetFocus() => Call(_ =>
    {
        var focused = Bridge.Host.GetFocusedAutomationPeer();
        return focused is null || !focused.IsAvailable ? null : (IRawElementProviderFragment)For(Bridge, focused);
    });
}
