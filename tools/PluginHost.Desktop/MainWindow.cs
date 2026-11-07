// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using PluginUI.Desktop;
using PluginUI.Host.Audio;
using PluginUI.Host.Windows;
using PluginUI.Host.Catalog;

namespace PluginUI.Host.Desktop;

/// <summary>
/// Main window: the plug-in list (search, filters, sortable and configurable columns), the details of the selection,
/// scans with their progress, the plug-in folders, and the windows of the open plug-ins.
/// </summary>
internal sealed class MainWindow
{
    private readonly DesktopApplication _app;
    private readonly AppSettings<GuiSettings> _settings;
    private readonly bool _saveChanges;
    private readonly TextBox _search;
    private readonly DropDown _format, _kind, _sort;
    private readonly CheckBox _favorites, _failed;
    private readonly Label _details;
    private readonly SplitView _split;
    private readonly ProgressBar _progress;
    private readonly PluginLoader _loader;
    private readonly AudioEngine _audio;
    private readonly LevelMeter _outputMeter;
    private readonly float[] _outputLevels = new float[2];
    private readonly List<PluginWindow> _open = new();
    private readonly List<(string Text, string Key, bool Descending)> _sortChoices;
    private PluginCatalog _catalog;
    private HostSettings _hostSettings;
    private List<PluginDescription> _rows = new();
    private CancellationTokenSource? _scan;
    private string _scanText = string.Empty;
    private bool _syncingSort;
    private bool _ready;

    public MainWindow(DesktopApplication app, AppSettings<GuiSettings> settings, PluginCatalog catalog, HostSettings hostSettings, bool saveChanges)
    {
        _app = app;
        _settings = settings;
        _catalog = catalog;
        _hostSettings = hostSettings;
        _saveChanges = saveChanges;
        _loader = new PluginLoader(_catalog, "PluginUI Plug-in Host");
        var t = UIStrings.ForLanguage(UIHost.DefaultLanguage);
        var s = settings.Value;

        // ---------------------------------------------------------------- table
        Table = new DataTable { MultiSelect = true, EmptyText = L.T("No plug-in. Scan the folders (F5).", "Aucun plug-in. Lancez une recherche (F5).") };
        AddColumns();
        Table.RestoreLayout(s.TableLayout);
        Table.RowActivated += (_, item) => Open((PluginDescription)item);
        Table.SelectionChanged += _ => _details?.InvalidateVisual();
        Table.ColumnsChanged += _ => SaveLayout();
        Table.SortChanged += _ => { SyncSortChoice(); SaveLayout(); };
        Table.RowContextMenu = RowMenu;

        // ---------------------------------------------------------------- filter bar
        _search = new TextBox { IsSearchBox = true, Placeholder = L.T("Search: name, vendor, category", "Rechercher : nom, marque, catégorie"), Width = 260f, Text = s.Search };
        _search.TextChanged += (_, _) => { _settings.Value.Search = _search.Text; Refresh(); };
        _format = Choice([L.T("All formats", "Tous formats"), "VST3", "VST2"], s.FormatFilter, i => _settings.Value.FormatFilter = i, 120f);
        _kind = Choice([L.T("Instruments and effects", "Instruments et effets"), L.T("Instruments", "Instruments"), L.T("Effects", "Effets")], s.KindFilter,
            i => _settings.Value.KindFilter = i, 180f);
        _sortChoices =
        [
            (L.T("Sort: name", "Tri : nom"), "name", false),
            (L.T("Sort: vendor", "Tri : marque"), "vendor", false),
            (L.T("Sort: category", "Tri : catégorie"), "category", false),
            (L.T("Sort: format", "Tri : format"), "format", false),
            (L.T("Sort: input channels", "Tri : canaux d'entrée"), "inputs", true),
            (L.T("Sort: output channels", "Tri : canaux de sortie"), "outputs", true),
            (L.T("Sort: latency", "Tri : latence"), "latency", false),
            (L.T("Sort: installation date", "Tri : date d'installation"), "installed", true),
            (L.T("Sort: recently used", "Tri : utilisés récemment"), "lastused", true),
            (L.T("Sort: most used", "Tri : les plus utilisés"), "uses", true),
            (L.T("Sort: favorites first", "Tri : favoris d'abord"), "favorite", false),
            (L.T("Sort: folder", "Tri : dossier"), "folder", false),
            (L.T("Sort: status", "Tri : état"), "status", false),
        ];
        _sort = new DropDown([.. _sortChoices.Select(c => c.Text)]) { Label = string.Empty, Width = 220f, Height = 26f };
        _sort.ValueChanged += (_, _) => ApplySortChoice();
        _favorites = Check(L.T("Favorites only", "Favoris seulement"), s.FavoritesOnly, v => _settings.Value.FavoritesOnly = v);
        _failed = Check(L.T("Show failures", "Afficher les échecs"), s.ShowFailed, v => _settings.Value.ShowFailed = v);
        _progress = new ProgressBar { Width = 160f, Height = 8f, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8f, Dock = Side.Top, Height = 40f, Padding = new Thickness(10f, 7f, 10f, 7f),
        };
        _outputMeter = new LevelMeter
        {
            Orientation = Orientation.Horizontal, ChannelCount = 2, Width = 150f, Height = 14f, ShowScale = false, MinDb = -60f,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("Output level of the audio device", "Niveau de sortie du périphérique audio"),
        };
        filters.Add(_search, _format, _kind, _sort, _favorites, _failed, _progress, _outputMeter);

        // ---------------------------------------------------------------- details
        _details = new Label
        {
            TextSource = DetailsText,
            Wrap = true,
            VerticalTextAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12f, 10f, 12f, 10f),
            TextStyle = new TextStyle(Size: 12.5f, VerticalAlign: VerticalAlignment.Top, Ellipsis: false),
        };
        _split = new SplitView(Table, _details) { Ratio = Math.Clamp(s.DetailsRatio, 0.3f, 0.95f), MinSecond = 180f, Margin = new Thickness(10f, 0f, 10f, 6f) };
        _split.RatioChanged += (_, r) => _settings.Value.DetailsRatio = r;
        _details.IsVisible = s.ShowDetails;

        // ---------------------------------------------------------------- commands
        var scan = new Command(L.T("&Scan for new plug-ins", "&Rechercher les nouveaux plug-ins"), () => StartScan(false), KeyGesture.Parse("F5"))
        {
            CanExecuteWhen = () => _scan is null,
            Description = L.T("Opens the new and changed modules of the plug-in folders", "Ouvre les modules nouveaux ou modifiés des dossiers de plug-ins"),
        };
        var rescan = new Command(L.T("Scan &everything again", "&Tout rechercher à nouveau"), () => StartScan(true), KeyGesture.Parse("Shift+F5"))
        {
            CanExecuteWhen = () => _scan is null,
            Description = L.T("Opens every module again, also those that failed", "Rouvre tous les modules, y compris ceux en échec"),
        };
        var stop = new Command(L.T("S&top the scan", "&Arrêter la recherche"), () => _scan?.Cancel())
        {
            CanExecuteWhen = () => _scan is not null,
            Description = L.T("Stops after the current module; the list keeps its previous content", "S'arrête après le module en cours ; la liste garde son contenu précédent"),
        };
        var folders = new Command(L.T("Plug-in &folders…", "&Dossiers de plug-ins…"), ShowFoldersDialog, KeyGesture.Parse("Ctrl+Shift+F"))
        {
            Description = L.T("Folders searched for VST3 and VST2 plug-ins", "Dossiers où chercher les plug-ins VST3 et VST2"),
        };
        var audio = new Command(L.T("&Audio and MIDI…", "&Audio et MIDI…"), ShowAudioDialog, KeyGesture.Parse("Ctrl+Shift+A"))
        {
            Description = L.T("Audio device, input, buffer size and MIDI input", "Périphérique audio, entrée, taille de tampon et entrée MIDI"),
        };
        var open = new Command(L.T("&Open", "&Ouvrir"), () => { foreach (var p in Selected) Open(p); }, KeyGesture.Parse("Ctrl+O"))
        {
            CanExecuteWhen = () => Selected.Any(p => p.Status == PluginStatus.Ok),
            Description = L.T("Opens the selected plug-ins in their own windows (Enter, double-click)", "Ouvre les plug-ins choisis dans leur fenêtre (Entrée, double-clic)"),
        };
        var favorite = new Command(L.T("&Favorite", "&Favori"), ToggleFavorite, KeyGesture.Parse("Ctrl+D"))
        {
            CanExecuteWhen = () => Selected.Any(),
            IsCheckedWhen = () => Selected.Any() && Selected.All(p => p.IsFavorite),
            Description = L.T("Marks or unmarks the selected plug-ins as favorites", "Marque ou retire les plug-ins choisis des favoris"),
        };
        var reveal = new Command(L.T("Show in &Explorer", "Afficher dans l'&Explorateur"), () => { if (Selected.FirstOrDefault() is { } p) Shell.RevealInExplorer(p.Path); })
        {
            CanExecuteWhen = () => Selected.Any(),
            Description = L.T("Opens the folder of the module", "Ouvre le dossier du module"),
        };
        var copyPath = new Command(L.T("&Copy the path", "&Copier le chemin"), CopyPaths, KeyGesture.Parse("Ctrl+Shift+C"))
        {
            CanExecuteWhen = () => Selected.Any(),
            Description = L.T("Copies the module paths to the clipboard", "Copie les chemins des modules dans le presse-papiers"),
        };
        var ignore = new Command(L.T("&Ignore the module", "&Ignorer le module"), IgnoreSelected)
        {
            CanExecuteWhen = () => Selected.Any(),
            Description = L.T("Removes the module from the list and from later scans (settings.json, IgnoredModules)",
                "Retire le module de la liste et des recherches suivantes (settings.json, IgnoredModules)"),
        };
        var details = new Command(L.T("&Details pane", "Volet des &détails"), ToggleDetails, KeyGesture.Parse("Ctrl+I"))
        {
            IsCheckedWhen = () => _settings.Value.ShowDetails,
            Description = L.T("Shows the details of the selected plug-in", "Affiche les détails du plug-in choisi"),
        };
        var light = new Command(L.T("&Light theme", "Thème &clair"), ToggleTheme, KeyGesture.Parse("Ctrl+T"))
        {
            IsCheckedWhen = () => _settings.Value.LightTheme,
            Description = L.T("Switches between the dark and the light theme", "Bascule entre le thème sombre et le thème clair"),
        };
        var search = new Command(L.T("&Search", "&Rechercher"), () => { _search.Focus(); _search.SelectAll(); }, KeyGesture.Parse("Ctrl+F"))
        {
            Description = L.T("Goes to the search box", "Va dans la zone de recherche"),
        };
        var about = new Command(t.FormatAbout(app.Name), ShowAbout, KeyGesture.Parse("F1")) { Description = L.T("Version and credits", "Version et crédits") };

        var fileMenu = new MenuItem(t.MenuFile);
        fileMenu.Items.Add(new MenuItem(scan));
        fileMenu.Items.Add(new MenuItem(rescan));
        fileMenu.Items.Add(new MenuItem(stop));
        fileMenu.Items.Add(MenuItem.Separator());
        fileMenu.Items.Add(new MenuItem(folders));
        fileMenu.Items.Add(new MenuItem(audio));
        fileMenu.Items.Add(MenuItem.Separator());
        fileMenu.Items.Add(new MenuItem(app.ExitCommand));
        var pluginMenu = new MenuItem(L.T("&Plug-in", "&Plug-in"));
        pluginMenu.Items.Add(new MenuItem(open));
        pluginMenu.Items.Add(new MenuItem(favorite));
        pluginMenu.Items.Add(MenuItem.Separator());
        pluginMenu.Items.Add(new MenuItem(reveal));
        pluginMenu.Items.Add(new MenuItem(copyPath));
        pluginMenu.Items.Add(MenuItem.Separator());
        pluginMenu.Items.Add(new MenuItem(ignore));
        var viewMenu = new MenuItem(t.MenuView);
        viewMenu.Items.Add(new MenuItem(search));
        viewMenu.Items.Add(new MenuItem(details));
        var columns = new MenuItem(L.T("&Columns", "&Colonnes"));
        viewMenu.Items.Add(columns);
        viewMenu.Items.Add(MenuItem.Separator());
        viewMenu.Items.Add(new MenuItem(light));
        var language = new MenuItem(L.T("L&anguage", "L&angue"));
        language.Items.Add(new MenuItem(L.T("&Automatic (Windows)", "&Automatique (Windows)"), () => SetLanguage("auto")) { Tag = "auto" });
        language.Items.Add(new MenuItem("&English", () => SetLanguage("en")) { Tag = "en" });
        language.Items.Add(new MenuItem("&Français", () => SetLanguage("fr")) { Tag = "fr" });
        viewMenu.Items.Add(language);
        var helpMenu = new MenuItem(t.MenuHelp);
        helpMenu.Items.Add(new MenuItem(about));
        MenuBar = new MenuBar(fileMenu, pluginMenu, viewMenu, helpMenu) { Dock = Side.Top };
        MenuBar.Opening += (_, top) =>
        {
            if (ReferenceEquals(top, viewMenu))
            {
                columns.Items.Clear();
                columns.Items.AddRange(Table.GetColumnMenuItems());
                foreach (var item in language.Items) item.IsChecked = Equals(item.Tag, _settings.Value.Language);
            }
        };

        ToolBar = new ToolBar { Dock = Side.Top };
        ToolBar.Add(scan, ToolGlyph.Play).ShowText = true;
        ToolBar.Add(stop, ToolGlyph.Stop);
        ToolBar.Add(folders, ToolGlyph.Settings);
        ToolBar.Add(audio, ToolGlyph.Wave);
        ToolBar.AddSeparator();
        ToolBar.Add(open, ToolGlyph.Open);
        ToolBar.Add(copyPath, ToolGlyph.Copy);
        ToolBar.AddSeparator();
        ToolBar.Add(details, ToolGlyph.Grid);
        ToolBar.Add(about, ToolGlyph.Help);

        StatusBar = new StatusBar { Dock = Side.Bottom };
        StatusBar.Segments.Add(new StatusBarSegment(() => _scanText.Length > 0 ? _scanText : Host!.Strings.StatusReady, float.PositiveInfinity));
        StatusBar.Segments.Add(new StatusBarSegment(CountText, 260f) { Align = HorizontalAlignment.Right });
        StatusBar.Segments.Add(new StatusBarSegment(() => _audio?.Describe() ?? string.Empty, 360f) { Align = HorizontalAlignment.Right });
        StatusBar.Segments.Add(new StatusBarSegment(LastScanText, 220f) { Align = HorizontalAlignment.Right });
        StatusBar.Follow(MenuBar);
        StatusBar.Follow(ToolBar);

        var root = new DockPanel();
        root.Children.Add(MenuBar);
        root.Children.Add(ToolBar);
        root.Children.Add(filters);
        root.Children.Add(StatusBar);
        root.Children.Add(_split);

        Host = new UIHost(root, new Size(1180f, 720f), SizingMode.Relayout) { MinSize = new Size(820f, 420f) };
        if (s.LightTheme) Host.Theme = Theme.Light;
        Window = new StandaloneWindow(Host, app.Name);
        Window.Closing += (_, _) => CloseAll();
        Window.Closed += _ => SaveAll();
        _audio = new AudioEngine(Host);
        _audio.Message += message => StatusBar.ShowMessage(message, 10.0);
        _audio.Changed += () => StatusBar.InvalidateVisual();
        _audio.Rack.SlotFailed += (slot, ex) => Host.Post(() => StatusBar.ShowMessage(
            L.T($"{slot.Plugin.Name} stopped processing: {ex.Message}", $"{slot.Plugin.Name} a cessé de traiter : {ex.Message}"), 8.0));
        _audio.Start(saveChanges ? s : new GuiSettings { AudioOutput = "none" });
        Host.AddTicker(new Ticker(() =>
        {
            _audio.Rack.OutputMeter.Read(_outputLevels);
            _outputMeter.SetLevels(_outputLevels);
            return true;
        }));

        _ready = true;
        SyncSortChoice();
        Refresh();
    }

    public UIHost Host { get; }

    public StandaloneWindow Window { get; }

    public MenuBar MenuBar { get; }

    public ToolBar ToolBar { get; }

    public StatusBar StatusBar { get; }

    public DataTable Table { get; }

    private IEnumerable<PluginDescription> Selected => Table.SelectedItems.Cast<PluginDescription>();

    // ---------------------------------------------------------------- table

    private void AddColumns()
    {
        static int Text(string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        DataColumn Column(string key, string header, Func<PluginDescription, string> text, float width, Comparison<PluginDescription>? compare = null,
            HorizontalAlignment align = HorizontalAlignment.Left, bool visible = true)
        {
            var column = new DataColumn(header, o => text((PluginDescription)o), width)
            {
                Key = key,
                Alignment = align,
                IsVisible = visible,
                Comparison = compare is null ? (a, b) => Text(text((PluginDescription)a), text((PluginDescription)b)) : (a, b) => compare((PluginDescription)a, (PluginDescription)b),
            };
            Table.AddColumn(column);
            return column;
        }
        static string Count(int n) => n == 0 ? "–" : n.ToString(CultureInfo.InvariantCulture);
        static string Date(DateTime? d) => d is { } v && v > DateTime.MinValue ? v.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

        var star = Column("favorite", L.T("Fav.", "Fav."), p => p.IsFavorite ? L.T("favorite", "favori") : string.Empty, 50f,
            (a, b) => b.IsFavorite.CompareTo(a.IsFavorite), HorizontalAlignment.Center);
        star.CellRenderer = (dc, r, o, color) =>
        {
            if (((PluginDescription)o).IsFavorite) DrawStar(dc, r.Center, 6.5f, Color.FromRgb(0xFFC107));
        };
        var name = Column("name", L.T("Name", "Nom"), p => p.Name, 200f);
        name.CanHide = false;
        name.CellColor = o => ((PluginDescription)o).Status == PluginStatus.Ok ? null : Color.FromRgb(0xE57373);
        Column("vendor", L.T("Vendor", "Marque"), p => p.Vendor, 130f);
        Column("category", L.T("Type", "Type"), p => p.IsInstrument ? L.T("Instrument", "Instrument") : p.Status == PluginStatus.Ok ? L.T("Effect", "Effet") : string.Empty, 80f);
        Column("subcategories", L.T("Categories", "Catégories"), p => p.SubCategories.Replace("|", ", ", StringComparison.Ordinal), float.NaN).MinWidth = 120f;
        Column("format", L.T("Format", "Format"), p => p.Format == PluginFormat.Vst3 ? "VST3" : "VST2", 56f);
        Column("inputs", L.T("Inputs", "Entrées"), p => Count(p.InputChannels), 58f, (a, b) => a.InputChannels.CompareTo(b.InputChannels), HorizontalAlignment.Right);
        Column("outputs", L.T("Outputs", "Sorties"), p => Count(p.OutputChannels), 62f, (a, b) => a.OutputChannels.CompareTo(b.OutputChannels), HorizontalAlignment.Right);
        Column("sidechain", L.T("Sidechain", "Sidechain"), p => Count(p.AuxInputChannels), 76f, (a, b) => a.AuxInputChannels.CompareTo(b.AuxInputChannels), HorizontalAlignment.Right, visible: false);
        Column("midi", "MIDI", p => p.HasMidiInput ? L.T("yes", "oui") : string.Empty, 52f, (a, b) => b.HasMidiInput.CompareTo(a.HasMidiInput), HorizontalAlignment.Center, visible: false);
        Column("latency", L.T("Latency", "Latence"), p => p.LatencySamples == 0 ? "–" : p.LatencySamples.ToString(CultureInfo.InvariantCulture), 64f,
            (a, b) => a.LatencySamples.CompareTo(b.LatencySamples), HorizontalAlignment.Right);
        Column("parameters", L.T("Parameters", "Paramètres"), p => Count(p.ParameterCount), 84f, (a, b) => a.ParameterCount.CompareTo(b.ParameterCount), HorizontalAlignment.Right, visible: false);
        Column("editor", L.T("Editor", "Éditeur"), p => p.HasEditor ? L.T("yes", "oui") : string.Empty, 60f, (a, b) => b.HasEditor.CompareTo(a.HasEditor), HorizontalAlignment.Center, visible: false);
        Column("version", L.T("Version", "Version"), p => p.Version, 80f, visible: false);
        Column("installed", L.T("Installed", "Installé"), p => Date(p.ModuleTimeUtc), 92f, (a, b) => a.ModuleTimeUtc.CompareTo(b.ModuleTimeUtc), visible: false);
        Column("lastused", L.T("Last used", "Utilisé le"), p => Date(p.LastUsedUtc), 92f, (a, b) => Nullable.Compare(a.LastUsedUtc, b.LastUsedUtc), visible: false);
        Column("uses", L.T("Uses", "Utilisations"), p => Count(p.UseCount), 84f, (a, b) => a.UseCount.CompareTo(b.UseCount), HorizontalAlignment.Right, visible: false);
        Column("folder", L.T("Folder", "Dossier"), p => p.Folder, 220f, visible: false);
        Column("status", L.T("Status", "État"), p => p.Status == PluginStatus.Ok ? string.Empty : L.T("Failed", "Échec"), 70f, (a, b) => a.Status.CompareTo(b.Status), visible: false);
        Table.Columns.Find(c => c.Key == "status")!.CellColor = o => ((PluginDescription)o).Status == PluginStatus.Ok ? null : Color.FromRgb(0xE57373);
    }

    private static void DrawStar(DrawingContext dc, Point center, float radius, Color color)
    {
        Span<Point> points = stackalloc Point[10];
        for (int i = 0; i < 10; i++)
        {
            float r = (i & 1) == 0 ? radius : radius * 0.45f;
            float a = -MathF.PI / 2f + i * MathF.PI / 5f;
            points[i] = new Point(center.X + r * MathF.Cos(a), center.Y + r * MathF.Sin(a));
        }
        dc.FillPolygon(points, color);
    }

    /// <summary>Rebuilds the rows from the catalog and the filters, keeping the selection.</summary>
    public void Refresh()
    {
        if (!_ready) return;
        var keep = Selected.Select(p => p.Key).ToHashSet();
        PluginFormat? format = _format.SelectedIndex switch { 1 => PluginFormat.Vst3, 2 => PluginFormat.Vst2, _ => null };
        bool? instruments = _kind.SelectedIndex switch { 1 => true, 2 => false, _ => null };
        _rows = _catalog.Query(_search.Text, PluginSort.Name, false, format, instruments, includeUnavailable: _failed.IsChecked)
            .Where(p => !_favorites.IsChecked || p.IsFavorite).ToList();
        Table.Items = _rows;
        if (keep.Count > 0 && _rows.FirstOrDefault(p => keep.Contains(p.Key)) is { } first) Table.SelectedItem = first;
        _details.InvalidateVisual();
        StatusBar.InvalidateVisual();
    }

    /// <summary>Selects a plug-in by name (snapshots).</summary>
    public void SelectByName(string name) => Table.SelectedItem = _rows.FirstOrDefault(p => p.Name == name);

    private void ApplySortChoice()
    {
        if (_syncingSort || _sort.SelectedIndex < 0) return;
        var (_, key, descending) = _sortChoices[_sort.SelectedIndex];
        var column = Table.Columns.Find(c => c.Key == key);
        if (column is null) return;
        if (!column.IsVisible) Table.SetColumnVisible(column, true);
        Table.Sort(column, descending);
    }

    private void SyncSortChoice()
    {
        int index = Table.SortColumn is { } column ? _sortChoices.FindIndex(c => c.Key == column.Key) : 0;
        _syncingSort = true;
        try
        {
            _sort.SelectedIndex = Math.Max(0, index);
        }
        finally
        {
            _syncingSort = false;
        }
    }

    private void SaveLayout() => _settings.Value.TableLayout = Table.SaveLayout();

    private IList<MenuItem>? RowMenu(IReadOnlyList<object> selection)
    {
        var plugins = selection.Cast<PluginDescription>().ToList();
        var items = new List<MenuItem>();
        if (plugins.Any(p => p.Status == PluginStatus.Ok))
            items.Add(new MenuItem(L.T("&Open", "&Ouvrir"), () => { foreach (var p in plugins) Open(p); }) { ShortcutText = L.T("Enter", "Entrée") });
        bool allFavorites = plugins.All(p => p.IsFavorite);
        items.Add(new MenuItem(L.T("&Favorite", "&Favori"), ToggleFavorite) { IsChecked = allFavorites, ShortcutText = "Ctrl+D" });
        items.Add(MenuItem.Separator());
        items.Add(new MenuItem(L.T("Show in &Explorer", "Afficher dans l'&Explorateur"), () => Shell.RevealInExplorer(plugins[0].Path)));
        items.Add(new MenuItem(L.T("&Copy the path", "&Copier le chemin"), CopyPaths) { ShortcutText = "Ctrl+Shift+C" });
        items.Add(MenuItem.Separator());
        items.Add(new MenuItem(L.T("&Ignore the module", "&Ignorer le module"), IgnoreSelected));
        return items;
    }

    // ---------------------------------------------------------------- texts

    private string CountText()
    {
        int ok = _catalog.Plugins.Count(p => p.Status == PluginStatus.Ok);
        int failed = _catalog.Plugins.Count - ok;
        string shown = L.T($"{_rows.Count} shown of {ok}", $"{_rows.Count} affichés sur {ok}");
        return failed > 0 ? shown + L.T($", {failed} failed", $", {failed} en échec") : shown;
    }

    private string LastScanText()
        => _catalog.LastScanUtc is { } last
            ? L.T("Last scan ", "Dernière recherche ") + last.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : L.T("Never scanned", "Jamais recherché");

    private string DetailsText()
    {
        var selection = Selected.ToList();
        if (selection.Count == 0) return L.T("Select a plug-in to see its details.\n\nDouble-click or Enter opens it.", "Choisissez un plug-in pour voir ses détails.\n\nDouble-clic ou Entrée l'ouvre.");
        if (selection.Count > 1) return L.T($"{selection.Count} plug-ins selected.", $"{selection.Count} plug-ins choisis.");
        var p = selection[0];
        var sb = new StringBuilder();
        sb.AppendLine(p.Name);
        if (p.Vendor.Length > 0) sb.AppendLine(p.Vendor);
        sb.AppendLine();
        void Line(string en, string fr, string value)
        {
            if (value.Length > 0) sb.Append(L.T(en, fr)).Append(L.French ? " : " : ": ").AppendLine(value);
        }
        Line("Format", "Format", (p.Format == PluginFormat.Vst3 ? "VST3" : "VST2") + (p.SdkVersion.Length > 0 ? $" ({p.SdkVersion})" : string.Empty));
        Line("Version", "Version", p.Version);
        Line("Type", "Type", p.IsInstrument ? L.T("instrument", "instrument") : L.T("effect", "effet"));
        Line("Categories", "Catégories", p.SubCategories.Replace("|", ", ", StringComparison.Ordinal));
        if (p.Status == PluginStatus.Ok)
        {
            Line("Audio", "Audio", L.T($"{p.InputChannels} in, {p.OutputChannels} out", $"{p.InputChannels} entrée(s), {p.OutputChannels} sortie(s)"));
            if (p.AuxInputChannels + p.AuxOutputChannels > 0)
                Line("Auxiliary", "Auxiliaires", L.T($"{p.AuxInputChannels} in (sidechain), {p.AuxOutputChannels} out", $"{p.AuxInputChannels} entrée(s) (sidechain), {p.AuxOutputChannels} sortie(s)"));
            Line("MIDI input", "Entrée MIDI", p.HasMidiInput ? L.T("yes", "oui") : L.T("no", "non"));
            Line("Latency", "Latence", p.LatencySamples == 0 ? "0" : L.T($"{p.LatencySamples} samples ({p.LatencySamples / 48.0:0.#} ms at 48 kHz)",
                $"{p.LatencySamples} échantillons ({p.LatencySamples / 48.0:0.#} ms à 48 kHz)"));
            Line("Parameters", "Paramètres", p.ParameterCount.ToString(CultureInfo.InvariantCulture));
            Line("Editor", "Éditeur", p.HasEditor ? L.T("yes", "oui") : L.T("no (generic view)", "non (vue générique)"));
        }
        sb.AppendLine();
        Line("Module", "Module", p.Path);
        if (p.ModuleTimeUtc > DateTime.MinValue) Line("Installed", "Installé", p.ModuleTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (p.LastUsedUtc is { } used) Line("Last used", "Dernière utilisation", used.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + L.T($" ({p.UseCount} times)", $" ({p.UseCount} fois)"));
        if (p.Status != PluginStatus.Ok)
        {
            sb.AppendLine();
            sb.AppendLine(L.T("Could not be loaded:", "N'a pas pu être chargé :"));
            sb.AppendLine(p.Error);
        }
        return sb.ToString().TrimEnd();
    }

    // ---------------------------------------------------------------- actions

    private void Open(PluginDescription description)
    {
        if (description.Status != PluginStatus.Ok)
        {
            StatusBar.ShowMessage(L.T($"{description.Name} could not be loaded during the scan.", $"{description.Name} n'a pas pu être chargé lors de la recherche."));
            return;
        }
        if (_open.Find(w => w.Description.Key == description.Key) is { } existing)
        {
            existing.Window.Activate();
            return;
        }
        PluginInstance instance;
        try
        {
            instance = _loader.Load(description);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageDialog.Show(Window, _app.Name, L.T($"{description.Name} could not be opened:\n{ex.Message}", $"{description.Name} n'a pas pu être ouvert :\n{ex.Message}"));
            return;
        }
        SaveCatalog();
        Table.InvalidateVisual();
        PluginWindow window;
        try
        {
            window = new PluginWindow(description, instance, _audio, Window, _settings.Value.LightTheme);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            instance.Dispose();
            MessageDialog.Show(Window, _app.Name, L.T($"{description.Name} could not be started:\n{ex.Message}", $"{description.Name} n'a pas pu démarrer :\n{ex.Message}"));
            return;
        }
        _open.Add(window);
        window.Window.Closed += _ => _open.Remove(window);
        window.Show();
    }

    private void ToggleFavorite()
    {
        var selection = Selected.ToList();
        if (selection.Count == 0) return;
        bool on = !selection.All(p => p.IsFavorite);
        foreach (var p in selection) p.IsFavorite = on;
        SaveCatalog();
        if (_favorites.IsChecked) Refresh();
        Table.InvalidateVisual();
        _details.InvalidateVisual();
    }

    private void CopyPaths()
    {
        var paths = Selected.Select(p => p.Path).Distinct().ToList();
        if (paths.Count > 0 && Clipboard.SetText(string.Join(Environment.NewLine, paths)))
            StatusBar.ShowMessage(L.T("Path copied to the clipboard", "Chemin copié dans le presse-papiers"));
    }

    private void IgnoreSelected()
    {
        var modules = Selected.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (modules.Count == 0) return;
        foreach (string m in modules)
        {
            if (!_hostSettings.IgnoredModules.Contains(m, StringComparer.OrdinalIgnoreCase)) _hostSettings.IgnoredModules.Add(m);
        }
        _catalog.Plugins.RemoveAll(p => modules.Contains(p.Path, StringComparer.OrdinalIgnoreCase));
        SaveHostSettings();
        SaveCatalog();
        Refresh();
        StatusBar.ShowMessage(L.T($"{modules.Count} module(s) ignored (remove them from IgnoredModules in settings.json to scan them again)",
            $"{modules.Count} module(s) ignoré(s) (retirez-les de IgnoredModules dans settings.json pour les rechercher à nouveau)"), 8.0);
    }

    private void ToggleDetails()
    {
        _settings.Value.ShowDetails = !_settings.Value.ShowDetails;
        _details.IsVisible = _settings.Value.ShowDetails;
        Host.InvalidateLayout();
    }

    private void ToggleTheme()
    {
        _settings.Value.LightTheme = !_settings.Value.LightTheme;
        Host.Theme = _settings.Value.LightTheme ? Theme.Light : Theme.Dark;
        Window.UpdateTitleBarTheme();
        foreach (var w in _open) w.SetTheme(_settings.Value.LightTheme);
    }

    private void SetLanguage(string code)
    {
        if (_settings.Value.Language == code) return;
        _settings.Value.Language = code;
        SaveSettings();
        MessageDialog.Show(Window, _app.Name, L.T("The new language applies the next time the host starts.", "La nouvelle langue s'appliquera au prochain démarrage de l'hôte."));
    }

    private void ShowAbout()
    {
        var about = new AboutBox(_app.Name, typeof(MainWindow).Assembly.GetName().Version?.ToString(3))
        {
            Text = L.T("Audio plug-in host built with PluginUI.Host: VST3 today, VST2 and audio devices next. Plug-in list and folders shared with the pluginhost command line tool.",
                       "Hôte de plug-ins audio construit avec PluginUI.Host : VST3 aujourd'hui, VST2 et périphériques audio ensuite. Liste et dossiers de plug-ins partagés avec l'outil en ligne de commande pluginhost."),
            Url = "https://github.com/asm8086/PluginUI",
        };
        Host.ShowPopup(about);
    }

    // ---------------------------------------------------------------- scan

    private void StartScan(bool all)
    {
        if (_scan is not null) return;
        _scan = new CancellationTokenSource();
        var token = _scan.Token;
        var settings = _hostSettings;
        var work = new PluginCatalog { Plugins = PluginCatalog.FromJson(PluginCatalog.ToJson(_catalog.Plugins)), LastScanUtc = _catalog.LastScanUtc };
        var scanner = PluginScanner.OutOfProcess(Environment.ProcessPath ?? "PluginHost.exe", SelfArguments());
        var progress = new UiProgress(Host, p =>
        {
            if (_scan is null) return;
            _progress.Value = p.Count == 0 ? 0.0 : (double)p.Index / p.Count;
            _scanText = p.Module.Length == 0 ? string.Empty
                : L.T($"Scanning {p.Index + 1}/{p.Count}: {Path.GetFileName(p.Module)}", $"Recherche {p.Index + 1}/{p.Count} : {Path.GetFileName(p.Module)}");
            StatusBar.InvalidateVisual();
        });
        _progress.IsVisible = true;
        _progress.Value = 0.0;
        _scanText = L.T("Looking for modules…", "Recherche des modules…");
        Host.InvalidateLayout();
        Task.Run(() => scanner.Scan(settings, work, all, progress, token), token).ContinueWith(task => Host.Post(() => ScanFinished(task, work)), TaskScheduler.Default);
    }

    private void ScanFinished(Task<ScanSummary> task, PluginCatalog work)
    {
        _scan?.Dispose();
        _scan = null;
        _progress.IsVisible = false;
        _scanText = string.Empty;
        Host.InvalidateLayout();
        if (task.IsCanceled || task.Exception?.InnerException is OperationCanceledException)
        {
            StatusBar.ShowMessage(L.T("Scan stopped; the list is unchanged.", "Recherche arrêtée ; la liste est inchangée."));
            return;
        }
        if (task.Exception is { } error)
        {
            MessageDialog.Show(Window, _app.Name, L.T("The scan failed:\n", "La recherche a échoué :\n") + error.InnerException?.Message);
            return;
        }
        // Favorites and uses changed during the scan are kept.
        var current = _catalog.Plugins.ToDictionary(p => p.Key);
        foreach (var p in work.Plugins)
        {
            if (current.TryGetValue(p.Key, out var older)) p.KeepUserData(older);
        }
        _catalog.Plugins = work.Plugins;
        _catalog.LastScanUtc = work.LastScanUtc;
        SaveCatalog();
        Refresh();
        var s = task.Result;
        StatusBar.ShowMessage(L.T($"{s.Modules} modules: {s.Scanned} scanned, {s.Unchanged} unchanged, {s.Removed} plug-ins removed, {s.Failed} failed.",
            $"{s.Modules} modules : {s.Scanned} ouverts, {s.Unchanged} inchangés, {s.Removed} plug-ins retirés, {s.Failed} en échec."), 10.0);
    }

    private static string[] SelfArguments()
    {
        string process = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        return process.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? [Path.Combine(AppContext.BaseDirectory, "PluginHost.dll")] : [];
    }

    /// <summary>Reports scan progress on the UI thread.</summary>
    private sealed class UiProgress(UIHost host, Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => host.Post(() => report(value));
    }

    // ---------------------------------------------------------------- folders

    /// <summary>Calls a function on every frame.</summary>
    private sealed class Ticker(Func<bool> tick) : ITickable
    {
        public bool Tick(double elapsedSeconds) => tick();
    }

    // ---------------------------------------------------------------- audio

    /// <summary>Shows the audio and MIDI dialog.</summary>
    public void ShowAudioDialog()
    {
        var s = _settings.Value;
        List<AudioEndpoint> outputs = [], inputs = [];
        List<MidiInputPort> ports = [];
        try
        {
            outputs = [.. Wasapi.GetEndpoints(input: false)];
            inputs = [.. Wasapi.GetEndpoints(input: true)];
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            StatusBar.ShowMessage(ex.Message, 8.0);
        }
        try
        {
            ports = [.. MidiInput.Ports];
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            StatusBar.ShowMessage(ex.Message, 8.0);
        }
        static string Describe(AudioEndpoint e) => $"{e.Name} ({e.Channels} ch, {e.SampleRate / 1000.0:0.#} kHz)";

        // Output: Windows default, the devices, or none.
        var outputIds = new List<string?> { null };
        var outputNames = new List<string> { L.T("Default device of Windows", "Périphérique par défaut de Windows") };
        foreach (var e in outputs) { outputIds.Add(e.Id); outputNames.Add(Describe(e)); }
        outputIds.Add("none");
        outputNames.Add(L.T("No audio device", "Aucun périphérique audio"));
        var output = new DropDown([.. outputNames]) { Label = string.Empty, Height = 26f, SelectedIndex = Math.Max(0, outputIds.IndexOf(s.AudioOutput)) };

        // Input: none, Windows default, the devices.
        var inputIds = new List<string?> { null, "" };
        var inputNames = new List<string> { L.T("No input", "Aucune entrée"), L.T("Default input of Windows", "Entrée par défaut de Windows") };
        foreach (var e in inputs) { inputIds.Add(e.Id); inputNames.Add(Describe(e)); }
        var input = new DropDown([.. inputNames]) { Label = string.Empty, Height = 26f, SelectedIndex = Math.Max(0, inputIds.IndexOf(s.AudioInput)) };

        int[] sizes = [0, 64, 128, 256, 512, 1024, 2048];
        var buffer = new DropDown([L.T("Smallest the device allows", "La plus petite permise par le périphérique"), "64", "128", "256", "512", "1024", "2048"])
        {
            Label = string.Empty, Height = 26f, SelectedIndex = Math.Max(0, Array.IndexOf(sizes, s.BufferFrames)),
        };

        List<MidiOutputPort> outPorts = [];
        try
        {
            outPorts = [.. MidiOutput.Ports];
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            StatusBar.ShowMessage(ex.Message, 8.0);
        }
        var midiOutNames = new List<string?> { null };
        midiOutNames.AddRange(outPorts.Select(p => (string?)p.Name));
        var midiOut = new DropDown([L.T("None", "Aucune"), .. outPorts.Select(p => p.Name)])
        {
            Label = string.Empty, Height = 26f, SelectedIndex = Math.Max(0, midiOutNames.IndexOf(s.MidiOutput)),
        };

        var midiNames = new List<string?> { null };
        midiNames.AddRange(ports.Select(p => (string?)p.Name));
        var midi = new DropDown([L.T("None", "Aucune"), .. ports.Select(p => p.Name)])
        {
            Label = string.Empty, Height = 26f, SelectedIndex = Math.Max(0, midiNames.IndexOf(s.MidiInput)),
        };

        UIElement Row(string caption, UIElement control)
        {
            var row = new DockPanel { Height = 28f };
            row.Children.Add(new Label(caption) { Width = 170f, Dock = Side.Left, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(control);
            return row;
        }
        var body = new StackPanel { Spacing = 8f };
        body.Add(
            Row(L.T("Output (WASAPI)", "Sortie (WASAPI)"), output),
            Row(L.T("Input", "Entrée"), input),
            Row(L.T("Buffer (samples)", "Tampon (échantillons)"), buffer),
            Row(L.T("MIDI input", "Entrée MIDI"), midi),
            Row(L.T("MIDI output", "Sortie MIDI"), midiOut),
            new Label(L.T("Now: ", "Actuellement : ") + _audio.Describe()) { Wrap = true, IsDim = true, Height = 34f },
            new Label(L.T("WASAPI shares the device with other applications at the sample rate chosen in the sound settings of Windows. ASIO comes next.",
                "WASAPI partage le périphérique avec les autres applications, à la fréquence choisie dans les paramètres de son de Windows. ASIO viendra ensuite."))
            { Wrap = true, IsDim = true, Height = 34f });
        var dialog = new Dialog(L.T("Audio and MIDI", "Audio et MIDI"), body) { DialogWidth = 640f };
        dialog.AddButton(L.T("Cancel", "Annuler"), null, isCancel: true).AddButton("OK", "ok", isDefault: true);
        dialog.ResultChosen += (_, result) =>
        {
            if (!Equals(result, "ok")) return;
            s.AudioOutput = outputIds[Math.Max(0, output.SelectedIndex)];
            s.AudioInput = inputIds[Math.Max(0, input.SelectedIndex)];
            s.BufferFrames = sizes[Math.Max(0, buffer.SelectedIndex)];
            s.MidiInput = midiNames[Math.Max(0, midi.SelectedIndex)];
            s.MidiOutput = midiOutNames[Math.Max(0, midiOut.SelectedIndex)];
            SaveSettings();
            Host.Post(() =>
            {
                _audio.Start(s);
                StatusBar.ShowMessage(_audio.Describe(), 6.0);
            });
        };
        Host.ShowPopup(dialog);
    }

    /// <summary>Shows the plug-in folders dialog.</summary>
    public void ShowFoldersDialog()
    {
        var vst3 = new ListBox(_hostSettings.Vst3Folders) { Height = 120f, EmptyText = L.T("No folder", "Aucun dossier") };
        var vst2 = new ListBox(_hostSettings.Vst2Folders) { Height = 120f, EmptyText = L.T("No folder", "Aucun dossier") };
        UIElement Section(string title, ListBox list, List<string> folders)
        {
            var add = new Button(L.T("Add…", "Ajouter…"), () =>
            {
                if (FileDialogs.PickFolder(Host, title) is not { } folder) return;
                if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) folders.Add(folder);
                list.Refresh();
                SaveHostSettings();
            }) { Width = 110f, Height = 26f };
            var remove = new Button(L.T("Remove", "Retirer"), () =>
            {
                foreach (int i in list.SelectedIndices.OrderDescending()) folders.RemoveAt(i);
                list.Refresh();
                SaveHostSettings();
            }) { Width = 110f, Height = 26f };
            var buttons = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f, Dock = Side.Right, Margin = new Thickness(8f, 0f, 0f, 0f) };
            buttons.Add(add, remove);
            var row = new DockPanel { Height = 120f };
            row.Children.Add(buttons);
            row.Children.Add(list);
            var section = new StackPanel { Spacing = 4f };
            section.Add(new Label(title) { TextStyle = new TextStyle(Weight: FontWeight.SemiBold) }, row);
            return section;
        }
        var body = new StackPanel { Spacing = 12f };
        body.Add(
            Section(L.T("VST3 folders", "Dossiers VST3"), vst3, _hostSettings.Vst3Folders),
            Section(L.T("VST2 folders (VST3 modules are also looked for there)", "Dossiers VST2 (on y cherche aussi les modules VST3)"), vst2, _hostSettings.Vst2Folders),
            new Label(L.T($"Saved in {CompactPath(HostPaths.SettingsFile)}, shared with pluginhost. Scan (F5) after a change.",
                $"Enregistré dans {CompactPath(HostPaths.SettingsFile)}, partagé avec pluginhost. Lancez une recherche (F5) après un changement."))
            { Wrap = true, IsDim = true, Height = 34f });
        var dialog = new Dialog(L.T("Plug-in folders", "Dossiers de plug-ins"), body) { DialogWidth = 620f };
        dialog.AddButton(L.T("Standard folders", "Dossiers standard"), "reset").AddButton(L.T("Close", "Fermer"), null, isDefault: true, isCancel: true);
        dialog.ResultChosen += (_, result) =>
        {
            if (!Equals(result, "reset")) return;
            var defaults = HostSettings.CreateDefault();
            _hostSettings.Vst3Folders = defaults.Vst3Folders;
            _hostSettings.Vst2Folders = defaults.Vst2Folders;
            SaveHostSettings();
            Host.Post(ShowFoldersDialog);
        };
        Host.ShowPopup(dialog);
    }

    private static string CompactPath(string path)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return appData.Length > 0 && path.StartsWith(appData, StringComparison.OrdinalIgnoreCase) ? "%APPDATA%" + path[appData.Length..] : path;
    }

    // ---------------------------------------------------------------- saving

    private void SaveCatalog()
    {
        if (!_saveChanges) return;
        try
        {
            _catalog.Save(HostPaths.CatalogFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusBar.ShowMessage(L.T("The plug-in list could not be saved: ", "La liste des plug-ins n'a pas pu être enregistrée : ") + ex.Message, 8.0);
        }
    }

    private void SaveHostSettings()
    {
        if (!_saveChanges) return;
        try
        {
            _hostSettings.Save(HostPaths.SettingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusBar.ShowMessage(L.T("The folders could not be saved: ", "Les dossiers n'ont pas pu être enregistrés : ") + ex.Message, 8.0);
        }
    }

    private void SaveSettings()
    {
        if (_saveChanges) _settings.Save();
    }

    private void CloseAll()
    {
        _scan?.Cancel();
        foreach (var w in _open.ToList()) w.Window.Close();
    }

    private void SaveAll()
    {
        SaveLayout();
        SaveSettings();
        SaveCatalog();
        _audio.Dispose();
        _loader.Dispose();
    }

    // ---------------------------------------------------------------- helpers

    private DropDown Choice(string[] items, int selected, Action<int> changed, float width)
    {
        var dropDown = new DropDown(items) { Label = string.Empty, Width = width, Height = 26f, SelectedIndex = Math.Clamp(selected, 0, items.Length - 1) };
        dropDown.ValueChanged += (_, _) =>
        {
            changed(dropDown.SelectedIndex);
            Refresh();
        };
        return dropDown;
    }

    private CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox(text, value) { VerticalAlignment = VerticalAlignment.Center };
        box.ValueChanged += (_, _) =>
        {
            changed(box.IsChecked);
            Refresh();
        };
        return box;
    }
}
