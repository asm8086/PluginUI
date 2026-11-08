// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Host.Graph;

namespace PluginUI.Host.Audio;

/// <summary>Options of an offline rendering.</summary>
public sealed class RenderOptions
{
    /// <summary>Block size passed to the plug-ins (default 512).</summary>
    public int BlockSize { get; set; } = 512;

    /// <summary>
    /// Seconds rendered after the end of the input (reverb and delay tails). Null = the longest tail the plug-ins report,
    /// capped at <see cref="MaxAutoTailSeconds"/>.
    /// </summary>
    public double? TailSeconds { get; set; }

    /// <summary>Cap of the automatic tail (default 10 s).</summary>
    public double MaxAutoTailSeconds { get; set; } = 10.0;

    /// <summary>Removes the latency the plug-ins report, so the output lines up with the input (default <c>true</c>).</summary>
    public bool CompensateLatency { get; set; } = true;

    /// <summary>Length in seconds when there is no input (instruments driven by <see cref="Midi"/>).</summary>
    public double? Length { get; set; }

    /// <summary>MIDI messages for the first plug-in (for a graph: its MIDI input modules), at absolute sample positions.</summary>
    public IReadOnlyList<(long Sample, MidiEvent Event)> Midi { get; set; } = [];

    /// <summary>
    /// Receives the MIDI messages the last plug-in produces (for a graph: what reaches its MIDI output modules), at
    /// absolute sample positions of the output (latency compensated like the audio). In a chain, the MIDI output of the
    /// other plug-ins feeds the next plug-in's MIDI input.
    /// </summary>
    public Action<long, MidiEvent>? MidiOutput { get; set; }

    /// <summary>Called after each block with the fraction done (0..1).</summary>
    public Action<double>? Progress { get; set; }
}

/// <summary>Runs audio through a chain of plug-ins, faster than real time.</summary>
public static class OfflineRenderer
{
    /// <summary>
    /// Processes <paramref name="input"/> through <paramref name="chain"/> (in order). The plug-ins are prepared at the
    /// input's sample rate; each one's outputs feed the next one's inputs channel by channel (a mono signal entering a
    /// stereo input is copied to both channels).
    /// </summary>
    /// <param name="chain">Plug-ins, first to last.</param>
    /// <param name="input">Input audio; null for an instrument (then <see cref="RenderOptions.Length"/> or the MIDI decides the length).</param>
    /// <param name="sampleRate">Sample rate when <paramref name="input"/> is null.</param>
    /// <param name="options">Options.</param>
    /// <returns>The output of the last plug-in.</returns>
    public static AudioBuffer Render(IReadOnlyList<PluginInstance> chain, AudioBuffer? input, double sampleRate = 48000, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0) throw new ArgumentException("The chain is empty.", nameof(chain));
        options ??= new RenderOptions();
        double rate = input?.SampleRate ?? sampleRate;
        int block = Math.Max(16, options.BlockSize);

        foreach (var plugin in chain) plugin.Prepare(rate, block, realtime: false);
        int latency = options.CompensateLatency ? chain.Sum(p => p.LatencySamples) : 0;

        long sourceLength = input?.Length ?? 0;
        if (input is null)
        {
            long midiEnd = options.Midi.Count == 0 ? 0 : options.Midi.Max(m => m.Sample) + 1;
            sourceLength = options.Length is double seconds ? (long)Math.Ceiling(seconds * rate) : midiEnd;
        }
        double tailSeconds = options.TailSeconds ?? Math.Min(options.MaxAutoTailSeconds, chain.Max(p => p.TailSamples == int.MaxValue ? double.PositiveInfinity : p.TailSamples / rate));
        long tail = (long)Math.Ceiling(Math.Max(0, tailSeconds) * rate);
        long outputLength = sourceLength + tail;
        long totalFrames = outputLength + latency;

        var last = chain[^1];
        var output = AudioBuffer.Create(rate, last.OutputChannelCount, (int)Math.Min(int.MaxValue, outputLength));
        var midi = options.Midi.OrderBy(m => m.Sample).ToArray();
        int midiIndex = 0;

        for (long position = 0; position < totalFrames; position += block)
        {
            int frames = (int)Math.Min(block, totalFrames - position);
            FeedFirst(chain[0], input, position, frames);
            while (midiIndex < midi.Length && midi[midiIndex].Sample < position + frames)
            {
                var (sample, e) = midi[midiIndex++];
                chain[0].AddMidiEvent(e with { SampleOffset = (int)Math.Max(0, sample - position) });
            }
            for (int i = 0; i < chain.Count; i++)
            {
                chain[i].Process(frames);
                if (i + 1 < chain.Count)
                {
                    Connect(chain[i], chain[i + 1], frames);
                    foreach (var e in chain[i].OutputMidi) chain[i + 1].AddMidiEvent(e); // MIDI effects feed the next plug-in
                }
                else if (options.MidiOutput is { } midiOut)
                {
                    foreach (var e in chain[i].OutputMidi)
                    {
                        // Messages from the first samples fall before 0 once compensated: they belong at the start.
                        long at = Math.Max(0, position + e.SampleOffset - latency);
                        midiOut(at, e with { SampleOffset = 0 });
                    }
                }
                chain[i].Idle();
            }
            for (int c = 0; c < output.ChannelCount; c++)
            {
                var source = last.GetOutput(c)[..frames];
                for (int f = 0; f < frames; f++)
                {
                    long at = position + f - latency;
                    if (at >= 0 && at < output.Length) output.Channels[c][at] = source[f];
                }
            }
            options.Progress?.Invoke(Math.Min(1.0, (position + frames) / (double)totalFrames));
        }
        foreach (var plugin in chain) plugin.Stop();
        return output;
    }

    /// <summary>
    /// Renders a <see cref="PluginGraph"/> faster than real time: <paramref name="input"/> plays the role of the device
    /// input (its channels feed the <see cref="AudioInputNode"/> modules), the result is the device output (every channel
    /// an <see cref="AudioOutputNode"/> writes to). <see cref="RenderOptions.Midi"/> goes to the <see cref="MidiInputNode"/>
    /// modules, <see cref="RenderOptions.MidiOutput"/> receives what reaches the <see cref="MidiOutputNode"/> modules.
    /// </summary>
    /// <remarks>
    /// The graph is prepared for offline processing at the input's rate: prepare it again before playing it live. With
    /// <see cref="RenderOptions.CompensateLatency"/>, the latency of the longest path to an output is removed. An automatic
    /// tail is the longest tail the plug-ins report, or <see cref="RenderOptions.MaxAutoTailSeconds"/> when the graph has a
    /// loop.
    /// </remarks>
    /// <param name="graph">The graph.</param>
    /// <param name="input">Input audio; null when the graph has no audio input (then <see cref="RenderOptions.Length"/> or the MIDI decides the length).</param>
    /// <param name="sampleRate">Sample rate when <paramref name="input"/> is null.</param>
    /// <param name="options">Options.</param>
    /// <returns>The device output channels.</returns>
    public static unsafe AudioBuffer Render(PluginGraph graph, AudioBuffer? input, double sampleRate = 48000, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new RenderOptions();
        double rate = input?.SampleRate ?? sampleRate;
        int block = Math.Max(16, options.BlockSize);
        var outputNodes = graph.Nodes.OfType<AudioOutputNode>().ToList();
        int outChannels = Math.Max(1, outputNodes.Select(o => o.FirstChannel + o.Channels).DefaultIfEmpty(2).Max());
        int inChannels = input?.ChannelCount ?? 0;
        bool realtime = graph.Realtime;
        var sink = graph.MidiOutput;
        var collector = new OfflineMidiSink(options.MidiOutput);
        float* inMemory = null, outMemory = null;
        float** inPointers = null, outPointers = null;
        try
        {
            graph.Realtime = false;
            graph.MidiOutput = collector;
            graph.Prepare(rate, block, inChannels, outChannels);
            int latency = options.CompensateLatency ? outputNodes.Select(o => o.PathLatencySamples).DefaultIfEmpty(0).Max() : 0;
            collector.Latency = latency;

            long sourceLength = input?.Length ?? 0;
            if (input is null)
            {
                long midiEnd = options.Midi.Count == 0 ? 0 : options.Midi.Max(m => m.Sample) + 1;
                sourceLength = options.Length is double seconds ? (long)Math.Ceiling(seconds * rate) : midiEnd;
            }
            var plugins = graph.Nodes.OfType<PluginNode>().Select(n => n.Plugin).ToList();
            double longest = plugins.Count == 0 ? 0 : plugins.Max(p => p.TailSamples == int.MaxValue ? double.PositiveInfinity : p.TailSamples / rate);
            if (graph.Nodes.Any(n => n.IsInLoop)) longest = double.PositiveInfinity;
            double tailSeconds = options.TailSeconds ?? Math.Min(options.MaxAutoTailSeconds, longest);
            long tail = (long)Math.Ceiling(Math.Max(0, tailSeconds) * rate);
            long outputLength = sourceLength + tail;
            long totalFrames = outputLength + latency;
            var output = AudioBuffer.Create(rate, outChannels, (int)Math.Min(int.MaxValue, outputLength));

            inMemory = (float*)NativeMemory.AllocZeroed((nuint)Math.Max(1, inChannels * block), sizeof(float));
            outMemory = (float*)NativeMemory.AllocZeroed((nuint)(outChannels * block), sizeof(float));
            inPointers = (float**)NativeMemory.Alloc((nuint)Math.Max(1, inChannels), (nuint)sizeof(float*));
            outPointers = (float**)NativeMemory.Alloc((nuint)outChannels, (nuint)sizeof(float*));
            for (int c = 0; c < inChannels; c++) inPointers[c] = inMemory + c * block;
            for (int c = 0; c < outChannels; c++) outPointers[c] = outMemory + c * block;

            var midi = options.Midi.OrderBy(m => m.Sample).ToArray();
            int midiIndex = 0;
            for (long position = 0; position < totalFrames; position += block)
            {
                int frames = (int)Math.Min(block, totalFrames - position);
                for (int c = 0; c < inChannels; c++)
                {
                    var target = new Span<float>(inPointers[c], frames);
                    target.Clear();
                    if (position < input!.Length) input.Channels[c].AsSpan((int)position, (int)Math.Min(frames, input.Length - position)).CopyTo(target);
                }
                while (midiIndex < midi.Length && midi[midiIndex].Sample < position + frames)
                {
                    var (sample, e) = midi[midiIndex++];
                    graph.PostMidiInput(e with { SampleOffset = (int)Math.Max(0, sample - position) });
                }
                collector.Position = position;
                graph.Render(new AudioBuffers(inPointers, inChannels, frames), new AudioBuffers(outPointers, outChannels, frames));
                foreach (var plugin in plugins) plugin.Idle();
                for (int c = 0; c < outChannels; c++)
                {
                    var source = new Span<float>(outPointers[c], frames);
                    for (int f = 0; f < frames; f++)
                    {
                        long at = position + f - latency;
                        if (at >= 0 && at < output.Length) output.Channels[c][at] = source[f];
                    }
                }
                options.Progress?.Invoke(Math.Min(1.0, (position + frames) / (double)Math.Max(1, totalFrames)));
            }
            return output;
        }
        finally
        {
            NativeMemory.Free(inMemory);
            NativeMemory.Free(outMemory);
            NativeMemory.Free(inPointers);
            NativeMemory.Free(outPointers);
            graph.MidiOutput = sink;
            graph.Realtime = realtime;
        }
    }

    /// <summary>Gives the graph's MIDI output its absolute, latency-compensated position.</summary>
    private sealed class OfflineMidiSink(Action<long, MidiEvent>? target) : IMidiSink
    {
        public long Position { get; set; }

        public int Latency { get; set; }

        public void Send(in MidiEvent e) => target?.Invoke(Math.Max(0, Position + e.SampleOffset - Latency), e with { SampleOffset = 0 });
    }

    private static void FeedFirst(PluginInstance plugin, AudioBuffer? input, long position, int frames)
    {
        int channels = plugin.InputChannelCount;
        for (int c = 0; c < channels; c++)
        {
            var target = plugin.GetInput(c)[..frames];
            target.Clear();
            if (input is null || input.ChannelCount == 0 || position >= input.Length) continue;
            // Main bus first: a mono source feeds both channels of a stereo input; extra channels stay silent.
            int sourceChannel = input.ChannelCount == 1 && c < 2 ? 0 : c;
            if (sourceChannel >= input.ChannelCount) continue;
            int n = (int)Math.Min(frames, input.Length - position);
            input.Channels[sourceChannel].AsSpan((int)position, n).CopyTo(target);
        }
    }

    private static void Connect(PluginInstance from, PluginInstance to, int frames)
    {
        int outs = from.OutputChannelCount, ins = to.InputChannelCount;
        for (int c = 0; c < ins; c++)
        {
            var target = to.GetInput(c)[..frames];
            int source = outs == 1 && c < 2 ? 0 : c;
            if (source < outs) from.GetOutput(source)[..frames].CopyTo(target);
            else target.Clear();
        }
    }
}
