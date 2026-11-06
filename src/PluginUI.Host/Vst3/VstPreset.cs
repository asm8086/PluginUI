// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;

namespace PluginUI.Host.Vst3;

/// <summary>
/// The .vstpreset file format of the VST 3 SDK: a 48-byte header ("VST3", version, class identifier, offset of the chunk
/// list), the chunks, then the list ("List", count, then id / offset / size of each chunk). Little-endian.
/// </summary>
public static class VstPreset
{
    private const int HeaderSize = 48;
    private const int FormatVersion = 1;

    /// <summary>Content of a preset.</summary>
    /// <param name="ClassId">Class of the plug-in.</param>
    /// <param name="ComponentState">'Comp' chunk: processor state.</param>
    /// <param name="ControllerState">'Cont' chunk: controller state (may be empty).</param>
    /// <param name="MetaInfo">'Info' chunk: XML meta information (may be empty).</param>
    public sealed record Content(Guid ClassId, byte[] ComponentState, byte[] ControllerState, string MetaInfo);

    /// <summary>Writes a preset image.</summary>
    public static byte[] Write(Guid classId, ReadOnlySpan<byte> component, ReadOnlySpan<byte> controller, string? metaInfo = null)
    {
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderSize];
        "VST3"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        Encoding.ASCII.GetBytes(Vst3Identifiers.ToFuidString(classId), header[8..40]);
        stream.Write(header);

        var chunks = new List<(string Id, long Offset, long Size)>();
        void Chunk(string id, ReadOnlySpan<byte> data)
        {
            chunks.Add((id, stream.Position, data.Length));
            stream.Write(data);
        }
        Chunk("Comp", component);
        if (!controller.IsEmpty) Chunk("Cont", controller);
        if (!string.IsNullOrEmpty(metaInfo)) Chunk("Info", Encoding.UTF8.GetBytes(metaInfo));

        long listOffset = stream.Position;
        Span<byte> entry = stackalloc byte[20];
        stream.Write("List"u8);
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, chunks.Count);
        stream.Write(count);
        foreach (var (id, offset, size) in chunks)
        {
            Encoding.ASCII.GetBytes(id, entry[..4]);
            BinaryPrimitives.WriteInt64LittleEndian(entry[4..], offset);
            BinaryPrimitives.WriteInt64LittleEndian(entry[12..], size);
            stream.Write(entry);
        }
        byte[] result = stream.ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(40), listOffset);
        return result;
    }

    /// <summary>Reads a preset image.</summary>
    /// <exception cref="FormatException">Not a .vstpreset.</exception>
    public static Content Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..4].SequenceEqual("VST3"u8)) throw new FormatException("Not a VST 3 preset.");
        var classId = Vst3Identifiers.FromFuidString(Encoding.ASCII.GetString(data[8..40]));
        long listOffset = BinaryPrimitives.ReadInt64LittleEndian(data[40..48]);
        if (listOffset < HeaderSize || listOffset + 8 > data.Length || !data.Slice((int)listOffset, 4).SequenceEqual("List"u8))
            throw new FormatException("The preset has no chunk list.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data[((int)listOffset + 4)..]);
        byte[] component = [], controller = [];
        string info = string.Empty;
        int position = (int)listOffset + 8;
        for (int i = 0; i < count && position + 20 <= data.Length; i++, position += 20)
        {
            string id = Encoding.ASCII.GetString(data.Slice(position, 4));
            long offset = BinaryPrimitives.ReadInt64LittleEndian(data[(position + 4)..]);
            long size = BinaryPrimitives.ReadInt64LittleEndian(data[(position + 12)..]);
            if (offset < 0 || size < 0 || offset + size > data.Length) throw new FormatException($"Chunk '{id}' lies outside the file.");
            var chunk = data.Slice((int)offset, (int)size);
            switch (id)
            {
                case "Comp": component = chunk.ToArray(); break;
                case "Cont": controller = chunk.ToArray(); break;
                case "Info": info = Encoding.UTF8.GetString(chunk); break;
            }
        }
        return new Content(classId, component, controller, info);
    }
}
