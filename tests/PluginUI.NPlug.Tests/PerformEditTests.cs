// 2026/10/24 18:46:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NPlug;
using PluginUI.DemoSynth.Plugin;

namespace PluginUI.NPlug.Tests;

/// <summary>
/// Edits made in the editor reach the host's <c>IComponentHandler::performEdit</c> with the value as a double (NPlug
/// 0.6.1 passes it in an integer register on Windows x64; see <see cref="ComponentHandlerAbi"/>).
/// </summary>
public unsafe class PerformEditTests
{
    private static readonly List<(uint Id, double Value)> Edits = [];
    private static readonly List<string> Calls = [];

    [UnmanagedCallersOnly] private static int QueryInterface(void* self, void* iid, void** obj) { *obj = null; return unchecked((int)0x80004002); }
    [UnmanagedCallersOnly] private static uint AddRef(void* self) => 1;
    [UnmanagedCallersOnly] private static int BeginEdit(void* self, uint id) { Calls.Add("begin " + id); return 0; }
    [UnmanagedCallersOnly] private static int PerformEdit(void* self, uint id, double value) { Edits.Add((id, value)); return 0; }
    [UnmanagedCallersOnly] private static int EndEdit(void* self, uint id) { Calls.Add("end " + id); return 0; }
    [UnmanagedCallersOnly] private static int RestartComponent(void* self, int flags) => 0;

    /// <summary>A native IComponentHandler, as a host provides it.</summary>
    private static void* NativeHandler()
    {
        var vtable = (nint*)NativeMemory.Alloc(7 * (nuint)sizeof(nint));
        vtable[0] = (nint)(delegate* unmanaged<void*, void*, void**, int>)&QueryInterface;
        vtable[1] = (nint)(delegate* unmanaged<void*, uint>)&AddRef;
        vtable[2] = (nint)(delegate* unmanaged<void*, uint>)&AddRef;
        vtable[3] = (nint)(delegate* unmanaged<void*, uint, int>)&BeginEdit;
        vtable[4] = (nint)(delegate* unmanaged<void*, uint, double, int>)&PerformEdit;
        vtable[5] = (nint)(delegate* unmanaged<void*, uint, int>)&EndEdit;
        vtable[6] = (nint)(delegate* unmanaged<void*, int, int>)&RestartComponent;
        var obj = (nint*)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        obj[0] = (nint)vtable;
        return obj;
    }

    /// <summary>NPlug's own wrapper around a native handler (what the host's setComponentHandler creates).</summary>
    private static IAudioControllerHandler NPlugProxy(void* native)
    {
        var type = Type.GetType("NPlug.Interop.LibVst+AudioControllerHandlerProxy, NPlug", throwOnError: true)!;
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var pointerType = ctor.GetParameters()[0].ParameterType;
        return (IAudioControllerHandler)ctor.Invoke([Pointer.Box(native, pointerType)]);
    }

    [Test]
    public void EditsReachTheHostWithTheirValue()
    {
        Edits.Clear();
        Calls.Clear();
        void* native = NativeHandler();
        var proxy = NPlugProxy(native);
        Assert.That(ComponentHandlerAbi.IsAvailable, Is.True, "NPlug's handler proxy keeps the native pointer");
        Assert.That((nint)ComponentHandlerAbi.NativeHandler(proxy), Is.EqualTo((nint)native));

        var controller = new DemoSynthController();
        ((IAudioController)controller).SetControllerHandler(proxy);
        var parameter = Enumerable.Range(0, controller.Model.ParameterCount).Select(controller.Model.GetParameterByIndex)
            .First(p => p.StepCount == 0 && (p.Flags & AudioParameterFlags.IsReadOnly) == 0);
        var sink = (IParameterEditSink)controller;
        sink.BeginEdit(parameter);
        sink.PerformEdit(parameter, 0.25);
        sink.PerformEdit(parameter, 0.75);
        sink.EndEdit(parameter);

        uint id = (uint)parameter.Id.Value;
        Assert.Multiple(() =>
        {
            Assert.That(Calls, Is.EqualTo(new[] { "begin " + id, "end " + id }));
            Assert.That(Edits, Is.EqualTo(new[] { (id, 0.25), (id, 0.75) }), "the host receives the values, not 0");
            Assert.That(parameter.NormalizedValue, Is.EqualTo(0.75));
        });
    }
}
