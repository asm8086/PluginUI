// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Values of a set of parameters captured at one moment, restored later as user edits (A/B comparison, undo
/// points, "random" with revert…).
/// </summary>
public sealed class ParameterSnapshot
{
    private readonly (IParameter Parameter, double Value)[] _values;

    private ParameterSnapshot((IParameter, double)[] values) => _values = values;

    /// <summary>Number of parameters captured.</summary>
    public int Count => _values.Length;

    /// <summary>Captures the current normalized values.</summary>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <c>null</c>.</exception>
    public static ParameterSnapshot Capture(IEnumerable<IParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new ParameterSnapshot([.. parameters.Select(p => (p, p.NormalizedValue))]);
    }

    /// <summary>Normalized value captured for a parameter, or <c>null</c> if it is not part of the snapshot.</summary>
    /// <param name="parameter">A parameter.</param>
    /// <returns>The captured value.</returns>
    public double? ValueOf(IParameter parameter)
    {
        foreach (var (p, v) in _values) if (ReferenceEquals(p, parameter)) return v;
        return null;
    }

    /// <summary>Restores every value that differs, each as a complete edit gesture (recorded by the host).</summary>
    public void Restore()
    {
        foreach (var (p, v) in _values)
        {
            if (p.NormalizedValue == v) continue;
            p.BeginEdit();
            p.PerformEdit(v);
            p.EndEdit();
        }
    }
}

/// <summary>
/// A/B comparison bar: two slots holding complete settings, switched with one click, plus a "copy to the other slot"
/// button. Works on a list of parameters (<see cref="Parameters"/>) or on any custom state
/// (<see cref="CaptureState"/> / <see cref="RestoreState"/>).
/// </summary>
/// <remarks>
/// The selected slot is always the live state of the plugin. Switching stores it and restores the other slot (the
/// first switch to B starts from a copy of A). Restoring goes through normal edit gestures, so the host records it.
/// </remarks>
/// <example>
/// <code>
/// var ab = new ABCompareBar { Parameters = Parameters.All };   // PluginEditorView: every bound parameter
/// </code>
/// </example>
public class ABCompareBar : Control
{
    private enum Zone { None, A, B, Copy }

    private object? _a, _b;
    private bool _isB;
    private Zone _hover;

    /// <summary>Creates an A/B bar.</summary>
    public ABCompareBar()
    {
    }

    /// <summary>Parameters compared (used when <see cref="CaptureState"/> is <c>null</c>).</summary>
    [Browsable(false)]
    public IReadOnlyList<IParameter>? Parameters { get; set; }

    /// <summary>Custom state capture (e.g. the whole plugin state as bytes); overrides <see cref="Parameters"/>.</summary>
    [Browsable(false)]
    public Func<object?>? CaptureState { get; set; }

    /// <summary>Custom state restore, paired with <see cref="CaptureState"/>.</summary>
    [Browsable(false)]
    public Action<object?>? RestoreState { get; set; }

    /// <summary><c>true</c> when slot B is the live one.</summary>
    [Browsable(false)]
    public bool IsB => _isB;

    /// <summary>Raised after the live slot changed.</summary>
    public event Action<ABCompareBar>? SlotChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(110f, 24f);

    private object? Capture() => CaptureState is { } capture ? capture() : Parameters is { } list ? ParameterSnapshot.Capture(list) : null;

    private void Restore(object? state)
    {
        if (RestoreState is { } restore) restore(state);
        else (state as ParameterSnapshot)?.Restore();
    }

    /// <summary>Makes slot A (<c>false</c>) or B (<c>true</c>) live.</summary>
    /// <param name="b"><c>true</c> for B.</param>
    public void Select(bool b)
    {
        if (b == _isB) return;
        var current = Capture();
        if (_isB) _b = current; else _a = current;
        var target = b ? _b : _a;
        if (target is not null) Restore(target);
        else if (b) _b = current; // first visit of B: copy of A
        _isB = b;
        SlotChanged?.Invoke(this);
        InvalidateVisual();
    }

    /// <summary>Copies the live slot into the other one.</summary>
    public void CopyToOther()
    {
        var current = Capture();
        if (_isB) _a = current; else _b = current;
        InvalidateVisual();
    }

    private void Zones(Rect widget, out Rect a, out Rect b, out Rect copy)
    {
        float copyW = MathF.Min(widget.Height * 1.4f, widget.Width * 0.3f);
        float half = (widget.Width - copyW - 4f) * 0.5f;
        a = new Rect(widget.X, widget.Y, half, widget.Height);
        b = new Rect(widget.X + half, widget.Y, half, widget.Height);
        copy = new Rect(widget.Right - copyW, widget.Y, copyW, widget.Height);
    }

    private Zone ZoneAt(Point p)
    {
        Zones(Bounds, out var a, out var b, out var copy);
        return a.Contains(p) ? Zone.A : b.Contains(p) ? Zone.B : copy.Contains(p) ? Zone.Copy : Zone.None;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        Zones(widget, out var a, out var b, out var copy);
        float radius = CornerRadiusFor(widget, style);
        var pair = Rect.FromEdges(a.X, a.Y, b.Right, b.Bottom);
        dc.FillRoundedRect(pair, radius, p.Surface);
        var live = _isB ? b : a;
        dc.FillRoundedRect(live.Deflate(2f), MathF.Max(0f, radius - 2f), style.Accent);
        var text = Theme.Typography.Normal with { Align = HorizontalAlignment.Center, Weight = FontWeight.SemiBold };
        dc.DrawText("A", a, text, _isB ? (_hover == Zone.A ? p.Text : p.TextDim) : style.Accent.Contrasting);
        dc.DrawText("B", b, text, !_isB ? (_hover == Zone.B ? p.Text : p.TextDim) : style.Accent.Contrasting);
        dc.DrawRoundedRect(pair.Inflate(-0.5f), radius, p.Border, 1f);
        DrawBox(dc, copy, style, false, _hover == Zone.Copy);
        dc.DrawText(_isB ? "B→A" : "A→B", copy, Theme.Typography.Small with { Align = HorizontalAlignment.Center, Ellipsis = false }, p.Text);
    }

    /// <inheritdoc />
    protected internal override string? GetToolTipText()
        => ToolTip ?? (_hover == Zone.Copy ? (_isB ? Strings.CopyBToA : Strings.CopyAToB) : null);

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var zone = ZoneAt(e.Position);
        if (zone == _hover) return;
        _hover = zone;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = Zone.None;
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        switch (ZoneAt(e.Position))
        {
            case Zone.A: Select(false); break;
            case Zone.B: Select(true); break;
            case Zone.Copy: CopyToOther(); break;
            default: return;
        }
        e.Handled = true;
    }
}
