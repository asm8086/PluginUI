// 2026/10/24 18:46:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using NPlug;

namespace PluginUI.NPlug;

/// <summary>
/// Sends <c>IComponentHandler::performEdit</c> to the host with the value as a <see cref="double"/>, as the VST3 ABI
/// requires.
/// </summary>
/// <remarks>
/// <para>NPlug 0.6.1 declares the value of <c>performEdit</c> as <c>LibVst.ParamValue</c>, a struct wrapping a
/// <see cref="double"/>, in the function pointer it calls. On Windows x64 a struct of 8 bytes is passed in an integer
/// register (R8), while the host, compiled from <c>performEdit (ParamID id, ParamValue valueNormalized)</c> with
/// <c>typedef double ParamValue</c>, reads the value from XMM2: every edit made in the plug-in's editor reaches the
/// host as a meaningless value (0 in practice), so the processor never receives it. The editor still shows the
/// change, because the controller's own model is updated. Linux and macOS x64 (System V ABI) and ARM64 pass a
/// single-double struct in a floating-point register, so the bug only shows on Windows x64.</para>
/// <para>This helper reads the host's native <c>IComponentHandler*</c> kept by NPlug's handler proxy and calls its
/// <c>performEdit</c> slot with the correct signature. When the proxy is not NPlug's (tests, another NPlug version
/// without that field) it does nothing and the caller falls back on NPlug.</para>
/// </remarks>
internal static unsafe class ComponentHandlerAbi
{
    /// <summary>Vtable slot of <c>performEdit</c>: FUnknown (3), then beginEdit, performEdit.</summary>
    private const int PerformEditSlot = 4;

    // String literals: the trimmer and NativeAOT keep this type and field for reflection.
    private static readonly FieldInfo? HandlerField =
        Type.GetType("NPlug.Interop.LibVst+AudioControllerHandlerProxy, NPlug")
            ?.GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary><see langword="true"/> when NPlug's handler proxy and its native pointer were found.</summary>
    internal static bool IsAvailable => HandlerField is not null && HandlerField.FieldType.IsPointer;

    /// <summary>The host's native <c>IComponentHandler*</c> behind an NPlug handler, or <see langword="null"/>.</summary>
    internal static void* NativeHandler(IAudioControllerHandler? handler)
    {
        if (handler is null || !IsAvailable || handler.GetType() != HandlerField!.DeclaringType) return null;
        try
        {
            return HandlerField.GetValue(handler) is { } boxed ? Pointer.Unbox(boxed) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Calls <c>performEdit(id, value)</c> on the host's handler. Returns <see langword="false"/> when the native
    /// handler is not reachable (the caller then uses NPlug's call).
    /// </summary>
    internal static bool TryPerformEdit(IAudioControllerHandler? handler, AudioParameterId id, double normalizedValue)
    {
        void* native = NativeHandler(handler);
        if (native is null) return false;
        var performEdit = (delegate* unmanaged[MemberFunction]<void*, uint, double, int>)(*(void***)native)[PerformEditSlot];
        performEdit(native, (uint)id.Value, normalizedValue);
        return true;
    }
}
