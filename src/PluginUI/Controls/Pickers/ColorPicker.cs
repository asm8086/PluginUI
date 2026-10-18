// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;

namespace PluginUI;

/// <summary>
/// HSV color picker: saturation/value square, hue strip, optional alpha strip, hex field, old/new preview and
/// recently used colors. Usually shown in a <see cref="ColorPickerPopup"/> by a <see cref="ColorSwatch"/>.
/// </summary>
public class ColorPicker : Control
{
    private const float StripWidth = 14f;
    private const float Gap = 8f;
    private const float SquareHeight = 150f;
    private const float RowHeight = 24f;
    private const float SwatchSize = 16f;
    private const int MaxRecent = 12;

    private static readonly List<Color> s_recent = new();

    private readonly ElementCollection _children;
    private readonly TextBox _hex;
    private float _h, _s, _v, _a = 1f;
    private Color _original;
    private DragTarget _drag;

    private enum DragTarget { None, Square, Hue, Alpha }

    /// <summary>Creates a picker.</summary>
    public ColorPicker() : this(Color.White) { }

    /// <summary>Creates a picker showing <paramref name="color"/>.</summary>
    public ColorPicker(Color color)
    {
        _children = new ElementCollection(this);
        _hex = new TextBox { MaxLength = 9, CharacterFilter = c => Uri.IsHexDigit(c) || c == '#', SelectAllOnFocus = true };
        _hex.Committed += (_, text) => ApplyHex(text);
        _children.Add(_hex);
        SetColorCore(color, updateHsv: true);
        _original = color;
    }

    /// <summary>Recently committed colors (shared by all pickers, most recent first).</summary>
    public static IReadOnlyList<Color> RecentColors => s_recent;

    /// <summary>Adds a color to <see cref="RecentColors"/>.</summary>
    public static void AddRecentColor(Color color)
    {
        s_recent.RemoveAll(c => c.ToHex() == color.ToHex());
        s_recent.Insert(0, color);
        if (s_recent.Count > MaxRecent) s_recent.RemoveRange(MaxRecent, s_recent.Count - MaxRecent);
    }

    /// <summary>Shows and edits the alpha channel.</summary>
    [Category("Appearance")]
    public bool ShowAlpha { get; set; } = true;

    /// <summary>Color shown as "before" in the preview (set when the picker opens).</summary>
    [Category("Value")]
    public Color OriginalColor { get => _original; set { _original = value; InvalidateVisual(); } }

    /// <summary>Selected color.</summary>
    [Category("Value")]
    public Color Color
    {
        get => Color.FromHsv(_h, _s, _v, _a);
        set
        {
            if (value == Color) return;
            SetColorCore(value, updateHsv: true);
            InvalidateVisual();
        }
    }

    /// <summary>Raised while the user changes the color.</summary>
    public event Action<ColorPicker, Color>? ColorChanged;

    /// <inheritdoc />
    public override IReadOnlyList<UIElement> VisualChildren => _children;

    /// <inheritdoc />
    protected override Size DefaultSize => new(236f, SquareHeight + Gap + RowHeight + Gap + SwatchSize);

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    private void SetColorCore(Color c, bool updateHsv)
    {
        if (updateHsv)
        {
            var (h, s, v) = c.ToHsv();
            // Keep the current hue for greys and black (hue is undefined there).
            if (s > 0.0001f && v > 0.0001f) _h = h;
            if (v > 0.0001f) _s = s;
            _v = v;
            _a = c.A;
        }
        if (!_hex.IsFocused) _hex.Text = Color.ToHex();
    }

    private void UserChanged()
    {
        SetColorCore(Color, updateHsv: false);
        ColorChanged?.Invoke(this, Color);
        InvalidateVisual();
    }

    private void ApplyHex(string text)
    {
        if (Color.TryParse(text, out var c))
        {
            if (!ShowAlpha) c = c.WithAlpha(1f);
            SetColorCore(c, updateHsv: true);
            ColorChanged?.Invoke(this, Color);
            InvalidateVisual();
        }
        _hex.Text = Color.ToHex();
    }

    // ---------------------------------------------------------------- layout

    private Rect SquareRect => new(Bounds.X, Bounds.Y, Bounds.Width - StripWidth - Gap - (ShowAlpha ? StripWidth + Gap : 0f), SquareHeight);

    private Rect HueRect => new(SquareRect.Right + Gap, Bounds.Y, StripWidth, SquareHeight);

    private Rect AlphaRect => new(HueRect.Right + Gap, Bounds.Y, StripWidth, SquareHeight);

    private Rect PreviewRect => new(Bounds.X, Bounds.Y + SquareHeight + Gap, 52f, RowHeight);

    private Rect HexRect => Rect.FromEdges(PreviewRect.Right + Gap, PreviewRect.Y, Bounds.Right, PreviewRect.Bottom);

    private Rect SwatchRect(int i) => new(Bounds.X + i * (SwatchSize + 4f), PreviewRect.Bottom + Gap, SwatchSize, SwatchSize);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        _hex.Measure(new Size(available.Width, RowHeight));
        return base.MeasureOverride(available);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var r = HexRect;
        _hex.Measure(r.Size);
        _hex.Arrange(r);
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var sq = SquareRect;
        const float radius = 3f;

        // Saturation / value square: white→hue horizontally, then transparent→black vertically.
        dc.FillRoundedRect(sq, radius, Paint.Horizontal(Color.White, Color.FromHsv(_h, 1f, 1f)));
        dc.FillRoundedRect(sq, radius, Paint.Vertical(Color.Black.WithAlpha(0f), Color.Black));
        dc.DrawRoundedRect(sq.Inflate(-0.5f), radius, Color.Black.WithAlpha(0.35f));
        var cursor = new Point(sq.X + _s * sq.Width, sq.Y + (1f - _v) * sq.Height);
        using (dc.Clip(sq.Inflate(6f)))
        {
            dc.DrawCircle(cursor, 6f, Color.Black.WithAlpha(0.5f), 1f);
            dc.DrawCircle(cursor, 5f, Color.White, 1.6f);
        }

        // Hue strip.
        var hue = HueRect;
        GradientStop[] stops =
        [
            new(0f, Color.FromHsv(0f, 1f, 1f)), new(1f / 6f, Color.FromHsv(60f, 1f, 1f)), new(2f / 6f, Color.FromHsv(120f, 1f, 1f)),
            new(3f / 6f, Color.FromHsv(180f, 1f, 1f)), new(4f / 6f, Color.FromHsv(240f, 1f, 1f)), new(5f / 6f, Color.FromHsv(300f, 1f, 1f)),
            new(1f, Color.FromHsv(360f, 1f, 1f)),
        ];
        dc.FillRoundedRect(hue, radius, new LinearGradientPaint(new Point(0f, 0f), new Point(0f, 1f), stops));
        dc.DrawRoundedRect(hue.Inflate(-0.5f), radius, Color.Black.WithAlpha(0.35f));
        DrawStripMarker(dc, hue, _h / 360f);

        // Alpha strip.
        if (ShowAlpha)
        {
            var ar = AlphaRect;
            using (dc.Clip(ar)) Glyphs.Checkerboard(dc, ar, 4f);
            var opaque = Color.FromHsv(_h, _s, _v);
            dc.FillRect(ar, Paint.Vertical(opaque, opaque.WithAlpha(0f)));
            dc.DrawRect(ar.Inflate(-0.5f), Color.Black.WithAlpha(0.35f));
            DrawStripMarker(dc, ar, 1f - _a);
        }

        // Preview: original (left) / new (right).
        var pv = PreviewRect;
        using (dc.Clip(pv)) Glyphs.Checkerboard(dc, pv, 4f);
        dc.FillRect(pv.TakeLeft(pv.Width * 0.5f), _original);
        dc.FillRect(pv.TakeRight(pv.Width * 0.5f), Color);
        dc.DrawRect(pv.Inflate(-0.5f), style.Frame);

        // Recent colors.
        for (int i = 0; i < s_recent.Count; i++)
        {
            var r = SwatchRect(i);
            if (r.Right > Bounds.Right) break;
            if (s_recent[i].A < 1f) using (dc.Clip(r)) Glyphs.Checkerboard(dc, r, 4f);
            dc.FillRoundedRect(r, 2f, s_recent[i]);
            dc.DrawRoundedRect(r.Inflate(-0.5f), 2f, p.Border);
        }
    }

    private static void DrawStripMarker(DrawingContext dc, Rect strip, float t)
    {
        float y = strip.Y + Math.Clamp(t, 0f, 1f) * strip.Height;
        var marker = new Rect(strip.X - 2f, y - 2.5f, strip.Width + 4f, 5f);
        dc.FillRoundedRect(marker, 2f, Color.White);
        dc.DrawRoundedRect(marker.Inflate(0.5f), 2.5f, Color.Black.WithAlpha(0.5f));
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        var pt = e.Position;
        if (SquareRect.Inflate(4f).Contains(pt)) _drag = DragTarget.Square;
        else if (HueRect.Inflate(4f).Contains(pt)) _drag = DragTarget.Hue;
        else if (ShowAlpha && AlphaRect.Inflate(4f).Contains(pt)) _drag = DragTarget.Alpha;
        else
        {
            for (int i = 0; i < s_recent.Count; i++)
            {
                if (!SwatchRect(i).Contains(pt)) continue;
                SetColorCore(ShowAlpha ? s_recent[i] : s_recent[i].WithAlpha(1f), updateHsv: true);
                ColorChanged?.Invoke(this, Color);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (PreviewRect.TakeLeft(PreviewRect.Width * 0.5f).Contains(pt))
            {
                SetColorCore(_original, updateHsv: true);
                ColorChanged?.Invoke(this, Color);
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }
        e.Handled = true;
        Focus();
        DragTo(pt);
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag != DragTarget.None && IsMouseCaptured) DragTo(e.Position);
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = DragTarget.None;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _drag = DragTarget.None;
    }

    private void DragTo(Point pt)
    {
        switch (_drag)
        {
            case DragTarget.Square:
            {
                var sq = SquareRect;
                _s = Math.Clamp((pt.X - sq.X) / sq.Width, 0f, 1f);
                _v = Math.Clamp(1f - (pt.Y - sq.Y) / sq.Height, 0f, 1f);
                break;
            }
            case DragTarget.Hue:
                _h = Math.Clamp((pt.Y - HueRect.Y) / HueRect.Height, 0f, 1f) * 360f;
                if (_h >= 360f) _h = 359.99f;
                break;
            case DragTarget.Alpha:
                _a = Math.Clamp(1f - (pt.Y - AlphaRect.Y) / AlphaRect.Height, 0f, 1f);
                break;
            default:
                return;
        }
        UserChanged();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        float step = e.Shift ? 0.1f : 0.01f;
        switch (e.Key)
        {
            case Key.Left: _s = Math.Clamp(_s - step, 0f, 1f); break;
            case Key.Right: _s = Math.Clamp(_s + step, 0f, 1f); break;
            case Key.Up: _v = Math.Clamp(_v + step, 0f, 1f); break;
            case Key.Down: _v = Math.Clamp(_v - step, 0f, 1f); break;
            case Key.PageUp: _h = (_h + 360f - step * 360f) % 360f; break;
            case Key.PageDown: _h = (_h + step * 360f) % 360f; break;
            default: return;
        }
        e.Handled = true;
        UserChanged();
    }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"ColorPicker {Color.ToHex()}");
}

/// <summary>
/// Popup containing a <see cref="ColorPicker"/>. Changes are live (<see cref="ColorChanged"/>); closing commits
/// (<see cref="Committed"/>, the color is added to the recent colors) and Escape restores the original color.
/// </summary>
public class ColorPickerPopup : Popup
{
    private readonly Color _initial;
    private bool _cancelled;

    /// <summary>Creates the popup.</summary>
    public ColorPickerPopup(Color initial, bool showAlpha = true)
    {
        _initial = initial;
        Picker = new ColorPicker(initial) { ShowAlpha = showAlpha, OriginalColor = initial };
        Picker.ColorChanged += (_, c) => ColorChanged?.Invoke(this, c);
        Padding = 10f;
        CapturesKeyboard = true;
        Content = Picker;
    }

    /// <summary>The picker.</summary>
    public ColorPicker Picker { get; }

    /// <summary>Raised while the user changes the color.</summary>
    public event Action<ColorPickerPopup, Color>? ColorChanged;

    /// <summary>Raised when the popup closes without cancellation.</summary>
    public event Action<ColorPickerPopup, Color>? Committed;

    /// <summary>Raised when the user pressed Escape (the original color was restored).</summary>
    public event Action<ColorPickerPopup>? Cancelled;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        float radius = Theme.Metrics.CornerRadius + 2f;
        dc.DrawShadow(Bounds, radius, 12f, new Point(0f, 4f), p.Shadow);
        dc.FillRoundedRect(Bounds, radius, p.SurfaceRaised);
        dc.DrawRoundedRect(Bounds.Inflate(-0.5f), radius, p.Border);
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _cancelled = true;
            Picker.Color = _initial;
            ColorChanged?.Invoke(this, _initial);
            Cancelled?.Invoke(this);
            Close();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected internal override void OnClosed()
    {
        if (!_cancelled)
        {
            ColorPicker.AddRecentColor(Picker.Color);
            Committed?.Invoke(this, Picker.Color);
        }
        base.OnClosed();
    }
}
