// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using NPlug;

namespace PluginUI.NPlug;

/// <summary>
/// Bridge between the editor and the controller: VST3 edit protocol, MIDI learn and notes played on the editor
/// (<see cref="PluginUIController{TModel}"/> implements it for you).
/// </summary>
public interface IParameterEditSink
{
    /// <summary>Controller-side model: its events drive the repaints of bound controls.</summary>
    AudioProcessorModel Model { get; }

    /// <summary>Host services (context menus...), null before the host connected the controller.</summary>
    IAudioControllerHandler? ControllerHandler { get; }

    /// <summary>MIDI learn service offered by the editor's context menus, or <c>null</c> (default) for none.</summary>
    IMidiLearnService? MidiLearn => null;

    /// <summary>Starts a gesture (the host records automation from here).</summary>
    void BeginEdit(AudioParameter parameter);

    /// <summary>Changes the value during a gesture (forwarded to the host, then to the processor).</summary>
    void PerformEdit(AudioParameter parameter, double normalizedValue);

    /// <summary>Ends the gesture.</summary>
    void EndEdit(AudioParameter parameter);

    /// <summary><c>true</c> while a gesture is open on <paramref name="parameter"/>.</summary>
    bool IsEditing(AudioParameter parameter);

    /// <summary>Sends a note played on the editor to the processor; the default does nothing and returns <c>false</c>.</summary>
    /// <param name="note">The note event.</param>
    /// <returns><c>true</c> if the note was sent.</returns>
    bool TrySendNote(UINoteEvent note) => false;
}
