// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Absolute positioning: each child is placed at its <see cref="UIElement.Left"/>/<see cref="UIElement.Top"/>
/// with its explicit or desired size. The usual root of fixed-size plug-in editors (and the live editor's favorite).
/// </summary>
public class Canvas : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float w = 0f, h = 0f;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinite);
            w = MathF.Max(w, child.Left + child.DesiredSize.Width);
            h = MathF.Max(h, child.Top + child.DesiredSize.Height);
        }
        return new Size(w + Padding.Horizontal, h + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        foreach (var child in Children)
        {
            var d = child.DesiredSize;
            child.Arrange(new Rect(inner.X + child.Left, inner.Y + child.Top, d.Width, d.Height));
        }
    }
}

/// <summary>Stacks children vertically or horizontally with optional spacing.</summary>
public class StackPanel : Panel
{
    private Orientation _orientation = Orientation.Vertical;
    private float _spacing;

    /// <summary>Creates a stack panel.</summary>
    public StackPanel() { }

    /// <summary>Creates a stack panel with an orientation and spacing.</summary>
    public StackPanel(Orientation orientation, float spacing = 0f)
    {
        _orientation = orientation;
        _spacing = spacing;
    }

    /// <summary>Stacking direction.</summary>
    [Category("Layout")]
    public Orientation Orientation { get => _orientation; set { _orientation = value; InvalidateMeasure(); } }

    /// <summary>Space between children.</summary>
    [Category("Layout")]
    public float Spacing { get => _spacing; set { _spacing = value; InvalidateMeasure(); } }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        bool vertical = _orientation == Orientation.Vertical;
        var inner = new Size(MathF.Max(0f, available.Width - Padding.Horizontal), MathF.Max(0f, available.Height - Padding.Vertical));
        var slot = vertical ? new Size(inner.Width, float.PositiveInfinity) : new Size(float.PositiveInfinity, inner.Height);
        float main = 0f, cross = 0f;
        int visible = 0;
        foreach (var child in Children)
        {
            child.Measure(slot);
            if (!child.IsVisible) continue;
            visible++;
            main += vertical ? child.DesiredSize.Height : child.DesiredSize.Width;
            cross = MathF.Max(cross, vertical ? child.DesiredSize.Width : child.DesiredSize.Height);
        }
        main += _spacing * Math.Max(0, visible - 1);
        return vertical
            ? new Size(cross + Padding.Horizontal, main + Padding.Vertical)
            : new Size(main + Padding.Horizontal, cross + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        bool vertical = _orientation == Orientation.Vertical;
        float offset = 0f;
        foreach (var child in Children)
        {
            if (!child.IsVisible) { child.Arrange(new Rect(inner.X, inner.Y, 0f, 0f)); continue; }
            if (vertical)
            {
                float h = child.DesiredSize.Height;
                child.Arrange(new Rect(inner.X, inner.Y + offset, inner.Width, h));
                offset += h + _spacing;
            }
            else
            {
                float w = child.DesiredSize.Width;
                child.Arrange(new Rect(inner.X + offset, inner.Y, w, inner.Height));
                offset += w + _spacing;
            }
        }
    }
}

/// <summary>Kind of a <see cref="GridLength"/>.</summary>
public enum GridUnit
{
    /// <summary>Fits the content.</summary>
    Auto,
    /// <summary>Fixed size in DIPs.</summary>
    Pixel,
    /// <summary>Proportional share of the remaining space.</summary>
    Star,
}

/// <summary>Size of a grid row or column: <c>Auto</c>, fixed DIPs, or a star weight.</summary>
public readonly record struct GridLength(float Value, GridUnit Unit)
{
    /// <summary>Auto-sized.</summary>
    public static GridLength Auto => new(0f, GridUnit.Auto);

    /// <summary>One star.</summary>
    public static GridLength OneStar => new(1f, GridUnit.Star);

    /// <summary>Star weight.</summary>
    public static GridLength Star(float weight = 1f) => new(weight, GridUnit.Star);

    /// <summary>Fixed size.</summary>
    public static GridLength Pixels(float value) => new(value, GridUnit.Pixel);

    /// <summary>Fixed size from a number.</summary>
    public static implicit operator GridLength(float pixels) => Pixels(pixels);

    /// <summary>Parses "Auto", "*", "2*", "120".</summary>
    public static GridLength Parse(string text)
    {
        text = text.Trim();
        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase)) return Auto;
        if (text.EndsWith('*'))
        {
            var weight = text[..^1];
            return Star(weight.Length == 0 ? 1f : float.Parse(weight, System.Globalization.CultureInfo.InvariantCulture));
        }
        return Pixels(float.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Rows × columns layout with Auto, fixed and star sizes, spans and spacing. Children use
/// <see cref="UIElement.Row"/>, <see cref="UIElement.Column"/>, <see cref="UIElement.RowSpan"/> and
/// <see cref="UIElement.ColumnSpan"/>.
/// </summary>
/// <example><code>var grid = new Grid("Auto,*", "120,*,120") { ColumnSpacing = 8 };</code></example>
public class Grid : Panel
{
    private float[] _rowSizes = [], _columnSizes = [];

    /// <summary>Creates an empty grid (one star row and column).</summary>
    public Grid() { }

    /// <summary>Creates a grid from comma-separated definitions ("Auto,*,2*,100").</summary>
    public Grid(string rows, string columns)
    {
        foreach (var r in rows.Split(',', StringSplitOptions.RemoveEmptyEntries)) RowDefinitions.Add(GridLength.Parse(r));
        foreach (var c in columns.Split(',', StringSplitOptions.RemoveEmptyEntries)) ColumnDefinitions.Add(GridLength.Parse(c));
    }

    /// <summary>Row sizes (empty = one star row).</summary>
    public List<GridLength> RowDefinitions { get; } = new();

    /// <summary>Column sizes (empty = one star column).</summary>
    public List<GridLength> ColumnDefinitions { get; } = new();

    /// <summary>Space between rows.</summary>
    [Category("Layout")]
    public float RowSpacing { get; set; }

    /// <summary>Space between columns.</summary>
    [Category("Layout")]
    public float ColumnSpacing { get; set; }

    /// <summary>Adds a child at a cell (fluent).</summary>
    public Grid Add(UIElement element, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        element.Row = row;
        element.Column = column;
        element.RowSpan = rowSpan;
        element.ColumnSpan = columnSpan;
        Children.Add(element);
        return this;
    }

    private List<GridLength> Rows => RowDefinitions.Count > 0 ? RowDefinitions : [GridLength.OneStar];
    private List<GridLength> Columns => ColumnDefinitions.Count > 0 ? ColumnDefinitions : [GridLength.OneStar];

    private static float[] Resolve(List<GridLength> defs, float available, float spacing, Func<int, float> autoSize)
    {
        var sizes = new float[defs.Count];
        float used = spacing * Math.Max(0, defs.Count - 1);
        float stars = 0f;
        for (int i = 0; i < defs.Count; i++)
        {
            switch (defs[i].Unit)
            {
                case GridUnit.Pixel: sizes[i] = defs[i].Value; used += sizes[i]; break;
                case GridUnit.Auto: sizes[i] = autoSize(i); used += sizes[i]; break;
                default: stars += MathF.Max(0f, defs[i].Value); break;
            }
        }
        if (stars > 0f)
        {
            float remaining = float.IsFinite(available) ? MathF.Max(0f, available - used) : 0f;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].Unit != GridUnit.Star) continue;
                // Infinite space: stars size to content.
                sizes[i] = float.IsFinite(available) ? remaining * MathF.Max(0f, defs[i].Value) / stars : autoSize(i);
            }
        }
        return sizes;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var rows = Rows;
        var cols = Columns;
        var inner = new Size(MathF.Max(0f, available.Width - Padding.Horizontal), MathF.Max(0f, available.Height - Padding.Vertical));
        foreach (var child in Children) child.Measure(Size.Infinite);

        float AutoColumn(int c)
        {
            float m = 0f;
            foreach (var ch in Children) if (Math.Clamp(ch.Column, 0, cols.Count - 1) == c && ch.ColumnSpan == 1) m = MathF.Max(m, ch.DesiredSize.Width);
            return m;
        }
        float AutoRow(int r)
        {
            float m = 0f;
            foreach (var ch in Children) if (Math.Clamp(ch.Row, 0, rows.Count - 1) == r && ch.RowSpan == 1) m = MathF.Max(m, ch.DesiredSize.Height);
            return m;
        }

        _columnSizes = Resolve(cols, inner.Width, ColumnSpacing, AutoColumn);
        _rowSizes = Resolve(rows, inner.Height, RowSpacing, AutoRow);

        // Second pass: measure children with their real cell size (wrapping text, nested layouts).
        foreach (var child in Children)
        {
            var cell = CellRect(new Rect(0f, 0f, inner.Width, inner.Height), child);
            child.Measure(cell.Size);
        }

        float w = _columnSizes.Sum() + ColumnSpacing * Math.Max(0, cols.Count - 1);
        float h = _rowSizes.Sum() + RowSpacing * Math.Max(0, rows.Count - 1);
        return new Size(w + Padding.Horizontal, h + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        var rows = Rows;
        var cols = Columns;
        // Re-resolve star sizes for the final space (it may differ from the measured one).
        _columnSizes = Resolve(cols, inner.Width, ColumnSpacing, i => i < _columnSizes.Length ? _columnSizes[i] : 0f);
        _rowSizes = Resolve(rows, inner.Height, RowSpacing, i => i < _rowSizes.Length ? _rowSizes[i] : 0f);
        foreach (var child in Children) child.Arrange(CellRect(inner, child));
    }

    private Rect CellRect(Rect inner, UIElement child)
    {
        int c0 = Math.Clamp(child.Column, 0, _columnSizes.Length - 1), r0 = Math.Clamp(child.Row, 0, _rowSizes.Length - 1);
        int c1 = Math.Min(_columnSizes.Length, c0 + child.ColumnSpan), r1 = Math.Min(_rowSizes.Length, r0 + child.RowSpan);
        float x = inner.X, y = inner.Y;
        for (int i = 0; i < c0; i++) x += _columnSizes[i] + ColumnSpacing;
        for (int i = 0; i < r0; i++) y += _rowSizes[i] + RowSpacing;
        float w = 0f, h = 0f;
        for (int i = c0; i < c1; i++) w += _columnSizes[i] + (i > c0 ? ColumnSpacing : 0f);
        for (int i = r0; i < r1; i++) h += _rowSizes[i] + (i > r0 ? RowSpacing : 0f);
        return new Rect(x, y, w, h);
    }

    /// <summary>Column widths after the last layout.</summary>
    public IReadOnlyList<float> ActualColumnWidths => _columnSizes;

    /// <summary>Row heights after the last layout.</summary>
    public IReadOnlyList<float> ActualRowHeights => _rowSizes;
}

/// <summary>Grid with equal cells; children fill it in order.</summary>
public class UniformGrid : Panel
{
    /// <summary>Number of columns (0 = automatic, square-ish).</summary>
    [Category("Behavior")]
    public int Columns { get; set; }

    /// <summary>Number of rows (0 = from the child count).</summary>
    [Category("Layout")]
    public int Rows { get; set; }

    /// <summary>Space between cells.</summary>
    [Category("Layout")]
    public float Spacing { get; set; }

    private (int Rows, int Columns) Shape()
    {
        int n = Math.Max(1, Children.Count(c => c.IsVisible));
        int cols = Columns > 0 ? Columns : Rows > 0 ? (int)Math.Ceiling(n / (double)Rows) : (int)Math.Ceiling(Math.Sqrt(n));
        int rows = Rows > 0 ? Rows : (int)Math.Ceiling(n / (double)cols);
        return (Math.Max(1, rows), Math.Max(1, cols));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var (rows, cols) = Shape();
        float cw = 0f, chh = 0f;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinite);
            cw = MathF.Max(cw, child.DesiredSize.Width);
            chh = MathF.Max(chh, child.DesiredSize.Height);
        }
        return new Size(cw * cols + Spacing * (cols - 1) + Padding.Horizontal, chh * rows + Spacing * (rows - 1) + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        var (rows, cols) = Shape();
        float cw = (inner.Width - Spacing * (cols - 1)) / cols;
        float chh = (inner.Height - Spacing * (rows - 1)) / rows;
        int index = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) { child.Arrange(new Rect(inner.X, inner.Y, 0f, 0f)); continue; }
            int r = index / cols, c = index % cols;
            child.Arrange(new Rect(inner.X + c * (cw + Spacing), inner.Y + r * (chh + Spacing), MathF.Max(0f, cw), MathF.Max(0f, chh)));
            index++;
        }
    }
}

/// <summary>Flows children in lines, wrapping when the line is full.</summary>
public class WrapPanel : Panel
{
    /// <summary>Flow direction.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Space between items on a line.</summary>
    [Category("Layout")]
    public float ItemSpacing { get; set; }

    /// <summary>Space between lines.</summary>
    [Category("Layout")]
    public float LineSpacing { get; set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var inner = new Size(MathF.Max(0f, available.Width - Padding.Horizontal), MathF.Max(0f, available.Height - Padding.Vertical));
        foreach (var child in Children) child.Measure(Size.Infinite);
        var size = Flow(inner, null);
        return new Size(size.Width + Padding.Horizontal, size.Height + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds) => Flow(bounds.Deflate(Padding).Size, bounds.Deflate(Padding).Location);

    private Size Flow(Size inner, Point? origin)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        float limit = horizontal ? inner.Width : inner.Height;
        float main = 0f, cross = 0f, lineCross = 0f, maxMain = 0f;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            float cm = horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
            float cc = horizontal ? child.DesiredSize.Height : child.DesiredSize.Width;
            if (main > 0f && main + cm > limit + 0.01f)
            {
                cross += lineCross + LineSpacing;
                main = 0f;
                lineCross = 0f;
            }
            if (origin is { } o)
            {
                child.Arrange(horizontal
                    ? new Rect(o.X + main, o.Y + cross, cm, cc)
                    : new Rect(o.X + cross, o.Y + main, cc, cm));
            }
            main += cm + ItemSpacing;
            maxMain = MathF.Max(maxMain, main - ItemSpacing);
            lineCross = MathF.Max(lineCross, cc);
        }
        float totalCross = cross + lineCross;
        return horizontal ? new Size(maxMain, totalCross) : new Size(totalCross, maxMain);
    }
}

/// <summary>
/// Docks children to the edges in order (<see cref="UIElement.Dock"/>); the last child fills the remaining space
/// when <see cref="LastChildFill"/> is set.
/// </summary>
public class DockPanel : Panel
{
    /// <summary>The last child fills the remaining space.</summary>
    [Category("Layout")]
    public bool LastChildFill { get; set; } = true;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float usedW = 0f, usedH = 0f, maxW = 0f, maxH = 0f;
        float aw = MathF.Max(0f, available.Width - Padding.Horizontal), ah = MathF.Max(0f, available.Height - Padding.Vertical);
        foreach (var child in Children)
        {
            child.Measure(new Size(MathF.Max(0f, aw - usedW), MathF.Max(0f, ah - usedH)));
            var d = child.DesiredSize;
            if (child.Dock is Side.Left or Side.Right)
            {
                maxH = MathF.Max(maxH, usedH + d.Height);
                usedW += d.Width;
            }
            else
            {
                maxW = MathF.Max(maxW, usedW + d.Width);
                usedH += d.Height;
            }
        }
        return new Size(MathF.Max(maxW, usedW) + Padding.Horizontal, MathF.Max(maxH, usedH) + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var r = bounds.Deflate(Padding);
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var d = child.DesiredSize;
            if (LastChildFill && i == Children.Count - 1)
            {
                child.Arrange(r);
                break;
            }
            switch (child.Dock)
            {
                case Side.Left:
                    child.Arrange(r.TakeLeft(d.Width));
                    r = Rect.FromEdges(r.X + d.Width, r.Y, r.Right, r.Bottom);
                    break;
                case Side.Right:
                    child.Arrange(r.TakeRight(d.Width));
                    r = Rect.FromEdges(r.X, r.Y, r.Right - d.Width, r.Bottom);
                    break;
                case Side.Bottom:
                    child.Arrange(r.TakeBottom(d.Height));
                    r = Rect.FromEdges(r.X, r.Y, r.Right, r.Bottom - d.Height);
                    break;
                default:
                    child.Arrange(r.TakeTop(d.Height));
                    r = Rect.FromEdges(r.X, r.Y + d.Height, r.Right, r.Bottom);
                    break;
            }
        }
    }
}

/// <summary>Decorator: background, border, corner radius, shadow and padding around a single child.</summary>
public class Border : ContentPanel
{
    /// <summary>Creates a border.</summary>
    public Border() { }

    /// <summary>Creates a border around <paramref name="content"/>.</summary>
    public Border(UIElement content) => Content = content;
}

/// <summary>
/// Stack of full-size layers drawn on top of each other (VSTGUI <c>CLayeredViewContainer</c>): backgrounds, a
/// control layer, an overlay page that fades in, a modulation layer… Every child fills the panel (minus its
/// <see cref="UIElement.Margin"/>, with its alignment), the last child is on top, and layer opacity is composited
/// properly (a half-transparent layer does not reveal the overlaps of its own children).
/// </summary>
/// <example>
/// <code>
/// var layers = new LayeredPanel();
/// layers.Add(background, controls, overlay);
/// overlay.Opacity = 0f;                       // hidden page
/// host.Animator.Animate(0, 1, 0.25, v => overlay.Opacity = (float)v);
/// layers.BringToFront(controls);
/// </code>
/// </example>
public class LayeredPanel : Panel
{
    /// <summary>Composites the opacity of each layer through an offscreen layer (default <c>true</c>).</summary>
    [Category("Appearance")]
    public bool CompositeLayers { get; set; } = true;

    /// <summary>Moves a layer to the top (drawn last, first hit).</summary>
    /// <param name="layer">A child of this panel.</param>
    /// <exception cref="ArgumentException"><paramref name="layer"/> is not a child of this panel.</exception>
    public void BringToFront(UIElement layer) => Move(layer, Children.Count - 1);

    /// <summary>Moves a layer to the bottom (drawn first).</summary>
    /// <param name="layer">A child of this panel.</param>
    /// <exception cref="ArgumentException"><paramref name="layer"/> is not a child of this panel.</exception>
    public void SendToBack(UIElement layer) => Move(layer, 0);

    private void Move(UIElement layer, int index)
    {
        int current = Children.IndexOf(layer);
        if (current < 0) throw new ArgumentException("Not a layer of this panel.", nameof(layer));
        if (current == index) return;
        Children.RemoveAt(current);
        Children.Insert(index, layer);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var inner = new Size(MathF.Max(0f, available.Width - Padding.Horizontal), MathF.Max(0f, available.Height - Padding.Vertical));
        float w = 0f, h = 0f;
        foreach (var child in Children)
        {
            child.Measure(inner);
            w = MathF.Max(w, child.DesiredSize.Width);
            h = MathF.Max(h, child.DesiredSize.Height);
        }
        return new Size(w + Padding.Horizontal, h + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var inner = bounds.Deflate(Padding);
        foreach (var child in Children) child.Arrange(inner);
    }
}
