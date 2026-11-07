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

    /// <summary>Audio driver: "wasapi", "asio" or "none" (null = from <see cref="AudioOutput"/>, as before ASIO).</summary>
    public string? AudioApi { get; set; }

    /// <summary>The driver in use: "wasapi", "asio" or "none".</summary>
    [JsonIgnore]
    public string Api => AudioApi ?? (AudioOutput == "none" ? "none" : "wasapi");

    /// <summary>Registered name of the ASIO driver (null = the first installed).</summary>
    public string? AsioDriver { get; set; }

    /// <summary>Use the inputs of the ASIO device.</summary>
    public bool AsioInputs { get; set; }

    /// <summary>ASIO sample rate (0 = the driver's current rate).</summary>
    public int SampleRate { get; set; }

    /// <summary>WASAPI output endpoint (null = default device of Windows; "none" = no audio device, before ASIO).</summary>
    public string? AudioOutput { get; set; }

    /// <summary>Audio input endpoint (null = no input; "" = default input device of Windows).</summary>
    public string? AudioInput { get; set; }

    /// <summary>Requested buffer in samples (0 = the smallest WASAPI allows, or the size of the ASIO control panel).</summary>
    public int BufferFrames { get; set; }

    /// <summary>MIDI input port name (null = none).</summary>
    public string? MidiInput { get; set; }

    /// <summary>MIDI output port name (null = none): where plug-ins can send their MIDI.</summary>
    public string? MidiOutput { get; set; }
}

[JsonSerializable(typeof(GuiSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class GuiJson : JsonSerializerContext;
