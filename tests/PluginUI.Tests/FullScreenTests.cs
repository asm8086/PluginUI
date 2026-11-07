// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Desktop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Tests;

/// <summary>Full screen of <see cref="StandaloneWindow"/> (real windows: Windows only) and its desktop command.</summary>
[NonParallelizable]
public unsafe class FullScreenTests
{
    private const int GwlStyle = -16;
    private const uint WsCaption = 0x00C00000, WsThickFrame = 0x00040000, WmKeyDown = 0x0100;

    private static RECT WindowRect(StandaloneWindow window)
    {
        RECT r;
        GetWindowRect((HWND)window.WindowHandle, &r);
        return r;
    }

    private static RECT MonitorRect(StandaloneWindow window)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(MonitorFromWindow((HWND)window.WindowHandle, 2 /* MONITOR_DEFAULTTONEAREST */), &info);
        return info.rcMonitor;
    }

    private static uint Style(StandaloneWindow window) => (uint)GetWindowLongPtrW((HWND)window.WindowHandle, GwlStyle);

    private static (int, int, int, int) Box(RECT r) => (r.left, r.top, r.right - r.left, r.bottom - r.top);

    [Test]
    public void FullScreenCoversTheMonitorAndComesBack() => DesktopTests.OnThread(ApartmentState.STA, () =>
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Real windows: Windows only.");
        StandaloneWindow.EnablePerMonitorDpiAwareness();
        using var host = new UIHost(new StackPanel(), new Size(400f, 300f), SizingMode.Relayout) { MaxSize = new Size(600f, 450f) };
        using var window = new StandaloneWindow(host, "Full screen test") { QuitOnClose = false, Placement = new WindowPlacement(100, 100, 500, 400) };
        int changes = 0;
        window.FullScreenChanged += _ => changes++;
        window.Show();
        var before = window.Placement!.Value;
        Assert.That(before.IsFullScreen, Is.False);

        window.IsFullScreen = true;
        Assert.Multiple(() =>
        {
            Assert.That(Box(WindowRect(window)), Is.EqualTo(Box(MonitorRect(window))), "the whole monitor, beyond MaxSize");
            Assert.That(Style(window) & (WsCaption | WsThickFrame), Is.Zero, "no title bar or border");
            Assert.That(window.Placement, Is.EqualTo(before with { IsFullScreen = true }), "the placement to come back to");
            Assert.That(changes, Is.EqualTo(1));
        });

        // Esc that the hosted UI does not use reaches the frame.
        SendMessageW((HWND)window.WindowHandle, WmKeyDown, 0x1B, 0);
        Assert.Multiple(() =>
        {
            Assert.That(window.IsFullScreen, Is.False);
            Assert.That(Box(WindowRect(window)), Is.EqualTo((before.X, before.Y, before.Width, before.Height)));
            Assert.That(Style(window) & WsCaption, Is.EqualTo(WsCaption));
            Assert.That(changes, Is.EqualTo(2));
        });

        window.ExitFullScreenOnEscape = false;
        window.ToggleFullScreen();
        SendMessageW((HWND)window.WindowHandle, WmKeyDown, 0x1B, 0);
        Assert.That(window.IsFullScreen, Is.True, "Esc can be left to the application");
        window.ToggleFullScreen();
        Assert.That(window.IsFullScreen, Is.False);
        window.Close();
    });

    [Test]
    public void FullScreenSetBeforeShowAndMaximizedWindowsComeBack() => DesktopTests.OnThread(ApartmentState.STA, () =>
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Real windows: Windows only.");
        using var host = new UIHost(new StackPanel(), new Size(400f, 300f), SizingMode.Relayout);
        using var window = new StandaloneWindow(host, "Full screen test")
        {
            QuitOnClose = false,
            Placement = new WindowPlacement(120, 90, 520, 380, IsMaximized: true, IsFullScreen: true),
        };
        window.Show();
        Assert.That(window.IsFullScreen, Is.True, "restored in full screen");
        Assert.That(Box(WindowRect(window)), Is.EqualTo(Box(MonitorRect(window))));
        window.IsFullScreen = false;
        Assert.That(IsZoomed((HWND)window.WindowHandle) != BOOL.FALSE, Is.True, "maximized again");
        Assert.That(window.Placement, Is.EqualTo(new WindowPlacement(120, 90, 520, 380, IsMaximized: true)));
        window.Close();
    });

    [Test]
    public void TheDesktopCommandIsF11AndCheckable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pluginui-fs-" + Guid.NewGuid().ToString("N"));
        try
        {
            DesktopTests.OnThread(ApartmentState.STA, () =>
            {
                var app = new DesktopApplication("Test App", dataFolder: dir);
                var command = app.FullScreenCommand;
                Assert.Multiple(() =>
                {
                    Assert.That(command.Gesture, Is.EqualTo(KeyGesture.Parse("F11")));
                    Assert.That(command.IsCheckable, Is.True);
                    Assert.That(command.IsChecked, Is.False);
                    Assert.That(command.CanExecute(), Is.True, "nothing to toggle yet, but not a fixed-size window");
                    Assert.That(app.RestoreFullScreen, Is.False, "opens as a normal window by default");
                    Assert.That(UIStrings.French.MenuFullScreen, Is.EqualTo("&Plein écran"));
                    Assert.That(UIStrings.English.HintFullScreen, Does.Contain("Esc"));
                });
                // The full screen flag is saved with the placement.
                app.MainWindowPlacement = new WindowPlacement(10, 20, 800, 600, IsFullScreen: true);
                Assert.That(app.SaveState(), Is.True);
                var reloaded = new DesktopApplication("Test App", dataFolder: dir);
                Assert.That(reloaded.MainWindowPlacement?.IsFullScreen, Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
