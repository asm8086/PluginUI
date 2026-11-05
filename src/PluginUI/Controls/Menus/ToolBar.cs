// 2026/11/05 15:08:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Built-in icons of <see cref="ToolBarButton"/> (drawn as vectors, sharp at every scale, in the theme colors).</summary>
public enum ToolGlyph
{
    /// <summary>No icon (the button shows its text).</summary>
    None,
    /// <summary>Blank page: File / New.</summary>
    New,
    /// <summary>Folder: File / Open.</summary>
    Open,
    /// <summary>Floppy disk: File / Save.</summary>
    Save,
    /// <summary>Arrow turning back: Edit / Undo.</summary>
    Undo,
    /// <summary>Arrow turning forward: Edit / Redo.</summary>
    Redo,
    /// <summary>Scissors: Edit / Cut.</summary>
    Cut,
    /// <summary>Two sheets: Edit / Copy.</summary>
    Copy,
    /// <summary>Clipboard: Edit / Paste.</summary>
    Paste,
    /// <summary>Bin: Delete.</summary>
    Delete,
    /// <summary>Magnifier with a plus: Zoom in.</summary>
    ZoomIn,
    /// <summary>Magnifier with a minus: Zoom out.</summary>
    ZoomOut,
    /// <summary>Gear: settings, preferences.</summary>
    Settings,
    /// <summary>Question mark in a circle: Help.</summary>
    Help,
    /// <summary>Triangle: Play.</summary>
    Play,
    /// <summary>Square: Stop.</summary>
    Stop,
    /// <summary>Disc: Record.</summary>
    Record,
    /// <summary>Grid of 3 × 3 cells: show grid.</summary>
    Grid,
    /// <summary>One period of a sine wave: show a curve, signal.</summary>
    Wave,
}

/// <summary>Draws the <see cref="ToolGlyph"/> icons.</summary>
public static class ToolGlyphs
{
    /// <summary>Draws <paramref name="glyph"/> fitted in a square of side <paramref name="size"/> centered at <paramref name="center"/>.</summary>
    /// <param name="dc">Drawing context.</param>
    /// <param name="glyph">Icon.</param>
    /// <param name="center">Center of the icon.</param>
    /// <param name="size">Side of the icon square (16 is the usual tool bar size).</param>
    /// <param name="color">Stroke and fill color.</param>
    public static void Draw(DrawingContext dc, ToolGlyph glyph, Point center, float size, Color color)
    {
        ArgumentNullException.ThrowIfNull(dc);
        float k = size / 16f;
        float ox = center.X - size * 0.5f, oy = center.Y - size * 0.5f;
        Point P(float x, float y) => new(ox + x * k, oy + y * k);
        float t = MathF.Max(1f, 1.35f * k);
        switch (glyph)
        {
            case ToolGlyph.New:
            {
                ReadOnlySpan<Point> page = [P(3.5f, 1.5f), P(9.5f, 1.5f), P(12.5f, 4.5f), P(12.5f, 14.5f), P(3.5f, 14.5f), P(3.5f, 1.5f)];
                dc.DrawPolyline(page, color, t, StrokeStyle.Round);
                ReadOnlySpan<Point> fold = [P(9.5f, 1.5f), P(9.5f, 4.5f), P(12.5f, 4.5f)];
                dc.DrawPolyline(fold, color, t, StrokeStyle.Round);
                break;
            }
            case ToolGlyph.Open:
            {
                ReadOnlySpan<Point> folder = [P(1.5f, 13.5f), P(1.5f, 3.5f), P(6f, 3.5f), P(7.5f, 5f), P(13.5f, 5f), P(13.5f, 7.5f)];
                dc.DrawPolyline(folder, color, t, StrokeStyle.Round);
                ReadOnlySpan<Point> front = [P(1.5f, 13.5f), P(4f, 7.5f), P(15f, 7.5f), P(12.5f, 13.5f), P(1.5f, 13.5f)];
                dc.DrawPolyline(front, color, t, StrokeStyle.Round);
                break;
            }
            case ToolGlyph.Save:
            {
                ReadOnlySpan<Point> body = [P(2.5f, 2.5f), P(11.5f, 2.5f), P(13.5f, 4.5f), P(13.5f, 13.5f), P(2.5f, 13.5f), P(2.5f, 2.5f)];
                dc.DrawPolyline(body, color, t, StrokeStyle.Round);
                dc.DrawRect(Rect.FromEdges(P(5f, 2.5f).X, P(5f, 2.5f).Y, P(10.5f, 6f).X, P(10.5f, 6f).Y), color, t);
                dc.DrawRect(Rect.FromEdges(P(4.5f, 9f).X, P(4.5f, 9f).Y, P(11.5f, 13.5f).X, P(11.5f, 13.5f).Y), color, t);
                break;
            }
            case ToolGlyph.Undo:
            case ToolGlyph.Redo:
            {
                bool redo = glyph == ToolGlyph.Redo;
                Point M(float x, float y) => redo ? P(16f - x, y) : P(x, y);
                Span<Point> hook = stackalloc Point[12];
                hook[0] = M(3.5f, 6f);
                for (int i = 0; i <= 9; i++)
                {
                    float a = -MathF.PI / 2f + MathF.PI * i / 9f;
                    hook[i + 1] = M(9.5f + 3.75f * MathF.Cos(a), 9.75f + 3.75f * MathF.Sin(a));
                }
                hook[11] = M(6f, 13.5f);
                dc.DrawPolyline(hook, color, t, StrokeStyle.Round);
                ReadOnlySpan<Point> head = [M(6.5f, 3f), M(3.5f, 6f), M(6.5f, 9f)];
                dc.DrawPolyline(head, color, t, StrokeStyle.Round);
                break;
            }
            case ToolGlyph.Cut:
                dc.DrawCircle(P(4.5f, 12f), 2.3f * k, color, t);
                dc.DrawCircle(P(11.5f, 12f), 2.3f * k, color, t);
                dc.DrawLine(P(5.8f, 10.1f), P(11.5f, 1.5f), color, t, StrokeStyle.Round);
                dc.DrawLine(P(10.2f, 10.1f), P(4.5f, 1.5f), color, t, StrokeStyle.Round);
                break;
            case ToolGlyph.Copy:
            {
                ReadOnlySpan<Point> back = [P(5.5f, 4f), P(5.5f, 1.5f), P(14f, 1.5f), P(14f, 10.5f), P(11f, 10.5f)];
                dc.DrawPolyline(back, color, t, StrokeStyle.Round);
                dc.DrawRect(Rect.FromEdges(P(2f, 4.5f).X, P(2f, 4.5f).Y, P(10.5f, 14.5f).X, P(10.5f, 14.5f).Y), color, t);
                break;
            }
            case ToolGlyph.Paste:
            {
                ReadOnlySpan<Point> board = [P(5f, 3f), P(2.5f, 3f), P(2.5f, 14.5f), P(13.5f, 14.5f), P(13.5f, 3f), P(11f, 3f)];
                dc.DrawPolyline(board, color, t, StrokeStyle.Round);
                dc.FillRoundedRect(Rect.FromEdges(P(5f, 1.5f).X, P(5f, 1.5f).Y, P(11f, 4.5f).X, P(11f, 4.5f).Y), k, color);
                dc.DrawLine(P(5.5f, 8f), P(10.5f, 8f), color, t, StrokeStyle.Round);
                dc.DrawLine(P(5.5f, 11f), P(10.5f, 11f), color, t, StrokeStyle.Round);
                break;
            }
            case ToolGlyph.Delete:
            {
                dc.DrawLine(P(2f, 4f), P(14f, 4f), color, t, StrokeStyle.Round);
                ReadOnlySpan<Point> handle = [P(6f, 4f), P(6f, 1.8f), P(10f, 1.8f), P(10f, 4f)];
                dc.DrawPolyline(handle, color, t, StrokeStyle.Round);
                ReadOnlySpan<Point> can = [P(3.5f, 4f), P(4.5f, 14.5f), P(11.5f, 14.5f), P(12.5f, 4f)];
                dc.DrawPolyline(can, color, t, StrokeStyle.Round);
                dc.DrawLine(P(6.5f, 7f), P(6.7f, 12f), color, t, StrokeStyle.Round);
                dc.DrawLine(P(9.5f, 7f), P(9.3f, 12f), color, t, StrokeStyle.Round);
                break;
            }
            case ToolGlyph.ZoomIn:
            case ToolGlyph.ZoomOut:
                dc.DrawCircle(P(6.5f, 6.5f), 4.75f * k, color, t);
                dc.DrawLine(P(10f, 10f), P(14.5f, 14.5f), color, t * 1.4f, StrokeStyle.Round);
                dc.DrawLine(P(4.3f, 6.5f), P(8.7f, 6.5f), color, t, StrokeStyle.Round);
                if (glyph == ToolGlyph.ZoomIn) dc.DrawLine(P(6.5f, 4.3f), P(6.5f, 8.7f), color, t, StrokeStyle.Round);
                break;
            case ToolGlyph.Settings:
            {
                var c = P(8f, 8f);
                for (int i = 0; i < 8; i++)
                {
                    float a = MathF.PI * i / 4f;
                    var (s, co) = MathF.SinCos(a);
                    dc.DrawLine(new Point(c.X + co * 4.6f * k, c.Y + s * 4.6f * k), new Point(c.X + co * 6.6f * k, c.Y + s * 6.6f * k), color, t * 1.6f);
                }
                dc.DrawCircle(c, 4.4f * k, color, t);
                dc.DrawCircle(c, 1.8f * k, color, t);
                break;
            }
            case ToolGlyph.Help:
            {
                var c = P(8f, 8f);
                dc.DrawCircle(c, 6.6f * k, color, t);
                Span<Point> hook = stackalloc Point[8];
                for (int i = 0; i < 7; i++)
                {
                    float a = MathF.PI * (1.05f + i / 6f * 1.25f);
                    hook[i] = new Point(c.X + 2.3f * k * MathF.Cos(a), c.Y - 1.6f * k + 2.3f * k * MathF.Sin(a));
                }
                hook[7] = P(8f, 9.3f);
                dc.DrawPolyline(hook, color, t, StrokeStyle.Round);
                dc.FillCircle(P(8f, 11.8f), 0.95f * k, color);
                break;
            }
            case ToolGlyph.Play:
            {
                ReadOnlySpan<Point> tri = [P(4.5f, 2.5f), P(13.5f, 8f), P(4.5f, 13.5f)];
                dc.FillPolygon(tri, color);
                break;
            }
            case ToolGlyph.Stop:
                dc.FillRoundedRect(Rect.FromEdges(P(3.5f, 3.5f).X, P(3.5f, 3.5f).Y, P(12.5f, 12.5f).X, P(12.5f, 12.5f).Y), k, color);
                break;
            case ToolGlyph.Record:
                dc.FillCircle(P(8f, 8f), 5f * k, color);
                break;
            case ToolGlyph.Grid:
                dc.DrawRect(Rect.FromEdges(P(2f, 2f).X, P(2f, 2f).Y, P(14f, 14f).X, P(14f, 14f).Y), color, t);
                dc.DrawLine(P(6f, 2f), P(6f, 14f), color, t);
                dc.DrawLine(P(10f, 2f), P(10f, 14f), color, t);
                dc.DrawLine(P(2f, 6f), P(14f, 6f), color, t);
                dc.DrawLine(P(2f, 10f), P(14f, 10f), color, t);
                break;
            case ToolGlyph.Wave:
                Glyphs.Wave(dc, center, size, color, t);
                break;
        }
    }
}

/// <summary>
/// Flat button of a <see cref="ToolBar"/> that shows and runs a <see cref="Command"/>: icon (and optionally text),
/// greyed out when the command cannot run, pressed look for a checked command, tooltip "Text (Ctrl+S)".
/// </summary>
public class ToolBarButton : UIElement
{
    private bool _pressed;

    /// <summary>Creates a button with an empty command (set <see cref="Command"/>; designers).</summary>
    public ToolBarButton() : this(new Command(string.Empty), ToolGlyph.None)
    {
    }

    /// <summary>Creates a button for a command.</summary>
    /// <param name="command">Command shown and run.</param>
    /// <param name="glyph">Icon (<see cref="ToolGlyph.None"/>: the text is shown).</param>
    public ToolBarButton(Command command, ToolGlyph glyph = ToolGlyph.None)
    {
        ArgumentNullException.ThrowIfNull(command);
        Command = command;
        Glyph = glyph;
        ShowText = glyph == ToolGlyph.None;
    }

    /// <summary>Command shown and run.</summary>
    [Browsable(false)]
    public Command Command
    {
        get => _command;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _command = value;
            InvalidateMeasure();
        }
    }

    private Command _command = null!;

    /// <summary>Icon.</summary>
    [Category("Appearance")]
    public ToolGlyph Glyph { get; set; }

    /// <summary>Custom icon drawn instead of <see cref="Glyph"/> (arguments: context, icon square, color).</summary>
    [Browsable(false)]
    public Action<DrawingContext, Rect, Color>? DrawIcon { get; set; }

    /// <summary>Shows the command text (without its mnemonic) next to the icon.</summary>
    [Category("Appearance")]
    public bool ShowText { get; set; }

    /// <summary><c>true</c> when the command can run now.</summary>
    public bool CanRun => IsEffectivelyEnabled && Command.CanExecute();

    private TextStyle CaptionStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Left, Ellipsis = false };

    private bool HasIcon => Glyph != ToolGlyph.None || DrawIcon is not null;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float h = Theme.Metrics.ControlHeight + 4f;
        float w = HasIcon ? h : 8f;
        if (ShowText) w += TextMeasurer.Measure(Command.DisplayText, CaptionStyle).Width + (HasIcon ? 6f : 8f);
        return new Size(w, h);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds;
        bool can = CanRun;
        bool on = Command.IsCheckable && Command.IsChecked;
        float radius = MathF.Max(0f, Theme.Metrics.CornerRadius - 1f);
        if (can && _pressed && IsMouseOver) dc.FillRoundedRect(r, radius, p.Selection);
        else if (on) dc.FillRoundedRect(r, radius, p.Selection.WithAlpha(0.55f));
        else if (can && IsMouseOver) dc.FillRoundedRect(r, radius, p.SurfaceRaised);
        if (on) dc.DrawRoundedRect(r.Inflate(-0.5f), radius, p.Accent.WithAlpha(0.6f));
        var color = can ? p.Text : p.TextDisabled;
        float x = r.X;
        if (HasIcon)
        {
            var icon = new Rect(r.X + (r.Height - 16f) * 0.5f, r.Y + (r.Height - 16f) * 0.5f, 16f, 16f);
            if (DrawIcon is { } draw) draw(dc, icon, color);
            else ToolGlyphs.Draw(dc, Glyph, icon.Center, 16f, color);
            x += r.Height;
        }
        if (ShowText)
        {
            dc.DrawText(Command.DisplayText, Rect.FromEdges(x + (HasIcon ? 0f : 8f), r.Y, r.Right, r.Bottom), CaptionStyle, color);
        }
    }

    /// <inheritdoc />
    protected internal override string? GetToolTipText()
    {
        if (!string.IsNullOrEmpty(ToolTip)) return ToolTip;
        string text = Command.DisplayText.TrimEnd('…', '.');
        return Command.Gesture is { } g ? $"{text} ({g.ToDisplayString(Host?.Strings)})" : text;
    }

    /// <inheritdoc />
    protected internal override void OnMouseEnter()
    {
        base.OnMouseEnter();
        (Parent as ToolBar)?.OnButtonHovered(this);
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        (Parent as ToolBar)?.OnButtonHovered(null);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButton.Left) return;
        e.Handled = true;
        _pressed = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButton.Left || !_pressed) return;
        e.Handled = true;
        _pressed = false;
        InvalidateVisual();
        if (HitTest(e.Position) && CanRun) Command.Execute();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _pressed = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Space or Key.Enter) || e.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        if (CanRun) Command.Execute();
    }
}

/// <summary>Thin vertical line between groups of a <see cref="ToolBar"/>.</summary>
public class ToolBarSeparator : UIElement
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size available) => new(9f, Theme.Metrics.ControlHeight + 4f);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var r = Bounds;
        float x = MathF.Round(r.Center.X) + 0.5f;
        dc.DrawLine(new Point(x, r.Y + 5f), new Point(x, r.Bottom - 5f), Theme.Palette.Border);
    }
}

/// <summary>
/// Tool bar of a desktop application: a row of <see cref="ToolBarButton"/> built from the same <see cref="Command"/>
/// objects as the menus, so that both stay in step (enabled, checked). Hovering a button raises
/// <see cref="HintChanged"/> with the command description, for a <see cref="StatusBar"/>.
/// </summary>
/// <example>
/// <code>
/// var tools = new ToolBar { Dock = Side.Top };
/// tools.Add(doc.NewCommand, ToolGlyph.New);
/// tools.Add(doc.OpenCommand, ToolGlyph.Open);
/// tools.Add(doc.SaveCommand, ToolGlyph.Save);
/// tools.AddSeparator();
/// tools.Add(doc.Undo.UndoCommand, ToolGlyph.Undo);
/// </code>
/// </example>
public class ToolBar : Panel
{
    /// <summary>Creates an empty tool bar.</summary>
    public ToolBar() => Padding = new Thickness(6f, 3f, 6f, 3f);

    /// <summary>Space between buttons.</summary>
    [Category("Layout")]
    public float Spacing { get; set; } = 2f;

    /// <summary>Draws the bar background and its bottom line (off: transparent, for a bar inside another panel).</summary>
    [Category("Appearance")]
    public bool ShowBackground { get; set; } = true;

    /// <summary>Raised when the mouse enters a button (its command description) or leaves it (null).</summary>
    public event Action<ToolBar, string?>? HintChanged;

    /// <summary>Adds a button for a command. Returns it for further settings.</summary>
    /// <param name="command">Command shown and run.</param>
    /// <param name="glyph">Icon (<see cref="ToolGlyph.None"/>: the text is shown).</param>
    public ToolBarButton Add(Command command, ToolGlyph glyph = ToolGlyph.None)
    {
        var button = new ToolBarButton(command, glyph);
        Children.Add(button);
        return button;
    }

    /// <summary>Adds a separator line.</summary>
    public void AddSeparator() => Children.Add(new ToolBarSeparator());

    internal void OnButtonHovered(ToolBarButton? button)
    {
        string? hint = button is null ? null : button.Command.Description ?? button.Command.DisplayText;
        HintChanged?.Invoke(this, hint);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float w = 0f, h = 0f;
        int visible = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
            w += child.DesiredSize.Width;
            h = MathF.Max(h, child.DesiredSize.Height);
            visible++;
        }
        if (visible > 1) w += Spacing * (visible - 1);
        if (h <= 0f) h = Theme.Metrics.ControlHeight + 4f;
        w += Padding.Horizontal;
        return new Size(float.IsFinite(available.Width) ? MathF.Max(w, available.Width) : w, h + Padding.Vertical);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var r = bounds.Deflate(Padding);
        float x = r.X;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var d = child.DesiredSize;
            child.Arrange(new Rect(x, r.Y + (r.Height - d.Height) * 0.5f, d.Width, d.Height));
            x += d.Width + Spacing;
        }
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        if (!ShowBackground) return;
        var p = Theme.Palette;
        dc.FillRect(Bounds, p.Surface);
        float y = MathF.Round(Bounds.Bottom) - 0.5f;
        dc.DrawLine(new Point(Bounds.X, y), new Point(Bounds.Right, y), p.Border);
    }
}
