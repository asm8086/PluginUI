// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using NPlug;
using PluginUI.DemoSynth.Dsp;

namespace PluginUI.DemoSynth.Plugin;

/// <summary>
/// Parameters of the demo synth, shared (by value, through the host) between the processor and the controller.
/// </summary>
/// <remarks>
/// Identifiers follow the declaration order: only append new parameters at the end once presets or automation exist.
/// An instrument needs no bypass parameter.
/// </remarks>
public sealed class DemoSynthModel : AudioProcessorModel
{
    /// <summary>Creates the model and declares its parameters.</summary>
    public DemoSynthModel() : base("PluginUI.DemoSynth")
    {
        Volume = AddParameter(new AudioRangeParameter("Volume", units: "dB", minValue: -48.0, maxValue: 6.0, defaultPlainValue: -9.0) { Precision = 1 });
        Shape = AddParameter(new AudioRangeParameter("Shape", units: "%", minValue: 0.0, maxValue: 100.0, defaultPlainValue: 0.0) { Precision = 0 });
        Detune = AddParameter(new AudioRangeParameter("Detune", units: "ct", minValue: 0.0, maxValue: 50.0, defaultPlainValue: 8.0) { Precision = 1 });
        Cutoff = AddParameter(new LogRangeParameter("Cutoff", "Hz", minValue: 20.0, maxValue: 20000.0, defaultPlainValue: 2400.0, decimals: 0));
        Resonance = AddParameter(new AudioRangeParameter("Resonance", units: "%", minValue: 0.0, maxValue: 100.0, defaultPlainValue: 20.0) { Precision = 0 });
        FilterEnvAmount = AddParameter(new AudioRangeParameter("Filter Env", units: "oct", minValue: -5.0, maxValue: 5.0, defaultPlainValue: 2.0) { Precision = 2 });

        AmpAttack = AddParameter(new LogRangeParameter("Amp Attack", "s", 0.001, 5.0, 0.005, decimals: 3));
        AmpDecay = AddParameter(new LogRangeParameter("Amp Decay", "s", 0.005, 10.0, 0.3, decimals: 3));
        AmpSustain = AddParameter(new AudioRangeParameter("Amp Sustain", units: "%", minValue: 0.0, maxValue: 100.0, defaultPlainValue: 70.0) { Precision = 0 });
        AmpRelease = AddParameter(new LogRangeParameter("Amp Release", "s", 0.005, 10.0, 0.35, decimals: 3));

        FilterAttack = AddParameter(new LogRangeParameter("Filter Attack", "s", 0.001, 5.0, 0.002, decimals: 3));
        FilterDecay = AddParameter(new LogRangeParameter("Filter Decay", "s", 0.005, 10.0, 0.4, decimals: 3));
        FilterSustain = AddParameter(new AudioRangeParameter("Filter Sustain", units: "%", minValue: 0.0, maxValue: 100.0, defaultPlainValue: 20.0) { Precision = 0 });
        FilterRelease = AddParameter(new LogRangeParameter("Filter Release", "s", 0.005, 10.0, 0.4, decimals: 3));

        // Driven by the host from MIDI pitch bend and CC 1 (see DemoSynthController).
        PitchBend = AddParameter(new AudioRangeParameter("Pitch Bend", units: "st", minValue: -2.0, maxValue: 2.0, defaultPlainValue: 0.0) { Precision = 2 });
        ModWheel = AddParameter(new AudioRangeParameter("Mod Wheel", units: "%", minValue: 0.0, maxValue: 100.0, defaultPlainValue: 0.0) { Precision = 0 });

        Duck = AddParameter(new AudioRangeParameter("Sidechain Duck", units: "%", minValue: 0.0, maxValue: 100.0, defaultPlainValue: 0.0) { Precision = 0 });
        DuckRelease = AddParameter(new LogRangeParameter("Duck Release", "s", 0.02, 1.0, 0.15, decimals: 3));
    }

    /// <summary>Output level in dB.</summary>
    public AudioRangeParameter Volume { get; }

    /// <summary>Oscillator shape: 0 % = sawtooth, 100 % = square.</summary>
    public AudioRangeParameter Shape { get; }

    /// <summary>Detune between the two oscillators in cents.</summary>
    public AudioRangeParameter Detune { get; }

    /// <summary>Filter cutoff in Hz.</summary>
    public LogRangeParameter Cutoff { get; }

    /// <summary>Filter resonance in %.</summary>
    public AudioRangeParameter Resonance { get; }

    /// <summary>Filter envelope depth in octaves.</summary>
    public AudioRangeParameter FilterEnvAmount { get; }

    /// <summary>Amplitude envelope attack in seconds.</summary>
    public LogRangeParameter AmpAttack { get; }

    /// <summary>Amplitude envelope decay in seconds.</summary>
    public LogRangeParameter AmpDecay { get; }

    /// <summary>Amplitude envelope sustain in %.</summary>
    public AudioRangeParameter AmpSustain { get; }

    /// <summary>Amplitude envelope release in seconds.</summary>
    public LogRangeParameter AmpRelease { get; }

    /// <summary>Filter envelope attack in seconds.</summary>
    public LogRangeParameter FilterAttack { get; }

    /// <summary>Filter envelope decay in seconds.</summary>
    public LogRangeParameter FilterDecay { get; }

    /// <summary>Filter envelope sustain in %.</summary>
    public AudioRangeParameter FilterSustain { get; }

    /// <summary>Filter envelope release in seconds.</summary>
    public LogRangeParameter FilterRelease { get; }

    /// <summary>Pitch bend in semitones (MIDI pitch bend).</summary>
    public AudioRangeParameter PitchBend { get; }

    /// <summary>Mod wheel in % (MIDI CC 1): vibrato depth.</summary>
    public AudioRangeParameter ModWheel { get; }

    /// <summary>Side-chain ducking depth in %.</summary>
    public AudioRangeParameter Duck { get; }

    /// <summary>Side-chain release time in seconds.</summary>
    public LogRangeParameter DuckRelease { get; }

    /// <summary>Current values as engine settings (audio thread, no allocation).</summary>
    /// <returns>The settings.</returns>
    public SynthSettings ToSettings() => new()
    {
        Volume = (float)Math.Pow(10.0, Volume.Value / 20.0),
        Shape = (float)(Shape.Value / 100.0),
        DetuneCents = Detune.Value,
        Cutoff = Cutoff.Value,
        Resonance = Resonance.Value / 100.0,
        FilterEnvOctaves = FilterEnvAmount.Value,
        AmpAttack = AmpAttack.Value,
        AmpDecay = AmpDecay.Value,
        AmpSustain = AmpSustain.Value / 100.0,
        AmpRelease = AmpRelease.Value,
        FilterAttack = FilterAttack.Value,
        FilterDecay = FilterDecay.Value,
        FilterSustain = FilterSustain.Value / 100.0,
        FilterRelease = FilterRelease.Value,
        PitchBendSemitones = PitchBend.Value,
        ModWheel = ModWheel.Value / 100.0,
        Duck = (float)(Duck.Value / 100.0),
        DuckRelease = DuckRelease.Value,
    };
}
