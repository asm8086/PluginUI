// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;

namespace PluginUI.Host.Catalog;

/// <summary>Result of the scan of a plug-in.</summary>
public enum PluginStatus
{
    /// <summary>Loaded and described.</summary>
    Ok,
    /// <summary>Crashed, timed out or refused to load: kept in the list, skipped until the next full rescan.</summary>
    Failed,
    /// <summary>Excluded by the user.</summary>
    Ignored,
}

/// <summary>A plug-in found on the computer, as remembered between sessions.</summary>
public sealed class PluginDescription
{
    /// <summary>Format.</summary>
    public PluginFormat Format { get; set; }

    /// <summary>Module path (.vst3 file or bundle, VST 2 DLL).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Class identifier (VST 3: 32 hexadecimal digits; VST 2: unique id).</summary>
    public string ClassId { get; set; } = string.Empty;

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Vendor (brand).</summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>Version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>SDK version the plug-in was built with.</summary>
    public string SdkVersion { get; set; } = string.Empty;

    /// <summary>Main category: "Instrument" or "Effect".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Detailed categories separated by '|' ("Fx|EQ", "Instrument|Synth").</summary>
    public string SubCategories { get; set; } = string.Empty;

    /// <summary>Channels of the main input bus.</summary>
    public int InputChannels { get; set; }

    /// <summary>Channels of the main output bus.</summary>
    public int OutputChannels { get; set; }

    /// <summary>Channels of the auxiliary inputs (side chain).</summary>
    public int AuxInputChannels { get; set; }

    /// <summary>Channels of the auxiliary outputs.</summary>
    public int AuxOutputChannels { get; set; }

    /// <summary><c>true</c> if the plug-in takes MIDI / notes.</summary>
    public bool HasMidiInput { get; set; }

    /// <summary>Number of parameters.</summary>
    public int ParameterCount { get; set; }

    /// <summary>Latency in samples at 48 kHz.</summary>
    public int LatencySamples { get; set; }

    /// <summary><c>true</c> if the plug-in has its own editor window.</summary>
    public bool HasEditor { get; set; }

    /// <summary>Size of the module binary (detects updates).</summary>
    public long ModuleSize { get; set; }

    /// <summary>Last write time of the module binary, UTC (detects updates; "installed" date).</summary>
    public DateTime ModuleTimeUtc { get; set; }

    /// <summary>Scan result.</summary>
    public PluginStatus Status { get; set; }

    /// <summary>Why the scan failed.</summary>
    public string? Error { get; set; }

    /// <summary>When the plug-in was scanned, UTC.</summary>
    public DateTime ScannedUtc { get; set; }

    /// <summary>Marked as favorite by the user (kept across scans).</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Last time the plug-in was loaded by a host, UTC (kept across scans).</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>Number of times it was loaded (kept across scans).</summary>
    public int UseCount { get; set; }

    /// <summary><c>true</c> for an instrument.</summary>
    [JsonIgnore]
    public bool IsInstrument => Category == "Instrument";

    /// <summary>Folder of the module.</summary>
    [JsonIgnore]
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>Identity across scans.</summary>
    [JsonIgnore]
    public string Key => $"{Format}|{Path}|{ClassId}".ToUpperInvariant();

    /// <summary>"Name (Vendor, VST3)".</summary>
    public override string ToString() => $"{Name} ({Vendor}, {Format.ToString().ToUpperInvariant()})";

    /// <summary>Copies the user's data (favorite, usage) from an older entry of the same plug-in.</summary>
    public void KeepUserData(PluginDescription older)
    {
        IsFavorite = older.IsFavorite;
        LastUsedUtc = older.LastUsedUtc;
        UseCount = older.UseCount;
    }
}

/// <summary>Sort orders of the plug-in list.</summary>
public enum PluginSort
{
    /// <summary>Name, A to Z.</summary>
    Name,
    /// <summary>Vendor, then name.</summary>
    Vendor,
    /// <summary>Category (instrument / effect, then sub-category), then name.</summary>
    Category,
    /// <summary>Format (VST2, VST3), then name.</summary>
    Format,
    /// <summary>Main input channels (most first).</summary>
    Inputs,
    /// <summary>Main output channels (most first).</summary>
    Outputs,
    /// <summary>Latency (smallest first).</summary>
    Latency,
    /// <summary>Installation or update date (newest first).</summary>
    Installed,
    /// <summary>Last use (most recent first).</summary>
    RecentlyUsed,
    /// <summary>Number of uses (most first).</summary>
    MostUsed,
    /// <summary>Favorites first, then name.</summary>
    Favorites,
    /// <summary>Folder, then name.</summary>
    Folder,
    /// <summary>Scan status (failed first), then name.</summary>
    Status,
}

/// <summary>The list of plug-ins found on the computer, saved between sessions.</summary>
public sealed class PluginCatalog
{
    /// <summary>Plug-ins.</summary>
    public List<PluginDescription> Plugins { get; set; } = [];

    /// <summary>Last scan, UTC.</summary>
    public DateTime? LastScanUtc { get; set; }

    /// <summary>Loads a catalog (an empty one if the file does not exist or cannot be read).</summary>
    public static PluginCatalog Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new PluginCatalog();
            return JsonSerializer.Deserialize(File.ReadAllText(path), HostJsonContext.Default.PluginCatalog) ?? new PluginCatalog();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PluginCatalog();
        }
    }

    /// <summary>Saves the catalog (atomically: written next to it, then moved).</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, HostJsonContext.Default.PluginCatalog));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Plug-ins matching <paramref name="query"/>: a path, a name (exact, then starting with, then containing), optionally
    /// "Vendor/Name"; failed and ignored plug-ins excluded unless <paramref name="includeUnavailable"/>.
    /// </summary>
    public IReadOnlyList<PluginDescription> Find(string query, bool includeUnavailable = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Trim();
        var candidates = Plugins.Where(p => includeUnavailable || p.Status == PluginStatus.Ok).ToList();
        string? vendor = null;
        string name = query;
        int slash = query.IndexOf('/');
        if (slash > 0 && !Path.IsPathRooted(query))
        {
            vendor = query[..slash].Trim();
            name = query[(slash + 1)..].Trim();
        }
        bool VendorOk(PluginDescription p) => vendor is null || p.Vendor.Equals(vendor, StringComparison.OrdinalIgnoreCase);
        var byPath = candidates.Where(p => string.Equals(p.Path, query, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList();
        if (byPath.Count > 0) return byPath;
        foreach (var rule in new Func<PluginDescription, bool>[]
        {
            p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase),
            p => p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase),
            p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase),
        })
        {
            var found = candidates.Where(p => VendorOk(p) && rule(p)).ToList();
            if (found.Count > 0) return found;
        }
        return [];
    }

    /// <summary>Plug-ins filtered by a text (name, vendor or category) and sorted.</summary>
    public IEnumerable<PluginDescription> Query(string? text = null, PluginSort sort = PluginSort.Name, bool descending = false,
        PluginFormat? format = null, bool? instruments = null, bool includeUnavailable = true)
    {
        IEnumerable<PluginDescription> items = Plugins;
        if (!includeUnavailable) items = items.Where(p => p.Status == PluginStatus.Ok);
        if (format is { } f) items = items.Where(p => p.Format == f);
        if (instruments is { } i) items = items.Where(p => p.IsInstrument == i);
        if (!string.IsNullOrWhiteSpace(text))
        {
            string t = text.Trim();
            items = items.Where(p => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || p.Vendor.Contains(t, StringComparison.OrdinalIgnoreCase)
                || p.SubCategories.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        var ordered = Sort(items, sort);
        return descending ? ordered.Reverse() : ordered;
    }

    /// <summary>Sorts plug-ins.</summary>
    public static IEnumerable<PluginDescription> Sort(IEnumerable<PluginDescription> items, PluginSort sort)
    {
        var byName = StringComparer.OrdinalIgnoreCase;
        return sort switch
        {
            PluginSort.Vendor => items.OrderBy(p => p.Vendor, byName).ThenBy(p => p.Name, byName),
            PluginSort.Category => items.OrderBy(p => p.Category, byName).ThenBy(p => p.SubCategories, byName).ThenBy(p => p.Name, byName),
            PluginSort.Format => items.OrderBy(p => p.Format).ThenBy(p => p.Name, byName),
            PluginSort.Inputs => items.OrderByDescending(p => p.InputChannels).ThenBy(p => p.Name, byName),
            PluginSort.Outputs => items.OrderByDescending(p => p.OutputChannels).ThenBy(p => p.Name, byName),
            PluginSort.Latency => items.OrderBy(p => p.LatencySamples).ThenBy(p => p.Name, byName),
            PluginSort.Installed => items.OrderByDescending(p => p.ModuleTimeUtc).ThenBy(p => p.Name, byName),
            PluginSort.RecentlyUsed => items.OrderByDescending(p => p.LastUsedUtc ?? DateTime.MinValue).ThenBy(p => p.Name, byName),
            PluginSort.MostUsed => items.OrderByDescending(p => p.UseCount).ThenBy(p => p.Name, byName),
            PluginSort.Favorites => items.OrderByDescending(p => p.IsFavorite).ThenBy(p => p.Name, byName),
            PluginSort.Folder => items.OrderBy(p => p.Folder, byName).ThenBy(p => p.Name, byName),
            PluginSort.Status => items.OrderByDescending(p => p.Status).ThenBy(p => p.Name, byName),
            _ => items.OrderBy(p => p.Name, byName).ThenBy(p => p.Vendor, byName),
        };
    }

    /// <summary>JSON array of plug-in descriptions (as in the catalog file).</summary>
    public static string ToJson(IEnumerable<PluginDescription> plugins)
        => JsonSerializer.Serialize(plugins.ToList(), HostJsonContext.Default.ListPluginDescription);

    /// <summary>Plug-in descriptions from <see cref="ToJson"/>.</summary>
    public static List<PluginDescription> FromJson(string json)
        => JsonSerializer.Deserialize(json, HostJsonContext.Default.ListPluginDescription) ?? [];

    /// <summary>Records that a plug-in was loaded (for the "recently used" and "most used" orders).</summary>
    public void MarkUsed(PluginDescription plugin)
    {
        plugin.LastUsedUtc = DateTime.UtcNow;
        plugin.UseCount++;
    }
}

/// <summary>Host settings: plug-in folders and scan options, shared by the graphical and command-line hosts.</summary>
public sealed class HostSettings
{
    /// <summary>Folders searched for VST 3 plug-ins.</summary>
    public List<string> Vst3Folders { get; set; } = [];

    /// <summary>Folders searched for VST 2 plug-ins.</summary>
    public List<string> Vst2Folders { get; set; } = [];

    /// <summary>Modules never scanned (paths).</summary>
    public List<string> IgnoredModules { get; set; } = [];

    /// <summary>Seconds after which the scan of one module is abandoned (default 30).</summary>
    public int ScanTimeoutSeconds { get; set; } = 30;

    /// <summary>Loads the settings; the default folders of the platform when the file does not exist.</summary>
    public static HostSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllText(path), HostJsonContext.Default.HostSettings);
                if (settings is not null) return settings;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable settings: start again from the defaults.
        }
        return CreateDefault();
    }

    /// <summary>Saves the settings.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, HostJsonContext.Default.HostSettings));
    }

    /// <summary>Settings with the standard plug-in folders of the platform that exist.</summary>
    public static HostSettings CreateDefault() => new()
    {
        Vst3Folders = DefaultVst3Folders().Where(Directory.Exists).ToList(),
        Vst2Folders = DefaultVst2Folders().Where(Directory.Exists).ToList(),
    };

    /// <summary>Standard VST 3 folders (whether they exist or not).</summary>
    public static IEnumerable<string> DefaultVst3Folders()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3");
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Audio/Plug-Ins/VST3";
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Audio/Plug-Ins/VST3");
        }
        else
        {
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vst3");
            yield return "/usr/lib/vst3";
            yield return "/usr/local/lib/vst3";
        }
    }

    /// <summary>Usual VST 2 folders (there is no standard one).</summary>
    public static IEnumerable<string> DefaultVst2Folders()
    {
        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            yield return System.IO.Path.Combine(programFiles, "VSTPlugins");
            yield return System.IO.Path.Combine(programFiles, "Steinberg", "VSTPlugins");
            yield return System.IO.Path.Combine(common, "VST2");
            yield return System.IO.Path.Combine(common, "Steinberg", "VST2");
            if (ReadVst2RegistryPath() is { } registry) yield return registry;
        }
        else if (!OperatingSystem.IsMacOS())
        {
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vst");
            yield return "/usr/lib/vst";
            yield return "/usr/local/lib/vst";
        }
    }

    /// <summary>VSTPluginsPath of HKEY_LOCAL_MACHINE\SOFTWARE\VST, where many installers put VST 2 plug-ins.</summary>
    private static string? ReadVst2RegistryPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\VST");
            return key?.GetValue("VSTPluginsPath") as string;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Where the hosts keep their files: %AppData%\PluginUI\PluginHost on Windows, ~/.config/PluginUI/PluginHost elsewhere.</summary>
public static class HostPaths
{
    /// <summary>Configuration folder.</summary>
    public static string ConfigFolder { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "PluginUI", "PluginHost");

    /// <summary>Settings file (folders, options).</summary>
    public static string SettingsFile => System.IO.Path.Combine(ConfigFolder, "settings.json");

    /// <summary>Plug-in list.</summary>
    public static string CatalogFile => System.IO.Path.Combine(ConfigFolder, "plugins.json");
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PluginCatalog))]
[JsonSerializable(typeof(HostSettings))]
[JsonSerializable(typeof(List<PluginDescription>))]
internal sealed partial class HostJsonContext : JsonSerializerContext
{
}
