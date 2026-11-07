// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host;

/// <summary>
/// Editor window of a plug-in, whatever its format, attached to a window of the host. Sizes are in physical pixels.
/// Created by <see cref="PluginInstance.CreateEditor"/>; dispose it before the plug-in.
/// </summary>
public abstract class PluginEditor : IDisposable
{
    /// <summary>The plug-in asked for a new size (width, height); return <c>true</c> after resizing the parent window.</summary>
    public Func<int, int, bool>? ResizeRequested { get; set; }

    /// <summary>Size the editor wants.</summary>
    public abstract (int Width, int Height) Size { get; }

    /// <summary><c>true</c> if the user can resize the editor.</summary>
    public abstract bool CanResize { get; }

    /// <summary><c>true</c> if the platform's window type is supported ("HWND" on Windows).</summary>
    public abstract bool IsPlatformSupported(string platformType);

    /// <summary>Opens the editor inside <paramref name="parent"/> (an HWND on Windows).</summary>
    /// <param name="parent">Native parent window.</param>
    /// <param name="platformType">"HWND" on Windows, "X11EmbedWindowID" on Linux.</param>
    public abstract bool Attach(nint parent, string platformType = "HWND");

    /// <summary>Tells the editor its new size (after the parent window was resized).</summary>
    public virtual void SetSize(int width, int height)
    {
    }

    /// <summary>Corrects a size the user dragged to one the editor accepts.</summary>
    public virtual (int Width, int Height) ConstrainSize(int width, int height) => CanResize ? (width, height) : Size;

    /// <summary>Tells the editor the display scale (1.0 = 96 DPI), when it supports it.</summary>
    public virtual void SetContentScale(float scale)
    {
    }

    /// <summary>Closes the editor.</summary>
    public abstract void Dispose();
}

/// <summary>A plug-in module or plug-in failed (base of the format-specific exceptions).</summary>
public class PluginException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PluginException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    public PluginException(string message, Exception inner) : base(message, inner)
    {
    }
}
