// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Titled section: a rounded frame with a header strip holding the title, an optional collapse chevron and
/// a single content element.
/// </summary>
/// <remarks>Collapsing hides the content (its <see cref="UIElement.IsVisible"/> is restored on expand).</remarks>
public class GroupBox : ContentPanel
{
    private string? _header;
    private bool _collapsed;
    private bool _contentWasVisible = true;
    private bool _headerHot;

    /// <summary>Creates a group box.</summary>
    public GroupBox()
    {
        Padding = new Thickness(10f, 8f, 10f, 10f);
        CornerRadius = 6f;
    }

    /// <summary>Creates a titled group box.</summary>
    public GroupBox(string header, UIElement? content = null) : this()
    {
        _header = header;
        Content = content;
    }

    /// <summary>Title.</summary>
    [Category("Text")]
    public string? Header { get => _header; set { _header = value; InvalidateVisual(); } }

    /// <summary>Height of the header strip.</summary>
    [Category("Layout")]
    public float HeaderHeight { get; set; } = 26f;

    /// <summary>Shows a chevron and lets a click on the header collapse the content.</summary>
    [Category("Behavior")]
    public bool IsCollapsible { get; set; }

    /// <summary>Draws the header strip with a tinted background (otherwise only a rule under the title).</summary>
    [Category("Appearance")]
    public bool ShowHeaderStrip { get; set; } = true;

    /// <summary>Accent bar color at the left of the title (transparent = none).</summary>
    [Category("Appearance")]
    public Color HeaderAccent { get; set; } = Color.Transparent;

    /// <summary>Collapsed state.</summary>
    [Category("Behavior")]
    public bool IsCollapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            if (Content is { } content)
            {
                if (value)
                {
                    _contentWasVisible = content.IsVisible;
                    content.IsVisible = false;
                }
                else
                {
                    content.IsVisible = _contentWasVisible;
                }
            }
            CollapsedChanged?.Invoke(this, value);
            InvalidateMeasure();
        }
    }

    /// <summary>Raised when the group collapses or expands.</summary>
    public event Action<GroupBox, bool>? CollapsedChanged;

    private Rect HeaderRect => Bounds.TakeTop(HeaderHeight);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        var inner = new Size(MathF.Max(0f, available.Width - Padding.Horizontal), MathF.Max(0f, available.Height - Padding.Vertical - HeaderHeight));
        float w = 0f, h = 0f;
        foreach (var child in Children)
        {
            child.Measure(inner);
            w = MathF.Max(w, child.DesiredSize.Width);
            h = MathF.Max(h, child.DesiredSize.Height);
        }
        float title = string.IsNullOrEmpty(_header) ? 0f : TextMeasurer.Measure(_header, HeaderStyle).Width + 24f + (IsCollapsible ? 18f : 0f);
        if (_collapsed) return new Size(MathF.Max(w + Padding.Horizontal, title), HeaderHeight);
        return new Size(MathF.Max(w + Padding.Horizontal, title), h + Padding.Vertical + HeaderHeight);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var inner = Rect.FromEdges(bounds.X, bounds.Y + HeaderHeight, bounds.Right, bounds.Bottom).Deflate(Padding);
        foreach (var child in Children) child.Arrange(inner);
    }

    private TextStyle HeaderStyle => Theme.Typography.Small with { Weight = FontWeight.SemiBold };

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds;
        float radius = CornerRadius;
        if (!ShadowColor.IsTransparent) dc.DrawShadow(r, radius, ShadowBlur, ShadowOffset, ShadowColor);
        dc.FillRoundedRect(r, radius, Background ?? (Paint)p.Surface);

        var header = HeaderRect;
        if (ShowHeaderStrip)
        {
            using (dc.Clip(header))
            {
                dc.FillRoundedRect(_collapsed ? r : Rect.FromEdges(r.X, r.Y, r.Right, r.Y + HeaderHeight + radius), radius, p.SurfaceRaised);
                if (_headerHot && IsCollapsible && Host?.HoveredElement == this) dc.FillRect(header, p.Hover);
            }
        }
        if (!_collapsed)
        {
            float y = header.Bottom - 0.5f / dc.Scale;
            dc.DrawLine(new Point(r.X, y), new Point(r.Right, y), p.Border, 1f / dc.Scale);
        }
        float x = r.X + 10f;
        if (!HeaderAccent.IsTransparent)
        {
            dc.FillRoundedRect(new Rect(x, header.Center.Y - 6f, 3f, 12f), 1.5f, HeaderAccent);
            x += 9f;
        }
        if (IsCollapsible)
        {
            var c = new Point(r.Right - 14f, header.Center.Y);
            ReadOnlySpan<Point> chevron = _collapsed
                ? [new(c.X - 2f, c.Y - 4f), new(c.X + 2f, c.Y), new(c.X - 2f, c.Y + 4f)]
                : [new(c.X - 4f, c.Y - 2f), new(c.X, c.Y + 2f), new(c.X + 4f, c.Y - 2f)];
            dc.DrawPolyline(chevron, p.TextDim, 1.5f, StrokeStyle.Round);
        }
        dc.DrawText(_header, Rect.FromEdges(x, header.Y, r.Right - (IsCollapsible ? 26f : 10f), header.Bottom), HeaderStyle, p.TextDim);
        var border = BorderColor.IsTransparent ? p.Border : BorderColor;
        dc.DrawRoundedRect(r.Inflate(-0.5f), radius, border, 1f);
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hot = IsCollapsible && HeaderRect.Contains(e.Position);
        if (hot != _headerHot)
        {
            _headerHot = hot;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _headerHot = false;
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsCollapsible || e.Button != MouseButton.Left || !HeaderRect.Contains(e.Position)) return;
        IsCollapsed = !IsCollapsed;
        e.Handled = true;
    }
}
