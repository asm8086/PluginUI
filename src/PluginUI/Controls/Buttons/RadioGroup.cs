// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Vertical (or horizontal) list of radio buttons bound to one discrete value. Texts come from <see cref="Items"/>
/// or the parameter's state labels.
/// </summary>
public class RadioGroup : ValueControl
{
    private IReadOnlyList<string>? _items;
    private int _hoverIndex = -1;

    /// <summary>Creates a radio group.</summary>
    public RadioGroup() { }

    /// <summary>Creates a radio group with local items.</summary>
    public RadioGroup(params string[] items) => Items = items;

    /// <summary>Option texts (null = parameter state labels).</summary>
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

    /// <summary>Stacking direction.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Height of a row (vertical) in DIPs.</summary>
    [Category("Layout")]
    public float RowHeight { get; set; } = 22f;

    /// <summary>Selected option index.</summary>
    [Category("Value")]
    public int SelectedIndex
    {
        get => StateIndex;
        set => Value = Count <= 1 ? 0.0 : Math.Clamp(value, 0, Count - 1) / (double)(Count - 1);
    }

    /// <summary>Option labels in effect.</summary>
    public IReadOnlyList<string> Labels => StateLabels.For(this, _items);

    /// <summary>Number of options.</summary>
    public int Count => Math.Max(Labels.Count, StepCount + 1);

    /// <inheritdoc />
    public override string? ValueText => null;

    private TextStyle ItemStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Left };

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            var labels = Labels;
            float extra = EffectiveStyle.ShowLabel && !string.IsNullOrEmpty(Label) ? EffectiveStyle.LabelText.Size * 1.5f : 0f;
            if (Orientation == Orientation.Vertical)
            {
                float w = 40f;
                foreach (var l in labels) w = MathF.Max(w, TextMeasurer.Measure(l, ItemStyle).Width + 28f);
                return new Size(w, RowHeight * Math.Max(1, Count) + extra);
            }
            float total = 0f;
            foreach (var l in labels) total += TextMeasurer.Measure(l, ItemStyle).Width + 36f;
            return new Size(MathF.Max(40f, total), RowHeight + extra);
        }
    }

    private Rect ItemRect(Rect widget, int index)
    {
        if (Orientation == Orientation.Vertical) return new Rect(widget.X, widget.Y + index * RowHeight, widget.Width, RowHeight);
        int n = Math.Max(1, Count);
        float w = widget.Width / n;
        return new Rect(widget.X + index * w, widget.Y, w, widget.Height);
    }

    private int IndexAt(Point p)
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        for (int i = 0; i < Count; i++)
        {
            if (ItemRect(widget, i).Contains(p)) return i;
        }
        return -1;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var labels = Labels;
        int selected = StateIndex;
        bool enabled = IsEffectivelyEnabled;
        for (int i = 0; i < Count; i++)
        {
            var r = ItemRect(widget, i);
            var c = new Point(r.X + 9f, r.Center.Y);
            bool on = i == selected;
            dc.FillCircle(c, 7f, style.Background);
            dc.DrawCircle(c, 6.5f, !enabled ? p.TextDisabled : on ? style.Accent : (i == _hoverIndex && IsMouseOver) ? style.Frame.Lighten(0.3f) : style.Frame.Lighten(0.1f), 1f);
            if (on) dc.FillCircle(c, 3.6f, enabled ? style.Accent : p.TextDisabled);
            if (i == _hoverIndex && IsMouseOver && enabled) dc.FillCircle(c, 10f, style.Hover);
            string text = i < labels.Count ? labels[i] : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            dc.DrawText(text, Rect.FromEdges(r.X + 22f, r.Y, r.Right, r.Bottom), ItemStyle, !enabled ? p.TextDisabled : on ? p.Text : p.TextDim);
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        int index = IndexAt(e.Position);
        if (index < 0) return;
        e.Handled = true;
        SetValueByUser(Count <= 1 ? 0.0 : index / (double)(Count - 1));
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Position);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        // Down/Right = next option (base maps Up to "increase").
        switch (e.Key)
        {
            case Key.Down or Key.Right when IsEffectivelyEnabled:
                SelectByUser(SelectedIndex + 1); e.Handled = true; return;
            case Key.Up or Key.Left when IsEffectivelyEnabled:
                SelectByUser(SelectedIndex - 1); e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }

    private void SelectByUser(int index) => SetValueByUser(Count <= 1 ? 0.0 : Math.Clamp(index, 0, Count - 1) / (double)(Count - 1));
}
