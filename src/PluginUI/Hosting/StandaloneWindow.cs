// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>
/// Top-level window hosting a <see cref="UIHost"/>: standalone versions of plug-ins, test benches, the gallery.
/// Handles per-monitor DPI, resizing constraints and the message loop.
/// </summary>
/// <example>
/// <code>
/// var host = new UIHost(root, new Size(640, 400), SizingMode.ScaleToFit);
/// return new StandaloneWindow(host, "My plug-in").Run();
/// </code>
/// </example>
public sealed unsafe class StandaloneWindow : IWindowMessageHandler, IDisposable
{
    private static readonly WindowClass FrameClass = new("PluginUI.Frame", Win32.CS_HREDRAW | Win32.CS_VREDRAW);
    private const uint SIZE_MINIMIZED = 1;

    private HWND _hwnd;
    private GCHandle _selfHandle;
    private bool _holdsClass;
    private bool _inDpiChange;
    private string _title;
    private uint _style;

    /// <summary>Creates the window object (the native window is created by <see cref="Show"/> or <see cref="Run"/>).</summary>
    public StandaloneWindow(UIHost host, string title)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        _title = title ?? string.Empty;
        host.TakeFocusOnClick = true;
    }

    /// <summary>Hosted UI.</summary>
    public UIHost Host { get; }

    /// <summary>Posts WM_QUIT when the window is destroyed (ends <see cref="Run"/>).</summary>
    public bool QuitOnClose { get; set; } = true;

    /// <summary>Raised when the user asks to close the window; set the argument's Cancel to keep it open.</summary>
    public event Action<StandaloneWindow, System.ComponentModel.CancelEventArgs>? Closing;

    /// <summary>Raised after the window was destroyed.</summary>
    public event Action<StandaloneWindow>? Closed;

    /// <summary>Native handle (0 before <see cref="Show"/>).</summary>
    public nint WindowHandle => (nint)_hwnd.Value;

    /// <summary>Window caption.</summary>
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            if (_hwnd != HWND.NULL) fixed (char* p = _title) SetWindowTextW(_hwnd, p);
        }
    }

    /// <summary>Opts the process into per-monitor DPI awareness (v2). Call before creating any window; harmless if already set by a manifest.</summary>
    public static void EnablePerMonitorDpiAwareness()
    {
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch (EntryPointNotFoundException)
        {
            // Windows older than 10 1703: system DPI only.
        }
    }

    /// <summary>Creates and shows the window.</summary>
    public void Show()
    {
        if (_hwnd != HWND.NULL)
        {
            ShowWindow(_hwnd, Win32.SW_SHOW);
            return;
        }
        FrameClass.AddRef();
        _holdsClass = true;
        _selfHandle = GCHandle.Alloc(this);
        _style = Host.IsResizable
            ? Win32.WS_OVERLAPPEDWINDOW | Win32.WS_CLIPCHILDREN
            : Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX | Win32.WS_CLIPCHILDREN;
        var (w, h) = Host.PreferredPixelSize;
        _hwnd = FrameClass.Create(_selfHandle, _style, 0, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, w, h, HWND.NULL, _title);

        // Now that the window lives on a monitor, apply its DPI and size the client area exactly.
        uint dpi = GetDpiForWindow(_hwnd);
        Host.ResizeRequestHandler = OnHostResizeRequest;
        Host.FollowWindowDpi = false; // the frame drives the scale through WM_DPICHANGED
        if (dpi != 0) Host.SetDpiScale(dpi / 96f);
        var (cw, ch) = Host.PreferredPixelSize;
        SetClientSize(cw, ch);
        Host.SetPixelSize(cw, ch);
        Host.AttachToParent((nint)_hwnd.Value, detectDpi: false);
        ShowWindow(_hwnd, Win32.SW_SHOW);
        Host.RequestKeyboardFocus();
    }

    /// <summary>Shows the window and runs the message loop until it closes. Returns the exit code.</summary>
    public int Run()
    {
        Show();
        MSG msg;
        while (GetMessageW(&msg, HWND.NULL, 0, 0).Value > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return (int)msg.wParam;
    }

    /// <summary>Closes the window.</summary>
    public void Close()
    {
        if (_hwnd != HWND.NULL) DestroyWindow(_hwnd);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
        Cleanup();
    }

    private void Cleanup()
    {
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        if (_holdsClass)
        {
            _holdsClass = false;
            FrameClass.Release();
        }
    }

    private bool OnHostResizeRequest(int pixelWidth, int pixelHeight)
    {
        if (_hwnd == HWND.NULL || _inDpiChange) return true;
        SetClientSize(pixelWidth, pixelHeight); // WM_SIZE then calls Host.SetPixelSize
        return true;
    }

    private void SetClientSize(int width, int height)
    {
        var rect = new RECT { left = 0, top = 0, right = width, bottom = height };
        uint dpi = GetDpiForWindow(_hwnd);
        AdjustWindowRectExForDpi(&rect, _style, BOOL.FALSE, 0, dpi == 0 ? 96u : dpi);
        SetWindowPos(_hwnd, HWND.NULL, 0, 0, rect.right - rect.left, rect.bottom - rect.top, Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    private (int W, int H) FrameExtra()
    {
        var rect = new RECT();
        uint dpi = GetDpiForWindow(_hwnd);
        AdjustWindowRectExForDpi(&rect, _style, BOOL.FALSE, 0, dpi == 0 ? 96u : dpi);
        return (rect.right - rect.left, rect.bottom - rect.top);
    }

    void IWindowMessageHandler.OnNativeWindowDestroyed(HWND hwnd)
    {
        if (_hwnd != hwnd) return;
        _hwnd = HWND.NULL;
        Cleanup();
    }

    bool IWindowMessageHandler.HandleMessage(HWND hwnd, uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case Win32.WM_ERASEBKGND:
                result = 1;
                return true;

            case Win32.WM_SIZE:
                if (wParam != SIZE_MINIMIZED)
                {
                    int w = Win32.LowUnsigned(lParam), h = Win32.HighUnsigned(lParam);
                    if (w > 0 && h > 0) Host.SetPixelSize(w, h);
                }
                return true;

            case Win32.WM_SETFOCUS:
                Host.RequestKeyboardFocus();
                return true;

            case Win32.WM_GETMINMAXINFO:
            {
                if (!Host.IsResizable) return false;
                var info = (MINMAXINFO*)lParam;
                var (ew, eh) = FrameExtra();
                float k = Host.DpiScale * (Host.Sizing == SizingMode.Relayout ? Host.Zoom : 1f);
                info->ptMinTrackSize.x = (int)(Host.MinSize.Width * k) + ew;
                info->ptMinTrackSize.y = (int)(Host.MinSize.Height * k) + eh;
                if (float.IsFinite(Host.MaxSize.Width)) info->ptMaxTrackSize.x = (int)(Host.MaxSize.Width * k) + ew;
                if (float.IsFinite(Host.MaxSize.Height)) info->ptMaxTrackSize.y = (int)(Host.MaxSize.Height * k) + eh;
                return true;
            }

            case 0x0214: // WM_SIZING: keep the aspect ratio / constraints while dragging the frame
            {
                if (!Host.IsResizable) return false;
                var rect = (RECT*)lParam;
                var (ew, eh) = FrameExtra();
                int w = rect->right - rect->left - ew, h = rect->bottom - rect->top - eh;
                int cw = w, ch = h;
                Host.ConstrainPixelSize(ref cw, ref ch);
                int edge = (int)wParam; // WMSZ_LEFT=1 RIGHT=2 TOP=3 TOPLEFT=4 TOPRIGHT=5 BOTTOM=6 BOTTOMLEFT=7 BOTTOMRIGHT=8
                if (edge is 1 or 4 or 7) rect->left = rect->right - cw - ew; else rect->right = rect->left + cw + ew;
                if (edge is 3 or 4 or 5) rect->top = rect->bottom - ch - eh; else rect->bottom = rect->top + ch + eh;
                result = 1;
                return true;
            }

            case Win32.WM_DPICHANGED:
            {
                float scale = Win32.LowUnsigned((nint)wParam) / 96f;
                var suggested = (RECT*)lParam;
                _inDpiChange = true;
                try
                {
                    Host.SetDpiScale(scale);
                }
                finally
                {
                    _inDpiChange = false;
                }
                SetWindowPos(hwnd, HWND.NULL, suggested->left, suggested->top, suggested->right - suggested->left, suggested->bottom - suggested->top, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                return true;
            }

            case Win32.WM_CLOSE:
            {
                var args = new System.ComponentModel.CancelEventArgs();
                Closing?.Invoke(this, args);
                if (!args.Cancel) DestroyWindow(hwnd);
                return true;
            }

            case Win32.WM_DESTROY:
                Host.Detach();
                Closed?.Invoke(this);
                if (QuitOnClose) PostQuitMessage(0);
                return true;
        }
        return false;
    }
}
