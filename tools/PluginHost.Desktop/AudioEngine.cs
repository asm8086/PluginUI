// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Host.Audio;
using PluginUI.Host.Graph;
using PluginUI.Host.Windows;

namespace PluginUI.Host.Desktop;

/// <summary>
/// The audio of the host: one <see cref="PluginGraph"/> played by the chosen device (ASIO, WASAPI, or a clock without
/// device when none is available), the MIDI input port, whose messages go to the graph's MIDI input modules, and the
/// MIDI output port, which receives what reaches its MIDI output modules.
/// </summary>
internal sealed class AudioEngine : IDisposable
{
    private readonly UIHost _host;
    private MidiInput? _midi;
    private MidiOutput? _midiOut;
    private GuiSettings? _settings;

    public AudioEngine(UIHost host) => _host = host;

    public PluginGraph Graph { get; } = new();

    public AudioDriver Driver { get; private set; } = new NullAudioDriver();

    /// <summary>The main window, owner of the ASIO driver's dialogs.</summary>
    public Func<nint>? WindowHandle { get; set; }

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
        _settings = settings;
        StartDriver(settings);
        OpenMidi(settings.MidiInput);
        OpenMidiOutput(settings.MidiOutput);
        Changed?.Invoke();
    }

    private void StartDriver(GuiSettings settings)
    {
        Driver.Dispose();
        AudioDriver? driver = null;
        try
        {
            switch (settings.Api)
            {
                case "asio":
                    string? name = settings.AsioDriver ?? Asio.GetDrivers().FirstOrDefault()?.Name;
                    if (name is null) throw new InvalidOperationException(L.T("no ASIO driver is installed", "aucun pilote ASIO n'est installé"));
                    driver = new AsioDriver(name, settings.BufferFrames, settings.SampleRate, settings.AsioInputs ? -1 : 0)
                    {
                        WindowHandle = WindowHandle?.Invoke() ?? 0,
                    };
                    break;
                case "wasapi":
                    driver = new WasapiDriver(settings.AudioOutput == "none" ? null : settings.AudioOutput, settings.AudioInput, settings.BufferFrames);
                    break;
                default:
                    driver = new NullAudioDriver();
                    break;
            }
            driver.Start(Graph);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            driver?.Dispose();
            Message?.Invoke(L.T("The audio device could not be opened: ", "Le périphérique audio n'a pas pu être ouvert : ") + ex.Message);
            driver = new NullAudioDriver();
            driver.Start(Graph);
        }
        driver.ResetRequested += d => _host.Post(() =>
        {
            // The ASIO control panel changed the buffer or the sample rate: start again with them.
            if (!ReferenceEquals(d, Driver) || _settings is null) return;
            StartDriver(_settings);
            Message?.Invoke(L.T("The audio device was restarted: ", "Le périphérique audio a été redémarré : ") + Describe());
            Changed?.Invoke();
        });
        driver.Failed += (d, message) => _host.Post(() =>
        {
            if (!ReferenceEquals(d, Driver)) return;
            Message?.Invoke(L.T("The audio device stopped: ", "Le périphérique audio s'est arrêté : ") + message);
            Driver.Dispose();
            Driver = new NullAudioDriver();
            Driver.Start(Graph);
            Changed?.Invoke();
        });
        Driver = driver;
    }

    /// <summary>Opens the control panel of the running ASIO driver; false when ASIO does not run.</summary>
    public bool ShowAsioControlPanel(string? registeredName)
    {
        if (Driver is not AsioDriver asio || (registeredName is not null && !string.Equals(asio.RegisteredName, registeredName, StringComparison.OrdinalIgnoreCase))) return false;
        try
        {
            asio.ShowControlPanel();
        }
        catch (InvalidOperationException ex)
        {
            Message?.Invoke(ex.Message);
        }
        return true;
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
        Graph.MidiOutput = null; // the graph stops sending to the old port first
        MidiOutputChanged?.Invoke();
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
            Graph.MidiOutput = _midiOut;
            MidiOutputChanged?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Message?.Invoke(ex.Message);
        }
    }

    /// <summary>MIDI driver thread: the message goes to the graph's MIDI input modules.</summary>
    private void Route(MidiEvent e)
    {
        Graph.PostMidiInput(e);
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
        if (d is AsioDriver { InputChannels: > 0 } asio) text += L.T($" · {asio.InputChannels} inputs", $" · {asio.InputChannels} entrées");
        return text;
    }

    public void Dispose()
    {
        _midi?.Dispose();
        _midi = null;
        Graph.MidiOutput = null;
        _midiOut?.Dispose();
        _midiOut = null;
        Driver.Dispose();
        Graph.Dispose();
    }
}
