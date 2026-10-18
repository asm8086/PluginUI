// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

/// <summary>Minimal drag control used to exercise the host input routing and the gesture protocol.</summary>
internal sealed class TestSlider : ValueControl
{
    private Point _start;
    private double _startValue;

    public int MouseDowns { get; private set; }
    public int CaptureLosses { get; private set; }

    protected override Size DefaultSize => new(40f, 100f);

    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style) { }

    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        MouseDowns++;
        if (TryHandleReset(e) || e.Button != MouseButton.Left) return;
        _start = e.Position;
        _startValue = Value;
        BeginGesture();
        e.Handled = true;
    }

    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsEditing) UpdateGesture(_startValue + (_start.Y - e.Position.Y) / DragSensitivity);
    }

    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndGesture();
    }

    protected internal override void OnCaptureLost()
    {
        CaptureLosses++;
        base.OnCaptureLost();
    }
}

/// <summary>Element recording the events it receives.</summary>
internal sealed class Probe : UIElement
{
    public List<string> Log { get; } = new();

    public Size Natural { get; set; } = new(20f, 10f);

    protected override Size MeasureOverride(Size available) => Natural;

    protected internal override void OnMouseEnter() { Log.Add("enter"); base.OnMouseEnter(); }

    protected internal override void OnMouseLeave() { Log.Add("leave"); base.OnMouseLeave(); }

    protected internal override void OnGotFocus() { Log.Add("focus"); base.OnGotFocus(); }

    protected internal override void OnLostFocus() { Log.Add("blur"); base.OnLostFocus(); }

    protected internal override void OnKeyDown(KeyEventArgs e) { Log.Add("key:" + e.Key); base.OnKeyDown(e); }
}
