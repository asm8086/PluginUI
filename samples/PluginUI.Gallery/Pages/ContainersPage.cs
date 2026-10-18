// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI.Gallery.Pages;

/// <summary>Group boxes, nested tabs, list, table, split view, scroll viewer, images.</summary>
internal static class ContainersPage
{
    public static UIElement Build()
    {
        var root = new Grid("*,190", "250,*,*") { Padding = 12f, RowSpacing = 12f, ColumnSpacing = 12f };

        var group = new GroupBox(L.T("OSCILLATOR", "OSCILLATEUR"), new StackPanel(Orientation.Vertical, 6f)
            .Add(new DialogText(L.T("Two detuned saws, sub-oscillator and noise.", "Deux dents de scie désaccordées, sous-oscillateur et bruit.")),
                 new Separator(),
                 new DialogText(L.T("Unison: 7 voices", "Unisson : 7 voix")) { Color = Color.FromRgb(0x9AA0AA) })) { IsCollapsible = true, HeaderAccent = Color.FromRgb(0x4FC3F7) };
        var collapsed = new GroupBox(L.T("FILTER", "FILTRE"), new DialogText(L.T("hidden", "caché"))) { IsCollapsible = true, IsCollapsed = true };
        var tabs = new TabView();
        tabs.AddPage(L.T("Synth", "Synthé"), new StackPanel(Orientation.Vertical, 10f).Add(group, collapsed));
        tabs.AddPage(L.T("Effects", "Effets"), new DialogText(L.T("Effects page", "Page des effets")));
        tabs.AddPage(L.T("Settings", "Réglages"), new DialogText(L.T("Settings page", "Page des réglages")));
        tabs.Pages[2].IsEnabled = false;
        root.Add(tabs, 0, 0);

        var presets = new List<string>();
        string[] kinds = [L.T("Warm pad", "Nappe chaude"), L.T("Bright lead", "Lead brillant"), L.T("Deep bass", "Basse profonde")];
        for (int i = 0; i < 200; i++) presets.Add($"Preset {i + 1:000} - {kinds[i % 3]}");
        root.Add(new ListBox(presets) { MultiSelect = true, SelectedIndex = 2, ToolTip = L.T("Virtualized list of 200 items", "Liste virtualisée de 200 éléments") }, 0, 1);

        var rows = new List<object>();
        string[] names = [L.T("Kick", "Grosse caisse"), L.T("Snare", "Caisse claire"), L.T("Hi-hat", "Charleston"), "Clap", L.T("Low tom", "Tom grave"), "Ride", "Crash", "Shaker", L.T("Wood block", "Bois"), "Rim shot", L.T("Cowbell", "Cloche"), "Conga"];
        for (int i = 0; i < names.Length * 3; i++) rows.Add(new Sample(names[i % names.Length] + (i >= names.Length ? $" v{i / names.Length + 1}" : ""), 0.1 + (i * 37 % 23) / 10.0, "C" + (i % 4 + 1)));
        var table = new DataTable { Items = rows, MultiSelect = true };
        table.AddColumn(new DataColumn(L.T("Name", "Nom"), o => ((Sample)o).Name).SortByText());
        table.AddColumn(new DataColumn(L.T("Length", "Durée"), o => ((Sample)o).Length.ToString("0.00 s", CultureInfo.InvariantCulture), 80f)
        {
            Alignment = HorizontalAlignment.Right,
            Comparison = (a, b) => ((Sample)a).Length.CompareTo(((Sample)b).Length),
        });
        table.AddColumn(new DataColumn("Note", o => ((Sample)o).Key, 56f) { Alignment = HorizontalAlignment.Center, CommitEdit = (o, s) => ((Sample)o).Key = s });
        table.Sort(table.Columns[1], true);
        root.Add(table, 0, 2);

        var left = new Border(new DialogText(L.T("Left pane: drag the splitter", "Panneau gauche : faites glisser la séparation"))) { Background = Color.FromRgb(0x26282E), CornerRadius = 6f, Padding = 10f };
        var right = new Border(new DialogText(L.T("Right pane", "Panneau droit"))) { Background = Color.FromRgb(0x2B2E35), CornerRadius = 6f, Padding = 10f };
        root.Add(new SplitView(left, right) { Ratio = 0.45f }, 1, 0);

        var tall = new StackPanel(Orientation.Vertical, 4f) { Padding = 8f };
        for (int i = 0; i < 30; i++) tall.Add(new DialogText(L.T("Scrolling line ", "Ligne défilante ") + (i + 1).ToString(CultureInfo.InvariantCulture)));
        root.Add(new ScrollViewer(tall) { VerticalScrollBar = ScrollBarVisibility.Visible, Background = Color.FromRgb(0x222429), CornerRadius = 6f }, 1, 1);

        var images = new Grid("*", "*,*") { ColumnSpacing = 8f };
        images.Add(new ImageView(Art.Disc(96)) { Label = "ImageView" }, 0, 0);
        images.Add(new SvgView(SvgImage.FromString(Art.LogoSvg)) { Label = "SvgView" }, 0, 1);
        root.Add(images, 1, 2);
        return root;
    }

    private sealed class Sample(string name, double length, string key)
    {
        public string Name { get; } = name;
        public double Length { get; } = length;
        public string Key { get; set; } = key;
    }
}
