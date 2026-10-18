// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Where a popup is placed relative to its anchor.</summary>
public enum PopupPlacement
{
    /// <summary>Below the anchor, left-aligned (flips above when there is no room).</summary>
    Below,
    /// <summary>Above the anchor (flips below when there is no room).</summary>
    Above,
    /// <summary>Right of the anchor, top-aligned (sub-menus; flips left).</summary>
    Right,
    /// <summary>Left of the anchor.</summary>
    Left,
    /// <summary>At the anchor's top-left point (context menus).</summary>
    AtPoint,
    /// <summary>Centered in the host (dialogs, splash screens).</summary>
    Center,
}

/// <summary>
/// Element shown above the main tree, inside the host window (menus, drop-down lists, color pickers,
/// tooltips, dialogs). Show it with <see cref="UIHost.ShowPopup"/>.
/// </summary>
public class Popup : ContentPanel
{
    /// <summary>Closes when the user clicks outside it.</summary>
    [Category("Behavior")]
    public bool IsLightDismiss { get; set; } = true;

    /// <summary>Dims the host and blocks input to the rest of the UI.</summary>
    [Category("Behavior")]
    public bool IsModal { get; set; }

    /// <summary>A click outside that closes the popup is not passed to the element below.</summary>
    [Category("Behavior")]
    public bool ConsumesOutsideClick { get; set; } = true;

    /// <summary>Receives keyboard events before the focused element (menus, lists).</summary>
    [Category("Behavior")]
    public bool CapturesKeyboard { get; set; }

    /// <summary>Anchor rectangle in host DIPs.</summary>
    [Category("Layout")]
    public Rect Anchor { get; set; }

    /// <summary>Placement relative to <see cref="Anchor"/>.</summary>
    [Category("Layout")]
    public PopupPlacement Placement { get; set; } = PopupPlacement.Below;

    /// <summary>Centers the popup horizontally on the anchor for <see cref="PopupPlacement.Above"/> / <see cref="PopupPlacement.Below"/>.</summary>
    [Category("Layout")]
    public bool CenterOnAnchor { get; set; }

    /// <summary>Minimum width (e.g. the width of the drop-down that opened it).</summary>
    [Category("Layout")]
    public float MinPopupWidth { get; set; }

    /// <summary><c>true</c> while shown.</summary>
    public bool IsOpen => Host is not null;

    /// <summary>Raised after the popup closed.</summary>
    public event Action<Popup>? Closed;

    /// <summary>Closes the popup.</summary>
    public void Close() => Host?.ClosePopup(this);

    /// <summary>Called after the popup closed (raises <see cref="Closed"/>).</summary>
    protected internal virtual void OnClosed() => Closed?.Invoke(this);

    /// <summary>Keyboard routed to the popup when <see cref="CapturesKeyboard"/> is set; Escape closes by default.</summary>
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>Computes the popup rectangle inside a host of the given logical size.</summary>
    internal Rect ComputeRect(Size host)
    {
        Measure(new Size(host.Width, host.Height));
        float w = MathF.Min(MathF.Max(DesiredSize.Width, MinPopupWidth), host.Width);
        float h = MathF.Min(DesiredSize.Height, host.Height);
        var a = Anchor;
        float x, y;
        switch (Placement)
        {
            case PopupPlacement.Above:
                x = CenterOnAnchor ? a.X + (a.Width - w) * 0.5f : a.X; y = a.Y - h;
                if (y < 0f) y = a.Bottom;
                break;
            case PopupPlacement.Right:
                x = a.Right; y = a.Y;
                if (x + w > host.Width) x = a.X - w;
                break;
            case PopupPlacement.Left:
                x = a.X - w; y = a.Y;
                if (x < 0f) x = a.Right;
                break;
            case PopupPlacement.AtPoint:
                x = a.X; y = a.Y;
                if (x + w > host.Width) x = a.X - w;
                if (y + h > host.Height) y = a.Y - h;
                break;
            case PopupPlacement.Center:
                x = (host.Width - w) * 0.5f; y = (host.Height - h) * 0.5f;
                break;
            default:
                x = CenterOnAnchor ? a.X + (a.Width - w) * 0.5f : a.X; y = a.Bottom;
                if (y + h > host.Height && a.Y - h >= 0f) y = a.Y - h;
                break;
        }
        x = Math.Clamp(x, 0f, MathF.Max(0f, host.Width - w));
        y = Math.Clamp(y, 0f, MathF.Max(0f, host.Height - h));
        return new Rect(x, y, w, h);
    }
}

/// <summary>Tooltip bubble (non interactive).</summary>
internal sealed class ToolTipPopup : Popup
{
    public ToolTipPopup(string text)
    {
        Text = text;
        IsHitTestVisible = false;
        IsLightDismiss = false;
        ConsumesOutsideClick = false;
        Placement = PopupPlacement.AtPoint;
    }

    public string Text { get; set; }

    private TextStyle TextStyle => Theme.Typography.Small with { Wrap = true, Ellipsis = false, VerticalAlign = VerticalAlignment.Top };

    protected override Size MeasureOverride(Size available)
    {
        var size = TextMeasurer.Measure(Text, TextStyle, MathF.Min(280f, available.Width - 12f));
        return new Size(size.Width + 12f, size.Height + 8f);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        dc.DrawShadow(Bounds, 4f, 6f, new Point(0f, 2f), p.Shadow);
        dc.FillRoundedRect(Bounds, 4f, p.SurfaceRaised);
        dc.DrawRoundedRect(Bounds.Inflate(-0.5f), 4f, p.Border);
        dc.DrawText(Text, Bounds.Deflate(new Thickness(6f, 4f)), TextStyle, p.Text);
    }
}

/// <summary>Value bubble shown above a control while it is being edited.</summary>
internal sealed class ValueBubblePopup : Popup
{
    public ValueBubblePopup()
    {
        IsHitTestVisible = false;
        IsLightDismiss = false;
        ConsumesOutsideClick = false;
        Placement = PopupPlacement.Above;
        CenterOnAnchor = true;
    }

    public string Text { get; set; } = string.Empty;

    public UIElement? Owner { get; set; }

    private TextStyle TextStyle => Theme.Typography.Small with { Align = HorizontalAlignment.Center, Weight = FontWeight.SemiBold };

    protected override Size MeasureOverride(Size available)
    {
        var size = TextMeasurer.Measure(Text, TextStyle);
        return new Size(MathF.Max(36f, size.Width + 14f), size.Height + 8f);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds.Inflate(0f, -1f);
        dc.DrawShadow(r, r.Height * 0.5f, 5f, new Point(0f, 2f), p.Shadow);
        dc.FillRoundedRect(r, r.Height * 0.5f, p.Accent);
        dc.DrawText(Text, r, TextStyle, p.Accent.Contrasting);
    }
}
