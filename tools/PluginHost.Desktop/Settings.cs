// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace PluginUI.Host.Desktop;

/// <summary>Interface settings of the graphical host (gui.json, next to the plug-in list).</summary>
internal sealed class GuiSettings
{
    public string Language { get; set; } = "auto";

    public bool LightTheme { get; set; }

    /// <summary>Column widths, visibility and sort of the plug-in table (<see cref="DataTable.SaveLayout"/>).</summary>
    public string TableLayout { get; set; } = string.Empty;

    public string Search { get; set; } = string.Empty;

    /// <summary>0 all, 1 VST3, 2 VST2.</summary>
    public int FormatFilter { get; set; }

    /// <summary>0 all, 1 instruments, 2 effects.</summary>
    public int KindFilter { get; set; }

    public bool FavoritesOnly { get; set; }

    public bool ShowFailed { get; set; }

    public bool ShowDetails { get; set; } = true;

    public float DetailsRatio { get; set; } = 0.72f;

    /// <summary>Audio output endpoint (null = default device of Windows; "none" = no audio device).</summary>
    public string? AudioOutput { get; set; }

    /// <summary>Audio input endpoint (null = no input; "" = default input device of Windows).</summary>
    public string? AudioInput { get; set; }

    /// <summary>Requested buffer in samples (0 = the smallest the device allows).</summary>
    public int BufferFrames { get; set; }

    /// <summary>MIDI input port name (null = none).</summary>
    public string? MidiInput { get; set; }

    /// <summary>MIDI output port name (null = none): where plug-ins can send their MIDI.</summary>
    public string? MidiOutput { get; set; }
}

[JsonSerializable(typeof(GuiSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class GuiJson : JsonSerializerContext;
