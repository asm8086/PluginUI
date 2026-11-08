// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Host.Catalog;

namespace PluginUI.Host.Vst2;

/// <summary>
/// Where the bridges are and when to use them: a VST 2 plug-in built for a processor this process cannot load (a 32-bit
/// plug-in in a 64-bit host) is scanned and run in a bridge executable of its architecture, when one is installed.
/// </summary>
/// <remarks>
/// The 32-bit bridge is <c>PluginHost.Bridge32.exe</c>, looked for in a <c>bridge32</c> folder next to the host's
/// executable, then next to it. <see cref="Vst2Hosting.Register"/> enables it for scans and loads.
/// </remarks>
public static class Vst2Bridge
{
    /// <summary>File name of the 32-bit bridge.</summary>
    public const string Bridge32FileName = "PluginHost.Bridge32.exe";

    private static string? s_bridge32;
    private static bool s_bridge32Set;

    /// <summary>
    /// The 32-bit bridge executable, or null when there is none (or when this process is 32-bit itself). Found next to the
    /// host by default; can be set (null disables bridging).
    /// </summary>
    public static string? Bridge32Executable
    {
        get
        {
            if (s_bridge32Set) return s_bridge32;
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) return null;
            string folder = AppContext.BaseDirectory;
            foreach (string candidate in new[] { Path.Combine(folder, "bridge32", Bridge32FileName), Path.Combine(folder, Bridge32FileName) })
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
        set
        {
            s_bridge32 = value;
            s_bridge32Set = true;
        }
    }

    /// <summary>The bridge that runs modules of <paramref name="architecture"/>, or null when there is none.</summary>
    public static string? ExecutableFor(ModuleArchitecture architecture)
        => architecture == ModuleArchitecture.X86 && ModuleArchitectures.Current is ModuleArchitecture.X64 or ModuleArchitecture.Arm64 ? Bridge32Executable : null;

    /// <summary>
    /// Opens a VST 2 plug-in in the bridge of its architecture.
    /// </summary>
    /// <exception cref="Vst2BridgeException">No bridge for that architecture, or the bridge could not load the plug-in.</exception>
    public static Vst2BridgedPlugin Load(string path, string? plugin, string hostName, ModuleArchitecture architecture)
    {
        string bridge = ExecutableFor(architecture)
            ?? throw new Vst2BridgeException($"'{Path.GetFileName(path)}' is a {ModuleArchitectures.DisplayName(architecture)} plug-in and no bridge for it is installed.");
        return Vst2BridgedPlugin.Start(bridge, path, plugin, hostName, architecture);
    }

    /// <summary>Makes scans describe the VST 2 modules of other architectures in their bridges (when installed).</summary>
    public static void RegisterScanners()
    {
        if (ExecutableFor(ModuleArchitecture.X86) is { } bridge) PluginScanner.BridgeScanners[ModuleArchitecture.X86] = new OutOfProcessModuleScanner(bridge);
    }
}
