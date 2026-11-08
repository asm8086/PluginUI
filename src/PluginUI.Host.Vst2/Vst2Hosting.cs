// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Host.Catalog;

namespace PluginUI.Host.Vst2;

/// <summary>
/// Plugs VST 2 hosting into PluginUI.Host: once <see cref="Register"/> is called, scans list VST 2 plug-ins and
/// <see cref="PluginLoader"/> opens them.
/// </summary>
public static class Vst2Hosting
{
    /// <summary>
    /// Makes the scanner and the loader handle VST 2 modules. Call it at start-up, in the host and in the process that
    /// scans modules (the same executable, for <see cref="ModuleScanProcess"/>).
    /// </summary>
    public static void Register()
    {
        InProcessModuleScanner.Vst2Scanner = Describe;
        PluginLoader.Vst2Loader = LoadAnyArchitecture;
        Vst2Bridge.RegisterScanners();
    }

    /// <summary>
    /// Opens a VST 2 plug-in in this process, or in a bridge when it is built for a processor this process cannot load (a
    /// 32-bit plug-in in a 64-bit host, see <see cref="Vst2Bridge"/>).
    /// </summary>
    public static PluginInstance LoadAnyArchitecture(string path, string? plugin = null, string hostName = "PluginUI Host")
    {
        ArgumentNullException.ThrowIfNull(path);
        var architecture = ModuleArchitectures.ReadMachine(path);
        if (!ModuleArchitectures.CanLoad(architecture) && Vst2Bridge.ExecutableFor(architecture) is not null)
            return Vst2Bridge.Load(path, string.IsNullOrEmpty(plugin) ? null : plugin, hostName, architecture);
        return Load(path, plugin, hostName);
    }

    /// <summary>Opens a VST 2 plug-in.</summary>
    /// <param name="path">Module path.</param>
    /// <param name="plugin">For a shell module, the plug-in's identifier or name; null for an ordinary module.</param>
    /// <param name="hostName">Name reported to the plug-in.</param>
    /// <exception cref="Vst2Exception">The module cannot be loaded, or the shell has no such plug-in.</exception>
    public static Vst2Plugin Load(string path, string? plugin = null, string hostName = "PluginUI Host")
    {
        using var module = Vst2Module.Load(path);
        int shellId = 0;
        if (!string.IsNullOrEmpty(plugin))
        {
            var shell = module.ShellPlugins;
            if (shell.Count > 0)
            {
                var match = int.TryParse(plugin, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                    ? shell.FirstOrDefault(p => p.Id == id)
                    : shell.FirstOrDefault(p => string.Equals(p.Name, plugin, StringComparison.OrdinalIgnoreCase));
                shellId = match?.Id ?? throw new Vst2Exception($"'{Path.GetFileName(path)}' holds no plug-in '{plugin}'.");
            }
        }
        // The plug-in keeps the module loaded.
        return module.CreatePlugin(shellId, hostName);
    }

    /// <summary>Describes the plug-ins of a VST 2 module: one, or each plug-in of a shell. Empty for a library that is not a plug-in.</summary>
    public static IReadOnlyList<PluginDescription> Describe(string path)
    {
        Vst2Module module;
        try
        {
            module = Vst2Module.Load(path);
        }
        catch (Vst2Exception ex) when (ex.IsNotAPlugin)
        {
            return [];
        }
        using (module) return Describe(module);
    }

    /// <summary>Describes the plug-ins of a loaded module.</summary>
    public static IReadOnlyList<PluginDescription> Describe(Vst2Module module)
    {
        ArgumentNullException.ThrowIfNull(module);
        IReadOnlyList<Vst2ShellPlugin> shell;
        var list = new List<PluginDescription>();
        using (var first = module.CreatePlugin())
        {
            if (first.Category != Vst2Category.Shell)
            {
                list.Add(Describe(first, module.Path));
                return list;
            }
            shell = first.ReadShellPlugins();
        }
        foreach (var entry in shell)
        {
            try
            {
                using var plugin = module.CreatePlugin(entry.Id);
                var d = Describe(plugin, module.Path);
                d.ClassId = entry.Id.ToString(CultureInfo.InvariantCulture);
                if (entry.Name.Length > 0) d.Name = entry.Name;
                list.Add(d);
            }
            catch (Exception ex) when (ex is PluginException or InvalidOperationException)
            {
                list.Add(new PluginDescription
                {
                    Format = PluginFormat.Vst2, Path = module.Path, ClassId = entry.Id.ToString(CultureInfo.InvariantCulture), Name = entry.Name,
                    Status = PluginStatus.Failed, Error = ex.Message,
                });
            }
        }
        return list;
    }

    /// <summary>Describes one plug-in instance.</summary>
    public static PluginDescription Describe(Vst2Plugin plugin, string path)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var d = new PluginDescription
        {
            Format = PluginFormat.Vst2,
            Path = path,
            ClassId = plugin.UniqueId.ToString(CultureInfo.InvariantCulture),
            Name = plugin.Name,
            Vendor = plugin.Vendor,
            Version = plugin.VendorVersion > 0 ? plugin.VendorVersion.ToString(CultureInfo.InvariantCulture) : plugin.PluginVersion.ToString(CultureInfo.InvariantCulture),
            SdkVersion = plugin.VstVersion >= 2000 ? $"VST {plugin.VstVersion / 1000}.{plugin.VstVersion / 100 % 10}" : "VST 1",
            SubCategories = SubCategories(plugin),
            Category = plugin.IsInstrument ? "Instrument" : "Effect",
            HasEditor = plugin.HasEditor,
        };
        InProcessModuleScanner.Describe(plugin, d);
        return d;
    }

    /// <summary>VST 3 style categories ("Fx|Reverb") for a VST 2 category.</summary>
    public static string SubCategories(Vst2Plugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return plugin.Category switch
        {
            Vst2Category.Synth => "Instrument|Synth",
            Vst2Category.Analysis => "Fx|Analyzer",
            Vst2Category.Mastering => "Fx|Mastering",
            Vst2Category.Spatializer => "Fx|Spatial",
            Vst2Category.RoomFx => "Fx|Reverb",
            Vst2Category.SurroundFx => "Fx|Surround",
            Vst2Category.Restoration => "Fx|Restoration",
            Vst2Category.OfflineProcess => "Fx|Offline",
            Vst2Category.Generator => "Fx|Generator",
            _ => plugin.IsInstrument ? "Instrument" : "Fx",
        };
    }
}
