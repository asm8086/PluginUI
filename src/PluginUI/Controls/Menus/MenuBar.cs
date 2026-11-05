// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Application menu bar drawn by PluginUI (File, Edit, View…), for desktop applications and standalone tools.
/// Works like the Windows menu bar: click or Alt+letter opens a menu, F10 or a lone Alt press enters the keyboard
/// mode (Left / Right / Down / Enter / Esc), hovering moves between open menus, and the shortcuts of every entry
/// (<see cref="Command.Gesture"/> or a parsable <see cref="MenuItem.ShortcutText"/>) run from anywhere in the window.
/// </summary>
/// <remarks>
/// <para>Top-level entries are the <see cref="Items"/>; their <see cref="MenuItem.Items"/> are the menus. Write a
/// mnemonic with '&amp;' ("&amp;File", "&amp;Edition"); without one, the first letter is used. Rebuild dynamic menus
/// (recent files) in <see cref="Opening"/>.</para>
/// <para>A top-level entry without sub-entries behaves as a button (a "Help" entry that opens a page, for instance).</para>
/// <para>In a plug-in editor the DAW owns the Alt and F10 keys: keep <see cref="HandlesAltKey"/> off there.</para>
/// </remarks>
/// <example>
/// <code>
/// var file = new MenuItem("&amp;File");
/// file.Items.Add(new MenuItem(openCommand));
/// file.Items.Add(new MenuItem(saveCommand));
/// var bar = new MenuBar(file, edit, view);
/// root.Dock(bar, Dock.Top);
/// </code>
/// </example>
public class MenuBar : UIElement
{
    private const float ItemPadding = 10f;
    private const long ReopenGuardMs = 150;

    private UIHost? _subscribed;
    private MenuPopup? _popup;
    private int _open = -1;
    private int _hot = -1;
    private int _keyboardIndex = -1;
    private bool _menuMode;
    private bool _showMnemonics;
    private bool _altPending;
    private int _altPressCount;
    private bool _switching;
    private int _closedIndex = -1;
    private long _closedAt;

    /// <summary>Creates an empty menu bar.</summary>
    public MenuBar() { }

    /// <summary>Creates a menu bar with top-level entries.</summary>
    public MenuBar(params MenuItem[] items)
    {
        foreach (var item in items) Items.Add(item);
    }

    /// <summary>Top-level entries.</summary>
    [Category("Value")]
    public IList<MenuItem> Items { get; } = new List<MenuItem>();

    /// <summary>Runs the shortcuts of the entries of every menu, wherever the keyboard focus is (default <c>true</c>).</summary>
    [Category("Behavior")]
    public bool HandlesShortcuts { get; set; } = true;

    /// <summary>Alt+letter, a lone Alt and F10 activate the bar (default <c>true</c>; turn off in plug-in editors).</summary>
    [Category("Behavior")]
    public bool HandlesAltKey { get; set; } = true;

    /// <summary>Raised before a menu opens (argument: its top-level entry), to rebuild its entries.</summary>
    public event Action<MenuBar, MenuItem>? Opening;

    /// <summary>Index of the open menu (-1 = none).</summary>
    public int OpenIndex => _open;

    /// <summary><c>true</c> in keyboard mode (F10 or Alt) or while a menu is open.</summary>
    public bool IsActive => _menuMode || _open >= 0;

    private TextStyle ItemStyle => Theme.Typography.Normal with { Ellipsis = false };

    // ---------------------------------------------------------------- layout & rendering

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float w = 4f;
        foreach (var item in Items) w += ItemWidth(item);
        float h = MathF.Round(ItemStyle.Size + 14f);
        return new Size(float.IsFinite(available.Width) ? MathF.Max(w, available.Width) : w, h);
    }

    private float ItemWidth(MenuItem item) => MathF.Ceiling(TextMeasurer.Measure(item.DisplayText, ItemStyle).Width) + ItemPadding * 2f;

    /// <summary>Bounds of top-level entry <paramref name="index"/> (host DIPs).</summary>
    public Rect ItemRect(int index)
    {
        float x = Bounds.X + 4f;
        for (int i = 0; i < index; i++) x += ItemWidth(Items[i]);
        return new Rect(x, Bounds.Y + 2f, index < Items.Count ? ItemWidth(Items[index]) : 0f, Bounds.Height - 4f);
    }

    private int IndexAt(Point p)
    {
        if (!Bounds.Contains(p)) return -1;
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemRect(i).Contains(p)) return i;
        }
        return -1;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds;
        dc.FillRect(r, p.Surface);
        float y = MathF.Round(r.Bottom) - 0.5f;
        dc.DrawLine(new Point(r.X, y), new Point(r.Right, y), p.Border);
        var style = ItemStyle with { Align = HorizontalAlignment.Center };
        float radius = MathF.Max(0f, Theme.Metrics.CornerRadius - 1f);
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var ir = ItemRect(i);
            bool enabled = item.IsEnabled || item.HasSubmenu;
            if (i == _open) dc.FillRoundedRect(ir, radius, p.Selection);
            else if (i == _keyboardIndex && _menuMode) dc.FillRoundedRect(ir, radius, p.Selection.WithAlpha(0.6f));
            else if (i == _hot && enabled) dc.FillRoundedRect(ir, radius, p.SurfaceRaised);
            var color = enabled ? p.Text : p.TextDisabled;
            string text = Mnemonics.Parse(item.Text, out int mnemonic);
            dc.DrawText(text, ir, style, color);
            if (_showMnemonics && mnemonic >= 0) MenuPopup.DrawMnemonic(dc, TextMeasurer, text, mnemonic, ir, style, color);
        }
    }

    // ---------------------------------------------------------------- attachment

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        if (Host is not { } host) return;
        _subscribed = host;
        host.PreviewKeyDown += OnPreviewKeyDown;
        host.PreviewKeyUp += OnPreviewKeyUp;
        host.KeyboardFocusChanged += OnKeyboardFocusChanged;
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        CloseMenu();
        ExitMenuMode();
        if (_subscribed is { } host)
        {
            host.PreviewKeyDown -= OnPreviewKeyDown;
            host.PreviewKeyUp -= OnPreviewKeyUp;
            host.KeyboardFocusChanged -= OnKeyboardFocusChanged;
            _subscribed = null;
        }
        base.OnDetached();
    }

    private void OnKeyboardFocusChanged(UIHost host, bool focused)
    {
        if (focused) return;
        _altPending = false;
        CloseMenu();
        ExitMenuMode();
    }

    // ---------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(UIHost host, KeyEventArgs e)
    {
        if (e.Handled || !IsEffectivelyEnabled || !IsVisible) return;

        if (e.Key == Key.Alt)
        {
            if (!HandlesAltKey) return;
            if (!e.IsRepeat)
            {
                _altPending = true;
                _altPressCount = host.MousePressCount;
                SetShowMnemonics(true);
            }
            e.Handled = true;
            return;
        }
        _altPending = false;

        if (HandlesAltKey && e.Key == Key.F10 && e.Modifiers == ModifierKeys.None)
        {
            if (IsActive)
            {
                CloseMenu();
                ExitMenuMode();
            }
            else EnterMenuMode();
            e.Handled = true;
            return;
        }

        if (HandlesAltKey && e.Modifiers == ModifierKeys.Alt && _open < 0 && IsMnemonicKey(e.Key))
        {
            int index = IndexOfMnemonic((char)e.Key);
            if (index >= 0)
            {
                OpenMenu(index, fromKeyboard: true);
                e.Handled = true;
            }
            return;
        }

        if (_menuMode && _open < 0)
        {
            HandleMenuModeKey(e);
            if (e.Handled) return;
        }

        if (HandlesShortcuts && _open < 0 && !e.IsRepeat && TryRunShortcut(Items, e)) e.Handled = true;
    }

    private void OnPreviewKeyUp(UIHost host, KeyEventArgs e)
    {
        if (e.Key != Key.Alt || !HandlesAltKey) return;
        bool lone = _altPending && host.MousePressCount == _altPressCount;
        _altPending = false;
        e.Handled = true;
        if (!lone)
        {
            if (!IsActive) SetShowMnemonics(false);
            return;
        }
        if (IsActive)
        {
            CloseMenu();
            ExitMenuMode();
        }
        else EnterMenuMode();
    }

    private void HandleMenuModeKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _keyboardIndex = NextTop(_keyboardIndex, -1);
                InvalidateVisual();
                e.Handled = true;
                return;
            case Key.Right:
                _keyboardIndex = NextTop(_keyboardIndex, +1);
                InvalidateVisual();
                e.Handled = true;
                return;
            case Key.Down or Key.Up or Key.Enter or Key.Space:
                if (_keyboardIndex >= 0) OpenMenu(_keyboardIndex, fromKeyboard: true);
                e.Handled = true;
                return;
            case Key.Escape:
                ExitMenuMode();
                e.Handled = true;
                return;
        }
        if (IsMnemonicKey(e.Key))
        {
            int index = IndexOfMnemonic((char)e.Key);
            if (index >= 0) OpenMenu(index, fromKeyboard: true);
            e.Handled = true;
            return;
        }
        // Any other key leaves the keyboard mode and goes on to the window.
        ExitMenuMode();
    }

    private static bool IsMnemonicKey(Key key) => key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9;

    private int IndexOfMnemonic(char c)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Mnemonic == c) return i;
        }
        for (int i = 0; i < Items.Count; i++)
        {
            string plain = Items[i].DisplayText;
            if (Items[i].Mnemonic == '\0' && plain.Length > 0 && char.ToUpperInvariant(plain[0]) == c) return i;
        }
        return -1;
    }

    private int NextTop(int from, int direction)
    {
        int n = Items.Count;
        if (n == 0) return -1;
        for (int k = 1; k <= n; k++)
        {
            int i = ((from + direction * k) % n + n) % n;
            if (Items[i].HasSubmenu || Items[i].IsEnabled) return i;
        }
        return -1;
    }

    /// <summary>Runs the first enabled entry of <paramref name="items"/> (sub-menus included) whose shortcut is the key press.</summary>
    /// <returns><c>true</c> if an entry ran.</returns>
    public static bool TryRunShortcut(IEnumerable<MenuItem> items, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(e);
        foreach (var item in items)
        {
            if (item.IsSeparator) continue;
            if (item.HasSubmenu)
            {
                if (TryRunShortcut(item.Items, e)) return true;
                continue;
            }
            bool match = item.Command is { } command
                ? command.Matches(e)
                : KeyGesture.TryParse(item.ShortcutText, out var parsed) && parsed.Matches(e);
            if (match && item.IsEnabled) return item.Invoke();
        }
        return false;
    }

    private void EnterMenuMode()
    {
        if (Items.Count == 0) return;
        _menuMode = true;
        _keyboardIndex = NextTop(-1, +1);
        SetShowMnemonics(true);
        InvalidateVisual();
    }

    private void ExitMenuMode()
    {
        _menuMode = false;
        _keyboardIndex = -1;
        SetShowMnemonics(false);
        InvalidateVisual();
    }

    private void SetShowMnemonics(bool show)
    {
        if (_showMnemonics == show) return;
        _showMnemonics = show;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Position);
        if (index != _hot)
        {
            _hot = index;
            InvalidateVisual();
        }
        // While a menu is open, hovering another title switches menus.
        if (_open >= 0 && index >= 0 && index != _open && Items[index].HasSubmenu) OpenMenu(index, fromKeyboard: _showMnemonics);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hot = -1;
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButton.Left) return;
        e.Handled = true;
        int index = IndexAt(e.Position);
        if (index < 0)
        {
            CloseMenu();
            ExitMenuMode();
            return;
        }
        // The press that just closed this menu (outside click) must not reopen it.
        if (index == _closedIndex && Environment.TickCount64 - _closedAt < ReopenGuardMs)
        {
            _closedIndex = -1;
            ExitMenuMode();
            return;
        }
        if (index == _open) CloseMenu();
        else OpenMenu(index, fromKeyboard: false);
    }

    // ---------------------------------------------------------------- menus

    /// <summary>Opens the menu of top-level entry <paramref name="index"/> (runs it if it has no sub-entries).</summary>
    /// <param name="index">Top-level entry.</param>
    /// <param name="fromKeyboard">Highlights the first entry and underlines the mnemonics.</param>
    public void OpenMenu(int index, bool fromKeyboard = false)
    {
        if (Host is not { } host || index < 0 || index >= Items.Count) return;
        var item = Items[index];
        _switching = true;
        try
        {
            CloseMenu();
        }
        finally
        {
            _switching = false;
        }
        Opening?.Invoke(this, item);
        if (!item.HasSubmenu)
        {
            ExitMenuMode();
            item.Invoke();
            return;
        }
        var popup = new MenuPopup(item.Items)
        {
            Anchor = ItemRect(index),
            Placement = PopupPlacement.Below,
            ShowMnemonics = fromKeyboard,
            ConsumesOutsideClick = false,
        };
        popup.NavigateSibling += (_, direction) =>
        {
            int next = NextTop(index, direction);
            if (next >= 0 && next != index) OpenMenu(next, fromKeyboard: true);
        };
        popup.Closed += _ => OnPopupClosed(popup, index);
        _popup = popup;
        _open = index;
        _keyboardIndex = index;
        if (fromKeyboard) SetShowMnemonics(true);
        host.ShowPopup(popup);
        if (fromKeyboard)
        {
            for (int i = 0; i < item.Items.Count; i++)
            {
                if (!item.Items[i].IsSeparator && item.Items[i].IsEnabled)
                {
                    popup.HighlightedIndex = i;
                    break;
                }
            }
        }
        InvalidateVisual();
    }

    /// <summary>Closes the open menu, if any.</summary>
    public void CloseMenu()
    {
        var popup = _popup;
        if (popup is null) return;
        popup.Close();
    }

    private void OnPopupClosed(MenuPopup popup, int index)
    {
        if (!ReferenceEquals(_popup, popup)) return;
        _popup = null;
        _open = -1;
        _closedIndex = index;
        _closedAt = Environment.TickCount64;
        if (!_switching)
        {
            // Esc goes back to the keyboard mode on the same title, like Windows; any other way out leaves it.
            if (popup.ClosedByEscape && _showMnemonics)
            {
                _menuMode = true;
                _keyboardIndex = index;
            }
            else ExitMenuMode();
        }
        InvalidateVisual();
    }
}
