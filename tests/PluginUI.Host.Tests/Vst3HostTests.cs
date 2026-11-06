// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.DemoSynth.Plugin;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Tests;

/// <summary>NPlug plug-ins hosted through their real VST 3 interfaces, in-process (as a DAW would host them).</summary>
public class Vst3HostTests
{
    private static Vst3Module GainModule() => Vst3Module.FromFactory(TestGainPlugin.GetFactory().Export(), "Host Tests");

    private static Vst3Module SynthModule() => Vst3Module.FromFactory(DemoSynthPlugin.GetFactory().Export(), "Host Tests");

    [Test]
    public void FactoryListsTheClasses()
    {
        using var module = GainModule();
        Assert.Multiple(() =>
        {
            Assert.That(module.Vendor, Is.EqualTo("PluginUI Tests"));
            Assert.That(module.Plugins.Select(c => c.Name), Is.EqualTo(new[] { "Test Gain" }));
            var info = module.Plugins.Single();
            Assert.That(info.SubCategories, Does.Contain("Fx"));
            Assert.That(info.Version, Does.StartWith("1.2.3"));
            Assert.That(Vst3Identifiers.FromFuidString(info.ClassIdString), Is.EqualTo(info.ClassId));
            Assert.That(module.Classes.Count(c => !c.IsAudioEffect), Is.EqualTo(1), "the controller class");
        });
    }

    [Test]
    public void EffectExposesBusesParametersAndLatency()
    {
        using var module = GainModule();
        using var plugin = module.CreatePlugin();
        Assert.Multiple(() =>
        {
            Assert.That(plugin.IsInstrument, Is.False);
            Assert.That(plugin.InputBuses.Select(b => b.ChannelCount), Is.EqualTo(new[] { 2 }));
            Assert.That(plugin.OutputBuses.Select(b => b.ChannelCount), Is.EqualTo(new[] { 2 }));
            Assert.That(plugin.LatencySamples, Is.EqualTo(32));
            Assert.That(plugin.FindParameter("gain"), Is.Not.Null);
            Assert.That(plugin.FindParameter("Mode")!.StepCount, Is.EqualTo(2));
        });
        var gain = plugin.FindParameter("Gain")!;
        Assert.That(plugin.FormatParameter(gain.Id, 0.5), Does.Contain("0").And.EndsWith("dB"));
        Assert.That(plugin.TryParseParameter(gain.Id, "-12 dB", out double n), Is.True);
        Assert.That(n, Is.EqualTo(0.25).Within(1e-6));
        var mode = plugin.FindParameter("Mode")!;
        Assert.That(plugin.TryParseParameter(mode.Id, "Hot", out double hot), Is.True);
        Assert.That(hot, Is.EqualTo(1.0));
        Assert.That(plugin.FormatParameter(mode.Id, 0.5), Is.EqualTo("Warm"));
    }

    [Test]
    public void ProcessingAppliesParameterChanges()
    {
        using var module = GainModule();
        using var plugin = module.CreatePlugin();
        plugin.Prepare(48000, 256);
        var gain = plugin.FindParameter("Gain")!;
        plugin.TryParseParameter(gain.Id, "-6.0206", out double minus6);
        plugin.SetParameter(gain.Id, minus6);
        for (int c = 0; c < 2; c++) plugin.GetInput(c).Fill(0.8f);
        plugin.Process(256);
        Assert.Multiple(() =>
        {
            Assert.That(plugin.GetOutput(0)[100], Is.EqualTo(0.4f).Within(1e-4));
            Assert.That(plugin.GetOutput(1)[255], Is.EqualTo(0.4f).Within(1e-4));
            Assert.That(plugin.GetParameter(gain.Id), Is.EqualTo(minus6).Within(1e-9), "the controller has the value too");
            Assert.That(plugin.SamplePosition, Is.EqualTo(256));
        });
    }

    [Test]
    public void StateRoundTripsThroughAVstPreset()
    {
        using var module = GainModule();
        byte[] preset;
        uint gainId, modeId;
        using (var plugin = module.CreatePlugin())
        {
            gainId = plugin.FindParameter("Gain")!.Id;
            modeId = plugin.FindParameter("Mode")!.Id;
            plugin.Prepare(48000, 64);
            plugin.SetParameter(gainId, 0.75);
            plugin.SetParameter(modeId, 1.0);
            plugin.Process(64);
            preset = plugin.GetState();
        }
        var content = VstPreset.Read(preset);
        Assert.That(content.ClassId, Is.EqualTo(module.Plugins.Single().ClassId));
        Assert.That(content.ComponentState, Is.Not.Empty);

        using var restored = module.CreatePlugin();
        restored.SetState(preset);
        Assert.Multiple(() =>
        {
            Assert.That(restored.GetParameter(gainId), Is.EqualTo(0.75).Within(1e-9));
            Assert.That(restored.GetParameter(modeId), Is.EqualTo(1.0).Within(1e-9));
        });
    }

    [Test]
    public void InstrumentPlaysNotesAndMapsMidiControllers()
    {
        using var module = SynthModule();
        using var synth = module.CreatePlugin();
        Assert.Multiple(() =>
        {
            Assert.That(synth.IsInstrument, Is.True);
            Assert.That(synth.EventInputCount, Is.EqualTo(1));
            Assert.That(synth.InputBuses.Single().IsAuxiliary, Is.True, "side chain");
            Assert.That(synth.OutputChannelCount, Is.EqualTo(2));
        });
        synth.Prepare(48000, 512);
        synth.Process(512);
        Assert.That(Peak(synth), Is.EqualTo(0f), "silent before any note");

        synth.AddMidiEvent(MidiEvent.NoteOn(0, 0, 60, 100));
        synth.AddMidiEvent(MidiEvent.PitchBend(10, 0, 8191));
        float peak = 0f;
        for (int i = 0; i < 8; i++)
        {
            synth.Process(512);
            peak = Math.Max(peak, Peak(synth));
        }
        Assert.That(peak, Is.GreaterThan(0.01f), "the note sounds");

        synth.Idle();
        var bend = synth.FindParameter("Pitch Bend")!;
        Assert.That(synth.GetParameter(bend.Id), Is.EqualTo(1.0).Within(1e-3), "pitch bend reached the parameter through IMidiMapping");
    }

    private static float Peak(PluginInstance plugin)
    {
        float peak = 0f;
        for (int c = 0; c < plugin.OutputChannelCount; c++)
        {
            foreach (float v in plugin.GetOutput(c)) peak = Math.Max(peak, Math.Abs(v));
        }
        return peak;
    }
}
