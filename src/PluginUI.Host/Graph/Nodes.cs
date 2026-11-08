// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using PluginUI.Host.Audio;

namespace PluginUI.Host.Graph;

/// <summary>Channels of the audio device's input, as a source of the graph.</summary>
public sealed class AudioInputNode : GraphNode
{
    private int _firstChannel;
    private int _channels = 2;

    /// <summary>Creates the module (device input channels 1 and 2).</summary>
    public AudioInputNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "audio-input";

    /// <inheritdoc />
    public override string DefaultTitle => _channels == 1 ? $"Audio input {_firstChannel + 1}" : $"Audio input {_firstChannel + 1}-{_firstChannel + _channels}";

    /// <summary>First device channel (0-based).</summary>
    public int FirstChannel
    {
        get => _firstChannel;
        set => _firstChannel = Math.Max(0, value);
    }

    /// <summary>Number of channels (1 to 32); changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 1, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    /// <summary>Gain in decibels.</summary>
    public double GainDb { get; set; }

    private void UpdatePorts() => SetPorts([], [new PortDefinition("out", "Out", PortKind.Audio, _channels)]);

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        var output = context.Output(0);
        var device = context.DeviceInputChannels(_firstChannel, _channels);
        float gain = SignalSource.DbToGain(GainDb);
        for (int c = 0; c < output.ChannelCount; c++)
        {
            var target = output[c];
            if (c >= device.ChannelCount)
            {
                target.Clear();
                continue;
            }
            var source = device[c];
            for (int i = 0; i < target.Length; i++) target[i] = source[i] * gain;
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["firstChannel"] = _firstChannel;
        json["channels"] = _channels;
        json["gainDb"] = GainDb;
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        FirstChannel = GraphJson.Int(json, "firstChannel", 0);
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 1, 32);
        GainDb = GraphJson.Double(json, "gainDb", 0);
        UpdatePorts();
    }
}

/// <summary>Channels of the audio device's output: what arrives here is heard (several modules on the same channels add up).</summary>
public sealed class AudioOutputNode : GraphNode
{
    private int _firstChannel;
    private int _channels = 2;

    /// <summary>Creates the module (device output channels 1 and 2).</summary>
    public AudioOutputNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "audio-output";

    /// <inheritdoc />
    public override string DefaultTitle => _channels == 1 ? $"Audio output {_firstChannel + 1}" : $"Audio output {_firstChannel + 1}-{_firstChannel + _channels}";

    /// <summary>First device channel (0-based).</summary>
    public int FirstChannel
    {
        get => _firstChannel;
        set => _firstChannel = Math.Max(0, value);
    }

    /// <summary>Number of channels (1 to 32); changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 1, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    /// <summary>Gain in decibels.</summary>
    public double GainDb { get; set; }

    /// <summary>Silent output.</summary>
    public bool Mute { get; set; }

    /// <inheritdoc />
    protected internal override bool MetersFirstOutput => false;

    private void UpdatePorts() => SetPorts([new PortDefinition("in", "In", PortKind.Audio, _channels)], []);

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        var signal = context.Scratch(_channels);
        float gain = Mute ? 0f : SignalSource.DbToGain(GainDb);
        context.ReadAudio(0, signal, gain);
        Meter.Update(signal);
        var device = context.DeviceOutputChannels(_firstChannel, _channels);
        for (int c = 0; c < device.ChannelCount && c < signal.ChannelCount; c++)
        {
            var source = signal[c];
            var target = device[c];
            for (int i = 0; i < target.Length; i++) target[i] += source[i];
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["firstChannel"] = _firstChannel;
        json["channels"] = _channels;
        json["gainDb"] = GainDb;
        json["mute"] = Mute;
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        FirstChannel = GraphJson.Int(json, "firstChannel", 0);
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 1, 32);
        GainDb = GraphJson.Double(json, "gainDb", 0);
        Mute = GraphJson.Bool(json, "mute", false);
        UpdatePorts();
    }
}

/// <summary>Test signals (tones, noises, sweeps, impulses) as a source of the graph.</summary>
public sealed class TestSignalNode : GraphNode
{
    private int _channels = 2;

    /// <summary>Creates the module (stereo, 1 kHz sine at −20 dBFS).</summary>
    public TestSignalNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "test-signal";

    /// <inheritdoc />
    public override string DefaultTitle => "Test signal";

    /// <summary>The generator (waveform, frequency, level…).</summary>
    public TestSignalGenerator Generator { get; } = new();

    /// <summary>Number of channels (the same signal on each); changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 1, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    private void UpdatePorts() => SetPorts([], [new PortDefinition("out", "Out", PortKind.Audio, _channels)]);

    /// <inheritdoc />
    protected internal override void Prepare(double sampleRate, int maxFrames, bool realtime) => Generator.Prepare(sampleRate);

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context) => Generator.Read(context.Output(0), context.DeviceInputs);

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["channels"] = _channels;
        json["signal"] = Generator.Signal.ToString();
        json["frequency"] = Generator.Frequency;
        json["endFrequency"] = Generator.EndFrequency;
        json["sweepSeconds"] = Generator.SweepSeconds;
        json["period"] = Generator.Period;
        json["levelDb"] = Generator.LevelDb;
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 1, 32);
        Generator.Signal = Enum.TryParse<TestSignal>(GraphJson.String(json, "signal", nameof(TestSignal.Sine)), true, out var s) ? s : TestSignal.Sine;
        Generator.Frequency = GraphJson.Double(json, "frequency", 1000);
        Generator.EndFrequency = GraphJson.Double(json, "endFrequency", 20000);
        Generator.SweepSeconds = GraphJson.Double(json, "sweepSeconds", 5);
        Generator.Period = GraphJson.Double(json, "period", 1);
        Generator.LevelDb = GraphJson.Double(json, "levelDb", -20);
        UpdatePorts();
    }
}

/// <summary>An audio file (WAV) played in a loop, resampled to the device rate, as a source of the graph.</summary>
public sealed class AudioFileNode : GraphNode
{
    private int _channels = 2;
    private FileSource? _source;
    private double _sampleRate;

    /// <summary>Creates the module without a file (silent).</summary>
    public AudioFileNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "audio-file";

    /// <inheritdoc />
    public override string DefaultTitle => _source?.FileName ?? "Audio file";

    /// <summary>Path of the file (null = none).</summary>
    public string? Path { get; private set; }

    /// <summary>The file being played (null = none).</summary>
    public FileSource? Source => Volatile.Read(ref _source);

    /// <summary>Starts again at the end.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Gain in decibels.</summary>
    public double GainDb { get; set; }

    /// <summary>Paused (silence, position kept).</summary>
    public bool IsPaused { get; set; }

    /// <summary>Number of channels (a mono file feeds every channel); changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 1, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    /// <summary>Loads a WAV file (its channel count becomes the output's, at least one).</summary>
    public void Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var source = FileSource.Load(path);
        if (_sampleRate > 0) source.Prepare(_sampleRate);
        Path = System.IO.Path.GetFullPath(path);
        Volatile.Write(ref _source, source);
        Channels = Math.Max(1, source.Audio.ChannelCount);
    }

    private void UpdatePorts() => SetPorts([], [new PortDefinition("out", "Out", PortKind.Audio, _channels)]);

    /// <inheritdoc />
    protected internal override void Prepare(double sampleRate, int maxFrames, bool realtime)
    {
        _sampleRate = sampleRate;
        _source?.Prepare(sampleRate);
    }

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        var output = context.Output(0);
        var source = Volatile.Read(ref _source);
        if (source is null)
        {
            output.Clear();
            return;
        }
        source.Loop = Loop;
        source.GainDb = GainDb;
        source.IsPaused = IsPaused;
        source.Read(output, context.DeviceInputs);
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["path"] = Path;
        json["channels"] = _channels;
        json["loop"] = Loop;
        json["gainDb"] = GainDb;
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 1, 32);
        Loop = GraphJson.Bool(json, "loop", true);
        GainDb = GraphJson.Double(json, "gainDb", 0);
        UpdatePorts();
        string? path = GraphJson.String(json, "path", null);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            int channels = _channels;
            Open(path);
            _channels = channels; // the saved width wins over the file's
            UpdatePorts();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            Path = path;
            throw new GraphLoadException($"The audio file '{path}' could not be read: {ex.Message}", ex);
        }
    }
}

/// <summary>Messages of the MIDI input port, as a source of the graph (see <see cref="PluginGraph.PostMidiInput"/>).</summary>
public sealed class MidiInputNode : GraphNode
{
    private readonly MidiEventQueue _queue = new(1024);

    /// <summary>Creates the module.</summary>
    public MidiInputNode() => SetPorts([], [new PortDefinition("midi", "MIDI", PortKind.Midi)]);

    /// <inheritdoc />
    public override string TypeKey => "midi-input";

    /// <inheritdoc />
    public override string DefaultTitle => "MIDI input";

    /// <summary>Only this channel (1 to 16) passes; 0 = every channel.</summary>
    public int ChannelFilter { get; set; }

    /// <summary>Queues a message for the next block (any thread; its sample offset is kept, 0 for live input).</summary>
    public void Post(in MidiEvent e)
    {
        if (ChannelFilter == 0 || e.Channel == ChannelFilter - 1) _queue.Enqueue(e);
    }

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        while (_queue.TryDequeue(out var e)) context.SendMidi(0, e);
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json) => json["channelFilter"] = ChannelFilter;

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json) => ChannelFilter = Math.Clamp(GraphJson.Int(json, "channelFilter", 0), 0, 16);
}

/// <summary>An on-screen (or computer) keyboard, as a MIDI source of the graph.</summary>
public sealed class KeyboardNode : GraphNode
{
    private readonly MidiEventQueue _queue = new(1024);

    /// <summary>Creates the module.</summary>
    public KeyboardNode() => SetPorts([], [new PortDefinition("midi", "MIDI", PortKind.Midi)]);

    /// <inheritdoc />
    public override string TypeKey => "keyboard";

    /// <inheritdoc />
    public override string DefaultTitle => "Keyboard";

    /// <summary>MIDI channel of the notes (1 to 16).</summary>
    public int Channel { get; set; } = 1;

    /// <summary>Plays a note (any thread).</summary>
    public void NoteOn(int note, int velocity = 100) => _queue.Enqueue(MidiEvent.NoteOn(0, Channel - 1, note, velocity));

    /// <summary>Releases a note (any thread).</summary>
    public void NoteOff(int note) => _queue.Enqueue(MidiEvent.NoteOff(0, Channel - 1, note));

    /// <summary>Sends any message (any thread).</summary>
    public void Send(in MidiEvent e) => _queue.Enqueue(e with { SampleOffset = 0 });

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        while (_queue.TryDequeue(out var e)) context.SendMidi(0, e);
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json) => json["channel"] = Channel;

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json) => Channel = Math.Clamp(GraphJson.Int(json, "channel", 1), 1, 16);
}

/// <summary>The MIDI output port: what arrives here goes to <see cref="PluginGraph.MidiOutput"/> (sample offsets within the device block).</summary>
public sealed class MidiOutputNode : GraphNode
{
    /// <summary>Creates the module.</summary>
    public MidiOutputNode() => SetPorts([new PortDefinition("midi", "MIDI", PortKind.Midi)], []);

    /// <inheritdoc />
    public override string TypeKey => "midi-output";

    /// <inheritdoc />
    public override string DefaultTitle => "MIDI output";

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        var sink = Graph?.MidiOutput;
        var events = context.ReadMidi(0);
        if (sink is null) return;
        foreach (var e in events) sink.Send(e with { SampleOffset = e.SampleOffset + context.BlockOffset });
    }
}

/// <summary>Gain (and polarity, mute) on an audio signal.</summary>
public sealed class GainNode : GraphNode
{
    private int _channels = 2;

    /// <summary>Creates the module (stereo, 0 dB).</summary>
    public GainNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "gain";

    /// <inheritdoc />
    public override string DefaultTitle => "Gain";

    /// <summary>Gain in decibels.</summary>
    public double GainDb { get; set; }

    /// <summary>Inverts the polarity.</summary>
    public bool Invert { get; set; }

    /// <summary>Silent output.</summary>
    public bool Mute { get; set; }

    /// <summary>Number of channels; changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 1, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    private void UpdatePorts() => SetPorts([new PortDefinition("in", "In", PortKind.Audio, _channels)], [new PortDefinition("out", "Out", PortKind.Audio, _channels)]);

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        float gain = Mute ? 0f : SignalSource.DbToGain(GainDb) * (Invert ? -1f : 1f);
        context.ReadAudio(0, context.Output(0), gain);
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["channels"] = _channels;
        json["gainDb"] = GainDb;
        json["invert"] = Invert;
        json["mute"] = Mute;
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 1, 32);
        GainDb = GraphJson.Double(json, "gainDb", 0);
        Invert = GraphJson.Bool(json, "invert", false);
        Mute = GraphJson.Bool(json, "mute", false);
        UpdatePorts();
    }
}

/// <summary>
/// Adds audio signals: the only way to join several cables, since an input takes one cable. Each input has a gain; with
/// <see cref="CompensateLatency"/>, the inputs are delayed so that signals that took paths of different latency arrive
/// aligned.
/// </summary>
public sealed class AudioSumNode : GraphNode
{
    private int _channels = 2;
    private double[] _gainsDb = [0, 0];

    /// <summary>Creates the module (two stereo inputs).</summary>
    public AudioSumNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "audio-sum";

    /// <inheritdoc />
    public override string DefaultTitle => "Audio sum";

    /// <summary>Number of inputs (2 to 32); changing it rebuilds the graph.</summary>
    public int InputCount
    {
        get => _gainsDb.Length;
        set
        {
            value = Math.Clamp(value, 2, 32);
            if (value == _gainsDb.Length) return;
            var gains = new double[value];
            Array.Copy(_gainsDb, gains, Math.Min(value, _gainsDb.Length));
            _gainsDb = gains;
            UpdatePorts();
            PortsChanged();
        }
    }

    /// <summary>Number of channels of every input and of the output; changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 1, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    /// <summary>Delays the inputs so that they arrive aligned (default <c>true</c>); changing it rebuilds the graph.</summary>
    public bool CompensateLatency
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PortsChanged();
        }
    } = true;

    /// <summary>Gain of input <paramref name="input"/> in decibels.</summary>
    public double GetGainDb(int input) => _gainsDb[input];

    /// <summary>Sets the gain of input <paramref name="input"/> in decibels (takes effect at the next block).</summary>
    public void SetGainDb(int input, double db) => Volatile.Write(ref _gainsDb[input], db);

    private void UpdatePorts() => SetPorts(
        Enumerable.Range(1, _gainsDb.Length).Select(i => new PortDefinition($"in{i}", $"In {i}", PortKind.Audio, _channels)),
        [new PortDefinition("out", "Out", PortKind.Audio, _channels)]);

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        var output = context.Output(0);
        output.Clear();
        var gains = _gainsDb;
        for (int i = 0; i < gains.Length && i < Inputs.Count; i++)
        {
            if (context.IsConnected(i)) context.ReadAudio(i, output, SignalSource.DbToGain(Volatile.Read(ref gains[i])), add: true);
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["channels"] = _channels;
        json["compensateLatency"] = CompensateLatency;
        var gains = new JsonArray();
        foreach (double g in _gainsDb) gains.Add((JsonNode)JsonValue.Create(g));
        json["gainsDb"] = gains;
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 1, 32);
        CompensateLatency = GraphJson.Bool(json, "compensateLatency", true);
        var gains = json["gainsDb"] is JsonArray a ? a.Select(v => v is JsonValue j && j.TryGetValue(out double d) ? d : 0.0).ToArray() : [0.0, 0.0];
        _gainsDb = gains.Length >= 2 ? gains[..Math.Min(32, gains.Length)] : [0.0, 0.0];
        UpdatePorts();
    }
}

/// <summary>Merges MIDI streams (the only way to join several MIDI cables). With <see cref="CompensateLatency"/>, inputs are aligned like audio.</summary>
public sealed class MidiMergeNode : GraphNode
{
    private readonly MidiEvent[] _merged = new MidiEvent[1024];
    private int _inputs = 2;

    /// <summary>Creates the module (two inputs).</summary>
    public MidiMergeNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "midi-merge";

    /// <inheritdoc />
    public override string DefaultTitle => "MIDI merge";

    /// <summary>Number of inputs (2 to 32); changing it rebuilds the graph.</summary>
    public int InputCount
    {
        get => _inputs;
        set
        {
            value = Math.Clamp(value, 2, 32);
            if (value == _inputs) return;
            _inputs = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    /// <summary>Delays the inputs so that they arrive aligned (default <c>true</c>); changing it rebuilds the graph.</summary>
    public bool CompensateLatency
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PortsChanged();
        }
    } = true;

    private void UpdatePorts() => SetPorts(
        Enumerable.Range(1, _inputs).Select(i => new PortDefinition($"midi{i}", $"MIDI {i}", PortKind.Midi)),
        [new PortDefinition("midi", "MIDI", PortKind.Midi)]);

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        // Gather every input, then send in time order (stable: equal offsets keep the order of the inputs).
        var merged = _merged;
        int n = 0;
        for (int i = 0; i < Inputs.Count; i++)
        {
            foreach (var e in context.ReadMidi(i))
            {
                if (n == merged.Length) break;
                int j = n++;
                while (j > 0 && merged[j - 1].SampleOffset > e.SampleOffset)
                {
                    merged[j] = merged[j - 1];
                    j--;
                }
                merged[j] = e;
            }
        }
        for (int i = 0; i < n; i++) context.SendMidi(0, merged[i]);
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json)
    {
        json["inputs"] = _inputs;
        json["compensateLatency"] = CompensateLatency;
    }

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        _inputs = Math.Clamp(GraphJson.Int(json, "inputs", 2), 2, 32);
        CompensateLatency = GraphJson.Bool(json, "compensateLatency", true);
        UpdatePorts();
    }
}

/// <summary>Muxer: joins mono signals into one signal of several channels (two mono cables into a stereo one…).</summary>
public sealed class MuxNode : GraphNode
{
    private int _channels = 2;

    /// <summary>Creates the module (two mono inputs, a stereo output).</summary>
    public MuxNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "mux";

    /// <inheritdoc />
    public override string DefaultTitle => "Muxer";

    /// <summary>Number of mono inputs, and of channels of the output (2 to 32); changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 2, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    private void UpdatePorts() => SetPorts(
        Enumerable.Range(1, _channels).Select(i => new PortDefinition($"in{i}", ChannelName(i, _channels), PortKind.Audio, 1)),
        [new PortDefinition("out", "Out", PortKind.Audio, _channels)]);

    internal static string ChannelName(int channel, int channels) => channels == 2 ? (channel == 1 ? "L" : "R") : $"Ch {channel}";

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        var output = context.Output(0);
        for (int c = 0; c < output.ChannelCount; c++)
        {
            var one = context.Scratch(1);
            context.ReadAudio(c, one);
            one[0].CopyTo(output[c]);
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json) => json["channels"] = _channels;

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 2, 32);
        UpdatePorts();
    }
}

/// <summary>Demuxer: splits a signal of several channels into mono signals (a stereo cable into two mono ones…).</summary>
public sealed class DemuxNode : GraphNode
{
    private int _channels = 2;

    /// <summary>Creates the module (a stereo input, two mono outputs).</summary>
    public DemuxNode() => UpdatePorts();

    /// <inheritdoc />
    public override string TypeKey => "demux";

    /// <inheritdoc />
    public override string DefaultTitle => "Demuxer";

    /// <summary>Number of channels of the input, and of mono outputs (2 to 32); changing it rebuilds the graph.</summary>
    public int Channels
    {
        get => _channels;
        set
        {
            value = Math.Clamp(value, 2, 32);
            if (value == _channels) return;
            _channels = value;
            UpdatePorts();
            PortsChanged();
        }
    }

    /// <inheritdoc />
    protected internal override bool MetersFirstOutput => false;

    private void UpdatePorts()
    {
        SetPorts([new PortDefinition("in", "In", PortKind.Audio, _channels)],
            Enumerable.Range(1, _channels).Select(i => new PortDefinition($"out{i}", MuxNode.ChannelName(i, _channels), PortKind.Audio, 1)));
        Meter.Resize(Math.Min(_channels, 16)); // the meter shows the input
    }

    /// <inheritdoc />
    protected internal override void Process(in NodeContext context)
    {
        var signal = context.Scratch(_channels);
        context.ReadAudio(0, signal);
        Meter.Update(signal);
        for (int c = 0; c < Outputs.Count; c++)
        {
            var output = context.Output(c);
            if (output.ChannelCount == 0) continue;
            if (c < signal.ChannelCount) signal[c].CopyTo(output[0]);
            else output[0].Clear();
        }
    }

    /// <inheritdoc />
    protected internal override void WriteSettings(JsonObject json) => json["channels"] = _channels;

    /// <inheritdoc />
    protected internal override void ReadSettings(JsonObject json)
    {
        _channels = Math.Clamp(GraphJson.Int(json, "channels", 2), 2, 32);
        UpdatePorts();
    }
}
