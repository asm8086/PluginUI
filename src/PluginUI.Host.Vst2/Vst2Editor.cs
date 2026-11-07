// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Vst2;

/// <summary>
/// Editor of a VST 2 plug-in, opened inside a window of the host. VST 2 editors choose their size (they ask for a new
/// one through <see cref="PluginEditor.ResizeRequested"/>); the host calls <see cref="PluginInstance.Idle"/> regularly,
/// which gives them their idle time.
/// </summary>
public sealed unsafe class Vst2Editor : PluginEditor
{
    private Vst2Plugin? _plugin;

    internal Vst2Editor(Vst2Plugin plugin) => _plugin = plugin;

    /// <summary><c>true</c> while the editor is open in a window.</summary>
    public bool IsAttached { get; private set; }

    /// <inheritdoc />
    public override (int Width, int Height) Size
    {
        get
        {
            if (_plugin is null) return (0, 0);
            ERect* rect = null;
            _plugin.Dispatch(EffectOpcode.EditGetRect, 0, 0, &rect);
            return rect is null ? (0, 0) : (Math.Max(0, rect->Right - rect->Left), Math.Max(0, rect->Bottom - rect->Top));
        }
    }

    /// <inheritdoc />
    /// <remarks>VST 2 has no way for the host to resize an editor: the editor resizes itself.</remarks>
    public override bool CanResize => false;

    /// <inheritdoc />
    public override bool IsPlatformSupported(string platformType) => platformType is "HWND" or "X11EmbedWindowID";

    /// <inheritdoc />
    public override bool Attach(nint parent, string platformType = "HWND")
    {
        if (_plugin is null || parent == 0 || !IsPlatformSupported(platformType)) return false;
        if (IsAttached) return true;
        Vst2Module.Step("opening the editor");
        // Many plug-ins return 0 even when the editor opened: the call itself is the answer.
        _plugin.Dispatch(EffectOpcode.EditOpen, 0, 0, (void*)parent);
        IsAttached = true;
        return true;
    }

    internal bool OnResizeRequested(int width, int height) => width > 0 && height > 0 && (ResizeRequested?.Invoke(width, height) ?? false);

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_plugin is null) return;
        if (IsAttached) _plugin.Dispatch(EffectOpcode.EditClose);
        IsAttached = false;
        _plugin.EditorClosed(this);
        _plugin = null;
    }
}
