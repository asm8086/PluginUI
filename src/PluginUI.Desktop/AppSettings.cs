// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PluginUI.Desktop;

/// <summary>
/// Settings of an application stored as one JSON file, NativeAOT compatible: the serialization code comes from a
/// <c>JsonSerializerContext</c> you declare (System.Text.Json source generation), never from reflection.
/// </summary>
/// <typeparam name="T">Settings class (public settable properties, a parameterless constructor).</typeparam>
/// <remarks>
/// <para>Saving writes a temporary file then replaces the old one, so that a crash never leaves a truncated file. A
/// missing or unreadable file gives the defaults (an unreadable file is kept as <c>.bad</c> for inspection).</para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class MySettings { public bool ShowGrid { get; set; } = true; public double SampleRate { get; set; } = 48000; }
///
/// [JsonSerializable(typeof(MySettings))]
/// [JsonSourceGenerationOptions(WriteIndented = true)]
/// internal partial class MyJson : JsonSerializerContext;
///
/// var settings = AppSettings&lt;MySettings&gt;.Load(app.GetDataPath("settings.json"), MyJson.Default.MySettings);
/// settings.Value.ShowGrid = false;
/// settings.Save();
/// </code>
/// </example>
public sealed class AppSettings<T> where T : class, new()
{
    private readonly JsonTypeInfo<T> _typeInfo;

    private AppSettings(string path, JsonTypeInfo<T> typeInfo, T value, bool loadedFromFile)
    {
        FilePath = path;
        _typeInfo = typeInfo;
        Value = value;
        LoadedFromFile = loadedFromFile;
    }

    /// <summary>Settings file.</summary>
    public string FilePath { get; }

    /// <summary>Current settings (edit them, then call <see cref="Save"/>).</summary>
    public T Value { get; set; }

    /// <summary><c>true</c> when <see cref="Value"/> was read from the file (otherwise: defaults).</summary>
    public bool LoadedFromFile { get; }

    /// <summary>Reads the file, or returns the defaults (<c>new T()</c>) when it is missing or unreadable.</summary>
    /// <param name="path">Settings file, e.g. <see cref="DesktopApplication.GetDataPath"/>("settings.json").</param>
    /// <param name="typeInfo">Generated metadata, e.g. <c>MyJson.Default.MySettings</c>.</param>
    public static AppSettings<T> Load(string path, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(typeInfo);
        var value = TryRead(path, typeInfo);
        return new AppSettings<T>(path, typeInfo, value ?? new T(), value is not null);
    }

    /// <summary>Writes the settings (creates the folder if needed). Returns <c>false</c> if the file cannot be written.</summary>
    public bool Save() => TryWrite(FilePath, Value, _typeInfo);

    /// <summary>Puts the defaults back (not saved until <see cref="Save"/>).</summary>
    public void Reset() => Value = new T();

    internal static T? TryRead(string path, JsonTypeInfo<T> typeInfo)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, typeInfo);
        }
        catch (JsonException)
        {
            TryKeepBadCopy(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    internal static bool TryWrite(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, value, typeInfo);
            }
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static void TryKeepBadCopy(string path)
    {
        try
        {
            File.Copy(path, path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
