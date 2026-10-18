// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Vertical performance wheel, as on the left of a keyboard: a ridged cylinder turned by dragging. Base class of
/// <see cref="PitchWheel"/> (springs back to the center) and <see cref="ModWheel"/> (stays where it is left).
/// iPlug2 <c>IWheelControl</c>.
/// </summary>
/// <remarks>
/// <para>Bind it to the parameter that the plugin maps to pitch bend or modulation (in VST3, pitch bend and CCs reach
/// the processor as parameters through <c>IMidiMapping</c>). Dragging is relative and vertical (Shift = fine); the
/// mouse wheel also turns it.</para>
/// <para>With <see cref="SpringReturn"/>, releasing the mouse animates the value back to <see cref="ValueControl.DefaultValue"/>
/// in <see cref="ReturnTime"/> seconds inside the same edit gesture, so the host records the return in the automation.</para>
/// </remarks>
public class WheelControl : ValueControl
{
    private RotaryLinearHelpers.DragAccumulator _drag;
    private WheelTicker? _ticker;
    private bool _returning;
    private double _returnFrom;
    private double _returnElapsed;

    /// <summary>Creates a wheel.</summary>
    public WheelControl()
    {
        Cursor = StandardCursor.SizeNS;
        DragSensitivity = 160f;
    }

    /// <summary>Returns to the default value when released (pitch bend behavior).</summary>
    [Category("Behavior")]
    public bool SpringReturn { get; set; }

    /// <summary>Duration of the return animation in seconds (0 = instant).</summary>
    [Category("Behavior")]
    public float ReturnTime { get; set; } = 0.12f;

    /// <summary>Draws the value as a bar from the center (bipolar) instead of from the bottom.</summary>
    [Category("Value")]
    public bool IsBipolar { get; set; }

    /// <summary>Number of ridges visible on the cylinder.</summary>
    [Category("Appearance")]
    public int RidgeCount { get; set; } = 9;

    /// <summary>Draws the center detent mark (bipolar wheels).</summary>
    [Category("Appearance")]
    public bool ShowCenterMark { get; set; } = true;

    /// <inheritdoc />
    protected override Size DefaultSize => new(30f, 110f);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float w = MathF.Min(widget.Width, MathF.Max(14f, widget.Height * 0.32f));
        var slot = Rect.FromCenter(widget.Center, w, widget.Height);
        float radius = MathF.Min(6f, w * 0.3f);

        // Recessed slot.
        dc.FillRoundedRect(slot, radius, p.Background.Darken(0.35f));
        dc.DrawRoundedRect(slot.Inflate(-0.5f), radius, p.Border, 1f);

        // Cylinder: light in the middle, dark at the ends (curvature).
        var drum = slot.Deflate(new Thickness(3f, 4f));
        var fill = IsPressed ? style.Pressed : style.Fill;
        dc.FillRoundedRect(drum, radius * 0.6f, new LinearGradientPaint(new Point(0f, 0f), new Point(0f, 1f),
        [
            new(0f, fill.Darken(0.55f)), new(0.22f, fill.Darken(0.1f)), new(0.5f, fill.Lighten(0.12f)),
            new(0.78f, fill.Darken(0.1f)), new(1f, fill.Darken(0.55f)),
        ]));
        if (IsMouseOver && !IsPressed) dc.FillRoundedRect(drum, radius * 0.6f, style.Hover);

        // Ridges scroll with the value; their spacing shrinks towards the ends (projection of a cylinder).
        double value = Value;
        int ridges = Math.Max(3, RidgeCount);
        float phase = (float)(value * ridges * 0.9);
        dc.PushClip(drum);
        for (int i = -1; i <= ridges + 1; i++)
        {
            float t = (i + phase % 1f) / ridges;          // 0..1 along the visible half turn
            if (t < 0f || t > 1f) continue;
            float angle = (t - 0.5f) * MathF.PI;         // −90°..+90°
            float y = drum.Center.Y - MathF.Sin(angle) * drum.Height * 0.5f;
            float shade = MathF.Cos(angle);
            dc.DrawLine(new Point(drum.X + 1.5f, y), new Point(drum.Right - 1.5f, y), p.Shadow.WithAlpha(0.35f + 0.4f * shade), MathF.Max(1f, 1.6f * shade));
            dc.DrawLine(new Point(drum.X + 1.5f, y + 1f), new Point(drum.Right - 1.5f, y + 1f), fill.Lighten(0.35f).WithAlpha(0.35f * shade), 1f);
        }
        dc.PopClip();

        // Value indicator along the slot edge.
        var accent = style.Accent;
        float bar = 2.5f;
        var track = new Rect(slot.Right + 2f, slot.Y + 2f, bar, slot.Height - 4f);
        if (track.Right <= widget.Right + 0.5f)
        {
            dc.FillRoundedRect(track, bar * 0.5f, p.Track);
            float from = IsBipolar ? 0.5f : 0f;
            float y0 = track.Bottom - from * track.Height, y1 = track.Bottom - (float)value * track.Height;
            dc.FillRoundedRect(Rect.FromEdges(track.X, MathF.Min(y0, y1), track.Right, MathF.Max(y0, y1)), bar * 0.5f, accent);
        }
        if (IsBipolar && ShowCenterMark)
        {
            float cy = slot.Center.Y;
            dc.DrawLine(new Point(slot.X - 3f, cy), new Point(slot.X, cy), p.TextDim, 1.5f);
        }
        // Grip highlight at the current position.
        float gy = drum.Bottom - (float)value * drum.Height;
        dc.DrawLine(new Point(drum.X + 2f, gy), new Point(drum.Right - 2f, gy), accent.WithAlpha(0.9f), 2f, StrokeStyle.Round);
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        if (!SpringReturn && TryHandleReset(e)) return;
        StopReturn(endGesture: true);
        IsPressed = true;
        BeginGesture();
        _drag.Start(e.Position, Value);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsPressed) return;
        double factor = 1.0 / MathF.Max(10f, DragSensitivity) * (e.Shift ? FineFactor : 1.0);
        UpdateGesture(_drag.Move(e.Position, 0f, -1f, factor));
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsPressed || e.Button != MouseButton.Left) return;
        IsPressed = false;
        if (SpringReturn) StartReturn();
        else EndGesture();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        bool wasPressed = IsPressed;
        if (SpringReturn && wasPressed)
        {
            IsPressed = false;
            StartReturn();
            return;
        }
        base.OnCaptureLost();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        StopReturn(endGesture: false);
        base.OnDetached();
    }

    private void StartReturn()
    {
        if (ReturnTime <= 0f || Host is null)
        {
            UpdateGesture(DefaultValue);
            EndGesture();
            return;
        }
        _returning = true;
        _returnFrom = Value;
        _returnElapsed = 0.0;
        _ticker ??= new WheelTicker(this);
        Host.AddTicker(_ticker);
    }

    private void StopReturn(bool endGesture)
    {
        if (!_returning) return;
        _returning = false;
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        if (endGesture) EndGesture();
    }

    private bool StepReturn(double elapsed)
    {
        if (!_returning) return false;
        _returnElapsed += elapsed;
        double t = Math.Clamp(_returnElapsed / Math.Max(1e-3, ReturnTime), 0.0, 1.0);
        double eased = 1.0 - (1.0 - t) * (1.0 - t) * (1.0 - t); // ease-out cubic
        UpdateGesture(_returnFrom + (DefaultValue - _returnFrom) * eased);
        if (t < 1.0) return true;
        _returning = false;
        EndGesture();
        return false;
    }

    /// <summary>Completes a running return animation immediately (tests, offscreen hosts).</summary>
    public void FinishReturn()
    {
        if (_returning) StepReturn(double.MaxValue / 4);
    }

    private sealed class WheelTicker(WheelControl owner) : ITickable
    {
        public bool Tick(double elapsedSeconds) => owner.StepReturn(elapsedSeconds);
    }
}

/// <summary>
/// Pitch-bend wheel: bipolar, centered by default, springs back to the center when released.
/// </summary>
/// <example>
/// <code>
/// var bend = new PitchWheel { Parameter = Parameters[model.PitchBend] };
/// var mod = new ModWheel { Parameter = Parameters[model.ModWheel] };
/// </code>
/// </example>
public class PitchWheel : WheelControl
{
    /// <summary>Creates a pitch wheel (default value 0.5, spring return).</summary>
    public PitchWheel()
    {
        SpringReturn = true;
        IsBipolar = true;
        DefaultValue = 0.5;
        Value = 0.5;
    }
}

/// <summary>Modulation wheel: unipolar, starts at 0 and stays where it is left.</summary>
public class ModWheel : WheelControl
{
    /// <summary>Creates a modulation wheel.</summary>
    public ModWheel()
    {
        ShowCenterMark = false;
    }
}
