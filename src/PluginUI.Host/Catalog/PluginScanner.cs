// 2026/11/08 17:47:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Text;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Catalog;

/// <summary>Summary of a scan.</summary>
/// <param name="Modules">Modules found in the folders.</param>
/// <param name="Scanned">Modules scanned (new or changed).</param>
/// <param name="Unchanged">Modules taken from the cache.</param>
/// <param name="Removed">Plug-ins whose module disappeared.</param>
/// <param name="Failed">Modules that could not be loaded.</param>
/// <param name="Incompatible">Modules built for another processor (32-bit plug-ins in a 64-bit host), not loaded.</param>
public sealed record ScanSummary(int Modules, int Scanned, int Unchanged, int Removed, int Failed, int Incompatible = 0);

/// <summary>Progress of a scan: the module being scanned and how many are done.</summary>
/// <param name="Module">Module path.</param>
/// <param name="Index">Number of modules done.</param>
/// <param name="Count">Number of modules to scan.</param>
public readonly record struct ScanProgress(string Module, int Index, int Count);

/// <summary>Describes the plug-ins of one module (in this process, or in a child process that may crash safely).</summary>
public interface IModuleScanner
{
    /// <summary>Plug-ins of the module; throws when it cannot be loaded.</summary>
    IReadOnlyList<PluginDescription> Scan(string modulePath, PluginFormat format, TimeSpan timeout);
}

/// <summary>
/// Finds the plug-in modules in the folders of the settings and updates the catalog. Only new or changed modules (size
/// or date) are scanned again; the user's data (favorites, usage) is kept.
/// </summary>
public sealed class PluginScanner(IModuleScanner moduleScanner)
{
    /// <summary>Scanner loading modules out of process with <paramref name="executable"/> (see <see cref="OutOfProcessModuleScanner"/>).</summary>
    public static PluginScanner OutOfProcess(string executable, params string[] argumentsPrefix) => new(new OutOfProcessModuleScanner(executable, argumentsPrefix));

    /// <summary>Scanner loading modules in this process (a crashing plug-in takes the process down).</summary>
    public static PluginScanner InProcess() => new(new InProcessModuleScanner());

    /// <summary>
    /// Scans the VST 2 folders too (default: when VST 2 hosting is installed, see
    /// <see cref="InProcessModuleScanner.Vst2Scanner"/>). Without it, VST 2 modules are not listed.
    /// </summary>
    public bool? IncludeVst2 { get; set; }

    private bool ScansVst2 => IncludeVst2 ?? InProcessModuleScanner.Vst2Scanner is not null;

    /// <summary>
    /// Scanners of VST 2 modules built for a processor this process cannot load, by architecture (set by the VST 2
    /// package when a bridge is installed, see <c>Vst2Bridge</c>). Other such modules are listed as
    /// <see cref="PluginStatus.Incompatible"/>.
    /// </summary>
    public static Dictionary<ModuleArchitecture, IModuleScanner> BridgeScanners { get; } = [];

    /// <summary>
    /// Module files of the folders: .vst3 files and bundles (VST 3), and the libraries with a VST 2 entry point (.dll on
    /// Windows, .so on Linux), in every folder and its subfolders. Libraries inside .vst3 bundles are not VST 2 modules.
    /// </summary>
    /// <param name="settings">Folders.</param>
    /// <param name="includeVst2"><c>false</c> to look for VST 3 modules only.</param>
    public static IEnumerable<(string Path, PluginFormat Format)> FindModules(HostSettings settings, bool includeVst2 = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var ignored = new HashSet<string>(settings.IgnoredModules, seen.Comparer);
        foreach (string folder in settings.Folders)
        {
            foreach (string module in FindVst3(folder))
            {
                if (!ignored.Contains(module) && seen.Add(module)) yield return (module, PluginFormat.Vst3);
            }
        }
        if (!includeVst2) yield break;
        string vst2Extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".vst" : ".so";
        foreach (string folder in settings.Folders)
        {
            if (!Directory.Exists(folder)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*" + vst2Extension, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string module in files)
            {
                if (ignored.Contains(module) || InsideVst3Bundle(folder, module) || !seen.Add(module)) continue;
                // Plug-in folders also hold the libraries the plug-ins use: only modules with a VST 2 entry point count.
                if (vst2Extension == ".dll" && !PeExports.MayExport(module, "VSTPluginMain", "main")) continue;
                yield return (module, PluginFormat.Vst2);
            }
        }
    }

    /// <summary><c>true</c> when <paramref name="file"/> lies inside a .vst3 bundle under <paramref name="folder"/>.</summary>
    private static bool InsideVst3Bundle(string folder, string file)
    {
        string? directory = Path.GetDirectoryName(Path.GetRelativePath(folder, file));
        while (!string.IsNullOrEmpty(directory))
        {
            if (Path.GetFileName(directory).EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) return true;
            directory = Path.GetDirectoryName(directory);
        }
        return false;
    }

    private static IEnumerable<string> FindVst3(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            foreach (string entry in entries)
            {
                bool isVst3 = entry.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase);
                if (Directory.Exists(entry))
                {
                    if (isVst3) yield return entry; // a bundle: its binaries are inside, do not descend
                    else pending.Push(entry);
                }
                else if (isVst3)
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>Binary whose size and date identify a module version.</summary>
    public static string ModuleBinary(string path, PluginFormat format)
        => format == PluginFormat.Vst3 ? Vst3Module.ResolveBinary(path) ?? path : path;

    /// <summary>Scans the folders of <paramref name="settings"/> into <paramref name="catalog"/>.</summary>
    /// <param name="settings">Folders and options.</param>
    /// <param name="catalog">Catalog updated in place.</param>
    /// <param name="rescanAll"><c>true</c> to scan every module again (also those that failed before).</param>
    /// <param name="progress">Reports each module.</param>
    /// <param name="cancellation">Stops between modules.</param>
    public ScanSummary Scan(HostSettings settings, PluginCatalog catalog, bool rescanAll = false, IProgress<ScanProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var modules = FindModules(settings, ScansVst2).ToList();
        var present = new HashSet<string>(modules.Select(m => m.Path), comparer);
        var byModule = catalog.Plugins.GroupBy(p => p.Path, comparer).ToDictionary(g => g.Key, g => g.ToList(), comparer);

        int removed = catalog.Plugins.RemoveAll(p => !present.Contains(p.Path));
        int scanned = 0, unchanged = 0, failed = 0, incompatible = 0;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.ScanTimeoutSeconds));
        for (int i = 0; i < modules.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var (path, format) = modules[i];
            var (size, time) = FileStamp(ModuleBinary(path, format));
            byModule.TryGetValue(path, out var previous);
            bool same = previous is { Count: > 0 } && previous.All(p => p.ModuleSize == size && p.ModuleTimeUtc == time);
            if (same && !rescanAll && !NowBridged(previous!, path, format))
            {
                unchanged++;
                continue;
            }
            progress?.Report(new ScanProgress(path, i, modules.Count));
            var architecture = ModuleArchitectures.Of(path, format);
            List<PluginDescription> found;
            if (!ModuleArchitectures.CanLoad(architecture) && format == PluginFormat.Vst2 && BridgeScanners.TryGetValue(architecture, out var bridge))
            {
                try
                {
                    found = bridge.Scan(path, format, timeout).ToList();
                    if (found.Count == 0) throw new InvalidOperationException("The module contains no plug-in.");
                }
                catch (Exception ex)
                {
                    failed++;
                    found = [new PluginDescription
                    {
                        Format = format, Path = path, Name = Path.GetFileNameWithoutExtension(path), Status = PluginStatus.Failed,
                        Error = ex.Message, Category = string.Empty,
                    }];
                }
            }
            else if (!ModuleArchitectures.CanLoad(architecture))
            {
                // Not even started: the loader would only refuse it ("not a valid Win32 application").
                incompatible++;
                found = [new PluginDescription
                {
                    Format = format, Path = path, Name = Path.GetFileNameWithoutExtension(path), Status = PluginStatus.Incompatible,
                    Error = ModuleArchitectures.IncompatibilityMessage(architecture), Category = string.Empty,
                }];
            }
            else try
            {
                found = moduleScanner.Scan(path, format, timeout).ToList();
                if (found.Count == 0) throw new InvalidOperationException("The module contains no plug-in.");
            }
            catch (Exception ex)
            {
                failed++;
                found = [new PluginDescription
                {
                    Format = format, Path = path, Name = Path.GetFileNameWithoutExtension(path), Status = PluginStatus.Failed,
                    Error = ex.Message, Category = string.Empty,
                }];
            }
            foreach (var plugin in found)
            {
                plugin.Path = path;
                plugin.Architecture = architecture;
                plugin.ModuleSize = size;
                plugin.ModuleTimeUtc = time;
                plugin.ScannedUtc = DateTime.UtcNow;
                var older = previous?.FirstOrDefault(p => p.ClassId == plugin.ClassId) ?? (found.Count == 1 ? previous?.FirstOrDefault() : null);
                if (older is not null) plugin.KeepUserData(older);
            }
            catalog.Plugins.RemoveAll(p => comparer.Equals(p.Path, path));
            catalog.Plugins.AddRange(found);
            scanned++;
        }
        catalog.LastScanUtc = DateTime.UtcNow;
        progress?.Report(new ScanProgress(string.Empty, modules.Count, modules.Count));
        return new ScanSummary(modules.Count, scanned, unchanged, removed, failed, incompatible);
    }

    /// <summary>
    /// <c>true</c> when an unchanged module was recorded as not loadable (by a version without a bridge, or before
    /// its bridge was available) and a bridge can now scan it: it is scanned again without a full rescan.
    /// </summary>
    private static bool NowBridged(List<PluginDescription> previous, string path, PluginFormat format)
    {
        if (format != PluginFormat.Vst2 || BridgeScanners.Count == 0 || previous.All(p => p.Status == PluginStatus.Ok)) return false;
        var architecture = ModuleArchitectures.Of(path, format);
        return !ModuleArchitectures.CanLoad(architecture) && BridgeScanners.ContainsKey(architecture)
            && previous.Any(p => p.Architecture != architecture || p.Status == PluginStatus.Incompatible);
    }

    private static (long Size, DateTime Time) FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists) return (info.Length, info.LastWriteTimeUtc);
            var dir = new DirectoryInfo(path);
            return dir.Exists ? (0, dir.LastWriteTimeUtc) : (0, DateTime.MinValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, DateTime.MinValue);
        }
    }
}

/// <summary>Loads modules in this process to describe them.</summary>
public sealed class InProcessModuleScanner : IModuleScanner
{
    /// <summary>Describes the plug-ins of a VST 3 module (loads it, creates each plug-in once).</summary>
    /// <param name="module">Loaded module.</param>
    /// <param name="progress">Called before each plug-in with the number done and the total.</param>
    public static List<PluginDescription> DescribeVst3(Vst3Module module, Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var list = new List<PluginDescription>();
        var classes = module.Plugins.ToList();
        foreach (var info in classes)
        {
            progress?.Invoke(list.Count, classes.Count);
            var d = new PluginDescription
            {
                Format = PluginFormat.Vst3,
                Path = module.Path,
                ClassId = info.ClassIdString,
                Name = info.Name,
                Vendor = info.Vendor,
                Version = info.Version,
                SdkVersion = info.SdkVersion,
                SubCategories = info.SubCategories,
                Category = info.SubCategories.Contains("Instrument", StringComparison.OrdinalIgnoreCase) ? "Instrument" : "Effect",
            };
            try
            {
                using var plugin = module.CreatePlugin(info.ClassId);
                Describe(plugin, d);
                using var editor = plugin.CreateEditor();
                d.HasEditor = editor is not null;
            }
            catch (Exception ex)
            {
                d.Status = PluginStatus.Failed;
                d.Error = ex.Message;
            }
            list.Add(d);
        }
        return list;
    }

    /// <summary>Fills the bus, parameter and latency fields of <paramref name="d"/> from an instance.</summary>
    public static void Describe(PluginInstance plugin, PluginDescription d)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(d);
        d.InputChannels = plugin.InputBuses.Where(b => !b.IsAuxiliary).Sum(b => b.ChannelCount);
        d.OutputChannels = plugin.OutputBuses.Where(b => !b.IsAuxiliary).Sum(b => b.ChannelCount);
        d.AuxInputChannels = plugin.InputBuses.Where(b => b.IsAuxiliary).Sum(b => b.ChannelCount);
        d.AuxOutputChannels = plugin.OutputBuses.Where(b => b.IsAuxiliary).Sum(b => b.ChannelCount);
        d.HasMidiInput = plugin.EventInputCount > 0;
        d.ParameterCount = plugin.Parameters.Count;
        if (plugin.IsInstrument) d.Category = "Instrument";
        try
        {
            plugin.Prepare(48000, 512);
            d.LatencySamples = plugin.LatencySamples;
            plugin.Stop();
        }
        catch (Exception)
        {
            d.LatencySamples = plugin.LatencySamples;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginDescription> Scan(string modulePath, PluginFormat format, TimeSpan timeout) => Scan(modulePath, format, null);

    /// <summary>Plug-ins of a module, reporting each plug-in (number done, total).</summary>
    /// <remarks>On Windows, call it on a single-threaded apartment thread (see <see cref="ModuleScanProcess.RunOnUserInterfaceThread"/>).</remarks>
    public IReadOnlyList<PluginDescription> Scan(string modulePath, PluginFormat format, Action<int, int>? progress)
    {
        if (format == PluginFormat.Vst2)
        {
            if (Vst2Scanner is null) throw new NotSupportedException("VST 2 hosting is not installed.");
            return Vst2Scanner(modulePath);
        }
        using var module = Vst3Module.Load(modulePath);
        return DescribeVst3(module, progress);
    }

    /// <summary>Describes a VST 2 module (set by the VST 2 package when it is referenced).</summary>
    public static Func<string, IReadOnlyList<PluginDescription>>? Vst2Scanner { get; set; }
}

/// <summary>
/// Scans each module in a child process (<c>executable [prefix…] scan-module --format vst3 --out file.json "path"</c>, see
/// <see cref="ModuleScanProcess"/>): a plug-in that crashes or hangs while loading cannot take the host down; it is
/// marked as failed.
/// </summary>
/// <remarks>
/// The timeout counts the time without progress: a module with many plug-ins (a shell) may take minutes, as long as the
/// child reports each plug-in. What plug-ins write on the standard output is ignored; the last lines of the standard
/// error and the last step the child reached are kept to explain a failure. A complete result is used even when the
/// child crashed afterwards.
/// </remarks>
public sealed class OutOfProcessModuleScanner(string executable, params string[] argumentsPrefix) : IModuleScanner
{
    /// <inheritdoc />
    public IReadOnlyList<PluginDescription> Scan(string modulePath, PluginFormat format, TimeSpan timeout)
    {
        string output = Path.Combine(Path.GetTempPath(), "pluginhost-scan-" + Guid.NewGuid().ToString("N") + ".json");
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in argumentsPrefix) start.ArgumentList.Add(a);
        start.ArgumentList.Add(ModuleScanProcess.CommandName);
        start.ArgumentList.Add("--format");
        start.ArgumentList.Add(format == PluginFormat.Vst2 ? "vst2" : "vst3");
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(modulePath);

        var gate = new Lock();
        var noise = new Queue<string>();
        string? error = null, step = null;
        long lastActivity = Stopwatch.GetTimestamp(), doneAt = 0;
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp()); // plug-in output: ignored
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { } line) return;
            Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
            if (line.StartsWith(ModuleScanProcess.ProgressPrefix, StringComparison.Ordinal)) return;
            lock (gate)
            {
                if (line.StartsWith(ModuleScanProcess.StepPrefix, StringComparison.Ordinal))
                {
                    step = line[ModuleScanProcess.StepPrefix.Length..].Trim();
                    if (step == ModuleScanProcess.DoneStep) Interlocked.Exchange(ref doneAt, Stopwatch.GetTimestamp());
                    return;
                }
                if (line.StartsWith(ModuleScanProcess.ErrorPrefix, StringComparison.Ordinal))
                {
                    error = line[ModuleScanProcess.ErrorPrefix.Length..].Trim();
                    return;
                }
                if (line.Trim().Length == 0) return;
                noise.Enqueue(line.Trim());
                if (noise.Count > 3) noise.Dequeue();
            }
        };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Cannot start the scanner.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            while (!process.WaitForExit(250))
            {
                // The result is complete: the child is releasing the module. Some plug-ins keep threads that hang the
                // exit of the process, or crash while closing; after a grace period the child is stopped.
                long done = Interlocked.Read(ref doneAt);
                if (done != 0 && Stopwatch.GetElapsedTime(done) > ClosingGrace)
                {
                    Stop(process);
                    break;
                }
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastActivity)) <= timeout) continue;
                Stop(process);
                if (File.Exists(output)) break;
                throw new TimeoutException($"No answer for {timeout.TotalSeconds:0} s.");
            }
            process.WaitForExit(); // lets the output readers finish
            // A complete result counts even if the process crashed afterwards (some plug-ins crash while closing).
            if (File.Exists(output)) return PluginCatalog.FromJson(File.ReadAllText(output));
            string message;
            lock (gate)
            {
                message = error ?? Describe(process.ExitCode, noise);
                if (error is null && step is not null && step != ModuleScanProcess.DoneStep) message += $" Last step: {step}.";
            }
            throw new InvalidOperationException(message);
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temporary file left behind.
            }
        }
    }

    /// <summary>Time given to the child to release the module and exit once its result is written.</summary>
    public static TimeSpan ClosingGrace { get; set; } = TimeSpan.FromSeconds(5);

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static string Describe(int exitCode, IEnumerable<string> lastLines)
    {
        string reason = unchecked((uint)exitCode) switch
        {
            0xC0000005 => ": access violation in the plug-in",
            0xC0000409 => ": the plug-in ended the process (fail fast)",
            0xC00000FD => ": stack overflow in the plug-in",
            0xE0434352 => ": unhandled .NET exception",
            _ => string.Empty,
        };
        var sb = new StringBuilder($"The scanner stopped (code 0x{exitCode:X8}{reason}).");
        string output = string.Join(" / ", lastLines);
        if (output.Length > 0) sb.Append(" Output: ").Append(output.Length > 400 ? output[..400] + "…" : output);
        return sb.ToString();
    }
}
