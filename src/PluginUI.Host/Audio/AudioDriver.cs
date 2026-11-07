// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PluginUI.Host.Audio;

/// <summary>The channels of one audio block: one span of <see cref="Frames"/> samples per channel (no allocation).</summary>
public readonly unsafe struct AudioBuffers
{
    private readonly float** _channels;

    /// <summary>Wraps <paramref name="count"/> channel pointers.</summary>
    public AudioBuffers(float** channels, int count, int frames)
    {
        _channels = channels;
        ChannelCount = channels is null ? 0 : count;
        Frames = frames;
    }

    /// <summary>Number of channels (0 for none).</summary>
    public int ChannelCount { get; }

    /// <summary>Samples per channel in this block.</summary>
    public int Frames { get; }

    /// <summary>Samples of a channel.</summary>
    public Span<float> this[int channel] => new(_channels[channel], Frames);

    /// <summary>Zeroes every channel.</summary>
    public void Clear()
    {
        for (int c = 0; c < ChannelCount; c++) this[c].Clear();
    }

    /// <summary>The same channels, first <paramref name="frames"/> samples from <paramref name="offset"/>.</summary>
    internal AudioBuffers Slice(float** scratch, int offset, int frames)
    {
        for (int c = 0; c < ChannelCount; c++) scratch[c] = _channels[c] + offset;
        return new AudioBuffers(scratch, ChannelCount, frames);
    }
}

/// <summary>Processes the audio of a device, on the device's thread.</summary>
public interface IAudioRenderer
{
    /// <summary>Called before the device starts (not on the audio thread).</summary>
    /// <param name="sampleRate">Device sample rate.</param>
    /// <param name="maxFrames">Largest block <see cref="Render"/> receives.</param>
    /// <param name="inputChannels">Channels of the input device (0 = none).</param>
    /// <param name="outputChannels">Channels of the output device.</param>
    void Prepare(double sampleRate, int maxFrames, int inputChannels, int outputChannels);

    /// <summary>Fills <paramref name="outputs"/> from <paramref name="inputs"/> (audio thread: no allocation, no lock waits).</summary>
    void Render(in AudioBuffers inputs, in AudioBuffers outputs);
}

/// <summary>An audio device (or a device-less clock) that calls an <see cref="IAudioRenderer"/> in real time.</summary>
public abstract class AudioDriver : IDisposable
{
    /// <summary>Name shown to the user ("WASAPI — Speakers", "No audio device").</summary>
    public abstract string Name { get; }

    /// <summary>Sample rate.</summary>
    public double SampleRate { get; protected set; }

    /// <summary>Largest block given to the renderer.</summary>
    public int BlockSize { get; protected set; }

    /// <summary>Input channels (0 = no input).</summary>
    public int InputChannels { get; protected set; }

    /// <summary>Output channels.</summary>
    public int OutputChannels { get; protected set; }

    /// <summary>Output latency reported by the device, in samples (0 when unknown).</summary>
    public int OutputLatencySamples { get; protected set; }

    /// <summary><c>true</c> between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsRunning { get; protected set; }

    /// <summary>The device stopped by itself (unplugged, taken by another application); raised on the audio thread.</summary>
    public event Action<AudioDriver, string>? Failed;

    /// <summary>
    /// The device asks to be started again (its buffer size or sample rate changed in its control panel); raised on any
    /// thread. Call <see cref="Start"/> again from the thread that started it.
    /// </summary>
    public event Action<AudioDriver>? ResetRequested;

    /// <summary>Prepares <paramref name="renderer"/> and starts calling it.</summary>
    public abstract void Start(IAudioRenderer renderer);

    /// <summary>Stops calling the renderer; when this returns, the renderer is not running any more.</summary>
    public abstract void Stop();

    /// <summary>Reports a failure of the device.</summary>
    protected void RaiseFailed(string message) => Failed?.Invoke(this, message);

    /// <summary>Reports that the device asks to be started again.</summary>
    protected void RaiseResetRequested() => ResetRequested?.Invoke(this);

    /// <summary>Stops and releases the device.</summary>
    public void Dispose()
    {
        Stop();
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the device resources.</summary>
    protected virtual void Dispose(bool disposing) { }
}

/// <summary>
/// A clock without a device: calls the renderer in real time with silent input and discards the output, so that
/// plug-ins run as in a DAW when no audio device is chosen or available.
/// </summary>
public sealed unsafe class NullAudioDriver : AudioDriver
{
    private Thread? _thread;
    private volatile bool _stop;
    private IAudioRenderer? _renderer;

    /// <summary>Creates the clock.</summary>
    public NullAudioDriver(double sampleRate = 48000, int blockSize = 512, int outputChannels = 2)
    {
        SampleRate = sampleRate;
        BlockSize = blockSize;
        OutputChannels = outputChannels;
    }

    /// <inheritdoc />
    public override string Name => "No audio device";

    /// <inheritdoc />
    public override void Start(IAudioRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        Stop();
        _renderer = renderer;
        renderer.Prepare(SampleRate, BlockSize, 0, OutputChannels);
        _stop = false;
        _thread = new Thread(Run) { Name = "Audio (no device)", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        IsRunning = true;
        _thread.Start();
    }

    /// <inheritdoc />
    public override void Stop()
    {
        _stop = true;
        _thread?.Join();
        _thread = null;
        IsRunning = false;
    }

    private void Run()
    {
        int channels = OutputChannels, frames = BlockSize;
        float* memory = (float*)NativeMemory.AllocZeroed((nuint)(Math.Max(1, channels) * frames), sizeof(float));
        float** pointers = (float**)NativeMemory.Alloc((nuint)Math.Max(1, channels), (nuint)sizeof(float*));
        try
        {
            for (int c = 0; c < channels; c++) pointers[c] = memory + c * frames;
            var outputs = new AudioBuffers(pointers, channels, frames);
            var inputs = new AudioBuffers(null, 0, frames);
            var clock = Stopwatch.StartNew();
            long blocks = 0;
            while (!_stop)
            {
                _renderer!.Render(inputs, outputs);
                blocks++;
                double wait = blocks * frames / SampleRate - clock.Elapsed.TotalSeconds;
                if (wait > 0.002) Thread.Sleep((int)(wait * 1000.0));
                else if (wait < -0.25)
                {
                    // Far behind (debugger, sleep): start counting again instead of catching up.
                    clock.Restart();
                    blocks = 0;
                }
            }
        }
        finally
        {
            NativeMemory.Free(pointers);
            NativeMemory.Free(memory);
        }
    }
}
