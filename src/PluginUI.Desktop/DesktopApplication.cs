// 2026/11/05 15:08:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace PluginUI.Desktop;

/// <summary>
/// The application object of a PluginUI desktop program: DPI awareness, data folder, main window with its saved
/// placement, recent files, application-wide commands (exit, full screen), the message loop and a crash report. Works the same compiled
/// as IL (<c>dotnet run</c>, debugging) and as NativeAOT (<c>dotnet publish</c> with <c>PublishAot</c>).
/// </summary>
/// <example>
/// <code>
/// [STAThread]
/// static int Main()
/// {
///     var app = new DesktopApplication("Biquad Designer", company: "asm8086");
///     var window = new StandaloneWindow(new UIHost(BuildRoot(app), new Size(900, 600), SizingMode.Relayout), app.Name);
///     return app.Run(window);
/// }
/// </code>
/// </example>
public sealed partial class DesktopApplication
{
    private const string StateFileName = "state.json";

    private readonly string _dataFolder;
    private StandaloneWindow? _mainWindow;

    /// <summary>
    /// Creates the application: opts the process into per-monitor DPI awareness, reads the saved state (recent files,
    /// main window placement) and reports unhandled exceptions. Create it before any window.
    /// </summary>
    /// <param name="name">Application name (title bar, data folder, dialogs).</param>
    /// <param name="company">Optional publisher: the data folder becomes <c>%APPDATA%\company\name</c>.</param>
    /// <param name="dataFolder">Data folder to use instead of the one under <c>%APPDATA%</c> (portable mode, tests).</param>
    /// <exception cref="InvalidOperationException">The calling thread is not STA (<c>[STAThread]</c> missing on Main).</exception>
    public DesktopApplication(string name, string? company = null, string? dataFolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // The file dialogs, the clipboard and drag and drop are COM / OLE services: in a multithreaded apartment the
        // common item dialogs misbehave or hang. Fail early with the fix rather than later in a dialog.
        if (OperatingSystem.IsWindows() && Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
        {
            throw new InvalidOperationException("The UI thread of a desktop application must be single-threaded (STA): mark Main with [STAThread].");
        }
        Name = name;
        Company = company;
        _dataFolder = dataFolder ?? DefaultDataFolder(name, company);
        StandaloneWindow.EnablePerMonitorDpiAwareness();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        var state = AppSettings<DesktopState>.TryRead(GetDataPath(StateFileName), DesktopJson.Default.DesktopState);
        if (state is not null)
        {
            RecentFiles.Load(state.RecentFiles);
            MainWindowPlacement = state.MainWindow;
        }
    }

    /// <summary>Application name.</summary>
    public string Name { get; }

    /// <summary>Publisher, or null.</summary>
    public string? Company { get; }

    /// <summary>Folder of the settings and state files (created on first write).</summary>
    public string DataFolder => _dataFolder;

    /// <summary>Most recently used files (saved with the state).</summary>
    public RecentFiles RecentFiles { get; } = new();

    /// <summary>Application-wide commands, routed from the keyboard of the main window.</summary>
    public CommandSet Commands { get; } = new();

    /// <summary>Saved placement of the main window (restored by <see cref="Run"/>).</summary>
    public WindowPlacement? MainWindowPlacement { get; set; }

    /// <summary>
    /// Opens the main window in full screen when it was closed in full screen (default <c>false</c>: it opens as a
    /// normal window, at its bounds outside full screen).
    /// </summary>
    public bool RestoreFullScreen { get; set; }

    /// <summary>Main window while <see cref="Run"/> runs.</summary>
    public StandaloneWindow? MainWindow => _mainWindow;

    /// <summary>Path of a file in <see cref="DataFolder"/>.</summary>
    public string GetDataPath(string fileName) => Path.Combine(_dataFolder, fileName);

    /// <summary>
    /// Shows the main window (at its saved placement), runs the message loop until it closes, saves the state and
    /// returns the exit code.
    /// </summary>
    public int Run(StandaloneWindow mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        _mainWindow = mainWindow;
        if (MainWindowPlacement is { } placement && mainWindow.Placement is null)
            mainWindow.Placement = RestoreFullScreen ? placement : placement with { IsFullScreen = false };
        Commands.Attach(mainWindow.Host);
        mainWindow.Closed += w => MainWindowPlacement = w.Placement;
        try
        {
            return mainWindow.Run();
        }
        finally
        {
            Commands.Detach();
            SaveState();
            _mainWindow = null;
        }
    }

    /// <summary>
    /// File / Exit command (shortcut Alt+F4, texts in <see cref="UIHost.DefaultLanguage"/> at first use): runs
    /// <see cref="Exit"/>.
    /// </summary>
    public Command ExitCommand => _exitCommand ??= CreateExitCommand();

    private Command? _exitCommand;

    private Command CreateExitCommand()
    {
        var t = UIStrings.ForLanguage(UIHost.DefaultLanguage);
        return new Command(t.MenuExit, Exit, KeyGesture.Parse("Alt+F4")) { Description = t.HintExit };
    }

    /// <summary>
    /// View / Full Screen command (shortcut F11, checked while in full screen, texts in
    /// <see cref="UIHost.DefaultLanguage"/> at first use): runs <see cref="ToggleFullScreen"/>. Disabled when the main
    /// window has a fixed size (<see cref="SizingMode.Fixed"/>). Esc also leaves full screen (<see cref="StandaloneWindow.ExitFullScreenOnEscape"/>).
    /// </summary>
    public Command FullScreenCommand => _fullScreenCommand ??= CreateFullScreenCommand();

    private Command? _fullScreenCommand;

    private Command CreateFullScreenCommand()
    {
        var t = UIStrings.ForLanguage(UIHost.DefaultLanguage);
        return new Command(t.MenuFullScreen, ToggleFullScreen, KeyGesture.Parse("F11"))
        {
            Description = t.HintFullScreen,
            IsCheckable = true,
            IsCheckedWhen = () => _mainWindow?.IsFullScreen == true,
            // Disabled for a fixed-size main window, which would stay at its size in a corner of the screen.
            CanExecuteWhen = () => _mainWindow?.Host.IsResizable ?? true,
        };
    }

    /// <summary>Puts the main window in full screen, or back.</summary>
    public void ToggleFullScreen()
    {
        if (_mainWindow is { WindowHandle: not 0 } w) w.ToggleFullScreen();
    }

    /// <summary>Asks the main window to close (File / Exit): its <see cref="StandaloneWindow.Closing"/> handlers can cancel.</summary>
    public void Exit()
    {
        if (_mainWindow is { WindowHandle: not 0 } w) w.RequestClose();
    }

    /// <summary>Writes the state file (recent files, main window placement). Called by <see cref="Run"/>.</summary>
    public bool SaveState()
    {
        if (_mainWindow is { WindowHandle: not 0 } w) MainWindowPlacement = w.Placement;
        var state = new DesktopState { MainWindow = MainWindowPlacement, RecentFiles = [.. RecentFiles.Items] };
        return AppSettings<DesktopState>.TryWrite(GetDataPath(StateFileName), state, DesktopJson.Default.DesktopState);
    }

    /// <summary>Default data folder: <c>%APPDATA%\company\name</c> (or <c>%APPDATA%\name</c>).</summary>
    public static string DefaultDataFolder(string name, string? company = null)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;
        return string.IsNullOrWhiteSpace(company) ? Path.Combine(root, SafeName(name)) : Path.Combine(root, SafeName(company), SafeName(name));
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name.Trim()) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.Length == 0 ? "App" : sb.ToString();
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        string text = e.ExceptionObject?.ToString() ?? "Unknown error";
        string? logPath = null;
        try
        {
            Directory.CreateDirectory(_dataFolder);
            logPath = GetDataPath("crash.log");
            File.WriteAllText(logPath, $"{DateTime.UtcNow:yyyy/MM/dd HH:mm:ss} UTC - {Name}{Environment.NewLine}{text}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logPath = null;
        }
        if (!OperatingSystem.IsWindows()) return;
        string first = (e.ExceptionObject as Exception)?.Message ?? text;
        string message = logPath is null ? first : $"{first}{Environment.NewLine}{Environment.NewLine}{logPath}";
        try
        {
            NativeMethods.MessageBoxW(_mainWindow?.WindowHandle ?? 0, message, Name, 0x10 /* MB_ICONERROR */);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No user32 (tests): the log is enough.
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int MessageBoxW(nint owner, string text, string caption, uint type);
    }
}

/// <summary>State saved by <see cref="DesktopApplication"/> (state.json).</summary>
internal sealed class DesktopState
{
    public WindowPlacement? MainWindow { get; set; }

    public List<string> RecentFiles { get; set; } = new();
}

[JsonSerializable(typeof(DesktopState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class DesktopJson : JsonSerializerContext;
