// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Audio;

/// <summary>
/// Reads Standard MIDI Files (types 0 and 1, ticks per quarter note) into short messages at sample positions, following
/// the tempo changes of the file.
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
