// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Interop;

/// <summary>Receives the messages of a window created through <see cref="WindowClass"/>.</summary>
internal interface IWindowMessageHandler
{
    /// <summary>Handles a message; returns <c>false</c> to fall back to <c>DefWindowProcW</c>.</summary>
    bool HandleMessage(HWND hwnd, uint msg, nuint wParam, nint lParam, out nint result);

    /// <summary>WM_NCDESTROY: the window is gone (possibly destroyed by its parent).</summary>
    void OnNativeWindowDestroyed(HWND hwnd);
}

/// <summary>
/// A reference-counted Win32 window class with a unique name per module load, and the single
/// <c>[UnmanagedCallersOnly]</c> window procedure that routes messages to an <see cref="IWindowMessageHandler"/>
/// through a <see cref="GCHandle"/> stored in <c>GWLP_USERDATA</c>.
/// </summary>
/// <remarks>
/// A unique name guarantees that a class left registered by a previous load of the plug-in (hosts may unload
/// and reload modules) never collides. A class registered by a DLL is not unregistered when the DLL unloads,
/// hence the explicit reference counting.
/// </remarks>
internal sealed unsafe class WindowClass
{
    private readonly Lock _lock = new();
    private readonly uint _style;
    private int _refCount;

    public WindowClass(string prefix, uint style)
    {
        Name = prefix + "." + Guid.NewGuid().ToString("N");
        _style = style;
    }

    /// <summary>Registered class name.</summary>
    public string Name { get; }

    /// <summary>Module instance that owns the class.</summary>
    public HINSTANCE Instance { get; private set; }

    /// <summary>Registers the class on first use.</summary>
    public void AddRef()
    {
        lock (_lock)
        {
            if (_refCount++ > 0) return;

            delegate* unmanaged<HWND, uint, WPARAM, LPARAM, LRESULT> proc = &WndProc;

            // NativeAOT: the window procedure lives in our module. JIT (debug proxy, standalone): host module.
            HMODULE module;
            if (GetModuleHandleExW(Win32.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | Win32.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (char*)proc, &module) == BOOL.FALSE)
            {
                module = GetModuleHandleW(null);
            }
            Instance = (HINSTANCE)module.Value;

            fixed (char* name = Name)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    style = _style,
                    lpfnWndProc = proc,
                    hInstance = Instance,
                    hCursor = LoadCursorW(HINSTANCE.NULL, (char*)(nint)Win32.IDC_ARROW),
                    lpszClassName = name,
                };
                if (RegisterClassExW(&windowClass) == 0)
                {
                    int error = Marshal.GetLastSystemError();
                    _refCount--;
                    throw new InvalidOperationException($"RegisterClassExW failed (error {error}).");
                }
            }
        }
    }

    /// <summary>Unregisters the class when the last user releases it.</summary>
    public void Release()
    {
        lock (_lock)
        {
            if (_refCount == 0 || --_refCount > 0) return;
            fixed (char* name = Name)
            {
                UnregisterClassW(name, Instance);
            }
        }
    }

    /// <summary>Creates a window of this class bound to <paramref name="handler"/> (handle kept alive by the caller).</summary>
    public HWND Create(GCHandle handler, uint style, uint exStyle, int x, int y, int width, int height, HWND parent, string? title = null)
    {
        HWND hwnd;
        fixed (char* name = Name)
        fixed (char* text = title)
        {
            hwnd = CreateWindowExW(exStyle, name, text, style, x, y, width, height, parent, HMENU.NULL, Instance, (void*)GCHandle.ToIntPtr(handler));
        }
        if (hwnd == HWND.NULL)
        {
            throw new InvalidOperationException($"CreateWindowExW failed (error {Marshal.GetLastSystemError()}).");
        }
        return hwnd;
    }

    [UnmanagedCallersOnly]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (msg == Win32.WM_NCCREATE)
            {
                var createStruct = (CREATESTRUCTW*)(nint)lParam;
                Win32.SetWindowLongPtr((nint)hwnd.Value, Win32.GWLP_USERDATA, (nint)createStruct->lpCreateParams);
            }

            nint handle = Win32.GetWindowLongPtr((nint)hwnd.Value, Win32.GWLP_USERDATA);
            if (handle != 0 && GCHandle.FromIntPtr(handle).Target is IWindowMessageHandler handler)
            {
                if (msg == Win32.WM_NCDESTROY)
                {
                    Win32.SetWindowLongPtr((nint)hwnd.Value, Win32.GWLP_USERDATA, 0);
                    handler.OnNativeWindowDestroyed(hwnd);
                }
                else if (handler.HandleMessage(hwnd, msg, (nuint)wParam, (nint)lParam, out nint result))
                {
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            // An exception crossing an [UnmanagedCallersOnly] boundary terminates the process (the DAW!): swallow it.
            UIHost.ReportException(ex, $"WndProc (msg 0x{msg:X4})");
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
