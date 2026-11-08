// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Host.Audio;

namespace PluginUI.Host.Graph;

/// <summary>
/// What a <see cref="GraphNode"/> sees while it processes a block: its inputs (already delayed and converted), its
/// output buffers, the device channels. Valid only during <see cref="GraphNode.Process"/>.
/// </summary>
/// <remarks>
/// Channel conversion when a cable joins ports of different widths: equal widths copy channel by channel; a mono source
/// feeds every channel of the input; several channels entering a mono input are averaged; otherwise the first channels
/// are copied and the others stay silent.
/// </remarks>
public readonly unsafe ref struct NodeContext
{
    private readonly NodeRuntime _runtime;
    private readonly int _offset;

    internal NodeContext(NodeRuntime runtime, int offset, int frames, long sampleTime, double sampleRate, AudioBuffers deviceInputs, AudioBuffers deviceOutputs)
    {
        _runtime = runtime;
        _offset = offset;
        Frames = frames;
        SampleTime = sampleTime;
        SampleRate = sampleRate;
        DeviceInputs = deviceInputs;
        DeviceOutputs = deviceOutputs;
    }

    /// <summary>Samples to process.</summary>
    public int Frames { get; }

    /// <summary>Position of this call within the device block (not 0 when a loop runs in sub-blocks).</summary>
    public int BlockOffset => _offset;

    /// <summary>Position of the first sample since the graph was prepared.</summary>
    public long SampleTime { get; }

    /// <summary>Sample rate.</summary>
    public double SampleRate { get; }

    /// <summary>Input channels of the audio device for this block (may be empty).</summary>
    public AudioBuffers DeviceInputs { get; }

    /// <summary>Output channels of the audio device for this block (add to them, do not overwrite).</summary>
    public AudioBuffers DeviceOutputs { get; }

    /// <summary><c>true</c> when a cable arrives at input <paramref name="input"/>.</summary>
    public bool IsConnected(int input)
        => (uint)input < (uint)_runtime.AudioInputs.Length && (_runtime.AudioInputs[input] is not null || _runtime.MidiInputs[input] is not null);

    /// <summary>
    /// Writes the signal arriving at audio input <paramref name="input"/> into <paramref name="destination"/>, times
    /// <paramref name="gain"/> (silence when no cable arrives).
    /// </summary>
    /// <param name="input">Index of the input port.</param>
    /// <param name="destination">Channels to fill (<see cref="Frames"/> samples each).</param>
    /// <param name="gain">Linear gain.</param>
    /// <param name="add"><c>true</c> to add to <paramref name="destination"/> instead of replacing it.</param>
    public void ReadAudio(int input, in AudioBuffers destination, float gain = 1f, bool add = false)
    {
        // Bounds checked: ports may change one block before the new plan is swapped in.
        var binding = (uint)input < (uint)_runtime.AudioInputs.Length ? _runtime.AudioInputs[input] : null;
        if (binding is null)
        {
            if (!add) destination.Clear();
            return;
        }
        binding.Read(destination, _offset, Frames, SampleTime, gain, add);
    }

    /// <summary>The buffers of audio output <paramref name="output"/> for this block (to fill completely).</summary>
    public AudioBuffers Output(int output)
        => (uint)output < (uint)_runtime.Outputs.Length && _runtime.Outputs[output].Audio is { } audio ? audio.Slice(_offset, Frames) : new AudioBuffers(null, 0, Frames);

    /// <summary>The MIDI messages arriving at input <paramref name="input"/> during this block (offsets within the block).</summary>
    public ReadOnlySpan<MidiEvent> ReadMidi(int input)
    {
        var binding = (uint)input < (uint)_runtime.MidiInputs.Length ? _runtime.MidiInputs[input] : null;
        return binding is null ? [] : binding.Read(_offset, Frames, SampleTime);
    }

    /// <summary>Sends a MIDI message from output <paramref name="output"/> (its sample offset within this block).</summary>
    public void SendMidi(int output, in MidiEvent e)
    {
        if ((uint)output < (uint)_runtime.Outputs.Length && _runtime.Outputs[output].Midi is { } midi)
            midi.Add(e with { SampleOffset = _offset + Math.Clamp(e.SampleOffset, 0, Math.Max(0, Frames - 1)) });
    }

    /// <summary>Device channels <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/> − 1 that exist.</summary>
    public AudioBuffers DeviceOutputChannels(int first, int count) => _runtime.DeviceView(DeviceOutputs, first, count);

    /// <summary>Device input channels <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/> − 1 that exist.</summary>
    public AudioBuffers DeviceInputChannels(int first, int count) => _runtime.DeviceView(DeviceInputs, first, count);

    /// <summary>A scratch buffer of <paramref name="channels"/> channels for this block (up to the node's widest port).</summary>
    public AudioBuffers Scratch(int channels) => _runtime.ScratchBuffers(channels, Frames);
}

/// <summary>Native channel buffers of one audio output port: <c>channels × maxFrames</c> samples.</summary>
internal sealed unsafe class AudioPortBuffer : IDisposable
{
    private float* _memory;
    private float** _slice;

    public AudioPortBuffer(int channels, int maxFrames)
    {
        Count = channels;
        MaxFrames = maxFrames;
        _memory = (float*)NativeMemory.AllocZeroed((nuint)(Math.Max(1, channels) * maxFrames), sizeof(float));
        Channels = (float**)NativeMemory.Alloc((nuint)Math.Max(1, channels), (nuint)sizeof(float*));
        _slice = (float**)NativeMemory.Alloc((nuint)Math.Max(1, channels), (nuint)sizeof(float*));
        for (int c = 0; c < channels; c++) Channels[c] = _memory + c * maxFrames;
    }

    public int Count { get; }

    public int MaxFrames { get; }

    public float** Channels { get; private set; }

    public AudioBuffers Slice(int offset, int frames)
    {
        for (int c = 0; c < Count; c++) _slice[c] = Channels[c] + offset;
        return new AudioBuffers(_slice, Count, frames);
    }

    public Span<float> Channel(int channel, int offset, int frames) => new(Channels[channel] + offset, frames);

    public void Dispose()
    {
        NativeMemory.Free(_memory);
        NativeMemory.Free(Channels);
        NativeMemory.Free(_slice);
        _memory = null;
        Channels = null;
        _slice = null;
    }
}

/// <summary>The MIDI messages of one output port during the current device block (offsets from the block start).</summary>
internal sealed class MidiPortBuffer
{
    private readonly MidiEvent[] _events = new MidiEvent[1024];

    public int Count { get; private set; }

    public ReadOnlySpan<MidiEvent> Events => new(_events, 0, Count);

    public void Clear() => Count = 0;

    public void Add(in MidiEvent e)
    {
        if (Count < _events.Length) _events[Count++] = e;
    }
}

/// <summary>Delays audio by a fixed number of samples, by absolute sample time (zeros before anything was written).</summary>
internal sealed unsafe class DelayRing : IDisposable
{
    private float* _memory;
    private readonly int _mask;
    private readonly int _channels;

    public DelayRing(int channels, int capacity)
    {
        int size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(2, capacity));
        _mask = size - 1;
        _channels = channels;
        _memory = (float*)NativeMemory.AllocZeroed((nuint)(Math.Max(1, channels) * size), sizeof(float));
    }

    private int Size => _mask + 1;

    public void Write(long time, float** source, int offset, int frames)
    {
        for (int c = 0; c < _channels; c++)
        {
            float* line = _memory + c * Size;
            float* from = source[c] + offset;
            for (int i = 0; i < frames; i++) line[(time + i) & _mask] = from[i];
        }
    }

    public void Read(int channel, long time, Span<float> destination)
    {
        float* line = _memory + channel * Size;
        for (int i = 0; i < destination.Length; i++)
        {
            long t = time + i;
            destination[i] = t < 0 ? 0f : line[t & _mask];
        }
    }

    public void Dispose()
    {
        NativeMemory.Free(_memory);
        _memory = null;
    }
}

/// <summary>A cable arriving at an audio input: its source buffer, and a delay line when the cable is delayed.</summary>
internal sealed unsafe class AudioInputBinding : IDisposable
{
    private readonly AudioPortBuffer _source;
    private readonly DelayRing? _ring;
    private readonly AudioPortBuffer? _delayed;

    public AudioInputBinding(AudioPortBuffer source, int delay, int maxFrames)
    {
        _source = source;
        Delay = delay;
        if (delay > 0)
        {
            _ring = new DelayRing(source.Count, delay + maxFrames);
            _delayed = new AudioPortBuffer(source.Count, maxFrames);
        }
    }

    public int Delay { get; }

    /// <summary>Feeds the delay line once the source has processed [offset, offset + frames) at <paramref name="time"/>.</summary>
    public void Tap(long time, int offset, int frames) => _ring?.Write(time, _source.Channels, offset, frames);

    public void Read(in AudioBuffers destination, int offset, int frames, long time, float gain, bool add)
    {
        int sourceCount = _source.Count;
        float** channels;
        int at;
        if (_ring is not null)
        {
            for (int c = 0; c < sourceCount; c++) _ring.Read(c, time - Delay, _delayed!.Channel(c, 0, frames));
            channels = _delayed!.Channels;
            at = 0;
        }
        else
        {
            channels = _source.Channels;
            at = offset;
        }
        int targetCount = destination.ChannelCount;
        if (targetCount == 0) return;
        if (sourceCount == targetCount || sourceCount == 1)
        {
            for (int c = 0; c < targetCount; c++) Mix(new Span<float>(channels[sourceCount == 1 ? 0 : c] + at, frames), destination[c], gain, add);
            return;
        }
        if (targetCount == 1)
        {
            var target = destination[0];
            if (!add) target.Clear();
            float g = gain / sourceCount;
            for (int c = 0; c < sourceCount; c++) Mix(new Span<float>(channels[c] + at, frames), target, g, true);
            return;
        }
        for (int c = 0; c < targetCount; c++)
        {
            if (c < sourceCount) Mix(new Span<float>(channels[c] + at, frames), destination[c], gain, add);
            else if (!add) destination[c].Clear();
        }
    }

    private static void Mix(ReadOnlySpan<float> source, Span<float> target, float gain, bool add)
    {
        if (add)
        {
            for (int i = 0; i < target.Length; i++) target[i] += source[i] * gain;
        }
        else if (gain == 1f)
        {
            source.CopyTo(target);
        }
        else
        {
            for (int i = 0; i < target.Length; i++) target[i] = source[i] * gain;
        }
    }

    public void Dispose()
    {
        _ring?.Dispose();
        _delayed?.Dispose();
    }
}

/// <summary>A cable arriving at a MIDI input, delayed or not.</summary>
internal sealed class MidiInputBinding
{
    private readonly MidiPortBuffer _source;
    private readonly MidiEvent[] _read = new MidiEvent[1024];
    private readonly (long Time, MidiEvent Event)[] _queue;
    private int _head, _tail;

    public MidiInputBinding(MidiPortBuffer source, int delay)
    {
        _source = source;
        Delay = delay;
        _queue = delay > 0 ? new (long, MidiEvent)[1024] : [];
    }

    public int Delay { get; }

    /// <summary>Queues the source's messages of [offset, offset + frames) of the block that starts at <paramref name="blockStart"/>.</summary>
    public void Tap(long blockStart, int offset, int frames)
    {
        if (Delay <= 0) return;
        foreach (var e in _source.Events)
        {
            if (e.SampleOffset < offset || e.SampleOffset >= offset + frames) continue;
            if (_tail - _head >= _queue.Length) break; // full: dropped
            _queue[_tail % _queue.Length] = (blockStart + e.SampleOffset + Delay, e);
            _tail++;
        }
    }

    public ReadOnlySpan<MidiEvent> Read(int offset, int frames, long time)
    {
        int n = 0;
        if (Delay > 0)
        {
            while (_head < _tail && n < _read.Length)
            {
                var (at, e) = _queue[_head % _queue.Length];
                if (at >= time + frames) break;
                _read[n++] = e with { SampleOffset = (int)Math.Max(0, at - time) };
                _head++;
            }
            return new(_read, 0, n);
        }
        foreach (var e in _source.Events)
        {
            if (e.SampleOffset < offset || e.SampleOffset >= offset + frames || n >= _read.Length) continue;
            _read[n++] = e with { SampleOffset = e.SampleOffset - offset };
        }
        return new(_read, 0, n);
    }
}

/// <summary>An output port at run time: audio buffers or MIDI messages.</summary>
internal sealed class OutputRuntime(AudioPortBuffer? audio, MidiPortBuffer? midi)
{
    public AudioPortBuffer? Audio { get; } = audio;

    public MidiPortBuffer? Midi { get; } = midi;
}

/// <summary>A node at run time: its bindings, its buffers and the delay lines it feeds.</summary>
internal sealed unsafe class NodeRuntime : IDisposable
{
    private readonly AudioPortBuffer _scratch;
    private float** _view;
    private readonly int _viewSize;

    public NodeRuntime(GraphNode node, int maxFrames)
    {
        Node = node;
        AudioInputs = new AudioInputBinding?[node.Inputs.Count];
        MidiInputs = new MidiInputBinding?[node.Inputs.Count];
        Outputs = [.. node.Outputs.Select(p => new OutputRuntime(
            p.Kind == PortKind.Audio ? new AudioPortBuffer(p.Channels, maxFrames) : null,
            p.Kind == PortKind.Midi ? new MidiPortBuffer() : null))];
        int widest = node.Inputs.Concat(node.Outputs).Select(p => p.Channels).DefaultIfEmpty(0).Max();
        _scratch = new AudioPortBuffer(Math.Max(2, widest), maxFrames);
        _viewSize = 64;
        _view = (float**)NativeMemory.Alloc((nuint)_viewSize, (nuint)sizeof(float*));
    }

    public GraphNode Node { get; }

    public AudioInputBinding?[] AudioInputs { get; }

    public MidiInputBinding?[] MidiInputs { get; }

    public OutputRuntime[] Outputs { get; }

    /// <summary>Delayed cables leaving this node's outputs: fed after each processed block.</summary>
    public List<(int Output, AudioInputBinding Binding)> AudioTaps { get; } = [];

    /// <summary>Delayed MIDI cables leaving this node.</summary>
    public List<MidiInputBinding> MidiTaps { get; } = [];

    /// <summary>Index of the first audio output (−1 = none), for the meter.</summary>
    public int MeterOutput { get; init; } = -1;

    public AudioBuffers DeviceView(in AudioBuffers device, int first, int count)
    {
        int n = Math.Clamp(Math.Min(count, device.ChannelCount - first), 0, _viewSize);
        if (n <= 0 || first < 0) return new AudioBuffers(null, 0, device.Frames);
        for (int c = 0; c < n; c++)
        {
            fixed (float* p = device[first + c]) _view[c] = p;
        }
        return new AudioBuffers(_view, n, device.Frames);
    }

    public AudioBuffers ScratchBuffers(int channels, int frames) => new(_scratch.Channels, Math.Clamp(channels, 0, _scratch.Count), frames);

    public void ClearMidi()
    {
        foreach (var o in Outputs) o.Midi?.Clear();
    }

    public void Dispose()
    {
        foreach (var o in Outputs) o.Audio?.Dispose();
        foreach (var b in AudioInputs) b?.Dispose();
        _scratch.Dispose();
        NativeMemory.Free(_view);
        _view = null;
    }
}
