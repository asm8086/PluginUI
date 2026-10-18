// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Design;

/// <summary>
/// Links a running editor (inside the DAW) to its <c>.pluginui</c> file, in both directions, for development builds:
/// <list type="bullet">
/// <item><b>Live edit → design:</b> Ctrl+S in live edit mode (Ctrl+Shift+E) writes the moved and resized elements
/// back into the design file.</item>
/// <item><b>Design → live editor (hot reload):</b> when PluginUIEditor (or anything else) saves the design file,
/// the editor's element tree is rebuilt from it, without reloading the plug-in.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Saving merges the layout (<c>Left</c>, <c>Top</c>, <c>Width</c>, <c>Height</c>, <c>Margin</c>) of the elements into
/// the existing file, matched by element name (give the elements you edit a name): everything else in the file
/// (assets, parameters, theme, unknown content) is preserved. Only the elements moved or resized since the last save
/// are written; without a file, the whole tree is captured. A reload rebuilds the element tree and applies the theme and
/// the design size of the file.
/// </para>
/// <para>
/// After a reload, references kept by the editor (fields, event handlers) point to the old elements: handle
/// <see cref="Reloaded"/> to wire the new ones. Keep the bridge out of release builds (<c>#if DEBUG</c>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// #if DEBUG
/// private LiveDesignBridge? _bridge;
/// protected override void OnAttached() =&gt; _bridge = new LiveDesignBridge(Host, DesignFilePath, doc =&gt; DesignLoader.Build(doc, options: Options()));
/// protected override void OnRemoved() =&gt; _bridge?.Dispose();
/// #endif
/// </code>
/// </example>
public sealed class LiveDesignBridge : IDisposable
{
    private static readonly string[] LayoutProperties = [nameof(UIElement.Left), nameof(UIElement.Top), nameof(UIElement.Width), nameof(UIElement.Height), nameof(UIElement.Margin)];

    private readonly UIHost _host;
    private readonly Func<DesignDocument, DesignBuildResult>? _rebuild;
    private readonly DesignTypeRegistry _registry;
    private readonly FileSystemWatcher? _watcher;
    private readonly Lock _gate = new();
    private string? _lastWritten;
    private Timer? _debounce;
    private bool _disposed;

    /// <summary>Creates the bridge.</summary>
    /// <param name="host">The editor host.</param>
    /// <param name="designPath">The design file.</param>
    /// <param name="rebuild">
    /// Builds the tree from the design (typically <see cref="DesignLoader.Build"/> with the editor's parameter resolver
    /// and assets). <c>null</c>: no hot reload, saving only.
    /// </param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    public LiveDesignBridge(UIHost host, string designPath, Func<DesignDocument, DesignBuildResult>? rebuild = null, DesignTypeRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(designPath);
        _host = host;
        DesignPath = Path.GetFullPath(designPath);
        _rebuild = rebuild;
        _registry = registry ?? DesignTypeRegistry.Default;
        _host.LiveEditor.SaveRequested += OnSaveRequested;
        if (rebuild is not null && Directory.Exists(Path.GetDirectoryName(DesignPath)))
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(DesignPath)!, Path.GetFileName(DesignPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>The design file.</summary>
    public string DesignPath { get; }

    /// <summary>Raised on the UI thread after the layout was saved (argument: the file path).</summary>
    public event Action<LiveDesignBridge, string>? Saved;

    /// <summary>Raised on the UI thread after the tree was rebuilt from the file.</summary>
    public event Action<LiveDesignBridge, DesignBuildResult>? Reloaded;

    /// <summary>Raised on the UI thread when saving or reloading failed (argument: the message).</summary>
    public event Action<LiveDesignBridge, string>? Failed;

    /// <summary>
    /// Builds the tree from the design now and makes it the host root (also used for the first build, so that the
    /// bridge can match unnamed elements when saving).
    /// </summary>
    /// <returns>The build result, or <c>null</c> if the file could not be loaded.</returns>
    public DesignBuildResult? Reload()
    {
        if (_rebuild is null) return null;
        try
        {
            var document = DesignJson.Load(DesignPath, _registry);
            var result = _rebuild(document);
            var theme = document.Theme.Create();
            _host.Theme = theme;
            _host.Root = result.Root;
            if (document.DesignSize != _host.DesignSize)
            {
                _host.DesignSize = document.DesignSize;
                var (width, height) = _host.PreferredPixelSize;
                _host.RequestResize(width, height);
            }
            Reloaded?.Invoke(this, result);
            return result;
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Failed?.Invoke(this, e.Message);
            return null;
        }
    }

    /// <summary>Saves the current layout to the design file (what Ctrl+S does in live edit mode).</summary>
    /// <returns>The warnings (elements that could not be matched or captured).</returns>
    public IReadOnlyList<string> Save()
    {
        var warnings = new List<string>();
        try
        {
            DesignDocument document;
            if (File.Exists(DesignPath) && _host.LiveEditor.EditedElements.Count == 0)
            {
                // Nothing moved since the last save: writing the whole live tree could revert changes made in the
                // designer meanwhile.
                warnings.Add("No element was moved or resized: nothing to save.");
                return warnings;
            }
            if (File.Exists(DesignPath))
            {
                document = DesignJson.Load(DesignPath, _registry, warnings);
                MergeLayout(document, warnings);
            }
            else
            {
                document = DesignCapture.CaptureHost(_host, _registry, warnings);
                document.Name = Path.GetFileNameWithoutExtension(DesignPath);
            }
            string text = DesignJson.Write(document);
            lock (_gate) _lastWritten = text;
            File.WriteAllText(DesignPath, text, new System.Text.UTF8Encoding(false));
            _host.LiveEditor.ClearEdits();
            Saved?.Invoke(this, DesignPath);
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
        {
            warnings.Add(e.Message);
            Failed?.Invoke(this, e.Message);
        }
        return warnings;
    }

    private void MergeLayout(DesignDocument document, List<string> warnings)
    {
        var byName = document.AllNodes().Where(n => n.Name is not null).GroupBy(n => n.Name!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var element in _host.LiveEditor.EditedElements)
        {
            DesignNode? node = null;
            if (element.Name is not null) byName.TryGetValue(element.Name, out node);
            if (node is null)
            {
                warnings.Add($"{element}: no matching element in the design (give it a Name).");
                continue;
            }
            var info = _registry.Find(element);
            if (info is null) continue;
            foreach (var name in LayoutProperties)
            {
                var property = info.Find(name);
                if (property is null || !DesignValues.TryCapture(property.GetValue(element), property.PropertyType, out var value)) continue;
                if (DesignValues.AreEqual(value, info.DefaultValue(name))) node.Properties.Remove(name);
                else node.Set(name, value);
            }
        }
    }

    private void OnSaveRequested(LiveEditor editor) => Save();

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Editors write files in several steps: wait for the writes to settle.
        lock (_gate)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => _host.Post(ReloadIfChanged), null, 250, Timeout.Infinite);
        }
    }

    private void ReloadIfChanged()
    {
        if (_disposed) return;
        string? text;
        try
        {
            text = File.ReadAllText(DesignPath);
        }
        catch (IOException)
        {
            return; // still being written: the next change notification retries
        }
        lock (_gate)
        {
            if (text == _lastWritten) return; // our own save
            _lastWritten = text;
        }
        Reload();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Dispose();
        }
        _host.LiveEditor.SaveRequested -= OnSaveRequested;
        _watcher?.Dispose();
    }
}
