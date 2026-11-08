// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Vst2;

/// <summary>A plug-in inside a shell module (several plug-ins in one file, as Waves' WaveShell).</summary>
/// <param name="Id">Unique identifier, given back to the module when the plug-in is created.</param>
/// <param name="Name">Name.</param>
public sealed record Vst2ShellPlugin(int Id, string Name);

/// <summary>A VST 2 module failed, or a file is not one.</summary>
public sealed class Vst2Exception : PluginException
{
    /// <summary>Creates the exception.</summary>
    public Vst2Exception(string message, bool isNotAPlugin = false) : base(message) => IsNotAPlugin = isNotAPlugin;

    /// <summary><c>true</c> when the file is a library without a VST 2 entry point (a dependency of plug-ins).</summary>
    public bool IsNotAPlugin { get; }
}

/// <summary>
/// A VST 2 module (a .dll on Windows, a .so on Linux): its entry point creates one plug-in per call. The library stays
/// loaded while the module or one of its plug-ins is alive.
/// </summary>
public sealed unsafe class Vst2Module : IDisposable
{
    private nint _library;
    private int _references = 1;
    private byte* _directory;
    private readonly Lock _lock = new();

    private Vst2Module(string path, nint library, nint entry)
    {
        Path = path;
        _library = library;
        Entry = (delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint>, AEffect*>)entry;
        string directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        byte[] bytes = Encoding.Default.GetBytes(directory + "\0");
        _directory = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        bytes.CopyTo(new Span<byte>(_directory, bytes.Length));
    }

    /// <summary>Path of the module.</summary>
    public string Path { get; }

    internal delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint>, AEffect*> Entry { get; }

    internal byte* Directory => _directory;

    /// <summary>Loads a module.</summary>
    /// <exception cref="Vst2Exception">The file is not a 64-bit VST 2 plug-in, or cannot be loaded.</exception>
    public static Vst2Module Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The VST 2 module does not exist.", path);
        CheckArchitecture(path);
        Step($"loading '{System.IO.Path.GetFileName(path)}'");
        nint library;
        try
        {
            library = NativeLibrary.Load(path);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new Vst2Exception($"'{System.IO.Path.GetFileName(path)}' cannot be loaded: {ex.Message}");
        }
        if (!NativeLibrary.TryGetExport(library, "VSTPluginMain", out nint entry) && !NativeLibrary.TryGetExport(library, "main", out entry))
        {
            NativeLibrary.Free(library);
            throw new Vst2Exception($"'{System.IO.Path.GetFileName(path)}' is not a VST 2 plug-in (no VSTPluginMain entry point).", isNotAPlugin: true);
        }
        return new Vst2Module(path, library, entry);
    }

    /// <summary>
    /// Wraps an entry point of this process (tests, or a plug-in linked into the application): the function a VST 2
    /// module exports as VSTPluginMain.
    /// </summary>
    /// <param name="entryPoint">Pointer to <c>AEffect* VSTPluginMain(audioMasterCallback)</c>.</param>
    /// <param name="path">Path reported to the plug-in (its directory) and in the catalog.</param>
    public static Vst2Module FromEntryPoint(nint entryPoint, string path)
    {
        if (entryPoint == 0) throw new ArgumentNullException(nameof(entryPoint));
        return new Vst2Module(path, 0, entryPoint);
    }

    /// <summary>Creates a plug-in.</summary>
    /// <param name="shellId">For a shell module, the identifier of the plug-in (<see cref="ShellPlugins"/>); 0 otherwise.</param>
    /// <param name="hostName">Name reported to the plug-in.</param>
    public Vst2Plugin CreatePlugin(int shellId = 0, string hostName = "PluginUI Host")
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_references <= 0, this);
            _references++;
        }
        // The plug-in releases its reference when it is disposed, or when its constructor fails.
        return new Vst2Plugin(this, shellId, hostName);
    }

    /// <summary>
    /// The plug-ins of a shell module (empty for an ordinary module). Creates a plug-in to ask it, then closes it.
    /// </summary>
    public IReadOnlyList<Vst2ShellPlugin> ShellPlugins
    {
        get
        {
            using var shell = CreatePlugin();
            return shell.Category == Vst2Category.Shell ? shell.ReadShellPlugins() : [];
        }
    }

    internal void Release()
    {
        lock (_lock)
        {
            if (--_references > 0) return;
        }
        Step($"unloading '{System.IO.Path.GetFileName(Path)}'");
        if (_library != 0) NativeLibrary.Free(_library);
        _library = 0;
        NativeMemory.Free(_directory);
        _directory = null;
    }

    /// <summary>Releases the module; the library is unloaded once its plug-ins are disposed too.</summary>
    public void Dispose()
    {
        bool owned;
        lock (_lock) owned = !_disposed && (_disposed = true);
        if (owned) Release();
    }

    private bool _disposed;

    internal static void Step(string text) => Vst3Module.Trace?.Invoke(text);

    /// <summary>Refuses a module of another processor (a 32-bit DLL in a 64-bit host) with a clear message.</summary>
    private static void CheckArchitecture(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> header = stackalloc byte[64];
            if (file.Read(header) < 64 || header[0] != 'M' || header[1] != 'Z') return;
            int peOffset = BitConverter.ToInt32(header[60..]);
            Span<byte> pe = stackalloc byte[6];
            file.Position = peOffset;
            if (file.Read(pe) < 6 || pe[0] != 'P' || pe[1] != 'E') return;
            ushort machine = BitConverter.ToUInt16(pe[4..]);
            string? mine = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "ARM64",
                Architecture.X86 => "x86",
                _ => null,
            };
            string? theirs = machine switch
            {
                0x014C => "x86",
                0x8664 => "x64",
                0xAA64 => "ARM64",
                _ => null,
            };
            if (mine is null || theirs is null || mine == theirs) return;
            // An x64 module also loads in an ARM64EC process; plain ARM64 hosts cannot load it.
            throw new Vst2Exception(theirs == "x86"
                ? $"'{System.IO.Path.GetFileName(path)}' is a 32-bit plug-in: a 64-bit host cannot load it."
                : $"'{System.IO.Path.GetFileName(path)}' is built for {theirs}, this host runs on {mine}.");
        }
        catch (IOException)
        {
            // Unreadable: let the loader report it.
        }
    }
}
