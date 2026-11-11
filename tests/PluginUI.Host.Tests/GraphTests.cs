// 2026/11/11 15:14:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Host.Audio;
using PluginUI.Host.Graph;
using PluginUI.Host.Tests.Support;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Tests.Support
{
    /// <summary>
    /// A plug-in written in C# for graph tests: stereo in and out (optionally a side chain), a real delay of
    /// <see cref="Latency"/> samples, a gain, and a MIDI echo (note + <see cref="Transpose"/>).
    /// </summary>
    internal sealed class FakePlugin : PluginInstance
    {
        private readonly PluginBus[] _inputs;
        private readonly PluginBus[] _outputs;
        private readonly List<MidiEvent> _midi = [];
        private float[][] _delay = [];
        private int _position;

        public FakePlugin(string name = "Fake", int latency = 0, bool sideChain = false, bool instrument = false)
        {
            FakeName = name;
            Latency = latency;
            _inputs = instrument ? [] : sideChain ? [new("Main", 2, false, true), new("Side", 2, true, true)] : [new("Main", 2, false, true)];
            _outputs = [new("Out", 2, false, true)];
            IsInstrument = instrument;
        }

        public string FakeName { get; }

        public int Latency { get; set; }

        public float Gain { get; set; } = 1f;

        public int Transpose { get; set; } = 12;

        public bool Throws { get; set; }

        public List<int> Blocks { get; } = [];

        public List<MidiEvent> Received { get; } = [];

        public byte[] State { get; set; } = [1, 2, 3];

        public override string Name => FakeName;

        public override string Vendor => "Tests";

        public override PluginFormat Format => PluginFormat.Vst3;

        public override bool IsInstrument { get; }

        public override IReadOnlyList<PluginBus> InputBuses => _inputs;

        public override IReadOnlyList<PluginBus> OutputBuses => _outputs;

        public override int EventInputCount => 1;

        public override int EventOutputCount => 1;

        public override IReadOnlyList<PluginParameter> Parameters => [];

        public override int LatencySamples => Latency;

        public override int TailSamples => 0;

        public override bool HasEditor => false;

        public override void AddMidiEvent(in MidiEvent e) => _midi.Add(e);

        /// <summary>Changes the latency and tells the host, as a plug-in does through restartComponent.</summary>
        public void Restart(int latency)
        {
            Latency = latency;
            RaiseRestarted();
        }

        public override double GetParameter(uint id) => 0;

        protected override void OnSetParameterUi(uint id, double normalized)
        {
        }

        public override string FormatParameter(uint id, double normalized) => string.Empty;

        public override bool TryParseParameter(uint id, string text, out double normalized)
        {
            normalized = 0;
            return false;
        }

        public override byte[] GetState() => State;

        public override void SetState(ReadOnlySpan<byte> state)
        {
            if (RefusesState) throw new InvalidOperationException("state refused");
            State = state.ToArray();
        }

        public bool RefusesState { get; set; }

        protected override void OnPrepare(double sampleRate, int maxBlockSize, bool realtime)
        {
            _delay = [new float[Math.Max(1, Latency)], new float[Math.Max(1, Latency)]];
            _position = 0;
        }

        protected override void OnStop()
        {
        }

        protected override void OnProcess(int frames, ParameterQueue pending)
        {
            if (Throws) throw new InvalidOperationException("boom");
            Blocks.Add(frames);
            for (int c = 0; c < 2; c++)
            {
                var output = GetOutput(c)[..frames];
                var input = IsInstrument ? default : GetInput(c)[..frames];
                int p = _position;
                for (int i = 0; i < frames; i++)
                {
                    float x = IsInstrument ? 0f : input[i];
                    if (!IsInstrument && _inputs.Length > 1) x += GetInput(2 + c)[i];
                    if (Latency == 0)
                    {
                        output[i] = x * Gain;
                        continue;
                    }
                    output[i] = _delay[c][p] * Gain;
                    _delay[c][p] = x;
                    p = (p + 1) % Latency;
                }
                if (c == 1) _position = p;
            }
            foreach (var e in _midi)
            {
                Received.Add(e);
                if (e.Type is 0x90 or 0x80) AddOutputMidi(e with { Data1 = (byte)Math.Clamp(e.Data1 + Transpose, 0, 127) });
            }
            _midi.Clear();
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>Collects MIDI on the audio thread.</summary>
    internal sealed class MidiCollector : IMidiSink
    {
        public List<MidiEvent> Events { get; } = [];

        public void Send(in MidiEvent e) => Events.Add(e);
    }
}

namespace PluginUI.Host.Tests
{
    public class GraphTests
    {
        private const int Block = 64;

        private static PluginGraph Prepared(int block = Block, int outputs = 2, int inputs = 0)
        {
            var graph = new PluginGraph();
            graph.Prepare(48000, block, inputs, outputs);
            return graph;
        }

        /// <summary>Renders <paramref name="blocks"/> blocks and returns the device output channels.</summary>
        private static float[][] Run(PluginGraph graph, int blocks, int block = Block, int outputs = 2, TestBuffers? input = null)
        {
            var result = new List<float>[outputs];
            for (int c = 0; c < outputs; c++) result[c] = [];
            using var none = new TestBuffers(0, block);
            using var output = new TestBuffers(outputs, block);
            for (int n = 0; n < blocks; n++)
            {
                graph.Render(input?.Buffers ?? none.Buffers, output.Buffers);
                for (int c = 0; c < outputs; c++) result[c].AddRange(output.Channel(c));
            }
            return [.. result.Select(r => r.ToArray())];
        }

        private static TestSignalNode Impulse(double levelDb = 0) => new() { Generator = { Signal = TestSignal.Impulses, Period = 100, LevelDb = levelDb } };

        private static int[] Peaks(float[] x, float threshold = 1e-6f) => [.. x.Select((v, i) => (v, i)).Where(p => MathF.Abs(p.v) > threshold).Select(p => p.i)];

        [Test]
        public void ASourceThroughAGainReachesTheDevice()
        {
            using var graph = Prepared();
            var source = graph.Add(Impulse());
            var gain = graph.Add(new GainNode { GainDb = -6.0206 });
            var output = graph.Add(new AudioOutputNode());
            graph.Connect(source.Outputs[0], gain.Inputs[0]);
            graph.Connect(gain.Outputs[0], output.Inputs[0]);
            var x = Run(graph, 2);
            var peaks = new float[2];
            output.Meter.Read(peaks);
            Assert.Multiple(() =>
            {
                Assert.That(x[0][0], Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(x[1][0], Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(Peaks(x[0]), Is.EqualTo(new[] { 0 }));
                Assert.That(peaks[0], Is.EqualTo(0.5f).Within(1e-4f), "the output module meters what it plays");
            });
        }

        [Test]
        public void AnInputTakesOneCableAndAnOutputFeedsMany()
        {
            using var graph = Prepared();
            var a = graph.Add(Impulse());
            var b = graph.Add(Impulse());
            var gain = graph.Add(new GainNode());
            var left = graph.Add(new AudioOutputNode { FirstChannel = 0, Channels = 1 });
            var right = graph.Add(new AudioOutputNode { FirstChannel = 1, Channels = 1 });
            graph.Connect(a.Outputs[0], gain.Inputs[0]);
            var replacing = graph.Connect(b.Outputs[0], gain.Inputs[0]);
            graph.Connect(gain.Outputs[0], left.Inputs[0]);
            graph.Connect(gain.Outputs[0], right.Inputs[0]);
            Assert.Multiple(() =>
            {
                Assert.That(graph.Connections, Has.Count.EqualTo(3), "the second cable replaced the first");
                Assert.That(graph.ConnectionTo(gain.Inputs[0]), Is.SameAs(replacing));
                Assert.That(graph.ConnectionsFrom(gain.Outputs[0]).Count(), Is.EqualTo(2));
                Assert.That(graph.WhyNotConnect(gain.Outputs[0], gain.Outputs[0]), Is.Not.Null);
                Assert.Throws<ArgumentException>(() => graph.Connect(a.Outputs[0], graph.Add(new MidiOutputNode()).Inputs[0]), "audio to MIDI");
            });
            var x = Run(graph, 1);
            Assert.Multiple(() =>
            {
                Assert.That(x[0][0], Is.EqualTo(1f), "stereo average into a mono output");
                Assert.That(x[1][0], Is.EqualTo(1f));
            });
        }

        [Test]
        public void MonoFeedsEveryChannelAndChannelsAverageIntoMono()
        {
            using var graph = Prepared(outputs: 3);
            var mono = graph.Add(new TestSignalNode { Channels = 1, Generator = { Signal = TestSignal.Impulses, Period = 100, LevelDb = 0 } });
            var stereo = graph.Add(new GainNode { Channels = 2 });
            var output = graph.Add(new AudioOutputNode { Channels = 2 });
            var down = graph.Add(new GainNode { Channels = 1 });
            var third = graph.Add(new AudioOutputNode { FirstChannel = 2, Channels = 1 });
            var half = graph.Add(new TestSignalNode { Channels = 2, Generator = { Signal = TestSignal.Impulses, Period = 100, LevelDb = -6.0206 } });
            var side = graph.Add(new GainNode { Channels = 2 });
            graph.Connect(mono.Outputs[0], stereo.Inputs[0]);
            graph.Connect(stereo.Outputs[0], output.Inputs[0]);
            graph.Connect(half.Outputs[0], side.Inputs[0]);
            side.Invert = true;
            graph.Connect(side.Outputs[0], down.Inputs[0]);
            graph.Connect(down.Outputs[0], third.Inputs[0]);
            var x = Run(graph, 1, outputs: 3);
            Assert.Multiple(() =>
            {
                Assert.That(x[0][0], Is.EqualTo(1f));
                Assert.That(x[1][0], Is.EqualTo(1f), "the mono source feeds both channels");
                Assert.That(x[2][0], Is.EqualTo(-0.5f).Within(1e-4f), "two channels averaged into one, inverted");
            });
        }

        [Test]
        public void DemuxerAndMuxerSplitAndJoinChannels()
        {
            using var graph = Prepared();
            var left = graph.Add(new TestSignalNode { Channels = 1, Generator = { Signal = TestSignal.Impulses, Period = 100, LevelDb = 0 } });
            var right = graph.Add(new TestSignalNode { Channels = 1, Generator = { Signal = TestSignal.Impulses, Period = 100, LevelDb = -6.0206 } });
            var mux = graph.Add(new MuxNode());
            var demux = graph.Add(new DemuxNode());
            var swap = graph.Add(new MuxNode());
            var quiet = graph.Add(new GainNode { Channels = 1, GainDb = -6.0206 });
            var output = graph.Add(new AudioOutputNode());
            graph.Connect(left.Outputs[0], mux.Inputs[0]);
            graph.Connect(right.Outputs[0], mux.Inputs[1]);
            graph.Connect(mux.Outputs[0], demux.Inputs[0]);
            graph.Connect(demux.Outputs[0], quiet.Inputs[0]);
            graph.Connect(quiet.Outputs[0], swap.Inputs[1]);
            graph.Connect(demux.Outputs[1], swap.Inputs[0]);
            graph.Connect(swap.Outputs[0], output.Inputs[0]);
            var x = Run(graph, 1);
            var peaks = new float[2];
            demux.Meter.Read(peaks);
            Assert.Multiple(() =>
            {
                Assert.That(mux.Inputs.Select(p => (p.Name, p.Channels)), Is.EqualTo(new[] { ("L", 1), ("R", 1) }));
                Assert.That(mux.Outputs[0].Channels, Is.EqualTo(2));
                Assert.That(demux.Outputs.Select(p => p.Key), Is.EqualTo(new[] { "out1", "out2" }));
                Assert.That(x[0][0], Is.EqualTo(0.5f).Within(1e-4f), "the right channel now on the left");
                Assert.That(x[1][0], Is.EqualTo(0.5f).Within(1e-4f), "the left channel, 6 dB lower, on the right");
                Assert.That(peaks, Is.EqualTo(new[] { 1f, 0.5f }).Within(1e-4f), "the demuxer meters its input");
            });
            mux.Channels = 4;
            Assert.That(mux.Inputs, Has.Count.EqualTo(4));
            Assert.That(mux.Inputs[2].Name, Is.EqualTo("Ch 3"));
            string json = graph.ToJson();
            using var copy = new PluginGraph();
            Assert.That(copy.LoadJson(json, _ => throw new InvalidOperationException()), Is.Empty);
            Assert.That(copy.Nodes.OfType<MuxNode>().Select(m => m.Channels), Is.EqualTo(new[] { 4, 2 }));
        }

        [Test]
        public void ALoopIsBrokenByItsMostRecentCableWithOneBlockOfDelay()
        {
            using var graph = Prepared();
            var source = graph.Add(Impulse());
            var sum = graph.Add(new AudioSumNode());
            var gain = graph.Add(new GainNode { GainDb = -6.0206 });
            var output = graph.Add(new AudioOutputNode());
            graph.Connect(source.Outputs[0], sum.Inputs[0]);
            graph.Connect(sum.Outputs[0], gain.Inputs[0]);
            graph.Connect(sum.Outputs[0], output.Inputs[0]);
            var feedback = graph.Connect(gain.Outputs[0], sum.Inputs[1]);
            Assert.Multiple(() =>
            {
                Assert.That(feedback.IsFeedback, Is.True, "the cable that closed the loop");
                Assert.That(graph.Connections.Count(c => c.IsFeedback), Is.EqualTo(1));
                Assert.That(feedback.DelaySamples, Is.EqualTo(Block));
                Assert.That(sum.IsInLoop && gain.IsInLoop, Is.True);
                Assert.That(source.IsInLoop || output.IsInLoop, Is.False);
            });
            var x = Run(graph, 4)[0];
            Assert.Multiple(() =>
            {
                Assert.That(Peaks(x), Is.EqualTo(new[] { 0, 64, 128, 192 }), "one echo per block");
                Assert.That(x[64], Is.EqualTo(0.5f).Within(1e-4f));
                Assert.That(x[192], Is.EqualTo(0.125f).Within(1e-4f));
            });

            graph.BreakLoopAt(graph.ConnectionTo(gain.Inputs[0])!);
            Assert.Multiple(() =>
            {
                Assert.That(feedback.IsFeedback, Is.False, "the loop is now broken before the gain");
                Assert.That(graph.ConnectionTo(gain.Inputs[0])!.IsFeedback, Is.True);
            });
        }

        [Test]
        public void ALoopRunsInSubBlocksWhenAsked()
        {
            using var graph = Prepared();
            var fake = new FakePlugin("In the loop");
            var source = graph.Add(Impulse());
            var sum = graph.Add(new AudioSumNode());
            var plugin = graph.Add(new PluginNode(fake));
            var after = new FakePlugin("After");
            var outside = graph.Add(new PluginNode(after));
            var output = graph.Add(new AudioOutputNode());
            fake.Gain = 0.5f;
            graph.Connect(source.Outputs[0], sum.Inputs[0]);
            graph.Connect(sum.Outputs[0], plugin.Inputs[0]);
            graph.Connect(plugin.Outputs[0], outside.Inputs[0]);
            graph.Connect(outside.Outputs[0], output.Inputs[0]);
            var feedback = graph.Connect(plugin.Outputs[0], sum.Inputs[1]);
            graph.LoopBlockSize = 16;
            Assert.That(feedback.DelaySamples, Is.EqualTo(16));
            var x = Run(graph, 2)[0];
            Assert.Multiple(() =>
            {
                Assert.That(Peaks(x).Take(4), Is.EqualTo(new[] { 0, 16, 32, 48 }), "one echo per sub-block");
                Assert.That(x[16], Is.EqualTo(0.25f).Within(1e-5f));
                Assert.That(fake.Blocks, Is.All.EqualTo(16), "the loop's plug-in runs in sub-blocks");
                Assert.That(after.Blocks, Is.All.EqualTo(Block), "the rest of the graph runs on whole blocks");
            });
        }

        [Test]
        public void ASumAlignsPathsOfDifferentLatency()
        {
            using var graph = Prepared();
            var source = graph.Add(Impulse());
            var delayed = graph.Add(new PluginNode(new FakePlugin("Lookahead", latency: 32)));
            var sum = graph.Add(new AudioSumNode());
            var output = graph.Add(new AudioOutputNode());
            graph.Connect(source.Outputs[0], delayed.Inputs[0]);
            graph.Connect(delayed.Outputs[0], sum.Inputs[0]);
            var dry = graph.Connect(source.Outputs[0], sum.Inputs[1]);
            graph.Connect(sum.Outputs[0], output.Inputs[0]);
            Assert.Multiple(() =>
            {
                Assert.That(dry.DelaySamples, Is.EqualTo(32), "the dry path waits for the plug-in");
                Assert.That(delayed.PathLatencySamples, Is.EqualTo(32));
                Assert.That(sum.PathLatencySamples, Is.EqualTo(32));
            });
            var aligned = Run(graph, 2)[0];
            Assert.Multiple(() =>
            {
                Assert.That(Peaks(aligned), Is.EqualTo(new[] { 32 }));
                Assert.That(aligned[32], Is.EqualTo(2f));
            });

            ((FakePlugin)delayed.Plugin).Restart(48);
            Assert.That(dry.DelaySamples, Is.EqualTo(48), "a plug-in that changes its latency is followed");
            graph.Prepare(48000, Block, 0, 2); // the impulse source starts again
            Assert.That(Peaks(Run(graph, 2)[0]), Is.EqualTo(new[] { 48 }));

            sum.CompensateLatency = false;
            Assert.That(dry.DelaySamples, Is.Zero);
            graph.Prepare(48000, Block, 0, 2);
            var apart = Run(graph, 2)[0];
            Assert.That(Peaks(apart), Is.EqualTo(new[] { 0, 48 }));
        }

        [Test]
        public void MidiFlowsThroughMergesPluginsAndTheOutputPort()
        {
            using var graph = Prepared();
            var sink = new MidiCollector();
            graph.MidiOutput = sink;
            var keys = graph.Add(new KeyboardNode { Channel = 2 });
            var port = graph.Add(new MidiInputNode());
            var merge = graph.Add(new MidiMergeNode());
            var fake = new FakePlugin("Echo", instrument: true);
            var plugin = graph.Add(new PluginNode(fake));
            var midiOut = graph.Add(new MidiOutputNode());
            graph.Connect(keys.Outputs[0], merge.Inputs[0]);
            graph.Connect(port.Outputs[0], merge.Inputs[1]);
            graph.Connect(merge.Outputs[0], plugin.FindInput("midi-in")!);
            graph.Connect(plugin.FindOutput("midi-out")!, midiOut.Inputs[0]);
            keys.NoteOn(60);
            graph.PostMidiInput(MidiEvent.NoteOn(0, 0, 64, 90));
            Run(graph, 1);
            Assert.Multiple(() =>
            {
                Assert.That(fake.Received.Select(e => (e.Status, e.Data1)), Is.EquivalentTo(new[] { ((byte)0x91, (byte)60), ((byte)0x90, (byte)64) }));
                Assert.That(sink.Events.Select(e => e.Data1), Is.EquivalentTo(new[] { (byte)72, (byte)76 }), "transposed by the plug-in, sent to the port");
            });
        }

        [Test]
        public void TheMidiTransposeModuleMovesNotesAndLetsTheRestPass()
        {
            using var graph = Prepared();
            var sink = new MidiCollector();
            graph.MidiOutput = sink;
            var port = graph.Add(new MidiInputNode());
            var transpose = graph.Add(new MidiTransposeNode { Semitones = 12 });
            var midiOut = graph.Add(new MidiOutputNode());
            graph.Connect(port.Outputs[0], transpose.Inputs[0]);
            graph.Connect(transpose.Outputs[0], midiOut.Inputs[0]);
            List<(int Status, int Data1, int Data2)> Play(params MidiEvent[] events)
            {
                sink.Events.Clear();
                foreach (var e in events) graph.PostMidiInput(e);
                Run(graph, 1);
                return [.. sink.Events.Select(e => ((int)e.Status, (int)e.Data1, (int)e.Data2))];
            }

            Assert.That(Play(MidiEvent.NoteOn(0, 0, 60, 100), MidiEvent.ControlChange(0, 0, 1, 64), new MidiEvent(0, 0xA0, 60, 30)),
                Is.EqualTo(new[] { (0x90, 72, 100), (0xB0, 1, 64), (0xA0, 72, 30) }), "the note and its aftertouch move, the controller does not");

            // Changed while the note is held: its note off still goes to the note that sounds.
            transpose.Semitones = -5;
            Assert.That(Play(MidiEvent.NoteOff(0, 0, 60), MidiEvent.NoteOn(0, 0, 64, 90), MidiEvent.NoteOn(0, 0, 64, 0)),
                Is.EqualTo(new[] { (0x80, 72, 64), (0x90, 59, 90), (0x90, 59, 0) }), "a note on at velocity 0 is a note off");

            // Out of range: dropped with its note off, or folded back by octaves.
            transpose.Semitones = 12;
            Assert.That(Play(MidiEvent.NoteOn(0, 0, 120, 100)), Is.Empty);
            transpose.FoldIntoRange = true;
            Assert.That(Play(MidiEvent.NoteOff(0, 0, 120), MidiEvent.NoteOn(0, 1, 120, 100)), Is.EqualTo(new[] { (0x91, 120, 100) }),
                "the dropped note's note off is dropped too; 132 folds back to 120");
            Assert.That(transpose.Transpose(127), Is.EqualTo(127));
            transpose.Semitones = -48;
            Assert.That(transpose.Transpose(2), Is.EqualTo(2), "-46 is folded up by four octaves");
            transpose.FoldIntoRange = false;
            Assert.That(transpose.Transpose(2), Is.EqualTo(-1));

            // One channel only; the notes of the others pass unchanged.
            transpose.Semitones = 2;
            transpose.Channel = 3;
            Assert.That(Play(MidiEvent.NoteOn(0, 2, 60, 100), MidiEvent.NoteOn(0, 0, 60, 100)), Is.EqualTo(new[] { (0x92, 62, 100), (0x90, 60, 100) }));
            transpose.Channel = 0;
            Assert.That(Play(MidiEvent.NoteOff(0, 2, 60), MidiEvent.NoteOff(0, 0, 60)), Is.EqualTo(new[] { (0x82, 62, 64), (0x80, 60, 64) }),
                "each note off follows its own note on");

            // All notes off forgets the held notes of its channel.
            Play(MidiEvent.NoteOn(0, 0, 60, 100));
            transpose.Semitones = 7;
            Assert.That(Play(MidiEvent.ControlChange(0, 0, 123, 0), MidiEvent.NoteOff(0, 0, 60)), Is.EqualTo(new[] { (0xB0, 123, 0), (0x80, 67, 64) }));

            string json = graph.ToJson();
            using var copy = new PluginGraph();
            Assert.That(copy.LoadJson(json, _ => throw new InvalidOperationException()), Is.Empty);
            var restored = copy.Nodes.OfType<MidiTransposeNode>().Single();
            Assert.That((restored.Semitones, restored.Channel, restored.FoldIntoRange), Is.EqualTo((7, 0, false)));
            Assert.That(new MidiTransposeNode { Semitones = 100, Channel = 40 }, Has.Property("Semitones").EqualTo(48).And.Property("Channel").EqualTo(16));
        }

        // A MIDI input → module → MIDI output graph, and a way to play events through it.
        private static Func<MidiEvent[], List<(int Status, int Data1, int Data2)>> MidiChain(PluginGraph graph, GraphNode module)
        {
            var sink = new MidiCollector();
            graph.MidiOutput = sink;
            var port = graph.Add(new MidiInputNode());
            graph.Add(module);
            var midiOut = graph.Add(new MidiOutputNode());
            graph.Connect(port.Outputs[0], module.Inputs[0]);
            graph.Connect(module.Outputs[0], midiOut.Inputs[0]);
            return events =>
            {
                sink.Events.Clear();
                foreach (var e in events) graph.PostMidiInput(e);
                Run(graph, 1);
                return [.. sink.Events.Select(e => ((int)e.Status, (int)e.Data1, (int)e.Data2))];
            };
        }

        [Test]
        public void TheMidiVelocityModuleGivesEveryNoteTheSameVelocity()
        {
            using var graph = Prepared();
            var velocity = new MidiVelocityNode();
            var play = MidiChain(graph, velocity);
            Assert.That(velocity.Velocity, Is.EqualTo(100));

            Assert.That(play([MidiEvent.NoteOn(0, 0, 60, 12), MidiEvent.NoteOn(0, 1, 62, 127), MidiEvent.NoteOff(0, 0, 60, 40), MidiEvent.NoteOn(0, 1, 62, 0),
                    MidiEvent.ControlChange(0, 0, 7, 90), new MidiEvent(0, 0xA0, 60, 30)]),
                Is.EqualTo(new[] { (0x90, 60, 100), (0x91, 62, 100), (0x80, 60, 40), (0x91, 62, 0), (0xB0, 7, 90), (0xA0, 60, 30) }),
                "note ons only; a note on at velocity 0 stays a note off");

            velocity.Velocity = 64;
            velocity.Channel = 2;
            Assert.That(play([MidiEvent.NoteOn(0, 0, 60, 12), MidiEvent.NoteOn(0, 1, 62, 127)]), Is.EqualTo(new[] { (0x90, 60, 12), (0x91, 62, 64) }));

            string json = graph.ToJson();
            using var copy = new PluginGraph();
            Assert.That(copy.LoadJson(json, _ => throw new InvalidOperationException()), Is.Empty);
            var restored = copy.Nodes.OfType<MidiVelocityNode>().Single();
            Assert.That((restored.Velocity, restored.Channel), Is.EqualTo((64, 2)));
            Assert.That(new MidiVelocityNode { Velocity = 0 }.Velocity, Is.EqualTo(1), "0 would make note offs");
            Assert.That(new MidiVelocityNode { Velocity = 300 }.Velocity, Is.EqualTo(127));
        }

        [Test]
        public void TheMidiFilterLetsThroughTheChosenChannelsKindsAndNotes()
        {
            using var graph = Prepared();
            var filter = new MidiFilterNode();
            var play = MidiChain(graph, filter);
            MidiEvent[] mixed =
            [
                MidiEvent.NoteOn(0, 0, 60, 100), MidiEvent.NoteOn(0, 9, 36, 100), MidiEvent.ControlChange(0, 0, 64, 127), MidiEvent.PitchBend(0, 1, 9000),
                new MidiEvent(0, 0xC0, 5, 0), new MidiEvent(0, 0xD0, 40, 0), new MidiEvent(0, 0xF8, 0, 0),
            ];
            Assert.That(filter.PassesEverything, Is.True);
            Assert.That(play(mixed), Has.Count.EqualTo(7));

            filter.SetChannel(10, false);
            Assert.That(filter.PassesChannel(10), Is.False);
            Assert.That(play(mixed).Select(e => e.Status), Is.EqualTo(new[] { 0x90, 0xB0, 0xE1, 0xC0, 0xD0, 0xF8 }), "the drums of channel 10 are out");

            filter.Kinds = MidiMessageKinds.Notes | MidiMessageKinds.System;
            Assert.That(play(mixed).Select(e => e.Status), Is.EqualTo(new[] { 0x90, 0xF8 }), "the clock has no channel");

            // A note range splits the keyboard; the note off of a note let through passes after any change.
            filter.ChannelMask = 0xFFFF;
            filter.Kinds = MidiMessageKinds.All;
            filter.LowestNote = 60;
            Assert.That(play([MidiEvent.NoteOn(0, 0, 59, 100), MidiEvent.NoteOn(0, 0, 60, 100), MidiEvent.NoteOn(0, 0, 72, 100)]).Select(e => e.Data1),
                Is.EqualTo(new[] { 60, 72 }));
            filter.HighestNote = 64;
            filter.SetChannel(1, false);
            Assert.That(play([MidiEvent.NoteOff(0, 0, 72), MidiEvent.NoteOn(0, 0, 72, 0), MidiEvent.NoteOff(0, 0, 59), MidiEvent.NoteOff(0, 0, 60)]),
                Is.EqualTo(new[] { (0x80, 72, 64), (0x80, 60, 64) }), "only the notes it let through, once each");

            filter.SetChannel(1, true);
            Assert.That(play([MidiEvent.NoteOn(0, 0, 61, 100)]), Has.Count.EqualTo(1));
            Assert.That(play([MidiEvent.ControlChange(0, 0, 123, 0)]), Has.Count.EqualTo(1));
            filter.LowestNote = 0;
            filter.HighestNote = 0;
            Assert.That(play([MidiEvent.NoteOff(0, 0, 61)]), Is.Empty, "all notes off forgot the note");

            filter.ChannelMask = 0b101;
            filter.Kinds = MidiMessageKinds.ControlChange;
            string json = graph.ToJson();
            using var copy = new PluginGraph();
            Assert.That(copy.LoadJson(json, _ => throw new InvalidOperationException()), Is.Empty);
            var restored = copy.Nodes.OfType<MidiFilterNode>().Single();
            Assert.That((restored.ChannelMask, restored.Kinds, restored.LowestNote, restored.HighestNote), Is.EqualTo((0b101, MidiMessageKinds.ControlChange, 0, 0)));
        }

        [Test]
        public void AMidiLoopDeliversOneBlockLate()
        {
            using var graph = Prepared();
            var keys = graph.Add(new KeyboardNode());
            var merge = graph.Add(new MidiMergeNode());
            var fake = new FakePlugin("Echo", instrument: true) { Transpose = 1 };
            var plugin = graph.Add(new PluginNode(fake));
            graph.Connect(keys.Outputs[0], merge.Inputs[0]);
            graph.Connect(merge.Outputs[0], plugin.FindInput("midi-in")!);
            var back = graph.Connect(plugin.FindOutput("midi-out")!, merge.Inputs[1]);
            Assert.That(back.IsFeedback, Is.True);
            keys.NoteOn(60);
            Run(graph, 3);
            Assert.That(fake.Received.Select(e => (int)e.Data1), Is.EqualTo(new[] { 60, 61, 62 }), "each block the echo comes back one note higher");
        }

        [Test]
        public void AFailingNodeFallsSilentAndIsReported()
        {
            using var graph = Prepared();
            var fake = new FakePlugin { Throws = true };
            var source = graph.Add(Impulse());
            var plugin = graph.Add(new PluginNode(fake));
            var output = graph.Add(new AudioOutputNode());
            graph.Connect(source.Outputs[0], plugin.Inputs[0]);
            graph.Connect(plugin.Outputs[0], output.Inputs[0]);
            GraphNode? failed = null;
            graph.NodeFailed += (node, _) => failed = node;
            var x = Run(graph, 2)[0];
            Assert.Multiple(() =>
            {
                Assert.That(failed, Is.SameAs(plugin));
                Assert.That(plugin.HasFailed, Is.True);
                Assert.That(plugin.Failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(x.Max(), Is.Zero);
            });
            fake.Throws = false;
            graph.Prepare(48000, Block, 0, 2);
            Assert.That(plugin.HasFailed, Is.False, "preparing again gives it another chance");
        }

        [Test]
        public void CablesFollowTheModulesAndTheirPorts()
        {
            using var graph = Prepared();
            var a = graph.Add(Impulse());
            var b = graph.Add(Impulse());
            var c = graph.Add(Impulse());
            var sum = graph.Add(new AudioSumNode { InputCount = 3 });
            graph.Connect(a.Outputs[0], sum.Inputs[0]);
            graph.Connect(b.Outputs[0], sum.Inputs[1]);
            graph.Connect(c.Outputs[0], sum.Inputs[2]);
            int changes = 0;
            graph.Changed += () => changes++;
            sum.InputCount = 2;
            Assert.That(graph.Connections, Has.Count.EqualTo(2), "the cable of the removed input went with it");
            graph.Remove(a);
            Assert.Multiple(() =>
            {
                Assert.That(graph.Connections, Has.Count.EqualTo(1));
                Assert.That(graph.Nodes, Has.Count.EqualTo(3));
                Assert.That(a.Graph, Is.Null);
                Assert.That(changes, Is.EqualTo(2));
            });
        }

        [Test]
        public void DeviceInputsFeedTheGraph()
        {
            using var graph = Prepared(inputs: 4);
            var input = graph.Add(new AudioInputNode { FirstChannel = 2, Channels = 2 });
            var output = graph.Add(new AudioOutputNode());
            graph.Connect(input.Outputs[0], output.Inputs[0]);
            using var device = new TestBuffers(4, Block);
            var buffers = device.Buffers;
            buffers[2][5] = 0.25f;
            buffers[3][7] = -0.5f;
            buffers[0][1] = 1f;
            var x = Run(graph, 1, input: device);
            Assert.Multiple(() =>
            {
                Assert.That(Peaks(x[0]), Is.EqualTo(new[] { 5 }));
                Assert.That(x[0][5], Is.EqualTo(0.25f));
                Assert.That(x[1][7], Is.EqualTo(-0.5f));
            });
        }

        [Test]
        public void RealPluginsKeepTheirLatencyAlignedInTheGraph()
        {
            using var module = Vst3Module.FromFactory(TestGainPlugin.GetFactory().Export(), "Host Tests");
            using var plugin = module.CreatePlugin("Test Gain");
            using var graph = Prepared();
            var source = graph.Add(Impulse());
            var node = graph.Add(new PluginNode(plugin));
            var sum = graph.Add(new AudioSumNode());
            var output = graph.Add(new AudioOutputNode());
            graph.Connect(source.Outputs[0], node.Inputs[0]);
            graph.Connect(node.Outputs[0], sum.Inputs[0]);
            graph.Connect(source.Outputs[0], sum.Inputs[1]);
            graph.Connect(sum.Outputs[0], output.Inputs[0]);
            var x = Run(graph, 2)[0];
            Assert.Multiple(() =>
            {
                Assert.That(node.Inputs.Select(p => (p.Key, p.Channels)), Is.EqualTo(new[] { ("in0", 2) }));
                Assert.That(Peaks(x), Is.EqualTo(new[] { 32 }), "the dry path is delayed by the plug-in's 32 samples");
                Assert.That(x[32], Is.EqualTo(2f).Within(1e-5f));
            });
        }

        [Test]
        public void GraphsRenderOfflineWithTheirLatencyRemoved()
        {
            using var graph = new PluginGraph();
            var input = graph.Add(new AudioInputNode());
            var plugin = graph.Add(new PluginNode(new FakePlugin("Lookahead", latency: 100)));
            var sum = graph.Add(new AudioSumNode());
            var output = graph.Add(new AudioOutputNode { FirstChannel = 1 });
            graph.Connect(input.Outputs[0], plugin.Inputs[0]);
            graph.Connect(plugin.Outputs[0], sum.Inputs[0]);
            graph.Connect(input.Outputs[0], sum.Inputs[1]);
            graph.Connect(sum.Outputs[0], output.Inputs[0]);
            var audio = AudioBuffer.Create(44100, 2, 1000);
            audio.Channels[0][10] = 0.25f;
            audio.Channels[1][10] = 0.25f;
            var result = OfflineRenderer.Render(graph, audio, options: new RenderOptions { BlockSize = 64, TailSeconds = 0 });
            Assert.Multiple(() =>
            {
                Assert.That(result.SampleRate, Is.EqualTo(44100));
                Assert.That(result.ChannelCount, Is.EqualTo(3), "the output module uses channels 2 and 3");
                Assert.That(result.Length, Is.EqualTo(1000));
                Assert.That(Peaks(result.Channels[1]), Is.EqualTo(new[] { 10 }), "aligned, and the 100 samples of latency removed");
                Assert.That(result.Channels[2][10], Is.EqualTo(0.5f));
                Assert.That(result.Channels[0].Max(), Is.Zero);
                Assert.That(graph.Realtime, Is.True, "restored after the rendering");
            });
        }

        [Test]
        public void GraphsRenderMidiOffline()
        {
            using var graph = new PluginGraph();
            var port = graph.Add(new MidiInputNode());
            var echo = graph.Add(new PluginNode(new FakePlugin("Echo", instrument: true)));
            var midiOut = graph.Add(new MidiOutputNode());
            graph.Connect(port.Outputs[0], echo.FindInput("midi-in")!);
            graph.Connect(echo.FindOutput("midi-out")!, midiOut.Inputs[0]);
            var collected = new List<(long, MidiEvent)>();
            OfflineRenderer.Render(graph, null, 48000, new RenderOptions
            {
                BlockSize = 64,
                TailSeconds = 0,
                Midi = [(500, MidiEvent.NoteOn(0, 0, 60, 100)), (900, MidiEvent.NoteOff(0, 0, 60))],
                MidiOutput = (at, e) => collected.Add((at, e)),
            });
            Assert.That(collected, Is.EqualTo(new[] { (500L, MidiEvent.NoteOn(0, 0, 72, 100)), (900L, MidiEvent.NoteOff(0, 0, 72)) }));
        }

        [Test]
        public void GraphFilesKeepModulesCablesStatesAndMissingPlugins()
        {
            var fake = new FakePlugin("Saved", latency: 0) { State = [9, 8, 7] };
            string json;
            using (var graph = new PluginGraph())
            {
                var source = graph.Add(new TestSignalNode { Title = "Tone", X = 10, Y = 20, Generator = { Signal = TestSignal.Sweep, LevelDb = -3 } });
                var plugin = graph.Add(new PluginNode(fake, new PluginReference(PluginFormat.Vst3, "C:/Fake.vst3", "ABC", "Saved", "Tests")) { Bypass = true });
                var sum = graph.Add(new AudioSumNode { InputCount = 3 });
                sum.SetGainDb(2, -12);
                var output = graph.Add(new AudioOutputNode { FirstChannel = 2 });
                graph.Connect(source.Outputs[0], plugin.Inputs[0]);
                graph.Connect(plugin.Outputs[0], sum.Inputs[0]);
                graph.Connect(sum.Outputs[0], output.Inputs[0]);
                graph.Connect(sum.Outputs[0], sum.Inputs[2]);
                graph.LoopBlockSize = 32;
                json = graph.ToJson();
            }

            var restored = new FakePlugin("Saved");
            PluginReference? asked = null;
            using (var graph = new PluginGraph())
            {
                var warnings = graph.LoadJson(json, r =>
                {
                    asked = r;
                    return restored;
                });
                Assert.Multiple(() =>
                {
                    Assert.That(warnings, Is.Empty);
                    Assert.That(asked, Is.EqualTo(new PluginReference(PluginFormat.Vst3, "C:/Fake.vst3", "ABC", "Saved", "Tests")));
                    Assert.That(restored.State, Is.EqualTo(new byte[] { 9, 8, 7 }), "the plug-in state is restored");
                    Assert.That(graph.Nodes.Select(n => n.TypeKey), Is.EqualTo(new[] { "test-signal", "plugin", "audio-sum", "audio-output" }));
                    var tone = (TestSignalNode)graph.Nodes[0];
                    Assert.That((tone.Title, tone.X, tone.Y, tone.Generator.Signal, tone.Generator.LevelDb), Is.EqualTo(("Tone", 10f, 20f, TestSignal.Sweep, -3.0)));
                    Assert.That(((PluginNode)graph.Nodes[1]).Bypass, Is.True);
                    Assert.That(((AudioSumNode)graph.Nodes[2]).InputCount, Is.EqualTo(3));
                    Assert.That(((AudioSumNode)graph.Nodes[2]).GetGainDb(2), Is.EqualTo(-12));
                    Assert.That(((AudioOutputNode)graph.Nodes[3]).FirstChannel, Is.EqualTo(2));
                    Assert.That(graph.Connections.Select(c => c.ToString()), Is.EqualTo(new[]
                    {
                        "Tone.out → Saved.in0", "Saved.out0 → Audio sum.in1", "Audio sum.out → Audio output 3-4.in", "Audio sum.out → Audio sum.in3",
                    }));
                    Assert.That(graph.Connections[3].IsFeedback, Is.True);
                    Assert.That(graph.LoopBlockSize, Is.EqualTo(32));
                });
            }

            using (var graph = new PluginGraph())
            {
                var warnings = graph.LoadJson(json, _ => throw new FileNotFoundException("not installed"));
                var missing = graph.Nodes.OfType<MissingPluginNode>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(warnings, Has.Count.EqualTo(1));
                    Assert.That(warnings[0], Does.Contain("Saved").And.Contain("not installed"));
                    Assert.That(missing.DisplayTitle, Is.EqualTo("Saved"));
                    Assert.That(missing.Inputs.Select(p => p.Key), Is.EqualTo(new[] { "in0", "midi-in" }));
                    Assert.That(graph.Connections, Has.Count.EqualTo(4), "its cables are kept");
                });
                string again = graph.ToJson();
                var reloaded = new FakePlugin("Saved");
                using var third = new PluginGraph();
                Assert.That(third.LoadJson(again, _ => reloaded), Is.Empty, "saved back unchanged");
                Assert.That(reloaded.State, Is.EqualTo(new byte[] { 9, 8, 7 }));
            }

            // A plug-in that loads but refuses its saved state stays, with its own settings.
            var refusing = new FakePlugin("Saved") { RefusesState = true };
            using (var graph = new PluginGraph())
            {
                var warnings = graph.LoadJson(json, _ => refusing);
                Assert.Multiple(() =>
                {
                    Assert.That(graph.Nodes.OfType<PluginNode>().Single().Plugin, Is.SameAs(refusing));
                    Assert.That(graph.Nodes.OfType<MissingPluginNode>(), Is.Empty);
                    Assert.That(warnings, Has.Count.EqualTo(1));
                    Assert.That(warnings[0], Does.Contain("Saved").And.Contain("state refused").And.Contain("default settings"));
                    Assert.That(graph.Connections, Has.Count.EqualTo(4));
                });
            }

            using (var graph = new PluginGraph())
            {
                Assert.Throws<FormatException>(() => graph.LoadJson("{\"format\":\"other\"}", _ => restored));
                Assert.Throws<FormatException>(() => graph.LoadJson("not json", _ => restored));
            }
        }
    }
}
