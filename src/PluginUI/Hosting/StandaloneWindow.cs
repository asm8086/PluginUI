// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>Where a <see cref="StandaloneWindow"/> first appears.</summary>
public enum WindowStartPosition
{
    /// <summary>Chosen by Windows (cascade), or centred on the owner for a window with an <see cref="StandaloneWindow.Owner"/>.</summary>
    Default,
    /// <summary>Centred on the owner window (on the screen without owner).</summary>
    CenterOwner,
    /// <summary>Centred on the work area of the monitor under the mouse.</summary>
    CenterScreen,
}

/// <summary>
/// Saved position and size of a window, in physical pixels of the virtual screen (the "restored" bounds, also known
/// while the window is maximized). Store it in the application settings and give it back to
/// <see cref="StandaloneWindow.Placement"/> before <see cref="StandaloneWindow.Show"/>.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Outer width.</param>
/// <param name="Height">Outer height.</param>
/// <param name="IsMaximized">The window was maximized.</param>
public readonly record struct WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized = false);

/// <summary>
/// Top-level window hosting a <see cref="UIHost"/>: desktop applications, standalone versions of plug-ins, test
/// benches, the gallery. Handles per-monitor DPI, resizing constraints, the message loop, modal dialogs
/// (<see cref="ShowDialog"/>), the dark title bar, the application icon and the saved placement.
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
    private bool _modal;
    private bool _modalDone;
    private HWND _disabledOwner;
    private HICON _bigIcon, _smallIcon;
    private bool _ownsIcons;
    private WindowPlacement? _placement;

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

    /// <summary>Owner window: this window stays above it and is centred on it (set before <see cref="Show"/>).</summary>
    public StandaloneWindow? Owner { get; set; }

    /// <summary>First position (before <see cref="Show"/>; ignored when <see cref="Placement"/> is restored).</summary>
    public WindowStartPosition StartPosition { get; set; }

    /// <summary>Shows the minimize box (default <c>true</c>; dialogs usually hide it).</summary>
    public bool MinimizeBox { get; set; } = true;

    /// <summary>Shows the maximize box of a resizable window (default <c>true</c>).</summary>
    public bool MaximizeBox { get; set; } = true;

    /// <summary>
    /// Dark title bar (Windows 10 20H1 and later): <c>null</c> (default) follows the theme of the host, dark when its
    /// background is dark; <c>true</c> / <c>false</c> force it.
    /// </summary>
    public bool? DarkTitleBar { get; set; }

    /// <summary>
    /// Icon of the title bar and the taskbar: a path to an .ico file, or null (default) for the icon of the executable
    /// (<c>&lt;ApplicationIcon&gt;</c> of the project), then the Windows default.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>Result of a modal window, set before closing it (<see cref="ShowDialog"/> returns it).</summary>
    public int DialogResult { get; set; }

    /// <summary><c>true</c> while <see cref="ShowDialog"/> runs.</summary>
    public bool IsModal => _modal;

    /// <summary>
    /// Position and size: the current (restored) bounds once shown, the bounds to restore before. A saved placement
    /// that no longer lies on a monitor (screen removed) is ignored.
    /// </summary>
    public WindowPlacement? Placement
    {
        get
        {
            if (_hwnd == HWND.NULL) return _placement;
            var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            if (GetWindowPlacement(_hwnd, &wp) == BOOL.FALSE) return _placement;
            var r = wp.rcNormalPosition;
            // rcNormalPosition is in work-area coordinates: convert to screen coordinates.
            var offset = WorkAreaOffset(r);
            return new WindowPlacement(r.left + offset.x, r.top + offset.y, r.right - r.left, r.bottom - r.top, wp.showCmd == Win32.SW_SHOWMAXIMIZED || (wp.showCmd == Win32.SW_SHOWMINIMIZED && (wp.flags & Win32.WPF_RESTORETOMAXIMIZED) != 0));
        }
        set
        {
            _placement = value;
            if (_hwnd != HWND.NULL && value is { } v) ApplyPlacement(v, show: false);
        }
    }

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
        catch (DllNotFoundException)
        {
            // Not Windows (unit tests of application logic): nothing to do.
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
        _style = Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU | Win32.WS_CLIPCHILDREN;
        if (Host.IsResizable) _style |= Win32.WS_THICKFRAME | (MaximizeBox ? Win32.WS_MAXIMIZEBOX : 0);
        if (MinimizeBox) _style |= Win32.WS_MINIMIZEBOX;
        var (w, h) = Host.PreferredPixelSize;
        var owner = Owner is { WindowHandle: not 0 } o ? (HWND)o.WindowHandle : HWND.NULL;
        _hwnd = FrameClass.Create(_selfHandle, _style, 0, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, w, h, owner, _title);
        ApplyIcon();
        ApplyTitleBarTheme();

        // Now that the window lives on a monitor, apply its DPI and size the client area exactly.
        uint dpi = GetDpiForWindow(_hwnd);
        Host.ResizeRequestHandler = OnHostResizeRequest;
        Host.FollowWindowDpi = false; // the frame drives the scale through WM_DPICHANGED
        if (dpi != 0) Host.SetDpiScale(dpi / 96f);
        var (cw, ch) = Host.PreferredPixelSize;
        SetClientSize(cw, ch);
        Host.SetPixelSize(cw, ch);
        Host.AttachToParent((nint)_hwnd.Value, detectDpi: false);
        if (_placement is { } saved && IsOnAMonitor(saved)) ApplyPlacement(saved, show: true);
        else
        {
            PlaceAtStart(owner);
            ShowWindow(_hwnd, Win32.SW_SHOW);
        }
        Host.RequestKeyboardFocus();
    }

    /// <summary>Brings the window to the foreground.</summary>
    public void Activate()
    {
        if (_hwnd == HWND.NULL) return;
        if (IsIconic(_hwnd) != BOOL.FALSE) ShowWindow(_hwnd, Win32.SW_RESTORE);
        SetForegroundWindow(_hwnd);
    }

    /// <summary>
    /// Shows the window as a modal dialog of <paramref name="owner"/>: the owner is disabled and a nested message loop
    /// runs until this window closes. Returns <see cref="DialogResult"/>.
    /// </summary>
    /// <param name="owner">Owner window (null: the dialog has no owner, nothing is disabled).</param>
    /// <exception cref="InvalidOperationException">The window is already shown.</exception>
    public int ShowDialog(StandaloneWindow? owner)
    {
        if (_hwnd != HWND.NULL) throw new InvalidOperationException("The window is already shown.");
        Owner = owner;
        QuitOnClose = false;
        if (StartPosition == WindowStartPosition.Default) StartPosition = WindowStartPosition.CenterOwner;
        _modal = true;
        _modalDone = false;
        try
        {
            Show();
            if (owner is { WindowHandle: not 0 } o && IsWindowEnabled((HWND)o.WindowHandle) != BOOL.FALSE)
            {
                _disabledOwner = (HWND)o.WindowHandle;
                EnableWindow(_disabledOwner, BOOL.FALSE);
            }
            // WM_QUIT while the dialog is open: close it (the quit message is posted again for the outer loop).
            if (!RunMessageLoopUntil(() => _modalDone)) Close();
        }
        finally
        {
            ReenableOwner();
            _modal = false;
        }
        return DialogResult;
    }

    /// <summary>
    /// Runs a nested message loop until <paramref name="done"/> returns <c>true</c> (checked after each message): for
    /// synchronous questions shown inside a window, such as <see cref="Dialog"/> popups.
    /// </summary>
    /// <returns><c>false</c> if WM_QUIT arrived first; it is posted again so that the outer loop ends too.</returns>
    public static bool RunMessageLoopUntil(Func<bool> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        MSG msg;
        while (!done())
        {
            int got = GetMessageW(&msg, HWND.NULL, 0, 0).Value;
            if (got == 0)
            {
                PostQuitMessage((int)msg.wParam);
                return false;
            }
            if (got < 0) return false;
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return true;
    }

    /// <summary>Closes the window with a dialog result.</summary>
    public void Close(int dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    /// <summary>Applies <see cref="DarkTitleBar"/> again (call after changing the host theme).</summary>
    public void UpdateTitleBarTheme() => ApplyTitleBarTheme();

    private void ReenableOwner()
    {
        if (_disabledOwner == HWND.NULL) return;
        EnableWindow(_disabledOwner, BOOL.TRUE);
        _disabledOwner = HWND.NULL;
    }

    private void ApplyTitleBarTheme()
    {
        if (_hwnd == HWND.NULL) return;
        var bg = Host.EffectiveTheme.Palette.Background;
        bool dark = DarkTitleBar ?? (0.2126f * bg.R + 0.7152f * bg.G + 0.0722f * bg.B) < 0.45f;
        BOOL value = dark ? BOOL.TRUE : BOOL.FALSE;
        try
        {
            // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 since Windows 10 20H1, 19 on 1809–1909.
            if (DwmSetWindowAttribute(_hwnd, 20, &value, (uint)sizeof(BOOL)).FAILED) DwmSetWindowAttribute(_hwnd, 19, &value, (uint)sizeof(BOOL));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No DWM (some test environments): keep the default title bar.
        }
    }

    private void ApplyIcon()
    {
        uint dpi = GetDpiForWindow(_hwnd);
        if (dpi == 0) dpi = 96;
        int big = GetSystemMetricsForDpi(Win32.SM_CXICON, dpi), small = GetSystemMetricsForDpi(Win32.SM_CXSMICON, dpi);
        if (IconPath is { Length: > 0 } path && File.Exists(path))
        {
            fixed (char* p = path)
            {
                _bigIcon = (HICON)LoadImageW(HINSTANCE.NULL, p, Win32.IMAGE_ICON, big, big, Win32.LR_LOADFROMFILE);
                _smallIcon = (HICON)LoadImageW(HINSTANCE.NULL, p, Win32.IMAGE_ICON, small, small, Win32.LR_LOADFROMFILE);
            }
            _ownsIcons = true;
        }
        else
        {
            // The SDK embeds <ApplicationIcon> as icon group 32512 in the executable (IL apphost and NativeAOT).
            var module = GetModuleHandleW(null);
            _bigIcon = (HICON)LoadImageW(module, (char*)32512, Win32.IMAGE_ICON, big, big, 0);
            _smallIcon = (HICON)LoadImageW(module, (char*)32512, Win32.IMAGE_ICON, small, small, 0);
            _ownsIcons = true;
        }
        if (_bigIcon == HICON.NULL) _bigIcon = LoadIconW(HINSTANCE.NULL, (char*)32512); // IDI_APPLICATION
        if (_smallIcon == HICON.NULL) _smallIcon = _bigIcon;
        SendMessageW(_hwnd, Win32.WM_SETICON, Win32.ICON_BIG, (nint)_bigIcon.Value);
        SendMessageW(_hwnd, Win32.WM_SETICON, Win32.ICON_SMALL, (nint)_smallIcon.Value);
    }

    private void DestroyIcons()
    {
        if (!_ownsIcons) return;
        _ownsIcons = false;
        if (_smallIcon != HICON.NULL && _smallIcon != _bigIcon) DestroyIcon(_smallIcon);
        if (_bigIcon != HICON.NULL) DestroyIcon(_bigIcon);
        _bigIcon = _smallIcon = HICON.NULL;
    }

    private void PlaceAtStart(HWND owner)
    {
        var position = StartPosition;
        if (position == WindowStartPosition.Default)
        {
            if (owner == HWND.NULL) return;
            position = WindowStartPosition.CenterOwner;
        }
        RECT window;
        GetWindowRect(_hwnd, &window);
        int w = window.right - window.left, h = window.bottom - window.top;
        RECT target;
        HMONITOR monitor;
        if (position == WindowStartPosition.CenterOwner && owner != HWND.NULL && IsIconic(owner) == BOOL.FALSE)
        {
            GetWindowRect(owner, &target);
            monitor = MonitorFromWindow(owner, Win32.MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            POINT cursor;
            GetCursorPos(&cursor);
            monitor = MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
            target = WorkArea(monitor);
        }
        int x = target.left + (target.right - target.left - w) / 2, y = target.top + (target.bottom - target.top - h) / 2;
        // Keep the title bar on the work area.
        var work = WorkArea(monitor);
        x = Math.Clamp(x, work.left, Math.Max(work.left, work.right - w));
        y = Math.Clamp(y, work.top, Math.Max(work.top, work.bottom - h));
        SetWindowPos(_hwnd, HWND.NULL, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    private void ApplyPlacement(WindowPlacement v, bool show)
    {
        var r = new RECT { left = v.X, top = v.Y, right = v.X + Math.Max(1, v.Width), bottom = v.Y + Math.Max(1, v.Height) };
        var offset = WorkAreaOffset(r);
        var wp = new WINDOWPLACEMENT
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            showCmd = (uint)(!show && IsWindowVisible(_hwnd) == BOOL.FALSE ? Win32.SW_HIDE : v.IsMaximized && Host.IsResizable ? Win32.SW_SHOWMAXIMIZED : Win32.SW_SHOWNORMAL),
            rcNormalPosition = new RECT { left = r.left - offset.x, top = r.top - offset.y, right = r.right - offset.x, bottom = r.bottom - offset.y },
        };
        SetWindowPlacement(_hwnd, &wp);
    }

    private static bool IsOnAMonitor(WindowPlacement v)
    {
        // At least the top-left part of the title bar must be visible on some monitor.
        var probe = new RECT { left = v.X + 8, top = v.Y + 4, right = v.X + Math.Min(v.Width, 120), bottom = v.Y + 28 };
        return v.Width > 0 && v.Height > 0 && MonitorFromRect(&probe, Win32.MONITOR_DEFAULTTONULL) != HMONITOR.NULL;
    }

    private static RECT WorkArea(HMONITOR monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor == HMONITOR.NULL || GetMonitorInfoW(monitor, &info) == BOOL.FALSE) return new RECT { right = 1280, bottom = 800 };
        return info.rcWork;
    }

    // WINDOWPLACEMENT uses work-area coordinates (they differ from screen coordinates when the taskbar is on the left or top).
    private static POINT WorkAreaOffset(RECT r)
    {
        var monitor = MonitorFromRect(&r, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor == HMONITOR.NULL || GetMonitorInfoW(monitor, &info) == BOOL.FALSE) return default;
        return new POINT { x = info.rcWork.left - info.rcMonitor.left, y = info.rcWork.top - info.rcMonitor.top };
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

    /// <summary>Asks the window to close as the close button does: <see cref="Closing"/> handlers can cancel.</summary>
    public void RequestClose()
    {
        if (_hwnd != HWND.NULL) PostMessageW(_hwnd, Win32.WM_CLOSE, 0, 0);
    }

    /// <summary>Closes the window (no <see cref="Closing"/> event).</summary>
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
        _modalDone = true;
        DestroyIcons();
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
                if (args.Cancel) return true;
                // Re-enable the owner first, or Windows activates another application when the dialog disappears.
                ReenableOwner();
                DestroyWindow(hwnd);
                return true;
            }

            case Win32.WM_DESTROY:
                ReenableOwner();
                _placement = Placement;
                Host.Detach();
                Closed?.Invoke(this);
                if (QuitOnClose) PostQuitMessage(0);
                return true;
        }
        return false;
    }
}
