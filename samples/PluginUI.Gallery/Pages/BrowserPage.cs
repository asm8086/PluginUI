// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Browsing: a preset browser over a preset list, and a tree of sample folders.</summary>
internal static class BrowserPage
{
    public static UIElement Build()
    {
        var c = new Canvas();
        c.Caption(L.T("PRESET BROWSER (SEARCH, CATEGORIES, STAR = FAVORITE, DOUBLE-CLICK TO LOAD)",
                      "NAVIGATEUR DE PRESETS (RECHERCHE, CATÉGORIES, ÉTOILE = FAVORI, DOUBLE-CLIC : CHARGER)"), 20, 12, 560);
        (string Bank, string[] Names)[] banks =
        [
            ("Bass/Sub", ["Deep sub", "808 tail", "Sine floor", "Round sub"]),
            ("Bass/Reese", ["Reese classic", "Wide reese", "Dirty reese"]),
            ("Bass/Plucked", ["Finger bass", "Slap", "Muted pick"]),
            (L.T("Keys", "Claviers"), ["Glass piano", "Electric 73", "Toy box", "Bell keys", "Soft upright"]),
            (L.T("Pads/Warm", "Nappes/Chaudes"), ["Warm pad", "Velvet", "Analog strings", "Choir pad"]),
            (L.T("Pads/Evolving", "Nappes/Évolutives"), ["Slow motion", "Tidal", "Glacier", "Night drive"]),
            ("Leads", ["Saw lead", "Square lead", "Sync scream", "Flute lead", "Soft whistle"]),
            ("FX", ["Riser", "Downlifter", "Noise sweep", "Impact"]),
        ];
        var entries = new List<PresetEntry>();
        foreach (var (bank, names) in banks)
        {
            foreach (string name in names) entries.Add(new PresetEntry(name, bank, IsFactory: true));
        }
        var presets = new PresetList(entries);
        var browser = c.At(new PresetBrowser(presets) { AutomationName = L.T("Preset browser", "Navigateur de presets") }, 20, 36, 560, 470);
        browser.Favorites.UnionWith(["Bass/Sub/Deep sub", L.T("Pads/Warm", "Nappes/Chaudes") + "/Velvet", "Leads/Sync scream"]);
        presets.Load(entries.FindIndex(e => e.Name == "Velvet"));
        browser.Rebuild();
        browser.SelectCategory(L.T("Pads", "Nappes"));
        browser.Categories.SetExpanded(browser.Categories.SelectedNode!, true);

        c.Caption(L.T("TREE VIEW (ARROWS, FILTER)", "ARBORESCENCE (FLÈCHES, FILTRE)"), 600, 12, 270);
        var filter = c.At(new TextBox { IsSearchBox = true, Placeholder = L.T("Filter", "Filtrer") }, 600, 36, 270, 28);
        var tree = c.At(new TreeView { AutomationName = L.T("Samples", "Échantillons") }, 600, 70, 270, 436);
        var drums = tree.Nodes.Add(L.T("Drums", "Batterie"));
        drums.Color = Color.FromRgb(0xFF7043);
        foreach (var (name, count) in new[] { ("Kicks", "48"), ("Snares", "36"), ("Hats", "52"), ("Percussion", "27") }) drums.Add(name).Detail = count;
        drums.IsExpanded = true;
        var loops = tree.Nodes.Add("Loops");
        loops.Color = Color.FromRgb(0x26C6DA);
        var breaks = loops.Add("Breaks");
        foreach (var (name, count) in new[] { ("90 BPM", "12"), ("120 BPM", "18"), ("174 BPM", "22") }) breaks.Add(name).Detail = count;
        loops.Add(L.T("Textures", "Textures")).Detail = "9";
        loops.IsExpanded = true;
        breaks.IsExpanded = true;
        var vocals = tree.Nodes.Add(L.T("Vocals", "Voix"));
        vocals.Color = Color.FromRgb(0xAB47BC);
        vocals.Add(L.T("Phrases", "Phrases")).Detail = "15";
        vocals.Add(L.T("Chops", "Découpes")).Detail = "40";
        tree.Nodes.Add(L.T("One shots", "Coups uniques")).Detail = "64";
        tree.SelectedNode = breaks.Children[1];
        filter.TextChanged += (_, text) => tree.Filter = text;
        return c;
    }
}
