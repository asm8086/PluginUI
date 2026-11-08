// 2026/11/08 13:55:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Desktop;
using PluginUI.Host.Audio;
using PluginUI.Host.Catalog;
using PluginUI.Host.Graph;

namespace PluginUI.Host.Desktop;

/// <summary>
/// The Graph tab: the modules of the <see cref="PluginGraph"/> in a <see cref="NodeEditor"/>, their windows (plug-in
/// editors, properties of the other modules), the graph files, and the bar above the view.
/// </summary>
internal sealed class GraphView : ITickable
{
    private readonly UIHost _host;
    private readonly StandaloneWindow _window;
    private readonly AudioEngine _audio;
    private readonly PluginLoader _loader;
    private readonly Func<bool> _lightTheme;
    private readonly Dictionary<GraphNode, NodeEditorNode> _views = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GraphNode, float[]> _levels = new(ReferenceEqualityComparer.Instance);
    private readonly List<PluginWindow> _windows = [];
    private readonly List<ModuleWindow> _moduleWindows = [];
    private readonly DropDown _loop;
    private readonly Label _info;
    private readonly Button _zoomOut, _zoomIn, _zoomReset;
    private bool _syncing;

    private static readonly int[] LoopSizes = [0, 16, 32, 64, 128];

    public GraphView(UIHost host, StandaloneWindow window, AudioEngine audio, PluginLoader loader, Func<bool> lightTheme)
    {
        _host = host;
        _window = window;
        _audio = audio;
        _loader = loader;
        _lightTheme = lightTheme;

        Editor = new NodeEditor
        {
            AutomationName = L.T("Graph", "Graphe"),
            EmptyText = L.T("Right-click to add a module, or double-click a plug-in in the Plug-ins tab.",
                "Clic droit pour ajouter un module, ou double-clic sur un plug-in dans l'onglet Plug-ins."),
            Margin = new Thickness(10f, 0f, 10f, 6f),
        };
        Editor.CanConnect = (o, i) => o.Tag is GraphPort output && i.Tag is GraphPort input && Graph.WhyNotConnect(output, input) is null;
        Editor.CableRequested += (_, o, i) => Edit(() => Graph.Connect((GraphPort)o.Tag!, (GraphPort)i.Tag!));
        Editor.CableRemoveRequested += (_, c) => Edit(() => Graph.Disconnect((GraphConnection)c.Tag!));
        Editor.NodesMoved += (_, nodes) =>
        {
            foreach (var n in nodes)
            {
                var g = (GraphNode)n.Tag!;
                g.X = n.X;
                g.Y = n.Y;
            }
            IsModified = true;
        };
        Editor.NodeOpened += (_, n) => Open((GraphNode)n.Tag!);
        Editor.DeleteRequested += (_, nodes, _) =>
        {
            if (nodes.Count > 0) ConfirmRemove([.. nodes.Select(n => (GraphNode)n.Tag!)]);
        };
        Editor.ContextMenuOpening += (_, e) => FillMenu(e);

        _loop = new DropDown([L.T("Loops: one block of delay", "Boucles : un bloc de retard"), .. LoopSizes.Skip(1).Select(n => L.T($"Loops: {n} samples", $"Boucles : {n} échantillons"))])
        {
            Label = string.Empty, Width = 230f, Height = 26f,
            ToolTip = L.T("Delay of the cable that closes a loop: the device block, or shorter sub-blocks for the modules of the loop (more processor time)",
                "Retard du câble qui ferme une boucle : le bloc du périphérique, ou des sous-blocs plus courts pour les modules de la boucle (plus de calcul)"),
        };
        _loop.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            Graph.LoopBlockSize = LoopSizes[Math.Max(0, _loop.SelectedIndex)];
            IsModified = true;
        };
        var add = new MenuButton(MenuButtonStyle.TextWithChevron) { Text = L.T("Add a module", "Ajouter un module"), Width = 170f, Height = 26f };
        add.Items = AddModuleItems(null);
        var fit = new Button(L.T("Show all", "Tout afficher"), Editor.ZoomToFit) { Width = 110f, Height = 26f, ToolTip = L.T("Fits the graph in the view (Home)", "Ajuste le graphe à la vue (Origine)") };
        _zoomOut = new Button("−", Editor.ZoomOut) { Width = 28f, Height = 26f, ToolTip = L.T("Zoom out (−, Ctrl+wheel)", "Dézoomer (−, Ctrl+molette)") };
        _zoomReset = new Button("100 %", Editor.ResetZoom) { Width = 58f, Height = 26f, ToolTip = L.T("The zoom; click for the actual size (Ctrl+0)", "Le zoom ; clic pour la taille réelle (Ctrl+0)") };
        _zoomIn = new Button("+", Editor.ZoomIn) { Width = 28f, Height = 26f, ToolTip = L.T("Zoom in (+, Ctrl+wheel)", "Zoomer (+, Ctrl+molette)") };
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2f };
        zoom.Add(_zoomOut, _zoomReset, _zoomIn);
        _info = new Label { TextSource = InfoText, IsDim = true, VerticalAlignment = VerticalAlignment.Center };
        OutputMeter = new LevelMeter
        {
            Orientation = Orientation.Horizontal, ChannelCount = 2, Width = 204f, Height = 14f, ShowScale = false, MinDb = -60f, Dock = Side.Right, ShowPeakValue = true,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("Output level of the audio device; on the right the highest peak (click to clear)", "Niveau de sortie du périphérique audio ; à droite la crête la plus haute (clic pour effacer)"),
        };
        var bar = new DockPanel { Dock = Side.Top, Height = 40f, Padding = new Thickness(10f, 7f, 10f, 7f) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8f, Dock = Side.Left };
        buttons.Add(add, fit, zoom, _loop);
        bar.Children.Add(buttons);
        bar.Children.Add(OutputMeter);
        bar.Children.Add(_info);
        Root = new DockPanel();
        Root.Children.Add(bar);
        Root.Children.Add(Editor);

        Graph.Changed += () => _host.Post(Sync);
        Graph.NodeFailed += (node, ex) => _host.Post(() =>
        {
            Message?.Invoke(L.T($"{node.DisplayTitle} stopped processing: {ex.Message}", $"{node.DisplayTitle} a cessé de traiter : {ex.Message}"));
            Sync();
        });
        host.AddTicker(this);
    }

    public PluginGraph Graph => _audio.Graph;

    public NodeEditor Editor { get; }

    public DockPanel Root { get; }

    /// <summary>The meter of the bar (fed by the main window with the device output).</summary>
    public LevelMeter OutputMeter { get; }

    /// <summary>The graph file being edited (null = not saved yet).</summary>
    public string? FilePath { get; private set; }

    /// <summary>Changed since it was opened or saved.</summary>
    public bool IsModified { get; private set; }

    /// <summary>A message for the status bar (the main window also writes it in the log).</summary>
    public event Action<string>? Message;

    /// <summary>The log of the session: plug-ins loaded or refused, graph files.</summary>
    public HostLog? Log { get; set; }

    private static string Source => L.T("Graph", "Graphe");

    /// <summary>Asks to show the plug-in list (to add a plug-in).</summary>
    public event Action? PluginListRequested;

    private void Edit(Action edit)
    {
        try
        {
            edit();
            IsModified = true;
        }
        catch (ArgumentException ex)
        {
            Message?.Invoke(ex.Message);
        }
    }

    // ---------------------------------------------------------------- texts

    private string InfoText()
    {
        int latency = Graph.Nodes.OfType<AudioOutputNode>().Select(n => n.PathLatencySamples).DefaultIfEmpty(0).Max();
        string modules = L.T($"{Graph.Nodes.Count} modules", $"{Graph.Nodes.Count} modules");
        if (latency <= 0 || Graph.SampleRate <= 0) return modules;
        string ms = (latency * 1000.0 / Graph.SampleRate).ToString("0.0", CultureInfo.InvariantCulture);
        return modules + L.T($" · latency to the outputs {latency} samples ({ms} ms)", $" · latence jusqu'aux sorties {latency} échantillons ({ms} ms)");
    }

    internal static string TypeName(GraphNode node) => node switch
    {
        PluginNode or MissingPluginNode => L.T("Plug-in", "Plug-in"),
        AudioInputNode => L.T("Audio input", "Entrée audio"),
        AudioOutputNode => L.T("Audio output", "Sortie audio"),
        TestSignalNode => L.T("Test signal", "Signal de test"),
        AudioFileNode => L.T("Audio file", "Fichier audio"),
        MidiInputNode => L.T("MIDI input", "Entrée MIDI"),
        KeyboardNode => L.T("Keyboard", "Clavier"),
        MidiOutputNode => L.T("MIDI output", "Sortie MIDI"),
        GainNode => "Gain",
        AudioSumNode => L.T("Audio sum", "Somme audio"),
        MidiMergeNode => L.T("MIDI merge", "Fusion MIDI"),
        MuxNode => "Muxer",
        DemuxNode => "Demuxer",
        _ => node.TypeKey,
    };

    /// <summary>The title shown: the user's, else the plug-in name, else the module type in the interface language.</summary>
    private static string Title(GraphNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Title)) return node.Title;
        return node switch
        {
            AudioInputNode i => L.T("Audio input ", "Entrée audio ") + Channels(i.FirstChannel, i.Channels),
            AudioOutputNode o => L.T("Audio output ", "Sortie audio ") + Channels(o.FirstChannel, o.Channels),
            AudioFileNode { Source: not null } f => f.DefaultTitle,
            PluginNode or MissingPluginNode => node.DefaultTitle,
            _ => TypeName(node),
        };
    }

    private static string Channels(int first, int count)
        => count == 1 ? (first + 1).ToString(CultureInfo.InvariantCulture) : $"{first + 1}-{first + count}";

    private static string Db(double db) => (db > 0 ? "+" : db < 0 ? "−" : "") + Math.Abs(db).ToString("0.0", CultureInfo.InvariantCulture) + " dB";

    private static string? Subtitle(GraphNode node) => node switch
    {
        PluginNode p => p.Plugin.Vendor + (p.Plugin.LatencySamples > 0 ? L.T($" · latency {p.Plugin.LatencySamples}", $" · latence {p.Plugin.LatencySamples}") : string.Empty),
        MissingPluginNode m => m.Reference?.Vendor,
        GainNode g => Db(g.GainDb) + (g.Invert ? L.T(" · inverted", " · inversé") : string.Empty),
        TestSignalNode t => SignalName(t.Generator.Signal) + " · " + t.Generator.LevelDb.ToString("0.#", CultureInfo.InvariantCulture) + " dBFS",
        AudioFileNode f => f.Source is null ? L.T("no file", "aucun fichier") : f.Loop ? L.T("loop", "en boucle") : null,
        AudioSumNode s => s.CompensateLatency ? null : L.T("no latency compensation", "sans compensation de latence"),
        MidiInputNode m => m.ChannelFilter == 0 ? null : L.T($"channel {m.ChannelFilter}", $"canal {m.ChannelFilter}"),
        KeyboardNode k => L.T($"channel {k.Channel}", $"canal {k.Channel}"),
        _ => null,
    };

    internal static string SignalName(TestSignal signal) => signal switch
    {
        TestSignal.Sine => L.T("Sine", "Sinus"),
        TestSignal.Square => L.T("Square", "Carré"),
        TestSignal.Sawtooth => L.T("Sawtooth", "Dent de scie"),
        TestSignal.Triangle => L.T("Triangle", "Triangle"),
        TestSignal.WhiteNoise => L.T("White noise", "Bruit blanc"),
        TestSignal.PinkNoise => L.T("Pink noise", "Bruit rose"),
        TestSignal.Sweep => L.T("Sweep", "Balayage"),
        _ => L.T("Impulses", "Impulsions"),
    };

    // ---------------------------------------------------------------- view of the graph

    /// <summary>Updates the editor from the graph (modules, ports, cables, states).</summary>
    public void Sync()
    {
        _syncing = true;
        try
        {
            _loop.SelectedIndex = Math.Max(0, Array.IndexOf(LoopSizes, Graph.LoopBlockSize));
        }
        finally
        {
            _syncing = false;
        }
        var alive = new HashSet<GraphNode>(Graph.Nodes, ReferenceEqualityComparer.Instance);
        foreach (var gone in _views.Keys.Where(n => !alive.Contains(n)).ToList())
        {
            Editor.Nodes.Remove(_views[gone]);
            _views.Remove(gone);
            _levels.Remove(gone);
        }
        var ports = new Dictionary<GraphPort, NodeEditorPort>(ReferenceEqualityComparer.Instance);
        foreach (var node in Graph.Nodes)
        {
            if (!_views.TryGetValue(node, out var view))
            {
                view = new NodeEditorNode(Title(node)) { Tag = node };
                var captured = node;
                if (node is not (MidiInputNode or KeyboardNode or MidiOutputNode or MidiMergeNode or MissingPluginNode))
                    view.Level = () => _levels.TryGetValue(captured, out var l) && l.Length > 0 ? l.Max() : 0f;
                _views[node] = view;
                Editor.Nodes.Add(view);
            }
            view.Title = Title(node);
            view.Subtitle = Subtitle(node);
            view.X = node.X;
            view.Y = node.Y;
            view.HasError = node.HasFailed;
            view.IsDimmed = node is MissingPluginNode || node is PluginNode { Bypass: true } || node is PluginNode { Mute: true };
            view.Badge = node switch
            {
                _ when node.HasFailed => L.T("FAILED", "ÉCHEC"),
                MissingPluginNode => L.T("MISSING", "ABSENT"),
                PluginNode { Bypass: true } => L.T("BYPASS", "CONTOURNÉ"),
                PluginNode { Mute: true } or GainNode { Mute: true } or AudioOutputNode { Mute: true } => L.T("MUTE", "MUET"),
                _ => null,
            };
            view.ToolTip = node switch
            {
                MissingPluginNode m => L.T("Could not be loaded: ", "N'a pas pu être chargé : ") + m.Reason,
                _ when node.HasFailed => node.Failure?.Message,
                _ => TypeName(node) + (node.PathLatencySamples > 0 ? L.T($" · latency from the sources {node.PathLatencySamples} samples", $" · latence depuis les sources {node.PathLatencySamples} échantillons") : string.Empty),
            };
            if (!SamePorts(view, node))
            {
                view.SetPorts(node.Inputs.Select(Port), node.Outputs.Select(Port));
            }
            else
            {
                for (int i = 0; i < node.Inputs.Count; i++) view.Inputs[i].Tag = node.Inputs[i];
                for (int i = 0; i < node.Outputs.Count; i++) view.Outputs[i].Tag = node.Outputs[i];
            }
            for (int i = 0; i < node.Inputs.Count; i++) ports[node.Inputs[i]] = view.Inputs[i];
            for (int i = 0; i < node.Outputs.Count; i++) ports[node.Outputs[i]] = view.Outputs[i];
        }
        var selectedCable = Editor.SelectedCable?.Tag;
        Editor.Cables.Clear();
        foreach (var c in Graph.Connections)
        {
            if (c.SourcePort is not { } from || c.TargetPort is not { } to || !ports.TryGetValue(from, out var o) || !ports.TryGetValue(to, out var i)) continue;
            var cable = new NodeEditorCable(o, i) { Tag = c, IsFeedback = c.IsFeedback };
            if (c.IsFeedback) cable.Label = c.DelaySamples == Graph.MaxFrames || c.DelaySamples == 0 ? L.T("1 block", "1 bloc") : L.T($"{c.DelaySamples} samples", $"{c.DelaySamples} éch.");
            else if (c.DelaySamples > 0) cable.Label = "+" + c.DelaySamples.ToString(CultureInfo.InvariantCulture);
            Editor.Cables.Add(cable);
            if (ReferenceEquals(c, selectedCable)) Editor.SelectCable(cable);
        }
        foreach (var w in _windows) w.RefreshState();
        Editor.InvalidateVisual();
        _info.InvalidateVisual();
    }

    private static NodeEditorPort Port(GraphPort p)
        => new(p.Key, PortName(p), p.Kind == PortKind.Midi ? "midi" : "audio", p.Kind == PortKind.Audio ? p.Channels : 0) { Tag = p };

    private static string PortName(GraphPort p) => p.Name switch
    {
        "In" => L.T("In", "Entrée"),
        "Out" => L.T("Out", "Sortie"),
        _ when p.Name.StartsWith("In ", StringComparison.Ordinal) => L.T("In", "Entrée") + p.Name[2..],
        _ when p.Name.StartsWith("Ch ", StringComparison.Ordinal) => L.T("Ch", "Can.") + p.Name[2..],
        _ => p.Name,
    };

    private static bool SamePorts(NodeEditorNode view, GraphNode node)
    {
        if (view.Inputs.Count != node.Inputs.Count || view.Outputs.Count != node.Outputs.Count) return false;
        for (int i = 0; i < node.Inputs.Count; i++)
        {
            if (view.Inputs[i].Key != node.Inputs[i].Key || view.Inputs[i].Channels != (node.Inputs[i].Kind == PortKind.Audio ? node.Inputs[i].Channels : 0)) return false;
        }
        for (int i = 0; i < node.Outputs.Count; i++)
        {
            if (view.Outputs[i].Key != node.Outputs[i].Key || view.Outputs[i].Channels != (node.Outputs[i].Kind == PortKind.Audio ? node.Outputs[i].Channels : 0)) return false;
        }
        return true;
    }

    /// <summary>Reads the meters, and lets the plug-ins deliver their own changes (UI thread, every frame).</summary>
    bool ITickable.Tick(double elapsedSeconds)
    {
        string zoomText = MathF.Round(Editor.Zoom * 100f).ToString(CultureInfo.InvariantCulture) + " %";
        if (_zoomReset.Text != zoomText) _zoomReset.Text = zoomText;
        if (_zoomIn.IsEnabled != Editor.CanZoomIn) _zoomIn.IsEnabled = Editor.CanZoomIn;
        if (_zoomOut.IsEnabled != Editor.CanZoomOut) _zoomOut.IsEnabled = Editor.CanZoomOut;
        foreach (var node in Graph.Nodes)
        {
            if (node is PluginNode p) p.Plugin.Idle();
            int channels = node.Meter.ChannelCount;
            if (channels == 0) continue;
            if (!_levels.TryGetValue(node, out var levels) || levels.Length != channels) _levels[node] = levels = new float[channels];
            node.Meter.Read(levels);
        }
        return true;
    }

    /// <summary>The levels of a module's meter (read once per frame).</summary>
    public float[] LevelsOf(GraphNode node) => _levels.TryGetValue(node, out var l) ? l : [];

    // ---------------------------------------------------------------- modules

    /// <summary>Loads a plug-in of the list as a module of the graph, connected to the MIDI input and to a free audio output.</summary>
    public PluginNode? AddPlugin(PluginDescription description)
    {
        PluginInstance instance;
        try
        {
            instance = _loader.Load(description);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log?.Error(description.Name, L.T($"Could not be opened ({description.Path}): {ex.Message}", $"N'a pas pu être ouvert ({description.Path}) : {ex.Message}"));
            MessageDialog.Show(_window, _window.Title, L.T($"{description.Name} could not be opened:\n{ex.Message}", $"{description.Name} n'a pas pu être ouvert :\n{ex.Message}"));
            return null;
        }
        PluginNode node;
        try
        {
            node = new PluginNode(instance, PluginReference.From(description));
            Place(node, null);
            Graph.Add(node);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            instance.Dispose();
            Log?.Error(description.Name, L.T($"Could not be started: {ex.Message}", $"N'a pas pu démarrer : {ex.Message}"));
            MessageDialog.Show(_window, _window.Title, L.T($"{description.Name} could not be started:\n{ex.Message}", $"{description.Name} n'a pas pu démarrer :\n{ex.Message}"));
            return null;
        }
        Log?.Info(description.Name, L.T($"Loaded ({description.Path})", $"Chargé ({description.Path})") + LatencyText(node));
        // The usual connections: notes from the MIDI input, sound to a free output.
        if (node.FindInput("midi-in") is { } midiIn && Graph.Nodes.OfType<MidiInputNode>().FirstOrDefault() is { } port)
            Graph.Connect(port.Outputs[0], midiIn);
        var mainOut = node.Outputs.FirstOrDefault(p => p.Kind == PortKind.Audio);
        var freeOutput = Graph.Nodes.OfType<AudioOutputNode>().FirstOrDefault(o => Graph.ConnectionTo(o.Inputs[0]) is null);
        if (mainOut is not null && freeOutput is not null) Graph.Connect(mainOut, freeOutput.Inputs[0]);
        IsModified = true;
        Sync();
        SnapToConnectors(node);
        if (_views.TryGetValue(node, out var view))
        {
            Editor.Select([view]);
            Editor.ScrollIntoView(view);
        }
        return node;
    }

    /// <summary>Adds a module (at <paramref name="at"/> in graph units, else in a free place near the center).</summary>
    public void AddModule(GraphNode node, Point? at)
    {
        if (node is AudioFileNode file && !ChooseFile(file)) return;
        Place(node, at);
        Graph.Add(node);
        IsModified = true;
        Sync();
        SnapToConnectors(node);
        if (_views.TryGetValue(node, out var view)) Editor.Select([view]);
    }

    /// <summary>Snaps modules on the grid by their first connector (all modules when none is given), so that the cables between aligned modules are straight.</summary>
    public void SnapToConnectors(params GraphNode[] nodes)
    {
        foreach (var node in nodes.Length == 0 ? Graph.Nodes : nodes)
        {
            if (!_views.TryGetValue(node, out var view)) continue;
            Editor.Snap(view);
            node.X = view.X;
            node.Y = view.Y;
        }
    }

    private void Place(GraphNode node, Point? at)
    {
        var start = at ?? Editor.ViewCenter;
        float x = MathF.Round(start.X / 16f) * 16f, y = MathF.Round(start.Y / 16f) * 16f;
        if (at is null)
        {
            var taken = Editor.Nodes.Select(Editor.NodeBounds).ToList();
            for (int attempt = 0; attempt < 40 && taken.Any(r => r.Inflate(12f).Intersects(new Rect(x, y, 180f, 90f))); attempt++)
            {
                y += 48f;
                if (attempt % 8 == 7)
                {
                    y = MathF.Round(start.Y / 16f) * 16f;
                    x += 208f;
                }
            }
        }
        node.X = x;
        node.Y = y;
    }

    private bool ChooseFile(AudioFileNode node)
    {
        if (FileDialogs.OpenFile(_host, L.T("Play an audio file", "Lire un fichier audio"), [new(L.T("WAV files", "Fichiers WAV"), "*.wav")]) is not { } path) return false;
        try
        {
            node.Open(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or NotSupportedException)
        {
            MessageDialog.Show(_window, _window.Title, L.T("The file could not be read:\n", "Le fichier n'a pas pu être lu :\n") + ex.Message);
            return false;
        }
    }

    private void ConfirmRemove(IReadOnlyList<GraphNode> nodes)
    {
        if (!nodes.Any(n => n is PluginNode))
        {
            Remove(nodes);
            return;
        }
        string what = nodes.Count == 1 ? Title(nodes[0]) : L.T($"{nodes.Count} modules", $"{nodes.Count} modules");
        Dialog.ShowMessage(_host, L.T("Remove", "Supprimer"),
            L.T($"Remove {what}? The settings of its plug-ins will be lost.", $"Supprimer {what} ? Les réglages de ses plug-ins seront perdus."),
            MessageBoxButtons.YesNo, result =>
            {
                if (result == MessageBoxResult.Yes) Remove(nodes);
            });
    }

    /// <summary>Removes modules: their windows close, their plug-ins are released.</summary>
    public void Remove(IEnumerable<GraphNode> nodes)
    {
        foreach (var node in nodes.ToList())
        {
            if (node is PluginNode p && _windows.Find(w => ReferenceEquals(w.Node, p)) is { } window) window.Window.Close();
            if (_moduleWindows.Find(w => ReferenceEquals(w.Node, node)) is { } moduleWindow) moduleWindow.Window.Close();
            Graph.Remove(node);
            if (node is PluginNode plugin) plugin.Plugin.Dispose();
        }
        IsModified = true;
        Sync();
    }

    /// <summary>Opens a module: the window of a plug-in, or the window of the module's settings.</summary>
    public void Open(GraphNode node)
    {
        switch (node)
        {
            case PluginNode plugin:
                OpenPlugin(plugin);
                break;
            case MissingPluginNode missing:
                MessageDialog.Show(_window, _window.Title, L.T($"{missing.DisplayTitle} could not be loaded:\n{missing.Reason}\n\nIt stays silent; its cables and settings are kept in the graph file.",
                    $"{missing.DisplayTitle} n'a pas pu être chargé :\n{missing.Reason}\n\nIl reste muet ; ses câbles et ses réglages sont gardés dans le fichier du graphe."));
                break;
            default:
                OpenModule(node);
                break;
        }
    }

    /// <summary>Opens the window of a module that is not a plug-in (not modal: it stays open while the graph plays).</summary>
    private void OpenModule(GraphNode node)
    {
        if (_moduleWindows.Find(w => ReferenceEquals(w.Node, node)) is { } existing)
        {
            existing.Window.Activate();
            return;
        }
        var window = new ModuleWindow(node, WindowTitle(node), _window, _lightTheme(), () => LevelsOf(node), () =>
        {
            IsModified = true;
            Sync();
        });
        _moduleWindows.Add(window);
        window.Window.Closed += _ => _moduleWindows.Remove(window);
        window.Show();
    }

    private static string WindowTitle(GraphNode node)
        => string.IsNullOrWhiteSpace(node.Title) ? Title(node) : $"{node.Title} — {TypeName(node)}";

    private void OpenPlugin(PluginNode node)
    {
        if (_windows.Find(w => ReferenceEquals(w.Node, node)) is { } existing)
        {
            existing.Window.Activate();
            return;
        }
        PluginWindow window;
        try
        {
            window = new PluginWindow(node, _audio, _window, _lightTheme(), () => LevelsOf(node)) { Log = Log };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log?.Error(node.DisplayTitle, L.T($"Its window could not be opened: {ex.Message}", $"Sa fenêtre n'a pas pu s'ouvrir : {ex.Message}"));
            MessageDialog.Show(_window, _window.Title, L.T($"The window of {node.DisplayTitle} could not be opened:\n{ex.Message}", $"La fenêtre de {node.DisplayTitle} n'a pas pu s'ouvrir :\n{ex.Message}"));
            return;
        }
        _windows.Add(window);
        window.StateChanged += () =>
        {
            IsModified = true;
            Sync();
        };
        window.Window.Closed += _ => _windows.Remove(window);
        window.Show();
    }

    public void SetTheme(bool light)
    {
        foreach (var w in _windows) w.SetTheme(light);
        foreach (var w in _moduleWindows) w.SetTheme(light);
    }

    public void CloseWindows()
    {
        foreach (var w in _windows.ToList()) w.Window.Close();
        foreach (var w in _moduleWindows.ToList()) w.Window.Close();
    }

    internal IList<MenuItem> AddModuleItems(Point? at)
    {
        MenuItem Item(string text, Func<GraphNode> create) => new(text, () => AddModule(create(), at));
        return new List<MenuItem>
        {
            Item(L.T("&Audio input", "&Entrée audio"), () => new AudioInputNode()),
            Item(L.T("Audio &output", "&Sortie audio"), () => new AudioOutputNode()),
            Item(L.T("&Test signal", "Signal de &test"), () => new TestSignalNode()),
            Item(L.T("Audio &file…", "&Fichier audio…"), () => new AudioFileNode()),
            MenuItem.Separator(),
            Item(L.T("&MIDI input", "Entrée &MIDI"), () => new MidiInputNode()),
            Item(L.T("&Keyboard", "&Clavier"), () => new KeyboardNode()),
            Item(L.T("MIDI o&utput", "Sortie MI&DI"), () => new MidiOutputNode()),
            MenuItem.Separator(),
            Item("&Gain", () => new GainNode()),
            Item(L.T("Audio &sum", "Somme a&udio"), () => new AudioSumNode()),
            Item(L.T("MIDI m&erge", "Fusion MID&I"), () => new MidiMergeNode()),
            Item("Mu&xer", () => new MuxNode()),
            Item("&Demuxer", () => new DemuxNode()),
            MenuItem.Separator(),
            new MenuItem(L.T("A &plug-in from the list…", "Un &plug-in de la liste…"), () => PluginListRequested?.Invoke()),
        };
    }

    private void FillMenu(NodeEditorMenuEventArgs e)
    {
        if (e.Cable?.Tag is GraphConnection cable)
        {
            e.Items.Add(new MenuItem(L.T("&Remove the cable", "&Supprimer le câble"), () => Edit(() => Graph.Disconnect(cable))) { ShortcutText = L.T("Del", "Suppr") });
            if (!cable.IsFeedback && cable.Source.IsInLoop && cable.Target.IsInLoop)
                e.Items.Add(new MenuItem(L.T("&Break the loop here", "&Couper la boucle ici"), () => Edit(() => Graph.BreakLoopAt(cable))));
            return;
        }
        if (e.Node?.Tag is GraphNode node)
        {
                e.Items.Add(new MenuItem(L.T("&Open", "&Ouvrir"), () => Open(node)) { ShortcutText = L.T("Enter", "Entrée") });
            if (node is PluginNode plugin)
            {
                e.Items.Add(new MenuItem(L.T("&Bypass", "&Contourner"), () => { plugin.Bypass = !plugin.Bypass; IsModified = true; Sync(); }) { IsChecked = plugin.Bypass });
                e.Items.Add(new MenuItem(L.T("&Mute", "&Muet"), () => { plugin.Mute = !plugin.Mute; IsModified = true; Sync(); }) { IsChecked = plugin.Mute });
            }
            e.Items.Add(new MenuItem(L.T("Re&name…", "Re&nommer…"), () => Rename(node)) { ShortcutText = "F2" });
            var connect = new MenuItem(L.T("&Connect", "&Relier"));
            foreach (var output in node.Outputs)
            {
                foreach (var target in Graph.Nodes.SelectMany(n => n.Inputs))
                {
                    if (Graph.WhyNotConnect(output, target) is not null || Graph.ConnectionTo(target) is { } c && ReferenceEquals(c.Source, node) && c.SourceKey == output.Key) continue;
                    string text = (node.Outputs.Count > 1 ? PortName(output) + " → " : string.Empty) + Title(target.Node) + " · " + PortName(target);
                    if (Graph.ConnectionTo(target) is not null) text += L.T(" (replaces its cable)", " (remplace son câble)");
                    var (o, t) = (output, target);
                    connect.Items.Add(new MenuItem(text, () => Edit(() => Graph.Connect(o, t))));
                }
            }
            if (connect.Items.Count > 0) e.Items.Add(connect);
            var disconnect = new MenuItem(L.T("&Disconnect", "&Débrancher"));
            foreach (var c in Graph.Connections.Where(c => ReferenceEquals(c.Source, node) || ReferenceEquals(c.Target, node)))
            {
                string text = $"{Title(c.Source)} · {PortName(c.SourcePort!)} → {Title(c.Target)} · {PortName(c.TargetPort!)}";
                var cc = c;
                disconnect.Items.Add(new MenuItem(text, () => Edit(() => Graph.Disconnect(cc))));
            }
            if (disconnect.Items.Count > 0) e.Items.Add(disconnect);
            e.Items.Add(MenuItem.Separator());
            e.Items.Add(new MenuItem(L.T("&Remove", "&Supprimer"), () => ConfirmRemove([.. Editor.SelectedNodes.Select(n => (GraphNode)n.Tag!)])) { ShortcutText = L.T("Del", "Suppr") });
            return;
        }
        var add = new MenuItem(L.T("&Add a module", "&Ajouter un module"));
        add.Items.AddRange(AddModuleItems(e.GraphPosition));
        e.Items.Add(add);
        e.Items.Add(new MenuItem(L.T("Show &all", "Tout &afficher"), Editor.ZoomToFit) { ShortcutText = L.T("Home", "Origine") });
    }

    /// <summary>Renames the selected module (F2).</summary>
    public void RenameSelected()
    {
        if (Editor.SelectedNodes is [{ Tag: GraphNode node }]) Rename(node);
    }

    private void Rename(GraphNode node)
    {
        var box = new TextBox { Text = Title(node), Height = 28f };
        var body = new StackPanel { Spacing = 8f };
        body.Add(new Label(L.T("Name of the module (empty: the default name)", "Nom du module (vide : le nom par défaut)")) { Height = 20f }, box);
        var dialog = new Dialog(L.T("Rename", "Renommer"), body) { DialogWidth = 420f };
        dialog.AddButton(L.T("Cancel", "Annuler"), null, isCancel: true).AddButton("OK", "ok", isDefault: true);
        dialog.ResultChosen += (_, result) =>
        {
            if (!Equals(result, "ok")) return;
            string text = box.Text.Trim();
            node.Title = text.Length == 0 || text == node.DefaultTitle ? null : text;
            IsModified = true;
            Sync();
            if (_windows.Find(w => ReferenceEquals(w.Node, node)) is { } window) window.Window.Title = Title(node);
            if (_moduleWindows.Find(w => ReferenceEquals(w.Node, node)) is { } moduleWindow) moduleWindow.Window.Title = WindowTitle(node);
        };
        _host.ShowPopup(dialog);
        box.Focus();
        box.SelectAll();
    }

    // ---------------------------------------------------------------- files

    /// <summary>Replaces the graph with the default one: audio input, MIDI input, audio output.</summary>
    public void NewGraph()
    {
        CloseWindows();
        var plugins = Graph.Nodes.OfType<PluginNode>().Select(n => n.Plugin).ToList();
        Graph.Clear();
        foreach (var p in plugins) p.Dispose();
        Graph.LoopBlockSize = 0;
        Graph.Add(new AudioInputNode { X = 32f, Y = 48f });
        Graph.Add(new MidiInputNode { X = 32f, Y = 192f });
        Graph.Add(new AudioOutputNode { X = 704f, Y = 48f });
        FilePath = null;
        IsModified = false;
        Sync();
        SnapToConnectors();
        Editor.ZoomToFit();
    }

    /// <summary>Opens a graph file; plug-ins that cannot be loaded stay as silent modules.</summary>
    public bool Load(string path, bool remember = true)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message?.Invoke(L.T("The graph could not be read: ", "Le graphe n'a pas pu être lu : ") + ex.Message);
            return false;
        }
        CloseWindows();
        var plugins = Graph.Nodes.OfType<PluginNode>().Select(n => n.Plugin).ToList();
        IReadOnlyList<string> warnings;
        try
        {
            warnings = Graph.LoadJson(json, _loader.Load);
        }
        catch (FormatException ex)
        {
            Message?.Invoke(ex.Message);
            return false;
        }
        finally
        {
            foreach (var p in plugins) p.Dispose();
        }
        if (remember) FilePath = path;
        IsModified = false;
        Sync();
        Editor.ZoomToFit();
        Log?.Info(Source, (remember ? L.T("Opened: ", "Ouvert : ") : L.T("Graph of the last session restored: ", "Graphe de la dernière session restauré : ")) + path);
        foreach (var node in Graph.Nodes)
        {
            if (node is PluginNode plugin)
                Log?.Info(node.DisplayTitle, L.T($"Loaded ({plugin.Reference.Path})", $"Chargé ({plugin.Reference.Path})") + LatencyText(plugin));
            else if (node is MissingPluginNode missing)
                Log?.Error(node.DisplayTitle, L.T($"Could not be loaded ({missing.Reference?.Path}): {missing.Reason}", $"N'a pas pu être chargé ({missing.Reference?.Path}) : {missing.Reason}"));
        }
        foreach (var warning in warnings.Where(w => !w.Contains("could not be loaded", StringComparison.Ordinal))) Log?.Warning(Source, warning);
        if (warnings.Count > 0)
        {
            MessageDialog.Show(_window, _window.Title, L.T("Some parts of the graph could not be restored:\n\n", "Certaines parties du graphe n'ont pas pu être restaurées :\n\n")
                + string.Join("\n", warnings.Take(12)) + (warnings.Count > 12 ? "\n…" : string.Empty));
        }
        return true;
    }

    private static string LatencyText(PluginNode node) => node.Plugin.LatencySamples > 0
        ? L.T($", latency {node.Plugin.LatencySamples} samples", $", latence {node.Plugin.LatencySamples} échantillons")
        : string.Empty;

    /// <summary>Saves the graph (with the plug-in states).</summary>
    public bool Save(string path, bool remember = true)
    {
        try
        {
            Graph.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message?.Invoke(L.T("The graph could not be saved: ", "Le graphe n'a pas pu être enregistré : ") + ex.Message);
            return false;
        }
        if (remember)
        {
            FilePath = path;
            IsModified = false;
        }
        return true;
    }

    /// <summary>Makes <paramref name="path"/> the file of the restored session graph (modified when they differ).</summary>
    public void AdoptPath(string path)
    {
        FilePath = path;
        try
        {
            IsModified = File.ReadAllText(path) != Graph.ToJson();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsModified = true;
        }
    }

    internal static readonly FileFilter[] Filter = [new(L.T("PluginUI graph", "Graphe PluginUI") + " (.pluginui-graph)", "*.pluginui-graph")];
}
