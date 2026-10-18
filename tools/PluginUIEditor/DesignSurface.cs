// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Design;
using Canvas = PluginUI.Canvas;
using IParameter = PluginUI.IParameter;
using LiveEditor = PluginUI.LiveEditor;
using Theme = PluginUI.Theme;
using Point = PluginUI.Point;
using SizingMode = PluginUI.SizingMode;
using UIElement = PluginUI.UIElement;
using UIHost = PluginUI.UIHost;

namespace PluginUIEditor;

/// <summary>
/// The design canvas: the design rendered by a real <see cref="UIHost"/> (Direct2D), edited with PluginUI's live
/// editor (selection, move, resize handles, snap grid), and fed by drag and drop from the toolbox.
/// </summary>
internal sealed class DesignSurface : UserControl
{
    /// <summary>Data format of toolbox drags: the element type name.</summary>
    public const string ToolboxFormat = "PluginUIEditor.ElementType";

    private static readonly string[] LayoutProperties = [nameof(UIElement.Left), nameof(UIElement.Top), nameof(UIElement.Width), nameof(UIElement.Height), nameof(UIElement.Margin)];

    private readonly DesignSession _session;
    private readonly HostPanel _panel = new();
    private readonly Dictionary<UIElement, DesignNode> _nodeOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DesignNode, UIElement> _elementOf = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, IParameter> _previews = new(StringComparer.Ordinal);
    private string _previewKey = string.Empty;
    private UIHost? _host;
    private bool _syncing;
    private bool _preview;

    public DesignSurface(DesignSession session)
    {
        _session = session;
        AutoScroll = true;
        BackColor = System.Drawing.Color.FromArgb(0x2B, 0x2D, 0x33);
        DoubleBuffered = true;
        _panel.AllowDrop = true;
        _panel.DragEnter += OnDragOver;
        _panel.DragOver += OnDragOver;
        _panel.DragDrop += OnDragDrop;
        Controls.Add(_panel);
        _session.Changed += OnSessionChanged;
    }

    /// <summary>Problems of the last build (unknown types, missing assets…).</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>Raised after each rebuild.</summary>
    public event Action? Rebuilt;

    public UIHost? Host => _host;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Zoom
    {
        get => _host?.Zoom ?? 1f;
        set
        {
            if (_host is null) return;
            _host.Zoom = value;
            CenterPanel();
        }
    }

    /// <summary>Interactive preview: the controls react to the mouse (live edit off).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool PreviewMode
    {
        get => _preview;
        set
        {
            _preview = value;
            if (_host is not null) _host.IsLiveEditEnabled = !value;
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowOutlines
    {
        get => _host?.LiveEditor.ShowOutlines ?? true;
        set { if (_host is not null) { _host.LiveEditor.ShowOutlines = value; _host.Invalidate(); } }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float GridSize
    {
        get => _host?.LiveEditor.GridSize ?? 4f;
        set { if (_host is not null) _host.LiveEditor.GridSize = value; }
    }

    /// <summary>Zoom that shows the whole design in the visible area (never above 100 %).</summary>
    public float FitZoom()
    {
        if (_host is null || _panel.Width <= 0 || _panel.Height <= 0) return 1f;
        const int Margin = 24;
        float fit = Math.Min((ClientSize.Width - 2f * Margin) / _panel.Width, (ClientSize.Height - 2f * Margin) / _panel.Height);
        return Math.Clamp(Zoom * fit, 0.25f, 1f);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_host is not null || DesignMode) return;
        var doc = _session.Document;
        _host = new UIHost(new Canvas(), doc.DesignSize, SizingMode.Fixed);
        _host.ResizeRequestHandler = (w, h) =>
        {
            _host.SetPixelSize(w, h);
            _panel.Size = new System.Drawing.Size(w, h);
            CenterPanel();
            return true;
        };
        _host.AttachToParent(_panel.Handle, detectDpi: true);
        _panel.Size = new System.Drawing.Size(_host.PixelSize.Width, _host.PixelSize.Height);
        _host.IsLiveEditEnabled = !_preview;
        // Clicks give the keyboard focus to the host window, so arrows reach the live editor and the editing
        // shortcuts reach MainForm.ProcessCmdKey (WinForms pre-processes the keys of foreign child windows).
        _host.TakeFocusOnClick = true;
        // Live edit is driven by the editor (preview mode), not by Ctrl+Shift+E.
        _host.LiveEditShortcutEnabled = false;
        _panel.FocusTarget = _host.RequestKeyboardFocus;
        _host.LiveEditor.ShowHelpBar = false;
        _host.LiveEditor.ExitOnEscape = false;
        _host.LiveEditor.GridSize = _session.GridSize;
        _host.LiveEditor.SelectionChanged += OnLiveSelectionChanged;
        _host.LiveEditor.ElementEdited += OnLiveElementEdited;
        Rebuild();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Changed -= OnSessionChanged;
            _host?.Dispose();
            _host = null;
        }
        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        CenterPanel();
    }

    private void CenterPanel()
    {
        const int Margin = 24;
        var client = ClientSize;
        int x = Math.Max(Margin, (client.Width - _panel.Width) / 2);
        int y = Math.Max(Margin, (client.Height - _panel.Height) / 2);
        AutoScrollMinSize = new System.Drawing.Size(_panel.Width + 2 * Margin, _panel.Height + 2 * Margin);
        _panel.Location = new System.Drawing.Point(x + AutoScrollPosition.X, y + AutoScrollPosition.Y);
    }

    private void OnSessionChanged(DesignChange change)
    {
        switch (change)
        {
            case DesignChange.Selection:
                SyncSelection();
                break;
            case DesignChange.Layout:
                break; // the element already shows it
            default:
                Rebuild();
                break;
        }
    }

    /// <summary>Rebuilds the live tree from the document (fast: designs are small).</summary>
    public void Rebuild()
    {
        if (_host is null) return;
        var doc = _session.Document;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string key = string.Join("|", doc.Parameters.Select(p => string.Join("/", p.Name, p.Kind, p.Min.ToString("R", inv), p.Max.ToString("R", inv), p.Default.ToString("R", inv), p.Unit, p.Decimals.ToString(inv), string.Join(",", p.Choices))));
        if (key != _previewKey)
        {
            // Preview parameters keep their values while the definitions do not change.
            _previews = new Dictionary<string, IParameter>(StringComparer.Ordinal);
            foreach (var p in doc.Parameters)
            {
                try
                {
                    _previews.TryAdd(p.Name, p.CreatePreview());
                }
                catch (ArgumentException)
                {
                    // Inconsistent range while it is being typed: the controls stay unbound in the preview.
                }
            }
            _previewKey = key;
        }
        _nodeOf.Clear();
        _elementOf.Clear();
        DesignBuildResult result;
        Theme theme;
        try
        {
            result = DesignLoader.Build(doc, _session.Registry, new DesignLoadOptions
            {
                ResolveParameter = name => _previews.GetValueOrDefault(name),
                Assets = new FileAssetResolver(_session.AssetFolder),
                ElementCreated = (element, node) =>
                {
                    _nodeOf[element] = node;
                    _elementOf[node] = element;
                },
            });
            theme = doc.Theme.Create();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or NotSupportedException or IOException)
        {
            // Never leave the editor unusable because of one bad value: show the problem, keep the previous picture.
            Warnings = [e.Message];
            Rebuilt?.Invoke();
            return;
        }
        Warnings = result.Warnings;
        _host.Theme = theme;
        _host.DesignSize = doc.DesignSize;
        _host.Root = result.Root;
        var (w, h) = _host.PreferredPixelSize;
        _host.RequestResize(w, h);
        SyncSelection();
        Rebuilt?.Invoke();
    }

    private void SyncSelection()
    {
        if (_host is null) return;
        _syncing = true;
        try
        {
            var node = _session.Selected;
            _host.LiveEditor.Selected = node is not null && _elementOf.TryGetValue(node, out var element) ? element : null;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnLiveSelectionChanged(LiveEditor editor, UIElement? element)
    {
        if (_syncing) return;
        _session.Selected = element is not null && _nodeOf.TryGetValue(element, out var node) ? node : null;
    }

    private void OnLiveElementEdited(LiveEditor editor, UIElement element)
    {
        if (!_nodeOf.TryGetValue(element, out var node)) return;
        var info = _session.Registry.Find(node.Type);
        if (info is null) return;
        var layout = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in LayoutProperties)
        {
            var property = info.Find(name);
            if (property is null || !DesignValues.TryCapture(property.GetValue(element), property.PropertyType, out var value)) continue;
            layout[name] = DesignValues.AreEqual(value, info.DefaultValue(name)) ? null : value;
        }
        _session.SetLayout(node, layout);
    }

    // =====================================================================================
    // Drag and drop from the toolbox
    // =====================================================================================

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(ToolboxFormat) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (_host is null || e.Data?.GetData(ToolboxFormat) is not string type) return;
        var client = _panel.PointToClient(new System.Drawing.Point(e.X, e.Y));
        var point = new Point(client.X / _host.Scale, client.Y / _host.Scale);
        Drop(type, point);
    }

    /// <summary>Adds an element at a point (DIPs) in the deepest container under it.</summary>
    public void Drop(string type, Point? point)
    {
        if (_host is null) return;
        DesignNode container = _session.TargetContainer();
        Point? local = null;
        if (point is { } p)
        {
            container = _session.Document.Root;
            foreach (var element in _host.Root.DescendantsAndSelf())
            {
                if (!element.Bounds.Contains(p) || !_nodeOf.TryGetValue(element, out var candidate)) continue;
                if (_session.CanAccept(candidate)) container = candidate; // descendants come later: deepest wins
            }
            var bounds = _elementOf.TryGetValue(container, out var c) ? c.Bounds : default;
            local = new Point(p.X - bounds.X, p.Y - bounds.Y);
        }
        if (!_session.CanAccept(container))
        {
            MessageBox.Show(this, string.Format(System.Globalization.CultureInfo.InvariantCulture, EditorStrings.Current.CannotHoldChildren, container), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var node = _session.CreateNode(type, local, container);
        _session.Insert(node, container);
        _panel.Focus();
    }

    /// <summary>Renders the design to a PNG file (without the editing overlay).</summary>
    public void ExportPng(string path, float scale)
    {
        if (_host is null) return;
        bool live = _host.IsLiveEditEnabled;
        _host.IsLiveEditEnabled = false;
        try
        {
            _host.RenderToPng(path, scale);
        }
        finally
        {
            _host.IsLiveEditEnabled = live;
        }
    }

    /// <summary>
    /// Parent of the native host window. It can take the focus (Tab, toolbox drops) and passes it on to the host
    /// window, which receives the keys once WinForms has processed the menu and editing shortcuts.
    /// </summary>
    private sealed class HostPanel : Panel
    {
        public HostPanel()
        {
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Action? FocusTarget { get; set; }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            FocusTarget?.Invoke();
        }
    }
}
