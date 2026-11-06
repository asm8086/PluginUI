// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>One field of a <see cref="StatusBar"/>.</summary>
public sealed class StatusBarSegment
{
    /// <summary>Creates a segment with a fixed text.</summary>
    /// <param name="text">Text.</param>
    /// <param name="width">Width in DIPs; 0 = fits its text; <see cref="float.PositiveInfinity"/> = takes the remaining space.</param>
    public StatusBarSegment(string? text = null, float width = 0f)
    {
        Text = text;
        Width = width;
    }

    /// <summary>Creates a segment whose text is read at every repaint (cursor position, sample rate, zoom).</summary>
    /// <param name="source">Text source.</param>
    /// <param name="width">Width in DIPs; 0 = fits its text; <see cref="float.PositiveInfinity"/> = takes the remaining space.</param>
    public StatusBarSegment(Func<string> source, float width = 0f)
    {
        TextSource = source;
        Width = width;
    }

    /// <summary>Fixed text (used when <see cref="TextSource"/> is null).</summary>
    public string? Text { get; set; }

    /// <summary>Text read at every repaint.</summary>
    public Func<string>? TextSource { get; set; }

    /// <summary>Width in DIPs; 0 = fits its text; <see cref="float.PositiveInfinity"/> = takes the remaining space.</summary>
    public float Width { get; set; }

    /// <summary>Text alignment.</summary>
    public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;

    /// <summary>Text color (null: the theme text color).</summary>
    public Color? Foreground { get; set; }

    /// <summary>Hidden segments take no space.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Current text.</summary>
    public string CurrentText => TextSource?.Invoke() ?? Text ?? string.Empty;
}

/// <summary>
/// Status bar of a desktop application: fields from left to right (<see cref="Segments"/>), the first stretched one
/// showing <see cref="Hint"/> (the description of the menu entry or tool bar button under the mouse) or a temporary
/// message (<see cref="ShowMessage"/>: "Saved", "3 bands copied").
/// </summary>
/// <example>
/// <code>
/// var status = new StatusBar { Dock = Side.Bottom };
/// status.Segments.Add(new StatusBarSegment(() => doc.DisplayName, float.PositiveInfinity));
/// status.Segments.Add(new StatusBarSegment(() => $"{rate / 1000:0.#} kHz", 80f));
/// status.Follow(menuBar);
/// status.Follow(toolBar);
/// </code>
/// </example>
public class StatusBar : UIElement, ITickable
{
    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new StatusBarAutomationPeer(this);

    private string? _message;
    private double _messageLeft;

    /// <summary>Fields, from left to right.</summary>
    [Browsable(false)]
    public IList<StatusBarSegment> Segments { get; } = new List<StatusBarSegment>();

    /// <summary>
    /// Text shown instead of the first stretched field while it is set (null: the field text). Set by the menus and
    /// tool bars followed with <see cref="Follow(MenuBar)"/> and <see cref="Follow(ToolBar)"/>.
    /// </summary>
    [Category("Text")]
    public string? Hint
    {
        get => _hint;
        set
        {
            if (_hint == value) return;
            _hint = value;
            InvalidateVisual();
        }
    }

    private string? _hint;

    /// <summary>Temporary message being shown, or null.</summary>
    public string? Message => _message;

    private TextStyle SegmentStyle => Theme.Typography.Small with { Ellipsis = true };

    /// <summary>Screen readers speak the messages of <see cref="ShowMessage"/> (default <c>true</c>).</summary>
    [Category("Behavior")]
    public bool AnnounceMessages { get; set; } = true;

    /// <summary>Shows a message in the first stretched field for a while (a hint, if any, stays on top).</summary>
    /// <param name="text">Message.</param>
    /// <param name="seconds">Duration (0 or less: until the next message or <see cref="ClearMessage"/>).</param>
    public void ShowMessage(string text, double seconds = 4.0)
    {
        _message = text;
        _messageLeft = seconds > 0 ? seconds : double.PositiveInfinity;
        if (seconds > 0) Host?.AddTicker(this);
        InvalidateVisual();
        if (AnnounceMessages && !string.IsNullOrEmpty(text)) Host?.Announce(text);
    }

    /// <summary>Removes the temporary message.</summary>
    public void ClearMessage()
    {
        if (_message is null) return;
        _message = null;
        Host?.RemoveTicker(this);
        InvalidateVisual();
    }

    /// <summary>Shows the descriptions of the entries highlighted in the menus of <paramref name="menuBar"/>.</summary>
    public void Follow(MenuBar menuBar)
    {
        ArgumentNullException.ThrowIfNull(menuBar);
        menuBar.HintChanged += (_, hint) => Hint = hint;
    }

    /// <summary>Shows the descriptions of the buttons of <paramref name="toolBar"/> under the mouse.</summary>
    public void Follow(ToolBar toolBar)
    {
        ArgumentNullException.ThrowIfNull(toolBar);
        toolBar.HintChanged += (_, hint) => Hint = hint;
    }

    bool ITickable.Tick(double elapsedSeconds)
    {
        if (_message is null) return false;
        _messageLeft -= elapsedSeconds;
        if (_messageLeft > 0) return true;
        _message = null;
        InvalidateVisual();
        return false;
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        Host?.RemoveTicker(this);
        base.OnDetached();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float h = MathF.Round(SegmentStyle.Size + 12f);
        return new Size(float.IsFinite(available.Width) ? available.Width : 200f, h);
    }

    /// <summary>Text drawn in segment <paramref name="index"/> (the hint or message for the first stretched one).</summary>
    public string DisplayedText(int index)
    {
        var segment = Segments[index];
        if (index == FirstStretched()) return Hint ?? _message ?? segment.CurrentText;
        return segment.CurrentText;
    }

    private int FirstStretched()
    {
        for (int i = 0; i < Segments.Count; i++)
        {
            if (Segments[i].IsVisible && float.IsPositiveInfinity(Segments[i].Width)) return i;
        }
        return -1;
    }

    /// <summary>Bounds of the visible segments, in order (host DIPs; hidden segments get an empty rectangle).</summary>
    public IReadOnlyList<Rect> SegmentBounds()
    {
        var style = SegmentStyle;
        var r = Bounds;
        var widths = new float[Segments.Count];
        float fixedTotal = 0f;
        int stretched = 0;
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            if (!s.IsVisible) continue;
            if (float.IsPositiveInfinity(s.Width)) { stretched++; continue; }
            widths[i] = s.Width > 0f ? s.Width : TextMeasurer.Measure(DisplayedText(i), style).Width + 20f;
            fixedTotal += widths[i];
        }
        float share = stretched == 0 ? 0f : MathF.Max(0f, r.Width - fixedTotal) / stretched;
        var result = new Rect[Segments.Count];
        float x = r.X;
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            if (!s.IsVisible) { result[i] = new Rect(x, r.Y, 0f, r.Height); continue; }
            float w = float.IsPositiveInfinity(s.Width) ? share : widths[i];
            result[i] = new Rect(x, r.Y, w, r.Height);
            x += w;
        }
        return result;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds;
        dc.FillRect(r, p.Surface);
        float top = MathF.Round(r.Y) + 0.5f;
        dc.DrawLine(new Point(r.X, top), new Point(r.Right, top), p.Border);
        var style = SegmentStyle;
        var rects = SegmentBounds();
        int hinted = FirstStretched();
        bool first = true;
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            if (!s.IsVisible) continue;
            var sr = rects[i];
            if (!first)
            {
                float x = MathF.Round(sr.X) + 0.5f;
                dc.DrawLine(new Point(x, r.Y + 5f), new Point(x, r.Bottom - 4f), p.Border);
            }
            first = false;
            bool transient = i == hinted && (Hint is not null || _message is not null);
            var color = transient ? p.Text : s.Foreground ?? p.TextDim;
            dc.DrawText(DisplayedText(i), sr.Deflate(new Thickness(10f, 0f)), style with { Align = s.Align }, color);
        }
    }
}
