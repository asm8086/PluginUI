// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

/// <summary>NativeWindowHost without a window: sizing and the popup rule (the Win32 part runs in the desktop host).</summary>
public class NativeWindowHostTests
{
    [Test]
    public void PreferredPixelSizeIsMeasuredInDips()
    {
        var root = new StackPanel();
        using var host = new UIHost(root, new Size(800f, 600f));
        host.Zoom = 2f;
        var native = new NativeWindowHost { ProvidesContainer = true, HorizontalAlignment = HorizontalAlignment.Left };
        root.Add(native);
        host.UpdateLayout();
        Assert.That(native.Bounds.Height, Is.EqualTo(120f), "PreferredSize until the content reports a size");
        native.PreferredPixelSize = (600, 400);
        host.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(native.Bounds.Width, Is.EqualTo(300f));
            Assert.That(native.Bounds.Height, Is.EqualTo(200f));
            Assert.That(native.ChildWindow, Is.EqualTo((nint)0), "no container without a host window");
            Assert.Throws<InvalidOperationException>(() => native.ChildWindow = 1);
        });
    }

    [Test]
    public void PopupsOverlappingTheAreaCoverIt()
    {
        var root = new Canvas();
        using var host = new UIHost(root, new Size(400f, 300f));
        var native = new NativeWindowHost { HideUnderPopups = true };
        native.SetBounds(0f, 100f, 400f, 200f);
        root.Add(native);
        host.UpdateLayout();
        Assert.That(native.IsCoveredByPopup(), Is.False);
        host.ShowMenu([new MenuItem("One"), new MenuItem("Two")], new Rect(10f, 90f, 80f, 20f));
        host.UpdateLayout();
        Assert.That(native.IsCoveredByPopup(), Is.True, "a menu opening over the area");
        host.CloseAllPopups();
        host.ShowMenu([new MenuItem("One")], new Rect(10f, 0f, 80f, 20f));
        host.UpdateLayout();
        Assert.That(native.IsCoveredByPopup(), Is.False, "a menu above the area");
    }
}
