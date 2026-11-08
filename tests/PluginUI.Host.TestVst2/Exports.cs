// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Host.Vst2;

namespace PluginUI.Host.Tests;

/// <summary>The entry point of the native module.</summary>
internal static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "VSTPluginMain", CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static AEffect* VstPluginMain(delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint> host) => FakeVst2.Create(host);
}
