// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Matrices: a modulation matrix fed by draggable sources, and a routing matrix.</summary>
internal static class MatrixPage
{
    public static UIElement Build()
    {
        var c = new Canvas();
        c.Caption(L.T("MODULATION MATRIX (CLICK A SOURCE OR A TARGET, DRAG THE AMOUNT, DROP A SOURCE TO ADD A SLOT)",
                      "MATRICE DE MODULATION (CLIC SUR UNE SOURCE OU UNE DESTINATION, GLISSER L'INTENSITÉ, DÉPOSER UNE SOURCE)"), 20, 12, 850);
        ModulationSource[] sources =
        [
            new("LFO 1", Color.FromRgb(0x4FC3F7)), new("LFO 2", Color.FromRgb(0x26C6DA)), new(L.T("Env 2", "Env 2"), Color.FromRgb(0xFFCA28)),
            new(L.T("Velocity", "Vélocité"), Color.FromRgb(0xFF7043)), new(L.T("Mod wheel", "Molette"), Color.FromRgb(0xAB47BC)),
        ];
        for (int i = 0; i < sources.Length; i++) c.At(new ModulationSourceButton { Source = sources[i] }, 20 + i * 92, 36, 84, 24);

        var matrix = c.At(new ModulationMatrix { MaxSlots = 8, AutomationName = L.T("Modulation matrix", "Matrice de modulation") }, 20, 68, 560, 190);
        foreach (var s in sources) matrix.Sources.Add(s);
        string osc = L.T("Oscillator", "Oscillateur"), filter = L.T("Filter", "Filtre"), amp = "Amp";
        ModulationTarget[] targets =
        [
            new(L.T("Pitch", "Hauteur"), osc), new(L.T("Wavetable position", "Position de table"), osc), new(L.T("Fine", "Accord fin"), osc),
            new(L.T("Cutoff", "Coupure"), filter), new(L.T("Resonance", "Résonance"), filter), new(L.T("Drive", "Saturation"), filter),
            new(L.T("Level", "Niveau"), amp), new(L.T("Pan", "Panoramique"), amp),
        ];
        foreach (var t in targets) matrix.Targets.Add(t);
        matrix.Slots.Add(new ModulationSlot(sources[0], targets[3], 0.42));
        matrix.Slots.Add(new ModulationSlot(sources[2], targets[1], -0.3));
        matrix.Slots.Add(new ModulationSlot(sources[3], targets[6], 0.65));
        matrix.Slots.Add(new ModulationSlot(sources[4], targets[4], 0.2) { IsEnabled = false });
        var amount = new LocalParameter(L.T("Slot 1 amount", "Intensité 1"), -100, 100, 42, "%");
        matrix.Slots[0].AmountParameter = amount;
        c.At(new Knob(amount) { IsBipolar = true, ShowValueBubble = true, Label = L.T("SLOT 1", "EMPL. 1") }, 600, 68, 92, 96);
        c.At(new Label(L.T("The knob and the first slot share their amount parameter.", "Le bouton et le premier emplacement partagent leur paramètre d'intensité."))
        { Wrap = true, IsDim = true }, 700, 76, 170, 70);

        c.Caption(L.T("ROUTING MATRIX (CLICK OR DRAG ACROSS THE CROSSINGS)", "MATRICE DE ROUTAGE (CLIC OU GLISSER SUR LES CROISEMENTS)"), 20, 276, 560);
        var router = c.At(new RoutingMatrix { AutomationName = L.T("Output routing", "Routage des sorties") }, 20, 300, 400, 220);
        router.Inputs.AddRange([L.T("Main L", "Principale G"), L.T("Main R", "Principale D"), L.T("Side L", "Latérale G"), L.T("Side R", "Latérale D"), L.T("FX return", "Retour FX")]);
        router.Outputs.AddRange(["Out 1", "Out 2", "Out 3", "Out 4", "Out 5", "Out 6"]);
        foreach (var (i, o) in new[] { (0, 0), (1, 1), (2, 2), (3, 3), (4, 0), (4, 1) }) router.SetConnected(i, o, true);
        c.At(new Label(L.T("Free mode: an input can feed several outputs. RoutingMode.OneInputPerOutput makes a channel router, " +
                           "RoutingMode.OneOutputPerInput a pin connector.",
                           "Mode libre : une entrée peut alimenter plusieurs sorties. RoutingMode.OneInputPerOutput en fait un routeur de canaux, " +
                           "RoutingMode.OneOutputPerInput un connecteur de broches."))
        { Wrap = true, IsDim = true }, 440, 306, 430, 90);
        return c;
    }
}
