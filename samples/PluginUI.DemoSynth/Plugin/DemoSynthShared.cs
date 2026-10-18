// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using PluginUI.Dsp;

namespace PluginUI.DemoSynth.Plugin;

/// <summary>
/// Real-time data the processor shares with the editor (written on the audio thread, read on the UI thread, no locks).
/// </summary>
public sealed class DemoSynthShared
{
    /// <summary>Notes held (host MIDI and editor keyboard), shown on the editor's keyboard.</summary>
    public NoteActivity Notes { get; } = new();

    /// <summary>Output peaks, left and right.</summary>
    public PeakValue[] Output { get; } = [new PeakValue(), new PeakValue()];

    /// <summary>Side-chain input peak (mono, max of both channels).</summary>
    public PeakValue Sidechain { get; } = new();

    /// <summary>Gain reduction applied by the ducking, in [0, 1] (1 = full mute).</summary>
    public PeakValue Ducking { get; } = new();
}
