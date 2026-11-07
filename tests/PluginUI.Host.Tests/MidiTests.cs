// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.DemoSynth.Plugin;
using PluginUI.Host.Audio;
using PluginUI.Host.Tests.Support;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Tests;

/// <summary>MIDI produced by plug-ins (VST3 output events), MIDI chains and MIDI files.</summary>
public class MidiTests
{
    private static Vst3Module TestModule() => Vst3Module.FromFactory(TestGainPlugin.GetFactory().Export(), "Host Tests");

    private static Vst3Module SynthModule() => Vst3Module.FromFactory(DemoSynthPlugin.GetFactory().Export(), "Host Tests");

    [Test]
    public void PluginMidiOutputIsCollected()
    {
        using var module = TestModule();
        using var echo = module.CreatePlugin("Test MIDI Echo");
        Assert.Multiple(() =>
        {
            Assert.That(echo.EventInputCount, Is.EqualTo(1));
            Assert.That(echo.EventOutputCount, Is.EqualTo(1));
        });
        echo.Prepare(48000, 64, realtime: true);
        echo.AddMidiEvent(MidiEvent.NoteOn(10, 2, 60, 100));
        echo.Process(64);
        Assert.That(echo.OutputMidi.ToArray(), Is.EqualTo(new[]
        {
            new MidiEvent(10, 0x92, 72, 100),
            new MidiEvent(10, 0xB2, 74, 60),
        }));
        echo.AddMidiEvent(MidiEvent.NoteOff(5, 2, 60));
        echo.Process(64);
        Assert.That(echo.OutputMidi.ToArray(), Is.EqualTo(new[] { new MidiEvent(5, 0x82, 72, 64) }));
        echo.Process(64);
        Assert.That(echo.OutputMidi.Length, Is.Zero, "cleared at each block");
    }

    [Test]
    public void OfflineChainsPassMidiAlongAndReportTheLastOutput()
    {
        using var tests = TestModule();
        using var synths = SynthModule();
        using var echo = tests.CreatePlugin("Test MIDI Echo");
        using var synth = synths.CreatePlugin();
        var notes = new List<(long Sample, MidiEvent Event)> { (1000, MidiEvent.NoteOn(0, 0, 48, 100)), (20000, MidiEvent.NoteOff(0, 0, 48)) };

        var output = OfflineRenderer.Render([echo, synth], null, 48000, new RenderOptions { Midi = notes, TailSeconds = 0.1 });
        Assert.That(output.Peak(), Is.GreaterThan(0.01f), "the synthesizer plays the notes the echo sent");

        var collected = new List<(long, MidiEvent)>();
        using var alone = tests.CreatePlugin("Test MIDI Echo");
        OfflineRenderer.Render([alone], null, 48000, new RenderOptions { Midi = notes, TailSeconds = 0, MidiOutput = (at, e) => collected.Add((at, e)) });
        Assert.That(collected, Is.EqualTo(new[]
        {
            (1000L, new MidiEvent(0, 0x90, 60, 100)),
            (1000L, new MidiEvent(0, 0xB0, 74, 48)),
            (20000L, new MidiEvent(0, 0x80, 60, 64)),
        }));
    }

    [Test]
    public void RackRoutesMidiFromOnePluginToAnother()
    {
        using var tests = TestModule();
        using var synths = SynthModule();
        using var echo = tests.CreatePlugin("Test MIDI Echo");
        using var synth = synths.CreatePlugin();
        var rack = new PluginRack();
        rack.Prepare(48000, 256, 0, 2);
        var source = rack.Add(echo);
        var target = rack.Add(synth);
        source.MidiOut = target;
        source.SendMidi(MidiEvent.NoteOn(0, 0, 60, 110));
        using var none = new TestBuffers(0, 256);
        using var block = new TestBuffers(2, 256);
        float peak = 0;
        for (int n = 0; n < 10; n++)
        {
            rack.Render(none.Buffers, block.Buffers);
            peak = Math.Max(peak, block.Channel(0).Max(Math.Abs));
        }
        Assert.That(peak, Is.GreaterThan(0.01f));
    }

    [Test]
    public void MidiFilesRoundTrip()
    {
        var events = new List<(long Sample, MidiEvent Event)>
        {
            (0, MidiEvent.NoteOn(0, 0, 60, 100)),
            (24000, MidiEvent.ControlChange(0, 1, 7, 90)),
            (24000, new MidiEvent(0, 0xC1, 5, 0)),
            (48000, MidiEvent.NoteOff(0, 0, 60, 0)),
            (48000, MidiEvent.PitchBend(0, 3, 12000)),
        };
        byte[] file = MidiFile.Write(events, 48000, tempo: 90, ticksPerQuarter: 480);
        var back = MidiFile.Read(file, 48000);
        Assert.That(back.Select(e => (e.Sample, e.Event)), Is.EqualTo(events), "positions survive the tick conversion at 90 BPM");
    }
}
