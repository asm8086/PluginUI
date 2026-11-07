// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT
//
// The handling of system exclusive buffers (allocation, re-queuing, stop-before-reset, leaving to the driver a buffer it
// still holds) follows NAudio's MidiIn (MIT, Copyright 2008-2026 Mark Heath; see build/THIRD-PARTY-NOTICES.txt).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Host.Windows;

/// <summary>A MIDI input port of Windows (winmm).</summary>
/// <param name="Index">Port number.</param>
/// <param name="Name">Port name.</param>
public sealed record MidiInputPort(int Index, string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// Receives the messages of a MIDI input port: short messages (notes, controllers, pitch bend…) and, on request,
/// system exclusive messages. Clock and active sensing are ignored.
/// </summary>
/// <remarks>
/// The callbacks run on a thread of the MIDI driver: keep them short (queue the message). Short messages are passed
/// without allocation; each system exclusive message is copied to a new array.
/// </remarks>
public sealed unsafe class MidiInput : IDisposable
{
    private const int SysexBufferSize = 4096;
    private const int SysexBufferCount = 4;
    private const uint MidiErrStillPlaying = 65; // MIDIERR_STILLPLAYING

    private readonly Action<MidiEvent, uint> _received;
    private readonly Action<byte[], uint>? _sysex;
    private GCHandle _self;
    private HMIDIIN _handle;
    private MIDIHDR*[] _buffers = [];
    private volatile bool _closing;
    private int _errors;

    private MidiInput(Action<MidiEvent, uint> received, Action<byte[], uint>? sysex)
    {
        _received = received;
        _sysex = sysex;
    }

    /// <summary>Name of the open port.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Invalid messages reported by the driver since the port was opened.</summary>
    public int ErrorCount => Volatile.Read(ref _errors);

    /// <summary>The MIDI input ports.</summary>
    public static IReadOnlyList<MidiInputPort> Ports
    {
        get
        {
            var list = new List<MidiInputPort>();
            uint count = midiInGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                MIDIINCAPSW caps;
                if (midiInGetDevCapsW(i, &caps, (uint)sizeof(MIDIINCAPSW)) != 0) continue;
                list.Add(new MidiInputPort((int)i, new string((char*)&caps.szPname).TrimEnd('\0')));
            }
            return list;
        }
    }

    /// <summary>Opens a port for short messages.</summary>
    /// <param name="port">Port.</param>
    /// <param name="received">Called for each message (driver thread).</param>
    public static MidiInput Open(MidiInputPort port, Action<MidiEvent> received)
    {
        ArgumentNullException.ThrowIfNull(received);
        return Open(port, (e, _) => received(e));
    }

    /// <summary>Opens a port.</summary>
    /// <param name="port">Port.</param>
    /// <param name="received">Called for each short message with its time in milliseconds since the port started (driver thread).</param>
    /// <param name="sysex">Called for each system exclusive message (F0 … F7) with its time; null to ignore them.</param>
    /// <exception cref="InvalidOperationException">The port cannot be opened (used by another application).</exception>
    public static MidiInput Open(MidiInputPort port, Action<MidiEvent, uint> received, Action<byte[], uint>? sysex = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(received);
        var input = new MidiInput(received, sysex) { Name = port.Name };
        input._self = GCHandle.Alloc(input);
        HMIDIIN handle;
        delegate* unmanaged[Stdcall]<HMIDIIN, uint, nuint, nuint, nuint, void> callback = &Callback;
        uint result = midiInOpen(&handle, (uint)port.Index, (nuint)callback, (nuint)(nint)GCHandle.ToIntPtr(input._self), (uint)CALLBACK_FUNCTION);
        if (result != 0)
        {
            input._self.Free();
            throw new InvalidOperationException($"The MIDI port '{port.Name}' cannot be opened (error {result}; it may be used by another application).");
        }
        input._handle = handle;
        if (sysex is not null) input.AddSysexBuffers();
        midiInStart(handle);
        return input;
    }

    private void AddSysexBuffers()
    {
        _buffers = new MIDIHDR*[SysexBufferCount];
        for (int i = 0; i < SysexBufferCount; i++)
        {
            var header = (MIDIHDR*)NativeMemory.AllocZeroed((nuint)sizeof(MIDIHDR));
            header->lpData = (sbyte*)NativeMemory.Alloc(SysexBufferSize);
            header->dwBufferLength = SysexBufferSize;
            midiInPrepareHeader(_handle, header, (uint)sizeof(MIDIHDR));
            midiInAddBuffer(_handle, header, (uint)sizeof(MIDIHDR));
            _buffers[i] = header;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void Callback(HMIDIIN handle, uint message, nuint instance, nuint param1, nuint param2)
    {
        if (instance == 0) return;
        try
        {
            if (GCHandle.FromIntPtr((nint)instance).Target is not MidiInput input) return;
            uint time = (uint)param2;
            switch (message)
            {
                case (uint)MIM.MIM_DATA:
                {
                    uint data = (uint)param1;
                    byte status = (byte)data;
                    if (status < 0x80 || status >= 0xF0) return;
                    input._received(new MidiEvent(0, status, (byte)((data >> 8) & 0x7F), (byte)((data >> 16) & 0x7F)), time);
                    break;
                }
                case (uint)MIM.MIM_LONGDATA:
                {
                    var header = (MIDIHDR*)param1;
                    // Never trust the driver's count past the buffer we gave it.
                    int length = (int)Math.Min(header->dwBytesRecorded, header->dwBufferLength);
                    if (length > 0 && input._sysex is { } sysex) sysex(new ReadOnlySpan<byte>(header->lpData, length).ToArray(), time);
                    // While closing, the reset returns every buffer with no data: re-adding them would loop forever.
                    if (!input._closing) midiInAddBuffer(handle, header, (uint)sizeof(MIDIHDR));
                    break;
                }
                case (uint)MIM.MIM_ERROR:
                case (uint)MIM.MIM_LONGERROR:
                    Interlocked.Increment(ref input._errors);
                    break;
            }
        }
        catch
        {
            // Never let an exception reach the driver's thread.
        }
    }

    /// <summary>Closes the port.</summary>
    public void Dispose()
    {
        if (_handle != HMIDIIN.NULL)
        {
            _closing = true;
            // Stop before resetting: a reset on a running port returns the buffers while messages still arrive.
            midiInStop(_handle);
            midiInReset(_handle);
            foreach (var header in _buffers)
            {
                // A buffer the driver still holds is left to it rather than freed under its feet.
                if (midiInUnprepareHeader(_handle, header, (uint)sizeof(MIDIHDR)) == MidiErrStillPlaying) continue;
                NativeMemory.Free(header->lpData);
                NativeMemory.Free(header);
            }
            _buffers = [];
            midiInClose(_handle);
            _handle = HMIDIIN.NULL;
        }
        if (_self.IsAllocated) _self.Free();
    }
}
