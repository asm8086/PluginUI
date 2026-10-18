// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI.Design;

/// <summary>Kind of asset file.</summary>
public enum DesignAssetKind
{
    /// <summary>Bitmap (PNG, JPEG, BMP…), loaded as an <see cref="ImageSource"/>.</summary>
    Image,

    /// <summary>SVG document, loaded as an <see cref="SvgImage"/>.</summary>
    Svg,
}

/// <summary>
/// Reference to an image or SVG file of a design. The path is relative to the <c>.pluginui</c> file and uses
/// forward slashes ("assets/knob.png"); it becomes a file, an embedded resource or anything else through
/// <see cref="IDesignAssetResolver"/>.
/// </summary>
/// <param name="Path">Relative path.</param>
/// <param name="Kind">Kind of asset.</param>
public sealed record DesignAsset(string Path, DesignAssetKind Kind = DesignAssetKind.Image)
{
    /// <summary>Path with forward slashes and no leading "./".</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = path.Replace('\\', '/').Trim();
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        return p;
    }

    /// <inheritdoc />
    public override string ToString() => Path;
}

/// <summary>Loads the assets of a design.</summary>
public interface IDesignAssetResolver
{
    /// <summary>Loads a bitmap.</summary>
    /// <param name="path">Asset path (forward slashes, relative to the design).</param>
    /// <returns>The image.</returns>
    ImageSource LoadImage(string path);

    /// <summary>Loads an SVG document.</summary>
    /// <param name="path">Asset path (forward slashes, relative to the design).</param>
    /// <returns>The SVG image.</returns>
    SvgImage LoadSvg(string path);
}

/// <summary>Loads assets from files relative to a base directory, with a cache.</summary>
/// <param name="baseDirectory">Folder of the <c>.pluginui</c> file.</param>
public sealed class FileAssetResolver(string baseDirectory) : IDesignAssetResolver
{
    private readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folder the paths are relative to.</summary>
    public string BaseDirectory { get; } = baseDirectory;

    /// <summary>Full path of an asset.</summary>
    /// <param name="path">Asset path.</param>
    /// <returns>The file path.</returns>
    public string FullPath(string path) => System.IO.Path.GetFullPath(System.IO.Path.Combine(BaseDirectory, DesignAsset.Normalize(path)));

    /// <inheritdoc />
    public ImageSource LoadImage(string path)
    {
        if (_cache.TryGetValue("i:" + path, out var cached)) return (ImageSource)cached;
        var image = ImageSource.FromFile(FullPath(path));
        _cache["i:" + path] = image;
        return image;
    }

    /// <inheritdoc />
    public SvgImage LoadSvg(string path)
    {
        if (_cache.TryGetValue("s:" + path, out var cached)) return (SvgImage)cached;
        var svg = SvgImage.FromFile(FullPath(path));
        _cache["s:" + path] = svg;
        return svg;
    }
}

/// <summary>Loads assets embedded in an assembly whose manifest resource names are the asset paths.</summary>
/// <param name="assembly">The assembly.</param>
/// <remarks>Scaffolded projects embed <c>assets\**</c> with <c>LogicalName="assets/%(RecursiveDir)%(Filename)%(Extension)"</c>.</remarks>
public sealed class EmbeddedAssetResolver(System.Reflection.Assembly assembly) : IDesignAssetResolver
{
    /// <inheritdoc />
    public ImageSource LoadImage(string path) => ImageSource.FromResource(assembly, DesignAsset.Normalize(path));

    /// <inheritdoc />
    public SvgImage LoadSvg(string path) => SvgImage.FromResource(assembly, DesignAsset.Normalize(path));
}

/// <summary>Film strip of a design: a bitmap holding <see cref="FrameCount"/> frames.</summary>
public sealed class FilmStripDescriptor
{
    /// <summary>Image path.</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>Number of frames.</summary>
    public int FrameCount { get; set; } = 1;

    /// <summary>Frame layout.</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Creates the film strip.</summary>
    /// <param name="assets">Asset resolver.</param>
    /// <returns>The film strip.</returns>
    public FilmStrip Create(IDesignAssetResolver assets) => new(assets.LoadImage(Image), Math.Max(1, FrameCount), Orientation);

    /// <inheritdoc />
    public override string ToString() => $"{Image} × {FrameCount}";

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FilmStripDescriptor o && o.Image == Image && o.FrameCount == FrameCount && o.Orientation == Orientation;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Image, FrameCount, Orientation);
}

/// <summary>
/// Skin of a control in a design (<see cref="Control.Skin"/>): a description of the image files and options, turned
/// into the real <see cref="IControlSkin"/> when the design is built.
/// </summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public abstract class SkinDescriptor
{
    /// <summary>Kind name written in the file ("FilmStrip", "ImageRotary", "SvgRotary", "ImageState").</summary>
    [Browsable(false)]
    public abstract string Kind { get; }

    /// <summary>Creates the skin.</summary>
    /// <param name="assets">Asset resolver.</param>
    /// <returns>The skin.</returns>
    public abstract IControlSkin Create(IDesignAssetResolver assets);

    /// <summary>Asset paths used by the skin.</summary>
    /// <returns>The paths.</returns>
    public abstract IEnumerable<DesignAsset> Assets();

    /// <summary>Creates an empty descriptor of a kind.</summary>
    /// <param name="kind">Kind name.</param>
    /// <returns>The descriptor, or <c>null</c> for an unknown kind.</returns>
    public static SkinDescriptor? Create(string kind) => kind switch
    {
        "FilmStrip" => new FilmStripSkinDescriptor(),
        "ImageRotary" => new ImageRotarySkinDescriptor(),
        "SvgRotary" => new SvgRotarySkinDescriptor(),
        "ImageState" => new ImageStateSkinDescriptor(),
        _ => null,
    };

    /// <summary>Known kind names.</summary>
    public static IReadOnlyList<string> Kinds { get; } = ["FilmStrip", "ImageRotary", "SvgRotary", "ImageState"];

    /// <inheritdoc />
    public override string ToString() => Kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SkinDescriptor other && other.GetType() == GetType() && DesignValues.ToJsonString(this) == DesignValues.ToJsonString(other);

    /// <inheritdoc />
    public override int GetHashCode() => Kind.GetHashCode(StringComparison.Ordinal);
}

/// <summary>Film-strip skin: one frame per value (or per state).</summary>
public sealed class FilmStripSkinDescriptor : SkinDescriptor
{
    /// <inheritdoc />
    public override string Kind => "FilmStrip";

    /// <summary>Image path.</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>Number of frames.</summary>
    public int FrameCount { get; set; } = 1;

    /// <summary>Frame layout.</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Fit of the frame in the control.</summary>
    public ImageFit Fit { get; set; } = ImageFit.Contain;

    /// <summary>Selects the frame by state index (buttons) instead of value.</summary>
    public bool UseStateIndex { get; set; }

    /// <inheritdoc />
    public override IControlSkin Create(IDesignAssetResolver assets)
        => new FilmStripSkin(new FilmStrip(assets.LoadImage(Image), Math.Max(1, FrameCount), Orientation), Fit, UseStateIndex);

    /// <inheritdoc />
    public override IEnumerable<DesignAsset> Assets() { if (Image.Length > 0) yield return new DesignAsset(Image); }
}

/// <summary>Rotating bitmap skin (knobs).</summary>
public sealed class ImageRotarySkinDescriptor : SkinDescriptor
{
    /// <inheritdoc />
    public override string Kind => "ImageRotary";

    /// <summary>Rotating image path.</summary>
    public string Rotor { get; set; } = string.Empty;

    /// <summary>Fixed background image path (optional).</summary>
    public string? Background { get; set; }

    /// <summary>Fixed foreground image path (optional).</summary>
    public string? Foreground { get; set; }

    /// <summary>Angle at value 0, in degrees.</summary>
    public float MinAngle { get; set; } = -135f;

    /// <summary>Angle at value 1, in degrees.</summary>
    public float MaxAngle { get; set; } = 135f;

    /// <summary>Fit of the images in the control.</summary>
    public ImageFit Fit { get; set; } = ImageFit.Contain;

    /// <inheritdoc />
    public override IControlSkin Create(IDesignAssetResolver assets) => new ImageRotarySkin(
        assets.LoadImage(Rotor),
        string.IsNullOrEmpty(Background) ? null : assets.LoadImage(Background),
        string.IsNullOrEmpty(Foreground) ? null : assets.LoadImage(Foreground))
    { MinAngle = MinAngle, MaxAngle = MaxAngle, Fit = Fit };

    /// <inheritdoc />
    public override IEnumerable<DesignAsset> Assets()
    {
        if (Rotor.Length > 0) yield return new DesignAsset(Rotor);
        if (!string.IsNullOrEmpty(Background)) yield return new DesignAsset(Background);
        if (!string.IsNullOrEmpty(Foreground)) yield return new DesignAsset(Foreground);
    }
}

/// <summary>Rotating SVG skin (knobs).</summary>
public sealed class SvgRotarySkinDescriptor : SkinDescriptor
{
    /// <inheritdoc />
    public override string Kind => "SvgRotary";

    /// <summary>Rotating SVG path.</summary>
    public string Rotor { get; set; } = string.Empty;

    /// <summary>Fixed background SVG path (optional).</summary>
    public string? Background { get; set; }

    /// <summary>Fixed foreground SVG path (optional).</summary>
    public string? Foreground { get; set; }

    /// <summary>Angle at value 0, in degrees.</summary>
    public float MinAngle { get; set; } = -135f;

    /// <summary>Angle at value 1, in degrees.</summary>
    public float MaxAngle { get; set; } = 135f;

    /// <inheritdoc />
    public override IControlSkin Create(IDesignAssetResolver assets) => new SvgRotarySkin(
        assets.LoadSvg(Rotor),
        string.IsNullOrEmpty(Background) ? null : assets.LoadSvg(Background),
        string.IsNullOrEmpty(Foreground) ? null : assets.LoadSvg(Foreground))
    { MinAngle = MinAngle, MaxAngle = MaxAngle };

    /// <inheritdoc />
    public override IEnumerable<DesignAsset> Assets()
    {
        if (Rotor.Length > 0) yield return new DesignAsset(Rotor, DesignAssetKind.Svg);
        if (!string.IsNullOrEmpty(Background)) yield return new DesignAsset(Background, DesignAssetKind.Svg);
        if (!string.IsNullOrEmpty(Foreground)) yield return new DesignAsset(Foreground, DesignAssetKind.Svg);
    }
}

/// <summary>One image per state (buttons, toggles).</summary>
public sealed class ImageStateSkinDescriptor : SkinDescriptor
{
    /// <inheritdoc />
    public override string Kind => "ImageState";

    /// <summary>Normal (off) image path.</summary>
    public string Normal { get; set; } = string.Empty;

    /// <summary>Active (on) image path (optional).</summary>
    public string? Active { get; set; }

    /// <summary>Hover image path (optional).</summary>
    public string? Hover { get; set; }

    /// <summary>Pressed image path (optional).</summary>
    public string? Pressed { get; set; }

    /// <summary>Disabled image path (optional).</summary>
    public string? Disabled { get; set; }

    /// <summary>Fit of the images in the control.</summary>
    public ImageFit Fit { get; set; } = ImageFit.Contain;

    /// <inheritdoc />
    public override IControlSkin Create(IDesignAssetResolver assets)
    {
        ImageSource? Optional(string? path) => string.IsNullOrEmpty(path) ? null : assets.LoadImage(path);
        return new ImageStateSkin(assets.LoadImage(Normal), Optional(Active))
        {
            Hover = Optional(Hover),
            Pressed = Optional(Pressed),
            Disabled = Optional(Disabled),
            Fit = Fit,
        };
    }

    /// <inheritdoc />
    public override IEnumerable<DesignAsset> Assets()
    {
        foreach (var path in (string?[])[Normal, Active, Hover, Pressed, Disabled])
        {
            if (!string.IsNullOrEmpty(path)) yield return new DesignAsset(path);
        }
    }
}
