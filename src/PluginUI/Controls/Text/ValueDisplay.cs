// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// Shows a value as text (the parameter's display string with units). Double-click or Enter to type a new value,
/// optional vertical drag, arrow keys and wheel to step it. VSTGUI <c>CParamDisplay</c> / iPlug2 <c>ICaptionControl</c>.
/// </summary>
public class ValueDisplay : ValueControl
{
    private bool _dragging;
    private bool _dragStarted;
    private float _dragStartY;
    private double _dragStartValue;

    /// <summary>Creates a value display.</summary>
    public ValueDisplay()
    {
        ResetOnDoubleClick = false; // double-click types a value
    }

    /// <summary>Allows typing a value (double-click or Enter).</summary>
    [Category("Behavior")]
    public bool IsEditable { get; set; } = true;

    /// <summary>Allows changing the value by dragging vertically.</summary>
    [Category("Behavior")]
    public bool AllowDrag { get; set; }

    /// <summary>Draws the background box and frame.</summary>
    [Category("Appearance")]
    public bool ShowFrame { get; set; } = true;

    /// <summary>Text style of the value (null = style's value text at normal size).</summary>
    [Category("Text")]
    public TextStyle? TextStyle { get; set; }

    /// <summary>
    /// Parses typed text into a normalized value for unbound controls (bound controls use <see cref="IParameter.TryParse"/>).
    /// Default: a percentage (0–100).
    /// </summary>
    [Browsable(false)]
    public Func<string, double?>? Parser { get; set; }

    /// <summary><c>true</c> while the in-place editor is open.</summary>
    [Browsable(false)]
    public bool IsTextEditing { get; private set; }

    /// <inheritdoc />
    protected override Size DefaultSize => new(72f, Theme.Metrics.ControlHeight);

    /// <summary>Text style in effect for the value.</summary>
    protected TextStyle EffectiveValueTextStyle(VectorStyle style)
        => TextStyle ?? style.ValueText with { Size = Theme.Typography.NormalSize, Align = HorizontalAlignment.Center };

    /// <summary>Label on top, the rest is the value box (no separate value strip).</summary>
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        var r = Bounds;
        label = Rect.Empty;
        value = Rect.Empty;
        if (style.ShowLabel && !string.IsNullOrEmpty(Label))
        {
            float h = style.LabelText.Size * 1.5f;
            label = r.TakeTop(h);
            r = Rect.FromEdges(r.X, r.Y + h, r.Right, r.Bottom);
        }
        widget = r;
    }

    /// <summary>Area of the value text inside the widget.</summary>
    protected virtual Rect ValueArea(Rect widget) => widget.Deflate(new Thickness(4f, 0f));

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        if (ShowFrame)
        {
            float radius = MathF.Min(Theme.Metrics.CornerRadius, widget.Height * 0.5f);
            dc.FillRoundedRect(widget, radius, style.Background);
            var frame = IsFocused || _dragStarted ? style.Accent : IsMouseOver && IsEffectivelyEnabled ? style.Frame.Lighten(0.12f) : style.Frame;
            if (style.DrawFrame) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, frame, style.FrameThickness);
        }
        if (!IsTextEditing) dc.DrawText(ValueText, ValueArea(widget), EffectiveValueTextStyle(style), style.ValueColor);
    }

    // ---------------------------------------------------------------- editing

    /// <summary>Opens the in-place text editor over the value.</summary>
    public void BeginValueEdit()
    {
        if (Host is null || !IsEditable || !IsEffectivelyEnabled || IsTextEditing) return;
        var style = EffectiveStyle;
        LayoutParts(style, out _, out var widget, out _);
        string initial = Parameter is { } p ? p.ToDisplayString(Value) : (Value * 100.0).ToString("0.##", CultureInfo.InvariantCulture);
        IsTextEditing = true;
        InvalidateVisual();
        Host.BeginTextEdit(widget, initial, CommitText, EndTextEdit, EffectiveValueTextStyle(style));
    }

    private void EndTextEdit()
    {
        IsTextEditing = false;
        InvalidateVisual();
    }

    private bool CommitText(string text)
    {
        if (!TryParseValue(text, out double normalized)) return false; // editor stays open, frame turns red
        EndTextEdit();
        SetValueByUser(normalized);
        return true;
    }

    /// <summary>Parses typed text into a normalized value.</summary>
    protected virtual bool TryParseValue(string text, out double normalized)
    {
        normalized = 0.0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (Parameter is { } p) return p.TryParse(text.Trim(), out normalized);
        if (Parser is not null)
        {
            if (Parser(text) is not { } v) return false;
            normalized = v;
            return true;
        }
        var t = text.Trim().TrimEnd('%');
        if (!InvariantNumber.TryParse(t, out double percent)) return false;
        normalized = Math.Clamp(percent / 100.0, 0.0, 1.0);
        return true;
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left) return;
        if (TryHandleReset(e)) return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            BeginValueEdit();
            return;
        }
        if (AllowDrag) StartDrag(e.Position.Y);
    }

    /// <summary>Prepares a vertical drag (the gesture starts on the first movement).</summary>
    protected void StartDrag(float y)
    {
        _dragging = true;
        _dragStarted = false;
        _dragStartY = y;
        _dragStartValue = Value;
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || !IsMouseCaptured) return;
        float dy = _dragStartY - e.Position.Y;
        if (!_dragStarted)
        {
            if (MathF.Abs(dy) < 2f) return;
            _dragStarted = true;
            IsPressed = true;
            BeginGesture();
        }
        double delta = dy / MathF.Max(10f, DragSensitivity) * (e.Shift ? FineFactor : 1f);
        if (StepCount > 0)
        {
            // Discrete values: one step per 12 DIPs feels better than the continuous mapping.
            delta = Math.Round(dy / 12.0) / StepCount;
        }
        UpdateGesture(_dragStartValue + delta);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        if (_dragStarted)
        {
            _dragStarted = false;
            IsPressed = false;
            EndGesture();
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        _dragging = false;
        _dragStarted = false;
        base.OnCaptureLost();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && IsEditable)
        {
            BeginValueEdit();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}

/// <summary>
/// Numeric box: drag vertically to change (Shift = fine), wheel, arrows, double-click or Enter to type,
/// optional − / + step buttons with auto-repeat. iPlug2 <c>IVNumberBoxControl</c>.
/// </summary>
public class NumberBox : ValueDisplay
{
    private const float ButtonWidth = 18f;
    private int _pressedButton; // -1 = minus, +1 = plus
    private RepeatTicker? _repeat;

    /// <summary>Creates a number box.</summary>
    public NumberBox()
    {
        AllowDrag = true;
        Cursor = StandardCursor.SizeNS;
    }

    /// <summary>Shows the − / + buttons.</summary>
    [Category("Appearance")]
    public bool ShowStepButtons { get; set; } = true;

    /// <summary>Normalized step of the buttons for continuous values (discrete values step by one state).</summary>
    [Category("Value")]
    public double ButtonStep { get; set; } = 0.01;

    /// <inheritdoc />
    protected override Size DefaultSize => new(ShowStepButtons ? 96f : 64f, Theme.Metrics.ControlHeight);

    private static Rect MinusRect(Rect widget) => widget.TakeLeft(ButtonWidth);

    private static Rect PlusRect(Rect widget) => widget.TakeRight(ButtonWidth);

    private Rect CurrentWidget()
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        return widget;
    }

    /// <inheritdoc />
    protected override Rect ValueArea(Rect widget)
        => ShowStepButtons ? Rect.FromEdges(widget.X + ButtonWidth, widget.Y, widget.Right - ButtonWidth, widget.Bottom) : base.ValueArea(widget);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        base.RenderVector(dc, widget, style);
        if (!ShowStepButtons) return;
        var p = Theme.Palette;
        foreach (int dir in (ReadOnlySpan<int>)[-1, 1])
        {
            var r = dir < 0 ? MinusRect(widget) : PlusRect(widget);
            bool pressed = _pressedButton == dir;
            if (pressed) dc.FillRoundedRect(r.Inflate(-2f), 3f, style.Pressed);
            var c = r.Center;
            var color = IsEffectivelyEnabled ? (pressed ? style.Accent : p.TextDim) : p.TextDisabled;
            dc.DrawLine(new Point(c.X - 3.5f, c.Y), new Point(c.X + 3.5f, c.Y), color, 1.5f, StrokeStyle.Round);
            if (dir > 0) dc.DrawLine(new Point(c.X, c.Y - 3.5f), new Point(c.X, c.Y + 3.5f), color, 1.5f, StrokeStyle.Round);
            float sx = dir < 0 ? r.Right : r.X;
            dc.DrawLine(new Point(sx, widget.Y + 5f), new Point(sx, widget.Bottom - 5f), style.Frame.WithAlpha(0.6f), 1f);
        }
    }

    private double StepSize => StepCount > 0 ? 1.0 / StepCount : ButtonStep;

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        if (ShowStepButtons && e.Button == MouseButton.Left && IsEffectivelyEnabled && !e.Control)
        {
            var widget = CurrentWidget();
            int dir = MinusRect(widget).Contains(e.Position) ? -1 : PlusRect(widget).Contains(e.Position) ? 1 : 0;
            if (dir != 0)
            {
                e.Handled = true;
                _pressedButton = dir;
                SetValueByUser(Value + dir * StepSize);
                _repeat ??= new RepeatTicker(this);
                _repeat.Start();
                Host?.AddTicker(_repeat);
                InvalidateVisual();
                return;
            }
        }
        base.OnMouseDown(e);
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressedButton != 0)
        {
            StopRepeat();
            e.Handled = true;
            return;
        }
        base.OnMouseUp(e);
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        StopRepeat();
        base.OnCaptureLost();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        StopRepeat();
        base.OnDetached();
    }

    private void StopRepeat()
    {
        if (_pressedButton == 0) return;
        _pressedButton = 0;
        if (_repeat is not null) Host?.RemoveTicker(_repeat);
        InvalidateVisual();
    }

    private sealed class RepeatTicker(NumberBox owner) : ITickable
    {
        private double _elapsed;
        private double _next;

        public void Start()
        {
            _elapsed = 0;
            _next = 0.4;
        }

        public bool Tick(double elapsedSeconds)
        {
            if (owner._pressedButton == 0) return false;
            _elapsed += elapsedSeconds;
            while (_elapsed >= _next)
            {
                owner.SetValueByUser(owner.Value + owner._pressedButton * owner.StepSize);
                _next += 0.06;
            }
            return true;
        }
    }
}
