// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// Control editing a normalized value [0, 1], optionally bound to an <see cref="IParameter"/>.
/// Provides the gesture protocol, quantization, default value, fine adjustment and the value bubble.
/// </summary>
/// <remarks>
/// <para>Bound (<see cref="Parameter"/> set): the parameter is the source of truth; user edits go through
/// <see cref="IParameter.BeginEdit"/> / <see cref="IParameter.PerformEdit"/> / <see cref="IParameter.EndEdit"/>
/// and parameter changes (host automation, presets) repaint the control.</para>
/// <para>Unbound: the value is stored locally and <see cref="ValueChanged"/> notifies the application.</para>
/// </remarks>
/// <example>
/// Unbound use (UI state that is not a plug-in parameter):
/// <code>
/// var mix = new Slider(Orientation.Horizontal) { Label = "MIX", Value = 0.5 };
/// var readout = new Label("50 %");
/// mix.ValueChanged += (_, _) => readout.Text = InvariantNumber.Format(mix.Value * 100, "0") + " %";
/// </code>
/// </example>
public abstract class ValueControl : Control
{
    private IParameter? _parameter;
    private double _localValue;
    private double? _defaultValue;
    private int _localSteps;
    private bool _inGesture;
    private bool _subscribed;

    /// <summary>Bound parameter (null = local value).</summary>
    [Category("Value")]
    public IParameter? Parameter
    {
        get => _parameter;
        set
        {
            if (ReferenceEquals(_parameter, value)) return;
            EndGesture(); // close the gesture on the old parameter before rebinding
            Unsubscribe();
            _parameter = value;
            if (Host is not null) Subscribe();
            if (Label is null && value is not null) Label = value.Name;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Current normalized value. Setting it on a bound control performs a complete single-step edit gesture.
    /// </summary>
    [Category("Value")]
    public double Value
    {
        get => _parameter?.NormalizedValue ?? _localValue;
        set
        {
            value = Quantize(value);
            if (_parameter is not null)
            {
                if (_parameter.NormalizedValue != value) _parameter.SetValue(value);
                return;
            }
            if (_localValue == value) return;
            _localValue = value;
            OnValueChanged();
        }
    }

    /// <summary>Default normalized value (double-click / Ctrl+click reset).</summary>
    [Category("Value")]
    public double DefaultValue { get => _defaultValue ?? _parameter?.DefaultNormalizedValue ?? 0.0; set => _defaultValue = Math.Clamp(value, 0.0, 1.0); }

    /// <summary>0 = continuous; otherwise number of discrete steps (parameter's when bound).</summary>
    [Category("Value")]
    public int StepCount { get => _parameter?.StepCount ?? _localSteps; set { _localSteps = Math.Max(0, value); InvalidateVisual(); } }

    /// <summary>Custom value formatter (normalized → text). Defaults to the parameter's text, or a percentage.</summary>
    [Browsable(false)]
    public Func<double, string>? Formatter { get; set; }

    /// <summary>Pixels of mouse travel for the full range (drag controls).</summary>
    [Category("Behavior")]
    public float DragSensitivity { get; set; } = 200f;

    /// <summary>Sensitivity multiplier while Shift is held.</summary>
    [Category("Behavior")]
    public float FineFactor { get; set; } = 0.1f;

    /// <summary>Value change per wheel notch (continuous values).</summary>
    [Category("Behavior")]
    public double WheelStep { get; set; } = 0.02;

    /// <summary>
    /// Double-click resets to <see cref="DefaultValue"/> (default <c>true</c>). On controls that accept typed values,
    /// a double-click on the value text opens the text box instead (see <see cref="TextEntryTriggers"/>).
    /// </summary>
    [Category("Behavior")]
    public bool ResetOnDoubleClick { get; set; } = true;

    /// <summary>
    /// Actions that open the in-place text box to type a value (default <see cref="PluginUI.TextEntryTriggers.Default"/>).
    /// Only used by controls that support typed values (<see cref="SupportsTextEntry"/>), such as <see cref="Knob"/> and <see cref="Slider"/>.
    /// </summary>
    [Category("Behavior")]
    public TextEntryTriggers TextEntryTriggers { get; set; } = TextEntryTriggers.Default;

    /// <summary>
    /// Accepts <see cref="ModulationSource"/> drops from a <see cref="ModulationSourceButton"/>: the control lights up
    /// while a source hovers it and raises <see cref="ModulationDropped"/> on drop.
    /// </summary>
    [Category("Behavior")]
    public bool AcceptsModulation { get; set; }

    /// <summary><c>true</c> while a modulation source is dragged over this control.</summary>
    [Browsable(false)]
    public bool IsModulationDropTarget { get; private set; }

    /// <summary>Raised when a modulation source is dropped on this control (<see cref="AcceptsModulation"/>).</summary>
    public event Action<ValueControl, ModulationSource>? ModulationDropped;

    /// <inheritdoc />
    protected internal override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (!AcceptsModulation || e.Data is not ModulationSource) return;
        e.Accept = true;
        if (!IsModulationDropTarget)
        {
            IsModulationDropTarget = true;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnDragLeave()
    {
        base.OnDragLeave();
        if (!IsModulationDropTarget) return;
        IsModulationDropTarget = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (AcceptsModulation && e.Data is ModulationSource source) ModulationDropped?.Invoke(this, source);
        else e.Accept = false;
    }

    /// <summary><c>true</c> while the bound parameter waits for a MIDI controller (<see cref="UIHost.MidiLearn"/>).</summary>
    [Browsable(false)]
    public bool IsMidiLearning => Parameter is { } p && Host?.MidiLearn?.IsLearning(p) == true;

    /// <inheritdoc />
    /// <remarks>While MIDI learning, shows <see cref="UIStrings.MidiLearnWaiting"/>.</remarks>
    protected internal override string? GetToolTipText() => IsMidiLearning ? Strings.MidiLearnWaiting : base.GetToolTipText();

    /// <inheritdoc />
    /// <remarks>Adds the modulation drop highlight and the MIDI learn outline.</remarks>
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        base.OnRenderOverlay(dc);
        if (IsModulationDropTarget)
        {
            dc.DrawRoundedRect(Bounds.Inflate(2f), 6f, Theme.Palette.AccentAlt, 2f);
            dc.FillRoundedRect(Bounds.Inflate(2f), 6f, Theme.Palette.AccentAlt.WithAlpha(0.12f));
        }
        if (IsMidiLearning)
        {
            var r = Bounds.Inflate(2f);
            dc.DrawRoundedRect(r, 6f, Theme.Palette.Accent, 2f, StrokeStyle.Dashed);
            var badge = new Rect(r.Right - 30f, r.Y - 6f, 30f, 12f);
            dc.FillRoundedRect(badge, 3f, Theme.Palette.Accent);
            dc.DrawText("MIDI", badge, Theme.Typography.Small with { Size = 8f, Align = HorizontalAlignment.Center, Ellipsis = false }, Theme.Palette.Accent.Contrasting);
        }
    }

    /// <summary><c>true</c> if this control can open a text box to type its value (see <see cref="BeginTextEntry"/>).</summary>
    protected virtual bool SupportsTextEntry => false;

    /// <summary>
    /// Opens the in-place text box to type a value. Does nothing on controls that do not support typed values
    /// (<see cref="SupportsTextEntry"/> is <c>false</c>) or when the control is not attached to a host.
    /// </summary>
    public virtual void BeginTextEntry()
    {
    }

    /// <summary>Ctrl+click resets to <see cref="DefaultValue"/>.</summary>
    [Category("Behavior")]
    public bool ResetOnControlClick { get; set; } = true;

    /// <summary>Shows a bubble with the value while the user edits it.</summary>
    [Category("Appearance")]
    public bool ShowValueBubble { get; set; }

    /// <summary><c>true</c> during a user gesture.</summary>
    public bool IsEditing => _inGesture;

    /// <summary>Raised when the value changes (user, code, or bound parameter).</summary>
    public event Action<ValueControl, double>? ValueChanged;

    /// <summary>Raised when a user gesture starts.</summary>
    public event Action<ValueControl>? GestureStarted;

    /// <summary>Raised when a user gesture ends.</summary>
    public event Action<ValueControl>? GestureEnded;

    /// <inheritdoc />
    public override string? ValueText => FormatValue(Value);

    /// <summary>Index of the current discrete state.</summary>
    public int StateIndex => StepCount <= 0 ? 0 : (int)Math.Round(Math.Clamp(Value, 0.0, 1.0) * StepCount);

    /// <summary>Formats a normalized value for display (with units).</summary>
    public string FormatValue(double normalized)
    {
        if (Formatter is not null) return Formatter(normalized);
        if (_parameter is not null) return _parameter.ToDisplayStringWithUnits(normalized);
        return (normalized * 100.0).ToString("F0", CultureInfo.InvariantCulture) + " %";
    }

    /// <summary>Rounds to the nearest step for discrete values.</summary>
    public double Quantize(double normalized)
    {
        normalized = Math.Clamp(normalized, 0.0, 1.0);
        int steps = StepCount;
        return steps > 0 ? Math.Round(normalized * steps) / steps : normalized;
    }

    /// <inheritdoc />
    protected override SkinState GetSkinState(Rect widgetBounds) => new(widgetBounds, Value, StateIndex, IsMouseOver, IsPressed, IsEffectivelyEnabled, IsFocused);

    // ---------------------------------------------------------------- gestures (for subclasses)

    /// <summary>Starts a user edit gesture.</summary>
    protected void BeginGesture()
    {
        if (_inGesture) return;
        _inGesture = true;
        _parameter?.BeginEdit();
        GestureStarted?.Invoke(this);
        if (ShowValueBubble) Host?.ShowValueBubble(this, ValueText ?? string.Empty);
    }

    /// <summary>Changes the value during a gesture (quantized, clamped).</summary>
    protected void UpdateGesture(double normalized)
    {
        normalized = Quantize(normalized);
        if (_parameter is not null)
        {
            if (!_inGesture) { _parameter.SetValue(normalized); return; }
            if (_parameter.NormalizedValue != normalized) _parameter.PerformEdit(normalized);
        }
        else if (_localValue != normalized)
        {
            _localValue = normalized;
            OnValueChanged();
        }
        if (_inGesture && ShowValueBubble) Host?.ShowValueBubble(this, ValueText ?? string.Empty);
    }

    /// <summary>Ends the user edit gesture.</summary>
    protected void EndGesture()
    {
        if (!_inGesture) return;
        _inGesture = false;
        _parameter?.EndEdit();
        GestureEnded?.Invoke(this);
        if (ShowValueBubble) Host?.HideValueBubble(this);
    }

    /// <summary>Complete single-step gesture (click, wheel, keyboard, reset).</summary>
    protected void SetValueByUser(double normalized)
    {
        bool owns = !_inGesture;
        if (owns) BeginGesture();
        UpdateGesture(normalized);
        if (owns) EndGesture();
    }

    /// <summary>Resets to <see cref="DefaultValue"/> as a user gesture.</summary>
    public void ResetToDefault() => SetValueByUser(DefaultValue);

    /// <summary>Called after every value change; raises <see cref="ValueChanged"/> and repaints.</summary>
    protected virtual void OnValueChanged()
    {
        ValueChanged?.Invoke(this, Value);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- default keyboard / wheel behavior

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        int steps = StepCount;
        double delta = steps > 0 ? Math.Sign(e.WheelDelta) / (double)steps : e.WheelDelta * WheelStep * (e.Shift ? FineFactor : 1.0);
        SetValueByUser(Value + delta);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        int steps = StepCount;
        double step = steps > 0 ? 1.0 / steps : (e.Shift ? WheelStep * FineFactor : WheelStep);
        switch (e.Key)
        {
            case Key.Up or Key.Right: SetValueByUser(Value + step); e.Handled = true; break;
            case Key.Down or Key.Left: SetValueByUser(Value - step); e.Handled = true; break;
            case Key.PageUp: SetValueByUser(Value + step * 10); e.Handled = true; break;
            case Key.PageDown: SetValueByUser(Value - step * 10); e.Handled = true; break;
            case Key.Home: SetValueByUser(0.0); e.Handled = true; break;
            case Key.End: SetValueByUser(1.0); e.Handled = true; break;
            case Key.Delete or Key.Back: ResetToDefault(); e.Handled = true; break;
            case Key.Enter when SupportsTextEntry && (TextEntryTriggers & TextEntryTriggers.EnterKey) != 0:
                BeginTextEntry();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Handles the text-entry shortcuts selected by <see cref="TextEntryTriggers"/> (double-click on the value text,
    /// Alt+click, right double-click…). Returns <c>true</c> if the press opened the text box.
    /// Call from <see cref="UIElement.OnMouseDown"/> overrides before <see cref="TryHandleReset"/>.
    /// </summary>
    /// <param name="e">The mouse-down event.</param>
    /// <returns><c>true</c> if the event was consumed.</returns>
    protected bool TryHandleTextEntry(MouseEventArgs e)
    {
        var triggers = TextEntryTriggers;
        if (!SupportsTextEntry || triggers == TextEntryTriggers.None || !IsEffectivelyEnabled) return false;
        bool open = e.Button switch
        {
            MouseButton.Left when e.ClickCount == 2 && (triggers & TextEntryTriggers.DoubleClickAnywhere) != 0 => true,
            MouseButton.Left when e.ClickCount == 2 && (triggers & TextEntryTriggers.DoubleClickOnValue) != 0 && IsOverValueText(e.Position) => true,
            MouseButton.Left when e.Alt && (triggers & TextEntryTriggers.AltClick) != 0 => true,
            MouseButton.Right when e.ClickCount == 2 && (triggers & TextEntryTriggers.RightDoubleClick) != 0 => true,
            _ => false,
        };
        if (!open) return false;
        BeginTextEntry();
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// <c>true</c> if <paramref name="position"/> is over the value text drawn by the control (the value area of
    /// <see cref="Control.LayoutParts"/>). Override for controls that draw their value elsewhere.
    /// </summary>
    /// <param name="position">A point in host coordinates (DIP).</param>
    /// <returns><c>true</c> if the point is over the value text.</returns>
    protected virtual bool IsOverValueText(Point position)
    {
        LayoutParts(EffectiveStyle, out _, out _, out var value);
        return !value.IsEmpty && value.Contains(position);
    }

    /// <summary>
    /// Handles the reset shortcuts (double-click, Ctrl+click). Returns <c>true</c> if the press was consumed.
    /// Call from <see cref="UIElement.OnMouseDown"/> overrides before starting a drag.
    /// </summary>
    protected bool TryHandleReset(MouseEventArgs e)
    {
        if (e.Button != MouseButton.Left) return false;
        if ((ResetOnDoubleClick && e.ClickCount == 2) || (ResetOnControlClick && e.Control))
        {
            ResetToDefault();
            e.Handled = true;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        IsPressed = false;
        EndGesture();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- binding lifecycle

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Subscribe();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        EndGesture();
        Unsubscribe();
        base.OnDetached();
    }

    private void Subscribe()
    {
        if (_subscribed || _parameter is null) return;
        _parameter.Changed += OnParameterChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _parameter is null) return;
        _parameter.Changed -= OnParameterChanged;
        _subscribed = false;
    }

    private void OnParameterChanged(IParameter parameter) => OnValueChanged();
}
