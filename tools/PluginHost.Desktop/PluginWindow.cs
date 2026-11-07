// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Desktop;
using PluginUI.Host.Catalog;
using PluginUI.Host.Vst3;

namespace PluginUI.Host.Desktop;

/// <summary>
/// Window of one open plug-in: its own editor (embedded with <see cref="NativeWindowHost"/>) or the generic parameter
/// table, presets, latency. The plug-in is processed in real time by the <see cref="ProcessingPump"/> while the window
/// is open.
/// </summary>
internal sealed class PluginWindow : ITickable
{
    private const float BarHeight = 40f;

    private readonly PluginInstance _plugin;
    private readonly ProcessingPump _pump;
    private readonly Vst3Editor? _editor;
    private readonly NativeWindowHost? _native;
    private readonly DataTable _parameters;
    private readonly ToggleButton? _generic;
    private readonly Label _info;
    private bool _closed;
    private bool _fitting;

    public PluginWindow(PluginDescription description, PluginInstance plugin, ProcessingPump pump, StandaloneWindow owner, bool lightTheme)
    {
        Description = description;
        _plugin = plugin;
        _pump = pump;

        _editor = (plugin as Vst3Plugin)?.CreateEditor();
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
            new Button(L.T("Load preset…", "Charger un preset…"), LoadPreset) { Height = 26f, ToolTip = L.T("Loads a .vstpreset file", "Charge un fichier .vstpreset") },
            new Button(L.T("Save preset…", "Enregistrer le preset…"), SavePreset) { Height = 26f, ToolTip = L.T("Saves the state as a .vstpreset file", "Enregistre l'état dans un fichier .vstpreset") });
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

        var root = new DockPanel();
        root.Children.Add(bar);
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
        var design = w0 > 0 && h0 > 0 ? new Size(w0, h0 + BarHeight) : new Size(640f, 520f);
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
        plugin.Prepare(ProcessingPump.SampleRate, ProcessingPump.BlockSize, realtime: true);
        pump.Add(plugin);
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

    private string InfoText()
    {
        string latency = _plugin.LatencySamples == 0 ? string.Empty
            : L.T($"latency {_plugin.LatencySamples} samples · ", $"latence {_plugin.LatencySamples} échantillons · ");
        return latency + L.T($"{_plugin.InputChannelCount} in, {_plugin.OutputChannelCount} out · 48 kHz, no audio device",
            $"{_plugin.InputChannelCount} entrée(s), {_plugin.OutputChannelCount} sortie(s) · 48 kHz, sans périphérique audio");
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
            if (_editor is { CanResize: false }) Host.MinSize = new Size(width / scale, height / scale + BarHeight);
            if (_parameters.IsVisible) return true; // the table is shown: the editor gets its size when it comes back
            return Host.RequestResize(width, (int)MathF.Round(height + BarHeight * scale));
        }
        finally
        {
            _fitting = false;
        }
    }

    private void OnAreaResized(NativeWindowHost native, int width, int height)
    {
        if (_editor is null || _fitting || !_editor.CanResize) return;
        var (w, h) = _editor.ConstrainSize(width, height);
        _editor.SetSize(w, h);
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

    private static readonly FileFilter[] PresetFilter = [new(L.T("VST3 preset", "Preset VST3"), "*.vstpreset")];

    private void LoadPreset()
    {
        if (FileDialogs.OpenFile(Host, L.T("Load a preset", "Charger un preset"), PresetFilter) is not { } path) return;
        try
        {
            _plugin.SetState(File.ReadAllBytes(path));
            _parameters.InvalidateVisual();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or Vst3Exception or InvalidDataException or ArgumentException)
        {
            MessageDialog.Show(Window, Window.Title, L.T("The preset could not be loaded:\n", "Le preset n'a pas pu être chargé :\n") + ex.Message);
        }
    }

    private void SavePreset()
    {
        if (FileDialogs.SaveFile(Host, L.T("Save the preset", "Enregistrer le preset"), PresetFilter, Description.Name + ".vstpreset", "vstpreset") is not { } path) return;
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
        return true;
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _pump.Remove(_plugin);
        _editor?.Dispose();
        _plugin.Dispose();
    }
}
