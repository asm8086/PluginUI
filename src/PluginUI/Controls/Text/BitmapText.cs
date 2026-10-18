// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Monospace bitmap font: an image (atlas) cut into equal cells, one glyph per cell, in character order.
/// Used by <see cref="BitmapText"/> for LCD, VFD, pixel-art or "hardware" displays that a vector font cannot
/// reproduce.
/// </summary>
/// <remarks>
/// <para>The atlas is read row by row: cell 0 is <see cref="FirstCharacter"/>, cell 1 the next character, and so on
/// (<see cref="Characters"/> can list an arbitrary order instead). Glyphs are drawn without smoothing so that pixel
/// fonts stay crisp when scaled by an integer factor.</para>
/// <para>Missing characters are drawn as <see cref="FallbackCharacter"/> (a space by default).</para>
/// </remarks>
/// <example>
/// <code>
/// // 16 × 6 cells of 8 × 12 pixels covering ASCII 32..127:
/// var font = new BitmapFont(ImageSource.FromFile("lcd-font.png"), cellWidth: 8, cellHeight: 12);
/// var display = new BitmapText { Font = font, Text = "CUTOFF 1.2k", PixelScale = 2 };
/// </code>
/// </example>
public sealed class BitmapFont
{
    private readonly Dictionary<char, int> _map;

    /// <summary>Creates a font from an atlas image.</summary>
    /// <param name="atlas">The glyph atlas.</param>
    /// <param name="cellWidth">Width of one glyph cell, in image pixels.</param>
    /// <param name="cellHeight">Height of one glyph cell, in image pixels.</param>
    /// <param name="firstCharacter">Character of the first cell (default: space, ASCII 32).</param>
    /// <param name="characters">Explicit characters of the cells in order, or <c>null</c> to use consecutive characters from <paramref name="firstCharacter"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="atlas"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A cell size is not positive or larger than the atlas.</exception>
    public BitmapFont(ImageSource atlas, int cellWidth, int cellHeight, char firstCharacter = ' ', string? characters = null)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        if (cellWidth <= 0 || cellWidth > atlas.PixelWidth) throw new ArgumentOutOfRangeException(nameof(cellWidth));
        if (cellHeight <= 0 || cellHeight > atlas.PixelHeight) throw new ArgumentOutOfRangeException(nameof(cellHeight));
        Atlas = atlas;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Columns = atlas.PixelWidth / cellWidth;
        int cells = Columns * (atlas.PixelHeight / cellHeight);
        FirstCharacter = firstCharacter;
        Characters = characters;
        _map = new Dictionary<char, int>(cells);
        for (int i = 0; i < cells; i++)
        {
            char c = characters is null ? (char)(firstCharacter + i) : i < characters.Length ? characters[i] : '\0';
            if (c != '\0') _map.TryAdd(c, i);
        }
    }

    /// <summary>The glyph atlas.</summary>
    public ImageSource Atlas { get; }

    /// <summary>Width of a glyph cell in image pixels.</summary>
    public int CellWidth { get; }

    /// <summary>Height of a glyph cell in image pixels.</summary>
    public int CellHeight { get; }

    /// <summary>Number of cells per atlas row.</summary>
    public int Columns { get; }

    /// <summary>Character of the first cell when <see cref="Characters"/> is <c>null</c>.</summary>
    public char FirstCharacter { get; }

    /// <summary>Explicit cell order, or <c>null</c> for consecutive characters.</summary>
    public string? Characters { get; }

    /// <summary>Character drawn for characters missing from the atlas (default: space).</summary>
    public char FallbackCharacter { get; init; } = ' ';

    /// <summary>Size of a glyph in DIPs (cell size divided by the atlas density).</summary>
    public Size GlyphSize => new(CellWidth / Atlas.PixelsPerDip, CellHeight / Atlas.PixelsPerDip);

    /// <summary><c>true</c> if the atlas contains <paramref name="c"/>.</summary>
    /// <param name="c">A character.</param>
    /// <returns><c>true</c> if the character has a glyph.</returns>
    public bool Contains(char c) => _map.ContainsKey(c);

    /// <summary>Source rectangle (atlas pixels) of a character, or <c>null</c> if neither it nor the fallback exists.</summary>
    /// <param name="c">A character.</param>
    /// <returns>The glyph cell in atlas pixels.</returns>
    public Rect? GlyphPixels(char c)
    {
        if (!_map.TryGetValue(c, out int index) && !_map.TryGetValue(FallbackCharacter, out index)) return null;
        return new Rect(index % Columns * CellWidth, index / Columns * CellHeight, CellWidth, CellHeight);
    }
}

/// <summary>
/// Text drawn with a <see cref="BitmapFont"/> (iPlug2 <c>IBTextControl</c>): fixed text or text refreshed every frame
/// (<see cref="TextSource"/>), integer pixel scaling, alignment, character spacing and an optional dim "unlit
/// segments" background that mimics LCD / VFD displays.
/// </summary>
public class BitmapText : Control
{
    private string? _text;
    private Func<string?>? _textSource;

    /// <summary>Creates a bitmap text element.</summary>
    public BitmapText()
    {
        Focusable = false;
    }

    /// <summary>The font (nothing is drawn while it is <c>null</c>).</summary>
    [Browsable(false)]
    public BitmapFont? Font { get; set; }

    /// <summary>Text to display.</summary>
    [Category("Text")]
    public string? Text { get => _text; set { _text = value; InvalidateMeasure(); InvalidateVisual(); } }

    /// <summary>Function polled every frame for live text (meters, counters); overrides <see cref="Text"/> when set.</summary>
    [Browsable(false)]
    public Func<string?>? TextSource
    {
        get => _textSource;
        set
        {
            _textSource = value;
            if (value is not null && Host is not null) Host.AddTicker(_ticker ??= new LiveTicker(this));
            InvalidateVisual();
        }
    }

    /// <summary>Glyph magnification: 0 = fit the height of the element, otherwise an exact factor (use integers for pixel fonts).</summary>
    [Category("Appearance")]
    public float PixelScale { get; set; }

    /// <summary>Extra space between glyphs, in glyph pixels before scaling.</summary>
    [Category("Layout")]
    public float CharacterSpacing { get; set; }

    /// <summary>Horizontal alignment of the text inside the element.</summary>
    [Category("Layout")]
    public HorizontalAlignment TextAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Fixed number of character cells (0 = length of the text); unused cells show the "unlit" glyph.</summary>
    [Category("Layout")]
    public int CellCount { get; set; }

    /// <summary>Character drawn dimmed behind every cell (e.g. '8' or a full block for LCD displays); '\0' = none.</summary>
    [Category("Appearance")]
    public char UnlitCharacter { get; set; }

    /// <summary>Opacity of the unlit characters.</summary>
    [Category("Appearance")]
    public float UnlitOpacity { get; set; } = 0.12f;

    /// <summary>Draws a dark display background behind the text.</summary>
    [Category("Appearance")]
    public bool DrawBackground { get; set; }

    /// <summary>Text currently displayed (<see cref="TextSource"/> result or <see cref="Text"/>).</summary>
    [Browsable(false)]
    public string CurrentText => TextSource?.Invoke() ?? _text ?? string.Empty;

    /// <inheritdoc />
    protected override Size DefaultSize => Font is { } f
        ? new Size(f.GlyphSize.Width * Math.Max(1, CellCount > 0 ? CellCount : (_text?.Length ?? 8)) * Math.Max(1f, PixelScale), f.GlyphSize.Height * Math.Max(1f, PixelScale))
        : new Size(120f, 20f);

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        if (TextSource is not null) Host!.AddTicker(_ticker ??= new LiveTicker(this));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private LiveTicker? _ticker;

    private sealed class LiveTicker(BitmapText owner) : ITickable
    {
        private string? _last;

        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            string text = owner.CurrentText;
            if (!string.Equals(text, _last, StringComparison.Ordinal)) { _last = text; owner.InvalidateVisual(); }
            return true;
        }
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        if (DrawBackground) GraphHelpers.DrawPanel(dc, Theme, widget);
        if (Font is not { } font) return;
        string text = CurrentText;
        int cells = CellCount > 0 ? CellCount : text.Length;
        if (cells == 0) return;
        var glyph = font.GlyphSize;
        float scale = PixelScale > 0f ? PixelScale : widget.Height / MathF.Max(1f, glyph.Height);
        float spacing = CharacterSpacing * scale / font.Atlas.PixelsPerDip;
        float gw = glyph.Width * scale, gh = glyph.Height * scale;
        float total = cells * gw + (cells - 1) * spacing;
        float x = TextAlignment switch
        {
            HorizontalAlignment.Right => widget.Right - total,
            HorizontalAlignment.Center => widget.Center.X - total * 0.5f,
            _ => widget.X,
        };
        float y = widget.Center.Y - gh * 0.5f;
        float opacity = IsEffectivelyEnabled ? 1f : 0.5f;
        dc.PushClip(widget);
        for (int i = 0; i < cells; i++)
        {
            var dest = dc.Snap(new Rect(x + i * (gw + spacing), y, gw, gh));
            if (UnlitCharacter != '\0' && font.GlyphPixels(UnlitCharacter) is { } unlit)
                dc.DrawImage(font.Atlas, dest, UnlitOpacity * opacity, unlit, smooth: false);
            int index = TextAlignment == HorizontalAlignment.Right ? i - (cells - text.Length) : i;
            if (index < 0 || index >= text.Length) continue;
            if (font.GlyphPixels(text[index]) is { } source) dc.DrawImage(font.Atlas, dest, opacity, source, smooth: false);
        }
        dc.PopClip();
    }
}
