// 2026/11/08 18:01:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Host.Catalog;
using PluginUI.Host.Vst2;

namespace PluginUI.Host.Tests;

/// <summary>
/// The bridge protocol, both sides in this process: the fake VST 2 plug-in served on a thread through a channel without
/// a name, used through a <see cref="Vst2BridgedPlugin"/> as a 64-bit host uses a 32-bit plug-in.
/// </summary>
[NonParallelizable]
public sealed class Vst2BridgeTests
{
    private Vst2Module _module = null!;
    private Vst2Plugin _served = null!;
    private Thread _server = null!;
    private volatile bool _hostAlive;
    private const int ControlCapacity = 96;
    private Vst2BridgedPlugin _plugin = null!;

    [SetUp]
    public void SetUp()
    {
        FakeVst2.ShellMode = false;
        _module = Vst2Module.FromEntryPoint(FakeVst2.EntryPoint, Path.Combine(Path.GetTempPath(), "Fake Gain.dll"));
        _served = _module.CreatePlugin(hostName: "Bridge Tests");
        // A tiny control area: descriptions and states go in pieces, as large states do through the real one.
        // The bridged plug-in owns the channel and disposes of it.
        var channel = BridgeChannel.Create(null, ControlCapacity);
        _hostAlive = true;
        _server = new Thread(() => Vst2BridgeServer.Serve(channel, _served, () => _hostAlive)) { IsBackground = true };
        _server.Start();
        _plugin = new Vst2BridgedPlugin(channel, () => _server.IsAlive, null, ModuleArchitecture.X86);
    }

    [TearDown]
    public void TearDown()
    {
        _plugin.Dispose();
        _hostAlive = false;
        _server.Join(5000);
        _served.Dispose();
        _module.Dispose();
    }

    [Test]
    public void ABridgedPluginIsDescribedAsTheLoadedOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That((_plugin.Name, _plugin.Vendor, _plugin.Format), Is.EqualTo(("Fake Gain", "Test Vendor", PluginFormat.Vst2)));
            Assert.That(_plugin.UniqueId, Is.EqualTo(FakeVst2.GainId));
            Assert.That(_plugin.Architecture, Is.EqualTo(ModuleArchitecture.X86));
            Assert.That(_plugin.InputBuses.Single().ChannelCount, Is.EqualTo(2));
            Assert.That(_plugin.OutputBuses.Single().ChannelCount, Is.EqualTo(2));
            Assert.That((_plugin.EventInputCount, _plugin.EventOutputCount), Is.EqualTo((1, 1)));
            Assert.That((_plugin.LatencySamples, _plugin.TailSamples), Is.EqualTo((32, 4800)));
            Assert.That(_plugin.Parameters.Select(p => p.Title), Is.EqualTo(new[] { "Gain", "Mode" }));
            Assert.That(_plugin.Parameters[1].ShortTitle, Is.EqualTo("Mod"));
            Assert.That(_plugin.PresetExtension, Is.EqualTo(".fxb"));
            Assert.That(_plugin.HasEditor, Is.True);
        });
    }

    [Test]
    public void ParametersAreReadFromTheMirrorAndTextGoesThroughTheBridge()
    {
        Assert.That(_plugin.TryParseParameter(0, "-6 dB", out double half), Is.True);
        Assert.That(half, Is.EqualTo(0.25).Within(1e-3));
        _plugin.SetParameter(0, 0.25);
        Assert.Multiple(() =>
        {
            Assert.That(_plugin.GetParameter(0), Is.EqualTo(0.25).Within(1e-6));
            Assert.That(_served.GetParameter(0), Is.EqualTo(0.25).Within(1e-6), "applied in the bridge while not processing");
            Assert.That(_plugin.FormatParameter(0, 0.25), Is.EqualTo("-6.0 dB"));
            Assert.That(_plugin.TryParseParameter(1, "loud", out _), Is.False);
        });
    }

    [Test]
    public void BlocksCarryAudioParametersAndMidi()
    {
        _plugin.SetParameter(0, 0.25); // gain 0.5
        _plugin.Prepare(48000, 64);
        _plugin.GetInput(0).Fill(0.8f);
        _plugin.GetInput(1).Fill(-0.4f);
        _plugin.AddMidiEvent(MidiEvent.NoteOn(10, 0, 60, 100));
        _plugin.Process(64);
        Assert.Multiple(() =>
        {
            Assert.That(_plugin.GetOutput(0)[..64].ToArray(), Is.All.EqualTo(0.4f).Within(1e-6f));
            Assert.That(_plugin.GetOutput(1)[..64].ToArray(), Is.All.EqualTo(-0.2f).Within(1e-6f));
            Assert.That(_plugin.OutputMidi.ToArray(), Is.EqualTo(new[] { MidiEvent.NoteOn(10, 0, 72, 100) }), "the echo an octave up comes back");
        });

        _plugin.SetParameter(0, 0.5); // gain 1, with the next block
        _plugin.Process(32);
        Assert.That(_plugin.GetOutput(0)[..32].ToArray(), Is.All.EqualTo(0.8f).Within(1e-6f));
        Assert.That(_served.GetParameter(0), Is.EqualTo(0.5).Within(1e-6));
    }

    [Test]
    public void BlocksLargerThanTheSharedBuffersAreCutInPieces()
    {
        int frames = BridgeChannel.MaxFrames + 1000;
        _plugin.SetParameter(0, 0.25);
        _plugin.Prepare(48000, frames);
        var input = _plugin.GetInput(0);
        for (int i = 0; i < frames; i++) input[i] = i / (float)frames;
        _plugin.AddMidiEvent(MidiEvent.NoteOn(BridgeChannel.MaxFrames + 500, 0, 60, 100));
        _plugin.Process(frames);
        var output = _plugin.GetOutput(0).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(output[frames - 1], Is.EqualTo(0.5f * (frames - 1) / frames).Within(1e-6f));
            Assert.That(output[BridgeChannel.MaxFrames], Is.EqualTo(0.5f * BridgeChannel.MaxFrames / frames).Within(1e-6f));
            Assert.That(_plugin.OutputMidi.ToArray().Single().SampleOffset, Is.EqualTo(BridgeChannel.MaxFrames + 500));
        });
    }

    [Test]
    public void TheEditorOpensInTheBridgeAndItsSizeRequestsReachTheHost()
    {
        var st = FakeVst2.Last!;
        Assert.That(_plugin.HasEditor, Is.True);
        using (var editor = _plugin.CreateEditor()!)
        {
            Assert.Multiple(() =>
            {
                Assert.That(editor.Size, Is.EqualTo((300, 200)));
                Assert.That(editor.CanResize, Is.False);
                Assert.That(editor.IsPlatformSupported("HWND"), Is.True);
                Assert.That(editor.Attach(0x1234), Is.True);
                Assert.That(st.Parent, Is.EqualTo((nint)0x1234));
            });
            (int, int)? asked = null;
            editor.ResizeRequested = (w, h) => { asked = (w, h); return true; };
            st.WantResize = true;
            // The bridge gives the editor its idle time by itself; the host learns the size at an idle call.
            for (int i = 0; i < 50 && asked is null; i++)
            {
                _plugin.Idle();
                if (asked is null) Thread.Sleep(20);
            }
            Assert.Multiple(() =>
            {
                Assert.That(asked, Is.EqualTo((400, 250)));
                Assert.That(editor.Size, Is.EqualTo((400, 250)));
                Assert.That(st.ResizeAnswer, Is.EqualTo(1));
            });
        }
        Assert.That(st.EditCloses, Is.EqualTo(1));
        // The plug-in still answers after its editor closed.
        Assert.That(_plugin.FormatParameter(0, 0.5), Is.EqualTo("0.0 dB"));
    }

    [Test]
    public void StatesGoBothWaysAndThePluginsChangesReachTheHost()
    {
        _plugin.SetParameter(0, 0.25);
        byte[] state = _plugin.GetState();
        Assert.That(state.Length, Is.GreaterThan(ControlCapacity), "sent back in pieces");
        _plugin.SetParameter(0, 0.9);
        var changes = new List<(uint, double)>();
        _plugin.ParameterChanged += (_, id, value) => changes.Add((id, value));
        _plugin.SetState(state);
        Assert.Multiple(() =>
        {
            Assert.That(_served.GetParameter(0), Is.EqualTo(0.25).Within(1e-6), "the state was sent in pieces");
            Assert.That(_plugin.GetParameter(0), Is.EqualTo(0.25).Within(1e-6), "the mirror follows");
        });
        Assert.That(changes, Does.Contain((0u, 0.25)), "the values the state set are reported, as by the plug-in in this process");
        changes.Clear();
        _plugin.Idle();
        Assert.That(changes, Is.Empty, "once");
    }

    [Test]
    public void ABridgeThatStopsFailsTheNextBlockWithoutHangingTheHost()
    {
        var saved = Vst2BridgedPlugin.BlockTimeout;
        Vst2BridgedPlugin.BlockTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            _plugin.Prepare(48000, 64);
            _plugin.Process(64);
            _hostAlive = false; // the bridge gives up, as when the host stops
            Assert.That(_server.Join(5000), Is.True);
            Assert.Throws<Vst2BridgeException>(() => _plugin.Process(64));
            Assert.That(_plugin.HasFailed, Is.True);
            _plugin.Idle(); // quiet once failed
            Assert.That(_plugin.FormatParameter(0, 0.5), Is.EqualTo("0.5"));
        }
        finally
        {
            Vst2BridgedPlugin.BlockTimeout = saved;
        }
    }
}

/// <summary>Scans and loads of a real 32-bit VST 2 module (TestData/Gain32.dll, built from gain32.c).</summary>
[NonParallelizable]
public sealed class Vst2Bridge32Tests
{
    private static string Gain32 => Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Gain32.dll");

    private sealed class RecordingScanner : IModuleScanner
    {
        public List<string> Scanned { get; } = [];

        public IReadOnlyList<PluginDescription> Scan(string modulePath, PluginFormat format, TimeSpan timeout)
        {
            Scanned.Add(Path.GetFileName(modulePath));
            return [new PluginDescription { Format = format, Name = "Gain32", Vendor = "Bridge Tests", Category = "Effect", InputChannels = 2, OutputChannels = 2 }];
        }
    }

    [Test]
    public void A32BitVst2ModuleIsScannedByTheBridgeOfItsArchitecture()
    {
        Assert.That(ModuleArchitectures.ReadMachine(Gain32), Is.EqualTo(ModuleArchitecture.X86));
        Assume.That(OperatingSystem.IsWindows() && ModuleArchitectures.Current == ModuleArchitecture.X64, "64-bit Windows (VST 2 modules are .dll files there)");
        string folder = Path.Combine(Path.GetTempPath(), "bridge-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = PluginScanner.BridgeScanners.TryGetValue(ModuleArchitecture.X86, out var previous) ? previous : null;
        try
        {
            File.Copy(Gain32, Path.Combine(folder, "Gain32.dll"));
            var settings = new HostSettings { Folders = [folder] };
            var scanner = new PluginScanner(new RecordingScanner()) { IncludeVst2 = true };

            // Without a bridge: listed, not loadable.
            PluginScanner.BridgeScanners.Remove(ModuleArchitecture.X86);
            var catalog = new PluginCatalog();
            scanner.Scan(settings, catalog);
            Assert.That(catalog.Plugins.Single().Status, Is.EqualTo(PluginStatus.Incompatible));

            // With a bridge: the unchanged module is scanned again by an ordinary scan, described by the bridge, usable.
            var bridge = new RecordingScanner();
            PluginScanner.BridgeScanners[ModuleArchitecture.X86] = bridge;
            var summary = scanner.Scan(settings, catalog);
            var gain = catalog.Plugins.Single();
            Assert.Multiple(() =>
            {
                Assert.That(bridge.Scanned, Is.EqualTo(new[] { "Gain32.dll" }));
                Assert.That((summary.Scanned, summary.Incompatible), Is.EqualTo((1, 0)));
                Assert.That((gain.Status, gain.Architecture, gain.Name), Is.EqualTo((PluginStatus.Ok, ModuleArchitecture.X86, "Gain32")));
            });

            // Then it is taken from the cache, even when the bridge failed on it.
            gain.Status = PluginStatus.Failed;
            Assert.That(scanner.Scan(settings, catalog).Unchanged, Is.EqualTo(1));

            // An entry written by a version without architectures (failed, "Unknown") is scanned again too.
            gain.Architecture = ModuleArchitecture.Unknown;
            Assert.That(scanner.Scan(settings, catalog).Scanned, Is.EqualTo(1));
            Assert.That(bridge.Scanned, Has.Count.EqualTo(2));
        }
        finally
        {
            if (saved is null) PluginScanner.BridgeScanners.Remove(ModuleArchitecture.X86);
            else PluginScanner.BridgeScanners[ModuleArchitecture.X86] = saved;
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The bridge built with the solution (tools/PluginHost.Bridge), or null.</summary>
    private static string? BuiltBridge()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "PluginUI.slnx"))) continue;
            foreach (string configuration in new[] { "Release", "Debug" })
            {
                string exe = Path.Combine(dir.FullName, "tools", "PluginHost.Bridge", "bin", configuration, "net10.0", "win-x86", Vst2Bridge.Bridge32FileName);
                if (File.Exists(exe)) return exe;
            }
            return null;
        }
        return null;
    }

    [Test]
    public void A32BitPluginPlaysInItsBridgeProcess()
    {
        Assume.That(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "64-bit Windows");
        string? bridge = BuiltBridge();
        Assume.That(bridge, Is.Not.Null, "the bridge is built with the solution");
        using var plugin = Vst2BridgedPlugin.Start(bridge!, Gain32, null, "Bridge Tests", ModuleArchitecture.X86);
        Assert.Multiple(() =>
        {
            Assert.That((plugin.Name, plugin.Vendor, plugin.LatencySamples), Is.EqualTo(("Gain32", "Bridge Tests", 16)));
            Assert.That(plugin.Parameters.Single().Title, Is.EqualTo("Gain"));
            Assert.That(plugin.FormatParameter(0, 0.25), Does.StartWith("0.5"));
        });
        plugin.SetParameter(0, 0.25); // gain 0.5
        plugin.Prepare(48000, 256, realtime: true);
        plugin.GetInput(0).Fill(0.8f);
        plugin.GetInput(1).Fill(-0.4f);
        plugin.AddMidiEvent(MidiEvent.NoteOn(3, 0, 60, 90));
        plugin.Process(256);
        Assert.Multiple(() =>
        {
            Assert.That(plugin.GetOutput(0)[..256].ToArray(), Is.All.EqualTo(0.4f).Within(1e-6f));
            Assert.That(plugin.GetOutput(1)[..256].ToArray(), Is.All.EqualTo(-0.2f).Within(1e-6f));
            Assert.That(plugin.OutputMidi.ToArray(), Is.EqualTo(new[] { MidiEvent.NoteOn(3, 0, 72, 90) }));
        });
        byte[] state = plugin.GetState();
        plugin.SetParameter(0, 1.0);
        plugin.Process(256);
        Assert.That(plugin.GetOutput(0)[0], Is.EqualTo(1.6f).Within(1e-6f));
        plugin.SetState(state);
        Assert.That(plugin.GetParameter(0), Is.EqualTo(0.25).Within(1e-6));
    }

    [Test]
    public void A32BitPluginsEditorOpensInsideAWindowOfTheHost()
    {
        Assume.That(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "64-bit Windows");
        string? bridge = BuiltBridge();
        Assume.That(bridge, Is.Not.Null, "the bridge is built with the solution");
        using var plugin = Vst2BridgedPlugin.Start(bridge!, Gain32, null, "Bridge Tests", ModuleArchitecture.X86);
        Assert.That(plugin.HasEditor, Is.True);
        nint parent = CreateWindowExW(0, "STATIC", "Bridge test", 0x00CF0000 /* WS_OVERLAPPEDWINDOW */, 0, 0, 400, 300, 0, 0, 0, 0);
        Assert.That(parent, Is.Not.Zero);
        try
        {
            using (var editor = plugin.CreateEditor()!)
            {
                Assert.That(editor.Size, Is.EqualTo((200, 100)));
                // The bridge creates the plug-in's window as a child of this one: this thread handles what it sends meanwhile.
                Assert.That(editor.Attach(parent), Is.True);
                nint child = GetWindow(parent, 5 /* GW_CHILD */);
                Assert.That(child, Is.Not.Zero);
                GetWindowThreadProcessId(child, out uint owner);
                Assert.That(owner, Is.Not.EqualTo((uint)Environment.ProcessId), "the window belongs to the bridge");
                (int, int)? asked = null;
                editor.ResizeRequested = (w, h) => { asked = (w, h); return true; };
                for (int i = 0; i < 100 && asked is null; i++)
                {
                    plugin.Idle();
                    if (asked is null) Thread.Sleep(20);
                }
                Assert.That(asked, Is.EqualTo((240, 120)), "the bridge gives the editor its idle time");
            }
            Assert.That(GetWindow(parent, 5), Is.Zero, "the editor's window is closed with the editor");
            Assert.That(plugin.FormatParameter(0, 0.25), Does.StartWith("0.5"));
        }
        finally
        {
            DestroyWindow(parent);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);
}

