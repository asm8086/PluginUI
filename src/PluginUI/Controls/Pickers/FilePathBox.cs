// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>What a <see cref="FilePathBox"/> selects.</summary>
public enum FilePathMode
{
    /// <summary>An existing file (open dialog).</summary>
    OpenFile,
    /// <summary>A destination file (save dialog).</summary>
    SaveFile,
    /// <summary>A folder.</summary>
    Folder,
}

/// <summary>
/// Path field with a browse button: shows the path with a middle ellipsis, opens the native file/folder dialog
/// and accepts files dropped from Explorer (sample loaders, IR loaders, export folders).
/// </summary>
public class FilePathBox : Control
{
    private string? _path;
    private bool _buttonPressed;
    private bool _buttonHover;

    /// <summary>Creates a path box.</summary>
    public FilePathBox()
    {
        AllowFileDrop = true;
    }

    /// <summary>Selected path (null = none).</summary>
    [Category("Value")]
    public string? Path
    {
        get => _path;
        set
        {
            if (_path == value) return;
            _path = value;
            ToolTip = value;
            PathChanged?.Invoke(this, value);
            InvalidateVisual();
        }
    }

    /// <summary>Selection mode.</summary>
    [Category("Data")]
    public FilePathMode Mode { get; set; } = FilePathMode.OpenFile;

    /// <summary>File type filters of the dialog.</summary>
    [Category("Data")]
    public IReadOnlyList<FileFilter>? Filters { get; set; }

    /// <summary>Title of the file dialog (<c>null</c> = localized <see cref="UIStrings.FileDialogTitle"/>, "Select").</summary>
    [Category("Text")]
    public string? DialogTitle { get; set; }

    /// <summary>Default extension for <see cref="FilePathMode.SaveFile"/> (without dot).</summary>
    [Category("Data")]
    public string? DefaultExtension { get; set; }

    /// <summary>Text shown when no path is selected (<c>null</c> = localized <see cref="UIStrings.NoFile"/>, "No file").</summary>
    [Category("Text")]
    public string? Placeholder { get; set; }

    /// <summary>Shows only the file name instead of the full path.</summary>
    [Category("Appearance")]
    public bool ShowFileNameOnly { get; set; }

    /// <summary>Raised when the path changes (dialog, drop or code).</summary>
    public event Action<FilePathBox, string?>? PathChanged;

    /// <summary>
    /// Validates dropped or chosen paths (default: existing file for files, existing folder for folders, and the
    /// extension list of <see cref="Filters"/>).
    /// </summary>
    [Browsable(false)]
    public Func<string, bool>? Validate { get; set; }

    /// <inheritdoc />
    protected override Size DefaultSize => new(220f, Theme.Metrics.ControlHeight + ControlSizing.LabelAndValueHeight(this));

    private TextStyle PathStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Left, Ellipsis = false };

    private Rect ButtonRect(Rect widget) => widget.TakeRight(MathF.Min(28f, widget.Width * 0.4f));

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        bool enabled = IsEffectivelyEnabled;
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.35f) });
        dc.FillRoundedRect(widget, radius, style.Background);

        var button = ButtonRect(widget);
        using (dc.Clip(button))
        {
            dc.FillRoundedRect(button, radius, _buttonPressed ? style.Pressed : _buttonHover && enabled ? style.Fill.Lighten(0.06f) : style.Fill);
        }
        float sepX = MathF.Round(button.X) + 0.5f;
        dc.DrawLine(new Point(sepX, widget.Y + 1f), new Point(sepX, widget.Bottom - 1f), style.Frame);
        Glyphs.Ellipsis(dc, button.Center, 12f, enabled ? p.Text : p.TextDisabled);
        dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, IsMouseOver && enabled ? style.Frame.Lighten(0.2f) : style.Frame, 1f);

        // Folder/file glyph.
        var icon = new Rect(widget.X + 7f, widget.Center.Y - 5f, 12f, 10f);
        var iconColor = enabled ? (string.IsNullOrEmpty(_path) ? p.TextDisabled : style.Accent) : p.TextDisabled;
        if (Mode == FilePathMode.Folder)
        {
            dc.FillRoundedRect(new Rect(icon.X, icon.Y - 1.5f, 5f, 3f), 1f, iconColor);
            dc.FillRoundedRect(icon, 1.5f, iconColor);
        }
        else
        {
            dc.DrawRoundedRect(new Rect(icon.X + 2f, icon.Y - 1.5f, 8f, 12f), 1.5f, iconColor, 1.2f);
            dc.DrawLine(new Point(icon.X + 4f, icon.Y + 3f), new Point(icon.X + 8f, icon.Y + 3f), iconColor, 1f);
            dc.DrawLine(new Point(icon.X + 4f, icon.Y + 6f), new Point(icon.X + 8f, icon.Y + 6f), iconColor, 1f);
        }

        var textArea = Rect.FromEdges(widget.X + 25f, widget.Y, button.X - 6f, widget.Bottom);
        if (string.IsNullOrEmpty(_path))
        {
            dc.DrawText(Placeholder ?? Strings.NoFile, textArea, PathStyle, p.TextDisabled);
        }
        else
        {
            string shown = ShowFileNameOnly ? System.IO.Path.GetFileName(_path.TrimEnd('\\', '/')) : _path;
            dc.DrawText(MiddleEllipsis(shown, PathStyle, textArea.Width), textArea, PathStyle, enabled ? p.Text : p.TextDisabled);
        }
    }

    /// <summary>Shortens <paramref name="text"/> with "…" in the middle so that it fits <paramref name="maxWidth"/>.</summary>
    protected string MiddleEllipsis(string text, TextStyle style, float maxWidth)
    {
        if (maxWidth <= 0f || TextMeasurer.Measure(text, style).Width <= maxWidth) return text;
        int lo = 0, hi = text.Length;
        string best = "…";
        while (lo <= hi)
        {
            int keep = (lo + hi) / 2;
            int head = (keep + 1) / 2, tail = keep / 2;
            string candidate = text[..head] + "…" + text[^tail..];
            if (TextMeasurer.Measure(candidate, style).Width <= maxWidth)
            {
                best = candidate;
                lo = keep + 1;
            }
            else
            {
                hi = keep - 1;
            }
        }
        return best;
    }

    /// <summary>Opens the dialog for the current mode.</summary>
    public void Browse()
    {
        string? folder = string.IsNullOrEmpty(_path) ? null : (Directory.Exists(_path) ? _path : System.IO.Path.GetDirectoryName(_path));
        string title = DialogTitle ?? Strings.FileDialogTitle;
        string? chosen = Mode switch
        {
            FilePathMode.Folder => FileDialogs.PickFolder(Host, title, folder),
            FilePathMode.SaveFile => FileDialogs.SaveFile(Host, title, Filters, string.IsNullOrEmpty(_path) ? null : System.IO.Path.GetFileName(_path), DefaultExtension, folder),
            _ => FileDialogs.OpenFile(Host, title, Filters, folder),
        };
        if (chosen is not null) Path = chosen;
    }

    private bool IsAcceptable(string path)
    {
        if (Validate is not null) return Validate(path);
        if (Mode == FilePathMode.Folder) return Directory.Exists(path);
        if (Mode == FilePathMode.OpenFile && !File.Exists(path)) return false;
        if (Filters is not { Count: > 0 }) return true;
        string ext = System.IO.Path.GetExtension(path);
        foreach (var f in Filters)
        {
            foreach (var pattern in f.Patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (pattern is "*" or "*.*") return true;
                if (pattern.StartsWith("*.", StringComparison.Ordinal) && ext.Equals(pattern[1..], StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    protected internal override void OnFilesDropped(FilesDroppedEventArgs e)
    {
        base.OnFilesDropped(e);
        if (!IsEffectivelyEnabled) return;
        foreach (var file in e.Files)
        {
            if (!IsAcceptable(file)) continue;
            Path = file;
            e.Handled = true;
            return;
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        e.Handled = true;
        if (ButtonRect(widget).Contains(e.Position))
        {
            _buttonPressed = true;
            InvalidateVisual();
        }
        else if (e.ClickCount == 2)
        {
            Browse();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_buttonPressed) return;
        _buttonPressed = false;
        InvalidateVisual();
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        if (ButtonRect(widget).Contains(e.Position)) Host?.Post(Browse); // after the capture is released
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        bool hover = ButtonRect(widget).Contains(e.Position);
        if (hover != _buttonHover)
        {
            _buttonHover = hover;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _buttonHover = false;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _buttonPressed = false;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (e.Key is Key.Space or Key.Enter)
        {
            Browse();
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && _path is not null)
        {
            Path = null;
            e.Handled = true;
        }
    }
}
