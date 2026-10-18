// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// State handed to a skin for one frame.
/// </summary>
/// <param name="Bounds">Widget area (label/value areas excluded).</param>
/// <param name="Value">Normalized value (0 for controls without value).</param>
/// <param name="StateIndex">Discrete state (0/1 for toggles, step index for switches).</param>
/// <param name="IsHovered">Mouse over the control.</param>
/// <param name="IsPressed">Primary button held.</param>
/// <param name="IsEnabled">Control enabled.</param>
/// <param name="IsFocused">Keyboard focus.</param>
public readonly record struct SkinState(Rect Bounds, double Value, int StateIndex, bool IsHovered, bool IsPressed, bool IsEnabled, bool IsFocused);

/// <summary>
/// Draws the widget part of a control instead of its vector renderer. Implement to create custom looks;
/// the same skin instance may be shared by many controls.
/// </summary>
public interface IControlSkin
{
    /// <summary>Draws the control for the given state.</summary>
    void Render(DrawingContext dc, Control control, in SkinState state);
}

/// <summary>How an image is fitted into the widget area.</summary>
public enum ImageFit
{
    /// <summary>Stretched to fill (aspect ratio not preserved).</summary>
    Stretch,
    /// <summary>Scaled to fit entirely, centered (aspect preserved).</summary>
    Contain,
    /// <summary>Natural DIP size, centered.</summary>
    Center,
}

/// <summary>Image placement helpers.</summary>
public static class ImageLayout
{
    /// <summary>Destination rectangle of content of <paramref name="natural"/> size inside <paramref name="area"/>.</summary>
    public static Rect Fit(Size natural, Rect area, ImageFit fit)
    {
        if (natural.IsEmpty) return area;
        switch (fit)
        {
            case ImageFit.Stretch:
                return area;
            case ImageFit.Center:
                return Rect.FromCenter(area.Center, natural.Width, natural.Height);
            default:
                float k = MathF.Min(area.Width / natural.Width, area.Height / natural.Height);
                return Rect.FromCenter(area.Center, natural.Width * k, natural.Height * k);
        }
    }
}

/// <summary>
/// Film-strip skin: the frame is chosen from the normalized value (knobs, sliders, meters, multi-state switches).
/// </summary>
public sealed class FilmStripSkin(FilmStrip strip, ImageFit fit = ImageFit.Contain, bool useStateIndex = false) : IControlSkin
{
    /// <summary>The film-strip.</summary>
    public FilmStrip Strip { get; } = strip;

    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state)
    {
        int frame = useStateIndex ? state.StateIndex : Strip.FrameForValue(state.Value);
        dc.DrawFilmStripFrame(Strip, frame, ImageLayout.Fit(Strip.FrameSize, state.Bounds, fit));
    }
}

/// <summary>
/// One image per state: off/on (toggles), optional hover, pressed and disabled variants.
/// </summary>
public sealed class ImageStateSkin : IControlSkin
{
    /// <summary>Creates a state skin.</summary>
    public ImageStateSkin(ImageSource normal, ImageSource? active = null)
    {
        Normal = normal;
        Active = active;
    }

    /// <summary>Normal / off image.</summary>
    public ImageSource Normal { get; }

    /// <summary>On / active image (StateIndex &gt; 0).</summary>
    public ImageSource? Active { get; }

    /// <summary>Image while hovered (optional).</summary>
    public ImageSource? Hover { get; init; }

    /// <summary>Image while pressed (optional).</summary>
    public ImageSource? Pressed { get; init; }

    /// <summary>Image while disabled (optional).</summary>
    public ImageSource? Disabled { get; init; }

    /// <summary>Fitting mode.</summary>
    public ImageFit Fit { get; init; } = ImageFit.Contain;

    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state)
    {
        var image = !state.IsEnabled && Disabled is not null ? Disabled
            : state.IsPressed && Pressed is not null ? Pressed
            : state.StateIndex > 0 && Active is not null ? Active
            : state.IsHovered && Hover is not null ? Hover
            : Normal;
        dc.DrawImage(image, ImageLayout.Fit(image.Size, state.Bounds, Fit));
    }
}

/// <summary>
/// SVG knob: optional static background, a rotor rotated from <see cref="MinAngle"/> to <see cref="MaxAngle"/>
/// with the value, and an optional static foreground (highlights, glass).
/// </summary>
public sealed class SvgRotarySkin(SvgImage rotor, SvgImage? background = null, SvgImage? foreground = null) : IControlSkin
{
    /// <summary>Angle at value 0 (degrees, clockwise from 12 o'clock).</summary>
    public float MinAngle { get; init; } = -135f;

    /// <summary>Angle at value 1.</summary>
    public float MaxAngle { get; init; } = 135f;

    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state)
    {
        var square = state.Bounds.CenteredSquare();
        if (background is not null) dc.DrawSvg(background, square);
        float angle = MinAngle + (MaxAngle - MinAngle) * (float)state.Value;
        dc.DrawSvg(rotor, square, angle);
        if (foreground is not null) dc.DrawSvg(foreground, square);
    }
}

/// <summary>
/// Bitmap knob made of a single rotor image rotated with the value (iPlug2 <c>IBKnobRotaterControl</c>), with an
/// optional static background (scale, shadow) and foreground (highlights, glass). Lighter than a film-strip: one image
/// instead of 64–128 frames, at the cost of rotating the lighting with the knob.
/// </summary>
/// <remarks>Provide the rotor at 2× (and <c>pixelsPerDip: 2</c>) for sharp HiDPI rendering; it is drawn with smoothing.</remarks>
/// <example>
/// <code>
/// var rotor = ImageSource.FromFile("knob-rotor@2x.png", pixelsPerDip: 2f);
/// var knob = new Knob(parameter) { Skin = new ImageRotarySkin(rotor) { MinAngle = -150f, MaxAngle = 150f } };
/// </code>
/// </example>
public sealed class ImageRotarySkin(ImageSource rotor, ImageSource? background = null, ImageSource? foreground = null) : IControlSkin
{
    /// <summary>Angle at value 0 (degrees, clockwise from 12 o'clock).</summary>
    public float MinAngle { get; init; } = -135f;

    /// <summary>Angle at value 1.</summary>
    public float MaxAngle { get; init; } = 135f;

    /// <summary>How the images are fitted into the widget area (default: contained, aspect preserved).</summary>
    public ImageFit Fit { get; init; } = ImageFit.Contain;

    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state)
    {
        var area = state.Bounds;
        float opacity = state.IsEnabled ? 1f : 0.5f;
        if (background is not null) dc.DrawImage(background, ImageLayout.Fit(background.Size, area, Fit), opacity);
        var dest = ImageLayout.Fit(rotor.Size, area, Fit);
        float angle = MinAngle + (MaxAngle - MinAngle) * (float)Math.Clamp(state.Value, 0.0, 1.0);
        using (dc.Rotate(angle, dest.Center)) dc.DrawImage(rotor, dest, opacity);
        if (foreground is not null) dc.DrawImage(foreground, ImageLayout.Fit(foreground.Size, area, Fit), opacity);
    }
}

/// <summary>
/// One SVG per discrete state (switches, toggles) or per value range (meters): frame = state index,
/// or value × (count − 1) when <c>byValue</c> is true.
/// </summary>
public sealed class SvgStateSkin(IReadOnlyList<SvgImage> frames, bool byValue = false) : IControlSkin
{
    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state)
    {
        if (frames.Count == 0) return;
        int index = byValue ? (int)Math.Round(state.Value * (frames.Count - 1)) : state.StateIndex;
        var svg = frames[Math.Clamp(index, 0, frames.Count - 1)];
        dc.DrawSvg(svg, ImageLayout.Fit(svg.IntrinsicSize, state.Bounds, ImageFit.Contain));
    }
}

/// <summary>
/// Nine-slice background stretched over the widget area, with optional pressed/active variants
/// (scalable bitmap buttons and panels).
/// </summary>
public sealed class NineSliceSkin(NineSlice normal, NineSlice? active = null, NineSlice? pressed = null) : IControlSkin
{
    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state)
    {
        var nine = state.IsPressed && pressed is not null ? pressed : state.StateIndex > 0 && active is not null ? active : normal;
        dc.DrawNineSlice(nine, state.Bounds);
    }
}

/// <summary>
/// Skin from a delegate: the quickest way to give one control a custom look.
/// </summary>
public sealed class DelegateSkin(DelegateSkin.RenderCallback render) : IControlSkin
{
    /// <summary>Render callback signature.</summary>
    public delegate void RenderCallback(DrawingContext dc, Control control, in SkinState state);

    /// <inheritdoc />
    public void Render(DrawingContext dc, Control control, in SkinState state) => render(dc, control, state);
}
