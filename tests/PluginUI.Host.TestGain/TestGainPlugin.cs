// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using NPlug;

namespace PluginUI.Host.Tests;

/// <summary>Minimal NPlug effect used to test the host: stereo gain in dB, delayed by its reported latency (32 samples).</summary>
public sealed class TestGainModel : AudioProcessorModel
{
    public TestGainModel() : base("PluginUI.Host.Tests.Gain")
    {
        Gain = AddParameter(new AudioRangeParameter("Gain", units: "dB", minValue: -24.0, maxValue: 24.0, defaultPlainValue: 0.0) { Precision = 1 });
        Mode = AddParameter(new AudioStringListParameter("Mode", ["Clean", "Warm", "Hot"]));
    }

    public AudioRangeParameter Gain { get; }

    public AudioStringListParameter Mode { get; }
}

public sealed class TestGainProcessor : AudioProcessor<TestGainModel>
{
    public static readonly Guid ClassId = new("2b8f4c1e-6a3d-4e5f-9b7a-1c2d3e4f5a6b");

    public TestGainProcessor() : base(AudioSampleSizeSupport.Float32, latencySamples: 32, tailSamples: 0, AudioProcessContextRequirementFlags.None)
    {
    }

    public override Guid ControllerClassId => TestGainController.ClassId;

    protected override bool Initialize(AudioHostApplication host)
    {
        AddAudioInput("Input", SpeakerArrangement.SpeakerStereo);
        AddAudioOutput("Output", SpeakerArrangement.SpeakerStereo);
        return true;
    }

    private const int Latency = 32;
    private readonly float[][] _delay = [new float[Latency], new float[Latency]];
    private int _position;

    protected override void OnActivate(bool isActive)
    {
        foreach (var line in _delay) Array.Clear(line);
        _position = 0;
    }

    protected override void ProcessMain(in AudioProcessData data)
    {
        // Really delays by the reported latency, so latency compensation can be checked.
        float gain = (float)Math.Pow(10.0, Model.Gain.Value / 20.0);
        int start = _position;
        for (int channel = 0; channel < 2; channel++)
        {
            var input = data.Input[0].GetChannelSpanAsFloat32(ProcessSetupData, data, channel);
            var output = data.Output[0].GetChannelSpanAsFloat32(ProcessSetupData, data, channel);
            var line = _delay[channel];
            int p = start;
            for (int i = 0; i < data.SampleCount; i++)
            {
                float x = input[i];
                output[i] = line[p] * gain;
                line[p] = x;
                p = (p + 1) % Latency;
            }
            _position = p;
        }
    }
}

public sealed class TestGainController : AudioController<TestGainModel>
{
    public static readonly Guid ClassId = new("7c6d5e4f-3a2b-4c1d-8e9f-0a1b2c3d4e5f");
}

public static class TestGainPlugin
{
    public static AudioPluginFactory GetFactory()
    {
        var factory = new AudioPluginFactory(new AudioPluginFactoryInfo("PluginUI Tests", "https://github.com/asm8086/PluginUI", "tests@example.invalid"));
        factory.RegisterPlugin<TestGainProcessor>(new AudioProcessorClassInfo(TestGainProcessor.ClassId, "Test Gain", AudioProcessorCategory.Effect) { Version = new Version(1, 2, 3) });
        factory.RegisterPlugin<TestGainController>(new AudioControllerClassInfo(TestGainController.ClassId, "Test Gain Controller"));
        return factory;
    }

    /// <summary>Publishes the factory when the module is loaded as a real .vst3 (NativeAOT build).</summary>
    [ModuleInitializer]
    internal static void ExportThisPlugin() => AudioPluginFactoryExporter.Instance = GetFactory();
}
