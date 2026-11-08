// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Desktop;
using PluginUI.Host.Audio;
using PluginUI.Host.Graph;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Host.Desktop;

/// <summary>
/// Window of a plug-in module of the graph: its own editor (embedded with <see cref="NativeWindowHost"/>) or the generic
/// parameter table, presets, its keyboard (instruments), bypass, mute and output meter. The plug-in plays in the graph
/// whether the window is open or not: closing the window closes only the editor.
/// </summary>
internal sealed class PluginWindow : ITickable
{
    private const float BarHeight = 40f, TransportHeight = 40f, KeyboardHeight = 76f;

    private readonly PluginInstance _plugin;
    private readonly PluginNode _node;
    private readonly AudioEngine _engine;
    private readonly Func<float[]> _levelSource;
    private readonly LevelMeter _meter;
    private readonly PianoKeyboard? _keyboard;
    private readonly ToggleButton _fullScreen;
    private readonly ToggleButton _bypass, _mute;
    private readonly PluginEditor? _editor;
    private readonly NativeWindowHost? _native;
    private readonly DataTable _parameters;
    private readonly ToggleButton? _generic;
    private readonly Label _info;
    private bool _closed;
    private bool _fitting;

    public PluginWindow(PluginNode node, AudioEngine engine, StandaloneWindow owner, bool lightTheme, Func<float[]> levels)
    {
        _node = node;
        _plugin = node.Plugin;
        var plugin = _plugin;
        _engine = engine;
        _levelSource = levels;

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
        _fullScreen = new ToggleButton(L.T("Full screen", "Plein écran"))
        {
            Height = 26f, Width = 110f,
            ToolTip = L.T("Shows the window on the whole screen (F11; Esc or this button to leave)", "Affiche la fenêtre sur tout l'écran (F11 ; Échap ou ce bouton pour en sortir)"),
        };
        buttons.Add(_fullScreen);
        bar.Children.Add(buttons);
        bar.Children.Add(_info);

        // ---------------------------------------------------------------- transport: keyboard hint, bypass, mute, meter
        int mainOutputs = plugin.OutputBuses.Where(b => !b.IsAuxiliary).Sum(b => b.ChannelCount);
        bool playable = plugin.EventInputCount > 0 && (plugin.IsInstrument || !plugin.InputBuses.Any(b => !b.IsAuxiliary && b.ChannelCount > 0));
        _meter = new LevelMeter
        {
            Orientation = Orientation.Horizontal, ChannelCount = Math.Clamp(mainOutputs, 1, 16), Width = 170f, Height = Math.Min(26f, 6f * Math.Clamp(mainOutputs, 1, 16) + 4f),
            ShowScale = false, Dock = Side.Right, VerticalAlignment = VerticalAlignment.Center, MinDb = -60f,
        };
        var transport = new DockPanel { Dock = Side.Bottom, Height = TransportHeight, Padding = new Thickness(8f, 7f, 8f, 7f) };
        transport.Children.Add(_meter);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f };
        _bypass = new ToggleButton(L.T("Bypass", "Contourner")) { Height = 26f, Width = 90f, IsOn = node.Bypass, ToolTip = L.T("The main input goes straight to the main output", "L'entrée principale va directement à la sortie principale") };
        _mute = new ToggleButton(L.T("Mute", "Muet")) { Height = 26f, Width = 70f, IsOn = node.Mute, ToolTip = L.T("Silences the outputs of this plug-in", "Coupe les sorties de ce plug-in") };
        left.Add(_bypass, _mute);
        if (playable)
        {
            left.Add(new Label
            {
                TextSource = () => L.T("The keyboard plays this plug-in; the graph brings it its MIDI input.", "Le clavier joue ce plug-in ; le graphe lui apporte son entrée MIDI."),
                IsDim = true, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        transport.Children.Add(left);
        _bypass.ValueChanged += (_, _) =>
        {
            _node.Bypass = _bypass.IsOn;
            StateChanged?.Invoke();
        };
        _mute.ValueChanged += (_, _) =>
        {
            _node.Mute = _mute.IsOn;
            StateChanged?.Invoke();
        };
        if (playable)
        {
            _keyboard = new PianoKeyboard { Dock = Side.Bottom, Height = KeyboardHeight, LowestNote = 36, HighestNote = 96, Margin = new Thickness(8f, 0f, 8f, 0f) };
            _keyboard.NoteOn += (_, note, velocity) => _node.SendMidi(MidiEvent.NoteOn(0, 0, note, Math.Clamp((int)MathF.Round(velocity * 127f), 1, 127)));
            _keyboard.NoteOff += (_, note) => _node.SendMidi(MidiEvent.NoteOff(0, 0, note));
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
        Window = new StandaloneWindow(Host, plugin.Vendor.Length > 0 ? $"{node.DisplayTitle} — {plugin.Vendor}" : node.DisplayTitle)
        {
            Owner = owner,
            QuitOnClose = false,
            StartPosition = WindowStartPosition.CenterOwner,
            MaximizeBox = _editor is null || _editor.CanResize,
        };
        Window.Closed += _ => Close();
        _fullScreen.ValueChanged += (_, _) => Window.IsFullScreen = _fullScreen.IsOn;
        Window.FullScreenChanged += w => _fullScreen.IsOn = w.IsFullScreen;
        Host.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F11 || e.Control || e.Shift || (e.Modifiers & ModifierKeys.Alt) != 0) return;
            Window.ToggleFullScreen();
            e.Handled = true;
        };

        plugin.ParameterChanged += OnParameterChanged;
        plugin.Restarted += OnRestarted;
        engine.MidiReceived += OnMidi;
        Host.AddTicker(this);
    }

    /// <summary>The module.</summary>
    public PluginNode Node => _node;

    /// <summary>Bypass or mute changed (UI thread).</summary>
    public event Action? StateChanged;

    /// <summary>Shows the bypass and mute of the module again (changed from the graph).</summary>
    public void RefreshState()
    {
        _bypass.IsOn = _node.Bypass;
        _mute.IsOn = _node.Mute;
    }

    private void OnParameterChanged(PluginInstance plugin, uint id, double value) => _parameters.InvalidateVisual();

    private void OnRestarted(PluginInstance plugin)
    {
        ReadParameters();
        _info.InvalidateVisual();
    }

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
        string rate = (_engine.Graph.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz";
        string device = _engine.Driver is NullAudioDriver ? L.T(", no audio device", ", sans périphérique audio") : string.Empty;
        return latency + L.T($"{_plugin.InputChannelCount} in, {_plugin.OutputChannelCount} out · {rate}{device}",
            $"{_plugin.InputChannelCount} entrée(s), {_plugin.OutputChannelCount} sortie(s) · {rate}{device}");
    }

    // ---------------------------------------------------------------- MIDI

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
        if (_editor is null || _fitting) return;
        if (!_editor.CanResize)
        {
            // A fixed-size editor stays at its size, centred (maximized window, full screen).
            var (ew, eh) = _editor.Size;
            CenterEditor(native, ew, eh);
            return;
        }
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
        if (FileDialogs.SaveFile(Host, L.T("Save the preset", "Enregistrer le preset"), filter, _plugin.Name + extension, extension.TrimStart('.')) is not { } path) return;
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

    /// <summary>Shows the module's output level (UI thread, every frame; the graph view delivers the plug-ins' changes).</summary>
    bool ITickable.Tick(double elapsedSeconds)
    {
        if (_closed) return false;
        _meter.SetLevels(_levelSource());
        return true;
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _engine.MidiReceived -= OnMidi;
        _plugin.ParameterChanged -= OnParameterChanged;
        _plugin.Restarted -= OnRestarted;
        _editor?.Dispose();
    }
}
