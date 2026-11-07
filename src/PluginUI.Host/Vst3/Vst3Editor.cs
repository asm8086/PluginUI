// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Vst3;

/// <summary>
/// Editor window of a VST 3 plug-in (<c>IPlugView</c>), attached to a window of the host. Sizes are in physical pixels.
/// </summary>
public sealed unsafe class Vst3Editor : PluginEditor
{
    private readonly Vst3Plugin _plugin;
    private void* _view;
    private bool _attached;

    internal Vst3Editor(Vst3Plugin plugin, void* view, PlugFrame frame)
    {
        _plugin = plugin;
        _view = view;
        ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_view, 12))(_view, frame.Pointer);
    }

    /// <inheritdoc />
    public override bool IsPlatformSupported(string platformType)
    {
        byte[] type = System.Text.Encoding.ASCII.GetBytes(platformType + "\0");
        fixed (byte* t = type) return ((delegate* unmanaged[MemberFunction]<void*, byte*, int>)Com.Slot(_view, 3))(_view, t) == Vst3Result.Ok;
    }

    /// <inheritdoc />
    public override (int Width, int Height) Size
    {
        get
        {
            ViewRect r;
            return ((delegate* unmanaged[MemberFunction]<void*, ViewRect*, int>)Com.Slot(_view, 9))(_view, &r) == Vst3Result.Ok ? (r.Width, r.Height) : (0, 0);
        }
    }

    /// <inheritdoc />
    public override bool CanResize => ((delegate* unmanaged[MemberFunction]<void*, int>)Com.Slot(_view, 13))(_view) == Vst3Result.Ok;

    /// <inheritdoc />
    public override bool Attach(nint parent, string platformType = "HWND")
    {
        if (_attached) return true;
        byte[] type = System.Text.Encoding.ASCII.GetBytes(platformType + "\0");
        fixed (byte* t = type) _attached = ((delegate* unmanaged[MemberFunction]<void*, void*, byte*, int>)Com.Slot(_view, 4))(_view, (void*)parent, t) == Vst3Result.Ok;
        return _attached;
    }

    /// <inheritdoc />
    public override void SetSize(int width, int height)
    {
        var r = new ViewRect { Left = 0, Top = 0, Right = width, Bottom = height };
        ((delegate* unmanaged[MemberFunction]<void*, ViewRect*, int>)Com.Slot(_view, 10))(_view, &r);
    }

    /// <inheritdoc />
    public override (int Width, int Height) ConstrainSize(int width, int height)
    {
        var r = new ViewRect { Left = 0, Top = 0, Right = width, Bottom = height };
        ((delegate* unmanaged[MemberFunction]<void*, ViewRect*, int>)Com.Slot(_view, 14))(_view, &r);
        return (r.Width, r.Height);
    }

    /// <inheritdoc />
    public override void SetContentScale(float scale)
    {
        void* support = Com.As(_view, Tuid.IPlugViewContentScaleSupport);
        if (support is null) return;
        ((delegate* unmanaged[MemberFunction]<void*, float, int>)Com.Slot(support, 3))(support, scale);
        Com.Release(support);
    }

    internal bool OnResizeRequested(void* view, ViewRect rect)
    {
        if (view != _view) return false;
        bool accepted = ResizeRequested?.Invoke(rect.Width, rect.Height) ?? false;
        if (accepted) SetSize(rect.Width, rect.Height);
        return accepted;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_view is null) return;
        if (_attached) ((delegate* unmanaged[MemberFunction]<void*, int>)Com.Slot(_view, 5))(_view);
        _attached = false;
        ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_view, 12))(_view, null);
        Com.Release(_view);
        _view = null;
        _plugin.EditorClosed(this);
    }
}
