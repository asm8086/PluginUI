// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst3;

// Objects passed with every process call. Preallocated, no allocation and no lock: they run on the audio thread.

/// <summary><c>IParamValueQueue</c>: the automation points of one parameter within a block.</summary>
internal sealed unsafe class ParamValueQueue : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, uint>)&GetParameterId,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int>)&GetPointCount,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int, int*, double*, int>)&GetPoint,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int, double, int*, int>)&AddPoint);

    private readonly int[] _offsets;
    private readonly double[] _values;

    public ParamValueQueue(int capacity) : base((Tuid.IParamValueQueue, Vtable))
    {
        _offsets = new int[capacity];
        _values = new double[capacity];
    }

    public uint Id { get; set; }

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public int OffsetAt(int index) => _offsets[index];

    public double ValueAt(int index) => _values[index];

    /// <summary>Adds a point, keeping the offsets sorted (a later point at the same offset replaces the value).</summary>
    public bool Add(int sampleOffset, double value)
    {
        if (Count > 0 && _offsets[Count - 1] >= sampleOffset)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_offsets[i] == sampleOffset)
                {
                    _values[i] = value;
                    return true;
                }
            }
        }
        if (Count == _offsets.Length) return false;
        int at = Count;
        while (at > 0 && _offsets[at - 1] > sampleOffset)
        {
            _offsets[at] = _offsets[at - 1];
            _values[at] = _values[at - 1];
            at--;
        }
        _offsets[at] = sampleOffset;
        _values[at] = value;
        Count++;
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static uint GetParameterId(void* self) => Get<ParamValueQueue>(self).Id;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetPointCount(void* self) => Get<ParamValueQueue>(self).Count;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetPoint(void* self, int index, int* sampleOffset, double* value)
    {
        var queue = Get<ParamValueQueue>(self);
        if ((uint)index >= (uint)queue.Count) return Vst3Result.InvalidArgument;
        if (sampleOffset is not null) *sampleOffset = queue._offsets[index];
        if (value is not null) *value = queue._values[index];
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int AddPoint(void* self, int sampleOffset, double value, int* index)
    {
        var queue = Get<ParamValueQueue>(self);
        if (!queue.Add(sampleOffset, value)) return Vst3Result.False;
        if (index is not null)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue._offsets[i] == sampleOffset)
                {
                    *index = i;
                    break;
                }
            }
        }
        return Vst3Result.Ok;
    }
}

/// <summary><c>IParameterChanges</c>: the parameter queues of a block (host to plug-in, or plug-in to host).</summary>
internal sealed unsafe class ParameterChanges : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, int>)&GetParameterCount,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int, void*>)&GetParameterData,
        (nint)(delegate* unmanaged[MemberFunction]<void*, uint*, int*, void*>)&AddParameterData);

    private readonly ParamValueQueue[] _queues;

    public ParameterChanges(int parameterCapacity = 256, int pointCapacity = 64) : base((Tuid.IParameterChanges, Vtable))
    {
        _queues = new ParamValueQueue[parameterCapacity];
        for (int i = 0; i < _queues.Length; i++) _queues[i] = new ParamValueQueue(pointCapacity);
    }

    public int Count { get; private set; }

    public ParamValueQueue this[int index] => _queues[index];

    public void Clear()
    {
        for (int i = 0; i < Count; i++) _queues[i].Clear();
        Count = 0;
    }

    /// <summary>Queue of parameter <paramref name="id"/> (created if needed), or null when full.</summary>
    public ParamValueQueue? Queue(uint id, out int index)
    {
        for (int i = 0; i < Count; i++)
        {
            if (_queues[i].Id == id)
            {
                index = i;
                return _queues[i];
            }
        }
        if (Count == _queues.Length)
        {
            index = -1;
            return null;
        }
        var queue = _queues[Count];
        queue.Id = id;
        queue.Clear();
        index = Count++;
        return queue;
    }

    /// <summary>Adds a point for parameter <paramref name="id"/>.</summary>
    public bool Add(uint id, int sampleOffset, double value) => Queue(id, out _)?.Add(sampleOffset, value) == true;

    protected override void OnFinalRelease()
    {
        foreach (var queue in _queues) queue.Release();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetParameterCount(void* self) => Get<ParameterChanges>(self).Count;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static void* GetParameterData(void* self, int index)
    {
        var changes = Get<ParameterChanges>(self);
        return (uint)index < (uint)changes.Count ? changes._queues[index].Pointer : null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static void* AddParameterData(void* self, uint* id, int* index)
    {
        if (id is null) return null;
        var queue = Get<ParameterChanges>(self).Queue(*id, out int i);
        if (index is not null) *index = i;
        return queue is null ? null : queue.Pointer;
    }
}

/// <summary><c>IEventList</c>: notes and MIDI data of a block.</summary>
internal sealed unsafe class EventList : NativeComObject
{
    private static readonly nint Vtable = MakeVtable(
        (nint)(delegate* unmanaged[MemberFunction]<void*, int>)&GetEventCount,
        (nint)(delegate* unmanaged[MemberFunction]<void*, int, EventNative*, int>)&GetEvent,
        (nint)(delegate* unmanaged[MemberFunction]<void*, EventNative*, int>)&AddEvent);

    private readonly EventNative[] _events;

    public EventList(int capacity = 2048) : base((Tuid.IEventList, Vtable)) => _events = new EventNative[capacity];

    public int Count { get; private set; }

    public ref EventNative this[int index] => ref _events[index];

    public void Clear() => Count = 0;

    /// <summary>Adds an event, keeping them sorted by sample offset.</summary>
    public bool Add(in EventNative e)
    {
        if (Count == _events.Length) return false;
        int at = Count;
        while (at > 0 && _events[at - 1].SampleOffset > e.SampleOffset)
        {
            _events[at] = _events[at - 1];
            at--;
        }
        _events[at] = e;
        Count++;
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetEventCount(void* self) => Get<EventList>(self).Count;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int GetEvent(void* self, int index, EventNative* e)
    {
        var list = Get<EventList>(self);
        if (e is null || (uint)index >= (uint)list.Count) return Vst3Result.InvalidArgument;
        *e = list._events[index];
        return Vst3Result.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int AddEvent(void* self, EventNative* e)
    {
        if (e is null) return Vst3Result.InvalidArgument;
        return Get<EventList>(self).Add(*e) ? Vst3Result.Ok : Vst3Result.OutOfMemory;
    }
}
