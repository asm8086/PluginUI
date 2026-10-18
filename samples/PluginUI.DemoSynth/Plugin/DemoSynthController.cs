// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using System.Runtime.Versioning;
using NPlug;
using PluginUI.DemoSynth.Editor;
using PluginUI.NPlug;

namespace PluginUI.DemoSynth.Plugin;

/// <summary>
/// Edit controller of the demo synth: exposes the parameters, maps MIDI pitch bend and the mod wheel to their
/// parameters, and creates the editor. The note bridge, the shared state and MIDI learn come from
/// <see cref="PluginUIController{TModel}"/>.
/// </summary>
public sealed class DemoSynthController : PluginUIController<DemoSynthModel>
{
    /// <summary>VST3 class identifier of the controller component.</summary>
    public static readonly Guid ClassId = new("9b2d6a7e-4c1f-4e38-b0a5-7d3e8f1c2a64");

    /// <summary>Creates the controller.</summary>
    public DemoSynthController()
    {
        // VST3 delivers no MIDI controllers to the processor: the host turns them into parameter changes.
        SetMidiCCMapping(AudioMidiControllerNumber.PitchBend, Model.PitchBend);
        SetMidiCCMapping(AudioMidiControllerNumber.ModWheel, Model.ModWheel);
        // The synth receives MIDI: offer "MIDI Learn" in the context menus of its controls.
        IsMidiLearnEnabled = true;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.17763")]
    protected override PluginEditorView? CreateEditor() => new DemoSynthEditor(this);
}
