// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

public sealed unsafe partial class UIHost
{
    private const nuint ToolTipTimerId = 2;

    private readonly List<Popup> _popups = new();
    private ToolTipPopup? _toolTip;
    private UIElement? _toolTipOwner;
    private ValueBubblePopup? _valueBubble;

    /// <summary>Open popups, bottom to top.</summary>
    public IReadOnlyList<Popup> Popups => _popups;

    /// <summary>Color drawn over the UI behind a modal popup.</summary>
    public Color ModalBackdrop { get; set; } = new(0f, 0f, 0f, 0.45f);

    // =====================================================================================
    // Popups
    // =====================================================================================

    /// <summary>Shows a popup above the UI (it is laid out at the next frame).</summary>
    public void ShowPopup(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        if (popup.Parent is not null) throw new InvalidOperationException("A popup cannot have a parent element.");
        if (_popups.Contains(popup))
        {
            _popups.Remove(popup);
            _popups.Add(popup);
            Invalidate();
            return;
        }
        if (popup.Host is not null && !ReferenceEquals(popup.Host, this)) popup.Close();
        HideToolTip();
        _popups.Add(popup);
        popup.AttachTo(this);
        InvalidateLayout();
    }

    /// <summary>Closes a popup and every popup opened after it (sub-menus).</summary>
    public void ClosePopup(Popup popup)
    {
        int index = _popups.IndexOf(popup);
        if (index < 0) return;
        // Detach the whole range first: Closed handlers may open or close popups (re-entrancy).
        var closing = _popups.GetRange(index, _popups.Count - index);
        _popups.RemoveRange(index, closing.Count);
        for (int i = closing.Count - 1; i >= 0; i--)
        {
            var p = closing[i];
            p.DetachFromHost();
            try
            {
                p.OnClosed();
            }
            catch (Exception ex)
            {
                ReportException(ex, "Popup.Closed");
            }
        }
        if (_captured is null) UpdateHover(HitAt(_lastMouse));
        InvalidateLayout();
    }

    /// <summary>Closes every popup.</summary>
    public void CloseAllPopups()
    {
        if (_popups.Count > 0) ClosePopup(_popups[0]);
        if (_valueBubble?.Owner is { } owner) HideValueBubble(owner);
    }

    private Popup? TopKeyboardPopup()
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            if (_popups[i].CapturesKeyboard || _popups[i].IsModal) return _popups[i];
        }
        return null;
    }

    /// <summary>
    /// Light dismiss: returns <c>true</c> if the point is inside an open popup; otherwise closes the light-dismiss
    /// popups and reports through <paramref name="blocked"/> whether the click must not reach the UI below.
    /// </summary>
    private bool IsInsidePopup(Point p, out bool blocked)
    {
        blocked = false;
        for (int i = _popups.Count - 1; i >= 0; i = Math.Min(i - 1, _popups.Count - 1))
        {
            var popup = _popups[i];
            if (popup.IsHitTestVisible && popup.Bounds.Contains(p)) return true;
            if (popup.IsLightDismiss)
            {
                if (popup.ConsumesOutsideClick || popup.IsModal) blocked = true;
                ClosePopup(popup);
                continue;
            }
            if (popup.IsModal)
            {
                blocked = true;
                return false;
            }
        }
        return false;
    }

    private void LayoutPopups(Size size)
    {
        foreach (var popup in _popups) popup.Arrange(popup.ComputeRect(size));
        if (_valueBubble is { Owner: { } owner } bubble)
        {
            bubble.Anchor = owner.Bounds;
            bubble.Arrange(bubble.ComputeRect(size));
        }
        if (_toolTip is not null) _toolTip.Arrange(_toolTip.ComputeRect(size));
    }

    private void RenderPopups(DrawingContext dc)
    {
        var size = LogicalSize;
        foreach (var popup in _popups)
        {
            if (popup.IsModal) dc.FillRect(new Rect(0f, 0f, size.Width, size.Height), ModalBackdrop);
            popup.RenderTree(dc);
        }
        _valueBubble?.RenderTree(dc);
        _toolTip?.RenderTree(dc);
    }

    // =====================================================================================
    // Menus
    // =====================================================================================

    /// <summary>Shows a menu at a point (context menus).</summary>
    public void ShowMenu(IList<MenuItem> items, Point at)
        => ShowMenu(items, new Rect(at.X, at.Y, 0f, 0f), PopupPlacement.AtPoint);

    /// <summary>Shows a menu next to an anchor rectangle (drop-downs, menu buttons).</summary>
    /// <param name="items">Entries.</param>
    /// <param name="anchor">Anchor in host DIPs.</param>
    /// <param name="placement">Placement relative to the anchor.</param>
    /// <param name="minWidth">Minimum width (e.g. the drop-down width).</param>
    /// <param name="closed">Called when the menu closes (chosen or dismissed).</param>
    public void ShowMenu(IList<MenuItem> items, Rect anchor, PopupPlacement placement = PopupPlacement.Below, float minWidth = 0f, Action? closed = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return;
        if (UseNativeMenus && IsAttached)
        {
            var at = placement == PopupPlacement.AtPoint ? new Point(anchor.X, anchor.Y) : new Point(anchor.X, anchor.Bottom);
            NativeMenu.Show(this, items, at);
            closed?.Invoke();
            return;
        }
        var menu = new MenuPopup(items) { Anchor = anchor, Placement = placement, MinPopupWidth = minWidth };
        if (closed is not null) menu.Closed += _ => closed();
        ShowPopup(menu);
    }

    // =====================================================================================
    // Value bubble
    // =====================================================================================

    /// <summary>Shows (or updates) the value bubble above <paramref name="owner"/>.</summary>
    public void ShowValueBubble(UIElement owner, string text)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _valueBubble ??= new ValueBubblePopup();
        bool layout = !ReferenceEquals(_valueBubble.Owner, owner) || _valueBubble.Text.Length != text.Length || _valueBubble.Host is null;
        _valueBubble.Owner = owner;
        _valueBubble.Text = text;
        if (_valueBubble.Host is null) _valueBubble.AttachTo(this);
        if (layout) InvalidateLayout(); else Invalidate();
    }

    /// <summary>Hides the value bubble if <paramref name="owner"/> shows it.</summary>
    public void HideValueBubble(UIElement owner)
    {
        if (_valueBubble is null || !ReferenceEquals(_valueBubble.Owner, owner)) return;
        var bubble = _valueBubble;
        _valueBubble = null;
        bubble.Owner = null;
        bubble.DetachFromHost();
        Invalidate();
    }

    // =====================================================================================
    // Tooltips
    // =====================================================================================

    /// <summary>Shows a tooltip immediately at an anchor (custom tooltips; normal tooltips use <see cref="UIElement.ToolTip"/>).</summary>
    public void ShowToolTip(string text, Rect anchor, UIElement? owner = null)
    {
        HideToolTip();
        if (string.IsNullOrEmpty(text)) return;
        _toolTip = new ToolTipPopup(text) { Anchor = anchor };
        _toolTipOwner = owner;
        _toolTip.AttachTo(this);
        InvalidateLayout();
    }

    /// <summary>Hides the tooltip.</summary>
    public void HideToolTip()
    {
        if (_hwnd != HWND.NULL) KillTimer(_hwnd, ToolTipTimerId);
        if (_toolTip is null) return;
        var tip = _toolTip;
        _toolTip = null;
        _toolTipOwner = null;
        tip.DetachFromHost();
        Invalidate();
    }

    private bool ToolTipOwnerContains(UIElement? element) => _toolTipOwner is not null && _toolTipOwner.IsAncestorOf(element);

    private void StartToolTipTimer()
    {
        if (_suppressToolTip || _hwnd == HWND.NULL || _captured is not null) return;
        SetTimer(_hwnd, ToolTipTimerId, (uint)Math.Clamp(ToolTipDelay.TotalMilliseconds, 10, 10000), null);
    }

    private void OnToolTipTimer()
    {
        if (_hwnd != HWND.NULL) KillTimer(_hwnd, ToolTipTimerId);
        if (_captured is not null || _suppressToolTip) return;
        var owner = FindToolTipElement(_hovered);
        if (owner?.GetToolTipText() is { Length: > 0 } text)
        {
            ShowToolTip(text, new Rect(_lastMouse.X + 4f, _lastMouse.Y + 20f, 0f, 0f), owner);
        }
    }
}
