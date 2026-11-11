// 2026/11/11 15:13:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PluginUI.Host.Audio;

namespace PluginUI.Host.Graph;

/// <summary>
/// A modular audio and MIDI graph played on an audio device (or rendered offline): modules (<see cref="GraphNode"/>) —
/// plug-ins, device inputs and outputs, files, test signals, keyboards, sums, gains — joined by cables
/// (<see cref="GraphConnection"/>). Give it to <see cref="AudioDriver.Start"/>.
/// </summary>
/// <remarks>
/// <para>
/// Rules: a cable goes from an output to an input of the same kind (audio or MIDI); an output feeds any number of cables,
/// an input takes at most one — signals are added only by a summing module (<see cref="AudioSumNode"/>,
/// <see cref="MidiMergeNode"/>), which can also align its inputs on the latency of their paths.
/// </para>
/// <para>
/// Loops are allowed: the most recent cable of each loop is a feedback cable (<see cref="GraphConnection.IsFeedback"/>)
/// that delivers its signal one block late — a plug-in processes whole blocks, so a loop cannot be shorter. With
/// <see cref="LoopBlockSize"/>, the modules of a loop run in shorter sub-blocks, and the loop delay is that sub-block.
/// </para>
/// <para>
/// Threads: edit the graph (add, remove, connect, properties that change ports) on one thread, the UI thread; each edit
/// rebuilds an execution plan there and swaps it in. The audio thread never waits: during the swap (microseconds) it
/// outputs one block of silence.
/// </para>
/// </remarks>
public sealed unsafe class PluginGraph : IAudioRenderer, IDisposable
{
    private static readonly Dictionary<string, Func<GraphNode>> NodeFactories = new(StringComparer.Ordinal)
    {
        ["audio-input"] = () => new AudioInputNode(),
        ["audio-output"] = () => new AudioOutputNode(),
        ["test-signal"] = () => new TestSignalNode(),
        ["audio-file"] = () => new AudioFileNode(),
        ["midi-input"] = () => new MidiInputNode(),
        ["keyboard"] = () => new KeyboardNode(),
        ["midi-output"] = () => new MidiOutputNode(),
        ["gain"] = () => new GainNode(),
        ["audio-sum"] = () => new AudioSumNode(),
        ["midi-merge"] = () => new MidiMergeNode(),
        ["midi-transpose"] = () => new MidiTransposeNode(),
        ["midi-velocity"] = () => new MidiVelocityNode(),
        ["midi-filter"] = () => new MidiFilterNode(),
        ["mux"] = () => new MuxNode(),
        ["demux"] = () => new DemuxNode(),
    };

    private readonly Lock _gate = new();
    private readonly List<GraphNode> _nodes = [];
    private readonly List<GraphConnection> _connections = [];
    private GraphPlan? _plan;
    private MidiInputNode[] _midiInputs = [];
    private int _nextId = 1;
    private long _sequence;
    private long _time;
    private float** _deviceInputSlice;
    private float** _deviceOutputSlice;
    private int _deviceSliceSize;
    private int _loopBlockSize;
    private bool _disposed;

    /// <summary>Registers a module type so graph files can create it (<paramref name="typeKey"/> = its <see cref="GraphNode.TypeKey"/>).</summary>
    public static void RegisterNodeType(string typeKey, Func<GraphNode> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeKey);
        ArgumentNullException.ThrowIfNull(factory);
        if (typeKey == "plugin") throw new ArgumentException("'plugin' is reserved for plug-ins.", nameof(typeKey));
        lock (NodeFactories) NodeFactories[typeKey] = factory;
    }

    /// <summary>Creates a module of a registered type (not a plug-in), or null for an unknown type.</summary>
    public static GraphNode? CreateNode(string typeKey)
    {
        lock (NodeFactories) return NodeFactories.TryGetValue(typeKey, out var factory) ? factory() : null;
    }

    /// <summary>The modules, in the order they were added.</summary>
    public IReadOnlyList<GraphNode> Nodes => _nodes;

    /// <summary>The cables, oldest first.</summary>
    public IReadOnlyList<GraphConnection> Connections => _connections;

    /// <summary>Sample rate (0 before <see cref="Prepare"/>).</summary>
    public double SampleRate { get; private set; }

    /// <summary>Largest device block.</summary>
    public int MaxFrames { get; private set; }

    /// <summary>Input channels of the device.</summary>
    public int InputChannels { get; private set; }

    /// <summary>Output channels of the device.</summary>
    public int OutputChannels { get; private set; }

    /// <summary><c>true</c> once <see cref="Prepare"/> was called.</summary>
    public bool IsPrepared { get; private set; }

    /// <summary>Live processing (default) or offline rendering, told to the plug-ins when they are prepared.</summary>
    public bool Realtime { get; set; } = true;

    /// <summary>
    /// Sub-block in samples for the modules of a loop (0 = the device block, the default). Smaller values shorten the
    /// loop delay to that many samples, at the cost of more calls to the plug-ins of the loop.
    /// </summary>
    public int LoopBlockSize
    {
        get => _loopBlockSize;
        set
        {
            value = Math.Max(0, value);
            if (value == _loopBlockSize) return;
            _loopBlockSize = value;
            Rebuild();
        }
    }

    /// <summary>Master gain in decibels (on every device output).</summary>
    public double MasterGainDb { get; set; }

    /// <summary>Peaks of the device output.</summary>
    public PeakMeter OutputMeter { get; } = new();

    /// <summary>Receives what reaches the <see cref="MidiOutputNode"/> modules (audio thread; null = discarded).</summary>
    public IMidiSink? MidiOutput { get; set; }

    /// <summary>The structure changed: modules, cables, ports, loops or latencies (raised on the editing thread).</summary>
    public event Action? Changed;

    /// <summary>A module stopped after it threw while processing (raised on the audio thread).</summary>
    public event Action<GraphNode, Exception>? NodeFailed;

    /// <summary>Adds a module (prepared at once when the graph is) and returns it.</summary>
    public T Add<T>(T node) where T : GraphNode
    {
        AddCore(node, 0);
        Rebuild();
        return node;
    }

    private void AddCore(GraphNode node, int id)
    {
        ArgumentNullException.ThrowIfNull(node);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (node.Graph is not null) throw new InvalidOperationException("The module already belongs to a graph.");
        if (id <= 0 || _nodes.Any(n => n.Id == id)) id = _nextId;
        node.Id = id;
        _nextId = Math.Max(_nextId, id + 1);
        node.Graph = this;
        node.HasFailed = false;
        node.Failure = null;
        if (IsPrepared) node.Prepare(SampleRate, MaxFrames, Realtime);
        _nodes.Add(node);
    }

    /// <summary>Removes a module and its cables, and stops it (a plug-in is stopped, not disposed).</summary>
    public void Remove(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(node.Graph, this)) return;
        _connections.RemoveAll(c => ReferenceEquals(c.Source, node) || ReferenceEquals(c.Target, node));
        _nodes.Remove(node);
        Rebuild();
        node.Release();
        node.Graph = null;
    }

    /// <summary>Removes every module and cable.</summary>
    public void Clear()
    {
        var nodes = _nodes.ToArray();
        _connections.Clear();
        _nodes.Clear();
        Rebuild();
        foreach (var node in nodes)
        {
            node.Release();
            node.Graph = null;
        }
        _nextId = 1;
        _sequence = 0;
    }

    /// <summary>Why <paramref name="output"/> cannot be cabled to <paramref name="input"/>, or null when it can.</summary>
    public string? WhyNotConnect(GraphPort output, GraphPort input)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        if (output.IsInput) return "A cable starts at an output.";
        if (!input.IsInput) return "A cable ends at an input.";
        if (!ReferenceEquals(output.Node.Graph, this) || !ReferenceEquals(input.Node.Graph, this)) return "The modules are not in this graph.";
        if (output.Kind != input.Kind) return output.Kind == PortKind.Audio ? "An audio output goes to an audio input." : "A MIDI output goes to a MIDI input.";
        return null;
    }

    /// <summary>
    /// Cables <paramref name="output"/> to <paramref name="input"/>; a cable already arriving at <paramref name="input"/>
    /// is replaced (an input takes one cable). Closing a loop is allowed (see <see cref="GraphConnection.IsFeedback"/>).
    /// </summary>
    public GraphConnection Connect(GraphPort output, GraphPort input)
    {
        if (WhyNotConnect(output, input) is { } reason) throw new ArgumentException(reason);
        var existing = ConnectionTo(input);
        if (existing is not null && ReferenceEquals(existing.Source, output.Node) && existing.SourceKey == output.Key) return existing;
        if (existing is not null) _connections.Remove(existing);
        var connection = new GraphConnection(output.Node, output.Key, input.Node, input.Key, ++_sequence);
        _connections.Add(connection);
        Rebuild();
        return connection;
    }

    /// <summary>Removes a cable.</summary>
    public void Disconnect(GraphConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (_connections.Remove(connection)) Rebuild();
    }

    /// <summary>The cable arriving at <paramref name="input"/>, or null.</summary>
    public GraphConnection? ConnectionTo(GraphPort input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _connections.Find(c => ReferenceEquals(c.Target, input.Node) && c.TargetKey == input.Key);
    }

    /// <summary>The cables leaving <paramref name="output"/>.</summary>
    public IEnumerable<GraphConnection> ConnectionsFrom(GraphPort output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _connections.Where(c => ReferenceEquals(c.Source, output.Node) && c.SourceKey == output.Key);
    }

    /// <summary>
    /// Makes <paramref name="connection"/> the feedback cable of its loop (it becomes the most recent cable, so the loop is
    /// broken there). Does nothing to a cable outside any loop.
    /// </summary>
    public void BreakLoopAt(GraphConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!_connections.Contains(connection)) return;
        connection.Sequence = ++_sequence;
        Rebuild();
    }

    /// <summary>Delivers a message of the MIDI input port to every <see cref="MidiInputNode"/> (any thread).</summary>
    public void PostMidiInput(in MidiEvent e)
    {
        foreach (var node in Volatile.Read(ref _midiInputs)) node.Post(e);
    }

    /// <summary>
    /// Applies the edits: drops the cables whose ports disappeared, finds the loops and the latencies, and swaps in a new
    /// execution plan. Called by every edit; call it after changing something the graph cannot see (a plug-in's latency).
    /// </summary>
    public void Rebuild() => Apply(null);

    /// <summary>The plug-in of <paramref name="node"/> restarted (latency, buses): prepares it again and rebuilds.</summary>
    internal void RefreshNode(PluginNode node) => Apply(() =>
    {
        node.UpdatePorts();
        if (IsPrepared) node.Prepare(SampleRate, MaxFrames, Realtime);
    });

    private void Apply(Action? underLock)
    {
        if (_disposed) return;
        GraphPlan? previous;
        if (underLock is null)
        {
            Prune();
            var plan = GraphPlan.Build(_nodes, _connections, IsPrepared ? MaxFrames : 0, _loopBlockSize);
            lock (_gate)
            {
                previous = _plan;
                _plan = IsPrepared ? plan : null;
            }
            if (!IsPrepared) plan.Dispose();
        }
        else
        {
            // The node changes under the lock: the audio thread must not run it between the change and the new plan.
            lock (_gate)
            {
                underLock();
                Prune();
                var plan = GraphPlan.Build(_nodes, _connections, IsPrepared ? MaxFrames : 0, _loopBlockSize);
                previous = _plan;
                _plan = IsPrepared ? plan : null;
                if (!IsPrepared) plan.Dispose();
            }
        }
        previous?.Dispose();
        Changed?.Invoke();
    }

    /// <summary>Drops the cables whose ports disappeared or no longer match, and refreshes the MIDI input list.</summary>
    private void Prune()
    {
        _connections.RemoveAll(c => c.SourcePort is not { } s || c.TargetPort is not { } t || s.Kind != t.Kind);
        Volatile.Write(ref _midiInputs, [.. _nodes.OfType<MidiInputNode>()]);
    }

    /// <inheritdoc />
    public void Prepare(double sampleRate, int maxFrames, int inputChannels, int outputChannels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            SampleRate = sampleRate;
            MaxFrames = maxFrames;
            InputChannels = Math.Max(0, inputChannels);
            OutputChannels = Math.Max(0, outputChannels);
            OutputMeter.Resize(OutputChannels);
            FreeSlices();
            _deviceSliceSize = Math.Max(1, Math.Max(InputChannels, OutputChannels));
            _deviceInputSlice = (float**)NativeMemory.Alloc((nuint)_deviceSliceSize, (nuint)sizeof(float*));
            _deviceOutputSlice = (float**)NativeMemory.Alloc((nuint)_deviceSliceSize, (nuint)sizeof(float*));
            foreach (var node in _nodes)
            {
                node.HasFailed = false;
                node.Failure = null;
                node.Prepare(sampleRate, maxFrames, Realtime);
            }
            _time = 0;
            IsPrepared = true;
        }
        Rebuild();
    }

    /// <inheritdoc />
    public void Render(in AudioBuffers inputs, in AudioBuffers outputs)
    {
        outputs.Clear();
        if (!_gate.TryEnter()) return;
        try
        {
            var plan = _plan;
            if (plan is null) return;
            int frames = outputs.Frames;
            if (frames <= 0 || frames > MaxFrames) return;
            long blockStart = _time;
            foreach (var runtime in plan.Runtimes) runtime.ClearMidi();
            foreach (var step in plan.Steps)
            {
                int sub = step.SubBlock;
                if (sub <= 0 || sub >= frames)
                {
                    foreach (var runtime in step.Nodes) Run(runtime, blockStart, 0, frames, inputs, outputs);
                }
                else
                {
                    for (int offset = 0; offset < frames; offset += sub)
                    {
                        int n = Math.Min(sub, frames - offset);
                        foreach (var runtime in step.Nodes) Run(runtime, blockStart, offset, n, inputs, outputs);
                    }
                }
            }
            float master = SignalSource.DbToGain(MasterGainDb);
            if (master != 1f)
            {
                for (int c = 0; c < outputs.ChannelCount; c++)
                {
                    var o = outputs[c];
                    for (int i = 0; i < o.Length; i++) o[i] *= master;
                }
            }
            OutputMeter.Update(outputs);
            _time = blockStart + frames;
        }
        finally
        {
            _gate.Exit();
        }
    }

    private void Run(NodeRuntime runtime, long blockStart, int offset, int frames, in AudioBuffers inputs, in AudioBuffers outputs)
    {
        var node = runtime.Node;
        long time = blockStart + offset;
        var deviceIn = Slice(inputs, _deviceInputSlice, offset, frames);
        var deviceOut = Slice(outputs, _deviceOutputSlice, offset, frames);
        if (!node.HasFailed)
        {
            try
            {
                node.Process(new NodeContext(runtime, offset, frames, time, SampleRate, deviceIn, deviceOut));
            }
            catch (Exception ex)
            {
                node.HasFailed = true;
                node.Failure = ex;
                NodeFailed?.Invoke(node, ex);
            }
        }
        if (node.HasFailed)
        {
            foreach (var o in runtime.Outputs) o.Audio?.Slice(offset, frames).Clear();
        }
        if (node.MetersFirstOutput && runtime.MeterOutput >= 0) node.Meter.Update(runtime.Outputs[runtime.MeterOutput].Audio!.Slice(offset, frames));
        foreach (var (_, binding) in runtime.AudioTaps) binding.Tap(time, offset, frames);
        foreach (var binding in runtime.MidiTaps) binding.Tap(blockStart, offset, frames);
    }

    private AudioBuffers Slice(in AudioBuffers device, float** scratch, int offset, int frames)
    {
        int n = Math.Min(device.ChannelCount, _deviceSliceSize);
        if (n == 0) return new AudioBuffers(null, 0, frames);
        for (int c = 0; c < n; c++)
        {
            fixed (float* p = device[c]) scratch[c] = p + offset;
        }
        return new AudioBuffers(scratch, n, frames);
    }

    // ------------------------------------------------------------------------------------------------ files

    /// <summary>The graph as JSON (the "PluginUI.Graph" format): modules with their settings and plug-in states, cables.</summary>
    public string ToJson()
    {
        var nodes = new JsonArray();
        foreach (var node in _nodes)
        {
            var o = new JsonObject
            {
                ["id"] = node.Id,
                ["type"] = node.TypeKey,
            };
            if (!string.IsNullOrWhiteSpace(node.Title)) o["title"] = node.Title;
            o["x"] = Math.Round(node.X, 1);
            o["y"] = Math.Round(node.Y, 1);
            node.WriteSettings(o);
            nodes.Add((JsonNode)o);
        }
        var cables = new JsonArray();
        foreach (var c in _connections.OrderBy(c => c.Sequence))
        {
            cables.Add((JsonNode)new JsonObject
            {
                ["from"] = c.Source.Id,
                ["output"] = c.SourceKey,
                ["to"] = c.Target.Id,
                ["input"] = c.TargetKey,
            });
        }
        var root = new JsonObject
        {
            ["format"] = "PluginUI.Graph",
            ["version"] = 1,
            ["loopBlockSize"] = _loopBlockSize,
            ["masterGainDb"] = MasterGainDb,
            ["nodes"] = nodes,
            ["connections"] = cables,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Writes the graph to a file (see <see cref="ToJson"/>).</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Replaces the graph with the one in <paramref name="json"/>. Plug-ins are created by <paramref name="loadPlugin"/>;
    /// one that cannot be loaded becomes a <see cref="MissingPluginNode"/> (it keeps its place, cables and state).
    /// </summary>
    /// <param name="json">Text written by <see cref="ToJson"/>.</param>
    /// <param name="loadPlugin">Loads the plug-in a reference designates (throws when it cannot).</param>
    /// <returns>What could not be restored, one message per problem (empty when everything was).</returns>
    /// <exception cref="FormatException">The text is not a graph.</exception>
    public IReadOnlyList<string> LoadJson(string json, Func<PluginReference, PluginInstance> loadPlugin)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(loadPlugin);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("A graph file holds a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("The graph file is not valid JSON: " + ex.Message, ex);
        }
        if (GraphJson.String(root, "format", null) != "PluginUI.Graph") throw new FormatException("This is not a PluginUI graph file.");

        var warnings = new List<string>();
        Clear();
        _loopBlockSize = Math.Max(0, GraphJson.Int(root, "loopBlockSize", 0));
        MasterGainDb = GraphJson.Double(root, "masterGainDb", 0);
        var byId = new Dictionary<int, GraphNode>();
        foreach (var item in root["nodes"] as JsonArray ?? [])
        {
            if (item is not JsonObject o) continue;
            string type = GraphJson.String(o, "type", null) ?? string.Empty;
            int id = GraphJson.Int(o, "id", 0);
            GraphNode? node;
            if (type == "plugin")
            {
                var reference = GraphJson.ReadReference(o["plugin"]);
                string name = reference?.Name is { Length: > 0 } n ? n : "?";
                PluginInstance? plugin = null;
                try
                {
                    if (reference is null) throw new FormatException("the plug-in is not described");
                    plugin = loadPlugin(reference);
                    var pluginNode = new PluginNode(plugin, reference);
                    try
                    {
                        pluginNode.ReadSettings(o);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // The plug-in works: it stays, with the settings it starts with.
                        warnings.Add($"Plug-in '{name}': its saved settings could not be restored ({ex.Message}); it starts with its default settings.");
                    }
                    node = pluginNode;
                }
                catch (Exception ex)
                {
                    plugin?.Dispose();
                    warnings.Add($"Plug-in '{name}' could not be loaded: {ex.Message}");
                    node = new MissingPluginNode(o, reference, ex.Message);
                }
            }
            else
            {
                node = CreateNode(type);
                if (node is null)
                {
                    warnings.Add($"Unknown module type '{type}' (module {id}) was left out.");
                    continue;
                }
                try
                {
                    node.ReadSettings(o);
                }
                catch (GraphLoadException ex)
                {
                    warnings.Add(ex.Message);
                }
            }
            node.Title = GraphJson.String(o, "title", null);
            node.X = (float)GraphJson.Double(o, "x", 0);
            node.Y = (float)GraphJson.Double(o, "y", 0);
            AddCore(node, id);
            byId[node.Id] = node;
            if (id > 0 && node.Id != id) warnings.Add($"Module {id} appears twice; the second one got the number {node.Id}.");
            byId.TryAdd(id, node);
        }
        foreach (var item in root["connections"] as JsonArray ?? [])
        {
            if (item is not JsonObject o) continue;
            byId.TryGetValue(GraphJson.Int(o, "from", 0), out var from);
            byId.TryGetValue(GraphJson.Int(o, "to", 0), out var to);
            var output = from?.FindOutput(GraphJson.String(o, "output", null) ?? string.Empty);
            var input = to?.FindInput(GraphJson.String(o, "input", null) ?? string.Empty);
            if (output is null || input is null || WhyNotConnect(output, input) is not null)
            {
                warnings.Add($"A cable ({GraphJson.Int(o, "from", 0)} → {GraphJson.Int(o, "to", 0)}) no longer matches the modules and was left out.");
                continue;
            }
            if (ConnectionTo(input) is { } existing)
            {
                warnings.Add($"Input {input} had several cables; only the last one was kept.");
                _connections.Remove(existing);
            }
            _connections.Add(new GraphConnection(output.Node, output.Key, input.Node, input.Key, ++_sequence));
        }
        Rebuild();
        return warnings;
    }

    /// <summary>Reads a graph file (see <see cref="LoadJson"/>).</summary>
    public IReadOnlyList<string> Load(string path, Func<PluginReference, PluginInstance> loadPlugin)
    {
        ArgumentNullException.ThrowIfNull(path);
        return LoadJson(File.ReadAllText(path), loadPlugin);
    }

    // ------------------------------------------------------------------------------------------------ lifetime

    private void FreeSlices()
    {
        NativeMemory.Free(_deviceInputSlice);
        NativeMemory.Free(_deviceOutputSlice);
        _deviceInputSlice = null;
        _deviceOutputSlice = null;
        _deviceSliceSize = 0;
    }

    /// <summary>Stops every module and frees the buffers (plug-ins are stopped, not disposed).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        lock (_gate)
        {
            _disposed = true;
            _plan?.Dispose();
            _plan = null;
            foreach (var node in _nodes) node.Release();
            FreeSlices();
            IsPrepared = false;
        }
    }
}

/// <summary>The compiled graph: modules in processing order, their buffers and cables, loops and latencies.</summary>
internal sealed class GraphPlan : IDisposable
{
    private GraphPlan(NodeRuntime[] runtimes, PlanStep[] steps)
    {
        Runtimes = runtimes;
        Steps = steps;
    }

    public NodeRuntime[] Runtimes { get; }

    public PlanStep[] Steps { get; }

    internal sealed record PlanStep(NodeRuntime[] Nodes, int SubBlock);

    /// <summary>
    /// Analyses the graph (feedback cables, loops, order, latencies — written to the nodes and cables) and, when
    /// <paramref name="maxFrames"/> &gt; 0, allocates the run-time buffers.
    /// </summary>
    public static GraphPlan Build(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphConnection> connections, int maxFrames, int loopBlockSize)
    {
        int count = nodes.Count;
        var index = new Dictionary<GraphNode, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < count; i++) index[nodes[i]] = i;
        var edges = connections.Where(c => index.ContainsKey(c.Source) && index.ContainsKey(c.Target)).OrderBy(c => c.Sequence).ToList();

        // Feedback cables: in creation order, a cable whose target already reaches its source closes a loop.
        var accepted = new List<int>[count];
        for (int i = 0; i < count; i++) accepted[i] = [];
        foreach (var c in edges)
        {
            int u = index[c.Source], v = index[c.Target];
            c.IsFeedback = u == v || Reaches(accepted, v, u);
            if (!c.IsFeedback) accepted[u].Add(v);
        }

        // Loops: strongly connected components over every cable.
        var all = new List<int>[count];
        for (int i = 0; i < count; i++) all[i] = [];
        foreach (var c in edges) all[index[c.Source]].Add(index[c.Target]);
        int[] component = StronglyConnected(all, out int components);
        var loop = new bool[components];
        var size = new int[components];
        for (int i = 0; i < count; i++) size[component[i]]++;
        foreach (var c in edges)
        {
            int u = index[c.Source], v = index[c.Target];
            if (component[u] == component[v] && (u == v || size[component[u]] > 1)) loop[component[u]] = true;
        }
        for (int i = 0; i < count; i++) nodes[i].IsInLoop = loop[component[i]];

        int[] order = Order(nodes, accepted, all, component, components);

        // Latencies along the order; summing modules align their inputs.
        var incoming = new GraphConnection?[count][];
        for (int i = 0; i < count; i++) incoming[i] = new GraphConnection?[nodes[i].Inputs.Count];
        foreach (var c in edges)
        {
            int v = index[c.Target];
            var port = c.TargetPort;
            if (port is not null) incoming[v][port.Index] = c;
        }
        bool subBlocks = maxFrames > 0 && loopBlockSize > 0 && loopBlockSize < maxFrames;
        int loopDelay = maxFrames <= 0 ? 0 : subBlocks ? loopBlockSize : maxFrames;
        foreach (int v in order)
        {
            var node = nodes[v];
            int aligned = 0;
            foreach (var c in incoming[v])
            {
                if (c is null || c.IsFeedback) continue;
                aligned = Math.Max(aligned, c.Source.PathLatencySamples);
            }
            bool compensate = node is AudioSumNode { CompensateLatency: true } || node is MidiMergeNode { CompensateLatency: true };
            foreach (var c in incoming[v])
            {
                if (c is null) continue;
                c.DelaySamples = c.IsFeedback ? (loop[component[v]] && subBlocks ? loopBlockSize : loopDelay)
                    : compensate ? aligned - c.Source.PathLatencySamples : 0;
            }
            node.PathLatencySamples = aligned + Math.Max(0, node.LatencySamples);
        }

        if (maxFrames <= 0) return new GraphPlan([], []);

        // Run-time objects.
        var runtimes = new NodeRuntime[count];
        for (int i = 0; i < count; i++)
        {
            var node = nodes[i];
            int meter = -1;
            for (int p = 0; p < node.Outputs.Count; p++)
            {
                if (node.Outputs[p].Kind == PortKind.Audio)
                {
                    meter = p;
                    break;
                }
            }
            runtimes[i] = new NodeRuntime(node, maxFrames) { MeterOutput = meter };
        }
        foreach (var c in edges)
        {
            int u = index[c.Source], v = index[c.Target];
            var output = c.SourcePort;
            var input = c.TargetPort;
            if (output is null || input is null) continue;
            var source = runtimes[u].Outputs[output.Index];
            int delay = Math.Max(0, c.DelaySamples);
            if (output.Kind == PortKind.Audio)
            {
                var binding = new AudioInputBinding(source.Audio!, delay, maxFrames);
                runtimes[v].AudioInputs[input.Index] = binding;
                if (delay > 0) runtimes[u].AudioTaps.Add((output.Index, binding));
            }
            else
            {
                var binding = new MidiInputBinding(source.Midi!, delay);
                runtimes[v].MidiInputs[input.Index] = binding;
                if (delay > 0) runtimes[u].MidiTaps.Add(binding);
            }
        }

        // Steps: consecutive modules of one loop share sub-blocks; the others run on the whole block.
        var steps = new List<PlanStep>();
        var current = new List<NodeRuntime>();
        int currentSub = 0, currentComponent = -1;
        foreach (int v in order)
        {
            int sub = subBlocks && loop[component[v]] ? loopBlockSize : 0;
            int comp = sub > 0 ? component[v] : -1;
            if (current.Count > 0 && (sub != currentSub || comp != currentComponent))
            {
                steps.Add(new PlanStep([.. current], currentSub));
                current.Clear();
            }
            currentSub = sub;
            currentComponent = comp;
            current.Add(runtimes[v]);
        }
        if (current.Count > 0) steps.Add(new PlanStep([.. current], currentSub));
        return new GraphPlan([.. order.Select(v => runtimes[v])], [.. steps]);
    }

    private static bool Reaches(List<int>[] adjacency, int from, int to)
    {
        var seen = new bool[adjacency.Length];
        var stack = new Stack<int>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (n == to) return true;
            if (seen[n]) continue;
            seen[n] = true;
            foreach (int m in adjacency[n]) stack.Push(m);
        }
        return false;
    }

    /// <summary>Tarjan's algorithm (iterative): the component of each node.</summary>
    private static int[] StronglyConnected(List<int>[] adjacency, out int components)
    {
        int n = adjacency.Length;
        var indexOf = new int[n];
        var low = new int[n];
        var onStack = new bool[n];
        var component = new int[n];
        Array.Fill(indexOf, -1);
        var stack = new Stack<int>();
        int next = 0, found = 0;
        var work = new Stack<(int Node, int Edge)>();
        for (int root = 0; root < n; root++)
        {
            if (indexOf[root] >= 0) continue;
            work.Push((root, 0));
            while (work.Count > 0)
            {
                var (v, e) = work.Pop();
                if (e == 0)
                {
                    indexOf[v] = low[v] = next++;
                    stack.Push(v);
                    onStack[v] = true;
                }
                bool descended = false;
                for (; e < adjacency[v].Count; e++)
                {
                    int w = adjacency[v][e];
                    if (indexOf[w] < 0)
                    {
                        work.Push((v, e + 1));
                        work.Push((w, 0));
                        descended = true;
                        break;
                    }
                    if (onStack[w]) low[v] = Math.Min(low[v], indexOf[w]);
                }
                if (descended) continue;
                if (low[v] == indexOf[v])
                {
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack[w] = false;
                        component[w] = found;
                    }
                    while (w != v);
                    found++;
                }
                if (work.Count > 0)
                {
                    var (parent, _) = work.Peek();
                    low[parent] = Math.Min(low[parent], low[v]);
                }
            }
        }
        components = found;
        return component;
    }

    /// <summary>
    /// Processing order: the loops (components) in dependency order, each one contiguous and ordered inside by its
    /// non-feedback cables; ties go to the oldest module.
    /// </summary>
    private static int[] Order(IReadOnlyList<GraphNode> nodes, List<int>[] accepted, List<int>[] all, int[] component, int components)
    {
        int n = nodes.Count;
        var members = new List<int>[components];
        for (int c = 0; c < components; c++) members[c] = [];
        for (int i = 0; i < n; i++) members[component[i]].Add(i);

        // Components in topological order.
        var compIn = new int[components];
        var compOut = new HashSet<int>[components];
        for (int c = 0; c < components; c++) compOut[c] = [];
        for (int u = 0; u < n; u++)
        {
            foreach (int v in all[u])
            {
                int cu = component[u], cv = component[v];
                if (cu != cv && compOut[cu].Add(cv)) compIn[cv]++;
            }
        }
        var ready = new SortedSet<(int Key, int Comp)>();
        for (int c = 0; c < components; c++)
        {
            if (compIn[c] == 0) ready.Add((members[c].Min(), c));
        }
        var result = new List<int>(n);
        while (ready.Count > 0)
        {
            var first = ready.Min;
            ready.Remove(first);
            result.AddRange(OrderInside(members[first.Comp], accepted, component, first.Comp));
            foreach (int next in compOut[first.Comp])
            {
                if (--compIn[next] == 0) ready.Add((members[next].Min(), next));
            }
        }
        return [.. result];
    }

    private static IEnumerable<int> OrderInside(List<int> members, List<int>[] accepted, int[] component, int comp)
    {
        if (members.Count == 1) return members;
        var degree = members.ToDictionary(m => m, _ => 0);
        foreach (int u in members)
        {
            foreach (int v in accepted[u])
            {
                if (component[v] == comp) degree[v]++;
            }
        }
        var ready = new SortedSet<int>(members.Where(m => degree[m] == 0));
        var order = new List<int>(members.Count);
        while (ready.Count > 0)
        {
            int u = ready.Min;
            ready.Remove(u);
            order.Add(u);
            foreach (int v in accepted[u])
            {
                if (component[v] == comp && --degree[v] == 0) ready.Add(v);
            }
        }
        return order;
    }

    public void Dispose()
    {
        foreach (var r in Runtimes) r.Dispose();
    }
}
