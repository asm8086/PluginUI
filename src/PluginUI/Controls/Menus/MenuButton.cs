// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Look of a <see cref="MenuButton"/>.</summary>
public enum MenuButtonStyle
{
    /// <summary>Text followed by a chevron.</summary>
    TextWithChevron,
    /// <summary>Three bars.</summary>
    Hamburger,
    /// <summary>Three dots.</summary>
    Ellipsis,
    /// <summary>Text only.</summary>
    Text,
}

/// <summary>
/// Button opening an arbitrary menu (<see cref="MenuItem"/> tree): settings, "more" menus, file menus.
/// Handle <see cref="Opening"/> to rebuild the entries right before the menu appears.
/// </summary>
public class MenuButton : Control
{
    private bool _menuOpen;

    /// <summary>Creates a menu button.</summary>
    public MenuButton() { }

    /// <summary>Creates a menu button with entries.</summary>
    public MenuButton(MenuButtonStyle look, params MenuItem[] items)
    {
        Look = look;
        foreach (var i in items) Items.Add(i);
    }

    /// <summary>Menu entries.</summary>
    [Category("Value")]
    public IList<MenuItem> Items { get; set; } = new List<MenuItem>();

    /// <summary>Visual style.</summary>
    [Category("Appearance")]
    public MenuButtonStyle Look { get; set; } = MenuButtonStyle.Hamburger;

    /// <summary>Caption (text styles).</summary>
    [Category("Text")]
    public string? Text { get; set; }

    /// <summary>Draws the raised box (otherwise flat until hovered).</summary>
    [Category("Appearance")]
    public bool ShowBackground { get; set; } = true;

    /// <summary>Menu placement relative to the button.</summary>
    [Category("Layout")]
    public PopupPlacement MenuPlacement { get; set; } = PopupPlacement.Below;

    /// <summary>Raised before the menu opens (update <see cref="Items"/> here).</summary>
    public event Action<MenuButton>? Opening;

    /// <summary><c>true</c> while the menu is open.</summary>
    public bool IsMenuOpen => _menuOpen;

    private TextStyle TextStyleEffective => Theme.Typography.Normal with { Align = HorizontalAlignment.Left, Weight = FontWeight.Medium };

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            float h = Theme.Metrics.ControlHeight;
            float extra = ControlSizing.LabelAndValueHeight(this);
            if (Look is MenuButtonStyle.Hamburger or MenuButtonStyle.Ellipsis) return new Size(h, h + extra);
            float w = string.IsNullOrEmpty(Text) ? 20f : TextMeasurer.Measure(Text, TextStyleEffective).Width;
            return new Size(w + (Look == MenuButtonStyle.TextWithChevron ? 34f : 20f), h + extra);
        }
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        bool pressed = IsPressed || _menuOpen;
        if (ShowBackground) DrawBox(dc, widget, style, pressed, IsMouseOver);
        else if (pressed || IsMouseOver) dc.FillRoundedRect(widget, CornerRadiusFor(widget, style), pressed ? style.Pressed : style.Fill);
        var fg = !IsEffectivelyEnabled ? p.TextDisabled : _menuOpen ? style.Accent : p.Text;
        float g = MathF.Min(widget.Width, widget.Height) * 0.5f;
        switch (Look)
        {
            case MenuButtonStyle.Hamburger:
                Glyphs.Hamburger(dc, widget.Center, g, fg);
                break;
            case MenuButtonStyle.Ellipsis:
                Glyphs.Ellipsis(dc, widget.Center, g, fg);
                break;
            case MenuButtonStyle.TextWithChevron:
                dc.DrawText(Text, Rect.FromEdges(widget.X + 10f, widget.Y, widget.Right - 22f, widget.Bottom), TextStyleEffective, fg);
                Glyphs.Chevron(dc, new Point(widget.Right - 13f, widget.Center.Y), 8f, fg, up: _menuOpen);
                break;
            default:
                dc.DrawText(Text, widget.Deflate(new Thickness(8f, 0f)), TextStyleEffective with { Align = HorizontalAlignment.Center }, fg);
                break;
        }
    }

    /// <summary>Opens the menu.</summary>
    public void OpenMenu()
    {
        if (Host is null || _menuOpen) return;
        Opening?.Invoke(this);
        if (Items.Count == 0) return;
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        _menuOpen = true;
        InvalidateVisual();
        Host.ShowMenu(Items, widget, MenuPlacement, 0f, () =>
        {
            _menuOpen = false;
            InvalidateVisual();
        });
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        OpenMenu();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (e.Key is Key.Space or Key.Enter or Key.Down)
        {
            OpenMenu();
            e.Handled = true;
        }
    }
}
