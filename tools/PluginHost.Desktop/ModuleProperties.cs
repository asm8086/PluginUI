// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Desktop;
using PluginUI.Host.Audio;
using PluginUI.Host.Graph;

namespace PluginUI.Host.Desktop;

/// <summary>The properties dialog of a module that is not a plug-in: its settings apply as soon as they change.</summary>
internal static class ModuleProperties
{
    public static void Show(GraphView view, UIHost host, GraphNode node, Action changed)
    {
        var body = new StackPanel { Spacing = 8f };

        DockPanel Row(string caption, UIElement control, UIElement? extra = null)
        {
            var row = new DockPanel { Height = 28f };
            row.Children.Add(new Label(caption) { Width = 190f, Dock = Side.Left, VerticalAlignment = VerticalAlignment.Center });
            if (extra is not null) row.Children.Add(extra);
            row.Children.Add(control);
            return row;
        }
        void Number(string caption, double min, double max, double value, string units, int decimals, Action<double> apply)
        {
            var p = new LocalParameter(caption, min, max, value, units, decimals: decimals, stepCount: decimals == 0 ? (int)(max - min) : 0);
            p.Changed += _ =>
            {
                apply(decimals == 0 ? Math.Round(p.Value) : p.Value);
                changed();
            };
            body.Add(Row(caption, new NumberBox { Parameter = p, Label = string.Empty, Width = 150f, Height = 26f, HorizontalAlignment = HorizontalAlignment.Left }));
        }
        void Choice(string caption, string[] items, int selected, Action<int> apply)
        {
            var drop = new DropDown(items) { Label = string.Empty, Height = 26f, SelectedIndex = Math.Clamp(selected, 0, items.Length - 1) };
            drop.ValueChanged += (_, _) =>
            {
                apply(Math.Max(0, drop.SelectedIndex));
                changed();
            };
            body.Add(Row(caption, drop));
        }
        void Check(string text, bool value, Action<bool> apply)
        {
            var box = new CheckBox(text, value) { Height = 24f };
            box.ValueChanged += (_, _) =>
            {
                apply(box.IsChecked);
                changed();
            };
            body.Add(box);
        }
        void Note(string text) => body.Add(new Label(text) { Wrap = true, IsDim = true, Height = 40f });

        string gain = L.T("Gain (dB)", "Gain (dB)");
        string channels = L.T("Channels", "Canaux");
        switch (node)
        {
            case AudioInputNode input:
                Number(L.T("First channel of the device", "Premier canal du périphérique"), 1, 64, input.FirstChannel + 1, "", 0, v => input.FirstChannel = (int)v - 1);
                Number(channels, 1, 32, input.Channels, "", 0, v => input.Channels = (int)v);
                Number(gain, -60, 24, input.GainDb, "dB", 1, v => input.GainDb = v);
                Note(L.T("Channels the device does not have stay silent.", "Les canaux que le périphérique n'a pas restent muets."));
                break;
            case AudioOutputNode output:
                Number(L.T("First channel of the device", "Premier canal du périphérique"), 1, 64, output.FirstChannel + 1, "", 0, v => output.FirstChannel = (int)v - 1);
                Number(channels, 1, 32, output.Channels, "", 0, v => output.Channels = (int)v);
                Number(gain, -60, 24, output.GainDb, "dB", 1, v => output.GainDb = v);
                Check(L.T("Mute", "Muet"), output.Mute, v => output.Mute = v);
                Note(L.T("Several output modules on the same channels add up.", "Plusieurs modules de sortie sur les mêmes canaux s'additionnent."));
                break;
            case TestSignalNode test:
            {
                var g = test.Generator;
                Choice(L.T("Signal", "Signal"), [.. Enum.GetValues<TestSignal>().Select(GraphView.SignalName)], (int)g.Signal, i =>
                {
                    g.Signal = (TestSignal)i;
                    if (g.Signal == TestSignal.Sweep) g.Frequency = 20;
                });
                Number(L.T("Frequency (start of sweeps)", "Fréquence (début des balayages)"), 1, 20000, g.Frequency, "Hz", 0, v => g.Frequency = v);
                Number(L.T("End of sweeps", "Fin des balayages"), 1, 20000, g.EndFrequency, "Hz", 0, v => g.EndFrequency = v);
                Number(L.T("Sweep duration", "Durée d'un balayage"), 0.1, 60, g.SweepSeconds, "s", 1, v => g.SweepSeconds = v);
                Number(L.T("Interval of impulses", "Intervalle des impulsions"), 0.01, 10, g.Period, "s", 2, v => g.Period = v);
                Number(L.T("Level (dBFS)", "Niveau (dBFS)"), -96, 0, g.LevelDb, "dBFS", 1, v => g.LevelDb = v);
                Number(channels, 1, 32, test.Channels, "", 0, v => test.Channels = (int)v);
                break;
            }
            case AudioFileNode file:
            {
                var name = new Label(file.Path ?? L.T("No file", "Aucun fichier")) { VerticalAlignment = VerticalAlignment.Center, IsDim = true };
                var choose = new Button(L.T("Choose…", "Choisir…"), () =>
                {
                    if (FileDialogs.OpenFile(host, L.T("Play an audio file", "Lire un fichier audio"), [new(L.T("WAV files", "Fichiers WAV"), "*.wav")]) is not { } path) return;
                    try
                    {
                        file.Open(path);
                        name.Text = file.Path;
                        changed();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or NotSupportedException)
                    {
                        Dialog.ShowMessage(host, L.T("Audio file", "Fichier audio"), L.T("The file could not be read:\n", "Le fichier n'a pas pu être lu :\n") + ex.Message);
                    }
                }) { Width = 110f, Height = 26f, Dock = Side.Right };
                body.Add(Row(L.T("File", "Fichier"), name, choose));
                Check(L.T("Loop", "En boucle"), file.Loop, v => file.Loop = v);
                Check(L.T("Pause", "Pause"), file.IsPaused, v => file.IsPaused = v);
                Number(gain, -60, 24, file.GainDb, "dB", 1, v => file.GainDb = v);
                Number(channels, 1, 32, file.Channels, "", 0, v => file.Channels = (int)v);
                break;
            }
            case MidiInputNode midi:
                Choice(L.T("Channels", "Canaux"), [L.T("All", "Tous"), .. Enumerable.Range(1, 16).Select(i => L.T($"Channel {i} only", $"Canal {i} seulement"))], midi.ChannelFilter, i => midi.ChannelFilter = i);
                Note(L.T("The MIDI input port is chosen in File > Audio and MIDI.", "Le port d'entrée MIDI se choisit dans Fichier > Audio et MIDI."));
                break;
            case KeyboardNode keys:
            {
                Choice(L.T("MIDI channel", "Canal MIDI"), [.. Enumerable.Range(1, 16).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))], keys.Channel - 1, i => keys.Channel = i + 1);
                var piano = new PianoKeyboard { Height = 90f, LowestNote = 36, HighestNote = 96 };
                piano.NoteOn += (_, note, velocity) => keys.NoteOn(note, Math.Clamp((int)MathF.Round(velocity * 127f), 1, 127));
                piano.NoteOff += (_, note) => keys.NoteOff(note);
                body.Add(piano);
                Note(L.T("Click the keys, or type on the computer keyboard while it has the focus.", "Cliquez sur les touches, ou jouez sur le clavier de l'ordinateur quand il a le focus."));
                break;
            }
            case MidiOutputNode:
                Note(L.T("What reaches this module goes to the MIDI output port chosen in File > Audio and MIDI.",
                    "Ce qui arrive à ce module va au port de sortie MIDI choisi dans Fichier > Audio et MIDI."));
                break;
            case GainNode g:
                Number(gain, -96, 24, g.GainDb, "dB", 1, v => g.GainDb = v);
                Check(L.T("Invert the polarity", "Inverser la polarité"), g.Invert, v => g.Invert = v);
                Check(L.T("Mute", "Muet"), g.Mute, v => g.Mute = v);
                Number(channels, 1, 32, g.Channels, "", 0, v => g.Channels = (int)v);
                break;
            case AudioSumNode sum:
            {
                Number(L.T("Inputs", "Entrées"), 2, 32, sum.InputCount, "", 0, v => sum.InputCount = (int)v);
                Number(channels, 1, 32, sum.Channels, "", 0, v => sum.Channels = (int)v);
                Check(L.T("Align the inputs on the latency of their paths", "Aligner les entrées sur la latence de leurs chemins"), sum.CompensateLatency, v => sum.CompensateLatency = v);
                for (int i = 0; i < Math.Min(sum.InputCount, 8); i++)
                {
                    int input = i;
                    Number(L.T($"Gain of input {i + 1} (dB)", $"Gain de l'entrée {i + 1} (dB)"), -96, 24, sum.GetGainDb(i), "dB", 1, v => sum.SetGainDb(input, v));
                }
                break;
            }
            case MidiMergeNode merge:
                Number(L.T("Inputs", "Entrées"), 2, 32, merge.InputCount, "", 0, v => merge.InputCount = (int)v);
                Check(L.T("Align the inputs on the latency of their paths", "Aligner les entrées sur la latence de leurs chemins"), merge.CompensateLatency, v => merge.CompensateLatency = v);
                break;
            case MuxNode mux:
                Number(L.T("Mono inputs (channels of the output)", "Entrées mono (canaux de la sortie)"), 2, 32, mux.Channels, "", 0, v => mux.Channels = (int)v);
                break;
            case DemuxNode demux:
                Number(L.T("Channels of the input (mono outputs)", "Canaux de l'entrée (sorties mono)"), 2, 32, demux.Channels, "", 0, v => demux.Channels = (int)v);
                break;
            default:
                Note(L.T("This module has no settings.", "Ce module n'a pas de réglages."));
                break;
        }
        var dialog = new Dialog(GraphView.TypeName(node) + (string.IsNullOrWhiteSpace(node.Title) ? string.Empty : " — " + node.Title), body) { DialogWidth = 520f };
        dialog.AddButton(L.T("Close", "Fermer"), null, isDefault: true, isCancel: true);
        host.ShowPopup(dialog);
        _ = view;
    }
}
