// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

public sealed unsafe partial class UIHost
{
    private UIElement? _hovered;
    private UIElement? _captured;
    private UIElement? _focused;
    private bool _trackingLeave;
    private int _buttonsDown;
    private Point _lastMouse;
    private bool _suppressToolTip;
    private bool _implicitCapture;

    private sealed class DragState(UIElement source, object data, Action<bool>? completed)
    {
        public UIElement Source { get; } = source;
        public object Data { get; } = data;
        public Action<bool>? Completed { get; } = completed;
        public UIElement? Target { get; set; }
    }

    private DragState? _drag;

    /// <summary>Element holding the mouse capture.</summary>
    public UIElement? CapturedElement => _captured;

    /// <summary>Element with keyboard focus.</summary>
    public UIElement? FocusedElement => _focused;

    /// <summary>Deepest element under the mouse.</summary>
    public UIElement? HoveredElement => _hovered;

    /// <summary>Last mouse position in DIPs.</summary>
    public Point MousePosition => _lastMouse;

    /// <summary><c>true</c> during an internal drag-and-drop operation.</summary>
    public bool IsDragging => _drag is not null;

    /// <summary>Raised when the keyboard focus moves.</summary>
    public event Action<UIHost, UIElement?>? FocusChanged;

    /// <summary>Raised before a key is dispatched to the focused element (global shortcuts). Set Handled to stop it.</summary>
    public event Action<UIHost, KeyEventArgs>? PreviewKeyDown;

    /// <summary>Raised when files are dropped where no element accepted them.</summary>
    public event Action<UIHost, FilesDroppedEventArgs>? FilesDropped;

    // =====================================================================================
    // Capture & focus
    // =====================================================================================

    /// <summary>Gives the mouse capture to <paramref name="element"/>.</summary>
    public void CaptureMouse(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (ReferenceEquals(_captured, element)) return;
        var previous = _captured;
        _captured = element;
        _implicitCapture = false;
        previous?.OnCaptureLost();
        if (_hwnd != HWND.NULL && GetCapture() != _hwnd) SetCapture(_hwnd);
    }

    /// <summary>Releases the mouse capture (no <see cref="UIElement.OnCaptureLost"/> callback: voluntary release).</summary>
    public void ReleaseMouseCapture()
    {
        if (_captured is null) return;
        _captured = null;
        _implicitCapture = false;
        if (_hwnd != HWND.NULL && GetCapture() == _hwnd) ReleaseCapture();
        UpdateHover(HitAt(_lastMouse));
    }

    /// <summary>Moves the keyboard focus (null clears it). Returns <c>false</c> if the element cannot take it.</summary>
    public bool Focus(UIElement? element)
    {
        if (element is not null && (!element.Focusable || !ReferenceEquals(element.Host, this) || !element.IsEffectivelyEnabled || !IsEffectivelyVisible(element)))
        {
            return false;
        }
        if (ReferenceEquals(element, _focused)) return true;
        var previous = _focused;
        _focused = element;
        previous?.OnLostFocus();
        element?.OnGotFocus();
        FocusChanged?.Invoke(this, element);
        Invalidate();
        return true;
    }

    /// <summary>Moves the focus to the next (or previous) focusable element (Tab navigation).</summary>
    public bool MoveFocus(bool forward)
    {
        UIElement scope = TopKeyboardPopup() ?? (UIElement)_root;
        var candidates = new List<UIElement>();
        foreach (var e in scope.DescendantsAndSelf())
        {
            if (e.Focusable && e.IsEffectivelyEnabled && IsEffectivelyVisible(e)) candidates.Add(e);
        }
        if (candidates.Count == 0) return false;
        int index = _focused is null ? -1 : candidates.IndexOf(_focused);
        int next = index < 0 ? (forward ? 0 : candidates.Count - 1) : (index + (forward ? 1 : -1) + candidates.Count) % candidates.Count;
        return Focus(candidates[next]);
    }

    private bool IsButtonPhysicallyDown(MouseButton button)
    {
        if (_hwnd == HWND.NULL) return true; // offscreen hosts and tests: trust the dispatched events
        int vk = button switch { MouseButton.Right => 0x02, MouseButton.Middle => 0x04, _ => 0x01 };
        return GetKeyState(vk) < 0;
    }

    private bool AnyButtonPhysicallyDown()
        => _hwnd == HWND.NULL || GetKeyState(0x01) < 0 || GetKeyState(0x02) < 0 || GetKeyState(0x04) < 0;

    private static bool IsEffectivelyVisible(UIElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (!e.IsVisible) return false;
        }
        return true;
    }

    // =====================================================================================
    // Hit testing
    // =====================================================================================

    /// <summary>Deepest element under a point, popups included (null over a modal backdrop).</summary>
    public UIElement? HitAt(Point point)
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var popup = _popups[i];
            if (!popup.IsHitTestVisible) continue;
            if (popup.Bounds.Contains(point)) return popup.HitTestTree(point) ?? popup;
            if (popup.IsModal) return null;
        }
        return _root.HitTestTree(point);
    }

    private static UIElement? Bubble<T>(UIElement? start, T e, Action<UIElement, T> invoke) where T : RoutedEventArgs
    {
        e.Source = start;
        for (var element = start; element is not null; element = element.Parent)
        {
            if (!element.IsEffectivelyEnabled) continue;
            invoke(element, e);
            if (e.Handled) return element;
        }
        return null;
    }

    // =====================================================================================
    // Mouse
    // =====================================================================================

    private void TrackMouseLeave()
    {
        if (_trackingLeave || _hwnd == HWND.NULL) return;
        var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = Win32.TME_LEAVE, hwndTrack = _hwnd };
        _trackingLeave = TrackMouseEvent(&tme) != BOOL.FALSE;
    }

    private void OnNativeMouseMove(Point p, nuint wParam)
    {
        TrackMouseLeave();
        DispatchMouseMove(p, CurrentModifiers());
    }

    private void OnNativeMouseLeave()
    {
        if (_captured is null && _drag is null) UpdateHover(null);
        HideToolTip();
    }

    private void OnNativeMouseDown(Point p, MouseButton button, int clickCount) => DispatchMouseDown(p, button, clickCount, CurrentModifiers());

    private void OnNativeMouseUp(Point p, MouseButton button) => DispatchMouseUp(p, button, CurrentModifiers());

    private void OnNativeCaptureLost()
    {
        _buttonsDown = 0;
        _implicitCapture = false;
        CancelDrag();
        var element = _captured;
        if (element is null) return;
        _captured = null;
        element.OnCaptureLost();
        Invalidate();
    }

    /// <summary>Dispatches a mouse move (DIPs). Public for custom containers and tests.</summary>
    public void DispatchMouseMove(Point p, ModifierKeys modifiers)
    {
        bool moved = p != _lastMouse;
        _lastMouse = p;
        if (_liveEditor is { IsEnabled: true } editor && editor.OnMouseMove(p, modifiers)) return;

        if (_drag is not null) UpdateDrag(p);

        // Safety net: an implicit capture whose button was released outside our knowledge (modal loop).
        if (_implicitCapture && _captured is not null && !AnyButtonPhysicallyDown()) OnNativeCaptureLost();

        var e = new MouseEventArgs(p, MouseButton.None, modifiers, 0);
        if (_captured is not null)
        {
            e.Source = _captured;
            _captured.OnMouseMove(e);
        }
        else
        {
            var hit = HitAt(p);
            Bubble(hit, e, static (el, args) => el.OnMouseMove(args));
            UpdateHover(hit);
        }
        if (moved && _toolTip is not null && _captured is null && !ToolTipOwnerContains(_hovered)) HideToolTip();
    }

    /// <summary>Dispatches a mouse button press (DIPs).</summary>
    public void DispatchMouseDown(Point p, MouseButton button, int clickCount, ModifierKeys modifiers)
    {
        _lastMouse = p;
        HideToolTip();
        _suppressToolTip = true;
        if (TakeFocusOnClick) RequestKeyboardFocus();

        if (_liveEditor is { IsEnabled: true } editor && editor.OnMouseDown(p, button, clickCount, modifiers)) return;

        // Click outside the open popups: light dismiss.
        if (_popups.Count > 0 && !IsInsidePopup(p, out bool blocked))
        {
            if (blocked) return;
        }

        var target = HitAt(p);
        if (target is null) return;

        if (button == MouseButton.Left)
        {
            ShowFocusRings = false;
            UIElement? focusable = target;
            while (focusable is not null && !(focusable.Focusable && focusable.IsEffectivelyEnabled)) focusable = focusable.Parent;
            Focus(focusable);
        }

        var e = new MouseEventArgs(p, button, modifiers, clickCount);
        var handler = Bubble(target, e, static (el, args) => el.OnMouseDown(args));
        // A handler may have run a modal loop (native menu, file dialog) that swallowed the release:
        // then the button is no longer down and no capture must be taken.
        if (!IsButtonPhysicallyDown(button))
        {
            Invalidate();
            return;
        }
        _buttonsDown |= 1 << (int)button;
        // Implicit capture: the element that handled the press receives the moves and the release.
        if (handler is not null && _captured is null && handler.Host == this)
        {
            CaptureMouse(handler);
            _implicitCapture = true;
        }
        Invalidate();
    }

    /// <summary>Dispatches a mouse button release (DIPs).</summary>
    public void DispatchMouseUp(Point p, MouseButton button, ModifierKeys modifiers)
    {
        _lastMouse = p;
        if (_liveEditor is { IsEnabled: true } editor && editor.OnMouseUp(p, button, modifiers)) return;

        if (button == MouseButton.Left && _drag is not null) CompleteDrag(p);

        var e = new MouseEventArgs(p, button, modifiers, 1);
        var hit = HitAt(p);
        var target = _captured ?? hit;
        bool handled;
        if (_captured is not null)
        {
            e.Source = _captured;
            _captured.OnMouseUp(e);
            handled = e.Handled;
        }
        else
        {
            handled = Bubble(target, e, static (el, args) => el.OnMouseUp(args)) is not null;
        }

        _buttonsDown &= ~(1 << (int)button);
        if (_buttonsDown == 0 && _captured is not null) ReleaseMouseCapture();

        if (button == MouseButton.Right && !handled && hit is not null) ShowContextMenuFor(hit, p);
        UpdateHover(HitAt(p));
        Invalidate();
    }

    /// <summary>Dispatches a wheel event (DIPs, notches). Returns <c>true</c> if an element handled it.</summary>
    public bool DispatchWheel(Point p, float notches, bool horizontal, ModifierKeys modifiers)
    {
        HideToolTip();
        var target = _captured ?? HitAt(p);
        var e = new MouseEventArgs(p, MouseButton.None, modifiers, 0, notches, horizontal);
        return Bubble(target, e, static (el, args) => el.OnMouseWheel(args)) is not null;
    }

    /// <summary>Wheel at the last known mouse position (VST3 <c>onWheel</c>).</summary>
    public bool DispatchWheel(float notches) => DispatchWheel(_lastMouse, notches, false, IsAttached ? CurrentModifiers() : ModifierKeys.None);

    private void UpdateHover(UIElement? element)
    {
        if (ReferenceEquals(element, _hovered)) return;
        var oldChain = new List<UIElement>();
        for (var e = _hovered; e is not null; e = e.Parent) oldChain.Add(e);
        var newChain = new List<UIElement>();
        for (var e = element; e is not null; e = e.Parent) newChain.Add(e);
        _hovered = element;

        foreach (var e in oldChain)
        {
            if (newChain.Contains(e)) continue;
            e.IsMouseOver = false;
            e.OnMouseLeave();
        }
        for (int i = newChain.Count - 1; i >= 0; i--)
        {
            var e = newChain[i];
            if (oldChain.Contains(e)) continue;
            e.IsMouseOver = true;
            e.OnMouseEnter();
        }

        _suppressToolTip = false;
        HideToolTip();
        if (FindToolTipElement(element) is not null) StartToolTipTimer();
    }

    private static UIElement? FindToolTipElement(UIElement? element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (!string.IsNullOrEmpty(e.GetToolTipText())) return e;
        }
        return null;
    }

    private StandardCursor CurrentCursor()
    {
        if (_liveEditor is { IsEnabled: true } editor && editor.GetCursor(_lastMouse) is { } c) return c;
        if (_drag is not null) return _drag.Target is not null ? StandardCursor.Hand : StandardCursor.No;
        for (var e = _captured ?? _hovered; e is not null; e = e.Parent)
        {
            if (e.Cursor != StandardCursor.Default) return e.Cursor;
        }
        return StandardCursor.Arrow;
    }

    // =====================================================================================
    // Context menus
    // =====================================================================================

    private void ShowContextMenuFor(UIElement hit, Point p)
    {
        for (var e = hit; e is not null; e = e.Parent)
        {
            if (e.ContextMenu is { Count: > 0 } menu && e.IsEffectivelyEnabled)
            {
                ShowMenu(menu, p);
                return;
            }
        }
        for (var e = hit; e is not null; e = e.Parent)
        {
            if (e is ValueControl { Parameter: { } parameter })
            {
                // The plug-in adapter shows the DAW's menu (with our entries merged); otherwise show our own entries.
                if (ParameterContextMenuRequested?.Invoke(parameter, p) == true) return;
                var items = GetParameterMenuItems(parameter);
                if (items.Count > 0) ShowMenu([.. items], p);
                return;
            }
        }
    }

    // =====================================================================================
    // Keyboard
    // =====================================================================================

    /// <summary>Enables the Ctrl+Shift+E live-edit shortcut.</summary>
    public bool LiveEditShortcutEnabled { get; set; } = true;

    /// <summary>Dispatches a key press. Returns <c>true</c> if handled (unhandled keys go back to the DAW).</summary>
    public bool DispatchKeyDown(KeyEventArgs e)
    {
        HideToolTip();
        PreviewKeyDown?.Invoke(this, e);
        if (e.Handled) return true;

        if (LiveEditShortcutEnabled && e.Key == Key.E && e.Control && e.Shift)
        {
            IsLiveEditEnabled = !IsLiveEditEnabled;
            return true;
        }
        if (_liveEditor is { IsEnabled: true } editor && editor.OnKeyDown(e)) return true;

        var keyboardPopup = TopKeyboardPopup();
        if (keyboardPopup is not null)
        {
            var start = _focused is not null && keyboardPopup.IsAncestorOf(_focused) ? _focused : keyboardPopup;
            Bubble(start, e, static (el, args) => el.OnKeyDown(args));
            if (!e.Handled && e.Key == Key.Tab) { MoveFocus(!e.Shift); ShowFocusRings = true; e.Handled = true; }
            return e.Handled || keyboardPopup.IsModal;
        }

        if (_focused is not null) Bubble(_focused, e, static (el, args) => el.OnKeyDown(args));
        if (e.Handled) return true;

        if (e.Key == Key.Tab && !e.Control && (e.Modifiers & ModifierKeys.Alt) == 0)
        {
            if (MoveFocus(!e.Shift))
            {
                ShowFocusRings = true;
                Invalidate();
                return true;
            }
        }
        return false;
    }

    /// <summary>Dispatches a key release.</summary>
    public bool DispatchKeyUp(KeyEventArgs e)
    {
        var start = TopKeyboardPopup() is { } popup && (_focused is null || !popup.IsAncestorOf(_focused)) ? popup : _focused;
        Bubble(start, e, static (el, args) => el.OnKeyUp(args));
        return e.Handled;
    }

    /// <summary>Dispatches typed text to the focused element.</summary>
    public bool DispatchTextInput(string text)
    {
        var popup = TopKeyboardPopup();
        var start = popup is not null && (_focused is null || !popup.IsAncestorOf(_focused)) ? popup : _focused;
        if (start is null) return false;
        var e = new TextInputEventArgs(text);
        Bubble(start, e, static (el, args) => el.OnTextInput(args));
        return e.Handled;
    }

    // =====================================================================================
    // File drop & internal drag-and-drop
    // =====================================================================================

    private void DispatchFilesDropped(Point p, IReadOnlyList<string> files)
    {
        var e = new FilesDroppedEventArgs(p, files);
        for (var el = HitAt(p); el is not null; el = el.Parent)
        {
            if (!el.AllowFileDrop || !el.IsEffectivelyEnabled) continue;
            e.Source ??= el;
            el.OnFilesDropped(e);
            if (e.Handled) return;
        }
        FilesDropped?.Invoke(this, e);
    }

    /// <summary>
    /// Starts an internal drag-and-drop (call while the mouse button is held, e.g. from <see cref="UIElement.OnMouseMove"/>).
    /// Elements under the pointer receive <see cref="UIElement.OnDragOver"/>, the accepting one <see cref="UIElement.OnDrop"/>.
    /// </summary>
    /// <param name="source">Element starting the drag.</param>
    /// <param name="data">Payload.</param>
    /// <param name="completed">Called with <c>true</c> if dropped on an accepting element.</param>
    public void BeginDrag(UIElement source, object data, Action<bool>? completed = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);
        CancelDrag();
        _drag = new DragState(source, data, completed);
        UpdateDrag(_lastMouse);
    }

    private void UpdateDrag(Point p)
    {
        var drag = _drag!;
        UIElement? accepted = null;
        for (var el = HitAt(p); el is not null; el = el.Parent)
        {
            if (!el.IsEffectivelyEnabled) continue;
            var e = new DragEventArgs(p, drag.Data, drag.Source) { Source = el };
            el.OnDragOver(e);
            if (e.Accept) { accepted = el; break; }
        }
        if (!ReferenceEquals(accepted, drag.Target))
        {
            drag.Target?.OnDragLeave();
            drag.Target = accepted;
            Invalidate();
        }
    }

    private void CompleteDrag(Point p)
    {
        var drag = _drag!;
        UpdateDrag(p);
        _drag = null;
        bool dropped = false;
        if (drag.Target is { } target)
        {
            var e = new DragEventArgs(p, drag.Data, drag.Source) { Source = target, Accept = true };
            target.OnDragLeave();
            target.OnDrop(e);
            dropped = e.Accept;
        }
        drag.Completed?.Invoke(dropped);
        Invalidate();
    }

    private void CancelDrag()
    {
        var drag = _drag;
        if (drag is null) return;
        _drag = null;
        drag.Target?.OnDragLeave();
        drag.Completed?.Invoke(false);
    }

    // =====================================================================================
    // Housekeeping
    // =====================================================================================

    /// <summary>Cancels capture, drag and hover (window destroyed, popups closing...).</summary>
    private void CancelInteraction()
    {
        CancelDrag();
        _buttonsDown = 0;
        _implicitCapture = false;
        var captured = _captured;
        _captured = null;
        if (_hwnd != HWND.NULL && GetCapture() == _hwnd) ReleaseCapture();
        captured?.OnCaptureLost();
        UpdateHover(null);
    }

    /// <summary>Called by <see cref="UIElement"/> when it leaves the host: drops every reference to it.</summary>
    internal void OnElementDetaching(UIElement element)
    {
        if (ReferenceEquals(_captured, element))
        {
            _captured = null;
            _buttonsDown = 0;
            if (_hwnd != HWND.NULL && GetCapture() == _hwnd) ReleaseCapture();
        }
        if (_hovered is not null && element.IsAncestorOf(_hovered))
        {
            for (var e = _hovered; e is not null; e = e.Parent) e.IsMouseOver = false;
            _hovered = null;
        }
        if (ReferenceEquals(_focused, element))
        {
            _focused = null;
            FocusChanged?.Invoke(this, null);
        }
        if (_drag is not null && (ReferenceEquals(_drag.Source, element) || ReferenceEquals(_drag.Target, element)))
        {
            if (ReferenceEquals(_drag.Target, element)) _drag.Target = null;
            if (ReferenceEquals(_drag.Source, element)) CancelDrag();
        }
        if (_valueBubble is not null && ReferenceEquals(_valueBubble.Owner, element)) HideValueBubble(element);
        if (_toolTipOwner is not null && ReferenceEquals(_toolTipOwner, element)) HideToolTip();
        _liveEditor?.OnElementDetaching(element);
    }
}
