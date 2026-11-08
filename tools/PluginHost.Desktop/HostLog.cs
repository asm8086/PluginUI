// 2026/11/08 13:55:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Desktop;

/// <summary>How serious a message of the log is.</summary>
internal enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>A message of the log.</summary>
internal sealed record LogEntry(DateTime Time, LogLevel Level, string Source, string Text);

/// <summary>
/// The messages of the session (plug-ins loaded or refused, graph files, audio device, scans), shown in the Log tab.
/// Messages may come from any thread; <see cref="Added"/> is raised on the thread that adds them.
/// </summary>
internal sealed class HostLog
{
    /// <summary>The oldest messages are dropped past this count.</summary>
    public const int Capacity = 5000;

    private readonly Lock _lock = new();
    private readonly List<LogEntry> _entries = [];

    /// <summary>Raised after a message is added (on the thread that added it) or after <see cref="Clear"/>.</summary>
    public event Action? Changed;

    /// <summary>A copy of the messages, oldest first.</summary>
    public List<LogEntry> Snapshot()
    {
        lock (_lock) return [.. _entries];
    }

    /// <summary>Number of messages of a level.</summary>
    public int Count(LogLevel level)
    {
        lock (_lock) return _entries.Count(e => e.Level == level);
    }

    public void Info(string source, string text) => Add(LogLevel.Info, source, text);

    public void Warning(string source, string text) => Add(LogLevel.Warning, source, text);

    public void Error(string source, string text) => Add(LogLevel.Error, source, text);

    public void Add(LogLevel level, string source, string text)
    {
        // One message per line in the table: line breaks become " · ".
        string flat = string.Join(" · ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        lock (_lock)
        {
            _entries.Add(new LogEntry(DateTime.Now, level, source, flat));
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Changed?.Invoke();
    }

    /// <summary>The messages as text, one per line (time, level, source, message), for the clipboard.</summary>
    public static string ToText(IEnumerable<LogEntry> entries)
        => string.Join(Environment.NewLine, entries.Select(e => $"{e.Time:yyyy-MM-dd HH:mm:ss}\t{LevelName(e.Level)}\t{e.Source}\t{e.Text}"));

    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Warning => L.T("Warning", "Avertissement"),
        LogLevel.Error => L.T("Error", "Erreur"),
        _ => L.T("Info", "Info"),
    };
}
