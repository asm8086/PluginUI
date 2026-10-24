// 2026/10/24 14:21:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Interop;
using PluginUI.Rendering;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

public sealed unsafe partial class UIHost : IWindowMessageHandler
{
    private static readonly WindowClass HostClass = new("PluginUI.Host", Win32.CS_DBLCLKS);

    private HWND _hwnd;
    private GCHandle _selfHandle;
    private bool _holdsClass;
    private bool _acceptsFiles;
    private int _uiThreadId;

    /// <summary>
    /// Called when an exception is caught at a native boundary (window procedure, timers, posted actions).
    /// Exceptions never propagate into the host application; by default they are written to the debug output.
    /// </summary>
    public static Action<Exception, string>? UnhandledException { get; set; }

    /// <summary>
    /// True when the editor can be seen: its window is visible and its top-level window is not minimized. Hosts without a
    /// window (off-screen rendering, tests) report <see langword="true"/>. Analyzers can skip their work while it is false.
    /// </summary>
    public bool IsShownOnScreen
    {
        get
        {
            if (_hwnd == HWND.NULL) return true;
            if (!IsWindowVisible(_hwnd)) return false;
            HWND root = GetAncestor(_hwnd, 2u);   // GA_ROOT
            return root == HWND.NULL || !IsIconic(root);
        }
    }

    internal static void ReportException(Exception ex, string where)
    {
        try
        {
            if (UnhandledException is { } handler) handler(ex, where);
            else System.Diagnostics.Debug.WriteLine($"[PluginUI] Exception in {where}: {ex}");
        }
        catch
        {
            // never throw from the error path
        }
    }

    /// <summary><c>true</c> while a native window exists.</summary>
    public bool IsAttached => _hwnd != HWND.NULL;

    /// <summary>Native window handle (0 when detached).</summary>
    public nint WindowHandle => (nint)_hwnd.Value;

    /// <summary>Raised after the native window was created.</summary>
    public event Action<UIHost>? Attached;

    /// <summary>Raised before the native window is destroyed.</summary>
    public event Action<UIHost>? Detaching;

    /// <summary>
    /// Creates the child window inside <paramref name="parentHwnd"/> at the current pixel size.
    /// </summary>
    /// <param name="parentHwnd">Parent window (DAW editor frame, standalone window...).</param>
    /// <param name="detectDpi">Reads the DPI of the parent and applies it (when the container does not provide a scale).</param>
    public void AttachToParent(nint parentHwnd, bool detectDpi = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (parentHwnd == 0) throw new ArgumentException("Parent window handle is null.", nameof(parentHwnd));
        // Also frees what is left when the parent destroyed our window without a detach (renderer, handle, class).
        Detach();

        try
        {
            _uiThreadId = Environment.CurrentManagedThreadId;
            if (detectDpi)
            {
                uint dpi = GetDpiForWindow((HWND)parentHwnd);
                if (dpi != 0) SetDpiScale(dpi / 96f);
            }

            HostClass.AddRef();
            _holdsClass = true;
            _renderer = new Renderer();
            _selfHandle = GCHandle.Alloc(this);
            _hwnd = HostClass.Create(_selfHandle,
                Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_CLIPSIBLINGS | Win32.WS_TABSTOP,
                0, 0, 0, _pixelWidth, _pixelHeight, (HWND)parentHwnd);
            _renderer.AttachWindow(_hwnd, (uint)_pixelWidth, (uint)_pixelHeight, Scale);
            UpdateFileDropRegistration();
            InvalidateLayout();
            if (Animator.IsRunning || _tickers.Count > 0) EnsureFrameTimer();
            if (!_posted.IsEmpty) PostMessageW(_hwnd, Win32.WM_APP + 1, 0, 0);
            Attached?.Invoke(this);
        }
        catch
        {
            DestroyNativeResources();
            throw;
        }
    }

    /// <summary>Destroys the native window (the element tree is kept; the host can be attached again).</summary>
    public void Detach()
    {
        if (!IsAttached && _renderer is null && !_selfHandle.IsAllocated) return;
        if (IsAttached) Detaching?.Invoke(this);
        CancelInteraction();
        CloseAllPopups();
        HideToolTip();
        DestroyNativeResources();
    }

    private void DestroyNativeResources()
    {
        StopFrameTimer();
        // Release the render target before the window it is bound to.
        _renderer?.Dispose();
        _renderer = null;
        if (_hwnd != HWND.NULL)
        {
            var hwnd = _hwnd;
            _hwnd = HWND.NULL;
            // Unbind first: even if DestroyWindow fails, the window procedure never sees the freed GCHandle.
            Win32.SetWindowLongPtr((nint)hwnd.Value, Win32.GWLP_USERDATA, 0);
            DestroyWindow(hwnd);
        }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        if (_holdsClass)
        {
            _holdsClass = false;
            HostClass.Release();
        }
        _acceptsFiles = false;
    }

    void IWindowMessageHandler.OnNativeWindowDestroyed(HWND hwnd)
    {
        // Normally our own DestroyWindow; the parent may also have been destroyed without a detach.
        if (_hwnd != hwnd) return;
        _hwnd = HWND.NULL;
        _frameTimerRunning = false;
        CancelInteraction();
        _renderer?.DiscardDeviceResources();
    }

    /// <summary>Requests a repaint (coalesced into one WM_PAINT).</summary>
    public void Invalidate()
    {
        if (_hwnd != HWND.NULL) InvalidateRect(_hwnd, null, BOOL.FALSE);
    }

    private void ResizeWindow()
    {
        if (_hwnd != HWND.NULL)
        {
            SetWindowPos(_hwnd, HWND.NULL, 0, 0, _pixelWidth, _pixelHeight, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOMOVE);
        }
    }

    /// <summary>Gives the Win32 keyboard focus to the host window (text input, standalone apps).</summary>
    public void RequestKeyboardFocus()
    {
        if (_hwnd != HWND.NULL && GetFocus() != _hwnd) SetFocus(_hwnd);
    }

    /// <summary><c>true</c> while the host window has the Win32 keyboard focus.</summary>
    public bool HasKeyboardFocus { get; private set; }

    /// <summary>Raised when the host window gains or loses the Win32 keyboard focus.</summary>
    public event Action<UIHost, bool>? KeyboardFocusChanged;

    private void UpdateFileDropRegistration()
    {
        if (_hwnd == HWND.NULL) return;
        bool wanted = false;
        foreach (var e in _root.DescendantsAndSelf())
        {
            if (e.AllowFileDrop) { wanted = true; break; }
        }
        if (wanted == _acceptsFiles) return;
        _acceptsFiles = wanted;
        DragAcceptFiles(_hwnd, wanted ? BOOL.TRUE : BOOL.FALSE);
    }

    bool IWindowMessageHandler.HandleMessage(HWND hwnd, uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case Win32.WM_ERASEBKGND:
                result = 1; // Direct2D paints every pixel
                return true;

            case Win32.WM_PAINT:
            {
                PAINTSTRUCT paint;
                BeginPaint(hwnd, &paint);
                bool presented = true;
                try
                {
                    UpdateLayout();
                    presented = _renderer?.Render(_paintCallback ??= Paint) ?? true;
                }
                finally
                {
                    EndPaint(hwnd, &paint);
                }
                if (presented)
                {
                    _paintFailures = 0;
                }
                else if (++_paintFailures <= 3)
                {
                    InvalidateRect(hwnd, null, BOOL.FALSE); // device lost: the next frame rebuilds the resources
                }
                else
                {
                    SetTimer(hwnd, PaintRetryTimerId, 250, null); // persistent failure: retry slowly, never spin
                }
                return true;
            }

            case Win32.WM_SIZE:
            {
                int w = Win32.LowUnsigned(lParam), h = Win32.HighUnsigned(lParam);
                if (w > 0 && h > 0 && (w != _pixelWidth || h != _pixelHeight))
                {
                    // The parent resized us directly (some hosts do): follow.
                    _pixelWidth = w;
                    _pixelHeight = h;
                    _renderer?.Resize((uint)w, (uint)h);
                    _renderer?.SetScale(Scale);
                    InvalidateLayout();
                }
                return true;
            }

            case Win32.WM_GETDLGCODE:
                result = Win32.DLGC_WANTALLKEYS | Win32.DLGC_WANTCHARS;
                return true;

            case Win32.WM_MOUSEACTIVATE:
                // Do not steal the activation/focus from the DAW on click (focus is taken only for text input).
                if (!TakeFocusOnClick)
                {
                    result = Win32.MA_NOACTIVATE;
                    return true;
                }
                return false;

            case Win32.WM_SETFOCUS:
                HasKeyboardFocus = true;
                KeyboardFocusChanged?.Invoke(this, true);
                Invalidate();
                return true;

            case Win32.WM_KILLFOCUS:
                HasKeyboardFocus = false;
                KeyboardFocusChanged?.Invoke(this, false);
                Invalidate();
                return true;

            case Win32.WM_MOUSEMOVE:
                OnNativeMouseMove(ToDip(lParam), wParam);
                return true;

            case Win32.WM_MOUSELEAVE:
                _trackingLeave = false;
                OnNativeMouseLeave();
                return true;

            case Win32.WM_LBUTTONDOWN: OnNativeMouseDown(ToDip(lParam), MouseButton.Left, 1); return true;
            case Win32.WM_LBUTTONDBLCLK: OnNativeMouseDown(ToDip(lParam), MouseButton.Left, 2); return true;
            case Win32.WM_RBUTTONDOWN: OnNativeMouseDown(ToDip(lParam), MouseButton.Right, 1); return true;
            case Win32.WM_RBUTTONDBLCLK: OnNativeMouseDown(ToDip(lParam), MouseButton.Right, 2); return true;
            case Win32.WM_MBUTTONDOWN: OnNativeMouseDown(ToDip(lParam), MouseButton.Middle, 1); return true;
            case Win32.WM_MBUTTONDBLCLK: OnNativeMouseDown(ToDip(lParam), MouseButton.Middle, 2); return true;
            case Win32.WM_LBUTTONUP: OnNativeMouseUp(ToDip(lParam), MouseButton.Left); return true;
            case Win32.WM_RBUTTONUP: OnNativeMouseUp(ToDip(lParam), MouseButton.Right); return true;
            case Win32.WM_MBUTTONUP: OnNativeMouseUp(ToDip(lParam), MouseButton.Middle); return true;

            case Win32.WM_CAPTURECHANGED:
                if ((HWND)lParam != hwnd) OnNativeCaptureLost();
                return true;

            case Win32.WM_MOUSEWHEEL:
            case Win32.WM_MOUSEHWHEEL:
            {
                var screen = new POINT { x = Win32.LowSigned(lParam), y = Win32.HighSigned(lParam) };
                ScreenToClient(hwnd, &screen);
                float notches = Win32.HighSigned((nint)wParam) / 120f;
                bool horizontal = msg == Win32.WM_MOUSEHWHEEL; // positive = away from the user / tilt right
                // Unhandled: DefWindowProc forwards the wheel to the parent (the DAW scrolls).
                var offset = ContentOffset;
                return DispatchWheel(new Point(screen.x / Scale - offset.X, screen.y / Scale - offset.Y), notches, horizontal, CurrentModifiers());
            }

            case Win32.WM_SETCURSOR:
                if (Win32.LowUnsigned(lParam) != Win32.HTCLIENT) return false;
                SetCursor(LoadCursorW(HINSTANCE.NULL, (char*)(nint)CursorResource(CurrentCursor())));
                result = 1;
                return true;

            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
            {
                var e = new KeyEventArgs((Key)(int)wParam, CurrentModifiers(), (lParam & (1 << 30)) != 0);
                if (DispatchKeyDown(e)) return true;
                return ForwardToParent(hwnd, msg, wParam, lParam, ref result);
            }

            case Win32.WM_KEYUP:
            case Win32.WM_SYSKEYUP:
            {
                var e = new KeyEventArgs((Key)(int)wParam, CurrentModifiers());
                if (DispatchKeyUp(e)) return true;
                return ForwardToParent(hwnd, msg, wParam, lParam, ref result);
            }

            case Win32.WM_CHAR:
            {
                char c = (char)wParam;
                if (char.IsHighSurrogate(c)) { _pendingHighSurrogate = c; return true; }
                string text = _pendingHighSurrogate != 0 && char.IsLowSurrogate(c) ? new string([_pendingHighSurrogate, c]) : c.ToString();
                _pendingHighSurrogate = '\0';
                if (DispatchTextInput(text)) return true;
                return ForwardToParent(hwnd, msg, wParam, lParam, ref result);
            }

            case Win32.WM_TIMER:
                OnNativeTimer(wParam);
                return true;

            case Win32.WM_APP + 1:
                ProcessPostedActions();
                return true;

            case Win32.WM_DROPFILES:
                OnNativeDropFiles((HDROP)(nint)wParam);
                return true;

            case Win32.WM_DPICHANGED_AFTERPARENT:
                if (FollowWindowDpi)
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi != 0) SetDpiScale(dpi / 96f);
                }
                return true;
        }
        return false;
    }

    /// <summary>
    /// Follows DPI changes of the parent window (WM_DPICHANGED_AFTERPARENT). Plug-in adapters turn this off when the
    /// DAW drives the scale explicitly (VST3 <c>setContentScaleFactor</c>).
    /// </summary>
    public bool FollowWindowDpi { get; set; } = true;

    private const nuint PaintRetryTimerId = 3;
    private int _paintFailures;
    private char _pendingHighSurrogate;
    private Action<DrawingContext>? _paintCallback;

    private static bool ForwardToParent(HWND hwnd, uint msg, nuint wParam, nint lParam, ref nint result)
    {
        // Unused keys go back to the DAW (transport, shortcuts).
        HWND parent = GetParent(hwnd);
        if (parent == HWND.NULL) return false;
        result = SendMessageW(parent, msg, wParam, lParam);
        return true;
    }

    private Point ToDip(nint lParam)
    {
        float s = Scale;
        var offset = ContentOffset;
        return new Point(Win32.LowSigned(lParam) / s - offset.X, Win32.HighSigned(lParam) / s - offset.Y);
    }

    /// <summary>Offset of the design inside the window in <see cref="SizingMode.ScaleToFit"/> with letterboxing (DIPs).</summary>
    internal Point ContentOffset => Point.Zero;

    private static ModifierKeys CurrentModifiers()
    {
        var m = ModifierKeys.None;
        if (GetKeyState(Win32.VK_SHIFT) < 0) m |= ModifierKeys.Shift;
        if (GetKeyState(Win32.VK_CONTROL) < 0) m |= ModifierKeys.Control;
        if (GetKeyState(Win32.VK_MENU) < 0) m |= ModifierKeys.Alt;
        return m;
    }

    private static int CursorResource(StandardCursor cursor) => cursor switch
    {
        StandardCursor.Hand => Win32.IDC_HAND,
        StandardCursor.IBeam => Win32.IDC_IBEAM,
        StandardCursor.SizeNS => Win32.IDC_SIZENS,
        StandardCursor.SizeWE => Win32.IDC_SIZEWE,
        StandardCursor.SizeAll => Win32.IDC_SIZEALL,
        StandardCursor.SizeNWSE => Win32.IDC_SIZENWSE,
        StandardCursor.SizeNESW => Win32.IDC_SIZENESW,
        StandardCursor.Cross => Win32.IDC_CROSS,
        StandardCursor.No => Win32.IDC_NO,
        StandardCursor.Wait => Win32.IDC_WAIT,
        _ => Win32.IDC_ARROW,
    };

    private void OnNativeDropFiles(HDROP drop)
    {
        try
        {
            POINT pt;
            DragQueryPoint(drop, &pt);
            uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
            var files = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFileW(drop, i, null, 0);
                var buffer = new char[length + 1];
                fixed (char* p = buffer)
                {
                    DragQueryFileW(drop, i, p, length + 1);
                }
                files.Add(new string(buffer, 0, (int)length));
            }
            float s = Scale;
            DispatchFilesDropped(new Point(pt.x / s, pt.y / s), files);
        }
        finally
        {
            DragFinish(drop);
        }
    }

    /// <summary>Converts a point in host DIPs to screen pixels (native menus, external windows).</summary>
    public (int X, int Y) DipToScreen(Point point)
    {
        var pt = new POINT { x = (int)MathF.Round(point.X * Scale), y = (int)MathF.Round(point.Y * Scale) };
        if (_hwnd != HWND.NULL) ClientToScreen(_hwnd, &pt);
        return (pt.x, pt.y);
    }
}
