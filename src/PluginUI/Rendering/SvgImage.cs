// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginUI;

/// <summary>
/// SVG document rendered natively by Direct2D (<c>ID2D1SvgDocument</c>, Windows 10 1703+).
/// </summary>
/// <remarks>
/// Direct2D supports the static SVG 1.1 subset (shapes, paths, gradients, transforms, groups, clip paths);
/// no text, filters, scripts or animations. When the platform cannot render SVG, a neutral placeholder is drawn.
/// </remarks>
public sealed partial class SvgImage
{
    private SvgImage(byte[] utf8)
    {
        Data = utf8;
        IntrinsicSize = ParseSize(Encoding.UTF8.GetString(utf8));
    }

    /// <summary>UTF-8 SVG markup.</summary>
    public byte[] Data { get; }

    /// <summary>Size declared by the document (width/height or viewBox), 100×100 if none.</summary>
    public Size IntrinsicSize { get; }

    /// <summary>Loads SVG markup from a string.</summary>
    public static SvgImage FromString(string svg) => new(Encoding.UTF8.GetBytes(svg));

    /// <summary>Loads an SVG file.</summary>
    public static SvgImage FromFile(string path) => new(File.ReadAllBytes(path));

    /// <summary>Loads an embedded SVG resource.</summary>
    public static SvgImage FromResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found in {assembly.GetName().Name}.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new SvgImage(memory.ToArray());
    }

    private static Size ParseSize(string svg)
    {
        var root = RootRegex().Match(svg);
        if (!root.Success) return new Size(100f, 100f);
        string attributes = root.Groups[1].Value;
        var viewBox = ViewBoxRegex().Match(attributes);
        float w = ParseLength(WidthRegex().Match(attributes)), h = ParseLength(HeightRegex().Match(attributes));
        if ((w <= 0f || h <= 0f) && viewBox.Success)
        {
            var parts = viewBox.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vw)
                && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float vh))
            {
                w = w > 0f ? w : vw;
                h = h > 0f ? h : vh;
            }
        }
        return w > 0f && h > 0f ? new Size(w, h) : new Size(100f, 100f);
    }

    private static float ParseLength(Match match)
    {
        if (!match.Success) return 0f;
        var text = match.Groups[1].Value.Trim();
        if (text.EndsWith('%')) return 0f;
        text = text.Replace("px", "", StringComparison.OrdinalIgnoreCase);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    [GeneratedRegex(@"<svg\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RootRegex();

    [GeneratedRegex(@"\bviewBox\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ViewBoxRegex();

    [GeneratedRegex(@"\bwidth\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex WidthRegex();

    [GeneratedRegex(@"\bheight\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HeightRegex();
}
