// 2026/11/06 23:57:00 UTC - PluginUI
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
}

[JsonSerializable(typeof(GuiSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class GuiJson : JsonSerializerContext;
