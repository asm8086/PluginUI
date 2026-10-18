// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using System.Runtime.Versioning;
using PluginUI.DemoSynth.Plugin;
using PluginUI.NPlug;

namespace PluginUI.DemoSynth.Editor;

/// <summary>
/// Editor of the demo synth: oscillator, filter, side-chain and output sections, both envelopes as draggable curves,
/// pitch and mod wheels, an on-screen keyboard played into the processor, A/B comparison and MIDI learn (right click
/// on any control).
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class DemoSynthEditor : PluginEditorView
{
    private const float EditorWidth = 820f;
    private const float EditorHeight = 470f;

    private readonly DemoSynthController _controller;
    private readonly DemoSynthModel _model;
    private PianoKeyboard? _keyboard;
    private LevelMeter? _outputMeter, _sidechainMeter;
    private HistoryGraph? _duckGraph;

    /// <summary>Creates the editor for a controller.</summary>
    /// <param name="controller">The controller.</param>
    public DemoSynthEditor(DemoSynthController controller)
        : base(controller, new Size(EditorWidth, EditorHeight), SizingMode.ScaleToFit)
    {
        _controller = controller;
        _model = controller.Model;
    }

    /// <inheritdoc />
    protected override void ConfigureHost(UIHost host)
    {
        host.MinSize = new Size(EditorWidth * 0.75f, EditorHeight * 0.75f);
        host.MaxSize = new Size(EditorWidth * 2.5f, EditorHeight * 2.5f);
        host.Theme = Theme.Dark.WithAccent(Color.FromRgb(0xFFB74D));
#if !DEBUG
        host.LiveEditShortcutEnabled = false;
#endif
    }

    /// <inheritdoc />
    protected override UIElement Build()
    {
        var root = new Canvas();

        var title = new Label("DemoSynth") { Name = "title", TextStyle = new TextStyle("Segoe UI", 18f, FontWeight.SemiBold) };
        title.SetBounds(18f, 10f, 160f, 30f);
        var subtitle = new Label("PluginUI · NPlug") { Name = "subtitle", IsDim = true, TextStyle = new TextStyle("Segoe UI", 10.5f) };
        subtitle.SetBounds(140f, 17f, 200f, 20f);

        // A/B over the sound parameters (not the performance controls).
        var ab = new ABCompareBar
        {
            Name = "ab",
            Parameters = [.. Parameters.GetSnapshotParameters().Where(p => p.Parameter != _model.PitchBend && p.Parameter != _model.ModWheel)],
        };
        ab.SetBounds(EditorWidth - 18f - 120f, 14f, 120f, 24f);
        root.Add(title, subtitle, ab);

        // ---- Row 1: oscillators, filter, side-chain, output
        const float row1 = 50f, row1Height = 152f, knobTop = row1 + 32f;
        Section(root, "OSCILLATORS", 14f, row1, 180f, row1Height);
        AddKnob(root, "shape", "SHAPE", _model.Shape, 26f, knobTop, n => _model.Shape.ToPlain(n) switch
        {
            < 1.0 => "Saw",
            > 99.0 => "Square",
            var v => InvariantNumber.Format(v, 0) + " %",
        });
        AddKnob(root, "detune", "DETUNE", _model.Detune, 110f, knobTop, n => InvariantNumber.Format(_model.Detune.ToPlain(n), 1) + " ct");

        Section(root, "FILTER", 202f, row1, 262f, row1Height);
        AddKnob(root, "cutoff", "CUTOFF", _model.Cutoff, 212f, knobTop, n => FormatHz(_model.Cutoff.ToPlain(n)));
        AddKnob(root, "resonance", "RESO", _model.Resonance, 296f, knobTop, n => InvariantNumber.Format(_model.Resonance.ToPlain(n), 0) + " %");
        var env = AddKnob(root, "filterEnv", "ENV", _model.FilterEnvAmount, 380f, knobTop, n => InvariantNumber.Format(_model.FilterEnvAmount.ToPlain(n), "+0.0;-0.0;0.0") + " oct");
        env.IsBipolar = true;
        env.BipolarCenter = 0.5;

        Section(root, "SIDECHAIN", 472f, row1, 232f, row1Height);
        AddKnob(root, "duck", "DUCK", _model.Duck, 480f, knobTop, n => InvariantNumber.Format(_model.Duck.ToPlain(n), 0) + " %");
        AddKnob(root, "duckRelease", "RELEASE", _model.DuckRelease, 552f, knobTop, n => InvariantNumber.Format(_model.DuckRelease.ToPlain(n) * 1000.0, 0) + " ms");
        _sidechainMeter = new LevelMeter { Name = "sidechainMeter", ShowScale = false, MinDb = -48f, ToolTip = "Sidechain input" };
        _sidechainMeter.SetBounds(632f, knobTop + 2f, 12f, 90f);
        _duckGraph = new HistoryGraph { Name = "duckGraph", Mode = HistoryGraphMode.Filled, FillFromTop = true, ShowCurrentValue = false, ToolTip = "Gain reduction" };
        _duckGraph.SetBounds(650f, knobTop + 2f, 46f, 90f);
        root.Add(_sidechainMeter, _duckGraph);

        Section(root, "OUTPUT", 712f, row1, 94f, row1Height);
        AddKnob(root, "volume", "VOLUME", _model.Volume, 716f, knobTop, n => InvariantNumber.Format(_model.Volume.ToPlain(n), "+0.0;-0.0;0.0") + " dB", width: 62f);
        _outputMeter = new LevelMeter { Name = "outputMeter", ShowScale = false, MinDb = -48f };
        _outputMeter.SetBounds(782f, knobTop + 2f, 16f, 90f);
        root.Add(_outputMeter);

        // ---- Row 2: envelopes
        const float row2 = 210f, row2Height = 140f;
        Section(root, "AMP ENVELOPE", 14f, row2, 392f, row2Height);
        var ampEnv = new EnvelopeEditor
        {
            Name = "ampEnvelope",
            AttackParameter = Parameters[_model.AmpAttack],
            DecayParameter = Parameters[_model.AmpDecay],
            SustainParameter = Parameters[_model.AmpSustain],
            ReleaseParameter = Parameters[_model.AmpRelease],
        };
        ampEnv.SetBounds(22f, row2 + 30f, 376f, row2Height - 38f);
        Section(root, "FILTER ENVELOPE", 414f, row2, 392f, row2Height);
        var filterEnv = new EnvelopeEditor
        {
            Name = "filterEnvelope",
            AttackParameter = Parameters[_model.FilterAttack],
            DecayParameter = Parameters[_model.FilterDecay],
            SustainParameter = Parameters[_model.FilterSustain],
            ReleaseParameter = Parameters[_model.FilterRelease],
        };
        filterEnv.SetBounds(422f, row2 + 30f, 376f, row2Height - 38f);
        root.Add(ampEnv, filterEnv);

        // ---- Row 3: wheels and keyboard
        const float row3 = 360f, row3Height = 96f;
        var pitch = new PitchWheel { Name = "pitchWheel", Parameter = Parameters[_model.PitchBend], Label = "PITCH" };
        pitch.SetBounds(16f, row3, 34f, row3Height);
        var mod = new ModWheel { Name = "modWheel", Parameter = Parameters[_model.ModWheel], Label = "MOD" };
        mod.SetBounds(56f, row3, 34f, row3Height);
        _keyboard = new PianoKeyboard { Name = "keyboard", LowestNote = 36, HighestNote = 96 };
        _keyboard.SetBounds(98f, row3, EditorWidth - 98f - 30f, row3Height);
        ConnectKeyboard(_keyboard);

        var resizer = new CornerResizer();
        resizer.SetBounds(EditorWidth - 20f, EditorHeight - 20f, 18f, 18f);
        root.Add(pitch, mod, _keyboard, resizer);

        BindSharedState();
        return root;
    }

    /// <inheritdoc />
    protected override void OnAttached() => _controller.SharedStateChanged += BindSharedState;

    /// <inheritdoc />
    protected override void OnRemoved() => _controller.SharedStateChanged -= BindSharedState;

    // The processor's shared state may arrive after the editor was built (the components connect in any order).
    private void BindSharedState()
    {
        _controller.TryGetSharedState<DemoSynthShared>(out var shared);
        if (_keyboard is not null) _keyboard.ActivitySource = shared?.Notes;
        if (_outputMeter is not null) _outputMeter.Sources = shared?.Output;
        if (_sidechainMeter is not null) _sidechainMeter.Sources = shared is null ? null : [shared.Sidechain];
        if (_duckGraph is not null) _duckGraph.PeakSource = shared?.Ducking;
    }

    private static void Section(Canvas root, string header, float x, float y, float width, float height)
    {
        var box = new GroupBox(header) { Name = "section" + header.Replace(" ", string.Empty, StringComparison.Ordinal) };
        box.SetBounds(x, y, width, height);
        root.Add(box);
    }

    private Knob AddKnob(Canvas root, string name, string label, global::NPlug.AudioParameter parameter, float x, float y, Func<double, string> formatter, float width = 76f)
    {
        var knob = new Knob(Parameters[parameter]) { Name = name, Label = label, Formatter = formatter };
        knob.SetBounds(x, y, width, 104f);
        root.Add(knob);
        return knob;
    }

    private static string FormatHz(double hertz)
        => hertz >= 1000.0 ? InvariantNumber.Format(hertz / 1000.0, 2) + " kHz" : InvariantNumber.Format(hertz, 0) + " Hz";
}
