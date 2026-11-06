<!-- 2026/11/06 22:50:00 UTC - PluginUI -->

# Plug-in host: PluginUI.Host and pluginhost

*[Version française](PLUGIN_HOST_FR.md)*

`PluginUI.Host` loads VST3 plug-ins and runs them from .NET code. The `pluginhost` command line tool, built on it,
scans and lists the installed plug-ins, processes WAV files through chains of plug-ins, renders MIDI files with
instruments and measures frequency responses: it replaces scripts written with Python and Pedalboard, and it compiles
to one native executable (NativeAOT).

What exists today and what comes next:

| Part | Status |
|---|---|
| VST3 hosting (files, bundles, NPlug factory in the same process) | done |
| Plug-in folders in a settings file, scanned list kept between runs | done |
| Offline processing, MIDI rendering, response measurement (`pluginhost`) | done |
| VST 2 plug-ins | planned (separate package, written from public documentation) |
| Real-time audio and MIDI devices (WASAPI, then ASIO), signal sources | planned |
| Graphical host (plug-in list with sort orders, editor windows) | planned |

## Contents

- [The command line host](#the-command-line-host)
  - [Building](#building)
  - [Plug-in folders and the plug-in list](#plug-in-folders-and-the-plug-in-list)
  - [Naming a plug-in](#naming-a-plug-in)
  - [Inspecting a plug-in](#inspecting-a-plug-in)
  - [Processing audio files](#processing-audio-files)
  - [Rendering MIDI with an instrument](#rendering-midi-with-an-instrument)
  - [Measuring a response](#measuring-a-response)
  - [Presets](#presets)
  - [From Pedalboard to pluginhost](#from-pedalboard-to-pluginhost)
- [The library](#the-library)
- [Files](#files)
- [How it works](#how-it-works)

## The command line host

### Building

```
dotnet build tools/PluginHost.Cli                                   # IL, run with: dotnet pluginhost.dll
dotnet publish tools/PluginHost.Cli -c Release -r win-x64 -p:PublishAot=true   # one native pluginhost.exe
```

`pluginhost --help` lists the commands. Exit codes: 0 success, 1 wrong command line, 2 error (file, plug-in).

### Plug-in folders and the plug-in list

The folders to scan are kept in `settings.json`; the first run fills it with the standard folders
(`C:\Program Files\Common Files\VST3`, `%LOCALAPPDATA%\Programs\Common\VST3`; on Linux `~/.vst3`, `/usr/lib/vst3`,
`/usr/local/lib/vst3`).

```
pluginhost folders                          # list the folders
pluginhost folders add "D:\Audio\VST3"      # add one
pluginhost folders remove "D:\Audio\VST3"
pluginhost folders reset                    # back to the standard folders
```

`scan` finds the plug-ins and stores them in `plugins.json`. Each module is opened **in a separate process**: a
plug-in that crashes or hangs (30 s by default) is listed as failed and the scan goes on. Later scans only open the
modules that are new or changed (size or date); `--all` opens everything again. Favorites and usage counts survive
rescans.

```
pluginhost scan
[1/3] C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 3.vst3
…
41 modules: 3 scanned, 38 unchanged, 0 plug-ins removed, 1 failed.
52 plug-ins available. List: C:\Users\…\AppData\Roaming\PluginUI\PluginHost\plugins.json
  failed: C:\…\Old.vst3: The scanner stopped (code 0xC0000005).
```

`list` shows the list without opening any plug-in:

```
pluginhost list                              # by name
pluginhost list eq --sort vendor             # text search (name, vendor, sub-categories), sorted by vendor
pluginhost list --instruments --sort recent  # instruments, most recently used first
pluginhost list --sort inputs --desc         # most input channels first
pluginhost list --failed                     # also show the modules that could not be opened, with the reason
pluginhost list --json                       # everything, for scripts
pluginhost favorite "Pro-Q 3"                # mark (or --off) a favorite
```

Sort orders: `name`, `vendor` (or `brand`), `category`, `format` (or `type`: VST2 / VST3), `inputs`, `outputs`
(channels), `latency`, `installed` (module date), `recent` (last use), `used` (use count), `favorites`, `folder`,
`status`.

### Naming a plug-in

Wherever a command takes `<plugin>` (`-p`), give:

- a name from the list, whole or partial, case ignored: `"Pro-Q 3"`, `pro-q`;
- `Vendor/Name` when two vendors use the same name: `"FabFilter/Pro-Q"`;
- a path to a `.vst3` file or bundle, optionally followed by `|Class name` when the module holds several plug-ins:
  `"D:\Tests\MyPlugin.vst3"`, `"D:\Tests\Suite.vst3|Suite Compressor"`.

### Inspecting a plug-in

```
pluginhost info "Pro-Q 3"
pluginhost info "Pro-Q 3" --json
```

Vendor, version, category, buses and channels, latency, tail, editor, and every parameter with its id, current value
as the plug-in displays it, default value, steps and flags.

### Processing audio files

```
pluginhost process -i dry.wav -o wet.wav -p "Pro-Q 3" --preset bright.vstpreset -p "Pro-L 2" --set "Gain=+3 dB"
```

- `-p` adds a plug-in to the chain; the options after it apply to that plug-in: `--set "Name=value"` (the text the
  plug-in displays, units optional: `"Gain=-6 dB"`, `"Gain=-6"`, `"Mode=Warm"`), `--set-normalized "Name=0.25"`,
  `--preset file.vstpreset`.
- A mono file through a stereo plug-in feeds both channels. Files with more than two channels are read and written
  (`WAVE_FORMAT_EXTENSIBLE`); each plug-in receives as many channels as its input bus has.
- The latencies reported by the plug-ins are compensated (the output is aligned with the input and keeps its length);
  `--no-latency-compensation` keeps the raw output.
- The tail (reverb, delay) is rendered after the end of the file: as long as the plug-ins report, at most 10 s, or
  `--tail <seconds>`.
- `--block 512` processing block size; `--bits 16|24|32|f32|f64` output format (default 32-bit float; 16 and 24-bit
  are dithered).
- Sample rate: that of the input file.

### Rendering MIDI with an instrument

```
pluginhost render -p "Diva" --preset pad.vstpreset --midi chords.mid -o pad.wav --rate 48000 --tail 4
```

Standard MIDI files of type 0 or 1, with their tempo changes. Notes and polyphonic pressure are sent as VST3 events;
controllers, channel pressure and pitch bend go to the parameters the instrument assigns to them (`IMidiMapping`), as
in a DAW. Effects can follow the instrument: add more `-p`.

### Measuring a response

```
pluginhost measure -p "Pro-Q 3" --preset tilt.vstpreset -o tilt.csv --ir tilt-ir.wav
pluginhost measure -p "My EQ" -o eq.csv --method sine --points 60 --level -20 --db 6log2
```

- `impulse` (default): an impulse through the plug-in, FFT of the response, with the latency removed from the phase.
  `--ir` also saves the impulse response.
- `sine`: one sine per frequency, the fundamental is measured after the plug-in has settled; slower, but suited to
  nonlinear plug-ins (`--level` in dBFS).
- `--min 20 --max 20000 --points 200`: logarithmic frequencies. `--channel` chooses the input channel that receives
  the signal; every output channel is measured.
- CSV: `frequency_hz,magnitude_db_1,phase_deg_1,…`. `--db 6log2` writes 6·log2 of the ratio instead of 20·log10.

### Presets

```
pluginhost preset show bright.vstpreset                       # class, plug-in, state sizes, metadata
pluginhost preset save "Pro-Q 3" --set "Output Level=-3 dB" -o quieter.vstpreset
```

`.vstpreset` files are those of Steinberg's SDK: they open in any VST3 host.

### From Pedalboard to pluginhost

| Python and Pedalboard | pluginhost |
|---|---|
| `load_plugin("Pro-Q 3.vst3")` | `-p "Pro-Q 3"` (from the list) or `-p "C:\…\Pro-Q 3.vst3"` |
| `plugin.gain_db = -6` | `--set "Gain=-6 dB"` |
| `plugin.raw_state = …` / `load_preset` | `--preset file.vstpreset` |
| `Pedalboard([a, b])(audio, sr)` | `process -i in.wav -o out.wav -p a -p b` |
| `instrument(midi_messages, duration, sr)` | `render -p instrument --midi file.mid -o out.wav` |
| `plugin.parameters` | `info <plugin>` |
| plug-in discovery by hand | `scan`, `list`, kept between runs |

Main differences: plug-ins are scanned once and found by name; a crashing plug-in does not stop the scan; latency is
compensated; responses are measured directly; one executable, no Python installation.

## The library

```csharp
using PluginUI.Host;
using PluginUI.Host.Audio;
using PluginUI.Host.Catalog;

var catalog = PluginCatalog.Load(HostPaths.CatalogFile);   // the list made by "pluginhost scan"
using var loader = new PluginLoader(catalog);
var eq = loader.Load("Pro-Q 3");                            // or a path
eq.TryParseParameter(eq.FindParameter("Output Level")!.Id, "-3 dB", out double value);
eq.SetParameter(eq.FindParameter("Output Level")!.Id, value);

var input = WavFile.Read("dry.wav");
var output = OfflineRenderer.Render([eq], input, input.SampleRate);
WavFile.Write("wet.wav", output, WavSampleFormat.Int24);
catalog.Save(HostPaths.CatalogFile);                        // keeps "last used"
```

Real-time use, without the renderer: `Prepare(sampleRate, maxBlock, realtime: true)`, then for each block fill
`GetInput(channel)`, call `Process(frames)` and read `GetOutput(channel)`. `SetParameter` and `AddMidiEvent` can be
called from any thread for the next block; `Idle()`, on the user interface thread, delivers the values the plug-in
changes itself (`ParameterChanged`) and the editor's edits. `GetState` / `SetState` read and write `.vstpreset`
images. For an editor window: `((Vst3Plugin)plugin).CreateEditor()?.Attach(hwnd, "HWND")`.

An NPlug plug-in can be hosted in its own process, without a `.vst3` file, for tests or a standalone application:

```csharp
using var module = Vst3Module.FromFactory(MyPlugin.GetFactory().Export());
using var plugin = module.CreatePlugin();
```

(the project that does this sets `<NPlugFactoryExport>false</NPlugFactoryExport>`).

Scanning from your own application: `PluginScanner.OutOfProcess(exe, args)` runs a helper process (for example
`pluginhost scan-module`) per module; `PluginScanner.InProcess()` opens the modules in the calling process.

## Files

| File | Contents |
|---|---|
| `%APPDATA%\PluginUI\PluginHost\settings.json` (`~/.config/PluginUI/PluginHost` on Linux) | VST3 and VST 2 folders, ignored modules, scan timeout |
| `…\plugins.json` | the plug-in list: identity, buses, channels, latency, parameter count, editor, module size and date, status, favorite, last use, use count |

`--config <folder>` makes `pluginhost` use another folder (one per project, tests). Both files are plain indented
JSON: they can be edited by hand, and are rewritten atomically.

## How it works

The host speaks VST3 directly: the plug-in interfaces are called through their virtual tables
(`delegate* unmanaged[MemberFunction]`), and the host interfaces (`IHostApplication`, `IComponentHandler`,
`IPlugFrame`, `IBStream`, `IParameterChanges`, `IEventList`, `IMessage`, `IAttributeList`) are native blocks pointing to
static `[UnmanagedCallersOnly]` functions. No reflection and no generated COM: NativeAOT compatible, and nothing is
allocated on the audio thread. The structure layouts and interface identifiers follow the VST 3 SDK (MIT license
since version 3.8). Tests (`tests/PluginUI.Host.Tests`) load the DemoSynth sample and a test plug-in with latency in
the same process.
