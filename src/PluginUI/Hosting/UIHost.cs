// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Rendering;

namespace PluginUI;

/// <summary>How the host maps its pixel size to the logical (DIP) layout.</summary>
public enum SizingMode
{
    /// <summary>The editor keeps its design size (× DPI × <see cref="UIHost.Zoom"/>). Not resizable by the user.</summary>
    Fixed,
    /// <summary>The design is zoomed to fill the window (aspect preserved when <see cref="UIHost.KeepAspectRatio"/>).</summary>
    ScaleToFit,
    /// <summary>The layout is recomputed for the new logical size (responsive editors).</summary>
    Relayout,
}

/// <summary>
/// Hosts a tree of <see cref="UIElement"/>s in a Win32 child window rendered with Direct2D.
/// The same class serves plug-in editors (child of the DAW window) and standalone applications
/// (child of a <see cref="StandaloneWindow"/>).
/// </summary>
/// <remarks>
/// <para>Lifecycle: create with a root and a design size → <see cref="AttachToParent"/> (window created) →
/// <see cref="SetPixelSize"/> / <see cref="SetDpiScale"/> as the container dictates → <see cref="Detach"/>
/// (window destroyed) → <see cref="Dispose"/>. A host can be attached again after a detach.</para>
/// <para>The container owns the message loop: no <c>Application.Run</c> inside a plug-in.</para>
/// </remarks>
/// <example>
/// A standalone application (in a plug-in, <c>PluginEditorView</c> creates and attaches the host):
/// <code>
/// StandaloneWindow.EnablePerMonitorDpiAwareness();
/// var root = new Canvas();
/// root.Add(new Knob(new LocalParameter("Drive", 0, 10, 2)) { Label = "DRIVE", Left = 20, Top = 20, Width = 72, Height = 96 });
/// var host = new UIHost(root, new Size(320, 200), SizingMode.ScaleToFit)
/// {
///     Theme = Theme.Dark.WithAccent(Color.FromRgb(0xFFB74D)),
///     MinSize = new Size(240, 150),
/// };
/// return new StandaloneWindow(host, "Drive").Run();
/// </code>
/// </example>
public sealed partial class UIHost : IDisposable
{
    private UIElement _root;
    private Theme _theme = Theme.Dark;
    private string? _language;
    private UIStrings? _strings;
    private IMidiLearnService? _midiLearn;
    private Size _designSize;
    private SizingMode _sizing;
    private int _pixelWidth, _pixelHeight;
    private float _dpiScale = 1f;
    private float _zoom = 1f;
    private bool _layoutDirty = true;
    private Renderer? _renderer;
    private bool _disposed;

    /// <summary>Creates a host.</summary>
    /// <param name="root">Root element (often a <see cref="Canvas"/> or a <see cref="Grid"/>).</param>
    /// <param name="designSize">Logical size of the editor at 100 %.</param>
    /// <param name="sizing">Sizing behavior.</param>
    public UIHost(UIElement root, Size designSize, SizingMode sizing = SizingMode.Fixed)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _designSize = designSize;
        _sizing = sizing;
        MinSize = new Size(designSize.Width * 0.5f, designSize.Height * 0.5f);
        MaxSize = new Size(designSize.Width * 4f, designSize.Height * 4f);
        Animator = new Animator(this);
        (_pixelWidth, _pixelHeight) = PreferredPixelSize;
        _root.AttachTo(this);
    }

    // =====================================================================================
    // Content & appearance
    // =====================================================================================

    /// <summary>Root element.</summary>
    public UIElement Root
    {
        get => _root;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_root, value)) return;
            CloseAllPopups();
            _root.DetachFromHost();
            _root = value;
            _root.AttachTo(this);
            InvalidateLayout();
        }
    }

    /// <summary>Theme of every element in this host.</summary>
    public Theme Theme
    {
        get => _theme;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _theme = value;
            InvalidateLayout();
        }
    }

    /// <summary>
    /// Language used by every host whose <see cref="Language"/> is <c>null</c> (default <see cref="UILanguages.Auto"/>:
    /// the Windows display language of the user). Set it once at startup, e.g. from a plugin setting.
    /// </summary>
    public static string DefaultLanguage { get; set; } = UILanguages.Auto;

    /// <summary>
    /// Language of the texts PluginUI displays in this host (dialog buttons, preset menu, tooltips…):
    /// <see cref="UILanguages.Auto"/>, <see cref="UILanguages.English"/>, <see cref="UILanguages.French"/> or any code
    /// registered with <see cref="UIStrings.Register"/>; <c>null</c> = <see cref="DefaultLanguage"/>.
    /// Numbers always use a decimal point, whatever the language (see <see cref="InvariantNumber"/>).
    /// </summary>
    public string? Language
    {
        get => _language;
        set
        {
            if (string.Equals(_language, value, StringComparison.OrdinalIgnoreCase)) return;
            _language = value;
            InvalidateLayout();
        }
    }

    /// <summary>
    /// Texts in effect: the table assigned here, or the table of <see cref="Language"/>. Assign a custom
    /// <see cref="UIStrings"/> to override some texts for this host only; assign <c>null</c> to go back to the language table.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public UIStrings Strings
    {
        get => _strings ?? UIStrings.ForLanguage(_language ?? DefaultLanguage);
        set
        {
            _strings = value;
            InvalidateLayout();
        }
    }

    /// <summary>Background color cleared before drawing (defaults to the theme background).</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Draws keyboard focus rings (turned on by keyboard navigation, off by mouse clicks).</summary>
    public bool ShowFocusRings { get; set; }

    /// <summary>Gives the host window the keyboard focus on every click (standalone apps); plug-ins only take it for text input.</summary>
    public bool TakeFocusOnClick { get; set; }

    /// <summary>Shows menus with the native Win32 menu instead of the custom-drawn popup.</summary>
    public bool UseNativeMenus { get; set; }

    /// <summary>Delay before a tooltip appears.</summary>
    public TimeSpan ToolTipDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Animation engine.</summary>
    public Animator Animator { get; }

    /// <summary>Text measurement (device-accurate once a renderer exists).</summary>
    public ITextMeasurer TextMeasurer => (ITextMeasurer?)_renderer ?? FallbackTextMeasurer.Instance;

    /// <summary>
    /// Called on right click over a control bound to a parameter (when the control has no own context menu):
    /// lets the plug-in adapter show the DAW's parameter menu. Return <c>true</c> if handled.
    /// </summary>
    public Func<IParameter, Point, bool>? ParameterContextMenuRequested { get; set; }

    /// <summary>
    /// MIDI learn back-end (null = no MIDI learn). When set, the right-click menu of controls bound to a parameter
    /// offers "MIDI Learn" / "Forget MIDI assignment", merged into the DAW's parameter menu by plug-in adapters.
    /// </summary>
    public IMidiLearnService? MidiLearn
    {
        get => _midiLearn;
        set
        {
            if (ReferenceEquals(_midiLearn, value)) return;
            if (_midiLearn is not null) _midiLearn.Changed -= Invalidate;
            _midiLearn = value;
            if (value is not null) value.Changed += Invalidate;
            Invalidate();
        }
    }

    /// <summary>
    /// Menu entries PluginUI adds for a parameter (MIDI learn commands when <see cref="MidiLearn"/> is set), in the
    /// interface language. Plug-in adapters append them to the DAW's parameter menu.
    /// </summary>
    /// <param name="parameter">The parameter of the control that was right-clicked.</param>
    /// <returns>The entries (possibly empty).</returns>
    public IReadOnlyList<MenuItem> GetParameterMenuItems(IParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (_midiLearn is not { } learn) return [];
        var t = Strings;
        var items = new List<MenuItem>();
        if (learn.IsLearning(parameter)) items.Add(new MenuItem(t.MidiLearnCancel, learn.CancelLearn));
        else items.Add(new MenuItem(t.MidiLearn, () => learn.BeginLearn(parameter)));
        if (learn.GetAssignment(parameter) is { } assignment) items.Add(new MenuItem(t.FormatMidiForget(assignment), () => learn.Forget(parameter)));
        return items;
    }

    // =====================================================================================
    // Sizing
    // =====================================================================================

    /// <summary>Sizing behavior.</summary>
    public SizingMode Sizing { get => _sizing; set { _sizing = value; InvalidateLayout(); } }

    /// <summary>Logical size of the design at 100 %.</summary>
    public Size DesignSize { get => _designSize; set { _designSize = value; InvalidateLayout(); } }

    /// <summary>Minimum logical size (resizable modes).</summary>
    public Size MinSize { get; set; }

    /// <summary>Maximum logical size (resizable modes).</summary>
    public Size MaxSize { get; set; }

    /// <summary>Keeps the design aspect ratio when resizing in <see cref="SizingMode.ScaleToFit"/>.</summary>
    public bool KeepAspectRatio { get; set; } = true;

    /// <summary><c>true</c> if the container may let the user resize the editor.</summary>
    public bool IsResizable => _sizing != SizingMode.Fixed;

    /// <summary>System/host content scale (1 = 96 DPI).</summary>
    public float DpiScale => _dpiScale;

    /// <summary>User zoom factor (Fixed and Relayout modes); changing it asks the container to resize.</summary>
    public float Zoom
    {
        get => _zoom;
        set
        {
            value = Math.Clamp(value, 0.25f, 8f);
            if (MathF.Abs(value - _zoom) < 1e-4f) return;
            float previous = _zoom;
            _zoom = value;
            var (w, h) = PreferredPixelSize;
            if (_sizing != SizingMode.ScaleToFit && !RequestResize(w, h)) _zoom = previous; // refused: content must keep fitting
            _renderer?.SetScale(Scale);
            InvalidateLayout();
        }
    }

    /// <summary>Pixels per DIP currently applied.</summary>
    /// <remarks>
    /// In <see cref="SizingMode.ScaleToFit"/> the design is scaled uniformly to fit both dimensions (a window with
    /// another aspect ratio leaves an empty band on the right or at the bottom).
    /// </remarks>
    public float Scale => _sizing == SizingMode.ScaleToFit
        ? MathF.Max(0.05f, MathF.Min(_pixelWidth / MathF.Max(1f, _designSize.Width), _pixelHeight / MathF.Max(1f, _designSize.Height)))
        : _dpiScale * _zoom;

    /// <summary>Logical size the root is laid out in.</summary>
    public Size LogicalSize => _sizing == SizingMode.Relayout
        ? new Size(_pixelWidth / Scale, _pixelHeight / Scale)
        : _designSize;

    /// <summary>Current size in physical pixels.</summary>
    public (int Width, int Height) PixelSize => (_pixelWidth, _pixelHeight);

    /// <summary>Pixel size the editor would like (design × DPI × zoom).</summary>
    public (int Width, int Height) PreferredPixelSize
        => ((int)MathF.Round(_designSize.Width * _dpiScale * _zoom), (int)MathF.Round(_designSize.Height * _dpiScale * _zoom));

    /// <summary>
    /// Asked by the host when it needs a new pixel size (zoom, DPI change, <see cref="CornerResizer"/>).
    /// Return <c>true</c> if the container accepted (it then calls <see cref="SetPixelSize"/>).
    /// When null, the host resizes its own window.
    /// </summary>
    public Func<int, int, bool>? ResizeRequestHandler { get; set; }

    /// <summary>Requests a new pixel size from the container.</summary>
    public bool RequestResize(int pixelWidth, int pixelHeight)
    {
        ConstrainPixelSize(ref pixelWidth, ref pixelHeight);
        if (pixelWidth == _pixelWidth && pixelHeight == _pixelHeight) return true;
        if (ResizeRequestHandler is not null) return ResizeRequestHandler(pixelWidth, pixelHeight);
        SetPixelSize(pixelWidth, pixelHeight);
        return true;
    }

    /// <summary>Clamps a proposed pixel size to the min/max size and aspect ratio.</summary>
    public void ConstrainPixelSize(ref int pixelWidth, ref int pixelHeight)
    {
        if (_sizing == SizingMode.Fixed)
        {
            (pixelWidth, pixelHeight) = PreferredPixelSize;
            return;
        }
        float k = _dpiScale * (_sizing == SizingMode.Relayout ? _zoom : 1f);
        float minW = MinSize.Width * k, minH = MinSize.Height * k, maxW = MaxSize.Width * k, maxH = MaxSize.Height * k;
        float w = Math.Clamp(pixelWidth, minW, MathF.Max(minW, maxW));
        float h = Math.Clamp(pixelHeight, minH, MathF.Max(minH, maxH));
        if (_sizing == SizingMode.ScaleToFit && KeepAspectRatio && _designSize.Width > 0f && _designSize.Height > 0f)
        {
            float aspect = _designSize.Width / _designSize.Height;
            // Follow the dimension the user changed (dragging a top/bottom edge changes the height only).
            bool heightDriven = pixelWidth == _pixelWidth && pixelHeight != _pixelHeight;
            if (heightDriven)
            {
                w = h * aspect;
                if (w < minW || w > maxW) { w = Math.Clamp(w, minW, maxW); h = w / aspect; }
            }
            else
            {
                h = w / aspect;
                if (h < minH || h > maxH) { h = Math.Clamp(h, minH, maxH); w = h * aspect; }
            }
        }
        pixelWidth = (int)MathF.Round(w);
        pixelHeight = (int)MathF.Round(h);
    }

    /// <summary>Sets the pixel size (called by the container: VST3 onSize, window WM_SIZE...).</summary>
    public void SetPixelSize(int pixelWidth, int pixelHeight)
    {
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        if (pixelWidth == _pixelWidth && pixelHeight == _pixelHeight) return;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        ResizeWindow();
        _renderer?.Resize((uint)_pixelWidth, (uint)_pixelHeight);
        _renderer?.SetScale(Scale);
        InvalidateLayout();
    }

    /// <summary>
    /// Sets the content scale (DPI / 96). The host asks the container for the matching pixel size through
    /// <see cref="RequestResize"/> (in <see cref="SizingMode.ScaleToFit"/> the current size is scaled proportionally).
    /// </summary>
    public void SetDpiScale(float scale)
    {
        if (!(scale > 0f) || scale > 8f || MathF.Abs(scale - _dpiScale) < 1e-4f) return;
        float previous = _dpiScale;
        _dpiScale = scale;
        if (_sizing == SizingMode.ScaleToFit)
        {
            // The content scale follows the pixel size; a refusal simply keeps the current zoom.
            RequestResize((int)MathF.Round(_pixelWidth * scale / previous), (int)MathF.Round(_pixelHeight * scale / previous));
        }
        else
        {
            var (w, h) = _sizing == SizingMode.Fixed
                ? PreferredPixelSize
                : ((int)MathF.Round(_pixelWidth * scale / previous), (int)MathF.Round(_pixelHeight * scale / previous));
            if (!RequestResize(w, h))
            {
                _dpiScale = previous; // container refused: keep content fitting the current size
            }
        }
        _renderer?.SetScale(Scale);
        InvalidateLayout();
    }

    // =====================================================================================
    // Layout & painting
    // =====================================================================================

    /// <summary>Requests a layout pass and a repaint.</summary>
    public void InvalidateLayout()
    {
        _layoutDirty = true;
        Invalidate();
    }

    /// <summary>Runs the layout pass now if needed.</summary>
    public void UpdateLayout()
    {
        if (!_layoutDirty) return;
        _layoutDirty = false;
        var size = LogicalSize;
        _root.Measure(size);
        _root.Arrange(new Rect(0f, 0f, size.Width, size.Height));
        LayoutPopups(size);
        UpdateFileDropRegistration();
    }

    private void Paint(DrawingContext dc)
    {
        dc.Clear(BackgroundColor ?? _theme.Palette.Background);
        _root.RenderTree(dc);
        RenderPopups(dc);
        _liveEditor?.Render(dc);
    }

    /// <summary>
    /// Renders the UI offscreen and saves it as PNG (documentation, previews, automated tests).
    /// Works whether or not the host is attached to a window.
    /// </summary>
    /// <param name="path">Destination file.</param>
    /// <param name="scale">Pixels per DIP of the image.</param>
    public void RenderToPng(string path, float scale = 1f)
    {
        using var com = new ComScope();
        var size = LogicalSize;
        var renderer = new Renderer();
        var previous = _renderer;
        bool borrowed = previous is null;
        try
        {
            if (borrowed) _renderer = renderer; // accurate text measurement for the layout pass
            if (!IsAttached)
            {
                // No frame timer without a window: finish pending transitions so the image shows final states.
                for (int i = 0; i < 64 && Animator.IsRunning; i++) Animator.Step(1.0);
                ProcessPostedActions();
            }
            _layoutDirty = true;
            UpdateLayout();
            renderer.CreateOffscreen((uint)MathF.Ceiling(size.Width * scale), (uint)MathF.Ceiling(size.Height * scale), scale);
            renderer.Render(Paint);
            renderer.SavePng(path);
        }
        finally
        {
            if (borrowed) _renderer = null;
            renderer.Dispose();
            _layoutDirty = true;
        }
    }

    // =====================================================================================

    /// <summary>
    /// Releases the GPU copy of an image or SVG this host has drawn (it is rebuilt if drawn again). Device copies are
    /// cached per host until the window closes: evict images you replace dynamically (album art, waveforms...).
    /// </summary>
    public void EvictImage(object imageOrSvg)
    {
        ArgumentNullException.ThrowIfNull(imageOrSvg);
        _renderer?.Evict(imageOrSvg);
    }

    /// <summary>Detaches (if needed) and releases every resource.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        CloseAllPopups();
        Animator.CancelAll();
        _root.DetachFromHost();
    }
}
