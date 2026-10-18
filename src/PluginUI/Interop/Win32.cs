// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace PluginUI.Interop;

/// <summary>
/// Win32 constants (WinUser.h, WinBase.h...) and the few entry points not taken from TerraFX.
/// </summary>
internal static partial class Win32
{
    // ---- Window messages ----
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_NCCREATE = 0x0081;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_GETDLGCODE = 0x0087;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_RBUTTONDBLCLK = 0x0206;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MBUTTONDBLCLK = 0x0209;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_MOUSEHWHEEL = 0x020E;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_DROPFILES = 0x0233;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_DPICHANGED_AFTERPARENT = 0x02E3;
    public const uint WM_APP = 0x8000;

    // ---- Window styles ----
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_TABSTOP = 0x00010000;
    public const uint WS_EX_ACCEPTFILES = 0x00000010;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);

    // ---- Class styles ----
    public const uint CS_DBLCLKS = 0x0008;
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;

    // ---- SetWindowPos / ShowWindow ----
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;

    // ---- Mouse/keyboard ----
    public const nuint MK_LBUTTON = 0x0001;
    public const nuint MK_RBUTTON = 0x0002;
    public const nuint MK_SHIFT = 0x0004;
    public const nuint MK_CONTROL = 0x0008;
    public const nuint MK_MBUTTON = 0x0010;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const uint TME_LEAVE = 0x00000002;
    public const int HTCLIENT = 1;
    public const int MA_NOACTIVATE = 3;
    public const int DLGC_WANTALLKEYS = 0x0004;
    public const int DLGC_WANTCHARS = 0x0080;

    // ---- Cursors (MAKEINTRESOURCE) ----
    public const int IDC_ARROW = 32512;
    public const int IDC_IBEAM = 32513;
    public const int IDC_WAIT = 32514;
    public const int IDC_CROSS = 32515;
    public const int IDC_SIZENWSE = 32642;
    public const int IDC_SIZENESW = 32643;
    public const int IDC_SIZEWE = 32644;
    public const int IDC_SIZENS = 32645;
    public const int IDC_SIZEALL = 32646;
    public const int IDC_NO = 32648;
    public const int IDC_HAND = 32649;

    // ---- Misc ----
    public const uint GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x00000002;
    public const uint GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;
    public const int GWLP_USERDATA = -21;
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint MF_GRAYED = 0x0001;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_POPUP = 0x0010;

    /// <summary>Low-order signed 16-bit word (GET_X_LPARAM).</summary>
    public static int LowSigned(nint value) => (short)(value & 0xFFFF);

    /// <summary>High-order signed 16-bit word (GET_Y_LPARAM, GET_WHEEL_DELTA_WPARAM).</summary>
    public static int HighSigned(nint value) => (short)((value >> 16) & 0xFFFF);

    /// <summary>Low-order unsigned 16-bit word.</summary>
    public static int LowUnsigned(nint value) => (int)(value & 0xFFFF);

    /// <summary>High-order unsigned 16-bit word.</summary>
    public static int HighUnsigned(nint value) => (int)((value >> 16) & 0xFFFF);

    /// <summary>SetWindowLongPtrW (64-bit) or SetWindowLongW (32-bit, where the Ptr variant is a macro).</summary>
    public static nint SetWindowLongPtr(nint hwnd, int index, nint newValue)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, newValue) : SetWindowLong32(hwnd, index, (int)newValue);

    /// <summary>GetWindowLongPtrW (64-bit) or GetWindowLongW (32-bit).</summary>
    public static nint GetWindowLongPtr(nint hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);

    /// <summary>Language identifier (LANGID) of the Windows display language of the current user.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial ushort GetUserDefaultUILanguage();

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr64(nint hwnd, int index, nint newValue);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr64(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong32(nint hwnd, int index, int newValue);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(nint hwnd, int index);
}
