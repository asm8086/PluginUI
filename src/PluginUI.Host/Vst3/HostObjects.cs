// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace PluginUI.Host.Vst3;

// Objects the host gives to plug-ins outside of audio processing.

/// <summary><c>IHostApplication</c>: the host name, and the factory of messages and attribute lists.</summary>
internal sealed unsafe class HostApplication : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, char*, int>)&GetName,
        (nint)(delegate* unmanaged[MemberFunction]<void*, Guid*, Guid*, void**, int>)&CreateInstance);

    public HostApplication(string name) : base((Tuid.IHostApplication, Vtable)) => Name = name;

    public string Name { get; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetName(void* self, char* name)
    {
        if (name is null) return Vst3Result.InvalidArgument;
        FixedText.WriteUtf16(name, 128, Get<HostApplication>(self).Name);
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int CreateInstance(void* self, Guid* cid, Guid* iid, void** obj)
    {
        if (obj is null || cid is null || iid is null) return Vst3Result.InvalidArgument;
        *obj = null;
        NativeComObject? created = null;
        if (*cid == Tuid.IMessage && (*iid == Tuid.IMessage || *iid == Tuid.FUnknown)) created = new Message();
        else if (*cid == Tuid.IAttributeList && (*iid == Tuid.IAttributeList || *iid == Tuid.FUnknown)) created = new AttributeList();
        if (created is null) return Vst3Result.False;
        *obj = created.Pointer; // the plug-in owns the first reference
        return Vst3Result.Ok;
    }
}

/// <summary><c>IMessage</c>: messages between the processor and the controller of a plug-in.</summary>
internal sealed unsafe class Message : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*>)&GetMessageId,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, void>)&SetMessageId,
        (nint)(delegate* unmanaged[MemberFunction]<void*, void*>)&GetAttributes);

    private byte* _id;
    private readonly AttributeList _attributes = new();

    public Message() : base((Tuid.IMessage, Vtable))
    {
    }

    protected override void OnFinalRelease()
    {
        NativeMemory.Free(_id);
        _id = null;
        _attributes.Release();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static byte* GetMessageId(void* self) => Get<Message>(self)._id;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static void SetMessageId(void* self, byte* id)
    {
        var message = Get<Message>(self);
        NativeMemory.Free(message._id);
        message._id = null;
        if (id is null) return;
        nuint length = 0;
        while (id[length] != 0) length++;
        message._id = (byte*)NativeMemory.Alloc(length + 1);
        Buffer.MemoryCopy(id, message._id, length + 1, length + 1);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static void* GetAttributes(void* self) => Get<Message>(self)._attributes.Pointer;
}

/// <summary><c>IAttributeList</c>: named integers, floats, strings and binary blobs.</summary>
internal sealed unsafe class AttributeList : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, long, int>)&SetInt,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, long*, int>)&GetInt,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, double, int>)&SetFloat,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, double*, int>)&GetFloat,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, char*, int>)&SetString,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, char*, uint, int>)&GetString,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, void*, uint, int>)&SetBinary,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, void**, uint*, int>)&GetBinary);

    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, nint> _blobs = new(StringComparer.Ordinal);

    public AttributeList() : base((Tuid.IAttributeList, Vtable))
    {
    }

    protected override void OnFinalRelease()
    {
        foreach (var blob in _blobs.Values) NativeMemory.Free((void*)blob);
        _blobs.Clear();
    }

    private static string Key(byte* id)
    {
        if (id is null) return string.Empty;
        int n = 0;
        while (id[n] != 0) n++;
        return Encoding.UTF8.GetString(id, n);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int SetInt(void* self, byte* id, long value)
    {
        Get<AttributeList>(self)._values[Key(id)] = value;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetInt(void* self, byte* id, long* value)
    {
        if (value is null || !Get<AttributeList>(self)._values.TryGetValue(Key(id), out var v) || v is not long l) return Vst3Result.False;
        *value = l;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int SetFloat(void* self, byte* id, double value)
    {
        Get<AttributeList>(self)._values[Key(id)] = value;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetFloat(void* self, byte* id, double* value)
    {
        if (value is null || !Get<AttributeList>(self)._values.TryGetValue(Key(id), out var v) || v is not double d) return Vst3Result.False;
        *value = d;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int SetString(void* self, byte* id, char* text)
    {
        Get<AttributeList>(self)._values[Key(id)] = text is null ? string.Empty : new string(text);
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetString(void* self, byte* id, char* text, uint sizeInBytes)
    {
        if (text is null || sizeInBytes < 2 || !Get<AttributeList>(self)._values.TryGetValue(Key(id), out var v) || v is not string s) return Vst3Result.False;
        FixedText.WriteUtf16(text, (int)(sizeInBytes / 2), s);
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int SetBinary(void* self, byte* id, void* data, uint size)
    {
        var list = Get<AttributeList>(self);
        string key = Key(id);
        if (list._blobs.Remove(key, out nint old)) NativeMemory.Free((void*)old);
        void* copy = NativeMemory.Alloc(Math.Max(1u, size));
        if (size > 0 && data is not null) Buffer.MemoryCopy(data, copy, size, size);
        list._blobs[key] = (nint)copy;
        list._values[key] = size;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetBinary(void* self, byte* id, void** data, uint* size)
    {
        var list = Get<AttributeList>(self);
        string key = Key(id);
        if (data is null || size is null || !list._blobs.TryGetValue(key, out nint blob) || list._values[key] is not uint length) return Vst3Result.False;
        *data = (void*)blob;
        *size = length;
        return Vst3Result.Ok;
    }
}

/// <summary><c>IBStream</c> over a growable byte buffer (plug-in and controller state, presets).</summary>
internal sealed unsafe class BStream : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, void*, int, int*, int>)&Read,
        (nint)(delegate* unmanaged[MemberFunction]<void*, void*, int, int*, int>)&Write,
        (nint)(delegate* unmanaged[MemberFunction]<void*, long, int, long*, int>)&Seek,
        (nint)(delegate* unmanaged[MemberFunction]<void*, long*, int>)&Tell);

    private readonly MemoryStream _stream;

    /// <summary>Empty stream to write into.</summary>
    public BStream() : base((Tuid.IBStream, Vtable)) => _stream = new MemoryStream();

    /// <summary>Stream reading <paramref name="data"/>.</summary>
    public BStream(ReadOnlySpan<byte> data) : base((Tuid.IBStream, Vtable))
    {
        _stream = new MemoryStream();
        _stream.Write(data);
        _stream.Position = 0;
    }

    /// <summary>Content written so far.</summary>
    public byte[] ToArray() => _stream.ToArray();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Read(void* self, void* buffer, int count, int* read)
    {
        if (buffer is null || count < 0) return Vst3Result.InvalidArgument;
        int n = Get<BStream>(self)._stream.Read(new Span<byte>(buffer, count));
        if (read is not null) *read = n;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Write(void* self, void* buffer, int count, int* written)
    {
        if (buffer is null || count < 0) return Vst3Result.InvalidArgument;
        Get<BStream>(self)._stream.Write(new ReadOnlySpan<byte>(buffer, count));
        if (written is not null) *written = count;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Seek(void* self, long position, int mode, long* result)
    {
        var stream = Get<BStream>(self)._stream;
        long target = mode switch
        {
            0 => position,
            1 => stream.Position + position,
            2 => stream.Length + position,
            _ => -1,
        };
        if (target < 0) return Vst3Result.InvalidArgument;
        stream.Position = target;
        if (result is not null) *result = target;
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int Tell(void* self, long* position)
    {
        if (position is null) return Vst3Result.InvalidArgument;
        *position = Get<BStream>(self)._stream.Position;
        return Vst3Result.Ok;
    }
}

/// <summary>What a plug-in instance reports to its host (edits, restarts, editor size).</summary>
internal unsafe interface IVst3HostCallbacks
{
    void BeginEdit(uint id);
    void PerformEdit(uint id, double value);
    void EndEdit(uint id);
    void RestartComponent(int flags);
    void SetDirty(bool dirty);
    bool ResizeView(void* view, ViewRect* rect);
}

/// <summary><c>IComponentHandler</c> and <c>IComponentHandler2</c>: edits made in the plug-in's editor.</summary>
internal sealed unsafe class ComponentHandler : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, uint, int>)&BeginEdit,
        (nint)(delegate* unmanaged[MemberFunction]<void*, uint, double, int>)&PerformEdit,
        (nint)(delegate* unmanaged[MemberFunction]<void*, uint, int>)&EndEdit,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int, int>)&RestartComponent);

    private static readonly nint Vtable2 = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte, int>)&SetDirty,
        (nint)(delegate* unmanaged[MemberFunction]<void*, byte*, int>)&RequestOpenEditor,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int>)&GroupEdit,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int>)&GroupEdit);

    private readonly IVst3HostCallbacks _host;

    public ComponentHandler(IVst3HostCallbacks host) : base((Tuid.IComponentHandler, Vtable), (Tuid.IComponentHandler2, Vtable2)) => _host = host;

    private static int Guard(Action action)
    {
        try
        {
            action();
            return Vst3Result.Ok;
        }
        catch
        {
            return Vst3Result.InternalError;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int BeginEdit(void* self, uint id) => Guard(() => Get<ComponentHandler>(self)._host.BeginEdit(id));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int PerformEdit(void* self, uint id, double value) => Guard(() => Get<ComponentHandler>(self)._host.PerformEdit(id, value));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int EndEdit(void* self, uint id) => Guard(() => Get<ComponentHandler>(self)._host.EndEdit(id));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RestartComponent(void* self, int flags) => Guard(() => Get<ComponentHandler>(self)._host.RestartComponent(flags));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int SetDirty(void* self, byte dirty) => Guard(() => Get<ComponentHandler>(self)._host.SetDirty(dirty != 0));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int RequestOpenEditor(void* self, byte* name) => Vst3Result.False;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GroupEdit(void* self) => Vst3Result.Ok;
}

/// <summary><c>IPlugFrame</c>: the plug-in asks for a new editor size.</summary>
internal sealed unsafe class PlugFrame : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, void*, ViewRect*, int>)&ResizeView);

    private readonly IVst3HostCallbacks _host;

    public PlugFrame(IVst3HostCallbacks host) : base((Tuid.IPlugFrame, Vtable)) => _host = host;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int ResizeView(void* self, void* view, ViewRect* rect)
    {
        if (rect is null) return Vst3Result.InvalidArgument;
        try
        {
            return Get<PlugFrame>(self)._host.ResizeView(view, rect) ? Vst3Result.Ok : Vst3Result.False;
        }
        catch
        {
            return Vst3Result.InternalError;
        }
    }
}
