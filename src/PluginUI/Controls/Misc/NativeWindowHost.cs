// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>
/// Embeds a foreign Win32 window (video surface, WebView2 controller window, third-party view...) into the layout:
/// the window is re-parented to the host window and kept over <see cref="UIElement.Bounds"/> (converted to pixels).
/// It is hidden while this element is collapsed or detached, and re-parented when the host window is recreated.
/// </summary>
/// <remarks>
/// A native window always paints above the Direct2D content and is not clipped by scrolling containers or covered by
/// popups. The application keeps ownership of the child window (create it, destroy it after removing it here).
/// </remarks>
public sealed unsafe class NativeWindowHost : UIElement
{
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private nint _child;
    private UIHost? _subscribedHost;

    /// <summary>Creates an empty native host.</summary>
    public NativeWindowHost() { }

    /// <summary>Creates a native host for <paramref name="childWindow"/>.</summary>
    public NativeWindowHost(nint childWindow) => ChildWindow = childWindow;

    /// <summary>The embedded window handle (0 = none). Changing it hides the previous window.</summary>
    [Browsable(false)]
    public nint ChildWindow
    {
        get => _child;
        set
        {
            if (_child == value) return;
            HideChild();
            _child = value;
            Sync();
        }
    }

    /// <summary>Natural size reported to layouts when no explicit size is set.</summary>
    [Category("Layout")]
    public Size PreferredSize { get; set; } = new(200f, 120f);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available) => PreferredSize;

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
        base.OnDetached();
    }

    /// <inheritdoc />
    protected override void OnVisibilityChanged()
    {
        base.OnVisibilityChanged();
        if (!IsVisible) HideChild(); else Sync();
    }

    private void OnHostWindowCreated(UIHost host) => Sync();

    private void OnHostWindowDestroying(UIHost host) => HideChild();

    /// <summary>Re-applies parent, position and visibility (call after moving the window yourself).</summary>
    public void Sync()
    {
        if (_child == 0) return;
        var host = Host;
        if (host is not { IsAttached: true } || !IsEffectivelyVisible() || Bounds.IsEmpty)
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
        }
        float s = host.Scale;
        int x = (int)MathF.Round(Bounds.X * s), y = (int)MathF.Round(Bounds.Y * s);
        int w = Math.Max(1, (int)MathF.Round(Bounds.Right * s) - x), h = Math.Max(1, (int)MathF.Round(Bounds.Bottom * s) - y);
        SetWindowPos(hwnd, HWND.NULL, x, y, w, h, 0x0040 /* SWP_SHOWWINDOW */ | 0x0010 /* SWP_NOACTIVATE */ | SWP_NOOWNERZORDER);
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
        if (_child != 0) ShowWindow((HWND)_child, PluginUI.Interop.Win32.SW_HIDE);
    }
}

/// <summary>A thin horizontal or vertical rule.</summary>
public class Separator : UIElement
{
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
