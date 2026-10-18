// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// A modulation source (LFO, envelope, macro, velocity…) that can be dragged onto value controls accepting
/// modulation (<see cref="ValueControl.AcceptsModulation"/>), as in Serum or Vital.
/// </summary>
/// <param name="Name">Name shown on the drag chip, e.g. "LFO 1".</param>
/// <param name="Color">Color identifying the source on the chip and on the targets (null = theme alternate accent).</param>
/// <param name="Id">Application identifier of the source (index, enum value…), passed back on drop.</param>
public sealed record ModulationSource(string Name, Color? Color = null, object? Id = null);

/// <summary>
/// Draggable chip representing a <see cref="ModulationSource"/>: drag it onto a knob or slider whose
/// <see cref="ValueControl.AcceptsModulation"/> is <c>true</c>; the target raises <see cref="ValueControl.ModulationDropped"/>
/// and the application creates the routing (and usually sets <see cref="Knob.ModulationAmount"/> to show it).
/// </summary>
/// <example>
/// <code>
/// var lfo1 = new ModulationSourceButton { Source = new ModulationSource("LFO 1", Color.FromRgb(0xFFB74D), 0) };
/// cutoffKnob.AcceptsModulation = true;
/// cutoffKnob.ModulationDropped += (target, source) =>
/// {
///     matrix.Add(source.Id, target.Parameter);
///     ((Knob)target).ModulationAmount = 0.25;
/// };
/// </code>
/// </example>
public class ModulationSourceButton : Control
{
    private Point _pressPoint;
    private bool _armed;
    private bool _dragging;

    /// <summary>Creates a modulation source chip.</summary>
    public ModulationSourceButton()
    {
        Cursor = StandardCursor.SizeAll;
    }

    /// <summary>The source carried by drags (null = inactive).</summary>
    [Browsable(false)]
    public ModulationSource? Source { get; set; }

    /// <summary><c>true</c> while the chip is being dragged.</summary>
    [Browsable(false)]
    public bool IsDragging => _dragging;

    /// <summary>Raised after a drag ended: <c>true</c> if a control accepted the drop.</summary>
    public event Action<ModulationSourceButton, bool>? DragCompleted;

    /// <inheritdoc />
    protected override Size DefaultSize => new(72f, 22f);

    private Color SourceColor => Source?.Color ?? Theme.Palette.AccentAlt;

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var color = SourceColor;
        float radius = widget.Height * 0.5f;
        dc.FillRoundedRect(widget, radius, color.WithAlpha(_dragging ? 0.45f : IsMouseOver ? 0.32f : 0.22f));
        dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, color, 1f);
        // Grip dots on the left, name on the right.
        float gx = widget.X + radius * 0.8f;
        for (int i = -1; i <= 1; i++)
        {
            dc.FillCircle(new Point(gx, widget.Center.Y + i * 3.5f), 1.2f, color);
            dc.FillCircle(new Point(gx + 3.5f, widget.Center.Y + i * 3.5f), 1.2f, color);
        }
        var text = new Rect(gx + 7f, widget.Y, widget.Right - gx - 10f, widget.Height);
        dc.DrawText(Source?.Name ?? Label, text, Theme.Typography.Small with { Align = HorizontalAlignment.Center }, Theme.Palette.Text);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || Source is null || !IsEffectivelyEnabled) return;
        _armed = true;
        _pressPoint = e.Position;
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_armed || _dragging || Host is null || Source is null) return;
        if ((e.Position - _pressPoint).Length < 4f) return;
        _armed = false;
        _dragging = true;
        InvalidateVisual();
        Host.BeginDrag(this, Source, dropped =>
        {
            _dragging = false;
            InvalidateVisual();
            DragCompleted?.Invoke(this, dropped);
        });
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _armed = false;
    }
}
