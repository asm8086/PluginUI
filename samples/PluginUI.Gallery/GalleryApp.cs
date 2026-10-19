// 2026/10/19 22:34:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Gallery.Pages;

namespace PluginUI.Gallery;

/// <summary>Builds the gallery: tool bar, one tab per control family, status bar.</summary>
internal sealed class GalleryApp
{
    // An instance field: the names depend on the language chosen on the command line.
    private readonly (string Name, Color Color)[] _accents =
    [
        (L.T("Blue", "Bleu"), Color.FromRgb(0x4FC3F7)),
        ("Orange", Color.FromRgb(0xFF9800)),
        (L.T("Green", "Vert"), Color.FromRgb(0x66BB6A)),
        (L.T("Purple", "Violet"), Color.FromRgb(0xB388FF)),
    ];

    private readonly ToggleButton _liveEdit;
    private readonly SegmentedControl _themeSelector;
    private readonly DropDown _accentSelector;

    public GalleryApp()
    {
        Audio = new DemoAudio();
        Tabs = new TabView();
        Tabs.AddPage(L.T("Rotary", "Rotatifs"), RotaryLinearPage.Build());
        Tabs.AddPage(L.T("Buttons", "Boutons"), ButtonsPage.Build());
        Tabs.AddPage(L.T("Text", "Texte"), TextPage.Build());
        Tabs.AddPage(L.T("Meters", "Mesures"), MetersPage.Build(Audio));
        Tabs.AddPage(L.T("Containers", "Conteneurs"), ContainersPage.Build());
        Tabs.AddPage(L.T("Skins", "Habillages"), SkinsPage.Build());
        Tabs.AddPage("Instrument", InstrumentPage.Build(Audio));
        Tabs.AddPage(L.T("Analysis", "Analyse"), AnalysisPage.Build(Audio));
        Tabs.AddPage(L.T("Response", "Réponse"), ResponsePage.Build(Audio));

        _themeSelector = new SegmentedControl(L.T("Dark", "Sombre"), L.T("Light", "Clair")) { ToolTip = L.T("Theme", "Thème") };
        _accentSelector = new DropDown(_accents.Select(a => a.Name).ToArray()) { ToolTip = L.T("Accent color", "Couleur d'accent") };
        _liveEdit = new ToggleButton(L.T("Live edit", "Édition live")) { ShowLed = true, ToolTip = L.T("Ctrl+Shift+E: move and resize the elements, Ctrl+C copies the code", "Ctrl+Maj+E : déplacer et redimensionner les éléments, Ctrl+C copie le code") };
        var about = new Button(L.T("About", "À propos")) { Appearance = ButtonAppearance.Flat };

        var toolbar = new DockPanel { Height = 44f, Dock = Side.Top, Padding = new Thickness(14f, 8f), LastChildFill = false };
        var title = new Label(L.T("PluginUI — gallery", "PluginUI — galerie")) { TextStyle = new TextStyle("Segoe UI", 16f, FontWeight.SemiBold), Width = 220f, Dock = Side.Left };
        about.Dock = Side.Right; about.Width = 90f;
        _liveEdit.Dock = Side.Right; _liveEdit.Width = 130f; _liveEdit.Margin = new Thickness(8f, 0f);
        _accentSelector.Dock = Side.Right; _accentSelector.Width = 110f; _accentSelector.Margin = new Thickness(8f, 0f);
        _themeSelector.Dock = Side.Right; _themeSelector.Width = 150f;
        toolbar.Add(title, about, _liveEdit, _accentSelector, _themeSelector);

        var status = new DockPanel { Height = 24f, Dock = Side.Bottom, Padding = new Thickness(14f, 0f, 0f, 0f) };
        var resizer = new CornerResizer { Dock = Side.Right, Width = 18f };
        var info = new Label { IsDim = true, TextStyle = new TextStyle("Segoe UI", 10.5f) };
        status.Add(resizer, info);

        Root = new DockPanel();
        Tabs.Dock = Side.Top;
        Root.Add(toolbar, status, Tabs);

        Host = new UIHost(Root, new Size(900f, 680f), SizingMode.ScaleToFit)
        {
            MinSize = new Size(600f, 453f),
            MaxSize = new Size(2700f, 2040f),
        };
        Host.AddTicker(Audio);
        info.TextSource = () =>
        {
            var e = Host.HoveredElement;
            string where = e is null ? "" : string.Create(CultureInfo.InvariantCulture, $"{e}  ·  {e.Bounds.X:0},{e.Bounds.Y:0}  {e.Bounds.Width:0}×{e.Bounds.Height:0}");
            return string.Create(CultureInfo.InvariantCulture, $"{Host.PixelSize.Width}×{Host.PixelSize.Height} px  ·  {L.T("scale", "échelle")} {Host.Scale:0.00}  ·  {where}");
        };

        _themeSelector.ValueChanged += (_, _) => ApplyTheme();
        _accentSelector.ValueChanged += (_, _) => ApplyTheme();
        _liveEdit.ValueChanged += (_, _) => Host.IsLiveEditEnabled = _liveEdit.IsOn;
        Host.LiveEditChanged += (_, on) => _liveEdit.IsOn = on;
        about.Clicked = () => Host.ShowPopup(new AboutBox(L.T("PluginUI Gallery", "Galerie PluginUI"), "0.1.0")
        {
            Text = L.T("Win32 + Direct2D user interface toolkit for VST3 plug-ins written in C# (.NET 10, NativeAOT).", "Boîte à outils d'interface Win32 + Direct2D pour les plugins VST3 écrits en C# (.NET 10, NativeAOT)."),
            Url = "https://github.com/xoofx/NPlug",
            SvgLogo = SvgImage.FromString(Art.LogoSvg),
        });
        _accentSelector.SelectedIndex = 0;
        ApplyTheme();
    }

    public DockPanel Root { get; }

    public TabView Tabs { get; }

    public UIHost Host { get; }

    public DemoAudio Audio { get; }

    private void ApplyTheme()
    {
        var baseTheme = _themeSelector.SelectedIndex == 1 ? Theme.Light : Theme.Dark;
        Host.Theme = baseTheme.WithAccent(_accents[Math.Clamp(_accentSelector.SelectedIndex, 0, _accents.Length - 1)].Color);
    }

    /// <summary>Selects the theme (snapshot mode).</summary>
    public void SetTheme(bool light) => _themeSelector.SelectedIndex = light ? 1 : 0;
}
