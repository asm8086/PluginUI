// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Host.Audio;
using PluginUI.Host.Tests.Support;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Tests.Support
{
    /// <summary>Native channel buffers for <see cref="AudioBuffers"/> in tests.</summary>
    internal sealed unsafe class TestBuffers : IDisposable
    {
        private readonly float* _memory;
        private readonly float** _pointers;

        public TestBuffers(int channels, int frames)
        {
            Channels = channels;
            Frames = frames;
            _memory = (float*)NativeMemory.AllocZeroed((nuint)Math.Max(1, channels * frames), sizeof(float));
            _pointers = (float**)NativeMemory.Alloc((nuint)Math.Max(1, channels), (nuint)sizeof(float*));
            for (int c = 0; c < channels; c++) _pointers[c] = _memory + c * frames;
        }

        public int Channels { get; }

        public int Frames { get; }

        public AudioBuffers Buffers => new(_pointers, Channels, Frames);

        public float[] Channel(int c) => new Span<float>(_pointers[c], Frames).ToArray();

        public void Dispose()
        {
            NativeMemory.Free(_pointers);
            NativeMemory.Free(_memory);
        }
    }
}

namespace PluginUI.Host.Tests
{
    public class LiveAudioTests
    {
        private static Vst3Module GainModule() => Vst3Module.FromFactory(TestGainPlugin.GetFactory().Export(), "Host Tests");

        private static double Rms(float[] x) => Math.Sqrt(x.Sum(v => (double)v * v) / x.Length);

        [TestCase(TestSignal.Sine, 0.7071)]
        [TestCase(TestSignal.Square, 1.0)]
        [TestCase(TestSignal.WhiteNoise, 1.0)]
        [TestCase(TestSignal.PinkNoise, 1.0)]
        public void TestSignalsHaveTheirLevel(TestSignal signal, double rmsAtFullScale)
        {
            var generator = new TestSignalGenerator { Signal = signal, Frequency = 1000, LevelDb = -12 };
            generator.Prepare(48000);
            using var out2 = new TestBuffers(2, 48000);
            using var none = new TestBuffers(0, 48000);
            generator.Read(out2.Buffers, none.Buffers);
            double expected = rmsAtFullScale * Math.Pow(10, -12 / 20.0);
            Assert.Multiple(() =>
            {
                Assert.That(Rms(out2.Channel(0)), Is.EqualTo(expected).Within(expected * 0.06));
                Assert.That(out2.Channel(1), Is.EqualTo(out2.Channel(0)), "the same on every channel");
            });
        }

        [Test]
        public void ImpulsesAndSweepsRepeat()
        {
            var impulses = new TestSignalGenerator { Signal = TestSignal.Impulses, Period = 0.01, LevelDb = 0 };
            impulses.Prepare(48000);
            using var b = new TestBuffers(1, 2000);
            using var none = new TestBuffers(0, 2000);
            impulses.Read(b.Buffers, none.Buffers);
            var hits = b.Channel(0).Select((v, i) => (v, i)).Where(x => x.v != 0f).Select(x => x.i).ToArray();
            Assert.That(hits, Is.EqualTo(new[] { 0, 480, 960, 1440, 1920 }));
        }

        [Test]
        public void FileSourceLoopsAndResamples()
        {
            var audio = AudioBuffer.Create(24000, 1, 100);
            for (int i = 0; i < 100; i++) audio.Channels[0][i] = i;
            var source = new FileSource(audio, "ramp.wav");
            source.Prepare(48000);
            using var b = new TestBuffers(2, 250);
            using var none = new TestBuffers(0, 250);
            source.Read(b.Buffers, none.Buffers);
            var x = b.Channel(0);
            Assert.Multiple(() =>
            {
                Assert.That(x[0], Is.EqualTo(0f));
                Assert.That(x[1], Is.EqualTo(0.5f), "twice the rate: interpolated halfway");
                Assert.That(x[10], Is.EqualTo(5f));
                Assert.That(x[200], Is.EqualTo(0f), "looped after 100 source samples = 200 device samples");
                Assert.That(b.Channel(1), Is.EqualTo(x), "a mono file feeds both channels");
                Assert.That(source.PositionSeconds, Is.EqualTo(25 / 24000.0).Within(1e-9));
            });
        }

        [Test]
        public void RackRunsAPluginFromATestSignalWithItsLatency()
        {
            using var module = GainModule();
            using var plugin = module.CreatePlugin();
            var rack = new PluginRack();
            rack.Prepare(48000, 64, 0, 2);
            var generator = new TestSignalGenerator { Signal = TestSignal.Impulses, Period = 1, LevelDb = 0 };
            var slot = rack.Add(plugin, generator);
            plugin.TryParseParameter(plugin.FindParameter("Gain")!.Id, "-6", out double minus6);
            plugin.SetParameter(plugin.FindParameter("Gain")!.Id, minus6);
            using var none = new TestBuffers(0, 64);
            var output = new List<float>();
            using (var block = new TestBuffers(2, 64))
            {
                for (int n = 0; n < 4; n++)
                {
                    rack.Render(none.Buffers, block.Buffers);
                    output.AddRange(block.Channel(0));
                }
            }
            var peaks = new float[2];
            slot.Meter.Read(peaks);
            var device = new float[2];
            rack.OutputMeter.Read(device);
            Assert.Multiple(() =>
            {
                Assert.That(output.IndexOf(output.Max()), Is.EqualTo(32), "the impulse comes out after the plug-in's latency");
                Assert.That(output.Max(), Is.EqualTo(0.501f).Within(0.001f));
                Assert.That(peaks[0], Is.EqualTo(0.501f).Within(0.001f));
                Assert.That(device[1], Is.EqualTo(0.501f).Within(0.001f));
                slot.Meter.Read(peaks);
                Assert.That(peaks[0], Is.Zero, "reading starts again from zero");
            });

            slot.Bypass = true;
            using (var block = new TestBuffers(2, 64))
            {
                generator.Prepare(48000);
                rack.Render(none.Buffers, block.Buffers);
                Assert.That(block.Channel(0)[0], Is.EqualTo(1f), "bypassed: the source goes straight out");
            }
            rack.Remove(slot);
            using (var block = new TestBuffers(2, 64))
            {
                rack.Render(none.Buffers, block.Buffers);
                Assert.That(block.Channel(0).Max(), Is.Zero);
            }
        }

        [Test]
        public void RackDeliversMidiToInstruments()
        {
            using var module = Vst3Module.FromFactory(PluginUI.DemoSynth.Plugin.DemoSynthPlugin.GetFactory().Export(), "Host Tests");
            using var synth = module.CreatePlugin();
            var rack = new PluginRack();
            rack.Prepare(48000, 256, 0, 2);
            var slot = rack.Add(synth);
            using var none = new TestBuffers(0, 256);
            using var block = new TestBuffers(2, 256);
            rack.Render(none.Buffers, block.Buffers);
            Assert.That(block.Channel(0).Max(), Is.Zero, "no note, no sound");
            slot.SendMidi(MidiEvent.NoteOn(0, 0, 60, 100));
            double energy = 0;
            for (int n = 0; n < 20; n++)
            {
                rack.Render(none.Buffers, block.Buffers);
                energy += Rms(block.Channel(0));
            }
            Assert.That(energy, Is.GreaterThan(0.01), "the note sounds");
        }

        [Test]
        public void NullDriverRunsInRealTime()
        {
            var renderer = new CountingRenderer();
            using var driver = new NullAudioDriver(48000, 480);
            driver.Start(renderer);
            Thread.Sleep(300);
            driver.Stop();
            Assert.Multiple(() =>
            {
                Assert.That(renderer.Prepared, Is.EqualTo((48000.0, 480, 0, 2)));
                Assert.That(renderer.Blocks, Is.InRange(15, 45), "about one block every 10 ms");
            });
        }

        private sealed class CountingRenderer : IAudioRenderer
        {
            public (double, int, int, int) Prepared { get; private set; }

            public int Blocks;

            public void Prepare(double sampleRate, int maxFrames, int inputChannels, int outputChannels) => Prepared = (sampleRate, maxFrames, inputChannels, outputChannels);

            public void Render(in AudioBuffers inputs, in AudioBuffers outputs) => Interlocked.Increment(ref Blocks);
        }

        [Test]
        public void MidiQueueKeepsOrderAndRefusesWhenFull()
        {
            var q = new MidiEventQueue(4);
            for (int i = 0; i < 4; i++) Assert.That(q.Enqueue(MidiEvent.NoteOn(0, 0, 60 + i, 100)), Is.True);
            Assert.That(q.Enqueue(MidiEvent.NoteOn(0, 0, 70, 100)), Is.False);
            var notes = new List<int>();
            while (q.TryDequeue(out var e)) notes.Add(e.Data1);
            Assert.That(notes, Is.EqualTo(new[] { 60, 61, 62, 63 }));
        }
    }
}
