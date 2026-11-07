// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Host.Audio;
using PluginUI.Host.Catalog;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Tests;

public class AudioFileTests
{
    [TestCase(WavSampleFormat.Int16, 1e-4)]
    [TestCase(WavSampleFormat.Int24, 1e-6)]
    [TestCase(WavSampleFormat.Int32, 1e-8)]
    [TestCase(WavSampleFormat.Float32, 0)]
    [TestCase(WavSampleFormat.Float64, 0)]
    public void WavRoundTrips(WavSampleFormat format, double tolerance)
    {
        var audio = AudioBuffer.Create(44100, 3, 1000);
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < 1000; i++) audio.Channels[c][i] = (float)(0.5 * Math.Sin(i * 0.01 * (c + 1)));
        var back = WavFile.Read(WavFile.Write(audio, format));
        Assert.Multiple(() =>
        {
            Assert.That(back.SampleRate, Is.EqualTo(44100));
            Assert.That(back.ChannelCount, Is.EqualTo(3), "WAVE_FORMAT_EXTENSIBLE above two channels");
            Assert.That(back.Length, Is.EqualTo(1000));
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 1000; i += 37) Assert.That(back.Channels[c][i], Is.EqualTo(audio.Channels[c][i]).Within(tolerance));
        });
    }

    [Test]
    public void MidiFileFollowsTheTempo()
    {
        // Type 0, 96 ticks per quarter: note on at 0, tempo 60 BPM at tick 96, note off at tick 192.
        byte[] track =
        [
            0x00, 0x90, 60, 100,
            0x60, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40, // 1 000 000 µs per quarter
            0x60, 0x80, 60, 0,
            0x00, 0xFF, 0x2F, 0x00,
        ];
        var file = new List<byte>();
        file.AddRange("MThd"u8.ToArray()); file.AddRange([0, 0, 0, 6, 0, 0, 0, 1, 0, 96]);
        file.AddRange("MTrk"u8.ToArray()); file.AddRange([0, 0, 0, (byte)track.Length]); file.AddRange(track);
        var events = MidiFile.Read(file.ToArray(), 48000);
        Assert.That(events.Select(e => (e.Sample, e.Event.Type)), Is.EqualTo(new[] { (0L, 0x90), (24000L + 48000L, 0x80) }),
            "half a second at 120 BPM, then one second at 60 BPM");
    }
}

public class CatalogTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "pluginhost-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private sealed class FakeScanner : IModuleScanner
    {
        public List<string> Scanned { get; } = [];

        public IReadOnlyList<PluginDescription> Scan(string modulePath, PluginFormat format, TimeSpan timeout)
        {
            Scanned.Add(Path.GetFileName(modulePath));
            if (modulePath.Contains("Crash", StringComparison.Ordinal)) throw new InvalidOperationException("The scanner stopped (code 0xC0000005).");
            return [new PluginDescription { Format = format, Name = Path.GetFileNameWithoutExtension(modulePath), Vendor = "V", ClassId = "A", Category = "Effect", InputChannels = 2, OutputChannels = 2 }];
        }
    }

    [Test]
    public void ScanFindsBundlesAndFilesAndRescansOnlyChangedModules()
    {
        string vst3 = Path.Combine(_root, "VST3");
        Directory.CreateDirectory(Path.Combine(vst3, "Vendor", "Eq.vst3", "Contents", Vst3Module.BundleArchitectureFolder));
        File.WriteAllText(Path.Combine(vst3, "Vendor", "Eq.vst3", "Contents", Vst3Module.BundleArchitectureFolder, OperatingSystem.IsWindows() ? "Eq.vst3" : "Eq.so"), "x");
        File.WriteAllText(Path.Combine(vst3, "Single.vst3"), "x");
        File.WriteAllText(Path.Combine(vst3, "Crash.vst3"), "x");
        File.WriteAllText(Path.Combine(vst3, "readme.txt"), "x");
        var settings = new HostSettings { Vst3Folders = [vst3] };
        var catalog = new PluginCatalog();
        var fake = new FakeScanner();
        var scanner = new PluginScanner(fake);

        var first = scanner.Scan(settings, catalog);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new ScanSummary(3, 3, 0, 0, 1)));
            Assert.That(catalog.Plugins.Select(p => p.Name).Order(), Is.EqualTo(new[] { "Crash", "Eq", "Single" }));
            Assert.That(catalog.Plugins.Single(p => p.Name == "Crash").Status, Is.EqualTo(PluginStatus.Failed));
        });

        catalog.Plugins.Single(p => p.Name == "Single").IsFavorite = true;
        string file = Path.Combine(_root, "plugins.json");
        catalog.Save(file);
        catalog = PluginCatalog.Load(file);

        fake.Scanned.Clear();
        File.SetLastWriteTimeUtc(Path.Combine(vst3, "Single.vst3"), DateTime.UtcNow.AddMinutes(1));
        File.Delete(Path.Combine(vst3, "Crash.vst3"));
        var second = scanner.Scan(settings, catalog);
        Assert.Multiple(() =>
        {
            Assert.That(fake.Scanned, Is.EqualTo(new[] { "Single.vst3" }), "only the changed module");
            Assert.That(second.Removed, Is.EqualTo(1));
            Assert.That(catalog.Plugins.Single(p => p.Name == "Single").IsFavorite, Is.True, "user data survives a rescan");
        });
    }

    [Test]
    public void Vst3ModulesAreFoundInVst2FoldersAndVst2OnlyWhenHosted()
    {
        string mixed = Path.Combine(_root, "VSTPlugins");
        Directory.CreateDirectory(mixed);
        File.WriteAllText(Path.Combine(mixed, "Ours.vst3"), "x");
        File.WriteAllText(Path.Combine(mixed, OperatingSystem.IsWindows() ? "Old.dll" : "Old.so"), "x");
        var settings = new HostSettings { Vst2Folders = [mixed] };
        Assert.Multiple(() =>
        {
            Assert.That(PluginScanner.FindModules(settings).Select(m => (Path.GetFileName(m.Path), m.Format)),
                Is.EquivalentTo(new[] { ("Ours.vst3", PluginFormat.Vst3), (OperatingSystem.IsWindows() ? "Old.dll" : "Old.so", PluginFormat.Vst2) }));
            Assert.That(PluginScanner.FindModules(settings, includeVst2: false).Single().Format, Is.EqualTo(PluginFormat.Vst3));
        });

        var catalog = new PluginCatalog();
        var fake = new FakeScanner();
        new PluginScanner(fake).Scan(settings, catalog);
        Assert.That(fake.Scanned, Is.EqualTo(new[] { "Ours.vst3" }), "no VST 2 hosting installed: VST 2 modules are not listed as failures");
    }

    [Test]
    public void QuerySortsAndFindResolvesNames()
    {
        var catalog = new PluginCatalog
        {
            Plugins =
            [
                new() { Name = "Zeta Verb", Vendor = "B", Category = "Effect", SubCategories = "Fx|Reverb", InputChannels = 2, OutputChannels = 2, Format = PluginFormat.Vst3 },
                new() { Name = "Alpha Synth", Vendor = "C", Category = "Instrument", SubCategories = "Instrument|Synth", OutputChannels = 2, Format = PluginFormat.Vst2 },
                new() { Name = "Mid EQ", Vendor = "A", Category = "Effect", SubCategories = "Fx|EQ", InputChannels = 6, OutputChannels = 6, Format = PluginFormat.Vst3, UseCount = 5 },
                new() { Name = "Broken", Vendor = "A", Status = PluginStatus.Failed },
            ],
        };
        Assert.Multiple(() =>
        {
            Assert.That(catalog.Query(sort: PluginSort.Name, includeUnavailable: false).Select(p => p.Name), Is.EqualTo(new[] { "Alpha Synth", "Mid EQ", "Zeta Verb" }));
            Assert.That(catalog.Query(sort: PluginSort.Vendor, includeUnavailable: false).Select(p => p.Vendor), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(catalog.Query(sort: PluginSort.Inputs).First().Name, Is.EqualTo("Mid EQ"));
            Assert.That(catalog.Query(sort: PluginSort.MostUsed).First().Name, Is.EqualTo("Mid EQ"));
            Assert.That(catalog.Query(format: PluginFormat.Vst2).Single().Name, Is.EqualTo("Alpha Synth"));
            Assert.That(catalog.Query(instruments: true).Single().Name, Is.EqualTo("Alpha Synth"));
            Assert.That(catalog.Query("reverb").Single().Name, Is.EqualTo("Zeta Verb"));
            Assert.That(catalog.Find("mid eq").Single().Name, Is.EqualTo("Mid EQ"));
            Assert.That(catalog.Find("A/mid").Single().Name, Is.EqualTo("Mid EQ"));
            Assert.That(catalog.Find("Broken"), Is.Empty, "failed plug-ins are not loadable");
        });
    }

    [Test]
    public void SettingsRoundTrip()
    {
        string file = Path.Combine(_root, "settings.json");
        new HostSettings { Vst3Folders = ["a", "b"], Vst2Folders = ["c"], ScanTimeoutSeconds = 12 }.Save(file);
        var settings = HostSettings.Load(file);
        Assert.That(settings.Vst3Folders, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(settings.Vst2Folders, Is.EqualTo(new[] { "c" }));
        Assert.That(settings.ScanTimeoutSeconds, Is.EqualTo(12));
    }

    [Test]
    public void ClassIdStringsUseTheSdkForm()
    {
        var id = Tuid.From(0x12345678, 0x9ABCDEF0, 0x0F1E2D3C, 0x4B5A6978);
        Assert.That(Vst3Identifiers.ToFuidString(id), Is.EqualTo("123456789ABCDEF00F1E2D3C4B5A6978"), "the four words, whatever the platform");
        Assert.That(Vst3Identifiers.FromFuidString("123456789abcdef00f1e2d3c4b5a6978"), Is.EqualTo(id));
    }
}

public class RenderingTests
{
    private static Vst3Module GainModule() => Vst3Module.FromFactory(TestGainPlugin.GetFactory().Export(), "Host Tests");

    [Test]
    public void ChainRendersWithLatencyCompensation()
    {
        using var module = GainModule();
        using var a = module.CreatePlugin();
        using var b = module.CreatePlugin();
        a.TryParseParameter(a.FindParameter("Gain")!.Id, "-6", out double minus6);
        a.SetParameter(a.FindParameter("Gain")!.Id, minus6);
        var input = AudioBuffer.Create(48000, 1, 1000);
        input.Channels[0][100] = 1f;
        var output = OfflineRenderer.Render([a, b], input, options: new RenderOptions { BlockSize = 64, TailSeconds = 0 });
        Assert.Multiple(() =>
        {
            Assert.That(output.ChannelCount, Is.EqualTo(2), "mono into a stereo plug-in");
            Assert.That(output.Length, Is.EqualTo(1000));
            Assert.That(output.Channels[0][100], Is.EqualTo(0.501f).Within(0.001f), "64 samples of latency removed");
            Assert.That(output.Channels[1][100], Is.EqualTo(0.501f).Within(0.001f));
            Assert.That(output.Channels[0][99] + output.Channels[0][101], Is.EqualTo(0f));
        });

        var raw = OfflineRenderer.Render([a, b], input, options: new RenderOptions { BlockSize = 64, TailSeconds = 0, CompensateLatency = false });
        Assert.That(raw.Channels[0][164], Is.EqualTo(0.501f).Within(0.001f), "without compensation the impulse arrives 64 samples later");
    }

    [Test]
    public void MeasurementsAgree()
    {
        using var module = GainModule();
        using var gain = module.CreatePlugin();
        gain.TryParseParameter(gain.FindParameter("Gain")!.Id, "4.5", out double n);
        gain.SetParameter(gain.FindParameter("Gain")!.Id, n);
        var impulse = ResponseMeasurer.Measure(gain, new ResponseOptions { Points = 4 });
        var sine = ResponseMeasurer.Measure(gain, new ResponseOptions { Points = 4, Method = MeasurementMethod.SteppedSine });
        Assert.Multiple(() =>
        {
            foreach (var p in impulse.Channels[0]) Assert.That(p.MagnitudeDb, Is.EqualTo(4.5).Within(0.01));
            foreach (var p in sine.Channels[0]) Assert.That(p.MagnitudeDb, Is.EqualTo(4.5).Within(0.05));
            foreach (var p in impulse.Channels[0]) Assert.That(p.PhaseDegrees, Is.EqualTo(0).Within(0.5), "latency removed from the phase");
            Assert.That(impulse.ToCsv(), Does.StartWith("frequency_hz,magnitude_db_1,phase_deg_1"));
        });
    }
}
