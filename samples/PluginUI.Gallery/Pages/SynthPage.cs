// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Synthesizer tools: a wavetable shown in 3D and alone, sharing its position parameter, and two tuners.</summary>
internal static class SynthPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();
        c.Caption(L.T("WAVETABLE (DRAG UP / DOWN TO SCAN THE FRAMES; BOTH VIEWS AND THE KNOB SHARE THE POSITION)",
                      "TABLE D'ONDES (GLISSER VERS LE HAUT / LE BAS POUR PARCOURIR ; LES DEUX VUES ET LE BOUTON PARTAGENT LA POSITION)"), 20, 12, 850);
        var position = new LocalParameter(L.T("Wave position", "Position d'onde"), 0, 100, 38, "%");
        var frames = WavetableView.CreateMorph(64, 256, 24);
        var stack = c.At(new WavetableView { Parameter = position, Label = "", AutomationName = L.T("Wavetable", "Table d'ondes") }, 20, 36, 420, 230);
        stack.SetFrames(frames);
        var single = c.At(new WavetableView { Parameter = position, Display = WavetableDisplay.Single, Label = "", AutomationName = L.T("Current frame", "Trame courante") }, 456, 36, 300, 230);
        single.SetFrames(frames);
        c.At(new Knob(position) { ShowValueBubble = true, Label = L.T("POSITION", "POSITION") }, 772, 86, 92, 96);

        c.Caption(L.T("TUNERS (A DRIFTING LOW A; GREEN WITHIN ±3 CENTS)", "ACCORDEURS (UN LA GRAVE QUI DÉRIVE ; VERT À ±3 CENTS PRÈS)"), 20, 286, 850);
        c.At(new Tuner { Source = audio.StringRing, AutomationName = L.T("Tuner", "Accordeur") }, 20, 310, 300, 180);
        c.At(new Tuner { Source = audio.StringRing, Look = TunerStyle.Strip, AutomationName = L.T("Strip tuner", "Accordeur à diodes") }, 340, 310, 300, 120);
        c.At(new Label(L.T("Pitch detection with YIN (PluginUI.Dsp.PitchDetector) on the UI thread, from an AudioRingBuffer the processor fills. " +
                           "A plug-in that measures the pitch itself sets FrequencySource instead.",
                           "Détection de hauteur par YIN (PluginUI.Dsp.PitchDetector) sur le thread de l'interface, depuis un AudioRingBuffer rempli par le processeur. " +
                           "Un plug-in qui mesure lui-même la hauteur renseigne FrequencySource à la place."))
        { Wrap = true, IsDim = true }, 660, 316, 210, 170);
        return c;
    }
}
