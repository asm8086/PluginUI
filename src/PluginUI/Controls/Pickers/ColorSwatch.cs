// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Color well: shows a color (checkerboard behind translucent colors); clicking opens a
/// <see cref="ColorPickerPopup"/> and updates the color live.
/// </summary>
public class ColorSwatch : Control
{
    private Color _color = Color.White;
    private ColorPickerPopup? _popup;

    /// <summary>Creates a swatch.</summary>
    public ColorSwatch() { }

    /// <summary>Creates a swatch showing <paramref name="color"/>.</summary>
    public ColorSwatch(Color color) => _color = color;

    /// <summary>Color.</summary>
    [Category("Value")]
    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            ColorChanged?.Invoke(this, value);
            InvalidateVisual();
        }
    }

    /// <summary>Lets the picker edit the alpha channel.</summary>
    [Category("Behavior")]
    public bool AllowAlpha { get; set; } = true;

    /// <summary>Opens the picker on click (otherwise the swatch is display-only).</summary>
    [Category("Behavior")]
    public bool IsEditable { get; set; } = true;

    /// <summary>Raised when the color changes (live while picking).</summary>
    public event Action<ColorSwatch, Color>? ColorChanged;

    /// <summary>Raised when a picking session ends with a new color.</summary>
    public event Action<ColorSwatch, Color>? ColorCommitted;

    /// <inheritdoc />
    protected override Size DefaultSize => new(40f, Theme.Metrics.ControlHeight + ControlSizing.LabelAndValueHeight(this));

    /// <inheritdoc />
    public override string? ValueText => null;

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.3f) });
        var inner = widget.Inflate(-3f);
        dc.FillRoundedRect(widget, radius, style.Background);
        if (_color.A < 1f)
        {
            using (dc.Clip(inner)) Glyphs.Checkerboard(dc, inner, 4f);
        }
        dc.FillRoundedRect(inner, MathF.Max(0f, radius - 2f), _color);
        var frame = _popup is not null ? style.Accent : IsMouseOver && IsEditable && IsEffectivelyEnabled ? style.Frame.Lighten(0.3f) : style.Frame;
        dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, frame, _popup is not null ? 1.5f : 1f);
    }

    /// <summary>Opens the color picker popup below the swatch.</summary>
    public void OpenPicker()
    {
        if (Host is null || _popup is not null) return;
        var before = _color;
        var popup = new ColorPickerPopup(_color, AllowAlpha) { Anchor = Bounds, Placement = PopupPlacement.Below };
        popup.ColorChanged += (_, c) => Color = c;
        popup.Committed += (_, c) => { if (c != before) ColorCommitted?.Invoke(this, c); };
        popup.Closed += _ => { _popup = null; InvalidateVisual(); };
        _popup = popup;
        Host.ShowPopup(popup);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled || !IsEditable) return;
        e.Handled = true;
        OpenPicker();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled || !IsEditable) return;
        if (e.Key is Key.Space or Key.Enter)
        {
            OpenPicker();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        _popup?.Close();
        base.OnDetached();
    }
}
