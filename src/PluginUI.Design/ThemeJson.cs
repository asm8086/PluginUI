// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;

namespace PluginUI.Design;

/// <summary>
/// Themes as JSON: the theme section of a design (<see cref="DesignTheme"/>: base theme, accent and overrides) and
/// complete theme files (every palette color, typography and metric), exported to share a look between projects.
/// </summary>
public static class ThemeJson
{
    private static readonly JsonWriterOptions Indented = new() { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Builds the theme described by a design theme.</summary>
    /// <param name="design">The design theme.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="ArgumentException">An override names an unknown field or has the wrong type.</exception>
    public static Theme Apply(DesignTheme design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var baseTheme = BaseTheme(design.Base);
        var palette = baseTheme.Palette;
        if (design.Accent is { } accent)
        {
            palette = palette with { Accent = accent, Focus = accent.WithAlpha(0.8f), Selection = accent.WithAlpha(0.3f) };
        }
        foreach (var (name, color) in design.Palette) palette = RecordJson.With(palette, name, color);
        var typography = baseTheme.Typography;
        foreach (var (name, value) in design.Typography)
        {
            object v = value is double d ? (float)d : value;
            typography = RecordJson.With(typography, name, v);
        }
        var metrics = baseTheme.Metrics;
        foreach (var (name, value) in design.Metrics) metrics = RecordJson.With(metrics, name, value);
        // A new instance (not "with"): the theme caches styles derived from its palette.
        return new Theme(design.Base, palette, typography, metrics);
    }

    /// <summary>The built-in theme of a base name ("Dark" or "Light"; anything else: Dark).</summary>
    /// <param name="name">Base name.</param>
    /// <returns>The theme.</returns>
    public static Theme BaseTheme(string? name) => string.Equals(name, "Light", StringComparison.OrdinalIgnoreCase) ? Theme.Light : Theme.Dark;

    internal static void Write(Utf8JsonWriter writer, DesignTheme theme)
    {
        writer.WriteStartObject();
        writer.WriteString("base", theme.Base);
        if (theme.Accent is { } accent)
        {
            writer.WritePropertyName("accent");
            DesignValues.WriteColor(writer, accent);
        }
        if (theme.Palette.Count > 0 || HasUnknown(theme, "palette"))
        {
            writer.WriteStartObject("palette");
            foreach (var (name, color) in theme.Palette)
            {
                writer.WritePropertyName(name);
                DesignValues.WriteColor(writer, color);
            }
            WriteUnknown(writer, theme, "palette");
            writer.WriteEndObject();
        }
        if (theme.Typography.Count > 0 || HasUnknown(theme, "typography"))
        {
            writer.WriteStartObject("typography");
            foreach (var (name, value) in theme.Typography)
            {
                writer.WritePropertyName(name);
                DesignValues.Write(writer, value);
            }
            WriteUnknown(writer, theme, "typography");
            writer.WriteEndObject();
        }
        if (theme.Metrics.Count > 0 || HasUnknown(theme, "metrics"))
        {
            writer.WriteStartObject("metrics");
            foreach (var (name, value) in theme.Metrics)
            {
                writer.WritePropertyName(name);
                DesignValues.WriteNumber(writer, value);
            }
            WriteUnknown(writer, theme, "metrics");
            writer.WriteEndObject();
        }
        foreach (var (key, raw) in theme.UnknownMembers)
        {
            if (key.Contains('.', StringComparison.Ordinal) || key is "base" or "accent" or "palette" or "typography" or "metrics") continue;
            writer.WritePropertyName(key);
            writer.WriteRawValue(raw);
        }
        writer.WriteEndObject();
    }

    private static bool HasUnknown(DesignTheme theme, string section)
        => theme.UnknownMembers.Keys.Any(k => k.StartsWith(section + ".", StringComparison.Ordinal));

    private static void WriteUnknown(Utf8JsonWriter writer, DesignTheme theme, string section)
    {
        foreach (var (key, raw) in theme.UnknownMembers)
        {
            if (!key.StartsWith(section + ".", StringComparison.Ordinal)) continue;
            string name = key[(section.Length + 1)..];
            if (section == "palette" && theme.Palette.ContainsKey(name) || section == "typography" && theme.Typography.ContainsKey(name)
                || section == "metrics" && theme.Metrics.ContainsKey(name)) continue;
            writer.WritePropertyName(name);
            writer.WriteRawValue(raw);
        }
    }

    internal static DesignTheme Read(JsonElement json, ICollection<string>? warnings = null)
    {
        var theme = new DesignTheme();
        if (json.ValueKind != JsonValueKind.Object)
        {
            warnings?.Add("theme: object expected, the default theme is used.");
            return theme;
        }
        if (json.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.String) theme.Base = b.GetString() ?? "Dark";
        if (json.TryGetProperty("accent", out var a))
        {
            if (TryRead(a, typeof(Color), out var accent)) theme.Accent = (Color)accent!;
            else warnings?.Add($"theme: invalid accent color {a.GetRawText()} (ignored).");
        }
        // Overrides naming a field this version does not have, or with a value of the wrong type, are skipped with
        // a warning: a design saved by a newer version still opens.
        ReadOverrides(json, "palette", FieldTypes(Theme.Dark.Palette), theme, theme.Palette, warnings, v => (Color)v);
        ReadOverrides(json, "typography", FieldTypes(Theme.Dark.Typography), theme, theme.Typography, warnings, v => v);
        ReadOverrides(json, "metrics", FieldTypes(Theme.Dark.Metrics), theme, theme.Metrics, warnings, v => (float)v);
        foreach (var p in json.EnumerateObject())
        {
            if (p.Name is "base" or "accent" or "palette" or "typography" or "metrics") continue;
            warnings?.Add($"theme: unknown section '{p.Name}' (kept, not applied).");
            theme.UnknownMembers[p.Name] = p.Value.GetRawText();
        }
        return theme;
    }

    private static Dictionary<string, Type> FieldTypes<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(RecordJson.RecordMembers)] T>(T value) where T : class
        => RecordJson.Fields(value).ToDictionary(f => f.Name, f => f.Type, StringComparer.Ordinal);

    private static void ReadOverrides<TValue>(JsonElement json, string section, Dictionary<string, Type> fields, DesignTheme theme,
        IDictionary<string, TValue> target, ICollection<string>? warnings, Func<object, TValue> convert)
    {
        if (!json.TryGetProperty(section, out var overrides)) return;
        if (overrides.ValueKind != JsonValueKind.Object)
        {
            warnings?.Add($"theme.{section}: object expected (ignored).");
            return;
        }
        foreach (var p in overrides.EnumerateObject())
        {
            if (!fields.TryGetValue(p.Name, out var type))
            {
                warnings?.Add($"theme.{section}: unknown field '{p.Name}' (kept, not applied).");
                theme.UnknownMembers[section + "." + p.Name] = p.Value.GetRawText();
                continue;
            }
            if (!TryRead(p.Value, type, out var value) || value is null)
            {
                warnings?.Add($"theme.{section}.{p.Name}: invalid value {p.Value.GetRawText()} (kept, not applied).");
                theme.UnknownMembers[section + "." + p.Name] = p.Value.GetRawText();
                continue;
            }
            target[p.Name] = convert(value);
        }
    }

    private static bool TryRead(JsonElement json, Type type, out object? value)
    {
        try
        {
            value = DesignValues.Read(json, type);
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or NotSupportedException or KeyNotFoundException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Exports a complete theme (every field) as an indented JSON file content.</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>The JSON text.</returns>
    public static string Export(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, Indented))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "pluginui-theme");
            writer.WriteNumber("version", 1);
            writer.WriteString("name", theme.Name);
            writer.WritePropertyName("palette");
            RecordJson.Write<Palette>(writer, theme.Palette, null);
            writer.WritePropertyName("typography");
            RecordJson.Write<Typography>(writer, theme.Typography, null);
            writer.WritePropertyName("metrics");
            RecordJson.Write<Metrics>(writer, theme.Metrics, null);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Imports a theme file (as written by <see cref="Export"/>) as a design theme: the closest base theme plus the
    /// fields that differ from it.
    /// </summary>
    /// <param name="json">The theme file content.</param>
    /// <returns>The design theme.</returns>
    /// <exception cref="FormatException">The text is not a theme file.</exception>
    public static DesignTheme Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var palette = RecordJson.Read(root.GetProperty("palette"), Theme.Dark.Palette);
            var typography = root.TryGetProperty("typography", out var t) ? RecordJson.Read(t, Theme.Dark.Typography) : Theme.Dark.Typography;
            var metrics = root.TryGetProperty("metrics", out var m) ? RecordJson.Read(m, Theme.Dark.Metrics) : Theme.Dark.Metrics;
            string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Dark" : "Dark";
            return FromTheme(new Theme(name, palette, typography, metrics));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("Not a PluginUI theme file: " + e.Message, e);
        }
    }

    /// <summary>Describes a theme as the closest base theme (by background luminance) plus the fields that differ.</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>The design theme.</returns>
    public static DesignTheme FromTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var bg = theme.Palette.Background;
        bool light = bg.R + bg.G + bg.B > 1.5f;
        var baseTheme = light ? Theme.Light : Theme.Dark;
        var design = new DesignTheme { Base = light ? "Light" : "Dark" };
        var baseFields = RecordJson.Fields(baseTheme.Palette).ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
        foreach (var (field, _, value) in RecordJson.Fields(theme.Palette))
        {
            if (!DesignValues.AreEqual(value, baseFields[field])) design.Palette[field] = (Color)value!;
        }
        // Accent, Focus and Selection derived from the same accent collapse into "accent".
        if (design.Palette.TryGetValue("Accent", out var accent)
            && DesignValues.AreEqual(theme.Palette.Focus, accent.WithAlpha(0.8f))
            && DesignValues.AreEqual(theme.Palette.Selection, accent.WithAlpha(0.3f)))
        {
            design.Accent = accent;
            design.Palette.Remove("Accent");
            design.Palette.Remove("Focus");
            design.Palette.Remove("Selection");
        }
        var baseTypography = RecordJson.Fields(baseTheme.Typography).ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
        foreach (var (field, _, value) in RecordJson.Fields(theme.Typography))
        {
            if (!DesignValues.AreEqual(value, baseTypography[field])) design.Typography[field] = value!;
        }
        var baseMetrics = RecordJson.Fields(baseTheme.Metrics).ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
        foreach (var (field, _, value) in RecordJson.Fields(theme.Metrics))
        {
            if (!DesignValues.AreEqual(value, baseMetrics[field])) design.Metrics[field] = (float)value!;
        }
        return design;
    }

    /// <summary>C# expression building the theme of a design (generated code).</summary>
    /// <param name="design">The design theme.</param>
    /// <param name="names">Type name formatter.</param>
    /// <returns>The expression.</returns>
    public static string ToCSharp(DesignTheme design, Func<Type, string> names)
    {
        ArgumentNullException.ThrowIfNull(design);
        var theme = Apply(design);
        return $"new Theme({DesignValues.CSharpString(design.Base)}, {RecordJson.ToCSharp(theme.Palette, names)}, {RecordJson.ToCSharp(theme.Typography, names)}, {RecordJson.ToCSharp(theme.Metrics, names)})";
    }
}
