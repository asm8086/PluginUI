// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;

namespace PluginUI.Host.Vst2;

/// <summary>
/// Bridge side of <see cref="Vst2BridgedPlugin"/>: a process of another architecture (a 32-bit
/// <c>PluginHost.Bridge32.exe</c>) that loads one VST 2 plug-in and serves the host through a <see cref="BridgeChannel"/>.
/// </summary>
/// <remarks>
/// The plug-in is loaded and called on a single-threaded apartment thread, as in a DAW, which also answers the control
/// calls; the blocks are processed on a second thread. The bridge stops when the host closes it or stops itself.
/// </remarks>
public static class Vst2BridgeServer
{
    /// <summary>Command name: <c>bridge &lt;channel&gt; &lt;host process id&gt; &lt;module&gt; &lt;plug-in or -&gt; &lt;host name&gt;</c>.</summary>
    public const string CommandName = "bridge";

    /// <summary>Runs the bridge; <paramref name="arguments"/> are those after <see cref="CommandName"/>.</summary>
    /// <returns>Exit code: 0 closed by the host, 1 wrong arguments, 2 the plug-in could not be loaded, 3 the host stopped.</returns>
    public static int Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count < 5 || !int.TryParse(arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hostId))
        {
            Console.Error.WriteLine($"Usage: {CommandName} <channel> <host process id> <module> <plug-in or -> <host name>");
            return 1;
        }
        using var channel = BridgeChannel.Open(arguments[0]);
        channel.BridgeProcessId = Environment.ProcessId;
        Process? host = null;
        try
        {
            host = Process.GetProcessById(hostId);
        }
        catch (ArgumentException)
        {
            return 3;
        }
        int exit = 0;
        Catalog.ModuleScanProcess.RunOnUserInterfaceThread(() =>
        {
            Vst2Plugin plugin;
            try
            {
                plugin = Vst2Hosting.Load(arguments[2], arguments[3] == "-" ? null : arguments[3], arguments[4]);
            }
            catch (Exception ex)
            {
                channel.ReportStart(ex.Message);
                exit = 2;
                return;
            }
            using (plugin)
            {
                channel.ReportStart(null);
                exit = Serve(channel, plugin, () => !host.HasExited) ? 0 : 3;
            }
        });
        return exit;
    }

    /// <summary>
    /// Serves <paramref name="plugin"/> through <paramref name="channel"/> on this thread (the audio on another one) until the
    /// host closes the channel (<c>true</c>) or <paramref name="hostAlive"/> turns false.
    /// </summary>
    internal static bool Serve(BridgeChannel channel, Vst2Plugin plugin, Func<bool> hostAlive)
    {
        var session = new Session(channel, plugin);
        var audio = new Thread(() => session.ServeAudio(hostAlive)) { Name = "Bridge audio", IsBackground = true, Priority = ThreadPriority.Highest };
        audio.Start();
        try
        {
            while (!session.Closed)
            {
                if (!channel.Serve(session.Handle, 100) && !hostAlive()) return false;
            }
            return true;
        }
        finally
        {
            session.Closed = true;
            audio.Join(2000);
        }
    }

    private sealed unsafe class Session
    {
        private readonly BridgeChannel _channel;
        private readonly Vst2Plugin _plugin;
        private readonly Lock _gate = new();
        private readonly List<(uint Id, double Value)> _changes = [];
        private bool _restarted;
        private volatile bool _closed;

        public Session(BridgeChannel channel, Vst2Plugin plugin)
        {
            _channel = channel;
            _plugin = plugin;
            plugin.ParameterChanged += (_, id, value) =>
            {
                lock (_gate) _changes.Add((id, value));
            };
            plugin.Restarted += _ =>
            {
                lock (_gate) _restarted = true;
            };
            RefreshValues();
        }

        public bool Closed
        {
            get => _closed;
            set => _closed = value;
        }

        private void RefreshValues()
        {
            var values = _channel.Parameters;
            int count = Math.Min(_plugin.Parameters.Count, values.Length);
            for (int i = 0; i < count; i++) values[i] = _plugin.GetParameter(_plugin.Parameters[i].Id);
        }

        public byte[] Handle(int operation, ReadOnlySpan<byte> payload)
        {
            switch (operation)
            {
                case BridgeOperation.Describe:
                    return BridgePayload.Write(BridgeDescription.Of(_plugin).Write);
                case BridgeOperation.Prepare:
                {
                    var (rate, block, realtime) = BridgePayload.Read(payload, r => (r.ReadDouble(), r.ReadInt32(), r.ReadBoolean()));
                    _plugin.Prepare(rate, Math.Min(block, BridgeChannel.MaxFrames), realtime);
                    return [];
                }
                case BridgeOperation.Stop:
                    _plugin.Stop();
                    return [];
                case BridgeOperation.SetParameter:
                {
                    var (id, value) = BridgePayload.Read(payload, r => (r.ReadUInt32(), r.ReadDouble()));
                    _plugin.SetParameter(id, value);
                    RefreshValues();
                    return [];
                }
                case BridgeOperation.FormatParameter:
                {
                    var (id, value) = BridgePayload.Read(payload, r => (r.ReadUInt32(), r.ReadDouble()));
                    string text = _plugin.FormatParameter(id, value);
                    return BridgePayload.Write(w => w.Write(text));
                }
                case BridgeOperation.ParseParameter:
                {
                    var (id, text) = BridgePayload.Read(payload, r => (r.ReadUInt32(), r.ReadString()));
                    bool ok = _plugin.TryParseParameter(id, text, out double value);
                    return BridgePayload.Write(w =>
                    {
                        w.Write(ok);
                        w.Write(value);
                    });
                }
                case BridgeOperation.GetState:
                    return _plugin.GetState();
                case BridgeOperation.SetState:
                    _plugin.SetState(payload);
                    RefreshValues();
                    return [];
                case BridgeOperation.Idle:
                {
                    _plugin.Idle();
                    RefreshValues();
                    List<(uint Id, double Value)> changes;
                    bool restarted;
                    lock (_gate)
                    {
                        changes = [.. _changes];
                        _changes.Clear();
                        restarted = _restarted;
                        _restarted = false;
                    }
                    return BridgePayload.Write(w =>
                    {
                        w.Write(changes.Count);
                        foreach (var (id, value) in changes)
                        {
                            w.Write(id);
                            w.Write(value);
                        }
                        w.Write(restarted);
                        if (restarted) BridgeDescription.Of(_plugin).Write(w);
                    });
                }
                case BridgeOperation.Close:
                    _closed = true;
                    return [];
                default:
                    throw new Vst2BridgeException($"Unknown bridge operation {operation}.");
            }
        }

        public void ServeAudio(Func<bool> hostAlive)
        {
            while (!_closed)
            {
                if (!_channel.AudioRequest.WaitOne(100))
                {
                    if (!hostAlive()) return;
                    continue;
                }
                ref var header = ref _channel.Audio;
                header.Status = 0;
                try
                {
                    ProcessBlock(ref header);
                }
                catch (Exception)
                {
                    header.Status = 1;
                }
                Thread.MemoryBarrier();
                _channel.AudioReply.Set();
            }
        }

        private void ProcessBlock(ref BridgeAudioHeader header)
        {
            int frames = Math.Clamp(header.Frames, 0, Math.Min(BridgeChannel.MaxFrames, _plugin.MaxBlockSize));
            _plugin.Tempo = header.Tempo;
            _plugin.TimeSignature = (header.TimeSigNumerator, header.TimeSigDenominator);
            _plugin.IsPlaying = header.Playing != 0;
            var changes = _channel.Changes;
            for (int i = 0; i < Math.Min(header.ChangeCount, changes.Length); i++) _plugin.SetParameter(changes[i].Id, changes[i].Value);
            var midi = _channel.MidiIn;
            for (int i = 0; i < Math.Min(header.MidiInCount, midi.Length); i++)
                _plugin.AddMidiEvent(new MidiEvent(midi[i].Offset, midi[i].Status, midi[i].Data1, midi[i].Data2));
            int ins = Math.Min(_plugin.InputChannelCount, BridgeChannel.MaxChannels), outs = Math.Min(_plugin.OutputChannelCount, BridgeChannel.MaxChannels);
            for (int c = 0; c < ins; c++) new ReadOnlySpan<float>(_channel.Input(c), frames).CopyTo(_plugin.GetInput(c));
            _plugin.Process(frames);
            for (int c = 0; c < outs; c++) _plugin.GetOutput(c)[..frames].CopyTo(new Span<float>(_channel.Output(c), frames));
            var produced = _plugin.OutputMidi;
            var midiOut = _channel.MidiOut;
            int n = Math.Min(produced.Length, midiOut.Length);
            for (int i = 0; i < n; i++) midiOut[i] = new BridgeMidi { Offset = produced[i].SampleOffset, Status = produced[i].Status, Data1 = produced[i].Data1, Data2 = produced[i].Data2 };
            header.MidiOutCount = n;
            header.Latency = _plugin.LatencySamples;
        }
    }
}
