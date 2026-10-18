// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.NPlug;

/// <summary>Kind of a note event coming from the editor.</summary>
public enum UINoteKind
{
    /// <summary>A note starts.</summary>
    NoteOn,

    /// <summary>A note stops.</summary>
    NoteOff,

    /// <summary>
    /// Every note stops (panic button). The processor decides what this covers; the demo synth releases all its voices.
    /// Closing the editor sends one <see cref="NoteOff"/> per held key instead.
    /// </summary>
    AllNotesOff,
}

/// <summary>A note played on the editor (on-screen keyboard, pads), delivered to the processor.</summary>
/// <param name="Kind">What happens.</param>
/// <param name="Pitch">MIDI note number (0..127); unused for <see cref="UINoteKind.AllNotesOff"/>.</param>
/// <param name="Velocity">Velocity in [0, 1] (note-on), release velocity (note-off).</param>
/// <param name="Channel">MIDI channel, 0-based.</param>
public readonly record struct UINoteEvent(UINoteKind Kind, int Pitch, float Velocity, int Channel = 0)
{
    /// <summary>A note-on event.</summary>
    /// <param name="pitch">MIDI note number.</param>
    /// <param name="velocity">Velocity in [0, 1].</param>
    /// <param name="channel">MIDI channel, 0-based.</param>
    /// <returns>The event.</returns>
    public static UINoteEvent On(int pitch, float velocity, int channel = 0) => new(UINoteKind.NoteOn, pitch, velocity, channel);

    /// <summary>A note-off event.</summary>
    /// <param name="pitch">MIDI note number.</param>
    /// <param name="channel">MIDI channel, 0-based.</param>
    /// <returns>The event.</returns>
    public static UINoteEvent Off(int pitch, int channel = 0) => new(UINoteKind.NoteOff, pitch, 0f, channel);

    /// <summary>The "all notes off" event.</summary>
    public static UINoteEvent AllOff => new(UINoteKind.AllNotesOff, 0, 0f);
}

/// <summary>
/// Bounded queue carrying <see cref="UINoteEvent"/>s from the UI thread to the audio thread. The audio side
/// (<see cref="TryDequeue"/>) never blocks nor allocates; producers are serialized by a lock that the audio thread
/// never takes.
/// </summary>
/// <remarks>
/// When the queue is full (host not calling <c>process</c>), note-ons are dropped and <see cref="TryEnqueue"/> returns
/// <c>false</c>, but note-offs and "all notes off" are never lost: they are kept in a per-channel bitmap, delivered
/// after the queued events, and note-ons are refused until that backlog is drained. No note can stay stuck.
/// </remarks>
public sealed class UINoteQueue
{
    private readonly UINoteEvent[] _items;
    private readonly int _mask;
    private readonly Lock _producerLock = new();
    private readonly ulong[] _pendingOff = new ulong[16 * 2]; // channel × 128 pitches
    private long _head; // next slot to read (audio thread)
    private long _tail; // next slot to write (UI thread)
    private int _overflow; // 1 while note-offs wait in the bitmap (note-ons refused)
    private int _pendingCount; // bits set in _pendingOff
    private int _pendingAllOff;

    /// <summary>Creates a queue.</summary>
    /// <param name="capacity">Capacity, rounded up to a power of two (at least 2).</param>
    public UINoteQueue(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        int size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)capacity);
        _items = new UINoteEvent[size];
        _mask = size - 1;
    }

    /// <summary>Capacity of the queue.</summary>
    public int Capacity => _items.Length;

    /// <summary>Number of events waiting (approximate while another thread works on the queue).</summary>
    public int Count => (int)(Volatile.Read(ref _tail) - Volatile.Read(ref _head));

    /// <summary>Adds an event (any thread except the audio thread).</summary>
    /// <param name="note">The event.</param>
    /// <returns><c>false</c> if the queue was full and the event was dropped.</returns>
    public bool TryEnqueue(in UINoteEvent note)
    {
        lock (_producerLock)
        {
            long tail = _tail;
            bool full = tail - Volatile.Read(ref _head) >= _items.Length;
            // Only the producer leaves overflow mode, once the consumer delivered every kept note-off. The consumer
            // clears a bit before decrementing the count, so a zero count means the bitmap is empty.
            if (Volatile.Read(ref _overflow) != 0 && Volatile.Read(ref _pendingCount) <= 0 && Volatile.Read(ref _pendingAllOff) == 0)
            {
                Volatile.Write(ref _overflow, 0);
            }
            if (!full && Volatile.Read(ref _overflow) == 0)
            {
                _items[tail & _mask] = note;
                Volatile.Write(ref _tail, tail + 1); // publishes the item
                return true;
            }
            if (note.Kind == UINoteKind.NoteOn) return false;
            Volatile.Write(ref _overflow, 1);
            if (note.Kind == UINoteKind.AllNotesOff)
            {
                Volatile.Write(ref _pendingAllOff, 1);
                return true;
            }
            int word = (note.Channel & 15) * 2 + (note.Pitch >> 6);
            ulong bit = 1UL << (note.Pitch & 63);
            if ((Interlocked.Or(ref _pendingOff[word], bit) & bit) == 0) Interlocked.Increment(ref _pendingCount);
            return true;
        }
    }

    /// <summary>Takes the oldest event (single consumer: the audio thread).</summary>
    /// <param name="note">The event.</param>
    /// <returns><c>false</c> if the queue is empty.</returns>
    public bool TryDequeue(out UINoteEvent note)
    {
        long head = _head;
        if (head < Volatile.Read(ref _tail))
        {
            note = _items[head & _mask];
            Volatile.Write(ref _head, head + 1); // frees the slot
            return true;
        }
        // Queue empty: deliver the note-offs kept during an overflow (they are newer than every queued event).
        if (Interlocked.Exchange(ref _pendingAllOff, 0) != 0)
        {
            note = UINoteEvent.AllOff;
            return true;
        }
        if (Volatile.Read(ref _pendingCount) > 0)
        {
            for (int word = 0; word < _pendingOff.Length; word++)
            {
                ulong bits = Volatile.Read(ref _pendingOff[word]);
                if (bits == 0) continue;
                int index = System.Numerics.BitOperations.TrailingZeroCount(bits);
                Interlocked.And(ref _pendingOff[word], ~(1UL << index));
                Interlocked.Decrement(ref _pendingCount);
                note = UINoteEvent.Off((word & 1) * 64 + index, word >> 1);
                return true;
            }
        }
        note = default;
        return false;
    }
}
