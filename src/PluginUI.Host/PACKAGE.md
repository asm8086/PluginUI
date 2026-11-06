<!-- 2026/11/06 22:50:00 UTC - PluginUI -->
# PluginUI.Host

Audio plug-in host for .NET 10: loads VST3 plug-ins and runs them from your own code, without any user interface
dependency. NativeAOT compatible, no reflection, allocation-free on the audio thread.

- `Vst3Module`: opens a `.vst3` file or bundle (Windows x64 / ARM64, Linux), or an NPlug factory in the same
  process (`Vst3Module.FromFactory(factory.Export())`), and lists its classes.
- `PluginInstance` / `Vst3Plugin`: buses and channel layouts, parameters (display text, typed values with units,
  normalized values; changes from any thread reach the audio thread through a lock-free queue), notes and MIDI
  controllers (through the plug-in's `IMidiMapping`), latency and tail, transport (tempo, signature, position), state
  and `.vstpreset` files, editor window (`Vst3Editor.Attach(HWND)`).
- `PluginCatalog` / `PluginScanner` / `HostSettings`: plug-in folders in a settings file, scan in a separate process
  (a crashing plug-in is marked as failed instead of stopping the host), incremental rescans, the list kept in
  `plugins.json` with favorites and usage; queries by text, format, instrument / effect and a dozen sort orders.
- `OfflineRenderer`: renders a chain of plug-ins on a WAV file or a MIDI file, with latency compensation and tails.
- `ResponseMeasurer`: magnitude and phase responses (impulse with FFT, or stepped sine), impulse response.
- `WavFile` (16 / 24 / 32-bit integer, 32 / 64-bit float, more than two channels), `MidiFile` (types 0 and 1, tempo map).

The `pluginhost` command line tool of the repository is built on this package. Guide:
https://github.com/asm8086/PluginUI/blob/main/docs/PLUGIN_HOST.md
