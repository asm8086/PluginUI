// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;

namespace PluginUI.Design;

/// <summary>
/// Reads and writes <c>.pluginui</c> files (UTF-8 JSON). Writing is deterministic (same document, same text), so
/// designs diff well under version control.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "format": "pluginui", "version": 1, "name": "DemoSynth",
///   "size": [820, 470], "sizing": "ScaleToFit",
///   "theme": { "base": "Dark", "accent": "#FFB74D" },
///   "parameters": [ { "name": "Cutoff", "kind": "Logarithmic", "min": 20, "max": 20000, "default": 2400, "unit": "Hz", "decimals": 0 } ],
///   "codegen": { "namespace": "DemoSynth.Editor", "class": "DemoSynthEditor", "target": "PluginEditorView" },
///   "root": { "type": "Canvas", "children": [
///     { "type": "Knob", "name": "cutoff", "properties": { "Left": 20, "Top": 40, "Width": 76, "Height": 104, "Label": "CUTOFF" },
///       "bindings": { "Parameter": "Cutoff" } } ] }
/// }
/// </code>
/// Property keys are the C# property names. A property or type this version does not know is kept as is and
/// written back unchanged.
/// </remarks>
public static class DesignJson
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonDocumentOptions ReaderOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>Format identifier written in every file.</summary>
    public const string FormatName = "pluginui";

    /// <summary>Serializes a document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>Indented JSON text.</returns>
    public static string Write(DesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions)) Write(writer, document);
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Saves a document to a file (UTF-8 without BOM).</summary>
    /// <param name="document">The document.</param>
    /// <param name="path">File path.</param>
    public static void Save(DesignDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        File.WriteAllText(path, Write(document), new UTF8Encoding(false));
    }

    /// <summary>Loads a file.</summary>
    /// <param name="path">File path.</param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    /// <param name="warnings">Receives messages about content that could not be interpreted (optional).</param>
    /// <returns>The document.</returns>
    /// <exception cref="FormatException">The file is not a valid design.</exception>
    public static DesignDocument Load(string path, DesignTypeRegistry? registry = null, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Read(File.ReadAllText(path), registry, warnings);
    }

    /// <summary>Parses a document.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    /// <param name="warnings">Receives messages about content that could not be interpreted (optional).</param>
    /// <returns>The document.</returns>
    /// <exception cref="FormatException">The text is not a valid design.</exception>
    public static DesignDocument Read(string json, DesignTypeRegistry? registry = null, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        registry ??= DesignTypeRegistry.Default;
        try
        {
            using var parsed = JsonDocument.Parse(json, ReaderOptions);
            var root = parsed.RootElement;
            if (!root.TryGetProperty("format", out var format) || format.GetString() != FormatName)
            {
                throw new FormatException("Not a PluginUI design (missing \"format\": \"pluginui\").");
            }
            var document = new DesignDocument();
            if (root.TryGetProperty("version", out var version))
            {
                document.FormatVersion = version.GetInt32();
                if (document.FormatVersion > DesignDocument.CurrentFormatVersion)
                {
                    warnings?.Add($"The design was saved by a newer version (format {document.FormatVersion}); unknown content is kept but not interpreted.");
                }
            }
            if (root.TryGetProperty("comment", out var comment) && comment.ValueKind == JsonValueKind.String) document.Comment = comment.GetString();
            if (root.TryGetProperty("name", out var name)) document.Name = name.GetString() ?? document.Name;
            if (root.TryGetProperty("size", out var size)) document.DesignSize = (Size)DesignValues.Read(size, typeof(Size))!;
            if (root.TryGetProperty("sizing", out var sizing)) document.Sizing = (SizingMode)DesignValues.Read(sizing, typeof(SizingMode))!;
            if (root.TryGetProperty("theme", out var theme)) document.Theme = ThemeJson.Read(theme, warnings);
            if (root.TryGetProperty("parameters", out var parameters))
            {
                foreach (var p in parameters.EnumerateArray()) document.Parameters.Add(ReadParameter(p));
            }
            if (root.TryGetProperty("codegen", out var codegen)) document.Codegen = ReadCodegen(codegen);
            document.Root = root.TryGetProperty("root", out var tree) ? ReadNode(tree, registry, warnings, "root") : new DesignNode("Canvas");
            ReadUnknown(root, document.UnknownMembers, DocumentMembers);
            if (document.UnknownMembers.Count > 0) warnings?.Add($"Unknown sections kept as they are: {string.Join(", ", document.UnknownMembers.Keys)}.");
            return document;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new FormatException("Invalid PluginUI design: " + e.Message, e);
        }
    }

    /// <summary>Format identifier of copied elements (clipboard).</summary>
    public const string NodesFormatName = "pluginui-nodes";

    /// <summary>Serializes elements (with their children) for the clipboard.</summary>
    /// <param name="nodes">The elements.</param>
    /// <returns>JSON text.</returns>
    public static string WriteNodes(IEnumerable<DesignNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("format", NodesFormatName);
            w.WriteStartArray("nodes");
            foreach (var node in nodes) WriteNode(w, node);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Parses elements written by <see cref="WriteNodes"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    /// <returns>The elements, or <c>null</c> when the text is not a copy of elements.</returns>
    public static IReadOnlyList<DesignNode>? ReadNodes(string json, DesignTypeRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        registry ??= DesignTypeRegistry.Default;
        try
        {
            using var parsed = JsonDocument.Parse(json, ReaderOptions);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != NodesFormatName) return null;
            return root.GetProperty("nodes").EnumerateArray().Select(n => ReadNode(n, registry, null, "clipboard")).ToList();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    // =====================================================================================
    // Writing
    // =====================================================================================

    private static void Write(Utf8JsonWriter w, DesignDocument d)
    {
        w.WriteStartObject();
        if (!string.IsNullOrEmpty(d.Comment)) w.WriteString("comment", d.Comment);
        w.WriteString("format", FormatName);
        // A file from a newer version keeps its version number: its unknown members are written back unchanged.
        w.WriteNumber("version", Math.Max(DesignDocument.CurrentFormatVersion, d.FormatVersion));
        w.WriteString("name", d.Name);
        w.WritePropertyName("size");
        DesignValues.Write(w, d.DesignSize);
        w.WriteString("sizing", d.Sizing.ToString());
        w.WritePropertyName("theme");
        ThemeJson.Write(w, d.Theme);
        w.WriteStartArray("parameters");
        foreach (var p in d.Parameters) WriteParameter(w, p);
        w.WriteEndArray();
        w.WriteStartObject("codegen");
        w.WriteString("namespace", d.Codegen.Namespace);
        w.WriteString("class", d.Codegen.ClassName);
        w.WriteString("target", d.Codegen.Target.ToString());
        w.WriteBoolean("buildOverride", d.Codegen.GenerateBuildOverride);
        w.WriteString("fieldAccess", d.Codegen.FieldAccess);
        WriteUnknown(w, d.Codegen.UnknownMembers, CodegenMembers);
        w.WriteEndObject();
        w.WritePropertyName("root");
        WriteNode(w, d.Root);
        WriteUnknown(w, d.UnknownMembers, DocumentMembers);
        w.WriteEndObject();
    }

    private static void WriteParameter(Utf8JsonWriter w, DesignParameter p)
    {
        w.WriteStartObject();
        w.WriteString("name", p.Name);
        w.WriteString("kind", p.Kind.ToString());
        if (p.Kind is not (DesignParameterKind.Toggle or DesignParameterKind.Choice))
        {
            w.WritePropertyName("min"); DesignValues.WriteNumber(w, p.Min);
            w.WritePropertyName("max"); DesignValues.WriteNumber(w, p.Max);
        }
        w.WritePropertyName("default"); DesignValues.WriteNumber(w, p.Default);
        if (p.Unit.Length > 0) w.WriteString("unit", p.Unit);
        if (p.Kind is DesignParameterKind.Linear or DesignParameterKind.Logarithmic) w.WriteNumber("decimals", p.Decimals);
        if (p.Kind == DesignParameterKind.Choice)
        {
            w.WriteStartArray("choices");
            foreach (var c in p.Choices) w.WriteStringValue(c);
            w.WriteEndArray();
        }
        WriteUnknown(w, p.UnknownMembers, ParameterMembers);
        w.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter w, DesignNode node)
    {
        w.WriteStartObject();
        w.WriteString("type", node.Type);
        if (node.Name is not null) w.WriteString("name", node.Name);
        if (node.Slot is not null) w.WriteString("slot", node.Slot);
        if (node.Properties.Count > 0 || node.UnknownProperties.Count > 0)
        {
            w.WriteStartObject("properties");
            foreach (var (key, value) in node.Properties)
            {
                w.WritePropertyName(key);
                DesignValues.Write(w, value);
            }
            foreach (var (key, raw) in node.UnknownProperties)
            {
                if (node.Properties.ContainsKey(key)) continue;
                w.WritePropertyName(key);
                w.WriteRawValue(raw);
            }
            w.WriteEndObject();
        }
        if (node.Bindings.Count > 0)
        {
            w.WriteStartObject("bindings");
            foreach (var (key, value) in node.Bindings) w.WriteString(key, value);
            w.WriteEndObject();
        }
        if (node.Children.Count > 0)
        {
            w.WriteStartArray("children");
            foreach (var child in node.Children) WriteNode(w, child);
            w.WriteEndArray();
        }
        WriteUnknown(w, node.UnknownMembers, NodeMembers);
        w.WriteEndObject();
    }

    // Members each object understands; anything else is kept raw (UnknownMembers) and written back.
    private static readonly HashSet<string> DocumentMembers = ["comment", "format", "version", "name", "size", "sizing", "theme", "parameters", "codegen", "root"];
    private static readonly HashSet<string> ParameterMembers = ["name", "kind", "min", "max", "default", "unit", "decimals", "choices"];
    private static readonly HashSet<string> CodegenMembers = ["namespace", "class", "target", "buildOverride", "fieldAccess"];
    private static readonly HashSet<string> NodeMembers = ["type", "name", "slot", "properties", "bindings", "children"];

    private static void WriteUnknown(Utf8JsonWriter w, OrderedDictionary<string, string> members, HashSet<string> known)
    {
        foreach (var (key, raw) in members)
        {
            if (known.Contains(key)) continue;
            w.WritePropertyName(key);
            w.WriteRawValue(raw);
        }
    }

    private static void ReadUnknown(JsonElement e, OrderedDictionary<string, string> members, HashSet<string> known)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (!known.Contains(p.Name)) members[p.Name] = p.Value.GetRawText();
        }
    }

    // =====================================================================================
    // Reading
    // =====================================================================================

    private static DesignParameter ReadParameter(JsonElement e)
    {
        var p = new DesignParameter { Name = e.GetProperty("name").GetString() ?? "Parameter" };
        if (e.TryGetProperty("kind", out var kind)) p.Kind = Enum.Parse<DesignParameterKind>(kind.GetString() ?? "Linear");
        if (e.TryGetProperty("min", out var min)) p.Min = (double)DesignValues.Read(min, typeof(double))!;
        if (e.TryGetProperty("max", out var max)) p.Max = (double)DesignValues.Read(max, typeof(double))!;
        if (e.TryGetProperty("default", out var def)) p.Default = (double)DesignValues.Read(def, typeof(double))!;
        if (e.TryGetProperty("unit", out var unit)) p.Unit = unit.GetString() ?? string.Empty;
        if (e.TryGetProperty("decimals", out var decimals)) p.Decimals = decimals.GetInt32();
        if (e.TryGetProperty("choices", out var choices)) p.Choices.AddRange(choices.EnumerateArray().Select(c => c.GetString() ?? string.Empty));
        if (p.Kind == DesignParameterKind.Toggle) { p.Min = 0; p.Max = 1; }
        if (p.Kind == DesignParameterKind.Choice) { p.Min = 0; p.Max = Math.Max(0, p.Choices.Count - 1); }
        ReadUnknown(e, p.UnknownMembers, ParameterMembers);
        return p;
    }

    private static CodegenSettings ReadCodegen(JsonElement e)
    {
        var c = new CodegenSettings();
        if (e.TryGetProperty("namespace", out var ns)) c.Namespace = ns.GetString() ?? c.Namespace;
        if (e.TryGetProperty("class", out var cls)) c.ClassName = cls.GetString() ?? c.ClassName;
        if (e.TryGetProperty("target", out var target)) c.Target = Enum.Parse<CodegenTarget>(target.GetString() ?? nameof(CodegenTarget.PluginEditorView));
        if (e.TryGetProperty("buildOverride", out var build)) c.GenerateBuildOverride = build.GetBoolean();
        if (e.TryGetProperty("fieldAccess", out var access)) c.FieldAccess = access.GetString() ?? c.FieldAccess;
        ReadUnknown(e, c.UnknownMembers, CodegenMembers);
        return c;
    }

    private static DesignNode ReadNode(JsonElement e, DesignTypeRegistry registry, ICollection<string>? warnings, string path)
    {
        var node = new DesignNode(e.GetProperty("type").GetString() ?? "Canvas");
        if (e.TryGetProperty("name", out var name)) node.Name = name.GetString();
        if (e.TryGetProperty("slot", out var slot)) node.Slot = slot.GetString();
        string where = node.Name is null ? $"{path}/{node.Type}" : $"{path}/{node.Name}";
        var info = registry.Find(node.Type);
        if (info is null) warnings?.Add($"{where}: unknown element type '{node.Type}'.");
        if (e.TryGetProperty("properties", out var properties))
        {
            foreach (var p in properties.EnumerateObject())
            {
                var property = info?.Find(p.Name);
                if (property is null || property.IsBinding)
                {
                    if (info is not null) warnings?.Add($"{where}: unknown property '{p.Name}' (kept).");
                    node.UnknownProperties[p.Name] = p.Value.GetRawText();
                    continue;
                }
                try
                {
                    node.Properties[p.Name] = DesignValues.Read(p.Value, property.PropertyType);
                }
                catch (FormatException ex)
                {
                    warnings?.Add($"{where}.{p.Name}: {ex.Message} (kept).");
                    node.UnknownProperties[p.Name] = p.Value.GetRawText();
                }
            }
        }
        if (e.TryGetProperty("bindings", out var bindings))
        {
            foreach (var b in bindings.EnumerateObject())
            {
                if (info is not null && info.Find(b.Name) is not { IsBinding: true }) warnings?.Add($"{where}: '{b.Name}' is not a parameter property.");
                node.Bindings[b.Name] = b.Value.GetString() ?? string.Empty;
            }
        }
        if (e.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray()) node.Children.Add(ReadNode(child, registry, warnings, where));
        }
        ReadUnknown(e, node.UnknownMembers, NodeMembers);
        return node;
    }
}
