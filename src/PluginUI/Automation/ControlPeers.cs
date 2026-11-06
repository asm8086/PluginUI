// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI.Automation;

/// <summary>Root of the accessibility tree of a host: the root element and the open popups.</summary>
internal sealed class HostAutomationPeer(UIHost host) : AutomationPeer
{
    public override UIHost? Host => host;

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    public override string Name => host.AutomationName ?? string.Empty;

    public override string? ClassName => "PluginUI.Host";

    public override bool IsKeyboardFocusable => true;

    public override Rect BoundingRectangle
    {
        get
        {
            var size = host.LogicalSize;
            return new Rect(0f, 0f, size.Width, size.Height);
        }
    }

    public override bool IsOffscreen => false;

    public override AutomationPeer? GetParent() => null;

    protected override void CollectChildren(List<AutomationPeer> children)
    {
        var popups = host.Popups;
        int firstPopup = 0;
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            if (popups[i].IsModal) { firstPopup = i; break; }
        }
        // A modal popup hides what is below it, as it does for the mouse.
        bool modal = firstPopup > 0 || popups.Count > 0 && popups[0].IsModal;
        if (!modal) ElementAutomationPeer.CollectElementPeers(host.Root, children);
        for (int i = firstPopup; i < popups.Count; i++) ElementAutomationPeer.CollectElementPeers(popups[i], children);
    }

    public override void SetFocus() => host.RequestKeyboardFocus();
}

// =====================================================================================
// Buttons
// =====================================================================================

/// <summary>Peer of a <see cref="Button"/>: a button that can be invoked.</summary>
/// <param name="owner">The button.</param>
public class ButtonAutomationPeer(Button owner) : ElementAutomationPeer<Button>(owner), IInvokePattern
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Button;

    /// <inheritdoc />
    protected override string? GetNameCore() => Owner.Text is { Length: > 0 } text ? text : base.GetNameCore() ?? Owner.Parameter?.Name;

    /// <inheritdoc />
    public void Invoke()
    {
        ThrowIfNotEnabled();
        Owner.PerformClick();
        RaiseEvent(AutomationEvent.Invoked);
    }
}

/// <summary>Peer of a <see cref="Hyperlink"/>.</summary>
/// <param name="owner">The link.</param>
public class HyperlinkAutomationPeer(Hyperlink owner) : ElementAutomationPeer<Hyperlink>(owner), IInvokePattern
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Hyperlink;

    /// <inheritdoc />
    protected override string? GetNameCore() => Owner.Text;

    /// <inheritdoc />
    public override string? HelpText => base.HelpText ?? Owner.Url;

    /// <inheritdoc />
    public void Invoke()
    {
        ThrowIfNotEnabled();
        Owner.Activate();
    }
}

/// <summary>
/// Peer of a two-state value control (<see cref="CheckBox"/>, <see cref="ToggleButton"/>, <see cref="SlideSwitch"/>,
/// two-position <see cref="Switch"/>): a check box or a toggle button.
/// </summary>
/// <param name="owner">The control.</param>
public class ToggleAutomationPeer(ValueControl owner) : ElementAutomationPeer<ValueControl>(owner), ITogglePattern
{
    private ToggleState? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => Owner is CheckBox ? AutomationControlType.CheckBox : AutomationControlType.Button;

    /// <inheritdoc />
    protected override string? GetNameCore()
    {
        string? text = Owner switch
        {
            CheckBox c => c.Text,
            ToggleButton t => t.Text,
            SlideSwitch s => s.Text,
            _ => null,
        };
        return text is { Length: > 0 } ? text : base.GetNameCore() ?? Owner.Parameter?.Name;
    }

    /// <inheritdoc />
    public override string? ItemStatus => Owner.IsMidiLearning ? Strings.MidiLearnWaiting : null;

    /// <inheritdoc />
    public ToggleState ToggleState => Owner.Value >= 0.5 ? ToggleState.On : ToggleState.Off;

    /// <inheritdoc />
    public void Toggle()
    {
        ThrowIfNotEnabled();
        Owner.SetValueFromAutomation(ToggleState == ToggleState.On ? 0.0 : 1.0);
    }

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        var state = ToggleState;
        if (state == _reported) return;
        RaisePropertyChanged(AutomationProperty.ToggleState, _reported ?? (state == ToggleState.On ? ToggleState.Off : ToggleState.On), state);
        _reported = state;
    }
}

// =====================================================================================
// Continuous values
// =====================================================================================

/// <summary>
/// Peer of a value control with a range (<see cref="Knob"/>, <see cref="Slider"/>, wheels, number boxes): a slider
/// with the range value pattern and the formatted value ("-6.0 dB") as text value.
/// </summary>
/// <remarks>
/// A bound control reports the plain value of its parameter (<see cref="IParameter.ToPlain"/>, e.g. 20..20000 Hz);
/// an unbound one reports 0..100 %. Screen readers speak the text value, with units.
/// </remarks>
/// <param name="owner">The control.</param>
public class RangeValueAutomationPeer(ValueControl owner) : ElementAutomationPeer<ValueControl>(owner), IRangeValuePattern, IValuePattern
{
    private string? _reportedText;
    private double _reportedValue = double.NaN;

    /// <inheritdoc />
    public override AutomationControlType ControlType => Owner is ValueDisplay ? AutomationControlType.Spinner : AutomationControlType.Slider;

    /// <inheritdoc />
    public override string? LocalizedControlType => Owner is Knob ? Strings.AutomationKnob : null;

    /// <inheritdoc />
    protected override string? GetNameCore() => base.GetNameCore() ?? Owner.Parameter?.Name;

    /// <inheritdoc />
    public override string? ItemStatus => Owner.IsMidiLearning ? Strings.MidiLearnWaiting : null;

    /// <inheritdoc />
    public override AutomationOrientation Orientation => Owner switch
    {
        Slider s => s.Orientation == PluginUI.Orientation.Horizontal ? AutomationOrientation.Horizontal : AutomationOrientation.Vertical,
        WheelControl => AutomationOrientation.Vertical,
        _ => AutomationOrientation.None,
    };

    private bool UsesPlainRange(out double low, out double high)
    {
        low = high = 0.0;
        if (Owner.Parameter is not { } p) return false;
        double a = p.ToPlain(0.0), b = p.ToPlain(1.0);
        if (!double.IsFinite(a) || !double.IsFinite(b) || a == b) return false;
        (low, high) = a < b ? (a, b) : (b, a);
        return true;
    }

    private double ToRange(double normalized)
        => UsesPlainRange(out _, out _) ? Owner.Parameter!.ToPlain(normalized) : normalized * 100.0;

    private double StepNormalized(int multiplier)
    {
        int steps = Owner.StepCount;
        return steps > 0 ? multiplier / (double)steps : Owner.WheelStep * multiplier;
    }

    private double RangeDelta(double normalizedDelta)
    {
        double v = Owner.Value;
        double from = Math.Clamp(v, 0.0, 1.0 - normalizedDelta);
        return Math.Abs(ToRange(Math.Min(1.0, from + normalizedDelta)) - ToRange(from));
    }

    // ---------------------------------------------------------------- range value

    double IRangeValuePattern.Value => ToRange(Owner.Value);

    /// <inheritdoc />
    public double Minimum => UsesPlainRange(out double low, out _) ? low : 0.0;

    /// <inheritdoc />
    public double Maximum => UsesPlainRange(out _, out double high) ? high : 100.0;

    /// <inheritdoc />
    public double SmallChange => RangeDelta(StepNormalized(1));

    /// <inheritdoc />
    public double LargeChange => RangeDelta(Math.Min(1.0, StepNormalized(10)));

    bool IRangeValuePattern.IsReadOnly => !Owner.IsEffectivelyEnabled;

    /// <inheritdoc />
    public void SetValue(double value)
    {
        ThrowIfNotEnabled();
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        double normalized = UsesPlainRange(out double low, out double high)
            ? Owner.Parameter!.ToNormalized(Math.Clamp(value, low, high))
            : value / 100.0;
        Owner.SetValueFromAutomation(Math.Clamp(normalized, 0.0, 1.0));
    }

    // ---------------------------------------------------------------- text value

    string IValuePattern.Value => Owner.FormatValue(Owner.Value);

    bool IValuePattern.IsReadOnly => !Owner.IsEffectivelyEnabled;

    void IValuePattern.SetValue(string value)
    {
        ThrowIfNotEnabled();
        ArgumentNullException.ThrowIfNull(value);
        if (Owner.Parameter is { } p && p.TryParse(value.Trim(), out double n))
        {
            Owner.SetValueFromAutomation(n);
            return;
        }
        string digits = value.Trim().TrimEnd('%').Trim();
        if (double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || double.TryParse(digits, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
        {
            SetValue(number);
            return;
        }
        throw new ArgumentException("The text is not a valid value.", nameof(value));
    }

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        double value = ToRange(Owner.Value);
        string text = Owner.FormatValue(Owner.Value);
        if (value != _reportedValue)
        {
            RaisePropertyChanged(AutomationProperty.RangeValue, double.IsNaN(_reportedValue) ? null : _reportedValue, value);
            _reportedValue = value;
        }
        if (text != _reportedText)
        {
            RaisePropertyChanged(AutomationProperty.Value, _reportedText, text);
            _reportedText = text;
        }
    }
}

/// <summary>Peer of a <see cref="ProgressBar"/>: a read-only range 0..100.</summary>
/// <param name="owner">The progress bar.</param>
public class ProgressBarAutomationPeer(ProgressBar owner) : ElementAutomationPeer<ProgressBar>(owner), IRangeValuePattern
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.ProgressBar;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    /// <inheritdoc />
    public double Value => Owner.IsIndeterminate ? 0.0 : Owner.Value * 100.0;

    /// <inheritdoc />
    public double Minimum => 0.0;

    /// <inheritdoc />
    public double Maximum => 100.0;

    /// <inheritdoc />
    public double SmallChange => 0.0;

    /// <inheritdoc />
    public double LargeChange => 0.0;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(double value) => throw new InvalidOperationException("A progress bar is read-only.");
}

// =====================================================================================
// Text
// =====================================================================================

/// <summary>Peer of a text element (<see cref="Label"/>, <see cref="DialogText"/>): static text named by its content.</summary>
/// <param name="owner">The element.</param>
/// <param name="text">Reads the current text.</param>
public class TextAutomationPeer(UIElement owner, Func<string?> text) : ElementAutomationPeer<UIElement>(owner)
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Text;

    /// <inheritdoc />
    protected override string? GetNameCore() => text();

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;
}

/// <summary>Peer of a <see cref="TextBox"/>: an edit field with a text value.</summary>
/// <param name="owner">The text box.</param>
public class TextBoxAutomationPeer(TextBox owner) : ElementAutomationPeer<TextBox>(owner), IValuePattern
{
    private string? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Edit;

    /// <inheritdoc />
    protected override string? GetNameCore() => base.GetNameCore() ?? Owner.Placeholder;

    /// <inheritdoc />
    public override string? HelpText => base.HelpText ?? (Owner.Placeholder is { Length: > 0 } p && p != Name ? p : null);

    /// <inheritdoc />
    public override string? ItemStatus => Owner.IsInvalid ? Strings.AutomationInvalidEntry : null;

    /// <inheritdoc />
    public string Value => Owner.Text;

    /// <inheritdoc />
    public bool IsReadOnly => Owner.IsReadOnly || !Owner.IsEffectivelyEnabled;

    /// <inheritdoc />
    public void SetValue(string value)
    {
        ThrowIfNotEnabled();
        if (Owner.IsReadOnly) throw new InvalidOperationException("The text box is read-only.");
        Owner.Text = value ?? string.Empty;
    }

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        string text = Owner.Text;
        if (text == _reported) return;
        RaisePropertyChanged(AutomationProperty.Value, _reported, text);
        _reported = text;
    }
}

// =====================================================================================
// Containers
// =====================================================================================

/// <summary>Peer of a <see cref="GroupBox"/>: a group named by its header, expandable when collapsible.</summary>
/// <param name="owner">The group box.</param>
public class GroupBoxAutomationPeer(GroupBox owner) : ElementAutomationPeer<GroupBox>(owner), IExpandCollapsePattern
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    protected override string? GetNameCore() => Owner.Header;

    /// <inheritdoc />
    public override object? GetPattern(AutomationPattern pattern)
        => pattern == AutomationPattern.ExpandCollapse && !Owner.IsCollapsible ? null : base.GetPattern(pattern);

    /// <inheritdoc />
    public ExpandCollapseState ExpandCollapseState => Owner.IsCollapsed ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded;

    /// <inheritdoc />
    public void Expand()
    {
        ThrowIfNotEnabled();
        Owner.IsCollapsed = false;
    }

    /// <inheritdoc />
    public void Collapse()
    {
        ThrowIfNotEnabled();
        Owner.IsCollapsed = true;
    }
}

/// <summary>Peer of a <see cref="Popup"/>: a pane (a dialog for <see cref="Dialog"/>).</summary>
/// <param name="owner">The popup.</param>
public class PopupAutomationPeer(Popup owner) : ElementAutomationPeer<Popup>(owner)
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Pane;

    /// <inheritdoc />
    protected override string? GetNameCore() => Owner is Dialog d ? d.Title : null;

    /// <inheritdoc />
    public override bool IsDialog => Owner is Dialog || Owner.IsModal;

    /// <inheritdoc />
    public override string? LocalizedControlType => IsDialog ? Strings.AutomationDialog : null;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;
}

/// <summary>Peer of a <see cref="ToolBar"/>.</summary>
/// <param name="owner">The tool bar.</param>
public class ToolBarAutomationPeer(ToolBar owner) : ElementAutomationPeer<ToolBar>(owner)
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.ToolBar;

    /// <inheritdoc />
    public override AutomationOrientation Orientation => AutomationOrientation.Horizontal;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;
}

/// <summary>Peer of a <see cref="ToolBarButton"/>: a button running its command (a toggle for checkable commands).</summary>
/// <param name="owner">The tool bar button.</param>
public class ToolBarButtonAutomationPeer(ToolBarButton owner) : ElementAutomationPeer<ToolBarButton>(owner), IInvokePattern, ITogglePattern
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Button;

    /// <inheritdoc />
    protected override string? GetNameCore() => Owner.Command.DisplayText;

    /// <inheritdoc />
    public override string? HelpText => base.HelpText ?? Owner.Command.Description;

    /// <inheritdoc />
    public override string? AcceleratorKey => Owner.Command.Gesture?.ToDisplayString(Strings);

    /// <inheritdoc />
    public override bool IsEnabled => Owner.CanRun;

    /// <inheritdoc />
    public override object? GetPattern(AutomationPattern pattern) => pattern switch
    {
        AutomationPattern.Toggle => Owner.Command.IsCheckable ? this : null,
        _ => base.GetPattern(pattern),
    };

    /// <inheritdoc />
    public void Invoke()
    {
        ThrowIfNotEnabled();
        Owner.Command.Execute();
        RaiseEvent(AutomationEvent.Invoked);
    }

    /// <inheritdoc />
    public ToggleState ToggleState => Owner.Command.IsChecked ? ToggleState.On : ToggleState.Off;

    /// <inheritdoc />
    public void Toggle() => Invoke();
}

/// <summary>Peer of a separator line.</summary>
/// <param name="owner">The separator.</param>
public class SeparatorAutomationPeer(UIElement owner) : ElementAutomationPeer<UIElement>(owner)
{
    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Separator;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    /// <inheritdoc />
    public override bool IsContentElement => false;
}
