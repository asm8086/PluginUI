// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Patterns: the mini piano roll with a bass line, and drum pads lit by a drum pattern.</summary>
internal static class SequencerPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();
        c.Caption(L.T("PATTERN EDITOR (CLICK TO ADD, DRAG TO MOVE OR LENGTHEN, DOUBLE-CLICK TO DELETE, SHIFT+DRAG TO SELECT)",
                      "ÉDITEUR DE MOTIF (CLIC : AJOUTER, GLISSER : DÉPLACER OU ALLONGER, DOUBLE-CLIC : SUPPRIMER, MAJ+GLISSER : SÉLECTIONNER)"), 20, 12, 850);
        var roll = c.At(new PatternEditor
        {
            LengthBeats = 16,
            LowestVisibleNote = 38,
            VisibleNoteCount = 26,
            PlayPositionSource = () => audio.PatternBeat,
            AutomationName = L.T("Bass pattern", "Motif de basse"),
        }, 20, 36, 850, 270);
        // A bass line over four bars: root, fifth and octave, a passing note at the end of each bar.
        int[] roots = [45, 48, 41, 43];
        var notes = new List<PatternNote>();
        for (int bar = 0; bar < 4; bar++)
        {
            int r = roots[bar];
            double b = bar * 4.0;
            notes.Add(new PatternNote(r, b, 0.75, 0.95f));
            notes.Add(new PatternNote(r, b + 1.0, 0.25, 0.6f));
            notes.Add(new PatternNote(r + 7, b + 1.5, 0.5, 0.75f));
            notes.Add(new PatternNote(r + 12, b + 2.0, 0.5, 0.85f));
            notes.Add(new PatternNote(r + 7, b + 2.75, 0.25, 0.55f));
            notes.Add(new PatternNote(r + 10, b + 3.0, 0.5, 0.7f));
            notes.Add(new PatternNote(r + (bar == 3 ? 14 : 2), b + 3.5, 0.5, 0.65f));
        }
        roll.SetNotes(notes);
        roll.Select(roll.Notes.Where(n => n.Start >= 8.0 && n.Start < 12.0));

        c.Caption(L.T("DRUM PADS (MOUSE, 1 2 3 4 / Q W E R / A S D F / Z X C V, ARROWS AND SPACE)",
                      "PADS DE BATTERIE (SOURIS, 1 2 3 4 / Q W E R / A S D F / Z X C V, FLÈCHES ET ESPACE)"), 20, 318, 850);
        var pads = c.At(new DrumPads { AutomationName = L.T("Drum pads", "Pads de batterie") }, 20, 342, 330, 190);
        string[] names =
        [
            L.T("Kick", "Grosse caisse"), L.T("Side stick", "Cross stick"), L.T("Snare", "Caisse claire"), L.T("Clap", "Clap"),
            L.T("Snare 2", "Caisse claire 2"), L.T("Low tom", "Tom grave"), L.T("Closed hat", "Charley fermé"), L.T("Low tom 2", "Tom grave 2"),
            L.T("Pedal hat", "Charley pédale"), L.T("Mid tom", "Tom médium"), L.T("Open hat", "Charley ouvert"), L.T("Mid tom 2", "Tom médium 2"),
            L.T("High tom", "Tom aigu"), L.T("Crash", "Crash"), L.T("High tom 2", "Tom aigu 2"), L.T("Ride", "Ride"),
        ];
        Color[] colors = [Color.FromRgb(0xFF7043), Color.FromRgb(0xFFCA28), Color.FromRgb(0x26C6DA), Color.FromRgb(0xAB47BC)];
        for (int i = 0; i < pads.Pads.Count; i++)
        {
            pads.Pads[i].Name = names[i];
            pads.Pads[i].Color = colors[i / 4];
        }
        audio.Pads = pads;

        c.At(new Label(L.T("The pattern editor follows a simulated player at 120 BPM; the pads light up with the drum pattern (kick, snare, hi-hat). " +
                           "Notes and pads raise events: the plug-in plays them and saves the pattern with its state.",
                           "L'éditeur de motif suit un lecteur simulé à 120 BPM ; les pads s'allument avec le rythme de batterie (grosse caisse, caisse claire, charley). " +
                           "Les notes et les pads déclenchent des événements : le plug-in les joue et enregistre le motif avec son état."))
        { Wrap = true, IsDim = true }, 380, 346, 490, 100);
        return c;
    }
}
