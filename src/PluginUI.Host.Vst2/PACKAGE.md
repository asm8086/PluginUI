<!-- 2026/11/07 00:51:00 UTC - PluginUI -->
# PluginUI.Host.Vst2

VST 2.4 plug-in hosting for [PluginUI.Host](https://www.nuget.org/packages/PluginUI.Host): VST 2 plug-ins are scanned,
listed and played like VST 3 ones.

```csharp
Vst2Hosting.Register();                       // the scanner and PluginLoader now handle .dll / .so VST 2 modules
using var synth = Vst2Hosting.Load(@"C:\Program Files\VSTPlugins\MySynth.dll");
synth.Prepare(48000, 512);
synth.AddMidiEvent(MidiEvent.NoteOn(0, 0, 60, 100));
synth.Process(512);
File.WriteAllBytes("bank.fxb", synth.GetState());
```

- Scanning: one plug-in per module, or every plug-in of a shell module (`Vst2Module.ShellPlugins`); libraries without a
  VST 2 entry point are skipped; 32-bit modules are reported as such.
- Processing in 32-bit float (`processReplacing`; the accumulating `process` of VST 1 plug-ins too), MIDI to and from
  the plug-in, position and tempo (`audioMasterGetTime`), latency, tail.
- Parameters with their names, units and steps, typed values (`effString2Parameter`); programs.
- Presets: `.fxp` (one program) and `.fxb` (bank), as parameter values or chunks (`FxPreset`).
- Editors (`Vst2Editor`), opened in a window of the host and resized at the plug-in's request.
- 64-bit modules in a 64-bit process. NativeAOT compatible.

Written from the public description of the VST 2.4 interface. VST is a trademark of Steinberg Media Technologies GmbH;
no part of the VST SDK is used.
