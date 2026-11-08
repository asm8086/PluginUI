// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using PluginUI.Host.Audio;
using PluginUI.Host.Catalog;

namespace PluginUI.Host.Graph;

/// <summary>What a port carries.</summary>
public enum PortKind
{
    /// <summary>Audio channels.</summary>
    Audio,
    /// <summary>MIDI messages.</summary>
    Midi,
}

/// <summary>A port a node declares (see <see cref="GraphNode.SetPorts"/>).</summary>
/// <param name="Key">Identifier, stable across sessions and unique among the node's inputs (or outputs): saved in graph files.</param>
/// <param name="Name">Name shown to the user.</param>
/// <param name="Kind">Audio or MIDI.</param>
/// <param name="Channels">Audio channels (ignored for MIDI).</param>
public readonly record struct PortDefinition(string Key, string Name, PortKind Kind, int Channels = 0);

/// <summary>An input or output of a <see cref="GraphNode"/>.</summary>
public sealed class GraphPort
{
    internal GraphPort(GraphNode node, bool isInput, int index, PortDefinition definition)
    {
        Node = node;
        IsInput = isInput;
        Index = index;
        Key = definition.Key;
        Name = definition.Name;
        Kind = definition.Kind;
        Channels = definition.Kind == PortKind.Audio ? Math.Max(1, definition.Channels) : 0;
    }

    /// <summary>The node.</summary>
    public GraphNode Node { get; }

    /// <summary><c>true</c> for an input, <c>false</c> for an output.</summary>
    public bool IsInput { get; }

    /// <summary>Position among the node's inputs (or outputs).</summary>
    public int Index { get; }

    /// <summary>Identifier saved in graph files.</summary>
    public string Key { get; }

    /// <summary>Name shown to the user.</summary>
    public string Name { get; }

    /// <summary>Audio or MIDI.</summary>
    public PortKind Kind { get; }

    /// <summary>Audio channels (0 for MIDI).</summary>
    public int Channels { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Node.DisplayTitle}.{Key}";
}

/// <summary>A cable from an output to an input. An input takes at most one cable; an output feeds any number.</summary>
public sealed class GraphConnection
{
    internal GraphConnection(GraphNode source, string sourceKey, GraphNode target, string targetKey, long sequence)
    {
        Source = source;
        SourceKey = sourceKey;
        Target = target;
        TargetKey = targetKey;
        Sequence = sequence;
    }

    /// <summary>Node of the output.</summary>
    public GraphNode Source { get; }

    /// <summary>Key of the output port.</summary>
    public string SourceKey { get; }

    /// <summary>Node of the input.</summary>
    public GraphNode Target { get; }

    /// <summary>Key of the input port.</summary>
    public string TargetKey { get; }

    /// <summary>The output port (null when the node no longer has it).</summary>
    public GraphPort? SourcePort => Source.FindOutput(SourceKey);

    /// <summary>The input port (null when the node no longer has it).</summary>
    public GraphPort? TargetPort => Target.FindInput(TargetKey);

    /// <summary>Order of creation: the most recent cable of a loop is the one that breaks it.</summary>
    internal long Sequence { get; set; }

    /// <summary>
    /// <c>true</c> when the cable closes a loop: it delivers the signal one block (or one loop sub-block) late, see
    /// <see cref="PluginGraph.LoopBlockSize"/>. Updated by <see cref="PluginGraph.Rebuild"/>.
    /// </summary>
    public bool IsFeedback { get; internal set; }

    /// <summary>
    /// Delay in samples the cable adds: the loop delay of a feedback cable, or the latency compensation of an input of a
    /// summing module. Updated by <see cref="PluginGraph.Rebuild"/> (0 before the graph is prepared for loops).
    /// </summary>
    public int DelaySamples { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{Source.DisplayTitle}.{SourceKey} → {Target.DisplayTitle}.{TargetKey}";
}

/// <summary>Where a plug-in comes from, as saved in a graph file (enough to find it again in a catalog).</summary>
/// <param name="Format">VST 3 or VST 2.</param>
/// <param name="Path">Module path.</param>
/// <param name="ClassId">VST 3 class identifier or VST 2 unique identifier (may be empty).</param>
/// <param name="Name">Plug-in name.</param>
/// <param name="Vendor">Vendor.</param>
public sealed record PluginReference(PluginFormat Format, string Path, string ClassId, string Name, string Vendor)
{
    /// <summary>The reference of a catalog entry.</summary>
    public static PluginReference From(PluginDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return new(description.Format, description.Path, description.ClassId, description.Name, description.Vendor);
    }

    /// <summary>The catalog entry this reference designates (same module and class; else the same name), or null.</summary>
    public PluginDescription? FindIn(PluginCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var usable = catalog.Plugins.Where(p => p.Status != PluginStatus.Failed && p.Format == Format).ToList();
        return usable.FirstOrDefault(p => string.Equals(p.Path, Path, comparison) && (ClassId.Length == 0 || string.Equals(p.ClassId, ClassId, StringComparison.OrdinalIgnoreCase)))
            ?? usable.FirstOrDefault(p => ClassId.Length > 0 && string.Equals(p.ClassId, ClassId, StringComparison.OrdinalIgnoreCase))
            ?? usable.FirstOrDefault(p => string.Equals(p.Name, Name, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Vendor, Vendor, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// A module of a <see cref="PluginGraph"/>: a plug-in, an audio or MIDI input or output, a test signal, a sum, a gain…
/// It declares its ports and processes them block by block on the audio thread.
/// </summary>
/// <remarks>
/// <para>
/// Everything except <see cref="Process"/> runs on the thread that edits the graph (the UI thread). A property that
/// changes the ports calls <see cref="SetPorts"/>, then the graph is rebuilt; properties read by <see cref="Process"/>
/// (gains, switches) may change at any time and take effect at the next block.
/// </para>
/// <para>
/// To add a module type, derive from this class, give it a unique <see cref="TypeKey"/>, save its settings in
/// <see cref="WriteSettings"/> / <see cref="ReadSettings"/>, and register it with <see cref="PluginGraph.RegisterNodeType"/>
/// so graph files can create it.
/// </para>
/// </remarks>
public abstract class GraphNode
{
    private GraphPort[] _inputs = [];
    private GraphPort[] _outputs = [];

    /// <summary>Identifier, unique in the graph (given by <see cref="PluginGraph.Add{T}"/>).</summary>
    public int Id { get; internal set; }

    /// <summary>The graph the node belongs to (null before it is added).</summary>
    public PluginGraph? Graph { get; internal set; }

    /// <summary>Type saved in graph files ("plugin", "gain", "audio-sum"…).</summary>
    public abstract string TypeKey { get; }

    /// <summary>Title used when <see cref="Title"/> is empty (the plug-in name, "Gain"…).</summary>
    public abstract string DefaultTitle { get; }

    /// <summary>Title chosen by the user (null = <see cref="DefaultTitle"/>).</summary>
    public string? Title { get; set; }

    /// <summary>The title shown.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title;

    /// <summary>Horizontal position in the graph view.</summary>
    public float X { get; set; }

    /// <summary>Vertical position in the graph view.</summary>
    public float Y { get; set; }

    /// <summary>Inputs.</summary>
    public IReadOnlyList<GraphPort> Inputs => _inputs;

    /// <summary>Outputs.</summary>
    public IReadOnlyList<GraphPort> Outputs => _outputs;

    /// <summary>The input with this key, or null.</summary>
    public GraphPort? FindInput(string key) => Array.Find(_inputs, p => p.Key == key);

    /// <summary>The output with this key, or null.</summary>
    public GraphPort? FindOutput(string key) => Array.Find(_outputs, p => p.Key == key);

    /// <summary>Latency the node adds between its inputs and its outputs, in samples.</summary>
    public virtual int LatencySamples => 0;

    /// <summary>
    /// Latency from the sources of the graph to this node's outputs, in samples (the plug-ins and compensations on the way).
    /// Updated by <see cref="PluginGraph.Rebuild"/>.
    /// </summary>
    public int PathLatencySamples { get; internal set; }

    /// <summary><c>true</c> when the node belongs to a loop (see <see cref="GraphConnection.IsFeedback"/>).</summary>
    public bool IsInLoop { get; internal set; }

    /// <summary>Peaks of the node's first audio output (of its input for an audio output module).</summary>
    public PeakMeter Meter { get; } = new();

    /// <summary><c>true</c> after <see cref="Process"/> threw; the node then stays silent until it is prepared again.</summary>
    public bool HasFailed { get; internal set; }

    /// <summary>The exception that made the node fail.</summary>
    public Exception? Failure { get; internal set; }

    /// <summary>
    /// <c>true</c> (default) when the graph updates <see cref="Meter"/> from the first audio output after each block; a
    /// node returning <c>false</c> updates it itself.
    /// </summary>
    protected internal virtual bool MetersFirstOutput => true;

    /// <summary>Declares the ports (keys keep the cables: a cable whose port disappears is removed at the next rebuild).</summary>
    protected void SetPorts(IEnumerable<PortDefinition> inputs, IEnumerable<PortDefinition> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        _inputs = [.. inputs.Select((d, i) => new GraphPort(this, true, i, d))];
        _outputs = [.. outputs.Select((d, i) => new GraphPort(this, false, i, d))];
        var first = _outputs.FirstOrDefault(p => p.Kind == PortKind.Audio) ?? _inputs.FirstOrDefault(p => p.Kind == PortKind.Audio);
        Meter.Resize(Math.Min(first?.Channels ?? 0, 16));
    }

    /// <summary>Applies a change of the ports to the graph (call after <see cref="SetPorts"/> once the node is in a graph).</summary>
    protected void PortsChanged() => Graph?.Rebuild();

    /// <summary>Prepares processing (not on the audio thread).</summary>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="maxFrames">Largest block <see cref="Process"/> receives.</param>
    /// <param name="realtime"><c>true</c> for live processing, <c>false</c> for offline rendering.</param>
    protected internal virtual void Prepare(double sampleRate, int maxFrames, bool realtime)
    {
    }

    /// <summary>Stops processing (the node left the graph, or the graph was released).</summary>
    protected internal virtual void Release()
    {
    }

    /// <summary>Processes one block (audio thread: no allocation, no lock waits).</summary>
    protected internal abstract void Process(in NodeContext context);

    /// <summary>Saves the node's settings (the base fields — id, type, title, position — are written by the graph).</summary>
    protected internal virtual void WriteSettings(JsonObject json)
    {
    }

    /// <summary>Restores the settings from <see cref="WriteSettings"/> (before the node is added to the graph).</summary>
    protected internal virtual void ReadSettings(JsonObject json)
    {
    }

    /// <inheritdoc />
    public override string ToString() => $"{DisplayTitle} (#{Id})";
}
