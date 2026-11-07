<!-- 2026/11/07 00:51:00 UTC - PluginUI -->
# PluginUI.Host.Windows

Windows audio and MIDI devices for [PluginUI.Host](https://www.nuget.org/packages/PluginUI.Host): plays plug-ins live.

- `AsioDriver`: ASIO drivers of sound cards (`Asio.GetDrivers`): every channel, the card's sample rates and buffer
  sizes, its control panel, restart requests (`ResetRequested`); integer and float sample formats converted from and
  to 32-bit float. Open it on the UI thread (STA). 64-bit only. ASIO is a trademark of Steinberg Media Technologies
  GmbH; no part of the ASIO SDK is used.
- `WasapiDriver`: WASAPI in shared mode, event-driven, at the smallest period the audio engine allows (IAudioClient3,
  often under 3 ms at 48 kHz) or the requested buffer; optional input from a recording device at the same sample rate;
  samples converted from and to 32-bit float; the audio thread is registered with MMCSS ("Pro Audio"); a removed
  device is reported through `AudioDriver.Failed`.
- `Wasapi.GetEndpoints`: output and input devices with their names, channels and sample rates.
- `MidiInput`: MIDI input ports (winmm), short messages with their time delivered on the driver's thread, system
  exclusive messages on request.
- `MidiOutput`: MIDI output ports (winmm): `Send`, `SendSysex`, `AllNotesOff`; as an `IMidiSink` it takes the MIDI
  a plug-in produces on the audio thread without waiting (`RackSlot.MidiOut`).

The ASIO binding and the handling of system exclusive buffers follow NAudio (MIT, Copyright 2008-2026 Mark Heath).

```csharp
var rack = new PluginRack();
var slot = rack.Add(plugin, new TestSignalGenerator { Signal = TestSignal.PinkNoise, LevelDb = -18 });
using var device = new WasapiDriver(outputId: null);   // default device of Windows
device.Start(rack);
```

NativeAOT compatible (function pointers, no runtime marshalling). Windows 10 1809 or later. Build the application with
`<CETCompat>false</CETCompat>` when it loads plug-ins.
