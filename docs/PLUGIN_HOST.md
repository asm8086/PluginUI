<!-- 2026/11/07 00:51:00 UTC - PluginUI -->

# Plug-in host: PluginUI.Host and pluginhost

*[Version française](PLUGIN_HOST_FR.md)*

`PluginUI.Host` loads VST3 plug-ins and runs them from .NET code. The `pluginhost` command line tool, built on it,
scans and lists the installed plug-ins, processes WAV files through chains of plug-ins, renders MIDI files with
instruments and measures frequency responses: it replaces scripts written with Python and Pedalboard, and it compiles
to one native executable (NativeAOT). **Plug-in Host** is the graphical host, built with PluginUI on the same library.

What exists today and what comes next:

| Part | Status |
|---|---|
| VST3 hosting (files, bundles, NPlug factory in the same process) | done |
| Plug-in folders in a settings file, scanned list kept between runs | done |
| Offline processing, MIDI rendering, response measurement (`pluginhost`) | done |
| VST 2 plug-ins | planned (separate package, written from public documentation) |
| Real-time audio (WASAPI output and input), signal sources, on-screen keyboard, MIDI input | done |
| MIDI output, system exclusive, MIDI produced by plug-ins (to a port, another plug-in or a file) | done |
| ASIO (all the channels of the card, its sample rates, its control panel) | done |
| Graphical host: plug-in list with sort orders and columns, editor windows, presets | done |

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
- [The graphical host](#the-graphical-host)
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
`/usr/local/lib/vst3`) and the VST 2 folders found (`C:\Program Files\VSTPlugins`, `C:\Program Files\Steinberg\VSTPlugins`,
`C:\Program Files\Common Files\VST2`, the folder registered by Steinberg in the registry). VST 3 plug-ins are looked
for in every folder, VST 2 ones too; VST 2 plug-ins are listed once VST 2 hosting is available.

```
pluginhost folders                          # list the folders
pluginhost folders add "D:\Audio\VST3"      # add one
pluginhost folders remove "D:\Audio\VST3"
pluginhost folders reset                    # back to the standard folders
```

`scan` finds the plug-ins and stores them in `plugins.json`. Each module is opened **in a separate process**, on a
single-threaded apartment thread as in a DAW: a plug-in that crashes, or makes no progress for 30 s (a shell module with
hundreds of plug-ins may take minutes, as long as it advances), is listed as failed with the reason, and the scan goes
on. What plug-ins print while loading does not disturb the scan, and a failure names the last step the plug-in reached
(loading the library, creating the component, initializing…). Later scans only open the
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

### MIDI produced by plug-ins

```
pluginhost render -p "Arpeggiator" --midi chords.mid --midi-out arp.mid
pluginhost render -p "Arpeggiator" -p "Diva" --midi chords.mid -o arp.wav
pluginhost process -i dry.wav -p "Pitch Tracker" --midi-out melody.mid
```

A plug-in with a MIDI output (VST3 event output bus: arpeggiators, chord generators, pitch trackers) passes its notes
and controllers to the next plug-in of the chain; `--midi-out` writes what the last plug-in produces to a MIDI file
(type 0, 960 ticks per quarter note, 120 BPM, times compensated for the latencies). When the last plug-in produces only
MIDI, `-o` can be left out. `process` also accepts `--midi <file.mid>` (a vocoder or a side-chained gate driven by
notes).

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

## The graphical host

**Plug-in Host** (`tools/PluginHost.Desktop`, `PluginHost.exe`) is the same host with a window, built with PluginUI. It
shares the plug-in folders and the plug-in list with `pluginhost`: a scan made by one is seen by the other.

```
dotnet run --project tools/PluginHost.Desktop                                    # IL, with the debugger
dotnet publish tools/PluginHost.Desktop -c Release -r win-x64 -p:PublishAot=true # one native PluginHost.exe
```

![Plug-in Host](images/pluginhost-main.png)

- **Plug-in list**: search (name, vendor, categories; Ctrl+F), format (VST3 / VST2), instruments or effects, favorites
  only, failures shown or not. Click a column header to sort (again for the reverse order), or choose an order in the
  **Sort** list: name, vendor, category, format, input or output channels, latency, installation date, recently used,
  most used, favorites first, folder, status. Right click on the header (or **View > Columns**) shows or hides columns:
  favorite, name, vendor, type, categories, format, inputs, outputs, sidechain, MIDI, latency, parameters, editor,
  version, installed, last used, uses, folder, status. Widths, columns, sort and filters are kept between runs.
- **Details** (Ctrl+I): format and SDK, version, categories, buses, MIDI input, latency in samples and milliseconds,
  parameters, editor, module path, dates, use count, and the reason of a failure.
- **Plug-in menu** (also the right click on a row, or the menu key): open (Enter, double-click), favorite (Ctrl+D),
  show in Explorer, copy the path, ignore the module (it is left out of later scans).
- **Scan** (F5: new and changed modules; Shift+F5: everything again; **Stop the scan** in the File menu or the tool bar): the scan runs in separate processes,
  with its progress in the status bar; the list changes only when it is complete.
- **Plug-in folders** (Ctrl+Shift+F): VST3 and VST2 folders, standard folders.
- **Audio and MIDI** (Ctrl+Shift+A): the driver, **WASAPI**, **ASIO** or none, then
  - with WASAPI: output device, input device, buffer size (the smallest the device allows by default: often under
    3 ms at 48 kHz); the device is shared with the other applications at the rate of the sound settings of Windows;
  - with ASIO: the driver of the sound card and its **Control panel** (buffer, clock, routing; it opens while the
    driver runs), its inputs (none or all), the sample rate (the driver's, or 44.1 to 192 kHz when the card accepts
    it), the buffer (the control panel's by default). Every output channel of the card is available: a multichannel
    plug-in is heard on as many outputs. When the control panel changes the buffer or the rate, the host restarts
    the driver by itself;
  - MIDI input port, MIDI output port.

  The status bar shows the device, its sample rate and its buffer; the meter at the right of the filter bar shows the
  output level.

![Audio and MIDI](images/pluginhost-audio.png)

![ASIO](images/pluginhost-asio.png)

![Plug-in folders](images/pluginhost-folders.png)

Each open plug-in has its own window: the plug-in's editor, sized as the plug-in asks (and resizable when the plug-in
allows it), or the **generic parameter table** when it has none (**Parameters** button otherwise). Values can be typed
in the table as the plug-in displays them (`-6 dB`, `Warm`) or as normalized values. **Load preset** / **Save preset**
read and write `.vstpreset` files. While its window is open the plug-in plays on the audio device; every open plug-in is
heard (their outputs are added). The bar at the bottom of the window holds:

- for effects, the **source** of the input: silence, the **audio input** of the device, a **test signal** (sine,
  square, sawtooth, triangle, white or pink noise, 20 Hz – 20 kHz sweep, impulses every second; frequency and level in
  dBFS), or an **audio file** (WAV, looped, resampled to the device rate, with a pause button);
- for instruments, an **on-screen keyboard** (mouse, or the computer keyboard when it has the focus); the MIDI input
  port reaches every open plug-in with a MIDI input, and its notes light up the keys;
- for plug-ins that produce MIDI, where it goes: **nowhere**, the **MIDI output port** (a hardware synthesizer, or
  another application through a loopback port), or the **other open plug-ins** that have a MIDI input;
- **Bypass** (the source goes straight to the output), **Mute**, and the meter of the plug-in's output.

The plug-in's main output channels go to the device channels in order (a mono output to the first two): a multichannel
plug-in is heard on a multichannel device. Without a device, the plug-in runs on a clock at 48 kHz with silent output,
so that its editor and parameters still behave as in a DAW.

`PluginHost.exe` also serves as the scanner's child process (`PluginHost.exe scan-module …`), like `pluginhost`.
`PluginHost.exe --config <folder>` uses another settings folder; the interface settings are in `gui.json` next to
`settings.json`.

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

Live, on an audio device (`PluginUI.Host.Windows`): a `PluginRack` holds the plug-ins, each with its source, and a
driver plays it.

```csharp
using PluginUI.Host.Windows;

var rack = new PluginRack();
var slot = rack.Add(eq, new TestSignalGenerator { Signal = TestSignal.PinkNoise, LevelDb = -18 });
using var device = new WasapiDriver(outputId: null);       // default device of Windows; Wasapi.GetEndpoints lists them
device.Start(rack);
slot.SendMidi(MidiEvent.NoteOn(0, 0, 60, 100));            // any thread
```

`AsioDriver` plays it through an ASIO driver (`Asio.GetDrivers()` lists them):

```csharp
using var card = new AsioDriver("My Card ASIO", bufferFrames: 128, sampleRate: 96000, inputChannels: -1);
card.ResetRequested += _ => ui.Post(() => card.Start(rack));   // buffer or rate changed in its control panel
card.Start(rack);                                              // on the UI thread (STA, message loop)
card.ShowControlPanel();
```

`inputChannels`: 0 none (default), -1 all, n the first n; `outputChannels` likewise (-1, all, by default). After
`Start`: `InputChannelNames`, `OutputChannelNames`, `BufferSizes`, `SupportedSampleRates`, `SampleFormat`,
`InputLatencySamples` / `OutputLatencySamples`. Samples in 16, 24 and 32-bit integers (also right-justified in 32
bits) and 32 or 64-bit floats, little or big-endian, are converted from and to 32-bit float. One ASIO driver at a
time per process, 64-bit only. ASIO is a trademark of Steinberg Media Technologies GmbH; the binding is written from
NAudio's (MIT), without the ASIO SDK.

`NullAudioDriver` runs the rack on a clock without a device. Sources: `SilenceSource`, `DeviceInputSource`,
`TestSignalGenerator`, `FileSource`; `RackSlot.Bypass`, `Mute`, `GainDb`, `Meter`; `MidiInput` opens a MIDI input
port (short messages with their time, system exclusive on request), `MidiOutput` an output port (`Send`, `SendSysex`,
`AllNotesOff`).

The MIDI a plug-in produces is in `plugin.OutputMidi` after each `Process` (`EventOutputCount` > 0 when it has an event
output). In a rack, `RackSlot.MidiOut` takes an `IMidiSink`, called on the audio thread: a `MidiOutput` (it queues the
messages, a thread of the port sends them), another `RackSlot`, or your own. `RenderOptions.MidiOutput` receives it in
offline renders, and `MidiFile.Write` saves events to a file.

```csharp
using var port = MidiOutput.Open(MidiOutput.Ports[0]);
arpSlot.MidiOut = port;                                    // or synthSlot
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

An application that loads plug-ins should be built without the CET shadow stack, which .NET turns on by default since
version 9: add `<CETCompat>false</CETCompat>` to its project. Some plug-ins (copy protection, `longjmp`, coroutines)
manipulate the stack in ways CET forbids, and Windows then ends the process with code 0xC0000409 in `ntdll.dll`; DAWs
do not enable CET either. `pluginhost` is built this way.
