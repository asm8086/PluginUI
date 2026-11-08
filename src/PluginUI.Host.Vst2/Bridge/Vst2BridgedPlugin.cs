// 2026/11/08 18:01:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using PluginUI.Host.Catalog;

namespace PluginUI.Host.Vst2;

/// <summary>
/// A VST 2 plug-in running in a bridge process of another architecture (a 32-bit plug-in in a 64-bit host): the same
/// <see cref="PluginInstance"/> as a plug-in loaded in this process, its calls carried through shared memory.
/// </summary>
/// <remarks>
/// <para>Each block costs one round trip to the bridge. Parameter values are mirrored in the shared memory, so reading
/// them costs nothing; text values, states and the plug-in's own changes (<see cref="Idle"/>) are control calls.</para>
/// <para>A plug-in that crashes stops its bridge only: the next call fails with a <see cref="Vst2BridgeException"/> (a
/// block then leaves the outputs silent and the graph marks the module as failed).</para>
/// <para>The plug-in's editor (<see cref="CreateEditor"/>, a <see cref="Vst2BridgedEditor"/>) is opened by the bridge as
/// a child of the host's window; the host's waits for the bridge handle the messages that window sends.</para>
/// </remarks>
public sealed unsafe class Vst2BridgedPlugin : PluginInstance
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly BridgeChannel _channel;
    private readonly Func<bool> _alive;
    private readonly Action? _closed;
    private readonly Lock _control = new();
    private readonly MidiEvent[] _midi = new MidiEvent[BridgeChannel.MaxEvents];
    private int _midiCount;
    private BridgeDescription _description;
    private readonly Dictionary<uint, (double Value, string Text)> _formatted = [];
    private bool _prepared;
    private bool _failed;
    private bool _calling;
    private Vst2BridgedEditor? _editor;

    /// <summary>Time a block may take in the bridge before it is considered stopped.</summary>
    internal static TimeSpan BlockTimeout { get; set; } = TimeSpan.FromSeconds(2);

    internal Vst2BridgedPlugin(BridgeChannel channel, Func<bool> alive, Action? closed, ModuleArchitecture architecture)
    {
        _channel = channel;
        _alive = alive;
        _closed = closed;
        Architecture = architecture;
        _description = BridgePayload.Read(Call(BridgeOperation.Describe, []), BridgeDescription.Read);
        if (_description.Inputs.Sum(b => b.ChannelCount) > BridgeChannel.MaxChannels || _description.Outputs.Sum(b => b.ChannelCount) > BridgeChannel.MaxChannels)
            throw new Vst2BridgeException($"'{_description.Name}' has more than {BridgeChannel.MaxChannels} channels: the bridge cannot carry them.");
    }

    /// <summary>
    /// Starts <paramref name="bridgeExecutable"/> and loads the plug-in in it.
    /// </summary>
    /// <param name="bridgeExecutable">The bridge (an executable that runs <see cref="Vst2BridgeServer.Run"/> for <c>bridge</c>).</param>
    /// <param name="modulePath">The VST 2 module.</param>
    /// <param name="plugin">For a shell module, the plug-in's identifier or name; null for an ordinary module.</param>
    /// <param name="hostName">Name reported to the plug-in.</param>
    /// <param name="architecture">The architecture of the bridge (reported by <see cref="Architecture"/>).</param>
    /// <exception cref="Vst2BridgeException">The bridge did not start, or the plug-in could not be loaded in it.</exception>
    public static Vst2BridgedPlugin Start(string bridgeExecutable, string modulePath, string? plugin, string hostName, ModuleArchitecture architecture)
    {
        ArgumentNullException.ThrowIfNull(bridgeExecutable);
        ArgumentNullException.ThrowIfNull(modulePath);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Bridges run on Windows only.");
        string name = "Local\\PluginUI.Bridge." + Guid.NewGuid().ToString("N");
        var channel = BridgeChannel.Create(name);
        System.Diagnostics.Process? process = null;
        try
        {
            var start = new ProcessStartInfo(bridgeExecutable) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(Vst2BridgeServer.CommandName);
            start.ArgumentList.Add(name);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(modulePath);
            start.ArgumentList.Add(string.IsNullOrEmpty(plugin) ? "-" : plugin);
            start.ArgumentList.Add(hostName);
            process = System.Diagnostics.Process.Start(start) ?? throw new Vst2BridgeException($"'{bridgeExecutable}' could not be started.");
            var running = process;
            bool Alive() => !running.HasExited;
            channel.WaitForStart(Alive, StartTimeout);
            return new Vst2BridgedPlugin(channel, Alive, () =>
            {
                if (!running.WaitForExit(2000))
                {
                    try
                    {
                        running.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                running.Dispose();
            }, architecture);
        }
        catch (Exception ex) when (ex is Vst2BridgeException or System.ComponentModel.Win32Exception or IOException)
        {
            if (process is { HasExited: false })
            {
                process.Kill();
            }
            process?.Dispose();
            channel.Dispose();
            throw ex is Vst2BridgeException ? ex : new Vst2BridgeException($"The bridge could not be started: {ex.Message}");
        }
    }

    /// <summary>The processor the plug-in runs on (that of its bridge).</summary>
    public ModuleArchitecture Architecture { get; }

    /// <inheritdoc />
    public override string Name => _description.Name;

    /// <inheritdoc />
    public override string Vendor => _description.Vendor;

    /// <inheritdoc />
    public override PluginFormat Format => PluginFormat.Vst2;

    /// <inheritdoc />
    public override bool IsInstrument => _description.IsInstrument;

    /// <inheritdoc />
    public override IReadOnlyList<PluginBus> InputBuses => _description.Inputs;

    /// <inheritdoc />
    public override IReadOnlyList<PluginBus> OutputBuses => _description.Outputs;

    /// <inheritdoc />
    public override int EventInputCount => _description.EventInputs;

    /// <inheritdoc />
    public override int EventOutputCount => _description.EventOutputs;

    /// <inheritdoc />
    public override IReadOnlyList<PluginParameter> Parameters => _description.Parameters;

    /// <inheritdoc />
    public override int LatencySamples => _description.Latency;

    /// <inheritdoc />
    public override int TailSamples => _description.Tail;

    /// <inheritdoc />
    public override bool HasEditor => _description.HasEditor;

    /// <inheritdoc />
    public override string PresetExtension => _description.PresetExtension;

    /// <summary>The plug-in's VST 2 identifier.</summary>
    public int UniqueId => _description.UniqueId;

    private byte[] Call(int operation, ReadOnlySpan<byte> payload)
    {
        lock (_control)
        {
            if (_failed) throw new Vst2BridgeException("The bridge stopped.");
            // A message handled while waiting (a bridged editor's window) must not start a second call on the same thread.
            if (_calling) throw new Vst2BridgeException("The bridge is busy.");
            _calling = true;
            try
            {
                return _channel.Call(operation, payload, _alive, CallTimeout);
            }
            catch (Vst2BridgeException) when (!_alive())
            {
                _failed = true;
                throw;
            }
            finally
            {
                _calling = false;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPrepare(double sampleRate, int maxBlockSize, bool realtime)
    {
        Call(BridgeOperation.Prepare, BridgePayload.Write(w =>
        {
            w.Write(sampleRate);
            w.Write(Math.Min(maxBlockSize, BridgeChannel.MaxFrames));
            w.Write(realtime);
        }));
        _prepared = true;
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        if (!_prepared) return;
        _prepared = false;
        if (!_failed) Call(BridgeOperation.Stop, []);
    }

    /// <inheritdoc />
    public override void AddMidiEvent(in MidiEvent e)
    {
        if (_midiCount < _midi.Length) _midi[_midiCount++] = e;
    }

    /// <inheritdoc />
    protected override void OnProcess(int frames, ParameterQueue pending)
    {
        if (_failed) throw new Vst2BridgeException("The bridge stopped.");
        int ins = Math.Min(InputChannelCount, BridgeChannel.MaxChannels), outs = Math.Min(OutputChannelCount, BridgeChannel.MaxChannels);
        int midiIndex = 0;
        for (int start = 0; start < frames; start += BridgeChannel.MaxFrames)
        {
            int count = Math.Min(BridgeChannel.MaxFrames, frames - start);
            ref var header = ref _channel.Audio;
            header.Frames = count;
            header.Position = SamplePosition + start;
            header.Tempo = Tempo;
            header.TimeSigNumerator = TimeSignature.Numerator;
            header.TimeSigDenominator = TimeSignature.Denominator;
            header.Playing = IsPlaying ? 1 : 0;
            var changes = _channel.Changes;
            int changeCount = 0;
            while (changeCount < changes.Length && pending.TryDequeue(out uint id, out double value)) changes[changeCount++] = new BridgeParameterChange { Id = id, Value = value };
            header.ChangeCount = changeCount;
            var midi = _channel.MidiIn;
            int midiCount = 0;
            while (midiIndex < _midiCount && _midi[midiIndex].SampleOffset < start + count)
            {
                var e = _midi[midiIndex++];
                midi[midiCount++] = new BridgeMidi { Offset = Math.Max(0, e.SampleOffset - start), Status = e.Status, Data1 = e.Data1, Data2 = e.Data2 };
            }
            header.MidiInCount = midiCount;
            header.InputChannels = ins;
            header.OutputChannels = outs;
            for (int c = 0; c < ins; c++) new ReadOnlySpan<float>(InputPointer(c) + start, count).CopyTo(new Span<float>(_channel.Input(c), count));
            Thread.MemoryBarrier();
            _channel.AudioRequest.Set();
            if (!_channel.AudioReply.WaitOne(BlockTimeout))
            {
                _failed = true;
                _midiCount = 0;
                throw new Vst2BridgeException(_alive() ? "The bridge does not answer." : "The bridge stopped.");
            }
            Thread.MemoryBarrier();
            if (header.Status != 0)
            {
                _midiCount = 0;
                throw new Vst2BridgeException($"'{Name}' failed while processing in the bridge.");
            }
            for (int c = 0; c < outs; c++) new ReadOnlySpan<float>(_channel.Output(c), count).CopyTo(new Span<float>(OutputPointer(c) + start, count));
            var produced = _channel.MidiOut;
            for (int i = 0; i < Math.Min(header.MidiOutCount, produced.Length); i++)
                AddOutputMidi(new MidiEvent(produced[i].Offset + start, produced[i].Status, produced[i].Data1, produced[i].Data2));
        }
        _midiCount = 0;
    }

    /// <inheritdoc />
    public override double GetParameter(uint id)
    {
        int index = ParameterIndex(id);
        return index >= 0 ? _channel.Parameters[index] : 0.0;
    }

    private int ParameterIndex(uint id)
    {
        // VST 2 parameters are numbered from 0: the identifier is the index.
        var parameters = _description.Parameters;
        if (id < (uint)parameters.Count && parameters[(int)id].Id == id) return id < BridgeChannel.MaxParameters ? (int)id : -1;
        int index = parameters.FindIndex(p => p.Id == id);
        return index < BridgeChannel.MaxParameters ? index : -1;
    }

    /// <inheritdoc />
    protected override void OnSetParameterUi(uint id, double normalized)
    {
        int index = ParameterIndex(id);
        if (index >= 0) _channel.Parameters[index] = normalized;
        // While processing, the change goes with the next block; otherwise the bridge applies it now.
        if (!_prepared && !_failed)
        {
            Call(BridgeOperation.SetParameter, BridgePayload.Write(w =>
            {
                w.Write(id);
                w.Write(normalized);
            }));
        }
    }

    /// <inheritdoc />
    public override string FormatParameter(uint id, double normalized)
    {
        if (_failed) return normalized.ToString("0.###", CultureInfo.InvariantCulture);
        // Parameter tables ask for the text of every value on every frame: the last text of each parameter is kept.
        lock (_formatted)
        {
            if (_formatted.TryGetValue(id, out var known) && known.Value == normalized) return known.Text;
        }
        var reply = Call(BridgeOperation.FormatParameter, BridgePayload.Write(w =>
        {
            w.Write(id);
            w.Write(normalized);
        }));
        string text = BridgePayload.Read(reply, r => r.ReadString());
        lock (_formatted) _formatted[id] = (normalized, text);
        return text;
    }

    /// <inheritdoc />
    public override bool TryParseParameter(uint id, string text, out double normalized)
    {
        ArgumentNullException.ThrowIfNull(text);
        normalized = 0;
        if (_failed) return false;
        var reply = Call(BridgeOperation.ParseParameter, BridgePayload.Write(w =>
        {
            w.Write(id);
            w.Write(text);
        }));
        (bool ok, normalized) = BridgePayload.Read(reply, r => (r.ReadBoolean(), r.ReadDouble()));
        return ok;
    }

    /// <inheritdoc />
    /// <remarks>One editor at a time: creating a new one closes the previous one.</remarks>
    public override Vst2BridgedEditor? CreateEditor()
    {
        if (!HasEditor || _failed) return null;
        _editor?.Dispose();
        var size = BridgePayload.Read(Call(BridgeOperation.EditorCreate, []), r => (r.ReadInt32(), r.ReadInt32()));
        _editor = new Vst2BridgedEditor(this, size);
        return _editor;
    }

    internal (int Width, int Height) OpenEditor(nint parent)
        => BridgePayload.Read(Call(BridgeOperation.EditorOpen, BridgePayload.Write(w => w.Write((long)parent))), r => (r.ReadInt32(), r.ReadInt32()));

    internal void CloseEditor(Vst2BridgedEditor editor)
    {
        if (!ReferenceEquals(_editor, editor)) return;
        _editor = null;
        if (_failed) return;
        try
        {
            Call(BridgeOperation.EditorClose, []);
        }
        catch (Vst2BridgeException)
        {
            // A bridge that stopped took its window with it.
        }
    }

    /// <inheritdoc />
    public override byte[] GetState() => Call(BridgeOperation.GetState, []);

    /// <inheritdoc />
    public override void SetState(ReadOnlySpan<byte> state)
    {
        Call(BridgeOperation.SetState, state);
        lock (_formatted) _formatted.Clear();
        Idle();
    }

    /// <inheritdoc />
    /// <remarks>One control call: the plug-in's own parameter changes, a new description when it restarted, and the size
    /// its editor asked for.</remarks>
    public override void Idle()
    {
        if (_failed) return;
        byte[] reply;
        try
        {
            reply = Call(BridgeOperation.Idle, []);
        }
        catch (Vst2BridgeException)
        {
            return;
        }
        var (changes, restarted, editorSize) = BridgePayload.Read(reply, r =>
        {
            int count = r.ReadInt32();
            var list = new List<(uint, double)>(count);
            for (int i = 0; i < count; i++) list.Add((r.ReadUInt32(), r.ReadDouble()));
            var description = r.ReadBoolean() ? BridgeDescription.Read(r) : null;
            (int, int)? size = r.ReadBoolean() ? (r.ReadInt32(), r.ReadInt32()) : null;
            return (list, description, size);
        });
        foreach (var (id, value) in changes) RaiseParameterChanged(id, value);
        if (restarted is not null)
        {
            lock (_formatted) _formatted.Clear();
            _description = restarted;
            RaiseRestarted();
        }
        if (editorSize is var (width, height)) _editor?.OnResizeRequested(width, height);
    }

    /// <summary><c>true</c> once the bridge stopped (a crash of the plug-in, or a block it did not finish).</summary>
    public bool HasFailed => _failed;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // The bridge closes the editor with the plug-in.
        _editor?.Detach();
        _editor = null;
        lock (_control)
        {
            if (!_failed && _alive())
            {
                try
                {
                    _channel.Call(BridgeOperation.Close, [], _alive, TimeSpan.FromSeconds(5));
                }
                catch (Vst2BridgeException)
                {
                }
            }
            _failed = true;
        }
        _closed?.Invoke();
        _channel.Dispose();
    }
}
