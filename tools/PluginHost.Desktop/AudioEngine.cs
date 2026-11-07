// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Host.Audio;
using PluginUI.Host.Windows;

namespace PluginUI.Host.Desktop;

/// <summary>
/// The audio of the host: one <see cref="PluginRack"/> played by the chosen device (WASAPI, or a clock without device
/// when none is available), and the MIDI input port, whose messages go to every open plug-in with a MIDI input.
/// </summary>
internal sealed class AudioEngine : IDisposable
{
    private readonly UIHost _host;
    private MidiInput? _midi;
    private MidiOutput? _midiOut;

    public AudioEngine(UIHost host) => _host = host;

    public PluginRack Rack { get; } = new();

    public AudioDriver Driver { get; private set; } = new NullAudioDriver();

    /// <summary>Name of the open MIDI input port (null = none).</summary>
    public string? MidiInputName => _midi?.Name;

    /// <summary>The open MIDI output port (null = none); plug-ins can send their MIDI to it.</summary>
    public MidiOutput? MidiOutputPort => _midiOut;

    /// <summary>The open MIDI output port changed (UI thread).</summary>
    public event Action? MidiOutputChanged;

    /// <summary>A message for the status bar (device failed, MIDI port busy…), raised on the UI thread.</summary>
    public event Action<string>? Message;

    /// <summary>The device or its format changed (raised on the UI thread).</summary>
    public event Action? Changed;

    /// <summary>Opens the devices of the settings, or the clock without device when they cannot be opened.</summary>
    public void Start(GuiSettings settings)
    {
        Driver.Dispose();
        AudioDriver driver;
        if (settings.AudioOutput == "none")
        {
            driver = new NullAudioDriver();
            driver.Start(Rack);
        }
        else
        {
            try
            {
                driver = new WasapiDriver(settings.AudioOutput, settings.AudioInput, settings.BufferFrames);
                driver.Start(Rack);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException)
            {
                Message?.Invoke(L.T("The audio device could not be opened: ", "Le périphérique audio n'a pas pu être ouvert : ") + ex.Message);
                driver = new NullAudioDriver();
                driver.Start(Rack);
            }
        }
        driver.Failed += (d, message) => _host.Post(() =>
        {
            if (!ReferenceEquals(d, Driver)) return;
            Message?.Invoke(L.T("The audio device stopped: ", "Le périphérique audio s'est arrêté : ") + message);
            Driver.Dispose();
            Driver = new NullAudioDriver();
            Driver.Start(Rack);
            Changed?.Invoke();
        });
        Driver = driver;
        OpenMidi(settings.MidiInput);
        OpenMidiOutput(settings.MidiOutput);
        Changed?.Invoke();
    }

    private void OpenMidi(string? name)
    {
        _midi?.Dispose();
        _midi = null;
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var port = MidiInput.Ports.FirstOrDefault(p => p.Name == name);
            if (port is null)
            {
                Message?.Invoke(L.T($"The MIDI port '{name}' is not connected.", $"Le port MIDI « {name} » n'est pas branché."));
                return;
            }
            _midi = MidiInput.Open(port, Route);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Message?.Invoke(ex.Message);
        }
    }

    private void OpenMidiOutput(string? name)
    {
        if (_midiOut?.Name == name && name is not null) return;
        var previous = _midiOut;
        _midiOut = null;
        MidiOutputChanged?.Invoke(); // the windows stop sending to the old port first
        previous?.Dispose();
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var port = MidiOutput.Ports.FirstOrDefault(p => p.Name == name);
            if (port is null)
            {
                Message?.Invoke(L.T($"The MIDI port '{name}' is not connected.", $"Le port MIDI « {name} » n'est pas branché."));
                return;
            }
            _midiOut = MidiOutput.Open(port);
            MidiOutputChanged?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Message?.Invoke(ex.Message);
        }
    }

    /// <summary>Sends to every open plug-in with a MIDI input except one (audio thread, no allocation).</summary>
    public sealed class OtherPluginsSink(PluginRack rack, RackSlot except) : IMidiSink
    {
        public void Send(in MidiEvent e)
        {
            foreach (var slot in rack.Slots)
            {
                if (!ReferenceEquals(slot, except) && slot.Plugin.EventInputCount > 0) slot.SendMidi(e);
            }
        }
    }

    /// <summary>MIDI driver thread: the message goes to every plug-in with a MIDI input.</summary>
    private void Route(MidiEvent e)
    {
        foreach (var slot in Rack.Slots)
        {
            if (slot.Plugin.EventInputCount > 0) slot.SendMidi(e);
        }
        MidiReceived?.Invoke(e);
    }

    /// <summary>A MIDI message arrived (MIDI driver thread).</summary>
    public event Action<MidiEvent>? MidiReceived;

    /// <summary>"WASAPI — Speakers · 48 kHz · 144 samples (3.0 ms)".</summary>
    public string Describe()
    {
        var d = Driver;
        if (d is NullAudioDriver) return L.T("No audio device", "Aucun périphérique audio");
        double ms = d.BlockSize * 1000.0 / d.SampleRate;
        string text = $"{d.Name} · {(d.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} kHz · "
            + L.T($"{d.BlockSize} samples ({ms.ToString("0.0", CultureInfo.InvariantCulture)} ms)", $"{d.BlockSize} échantillons ({ms.ToString("0.0", CultureInfo.InvariantCulture)} ms)");
        if (d is WasapiDriver { InputName: { } input }) text += L.T(" · input ", " · entrée ") + input;
        return text;
    }

    public void Dispose()
    {
        _midi?.Dispose();
        _midi = null;
        _midiOut?.Dispose();
        _midiOut = null;
        Driver.Dispose();
    }
}
