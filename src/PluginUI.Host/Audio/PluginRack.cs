// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace PluginUI.Host.Audio;

/// <summary>
/// Plays plug-ins live on an audio device: each <see cref="RackSlot"/> feeds a plug-in from its <see cref="SignalSource"/>,
/// delivers its MIDI messages, and adds its output to the device output. Give it to <see cref="AudioDriver.Start"/>.
/// </summary>
/// <remarks>
/// <para>
/// Routing: the source fills the plug-in's main inputs (auxiliary inputs receive silence); output channel <c>c</c> of
/// the plug-in goes to device channel <c>c</c>, a mono output to the first two device channels. Slots can be added and
/// removed while the device runs; when <see cref="Remove"/> returns, the plug-in is not processing any more.
/// </para>
/// <para>
/// The audio thread never waits: during a change of the slots (a few microseconds) it outputs one block of silence.
/// </para>
/// </remarks>
public sealed class PluginRack : IAudioRenderer
{
    private readonly Lock _gate = new();
    private RackSlot[] _slots = [];
    private bool _prepared;

    /// <summary>Sample rate of the device (0 before the device starts).</summary>
    public double SampleRate { get; private set; }

    /// <summary>Largest block of the device.</summary>
    public int MaxFrames { get; private set; }

    /// <summary>Input channels of the device.</summary>
    public int InputChannels { get; private set; }

    /// <summary>Output channels of the device.</summary>
    public int OutputChannels { get; private set; }

    /// <summary>Peaks of the device output.</summary>
    public PeakMeter OutputMeter { get; } = new();

    /// <summary>Master gain in decibels.</summary>
    public double MasterGainDb { get; set; }

    /// <summary>The slots, in the order they were added.</summary>
    public IReadOnlyList<RackSlot> Slots => Volatile.Read(ref _slots);

    /// <summary>A slot stopped after its plug-in threw while processing (raised on the audio thread).</summary>
    public event Action<RackSlot, Exception>? SlotFailed;

    /// <summary>Adds a plug-in (prepared at the device rate when the device runs).</summary>
    public RackSlot Add(PluginInstance plugin, SignalSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var slot = new RackSlot(plugin, source ?? SilenceSource.Instance);
        lock (_gate)
        {
            if (_prepared) slot.Prepare(SampleRate, MaxFrames);
            _slots = [.. _slots, slot];
        }
        return slot;
    }

    /// <summary>Removes a slot and stops its plug-in (the plug-in is not released).</summary>
    public void Remove(RackSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        lock (_gate)
        {
            _slots = Array.FindAll(_slots, s => !ReferenceEquals(s, slot));
        }
        slot.Release();
    }

    /// <inheritdoc />
    public void Prepare(double sampleRate, int maxFrames, int inputChannels, int outputChannels)
    {
        lock (_gate)
        {
            SampleRate = sampleRate;
            MaxFrames = maxFrames;
            InputChannels = inputChannels;
            OutputChannels = outputChannels;
            OutputMeter.Resize(outputChannels);
            foreach (var slot in _slots) slot.Prepare(sampleRate, maxFrames);
            _prepared = true;
        }
    }

    /// <inheritdoc />
    public void Render(in AudioBuffers inputs, in AudioBuffers outputs)
    {
        outputs.Clear();
        if (!_gate.TryEnter()) return;
        try
        {
            foreach (var slot in _slots)
            {
                if (slot.HasFailed) continue;
                try
                {
                    slot.Render(inputs, outputs);
                }
                catch (Exception ex)
                {
                    slot.HasFailed = true;
                    SlotFailed?.Invoke(slot, ex);
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
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>Adds <paramref name="source"/> × <paramref name="gain"/> to <paramref name="destination"/> (mono to the first two channels).</summary>
    internal static void Mix(in AudioBuffers source, in AudioBuffers destination, float gain)
    {
        if (source.ChannelCount == 0 || destination.ChannelCount == 0) return;
        if (source.ChannelCount == 1)
        {
            var s = source[0];
            for (int c = 0; c < Math.Min(2, destination.ChannelCount); c++) Add(s, destination[c], gain);
            return;
        }
        for (int c = 0; c < Math.Min(source.ChannelCount, destination.ChannelCount); c++) Add(source[c], destination[c], gain);
    }

    private static void Add(Span<float> source, Span<float> destination, float gain)
    {
        for (int i = 0; i < destination.Length; i++) destination[i] += source[i] * gain;
    }
}

/// <summary>One plug-in of a <see cref="PluginRack"/>: its source, its MIDI input, its gain and its output meter.</summary>
public sealed unsafe class RackSlot
{
    private readonly MidiEventQueue _midi = new(1024);
    private SignalSource _source;
    private float** _inputs;
    private float** _outputs;
    private int _mainInputs, _mainOutputs;
    private double _sampleRate;

    internal RackSlot(PluginInstance plugin, SignalSource source)
    {
        Plugin = plugin;
        _source = source;
    }

    /// <summary>The plug-in.</summary>
    public PluginInstance Plugin { get; }

    /// <summary>What feeds the plug-in's main inputs (changing it takes effect at the next block).</summary>
    public SignalSource Source
    {
        get => Volatile.Read(ref _source);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_sampleRate > 0) value.Prepare(_sampleRate);
            Volatile.Write(ref _source, value);
        }
    }

    /// <summary>The source goes to the output, the plug-in is not processed.</summary>
    public bool Bypass { get; set; }

    /// <summary>Silent output (the plug-in still runs).</summary>
    public bool Mute { get; set; }

    /// <summary>Output gain in decibels.</summary>
    public double GainDb { get; set; }

    /// <summary>Peaks of the slot's output.</summary>
    public PeakMeter Meter { get; } = new();

    /// <summary><c>true</c> after the plug-in threw while processing; the slot is then silent.</summary>
    public bool HasFailed { get; internal set; }

    /// <summary>Main input channels of the plug-in (fed by the source).</summary>
    public int MainInputChannels => _mainInputs;

    /// <summary>Main output channels of the plug-in.</summary>
    public int MainOutputChannels => _mainOutputs;

    /// <summary>Queues a MIDI message for the next block (any thread: on-screen keyboard, MIDI input).</summary>
    public void SendMidi(in MidiEvent e) => _midi.Enqueue(e with { SampleOffset = 0 });

    internal void Prepare(double sampleRate, int maxFrames)
    {
        Plugin.Prepare(sampleRate, maxFrames, realtime: true);
        _sampleRate = sampleRate;
        _mainInputs = Plugin.InputBuses.Where(b => !b.IsAuxiliary).Sum(b => b.ChannelCount);
        _mainOutputs = Plugin.OutputBuses.Where(b => !b.IsAuxiliary).Sum(b => b.ChannelCount);
        FreePointers();
        _inputs = (float**)NativeMemory.Alloc((nuint)Math.Max(1, _mainInputs), (nuint)sizeof(float*));
        _outputs = (float**)NativeMemory.Alloc((nuint)Math.Max(1, _mainOutputs), (nuint)sizeof(float*));
        for (int c = 0; c < _mainInputs; c++) _inputs[c] = Plugin.InputPointer(c);
        for (int c = 0; c < _mainOutputs; c++) _outputs[c] = Plugin.OutputPointer(c);
        Meter.Resize(Math.Min(Math.Max(_mainOutputs, 1), 16));
        Source.Prepare(sampleRate);
        HasFailed = false;
    }

    internal void Render(in AudioBuffers deviceInputs, in AudioBuffers deviceOutputs)
    {
        int frames = deviceOutputs.Frames;
        var inputs = new AudioBuffers(_inputs, _mainInputs, frames);
        for (int c = _mainInputs; c < Plugin.InputChannelCount; c++) Plugin.GetInput(c)[..frames].Clear();
        Source.Read(inputs, deviceInputs);
        while (_midi.TryDequeue(out var e)) Plugin.AddMidiEvent(e);
        float gain = Mute ? 0f : SignalSource.DbToGain(GainDb);
        if (Bypass)
        {
            Meter.Update(inputs);
            PluginRack.Mix(inputs, deviceOutputs, gain);
            return;
        }
        Plugin.Process(frames);
        var outputs = new AudioBuffers(_outputs, _mainOutputs, frames);
        Meter.Update(outputs, gain);
        PluginRack.Mix(outputs, deviceOutputs, gain);
    }

    internal void Release()
    {
        Plugin.Stop();
        FreePointers();
        _sampleRate = 0;
    }

    private void FreePointers()
    {
        NativeMemory.Free(_inputs);
        NativeMemory.Free(_outputs);
        _inputs = null;
        _outputs = null;
    }
}

/// <summary>
/// Peak levels written by the audio thread and read by the user interface: each channel keeps the largest absolute sample
/// since the last <see cref="Read"/> (lock-free).
/// </summary>
public sealed class PeakMeter
{
    private int[] _bits = [];

    /// <summary>Number of channels.</summary>
    public int ChannelCount => Volatile.Read(ref _bits).Length;

    internal void Resize(int channels) => Volatile.Write(ref _bits, new int[Math.Max(0, channels)]);

    internal void Update(in AudioBuffers buffers, float gain = 1f)
    {
        int[] bits = Volatile.Read(ref _bits);
        int n = Math.Min(bits.Length, buffers.ChannelCount);
        for (int c = 0; c < n; c++)
        {
            float peak = 0f;
            foreach (float v in buffers[c]) peak = MathF.Max(peak, MathF.Abs(v));
            peak *= MathF.Abs(gain);
            int value = BitConverter.SingleToInt32Bits(peak); // positive floats order like their bits
            int current = Volatile.Read(ref bits[c]);
            while (value > current)
            {
                int seen = Interlocked.CompareExchange(ref bits[c], value, current);
                if (seen == current) break;
                current = seen;
            }
        }
    }

    /// <summary>Takes the peaks since the last call (linear, 1 = full scale) and starts again from zero.</summary>
    public void Read(Span<float> peaks)
    {
        int[] bits = Volatile.Read(ref _bits);
        for (int c = 0; c < peaks.Length; c++) peaks[c] = c < bits.Length ? BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref bits[c], 0)) : 0f;
    }
}

/// <summary>MIDI messages from any thread to the audio thread (producers serialized, consumer lock-free).</summary>
public sealed class MidiEventQueue
{
    private readonly MidiEvent[] _items;
    private readonly int _mask;
    private readonly Lock _producers = new();
    private int _head, _tail;

    /// <summary>Creates a queue (capacity rounded up to a power of two).</summary>
    public MidiEventQueue(int capacity)
    {
        int size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(2, capacity));
        _items = new MidiEvent[size];
        _mask = size - 1;
    }

    /// <summary>Adds a message; returns <c>false</c> when the queue is full (the message is dropped).</summary>
    public bool Enqueue(in MidiEvent e)
    {
        lock (_producers)
        {
            int tail = _tail;
            if (tail - Volatile.Read(ref _head) > _mask) return false;
            _items[tail & _mask] = e;
            Volatile.Write(ref _tail, tail + 1);
            return true;
        }
    }

    /// <summary>Takes the oldest message (single consumer).</summary>
    public bool TryDequeue(out MidiEvent e)
    {
        int head = _head;
        if (head == Volatile.Read(ref _tail))
        {
            e = default;
            return false;
        }
        e = _items[head & _mask];
        Volatile.Write(ref _head, head + 1);
        return true;
    }
}
