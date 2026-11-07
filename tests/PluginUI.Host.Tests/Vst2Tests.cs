// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text;
using PluginUI.Host.Catalog;
using PluginUI.Host.Vst2;

namespace PluginUI.Host.Tests;

[NonParallelizable]
public unsafe class Vst2Tests
{
    [SetUp]
    public void Reset() => FakeVst2.ShellMode = false;

    private static Vst2Module Module() => Vst2Module.FromEntryPoint(FakeVst2.EntryPoint, Path.Combine(Path.GetTempPath(), "Fake Gain.dll"));

    [Test]
    public void NativeLayoutsMatchTheInterface()
    {
        if (!Environment.Is64BitProcess) Assert.Ignore("64-bit layout.");
        Assert.That(sizeof(AEffect), Is.EqualTo(192));
        AEffect effect = default;
        byte* start = (byte*)&effect;
        Assert.That((byte*)&effect.ProcessReplacing - start, Is.EqualTo(120));
        Assert.That((byte*)&effect.UniqueId - start, Is.EqualTo(112));
        Assert.That((byte*)&effect.NumPrograms - start, Is.EqualTo(40));
        Assert.That((byte*)&effect.InitialDelay - start, Is.EqualTo(80));
        Assert.That(sizeof(VstMidiEvent), Is.EqualTo(32));
        Assert.That(sizeof(VstEvents), Is.EqualTo(16));
        Assert.That(sizeof(VstTimeInfo), Is.EqualTo(88));
        Assert.That(sizeof(VstParameterProperties), Is.EqualTo(152));
    }

    [Test]
    public void PluginIsDescribedAndProcesses()
    {
        using var module = Module();
        using var plugin = module.CreatePlugin(hostName: "Host Tests");
        var st = FakeVst2.Last!;
        Assert.That(st.HostVersion, Is.EqualTo(2400));
        Assert.That(st.Opens, Is.EqualTo(1));
        Assert.That(plugin.Name, Is.EqualTo("Fake Gain"));
        Assert.That(plugin.Vendor, Is.EqualTo("Test Vendor"));
        Assert.That(plugin.Format, Is.EqualTo(PluginFormat.Vst2));
        Assert.That(plugin.UniqueId, Is.EqualTo(FakeVst2.GainId));
        Assert.That(FxPreset.FourCc(plugin.UniqueId), Is.EqualTo("FkGn"));
        Assert.That(plugin.IsInstrument, Is.False);
        Assert.That(plugin.InputBuses.Single().ChannelCount, Is.EqualTo(2));
        Assert.That(plugin.OutputBuses.Single().ChannelCount, Is.EqualTo(2));
        Assert.That(plugin.EventInputCount, Is.EqualTo(1));
        Assert.That(plugin.EventOutputCount, Is.EqualTo(1));
        Assert.That(plugin.LatencySamples, Is.EqualTo(32));
        Assert.That(plugin.TailSamples, Is.EqualTo(4800));
        Assert.That(plugin.HasEditor, Is.True);
        Assert.That(plugin.PresetExtension, Is.EqualTo(".fxb"));
        Assert.That(plugin.Parameters.Select(p => p.Title), Is.EqualTo(new[] { "Gain", "Mode" }));
        Assert.That(plugin.Parameters[0].Units, Is.EqualTo("dB"));
        Assert.That(plugin.Parameters[1].StepCount, Is.EqualTo(1));
        Assert.That(plugin.Parameters[1].ShortTitle, Is.EqualTo("Mod"));
        Assert.That(plugin.ProgramCount, Is.EqualTo(3));
        Assert.That(plugin.GetProgramName(2), Is.EqualTo("Bright"));

        // Typed values and display.
        Assert.That(plugin.TryParseParameter(0, "-6 dB", out double half), Is.True);
        Assert.That(half, Is.EqualTo(0.25).Within(1e-3));
        Assert.That(plugin.GetParameter(0), Is.EqualTo(0.5).Within(1e-6), "parsing does not change the value");
        plugin.SetParameter(0, 0.25);
        Assert.That(plugin.FormatParameter(0, plugin.GetParameter(0)), Is.EqualTo("-6.0 dB"));
        Assert.That(plugin.FormatParameter(1, 1.0), Is.EqualTo("On"), "another value, while not processing");
        Assert.That(plugin.GetParameter(1), Is.Zero);

        plugin.Prepare(48000, 64);
        Assert.That(st.Rate, Is.EqualTo(48000));
        Assert.That(st.Active, Is.True);
        Assert.That(st.StartProcess, Is.EqualTo(1));
        plugin.GetInput(0).Fill(0.8f);
        plugin.GetInput(1).Fill(-0.4f);
        plugin.Process(64);
        Assert.That(plugin.GetOutput(0)[10], Is.EqualTo(0.4f).Within(1e-4));
        Assert.That(plugin.GetOutput(1)[63], Is.EqualTo(-0.2f).Within(1e-4));
        plugin.Stop();
        Assert.That(st.Active, Is.False);
    }

    [Test]
    public void MidiGoesBothWaysAndTimeIsGiven()
    {
        using var module = Module();
        using var plugin = module.CreatePlugin();
        var st = FakeVst2.Last!;
        plugin.Tempo = 90;
        plugin.Prepare(48000, 128, realtime: true);
        plugin.Process(128);
        plugin.AddMidiEvent(MidiEvent.NoteOn(17, 2, 60, 100));
        plugin.Process(128);
        Assert.That(st.Tempo, Is.EqualTo(90));
        Assert.That(st.Ppq, Is.EqualTo(128 / 48000.0 * 90 / 60).Within(1e-9), "second block");
        Assert.That(st.TimeFlags & VstTimeInfo.TransportPlaying, Is.Not.Zero);
        Assert.That(plugin.OutputMidi.ToArray(), Is.EqualTo(new[] { new MidiEvent(17, 0x92, 72, 100) }));
        plugin.Process(128);
        Assert.That(plugin.OutputMidi.Length, Is.Zero, "output MIDI belongs to its block");
    }

    [Test]
    public void OfflineRendersKeepTheMidiOfTheFirstSamples()
    {
        // The plug-in has 32 samples of latency: its echo of a note at 0 lands before 0 once compensated.
        using var module = Module();
        using var plugin = module.CreatePlugin();
        var output = new List<(long At, MidiEvent Event)>();
        var options = new PluginUI.Host.Audio.RenderOptions
        {
            Midi = [(0L, MidiEvent.NoteOn(0, 0, 60, 100)), (4800L, MidiEvent.NoteOff(0, 0, 60, 0))],
            MidiOutput = (at, e) => output.Add((at, e)),
        };
        var input = new PluginUI.Host.Audio.AudioBuffer(48000, [new float[9600], new float[9600]]);
        PluginUI.Host.Audio.OfflineRenderer.Render([plugin], input, 48000, options);
        Assert.That(output.Select(o => (o.At, o.Event.Status, o.Event.Data1)), Is.EqualTo(new[] { (0L, (byte)0x90, (byte)72), (4800L - 32, (byte)0x80, (byte)72) }));
    }

    [Test]
    public void StateRoundTripsThroughABankChunk()
    {
        using var module = Module();
        using var plugin = module.CreatePlugin();
        plugin.SetParameter(0, 0.75);
        plugin.SetParameter(1, 1);
        plugin.CurrentProgram = 2;
        byte[] state = plugin.GetState();
        Assert.That(Encoding.ASCII.GetString(state, 0, 4), Is.EqualTo("CcnK"));
        Assert.That(Encoding.ASCII.GetString(state, 8, 4), Is.EqualTo("FBCh"));
        var preset = FxPreset.Read(state);
        Assert.That(preset.Kind, Is.EqualTo(FxPresetKind.BankChunk));
        Assert.That(preset.PluginId, Is.EqualTo(FakeVst2.GainId));
        Assert.That(preset.CurrentProgram, Is.EqualTo(2));

        plugin.SetParameter(0, 0.1);
        plugin.SetParameter(1, 0);
        plugin.CurrentProgram = 0;
        plugin.SetState(state);
        Assert.That(plugin.GetParameter(0), Is.EqualTo(0.75).Within(1e-6));
        Assert.That(plugin.GetParameter(1), Is.EqualTo(1.0));
        Assert.That(plugin.CurrentProgram, Is.EqualTo(2));

        var other = new FxPreset { Kind = FxPresetKind.ProgramParameters, PluginId = 1234, Programs = [new FxProgram("x", [0.5f, 0f])] };
        Assert.Throws<InvalidDataException>(() => plugin.SetState(other.Write()));

        // A program as parameter values (.fxp) loads too.
        var fxp = new FxPreset { Kind = FxPresetKind.ProgramParameters, PluginId = FakeVst2.GainId, Programs = [new FxProgram("Mine", [0.3f, 0f])] };
        plugin.SetState(fxp.Write());
        Assert.That(plugin.GetParameter(0), Is.EqualTo(0.3).Within(1e-6));
        Assert.That(plugin.GetProgramName(plugin.CurrentProgram), Is.EqualTo("Mine"));
    }

    [Test]
    public void PresetFilesRoundTrip()
    {
        var bank = new FxPreset
        {
            Kind = FxPresetKind.BankParameters, PluginId = 0x41424344, PluginVersion = 3, CurrentProgram = 1,
            Programs = [new FxProgram("One", [0.1f, 0.2f]), new FxProgram("Two", [0.9f, 1f])],
        };
        byte[] bytes = bank.Write();
        Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("FxBk"));
        Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4)), Is.EqualTo(bytes.Length - 8));
        var back = FxPreset.Read(bytes);
        Assert.That(back.Kind, Is.EqualTo(FxPresetKind.BankParameters));
        Assert.That(back.PluginId, Is.EqualTo(0x41424344));
        Assert.That(back.CurrentProgram, Is.EqualTo(1));
        Assert.That(back.Programs.Select(p => p.Name), Is.EqualTo(new[] { "One", "Two" }));
        Assert.That(back.Programs[1].Parameters, Is.EqualTo(new[] { 0.9f, 1f }));

        var chunk = new FxPreset { Kind = FxPresetKind.ProgramChunk, PluginId = 7, ProgramName = "Lead", Chunk = [1, 2, 3] };
        var chunkBack = FxPreset.Read(chunk.Write());
        Assert.That(chunkBack.ProgramName, Is.EqualTo("Lead"));
        Assert.That(chunkBack.Chunk, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(FxPreset.FourCc(0x41424344), Is.EqualTo("ABCD"));

        Assert.Throws<InvalidDataException>(() => FxPreset.Read("RIFF0000"u8));
        Assert.Throws<InvalidDataException>(() => FxPreset.Read(bytes.AsSpan(0, 40)));
    }

    [Test]
    public void EditorOpensIdlesAndAsksForASize()
    {
        using var module = Module();
        using var plugin = module.CreatePlugin();
        var st = FakeVst2.Last!;
        using (var editor = plugin.CreateEditor()!)
        {
            Assert.That(editor.Size, Is.EqualTo((300, 200)));
            Assert.That(editor.CanResize, Is.False);
            Assert.That(editor.Attach(0x1234), Is.True);
            Assert.That(st.Parent, Is.EqualTo((nint)0x1234));
            (int, int)? asked = null;
            editor.ResizeRequested = (w, h) => { asked = (w, h); return true; };
            st.WantResize = true;
            plugin.Idle();
            Assert.That(asked, Is.EqualTo((400, 250)));
            Assert.That(st.ResizeAnswer, Is.EqualTo(1));
        }
        Assert.That(st.EditCloses, Is.EqualTo(1));
    }

    [Test]
    public void ShellModulesListTheirPlugins()
    {
        FakeVst2.ShellMode = true;
        using var module = Module();
        Assert.That(module.ShellPlugins, Is.EqualTo(new[] { new Vst2ShellPlugin(FakeVst2.ShellAId, "Shell A"), new Vst2ShellPlugin(FakeVst2.ShellBId, "Shell B") }));
        using (var b = module.CreatePlugin(FakeVst2.ShellBId))
        {
            Assert.That(b.Name, Is.EqualTo("Shell B"));
            Assert.That(b.UniqueId, Is.EqualTo(FakeVst2.ShellBId));
            Assert.That(b.ShellId, Is.EqualTo(FakeVst2.ShellBId));
        }
        var described = Vst2Hosting.Describe(module);
        Assert.That(described.Select(d => d.Name), Is.EqualTo(new[] { "Shell A", "Shell B" }));
        Assert.That(described[1].ClassId, Is.EqualTo(FakeVst2.ShellBId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(described.All(d => d.Format == PluginFormat.Vst2 && d.Status == PluginStatus.Ok && d.InputChannels == 2 && d.HasMidiInput), Is.True);
        Assert.That(described[0].SdkVersion, Is.EqualTo("VST 2.4"));
    }

    [Test]
    public void ModulesStayLoadedWhileAPluginLives()
    {
        var module = Module();
        var plugin = module.CreatePlugin();
        module.Dispose();
        Assert.That(plugin.Name, Is.EqualTo("Fake Gain"), "the plug-in keeps its module");
        plugin.Dispose();
        Assert.Throws<ObjectDisposedException>(() => module.CreatePlugin());
    }

    [Test]
    public void LibrariesWithoutAnEntryPointAreLeftOut()
    {
        // A managed DLL is a PE image without exports; a text file is not a PE image (the loader decides).
        string dll = typeof(Vst2Tests).Assembly.Location;
        Assert.That(PeExports.Read(dll), Is.Empty);
        Assert.That(PeExports.MayExport(dll, "VSTPluginMain", "main"), Is.False);
        string text = Path.Combine(Path.GetTempPath(), "pluginui-not-a-dll.txt");
        File.WriteAllText(text, "hello");
        try
        {
            Assert.That(PeExports.MayExport(text, "VSTPluginMain"), Is.True);
        }
        finally
        {
            File.Delete(text);
        }
    }
}
