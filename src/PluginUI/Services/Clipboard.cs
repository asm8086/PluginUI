// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>Unicode text clipboard (UI thread).</summary>
public static unsafe class Clipboard
{
    /// <summary>Replaces the clipboard content with <paramref name="text"/>. Returns <c>false</c> if the clipboard is busy.</summary>
    public static bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // EmptyClipboard with a NULL owner makes SetClipboardData fail: own it through a message-only window.
        HWND owner = CreateOwnerWindow();
        if (!Open(owner))
        {
            if (owner != HWND.NULL) DestroyWindow(owner);
            return false;
        }
        try
        {
            EmptyClipboard();
            nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
            HGLOBAL memory = GlobalAlloc(Win32.GMEM_MOVEABLE, bytes);
            if (memory == HGLOBAL.NULL) return false;
            char* target = (char*)GlobalLock(memory);
            if (target is null)
            {
                GlobalFree(memory);
                return false;
            }
            fixed (char* source = text)
            {
                Buffer.MemoryCopy(source, target, (long)bytes, text.Length * sizeof(char));
            }
            target[text.Length] = '\0';
            GlobalUnlock(memory);
            if (SetClipboardData(Win32.CF_UNICODETEXT, (HANDLE)memory.Value) == HANDLE.NULL)
            {
                GlobalFree(memory);
                return false;
            }
            return true; // the system owns the memory now
        }
        finally
        {
            CloseClipboard();
            if (owner != HWND.NULL) DestroyWindow(owner);
        }
    }

    private static HWND CreateOwnerWindow()
    {
        fixed (char* className = "STATIC")
        {
            // HWND_MESSAGE = -3: invisible message-only window.
            return CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, (HWND)(nint)(-3), HMENU.NULL, HINSTANCE.NULL, null);
        }
    }

    /// <summary>Clipboard text, or null if there is none.</summary>
    public static string? GetText()
    {
        if (IsClipboardFormatAvailable(Win32.CF_UNICODETEXT) == BOOL.FALSE || !Open(HWND.NULL)) return null;
        try
        {
            HANDLE data = GetClipboardData(Win32.CF_UNICODETEXT);
            if (data == HANDLE.NULL) return null;
            var memory = (HGLOBAL)data.Value;
            char* text = (char*)GlobalLock(memory);
            if (text is null) return null;
            try
            {
                return new string(text);
            }
            finally
            {
                GlobalUnlock(memory);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary><c>true</c> if the clipboard holds text.</summary>
    public static bool ContainsText => IsClipboardFormatAvailable(Win32.CF_UNICODETEXT) != BOOL.FALSE;

    private static bool Open(HWND owner)
    {
        // Another process may hold the clipboard briefly.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(owner) != BOOL.FALSE) return true;
            Thread.Sleep(10);
        }
        return false;
    }
}
