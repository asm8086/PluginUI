<!-- 2026/11/05 14:06:00 UTC - PluginUI -->
# PluginUI.Desktop

Builds complete Windows desktop applications with [PluginUI](https://www.nuget.org/packages/PluginUI) as their whole
user interface (Win32 + Direct2D, C#, .NET 10), compiled as IL (`dotnet run`, debugging) or NativeAOT (one native
`.exe`, set `<PublishAot>true</PublishAot>` and run `dotnet publish`).

- `DesktopApplication`: DPI awareness, data folder under `%APPDATA%`, main window with its saved placement, recent
  files, application commands (`ExitCommand`, `FullScreenCommand` on F11), message loop, crash report.
- `DocumentSession`: file path, modified state, title bar, New / Open / Save / Save As with their dialogs, the
  "save changes?" question when closing, recent files.
- `UndoStack`: undo / redo with merging of continuous edits and ready-made Ctrl+Z / Ctrl+Y commands.
- `AppSettings<T>`: JSON settings through System.Text.Json source generation (no reflection, AOT safe).
- `MessageDialog`: synchronous message boxes drawn with PluginUI.

The menu bar (`MenuBar`), commands and shortcuts (`Command`, `KeyGesture`), modal windows
(`StandaloneWindow.ShowDialog`), the dark title bar, the window icon and full screen (`StandaloneWindow.IsFullScreen`)
are in the PluginUI package itself.

Windows 10 1809 or later. Guide: https://asm8086.github.io/PluginUI/ — source and demo application:
https://github.com/asm8086/PluginUI
