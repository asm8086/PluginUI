// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Desktop;
using PluginUI.Host.Audio;
using PluginUI.Host.Catalog;
using PluginUI.Host.Vst3;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Host.Desktop;

/// <summary>
/// Window of one open plug-in: its own editor (embedded with <see cref="NativeWindowHost"/>) or the generic parameter
/// table, presets, its signal source (audio input, test signal, file) or its keyboard, bypass, mute and output meter. The
/// plug-in plays in the <see cref="AudioEngine"/>'s rack while the window is open.
/// </summary>
internal sealed class PluginWindow : ITickable
{
    private const float BarHeight = 40f, TransportHeight = 40f, KeyboardHeight = 76f;

    private readonly PluginInstance _plugin;
    private readonly AudioEngine _engine;
    private readonly RackSlot _slot;
    private readonly LevelMeter _meter;
    private readonly float[] _levels;
    private readonly PianoKeyboard? _keyboard;
    private readonly TestSignalGenerator _generator = new();
    private readonly DeviceInputSource _deviceInput = new();
    private FileSource? _file;
    private readonly PluginEditor? _editor;
    private readonly NativeWindowHost? _native;
    private readonly DataTable _parameters;
    private readonly ToggleButton? _generic;
    private readonly Label _info;
    private bool _closed;
    private bool _fitting;

    public PluginWindow(PluginDescription description, PluginInstance plugin, AudioEngine engine, StandaloneWindow owner, bool lightTheme)
    {
        Description = description;
        _plugin = plugin;
        _engine = engine;

        _editor = plugin.CreateEditor();
        if (_editor is not null && !_editor.IsPlatformSupported("HWND"))
        {
            _editor.Dispose();
            _editor = null;
        }

        // ---------------------------------------------------------------- generic parameter table
        _parameters = new DataTable { EmptyText = L.T("This plug-in has no parameters.", "Ce plug-in n'a aucun paramètre."), IsVisible = _editor is null };
        _parameters.AddColumn(new DataColumn(L.T("Parameter", "Paramètre"), o => ((PluginParameter)o).Title) { Star = 2f }.SortByText());
        _parameters.AddColumn(new DataColumn(L.T("Value", "Valeur"), o => plugin.FormatParameter(((PluginParameter)o).Id, plugin.GetParameter(((PluginParameter)o).Id)), float.NaN)
        {
            CommitEdit = (o, text) => SetFromText((PluginParameter)o, text),
        });
        _parameters.AddColumn(new DataColumn(L.T("Normalized", "Normalisé"), o => plugin.GetParameter(((PluginParameter)o).Id).ToString("0.000", CultureInfo.InvariantCulture), 96f)
        {
            Alignment = HorizontalAlignment.Right,
            CommitEdit = (o, text) =>
            {
                if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    plugin.SetParameter(((PluginParameter)o).Id, Math.Clamp(v, 0.0, 1.0));
            },
        });
        _parameters.AddColumn(new DataColumn(L.T("Default", "Défaut"), o => plugin.FormatParameter(((PluginParameter)o).Id, ((PluginParameter)o).DefaultValue), 110f));
        _parameters.AddColumn(new DataColumn(L.T("Steps", "Pas"), o => ((PluginParameter)o).StepCount == 0 ? "–" : (((PluginParameter)o).StepCount + 1).ToString(CultureInfo.InvariantCulture), 56f)
        {
            Alignment = HorizontalAlignment.Right,
        });
        _parameters.AddColumn(new DataColumn("Id", o => ((PluginParameter)o).Id.ToString(CultureInfo.InvariantCulture), 80f) { Alignment = HorizontalAlignment.Right, IsVisible = false });
        _parameters.Margin = new Thickness(8f, 0f, 8f, 8f);
        ReadParameters();

        // ---------------------------------------------------------------- top bar
        _info = new Label { TextSource = InfoText, IsDim = true, VerticalAlignment = VerticalAlignment.Center, TextAlignment = HorizontalAlignment.Right };
        var bar = new DockPanel { Dock = Side.Top, Height = BarHeight, Padding = new Thickness(8f, 7f, 8f, 7f) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, Dock = Side.Left };
        buttons.Add(
            new Button(L.T("Load preset…", "Charger un preset…"), LoadPreset) { Height = 26f, ToolTip = L.T($"Loads a {PresetKinds} file", $"Charge un fichier {PresetKinds}") },
            new Button(L.T("Save preset…", "Enregistrer le preset…"), SavePreset) { Height = 26f, ToolTip = L.T($"Saves the state as a {plugin.PresetExtension} file", $"Enregistre l'état dans un fichier {plugin.PresetExtension}") });
        if (_editor is not null)
        {
            _generic = new ToggleButton(L.T("Parameters", "Paramètres"))
            {
                Height = 26f, Width = 110f, ToolTip = L.T("Shows the generic parameter table instead of the plug-in's editor", "Affiche la table générique des paramètres au lieu de l'éditeur du plug-in"),
            };
            _generic.ValueChanged += (_, _) => ShowGeneric(_generic.IsOn);
            buttons.Add(_generic);
        }
        bar.Children.Add(buttons);
        bar.Children.Add(_info);

        // ---------------------------------------------------------------- transport: source or keyboard, bypass, mute, meter
        int mainInputs = plugin.InputBuses.Where(b => !b.IsAuxiliary).Sum(b => b.ChannelCount);
        int mainOutputs = plugin.OutputBuses.Where(b => !b.IsAuxiliary).Sum(b => b.ChannelCount);
        _levels = new float[Math.Clamp(mainOutputs, 1, 16)];
        _meter = new LevelMeter
        {
            Orientation = Orientation.Horizontal, ChannelCount = _levels.Length, Width = 170f, Height = Math.Min(26f, 6f * _levels.Length + 4f),
            ShowScale = false, Dock = Side.Right, VerticalAlignment = VerticalAlignment.Center, MinDb = -60f,
        };
        var transport = new DockPanel { Dock = Side.Bottom, Height = TransportHeight, Padding = new Thickness(8f, 7f, 8f, 7f) };
        transport.Children.Add(_meter);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f };
        var bypass = new ToggleButton(L.T("Bypass", "Contourner")) { Height = 26f, Width = 90f, ToolTip = L.T("The source goes straight to the output", "La source va directement à la sortie") };
        var mute = new ToggleButton(L.T("Mute", "Muet")) { Height = 26f, Width = 70f, ToolTip = L.T("Silences the output of this plug-in", "Coupe la sortie de ce plug-in") };
        if (mainInputs > 0)
        {
            left.Add(BuildSourceControls());
        }
        else if (plugin.EventInputCount > 0)
        {
            left.Add(new Label { TextSource = () => _engine.MidiInputName is { } port ? "MIDI : " + port : L.T("On-screen keyboard (or a MIDI input: File > Audio and MIDI)", "Clavier à l'écran (ou une entrée MIDI : Fichier > Audio et MIDI)"), IsDim = true, VerticalAlignment = VerticalAlignment.Center });
        }
        if (plugin.EventOutputCount > 0) left.Add(BuildMidiOutControls());
        left.Add(bypass, mute);
        transport.Children.Add(left);
        bypass.IsEnabled = mainInputs > 0;
        bypass.ValueChanged += (_, _) => _slot!.Bypass = bypass.IsOn;
        mute.ValueChanged += (_, _) => _slot!.Mute = mute.IsOn;
        if (mainInputs == 0 && plugin.EventInputCount > 0)
        {
            _keyboard = new PianoKeyboard { Dock = Side.Bottom, Height = KeyboardHeight, LowestNote = 36, HighestNote = 96, Margin = new Thickness(8f, 0f, 8f, 0f) };
            _keyboard.NoteOn += (_, note, velocity) => _slot!.SendMidi(MidiEvent.NoteOn(0, 0, note, Math.Clamp((int)MathF.Round(velocity * 127f), 1, 127)));
            _keyboard.NoteOff += (_, note) => _slot!.SendMidi(MidiEvent.NoteOff(0, 0, note));
        }

        var root = new DockPanel();
        root.Children.Add(bar);
        root.Children.Add(transport);
        if (_keyboard is not null) root.Children.Add(_keyboard);
        var content = new Grid("*", "*");
        if (_editor is not null)
        {
            bool resizable = _editor.CanResize;
            _native = new NativeWindowHost
            {
                ProvidesContainer = true,
                HideUnderPopups = true,
                PreferredPixelSize = _editor.Size,
                HorizontalAlignment = resizable ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
                VerticalAlignment = resizable ? VerticalAlignment.Stretch : VerticalAlignment.Top,
            };
            _native.ContainerCreated += AttachEditor;
            _native.ContainerDestroying += _ => _editor.Dispose();
            _native.PixelSizeChanged += OnAreaResized;
            _editor.ResizeRequested = (w, h) => FitToEditor(w, h);
            content.Add(_native, 0, 0);
        }
        content.Add(_parameters, 0, 0);
        root.Children.Add(content);

        var (w0, h0) = _editor?.Size ?? (0, 0);
        var design = w0 > 0 && h0 > 0 ? new Size(Math.Max(w0, 560f), h0 + ChromeHeight) : new Size(640f, 520f + ChromeHeight - BarHeight);
        Host = new UIHost(root, design, SizingMode.Relayout) { MinSize = new Size(Math.Min(design.Width, 420f), Math.Min(design.Height, 200f)) };
        if (lightTheme) Host.Theme = Theme.Light;
        Window = new StandaloneWindow(Host, $"{description.Name} — {description.Vendor}")
        {
            Owner = owner,
            QuitOnClose = false,
            StartPosition = WindowStartPosition.CenterOwner,
            MaximizeBox = _editor is null || _editor.CanResize,
        };
        Window.Closed += _ => Close();

        plugin.ParameterChanged += (_, _, _) => _parameters.InvalidateVisual();
        plugin.Restarted += _ =>
        {
            ReadParameters();
            _info.InvalidateVisual();
        };
        _slot = engine.Rack.Add(plugin);
        engine.MidiReceived += OnMidi;
        Host.AddTicker(this);
    }

    public PluginDescription Description { get; }

    public UIHost Host { get; }

    public StandaloneWindow Window { get; }

    public void Show() => Window.Show();

    public void SetTheme(bool light)
    {
        Host.Theme = light ? Theme.Light : Theme.Dark;
        Window.UpdateTitleBarTheme();
        _native?.Sync();
    }

    private float ChromeHeight => BarHeight + TransportHeight + (_keyboard is null ? 0f : KeyboardHeight);

    private string InfoText()
    {
        string latency = _plugin.LatencySamples == 0 ? string.Empty
            : L.T($"latency {_plugin.LatencySamples} samples · ", $"latence {_plugin.LatencySamples} échantillons · ");
        string rate = (_engine.Rack.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz";
        string device = _engine.Driver is NullAudioDriver ? L.T(", no audio device", ", sans périphérique audio") : string.Empty;
        return latency + L.T($"{_plugin.InputChannelCount} in, {_plugin.OutputChannelCount} out · {rate}{device}",
            $"{_plugin.InputChannelCount} entrée(s), {_plugin.OutputChannelCount} sortie(s) · {rate}{device}");
    }

    // ---------------------------------------------------------------- source

    private UIElement BuildSourceControls()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f };
        string[] sources = [L.T("Silence", "Silence"), L.T("Audio input", "Entrée audio"), L.T("Test signal", "Signal de test"), L.T("Audio file…", "Fichier audio…")];
        var source = new DropDown(sources) { Label = string.Empty, Width = 150f, Height = 26f, ToolTip = L.T("What feeds the plug-in's input", "Ce qui alimente l'entrée du plug-in") };

        var kinds = new DropDown(L.T("Sine", "Sinus"), L.T("Square", "Carré"), L.T("Sawtooth", "Dent de scie"), L.T("Triangle", "Triangle"),
            L.T("White noise", "Bruit blanc"), L.T("Pink noise", "Bruit rose"), L.T("Sweep 20 Hz–20 kHz", "Balayage 20 Hz–20 kHz"), L.T("Impulses (1 s)", "Impulsions (1 s)"))
        { Label = string.Empty, Width = 170f, Height = 26f };
        var frequency = new LocalParameter(L.T("Frequency", "Fréquence"), 20, 20000, 1000, "Hz", ParameterScale.Logarithmic(20, 20000), decimals: 0);
        var level = new LocalParameter(L.T("Level", "Niveau"), -60, 0, -20, "dBFS", decimals: 1);
        var frequencyBox = new NumberBox { Parameter = frequency, Width = 96f, Height = 26f, ToolTip = L.T("Frequency of tones", "Fréquence des sons") };
        var levelBox = new NumberBox { Parameter = level, Width = 96f, Height = 26f, ToolTip = L.T("Level (peak, or RMS for noises)", "Niveau (crête, ou efficace pour les bruits)") };
        frequency.Changed += _ => _generator.Frequency = frequency.Value;
        level.Changed += _ =>
        {
            _generator.LevelDb = level.Value;
            _deviceInput.GainDb = 0;
        };
        kinds.ValueChanged += (_, _) =>
        {
            _generator.Signal = (TestSignal)Math.Max(0, kinds.SelectedIndex);
            _generator.Frequency = _generator.Signal == TestSignal.Sweep ? 20 : frequency.Value;
            _generator.EndFrequency = 20000;
            frequencyBox.IsEnabled = _generator.Signal is TestSignal.Sine or TestSignal.Square or TestSignal.Sawtooth or TestSignal.Triangle;
        };
        _generator.LevelDb = level.Value;
        var generator = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, IsVisible = false };
        generator.Add(kinds, frequencyBox, levelBox);

        var fileName = new Label { IsDim = true, VerticalAlignment = VerticalAlignment.Center, Width = 180f };
        var pause = new ToggleButton(L.T("Pause", "Pause")) { Height = 26f, Width = 70f };
        pause.ValueChanged += (_, _) => { if (_file is not null) _file.IsPaused = pause.IsOn; };
        var file = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, IsVisible = false };
        file.Add(fileName, pause);

        int previous = 0;
        source.ValueChanged += (_, _) =>
        {
            int choice = source.SelectedIndex;
            if (choice == previous) return;
            if (choice == 3)
            {
                // Choose the file after the drop-down closed.
                Host.Post(() =>
                {
                    if (FileDialogs.OpenFile(Host, L.T("Play an audio file", "Lire un fichier audio"), [new(L.T("WAV files", "Fichiers WAV"), "*.wav")]) is { } path)
                    {
                        try
                        {
                            _file = FileSource.Load(path);
                            _file.IsPaused = pause.IsOn;
                            fileName.Text = _file.FileName;
                            _slot.Source = _file;
                            previous = 3;
                            generator.IsVisible = false;
                            file.IsVisible = true;
                            Host.InvalidateLayout();
                            return;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or NotSupportedException)
                        {
                            MessageDialog.Show(Window, Window.Title, L.T("The file could not be read:\n", "Le fichier n'a pas pu être lu :\n") + ex.Message);
                        }
                    }
                    source.SelectedIndex = previous;
                });
                return;
            }
            previous = choice;
            _slot.Source = choice switch { 1 => _deviceInput, 2 => _generator, _ => SilenceSource.Instance };
            generator.IsVisible = choice == 2;
            file.IsVisible = false;
            Host.InvalidateLayout();
        };
        panel.Add(source, generator, file);
        return panel;
    }

    private int _midiOutChoice;

    private UIElement BuildMidiOutControls()
    {
        var target = new DropDown(L.T("MIDI out: nowhere", "Sortie MIDI : nulle part"), L.T("MIDI out: output port", "Sortie MIDI : port de sortie"),
            L.T("MIDI out: other plug-ins", "Sortie MIDI : autres plug-ins"))
        {
            Label = string.Empty, Width = 210f, Height = 26f,
            ToolTip = L.T("Where the MIDI this plug-in produces goes (the output port is chosen in File > Audio and MIDI)",
                "Où va le MIDI produit par ce plug-in (le port de sortie se choisit dans Fichier > Audio et MIDI)"),
        };
        target.ValueChanged += (_, _) =>
        {
            _midiOutChoice = Math.Max(0, target.SelectedIndex);
            ApplyMidiOut();
            if (_midiOutChoice == 1 && _engine.MidiOutputPort is null)
                Window.Host.Announce(L.T("No MIDI output port is open: choose one in File > Audio and MIDI.", "Aucun port de sortie MIDI n'est ouvert : choisissez-en un dans Fichier > Audio et MIDI."));
        };
        _engine.MidiOutputChanged += ApplyMidiOut;
        return target;
    }

    private void ApplyMidiOut()
    {
        if (_closed) return;
        _slot.MidiOut = _midiOutChoice switch
        {
            1 => _engine.MidiOutputPort,
            2 => new AudioEngine.OtherPluginsSink(_engine.Rack, _slot),
            _ => null,
        };
    }

    private void OnMidi(MidiEvent e)
    {
        // MIDI driver thread: show the notes on the keyboard.
        if (_keyboard is null || _closed) return;
        int type = e.Status & 0xF0;
        if (type is not (0x80 or 0x90)) return;
        bool on = type == 0x90 && e.Data2 > 0;
        Host.Post(() => _keyboard.SetNoteState(e.Data1, on));
    }

    private void ReadParameters()
        => _parameters.Items = _plugin.Parameters.Where(p => (p.Flags & ParameterFlags.IsHidden) == 0).ToList();

    private void SetFromText(PluginParameter parameter, string text)
    {
        if (_plugin.TryParseParameter(parameter.Id, text, out double normalized)) _plugin.SetParameter(parameter.Id, normalized);
        else Window.Host.Announce(L.T($"'{text}' is not a value of {parameter.Title}", $"« {text} » n'est pas une valeur de {parameter.Title}"));
    }

    // ---------------------------------------------------------------- editor

    private void AttachEditor(NativeWindowHost native)
    {
        if (_editor is null) return;
        _editor.SetContentScale(Host.Scale);
        if (!_editor.Attach(native.ChildWindow))
        {
            ShowGeneric(true);
            if (_generic is not null) _generic.IsEnabled = false;
            return;
        }
        var (w, h) = _editor.Size;
        if (w > 0 && h > 0) FitToEditor(w, h);
    }

    /// <summary>Sizes the window around an editor of <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
    private bool FitToEditor(int width, int height)
    {
        if (_native is null || width <= 0 || height <= 0) return false;
        _fitting = true;
        try
        {
            _native.PreferredPixelSize = (width, height);
            float scale = Host.Scale;
            if (_editor is { CanResize: false }) Host.MinSize = new Size(width / scale, height / scale + ChromeHeight);
            if (_parameters.IsVisible) return true; // the table is shown: the editor gets its size when it comes back
            return Host.RequestResize(width, (int)MathF.Round(height + ChromeHeight * scale));
        }
        finally
        {
            _fitting = false;
        }
    }

    private void OnAreaResized(NativeWindowHost native, int width, int height)
    {
        if (_editor is null || _fitting || !_editor.CanResize) return;
        var (w, h) = FitInside(_editor, width, height);
        _editor.SetSize(w, h);
        CenterEditor(native, w, h);
    }

    /// <summary>
    /// The largest size the editor accepts inside <paramref name="width"/> × <paramref name="height"/>. Editors with a
    /// fixed aspect ratio may answer <c>checkSizeConstraint</c> with a size larger than the area (they follow the width,
    /// as when the user drags a corner); a maximized window then cuts them. The proposal is scaled down until it fits.
    /// </summary>
    internal static (int Width, int Height) FitInside(PluginEditor editor, int width, int height)
    {
        int w = width, h = height;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (cw, ch) = editor.ConstrainSize(w, h);
            if (cw <= width && ch <= height || cw <= 0 || ch <= 0) return (cw, ch);
            double k = Math.Min((double)width / cw, (double)height / ch);
            w = Math.Max(1, (int)Math.Floor(cw * k));
            h = Math.Max(1, (int)Math.Floor(ch * k));
        }
        return editor.ConstrainSize(w, h);
    }

    /// <summary>Centers the editor's window in the area when the editor is smaller (aspect ratio, maximum size).</summary>
    private static void CenterEditor(NativeWindowHost native, int width, int height)
    {
        if (native.ChildWindow == 0) return;
        var child = GetWindow((HWND)native.ChildWindow, 5 /* GW_CHILD */);
        if (child == HWND.NULL) return;
        var (areaWidth, areaHeight) = native.PixelSize;
        SetWindowPos(child, HWND.NULL, Math.Max(0, (areaWidth - width) / 2), Math.Max(0, (areaHeight - height) / 2), 0, 0,
            SWP.SWP_NOSIZE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE);
    }

    private void ShowGeneric(bool generic)
    {
        _parameters.IsVisible = generic;
        if (_native is not null) _native.IsVisible = !generic;
        if (!generic && _editor is not null)
        {
            var (w, h) = _editor.Size;
            FitToEditor(w, h);
        }
        Host.InvalidateLayout();
    }

    // ---------------------------------------------------------------- presets

    private string PresetKinds => _plugin.Format == PluginFormat.Vst2 ? ".fxp / .fxb" : ".vstpreset";

    private FileFilter[] PresetFilter => _plugin.Format == PluginFormat.Vst2
        ? [new(L.T("VST 2 preset or bank", "Preset ou banque VST 2"), "*.fxp;*.fxb")]
        : [new(L.T("VST3 preset", "Preset VST3"), "*.vstpreset")];

    private void LoadPreset()
    {
        if (FileDialogs.OpenFile(Host, L.T("Load a preset", "Charger un preset"), PresetFilter) is not { } path) return;
        try
        {
            _plugin.SetState(File.ReadAllBytes(path));
            _parameters.InvalidateVisual();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or PluginException or InvalidDataException or ArgumentException)
        {
            MessageDialog.Show(Window, Window.Title, L.T("The preset could not be loaded:\n", "Le preset n'a pas pu être chargé :\n") + ex.Message);
        }
    }

    private void SavePreset()
    {
        string extension = _plugin.PresetExtension;
        var filter = new FileFilter[] { new(L.T("Preset", "Preset") + $" ({extension})", "*" + extension) };
        if (FileDialogs.SaveFile(Host, L.T("Save the preset", "Enregistrer le preset"), filter, Description.Name + extension, extension.TrimStart('.')) is not { } path) return;
        try
        {
            File.WriteAllBytes(path, _plugin.GetState());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Show(Window, Window.Title, L.T("The preset could not be saved:\n", "Le preset n'a pas pu être enregistré :\n") + ex.Message);
        }
    }

    // ---------------------------------------------------------------- lifetime

    /// <summary>Delivers the plug-in's own parameter changes and the editor's edits (UI thread, every frame).</summary>
    bool ITickable.Tick(double elapsedSeconds)
    {
        if (_closed) return false;
        _plugin.Idle();
        _slot.Meter.Read(_levels);
        _meter.SetLevels(_levels);
        return true;
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _engine.MidiReceived -= OnMidi;
        _engine.MidiOutputChanged -= ApplyMidiOut;
        _slot.MidiOut = null;
        _engine.Rack.Remove(_slot);
        _editor?.Dispose();
        _plugin.Dispose();
    }
}
