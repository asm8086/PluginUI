// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI;
using PluginUI.Design;

namespace PluginUIEditor;

/// <summary>What changed in a <see cref="DesignSession"/>.</summary>
internal enum DesignChange
{
    /// <summary>The whole document was replaced (new, open, undo, redo).</summary>
    Document,

    /// <summary>Elements were added, removed or moved: the tree is rebuilt.</summary>
    Structure,

    /// <summary>A property of the selected element changed (the live element was updated in place).</summary>
    Property,

    /// <summary>Layout was edited on the design surface (element already up to date).</summary>
    Layout,

    /// <summary>Parameters, theme, size or code generation settings changed: the tree is rebuilt.</summary>
    Settings,

    /// <summary>The selection changed.</summary>
    Selection,
}

/// <summary>
/// The open design: document, file, undo history (document snapshots), selection and editing commands. UI panels
/// observe <see cref="Changed"/>.
/// </summary>
internal sealed class DesignSession
{
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private DesignNode? _selected;

    public DesignSession(DesignTypeRegistry registry)
    {
        Registry = registry;
        Document = NewDocument();
        SavedSnapshot = DesignJson.Write(Document);
    }

    public DesignTypeRegistry Registry { get; }

    public DesignDocument Document { get; private set; }

    public string? FilePath { get; private set; }

    private string SavedSnapshot { get; set; }

    public bool IsDirty => DesignJson.Write(Document) != SavedSnapshot;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string DisplayName => FilePath is null ? EditorStrings.Current.Untitled : Path.GetFileName(FilePath);

    /// <summary>Folder the asset paths are relative to.</summary>
    public string AssetFolder => FilePath is null ? AppContext.BaseDirectory : Path.GetDirectoryName(FilePath)!;

    public DesignNode? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            Changed?.Invoke(DesignChange.Selection);
        }
    }

    public event Action<DesignChange>? Changed;

    public static DesignDocument NewDocument()
    {
        var document = new DesignDocument { Name = "Editor", DesignSize = new PluginUI.Size(640f, 400f) };
        document.Root = new DesignNode("Canvas") { Name = "root" };
        document.Codegen.Namespace = "MyPlugin.Editor";
        document.Codegen.ClassName = "MyEditor";
        return document;
    }

    // =====================================================================================
    // Files
    // =====================================================================================

    public void New()
    {
        Replace(NewDocument(), null);
    }

    public IReadOnlyList<string> Open(string path)
    {
        var warnings = new List<string>();
        var document = DesignJson.Load(path, Registry, warnings);
        Replace(document, path);
        return warnings;
    }

    public void Save(string path)
    {
        DesignJson.Save(Document, path);
        FilePath = path;
        SavedSnapshot = DesignJson.Write(Document);
        Changed?.Invoke(DesignChange.Settings);
    }

    private void Replace(DesignDocument document, string? path)
    {
        Document = document;
        FilePath = path;
        SavedSnapshot = DesignJson.Write(document);
        _undo.Clear();
        _redo.Clear();
        _selected = null;
        Changed?.Invoke(DesignChange.Document);
    }

    // =====================================================================================
    // Undo
    // =====================================================================================

    /// <summary>Applies a change, recording an undo step.</summary>
    public void Edit(DesignChange kind, Action change)
    {
        string before = DesignJson.Write(Document);
        change();
        if (DesignJson.Write(Document) == before) return;
        _undo.Add(before);
        if (_undo.Count > 300) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke(kind);
    }

    public void Undo() => Restore(_undo, _redo);

    public void Redo() => Restore(_redo, _undo);

    private void Restore(List<string> from, List<string> to)
    {
        if (from.Count == 0) return;
        var path = _selected is null ? null : PathOf(_selected);
        to.Add(DesignJson.Write(Document));
        string snapshot = from[^1];
        from.RemoveAt(from.Count - 1);
        Document = DesignJson.Read(snapshot, Registry);
        _selected = path is null ? null : NodeAt(path);
        Changed?.Invoke(DesignChange.Document);
    }

    private List<int>? PathOf(DesignNode node)
    {
        var path = new List<int>();
        var current = node;
        while (!ReferenceEquals(current, Document.Root))
        {
            var parent = Document.Root.FindParent(current);
            if (parent is null) return null;
            path.Insert(0, parent.Children.IndexOf(current));
            current = parent;
        }
        return path;
    }

    private DesignNode? NodeAt(List<int> path)
    {
        var node = Document.Root;
        foreach (int index in path)
        {
            if (index < 0 || index >= node.Children.Count) return null;
            node = node.Children[index];
        }
        return node;
    }

    // =====================================================================================
    // Commands
    // =====================================================================================

    public DesignNode? ParentOf(DesignNode node) => Document.Root.FindParent(node);

    public DesignContainerKind ContainerOf(DesignNode node) => Registry.Find(node.Type)?.Container ?? DesignContainerKind.None;

    /// <summary>The container a new element goes into: the selection if it holds children, else its parent, else the root.</summary>
    public DesignNode TargetContainer()
    {
        for (var n = _selected; n is not null; n = ParentOf(n))
        {
            if (CanAccept(n)) return n;
        }
        return Document.Root;
    }

    public bool CanAccept(DesignNode container) => ContainerOf(container) switch
    {
        DesignContainerKind.Children or DesignContainerKind.Tabs => true,
        DesignContainerKind.Content => container.Children.Count == 0,
        DesignContainerKind.Split => container.Children.Count < 2,
        _ => false,
    };

    /// <summary>Creates a node of a type with a unique name and the natural size of the element.</summary>
    public DesignNode CreateNode(string type, PluginUI.Point? position, DesignNode container)
    {
        var info = Registry.Find(type) ?? throw new ArgumentException($"Unknown type {type}.", nameof(type));
        // "switch" (Switch) is a C# keyword: such base names get a digit.
        string baseName = char.ToLowerInvariant(type[0]) + type[1..];
        if (!IsElementName(baseName)) baseName += "1";
        var node = new DesignNode(type) { Name = Document.UniqueName(baseName) };
        if (Registry.Find(container.Type)?.Type is { } containerType && typeof(Canvas).IsAssignableFrom(containerType))
        {
            var size = info.DefaultElementSize();
            float grid = GridSize > 0f ? GridSize : 1f;
            var p = position ?? new PluginUI.Point(16f, 16f);
            node.Set(nameof(UIElement.Left), MathF.Round(p.X / grid) * grid);
            node.Set(nameof(UIElement.Top), MathF.Round(p.Y / grid) * grid);
            node.Set(nameof(UIElement.Width), MathF.Round(size.Width));
            node.Set(nameof(UIElement.Height), MathF.Round(size.Height));
        }
        node.Slot = SlotFor(container, node.Slot);
        return node;
    }

    public float GridSize { get; set; } = 4f;

    public void Insert(DesignNode node, DesignNode container, int index = -1)
    {
        Edit(DesignChange.Structure, () =>
        {
            if (index < 0 || index > container.Children.Count) container.Children.Add(node);
            else container.Children.Insert(index, node);
        });
        Selected = node;
    }

    public void Delete(DesignNode node)
    {
        if (ReferenceEquals(node, Document.Root)) return;
        var parent = ParentOf(node);
        if (parent is null) return;
        int index = parent.Children.IndexOf(node);
        Edit(DesignChange.Structure, () => parent.Children.Remove(node));
        _selected = parent.Children.Count == 0 ? parent : parent.Children[Math.Min(index, parent.Children.Count - 1)];
        Changed?.Invoke(DesignChange.Selection);
    }

    public void MoveInParent(DesignNode node, bool toFront)
    {
        var parent = ParentOf(node);
        if (parent is null) return;
        Edit(DesignChange.Structure, () =>
        {
            parent.Children.Remove(node);
            if (toFront) parent.Children.Add(node);
            else parent.Children.Insert(0, node);
        });
    }

    /// <summary>Moves an element into another container; returns false when the container cannot take it.</summary>
    public bool Reparent(DesignNode node, DesignNode newParent, int index)
    {
        var parent = ParentOf(node);
        if (parent is null || node.DescendantsAndSelf().Contains(newParent)) return false;
        // Moving inside the same parent never changes its capacity; elsewhere the container must have room.
        if (!ReferenceEquals(parent, newParent) && !CanAccept(newParent)) return false;
        Edit(DesignChange.Structure, () =>
        {
            int old = parent.Children.IndexOf(node);
            parent.Children.Remove(node);
            if (!ReferenceEquals(parent, newParent)) node.Slot = SlotFor(newParent, node.Slot);
            if (ReferenceEquals(parent, newParent) && old < index) index--;
            if (index < 0 || index > newParent.Children.Count) newParent.Children.Add(node);
            else newParent.Children.Insert(index, node);
        });
        return true;
    }

    /// <summary>Slot of a new child of a container: tab title, free split pane, nothing elsewhere.</summary>
    private string? SlotFor(DesignNode container, string? current) => ContainerOf(container) switch
    {
        DesignContainerKind.Tabs => current is null or "First" or "Second" ? "Tab " + (container.Children.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : current,
        DesignContainerKind.Split => container.Children.Any(c => c.Slot == "First") || (container.Children.Count > 0 && container.Children.All(c => c.Slot is null)) ? "Second" : "First",
        _ => null,
    };

    /// <summary><c>true</c> if a name can be given to an element (a field of the generated class).</summary>
    public bool IsElementName(string name) => CSharpDesignGenerator.IsElementName(name, Registry, Document.Codegen.ClassName);

    public string Copy(DesignNode node) => DesignJson.WriteNodes([node]);

    /// <summary>Pastes copied elements into the target container (new names, offset position on a canvas).</summary>
    public bool Paste(string text)
    {
        var nodes = DesignJson.ReadNodes(text, Registry);
        if (nodes is null || nodes.Count == 0) return false;
        var container = TargetContainer();
        var pasted = new List<DesignNode>();
        Edit(DesignChange.Structure, () =>
        {
            foreach (var node in nodes)
            {
                if (!CanAccept(container)) break; // a split view holds two panes, a border one child
                node.Slot = SlotFor(container, node.Slot);
                foreach (var n in node.DescendantsAndSelf())
                {
                    if (n.Name is not null) n.Name = Document.UniqueName(n.Name);
                }
                if (node.Get(nameof(UIElement.Left)) is float left) node.Set(nameof(UIElement.Left), left + 8f);
                if (node.Get(nameof(UIElement.Top)) is float top) node.Set(nameof(UIElement.Top), top + 8f);
                container.Children.Add(node);
                pasted.Add(node);
            }
        });
        if (pasted.Count == 0) return false;
        Selected = pasted[^1];
        return true;
    }

    public void SetProperty(DesignNode node, string name, object? value)
    {
        Edit(DesignChange.Property, () =>
        {
            if (value is null) node.Properties.Remove(name);
            else node.Set(name, value);
        });
    }

    public void SetBinding(DesignNode node, string property, string? parameter)
    {
        Edit(DesignChange.Property, () =>
        {
            if (string.IsNullOrEmpty(parameter)) node.Bindings.Remove(property);
            else node.Bindings[property] = parameter;
        });
    }

    /// <summary>
    /// Renames an element. The name becomes a field of the generated class, so it must be a C# identifier that does
    /// not hide a type used by the generated code;
    /// a name already used gets a numeric suffix. Returns false (and changes nothing) for an invalid name.
    /// </summary>
    public bool Rename(DesignNode node, string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (name is not null && !IsElementName(name)) return false;
        if (name is not null && Document.AllNodes().Any(n => !ReferenceEquals(n, node) && n.Name == name)) name = Document.UniqueName(name);
        Edit(DesignChange.Structure, () => node.Name = name);
        return true;
    }

    public void SetSlot(DesignNode node, string? slot) => Edit(DesignChange.Structure, () => node.Slot = string.IsNullOrWhiteSpace(slot) ? null : slot);

    /// <summary>Records the layout of an element moved or resized on the design surface.</summary>
    public void SetLayout(DesignNode node, IReadOnlyDictionary<string, object?> layout)
    {
        Edit(DesignChange.Layout, () =>
        {
            foreach (var (name, value) in layout)
            {
                if (value is null) node.Properties.Remove(name);
                else node.Set(name, value);
            }
        });
    }

    public void EditSettings(Action change) => Edit(DesignChange.Settings, change);
}
