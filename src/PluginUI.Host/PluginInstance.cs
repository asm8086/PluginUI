// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace PluginUI.Host;

/// <summary>Plug-in format.</summary>
public enum PluginFormat
{
    /// <summary>VST 3.</summary>
    Vst3,
    /// <summary>VST 2.x.</summary>
    Vst2,
}

/// <summary>Parameter flags (same values as VST 3 <c>ParameterInfo::ParameterFlags</c>).</summary>
[Flags]
public enum ParameterFlags
{
    /// <summary>No flag.</summary>
    None = 0,
    /// <summary>Can be automated.</summary>
    CanAutomate = 1 << 0,
    /// <summary>Read-only (a meter, a display).</summary>
    IsReadOnly = 1 << 1,
    /// <summary>Wraps around (a phase).</summary>
    IsWrapAround = 1 << 2,
    /// <summary>Shown as a list of values.</summary>
    IsList = 1 << 3,
    /// <summary>Not shown to the user.</summary>
    IsHidden = 1 << 4,
    /// <summary>Program change parameter.</summary>
    IsProgramChange = 1 << 15,
    /// <summary>Bypass parameter.</summary>
    IsBypass = 1 << 16,
}

/// <summary>Parameter of a plug-in. Values are normalized (0..1).</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Title">Name.</param>
/// <param name="ShortTitle">Short name.</param>
/// <param name="Units">Units ("dB", "Hz").</param>
/// <param name="StepCount">Number of steps (0 = continuous, 1 = on/off, n = n + 1 values).</param>
/// <param name="DefaultValue">Default normalized value.</param>
/// <param name="UnitId">Unit (group) the parameter belongs to.</param>
/// <param name="Flags">Flags.</param>
public sealed record PluginParameter(uint Id, string Title, string ShortTitle, string Units, int StepCount, double DefaultValue, int UnitId, ParameterFlags Flags);

/// <summary>Audio bus of a plug-in.</summary>
/// <param name="Name">Name ("Stereo In", "Sidechain").</param>
/// <param name="ChannelCount">Number of channels.</param>
/// <param name="IsAuxiliary"><c>true</c> for an auxiliary bus (side chain), <c>false</c> for the main bus.</param>
/// <param name="IsDefaultActive"><c>true</c> when the plug-in wants the bus active by default.</param>
public sealed record PluginBus(string Name, int ChannelCount, bool IsAuxiliary, bool IsDefaultActive);

/// <summary>Short MIDI message at a sample offset of the next block.</summary>
/// <param name="SampleOffset">Position in the block.</param>
/// <param name="Status">Status byte (0x90 note on, 0x80 note off, 0xB0 control change, 0xE0 pitch bend…; low nibble = channel).</param>
/// <param name="Data1">First data byte.</param>
/// <param name="Data2">Second data byte.</param>
public readonly record struct MidiEvent(int SampleOffset, byte Status, byte Data1, byte Data2)
{
    /// <summary>Message type (high nibble of the status).</summary>
    public int Type => Status & 0xF0;

    /// <summary>Channel (0..15).</summary>
    public int Channel => Status & 0x0F;

    /// <summary>Note on.</summary>
    public static MidiEvent NoteOn(int offset, int channel, int note, int velocity) => new(offset, (byte)(0x90 | (channel & 15)), (byte)note, (byte)velocity);

    /// <summary>Note off.</summary>
    public static MidiEvent NoteOff(int offset, int channel, int note, int velocity = 64) => new(offset, (byte)(0x80 | (channel & 15)), (byte)note, (byte)velocity);

    /// <summary>Control change.</summary>
    public static MidiEvent ControlChange(int offset, int channel, int controller, int value) => new(offset, (byte)(0xB0 | (channel & 15)), (byte)controller, (byte)value);

    /// <summary>Pitch bend (-8192..8191).</summary>
    public static MidiEvent PitchBend(int offset, int channel, int bend)
    {
        int v = Math.Clamp(bend + 8192, 0, 16383);
        return new(offset, (byte)(0xE0 | (channel & 15)), (byte)(v & 127), (byte)(v >> 7));
    }
}

/// <summary>
/// A loaded plug-in, whatever its format: parameters, audio buses, processing with host-owned buffers, state.
/// </summary>
/// <remarks>
/// <para>Processing: <see cref="Prepare"/> once (sample rate, maximum block), then for each block fill
/// <see cref="GetInput"/>, queue MIDI with <see cref="AddMidiEvent"/>, call <see cref="Process"/> and read
/// <see cref="GetOutput"/>. Nothing is allocated per block.</para>
/// <para>Threads: <see cref="Process"/>, <see cref="AddMidiEvent"/> on the audio thread; <see cref="SetParameter"/> on
/// any thread (queued to the next block); the rest on the UI thread.</para>
/// </remarks>
public abstract unsafe class PluginInstance : IDisposable
{
    private float* _buffers;
    private float*[] _inputs = [];
    private float*[] _outputs = [];
    private readonly ParameterQueue _pending = new(1024);

    /// <summary>Plug-in name.</summary>
    public abstract string Name { get; }

    /// <summary>Vendor.</summary>
    public abstract string Vendor { get; }

    /// <summary>Format.</summary>
    public abstract PluginFormat Format { get; }

    /// <summary><c>true</c> for an instrument (takes notes, has no main audio input).</summary>
    public abstract bool IsInstrument { get; }

    /// <summary>Audio input buses.</summary>
    public abstract IReadOnlyList<PluginBus> InputBuses { get; }

    /// <summary>Audio output buses.</summary>
    public abstract IReadOnlyList<PluginBus> OutputBuses { get; }

    /// <summary>Number of event (MIDI) input buses.</summary>
    public abstract int EventInputCount { get; }

    /// <summary>Parameters.</summary>
    public abstract IReadOnlyList<PluginParameter> Parameters { get; }

    /// <summary>Total input channels (all active input buses, main first).</summary>
    public int InputChannelCount => InputBuses.Sum(b => b.ChannelCount);

    /// <summary>Total output channels.</summary>
    public int OutputChannelCount => OutputBuses.Sum(b => b.ChannelCount);

    /// <summary>Latency in samples.</summary>
    public abstract int LatencySamples { get; }

    /// <summary>Tail in samples after the input stops (reverbs, delays); <see cref="int.MaxValue"/> = infinite.</summary>
    public abstract int TailSamples { get; }

    /// <summary>Sample rate set by <see cref="Prepare"/> (0 before).</summary>
    public double SampleRate { get; private set; }

    /// <summary>Maximum block size set by <see cref="Prepare"/>.</summary>
    public int MaxBlockSize { get; private set; }

    /// <summary>Tempo reported to the plug-in (beats per minute).</summary>
    public double Tempo { get; set; } = 120.0;

    /// <summary>Time signature reported to the plug-in.</summary>
    public (int Numerator, int Denominator) TimeSignature { get; set; } = (4, 4);

    /// <summary>Transport running (reported to the plug-in).</summary>
    public bool IsPlaying { get; set; } = true;

    /// <summary>Position of the next block in samples since <see cref="Prepare"/> (advanced by <see cref="Process"/>).</summary>
    public long SamplePosition { get; protected set; }

    /// <summary>A parameter changed in the plug-in (its editor or its own automation). Raised on the thread of the change.</summary>
    public event Action<PluginInstance, uint, double>? ParameterChanged;

    /// <summary>The plug-in changed its latency, parameters or buses (raised on the UI thread).</summary>
    public event Action<PluginInstance>? Restarted;

    /// <summary>Allocates the buffers and starts processing.</summary>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="maxBlockSize">Largest block passed to <see cref="Process"/>.</param>
    /// <param name="realtime"><c>true</c> for live processing, <c>false</c> for offline rendering.</param>
    public void Prepare(double sampleRate, int maxBlockSize, bool realtime = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBlockSize);
        Stop();
        FreeBuffers();
        SampleRate = sampleRate;
        MaxBlockSize = maxBlockSize;
        int ins = InputChannelCount, outs = OutputChannelCount;
        _buffers = (float*)NativeMemory.AllocZeroed((nuint)((ins + outs) * maxBlockSize), sizeof(float));
        _inputs = new float*[ins];
        _outputs = new float*[outs];
        for (int i = 0; i < ins; i++) _inputs[i] = _buffers + i * maxBlockSize;
        for (int i = 0; i < outs; i++) _outputs[i] = _buffers + (ins + i) * maxBlockSize;
        SamplePosition = 0;
        OnPrepare(sampleRate, maxBlockSize, realtime);
    }

    /// <summary>Stops processing (the plug-in is deactivated; <see cref="Prepare"/> starts again).</summary>
    public void Stop()
    {
        if (SampleRate > 0) OnStop();
    }

    /// <summary>Buffer of input channel <paramref name="channel"/> (<see cref="MaxBlockSize"/> samples).</summary>
    public Span<float> GetInput(int channel) => new(_inputs[channel], MaxBlockSize);

    /// <summary>Buffer of output channel <paramref name="channel"/>.</summary>
    public Span<float> GetOutput(int channel) => new(_outputs[channel], MaxBlockSize);

    internal float* InputPointer(int channel) => _inputs[channel];

    internal float* OutputPointer(int channel) => _outputs[channel];

    /// <summary>Processes <paramref name="frames"/> samples from the input buffers into the output buffers.</summary>
    public void Process(int frames)
    {
        if (SampleRate <= 0) throw new InvalidOperationException("Call Prepare first.");
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frames, MaxBlockSize);
        if (frames <= 0) return;
        OnProcess(frames, _pending);
        SamplePosition += frames;
    }

    /// <summary>Queues a MIDI message for the next block (audio thread).</summary>
    public abstract void AddMidiEvent(in MidiEvent e);

    /// <summary>Normalized value of a parameter.</summary>
    public abstract double GetParameter(uint id);

    /// <summary>Sets a parameter (applied to the processing at the start of the next block).</summary>
    /// <param name="id">Parameter identifier.</param>
    /// <param name="normalized">Value 0..1.</param>
    public void SetParameter(uint id, double normalized)
    {
        normalized = Math.Clamp(normalized, 0.0, 1.0);
        OnSetParameterUi(id, normalized);
        _pending.Enqueue(id, normalized);
    }

    /// <summary>Queues a change for the processor only (the plug-in's controller already has the value).</summary>
    protected void QueueForProcessing(uint id, double normalized) => _pending.Enqueue(id, normalized);

    /// <summary>Applies the UI side of a parameter change (controller value).</summary>
    protected abstract void OnSetParameterUi(uint id, double normalized);

    /// <summary>Value as the plug-in displays it ("-6.0 dB").</summary>
    public abstract string FormatParameter(uint id, double normalized);

    /// <summary>Parses a value as typed by the user ("-6", "-6 dB", "Hall").</summary>
    public abstract bool TryParseParameter(uint id, string text, out double normalized);

    /// <summary>Parameter named <paramref name="name"/> (title or short title, case-insensitive), or null.</summary>
    public PluginParameter? FindParameter(string name)
    {
        name = name.Trim();
        return Parameters.FirstOrDefault(p => string.Equals(p.Title, name, StringComparison.OrdinalIgnoreCase))
            ?? Parameters.FirstOrDefault(p => string.Equals(p.ShortTitle, name, StringComparison.OrdinalIgnoreCase))
            ?? (uint.TryParse(name, out uint id) ? Parameters.FirstOrDefault(p => p.Id == id) : null);
    }

    /// <summary>Complete state (processor and controller), as saved by a DAW project.</summary>
    public abstract byte[] GetState();

    /// <summary>Restores a state from <see cref="GetState"/>.</summary>
    public abstract void SetState(ReadOnlySpan<byte> state);

    /// <summary><c>true</c> if the plug-in has an editor window.</summary>
    public abstract bool HasEditor { get; }

    /// <summary>Delivers to the UI the changes the processor made (call regularly on the UI thread).</summary>
    public virtual void Idle()
    {
    }

    /// <summary>Starts processing at the given rate and block size.</summary>
    protected abstract void OnPrepare(double sampleRate, int maxBlockSize, bool realtime);

    /// <summary>Stops processing.</summary>
    protected abstract void OnStop();

    /// <summary>Processes one block; <paramref name="pending"/> holds the parameter changes to apply at its start.</summary>
    protected abstract void OnProcess(int frames, ParameterQueue pending);

    /// <summary>Raises <see cref="ParameterChanged"/>.</summary>
    protected void RaiseParameterChanged(uint id, double value) => ParameterChanged?.Invoke(this, id, value);

    /// <summary>Raises <see cref="Restarted"/>.</summary>
    protected void RaiseRestarted() => Restarted?.Invoke(this);

    private void FreeBuffers()
    {
        NativeMemory.Free(_buffers);
        _buffers = null;
        _inputs = [];
        _outputs = [];
    }

    /// <summary><c>true</c> once the plug-in is released.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Releases the plug-in (calling it again does nothing).</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Stop();
        Dispose(true);
        FreeBuffers();
        SampleRate = 0;
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the format-specific resources.</summary>
    protected abstract void Dispose(bool disposing);
}

/// <summary>
/// Lock-free queue of parameter changes from any thread to the audio thread (one consumer). When it overflows, the
/// oldest changes are dropped: the controller keeps the value and the next change catches up.
/// </summary>
public sealed class ParameterQueue
{
    private readonly (uint Id, double Value)[] _items;
    private readonly int _mask;
    private long _write;
    private long _read;
    private readonly Lock _writeLock = new();

    /// <summary>Creates a queue of <paramref name="capacity"/> (rounded up to a power of two) changes.</summary>
    public ParameterQueue(int capacity)
    {
        int size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(2, capacity));
        _items = new (uint, double)[size];
        _mask = size - 1;
    }

    /// <summary>Adds a change (any thread; producers are serialized by a short lock, never taken by the consumer).</summary>
    public void Enqueue(uint id, double value)
    {
        lock (_writeLock)
        {
            long w = _write;
            if (w - Volatile.Read(ref _read) >= _items.Length) return; // full: drop, the value catches up later
            _items[w & _mask] = (id, value);
            Volatile.Write(ref _write, w + 1);
        }
    }

    /// <summary>Takes the next change (audio thread).</summary>
    public bool TryDequeue(out uint id, out double value)
    {
        long r = _read;
        if (r == Volatile.Read(ref _write))
        {
            id = 0;
            value = 0;
            return false;
        }
        (id, value) = _items[r & _mask];
        Volatile.Write(ref _read, r + 1);
        return true;
    }
}
