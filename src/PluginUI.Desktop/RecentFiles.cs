// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Desktop;

/// <summary>
/// Most recently used files, newest first, with the File / Recent Files sub-menu. <see cref="DesktopApplication"/>
/// saves the list with its state; on its own, read and fill it with <see cref="Items"/> and <see cref="Load"/>.
/// </summary>
public sealed class RecentFiles
{
    private readonly List<string> _items = new();

    /// <summary>Maximum number of files kept (default 10).</summary>
    public int MaxCount { get; set; } = 10;

    /// <summary>Files, newest first (full paths).</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Raised when the list changes.</summary>
    public event Action<RecentFiles>? Changed;

    /// <summary>Puts a file at the top of the list (after opening or saving it).</summary>
    public void Add(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Normalize(path);
        _items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, full);
        Trim();
        Changed?.Invoke(this);
    }

    /// <summary>Removes a file (it no longer exists). Returns <c>true</c> if it was listed.</summary>
    public bool Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full = Normalize(path);
        if (_items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        Changed?.Invoke(this);
        return true;
    }

    /// <summary>Empties the list.</summary>
    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke(this);
    }

    /// <summary>Replaces the list (loading saved settings): duplicates and blank entries are skipped.</summary>
    public void Load(IEnumerable<string>? paths)
    {
        _items.Clear();
        if (paths is not null)
        {
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string full = Normalize(p);
                if (!_items.Contains(full, StringComparer.OrdinalIgnoreCase)) _items.Add(full);
            }
        }
        Trim();
        Changed?.Invoke(this);
    }

    /// <summary>
    /// Rebuilds a sub-menu: "&amp;1 path" entries that call <paramref name="open"/>, then "Clear List". Call it from
    /// <see cref="MenuBar.Opening"/> so that the menu is always current.
    /// </summary>
    /// <param name="menu">The Recent Files entry; its <see cref="MenuItem.Items"/> are replaced.</param>
    /// <param name="open">Opens a file of the list.</param>
    /// <param name="strings">Texts ("Clear List", "(empty)").</param>
    public void FillMenu(MenuItem menu, Action<string> open, UIStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(open);
        var t = strings ?? UIStrings.English;
        menu.Items.Clear();
        if (_items.Count == 0)
        {
            menu.Items.Add(new MenuItem(t.NoRecentFiles) { IsEnabled = false });
            return;
        }
        for (int i = 0; i < _items.Count; i++)
        {
            string path = _items[i];
            string number = i < 9 ? "&" + (i + 1) : i == 9 ? "1&0" : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            menu.Items.Add(new MenuItem(number + " " + Mnemonics.Escape(Compact(path)), () => open(path)) { Tag = path });
        }
        menu.Items.Add(MenuItem.Separator());
        menu.Items.Add(new MenuItem(t.ClearRecentFiles, Clear));
    }

    /// <summary>Shortens a path for a menu: "C:\Users\…\Presets\Warm.bqd" (keeps the root and the file name).</summary>
    /// <param name="path">Full path.</param>
    /// <param name="maxLength">Maximum length in characters (at least 20).</param>
    public static string Compact(string path, int maxLength = 60)
    {
        ArgumentNullException.ThrowIfNull(path);
        maxLength = Math.Max(20, maxLength);
        if (path.Length <= maxLength) return path;
        string name = Path.GetFileName(path);
        string root = Path.GetPathRoot(path) ?? string.Empty;
        if (root.Length + name.Length + 2 > maxLength)
        {
            return name.Length <= maxLength ? name : "…" + name[^(maxLength - 1)..];
        }
        // Keep as many trailing folders as fit: root\…\folder\name.
        string? dir = Path.GetDirectoryName(path);
        string tail = name;
        while (!string.IsNullOrEmpty(dir) && dir.Length > root.Length)
        {
            string folder = Path.GetFileName(dir);
            string candidate = folder + Path.DirectorySeparatorChar + tail;
            if (root.Length + 2 + candidate.Length > maxLength) break;
            tail = candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return root + "…" + Path.DirectorySeparatorChar + tail;
    }

    private void Trim()
    {
        int max = Math.Max(1, MaxCount);
        if (_items.Count > max) _items.RemoveRange(max, _items.Count - max);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
