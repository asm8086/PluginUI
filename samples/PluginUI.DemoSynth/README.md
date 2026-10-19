<!-- 2026/10/19 13:37:00 UTC - PluginUI -->

# PluginUI DemoSynth

[Version française](README_FR.md)

A small but complete VST3 instrument written in C# with [NPlug](https://github.com/xoofx/NPlug) and PluginUI. It shows what an instrument needs beyond an effect:

- a **MIDI event input** with sample-accurate notes;
- an **on-screen keyboard** played into the processor;
- **pitch bend and mod wheel** mapped to parameters (`IMidiMapping`);
- **MIDI learn** from the context menu of every control;
- an **audio input on an instrument**: an optional stereo side-chain that ducks the synth (pumping effect);
- **real-time feedback** from the audio thread: held notes, output and side-chain meters, gain reduction graph;
- **A/B comparison** of the whole sound.

![DemoSynth editor](../../docs/images/13-demosynth.png)

## Sound engine

16 voices, each with two detuned PolyBLEP oscillators (sawtooth → square), a zero-delay-feedback resonant low-pass with its own ADSR, an amplitude ADSR, vibrato from the mod wheel (5.5 Hz, up to ±50 cents) and pitch bend (±2 semitones). Voice stealing reuses the same note first, then a free voice, then the quietest releasing voice, then the oldest one. The engine (`Dsp/`) allocates nothing after construction.

| Parameter | Range | Default |
|---|---|---|
| Volume | −48 … +6 dB | −9 dB |
| Shape | 0 % (saw) … 100 % (square) | 0 % |
| Detune | 0 … 50 cents | 8 cents |
| Cutoff | 20 Hz … 20 kHz (log) | 2.4 kHz |
| Resonance | 0 … 100 % | 20 % |
| Filter Env | −5 … +5 octaves | +2 octaves |
| Amp / Filter Attack | 1 ms … 5 s (log) | 5 ms / 2 ms |
| Amp / Filter Decay | 5 ms … 10 s (log) | 300 ms / 400 ms |
| Amp / Filter Sustain | 0 … 100 % | 70 % / 20 % |
| Amp / Filter Release | 5 ms … 10 s (log) | 350 ms / 400 ms |
| Pitch Bend | ±2 semitones (MIDI pitch bend) | 0 |
| Mod Wheel | 0 … 100 % (MIDI CC 1) | 0 % |
| Sidechain Duck | 0 … 100 % | 0 % |
| Duck Release | 20 ms … 1 s (log) | 150 ms |

Buses: event input "MIDI In" (16 channels), audio output "Output" (stereo), audio input "Sidechain" (stereo, auxiliary, inactive until a track is routed to it).

## How it is built

| File | Role |
|---|---|
| `Plugin/DemoSynthModel.cs` | Parameters, conversion to `SynthSettings` |
| `Plugin/DemoSynthProcessor.cs` | `PluginUIProcessor<T>`: buses, sample-accurate events, notes from the editor, side-chain, meters |
| `Plugin/DemoSynthController.cs` | `PluginUIController<T>`: pitch bend / mod wheel mapping, MIDI learn enabled, editor |
| `Plugin/DemoSynthShared.cs` | Lock-free data shared by the processor with the editor |
| `Plugin/DemoSynthPlugin.cs` | Factory (category Instrument/Synth) |
| `Dsp/SynthDsp.cs` | PolyBLEP oscillator, ADSR, ZDF state-variable filter, envelope follower |
| `Dsp/SynthEngine.cs` | Voices, allocation, rendering, ducking |
| `Editor/DemoSynthEditor.cs` | PluginUI editor |

The editor sends the keys played on its keyboard with `ConnectKeyboard`; the processor reads them in `ProcessMain` with `TryDequeueUINote`. The processor's `CreateSharedState` object reaches the controller when the components connect, and the editor plugs its meters and keyboard into it.

## Build, debug, install

```
dotnet build -c Debug                      # bin\Debug\net10.0\win-x64\PluginUI.DemoSynth.vst3 (CoreCLR proxy, debuggable)
dotnet publish -c Release -r win-x64       # NativeAOT: PluginUI.DemoSynth.vst3, AVX2 build (x86-64-v3)
dotnet publish -c Release -r win-x64 -p:IlcInstructionSet=x86-64-v2   # for CPUs without AVX2
```

Copy the `.vst3` to `C:\Program Files\Common Files\VST3` and rescan in the DAW. NPlug comes from its NuGet package, through `PluginUI.NPlug`. Ready-made bundles (x64 and ARM64) are attached to each [release](https://github.com/asm8086/PluginUI/releases).

Side-chain routing: in Cubase, open the instrument's side-chain from the plug-in window header; in Reaper, set the track to 4 channels and send the kick to channels 3/4; in Bitwig, pick the source in the device's side-chain selector.

MIDI learn: right-click a control, choose **MIDI Learn**, move a knob on your controller. Learning needs a host that reports live MIDI controllers (VST3 `IMidiLearn`, e.g. Cubase and Nuendo); in other hosts use the host's own MIDI learn. Assignments are saved with the project.

## Tests

`tests/PluginUI.NPlug.Tests` checks the envelopes, filter and oscillators against closed forms, the engine (pitch, release, voice stealing, note ids, ducking, extreme settings) and runs the official Steinberg validator on the plug-in (47 tests passed).
