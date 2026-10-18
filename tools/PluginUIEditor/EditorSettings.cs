// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace PluginUIEditor;

/// <summary>User settings, saved as JSON in %AppData%\PluginUIEditor\settings.json.</summary>
internal sealed class EditorSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>"auto", "en" or "fr".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Snap grid in DIPs.</summary>
    public float GridSize { get; set; } = 4f;

    /// <summary>Recently opened designs, most recent first.</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>NPlug src folder written in new projects.</summary>
    public string NPlugRoot { get; set; } = string.Empty;

    /// <summary>PluginUI folder written in new projects.</summary>
    public string PluginUIRoot { get; set; } = string.Empty;

    /// <summary>Default parent folder of new projects.</summary>
    public string ProjectFolder { get; set; } = string.Empty;

    /// <summary>Assemblies whose custom elements are added to the toolbox (loaded at startup).</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>Default vendor of new projects.</summary>
    public string Vendor { get; set; } = "My Company";

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PluginUIEditor", "settings.json");

    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath), Options) ?? new EditorSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupted or locked settings: start from the defaults.
        }
        return new EditorSettings();
    }

    /// <summary>Settings that are never written (snapshot mode).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTransient { get; init; }

    public void Save()
    {
        if (IsTransient) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience: never fail because of them.
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }
}
