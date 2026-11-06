// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst3;

/// <summary>
/// Base of the objects the host hands to plug-ins (host application, component handler, streams, parameter changes…).
/// Each implemented interface gets a native block <c>[vtable*, GCHandle]</c>; the vtables are static
/// <c>UnmanagedCallersOnly</c> functions, so the plug-in calls managed code without runtime marshalling (NativeAOT, no
/// allocation on the audio thread).
/// </summary>
/// <remarks>
/// Reference counted: the creator holds the first reference and gives it up with <see cref="Release"/>. The managed object
/// stays alive while the count is positive; at zero the native blocks are freed.
/// </remarks>
internal abstract unsafe class NativeComObject
{
    private readonly Guid[] _iids;
    private readonly nint*[] _blocks;
    private GCHandle _handle;
    private int _refCount = 1;

    /// <summary>Creates the object with one reference.</summary>
    /// <param name="interfaces">Implemented interfaces and their vtables (the first one also answers FUnknown).</param>
    protected NativeComObject(params (Guid Iid, nint Vtable)[] interfaces)
    {
        _handle = GCHandle.Alloc(this);
        _iids = new Guid[interfaces.Length];
        _blocks = new nint*[interfaces.Length];
        for (int i = 0; i < interfaces.Length; i++)
        {
            _iids[i] = interfaces[i].Iid;
            var block = (nint*)NativeMemory.Alloc((nuint)(2 * sizeof(nint)));
            block[0] = interfaces[i].Vtable;
            block[1] = GCHandle.ToIntPtr(_handle);
            _blocks[i] = block;
        }
    }

    /// <summary>Pointer to the first interface (what is passed to the plug-in).</summary>
    public void* Pointer => _blocks[0];

    /// <summary>Pointer to interface <paramref name="iid"/>, without adding a reference (null if not implemented).</summary>
    public void* GetInterface(in Guid iid)
    {
        for (int i = 0; i < _iids.Length; i++)
        {
            if (_iids[i] == iid) return _blocks[i];
        }
        return null;
    }

    /// <summary><c>true</c> once the last reference is gone.</summary>
    public bool IsReleased => _refCount <= 0;

    public uint AddRef() => (uint)Interlocked.Increment(ref _refCount);

    public uint Release()
    {
        int count = Interlocked.Decrement(ref _refCount);
        if (count == 0)
        {
            OnFinalRelease();
            foreach (var block in _blocks) NativeMemory.Free(block);
            _handle.Free();
        }
        return (uint)Math.Max(0, count);
    }

    /// <summary>Called when the last reference goes away (free native resources here).</summary>
    protected virtual void OnFinalRelease()
    {
    }

    /// <summary>The managed object behind a native block.</summary>
    public static T Get<T>(void* self) where T : NativeComObject => (T)GCHandle.FromIntPtr(((nint*)self)[1]).Target!;

    // ---------------------------------------------------------------- vtables

    /// <summary>Allocates a vtable whose first three slots are FUnknown and the rest <paramref name="methods"/>.</summary>
    protected static nint MakeVtable(params nint[] methods)
    {
        var table = (nint*)NativeMemory.Alloc((nuint)((3 + methods.Length) * sizeof(nint)));
        table[0] = (nint)(delegate* unmanaged[MemberFunction]<void*, Guid*, void**, int>)&QueryInterfaceThunk;
        table[1] = (nint)(delegate* unmanaged[MemberFunction]<void*, uint>)&AddRefThunk;
        table[2] = (nint)(delegate* unmanaged[MemberFunction]<void*, uint>)&ReleaseThunk;
        for (int i = 0; i < methods.Length; i++) table[3 + i] = methods[i];
        return (nint)table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvMemberFunction)])]
    private static int QueryInterfaceThunk(void* self, Guid* iid, void** obj)
    {
        if (obj is null || iid is null) return Vst3Result.InvalidArgument;
        try
        {
            var target = Get<NativeComObject>(self);
            void* found = *iid == Tuid.FUnknown ? target.Pointer : target.GetInterface(*iid);
            if (found is null)
            {
                *obj = null;
                return Vst3Result.NoInterface;
            }
            target.AddRef();
            *obj = found;
            return Vst3Result.Ok;
        }
        catch
        {
            *obj = null;
            return Vst3Result.InternalError;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvMemberFunction)])]
    private static uint AddRefThunk(void* self) => Get<NativeComObject>(self).AddRef();

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvMemberFunction)])]
    private static uint ReleaseThunk(void* self) => Get<NativeComObject>(self).Release();
}
