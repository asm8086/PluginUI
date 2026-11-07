// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

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

    /// <summary>MIDI messages for the first plug-in, at absolute sample positions.</summary>
    public IReadOnlyList<(long Sample, MidiEvent Event)> Midi { get; set; } = [];

    /// <summary>
    /// Receives the MIDI messages the last plug-in produces, at absolute sample positions of the output (latency
    /// compensated like the audio). The MIDI output of the other plug-ins feeds the next plug-in's MIDI input.
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
                        long at = position + e.SampleOffset - latency;
                        if (at >= 0) midiOut(at, e with { SampleOffset = 0 });
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
