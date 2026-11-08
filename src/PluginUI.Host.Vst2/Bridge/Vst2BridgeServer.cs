// 2026/11/08 18:01:00 UTC - PluginUI
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
/// calls and runs the message loop of the plug-in's editor (a child window of a host window); the blocks are processed
/// on a second thread. The bridge stops when the host closes it or stops itself.
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
        bool windows = OperatingSystem.IsWindows();
        long nextIdle = 0;
        try
        {
            while (!session.Closed)
            {
                // With an editor open, the loop also dispatches its messages and gives it its idle time (about 30 per second).
                int wait = session.HasEditor ? 15 : 100;
                bool request = windows ? BridgePump.WaitDispatching(channel.ControlRequest, wait) : channel.ControlRequest.WaitOne(wait);
                if (request) session.Answer();
                else if (!hostAlive()) return false;
                if (session.HasEditor && Environment.TickCount64 >= nextIdle)
                {
                    nextIdle = Environment.TickCount64 + 30;
                    session.EditorIdle();
                }
            }
            return true;
        }
        finally
        {
            session.CloseEditor();
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
        private Vst2Editor? _editor;
        private (int Width, int Height)? _editorSize;
        private nuint _timer;
        private bool _answering;

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

        /// <summary><c>true</c> while the plug-in's editor is open.</summary>
        public bool HasEditor => _editor is { IsAttached: true };

        /// <summary>Answers the control call that arrived (not again while one is being answered).</summary>
        public void Answer()
        {
            if (_answering) return;
            _answering = true;
            try
            {
                _channel.Answer(Handle);
            }
            finally
            {
                _answering = false;
            }
        }

        // The plug-in's dialog boxes and menus run their own message loops: the timer still answers the host then.
        private void AnswerFromTimer()
        {
            if (!_answering && _channel.ControlRequest.WaitOne(0)) Answer();
        }

        /// <summary>Gives the open editor its idle time.</summary>
        public void EditorIdle()
        {
            try
            {
                _plugin.Idle();
            }
            catch (Exception)
            {
                // A plug-in that fails here fails on the next call too.
            }
        }

        public void CloseEditor()
        {
            if (_timer != 0)
            {
                BridgePump.StopTimer(_timer);
                _timer = 0;
            }
            _editor?.Dispose();
            _editor = null;
        }

        private byte[] EditorSize()
        {
            var (width, height) = _editor?.Size ?? (0, 0);
            return BridgePayload.Write(w =>
            {
                w.Write(width);
                w.Write(height);
            });
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
                    (int Width, int Height)? size;
                    lock (_gate)
                    {
                        changes = [.. _changes];
                        _changes.Clear();
                        restarted = _restarted;
                        _restarted = false;
                        size = _editorSize;
                        _editorSize = null;
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
                        w.Write(size.HasValue);
                        if (size is var (width, height))
                        {
                            w.Write(width);
                            w.Write(height);
                        }
                    });
                }
                case BridgeOperation.EditorCreate:
                    if (_editor is null)
                    {
                        _editor = _plugin.CreateEditor() ?? throw new Vst2BridgeException($"'{_plugin.Name}' has no editor.");
                        // The host learns the new size at its next idle call; the plug-in resizes its own window.
                        _editor.ResizeRequested = (width, height) =>
                        {
                            lock (_gate) _editorSize = (width, height);
                            return true;
                        };
                    }
                    return EditorSize();
                case BridgeOperation.EditorOpen:
                {
                    long parent = BridgePayload.Read(payload, r => r.ReadInt64());
                    if (_editor is null || !_editor.Attach((nint)parent)) throw new Vst2BridgeException("The editor could not be opened.");
                    if (OperatingSystem.IsWindows() && _timer == 0) _timer = BridgePump.StartTimer(10, AnswerFromTimer);
                    return EditorSize();
                }
                case BridgeOperation.EditorClose:
                    CloseEditor();
                    return [];
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
