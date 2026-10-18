// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Row selection shared by <see cref="ListBox"/> and <see cref="DataTable"/>: single or extended selection with a
/// current (keyboard) index and a Shift anchor. Indices are view indices.
/// </summary>
internal sealed class SelectionModel
{
    private readonly SortedSet<int> _selected = new();
    private int _anchor = -1;

    [Category("Behavior")]
    public bool MultiSelect { get; set; }

    /// <summary>Keyboard cursor (-1 = none).</summary>
    [Browsable(false)]
    public int Current { get; private set; } = -1;

    public IReadOnlyCollection<int> Selected => _selected;

    public int First => _selected.Count > 0 ? _selected.Min : -1;

    public bool IsSelected(int index) => _selected.Contains(index);

    /// <summary>Mouse click. Returns <c>true</c> if the selection changed.</summary>
    public bool Click(int index, bool ctrl, bool shift)
    {
        var before = Snapshot();
        if (MultiSelect && shift && _anchor >= 0)
        {
            if (!ctrl) _selected.Clear();
            for (int i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) _selected.Add(i);
        }
        else if (MultiSelect && ctrl)
        {
            if (!_selected.Remove(index)) _selected.Add(index);
            _anchor = index;
        }
        else
        {
            _selected.Clear();
            _selected.Add(index);
            _anchor = index;
        }
        Current = index;
        return Changed(before);
    }

    /// <summary>Keyboard move. Returns <c>true</c> if the selection changed.</summary>
    public bool MoveTo(int index, bool extend)
    {
        if (index < 0) return false;
        var before = Snapshot();
        if (MultiSelect && extend && _anchor >= 0)
        {
            _selected.Clear();
            for (int i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) _selected.Add(i);
        }
        else
        {
            _selected.Clear();
            _selected.Add(index);
            _anchor = index;
        }
        Current = index;
        return Changed(before);
    }

    public bool Toggle(int index)
    {
        if (index < 0) return false;
        if (!MultiSelect) return MoveTo(index, false);
        if (!_selected.Remove(index)) _selected.Add(index);
        Current = _anchor = index;
        return true;
    }

    public bool SelectAll(int count)
    {
        if (!MultiSelect || count == 0) return false;
        var before = Snapshot();
        for (int i = 0; i < count; i++) _selected.Add(i);
        return Changed(before);
    }

    public bool Set(int index)
    {
        var before = Snapshot();
        _selected.Clear();
        if (index >= 0) _selected.Add(index);
        Current = _anchor = index;
        return Changed(before);
    }

    public bool Clear() => Set(-1);

    /// <summary>Drops indices beyond <paramref name="count"/>. Returns <c>true</c> if the selection changed.</summary>
    public bool Clamp(int count)
    {
        bool changed = _selected.RemoveWhere(i => i >= count) > 0;
        if (Current >= count) Current = count - 1;
        if (_anchor >= count) _anchor = count - 1;
        return changed;
    }

    /// <summary>Follows a move of one item (drag reorder).</summary>
    public void OnItemMoved(int from, int to)
    {
        bool wasSelected = _selected.Remove(from);
        var shifted = new List<int>(_selected.Count);
        foreach (int i in _selected)
        {
            int j = i;
            if (from < to && i > from && i <= to) j--;
            else if (from > to && i >= to && i < from) j++;
            shifted.Add(j);
        }
        _selected.Clear();
        foreach (int i in shifted) _selected.Add(i);
        if (wasSelected) _selected.Add(to);
        if (Current == from) Current = to;
        _anchor = Current;
    }

    private (int Count, int Min, int Max, int Hash) Snapshot()
    {
        int hash = 17;
        foreach (int i in _selected) hash = hash * 31 + i;
        return (_selected.Count, First, _selected.Count > 0 ? _selected.Max : -1, hash);
    }

    private bool Changed((int Count, int Min, int Max, int Hash) before) => before != Snapshot();
}
