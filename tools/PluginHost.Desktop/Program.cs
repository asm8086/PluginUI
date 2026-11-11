// 2026/11/11 15:14:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI;
using PluginUI.Desktop;
using PluginUI.Host;
using PluginUI.Host.Catalog;
using PluginUI.Host.Desktop;
using PluginUI.Host.Graph;
using PluginUI.Host.Windows;

/// <summary>Entry point.</summary>
internal static class Program
{
    // Usage:
    //   PluginHost [--config <folder>] [--lang en|fr]          the application
    //   PluginHost --snapshot <dir> [--lang en|fr]            renders the window with sample plug-ins to PNG files (docs)
    //   PluginHost scan-module --format vst3 --out <file> <module>   child process of a scan
    // [STAThread]: plug-ins, file dialogs and the clipboard need a single-threaded apartment.
    [STAThread]
    private static int Main(string[] args)
    {
        PluginUI.Host.Vst2.Vst2Hosting.Register(); // VST 2 plug-ins, in the host and in its scan processes
        int scan = Array.IndexOf(args, ModuleScanProcess.CommandName);
        if (scan >= 0) return ModuleScanProcess.Run(args[(scan + 1)..]);

        var arguments = new List<string>(args);
        string? language = TakeOption(arguments, "--lang");
        string? config = TakeOption(arguments, "--config");
        string? snapshot = TakeOption(arguments, "--snapshot");
        if (snapshot is not null) config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PluginUI", "PluginHost-snapshot");
        if (config is not null) HostPaths.ConfigFolder = Path.GetFullPath(config);

        var app = new DesktopApplication("Plug-in Host", company: "asm8086", dataFolder: HostPaths.ConfigFolder);
        var settings = AppSettings<GuiSettings>.Load(app.GetDataPath("gui.json"), GuiJson.Default.GuiSettings);
        language ??= settings.Value.Language;
        UIHost.DefaultLanguage = language;
        L.French = UIStrings.ForLanguage(language).LanguageCode.StartsWith("fr", StringComparison.OrdinalIgnoreCase);

        if (snapshot is not null)
        {
            settings.Value = new GuiSettings();
            var folders = new HostSettings
            {
                Folders =
                [
                    @"C:\Program Files\Common Files\VST3", @"C:\Users\me\AppData\Local\Programs\Common\VST3",
                    @"C:\Program Files\VSTPlugins", @"C:\Program Files\Steinberg\VSTPlugins", @"C:\Program Files\Common Files\VST2",
                ],
            };
            var demo = new MainWindow(app, settings, Snapshots.SampleCatalog(), folders, saveChanges: false);
            return Snapshots.Run(demo, snapshot);
        }

        var hostSettings = HostSettings.Load(HostPaths.SettingsFile);
        if (!File.Exists(HostPaths.SettingsFile)) hostSettings.Save(HostPaths.SettingsFile);
        var main = new MainWindow(app, settings, PluginCatalog.Load(HostPaths.CatalogFile), hostSettings, saveChanges: true);
        return app.Run(main.Window);
    }

    private static string? TakeOption(List<string> arguments, string name)
    {
        int i = arguments.IndexOf(name);
        if (i < 0) return null;
        string? value = i + 1 < arguments.Count ? arguments[i + 1] : null;
        arguments.RemoveRange(i, value is null ? 1 : 2);
        return value;
    }
}

/// <summary>Texts of the host in the interface language.</summary>
internal static class L
{
    public static bool French { get; set; }

    public static string T(string english, string french) => French ? french : english;
}

/// <summary>Offscreen rendering of the window for the documentation (no window is created, no plug-in is loaded).</summary>
internal static class Snapshots
{
    public static PluginCatalog SampleCatalog()
    {
        static PluginDescription P(string name, string vendor, string sub, int ins, int outs, int latency = 0, bool favorite = false, int uses = 0,
            int aux = 0, bool midi = false, PluginStatus status = PluginStatus.Ok, string error = "", int days = 30, string folder = @"C:\Program Files\Common Files\VST3",
            PluginFormat format = PluginFormat.Vst3, ModuleArchitecture architecture = ModuleArchitecture.X64)
            => new()
            {
                Format = format, Architecture = architecture, Name = name, Vendor = vendor, SubCategories = sub,
                Path = Path.Combine(folder, name + (format == PluginFormat.Vst3 ? ".vst3" : ".dll")),
                Category = sub.Contains("Instrument", StringComparison.Ordinal) ? "Instrument" : "Effect", InputChannels = ins, OutputChannels = outs,
                AuxInputChannels = aux, HasMidiInput = midi, LatencySamples = latency, IsFavorite = favorite, UseCount = uses, Status = status, Error = error,
                Version = "1.2.0", SdkVersion = format == PluginFormat.Vst3 ? "VST 3.7.12" : "VST 2.4", ParameterCount = 12 + name.Length, HasEditor = status == PluginStatus.Ok,
                ModuleTimeUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(-days),
                LastUsedUtc = uses > 0 ? new DateTime(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc).AddHours(-uses) : null,
                ClassId = (name + vendor).GetHashCode(StringComparison.Ordinal).ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
            };
        const string ours = @"C:\Program Files\VSTPlugins";
        return new PluginCatalog
        {
            Plugins =
            [
                P("6B-EQ", "Particles", "Fx|EQ", 2, 2, favorite: true, uses: 14, folder: ours),
                P("MiddleAgeVerb Stereo", "Particles", "Fx|Reverb", 2, 2, favorite: true, uses: 9, folder: ours),
                P("MiddleAgeVerb Mono-Stereo", "Particles", "Fx|Reverb", 1, 2, uses: 2, folder: ours),
                P("MultibandSplitter", "Particles", "Fx|Tools", 2, 12, uses: 5, folder: ours),
                P("Glass Delay", "Example Audio", "Fx|Delay", 2, 2, days: 200),
                P("Glass Compressor", "Example Audio", "Fx|Dynamics", 2, 2, latency: 64, aux: 2, days: 200),
                P("Granite Limiter", "Example Audio", "Fx|Dynamics|Mastering", 2, 2, latency: 1024, days: 120, uses: 1),
                P("Oak Organ", "Woodworks", "Instrument|Synth", 0, 8, midi: true, days: 60, uses: 3),
                P("Felt Piano", "Woodworks", "Instrument|Piano", 0, 2, midi: true, days: 60, favorite: true, uses: 7),
                P("Spectral Cleaner", "Restoration Lab", "Fx|Restoration", 2, 2, latency: 2047, days: 400),
                P("Breath Tamer", "Restoration Lab", "Fx|Restoration", 2, 2, latency: 26623, days: 400),
                P("Mono Analyzer", "Measure Co", "Fx|Analyzer", 1, 1, days: 10),
                P("Old Shell", "Unknown", "", 0, 0, status: PluginStatus.Failed, error: "The scanner stopped (code 0xC0000005: access violation in the plug-in)."),
                P("Tape Chorus", "Vintage Bits", "Fx|Modulation", 2, 2, days: 3000, uses: 4, folder: @"C:\Program Files (x86)\VSTPlugins",
                    format: PluginFormat.Vst2, architecture: ModuleArchitecture.X86),
                P("Old Phaser", "", "", 0, 0, status: PluginStatus.Incompatible, error: ModuleArchitectures.IncompatibilityMessage(ModuleArchitecture.X86),
                    days: 3200, folder: @"C:\Program Files (x86)\Common Files\VST3", architecture: ModuleArchitecture.X86),
            ],
            LastScanUtc = new DateTime(2026, 11, 6, 21, 30, 0, DateTimeKind.Utc),
        };
    }

    public static int Run(MainWindow main, string dir)
    {
        Directory.CreateDirectory(dir);
        var host = main.Host;
        string suffix = L.French ? "-fr" : "";
        main.Table.Sort(main.Table.Columns.First(c => c.Key == "vendor"));
        main.SelectByName("MiddleAgeVerb Stereo");
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-main" + suffix + ".png"), 1f);

        main.ShowFoldersDialog();
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-folders" + suffix + ".png"), 1f);
        host.CloseAllPopups();

        main.ShowAudioDialog();
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-audio" + suffix + ".png"), 1f);
        host.CloseAllPopups();

        main.ShowAudioDialog("asio", [new AsioDriverInfo("Example Audio ASIO", Guid.Empty)]);
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-asio" + suffix + ".png"), 1f);
        host.CloseAllPopups();

        main.MenuBar.OpenMenu(2); // View: details, full screen (F11)…
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-view" + suffix + ".png"), 1f);
        main.MenuBar.CloseMenu();
        host.CloseAllPopups();

        host.ShowMenu(main.Table.GetColumnMenuItems(), new Point(420f, 130f));
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-columns" + suffix + ".png"), 1f);
        host.CloseAllPopups();

        // The graph tab with a small patch of sample plug-ins (stand-ins: nothing is loaded).
        BuildSampleGraph(main.Graph.Graph);
        main.Tabs.SelectedIndex = 1;
        Settle(host);
        main.Graph.Sync();
        main.Graph.SnapToConnectors();
        main.Graph.Editor.ZoomToFit();
        main.Graph.OutputMeter.SetLevels(0.42f, 0.38f);
        var compressor = main.Graph.Graph.Nodes.First(n => n.DisplayTitle == "Glass Compressor");
        main.Graph.Editor.Select(main.Graph.Editor.Nodes.Where(n => ReferenceEquals(n.Tag, compressor)));
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-graph" + suffix + ".png"), 1f);

        var view = main.Graph.Editor.Nodes.First(n => ReferenceEquals(n.Tag, compressor));
        var at = main.Graph.Editor.FromGraph(new Point(view.X + 40f, view.Y + 10f));
        host.DispatchMouseDown(at, MouseButton.Right, 1, ModifierKeys.None);
        host.DispatchMouseUp(at, MouseButton.Right, ModifierKeys.None);
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-graph-menu" + suffix + ".png"), 1f);
        host.CloseAllPopups();

        // The log, with what a session writes there.
        main.Log.Info("Glass Compressor", L.T(@"Loaded (C:\Program Files\Common Files\VST3\Example Audio\Glass Compressor.vst3), latency 64 samples",
            @"Chargé (C:\Program Files\Common Files\VST3\Example Audio\Glass Compressor.vst3), latence 64 échantillons"));
        main.Log.Info("Felt Piano", L.T(@"Loaded (C:\Program Files\Common Files\VST3\Woodworks\Felt Piano.vst3)", @"Chargé (C:\Program Files\Common Files\VST3\Woodworks\Felt Piano.vst3)"));
        main.Log.Error("Old Tape", L.T(@"Could not be loaded (C:\Program Files\Common Files\VST3\Old Tape.vst3): the module could not be loaded",
            @"N'a pas pu être chargé (C:\Program Files\Common Files\VST3\Old Tape.vst3) : le module n'a pas pu être chargé"));
        main.Log.Warning(L.T("Graph", "Graphe"), L.T("Plug-in 'Glass Delay': its saved settings could not be restored; it starts with its default settings.",
            "Plug-in « Glass Delay » : ses réglages enregistrés n'ont pas pu être restaurés ; il démarre avec ses réglages par défaut."));
        main.Tabs.SelectedIndex = 2;
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "pluginhost-log" + suffix + ".png"), 1f);

        // Windows of modules, rendered alone (no window is created).
        foreach (var (module, name) in new (GraphNode, string)[] { (main.Graph.Graph.Nodes.OfType<GainNode>().First(), "pluginhost-gain"), (new KeyboardNode(), "pluginhost-keyboard"), (new MidiTransposeNode { Semitones = 12 }, "pluginhost-transpose"), (new MidiFilterNode { ChannelMask = 0x00FF, Kinds = MidiMessageKinds.All & ~MidiMessageKinds.ProgramChange, LowestNote = 36, HighestNote = 59 }, "pluginhost-midi-filter") })
        {
            module.Meter.Read(new float[module.Meter.ChannelCount]);
            var (content, size, meter) = ModuleWindow.Build(module, () => host, () => [0.35f, 0.28f], () => { });
            using var window = new UIHost(content, size, SizingMode.Relayout);
            meter?.SetLevels(0.35f, 0.28f);
            Settle(window);
            window.RenderToPng(Path.Combine(dir, name + suffix + ".png"), 1f);
        }

        Console.WriteLine($"Snapshots written to {Path.GetFullPath(dir)}");
        return 0;
    }

    private static void BuildSampleGraph(PluginGraph graph)
    {
        graph.Clear();
        T Add<T>(T node, float x, float y) where T : GraphNode
        {
            node.X = x;
            node.Y = y;
            return graph.Add(node);
        }
        var input = Add(new AudioInputNode(), 32, 48);
        var tone = Add(new TestSignalNode { Generator = { Signal = PluginUI.Host.Audio.TestSignal.PinkNoise, LevelDb = -18 } }, 32, 160);
        var compressor = Add(new PluginNode(new SnapshotPlugin("Glass Compressor", "Example Audio", latency: 64, sideChain: true)), 272, 32);
        var delay = Add(new PluginNode(new SnapshotPlugin("Glass Delay", "Example Audio")), 272, 192);
        var feedback = Add(new GainNode { GainDb = -6 }, 512, 272);
        var loop = Add(new AudioSumNode { Title = L.T("Delay feedback", "Retour de l'écho") }, 272, 320);
        var midi = Add(new MidiInputNode(), 32, 448);
        var piano = Add(new PluginNode(new SnapshotPlugin("Felt Piano", "Woodworks", instrument: true)), 272, 432);
        var sum = Add(new AudioSumNode { InputCount = 4 }, 752, 48);
        var output = Add(new AudioOutputNode(), 992, 48);
        void Connect(GraphNode from, string outKey, GraphNode to, string inKey) => graph.Connect(from.FindOutput(outKey)!, to.FindInput(inKey)!);
        Connect(input, "out", compressor, "in0");
        Connect(tone, "out", compressor, "in1");
        Connect(compressor, "out0", sum, "in1");
        Connect(input, "out", sum, "in2");
        Connect(input, "out", loop, "in1");
        Connect(loop, "out", delay, "in0");
        Connect(delay, "out0", feedback, "in");
        Connect(feedback, "out", loop, "in2");
        Connect(delay, "out0", sum, "in3");
        Connect(midi, "midi", piano, "midi-in");
        Connect(piano, "out0", sum, "in4");
        Connect(sum, "out", output, "in");
        ((PluginNode)delay).Bypass = false;
        ((PluginNode)piano).Mute = false;
    }

    /// <summary>A stand-in plug-in for the snapshots: buses and a name, silent.</summary>
    private sealed class SnapshotPlugin(string name, string vendor, int latency = 0, bool sideChain = false, bool instrument = false) : PluginInstance
    {
        public override string Name => name;

        public override string Vendor => vendor;

        public override PluginFormat Format => PluginFormat.Vst3;

        public override bool IsInstrument => instrument;

        public override IReadOnlyList<PluginBus> InputBuses { get; } = instrument ? [] : sideChain
            ? [new("Input", 2, false, true), new("Sidechain", 2, true, true)] : [new("Input", 2, false, true)];

        public override IReadOnlyList<PluginBus> OutputBuses { get; } = [new("Output", 2, false, true)];

        public override int EventInputCount => instrument ? 1 : 0;

        public override IReadOnlyList<PluginParameter> Parameters => [];

        public override int LatencySamples => latency;

        public override int TailSamples => 0;

        public override bool HasEditor => false;

        public override void AddMidiEvent(in MidiEvent e)
        {
        }

        public override double GetParameter(uint id) => 0;

        protected override void OnSetParameterUi(uint id, double normalized)
        {
        }

        public override string FormatParameter(uint id, double normalized) => string.Empty;

        public override bool TryParseParameter(uint id, string text, out double normalized)
        {
            normalized = 0;
            return false;
        }

        public override byte[] GetState() => [];

        public override void SetState(ReadOnlySpan<byte> state)
        {
        }

        protected override void OnPrepare(double sampleRate, int maxBlockSize, bool realtime)
        {
        }

        protected override void OnStop()
        {
        }

        protected override void OnProcess(int frames, ParameterQueue pending)
        {
            for (int c = 0; c < OutputChannelCount; c++) GetOutput(c)[..frames].Clear();
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    private static void Settle(UIHost host)
    {
        host.UpdateLayout();
        for (int i = 0; i < 10; i++) host.StepFrame(1.0 / 60.0);
        host.ProcessPostedActions();
        host.UpdateLayout();
    }
}
