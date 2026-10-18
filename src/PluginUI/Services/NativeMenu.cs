// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>Shows <see cref="MenuItem"/> trees with the native Win32 popup menu (TrackPopupMenuEx).</summary>
public static unsafe class NativeMenu
{
    /// <summary>
    /// Shows a native menu at a point of the host (DIPs) and runs the chosen entry's action.
    /// Returns the chosen item, or null if dismissed.
    /// </summary>
    public static MenuItem? Show(UIHost host, IList<MenuItem> items, Point at)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(items);
        if (!host.IsAttached || items.Count == 0) return null;

        var map = new List<MenuItem>();
        HMENU menu = Build(items, map);
        try
        {
            var (x, y) = host.DipToScreen(at);
            int command = TrackPopupMenuEx(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_NONOTIFY, x, y, (HWND)host.WindowHandle, null);
            if (command <= 0 || command > map.Count) return null;
            var chosen = map[command - 1];
            chosen.Action?.Invoke();
            return chosen;
        }
        finally
        {
            DestroyMenu(menu); // destroys sub-menus too
        }
    }

    private static HMENU Build(IList<MenuItem> items, List<MenuItem> map)
    {
        HMENU menu = CreatePopupMenu();
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
                continue;
            }
            uint flags = Win32.MF_STRING;
            if (!item.IsEnabled) flags |= Win32.MF_GRAYED;
            if (item.IsChecked) flags |= Win32.MF_CHECKED;
            string text = item.ShortcutText is { Length: > 0 } shortcut ? item.Text + "\t" + shortcut : item.Text;
            fixed (char* p = text)
            {
                if (item.HasSubmenu)
                {
                    HMENU sub = Build(item.Items, map);
                    AppendMenuW(menu, flags | Win32.MF_POPUP, (nuint)sub.Value, p);
                }
                else
                {
                    map.Add(item);
                    AppendMenuW(menu, flags, (nuint)map.Count, p);
                }
            }
        }
        return menu;
    }
}
