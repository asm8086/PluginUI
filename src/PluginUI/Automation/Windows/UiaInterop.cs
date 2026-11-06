// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TerraFX.Interop.Windows;

namespace PluginUI.Automation.Windows;

// UI Automation provider interfaces (UIAutomationCore.h), declared for source-generated COM: NativeAOT compatible,
// no runtime marshalling. Methods are in vtable order; properties are spelled get_X as in the IDL. Each method
// returns an HRESULT; managed exceptions become their HResult (InvalidOperationException = UIA_E_INVALIDOPERATION).

/// <summary>Rectangle in screen pixels (left, top, width, height).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    int get_ProviderOptions();

    /// <returns>IUnknown* of the pattern provider (AddRef'd) or 0.</returns>
    nint GetPatternProvider(int patternId);

    /// <remarks>PreserveSig: VARIANT is not blittable for the generator, so it goes through a pointer.</remarks>
    [PreserveSig]
    unsafe int GetPropertyValue(int propertyId, VARIANT* value);

    /// <returns>IRawElementProviderSimple* of the window provider (AddRef'd) or 0.</returns>
    nint get_HostRawElementProvider();
}

[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    IRawElementProviderFragment? Navigate(int direction);

    /// <returns>SAFEARRAY(int)* or 0.</returns>
    nint GetRuntimeId();

    UiaRect get_BoundingRectangle();

    /// <returns>SAFEARRAY(IRawElementProviderSimple*)* or 0.</returns>
    nint GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot? get_FragmentRoot();
}

[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
}

[GeneratedComInterface]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IInvokeProvider
{
    void Invoke();
}

[GeneratedComInterface]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IToggleProvider
{
    void Toggle();

    int get_ToggleState();
}

[GeneratedComInterface]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IRangeValueProvider
{
    void SetValue(double val);

    double get_Value();

    int get_IsReadOnly();

    double get_Maximum();

    double get_Minimum();

    double get_LargeChange();

    double get_SmallChange();
}

[GeneratedComInterface]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IValueProvider
{
    void SetValue([MarshalAs(UnmanagedType.LPWStr)] string val);

    [return: MarshalAs(UnmanagedType.BStr)]
    string get_Value();

    int get_IsReadOnly();
}

[GeneratedComInterface]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
internal partial interface IExpandCollapseProvider
{
    void Expand();

    void Collapse();

    int get_ExpandCollapseState();
}

[GeneratedComInterface]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface ISelectionItemProvider
{
    void Select();

    void AddToSelection();

    void RemoveFromSelection();

    int get_IsSelected();

    IRawElementProviderSimple? get_SelectionContainer();
}

[GeneratedComInterface]
[Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168")]
internal partial interface ISelectionProvider
{
    /// <returns>SAFEARRAY(IRawElementProviderSimple*)* (never 0: an empty array when nothing is selected).</returns>
    nint GetSelection();

    int get_CanSelectMultiple();

    int get_IsSelectionRequired();
}

/// <summary>UIAutomationCore.dll exports and identifiers (verified against UIAutomationClient.h / UIAutomationCore.h).</summary>
internal static partial class Uia
{
    private const string Core = "UIAutomationCore.dll";

    public const int RootObjectId = -25;
    public const int AppendRuntimeId = 3;
    public const int HwndRuntimeId = 42;
    public const int E_ELEMENTNOTAVAILABLE = unchecked((int)0x80040201);

    public const int ProviderOptions_ServerSideProvider = 0x2;
    public const int ProviderOptions_UseComThreading = 0x20;

    // NavigateDirection
    public const int NavigateParent = 0, NavigateNextSibling = 1, NavigatePreviousSibling = 2, NavigateFirstChild = 3, NavigateLastChild = 4;

    // StructureChangeType / NotificationKind / NotificationProcessing
    public const int StructureChildrenInvalidated = 2;
    public const int NotificationKindOther = 4;
    public const int NotificationProcessingImportantMostRecent = 1;
    public const int NotificationProcessingMostRecent = 3;

    // Patterns
    public const int InvokePatternId = 10000, SelectionPatternId = 10001, ValuePatternId = 10002, RangeValuePatternId = 10003,
        ExpandCollapsePatternId = 10005, SelectionItemPatternId = 10010, TogglePatternId = 10015;

    // Events
    public const int MenuOpenedEventId = 20003, AutomationFocusChangedEventId = 20005, MenuClosedEventId = 20007,
        InvokeInvokedEventId = 20009, SelectionItemElementSelectedEventId = 20012, LiveRegionChangedEventId = 20024;

    // Properties
    public const int ControlTypePropertyId = 30003, LocalizedControlTypePropertyId = 30004, NamePropertyId = 30005,
        AcceleratorKeyPropertyId = 30006, AccessKeyPropertyId = 30007, HasKeyboardFocusPropertyId = 30008,
        IsKeyboardFocusablePropertyId = 30009, IsEnabledPropertyId = 30010, AutomationIdPropertyId = 30011,
        ClassNamePropertyId = 30012, HelpTextPropertyId = 30013, IsControlElementPropertyId = 30016,
        IsContentElementPropertyId = 30017, IsPasswordPropertyId = 30019, IsOffscreenPropertyId = 30022,
        OrientationPropertyId = 30023, FrameworkIdPropertyId = 30024, ItemStatusPropertyId = 30026,
        IsExpandCollapsePatternAvailablePropertyId = 30028, IsInvokePatternAvailablePropertyId = 30031,
        IsRangeValuePatternAvailablePropertyId = 30033, IsSelectionItemPatternAvailablePropertyId = 30036,
        IsTogglePatternAvailablePropertyId = 30041, IsValuePatternAvailablePropertyId = 30043,
        ValueValuePropertyId = 30045, RangeValueValuePropertyId = 30047, ExpandCollapseStatePropertyId = 30070,
        SelectionItemIsSelectedPropertyId = 30079, ToggleStatePropertyId = 30086, PositionInSetPropertyId = 30152,
        SizeOfSetPropertyId = 30153, IsDialogPropertyId = 30174;

    public static int ControlTypeId(AutomationControlType type) => type switch
    {
        AutomationControlType.Button => 50000,
        AutomationControlType.CheckBox => 50002,
        AutomationControlType.ComboBox => 50003,
        AutomationControlType.Edit => 50004,
        AutomationControlType.Hyperlink => 50005,
        AutomationControlType.Image => 50006,
        AutomationControlType.ListItem => 50007,
        AutomationControlType.List => 50008,
        AutomationControlType.Menu => 50009,
        AutomationControlType.MenuBar => 50010,
        AutomationControlType.MenuItem => 50011,
        AutomationControlType.ProgressBar => 50012,
        AutomationControlType.RadioButton => 50013,
        AutomationControlType.ScrollBar => 50014,
        AutomationControlType.Slider => 50015,
        AutomationControlType.Spinner => 50016,
        AutomationControlType.StatusBar => 50017,
        AutomationControlType.Tab => 50018,
        AutomationControlType.TabItem => 50019,
        AutomationControlType.Text => 50020,
        AutomationControlType.ToolBar => 50021,
        AutomationControlType.ToolTip => 50022,
        AutomationControlType.Group => 50026,
        AutomationControlType.Window => 50032,
        AutomationControlType.Pane => 50033,
        AutomationControlType.Separator => 50038,
        _ => 50025, // Custom
    };

    public static AutomationPattern? Pattern(int patternId) => patternId switch
    {
        InvokePatternId => AutomationPattern.Invoke,
        SelectionPatternId => AutomationPattern.Selection,
        ValuePatternId => AutomationPattern.Value,
        RangeValuePatternId => AutomationPattern.RangeValue,
        ExpandCollapsePatternId => AutomationPattern.ExpandCollapse,
        SelectionItemPatternId => AutomationPattern.SelectionItem,
        TogglePatternId => AutomationPattern.Toggle,
        _ => null,
    };

    [LibraryImport(Core)]
    public static partial nint UiaReturnRawElementProvider(nint hwnd, nuint wParam, nint lParam, nint provider);

    [LibraryImport(Core)]
    public static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    [LibraryImport(Core)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UiaClientsAreListening();

    [LibraryImport(Core)]
    public static partial int UiaRaiseAutomationEvent(nint provider, int eventId);

    private static unsafe delegate* unmanaged[Stdcall]<nint, int, VARIANT, VARIANT, int> s_raisePropertyChanged;

    /// <summary>
    /// UiaRaiseAutomationPropertyChangedEvent takes its VARIANTs by value: called through a function pointer, which
    /// passes unmanaged structs as the native ABI does without runtime marshalling.
    /// </summary>
    public static unsafe int UiaRaiseAutomationPropertyChangedEvent(nint provider, int propertyId, VARIANT oldValue, VARIANT newValue)
    {
        if (s_raisePropertyChanged == null)
        {
            nint module = NativeLibrary.Load(Core);
            s_raisePropertyChanged = (delegate* unmanaged[Stdcall]<nint, int, VARIANT, VARIANT, int>)NativeLibrary.GetExport(module, "UiaRaiseAutomationPropertyChangedEvent");
        }
        return s_raisePropertyChanged(provider, propertyId, oldValue, newValue);
    }

    [LibraryImport(Core)]
    public static unsafe partial int UiaRaiseStructureChangedEvent(nint provider, int structureChangeType, int* runtimeId, int runtimeIdLength);

    [LibraryImport(Core)]
    public static partial int UiaRaiseNotificationEvent(nint provider, int notificationKind, int notificationProcessing, nint displayString, nint activityId);

    [LibraryImport(Core)]
    public static partial int UiaDisconnectProvider(nint provider);
}

/// <summary>Builds VARIANTs (blittable TerraFX layout, correct on every architecture).</summary>
internal static unsafe class Variants
{
    public static VARIANT Int(int value)
    {
        VARIANT v = default;
        v.vt = (ushort)VARENUM.VT_I4;
        v.lVal = value;
        return v;
    }

    public static VARIANT Double(double value)
    {
        VARIANT v = default;
        v.vt = (ushort)VARENUM.VT_R8;
        v.dblVal = value;
        return v;
    }

    public static VARIANT Bool(bool value)
    {
        VARIANT v = default;
        v.vt = (ushort)VARENUM.VT_BOOL;
        v.boolVal = (short)(value ? -1 : 0); // VARIANT_TRUE
        return v;
    }

    public static VARIANT String(string value)
    {
        VARIANT v = default;
        v.vt = (ushort)VARENUM.VT_BSTR;
        v.bstrVal = (char*)Marshal.StringToBSTR(value);
        return v;
    }

    /// <summary>Text, or VT_EMPTY for null / empty text.</summary>
    public static VARIANT Text(string? value) => string.IsNullOrEmpty(value) ? default : String(value);

    public static void Clear(ref VARIANT v)
    {
        fixed (VARIANT* p = &v) TerraFX.Interop.Windows.Windows.VariantClear(p);
    }
}
