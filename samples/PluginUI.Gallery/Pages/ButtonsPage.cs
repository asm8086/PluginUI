// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Buttons, switches, segmented controls, menus.</summary>
internal static class ButtonsPage
{
    public static UIElement Build()
    {
        var c = new Canvas();
        c.Caption(L.T("BUTTONS", "BOUTONS"), 20, 12);
        c.At(new Button("Normal"), 20, 36);
        c.At(new Button("Accent") { Appearance = ButtonAppearance.Accent }, 110, 36);
        c.At(new Button(L.T("Flat", "Plat")) { Appearance = ButtonAppearance.Flat }, 200, 36);
        c.At(new Button(L.T("Outline", "Contour")) { Appearance = ButtonAppearance.Outline }, 260, 36);
        c.At(new Button(L.T("Icon", "Icône")) { IconRenderer = (dc, r, col) => Glyphs.Hamburger(dc, r.Center, 12, col) }, 360, 36);
        c.At(new Button(L.T("Disabled", "Désactivé")) { IsEnabled = false }, 460, 36);

        c.Caption(L.T("TOGGLES", "BASCULES"), 20, 84);
        c.At(new ToggleButton("Bypass") { IsOn = true }, 20, 108, 90);
        c.At(new ToggleButton { OnText = "ON", OffText = "OFF" }, 120, 108, 70);
        c.At(new ToggleButton("Sync") { ShowLed = true, IsOn = true }, 200, 108, 90);
        c.At(new CheckBox(L.T("Oversampling", "Suréchantillonnage"), true), 310, 108);
        c.At(new CheckBox(L.T("Unchecked", "Non coché")), 310, 132);
        c.At(new SlideSwitch(L.T("Stereo", "Stéréo"), true), 480, 108);
        c.At(new SlideSwitch("Mono"), 480, 136);

        c.Caption(L.T("SELECTORS", "SÉLECTEURS"), 20, 176);
        c.At(new SegmentedControl(L.T("Low", "Grave"), L.T("Mid", "Médium"), L.T("High", "Aigu"), "Air") { Label = L.T("BAND", "BANDE"), SelectedIndex = 1 }, 20, 200, 260);
        c.At(new SegmentedControl("A", "B", "C") { Orientation = Orientation.Vertical, SelectedIndex = 2 }, 300, 200, 50, 72);
        c.At(new RadioGroup(L.T("Sine", "Sinus"), "Triangle", L.T("Saw", "Dent de scie"), L.T("Square", "Carré")) { Label = L.T("SHAPE", "FORME"), SelectedIndex = 2 }, 370, 200);
        c.At(new Switch("x1", "x2", "x4") { Label = L.T("OS", "SUR-ÉCH."), SelectedIndex = 1 }, 520, 200);
        c.At(new Switch(L.T("Off", "Arrêt"), L.T("Low", "Bas"), L.T("High", "Haut")) { Orientation = Orientation.Horizontal, SelectedIndex = 2 }, 20, 254, 170);
        c.At(new RockerSwitch { Label = "OCT", Parameter = new LocalParameter("Octave", -2, 2, 0, stepCount: 4, decimals: 0) }, 600, 200, 30);
        c.At(new RockerSwitch { Orientation = Orientation.Horizontal }, 210, 260);

        c.Caption(L.T("GATE SEQUENCER", "SÉQUENCEUR À PORTES"), 20, 304);
        var gates = new MultiToggle(16) { PlayPosition = 5 };
        for (int i = 0; i < 16; i += 3) gates[i] = true;
        c.At(gates, 20, 328, 420, 22);

        c.Caption("MENUS", 20, 374);
        var patch = new DropDown(L.T("Bass/Sub", "Basse/Sub"), L.T("Bass/Reese", "Basse/Reese"), L.T("Lead/Saw", "Lead/Scie"), L.T("Lead/Square", "Lead/Carré"), L.T("Pad/Warm", "Nappe/Chaude")) { SubmenuSeparator = '/', Label = "PATCH", SelectedIndex = 2 };
        c.At(patch, 20, 398, 180);
        var menu = new MenuItem(L.T("Export", "Exporter"));
        menu.Items.Add(new MenuItem("WAV"));
        menu.Items.Add(new MenuItem("FLAC"));
        c.At(new MenuButton(MenuButtonStyle.Hamburger, new MenuItem(L.T("Open…", "Ouvrir…")) { ShortcutText = "Ctrl+O" }, new MenuItem(L.T("Save", "Enregistrer")) { ShortcutText = "Ctrl+S" }, MenuItem.Separator(), menu), 220, 412);
        c.At(new MenuButton(MenuButtonStyle.Ellipsis, new MenuItem(L.T("Copy", "Copier")), new MenuItem(L.T("Paste", "Coller")) { IsEnabled = false }), 256, 412);
        c.At(new MenuButton(MenuButtonStyle.TextWithChevron, new MenuItem(L.T("New", "Nouveau")), new MenuItem(L.T("Recent", "Récents")) { IsChecked = true }) { Text = L.T("File", "Fichier") }, 292, 412);
        var context = new Label(L.T("Right-click here: context menu", "Clic droit ici : menu contextuel")) { Background = Color.FromRgb(0x2A2D33), CornerRadius = 4, Padding = 8 };
        context.ContextMenu = [new MenuItem(L.T("Reset", "Réinitialiser")), new MenuItem(L.T("Copy value", "Copier la valeur")), MenuItem.Separator(), new MenuItem(L.T("MIDI learn", "Apprentissage MIDI")) { IsChecked = true }];
        c.At(context, 420, 404, 260, 36);
        return c;
    }
}
