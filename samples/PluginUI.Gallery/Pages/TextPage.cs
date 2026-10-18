// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI.Gallery.Pages;

/// <summary>Text, value entry and pickers.</summary>
internal static class TextPage
{
    public static UIElement Build()
    {
        var c = new Canvas();
        c.Caption(L.T("TEXT", "TEXTE"), 20, 12);
        c.At(new Label(L.T("Section title", "Titre de section")) { TextStyle = new TextStyle("Segoe UI", 18f, FontWeight.SemiBold) }, 20, 36, 300, 26);
        c.At(new Label(L.T("Multi-line label: the text wraps at word boundaries and resizes with its container.", "Libellé multiligne : le texte revient à la ligne sur les limites de mots et se dimensionne avec son conteneur.")) { Wrap = true, IsDim = true }, 20, 66, 300, 54);
        c.At(new Hyperlink("github.com/iPlug2/iPlug2", "https://github.com/iPlug2/iPlug2"), 20, 126, 220, 18);
        c.At(new Label { TextSource = () => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), TextStyle = new TextStyle("Consolas", 14f) }, 260, 126, 100, 18);

        c.Caption(L.T("VALUES", "VALEURS"), 380, 12);
        var cutoff = new LocalParameter(L.T("Cutoff", "Coupure"), 20, 20000, 1000, "Hz", ParameterScale.Logarithmic(20, 20000), decimals: 0);
        var gain = new LocalParameter("Gain", -24, 24, 3.5, "dB", decimals: 1);
        c.At(new ValueDisplay { Parameter = cutoff, ToolTip = L.T("Double-click or Enter to type a value (e.g. 2k)", "Double-clic ou Entrée pour saisir une valeur (ex. 2k)") }, 380, 36, 110, 44);
        c.At(new NumberBox { Parameter = gain }, 500, 36, 130, 44);
        c.At(new SegmentDisplay { Text = "128.5", DigitCount = 5 }, 640, 36, 140, 48);
        c.At(new SegmentDisplay { Text = "-12", DigitCount = 3, LitColor = Color.FromRgb(0xFF5252) }, 790, 36, 84, 48);

        c.Caption(L.T("INPUT", "SAISIE"), 20, 170);
        c.At(new TextBox { Placeholder = L.T("Search a preset…", "Rechercher un preset…"), IsSearchBox = true }, 20, 194, 240);
        c.At(new TextBox { Text = L.T("Initial patch", "Patch initial") }, 280, 194, 200);
        c.At(new TextBox { Text = L.T("Read-only", "Lecture seule"), IsReadOnly = true }, 500, 194, 160);

        c.Caption(L.T("SELECTORS", "SÉLECTEURS"), 20, 250);
        c.At(new ColorSwatch(Color.FromRgb(0xE91E63)) { ToolTip = L.T("Click: color picker", "Clic : sélecteur de couleur") }, 20, 274);
        c.At(new ColorSwatch(Color.FromRgb(0x4CAF50, 0.5f)), 70, 274);
        c.At(new FilePathBox { Path = L.T(@"C:\Users\Me\Documents\Samples\Drums\Kick_808_very_long_name.wav", @"C:\Users\Moi\Documents\Échantillons\Batterie\Kick_808_tres_long_nom.wav") }, 130, 274, 330);
        c.At(new FilePathBox { Mode = FilePathMode.Folder }, 480, 274, 180);

        var presets = new PresetList([new("Init"), new(L.T("Warm pad", "Nappe chaude"), L.T("Factory", "Usine"), null, true), new(L.T("Big lead", "Gros lead"), L.T("Factory", "Usine"), null, true), new(L.T("My bass", "Ma basse"), L.T("User", "Utilisateur"))]);
        presets.Load(2);
        c.Caption("PRESETS", 20, 330);
        c.At(new PresetBar(presets), 20, 354, 380);

        c.Caption(L.T("INLINE COLOR", "COULEUR EN LIGNE"), 460, 330);
        c.At(new ColorPicker(Color.FromRgb(0x4FC3F7)), 460, 354, 300, 230);
        return c;
    }
}
