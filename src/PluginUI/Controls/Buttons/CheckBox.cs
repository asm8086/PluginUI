// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Check box with a caption on the right. Clicking the box or the caption toggles the value.</summary>
public class CheckBox : ValueControl
{
    private float _anim;
    private Animation? _animation;

    /// <summary>Creates a check box.</summary>
    public CheckBox()
    {
        StepCount = 1;
    }

    /// <summary>Creates a check box with a caption.</summary>
    public CheckBox(string text, bool isChecked = false) : this()
    {
        Text = text;
        IsChecked = isChecked;
        _anim = isChecked ? 1f : 0f;
    }

    /// <summary>Caption (null = <see cref="Control.Label"/> or the parameter name).</summary>
    [Category("Text")]
    public string? Text { get; set; }

    /// <summary>Box size in DIPs.</summary>
    [Category("Layout")]
    public float BoxSize { get; set; } = 15f;

    /// <summary><c>true</c> when checked.</summary>
    [Category("Value")]
    public bool IsChecked
    {
        get => Value >= 0.5;
        set => Value = value ? 1.0 : 0.0;
    }

    /// <inheritdoc />
    public override string? ValueText => null;

    private string? Caption => Text ?? Label ?? Parameter?.Name;

    private TextStyle CaptionStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Left };

    /// <inheritdoc />
    protected override Size DefaultSize
    {
        get
        {
            float w = BoxSize + (string.IsNullOrEmpty(Caption) ? 0f : 8f + TextMeasurer.Measure(Caption!, CaptionStyle).Width);
            return new Size(MathF.Ceiling(w) + 2f, MathF.Max(BoxSize + 4f, 20f));
        }
    }

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    /// <inheritdoc />
    protected override void OnValueChanged()
    {
        base.OnValueChanged();
        float target = IsChecked ? 1f : 0f;
        _animation?.Cancel();
        if (Host is not { IsAttached: true }) { _anim = target; InvalidateVisual(); return; }
        _animation = Host.Animator.Animate(_anim, target, 0.12, v => { _anim = v; InvalidateVisual(); }, Easing.CubicOut);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _anim = IsChecked ? 1f : 0f;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var box = new Rect(widget.X + 1f, widget.Center.Y - BoxSize * 0.5f, BoxSize, BoxSize);
        float radius = MathF.Min(4f, BoxSize * 0.25f);
        bool enabled = IsEffectivelyEnabled;
        dc.FillRoundedRect(box, radius, style.Background);
        if (_anim > 0.001f)
        {
            dc.FillRoundedRect(box, radius, (enabled ? style.Accent : p.TextDisabled).WithAlpha(_anim));
        }
        var frame = IsChecked ? style.Accent : IsMouseOver ? style.Frame.Lighten(0.25f) : style.Frame.Lighten(0.1f);
        dc.DrawRoundedRect(box.Inflate(-0.5f), radius, enabled ? frame : p.TextDisabled, 1f);
        if (IsMouseOver && enabled) dc.FillRoundedRect(box, radius, style.Hover);
        if (_anim > 0.01f)
        {
            float k = 0.6f + 0.4f * _anim;
            Glyphs.Check(dc, box.Center, BoxSize * 0.62f * k, style.Accent.Contrasting.WithAlpha(_anim), 1.8f);
        }
        if (!string.IsNullOrEmpty(Caption))
        {
            var tr = Rect.FromEdges(box.Right + 8f, widget.Y, widget.Right, widget.Bottom);
            dc.DrawText(Caption, tr, CaptionStyle, enabled ? p.Text : p.TextDisabled);
        }
    }

    /// <inheritdoc />
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        if (IsFocused && Host?.ShowFocusRings == true)
        {
            var box = new Rect(Bounds.X + 1f, Bounds.Center.Y - BoxSize * 0.5f, BoxSize, BoxSize);
            dc.DrawRoundedRect(box.Inflate(2f), 5f, Theme.Palette.Focus, Theme.Metrics.FocusWidth);
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        e.Handled = true;
        IsPressed = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsPressed) return;
        IsPressed = false;
        if (Bounds.Contains(e.Position)) SetValueByUser(IsChecked ? 0.0 : 1.0);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space && IsEffectivelyEnabled)
        {
            e.Handled = true;
            if (!e.IsRepeat) SetValueByUser(IsChecked ? 0.0 : 1.0);
            return;
        }
        base.OnKeyDown(e);
    }
}
