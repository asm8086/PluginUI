// 2026/11/08 13:42:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Modular: a node editor with a small patch (parallel compression, a delay with feedback, a synthesizer).</summary>
internal static class ModularPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();
        c.Caption(L.T("NODE EDITOR (DRAG FROM A CONNECTOR TO CABLE, PULL A CABLE END OFF TO REMOVE IT, CTRL+WHEEL: ZOOM, DRAG THE BACKGROUND: PAN)",
                      "ÉDITEUR DE MODULES (GLISSER D'UN CONNECTEUR : CÂBLER, ÉCARTER UN BOUT DE CÂBLE : L'ENLEVER, CTRL+MOLETTE : ZOOM)"), 20, 12, 850);
        var editor = c.At(new NodeEditor
        {
            AutomationName = L.T("Patch", "Patch"),
            EmptyText = L.T("Right-click to add a module.", "Clic droit pour ajouter un module."),
        }, 20, 36, 850, 440);

        static NodeEditorPort Audio(string key, string name, int channels = 2) => new(key, name, "audio", channels);
        static NodeEditorPort Midi(string key) => new(key, "MIDI", "midi");
        NodeEditorNode Node(string title, float x, float y, NodeEditorPort[] inputs, NodeEditorPort[] outputs, string? subtitle = null, Func<float>? level = null)
        {
            var node = new NodeEditorNode(title) { X = x, Y = y, Subtitle = subtitle, Level = level };
            node.SetPorts(inputs, outputs);
            editor.Nodes.Add(node);
            return node;
        }

        float Wobble(double rate, float floor, float swing) => floor + swing * (float)(0.5 + 0.5 * Math.Sin(Environment.TickCount64 / 1000.0 * rate));

        var input = Node(L.T("Audio input 1-2", "Entrée audio 1-2"), 16, 48, [], [Audio("out", L.T("Out", "Sortie"))], level: () => Wobble(1.7, 0.25f, 0.4f));
        var comp = Node(L.T("Compressor", "Compresseur"), 232, 16, [Audio("in", L.T("Main", "Principale")), Audio("side", "Sidechain")], [Audio("out", L.T("Out", "Sortie"))],
            L.T("latency 64 samples", "latence 64 échantillons"), () => Wobble(2.3, 0.3f, 0.3f));
        var loop = Node(L.T("Feedback sum", "Somme de retour"), 232, 176, [Audio("in1", L.T("In 1", "Entrée 1")), Audio("in2", L.T("In 2", "Entrée 2"))], [Audio("out", L.T("Out", "Sortie"))]);
        var delay = Node(L.T("Delay", "Écho"), 448, 192, [Audio("in", L.T("In", "Entrée"))], [Audio("out", L.T("Out", "Sortie"))], "375 ms", () => Wobble(0.9, 0.1f, 0.3f));
        var gain = Node("Gain", 448, 304, [Audio("in", L.T("In", "Entrée"))], [Audio("out", L.T("Out", "Sortie"))], "−6 dB");
        var keys = Node(L.T("Keyboard", "Clavier"), 16, 416, [], [Midi("midi")]);
        var synth = Node(L.T("Synthesizer", "Synthétiseur"), 232, 400, [Midi("midi")], [Audio("out", L.T("Out", "Sortie"))], level: () => Wobble(3.1, 0.2f, 0.5f));
        synth.Badge = L.T("BYPASS", "CONTOURNÉ");
        synth.IsDimmed = true;
        var sum = Node(L.T("Audio sum", "Somme audio"), 672, 48,
            [Audio("in1", L.T("In 1", "Entrée 1")), Audio("in2", L.T("In 2", "Entrée 2")), Audio("in3", L.T("In 3", "Entrée 3")), Audio("in4", L.T("In 4", "Entrée 4"))],
            [Audio("out", L.T("Out", "Sortie"))], level: () => Wobble(1.3, 0.45f, 0.45f));
        var output = Node(L.T("Audio output 1-2", "Sortie audio 1-2"), 880, 96, [Audio("in", L.T("In", "Entrée"))], [], level: () => Wobble(1.3, 0.45f, 0.45f));
        foreach (var n in editor.Nodes) editor.Snap(n); // first connectors on the grid: aligned nodes get straight cables
        editor.Zoom = 0.8f;

        void Cable(NodeEditorNode from, string output, NodeEditorNode to, string input, string? label = null, bool isFeedback = false)
            => editor.Cables.Add(new NodeEditorCable(from.FindOutput(output)!, to.FindInput(input)!) { Label = label, IsFeedback = isFeedback });
        Cable(input, "out", comp, "in");
        Cable(comp, "out", sum, "in1");
        Cable(input, "out", sum, "in2", "+64");
        Cable(input, "out", loop, "in1");
        Cable(loop, "out", delay, "in");
        Cable(delay, "out", gain, "in");
        Cable(gain, "out", loop, "in2", L.T("1 block", "1 bloc"), isFeedback: true);
        Cable(delay, "out", sum, "in3");
        Cable(keys, "midi", synth, "midi");
        Cable(synth, "out", sum, "in4");
        Cable(sum, "out", output, "in");

        // A working patch: the editor asks, the page applies (one cable per input).
        editor.CableRequested += (e, from, to) =>
        {
            for (int i = e.Cables.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(e.Cables[i].Input, to)) e.Cables.RemoveAt(i);
            }
            e.Cables.Add(new NodeEditorCable(from, to));
            e.InvalidateVisual();
        };
        editor.CableRemoveRequested += (e, cable) =>
        {
            e.Cables.Remove(cable);
            e.InvalidateVisual();
        };
        editor.DeleteRequested += (e, nodes, _) => Remove(e, nodes);
        editor.ContextMenuOpening += (e, args) =>
        {
            if (args.Node is { } node)
            {
                args.Items.Add(new MenuItem(L.T("Remove", "Supprimer"), () => Remove(e, e.SelectedNodes)) { ShortcutText = "Del" });
                args.Items.Add(new MenuItem(node.IsDimmed ? L.T("Turn on", "Activer") : L.T("Bypass", "Contourner"), () =>
                {
                    node.IsDimmed = !node.IsDimmed;
                    node.Badge = node.IsDimmed ? L.T("BYPASS", "CONTOURNÉ") : null;
                    e.InvalidateVisual();
                }));
                return;
            }
            if (args.Cable is { } cable)
            {
                args.Items.Add(new MenuItem(L.T("Remove the cable", "Supprimer le câble"), () =>
                {
                    e.Cables.Remove(cable);
                    e.InvalidateVisual();
                }));
                return;
            }
            args.Items.Add(new MenuItem(L.T("Add a gain", "Ajouter un gain"), () =>
            {
                var gain = new NodeEditorNode("Gain") { X = args.GraphPosition.X, Y = args.GraphPosition.Y, Subtitle = "0 dB" };
                gain.SetPorts([Audio("in", L.T("In", "Entrée"))], [Audio("out", L.T("Out", "Sortie"))]);
                e.Nodes.Add(gain);
                e.Select([gain]);
            }));
            args.Items.Add(new MenuItem(L.T("Fit the view", "Tout afficher"), e.ZoomToFit) { ShortcutText = "Home" });
        };

        c.At(new Label(L.T("The editor shows modules and cables and asks the application for every change (CableRequested, CableRemoveRequested, " +
                           "DeleteRequested): this page keeps one cable per input. Dashed: the cable that closes a loop. Drag the background: pan; Shift+drag: select; right-click: menu.",
                           "L'éditeur affiche modules et câbles et demande chaque modification à l'application (CableRequested, CableRemoveRequested, " +
                           "DeleteRequested) : cette page garde un câble par entrée. En pointillés : le câble qui ferme une boucle. Glisser le fond : déplacer la vue ; Maj+glisser : sélectionner ; clic droit : menu."))
        { Wrap = true, IsDim = true }, 20, 484, 850, 40);
        _ = audio;
        return c;
    }

    private static void Remove(NodeEditor editor, IReadOnlyList<NodeEditorNode> nodes)
    {
        foreach (var node in nodes)
        {
            editor.Nodes.Remove(node);
            for (int i = editor.Cables.Count - 1; i >= 0; i--)
            {
                var cable = editor.Cables[i];
                if (ReferenceEquals(cable.Output.Node, node) || ReferenceEquals(cable.Input.Node, node)) editor.Cables.RemoveAt(i);
            }
        }
        editor.Select([]);
    }
}
