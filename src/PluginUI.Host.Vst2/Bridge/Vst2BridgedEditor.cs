// 2026/11/08 18:01:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Vst2;

/// <summary>
/// Editor of a <see cref="Vst2BridgedPlugin"/>: the plug-in's own window, opened by its bridge process as a child of the
/// host's window. The bridge runs its message loop and its idle time; a new size the plug-in asks for reaches
/// <see cref="PluginEditor.ResizeRequested"/> on the host's next <see cref="PluginInstance.Idle"/>.
/// </summary>
public sealed class Vst2BridgedEditor : PluginEditor
{
    private Vst2BridgedPlugin? _plugin;
    private (int Width, int Height) _size;

    internal Vst2BridgedEditor(Vst2BridgedPlugin plugin, (int Width, int Height) size)
    {
        _plugin = plugin;
        _size = size;
    }

    /// <summary><c>true</c> while the editor is open in a window.</summary>
    public bool IsAttached { get; private set; }

    /// <inheritdoc />
    public override (int Width, int Height) Size => _size;

    /// <inheritdoc />
    /// <remarks>VST 2 editors resize themselves.</remarks>
    public override bool CanResize => false;

    /// <inheritdoc />
    /// <remarks>Bridges run on Windows: only "HWND" parents.</remarks>
    public override bool IsPlatformSupported(string platformType) => platformType == "HWND";

    /// <inheritdoc />
    public override bool Attach(nint parent, string platformType = "HWND")
    {
        if (_plugin is null || parent == 0 || !IsPlatformSupported(platformType)) return false;
        if (IsAttached) return true;
        try
        {
            _size = _plugin.OpenEditor(parent);
        }
        catch (Vst2BridgeException)
        {
            return false;
        }
        IsAttached = true;
        return true;
    }

    internal void OnResizeRequested(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _size = (width, height);
        ResizeRequested?.Invoke(width, height);
    }

    internal void Detach() => _plugin = null;

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_plugin is null) return;
        _plugin.CloseEditor(this);
        IsAttached = false;
        _plugin = null;
    }
}
