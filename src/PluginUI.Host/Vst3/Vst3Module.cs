// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst3;

/// <summary>Class exported by a VST 3 module (processor component, controller, test…).</summary>
/// <param name="ClassId">Class identifier (raw bytes, as the plug-in sees them).</param>
/// <param name="Category">"Audio Module Class" for plug-ins, "Component Controller Class" for controllers.</param>
/// <param name="Name">Plug-in name.</param>
/// <param name="Vendor">Vendor (the factory vendor when the class gives none).</param>
/// <param name="Version">Version text.</param>
/// <param name="SdkVersion">VST 3 SDK version the plug-in was built with.</param>
/// <param name="SubCategories">Sub-categories separated by '|' ("Fx|EQ", "Instrument|Synth").</param>
/// <param name="ClassFlags">Class flags (1 = distributable, 2 = simple mode supported).</param>
public sealed record Vst3ClassInfo(Guid ClassId, string Category, string Name, string Vendor, string Version, string SdkVersion, string SubCategories, uint ClassFlags)
{
    /// <summary>Category of audio processor components (the plug-ins themselves).</summary>
    public const string AudioEffectCategory = "Audio Module Class";

    /// <summary><c>true</c> for a plug-in (audio processor component).</summary>
    public bool IsAudioEffect => Category == AudioEffectCategory;

    /// <summary>Class identifier as written in .vstpreset files and moduleinfo.json (32 hexadecimal digits).</summary>
    public string ClassIdString => Vst3Identifiers.ToFuidString(ClassId);
}

/// <summary>Conversions of VST 3 identifiers.</summary>
public static class Vst3Identifiers
{
    /// <summary>
    /// The 32-digit form of a class identifier used by the SDK (<c>FUID::toString</c>): the four 32-bit words, whatever the
    /// platform's byte order.
    /// </summary>
    public static string ToFuidString(Guid classId)
    {
        Span<byte> b = stackalloc byte[16];
        MemoryMarshal.Write(b, in classId);
        if (!OperatingSystem.IsWindows()) return Convert.ToHexString(b);
        // COM layout: Data1, Data2, Data3 little-endian, then 8 bytes.
        uint d1 = BitConverter.ToUInt32(b[..4]);
        ushort d2 = BitConverter.ToUInt16(b[4..6]), d3 = BitConverter.ToUInt16(b[6..8]);
        return $"{d1:X8}{d2:X4}{d3:X4}{Convert.ToHexString(b[8..])}";
    }

    /// <summary>Class identifier from its 32-digit form (<see cref="ToFuidString"/>).</summary>
    /// <exception cref="FormatException">The text is not 32 hexadecimal digits.</exception>
    public static Guid FromFuidString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (text.Length != 32) throw new FormatException("A VST 3 class identifier has 32 hexadecimal digits.");
        uint W(int i) => uint.Parse(text.AsSpan(i * 8, 8), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        return Tuid.From(W(0), W(1), W(2), W(3));
    }
}

/// <summary>
/// A loaded VST 3 module: a <c>.vst3</c> file or bundle, or a factory living in this process (an NPlug plug-in compiled
/// into the application). Gives the factory information and the classes, and creates instances.
/// </summary>
public sealed unsafe class Vst3Module : IDisposable
{
    private nint _library;
    private void* _factory;
    private readonly HostApplication _host;
    private bool _exitCalled;

    private Vst3Module(string path, nint library, void* factory, string hostName)
    {
        Path = path;
        _library = library;
        _factory = factory;
        _host = new HostApplication(hostName);
        ReadFactory();
    }

    /// <summary>File or bundle the module comes from ("in-process" for a factory of this process).</summary>
    public string Path { get; }

    /// <summary>Factory vendor.</summary>
    public string Vendor { get; private set; } = string.Empty;

    /// <summary>Factory URL.</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>Factory e-mail.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Exported classes.</summary>
    public IReadOnlyList<Vst3ClassInfo> Classes { get; private set; } = [];

    /// <summary>Plug-ins of the module (audio processor components).</summary>
    public IEnumerable<Vst3ClassInfo> Plugins => Classes.Where(c => c.IsAudioEffect);

    internal void* Factory => _factory;

    internal HostApplication HostContext => _host;

    /// <summary>Host name given to the plug-ins (<c>IHostApplication::getName</c>).</summary>
    public string HostName => _host.Name;

    /// <summary>
    /// Wraps a factory of this process, for example <c>AudioPluginFactory.Export()</c> of an NPlug plug-in compiled into the
    /// application (no file, no DLL).
    /// </summary>
    /// <param name="factory">IPluginFactory pointer (one reference is taken over).</param>
    /// <param name="hostName">Name reported to the plug-in.</param>
    public static Vst3Module FromFactory(nint factory, string hostName = "PluginUI Host")
    {
        if (factory == 0) throw new ArgumentException("The factory pointer is null.", nameof(factory));
        return new Vst3Module("in-process", 0, (void*)factory, hostName);
    }

    /// <summary>
    /// Receives the steps of loading and creating plug-ins ("loading the library", "creating the component"…), to tell
    /// where a plug-in that crashes stopped (the out-of-process scanner reports the last step).
    /// </summary>
    public static Action<string>? Trace { get; set; }

    internal static void Step(string step) => Trace?.Invoke(step);

    /// <summary>Loads a <c>.vst3</c> file or bundle.</summary>
    /// <param name="path">Path of the .vst3 file (single DLL) or bundle folder.</param>
    /// <param name="hostName">Name reported to the plug-in.</param>
    /// <exception cref="Vst3Exception">The module cannot be loaded or has no factory.</exception>
    public static Vst3Module Load(string path, string hostName = "PluginUI Host")
    {
        string binary = ResolveBinary(path) ?? throw new Vst3Exception($"No binary for this platform in '{path}'.");
        nint library;
        try
        {
            Step("loading the library");
            library = NativeLibrary.Load(binary);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw new Vst3Exception($"Cannot load '{binary}': {ex.Message}", ex);
        }
        try
        {
            Step("module entry (InitDll / ModuleEntry)");
            if (OperatingSystem.IsWindows())
            {
                if (NativeLibrary.TryGetExport(library, "InitDll", out nint init) && ((delegate* unmanaged<byte>)init)() == 0)
                    throw new Vst3Exception("InitDll failed.");
            }
            else if (NativeLibrary.TryGetExport(library, "ModuleEntry", out nint entry) && ((delegate* unmanaged<nint, byte>)entry)(library) == 0)
            {
                throw new Vst3Exception("ModuleEntry failed.");
            }
            if (!NativeLibrary.TryGetExport(library, "GetPluginFactory", out nint getFactory)) throw new Vst3Exception("The module exports no GetPluginFactory.");
            Step("GetPluginFactory");
            void* factory = ((delegate* unmanaged<void*>)getFactory)();
            if (factory is null) throw new Vst3Exception("GetPluginFactory returned no factory.");
            return new Vst3Module(System.IO.Path.GetFullPath(path), library, factory, hostName);
        }
        catch
        {
            CallExit(library);
            NativeLibrary.Free(library);
            throw;
        }
    }

    /// <summary>Platform folder of a bundle ("x86_64-win", "arm64-win"…).</summary>
    public static string BundleArchitectureFolder
    {
        get
        {
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            };
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : "macos";
            return $"{arch}-{os}";
        }
    }

    /// <summary>Binary to load for a .vst3 path (a single DLL, or the platform binary inside a bundle), or null.</summary>
    public static string? ResolveBinary(string path)
    {
        if (File.Exists(path)) return path;
        if (!Directory.Exists(path)) return null;
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string contents = System.IO.Path.Combine(path, "Contents");
        var candidates = new List<string> { BundleArchitectureFolder };
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64) candidates.Add("arm64ec-win");
        string extension = OperatingSystem.IsWindows() ? ".vst3" : ".so";
        foreach (string folder in candidates)
        {
            string file = System.IO.Path.Combine(contents, folder, name + extension);
            if (File.Exists(file)) return file;
        }
        return null;
    }

    private void ReadFactory()
    {
        Step("reading the factory");
        PFactoryInfo info;
        if (((delegate* unmanaged[MemberFunction]<void*, PFactoryInfo*, int>)Com.Slot(_factory, 3))(_factory, &info) == Vst3Result.Ok)
        {
            Vendor = FixedText.Ascii(info.Vendor, 64);
            Url = FixedText.Ascii(info.Url, 256);
            Email = FixedText.Ascii(info.Email, 128);
        }
        void* factory2 = Com.As(_factory, Tuid.IPluginFactory2);
        void* factory3 = Com.As(_factory, Tuid.IPluginFactory3);
        try
        {
            if (factory3 is not null)
            {
                // IPluginFactory3::setHostContext lets the plug-in reach the host application while loading.
                ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(factory3, 9))(factory3, _host.Pointer);
            }
            int count = ((delegate* unmanaged[MemberFunction]<void*, int>)Com.Slot(_factory, 4))(_factory);
            var classes = new List<Vst3ClassInfo>(count);
            for (int i = 0; i < count; i++)
            {
                if (factory3 is not null)
                {
                    PClassInfoW w;
                    if (((delegate* unmanaged[MemberFunction]<void*, int, PClassInfoW*, int>)Com.Slot(factory3, 8))(factory3, i, &w) == Vst3Result.Ok)
                    {
                        classes.Add(new Vst3ClassInfo(w.Cid, FixedText.Ascii(w.Category, 32), FixedText.Utf16(w.Name, 64),
                            Or(FixedText.Utf16(w.Vendor, 64), Vendor), FixedText.Utf16(w.Version, 64), FixedText.Utf16(w.SdkVersion, 64),
                            FixedText.Ascii(w.SubCategories, 128), w.ClassFlags));
                        continue;
                    }
                }
                if (factory2 is not null)
                {
                    PClassInfo2 c2;
                    if (((delegate* unmanaged[MemberFunction]<void*, int, PClassInfo2*, int>)Com.Slot(factory2, 7))(factory2, i, &c2) == Vst3Result.Ok)
                    {
                        classes.Add(new Vst3ClassInfo(c2.Cid, FixedText.Ascii(c2.Category, 32), FixedText.Ascii(c2.Name, 64),
                            Or(FixedText.Ascii(c2.Vendor, 64), Vendor), FixedText.Ascii(c2.Version, 64), FixedText.Ascii(c2.SdkVersion, 64),
                            FixedText.Ascii(c2.SubCategories, 128), c2.ClassFlags));
                        continue;
                    }
                }
                PClassInfo c;
                if (((delegate* unmanaged[MemberFunction]<void*, int, PClassInfo*, int>)Com.Slot(_factory, 5))(_factory, i, &c) == Vst3Result.Ok)
                {
                    classes.Add(new Vst3ClassInfo(c.Cid, FixedText.Ascii(c.Category, 32), FixedText.Ascii(c.Name, 64), Vendor, string.Empty, string.Empty, string.Empty, 0));
                }
            }
            Classes = classes;
        }
        finally
        {
            Com.Release(factory2);
            Com.Release(factory3);
        }
    }

    private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    /// <summary>Creates an instance of a class as interface <paramref name="iid"/> (AddRef'd), or null.</summary>
    internal void* CreateInstance(in Guid classId, in Guid iid)
    {
        ObjectDisposedException.ThrowIf(_factory is null, this);
        void* obj = null;
        fixed (Guid* cid = &classId)
        fixed (Guid* id = &iid)
        {
            int hr = ((delegate* unmanaged[MemberFunction]<void*, Guid*, Guid*, void**, int>)Com.Slot(_factory, 6))(_factory, cid, id, &obj);
            return hr == Vst3Result.Ok ? obj : null;
        }
    }

    /// <summary>Creates a plug-in instance.</summary>
    /// <param name="classId">Class of the audio processor component.</param>
    /// <exception cref="Vst3Exception">The plug-in cannot be created or initialized.</exception>
    public Vst3Plugin CreatePlugin(Guid classId)
    {
        var info = Classes.FirstOrDefault(c => c.ClassId == classId) ?? throw new Vst3Exception("The module has no such class.");
        return new Vst3Plugin(this, info);
    }

    /// <summary>Creates the first plug-in of the module (or the one named <paramref name="name"/>).</summary>
    public Vst3Plugin CreatePlugin(string? name = null)
    {
        var info = Plugins.FirstOrDefault(c => name is null || string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new Vst3Exception(name is null ? "The module contains no plug-in." : $"The module contains no plug-in named '{name}'.");
        return new Vst3Plugin(this, info);
    }

    private static void CallExit(nint library)
    {
        if (OperatingSystem.IsWindows())
        {
            if (NativeLibrary.TryGetExport(library, "ExitDll", out nint exit)) ((delegate* unmanaged<byte>)exit)();
        }
        else if (NativeLibrary.TryGetExport(library, "ModuleExit", out nint exit))
        {
            ((delegate* unmanaged<byte>)exit)();
        }
    }

    /// <summary>Releases the factory and unloads the module. Instances must be disposed first.</summary>
    public void Dispose()
    {
        if (_factory is null) return;
        Com.Release(_factory);
        _factory = null;
        _host.Release();
        if (_library != 0 && !_exitCalled)
        {
            Step("unloading the module (ExitDll / ModuleExit)");
            _exitCalled = true;
            CallExit(_library);
            NativeLibrary.Free(_library);
            _library = 0;
        }
    }
}

/// <summary>A VST 3 module or plug-in failed.</summary>
public sealed class Vst3Exception : Exception
{
    /// <summary>Creates the exception.</summary>
    public Vst3Exception(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    public Vst3Exception(string message, Exception inner) : base(message, inner)
    {
    }
}
