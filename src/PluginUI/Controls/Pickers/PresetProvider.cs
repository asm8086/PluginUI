// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Source of presets for a <see cref="PresetBar"/>: implement it over the plug-in's preset storage
/// (factory presets, a user folder, a VST3 program list...).
/// </summary>
public interface IPresetProvider
{
    /// <summary>Number of presets.</summary>
    int Count { get; }

    /// <summary>Index of the loaded preset (−1 = none).</summary>
    int CurrentIndex { get; }

    /// <summary><c>true</c> if parameters changed since the preset was loaded.</summary>
    bool IsModified { get; }

    /// <summary>Name of a preset.</summary>
    string GetName(int index);

    /// <summary>Bank / category of a preset (null = no grouping).</summary>
    string? GetBank(int index);

    /// <summary><c>true</c> if <see cref="Save"/> is supported.</summary>
    bool CanSave { get; }

    /// <summary><c>true</c> if the preset can be deleted (user presets).</summary>
    bool CanDelete(int index);

    /// <summary>Loads a preset.</summary>
    void Load(int index);

    /// <summary>Saves the current state under <paramref name="name"/> (overwrites a preset with the same name); returns its index.</summary>
    int Save(string name);

    /// <summary>Deletes a preset.</summary>
    void Delete(int index);

    /// <summary>Raised when the list, the current index or the modified flag changes.</summary>
    event Action<IPresetProvider>? Changed;
}

/// <summary>A preset entry of a <see cref="PresetList"/>.</summary>
/// <param name="Name">Name.</param>
/// <param name="Bank">Bank (null = none).</param>
/// <param name="Data">Payload (parameter values, state blob...).</param>
/// <param name="IsFactory">Factory presets cannot be deleted or overwritten.</param>
public sealed record PresetEntry(string Name, string? Bank = null, object? Data = null, bool IsFactory = false);

/// <summary>
/// In-memory <see cref="IPresetProvider"/>: the application supplies the load/capture callbacks
/// (<see cref="Loading"/>, <see cref="Capture"/>) and persists <see cref="Entries"/> as it likes.
/// </summary>
public sealed class PresetList : IPresetProvider
{
    private readonly List<PresetEntry> _entries = new();
    private int _current = -1;
    private bool _modified;

    /// <summary>Creates an empty list.</summary>
    public PresetList() { }

    /// <summary>Creates a list from entries.</summary>
    public PresetList(IEnumerable<PresetEntry> entries) => _entries.AddRange(entries);

    /// <summary>Entries.</summary>
    public IReadOnlyList<PresetEntry> Entries => _entries;

    /// <summary>Called to apply a preset (index, entry).</summary>
    [Browsable(false)]
    public Action<int, PresetEntry>? Loading { get; set; }

    /// <summary>Called to capture the current state when saving (returns the payload).</summary>
    [Browsable(false)]
    public Func<object?>? Capture { get; set; }

    /// <summary>Bank assigned to presets saved by the user.</summary>
    [Category("Data")]
    public string? UserBank { get; set; } = "User";

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public int CurrentIndex => _current;

    /// <inheritdoc />
    public bool IsModified => _modified;

    /// <inheritdoc />
    public bool CanSave => true;

    /// <inheritdoc />
    public event Action<IPresetProvider>? Changed;

    /// <summary>Adds an entry.</summary>
    public void Add(PresetEntry entry)
    {
        _entries.Add(entry);
        Changed?.Invoke(this);
    }

    /// <summary>Marks the current preset as modified (call when a parameter changes).</summary>
    public void MarkModified()
    {
        if (_modified) return;
        _modified = true;
        Changed?.Invoke(this);
    }

    /// <inheritdoc />
    public string GetName(int index) => _entries[index].Name;

    /// <inheritdoc />
    public string? GetBank(int index) => _entries[index].Bank;

    /// <inheritdoc />
    public bool CanDelete(int index) => index >= 0 && index < _entries.Count && !_entries[index].IsFactory;

    /// <inheritdoc />
    public void Load(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        _current = index;
        _modified = false;
        Loading?.Invoke(index, _entries[index]);
        Changed?.Invoke(this);
    }

    /// <inheritdoc />
    public int Save(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return _current;
        var entry = new PresetEntry(name, UserBank, Capture?.Invoke());
        int index = _entries.FindIndex(e => !e.IsFactory && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _entries[index] = entry;
        else
        {
            _entries.Add(entry);
            index = _entries.Count - 1;
        }
        _current = index;
        _modified = false;
        Changed?.Invoke(this);
        return index;
    }

    /// <inheritdoc />
    public void Delete(int index)
    {
        if (!CanDelete(index)) return;
        _entries.RemoveAt(index);
        if (_current == index) _current = -1;
        else if (_current > index) _current--;
        Changed?.Invoke(this);
    }
}
