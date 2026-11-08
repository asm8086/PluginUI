// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using PluginUI.Host.Audio;

namespace PluginUI.Host.Graph;

/// <summary>
/// A plug-in in the graph: one audio port per bus (main, side chain…, with its channels), a MIDI input and output when
/// the plug-in has them.
/// </summary>
/// <remarks>
/// The graph does not own the plug-in: removing the node stops it, the caller disposes of it. The node follows the
/// plug-in's restarts (latency, buses): ports and graph are rebuilt.
/// </remarks>
public sealed unsafe class PluginNode : GraphNode
{
    private readonly MidiEventQueue _midi = new(1024);
    private PortMap[] _audioInputs = [];
    private PortMap[] _audioOutputs = [];
    private int _midiInput = -1, _midiOutput = -1;
    private int _mainInput = -1, _mainOutput = -1;
    private nint[] _inputPointers = [];
    private nint[] _outputPointers = [];

    /// <summary>Wraps a loaded plug-in.</summary>
    /// <param name="plugin">The plug-in.</param>
    /// <param name="reference">Where it comes from (saved in graph files to load it again).</param>
    public PluginNode(PluginInstance plugin, PluginReference? reference = null)
    {
        Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        Reference = reference ?? new PluginReference(plugin.Format, string.Empty, string.Empty, plugin.Name, plugin.Vendor);
        UpdatePorts();
        plugin.Restarted += OnRestarted;
    }

    private readonly record struct PortMap(int Port, int FirstChannel, int Channels, bool IsAuxiliary);

    /// <summary>The plug-in.</summary>
    public PluginInstance Plugin { get; }

    /// <summary>Where the plug-in comes from.</summary>
    public PluginReference Reference { get; set; }

    /// <inheritdoc />
    public override string TypeKey => "plugin";

    /// <inheritdoc />
    public override string DefaultTitle => Plugin.Name;

    /// <inheritdoc />
    public override int LatencySamples => Plugin.LatencySamples;

    /// <summary>The main input goes straight to the main output (MIDI input to MIDI output); the plug-in is not processed.</summary>
    public bool Bypass { get; set; }

    /// <summary>Silent outputs (the plug-in still runs).</summary>
    public bool Mute { get; set; }

    /// <summary>Queues a MIDI message for the plug-in's next block (any thread: the keyboard of its window).</summary>
    public void SendMidi(in MidiEvent e) => _midi.Enqueue(e with { SampleOffset = 0 });

    private void OnRestarted(PluginInstance plugin) => Graph?.RefreshNode(this);

    /// <summary>Reads the plug-in's buses again (after a restart of the plug-in).</summary>
    internal void UpdatePorts()
    {
        var inputs = new List<PortDefinition>();
        var outputs = new List<PortDefinition>();
        var audioIn = new List<PortMap>();
        var audioOut = new List<PortMap>();
        int first = 0;
        var buses = Plugin.InputBuses;
        _mainInput = -1;
        for (int b = 0; b < buses.Count; b++)
        {
            var bus = buses[b];
            if (bus.ChannelCount > 0)
            {
                if (!bus.IsAuxiliary && _mainInput < 0) _mainInput = inputs.Count;
                audioIn.Add(new PortMap(inputs.Count, first, bus.ChannelCount, bus.IsAuxiliary));
                inputs.Add(new PortDefinition($"in{b}", string.IsNullOrEmpty(bus.Name) ? $"In {b + 1}" : bus.Name, PortKind.Audio, bus.ChannelCount));
            }
            first += bus.ChannelCount;
        }
        _midiInput = Plugin.EventInputCount > 0 ? inputs.Count : -1;
        if (_midiInput >= 0) inputs.Add(new PortDefinition("midi-in", "MIDI", PortKind.Midi));

        first = 0;
        buses = Plugin.OutputBuses;
        _mainOutput = -1;
        for (int b = 0; b < buses.Count; b++)
        {
            var bus = buses[b];
            if (bus.ChannelCount > 0)
            {
                if (!bus.IsAuxiliary && _mainOutput < 0) _mainOutput = outputs.Count;
                audioOut.Add(new PortMap(outputs.Count, first, bus.ChannelCount, bus.IsAuxiliary));
                outputs.Add(new PortDefinition($"out{b}", string.IsNullOrEmpty(bus.Name) ? $"Out {b + 1}" : bus.Name, PortKind.Audio, bus.ChannelCount));
            }
            first += bus.ChannelCount;
        }
        _midiOutput = Plugin.EventOutputCount > 0 ? outputs.Count : -1;
        if (_midiOutput >= 0) outputs.Add(new PortDefinition("midi-out", "MIDI", PortKind.Midi));

        _audioInputs = [.. audioIn];
        _audioOutputs = [.. audioOut];
        SetPorts(inputs, outputs);
    }

    /// <inheritdoc />
    protected internal override void Prepare(double sampleRate, int maxFrames, bool realtime)
    {
        Plugin.Prepare(sampleRate, maxFrames, realtime);
        FreePointers();
        _inputPointers = [.. _audioInputs.Select(m => Pointers(m, input: true))];
        _outputPointers = [.. _audioOutputs.Select(m => Pointers(m, input: false))];
    }

    private nint Pointers(PortMap map, bool input)
    {
        var pointers = (float**)NativeMemory.Alloc((nuint)Math.Max(1, map.Channels), (nuint)sizeof(float*));
        for (int c = 0; c < map.Channels; c++) pointers[c] = input ? Plugin.InputPointer(map.FirstChannel + c) : Plugin.OutputPointer(map.FirstChannel + c);
        return (nint)pointers;
    }

    /// <inheritdoc />
    protected internal override void Release()
    {
        Plugin.Stop();
        FreePointers();
    }

    private void FreePointers()
    {
        foreach (nint p in _inputPointers) NativeMemory.Free((void*)p);
        foreach (nint p in _outputPointers) NativeMemory.Free((void*)p);
        _inputPointers = [];
        _outputPointers = [];
    }

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        int frames = context.Frames;
        ReadOnlySpan<MidiEvent> midiIn = _midiInput >= 0 ? context.ReadMidi(_midiInput) : [];
        if (Bypass)
        {
            for (int k = 0; k < _audioOutputs.Length; k++)
            {
                var output = context.Output(_audioOutputs[k].Port);
                if (_audioOutputs[k].Port == _mainOutput && _mainInput >= 0 && !Mute) context.ReadAudio(_mainInput, output);
                else output.Clear();
            }
            if (_midiOutput >= 0)
            {
                foreach (var e in midiIn) context.SendMidi(_midiOutput, e);
            }
            while (_midi.TryDequeue(out _))
            {
            }
            return;
        }

        var plugin = Plugin;
        for (int k = 0; k < _audioInputs.Length && k < _inputPointers.Length; k++)
        {
            var map = _audioInputs[k];
            context.ReadAudio(map.Port, new AudioBuffers((float**)_inputPointers[k], map.Channels, frames));
        }
        foreach (var e in midiIn) plugin.AddMidiEvent(e);
        while (_midi.TryDequeue(out var queued)) plugin.AddMidiEvent(queued);

        plugin.Process(frames);

        for (int k = 0; k < _audioOutputs.Length && k < _outputPointers.Length; k++)
        {
            var map = _audioOutputs[k];
            var output = context.Output(map.Port);
            if (Mute)
            {
                output.Clear();
                continue;
            }
            var source = new AudioBuffers((float**)_outputPointers[k], map.Channels, frames);
            for (int c = 0; c < output.ChannelCount; c++) source[c].CopyTo(output[c]);
        }
        if (_midiOutput >= 0)
        {
            foreach (var e in plugin.OutputMidi) context.SendMidi(_midiOutput, e);
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["plugin"] = GraphJson.Write(Reference);
        json["state"] = Convert.ToBase64String(Plugin.GetState());
        json["bypass"] = Bypass;
        json["mute"] = Mute;
        json["ports"] = GraphJson.WritePorts(this);
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        Bypass = GraphJson.Bool(json, "bypass", false);
        Mute = GraphJson.Bool(json, "mute", false);
        string? state = GraphJson.String(json, "state", null);
        if (!string.IsNullOrEmpty(state)) Plugin.SetState(Convert.FromBase64String(state));
        UpdatePorts();
    }
}

/// <summary>
/// A plug-in of a graph file that could not be loaded (not installed, failed): it keeps its place, its ports, its cables
/// and its saved state, and stays silent. Saving the graph writes it back unchanged.
/// </summary>
public sealed class MissingPluginNode : GraphNode
{
    private readonly JsonObject _saved;

    internal MissingPluginNode(JsonObject saved, PluginReference? reference, string reason)
    {
        _saved = (JsonObject)saved.DeepClone();
        Reference = reference;
        Reason = reason;
        var (inputs, outputs) = GraphJson.ReadPorts(saved["ports"] as JsonObject);
        SetPorts(inputs, outputs);
    }

    /// <summary>The plug-in that was expected (null when the file does not say).</summary>
    public PluginReference? Reference { get; }

    /// <summary>Why it could not be loaded.</summary>
    public string Reason { get; }

    /// <inheritdoc />
    public override string TypeKey => "plugin";

    /// <inheritdoc />
    public override string DefaultTitle => Reference?.Name is { Length: > 0 } name ? name : "Missing plug-in";

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        for (int i = 0; i < Outputs.Count; i++)
        {
            if (Outputs[i].Kind == PortKind.Audio) context.Output(i).Clear();
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        foreach (var (key, value) in _saved)
        {
            if (!GraphJson.IsBaseField(key)) json[key] = value?.DeepClone();
        }
    }
}
