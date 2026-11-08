// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Host.Catalog;
using PluginUI.Host.Vst3;

namespace PluginUI.Host;

/// <summary>
/// Loads plug-ins by catalog entry, name or path, and keeps their modules loaded until it is disposed (several plug-ins of
/// one module share it).
/// </summary>
public sealed class PluginLoader : IDisposable
{
    private readonly Dictionary<string, Vst3Module> _modules = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly List<PluginInstance> _instances = [];

    /// <summary>Creates a loader.</summary>
    /// <param name="catalog">Catalog used to resolve names (may be empty).</param>
    /// <param name="hostName">Name reported to the plug-ins.</param>
    public PluginLoader(PluginCatalog catalog, string hostName = "PluginUI Host")
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        HostName = hostName;
    }

    /// <summary>Catalog used by <see cref="Load(string)"/>.</summary>
    public PluginCatalog Catalog { get; }

    /// <summary>Name reported to the plug-ins.</summary>
    public string HostName { get; }

    /// <summary>
    /// Opens a VST 2 plug-in (set by the VST 2 package when it is referenced): module path, plug-in identifier or name
    /// (a plug-in of a shell module; null or empty = the module's plug-in), host name.
    /// </summary>
    public static Func<string, string?, string, PluginInstance>? Vst2Loader { get; set; }

    /// <summary>
    /// Loads a plug-in from a reference: a module path (".vst3" file or bundle, optionally followed by "|Plugin name"), or a
    /// catalog name ("EQ", "Vendor/EQ").
    /// </summary>
    /// <exception cref="ArgumentException">Nothing or several plug-ins match.</exception>
    public PluginInstance Load(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        string path = reference, name = string.Empty;
        int bar = reference.LastIndexOf('|');
        if (bar > 0)
        {
            path = reference[..bar];
            name = reference[(bar + 1)..];
        }
        if (File.Exists(path) || Directory.Exists(path))
        {
            if (!path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
            {
                if (Vst2Loader is null) throw new ArgumentException($"'{path}' is not a VST 3 module (VST 2 hosting is not available).");
                return Track(Vst2Loader(Path.GetFullPath(path), name.Length > 0 ? name : null, HostName));
            }
            var module = Module(Path.GetFullPath(path));
            return Track(module.CreatePlugin(name.Length > 0 ? name : null));
        }
        var matches = Catalog.Find(reference);
        if (matches.Count == 0) throw new ArgumentException($"No plug-in matches '{reference}'. Scan the folders, or give a path.");
        if (matches.Count > 1 && matches.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            throw new ArgumentException($"'{reference}' matches several plug-ins: {string.Join(", ", matches.Select(m => m.ToString()))}.");
        return Load(matches.OrderBy(m => m.Format == PluginFormat.Vst3 ? 0 : 1).First());
    }

    /// <summary>Loads a plug-in of the catalog and records its use.</summary>
    public PluginInstance Load(PluginDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        PluginInstance instance;
        if (description.Format == PluginFormat.Vst2)
        {
            if (Vst2Loader is null) throw new NotSupportedException("VST 2 hosting is not available.");
            instance = Vst2Loader(description.Path, description.ClassId, HostName);
        }
        else
        {
            var module = Module(description.Path);
            instance = string.IsNullOrEmpty(description.ClassId)
                ? module.CreatePlugin(description.Name)
                : module.CreatePlugin(Vst3Identifiers.FromFuidString(description.ClassId));
        }
        Catalog.MarkUsed(description);
        return Track(instance);
    }

    /// <summary>
    /// Loads the plug-in a graph file refers to: the catalog entry it designates, or else its module path when the file
    /// exists (VST 3 class or name, VST 2 identifier).
    /// </summary>
    /// <exception cref="ArgumentException">The plug-in is neither in the catalog nor at its path.</exception>
    public PluginInstance Load(Graph.PluginReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.FindIn(Catalog) is { } description) return Load(description);
        if (reference.Path.Length > 0 && (File.Exists(reference.Path) || Directory.Exists(reference.Path)))
        {
            return Load(new PluginDescription
            {
                Format = reference.Format,
                Path = Path.GetFullPath(reference.Path),
                ClassId = reference.ClassId,
                Name = reference.Name,
                Vendor = reference.Vendor,
            });
        }
        throw new ArgumentException($"'{reference.Name}' ({reference.Vendor}) is not in the plug-in list and '{reference.Path}' does not exist. Scan the folders.");
    }

    private Vst3Module Module(string path)
    {
        if (!_modules.TryGetValue(path, out var module))
        {
            module = Vst3Module.Load(path, HostName);
            _modules[path] = module;
        }
        return module;
    }

    private PluginInstance Track(PluginInstance instance)
    {
        _instances.Add(instance);
        return instance;
    }

    /// <summary>Releases the plug-ins, then the modules.</summary>
    public void Dispose()
    {
        foreach (var instance in _instances) instance.Dispose();
        _instances.Clear();
        foreach (var module in _modules.Values) module.Dispose();
        _modules.Clear();
    }
}
