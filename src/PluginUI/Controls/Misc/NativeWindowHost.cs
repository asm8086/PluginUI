// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>
/// Embeds a foreign Win32 window (video surface, WebView2 controller window, third-party view, plug-in editor...) into
/// the layout: the window is re-parented to the host window and kept over <see cref="UIElement.Bounds"/> (converted to
/// pixels). It is hidden while this element is collapsed or detached, and re-parented when the host window is recreated.
/// </summary>
/// <remarks>
/// <para>
/// Two ways to use it: give an existing window to <see cref="ChildWindow"/> (the application keeps ownership: create it,
/// destroy it after removing it here), or set <see cref="ProvidesContainer"/> and let the element create an empty
/// container window, for content that needs a parent window to be created in (a VST3 <c>IPlugView</c> attaches to a
/// parent). The container exists while the host has a window: create the content in <see cref="ContainerCreated"/> and
/// detach it in <see cref="ContainerDestroying"/>.
/// </para>
/// <para>
/// A native window always paints above the Direct2D content and is not clipped by scrolling containers. With
/// <see cref="HideUnderPopups"/> it is hidden while a popup of the host (menu, drop-down list, dialog) overlaps it;
/// native menus (<see cref="UIHost.UseNativeMenus"/>) are drawn above it anyway.
/// </para>
/// </remarks>
public sealed unsafe class NativeWindowHost : UIElement
{
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private nint _child;
    private UIHost? _subscribedHost;
    private ContainerWindow? _container;
    private bool _providesContainer;
    private (int X, int Y, int W, int H) _placed;
    private bool _shown;

    /// <summary>Creates an empty native host.</summary>
    public NativeWindowHost() { }

    /// <summary>Creates a native host for <paramref name="childWindow"/>.</summary>
    public NativeWindowHost(nint childWindow) => ChildWindow = childWindow;

    /// <summary>The embedded window handle (0 = none). Changing it hides the previous window.</summary>
    /// <remarks>With <see cref="ProvidesContainer"/>, the container window (read-only).</remarks>
    [Browsable(false)]
    public nint ChildWindow
    {
        get => _child;
        set
        {
            if (_providesContainer) throw new InvalidOperationException("The element provides its own container window.");
            SetChild(value);
        }
    }

    /// <summary>
    /// The element creates an empty container window (see the remarks) instead of embedding <see cref="ChildWindow"/>.
    /// </summary>
    [Category("Behavior")]
    public bool ProvidesContainer
    {
        get => _providesContainer;
        set
        {
            if (_providesContainer == value) return;
            if (!value) DestroyContainer();
            else SetChild(0);
            _providesContainer = value;
            if (value) CreateContainer();
        }
    }

    /// <summary>Raised after the container window is created (create or attach the content in <see cref="ChildWindow"/>).</summary>
    public event Action<NativeWindowHost>? ContainerCreated;

    /// <summary>Raised before the container window is destroyed (detach the content).</summary>
    public event Action<NativeWindowHost>? ContainerDestroying;

    /// <summary>Raised when the embedded window changes size, in physical pixels.</summary>
    public event Action<NativeWindowHost, int, int>? PixelSizeChanged;

    /// <summary>Hides the window while a popup of the host overlaps it.</summary>
    [Category("Behavior")]
    public bool HideUnderPopups { get; set; }

    /// <summary>Color of the container where the content does not cover it (null = theme background).</summary>
    [Category("Appearance")]
    public Color? ContainerBackground { get; set; }

    /// <summary>Natural size reported to layouts when no explicit size is set.</summary>
    [Category("Layout")]
    public Size PreferredSize { get; set; } = new(200f, 120f);

    /// <summary>
    /// Natural size in physical pixels (for example the editor size a plug-in reports); when set (not zero) it replaces
    /// <see cref="PreferredSize"/>, converted at the host's scale.
    /// </summary>
    [Browsable(false)]
    public (int Width, int Height) PreferredPixelSize
    {
        get => _preferredPixels;
        set
        {
            if (_preferredPixels == value) return;
            _preferredPixels = value;
            InvalidateMeasure();
        }
    }

    private (int Width, int Height) _preferredPixels;

    /// <summary>Current size of the embedded window in physical pixels ((0, 0) while hidden or not placed).</summary>
    [Browsable(false)]
    public (int Width, int Height) PixelSize => (_placed.W, _placed.H);

    /// <summary>Converts a size in physical pixels to DIPs at the host's current scale.</summary>
    public Size PixelsToDips(int width, int height)
    {
        float scale = Host?.Scale ?? 1f;
        return new Size(width / scale, height / scale);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
        => _preferredPixels.Width > 0 && _preferredPixels.Height > 0 ? PixelsToDips(_preferredPixels.Width, _preferredPixels.Height) : PreferredSize;

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds) => Sync();

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _subscribedHost = Host;
        if (_subscribedHost is not null)
        {
            _subscribedHost.Attached += OnHostWindowCreated;
            _subscribedHost.Detaching += OnHostWindowDestroying;
        }
        if (_providesContainer) CreateContainer();
        Sync();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_subscribedHost is not null)
        {
            _subscribedHost.Attached -= OnHostWindowCreated;
            _subscribedHost.Detaching -= OnHostWindowDestroying;
            _subscribedHost = null;
        }
        HideChild();
        DestroyContainer();
        base.OnDetached();
    }

    /// <inheritdoc />
    protected override void OnVisibilityChanged()
    {
        base.OnVisibilityChanged();
        if (!IsVisible) HideChild(); else Sync();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        // Rendering follows every layout and popup change of the host: the moment to hide or show the window again.
        if (HideUnderPopups) Sync();
    }

    private void OnHostWindowCreated(UIHost host)
    {
        if (_providesContainer) CreateContainer();
        _placed = default;
        _shown = false;
        Sync();
    }

    private void OnHostWindowDestroying(UIHost host)
    {
        HideChild();
        DestroyContainer();
    }

    private void SetChild(nint value)
    {
        if (_child == value) return;
        HideChild();
        _child = value;
        _placed = default;
        _shown = false;
        Sync();
    }

    private void CreateContainer()
    {
        if (_container is not null || Host is not { IsAttached: true } host) return;
        _container = new ContainerWindow(this, (HWND)host.WindowHandle);
        _child = (nint)_container.Handle.Value;
        _placed = default;
        _shown = false;
        Sync();
        ContainerCreated?.Invoke(this);
    }

    private void DestroyContainer()
    {
        if (_container is null) return;
        try
        {
            ContainerDestroying?.Invoke(this);
        }
        finally
        {
            _container.Dispose();
            _container = null;
            _child = 0;
            _placed = default;
            _shown = false;
        }
    }

    internal void OnContainerLost()
    {
        // The host window was destroyed without a detach: the container and its content went with it.
        _container = null;
        _child = 0;
        _placed = default;
        _shown = false;
    }

    /// <summary><c>true</c> if an open popup of the host overlaps the element (tool tips and value bubbles excepted).</summary>
    internal bool IsCoveredByPopup()
    {
        if (Host is not { } host) return false;
        foreach (var popup in host.Popups)
        {
            if (popup is ToolTipPopup or ValueBubblePopup) continue;
            if (popup.IsModal || popup.Bounds.Intersects(Bounds)) return true;
        }
        return false;
    }

    /// <summary>Re-applies parent, position and visibility (call after moving the window yourself).</summary>
    public void Sync()
    {
        if (_child == 0) return;
        var host = Host;
        if (host is not { IsAttached: true } || !IsEffectivelyVisible() || Bounds.IsEmpty || (HideUnderPopups && IsCoveredByPopup()))
        {
            HideChild();
            return;
        }
        var hwnd = (HWND)_child;
        var parent = (HWND)host.WindowHandle;
        if (GetParent(hwnd) != parent)
        {
            // WS_CHILD is required for a proper embedded window.
            const int GWL_STYLE = -16;
            nint style = PluginUI.Interop.Win32.GetWindowLongPtr(_child, GWL_STYLE);
            style = (style | 0x40000000) & ~unchecked((nint)0x80000000L); // +WS_CHILD, -WS_POPUP
            PluginUI.Interop.Win32.SetWindowLongPtr(_child, GWL_STYLE, style);
            SetParent(hwnd, parent);
            _placed = default;
            _shown = false;
        }
        float s = host.Scale;
        int x = (int)MathF.Round(Bounds.X * s), y = (int)MathF.Round(Bounds.Y * s);
        int w = Math.Max(1, (int)MathF.Round(Bounds.Right * s) - x), h = Math.Max(1, (int)MathF.Round(Bounds.Bottom * s) - y);
        if (_shown && _placed == (x, y, w, h)) return;
        bool resized = _placed.W != w || _placed.H != h;
        _placed = (x, y, w, h);
        _shown = true;
        SetWindowPos(hwnd, HWND.NULL, x, y, w, h, 0x0040 /* SWP_SHOWWINDOW */ | 0x0010 /* SWP_NOACTIVATE */ | SWP_NOOWNERZORDER);
        if (resized) PixelSizeChanged?.Invoke(this, w, h);
    }

    private bool IsEffectivelyVisible()
    {
        for (UIElement? e = this; e is not null; e = e.Parent)
        {
            if (!e.IsVisible) return false;
        }
        return true;
    }

    private void HideChild()
    {
        if (_child != 0 && _shown) ShowWindow((HWND)_child, PluginUI.Interop.Win32.SW_HIDE);
        _shown = false;
    }

    /// <summary>Empty child window painted with the background color: the parent of embedded content.</summary>
    private sealed class ContainerWindow : PluginUI.Interop.IWindowMessageHandler, IDisposable
    {
        private static readonly PluginUI.Interop.WindowClass Class = new("PluginUI.NativeContainer", 0);
        private readonly NativeWindowHost _owner;
        private System.Runtime.InteropServices.GCHandle _self;
        private HBRUSH _brush;
        private Color _brushColor;

        public ContainerWindow(NativeWindowHost owner, HWND parent)
        {
            _owner = owner;
            Class.AddRef();
            _self = System.Runtime.InteropServices.GCHandle.Alloc(this);
            try
            {
                Handle = Class.Create(_self, PluginUI.Interop.Win32.WS_CHILD | PluginUI.Interop.Win32.WS_CLIPCHILDREN | PluginUI.Interop.Win32.WS_CLIPSIBLINGS,
                    0, 0, 0, 1, 1, parent);
            }
            catch
            {
                _self.Free();
                Class.Release();
                throw;
            }
        }

        public HWND Handle { get; private set; }

        public bool HandleMessage(HWND hwnd, uint msg, nuint wParam, nint lParam, out nint result)
        {
            result = 0;
            if (msg != PluginUI.Interop.Win32.WM_ERASEBKGND) return false;
            var color = _owner.ContainerBackground ?? _owner.Theme.Palette.Background;
            if (_brush == HBRUSH.NULL || color != _brushColor)
            {
                if (_brush != HBRUSH.NULL) DeleteObject((HGDIOBJ)_brush.Value);
                _brushColor = color;
                _brush = CreateSolidBrush(new COLORREF(Channel(color.R) | (Channel(color.G) << 8) | (Channel(color.B) << 16)));
            }
            RECT client;
            GetClientRect(hwnd, &client);
            FillRect((HDC)(nint)wParam, &client, _brush);
            result = 1;
            return true;
        }

        private static uint Channel(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

        public void OnNativeWindowDestroyed(HWND hwnd)
        {
            if (Handle != hwnd) return;
            Handle = HWND.NULL;
            ReleaseResources();
            _owner.OnContainerLost();
        }

        public void Dispose()
        {
            if (Handle != HWND.NULL)
            {
                var hwnd = Handle;
                Handle = HWND.NULL;
                PluginUI.Interop.Win32.SetWindowLongPtr((nint)hwnd.Value, PluginUI.Interop.Win32.GWLP_USERDATA, 0);
                DestroyWindow(hwnd);
            }
            ReleaseResources();
        }

        private void ReleaseResources()
        {
            if (_brush != HBRUSH.NULL)
            {
                DeleteObject((HGDIOBJ)_brush.Value);
                _brush = HBRUSH.NULL;
            }
            if (_self.IsAllocated)
            {
                _self.Free();
                Class.Release();
            }
        }
    }
}

/// <summary>A thin horizontal or vertical rule.</summary>
public class Separator : UIElement
{
    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new SeparatorAutomationPeer(this);

    /// <summary>Creates a horizontal separator.</summary>
    public Separator() { }

    /// <summary>Creates a separator with an orientation.</summary>
    public Separator(Orientation orientation) => Orientation = orientation;

    /// <summary>Line direction.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Line color (null = theme border).</summary>
    [Category("Value")]
    public Color? Color { get; set; }

    /// <summary>Line thickness in DIPs (0 = one physical pixel).</summary>
    [Category("Appearance")]
    public float Thickness { get; set; }

    /// <summary>Empty space on both sides of the line, across it.</summary>
    [Category("Layout")]
    public float Spacing { get; set; } = 4f;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float t = MathF.Max(1f, Thickness) + Spacing * 2f;
        return Orientation == Orientation.Horizontal ? new Size(0f, t) : new Size(t, 0f);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        float t = Thickness > 0f ? Thickness : 1f / dc.Scale;
        var color = Color ?? Theme.Palette.Border;
        var c = Bounds.Center;
        if (Orientation == Orientation.Horizontal)
        {
            float y = MathF.Round(c.Y * dc.Scale) / dc.Scale + t * 0.5f;
            dc.DrawLine(new Point(Bounds.X, y), new Point(Bounds.Right, y), color, t);
        }
        else
        {
            float x = MathF.Round(c.X * dc.Scale) / dc.Scale + t * 0.5f;
            dc.DrawLine(new Point(x, Bounds.Y), new Point(x, Bounds.Bottom), color, t);
        }
    }
}

/// <summary>Invisible element taking space: a fixed gap (<see cref="UIElement.Width"/>/<see cref="UIElement.Height"/>), or a stretchable filler.</summary>
public class Spacer : UIElement
{
    /// <summary>Creates a spacer that takes no space unless sized or stretched.</summary>
    public Spacer()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Creates a square fixed gap.</summary>
    public Spacer(float size) : this()
    {
        Width = size;
        Height = size;
    }
}
