// 2026/11/05 15:08:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Desktop;

namespace PluginUI.DesktopDemo;

/// <summary>
/// Main window of Biquad Designer: menu bar, frequency response with draggable nodes, one column of controls per
/// section, coefficients panel. Every edit is undoable; the document is saved as .bqd (JSON).
/// </summary>
internal sealed class DesignerWindow
{
    public const string FileExtension = "bqd";

    private readonly DesktopApplication _app;
    private readonly AppSettings<DemoSettings> _settings;
    private readonly FilterDocument _doc = new();
    private readonly FrequencyResponseView _graph;
    private readonly ResponseCurve _phase;
    private readonly List<ResponseCurve> _bandCurves = new();
    private readonly Label _coefficients;
    private readonly MenuItem _recentMenu;

    public DesignerWindow(DesktopApplication app, AppSettings<DemoSettings> settings)
    {
        _app = app;
        _settings = settings;
        var t = UIStrings.ForLanguage(UIHost.DefaultLanguage);

        // ---------------------------------------------------------------- graph
        _graph = new FrequencyResponseView
        {
            SampleRate = _doc.Fs,
            MinFrequency = 10.0,
            MaxFrequency = 22000.0,
            LeftMin = -24.0, LeftMax = 24.0, LeftStep = 6.0,
            RightAxisColor = Color.FromRgb(0xFFB300),
            RightAxisTicks = RightAxisTicks.PiMultiples,
            RightUnit = "rad",
            RightAxisCentered = true,
            ContentVersion = () => _doc.Version,
            Margin = new Thickness(10f, 10f, 6f, 6f),
        };
        foreach (var band in _doc.Bands)
        {
            var b = band;
            var curve = new ResponseCurve(b.Enabled.Name, b.Color, f => FilterDocument.Db(_doc.Response(b, f))) { Thickness = 1.1f, FillOpacity = 0.07f };
            _bandCurves.Add(curve);
            _graph.Curves.Add(curve);
        }
        _graph.Curves.Add(new ResponseCurve(L.T("Cascade", "Cascade"), Color.FromRgb(0xF5F5F5), f => FilterDocument.Db(_doc.CascadeResponse(f))) { Thickness = 2.2f });
        _phase = new ResponseCurve(L.T("Phase", "Phase"), Color.FromRgb(0xFFB300), _doc.CascadePhase, ResponseAxis.Right) { Thickness = 1.2f, Stroke = StrokeStyle.Dashed };
        _graph.Curves.Add(_phase);
        foreach (var band in _doc.Bands)
        {
            _graph.Nodes.Add(new ResponseNode
            {
                Text = (band.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Color = band.Color,
                FrequencyParameter = band.Frequency,
                ValueParameter = band.Gain,
                WheelParameter = band.Q,
                IsValueEditable = true,
                Tag = band,
            });
        }

        // ---------------------------------------------------------------- band columns
        var columns = new Grid("Auto", "*,*,*,*") { ColumnSpacing = 10f, Padding = new Thickness(10f, 6f, 10f, 10f), Dock = Side.Bottom };
        foreach (var band in _doc.Bands) columns.Add(BuildBandColumn(band), 0, band.Index);

        // ---------------------------------------------------------------- coefficients panel
        _coefficients = new Label
        {
            TextSource = _doc.CoefficientsText,
            Wrap = true,
            TextStyle = new TextStyle("Consolas", 11.5f, VerticalAlign: VerticalAlignment.Top, Ellipsis: false),
            VerticalTextAlignment = VerticalAlignment.Top,
            Padding = new Thickness(8f),
            CornerRadius = 4f,
        };
        var copy = new Button(L.T("Copy coefficients", "Copier les coefficients"), CopyCoefficients) { Height = 28f, Dock = Side.Bottom, Margin = new Thickness(0f, 8f, 0f, 0f) };
        var rate = new DropDown { Parameter = _doc.SampleRate, Label = L.T("SAMPLE RATE", "ÉCHANTILLONNAGE"), Dock = Side.Top, Height = 46f, Margin = new Thickness(0f, 0f, 0f, 8f) };
        var side = new DockPanel { Width = 270f, Dock = Side.Right, Padding = new Thickness(6f, 10f, 10f, 6f) };
        side.Children.Add(rate);
        side.Children.Add(copy);
        side.Children.Add(_coefficients);

        // ---------------------------------------------------------------- commands and menus
        var fileMenu = new MenuItem(t.MenuFile);
        var editMenu = new MenuItem(t.MenuEdit);
        var viewMenu = new MenuItem(t.MenuView);
        var helpMenu = new MenuItem(t.MenuHelp);
        MenuBar = new MenuBar(fileMenu, editMenu, viewMenu, helpMenu) { Dock = Side.Top };
        ToolBar = new ToolBar { Dock = Side.Top };
        StatusBar = new StatusBar { Dock = Side.Bottom };

        var root = new DockPanel { AllowFileDrop = true };
        root.Children.Add(MenuBar);
        root.Children.Add(ToolBar);
        root.Children.Add(StatusBar);
        root.Children.Add(columns);
        root.Children.Add(side);
        root.Children.Add(_graph);

        Host = new UIHost(root, new Size(1100f, 770f), SizingMode.Relayout) { MinSize = new Size(820f, 600f) };
        if (settings.Value.LightTheme) Host.Theme = Theme.Light;
        Window = new StandaloneWindow(Host, app.Name);

        Session = new DocumentSession(Window, app, [new FileFilter(L.T("Biquad design", "Conception biquad"), "*." + FileExtension)], FileExtension)
        {
            Reset = _doc.Reset,
            Read = _doc.Load,
            Write = _doc.Save,
        };
        foreach (var p in _doc.AllParameters) Session.Undo.Track(p);
        Session.Undo.Changed += _ => _coefficients.InvalidateVisual();

        _recentMenu = new MenuItem(t.RecentFiles);
        _recentMenu.Items.Add(new MenuItem(t.NoRecentFiles) { IsEnabled = false });
        fileMenu.Items.Add(new MenuItem(Session.NewCommand));
        fileMenu.Items.Add(new MenuItem(Session.OpenCommand));
        fileMenu.Items.Add(_recentMenu);
        fileMenu.Items.Add(MenuItem.Separator());
        fileMenu.Items.Add(new MenuItem(Session.SaveCommand));
        fileMenu.Items.Add(new MenuItem(Session.SaveAsCommand));
        fileMenu.Items.Add(MenuItem.Separator());
        fileMenu.Items.Add(new MenuItem(app.ExitCommand));

        var copyCommand = new Command(L.T("&Copy coefficients", "&Copier les coefficients"), CopyCoefficients, KeyGesture.Parse("Ctrl+Shift+C"))
        {
            Description = L.T("Copies b0 b1 b2 a1 a2 of the enabled sections", "Copie b0 b1 b2 a1 a2 des sections actives"),
        };
        var resetBand = new Command(L.T("&Disable all bands", "&Désactiver toutes les bandes"), DisableAll)
        {
            CanExecuteWhen = () => _doc.Bands.Any(b => b.IsEnabled),
            Description = L.T("Turns every section off (undoable)", "Désactive toutes les sections (annulable)"),
        };
        editMenu.Items.Add(new MenuItem(Session.Undo.UndoCommand));
        editMenu.Items.Add(new MenuItem(Session.Undo.RedoCommand));
        editMenu.Items.Add(MenuItem.Separator());
        editMenu.Items.Add(new MenuItem(copyCommand));
        editMenu.Items.Add(new MenuItem(resetBand));

        var phase = new Command(L.T("&Phase curve", "Courbe de &phase"), () => SetShowPhase(!_settings.Value.ShowPhase), KeyGesture.Parse("Ctrl+P"))
        {
            IsCheckedWhen = () => _settings.Value.ShowPhase,
            Description = L.T("Shows the phase of the cascade on the right axis", "Affiche la phase de la cascade sur l'axe de droite"),
        };
        var bands = new Command(L.T("&Section curves", "Courbes des &sections"), () => SetShowBands(!_settings.Value.ShowBands), KeyGesture.Parse("Ctrl+B"))
        {
            IsCheckedWhen = () => _settings.Value.ShowBands,
            Description = L.T("Shows the response of each section", "Affiche la réponse de chaque section"),
        };
        var light = new Command(L.T("&Light theme", "Thème &clair"), ToggleTheme, KeyGesture.Parse("Ctrl+T"))
        {
            IsCheckedWhen = () => _settings.Value.LightTheme,
            Description = L.T("Switches between the dark and the light theme", "Bascule entre le thème sombre et le thème clair"),
        };
        viewMenu.Items.Add(new MenuItem(phase));
        viewMenu.Items.Add(new MenuItem(bands));
        viewMenu.Items.Add(MenuItem.Separator());
        viewMenu.Items.Add(new MenuItem(light));
        var language = new MenuItem(L.T("L&anguage", "L&angue"));
        language.Items.Add(new MenuItem(L.T("&Automatic (Windows)", "&Automatique (Windows)"), () => SetLanguage("auto")) { Tag = "auto" });
        language.Items.Add(new MenuItem("&English", () => SetLanguage("en")) { Tag = "en" });
        language.Items.Add(new MenuItem("&Français", () => SetLanguage("fr")) { Tag = "fr" });
        viewMenu.Items.Add(language);

        var about = new Command(t.FormatAbout(app.Name), ShowAbout, KeyGesture.Parse("F1")) { Description = L.T("Version and credits", "Version et crédits") };
        helpMenu.Items.Add(new MenuItem(about));

        // Tool bar: the same commands as the menus, so both are always in step (greyed out, checked).
        ToolBar.Add(Session.NewCommand, ToolGlyph.New);
        ToolBar.Add(Session.OpenCommand, ToolGlyph.Open);
        ToolBar.Add(Session.SaveCommand, ToolGlyph.Save);
        ToolBar.AddSeparator();
        ToolBar.Add(Session.Undo.UndoCommand, ToolGlyph.Undo);
        ToolBar.Add(Session.Undo.RedoCommand, ToolGlyph.Redo);
        ToolBar.AddSeparator();
        ToolBar.Add(copyCommand, ToolGlyph.Copy);
        ToolBar.AddSeparator();
        ToolBar.Add(phase, ToolGlyph.Wave);
        ToolBar.Add(bands, ToolGlyph.Grid);
        ToolBar.AddSeparator();
        ToolBar.Add(about, ToolGlyph.Help);

        // Status bar: hints of the menus and buttons, then the design summary and the sample rate.
        StatusBar.Segments.Add(new StatusBarSegment(() => Host.Strings.StatusReady, float.PositiveInfinity));
        StatusBar.Segments.Add(new StatusBarSegment(SectionsSummary, 150f));
        StatusBar.Segments.Add(new StatusBarSegment(() => (_doc.Fs / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz", 80f) { Align = HorizontalAlignment.Right });
        StatusBar.Follow(MenuBar);
        StatusBar.Follow(ToolBar);
        Session.Saved += (_, path) => StatusBar.ShowMessage(Host.Strings.FormatStatusSaved(Path.GetFileName(path)));
        Session.Opened += (_, path) => StatusBar.ShowMessage(Host.Strings.FormatStatusOpened(Path.GetFileName(path)));

        MenuBar.Opening += (_, top) =>
        {
            if (ReferenceEquals(top, fileMenu)) app.RecentFiles.FillMenu(_recentMenu, path => Session.OpenFile(path), Host.Strings);
            foreach (var item in language.Items) item.IsChecked = Equals(item.Tag, _settings.Value.Language);
        };

        // Files dropped on the window open them.
        Host.FilesDropped += (_, e) =>
        {
            if (e.Files.FirstOrDefault(f => f.EndsWith("." + FileExtension, StringComparison.OrdinalIgnoreCase)) is { } path)
            {
                e.Handled = true;
                Host.Post(() => Session.OpenFile(path));
            }
        };
        _doc.SampleRate.Changed += _ => _graph.SampleRate = _doc.Fs;
        foreach (var band in _doc.Bands) band.Enabled.Changed += _ => UpdateVisibility();
        Window.Closed += _ => SaveSettings();
        ApplyView();
        UpdateVisibility();
    }

    public UIHost Host { get; }

    public StandaloneWindow Window { get; }

    public MenuBar MenuBar { get; }

    public ToolBar ToolBar { get; }

    public StatusBar StatusBar { get; }

    public DocumentSession Session { get; }

    public FilterDocument Document => _doc;

    private UIElement BuildBandColumn(Band band)
    {
        var column = new DockPanel { LastChildFill = false };
        var header = new Grid("26", "*,Auto") { ColumnSpacing = 6f, Dock = Side.Top };
        header.Add(new Label(L.T("SECTION ", "SECTION ") + (band.Index + 1)) { Foreground = band.Color, TextStyle = new TextStyle(Size: 12f, Weight: FontWeight.SemiBold) }, 0, 0);
        header.Add(new ToggleButton { Parameter = band.Enabled, Label = string.Empty, OnText = L.T("On", "Actif"), OffText = L.T("Off", "Inactif"), OnColor = band.Color, Width = 70f, Height = 24f }, 0, 1);
        column.Children.Add(header);
        column.Children.Add(new DropDown { Parameter = band.Type, Label = string.Empty, Height = 26f, Dock = Side.Top, Margin = new Thickness(0f, 6f, 0f, 6f) });
        var knobs = new Grid("88", "*,*,*") { ColumnSpacing = 4f, Dock = Side.Top };
        knobs.Add(new Knob(band.Frequency) { Label = L.T("FREQ", "FRÉQ"), Formatter = n => FormatHz(band.Frequency.ToPlain(n)) }, 0, 0);
        var gain = new Knob(band.Gain) { Label = "GAIN", Formatter = n => InvariantNumber.Format(band.Gain.ToPlain(n), "+0.0;-0.0;0.0") + " dB" };
        knobs.Add(gain, 0, 1);
        knobs.Add(new Knob(band.Q) { Label = "Q", Formatter = n => InvariantNumber.Format(band.Q.ToPlain(n), 2) }, 0, 2);
        column.Children.Add(knobs);
        band.Type.Changed += _ => gain.IsEnabled = band.HasGain;
        gain.IsEnabled = band.HasGain;
        return column;
    }

    private static string FormatHz(double hz) => hz >= 1000.0 ? InvariantNumber.Format(hz / 1000.0, hz >= 10000.0 ? 1 : 2) + " kHz" : InvariantNumber.Format(hz, 0) + " Hz";

    private void UpdateVisibility()
    {
        for (int i = 0; i < _doc.Bands.Count; i++)
        {
            bool on = _doc.Bands[i].IsEnabled;
            _graph.Nodes[i].IsVisible = on;
            _bandCurves[i].IsVisible = on && _settings.Value.ShowBands;
        }
        _graph.InvalidateVisual();
    }

    private void ApplyView()
    {
        _phase.IsVisible = _settings.Value.ShowPhase;
        _graph.RightUnit = _settings.Value.ShowPhase ? "rad" : "";
        UpdateVisibility();
    }

    private void SetShowPhase(bool show)
    {
        _settings.Value.ShowPhase = show;
        ApplyView();
    }

    private void SetShowBands(bool show)
    {
        _settings.Value.ShowBands = show;
        ApplyView();
    }

    private void ToggleTheme()
    {
        _settings.Value.LightTheme = !_settings.Value.LightTheme;
        Host.Theme = _settings.Value.LightTheme ? Theme.Light : Theme.Dark;
        Window.UpdateTitleBarTheme();
    }

    private void SetLanguage(string code)
    {
        if (_settings.Value.Language == code) return;
        _settings.Value.Language = code;
        SaveSettings();
        MessageDialog.Show(Window, _app.Name, L.T("The new language applies the next time Biquad Designer starts.", "La nouvelle langue s'appliquera au prochain démarrage de Biquad Designer."));
    }

    private void DisableAll()
    {
        var before = _doc.Bands.Select(b => b.Enabled.SelectedIndex).ToArray();
        Session.Undo.Do(L.T("Disable all bands", "Désactiver toutes les bandes"),
            redo: () => { foreach (var b in _doc.Bands) b.Enabled.SelectedIndex = 0; },
            undo: () => { for (int i = 0; i < before.Length; i++) _doc.Bands[i].Enabled.SelectedIndex = before[i]; });
    }

    private void CopyCoefficients()
    {
        if (Clipboard.SetText(_doc.CoefficientsText())) StatusBar.ShowMessage(L.T("Coefficients copied to the clipboard", "Coefficients copiés dans le presse-papiers"));
    }

    private string SectionsSummary()
    {
        int on = _doc.Bands.Count(b => b.IsEnabled);
        return L.T($"{on} of {_doc.Bands.Count} sections on", $"{on} section(s) active(s) sur {_doc.Bands.Count}");
    }

    private void ShowAbout()
    {
        var about = new AboutBox(_app.Name, typeof(DesignerWindow).Assembly.GetName().Version?.ToString(3))
        {
            Text = L.T("Cascade of RBJ biquads: drag the points, wheel = Q. A PluginUI.Desktop demonstration, compiled as IL or NativeAOT.",
                       "Cascade de biquads RBJ : glisser les points, molette = Q. Démonstration de PluginUI.Desktop, compilée en IL ou NativeAOT."),
            Url = "https://github.com/asm8086/PluginUI",
        };
        Host.ShowPopup(about);
    }

    private void SaveSettings() => _settings.Save();
}
