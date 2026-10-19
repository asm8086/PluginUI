// 2026/10/19 17:23:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Shows one of its children at a time, over the whole content area, chosen by <see cref="SelectedIndex"/> or by a
/// bound <see cref="Parameter"/>: for example the settings panel of the oscillator type selected in a menu. Unlike
/// <see cref="TabView"/> it draws no tabs. Equivalent of VSTGUI's <c>UIViewSwitchContainer</c>.
/// </summary>
/// <remarks>
/// <para>With a parameter, the view index follows the parameter value: the step index for a discrete parameter
/// (<see cref="IParameter.StepCount"/> &gt; 0), otherwise the value spread over the children. Indexes beyond the last
/// child show the last child. Setting <see cref="SelectedIndex"/> edits the parameter (one complete gesture).</para>
/// <para>Hidden views stay attached and keep their state (scroll position, text being typed); they are neither drawn
/// nor hit-tested. In PluginUIEditor, change <see cref="SelectedIndex"/> (or the bound preview parameter) to edit
/// each view.</para>
/// </remarks>
/// <example>
/// <code>
/// var panels = new ViewSwitcher { Parameter = Parameters[model.OscillatorType] };
/// panels.Add(sinePanel, sawPanel, wavetablePanel);
/// </code>
/// </example>
public class ViewSwitcher : Panel
{
    private readonly BoundValue _value;
    private int _localIndex;
    private int _shown = -1;

    /// <summary>Creates an empty switcher.</summary>
    public ViewSwitcher()
    {
        _value = new BoundValue(_ => OnValueChanged());
    }

    /// <summary>Parameter choosing the view (null: <see cref="SelectedIndex"/> alone decides).</summary>
    [Category("Value")]
    public IParameter? Parameter
    {
        get => _value.Parameter;
        set => _value.Parameter = value;
    }

    /// <summary>Index of the visible child (-1 when there is no child).</summary>
    [Category("Value")]
    public int SelectedIndex
    {
        get
        {
            if (Children.Count == 0) return -1;
            return Parameter is null ? Math.Clamp(_localIndex, 0, Children.Count - 1) : IndexForValue(Parameter.NormalizedValue, Children.Count);
        }
        set
        {
            if (Parameter is null)
            {
                _localIndex = Math.Max(0, value);
                OnValueChanged();
                return;
            }
            if (Children.Count == 0) return;
            _value.SetByUser(ValueForIndex(Math.Clamp(value, 0, Children.Count - 1), Children.Count));
        }
    }

    /// <summary>Visible child, or null.</summary>
    public UIElement? SelectedView => SelectedIndex is var i and >= 0 ? Children[i] : null;

    /// <summary>Raised when the visible child changes (argument: its index).</summary>
    public event Action<ViewSwitcher, int>? SelectionChanged;

    /// <summary>View index for a normalized value of <see cref="Parameter"/>.</summary>
    /// <param name="normalized">Normalized value in [0, 1].</param>
    /// <param name="viewCount">Number of views.</param>
    /// <returns>The index, in [0, <paramref name="viewCount"/> − 1], or -1 when there is no view.</returns>
    public int IndexForValue(double normalized, int viewCount)
    {
        if (viewCount <= 0) return -1;
        int steps = _value.StepCount > 0 ? _value.StepCount : viewCount - 1;
        int index = (int)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * steps, MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, viewCount - 1);
    }

    private double ValueForIndex(int index, int viewCount)
    {
        int steps = _value.StepCount > 0 ? _value.StepCount : viewCount - 1;
        return steps <= 0 ? 0.0 : Math.Clamp((double)index / steps, 0.0, 1.0);
    }

    private void OnValueChanged()
    {
        UpdateVisibility();
        InvalidateMeasure();
    }

    private void UpdateVisibility()
    {
        int selected = SelectedIndex;
        for (int i = 0; i < Children.Count; i++)
        {
            bool visible = i == selected;
            if (Children[i].IsVisible != visible) Children[i].IsVisible = visible;
        }
        if (selected != _shown)
        {
            _shown = selected;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, selected);
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _value.Attach();
        UpdateVisibility();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        _value.Detach();
        base.OnDetached();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        // Children added or removed since the last pass: only the selected one stays visible.
        UpdateVisibility();
        return base.MeasureOverride(available);
    }
}
