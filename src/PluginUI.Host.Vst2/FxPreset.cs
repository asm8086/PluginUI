// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;

namespace PluginUI.Host.Vst2;

/// <summary>Kind of a VST 2 preset file.</summary>
public enum FxPresetKind
{
    /// <summary>One program as parameter values (.fxp, 'FxCk').</summary>
    ProgramParameters,
    /// <summary>One program as an opaque chunk (.fxp, 'FPCh').</summary>
    ProgramChunk,
    /// <summary>All the programs as parameter values (.fxb, 'FxBk').</summary>
    BankParameters,
    /// <summary>All the programs as an opaque chunk (.fxb, 'FBCh').</summary>
    BankChunk,
}

/// <summary>One program of a preset: its name and its parameter values (0..1).</summary>
/// <param name="Name">Program name (at most 28 bytes).</param>
/// <param name="Parameters">Normalized parameter values.</param>
public sealed record FxProgram(string Name, float[] Parameters);

/// <summary>
/// A VST 2 preset (.fxp) or bank (.fxb). All numbers are big-endian; every file starts with 'CcnK' and its size.
/// </summary>
public sealed class FxPreset
{
    private const int CcnK = 0x43636E4B, FxCk = 0x4678436B, FPCh = 0x46504368, FxBk = 0x4678426B, FBCh = 0x46424368;
    private const int NameLength = 28;

    /// <summary>Kind of the file.</summary>
    public FxPresetKind Kind { get; init; }

    /// <summary>Unique identifier of the plug-in the preset belongs to.</summary>
    public int PluginId { get; init; }

    /// <summary>Version of the plug-in that wrote it.</summary>
    public int PluginVersion { get; init; }

    /// <summary>Programs (one for an .fxp; empty for a chunk bank).</summary>
    public IReadOnlyList<FxProgram> Programs { get; init; } = [];

    /// <summary>Program name of a program chunk (.fxp 'FPCh').</summary>
    public string ProgramName { get; init; } = string.Empty;

    /// <summary>Number of programs of a chunk bank.</summary>
    public int ProgramCount { get; init; }

    /// <summary>Program selected when the bank was saved (version 2 banks; 0 otherwise).</summary>
    public int CurrentProgram { get; init; }

    /// <summary>The plug-in's opaque state, for the chunk kinds.</summary>
    public byte[] Chunk { get; init; } = [];

    /// <summary><c>true</c> for a bank (.fxb).</summary>
    public bool IsBank => Kind is FxPresetKind.BankParameters or FxPresetKind.BankChunk;

    /// <summary>Reads an .fxp or .fxb image.</summary>
    /// <exception cref="InvalidDataException">The data is not a VST 2 preset or is truncated.</exception>
    public static FxPreset Read(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        var preset = ReadOne(ref reader, out _);
        return preset;
    }

    private static FxPreset ReadOne(ref Reader r, out int end)
    {
        if (r.Int() != CcnK) throw new InvalidDataException("This is not a VST 2 preset (no 'CcnK' header).");
        int size = r.Int();
        end = r.Position + size;
        int magic = r.Int();
        r.Int(); // format version
        int id = r.Int(), version = r.Int(), count = r.Int();
        switch (magic)
        {
            case FxCk:
            {
                string name = r.Name(NameLength);
                var values = new float[count];
                for (int i = 0; i < count; i++) values[i] = r.Float();
                return new FxPreset { Kind = FxPresetKind.ProgramParameters, PluginId = id, PluginVersion = version, Programs = [new FxProgram(name, values)] };
            }
            case FPCh:
            {
                string name = r.Name(NameLength);
                return new FxPreset { Kind = FxPresetKind.ProgramChunk, PluginId = id, PluginVersion = version, ProgramName = name, Chunk = r.Bytes(r.Int()) };
            }
            case FxBk:
            case FBCh:
            {
                int current = r.Int(); // version 2: current program; version 1: the first bytes of 'future' (zero)
                r.Skip(124);
                if (magic == FBCh)
                    return new FxPreset { Kind = FxPresetKind.BankChunk, PluginId = id, PluginVersion = version, ProgramCount = count, CurrentProgram = current, Chunk = r.Bytes(r.Int()) };
                var programs = new List<FxProgram>(count);
                for (int i = 0; i < count; i++)
                {
                    var program = ReadOne(ref r, out int programEnd);
                    if (program.Kind != FxPresetKind.ProgramParameters) throw new InvalidDataException("A program of the bank is not a parameter program.");
                    programs.Add(program.Programs[0]);
                    r.Seek(programEnd);
                }
                return new FxPreset { Kind = FxPresetKind.BankParameters, PluginId = id, PluginVersion = version, ProgramCount = count, CurrentProgram = current, Programs = programs };
            }
            default:
                throw new InvalidDataException($"Unknown VST 2 preset kind '{FourCc(magic)}'.");
        }
    }

    /// <summary>Writes the preset as an .fxp or .fxb image.</summary>
    public byte[] Write()
    {
        var w = new Writer();
        switch (Kind)
        {
            case FxPresetKind.ProgramParameters:
                WriteProgram(w, Programs.Count > 0 ? Programs[0] : new FxProgram(string.Empty, []));
                break;
            case FxPresetKind.ProgramChunk:
                w.Header(FPCh, PluginId, PluginVersion, 1);
                w.Name(ProgramName, NameLength);
                w.Int(Chunk.Length);
                w.Bytes(Chunk);
                break;
            case FxPresetKind.BankParameters:
                w.Header(FxBk, PluginId, PluginVersion, Programs.Count, formatVersion: 2);
                w.Int(CurrentProgram);
                w.Zeros(124);
                foreach (var program in Programs) WriteProgram(w, program);
                break;
            case FxPresetKind.BankChunk:
                w.Header(FBCh, PluginId, PluginVersion, ProgramCount, formatVersion: 2);
                w.Int(CurrentProgram);
                w.Zeros(124);
                w.Int(Chunk.Length);
                w.Bytes(Chunk);
                break;
        }
        return w.ToArray();
    }

    private void WriteProgram(Writer w, FxProgram program)
    {
        int start = w.Header(FxCk, PluginId, PluginVersion, program.Parameters.Length);
        w.Name(program.Name, NameLength);
        foreach (float v in program.Parameters) w.Float(v);
        w.EndBlock(start);
    }

    /// <summary>Four-character code of a unique identifier ("Ab12"), or its number when it is not printable.</summary>
    public static string FourCc(int id)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, id);
        foreach (byte c in b) if (c < 32 || c > 126) return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Encoding.ASCII.GetString(b);
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; }

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || Position + n > _data.Length) throw new InvalidDataException("The VST 2 preset is truncated.");
            var s = _data.Slice(Position, n);
            Position += n;
            return s;
        }

        public int Int() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

        public float Float() => BinaryPrimitives.ReadSingleBigEndian(Take(4));

        public byte[] Bytes(int n) => Take(n).ToArray();

        public void Skip(int n) => Take(n);

        public void Seek(int position)
        {
            if (position < Position || position > _data.Length) throw new InvalidDataException("The VST 2 preset is truncated.");
            Position = position;
        }

        public string Name(int n)
        {
            var s = Take(n);
            int end = s.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? s : s[..end]).Trim();
        }
    }

    private sealed class Writer
    {
        private readonly MemoryStream _stream = new();

        public int Header(int magic, int id, int version, int count, int formatVersion = 1)
        {
            Int(CcnK);
            int start = (int)_stream.Position;
            Int(0); // size, patched by EndBlock
            Int(magic);
            Int(formatVersion);
            Int(id);
            Int(version);
            Int(count);
            return start;
        }

        public void EndBlock(int sizePosition)
        {
            long end = _stream.Position;
            _stream.Position = sizePosition;
            Int((int)(end - sizePosition - 4));
            _stream.Position = end;
        }

        public void Int(int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, v);
            _stream.Write(b);
        }

        public void Float(float v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteSingleBigEndian(b, v);
            _stream.Write(b);
        }

        public void Bytes(byte[] data) => _stream.Write(data);

        public void Zeros(int n)
        {
            for (int i = 0; i < n; i++) _stream.WriteByte(0);
        }

        public void Name(string name, int n)
        {
            Span<byte> b = stackalloc byte[n];
            b.Clear();
            byte[] text = Encoding.Latin1.GetBytes(name);
            text.AsSpan(0, Math.Min(text.Length, n - 1)).CopyTo(b);
            _stream.Write(b);
        }

        public byte[] ToArray()
        {
            // The outer block runs from its size field to the end.
            if (_stream.Length >= 8)
            {
                _stream.Position = 4;
                Int((int)(_stream.Length - 8));
            }
            return _stream.ToArray();
        }
    }
}
