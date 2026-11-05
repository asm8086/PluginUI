// 2026/11/05 15:08:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Desktop;

/// <summary>
/// The document of a single-document application: file path, "modified" state, title bar ("Name* — Application"),
/// the New / Open / Save / Save As commands with their dialogs, the question before losing changes, the recent
/// files and the undo history. Your code only reads, writes and resets the document.
/// </summary>
/// <example>
/// <code>
/// var doc = new DocumentSession(window, app, [new FileFilter("Filter design", "*.bqd")], "bqd")
/// {
///     Reset = () => model.Reset(),
///     Read = path => model.Load(path),
///     Write = path => model.Save(path),
/// };
/// fileMenu.Items.Add(new MenuItem(doc.NewCommand));
/// </code>
/// </example>
public sealed class DocumentSession
{
    private readonly StandaloneWindow _window;
    private readonly DesktopApplication? _app;
    private readonly IReadOnlyList<FileFilter> _filters;
    private readonly string _defaultExtension;
    private readonly string _applicationName;
    private bool _forcedDirty;

    /// <summary>Creates the session of a window.</summary>
    /// <param name="window">Main window (title, dialogs, close confirmation).</param>
    /// <param name="app">Application (name, recent files), or null.</param>
    /// <param name="filters">File types of the Open and Save As dialogs; "All files" is added.</param>
    /// <param name="defaultExtension">Extension added by Save As when the user types none ("bqd").</param>
    /// <param name="applicationName">Name in the title bar (default: the application name, else the window title).</param>
    public DocumentSession(StandaloneWindow window, DesktopApplication? app, IReadOnlyList<FileFilter> filters, string defaultExtension, string? applicationName = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(filters);
        _window = window;
        _app = app;
        _filters = filters;
        _defaultExtension = (defaultExtension ?? string.Empty).TrimStart('.');
        _applicationName = applicationName ?? app?.Name ?? window.Title;
        var t = Strings;
        Undo.Strings = t;
        NewCommand = new Command(t.MenuNew, () => New(), KeyGesture.Parse("Ctrl+N")) { Description = t.HintNew };
        OpenCommand = new Command(t.MenuOpen, () => Open(), KeyGesture.Parse("Ctrl+O")) { Description = t.HintOpen };
        SaveCommand = new Command(t.MenuSave, () => Save(), KeyGesture.Parse("Ctrl+S")) { Description = t.HintSave };
        SaveAsCommand = new Command(t.MenuSaveAs, () => SaveAs(), KeyGesture.Parse("Ctrl+Shift+S")) { Description = t.HintSaveAs };
        Undo.Changed += _ => UpdateTitle();
        window.Closing += OnWindowClosing;
        UpdateTitle();
    }

    /// <summary>Undo history; the document is modified when it differs from its saved point.</summary>
    public UndoStack Undo { get; } = new();

    /// <summary>File of the document, or null for a new document.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Name shown in the title bar and the dialogs (file name without extension, or "Untitled").</summary>
    public string DisplayName => FilePath is null ? Strings.Untitled : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary><c>true</c> when the document has unsaved changes.</summary>
    public bool IsDirty => _forcedDirty || Undo.IsDirty;

    /// <summary>Clears the document (New). Required.</summary>
    public Action? Reset { get; set; }

    /// <summary>Loads a file into the document; returns <c>false</c> (or throws) when it cannot be read.</summary>
    public Func<string, bool>? Read { get; set; }

    /// <summary>Writes the document to a file; returns <c>false</c> (or throws) when it cannot be written.</summary>
    public Func<string, bool>? Write { get; set; }

    /// <summary>File / New (Ctrl+N).</summary>
    public Command NewCommand { get; }

    /// <summary>File / Open (Ctrl+O).</summary>
    public Command OpenCommand { get; }

    /// <summary>File / Save (Ctrl+S).</summary>
    public Command SaveCommand { get; }

    /// <summary>File / Save As (Ctrl+Shift+S).</summary>
    public Command SaveAsCommand { get; }

    /// <summary>Raised after New, Open, Save and Save As changed the file or the saved state.</summary>
    public event Action<DocumentSession>? DocumentChanged;

    /// <summary>Raised after the document was written to a file (argument: the path).</summary>
    public event Action<DocumentSession, string>? Saved;

    /// <summary>Raised after a file was read into the document (argument: the path).</summary>
    public event Action<DocumentSession, string>? Opened;

    private UIStrings Strings => _window.Host.Strings;

    /// <summary>Marks a change that is not in the undo history (until the next save).</summary>
    public void MarkDirty()
    {
        if (_forcedDirty) return;
        _forcedDirty = true;
        UpdateTitle();
    }

    /// <summary>New document, after asking to save the current one.</summary>
    public bool New()
    {
        if (!ConfirmDiscard()) return false;
        Reset?.Invoke();
        SetFile(null);
        return true;
    }

    /// <summary>Asks for a file and opens it, after asking to save the current document.</summary>
    public bool Open()
    {
        if (!ConfirmDiscard()) return false;
        string? folder = FilePath is null ? null : Path.GetDirectoryName(FilePath);
        var path = FileDialogs.OpenFile(_window.Host, Mnemonics.Strip(Strings.MenuOpen).TrimEnd('…', '.'), DialogFilters(), folder);
        return path is not null && OpenFile(path, confirm: false);
    }

    /// <summary>Opens a file (recent files, command line, drag and drop).</summary>
    /// <param name="path">File to open.</param>
    /// <param name="confirm">Asks to save the current document first.</param>
    public bool OpenFile(string path, bool confirm = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (confirm && !ConfirmDiscard()) return false;
        if (!File.Exists(path))
        {
            _app?.RecentFiles.Remove(path);
            MessageDialog.Show(_window, _applicationName, Strings.FormatFileNotFound(path));
            return false;
        }
        if (!Run(Read, path)) return false;
        SetFile(path);
        _app?.RecentFiles.Add(path);
        Opened?.Invoke(this, FilePath!);
        return true;
    }

    /// <summary>Saves to the current file, or asks for one. Returns <c>false</c> if cancelled or failed.</summary>
    public bool Save() => FilePath is null ? SaveAs() : SaveTo(FilePath);

    /// <summary>Asks for a file and saves to it.</summary>
    public bool SaveAs()
    {
        string? folder = FilePath is null ? null : Path.GetDirectoryName(FilePath);
        string name = (FilePath is null ? Strings.Untitled : Path.GetFileNameWithoutExtension(FilePath)) + (_defaultExtension.Length > 0 ? "." + _defaultExtension : string.Empty);
        var path = FileDialogs.SaveFile(_window.Host, Mnemonics.Strip(Strings.MenuSaveAs).TrimEnd('…', '.'), DialogFilters(), name, _defaultExtension.Length > 0 ? _defaultExtension : null, folder);
        return path is not null && SaveTo(path);
    }

    /// <summary>
    /// Asks to save a modified document (Save / Don't Save / Cancel). Returns <c>true</c> when the document may be
    /// replaced or closed.
    /// </summary>
    public bool ConfirmDiscard()
    {
        if (!IsDirty) return true;
        return MessageDialog.AskSaveChanges(_window, DisplayName, _applicationName) switch
        {
            SaveChangesChoice.Save => Save(),
            SaveChangesChoice.DontSave => true,
            _ => false,
        };
    }

    /// <summary>Updates the window title: "Name* — Application".</summary>
    public void UpdateTitle() => _window.Title = $"{DisplayName}{(IsDirty ? "*" : string.Empty)} — {_applicationName}";

    private bool SaveTo(string path)
    {
        if (!Run(Write, path)) return false;
        FilePath = Path.GetFullPath(path);
        _forcedDirty = false;
        Undo.MarkSaved();
        _app?.RecentFiles.Add(FilePath);
        UpdateTitle();
        DocumentChanged?.Invoke(this);
        Saved?.Invoke(this, FilePath);
        return true;
    }

    private void SetFile(string? path)
    {
        FilePath = path is null ? null : Path.GetFullPath(path);
        _forcedDirty = false;
        Undo.Clear();
        UpdateTitle();
        DocumentChanged?.Invoke(this);
    }

    private bool Run(Func<string, bool>? io, string path)
    {
        if (io is null) return false;
        try
        {
            if (io(path)) return true;
            MessageDialog.Show(_window, _applicationName, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            MessageDialog.Show(_window, _applicationName, $"{path}{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
        return false;
    }

    private List<FileFilter> DialogFilters() => [.. _filters, new FileFilter(Strings.AllFiles, "*.*")];

    private void OnWindowClosing(StandaloneWindow window, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
    }
}
