// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>How a <see cref="Switch"/> reacts to clicks.</summary>
public enum SwitchClickMode
{
    /// <summary>Clicking a position selects it; dragging along the track follows the mouse.</summary>
    Select,
    /// <summary>Every click advances to the next position (wrapping).</summary>
    Cycle,
}

/// <summary>
/// N-position selector switch (waveform, range, mode). Vector look: a slotted track with a lever and position
/// labels; bitmap look: a film-strip skin (<c>new FilmStripSkin(strip, useStateIndex: true)</c>).
/// </summary>
public class Switch : ValueControl
{
    private IReadOnlyList<string>? _items;

    /// <summary>Creates a switch (3 positions unless bound or given items).</summary>
    public Switch()
    {
        StepCount = 2;
    }

    /// <summary>Creates a switch with local position labels.</summary>
    public Switch(params string[] items) : this() => Items = items;

    /// <summary>Position labels (null = parameter state labels).</summary>
    [Category("Value")]
    public IReadOnlyList<string>? Items
    {
        get => _items;
        set
        {
            _items = value;
            if (value is { Count: > 1 } && Parameter is null) StepCount = value.Count - 1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Track direction.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Click behavior.</summary>
    [Category("Behavior")]
    public SwitchClickMode ClickMode { get; set; } = SwitchClickMode.Select;

    /// <summary>Draws the position labels next to the track.</summary>
    [Category("Appearance")]
    public bool ShowPositionLabels { get; set; } = true;

    /// <summary>Number of positions.</summary>
    public int PositionCount => Math.Max(2, StepCount + 1);

    /// <summary>Selected position.</summary>
    [Category("Value")]
    public int SelectedIndex
    {
        get => StateIndex;
        set => Value = Math.Clamp(value, 0, PositionCount - 1) / (double)(PositionCount - 1);
    }

    /// <inheritdoc />
    public override string? ValueText => ShowPositionLabels ? null : base.ValueText;

    private TextStyle PositionStyle => Theme.Typography.Small with { Align = Orientation == Orientation.Vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center };

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            var labels = StateLabels.For(this, _items);
            float maxW = 0f;
            foreach (var l in labels) maxW = MathF.Max(maxW, TextMeasurer.Measure(l, PositionStyle).Width);
            float extra = EffectiveStyle.ShowLabel && !string.IsNullOrEmpty(Label) ? EffectiveStyle.LabelText.Size * 1.5f : 0f;
            int n = PositionCount;
            return Orientation == Orientation.Vertical
                ? new Size(22f + (ShowPositionLabels ? maxW + 8f : 0f), MathF.Max(40f, n * 18f) + extra)
                : new Size(MathF.Max(48f, n * MathF.Max(24f, maxW + 8f)), 22f + (ShowPositionLabels ? 16f : 0f) + extra);
        }
    }

    private Rect Track(Rect widget)
    {
        const float thickness = 14f;
        int n = PositionCount;
        if (Orientation == Orientation.Vertical)
        {
            float pitch = widget.Height / n;
            return new Rect(widget.X + 4f, widget.Y + pitch * 0.5f - thickness * 0.5f, thickness, pitch * (n - 1) + thickness);
        }
        float pitchX = widget.Width / n;
        float y = ShowPositionLabels ? widget.Y + 2f : widget.Center.Y - thickness * 0.5f;
        return new Rect(widget.X + pitchX * 0.5f - thickness * 0.5f, y, pitchX * (n - 1) + thickness, thickness);
    }

    private Point PositionCenter(Rect track, float index)
    {
        int n = PositionCount;
        float t = n <= 1 ? 0f : index / (n - 1);
        return Orientation == Orientation.Vertical
            ? new Point(track.Center.X, track.Y + track.Width * 0.5f + (track.Height - track.Width) * t)
            : new Point(track.X + track.Height * 0.5f + (track.Width - track.Height) * t, track.Center.Y);
    }

    private int IndexAt(Point p)
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        int n = PositionCount;
        float t = Orientation == Orientation.Vertical ? (p.Y - widget.Y) / MathF.Max(1f, widget.Height) : (p.X - widget.X) / MathF.Max(1f, widget.Width);
        return Math.Clamp((int)(t * n), 0, n - 1);
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var track = Track(widget);
        float radius = MathF.Min(track.Width, track.Height) * 0.5f;
        bool enabled = IsEffectivelyEnabled;
        dc.FillRoundedRect(track, radius, style.Background);
        dc.DrawRoundedRect(track.Inflate(-0.5f), radius, style.Frame, 1f);

        int n = PositionCount, selected = StateIndex;
        var labels = StateLabels.For(this, _items);
        for (int i = 0; i < n; i++)
        {
            var c = PositionCenter(track, i);
            dc.FillCircle(c, 1.6f, i == selected ? Color.Transparent : p.TextDisabled);
            if (!ShowPositionLabels || i >= labels.Count) continue;
            Rect tr = Orientation == Orientation.Vertical
                ? Rect.FromEdges(track.Right + 7f, c.Y - 9f, widget.Right, c.Y + 9f)
                : new Rect(c.X - widget.Width / n * 0.5f, track.Bottom + 1f, widget.Width / n, 15f);
            dc.DrawText(labels[i], tr, PositionStyle, !enabled ? p.TextDisabled : i == selected ? p.Text : p.TextDim);
        }

        // Lever.
        var lc = PositionCenter(track, selected);
        float lr = radius + 2f;
        var accent = enabled ? style.Accent : p.TextDisabled;
        if (style.DrawShadow) dc.FillCircle(new Point(lc.X, lc.Y + 1.5f), lr, Color.Black.WithAlpha(0.35f));
        dc.FillCircle(lc, lr, Paint.Vertical(style.Fill.Lighten(0.12f), style.Fill.Darken(0.1f)));
        dc.DrawCircle(lc, lr - 0.5f, style.Frame.Lighten(0.1f), 1f);
        dc.FillCircle(lc, lr * 0.42f, accent);
        if (IsMouseOver && enabled) dc.FillCircle(lc, lr + 3f, style.Hover);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        if (TryHandleReset(e)) return;
        e.Handled = true;
        IsPressed = true;
        if (ClickMode == SwitchClickMode.Cycle)
        {
            SetValueByUser(((StateIndex + 1) % PositionCount) / (double)(PositionCount - 1));
            return;
        }
        BeginGesture();
        UpdateGesture(IndexAt(e.Position) / (double)(PositionCount - 1));
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && IsPressed && ClickMode == SwitchClickMode.Select) UpdateGesture(IndexAt(e.Position) / (double)(PositionCount - 1));
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        IsPressed = false;
        EndGesture();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        if (Orientation == Orientation.Vertical && e.Key is Key.Up or Key.Down && IsEffectivelyEnabled)
        {
            // Top position is index 0: Down moves to the next index.
            int next = Math.Clamp(StateIndex + (e.Key == Key.Down ? 1 : -1), 0, PositionCount - 1);
            SetValueByUser(next / (double)(PositionCount - 1));
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
