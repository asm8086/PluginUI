// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>
/// Resize grip for the bottom-right corner of a resizable editor (iPlug2 <c>ICornerResizerControl</c>).
/// Dragging asks the container for a new pixel size through <see cref="UIHost.RequestResize"/>; the DAW
/// (or the standalone window) decides and calls back <see cref="UIHost.SetPixelSize"/>. Hidden when the host is not
/// resizable. Double-click restores the preferred size.
/// </summary>
/// <remarks>
/// Place it last in a <see cref="Canvas"/> (or align it bottom-right in any panel) so that it stays on top.
/// The drag works in screen pixels, so it stays stable while the content rescales under the pointer.
/// </remarks>
public class CornerResizer : Control
{
    private bool _dragging;
    private int _startCursorX, _startCursorY;
    private int _startWidth, _startHeight;

    /// <summary>Creates a grip aligned to the bottom-right corner of its slot.</summary>
    public CornerResizer()
    {
        Focusable = false;
        Cursor = StandardCursor.SizeNWSE;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
    }

    /// <inheritdoc />
    /// <remarks>Without an explicit <see cref="UIElement.ToolTip"/>, shows the localized <see cref="UIStrings.DragToResize"/>.</remarks>
    protected internal override string? GetToolTipText() => ToolTip ?? Strings.DragToResize;

    /// <inheritdoc />
    protected override Size DefaultSize => new(16f, 16f);

    /// <summary>Grip color (null = theme dim text).</summary>
    [Category("Appearance")]
    public Color? GripColor { get; set; }

    private bool Active => Host is { IsResizable: true };

    /// <inheritdoc />
    public override bool HitTest(Point point) => Active && base.HitTest(point);

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        if (!Active) return;
        var p = Theme.Palette;
        var color = GripColor ?? (_dragging ? p.Accent : IsMouseOver ? p.Text : p.TextDim.WithAlpha(0.7f));
        float x = widget.Right - 3f, y = widget.Bottom - 3f;
        float s = MathF.Min(widget.Width, widget.Height) - 4f;
        for (int i = 1; i <= 3; i++)
        {
            float d = s * i / 3f;
            dc.DrawLine(new Point(x - d, y), new Point(x, y - d), color, 1.2f, StrokeStyle.Round);
        }
    }

    /// <inheritdoc />
    protected internal override unsafe void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || Host is not { IsResizable: true } host) return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            var (pw, ph) = host.PreferredPixelSize;
            host.RequestResize(pw, ph);
            return;
        }
        (_startWidth, _startHeight) = host.PixelSize;
        (_startCursorX, _startCursorY) = CursorScreenPosition(e.Position);
        _dragging = true;
        IsPressed = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || Host is not { } host) return;
        var (cx, cy) = CursorScreenPosition(e.Position);
        int w = _startWidth + (cx - _startCursorX);
        int h = _startHeight + (cy - _startCursorY);
        host.ConstrainPixelSize(ref w, ref h);
        if ((w, h) != host.PixelSize) host.RequestResize(w, h);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrag();
    }

    private void EndDrag()
    {
        _dragging = false;
        IsPressed = false;
        InvalidateVisual();
    }

    private unsafe (int X, int Y) CursorScreenPosition(Point fallbackDip)
    {
        if (Host is { IsAttached: true })
        {
            POINT pt;
            if (GetCursorPos(&pt) != BOOL.FALSE) return (pt.x, pt.y);
        }
        // Offscreen / tests: DIP position × scale is good enough.
        float s = Host?.Scale ?? 1f;
        return ((int)MathF.Round(fallbackDip.X * s), (int)MathF.Round(fallbackDip.Y * s));
    }
}
