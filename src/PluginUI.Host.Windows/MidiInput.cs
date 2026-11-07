// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

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
/// Receives the short messages (notes, controllers, pitch bend…) of a MIDI input port. System messages (clock, active
/// sensing, system exclusive) are ignored.
/// </summary>
public sealed unsafe class MidiInput : IDisposable
{
    private readonly Action<MidiEvent> _received;
    private GCHandle _self;
    private HMIDIIN _handle;

    private MidiInput(Action<MidiEvent> received) => _received = received;

    /// <summary>Name of the open port.</summary>
    public string Name { get; private set; } = string.Empty;

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

    /// <summary>Opens a port.</summary>
    /// <param name="port">Port.</param>
    /// <param name="received">Called for each message, on a thread of the MIDI driver: keep it short (queue the message).</param>
    /// <exception cref="InvalidOperationException">The port cannot be opened (used by another application).</exception>
    public static MidiInput Open(MidiInputPort port, Action<MidiEvent> received)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(received);
        var input = new MidiInput(received) { Name = port.Name };
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
        midiInStart(handle);
        return input;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void Callback(HMIDIIN handle, uint message, nuint instance, nuint param1, nuint param2)
    {
        if (message != (uint)MIM.MIM_DATA || instance == 0) return;
        try
        {
            if (GCHandle.FromIntPtr((nint)instance).Target is not MidiInput input) return;
            uint data = (uint)param1;
            byte status = (byte)data;
            if (status < 0x80 || status >= 0xF0) return;
            input._received(new MidiEvent(0, status, (byte)((data >> 8) & 0x7F), (byte)((data >> 16) & 0x7F)));
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
            midiInStop(_handle);
            midiInReset(_handle);
            midiInClose(_handle);
            _handle = HMIDIIN.NULL;
        }
        if (_self.IsAllocated) _self.Free();
    }
}
