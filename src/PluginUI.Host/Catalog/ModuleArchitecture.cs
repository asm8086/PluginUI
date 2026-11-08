// 2026/11/08 15:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PluginUI.Host.Catalog;

/// <summary>The processor a plug-in module is built for.</summary>
public enum ModuleArchitecture
{
    /// <summary>Not known (not a Windows module, unreadable file, or scanned before the architecture was recorded).</summary>
    Unknown,
    /// <summary>32-bit Intel / AMD (x86).</summary>
    X86,
    /// <summary>64-bit Intel / AMD (x64), ARM64EC included.</summary>
    X64,
    /// <summary>64-bit ARM (ARM64, ARM64X included).</summary>
    Arm64,
    /// <summary>32-bit ARM.</summary>
    Arm,
}

/// <summary>
/// Reads which processor a module is built for, from its file (without loading it), and tells whether this process can
/// load it: a 64-bit process loads no 32-bit module, an x64 process no ARM64 module, an ARM64 process no x64 module.
/// </summary>
public static class ModuleArchitectures
{
    /// <summary>The architecture of this process.</summary>
    public static ModuleArchitecture Current => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => ModuleArchitecture.X86,
        Architecture.X64 => ModuleArchitecture.X64,
        Architecture.Arm64 => ModuleArchitecture.Arm64,
        Architecture.Arm => ModuleArchitecture.Arm,
        _ => ModuleArchitecture.Unknown,
    };

    /// <summary><c>true</c> when this process can load a module built for <paramref name="architecture"/> (or when it is not known).</summary>
    public static bool CanLoad(ModuleArchitecture architecture)
        => architecture == ModuleArchitecture.Unknown || Current == ModuleArchitecture.Unknown || architecture == Current;

    /// <summary>
    /// The architecture of a plug-in module: the machine of a VST 2 library or of a single-file .vst3, or, for a VST 3
    /// bundle, the binary this process would load (else the architecture folders the bundle has: <c>x86-win</c>…).
    /// </summary>
    public static ModuleArchitecture Of(string path, PluginFormat format)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (format == PluginFormat.Vst3 && Directory.Exists(path))
        {
            // A bundle names its binaries by folder; the loader takes the one of this process when there is one.
            if (Vst3.Vst3Module.ResolveBinary(path) is not null) return Current;
            string contents = Path.Combine(path, "Contents");
            foreach (var (folder, architecture) in BundleFolders)
            {
                if (Directory.Exists(Path.Combine(contents, folder))) return architecture;
            }
            return ModuleArchitecture.Unknown;
        }
        return ReadMachine(path);
    }

    private static readonly (string Folder, ModuleArchitecture Architecture)[] BundleFolders =
    [
        ("x86_64-win", ModuleArchitecture.X64), ("arm64ec-win", ModuleArchitecture.X64), ("arm64x-win", ModuleArchitecture.Arm64),
        ("arm64-win", ModuleArchitecture.Arm64), ("x86-win", ModuleArchitecture.X86), ("x86_64-linux", ModuleArchitecture.X64),
        ("aarch64-linux", ModuleArchitecture.Arm64), ("i386-linux", ModuleArchitecture.X86),
    ];

    /// <summary>The machine field of a Windows module (PE file), <see cref="ModuleArchitecture.Unknown"/> for another file.</summary>
    public static ModuleArchitecture ReadMachine(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 256);
            Span<byte> header = stackalloc byte[64];
            if (stream.Read(header) < 64 || BinaryPrimitives.ReadUInt16LittleEndian(header) != 0x5A4D) return ModuleArchitecture.Unknown; // "MZ"
            int pe = BinaryPrimitives.ReadInt32LittleEndian(header[60..]);
            if (pe <= 0 || pe > stream.Length - 6) return ModuleArchitecture.Unknown;
            stream.Position = pe;
            Span<byte> signature = stackalloc byte[6];
            if (stream.Read(signature) < 6 || BinaryPrimitives.ReadUInt32LittleEndian(signature) != 0x00004550) return ModuleArchitecture.Unknown; // "PE\0\0"
            return BinaryPrimitives.ReadUInt16LittleEndian(signature[4..]) switch
            {
                0x014C => ModuleArchitecture.X86,
                0x8664 => ModuleArchitecture.X64,
                0xAA64 => ModuleArchitecture.Arm64,
                0x01C0 or 0x01C4 => ModuleArchitecture.Arm,
                _ => ModuleArchitecture.Unknown,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ModuleArchitecture.Unknown;
        }
    }

    /// <summary>"x86 (32-bit)", "x64 (64-bit)", "ARM64", "ARM (32-bit)" or "unknown".</summary>
    public static string DisplayName(ModuleArchitecture architecture) => architecture switch
    {
        ModuleArchitecture.X86 => "x86 (32-bit)",
        ModuleArchitecture.X64 => "x64 (64-bit)",
        ModuleArchitecture.Arm64 => "ARM64",
        ModuleArchitecture.Arm => "ARM (32-bit)",
        _ => "unknown",
    };

    /// <summary>Why this process cannot load a module built for <paramref name="architecture"/>.</summary>
    public static string IncompatibilityMessage(ModuleArchitecture architecture)
        => architecture is ModuleArchitecture.X86 or ModuleArchitecture.Arm && Current is ModuleArchitecture.X64 or ModuleArchitecture.Arm64
            ? $"32-bit plug-in ({DisplayName(architecture)}): this 64-bit host cannot load it."
            : $"Plug-in built for {DisplayName(architecture)}: this {DisplayName(Current)} host cannot load it.";
}
