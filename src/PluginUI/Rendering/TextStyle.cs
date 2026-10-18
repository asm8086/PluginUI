// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>Font weight (OpenType scale).</summary>
public enum FontWeight
{
    /// <summary>100.</summary>
    Thin = 100,
    /// <summary>300.</summary>
    Light = 300,
    /// <summary>400.</summary>
    Normal = 400,
    /// <summary>500.</summary>
    Medium = 500,
    /// <summary>600.</summary>
    SemiBold = 600,
    /// <summary>700.</summary>
    Bold = 700,
    /// <summary>900.</summary>
    Black = 900,
}

/// <summary>
/// Text appearance and layout options. Instances are immutable; use <c>with</c> to derive variants.
/// </summary>
/// <param name="Family">Font family name (falls back to the system font if missing).</param>
/// <param name="Size">Size in DIPs.</param>
/// <param name="Weight">Weight.</param>
/// <param name="Italic">Italic style.</param>
/// <param name="Align">Horizontal alignment inside the layout rectangle.</param>
/// <param name="VerticalAlign">Vertical alignment inside the layout rectangle.</param>
/// <param name="Wrap">Wraps on word boundaries when <c>true</c>, single line otherwise.</param>
/// <param name="Ellipsis">Trims overflowing text with "…" when <c>true</c>.</param>
public sealed record TextStyle(
    string Family = "Segoe UI",
    float Size = 12f,
    FontWeight Weight = FontWeight.Normal,
    bool Italic = false,
    HorizontalAlignment Align = HorizontalAlignment.Left,
    VerticalAlignment VerticalAlign = VerticalAlignment.Center,
    bool Wrap = false,
    bool Ellipsis = true)
{
    /// <summary>Default UI text.</summary>
    public static TextStyle Default { get; } = new();

    /// <summary>Returns a centered variant.</summary>
    public TextStyle Centered() => this with { Align = HorizontalAlignment.Center };

    /// <summary>Returns a variant with another size.</summary>
    public TextStyle WithSize(float size) => this with { Size = size };

    /// <summary>Returns a bold variant.</summary>
    public TextStyle Bold() => this with { Weight = FontWeight.Bold };
}
