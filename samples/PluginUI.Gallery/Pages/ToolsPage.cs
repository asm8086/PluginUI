// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Tools: timeline rulers, spin boxes and toast notifications.</summary>
internal static class ToolsPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();
        c.Caption(L.T("TIMELINE (CLICK TO MOVE THE PLAY POSITION, DRAG IN THE TOP STRIP FOR THE LOOP, CTRL+WHEEL TO ZOOM)",
                      "RÈGLE TEMPORELLE (CLIC : POSITION DE LECTURE, GLISSER EN HAUT : BOUCLE, CTRL+MOLETTE : ZOOM)"), 20, 12, 850);
        var bars = c.At(new TimelineRuler { LengthBeats = 32, ViewLength = 32, PlayPositionSource = () => audio.PatternBeat * 2.0, AutomationName = L.T("Song position", "Position du morceau") }, 20, 36, 850, 46);
        bars.LoopStart = 8;
        bars.LoopEnd = 16;
        bars.Markers.Add(new TimelineMarker(0, "Intro"));
        bars.Markers.Add(new TimelineMarker(8, L.T("Verse", "Couplet"), Color.FromRgb(0x26C6DA)));
        bars.Markers.Add(new TimelineMarker(24, L.T("Chorus", "Refrain"), Color.FromRgb(0xEC407A)));
        var seconds = c.At(new TimelineRuler { LengthBeats = 480, ViewLength = 120, Format = TimelineFormat.Seconds, Tempo = 96, AutomationName = L.T("Sample position", "Position dans l'échantillon") }, 20, 90, 850, 40);
        seconds.LoopStart = 24;
        seconds.LoopEnd = 40;
        seconds.IsLoopEnabled = false;
        seconds.PlayPosition = 31.5;

        c.Caption(L.T("SPIN BOXES (UP / DOWN BUTTONS, ARROWS, PAGE UP / DOWN, DOUBLE-CLICK TO TYPE)", "BOÎTES NUMÉRIQUES (BOUTONS HAUT / BAS, FLÈCHES, PAGE PRÉC. / SUIV., DOUBLE-CLIC POUR SAISIR)"), 20, 148, 850);
        var delay = new LocalParameter(L.T("Delay", "Retard"), 0, 2000, 375, "ms");
        var tempo = new LocalParameter("Tempo", 40, 240, 120, "BPM");
        var voices = LocalParameter.FromList(L.T("Voices", "Voix"), ["1", "2", "4", "8", "16", "32"], 3);
        var channel = LocalParameter.FromList(L.T("MIDI channel", "Canal MIDI"), [L.T("Omni", "Omni"), "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16"], 0);
        c.At(new NumericUpDown { Parameter = delay, PlainStep = 1.0, Label = L.T("DELAY", "RETARD") }, 20, 172, 110, 44);
        c.At(new NumericUpDown { Parameter = tempo, PlainStep = 0.5, Label = "TEMPO" }, 150, 172, 110, 44);
        c.At(new NumericUpDown { Parameter = voices, Label = L.T("VOICES", "VOIX") }, 280, 172, 90, 44);
        c.At(new NumericUpDown { Parameter = channel, Label = L.T("MIDI CHANNEL", "CANAL MIDI") }, 390, 172, 110, 44);
        c.At(new NumberBox { Parameter = new LocalParameter("Fine", -100, 100, 0, "ct"), PlainStep = 1.0, Label = L.T("FINE (NUMBER BOX)", "FIN (BOÎTE À BOUTONS)") }, 520, 172, 140, 44);

        c.Caption(L.T("TOASTS (THEY STACK IN THE CORNER, PAUSE UNDER THE MOUSE, FADE OUT; × CLOSES)", "NOTIFICATIONS (ELLES S'EMPILENT DANS LE COIN, S'ARRÊTENT SOUS LA SOURIS, S'EFFACENT ; × FERME)"), 20, 236, 850);
        var toasts = new ToastStack { Corner = ToastCorner.BottomRight, AutomationName = L.T("Notifications", "Notifications") };
        var info = c.At(new Button { Text = L.T("Information", "Information") }, 20, 262, 130, 28);
        var success = c.At(new Button { Text = L.T("Success", "Succès") }, 160, 262, 130, 28);
        var warning = c.At(new Button { Text = L.T("Warning + action", "Avertissement + action") }, 300, 262, 190, 28);
        var error = c.At(new Button { Text = L.T("Error", "Erreur") }, 500, 262, 130, 28);
        info.Click += _ => toasts.Show(L.T("The demo signal changes chord every two seconds.", "Le signal de démonstration change d'accord toutes les deux secondes."));
        success.Click += _ => toasts.Show(L.T("Preset “Velvet” saved", "Preset « Velvet » enregistré"), ToastKind.Success);
        warning.Click += _ => toasts.Show(L.T("3 notes deleted", "3 notes supprimées"), ToastKind.Warning, actionText: L.T("Undo", "Annuler"), action: () => toasts.Show(L.T("Notes restored", "Notes rétablies"), ToastKind.Success));
        error.Click += _ => toasts.Show(L.T("Sample “kick.wav” not found", "Échantillon « kick.wav » introuvable"), ToastKind.Error);
        c.At(new Label(L.T("A ToastStack covers the whole page: only the toasts take the mouse, the controls below stay usable. " +
                           "Post() shows a toast from any thread, at the next frame.",
                           "Un ToastStack couvre toute la page : seules les notifications prennent la souris, les contrôles dessous restent utilisables. " +
                           "Post() affiche une notification depuis n'importe quel thread, à l'image suivante."))
        { Wrap = true, IsDim = true }, 20, 304, 420, 80);
        // Shown from the start (they stay until closed) so the page shows the look of each kind.
        toasts.Show(L.T("Preset “Velvet” saved", "Preset « Velvet » enregistré"), ToastKind.Success, TimeSpan.Zero);
        toasts.Show(L.T("3 notes deleted", "3 notes supprimées"), ToastKind.Warning, TimeSpan.Zero, L.T("Undo", "Annuler"), () => { });
        c.At(toasts, 0, 0, 880, 560);
        return c;
    }
}
