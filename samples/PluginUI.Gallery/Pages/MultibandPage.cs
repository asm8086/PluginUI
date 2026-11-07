// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI.Gallery.Pages;

/// <summary>Multiband processing: the crossover editor, its bands bound to parameters that knobs share.</summary>
internal static class MultibandPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();
        c.Caption(L.T("CROSSOVER (DRAG THE SPLITS AND THE GAIN LINES, S / M = SOLO / MUTE, KEYS: PAGE UP / DOWN, ARROWS, S, M)",
                      "SÉPARATEUR DE BANDES (GLISSER LES SÉPARATIONS ET LES LIGNES DE GAIN, S / M = SOLO / MUET, TOUCHES : PAGE PRÉC. / SUIV., FLÈCHES, S, M)"), 20, 12, 850);
        var crossover = c.At(new CrossoverEditor { BandCount = 4, SpectrumSource = audio.Ring, AutomationName = L.T("Crossover", "Séparateur de bandes") }, 20, 36, 850, 260);
        string[] names = [L.T("Low", "Grave"), L.T("Low mid", "Bas médium"), L.T("High mid", "Haut médium"), L.T("High", "Aigu")];
        double[] splits = [120, 900, 5200];
        double[] gains = [2.5, -3.0, 0.0, 1.5];
        for (int i = 0; i < 3; i++)
        {
            var split = new LocalParameter(L.T($"Split {i + 1}", $"Séparation {i + 1}"), scale: ParameterScale.Logarithmic(20, 20000), defaultValue: splits[i], units: "Hz");
            crossover.Splits[i].FrequencyParameter = split;
        }

        c.Caption(L.T("BAND GAINS (THE SAME PARAMETERS AS THE GAIN LINES)", "GAINS DES BANDES (LES MÊMES PARAMÈTRES QUE LES LIGNES DE GAIN)"), 20, 312, 850);
        for (int i = 0; i < 4; i++)
        {
            var gain = new LocalParameter(names[i], -24, 24, gains[i], "dB");
            crossover.Bands[i].Name = names[i];
            crossover.Bands[i].GainParameter = gain;
            c.At(new Knob(gain) { IsBipolar = true, ShowValueBubble = true, Label = names[i].ToUpper(CultureInfo.InvariantCulture) }, 20 + i * 100, 336, 92, 96);
        }
        c.At(new Label(L.T("The editor and the knobs share the parameters: a change on one side shows on the other at the next frame, as host automation does.",
                           "L'éditeur et les boutons partagent les paramètres : une modification d'un côté apparaît de l'autre à l'image suivante, comme l'automation de l'hôte."))
        { Wrap = true, IsDim = true }, 440, 340, 430, 60);
        return c;
    }
}
