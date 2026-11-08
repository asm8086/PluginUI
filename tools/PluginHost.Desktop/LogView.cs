// 2026/11/08 13:55:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Desktop;

namespace PluginUI.Host.Desktop;

/// <summary>The Log tab: the messages of the session in a table, with a filter, Copy and Clear.</summary>
internal sealed class LogView
{
    private static readonly Color WarningColor = Color.FromRgb(0xFFB74D);
    private static readonly Color ErrorColor = Color.FromRgb(0xE57373);

    private readonly Func<UIHost> _host;
    private readonly HostLog _log;
    private readonly DropDown _filter;
    private readonly Label _count;
    private List<LogEntry> _rows = [];
    private bool _pending;

    public LogView(Func<UIHost> host, HostLog log)
    {
        _host = host;
        _log = log;
        Table = new DataTable { MultiSelect = true, EmptyText = L.T("No message yet.", "Aucun message pour l'instant.") };
        DataColumn Column(string key, string header, Func<LogEntry, string> text, float width)
        {
            var column = new DataColumn(header, o => text((LogEntry)o), width) { Key = key };
            Table.AddColumn(column);
            return column;
        }
        Column("time", L.T("Time", "Heure"), e => e.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture), 70f);
        var level = Column("level", L.T("Level", "Niveau"), e => HostLog.LevelName(e.Level), 100f);
        level.CellColor = o => ColorOf(((LogEntry)o).Level);
        Column("source", L.T("Source", "Source"), e => e.Source, 160f);
        var text = Column("message", L.T("Message", "Message"), e => e.Text, float.NaN);
        text.CanHide = false;
        text.MinWidth = 200f;
        text.CellColor = o => ColorOf(((LogEntry)o).Level);
        Table.RowContextMenu = _ => [new MenuItem(L.T("&Copy", "&Copier"), Copy), new MenuItem(L.T("Copy &all", "Copier &tout"), CopyAll)];

        _filter = new DropDown([L.T("All messages", "Tous les messages"), L.T("Warnings and errors", "Avertissements et erreurs"), L.T("Errors", "Erreurs")])
        {
            Label = string.Empty, Width = 210f, Height = 26f, SelectedIndex = 0,
        };
        _filter.ValueChanged += (_, _) => Refresh();
        var copy = new Button(L.T("Copy all", "Tout copier"), CopyAll) { Width = 120f, Height = 26f, ToolTip = L.T("Copies the messages shown, to paste them in a mail or a report", "Copie les messages affichés, pour les coller dans un courriel ou un rapport") };
        var clear = new Button(L.T("Clear", "Effacer"), _log.Clear) { Width = 100f, Height = 26f };
        _count = new Label { IsDim = true, VerticalAlignment = VerticalAlignment.Center };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8f, Dock = Side.Left };
        buttons.Add(_filter, copy, clear);
        var bar = new DockPanel { Dock = Side.Top, Height = 40f, Padding = new Thickness(10f, 7f, 10f, 7f) };
        bar.Children.Add(buttons);
        bar.Children.Add(_count);
        Root = new DockPanel();
        Root.Children.Add(bar);
        Table.Margin = new Thickness(10f, 0f, 10f, 6f);
        Root.Children.Add(Table);

        // Messages may come from the audio thread: the table is refreshed on the UI thread, once per batch.
        _log.Changed += () =>
        {
            if (_pending) return;
            _pending = true;
            _host().Post(Refresh);
        };
        Refresh();
    }

    public DockPanel Root { get; }

    public DataTable Table { get; }

    /// <summary>Raised on the UI thread when the counts of warnings and errors may have changed (for the tab header).</summary>
    public event Action? CountsChanged;

    private static Color? ColorOf(LogLevel level) => level switch
    {
        LogLevel.Warning => WarningColor,
        LogLevel.Error => ErrorColor,
        _ => null,
    };

    private void Refresh()
    {
        _pending = false;
        var all = _log.Snapshot();
        var minimum = _filter.SelectedIndex switch { 1 => LogLevel.Warning, 2 => LogLevel.Error, _ => LogLevel.Info };
        bool atEnd = Table.RowCount == 0 || Table.SelectedItems.Any() == false;
        _rows = [.. all.Where(e => e.Level >= minimum)];
        Table.Items = _rows;
        if (atEnd && _rows.Count > 0) Table.ScrollRowIntoView(_rows.Count - 1);
        int warnings = all.Count(e => e.Level == LogLevel.Warning), errors = all.Count(e => e.Level == LogLevel.Error);
        static string S(int n) => n > 1 ? "s" : string.Empty;
        _count.Text = L.T($"{all.Count} message{S(all.Count)} · {warnings} warning{S(warnings)} · {errors} error{S(errors)}",
            $"{all.Count} message{S(all.Count)} · {warnings} avertissement{S(warnings)} · {errors} erreur{S(errors)}");
        CountsChanged?.Invoke();
    }

    private void Copy()
    {
        var selected = Table.SelectedItems.Cast<LogEntry>().ToList();
        if (selected.Count > 0) Clipboard.SetText(HostLog.ToText(selected));
    }

    private void CopyAll()
    {
        if (_rows.Count > 0) Clipboard.SetText(HostLog.ToText(_rows));
    }
}
