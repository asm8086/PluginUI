<!-- 2026/11/06 23:57:00 UTC - PluginUI -->
# PluginUI.Host

Audio plug-in host for .NET 10: loads VST3 plug-ins (and VST 2 ones with
[PluginUI.Host.Vst2](https://www.nuget.org/packages/PluginUI.Host.Vst2)) and runs them from your own code, offline or
live, without any user interface dependency. NativeAOT compatible, no reflection, allocation-free on the audio thread.

- `Vst3Module`: opens a `.vst3` file or bundle (Windows x64 / ARM64, Linux), or an NPlug factory in the same
  process (`Vst3Module.FromFactory(factory.Export())`), and lists its classes.
- `PluginInstance` / `Vst3Plugin`: buses and channel layouts, parameters (display text, typed values with units,
  normalized values; changes from any thread reach the audio thread through a lock-free queue), notes and MIDI
  controllers (through the plug-in's `IMidiMapping`), latency and tail, transport (tempo, signature, position), state
  and `.vstpreset` files, MIDI the plug-in produces (`OutputMidi`), editor window (`CreateEditor()` gives a
  `PluginEditor`; `Attach(HWND)`).
- `PluginCatalog` / `PluginScanner` / `HostSettings`: plug-in folders in a settings file, scan in a separate process
  (a crashing plug-in is marked as failed instead of stopping the host), incremental rescans, the list kept in
  `plugins.json` with favorites and usage; queries by text, format, instrument / effect and a dozen sort orders. One
  list of folders, searched for both formats.
- `OfflineRenderer`: renders a chain of plug-ins on a WAV file or a MIDI file, with latency compensation and tails;
  the MIDI of each plug-in feeds the next one.
- Live audio: `AudioDriver` and `IAudioRenderer` (the devices are in
  [PluginUI.Host.Windows](https://www.nuget.org/packages/PluginUI.Host.Windows): ASIO, WASAPI, MIDI ports),
  `NullAudioDriver`, `PluginRack` (plug-ins played live, each with its source, bypass, mute, gain, meter and MIDI
  routing), signal sources (device input, test signals, audio files).
- `ResponseMeasurer`: magnitude and phase responses (impulse with FFT, or stepped sine), impulse response.
- `WavFile` (16 / 24 / 32-bit integer, 32 / 64-bit float, more than two channels), `MidiFile` (reads types 0 and 1
  with their tempo map, writes type 0).

Build the application that loads plug-ins with `<CETCompat>false</CETCompat>`: some plug-ins are not compatible with
the CET shadow stack .NET enables by default, and Windows ends the process (0xC0000409 in ntdll).

The `pluginhost` command line tool and the Plug-in Host application of the repository are built on this package. Guide:
https://github.com/asm8086/PluginUI/blob/main/docs/PLUGIN_HOST.md
