// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using PluginUI.Host;
using PluginUI.Host.Audio;
using PluginUI.Host.Catalog;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Cli;

/// <summary>pluginhost: scan, list, inspect plug-ins, process audio files and measure responses from the command line.</summary>
public static class Program
{
    private const string Usage = """
        pluginhost — audio plug-in host on the command line (VST3)

        Plug-in list (kept between runs):
          pluginhost folders                          list the plug-in folders
          pluginhost folders add <folder> [--vst2]    add a folder
          pluginhost folders remove <folder>          remove a folder
          pluginhost folders reset                    back to the standard folders
          pluginhost scan [--all] [--in-process]      find new and changed plug-ins (--all: everything again)
          pluginhost list [text] [--sort <order>] [--desc] [--vst2|--vst3] [--instruments|--effects] [--failed] [--json]
                 orders: name vendor category format inputs outputs latency installed recent used favorites folder status
          pluginhost favorite <plugin> [--off]

        One plug-in (<plugin>: a name from the list, "Vendor/Name", or a path to a .vst3 file or bundle):
          pluginhost info <plugin> [--json]           buses, latency, parameters with their current values
          pluginhost preset show <file.vstpreset>
          pluginhost preset save <plugin> -o <file.vstpreset> [--set ...] [--preset ...]

        Audio:
          pluginhost process -i <in.wav> -o <out.wav> -p <plugin> [plugin options] [-p <plugin> ...] [--midi <file.mid>] [audio options]
          pluginhost render  -p <instrument> --midi <file.mid> -o <out.wav> [--rate 48000] [plugin options] [audio options]
                 (a MIDI effect alone: -o is not written, use --midi-out)
          pluginhost measure -p <plugin> -o <response.csv> [plugin options] [--ir <ir.wav>] [--method impulse|sine]
                 [--rate 48000] [--min 20] [--max 20000] [--points 200] [--level -20] [--channel 0] [--db 20log10|6log2]

        Plug-in options (apply to the -p before them):
          --set "Name=value"           value as the plug-in displays it ("Gain=-6 dB", "Mode=Warm")
          --set-normalized "Name=0.25" normalized value (0..1)
          --preset <file.vstpreset>    load a preset first
        Audio options:
          --block 512   --tail <seconds>   --no-latency-compensation   --bits 16|24|32|f32|f64 (default f32)
          --midi-out <file.mid>   MIDI the last plug-in produces (the MIDI of the others feeds the next plug-in)

        General: --config <folder> (settings and plug-in list elsewhere), --json, --help
        """;

    /// <summary>Entry point.</summary>
    /// <remarks>
    /// [STAThread]: plug-ins are loaded on a single-threaded apartment thread, as in a DAW (some initialize COM or OLE for
    /// their user interface and fail in a multithreaded apartment).
    /// </remarks>
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Child process of an out-of-process scan (arguments after the command name; --config does not matter there).
        int scanCommand = Array.IndexOf(args, ModuleScanProcess.CommandName);
        if (scanCommand >= 0) return ModuleScanProcess.Run(args[(scanCommand + 1)..]);
        try
        {
            var a = new Arguments(args);
            if (a.Option("--config") is { } config) HostPaths.ConfigFolder = config;
            string command = a.Next() ?? "help";
            return command switch
            {
                "folders" => Folders(a),
                "scan" => Scan(a),
                "list" => List(a),
                "favorite" => Favorite(a),
                "info" => Info(a),
                "preset" => Preset(a),
                "process" => Process(a),
                "render" => Render(a),
                "measure" => Measure(a),
                "help" or "--help" or "-h" or "/?" => Help(),
                _ => throw new UsageException($"Unknown command '{command}'."),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("pluginhost --help for the commands.");
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or FormatException
            or Vst3Exception or NotSupportedException or UnauthorizedAccessException or TimeoutException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 2;
        }
    }

    private static int Help()
    {
        Console.WriteLine(Usage);
        return 0;
    }

    // ---------------------------------------------------------------- folders and scan

    private static int Folders(Arguments a)
    {
        var settings = HostSettings.Load(HostPaths.SettingsFile);
        string? action = a.Next();
        bool vst2 = a.Flag("--vst2");
        switch (action)
        {
            case null:
                break;
            case "add":
            {
                string folder = Path.GetFullPath(a.Next() ?? throw new UsageException("folders add <folder>"));
                if (!Directory.Exists(folder)) throw new ArgumentException($"'{folder}' does not exist.");
                var list = vst2 ? settings.Vst2Folders : settings.Vst3Folders;
                if (!list.Contains(folder, StringComparer.OrdinalIgnoreCase)) list.Add(folder);
                settings.Save(HostPaths.SettingsFile);
                break;
            }
            case "remove":
            {
                string folder = a.Next() ?? throw new UsageException("folders remove <folder>");
                string full = Path.GetFullPath(folder);
                int removed = settings.Vst3Folders.RemoveAll(f => Same(f, folder) || Same(f, full)) + settings.Vst2Folders.RemoveAll(f => Same(f, folder) || Same(f, full));
                if (removed == 0) throw new ArgumentException($"'{folder}' is not in the list.");
                settings.Save(HostPaths.SettingsFile);
                break;
            }
            case "reset":
                settings = HostSettings.CreateDefault();
                settings.Save(HostPaths.SettingsFile);
                break;
            default:
                throw new UsageException($"Unknown folders action '{action}'.");
        }
        Console.WriteLine($"Settings: {HostPaths.SettingsFile}");
        Console.WriteLine("VST3 folders:");
        foreach (string f in settings.Vst3Folders) Console.WriteLine($"  {f}{(Directory.Exists(f) ? "" : "   (missing)")}");
        Console.WriteLine("VST2 folders:");
        foreach (string f in settings.Vst2Folders) Console.WriteLine($"  {f}{(Directory.Exists(f) ? "" : "   (missing)")}");
        return 0;
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static int Scan(Arguments a)
    {
        var settings = HostSettings.Load(HostPaths.SettingsFile);
        if (!File.Exists(HostPaths.SettingsFile)) settings.Save(HostPaths.SettingsFile);
        var catalog = PluginCatalog.Load(HostPaths.CatalogFile);
        bool all = a.Flag("--all");
        bool inProcess = a.Flag("--in-process");
        a.EnsureEmpty();
        var scanner = inProcess ? PluginScanner.InProcess() : PluginScanner.OutOfProcess(Environment.ProcessPath!, SelfArguments());
        var progress = new SyncProgress<ScanProgress>(p =>
        {
            if (p.Module.Length > 0) Console.WriteLine($"[{p.Index + 1}/{p.Count}] {p.Module}");
        });
        var summary = scanner.Scan(settings, catalog, all, progress);
        catalog.Save(HostPaths.CatalogFile);
        int ok = catalog.Plugins.Count(p => p.Status == PluginStatus.Ok);
        Console.WriteLine($"{summary.Modules} modules: {summary.Scanned} scanned, {summary.Unchanged} unchanged, {summary.Removed} plug-ins removed, {summary.Failed} failed.");
        Console.WriteLine($"{ok} plug-ins available. List: {HostPaths.CatalogFile}");
        foreach (var failed in catalog.Plugins.Where(p => p.Status == PluginStatus.Failed))
            Console.WriteLine($"  failed: {failed.Path}: {failed.Error}");
        return 0;
    }

    /// <summary>Arguments that start this program again (the DLL path when run with "dotnet pluginhost.dll").</summary>
    private static string[] SelfArguments()
    {
        string process = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (process.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return [Path.Combine(AppContext.BaseDirectory, "pluginhost.dll"), "--config", HostPaths.ConfigFolder];
        return ["--config", HostPaths.ConfigFolder];
    }

    private static int List(Arguments a)
    {
        var catalog = PluginCatalog.Load(HostPaths.CatalogFile);
        string sortText = a.Option("--sort") ?? "name";
        bool desc = a.Flag("--desc");
        PluginFormat? format = a.Flag("--vst2") ? PluginFormat.Vst2 : a.Flag("--vst3") ? PluginFormat.Vst3 : null;
        bool? instruments = a.Flag("--instruments") ? true : a.Flag("--effects") ? false : null;
        bool failed = a.Flag("--failed");
        bool json = a.Flag("--json");
        string? text = a.Next();
        a.EnsureEmpty();
        var sort = sortText.ToLowerInvariant() switch
        {
            "name" => PluginSort.Name,
            "vendor" or "brand" => PluginSort.Vendor,
            "category" => PluginSort.Category,
            "format" or "type" => PluginSort.Format,
            "inputs" => PluginSort.Inputs,
            "outputs" => PluginSort.Outputs,
            "latency" => PluginSort.Latency,
            "installed" or "date" => PluginSort.Installed,
            "recent" => PluginSort.RecentlyUsed,
            "used" => PluginSort.MostUsed,
            "favorites" => PluginSort.Favorites,
            "folder" => PluginSort.Folder,
            "status" => PluginSort.Status,
            _ => throw new UsageException($"Unknown order '{sortText}'."),
        };
        var items = catalog.Query(text, sort, desc, format, instruments, includeUnavailable: failed).ToList();
        if (json)
        {
            Console.WriteLine(PluginCatalog.ToJson(items));
            return 0;
        }
        if (catalog.Plugins.Count == 0)
        {
            Console.WriteLine("The list is empty: run 'pluginhost scan'.");
            return 0;
        }
        var rows = items.Select(p => new[]
        {
            (p.IsFavorite ? "* " : "  ") + p.Name, p.Vendor, p.Format == PluginFormat.Vst3 ? "VST3" : "VST2",
            string.IsNullOrEmpty(p.SubCategories) ? p.Category : p.SubCategories,
            $"{p.InputChannels}{(p.AuxInputChannels > 0 ? "+" + p.AuxInputChannels : "")}/{p.OutputChannels}",
            p.LatencySamples.ToString(CultureInfo.InvariantCulture), p.Version, p.Status == PluginStatus.Ok ? "" : p.Status.ToString(),
        }).ToList();
        PrintTable(["Name", "Vendor", "Format", "Category", "In/Out", "Latency", "Version", "Status"], rows);
        Console.WriteLine($"{items.Count} plug-ins.");
        return 0;
    }

    private static int Favorite(Arguments a)
    {
        var catalog = PluginCatalog.Load(HostPaths.CatalogFile);
        bool off = a.Flag("--off");
        string reference = a.Next() ?? throw new UsageException("favorite <plugin>");
        a.EnsureEmpty();
        var matches = catalog.Find(reference, includeUnavailable: true);
        if (matches.Count == 0) throw new ArgumentException($"No plug-in matches '{reference}'.");
        foreach (var p in matches)
        {
            p.IsFavorite = !off;
            Console.WriteLine($"{(off ? "Removed from" : "Added to")} favorites: {p}");
        }
        catalog.Save(HostPaths.CatalogFile);
        return 0;
    }

    // ---------------------------------------------------------------- one plug-in

    private static int Info(Arguments a)
    {
        bool json = a.Flag("--json");
        string reference = a.Next() ?? throw new UsageException("info <plugin>");
        var catalog = PluginCatalog.Load(HostPaths.CatalogFile);
        using var loader = new PluginLoader(catalog, "pluginhost");
        var plugin = loader.Load(reference);
        ApplyPluginOptions(a, plugin);
        a.EnsureEmpty();
        plugin.Prepare(48000, 512);
        if (json)
        {
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"{{\"name\":{Json(plugin.Name)},\"vendor\":{Json(plugin.Vendor)},\"format\":\"{plugin.Format}\",\"instrument\":{(plugin.IsInstrument ? "true" : "false")},");
            sb.Append(CultureInfo.InvariantCulture, $"\"latencySamples\":{plugin.LatencySamples},\"tailSamples\":{plugin.TailSamples},\"midiInputs\":{plugin.EventInputCount},");
            sb.Append("\"inputs\":[").Append(string.Join(",", plugin.InputBuses.Select(BusJson))).Append("],\"outputs\":[").Append(string.Join(",", plugin.OutputBuses.Select(BusJson))).Append("],");
            sb.Append("\"parameters\":[");
            sb.Append(string.Join(",", plugin.Parameters.Select(p =>
            {
                double v = plugin.GetParameter(p.Id);
                return string.Create(CultureInfo.InvariantCulture,
                    $"{{\"id\":{p.Id},\"title\":{Json(p.Title)},\"units\":{Json(p.Units)},\"steps\":{p.StepCount},\"normalized\":{v:R},\"value\":{Json(plugin.FormatParameter(p.Id, v))},\"default\":{Json(plugin.FormatParameter(p.Id, p.DefaultValue))},\"flags\":{Json(p.Flags.ToString())}}}");
            })));
            sb.Append("]}");
            Console.WriteLine(sb.ToString());
        }
        else
        {
            Console.WriteLine($"{plugin.Name} — {plugin.Vendor} ({plugin.Format.ToString().ToUpperInvariant()}{(plugin.IsInstrument ? ", instrument" : "")})");
            foreach (var b in plugin.InputBuses) Console.WriteLine($"  input   {b.Name}: {b.ChannelCount} ch{(b.IsAuxiliary ? " (aux)" : "")}");
            foreach (var b in plugin.OutputBuses) Console.WriteLine($"  output  {b.Name}: {b.ChannelCount} ch{(b.IsAuxiliary ? " (aux)" : "")}");
            if (plugin.EventInputCount > 0) Console.WriteLine($"  MIDI inputs: {plugin.EventInputCount}");
            Console.WriteLine($"  latency {plugin.LatencySamples} samples at 48 kHz, tail {(plugin.TailSamples == int.MaxValue ? "infinite" : plugin.TailSamples + " samples")}");
            Console.WriteLine();
            var rows = plugin.Parameters.Where(p => (p.Flags & ParameterFlags.IsHidden) == 0).Select(p =>
            {
                double v = plugin.GetParameter(p.Id);
                return new[]
                {
                    p.Id.ToString(CultureInfo.InvariantCulture), p.Title, plugin.FormatParameter(p.Id, v), v.ToString("0.####", CultureInfo.InvariantCulture),
                    plugin.FormatParameter(p.Id, p.DefaultValue), p.StepCount == 0 ? "" : (p.StepCount + 1).ToString(CultureInfo.InvariantCulture) + " values",
                    (p.Flags & ParameterFlags.IsReadOnly) != 0 ? "read-only" : "",
                };
            }).ToList();
            PrintTable(["Id", "Parameter", "Value", "Normalized", "Default", "Steps", ""], rows);
        }
        return 0;
    }

    private static string BusJson(PluginBus b) => string.Create(CultureInfo.InvariantCulture, $"{{\"name\":{Json(b.Name)},\"channels\":{b.ChannelCount},\"auxiliary\":{(b.IsAuxiliary ? "true" : "false")}}}");

    private static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s, CliJsonContext.Default.String);

    private static int Preset(Arguments a)
    {
        string action = a.Next() ?? throw new UsageException("preset show|save");
        if (action == "show")
        {
            string file = a.Next() ?? throw new UsageException("preset show <file.vstpreset>");
            a.EnsureEmpty();
            var content = VstPreset.Read(File.ReadAllBytes(file));
            Console.WriteLine($"Class: {Vst3Identifiers.ToFuidString(content.ClassId)}");
            Console.WriteLine($"Processor state: {content.ComponentState.Length} bytes; controller state: {content.ControllerState.Length} bytes");
            var catalog = PluginCatalog.Load(HostPaths.CatalogFile);
            var owner = catalog.Plugins.FirstOrDefault(p => p.ClassId.Equals(Vst3Identifiers.ToFuidString(content.ClassId), StringComparison.OrdinalIgnoreCase));
            if (owner is not null) Console.WriteLine($"Plug-in: {owner}");
            if (content.MetaInfo.Length > 0) Console.WriteLine(content.MetaInfo);
            return 0;
        }
        if (action == "save")
        {
            string output = a.Option("-o") ?? a.Option("--output") ?? throw new UsageException("preset save <plugin> -o <file>");
            string reference = a.Next() ?? throw new UsageException("preset save <plugin> -o <file>");
            using var loader = new PluginLoader(PluginCatalog.Load(HostPaths.CatalogFile), "pluginhost");
            var plugin = loader.Load(reference);
            ApplyPluginOptions(a, plugin);
            a.EnsureEmpty();
            plugin.Prepare(48000, 512);
            plugin.Process(1); // lets the processor take the parameter changes before its state is read
            File.WriteAllBytes(output, plugin.GetState());
            Console.WriteLine($"Saved {output}");
            return 0;
        }
        throw new UsageException($"Unknown preset action '{action}'.");
    }

    // ---------------------------------------------------------------- audio

    /// <summary>Applies --preset, --set and --set-normalized options that follow a plug-in (until the next -p).</summary>
    private static void ApplyPluginOptions(Arguments a, PluginInstance plugin)
    {
        while (a.TakeUntil("-p", "--plugin", ["--preset", "--set", "--set-normalized"]) is var (option, value))
        {
            switch (option)
            {
                case "--preset":
                    plugin.SetState(File.ReadAllBytes(value));
                    break;
                case "--set":
                case "--set-normalized":
                {
                    int eq = value.IndexOf('=');
                    if (eq <= 0) throw new UsageException($"{option} expects \"Name=value\", not '{value}'.");
                    string name = value[..eq].Trim(), text = value[(eq + 1)..].Trim();
                    var parameter = plugin.FindParameter(name) ?? throw new ArgumentException($"'{plugin.Name}' has no parameter '{name}'. Parameters: {string.Join(", ", plugin.Parameters.Where(p => (p.Flags & ParameterFlags.IsHidden) == 0).Select(p => p.Title))}.");
                    double normalized;
                    if (option == "--set-normalized")
                    {
                        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out normalized)) throw new ArgumentException($"'{text}' is not a number.");
                    }
                    else if (!plugin.TryParseParameter(parameter.Id, text, out normalized))
                    {
                        throw new ArgumentException($"'{text}' is not a valid value for '{parameter.Title}'.");
                    }
                    plugin.SetParameter(parameter.Id, normalized);
                    break;
                }
            }
        }
    }

    private static List<PluginInstance> LoadChain(Arguments a, PluginLoader loader)
    {
        var chain = new List<PluginInstance>();
        while (a.TakeOption("-p", "--plugin") is { } reference)
        {
            var plugin = loader.Load(reference);
            ApplyPluginOptions(a, plugin);
            chain.Add(plugin);
        }
        if (chain.Count == 0) throw new UsageException("Give at least one plug-in with -p.");
        return chain;
    }

    private static RenderOptions RenderOptionsFrom(Arguments a)
    {
        var options = new RenderOptions
        {
            BlockSize = a.IntOption("--block") ?? 512,
            TailSeconds = a.DoubleOption("--tail"),
            CompensateLatency = !a.Flag("--no-latency-compensation"),
        };
        long lastPercent = -1;
        options.Progress = f =>
        {
            long percent = (long)(f * 100);
            if (percent / 10 != lastPercent / 10 && !Console.IsErrorRedirected) Console.Error.Write($"\r{percent,3} %");
            lastPercent = percent;
        };
        return options;
    }

    /// <summary>--midi-out: collects the MIDI of the last plug-in; the returned action writes the file.</summary>
    private static Action<double>? MidiOutputFrom(Arguments a, RenderOptions options)
    {
        if (a.Option("--midi-out") is not { } path) return null;
        var collected = new List<(long Sample, MidiEvent Event)>();
        options.MidiOutput = (at, e) => collected.Add((at, e));
        return rate =>
        {
            MidiFile.Write(path, collected, rate);
            Console.WriteLine($"{path}: {collected.Count} MIDI messages");
        };
    }

    private static WavSampleFormat BitsFrom(Arguments a) => (a.Option("--bits") ?? "f32") switch
    {
        "16" => WavSampleFormat.Int16,
        "24" => WavSampleFormat.Int24,
        "32" => WavSampleFormat.Int32,
        "f32" or "float" => WavSampleFormat.Float32,
        "f64" or "double" => WavSampleFormat.Float64,
        var b => throw new UsageException($"Unknown sample format '{b}'."),
    };

    private static int Process(Arguments a)
    {
        string input = a.Option("-i") ?? a.Option("--input") ?? throw new UsageException("process -i <in.wav> -o <out.wav> -p <plugin>");
        string output = a.Option("-o") ?? a.Option("--output") ?? throw new UsageException("process needs -o <out.wav>");
        var format = BitsFrom(a);
        var options = RenderOptionsFrom(a);
        string? midi = a.Option("--midi");
        var midiOut = MidiOutputFrom(a, options);
        using var loader = new PluginLoader(PluginCatalog.Load(HostPaths.CatalogFile), "pluginhost");
        var chain = LoadChain(a, loader);
        a.EnsureEmpty();
        var audio = WavFile.Read(input);
        if (midi is not null) options.Midi = MidiFile.Read(midi, audio.SampleRate);
        var result = OfflineRenderer.Render(chain, audio, options: options);
        if (!Console.IsErrorRedirected) Console.Error.WriteLine();
        WavFile.Write(output, result, format);
        midiOut?.Invoke(audio.SampleRate);
        SaveCatalogUsage(loader.Catalog);
        Console.WriteLine($"{output}: {result.ChannelCount} ch, {result.Duration:0.###} s at {result.SampleRate:0} Hz, peak {20 * Math.Log10(Math.Max(1e-12, result.Peak())):0.0} dBFS");
        return 0;
    }

    private static int Render(Arguments a)
    {
        string midi = a.Option("--midi") ?? throw new UsageException("render -p <instrument> --midi <file.mid> -o <out.wav>");
        string output = a.Option("-o") ?? a.Option("--output") ?? throw new UsageException("render needs -o <out.wav>");
        double rate = a.DoubleOption("--rate") ?? 48000;
        var format = BitsFrom(a);
        var options = RenderOptionsFrom(a);
        options.TailSeconds ??= 2.0;
        var midiOut = MidiOutputFrom(a, options);
        using var loader = new PluginLoader(PluginCatalog.Load(HostPaths.CatalogFile), "pluginhost");
        var chain = LoadChain(a, loader);
        a.EnsureEmpty();
        options.Midi = MidiFile.Read(midi, rate);
        var result = OfflineRenderer.Render(chain, null, rate, options);
        if (!Console.IsErrorRedirected) Console.Error.WriteLine();
        midiOut?.Invoke(rate);
        if (result.ChannelCount == 0)
        {
            // A MIDI effect alone has no audio output.
            SaveCatalogUsage(loader.Catalog);
            Console.WriteLine($"{chain[^1].Name} has no audio output: {output} was not written.");
            return 0;
        }
        WavFile.Write(output, result, format);
        SaveCatalogUsage(loader.Catalog);
        Console.WriteLine($"{output}: {result.ChannelCount} ch, {result.Duration:0.###} s, {options.Midi.Count} MIDI messages, peak {20 * Math.Log10(Math.Max(1e-12, result.Peak())):0.0} dBFS");
        return 0;
    }

    private static int Measure(Arguments a)
    {
        string output = a.Option("-o") ?? a.Option("--output") ?? throw new UsageException("measure -p <plugin> -o <response.csv>");
        string? irPath = a.Option("--ir");
        var options = new ResponseOptions
        {
            SampleRate = a.DoubleOption("--rate") ?? 48000,
            MinFrequency = a.DoubleOption("--min") ?? 20,
            MaxFrequency = a.DoubleOption("--max") ?? 20000,
            Points = a.IntOption("--points") ?? 200,
            LevelDb = a.DoubleOption("--level") ?? -20,
            InputChannel = a.IntOption("--channel") ?? 0,
            CompensateLatency = !a.Flag("--no-latency-compensation"),
            Method = (a.Option("--method") ?? "impulse") switch
            {
                "impulse" => MeasurementMethod.Impulse,
                "sine" or "sines" => MeasurementMethod.SteppedSine,
                var m => throw new UsageException($"Unknown method '{m}'."),
            },
            Decibels = (a.Option("--db") ?? "20log10") switch
            {
                "20log10" => DecibelConvention.TwentyLog10,
                "6log2" => DecibelConvention.SixLog2,
                var d => throw new UsageException($"Unknown dB convention '{d}'."),
            },
        };
        using var loader = new PluginLoader(PluginCatalog.Load(HostPaths.CatalogFile), "pluginhost");
        var chain = LoadChain(a, loader);
        a.EnsureEmpty();
        if (chain.Count != 1) throw new UsageException("measure takes one plug-in.");
        var response = ResponseMeasurer.Measure(chain[0], options);
        File.WriteAllText(output, response.ToCsv());
        if (irPath is not null)
        {
            if (response.ImpulseResponse is null) throw new UsageException("--ir needs --method impulse.");
            WavFile.Write(irPath, response.ImpulseResponse, WavSampleFormat.Float32);
        }
        SaveCatalogUsage(loader.Catalog);
        Console.WriteLine($"{output}: {response.Channels.Count} channels × {options.Points} frequencies ({options.Method}), latency {response.LatencySamples} samples");
        return 0;
    }

    private static void SaveCatalogUsage(PluginCatalog catalog)
    {
        try
        {
            if (catalog.Plugins.Count > 0) catalog.Save(HostPaths.CatalogFile);
        }
        catch (IOException)
        {
            // Usage statistics only.
        }
    }

    // ---------------------------------------------------------------- output

    private static void PrintTable(string[] headers, List<string[]> rows)
    {
        int[] widths = headers.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        for (int i = 0; i < widths.Length; i++) widths[i] = Math.Min(widths[i], i == 0 ? 40 : 28);
        string Line(string[] cells) => string.Join("  ", cells.Select((c, i) => (c.Length > widths[i] ? c[..(widths[i] - 1)] + "…" : c).PadRight(widths[i]))).TrimEnd();
        Console.WriteLine(Line(headers));
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in rows) Console.WriteLine(Line(row));
    }
}

/// <summary>Synchronous <see cref="IProgress{T}"/> (the console writes in order).</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

/// <summary>Wrong command line.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>Command-line arguments consumed in order (options can appear anywhere, except plug-in options which follow their -p).</summary>
internal sealed class Arguments(string[] args)
{
    private readonly List<string> _args = [.. args];

    /// <summary>Next positional argument (not starting with '-'), removed from the list.</summary>
    public string? Next()
    {
        for (int i = 0; i < _args.Count; i++)
        {
            if (_args[i].StartsWith('-') && _args[i].Length > 1 && !double.TryParse(_args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                if (TakesValue(_args[i])) i++;
                continue;
            }
            string value = _args[i];
            _args.RemoveAt(i);
            return value;
        }
        return null;
    }

    private static bool TakesValue(string option) => option is "-i" or "-o" or "-p" or "--input" or "--output" or "--plugin" or "--set" or "--set-normalized"
        or "--preset" or "--block" or "--tail" or "--bits" or "--sort" or "--config" or "--format" or "--midi" or "--midi-out" or "--rate" or "--min" or "--max"
        or "--points" or "--level" or "--channel" or "--db" or "--method" or "--ir";

    /// <summary>Value of the first occurrence of an option, removed from the list.</summary>
    public string? Option(string name)
    {
        int i = _args.IndexOf(name);
        if (i < 0) return null;
        if (i + 1 >= _args.Count) throw new UsageException($"{name} needs a value.");
        string value = _args[i + 1];
        _args.RemoveRange(i, 2);
        return value;
    }

    /// <summary>First of the given options (the earliest in the list) with its value, removed.</summary>
    public string? TakeOption(params string[] names)
    {
        for (int i = 0; i < _args.Count; i++)
        {
            if (!names.Contains(_args[i])) continue;
            if (i + 1 >= _args.Count) throw new UsageException($"{_args[i]} needs a value.");
            string value = _args[i + 1];
            _args.RemoveRange(i, 2);
            return value;
        }
        return null;
    }

    /// <summary>
    /// Next of <paramref name="options"/> found before the next occurrence of <paramref name="stopA"/> / <paramref name="stopB"/>
    /// (options belonging to the current plug-in), removed with its value.
    /// </summary>
    public (string Option, string Value)? TakeUntil(string stopA, string stopB, string[] options)
    {
        for (int i = 0; i < _args.Count; i++)
        {
            if (_args[i] == stopA || _args[i] == stopB) return null;
            if (!options.Contains(_args[i])) continue;
            if (i + 1 >= _args.Count) throw new UsageException($"{_args[i]} needs a value.");
            var result = (_args[i], _args[i + 1]);
            _args.RemoveRange(i, 2);
            return result;
        }
        return null;
    }

    public bool Flag(string name) => _args.Remove(name);

    public int? IntOption(string name) => Option(name) is { } v
        ? int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : throw new UsageException($"{name}: '{v}' is not an integer.")
        : null;

    public double? DoubleOption(string name) => Option(name) is { } v
        ? double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : throw new UsageException($"{name}: '{v}' is not a number.")
        : null;

    public void EnsureEmpty()
    {
        if (_args.Count > 0) throw new UsageException($"Unexpected argument(s): {string.Join(" ", _args)}");
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class CliJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
