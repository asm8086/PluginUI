// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;

namespace PluginUI.Host.Catalog;

/// <summary>Reads the names a Windows DLL exports, from the file (without loading it).</summary>
public static class PeExports
{
    /// <summary>
    /// <c>true</c> when <paramref name="path"/> exports one of <paramref name="names"/>; also <c>true</c> when the file
    /// cannot be read as a DLL (the loader then decides), <c>false</c> only for a DLL whose export table lacks them.
    /// </summary>
    public static bool MayExport(string path, params string[] names)
    {
        try
        {
            var exported = Read(path);
            return exported is null || exported.Overlaps(names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>The exported names, or null when the file is not a PE image. Reads only the headers and the export table.</summary>
    public static HashSet<string>? Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.RandomAccess);
        long length = file.Length;
        byte[] buffer = new byte[256];

        Span<byte> At(long offset, int count)
        {
            if (offset < 0 || count > buffer.Length || offset + count > length) return [];
            file.Position = offset;
            var span = buffer.AsSpan(0, count);
            file.ReadExactly(span);
            return span;
        }

        int Int(long offset) => At(offset, 4) is { Length: 4 } b ? BinaryPrimitives.ReadInt32LittleEndian(b) : -1;
        int Short(long offset) => At(offset, 2) is { Length: 2 } b ? BinaryPrimitives.ReadUInt16LittleEndian(b) : -1;

        if (Short(0) != 0x5A4D) return null; // "MZ"
        int pe = Int(60);
        if (pe <= 0 || Int(pe) != 0x00004550) return null; // "PE\0\0"
        long coff = pe + 4;
        int sections = Short(coff + 2), optionalSize = Short(coff + 16);
        long optional = coff + 20;
        int magic = Short(optional);
        long directories = magic switch { 0x10B => optional + 96, 0x20B => optional + 112, _ => -1 };
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (sections < 0 || optionalSize < 0 || directories < 0 || directories + 8 > optional + optionalSize) return names;
        int exportRva = Int(directories);
        if (exportRva <= 0) return names;
        var table = new (int VirtualAddress, int Size, int Raw)[Math.Min(sections, 96)];
        long sectionTable = optional + optionalSize;
        for (int i = 0; i < table.Length; i++)
        {
            long h = sectionTable + i * 40L;
            table[i] = (Int(h + 12), Math.Max(Int(h + 8), Int(h + 16)), Int(h + 20));
        }

        long Offset(int rva)
        {
            foreach (var (va, size, raw) in table)
            {
                if (rva >= va && rva < (long)va + size) return (long)rva - va + raw;
            }
            return -1;
        }

        long export = Offset(exportRva);
        if (export < 0) return null;
        int count = Int(export + 24);
        long namesTable = Offset(Int(export + 32));
        if (count < 0 || count > 100_000 || namesTable < 0) return null;
        for (int i = 0; i < count; i++)
        {
            long name = Offset(Int(namesTable + i * 4L));
            if (name < 0) continue;
            var text = At(name, (int)Math.Min(64, length - name));
            int end = text.IndexOf((byte)0);
            if (end > 0) names.Add(Encoding.ASCII.GetString(text[..end]));
        }
        return names;
    }
}
