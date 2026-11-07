// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT
//
// Sending system exclusive messages (the driver may keep the buffer after midiOutLongMsg returns: poll the unprepare with
// a back-off, reset as a last resort, leak rather than free a buffer the driver holds) follows NAudio's MidiOut (MIT,
// Copyright 2008-2026 Mark Heath; see build/THIRD-PARTY-NOTICES.txt).

using PluginUI.Host.Audio;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
using System.Runtime.InteropServices;

namespace PluginUI.Host.Windows;

/// <summary>A MIDI output port of Windows (winmm).</summary>
/// <param name="Index">Port number.</param>
/// <param name="Name">Port name.</param>
public sealed record MidiOutputPort(int Index, string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// Sends MIDI messages to an output port (a hardware synthesizer, another application through a loopback port).
/// </summary>
/// <remarks>
/// <see cref="Send"/> calls the driver directly (any thread except the audio thread). As an <see cref="IMidiSink"/> the
/// port takes messages on the audio thread without waiting: they are queued and sent by a thread of the port, in order.
/// </remarks>
public sealed unsafe class MidiOutput : IMidiSink, IDisposable
{
    private const uint MidiErrStillPlaying = 65; // MIDIERR_STILLPLAYING
    private readonly MidiEventQueue _queue = new(4096);
    private readonly AutoResetEvent _wake = new(false);
    private readonly Lock _send = new();
    private HMIDIOUT _handle;
    private Thread? _thread;
    private volatile bool _stop;

    private MidiOutput() { }

    /// <summary>Name of the open port.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The MIDI output ports (the first is often "Microsoft GS Wavetable Synth").</summary>
    public static IReadOnlyList<MidiOutputPort> Ports
    {
        get
        {
            var list = new List<MidiOutputPort>();
            uint count = midiOutGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                MIDIOUTCAPSW caps;
                if (midiOutGetDevCapsW(i, &caps, (uint)sizeof(MIDIOUTCAPSW)) != 0) continue;
                list.Add(new MidiOutputPort((int)i, new string((char*)&caps.szPname).TrimEnd('\0')));
            }
            return list;
        }
    }

    /// <summary>Opens a port.</summary>
    /// <exception cref="InvalidOperationException">The port cannot be opened (used by another application).</exception>
    public static MidiOutput Open(MidiOutputPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        HMIDIOUT handle;
        uint result = midiOutOpen(&handle, (uint)port.Index, 0, 0, 0 /* CALLBACK_NULL */);
        if (result != 0) throw new InvalidOperationException($"The MIDI port '{port.Name}' cannot be opened (error {result}; it may be used by another application).");
        var output = new MidiOutput { Name = port.Name, _handle = handle };
        output._thread = new Thread(output.Run) { Name = "MIDI output", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        output._thread.Start();
        return output;
    }

    /// <summary>Sends a short message now (not from the audio thread: use the port as an <see cref="IMidiSink"/> there).</summary>
    public void Send(MidiEvent e)
    {
        if (e.Status < 0x80) return;
        uint message = e.Status | (uint)(e.Data1 & 0x7F) << 8 | (uint)(e.Data2 & 0x7F) << 16;
        lock (_send)
        {
            if (_handle != HMIDIOUT.NULL) midiOutShortMsg(_handle, message);
        }
    }

    /// <inheritdoc />
    void IMidiSink.Send(in MidiEvent e)
    {
        _queue.Enqueue(e);
        _wake.Set();
    }

    /// <summary>Sends a system exclusive message (F0 … F7); waits until the driver has sent it.</summary>
    /// <exception cref="InvalidOperationException">The driver refused the message.</exception>
    public void SendSysex(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty) return;
        lock (_send)
        {
            if (_handle == HMIDIOUT.NULL) return;
            var header = (MIDIHDR*)NativeMemory.AllocZeroed((nuint)sizeof(MIDIHDR));
            header->lpData = (sbyte*)NativeMemory.Alloc((nuint)message.Length);
            message.CopyTo(new Span<byte>(header->lpData, message.Length));
            header->dwBufferLength = (uint)message.Length;
            header->dwBytesRecorded = (uint)message.Length;
            uint size = (uint)sizeof(MIDIHDR);
            bool driverOwns = false;
            try
            {
                uint result = midiOutPrepareHeader(_handle, header, size);
                if (result != 0) throw new InvalidOperationException($"The system exclusive message could not be prepared (error {result}).");
                driverOwns = true;
                result = midiOutLongMsg(_handle, header, size);
                // The driver may keep the buffer after midiOutLongMsg returns: wait until it gives it back.
                driverOwns = !Unprepare(header, size, message.Length);
                if (result != 0) throw new InvalidOperationException($"The system exclusive message could not be sent (error {result}).");
            }
            finally
            {
                // Leaking is the lesser evil: freeing a buffer the driver still holds corrupts the message or the heap.
                if (!driverOwns)
                {
                    NativeMemory.Free(header->lpData);
                    NativeMemory.Free(header);
                }
            }
        }
    }

    private bool Unprepare(MIDIHDR* header, uint size, int length)
    {
        long deadline = Environment.TickCount64 + 5000 + length;
        int delay = 1;
        uint result = midiOutUnprepareHeader(_handle, header, size);
        while (result == MidiErrStillPlaying && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(delay);
            delay = Math.Min(delay * 2, 50);
            result = midiOutUnprepareHeader(_handle, header, size);
        }
        if (result == MidiErrStillPlaying)
        {
            // A driver that stopped making progress: the reset marks every pending buffer as done.
            midiOutReset(_handle);
            result = midiOutUnprepareHeader(_handle, header, size);
        }
        return result != MidiErrStillPlaying;
    }

    /// <summary>Sends "all notes off" and "reset all controllers" on the 16 channels.</summary>
    public void AllNotesOff()
    {
        for (int channel = 0; channel < 16; channel++)
        {
            Send(MidiEvent.ControlChange(0, channel, 123, 0));
            Send(MidiEvent.ControlChange(0, channel, 121, 0));
        }
    }

    private void Run()
    {
        while (!_stop)
        {
            _wake.WaitOne(50);
            while (_queue.TryDequeue(out var e)) Send(e);
        }
    }

    /// <summary>Stops the sending thread, silences the port and closes it.</summary>
    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join();
        _thread = null;
        lock (_send)
        {
            if (_handle != HMIDIOUT.NULL)
            {
                midiOutReset(_handle);
                midiOutClose(_handle);
                _handle = HMIDIOUT.NULL;
            }
        }
        _wake.Dispose();
    }
}
