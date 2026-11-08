// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst2;

/// <summary>Header of an audio block exchanged with a bridge (same layout in 32-bit and 64-bit processes).</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct BridgeAudioHeader
{
    [FieldOffset(0)] public int Frames;
    [FieldOffset(4)] public int Playing;
    [FieldOffset(8)] public long Position;
    [FieldOffset(16)] public double Tempo;
    [FieldOffset(24)] public int TimeSigNumerator;
    [FieldOffset(28)] public int TimeSigDenominator;
    [FieldOffset(32)] public int ChangeCount;
    [FieldOffset(36)] public int MidiInCount;
    [FieldOffset(40)] public int MidiOutCount;
    [FieldOffset(44)] public int Latency;
    [FieldOffset(48)] public int InputChannels;
    [FieldOffset(52)] public int OutputChannels;
    [FieldOffset(56)] public int Status;
}

/// <summary>A parameter change carried with an audio block.</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct BridgeParameterChange
{
    [FieldOffset(0)] public uint Id;
    [FieldOffset(8)] public double Value;
}

/// <summary>A short MIDI message carried with an audio block.</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct BridgeMidi
{
    [FieldOffset(0)] public int Offset;
    [FieldOffset(4)] public byte Status;
    [FieldOffset(5)] public byte Data1;
    [FieldOffset(6)] public byte Data2;
}

/// <summary>Failure of a bridge: it stopped, did not answer, or reported an error.</summary>
public sealed class Vst2BridgeException : PluginException
{
    /// <summary>Creates the exception.</summary>
    public Vst2BridgeException(string message) : base(message)
    {
    }
}

/// <summary>
/// The memory and the signals a host and a bridge process share: a control area for calls from the host's user interface
/// thread (described below), a mirror of the parameter values, and an audio area for the blocks of the audio thread.
/// </summary>
/// <remarks>
/// <para>Each side has a request and a reply event. A control call writes <c>[operation][length][payload]</c>, sets the
/// request and waits for the reply <c>[code][length][payload]</c> (code 0 success, 1 error with a message, 2 a reply too
/// large for the area, fetched in pieces). A request larger than the area is sent before in pieces
/// (<see cref="PutBlob"/>) and marked with <see cref="BlobFlag"/>.</para>
/// <para>The layout uses fixed offsets and sizes only, so that a 64-bit host and a 32-bit bridge agree.</para>
/// <para>Named (between processes) on Windows; without a name (in this process) everywhere, for tests.</para>
/// </remarks>
internal sealed unsafe class BridgeChannel : IDisposable
{
    public const int Magic = 0x55504232; // "2BPU"
    public const int Version = 1;
    public const int MaxParameters = 8192;
    public const int MaxChannels = 64;
    public const int MaxFrames = 4096;
    public const int MaxEvents = 1024;
    public const int DefaultControlCapacity = 4 * 1024 * 1024;

    /// <summary>Operation of a piece of a large request (payload: <c>[int offset][bytes]</c>).</summary>
    public const int PutBlob = 1000;

    /// <summary>Operation of a piece of a large reply (payload: <c>[long offset]</c>).</summary>
    public const int GetBlob = 1001;

    /// <summary>Marks a request whose payload was sent before with <see cref="PutBlob"/>.</summary>
    public const int BlobFlag = 0x10000;

    private const int HeaderSize = 256;

    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _base;
    private readonly int _controlOffset, _parametersOffset, _audioOffset, _changesOffset, _midiInOffset, _midiOutOffset, _samplesOffset;
    private byte[] _staging = [];
    private int _stagingLength;

    private BridgeChannel(MemoryMappedFile map, string? name, bool create, int controlCapacity)
    {
        _map = map;
        _view = map.CreateViewAccessor();
        byte* pointer = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _base = pointer + _view.PointerOffset;
        if (create)
        {
            ((int*)_base)[0] = Magic;
            ((int*)_base)[1] = Version;
            ((int*)_base)[2] = controlCapacity;
        }
        else
        {
            if (((int*)_base)[0] != Magic || ((int*)_base)[1] != Version) throw new Vst2BridgeException("The bridge and the host are not the same version.");
            controlCapacity = ((int*)_base)[2];
        }
        ControlCapacity = controlCapacity;
        _controlOffset = HeaderSize;
        _parametersOffset = Align(_controlOffset + 8 + controlCapacity);
        _audioOffset = Align(_parametersOffset + MaxParameters * sizeof(double));
        _changesOffset = _audioOffset + sizeof(BridgeAudioHeader);
        _midiInOffset = _changesOffset + MaxEvents * sizeof(BridgeParameterChange);
        _midiOutOffset = _midiInOffset + MaxEvents * sizeof(BridgeMidi);
        _samplesOffset = Align(_midiOutOffset + MaxEvents * sizeof(BridgeMidi));
        ControlRequest = Signal(name, ".cq", create);
        ControlReply = Signal(name, ".cr", create);
        AudioRequest = Signal(name, ".aq", create);
        AudioReply = Signal(name, ".ar", create);
    }

    private static int Align(int offset) => (offset + 63) & ~63;

    private static EventWaitHandle Signal(string? name, string suffix, bool create)
        => name is null ? new EventWaitHandle(false, EventResetMode.AutoReset)
            : create ? new EventWaitHandle(false, EventResetMode.AutoReset, name + suffix)
            : OperatingSystem.IsWindows() ? EventWaitHandle.OpenExisting(name + suffix)
            : throw new PlatformNotSupportedException("Bridges run on Windows only.");

    /// <summary>Size of the shared memory for a control area of <paramref name="controlCapacity"/> bytes.</summary>
    public static long SizeFor(int controlCapacity)
        => Align(Align(Align(HeaderSize + 8 + controlCapacity) + MaxParameters * sizeof(double)) + sizeof(BridgeAudioHeader)
            + MaxEvents * (sizeof(BridgeParameterChange) + 2 * sizeof(BridgeMidi))) + 2L * MaxChannels * MaxFrames * sizeof(float);

    /// <summary>Creates the channel (host side): named between processes, or in this process when <paramref name="name"/> is null.</summary>
    public static BridgeChannel Create(string? name, int controlCapacity = DefaultControlCapacity)
    {
        long size = SizeFor(controlCapacity);
        var map = name is null ? MemoryMappedFile.CreateNew(null, size) : MemoryMappedFile.CreateNew(name, size);
        return new BridgeChannel(map, name, true, controlCapacity);
    }

    /// <summary>Opens the channel a host created (bridge side).</summary>
    public static BridgeChannel Open(string name)
        => OperatingSystem.IsWindows() ? new(MemoryMappedFile.OpenExisting(name), name, false, 0) : throw new PlatformNotSupportedException("Bridges run on Windows only.");

    /// <summary>Bytes a control message carries.</summary>
    public int ControlCapacity { get; }

    public EventWaitHandle ControlRequest { get; }

    public EventWaitHandle ControlReply { get; }

    public EventWaitHandle AudioRequest { get; }

    public EventWaitHandle AudioReply { get; }

    /// <summary>Process identifier of the bridge (written by the bridge).</summary>
    public int BridgeProcessId
    {
        get => Volatile.Read(ref ((int*)_base)[3]);
        set => Volatile.Write(ref ((int*)_base)[3], value);
    }

    /// <summary>The parameter values, as the bridge last read them from the plug-in.</summary>
    public Span<double> Parameters => new(_base + _parametersOffset, MaxParameters);

    public ref BridgeAudioHeader Audio => ref *(BridgeAudioHeader*)(_base + _audioOffset);

    public Span<BridgeParameterChange> Changes => new(_base + _changesOffset, MaxEvents);

    public Span<BridgeMidi> MidiIn => new(_base + _midiInOffset, MaxEvents);

    public Span<BridgeMidi> MidiOut => new(_base + _midiOutOffset, MaxEvents);

    /// <summary>Samples of input channel <paramref name="channel"/> (<see cref="MaxFrames"/> floats).</summary>
    public float* Input(int channel) => (float*)(_base + _samplesOffset) + channel * MaxFrames;

    /// <summary>Samples of output channel <paramref name="channel"/>.</summary>
    public float* Output(int channel) => (float*)(_base + _samplesOffset) + (MaxChannels + channel) * MaxFrames;

    // ---------------------------------------------------------------- control: host side

    /// <summary>
    /// Makes a control call and returns the reply payload. <paramref name="alive"/> is asked while waiting: a bridge that
    /// stopped fails the call at once.
    /// </summary>
    /// <exception cref="Vst2BridgeException">The bridge stopped, did not answer within <paramref name="timeout"/>, or failed.</exception>
    public byte[] Call(int operation, ReadOnlySpan<byte> payload, Func<bool> alive, TimeSpan timeout)
    {
        if (payload.Length > ControlCapacity)
        {
            var piece = new byte[4 + ControlCapacity - 4];
            for (int offset = 0; offset < payload.Length; offset += ControlCapacity - 4)
            {
                int count = Math.Min(ControlCapacity - 4, payload.Length - offset);
                BitConverter.TryWriteBytes(piece, offset);
                payload.Slice(offset, count).CopyTo(piece.AsSpan(4));
                Exchange(PutBlob, piece.AsSpan(0, 4 + count), alive, timeout);
            }
            return Finish(Exchange(operation | BlobFlag, [], alive, timeout), alive, timeout);
        }
        return Finish(Exchange(operation, payload, alive, timeout), alive, timeout);
    }

    private (int Code, byte[] Payload) Exchange(int operation, ReadOnlySpan<byte> payload, Func<bool> alive, TimeSpan timeout)
    {
        byte* area = _base + _controlOffset;
        *(int*)area = operation;
        *(int*)(area + 4) = payload.Length;
        payload.CopyTo(new Span<byte>(area + 8, ControlCapacity));
        Thread.MemoryBarrier();
        ControlRequest.Set();
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!ControlReply.WaitOne(50))
        {
            if (!alive()) throw new Vst2BridgeException("The bridge stopped.");
            if (Environment.TickCount64 > deadline) throw new Vst2BridgeException("The bridge does not answer.");
        }
        Thread.MemoryBarrier();
        int code = *(int*)area, length = *(int*)(area + 4);
        return (code, new Span<byte>(area + 8, Math.Clamp(length, 0, ControlCapacity)).ToArray());
    }

    private byte[] Finish((int Code, byte[] Payload) reply, Func<bool> alive, TimeSpan timeout)
    {
        switch (reply.Code)
        {
            case 0:
                return reply.Payload;
            case 2:
            {
                long total = BitConverter.ToInt64(reply.Payload);
                var result = new byte[total];
                for (long offset = 0; offset < total;)
                {
                    var piece = Exchange(GetBlob, BitConverter.GetBytes(offset), alive, timeout);
                    if (piece.Code != 0 || piece.Payload.Length == 0) throw new Vst2BridgeException("The bridge sent an incomplete reply.");
                    piece.Payload.CopyTo(result, offset);
                    offset += piece.Payload.Length;
                }
                return result;
            }
            default:
                throw new Vst2BridgeException(System.Text.Encoding.UTF8.GetString(reply.Payload));
        }
    }

    // ---------------------------------------------------------------- control: bridge side

    /// <summary>
    /// Waits for a control call (bridge side) and answers it with <paramref name="handler"/> (operation, payload → reply);
    /// pieces of large requests and replies are handled here. <c>false</c> when nothing came within <paramref name="wait"/>.
    /// </summary>
    public bool Serve(Func<int, ReadOnlySpan<byte>, byte[]> handler, int wait)
    {
        if (!ControlRequest.WaitOne(wait)) return false;
        Thread.MemoryBarrier();
        byte* area = _base + _controlOffset;
        int operation = *(int*)area, length = Math.Clamp(*(int*)(area + 4), 0, ControlCapacity);
        var payload = new ReadOnlySpan<byte>(area + 8, length);
        int code = 0;
        byte[] reply;
        try
        {
            switch (operation)
            {
                case PutBlob:
                {
                    int offset = BitConverter.ToInt32(payload);
                    var piece = payload[4..];
                    if (offset == 0) _stagingLength = 0;
                    if (_staging.Length < offset + piece.Length) Array.Resize(ref _staging, Math.Max(offset + piece.Length, _staging.Length * 2));
                    piece.CopyTo(_staging.AsSpan(offset));
                    _stagingLength = offset + piece.Length;
                    reply = [];
                    break;
                }
                case GetBlob:
                {
                    long offset = BitConverter.ToInt64(payload);
                    int count = (int)Math.Clamp(_stagingLength - offset, 0, ControlCapacity);
                    reply = _staging.AsSpan((int)offset, count).ToArray();
                    break;
                }
                default:
                    reply = (operation & BlobFlag) != 0 ? handler(operation & ~BlobFlag, _staging.AsSpan(0, _stagingLength)) : handler(operation, payload);
                    if (reply.Length > ControlCapacity)
                    {
                        _staging = reply;
                        _stagingLength = reply.Length;
                        reply = BitConverter.GetBytes((long)_stagingLength);
                        code = 2;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            code = 1;
            reply = System.Text.Encoding.UTF8.GetBytes(ex.Message);
        }
        *(int*)area = code;
        *(int*)(area + 4) = reply.Length;
        reply.CopyTo(new Span<byte>(area + 8, ControlCapacity));
        Thread.MemoryBarrier();
        ControlReply.Set();
        return true;
    }

    /// <summary>Answers the host's first wait (bridge side): the plug-in is loaded, or it failed with <paramref name="error"/>.</summary>
    public void ReportStart(string? error)
    {
        byte* area = _base + _controlOffset;
        var message = error is null ? [] : System.Text.Encoding.UTF8.GetBytes(error);
        *(int*)area = error is null ? 0 : 1;
        *(int*)(area + 4) = message.Length;
        message.AsSpan(0, Math.Min(message.Length, ControlCapacity)).CopyTo(new Span<byte>(area + 8, ControlCapacity));
        Thread.MemoryBarrier();
        ControlReply.Set();
    }

    /// <summary>Waits for <see cref="ReportStart"/> (host side).</summary>
    /// <exception cref="Vst2BridgeException">The plug-in could not be loaded, or the bridge stopped or did not start in time.</exception>
    public void WaitForStart(Func<bool> alive, TimeSpan timeout) => Finish(WaitReply(alive, timeout), alive, timeout);

    private (int Code, byte[] Payload) WaitReply(Func<bool> alive, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!ControlReply.WaitOne(50))
        {
            if (!alive()) throw new Vst2BridgeException("The bridge stopped.");
            if (Environment.TickCount64 > deadline) throw new Vst2BridgeException("The bridge did not start in time.");
        }
        Thread.MemoryBarrier();
        byte* area = _base + _controlOffset;
        int code = *(int*)area, length = *(int*)(area + 4);
        return (code, new Span<byte>(area + 8, Math.Clamp(length, 0, ControlCapacity)).ToArray());
    }

    public void Dispose()
    {
        ControlRequest.Dispose();
        ControlReply.Dispose();
        AudioRequest.Dispose();
        AudioReply.Dispose();
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _map.Dispose();
    }
}
