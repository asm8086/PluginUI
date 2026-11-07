// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Audio;

/// <summary>
/// Reads Standard MIDI Files (types 0 and 1, ticks per quarter note) into short messages at sample positions, following
/// the tempo changes of the file, and writes such messages back as a type 0 file.
/// </summary>
public static class MidiFile
{
    /// <summary>Messages of the file at <paramref name="sampleRate"/>, sorted by position (all tracks merged).</summary>
    /// <exception cref="FormatException">Not a supported MIDI file.</exception>
    public static List<(long Sample, MidiEvent Event)> Read(string path, double sampleRate) => Read(File.ReadAllBytes(path), sampleRate);

    /// <summary>Messages of a MIDI file image.</summary>
    /// <exception cref="FormatException">Not a supported MIDI file.</exception>
    public static List<(long Sample, MidiEvent Event)> Read(ReadOnlySpan<byte> data, double sampleRate)
    {
        if (data.Length < 14 || !data[..4].SequenceEqual("MThd"u8)) throw new FormatException("Not a MIDI file.");
        int headerLength = BigEndian32(data[4..]);
        int tracks = BigEndian16(data[10..]);
        int division = BigEndian16(data[12..]);
        if ((division & 0x8000) != 0) throw new FormatException("SMPTE time division is not supported.");
        int position = 8 + headerLength;

        var events = new List<(long Tick, int Order, MidiEvent? Event, int Tempo)>();
        int order = 0;
        for (int t = 0; t < tracks && position + 8 <= data.Length; t++)
        {
            if (!data.Slice(position, 4).SequenceEqual("MTrk"u8)) throw new FormatException("Track chunk expected.");
            int length = BigEndian32(data[(position + 4)..]);
            var track = data.Slice(position + 8, Math.Min(length, data.Length - position - 8));
            position += 8 + length;
            long tick = 0;
            int p = 0;
            byte status = 0;
            while (p < track.Length)
            {
                tick += ReadVarLength(track, ref p);
                if (p >= track.Length) break;
                byte b = track[p];
                if (b == 0xFF)
                {
                    byte type = track[p + 1];
                    p += 2;
                    int len = (int)ReadVarLength(track, ref p);
                    if (type == 0x51 && len == 3) events.Add((tick, order++, null, (track[p] << 16) | (track[p + 1] << 8) | track[p + 2]));
                    p += len;
                    if (type == 0x2F) break; // end of track
                    continue;
                }
                if (b is 0xF0 or 0xF7)
                {
                    p++;
                    p += (int)ReadVarLength(track, ref p); // system exclusive: skipped
                    continue;
                }
                if ((b & 0x80) != 0)
                {
                    status = b;
                    p++;
                }
                if (status == 0) throw new FormatException("Running status without a status byte.");
                int dataBytes = (status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
                byte d1 = track[p];
                byte d2 = dataBytes == 2 ? track[p + 1] : (byte)0;
                p += dataBytes;
                events.Add((tick, order++, new MidiEvent(0, status, d1, d2), 0));
            }
        }

        events.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));
        var result = new List<(long, MidiEvent)>(events.Count);
        double microsecondsPerQuarter = 500000; // 120 BPM until the first tempo event
        long lastTick = 0;
        double seconds = 0;
        foreach (var (tick, _, e, tempo) in events)
        {
            seconds += (tick - lastTick) * microsecondsPerQuarter / 1e6 / division;
            lastTick = tick;
            if (e is null)
            {
                microsecondsPerQuarter = tempo;
                continue;
            }
            result.Add(((long)Math.Round(seconds * sampleRate), e.Value));
        }
        return result;
    }

    /// <summary>Writes messages at sample positions as a Standard MIDI File (type 0, one tempo).</summary>
    /// <param name="path">File to write.</param>
    /// <param name="events">Messages and their sample positions (any order; equal positions keep their order).</param>
    /// <param name="sampleRate">Sample rate of the positions.</param>
    /// <param name="tempo">Tempo written in the file (quarter notes per minute).</param>
    /// <param name="ticksPerQuarter">Time resolution.</param>
    public static void Write(string path, IEnumerable<(long Sample, MidiEvent Event)> events, double sampleRate, double tempo = 120.0, int ticksPerQuarter = 960)
        => File.WriteAllBytes(path, Write(events, sampleRate, tempo, ticksPerQuarter));

    /// <summary>Image of a Standard MIDI File (type 0) holding <paramref name="events"/>.</summary>
    public static byte[] Write(IEnumerable<(long Sample, MidiEvent Event)> events, double sampleRate, double tempo = 120.0, int ticksPerQuarter = 960)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tempo);
        if (ticksPerQuarter is <= 0 or > 0x7FFF) throw new ArgumentOutOfRangeException(nameof(ticksPerQuarter));
        var track = new List<byte>();
        int microsecondsPerQuarter = (int)Math.Round(60_000_000.0 / tempo);
        track.AddRange([0x00, 0xFF, 0x51, 0x03, (byte)(microsecondsPerQuarter >> 16), (byte)(microsecondsPerQuarter >> 8), (byte)microsecondsPerQuarter]);
        double ticksPerSample = tempo / 60.0 * ticksPerQuarter / sampleRate;
        long lastTick = 0;
        foreach (var (sample, e) in events.Select((x, i) => (x, i)).OrderBy(x => x.x.Sample).ThenBy(x => x.i).Select(x => x.x))
        {
            if (e.Status < 0x80 || e.Status >= 0xF0) continue;
            long tick = Math.Max(lastTick, (long)Math.Round(Math.Max(0, sample) * ticksPerSample));
            WriteVarLength(track, tick - lastTick);
            lastTick = tick;
            track.Add(e.Status);
            track.Add((byte)(e.Data1 & 0x7F));
            if (e.Type is not (0xC0 or 0xD0)) track.Add((byte)(e.Data2 & 0x7F));
        }
        track.AddRange([0x00, 0xFF, 0x2F, 0x00]);

        var file = new List<byte>(track.Count + 22);
        file.AddRange("MThd"u8.ToArray());
        file.AddRange([0, 0, 0, 6, 0, 0, 0, 1, (byte)(ticksPerQuarter >> 8), (byte)ticksPerQuarter]);
        file.AddRange("MTrk"u8.ToArray());
        file.AddRange([(byte)(track.Count >> 24), (byte)(track.Count >> 16), (byte)(track.Count >> 8), (byte)track.Count]);
        file.AddRange(track);
        return [.. file];
    }

    private static void WriteVarLength(List<byte> target, long value)
    {
        Span<byte> bytes = stackalloc byte[5];
        int n = 0;
        do
        {
            bytes[n++] = (byte)(value & 0x7F);
            value >>= 7;
        }
        while (value > 0 && n < 5);
        for (int i = n - 1; i >= 0; i--) target.Add((byte)(bytes[i] | (i > 0 ? 0x80 : 0)));
    }

    private static int BigEndian32(ReadOnlySpan<byte> b) => (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];

    private static int BigEndian16(ReadOnlySpan<byte> b) => (b[0] << 8) | b[1];

    private static long ReadVarLength(ReadOnlySpan<byte> data, ref int p)
    {
        long value = 0;
        for (int i = 0; i < 4 && p < data.Length; i++)
        {
            byte b = data[p++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return value;
    }
}
