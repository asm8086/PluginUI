// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Notes currently sounding, written by the audio (or MIDI) thread and read by the UI without locks: assign it to
/// <see cref="PianoKeyboard.ActivitySource"/> to light the keys played by the host, a sequencer or an arpeggiator.
/// </summary>
/// <remarks>
/// Each note keeps a counter, so overlapping note-ons of the same pitch (several channels, note ids) only clear when
/// the last one ends. Writers never allocate nor block; readers poll <see cref="Version"/> to skip unchanged frames.
/// </remarks>
/// <example>
/// <code>
/// // Audio thread (processor):
/// activity.NoteOn(pitch); ... activity.NoteOff(pitch);
/// // UI (editor):
/// keyboard.ActivitySource = activity;
/// </code>
/// </example>
public sealed class NoteActivity
{
    private readonly int[] _counts = new int[128];
    private int _version;

    /// <summary>Incremented on every change (wraps around).</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Marks a note as sounding (any thread).</summary>
    /// <param name="pitch">MIDI note number (out-of-range values are ignored).</param>
    public void NoteOn(int pitch)
    {
        if ((uint)pitch > 127) return;
        Interlocked.Increment(ref _counts[pitch]);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Ends one occurrence of a note (any thread); unmatched note-offs are ignored.</summary>
    /// <param name="pitch">MIDI note number (out-of-range values are ignored).</param>
    public void NoteOff(int pitch)
    {
        if ((uint)pitch > 127) return;
        ref int count = ref _counts[pitch];
        int current = Volatile.Read(ref count);
        while (current > 0)
        {
            int seen = Interlocked.CompareExchange(ref count, current - 1, current);
            if (seen == current)
            {
                Interlocked.Increment(ref _version);
                return;
            }
            current = seen;
        }
    }

    /// <summary>Clears every note (any thread), e.g. on "all notes off" or when processing stops.</summary>
    public void Clear()
    {
        for (int i = 0; i < 128; i++) Volatile.Write(ref _counts[i], 0);
        Interlocked.Increment(ref _version);
    }

    /// <summary><c>true</c> if the note is sounding.</summary>
    /// <param name="pitch">MIDI note number.</param>
    /// <returns>The state of the note.</returns>
    public bool IsOn(int pitch) => (uint)pitch <= 127 && Volatile.Read(ref _counts[pitch]) > 0;
}
