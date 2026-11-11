// 2026/11/11 15:13:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Desktop;
using PluginUI.Host.Audio;
using PluginUI.Host.Graph;

namespace PluginUI.Host.Desktop;

/// <summary>
/// Window of a module that is not a plug-in (gain, keyboard, test signal, sum…): its settings, which apply as soon as
/// they change, and its level meter. Not modal: several stay open while the graph is edited and played.
/// </summary>
internal sealed class ModuleWindow : ITickable
{
    private readonly Func<float[]> _levels;
    private readonly LevelMeter? _meter;
    private bool _closed;

    public ModuleWindow(GraphNode node, string title, StandaloneWindow owner, bool lightTheme, Func<float[]> levels, Action changed)
    {
        Node = node;
        _levels = levels;
        UIHost? host = null;
        var (root, size, meter) = Build(node, () => host!, levels, changed);
        _meter = meter;
        host = Host = new UIHost(root, size, SizingMode.Relayout) { MinSize = new Size(Math.Min(size.Width, 360f), Math.Min(size.Height, 160f)) };
        if (lightTheme) Host.Theme = Theme.Light;
        Window = new StandaloneWindow(Host, title)
        {
            Owner = owner,
            QuitOnClose = false,
            StartPosition = WindowStartPosition.CenterOwner,
            MaximizeBox = node is KeyboardNode,
        };
        Window.Closed += _ => _closed = true;
        Host.AddTicker(this);
    }

    public GraphNode Node { get; }

    public UIHost Host { get; }

    public StandaloneWindow Window { get; }

    public void Show()
    {
        Window.Show();
        // The keyboard plays from the computer keyboard as soon as the window opens.
        Host.Root.DescendantsAndSelf().OfType<PianoKeyboard>().FirstOrDefault()?.Focus();
    }

    public void SetTheme(bool light)
    {
        Host.Theme = light ? Theme.Light : Theme.Dark;
        Window.UpdateTitleBarTheme();
    }

    bool ITickable.Tick(double elapsedSeconds)
    {
        if (_closed) return false;
        _meter?.SetLevels(_levels());
        return true;
    }

    /// <summary>The content of the window (also rendered alone for the documentation).</summary>
    internal static (UIElement Root, Size Size, LevelMeter? Meter) Build(GraphNode node, Func<UIHost> host, Func<float[]> levels, Action changed)
    {
        var body = new StackPanel { Spacing = 8f, Dock = Side.Top };
        int rows = 0;
        float extra = 0f;

        DockPanel Row(string caption, UIElement control, UIElement? side = null)
        {
            var row = new DockPanel { Height = 28f };
            row.Children.Add(new Label(caption) { Width = 200f, Dock = Side.Left, VerticalAlignment = VerticalAlignment.Center });
            if (side is not null) row.Children.Add(side);
            row.Children.Add(control);
            return row;
        }
        LocalParameter Number(string caption, double min, double max, double value, string units, int decimals, Action<double> apply, bool show = true)
        {
            var p = new LocalParameter(caption, min, max, value, units, decimals: decimals, stepCount: decimals == 0 ? (int)(max - min) : 0);
            p.Changed += _ =>
            {
                apply(decimals == 0 ? Math.Round(p.Value) : p.Value);
                changed();
            };
            if (show)
            {
                body.Add(Row(caption, new NumberBox { Parameter = p, Label = string.Empty, Width = 150f, Height = 26f, HorizontalAlignment = HorizontalAlignment.Left }));
                rows++;
            }
            return p;
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
            rows++;
        }
        CheckBox Check(string text, bool value, Action<bool> apply, bool show = true)
        {
            var box = new CheckBox(text, value) { Height = 24f };
            box.ValueChanged += (_, _) =>
            {
                apply(box.IsChecked);
                changed();
            };
            if (show)
            {
                body.Add(box);
                rows++;
            }
            return box;
        }
        void Note(string text)
        {
            body.Add(new Label(text) { Wrap = true, IsDim = true, Height = 40f });
            extra += 48f;
        }

        var root = new DockPanel { Padding = new Thickness(14f, 12f, 14f, 12f) };
        LevelMeter? meter = null;
        if (node.Meter.ChannelCount > 0)
        {
            meter = new LevelMeter
            {
                Orientation = Orientation.Horizontal, ChannelCount = Math.Clamp(node.Meter.ChannelCount, 1, 16), Height = Math.Min(24f, 6f * node.Meter.ChannelCount + 4f),
                ShowScale = false, MinDb = -60f, Dock = Side.Bottom, Margin = new Thickness(0f, 10f, 0f, 0f), ShowPeakValue = true,
                ToolTip = node is AudioOutputNode or DemuxNode ? L.T("Level arriving at the module", "Niveau qui arrive au module") : L.T("Output level of the module", "Niveau de sortie du module"),
            };
            root.Children.Add(meter);
        }

        string gain = L.T("Gain (dB)", "Gain (dB)");
        string channels = L.T("Channels", "Canaux");
        float width = 540f;
        switch (node)
        {
            case GainNode g:
            {
                // A large knob for the gain, played while listening; the exact value next to it.
                var p = Number(gain, -60, 24, g.GainDb, "dB", 1, v => g.GainDb = v, show: false);
                var knob = new Knob(p) { Width = 150f, Height = 160f, Dock = Side.Left, ShowValueBubble = true, Label = "GAIN", Margin = new Thickness(0f, 0f, 16f, 0f) };
                var side = new StackPanel { Spacing = 10f };
                side.Add(new NumberBox { Parameter = p, Label = string.Empty, Width = 150f, Height = 28f, HorizontalAlignment = HorizontalAlignment.Left },
                    Check(L.T("Invert the polarity", "Inverser la polarité"), g.Invert, v => g.Invert = v, show: false),
                    Check(L.T("Mute", "Muet"), g.Mute, v => g.Mute = v, show: false));
                var channelBox = new NumberBox
                {
                    Parameter = Number(channels, 1, 32, g.Channels, "", 0, v => g.Channels = (int)v, show: false), Label = string.Empty, Width = 150f, Height = 26f,
                    HorizontalAlignment = HorizontalAlignment.Left, ToolTip = channels,
                };
                side.Add(new Label(channels) { IsDim = true, Height = 18f }, channelBox);
                var panel = new DockPanel { Height = 170f, Dock = Side.Top };
                panel.Children.Add(knob);
                panel.Children.Add(side);
                root.Children.Add(panel);
                width = 420f;
                extra += 170f;
                break;
            }
            case KeyboardNode keys:
            {
                Choice(L.T("MIDI channel", "Canal MIDI"), [.. Enumerable.Range(1, 16).Select(i => i.ToString(CultureInfo.InvariantCulture))], keys.Channel - 1, i => keys.Channel = i + 1);
                var octave = new LocalParameter(L.T("Lowest octave", "Octave la plus basse"), 0, 7, 2, "", stepCount: 7, decimals: 0);
                var piano = new PianoKeyboard { LowestNote = 36, HighestNote = 96, Margin = new Thickness(0f, 10f, 0f, 0f), Focusable = true };
                octave.Changed += _ =>
                {
                    int low = 12 * (int)Math.Round(octave.Value);
                    piano.LowestNote = low;
                    piano.HighestNote = Math.Min(127, low + 60);
                };
                body.Add(Row(octave.Name, new NumberBox { Parameter = octave, Label = string.Empty, Width = 150f, Height = 26f, HorizontalAlignment = HorizontalAlignment.Left }));
                rows++;
                piano.NoteOn += (_, note, velocity) => keys.NoteOn(note, Math.Clamp((int)MathF.Round(velocity * 127f), 1, 127));
                piano.NoteOff += (_, note) => keys.NoteOff(note);
                root.Children.Add(body);
                root.Children.Add(new Label(L.T("Click the keys, or play on the computer keyboard when the keyboard has the focus. The window can be enlarged.",
                    "Cliquez sur les touches, ou jouez sur le clavier de l'ordinateur quand le clavier a le focus. La fenêtre peut être agrandie."))
                { Wrap = true, IsDim = true, Height = 36f, Dock = Side.Bottom, Margin = new Thickness(0f, 6f, 0f, 0f) });
                root.Children.Add(piano);
                return (root, new Size(820f, 300f), meter);
            }
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
                    var h = host();
                    if (FileDialogs.OpenFile(h, L.T("Play an audio file", "Lire un fichier audio"), [new(L.T("WAV files", "Fichiers WAV"), "*.wav")]) is not { } path) return;
                    try
                    {
                        file.Open(path);
                        name.Text = file.Path;
                        changed();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or NotSupportedException)
                    {
                        Dialog.ShowMessage(h, L.T("Audio file", "Fichier audio"), L.T("The file could not be read:\n", "Le fichier n'a pas pu être lu :\n") + ex.Message);
                    }
                }) { Width = 110f, Height = 26f, Dock = Side.Right };
                body.Add(Row(L.T("File", "Fichier"), name, choose));
                rows++;
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
            case MidiOutputNode:
                Note(L.T("What reaches this module goes to the MIDI output port chosen in File > Audio and MIDI.",
                    "Ce qui arrive à ce module va au port de sortie MIDI choisi dans Fichier > Audio et MIDI."));
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
            case MidiTransposeNode transpose:
                Number(L.T("Transposition (semitones)", "Transposition (demi-tons)"), -MidiTransposeNode.MaxSemitones, MidiTransposeNode.MaxSemitones,
                    transpose.Semitones, L.T("st", "dt"), 0, v => transpose.Semitones = (int)v);
                Choice(L.T("Notes transposed", "Notes transposées"), [L.T("All channels", "Tous les canaux"), .. Enumerable.Range(1, 16).Select(i => L.T($"Channel {i} only", $"Canal {i} seulement"))],
                    transpose.Channel, i => transpose.Channel = i);
                Check(L.T("Fold the notes out of range back by octaves (otherwise they are dropped)", "Ramener par octaves les notes hors limites (sinon elles sont supprimées)"),
                    transpose.FoldIntoRange, v => transpose.FoldIntoRange = v);
                Note(L.T("12 semitones = one octave. A held note keeps its transposition until it is released; the other messages pass unchanged.",
                    "12 demi-tons = une octave. Une note tenue garde sa transposition jusqu'à son relâchement ; les autres messages passent tels quels."));
                break;
            case MidiVelocityNode velocity:
                Number(L.T("Velocity of the notes", "Vélocité des notes"), 1, 127, velocity.Velocity, string.Empty, 0, v => velocity.Velocity = (int)v);
                Choice(L.T("Notes changed", "Notes modifiées"), [L.T("All channels", "Tous les canaux"), .. Enumerable.Range(1, 16).Select(i => L.T($"Channel {i} only", $"Canal {i} seulement"))],
                    velocity.Channel, i => velocity.Channel = i);
                Note(L.T("Every note played gets this velocity (1 to 127); note offs and the other messages pass unchanged.",
                    "Chaque note jouée prend cette vélocité (1 à 127) ; les relâchements de notes et les autres messages passent tels quels."));
                break;
            case MidiFilterNode filter:
            {
                // The channels: sixteen boxes in two lines, with All and None.
                var boxes = new List<CheckBox>();
                var lines = new StackPanel { Spacing = 4f };
                for (int line = 0; line < 2; line++)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2f, Height = 24f };
                    for (int i = 1; i <= 8; i++)
                    {
                        int channel = line * 8 + i;
                        var box = Check(channel.ToString(CultureInfo.InvariantCulture), filter.PassesChannel(channel), v => filter.SetChannel(channel, v), show: false);
                        box.Width = 40f;
                        boxes.Add(box);
                        row.Add(box);
                    }
                    lines.Add(row);
                }
                var allOrNone = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6f, Height = 24f };
                allOrNone.Add(
                    new Button(L.T("All", "Tous"), () => boxes.ForEach(b => b.IsChecked = true)) { Width = 80f, Height = 24f },
                    new Button(L.T("None", "Aucun"), () => boxes.ForEach(b => b.IsChecked = false)) { Width = 80f, Height = 24f });
                lines.Add(allOrNone);
                var channelPanel = new DockPanel { Height = 80f };
                channelPanel.Children.Add(new Label(L.T("Channels let through", "Canaux qui passent")) { Width = 200f, Dock = Side.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0f, 3f, 0f, 0f) });
                channelPanel.Children.Add(lines);
                body.Add(channelPanel);
                extra += 88f;

                // The kinds of messages, in two columns.
                (MidiMessageKinds Kind, string Text)[] kinds =
                [
                    (MidiMessageKinds.Notes, L.T("Notes", "Notes")),
                    (MidiMessageKinds.ControlChange, L.T("Controllers", "Contrôleurs")),
                    (MidiMessageKinds.PitchBend, L.T("Pitch bend", "Pitch bend")),
                    (MidiMessageKinds.ProgramChange, L.T("Program changes", "Changements de programme")),
                    (MidiMessageKinds.ChannelPressure, L.T("Channel aftertouch", "Aftertouch de canal")),
                    (MidiMessageKinds.PolyPressure, L.T("Polyphonic aftertouch", "Aftertouch polyphonique")),
                    (MidiMessageKinds.System, L.T("System messages (clock…)", "Messages système (horloge…)")),
                ];
                body.Add(new Label(L.T("Messages let through", "Messages qui passent")) { Height = 20f });
                extra += 28f;
                for (int i = 0; i < kinds.Length; i += 2)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8f, Height = 24f };
                    foreach (var (kind, text) in kinds.Skip(i).Take(2))
                    {
                        var box = Check(text, (filter.Kinds & kind) != 0, v => filter.Kinds = v ? filter.Kinds | kind : filter.Kinds & ~kind, show: false);
                        box.Width = 250f;
                        row.Add(box);
                    }
                    body.Add(row);
                    rows++;
                }

                string NoteText(double v) => $"{(int)Math.Round(v)} ({GraphView.NoteName((int)Math.Round(v))})";
                Number(L.T("Lowest note", "Note la plus basse"), 0, 127, filter.LowestNote, "", 0, v => filter.LowestNote = (int)v).Formatter = NoteText;
                Number(L.T("Highest note", "Note la plus haute"), 0, 127, filter.HighestNote, "", 0, v => filter.HighestNote = (int)v).Formatter = NoteText;
                width = 600f;
                Note(L.T("Two filters with complementary note ranges split a keyboard between two instruments. The note off of a note let through always passes.",
                    "Deux filtres aux plages de notes complémentaires partagent un clavier entre deux instruments. Le relâchement d'une note qui est passée passe toujours."));
                break;
            }
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
        root.Children.Add(body);
        float height = 24f + rows * 36f + extra + (meter is null ? 0f : 40f);
        return (root, new Size(width, Math.Max(150f, height)), meter);
    }
}
