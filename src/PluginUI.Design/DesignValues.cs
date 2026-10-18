// 2026/10/18 17:31:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PluginUI.Design;

/// <summary>
/// Conversions of property values between the forms a design uses: JSON (<c>.pluginui</c>), design values (what a
/// <see cref="DesignNode"/> holds), live values (what the element property holds) and C# source.
/// </summary>
/// <remarks>
/// <para>Supported property types:</para>
/// <list type="bullet">
/// <item><c>string</c>, <c>bool</c>, <c>int</c>, <c>float</c>, <c>double</c>, <c>char</c>, enums, and their nullable forms;</item>
/// <item><see cref="Color"/> ("#RRGGBB", "#RRGGBBAA" or [r, g, b, a]), <see cref="Thickness"/>, <see cref="Point"/>,
/// <see cref="Size"/>, <see cref="Rect"/> (number arrays);</item>
/// <item><see cref="TextStyle"/>, <see cref="VectorStyle"/> (objects), <see cref="Paint"/> (color string or gradient object);</item>
/// <item>read-only lists and arrays of <c>string</c>, <c>float</c> and <c>double</c>;</item>
/// <item><see cref="ImageSource"/>, <see cref="SvgImage"/> (asset path → <see cref="DesignAsset"/>), <see cref="FilmStrip"/>
/// (→ <see cref="FilmStripDescriptor"/>) and <see cref="IControlSkin"/> (→ <see cref="SkinDescriptor"/>).</item>
/// </list>
/// <para>Numbers always use the invariant culture. NaN and infinities are written as the strings "NaN", "Infinity", "-Infinity".</para>
/// </remarks>
public static class DesignValues
{
    private static readonly JsonWriterOptions CompactWriter = new() { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><c>true</c> if properties of this type can be designed.</summary>
    /// <param name="type">Property type.</param>
    /// <returns>Whether the type is supported.</returns>
    public static bool IsSupported(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(double)
            || t == typeof(char) || t.IsEnum || t == typeof(Color) || t == typeof(Thickness) || t == typeof(Point)
            || t == typeof(Size) || t == typeof(Rect) || t == typeof(TextStyle) || t == typeof(VectorStyle)
            || typeof(Paint).IsAssignableFrom(t) || t == typeof(ImageSource) || t == typeof(SvgImage) || t == typeof(FilmStrip)
            || t == typeof(IControlSkin) || ListElementType(t) is not null;
    }

    /// <summary><c>true</c> if values of this type are asset references in a design (not live values).</summary>
    /// <param name="type">Property type.</param>
    /// <returns>Whether the design value differs from the live value.</returns>
    public static bool IsAssetType(Type type) => type == typeof(ImageSource) || type == typeof(SvgImage) || type == typeof(FilmStrip) || type == typeof(IControlSkin);

    private static Type? ListElementType(Type t)
    {
        if (t == typeof(string[]) || t == typeof(IReadOnlyList<string>) || t == typeof(IList<string>)) return typeof(string);
        if (t == typeof(float[]) || t == typeof(IReadOnlyList<float>) || t == typeof(IList<float>)) return typeof(float);
        if (t == typeof(double[]) || t == typeof(IReadOnlyList<double>) || t == typeof(IList<double>)) return typeof(double);
        return null;
    }

    // =====================================================================================
    // JSON → design value
    // =====================================================================================

    /// <summary>Reads a design value for a property of type <paramref name="type"/>.</summary>
    /// <param name="json">The JSON value.</param>
    /// <param name="type">Property type.</param>
    /// <returns>The design value.</returns>
    /// <exception cref="FormatException">The JSON does not match the type.</exception>
    public static object? Read(JsonElement json, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (json.ValueKind == JsonValueKind.Null) return null;
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) type = underlying;
        try
        {
            if (type == typeof(string)) return json.GetString();
            if (type == typeof(bool)) return json.GetBoolean();
            if (type == typeof(int)) return json.GetInt32();
            if (type == typeof(float)) return (float)ReadDouble(json);
            if (type == typeof(double)) return ReadDouble(json);
            if (type == typeof(char)) return json.GetString() is { Length: > 0 } s ? s[0] : '\0';
            if (type.IsEnum) return Enum.Parse(type, json.GetString() ?? throw new FormatException("Enum name expected."));
            if (type == typeof(Color)) return ReadColor(json);
            if (type == typeof(Thickness)) return ReadThickness(json);
            if (type == typeof(Point)) { var a = Floats(json, 2); return new Point(a[0], a[1]); }
            if (type == typeof(Size)) { var a = Floats(json, 2); return new Size(a[0], a[1]); }
            if (type == typeof(Rect)) { var a = Floats(json, 4); return new Rect(a[0], a[1], a[2], a[3]); }
            if (type == typeof(TextStyle)) return RecordJson.Read(json, TextStyle.Default);
            if (type == typeof(VectorStyle)) return RecordJson.Read(json, PluginUI.Theme.Dark.DefaultStyle);
            if (typeof(Paint).IsAssignableFrom(type)) return ReadPaint(json);
            if (type == typeof(ImageSource)) return new DesignAsset(DesignAsset.Normalize(json.GetString() ?? string.Empty));
            if (type == typeof(SvgImage)) return new DesignAsset(DesignAsset.Normalize(json.GetString() ?? string.Empty), DesignAssetKind.Svg);
            if (type == typeof(FilmStrip)) return ObjectJson.Read(json, new FilmStripDescriptor());
            if (type == typeof(IControlSkin)) return ReadSkin(json);
            if (ListElementType(type) is { } element)
            {
                if (element == typeof(string)) return json.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
                if (element == typeof(float)) return json.EnumerateArray().Select(e => (float)ReadDouble(e)).ToArray();
                return json.EnumerateArray().Select(ReadDouble).ToArray();
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or OverflowException or KeyNotFoundException)
        {
            throw new FormatException($"Invalid {type.Name} value: {json.GetRawText()}", e);
        }
        throw new FormatException($"Unsupported property type {type.FullName}.");
    }

    private static double ReadDouble(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            return json.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                var s => InvariantNumber.TryParse(s ?? string.Empty, out var v) ? v : throw new FormatException($"Invalid number '{s}'."),
            };
        }
        return json.GetDouble();
    }

    private static float[] Floats(JsonElement json, int count)
    {
        var values = json.EnumerateArray().Select(e => (float)ReadDouble(e)).ToArray();
        if (values.Length != count) throw new FormatException($"{count} numbers expected: {json.GetRawText()}");
        return values;
    }

    private static Color ReadColor(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String) return Color.Parse(json.GetString()!);
        var a = json.EnumerateArray().Select(e => (float)ReadDouble(e)).ToArray();
        return a.Length switch
        {
            3 => new Color(a[0], a[1], a[2]),
            4 => new Color(a[0], a[1], a[2], a[3]),
            _ => throw new FormatException($"Color expected: {json.GetRawText()}"),
        };
    }

    private static Thickness ReadThickness(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number) return new Thickness((float)json.GetDouble());
        var a = json.EnumerateArray().Select(e => (float)ReadDouble(e)).ToArray();
        return a.Length switch
        {
            1 => new Thickness(a[0]),
            2 => new Thickness(a[0], a[1]),
            4 => new Thickness(a[0], a[1], a[2], a[3]),
            _ => throw new FormatException($"Thickness expected: {json.GetRawText()}"),
        };
    }

    private static Paint ReadPaint(JsonElement json)
    {
        if (json.ValueKind is JsonValueKind.String or JsonValueKind.Array) return new SolidPaint(ReadColor(json));
        string kind = json.GetProperty("type").GetString() ?? string.Empty;
        var stops = json.GetProperty("stops").EnumerateArray()
            .Select(s => new GradientStop((float)ReadDouble(s[0]), ReadColor(s[1]))).ToArray();
        bool relative = !json.TryGetProperty("relative", out var r) || r.GetBoolean();
        Point P(string name, Point fallback) => json.TryGetProperty(name, out var p) ? (Point)Read(p, typeof(Point))! : fallback;
        float F(string name, float fallback) => json.TryGetProperty(name, out var p) ? (float)ReadDouble(p) : fallback;
        return kind switch
        {
            "linear" => new LinearGradientPaint(P("start", default), P("end", new Point(0f, 1f)), stops, relative),
            "radial" => new RadialGradientPaint(P("center", new Point(0.5f, 0.5f)), F("radiusX", 0.5f), F("radiusY", 0.5f), stops, P("origin", default), relative),
            _ => throw new FormatException($"Unknown paint type '{kind}'."),
        };
    }

    private static SkinDescriptor ReadSkin(JsonElement json)
    {
        string kind = json.GetProperty("kind").GetString() ?? string.Empty;
        return kind switch
        {
            "FilmStrip" => ObjectJson.Read(json, new FilmStripSkinDescriptor()),
            "ImageRotary" => ObjectJson.Read(json, new ImageRotarySkinDescriptor()),
            "SvgRotary" => ObjectJson.Read(json, new SvgRotarySkinDescriptor()),
            "ImageState" => ObjectJson.Read(json, new ImageStateSkinDescriptor()),
            _ => throw new FormatException($"Unknown skin kind '{kind}'."),
        };
    }

    // =====================================================================================
    // Design value → JSON
    // =====================================================================================

    /// <summary>Writes a design value.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The design value.</param>
    /// <exception cref="NotSupportedException">The value type is not supported.</exception>
    public static void Write(Utf8JsonWriter writer, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case int i: writer.WriteNumberValue(i); break;
            case float f: WriteNumber(writer, f); break;
            case double d: WriteNumber(writer, d); break;
            case char c: writer.WriteStringValue(c.ToString()); break;
            case Enum e: writer.WriteStringValue(e.ToString()); break;
            case Color c: WriteColor(writer, c); break;
            case Thickness t:
                if (t.Left == t.Top && t.Top == t.Right && t.Right == t.Bottom) WriteNumber(writer, t.Left);
                else WriteFloats(writer, t.Left, t.Top, t.Right, t.Bottom);
                break;
            case Point p: WriteFloats(writer, p.X, p.Y); break;
            case Size s: WriteFloats(writer, s.Width, s.Height); break;
            case Rect r: WriteFloats(writer, r.X, r.Y, r.Width, r.Height); break;
            case TextStyle ts: RecordJson.Write(writer, ts, TextStyle.Default); break;
            case VectorStyle vs: RecordJson.Write(writer, vs, null); break;
            case Paint paint: WritePaint(writer, paint); break;
            case DesignAsset asset: writer.WriteStringValue(asset.Path); break;
            case FilmStripDescriptor strip: ObjectJson.Write(writer, strip, null); break;
            case FilmStripSkinDescriptor skin: ObjectJson.Write(writer, skin, skin.Kind); break;
            case ImageRotarySkinDescriptor skin: ObjectJson.Write(writer, skin, skin.Kind); break;
            case SvgRotarySkinDescriptor skin: ObjectJson.Write(writer, skin, skin.Kind); break;
            case ImageStateSkinDescriptor skin: ObjectJson.Write(writer, skin, skin.Kind); break;
            case string[] list: writer.WriteStartArray(); foreach (var s in list) writer.WriteStringValue(s); writer.WriteEndArray(); break;
            case float[] list: writer.WriteStartArray(); foreach (var f in list) WriteNumber(writer, f); writer.WriteEndArray(); break;
            case double[] list: writer.WriteStartArray(); foreach (var d in list) WriteNumber(writer, d); writer.WriteEndArray(); break;
            default: throw new NotSupportedException($"Unsupported design value {value.GetType().FullName}.");
        }
    }

    internal static void WriteNumber(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value)) writer.WriteStringValue("NaN");
        else if (double.IsPositiveInfinity(value)) writer.WriteStringValue("Infinity");
        else if (double.IsNegativeInfinity(value)) writer.WriteStringValue("-Infinity");
        else writer.WriteNumberValue(value);
    }

    internal static void WriteNumber(Utf8JsonWriter writer, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) WriteNumber(writer, (double)value);
        else writer.WriteRawValue(value.ToString("R", CultureInfo.InvariantCulture), skipInputValidation: false);
    }

    private static void WriteFloats(Utf8JsonWriter writer, params ReadOnlySpan<float> values)
    {
        foreach (var v in values)
        {
            if (!float.IsFinite(v))
            {
                writer.WriteStartArray();
                foreach (var x in values) WriteNumber(writer, x);
                writer.WriteEndArray();
                return;
            }
        }
        // Small numeric tuples stay on one line ("size": [820, 470]) even in indented files.
        var text = new System.Text.StringBuilder("[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) text.Append(writer.Options.Indented ? ", " : ",");
            text.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }
        writer.WriteRawValue(text.Append(']').ToString(), skipInputValidation: false);
    }

    private static bool OnByteGrid(float v) => MathF.Abs(MathF.Round(v * 255f) / 255f - v) < 1e-7f;

    internal static void WriteColor(Utf8JsonWriter writer, Color c)
    {
        if (OnByteGrid(c.R) && OnByteGrid(c.G) && OnByteGrid(c.B) && OnByteGrid(c.A) && c.R is >= 0f and <= 1f && c.G is >= 0f and <= 1f && c.B is >= 0f and <= 1f && c.A is >= 0f and <= 1f)
        {
            writer.WriteStringValue(c.ToHex());
        }
        else
        {
            WriteFloats(writer, c.R, c.G, c.B, c.A);
        }
    }

    private static void WritePaint(Utf8JsonWriter writer, Paint paint)
    {
        switch (paint)
        {
            case SolidPaint solid:
                WriteColor(writer, solid.Color);
                return;
            case LinearGradientPaint linear:
                writer.WriteStartObject();
                writer.WriteString("type", "linear");
                writer.WritePropertyName("start"); Write(writer, linear.Start);
                writer.WritePropertyName("end"); Write(writer, linear.End);
                WriteStops(writer, linear.Stops);
                writer.WriteBoolean("relative", linear.RelativeToBounds);
                writer.WriteEndObject();
                return;
            case RadialGradientPaint radial:
                writer.WriteStartObject();
                writer.WriteString("type", "radial");
                writer.WritePropertyName("center"); Write(writer, radial.Center);
                writer.WritePropertyName("radiusX"); WriteNumber(writer, radial.RadiusX);
                writer.WritePropertyName("radiusY"); WriteNumber(writer, radial.RadiusY);
                writer.WritePropertyName("origin"); Write(writer, radial.OriginOffset);
                WriteStops(writer, radial.Stops);
                writer.WriteBoolean("relative", radial.RelativeToBounds);
                writer.WriteEndObject();
                return;
            default:
                throw new NotSupportedException($"Unsupported paint {paint.GetType().Name}.");
        }
    }

    private static void WriteStops(Utf8JsonWriter writer, GradientStop[] stops)
    {
        writer.WritePropertyName("stops");
        writer.WriteStartArray();
        foreach (var s in stops)
        {
            writer.WriteStartArray();
            WriteNumber(writer, s.Offset);
            WriteColor(writer, s.Color);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }

    /// <summary>Compact JSON of a design value (also used to compare values).</summary>
    /// <param name="value">The design value.</param>
    /// <returns>The JSON text.</returns>
    public static string ToJsonString(object? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CompactWriter)) Write(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Parses compact JSON produced by <see cref="ToJsonString"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="type">Property type.</param>
    /// <returns>The design value.</returns>
    public static object? FromJsonString(string json, Type type)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement, type);
    }

    /// <summary><c>true</c> if both design values are equal (compared through their JSON form).</summary>
    /// <param name="a">First value.</param>
    /// <param name="b">Second value.</param>
    /// <returns>The comparison.</returns>
    public static bool AreEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        try
        {
            return ToJsonString(a) == ToJsonString(b);
        }
        catch (NotSupportedException)
        {
            return Equals(a, b);
        }
    }

    // =====================================================================================
    // Design value ↔ live value
    // =====================================================================================

    /// <summary>Turns a design value into the value assigned to the element property.</summary>
    /// <param name="value">The design value.</param>
    /// <param name="type">Property type.</param>
    /// <param name="assets">Resolves asset references (required when the value references assets).</param>
    /// <returns>The live value.</returns>
    /// <exception cref="InvalidOperationException">The value references an asset and no resolver was given.</exception>
    public static object? Materialize(object? value, Type type, IDesignAssetResolver? assets)
    {
        ArgumentNullException.ThrowIfNull(type);
        IDesignAssetResolver Assets() => assets ?? throw new InvalidOperationException("The design references assets: an asset resolver is required.");
        return value switch
        {
            DesignAsset { Kind: DesignAssetKind.Svg } svg => Assets().LoadSvg(svg.Path),
            DesignAsset image => Assets().LoadImage(image.Path),
            FilmStripDescriptor strip => strip.Create(Assets()),
            SkinDescriptor skin => skin.Create(Assets()),
            double d when (Nullable.GetUnderlyingType(type) ?? type) == typeof(float) => (float)d,
            _ => value,
        };
    }

    /// <summary>Converts the value of a live property into a design value.</summary>
    /// <param name="value">The live value.</param>
    /// <param name="type">Property type.</param>
    /// <param name="designValue">The design value.</param>
    /// <returns><c>false</c> for values a design cannot describe (loaded images, custom skins, unsupported types).</returns>
    public static bool TryCapture(object? value, Type type, out object? designValue)
    {
        ArgumentNullException.ThrowIfNull(type);
        designValue = null;
        if (value is null) return true;
        if (IsAssetType(type) || !IsSupported(type)) return false;
        designValue = value switch
        {
            IEnumerable<string> strings when value is not string => strings.ToArray(),
            IEnumerable<float> floats => floats.ToArray(),
            IEnumerable<double> doubles => doubles.ToArray(),
            _ => value,
        };
        return true;
    }

    internal static void CollectAssets(object? value, List<string> paths)
    {
        switch (value)
        {
            case DesignAsset asset when asset.Path.Length > 0: paths.Add(asset.Path); break;
            case FilmStripDescriptor strip when strip.Image.Length > 0: paths.Add(strip.Image); break;
            case SkinDescriptor skin: paths.AddRange(skin.Assets().Select(a => a.Path)); break;
        }
    }

    // =====================================================================================
    // Design value → C#
    // =====================================================================================

    /// <summary>C# expression creating a design value.</summary>
    /// <param name="value">The design value.</param>
    /// <param name="type">Property type.</param>
    /// <param name="names">Formats type names (e.g. adds <c>global::</c> outside the PluginUI namespace).</param>
    /// <returns>The expression.</returns>
    /// <exception cref="NotSupportedException">The value type is not supported.</exception>
    public static string ToCSharp(object? value, Type type, Func<Type, string> names)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(names);
        return value switch
        {
            null => "null",
            string s => CSharpString(s),
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            float f => CSharpFloat(f),
            double d => CSharpDouble(d),
            char c => CSharpChar(c),
            Enum e => CSharpEnum(e, names),
            Color c => CSharpColor(c),
            Thickness t => $"new Thickness({CSharpFloat(t.Left)}, {CSharpFloat(t.Top)}, {CSharpFloat(t.Right)}, {CSharpFloat(t.Bottom)})",
            Point p => $"new Point({CSharpFloat(p.X)}, {CSharpFloat(p.Y)})",
            Size s => $"new Size({CSharpFloat(s.Width)}, {CSharpFloat(s.Height)})",
            Rect r => $"new Rect({CSharpFloat(r.X)}, {CSharpFloat(r.Y)}, {CSharpFloat(r.Width)}, {CSharpFloat(r.Height)})",
            TextStyle ts => RecordJson.ToCSharp(ts, names),
            VectorStyle vs => RecordJson.ToCSharp(vs, names),
            SolidPaint solid => $"new SolidPaint({CSharpColor(solid.Color)})",
            LinearGradientPaint lin => $"new LinearGradientPaint({ToCSharp(lin.Start, typeof(Point), names)}, {ToCSharp(lin.End, typeof(Point), names)}, {CSharpStops(lin.Stops)}, {(lin.RelativeToBounds ? "true" : "false")})",
            RadialGradientPaint rad => $"new RadialGradientPaint({ToCSharp(rad.Center, typeof(Point), names)}, {CSharpFloat(rad.RadiusX)}, {CSharpFloat(rad.RadiusY)}, {CSharpStops(rad.Stops)}, {ToCSharp(rad.OriginOffset, typeof(Point), names)}, {(rad.RelativeToBounds ? "true" : "false")})",
            DesignAsset { Kind: DesignAssetKind.Svg } svg => $"LoadSvg({CSharpString(svg.Path)})",
            DesignAsset image => $"LoadImage({CSharpString(image.Path)})",
            FilmStripDescriptor strip => $"new FilmStrip(LoadImage({CSharpString(strip.Image)}), {strip.FrameCount.ToString(CultureInfo.InvariantCulture)}, {CSharpEnum(strip.Orientation, names)})",
            FilmStripSkinDescriptor fs => $"new FilmStripSkin(new FilmStrip(LoadImage({CSharpString(fs.Image)}), {fs.FrameCount.ToString(CultureInfo.InvariantCulture)}, {CSharpEnum(fs.Orientation, names)}), {CSharpEnum(fs.Fit, names)}, {(fs.UseStateIndex ? "true" : "false")})",
            ImageRotarySkinDescriptor ir => $"new ImageRotarySkin(LoadImage({CSharpString(ir.Rotor)}), {OptionalAsset(ir.Background, "LoadImage")}, {OptionalAsset(ir.Foreground, "LoadImage")}) {{ MinAngle = {CSharpFloat(ir.MinAngle)}, MaxAngle = {CSharpFloat(ir.MaxAngle)}, Fit = {CSharpEnum(ir.Fit, names)} }}",
            SvgRotarySkinDescriptor sr => $"new SvgRotarySkin(LoadSvg({CSharpString(sr.Rotor)}), {OptionalAsset(sr.Background, "LoadSvg")}, {OptionalAsset(sr.Foreground, "LoadSvg")}) {{ MinAngle = {CSharpFloat(sr.MinAngle)}, MaxAngle = {CSharpFloat(sr.MaxAngle)} }}",
            ImageStateSkinDescriptor st => $"new ImageStateSkin(LoadImage({CSharpString(st.Normal)}), {OptionalAsset(st.Active, "LoadImage")}) {{ Hover = {OptionalAsset(st.Hover, "LoadImage")}, Pressed = {OptionalAsset(st.Pressed, "LoadImage")}, Disabled = {OptionalAsset(st.Disabled, "LoadImage")}, Fit = {CSharpEnum(st.Fit, names)} }}",
            string[] list => "[" + string.Join(", ", list.Select(CSharpString)) + "]",
            float[] list => "[" + string.Join(", ", list.Select(CSharpFloat)) + "]",
            double[] list => "[" + string.Join(", ", list.Select(CSharpDouble)) + "]",
            _ => throw new NotSupportedException($"Unsupported design value {value.GetType().FullName}."),
        };
    }

    /// <summary><c>true</c> if <see cref="ToCSharp"/> of this value calls <c>LoadImage</c> or <c>LoadSvg</c>.</summary>
    /// <param name="value">The design value.</param>
    /// <param name="kind">Asset kind.</param>
    /// <returns>Whether the generated code needs that loader.</returns>
    public static bool UsesAssetLoader(object? value, DesignAssetKind kind) => value switch
    {
        DesignAsset asset => asset.Kind == kind,
        FilmStripDescriptor => kind == DesignAssetKind.Image,
        SkinDescriptor skin => skin.Assets().Any(a => a.Kind == kind),
        _ => false,
    };

    private static string OptionalAsset(string? path, string loader) => string.IsNullOrEmpty(path) ? "null" : $"{loader}({CSharpString(path)})";

    private static string CSharpStops(GradientStop[] stops)
        => "[" + string.Join(", ", stops.Select(s => $"new GradientStop({CSharpFloat(s.Offset)}, {CSharpColor(s.Color)})")) + "]";

    internal static string CSharpFloat(float f)
    {
        if (float.IsNaN(f)) return "float.NaN";
        if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
        return f.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    internal static string CSharpDouble(double d)
    {
        if (double.IsNaN(d)) return "double.NaN";
        if (double.IsPositiveInfinity(d)) return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(d)) return "double.NegativeInfinity";
        string s = d.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }

    internal static string CSharpColor(Color c)
    {
        static bool Byte(float v) => v is >= 0f and <= 1f && OnByteGrid(v);
        if (Byte(c.R) && Byte(c.G) && Byte(c.B))
        {
            uint rgb = ((uint)MathF.Round(c.R * 255f) << 16) | ((uint)MathF.Round(c.G * 255f) << 8) | (uint)MathF.Round(c.B * 255f);
            string hex = "0x" + rgb.ToString("X6", CultureInfo.InvariantCulture);
            return c.A >= 1f ? $"Color.FromRgb({hex})" : $"Color.FromRgb({hex}, {CSharpFloat(c.A)})";
        }
        return $"new Color({CSharpFloat(c.R)}, {CSharpFloat(c.G)}, {CSharpFloat(c.B)}, {CSharpFloat(c.A)})";
    }

    internal static string CSharpEnum(Enum e, Func<Type, string> names)
    {
        string typeName = names(e.GetType());
        string text = e.ToString();
        // Undefined values: "(T)(-1)" (without the parentheses, "(T)-1" parses as a subtraction).
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) return $"({typeName})({text})";
        return string.Join(" | ", text.Split(", ").Select(part => typeName + "." + part));
    }

    internal static string CSharpChar(char c) => c switch
    {
        '\'' => @"'\''",
        '\\' => @"'\\'",
        '\0' => @"'\0'",
        '\n' => @"'\n'",
        '\r' => @"'\r'",
        '\t' => @"'\t'",
        _ when char.IsControl(c) || char.IsSurrogate(c) || c is '\u2028' or '\u2029' or '\u0085' => "'\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + "'",
        _ => "'" + c + "'",
    };

    /// <summary>
    /// Text safe to put in a single-line C# comment: line terminators (CR, LF, NEL, LS, PS) and other control
    /// characters are replaced by spaces, so a name cannot end the comment and inject code.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <returns>The text on one line.</returns>
    public static string CommentText(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c);
        return sb.ToString();
    }

    /// <summary>C# string literal.</summary>
    /// <param name="s">The text.</param>
    /// <returns>The literal, with escapes.</returns>
    public static string CSharpString(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    // Line separators end a C# string literal like \n does: escape them too.
                    if (char.IsControl(c) || c is '\u2028' or '\u2029' or '\u0085') sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}

/// <summary>JSON and C# for the immutable records of PluginUI (<see cref="TextStyle"/>, <see cref="VectorStyle"/>, theme parts).</summary>
/// <remarks>
/// Uses the primary constructor and the matching public properties of a statically known record type, so the
/// reflection stays trimming- and NativeAOT-safe.
/// </remarks>
internal static class RecordJson
{
    internal const DynamicallyAccessedMemberTypes RecordMembers = DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties;

    private static ConstructorInfo PrimaryConstructor([DynamicallyAccessedMembers(RecordMembers)] Type type)
        => type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

    private static PropertyInfo Property([DynamicallyAccessedMembers(RecordMembers)] Type type, string name)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"{type.Name} has no property matching the constructor parameter '{name}'.");

    /// <summary>Writes every constructor field of <paramref name="value"/>, or only those differing from <paramref name="defaults"/>.</summary>
    public static void Write<[DynamicallyAccessedMembers(RecordMembers)] T>(Utf8JsonWriter writer, T value, T? defaults) where T : class
    {
        writer.WriteStartObject();
        foreach (var parameter in PrimaryConstructor(typeof(T)).GetParameters())
        {
            var property = Property(typeof(T), parameter.Name!);
            object? v = property.GetValue(value);
            if (defaults is not null && DesignValues.AreEqual(v, property.GetValue(defaults))) continue;
            writer.WritePropertyName(property.Name);
            DesignValues.Write(writer, v);
        }
        writer.WriteEndObject();
    }

    /// <summary>Reads a record: fields missing from the JSON come from <paramref name="defaults"/>.</summary>
    public static T Read<[DynamicallyAccessedMembers(RecordMembers)] T>(JsonElement json, T defaults) where T : class
    {
        if (json.ValueKind != JsonValueKind.Object) throw new FormatException($"Object expected for {typeof(T).Name}: {json.GetRawText()}");
        var ctor = PrimaryConstructor(typeof(T));
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var property = Property(typeof(T), parameters[i].Name!);
            args[i] = json.TryGetProperty(property.Name, out var field)
                ? DesignValues.Read(field, property.PropertyType)
                : property.GetValue(defaults);
        }
        return (T)ctor.Invoke(args);
    }

    /// <summary>Creates a record with one constructor field replaced.</summary>
    public static T With<[DynamicallyAccessedMembers(RecordMembers)] T>(T value, string field, object? fieldValue) where T : class
    {
        var ctor = PrimaryConstructor(typeof(T));
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        bool found = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            var property = Property(typeof(T), parameters[i].Name!);
            if (string.Equals(property.Name, field, StringComparison.Ordinal))
            {
                args[i] = fieldValue;
                found = true;
            }
            else
            {
                args[i] = property.GetValue(value);
            }
        }
        if (!found) throw new ArgumentException($"{typeof(T).Name} has no field '{field}'.", nameof(field));
        return (T)ctor.Invoke(args);
    }

    /// <summary>Constructor fields (name, type, value).</summary>
    public static IEnumerable<(string Name, Type Type, object? Value)> Fields<[DynamicallyAccessedMembers(RecordMembers)] T>(T value) where T : class
    {
        foreach (var parameter in PrimaryConstructor(typeof(T)).GetParameters())
        {
            var property = Property(typeof(T), parameter.Name!);
            yield return (property.Name, property.PropertyType, property.GetValue(value));
        }
    }

    /// <summary>C# constructor call with named arguments.</summary>
    public static string ToCSharp<[DynamicallyAccessedMembers(RecordMembers)] T>(T value, Func<Type, string> names) where T : class
    {
        var args = new List<string>();
        foreach (var parameter in PrimaryConstructor(typeof(T)).GetParameters())
        {
            var property = Property(typeof(T), parameter.Name!);
            args.Add($"{parameter.Name}: {DesignValues.ToCSharp(property.GetValue(value), property.PropertyType, names)}");
        }
        return $"new {names(typeof(T))}({string.Join(", ", args)})";
    }
}

/// <summary>JSON for the mutable descriptor classes of this library (public settable properties).</summary>
internal static class ObjectJson
{
    public static void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(Utf8JsonWriter writer, T value, string? kind) where T : class
    {
        writer.WriteStartObject();
        if (kind is not null) writer.WriteString("kind", kind);
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true }) continue;
            object? v = property.GetValue(value);
            if (v is null) continue;
            writer.WritePropertyName(property.Name);
            DesignValues.Write(writer, v);
        }
        writer.WriteEndObject();
    }

    public static T Read<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(JsonElement json, T target) where T : class
    {
        if (json.ValueKind != JsonValueKind.Object) throw new FormatException($"Object expected for {typeof(T).Name}: {json.GetRawText()}");
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true }) continue;
            if (json.TryGetProperty(property.Name, out var field)) property.SetValue(target, DesignValues.Read(field, property.PropertyType));
        }
        return target;
    }
}
