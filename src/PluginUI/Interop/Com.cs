// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using TerraFX.Interop.Windows;

namespace PluginUI.Interop;

/// <summary>
/// COM helpers.
/// </summary>
internal static unsafe class Com
{
    /// <summary>Releases a COM pointer and nulls it (no-op if already null).</summary>
    public static void Release<T>(ref T* pointer) where T : unmanaged, IUnknown.Interface
    {
        T* local = pointer;
        if (local is not null)
        {
            pointer = null;
            local->Release();
        }
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> if the HRESULT is a failure.</summary>
    public static void ThrowIfFailed(HRESULT hr, string operation)
    {
        if (hr.FAILED) throw new InvalidOperationException($"{operation} failed with HRESULT 0x{hr.Value:X8}.");
    }
}
