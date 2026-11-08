// 2026/11/08 12:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;

namespace PluginUI.Host.Graph;

/// <summary>A graph file could not be read completely (a module setting, a file it refers to).</summary>
public sealed class GraphLoadException : Exception
{
    /// <summary>Creates the exception.</summary>
    public GraphLoadException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public GraphLoadException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public GraphLoadException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>Reading and writing the values of graph files (JSON), for <see cref="GraphNode"/> implementations.</summary>
public static class GraphJson
{
    private static readonly HashSet<string> BaseFields = ["id", "type", "title", "x", "y"];

    /// <summary><c>true</c> for the fields the graph itself writes for every node.</summary>
    public static bool IsBaseField(string key) => BaseFields.Contains(key);

    /// <summary>An integer property, or <paramref name="fallback"/>.</summary>
    public static int Int(JsonObject json, string key, int fallback)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json[key] is not JsonValue v) return fallback;
        if (v.TryGetValue(out int i)) return i;
        return v.TryGetValue(out double d) && double.IsFinite(d) ? (int)Math.Round(d) : fallback;
    }

    /// <summary>A number property, or <paramref name="fallback"/>.</summary>
    public static double Double(JsonObject json, string key, double fallback)
    {
        ArgumentNullException.ThrowIfNull(json);
        return json[key] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? d : fallback;
    }

    /// <summary>A Boolean property, or <paramref name="fallback"/>.</summary>
    public static bool Bool(JsonObject json, string key, bool fallback)
    {
        ArgumentNullException.ThrowIfNull(json);
        return json[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
    }

    /// <summary>A string property, or <paramref name="fallback"/>.</summary>
    public static string? String(JsonObject json, string key, string? fallback)
    {
        ArgumentNullException.ThrowIfNull(json);
        return json[key] is JsonValue v && v.TryGetValue(out string? s) ? s : fallback;
    }

    internal static JsonObject Write(PluginReference reference) => new()
    {
        ["format"] = reference.Format == PluginFormat.Vst2 ? "vst2" : "vst3",
        ["path"] = reference.Path,
        ["classId"] = reference.ClassId,
        ["name"] = reference.Name,
        ["vendor"] = reference.Vendor,
    };

    internal static PluginReference? ReadReference(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var format = string.Equals(String(o, "format", "vst3"), "vst2", StringComparison.OrdinalIgnoreCase) ? PluginFormat.Vst2 : PluginFormat.Vst3;
        return new PluginReference(format, String(o, "path", null) ?? string.Empty, String(o, "classId", null) ?? string.Empty,
            String(o, "name", null) ?? string.Empty, String(o, "vendor", null) ?? string.Empty);
    }

    internal static JsonObject WritePorts(GraphNode node) => new()
    {
        ["inputs"] = Ports(node.Inputs),
        ["outputs"] = Ports(node.Outputs),
    };

    private static JsonArray Ports(IReadOnlyList<GraphPort> ports)
    {
        var array = new JsonArray();
        foreach (var p in ports)
        {
            array.Add((JsonNode)new JsonObject
            {
                ["key"] = p.Key,
                ["name"] = p.Name,
                ["kind"] = p.Kind == PortKind.Midi ? "midi" : "audio",
                ["channels"] = p.Channels,
            });
        }
        return array;
    }

    internal static (List<PortDefinition> Inputs, List<PortDefinition> Outputs) ReadPorts(JsonObject? json)
    {
        return (Read(json?["inputs"]), Read(json?["outputs"]));

        static List<PortDefinition> Read(JsonNode? node)
        {
            var list = new List<PortDefinition>();
            if (node is not JsonArray array) return list;
            foreach (var item in array)
            {
                if (item is not JsonObject o || String(o, "key", null) is not { Length: > 0 } key) continue;
                var kind = String(o, "kind", "audio") == "midi" ? PortKind.Midi : PortKind.Audio;
                list.Add(new PortDefinition(key, String(o, "name", key) ?? key, kind, Math.Max(1, Int(o, "channels", 2))));
            }
            return list;
        }
    }
}
