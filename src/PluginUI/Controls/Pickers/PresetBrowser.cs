// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// Preset browser for an <see cref="IPresetProvider"/>: a search box, a tree of categories (all presets, favorites and
/// the banks, nested with "/" in their names, each with its count) and the list of matching presets with their bank
/// and a favorite star. Double-click or Enter loads a preset.
/// </summary>
/// <remarks>
/// <para><b>Search.</b> Every word typed must appear in the name or the bank of a preset (case ignored).</para>
/// <para><b>Favorites.</b> Click the star of a row, or press Ctrl+D in the list, to add or remove a favorite.
/// <see cref="Favorites"/> holds their keys (<see cref="PresetKey"/>); save it with the plug-in settings and listen to
/// <see cref="FavoritesChanged"/>.</para>
/// <para><b>Keyboard.</b> Ctrl+F goes to the search box; in the search box, Down goes to the list. The category tree
/// and the list have their own keys (see <see cref="TreeView"/> and <see cref="ListBox"/>).</para>
/// </remarks>
/// <example>
/// <code>
/// var browser = new PresetBrowser { Provider = presets };
/// browser.Favorites.UnionWith(settings.FavoritePresets);
/// browser.FavoritesChanged += b => settings.FavoritePresets = [.. b.Favorites];
/// </code>
/// </example>
public class PresetBrowser : Panel
{
    private const string AllTag = "\u0001all", FavoritesTag = "\u0001favorites";

    private readonly SearchTextBox _search;
    private readonly TreeView _categories;
    private readonly PresetListBox _list;
    private readonly List<int> _visible = new();
    private IPresetProvider? _provider;
    private bool _subscribed;
    private bool _showCategories = true;

    /// <summary>Creates a browser.</summary>
    public PresetBrowser()
    {
        _search = new SearchTextBox { IsSearchBox = true, SelectAllOnFocus = true, CommitOnFocusLost = false };
        _categories = new TreeView();
        _list = new PresetListBox(this) { Items = _visible, ItemHeight = 26f };
        Children.Add(_search);
        Children.Add(_categories);
        Children.Add(_list);
        _search.TextChanged += (_, _) => UpdateList();
        _search.Down = () =>
        {
            if (_visible.Count == 0) return false;
            _list.Focus();
            if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
            return true;
        };
        _categories.SelectionChanged += _ => UpdateList();
        _list.ItemActivated += (_, row) => LoadRow(row);
        _list.SelectionChanged += _ =>
        {
            if (LoadOnSelect && _list.SelectedIndex >= 0) LoadRow(_list.SelectedIndex);
        };
    }

    /// <summary>Creates a browser over a provider.</summary>
    public PresetBrowser(IPresetProvider provider) : this() => Provider = provider;

    /// <summary>Preset source.</summary>
    [Browsable(false)]
    public IPresetProvider? Provider
    {
        get => _provider;
        set
        {
            if (ReferenceEquals(_provider, value)) return;
            Unsubscribe();
            _provider = value;
            if (Host is not null) Subscribe();
            Rebuild();
        }
    }

    /// <summary>Keys of the favorite presets (<see cref="PresetKey"/>). After changing it from code, call <see cref="Rebuild"/>.</summary>
    [Browsable(false)]
    public ISet<string> Favorites { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Width of the category tree.</summary>
    [Category("Layout")]
    public float CategoryWidth { get; set; } = 170f;

    /// <summary>Shows the category tree.</summary>
    [Category("Appearance")]
    public bool ShowCategories
    {
        get => _showCategories;
        set
        {
            _showCategories = value;
            _categories.IsVisible = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Loads a preset as soon as it is selected in the list (default: double-click or Enter).</summary>
    [Category("Behavior")]
    public bool LoadOnSelect { get; set; }

    /// <summary>Text of the search box.</summary>
    [Category("Value")]
    public string SearchText
    {
        get => _search.Text;
        set => _search.Text = value ?? string.Empty;
    }

    /// <summary>The search box.</summary>
    [Browsable(false)]
    public TextBox SearchBox => _search;

    /// <summary>The category tree.</summary>
    [Browsable(false)]
    public TreeView Categories => _categories;

    /// <summary>The list of presets (its items are preset indices).</summary>
    [Browsable(false)]
    public ListBox PresetList => _list;

    /// <summary>Indices of the presets shown in the list, in the order of the provider.</summary>
    [Browsable(false)]
    public IReadOnlyList<int> VisiblePresets => _visible;

    /// <summary>Raised after the browser loaded a preset (its index).</summary>
    public event Action<PresetBrowser, int>? PresetLoaded;

    /// <summary>Raised after the user added or removed a favorite.</summary>
    public event Action<PresetBrowser>? FavoritesChanged;

    private static readonly Size NaturalSize = new(460f, 300f);

    /// <summary>Key of a preset in <see cref="Favorites"/>: its bank and name ("Bass/Sub/Deep one").</summary>
    public string PresetKey(int index)
    {
        if (_provider is null) throw new InvalidOperationException("No provider.");
        string? bank = _provider.GetBank(index);
        return string.IsNullOrEmpty(bank) ? _provider.GetName(index) : bank + "/" + _provider.GetName(index);
    }

    /// <summary><c>true</c> when the preset is a favorite.</summary>
    public bool IsFavorite(int index) => _provider is not null && Favorites.Contains(PresetKey(index));

    /// <summary>Adds the preset to the favorites or removes it.</summary>
    public void ToggleFavorite(int index)
    {
        if (_provider is null || index < 0 || index >= _provider.Count) return;
        string key = PresetKey(index);
        if (!Favorites.Remove(key)) Favorites.Add(key);
        FavoritesChanged?.Invoke(this);
        UpdateCounts();
        if (_categories.SelectedNode?.Tag as string == FavoritesTag) UpdateList();
        _list.InvalidateVisual();
    }

    /// <summary>Selects a category: <c>null</c> for all presets, or a bank path ("Bass/Sub").</summary>
    public void SelectCategory(string? bank)
    {
        string tag = bank ?? AllTag;
        _categories.SelectedNode = _categories.AllNodes().FirstOrDefault(n => n.Tag as string == tag);
    }

    /// <summary>Selects the favorites category.</summary>
    public void SelectFavorites() => _categories.SelectedNode = _categories.AllNodes().FirstOrDefault(n => n.Tag as string == FavoritesTag);

    /// <summary>Re-reads the provider: rebuilds the categories (keeping the selected one) and the list.</summary>
    public void Rebuild()
    {
        string? selectedTag = _categories.SelectedNode?.Tag as string;
        var expanded = new HashSet<string>(_categories.AllNodes().Where(n => n.IsExpanded && n.Tag is string).Select(n => (string)n.Tag!));
        _categories.Nodes.Clear();
        var t = Strings;
        _categories.Nodes.Add(new TreeNode(t.AllPresets, AllTag));
        _categories.Nodes.Add(new TreeNode(t.Favorites, FavoritesTag) { Color = Color.FromRgb(0xFFCA28) });
        if (_provider is not null)
        {
            for (int i = 0; i < _provider.Count; i++)
            {
                if (_provider.GetBank(i) is not { Length: > 0 } bank) continue;
                var nodes = _categories.Nodes;
                string path = string.Empty;
                foreach (string part in bank.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    path = path.Length == 0 ? part : path + "/" + part;
                    var node = nodes.FirstOrDefault(n => n.Tag as string == path);
                    if (node is null)
                    {
                        node = new TreeNode(part, path) { IsExpanded = expanded.Contains(path) };
                        nodes.Add(node);
                    }
                    nodes = node.Children;
                }
            }
        }
        _categories.Refresh();
        _categories.SelectedNode = _categories.AllNodes().FirstOrDefault(n => n.Tag as string == (selectedTag ?? AllTag)) ?? _categories.Nodes[0];
        UpdateCounts();
        UpdateList();
    }

    private void UpdateCounts()
    {
        foreach (var node in _categories.AllNodes())
        {
            int count = 0;
            if (_provider is not null)
            {
                for (int i = 0; i < _provider.Count; i++) if (InCategory(i, node.Tag as string)) count++;
            }
            node.Detail = count.ToString(CultureInfo.InvariantCulture);
        }
        _categories.InvalidateVisual();
    }

    private bool InCategory(int index, string? tag)
    {
        if (tag is null || tag == AllTag) return true;
        if (tag == FavoritesTag) return IsFavorite(index);
        string? bank = _provider!.GetBank(index);
        if (string.IsNullOrEmpty(bank)) return false;
        bank = string.Join('/', bank.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return bank == tag || bank.StartsWith(tag + "/", StringComparison.Ordinal);
    }

    private bool MatchesSearch(int index)
    {
        string text = _search.Text;
        if (string.IsNullOrWhiteSpace(text)) return true;
        string haystack = _provider!.GetName(index) + " " + _provider.GetBank(index);
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!haystack.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private void UpdateList()
    {
        int selectedPreset = _list.SelectedIndex >= 0 && _list.SelectedIndex < _visible.Count ? _visible[_list.SelectedIndex] : -1;
        _visible.Clear();
        if (_provider is not null)
        {
            string? tag = _categories.SelectedNode?.Tag as string;
            for (int i = 0; i < _provider.Count; i++)
            {
                if (InCategory(i, tag) && MatchesSearch(i)) _visible.Add(i);
            }
        }
        _list.Refresh();
        int row = _visible.IndexOf(selectedPreset);
        bool load = LoadOnSelect;
        LoadOnSelect = false; // re-selecting the same preset after a filter change is not a choice of the user
        _list.SelectedIndex = row;
        LoadOnSelect = load;
        _list.EmptyText = Strings.NoPresetFound;
    }

    private void LoadRow(int row)
    {
        if (_provider is null || row < 0 || row >= _visible.Count) return;
        int index = _visible[row];
        _provider.Load(index);
        PresetLoaded?.Invoke(this, index);
        _list.InvalidateVisual();
    }

    // ---------------------------------------------------------------- provider

    private void Subscribe()
    {
        if (_provider is null || _subscribed) return;
        _provider.Changed += OnProviderChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_provider is null || !_subscribed) return;
        _provider.Changed -= OnProviderChanged;
        _subscribed = false;
    }

    private void OnProviderChanged(IPresetProvider provider) => Rebuild();

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Subscribe();
        var t = Strings;
        _search.Placeholder = t.SearchPresets;
        _search.AutomationName ??= t.SearchPresets;
        _categories.AutomationName ??= t.PresetCategories;
        _list.AutomationName ??= t.Presets;
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        Unsubscribe();
        base.OnDetached();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !e.Control || e.Key != Key.F) return;
        _search.Focus();
        e.Handled = true;
    }

    // ---------------------------------------------------------------- layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        foreach (var child in Children) child.Measure(available);
        return NaturalSize;
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var r = bounds.Deflate(Padding);
        float searchHeight = 28f, gap = 6f;
        _search.Arrange(new Rect(r.X, r.Y, r.Width, searchHeight));
        float top = r.Y + searchHeight + gap;
        float treeWidth = _showCategories ? MathF.Min(CategoryWidth, r.Width * 0.45f) : 0f;
        if (_showCategories) _categories.Arrange(Rect.FromEdges(r.X, top, r.X + treeWidth, r.Bottom));
        _list.Arrange(Rect.FromEdges(_showCategories ? r.X + treeWidth + gap : r.X, top, r.Right, r.Bottom));
    }

    // ---------------------------------------------------------------- children

    /// <summary>The search box: Down goes to the list.</summary>
    private sealed class SearchTextBox : TextBox
    {
        public Func<bool>? Down { get; set; }

        protected internal override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Down && !e.Handled && Down?.Invoke() == true)
            {
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }
    }

    /// <summary>The list of presets: name, bank, favorite star, current preset highlighted.</summary>
    private sealed class PresetListBox : ListBox
    {
        private const float StarWidth = 26f;
        private readonly PresetBrowser _owner;

        public PresetListBox(PresetBrowser owner)
        {
            _owner = owner;
            ItemText = o => Describe((int)o);
            ItemRenderer = RenderRow;
        }

        private string Describe(int index)
        {
            var provider = _owner._provider;
            if (provider is null || index >= provider.Count) return string.Empty;
            string text = provider.GetName(index);
            if (provider.GetBank(index) is { Length: > 0 } bank) text += ", " + bank;
            if (_owner.IsFavorite(index)) text += ", " + Strings.Favorite;
            if (index == provider.CurrentIndex) text += ", " + Strings.CurrentPreset;
            return text;
        }

        private void RenderRow(DrawingContext dc, Rect row, object item, ListItemState state)
        {
            var provider = _owner._provider;
            int index = (int)item;
            if (provider is null || index >= provider.Count) return;
            var p = Theme.Palette;
            bool current = index == provider.CurrentIndex;
            var star = new Rect(row.X + 4f, row.Y, StarWidth - 6f, row.Height);
            DrawStar(dc, star.Center, 6f, _owner.IsFavorite(index) ? Color.FromRgb(0xFFCA28) : null, p.TextDim.WithAlpha(0.6f));
            var text = Theme.Typography.Normal with { Weight = current ? FontWeight.SemiBold : FontWeight.Normal };
            var small = Theme.Typography.Small with { Align = HorizontalAlignment.Right };
            string? bank = provider.GetBank(index);
            float bankWidth = string.IsNullOrEmpty(bank) ? 0f : MathF.Min(row.Width * 0.4f, TextMeasurer.Measure(bank, small).Width + 8f);
            string name = provider.GetName(index) + (current && provider.IsModified ? " *" : string.Empty);
            dc.DrawText(name, Rect.FromEdges(row.X + StarWidth, row.Y, row.Right - bankWidth - 10f, row.Bottom), text, current ? Theme.Palette.Accent : p.Text.WithAlpha(state.IsSelected ? 1f : 0.88f));
            if (bankWidth > 0f) dc.DrawText(bank!, Rect.FromEdges(row.Right - bankWidth - 8f, row.Y, row.Right - 8f, row.Bottom), small, p.TextDim);
        }

        private static void DrawStar(DrawingContext dc, Point c, float r, Color? fill, Color outline)
        {
            var points = new Point[10];
            for (int i = 0; i < 10; i++)
            {
                float radius = i % 2 == 0 ? r : r * 0.45f;
                float angle = -MathF.PI / 2f + i * MathF.PI / 5f;
                points[i] = new Point(c.X + radius * MathF.Cos(angle), c.Y + radius * MathF.Sin(angle));
            }
            if (fill is { } f) dc.FillPolygon(points, f);
            else dc.DrawPolyline([.. points, points[0]], outline, 1f, StrokeStyle.Round);
        }

        private int RowAt(Point p)
        {
            if (Count == 0) return -1;
            var first = GetRowBounds(0);
            int row = (int)MathF.Floor((p.Y - first.Y) / ItemHeight);
            return row >= 0 && row < Count && Bounds.Contains(p) ? row : -1;
        }

        protected internal override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButton.Left && !e.Handled && RowAt(e.Position) is var row and >= 0 && e.Position.X < GetRowBounds(row).X + StarWidth)
            {
                _owner.ToggleFavorite(_owner._visible[row]);
                e.Handled = true;
                return;
            }
            base.OnMouseDown(e);
        }

        protected internal override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.Key == Key.D && SelectedIndex >= 0)
            {
                _owner.ToggleFavorite(_owner._visible[SelectedIndex]);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }
    }
}
