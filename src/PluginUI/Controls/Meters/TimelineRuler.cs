// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>How a <see cref="TimelineRuler"/> labels time.</summary>
public enum TimelineFormat
{
    /// <summary>Bars and beats ("3", "3.2").</summary>
    BarsBeats,
    /// <summary>Minutes and seconds at <see cref="TimelineRuler.Tempo"/> ("0:04", "1:12.5").</summary>
    Seconds,
}

/// <summary>A marker of a <see cref="TimelineRuler"/>: a position in beats and a name.</summary>
/// <param name="Position">Position in beats.</param>
/// <param name="Name">Name shown next to the flag.</param>
/// <param name="Color">Color of the flag (null = the alternate accent).</param>
public sealed record TimelineMarker(double Position, string Name, Color? Color = null);

/// <summary>
/// Time ruler for loopers, sequencers, sample editors and arrangers: bars and beats or minutes and seconds, zoom and
/// scroll, a play cursor, a loop region and markers. Clicking the ruler asks the plug-in to move the play position.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Click or drag in the ruler to request a position (<see cref="PositionRequested"/>). In the loop
/// strip at the top, drag to draw a loop, drag the loop to move it or its edges to resize it, double-click to switch it on
/// or off. Positions snap to the visible grid (Alt = free). The wheel scrolls, Ctrl+wheel zooms around the mouse.</para>
/// <para><b>Keyboard.</b> Left / Right request a beat earlier or later (Shift = a bar), Home / End the start or the end;
/// L switches the loop; + / − zoom.</para>
/// <para><b>Parameters.</b> <see cref="LoopStartParameter"/> and <see cref="LoopEndParameter"/> hold plain values in
/// beats; every loop change is a complete edit gesture.</para>
/// </remarks>
/// <example>
/// <code>
/// var ruler = new TimelineRuler { LengthBeats = 32, PlayPositionSource = () => processor.SongBeat };
/// ruler.PositionRequested += (_, beat) => processor.Seek(beat);
/// ruler.LoopStartParameter = Parameters[model.LoopStart];
/// ruler.LoopEndParameter = Parameters[model.LoopEnd];
/// </code>
/// </example>
public class TimelineRuler : Control
{
    private const float LoopHeight = 12f, EdgeZone = 5f;

    private double _length = 64.0, _viewStart, _viewLength = 64.0;
    private double _loopStart = 4.0, _loopEnd = 8.0;
    private bool _loopEnabled = true;
    private RefreshTicker? _ticker;
    private double _shownPlay = double.NaN;

    private enum DragMode { None, Seek, NewLoop, MoveLoop, LoopStart, LoopEnd }

    private DragMode _drag;
    private double _anchorBeat, _anchorLoopStart, _anchorLoopEnd;

    /// <summary>Creates a ruler of 16 bars of 4/4.</summary>
    public TimelineRuler()
    {
        Focusable = true;
        Cursor = StandardCursor.Hand;
    }

    /// <summary>Length in beats (1 … 100 000).</summary>
    [Category("Data")]
    public double LengthBeats
    {
        get => _length;
        set
        {
            _length = Math.Clamp(value, 1.0, 100000.0);
            SetView(_viewStart, Math.Min(_viewLength, _length));
        }
    }

    /// <summary>Beats per bar (1 … 32).</summary>
    [Category("Data")]
    public int BeatsPerBar { get; set; } = 4;

    /// <summary>Tempo in beats per minute, for <see cref="TimelineFormat.Seconds"/>.</summary>
    [Category("Data")]
    public double Tempo { get; set; } = 120.0;

    /// <summary>How time is labeled.</summary>
    [Category("Appearance")]
    public TimelineFormat Format { get; set; } = TimelineFormat.BarsBeats;

    /// <summary>First visible beat.</summary>
    [Category("Layout")]
    public double ViewStart
    {
        get => _viewStart;
        set => SetView(value, _viewLength);
    }

    /// <summary>Number of visible beats (zoom).</summary>
    [Category("Layout")]
    public double ViewLength
    {
        get => _viewLength;
        set => SetView(_viewStart, value);
    }

    /// <summary>Play cursor in beats (negative = hidden).</summary>
    [Category("Value")]
    public double PlayPosition { get; set; } = -1.0;

    /// <summary>Function polled every frame for <see cref="PlayPosition"/> (e.g. a volatile field written by the audio thread).</summary>
    [Browsable(false)]
    public Func<double>? PlayPositionSource { get; set; }

    /// <summary>Start of the loop in beats.</summary>
    [Category("Value")]
    public double LoopStart
    {
        get => LoopStartParameter is { } p ? p.ToPlain(p.NormalizedValue) : _loopStart;
        set
        {
            if (LoopStartParameter is { } p) p.PerformEdit(p.ToNormalized(value));
            else _loopStart = value;
            InvalidateVisual();
        }
    }

    /// <summary>End of the loop in beats.</summary>
    [Category("Value")]
    public double LoopEnd
    {
        get => LoopEndParameter is { } p ? p.ToPlain(p.NormalizedValue) : _loopEnd;
        set
        {
            if (LoopEndParameter is { } p) p.PerformEdit(p.ToNormalized(value));
            else _loopEnd = value;
            InvalidateVisual();
        }
    }

    /// <summary><c>true</c> when the loop is on.</summary>
    [Category("Value")]
    public bool IsLoopEnabled
    {
        get => _loopEnabled;
        set { _loopEnabled = value; InvalidateVisual(); NotifyAutomationStateChanged(); }
    }

    /// <summary>Shows the loop strip.</summary>
    [Category("Appearance")]
    public bool ShowLoop { get; set; } = true;

    /// <summary>Snaps positions to the visible grid (hold Alt for free positions).</summary>
    [Category("Behavior")]
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Loop start parameter (plain value in beats), or <c>null</c>.</summary>
    [Browsable(false)]
    public IParameter? LoopStartParameter { get; set; }

    /// <summary>Loop end parameter (plain value in beats), or <c>null</c>.</summary>
    [Browsable(false)]
    public IParameter? LoopEndParameter { get; set; }

    /// <summary>Markers shown along the bottom.</summary>
    [Browsable(false)]
    public IList<TimelineMarker> Markers { get; } = new List<TimelineMarker>();

    /// <summary>Raised when the user asks for a play position (beats): move the transport there.</summary>
    public event Action<TimelineRuler, double>? PositionRequested;

    /// <summary>Raised after the user changed the loop (start, end or on / off).</summary>
    public event Action<TimelineRuler>? LoopChanged;

    /// <summary>Raised after the visible range changed (scroll, zoom).</summary>
    public event Action<TimelineRuler>? ViewChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(560f, 44f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new TimelineRulerAutomationPeer(this);

    /// <summary>Shows <paramref name="length"/> beats from <paramref name="start"/>, kept inside the length.</summary>
    public void SetView(double start, double length)
    {
        length = Math.Clamp(length, Math.Min(1.0, _length), _length);
        start = Math.Clamp(start, 0.0, _length - length);
        if (start == _viewStart && length == _viewLength) return;
        _viewStart = start;
        _viewLength = length;
        ViewChanged?.Invoke(this);
        InvalidateVisual();
    }

    /// <summary>Zooms by <paramref name="factor"/> (above 1 = closer) keeping <paramref name="pivot"/> (beats) in place.</summary>
    public void Zoom(double factor, double pivot)
    {
        double length = _viewLength / factor;
        double ratio = (pivot - _viewStart) / _viewLength;
        SetView(pivot - ratio * Math.Clamp(length, Math.Min(1.0, _length), _length), length);
    }

    /// <summary>A position in the format of the ruler ("3.2" or "0:04.5").</summary>
    public string FormatPosition(double beats)
    {
        if (Format == TimelineFormat.Seconds)
        {
            double seconds = beats * 60.0 / Math.Max(1.0, Tempo);
            int minutes = (int)(seconds / 60.0);
            double rest = seconds - minutes * 60.0;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + rest.ToString(rest % 1.0 < 1e-6 ? "00" : "00.0", CultureInfo.InvariantCulture);
        }
        int bpb = Math.Max(1, BeatsPerBar);
        int bar = (int)Math.Floor(beats / bpb + 1e-9);
        double beat = beats - bar * bpb;
        int wholeBeat = (int)Math.Floor(beat + 1e-9);
        double fraction = beat - wholeBeat;
        string text = (bar + 1).ToString(CultureInfo.InvariantCulture) + "." + (wholeBeat + 1).ToString(CultureInfo.InvariantCulture);
        if (fraction > 1e-6) text += "." + ((int)Math.Round(fraction * 4.0) + 1).ToString(CultureInfo.InvariantCulture);
        return text;
    }

    // ---------------------------------------------------------------- geometry

    private Rect Area => Bounds.Deflate(1f);

    private Rect LoopStrip
    {
        get
        {
            var a = Area;
            return ShowLoop ? new Rect(a.X, a.Y, a.Width, LoopHeight) : new Rect(a.X, a.Y, a.Width, 0f);
        }
    }

    private float BeatToX(double beat)
    {
        var a = Area;
        return a.X + (float)((beat - _viewStart) / _viewLength) * a.Width;
    }

    private double XToBeat(float x)
    {
        var a = Area;
        return _viewStart + (x - a.X) / Math.Max(1f, a.Width) * _viewLength;
    }

    /// <summary>Grid step in beats: the smallest whose labels are at least 48 DIPs apart.</summary>
    private double LabelStep()
    {
        double pixelsPerBeat = Area.Width / _viewLength;
        int bpb = Math.Max(1, BeatsPerBar);
        if (Format == TimelineFormat.Seconds)
        {
            double beatsPerSecond = Math.Max(1.0, Tempo) / 60.0;
            foreach (double seconds in (ReadOnlySpan<double>)[0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600])
            {
                if (seconds * beatsPerSecond * pixelsPerBeat >= 48.0) return seconds * beatsPerSecond;
            }
            return 600.0 * beatsPerSecond;
        }
        foreach (double step in (ReadOnlySpan<double>)[0.25, 0.5, 1.0])
        {
            if (step * pixelsPerBeat >= 48.0) return step;
        }
        for (int bars = 1; bars <= 4096; bars *= 2)
        {
            if (bars * bpb * pixelsPerBeat >= 48.0) return bars * bpb;
        }
        return 4096.0 * bpb;
    }

    private double Snap(double beat, bool free)
    {
        if (!SnapToGrid || free) return beat;
        double step = LabelStep() / 4.0;
        if (Format == TimelineFormat.BarsBeats) step = Math.Max(0.25, step);
        return Math.Round(beat / step) * step;
    }

    // ---------------------------------------------------------------- live updates

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, _ =>
        {
            if (PlayPositionSource is { } source) PlayPosition = source();
            if (PlayPosition != _shownPlay)
            {
                _shownPlay = PlayPosition;
                InvalidateVisual();
                NotifyAutomationStateChanged();
            }
        }));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        EndDrag();
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var area = Area;
        var strip = LoopStrip;
        using (dc.Clip(area))
        {
            // Loop strip.
            if (ShowLoop)
            {
                dc.FillRect(strip, p.Background.WithAlpha(0.5f));
                float x1 = BeatToX(LoopStart), x2 = BeatToX(LoopEnd);
                var loop = Rect.FromEdges(x1, strip.Y + 1f, x2, strip.Bottom - 1f);
                var color = _loopEnabled ? style.Accent : p.TextDim;
                dc.FillRoundedRect(loop, 3f, color.WithAlpha(_loopEnabled ? 0.55f : 0.3f));
                dc.FillRect(new Rect(x1, strip.Y, 2f, strip.Height), color);
                dc.FillRect(new Rect(x2 - 2f, strip.Y, 2f, strip.Height), color);
                if (_loopEnabled) dc.FillRect(Rect.FromEdges(x1, strip.Bottom, x2, area.Bottom), style.Accent.WithAlpha(0.07f));
            }

            // Ticks and labels.
            double step = LabelStep();
            double minor = step / (Format == TimelineFormat.BarsBeats && step >= BeatsPerBar ? Math.Min(BeatsPerBar, 4) : 4.0);
            var label = Theme.Typography.Small with { Size = 9.5f, VerticalAlign = VerticalAlignment.Top, Ellipsis = false };
            float top = strip.Bottom + 1f;
            long firstMinor = (long)Math.Floor(_viewStart / minor);
            long lastMinor = (long)Math.Ceiling((_viewStart + _viewLength) / minor);
            for (long i = firstMinor; i <= lastMinor && i - firstMinor < 4000; i++)
            {
                double beat = i * minor;
                if (beat < 0.0 || beat > _length) continue;
                float x = BeatToX(beat);
                bool major = Math.Abs(beat / step - Math.Round(beat / step)) < 1e-6;
                float tick = major ? area.Bottom - top : (area.Bottom - top) * 0.3f;
                dc.DrawLine(new Point(x, area.Bottom - tick), new Point(x, area.Bottom), major ? p.TextDim.WithAlpha(0.7f) : p.Border, 1f);
                if (major) dc.DrawText(FormatLabel(beat, step), new Rect(x + 3f, top + 1f, 60f, 12f), label, p.TextDim);
            }

            // Markers.
            foreach (var marker in Markers)
            {
                float x = BeatToX(marker.Position);
                var color = marker.Color ?? p.AccentAlt;
                dc.DrawLine(new Point(x, top), new Point(x, area.Bottom), color, 1.2f);
                var flag = new Rect(x, area.Bottom - 12f, MathF.Min(90f, TextMeasurer.Measure(marker.Name, label).Width + 8f), 11f);
                dc.FillRect(flag, color.WithAlpha(0.85f));
                dc.DrawText(marker.Name, flag.Deflate(new Thickness(3f, 0f, 2f, 0f)), label with { VerticalAlign = VerticalAlignment.Center, Size = 8.5f }, color.Contrasting);
            }

            // Play cursor.
            if (PlayPosition >= 0.0)
            {
                float x = BeatToX(PlayPosition);
                dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), p.AccentAlt, 1.5f);
                dc.FillPolygon([new Point(x - 5f, top), new Point(x + 5f, top), new Point(x, top + 6f)], p.AccentAlt);
            }
        }
    }

    private string FormatLabel(double beat, double step)
    {
        if (Format == TimelineFormat.Seconds) return FormatPosition(beat);
        int bpb = Math.Max(1, BeatsPerBar);
        return step >= bpb ? ((int)Math.Round(beat / bpb) + 1).ToString(CultureInfo.InvariantCulture) : FormatPosition(beat);
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left || !Area.Contains(e.Position)) return;
        e.Handled = true;
        double beat = Math.Clamp(XToBeat(e.Position.X), 0.0, _length);
        if (ShowLoop && LoopStrip.Contains(e.Position))
        {
            if (e.ClickCount == 2)
            {
                _loopEnabled = !_loopEnabled;
                LoopChanged?.Invoke(this);
                InvalidateVisual();
                NotifyAutomationStateChanged();
                return;
            }
            float x1 = BeatToX(LoopStart), x2 = BeatToX(LoopEnd);
            _anchorBeat = beat;
            _anchorLoopStart = LoopStart;
            _anchorLoopEnd = LoopEnd;
            _drag = MathF.Abs(e.Position.X - x1) <= EdgeZone ? DragMode.LoopStart
                : MathF.Abs(e.Position.X - x2) <= EdgeZone ? DragMode.LoopEnd
                : e.Position.X > x1 && e.Position.X < x2 ? DragMode.MoveLoop
                : DragMode.NewLoop;
            LoopStartParameter?.BeginEdit();
            LoopEndParameter?.BeginEdit();
            if (_drag == DragMode.NewLoop) _anchorBeat = Snap(beat, e.Alt);
            return;
        }
        _drag = DragMode.Seek;
        RequestPosition(Snap(beat, e.Alt));
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        double beat = Math.Clamp(XToBeat(e.Position.X), 0.0, _length);
        double minimum = SnapToGrid && !e.Alt ? Math.Max(0.25, LabelStep() / 4.0) : 1.0 / 64.0;
        switch (_drag)
        {
            case DragMode.Seek:
                RequestPosition(Snap(beat, e.Alt));
                return;
            case DragMode.NewLoop:
            {
                double end = Snap(beat, e.Alt);
                if (Math.Abs(end - _anchorBeat) < minimum) return;
                SetLoop(Math.Min(_anchorBeat, end), Math.Max(_anchorBeat, end));
                return;
            }
            case DragMode.MoveLoop:
            {
                double length = _anchorLoopEnd - _anchorLoopStart;
                double start = Math.Clamp(Snap(_anchorLoopStart + beat - _anchorBeat, e.Alt), 0.0, _length - length);
                SetLoop(start, start + length);
                return;
            }
            case DragMode.LoopStart:
                SetLoop(Math.Clamp(Snap(beat, e.Alt), 0.0, LoopEnd - minimum), LoopEnd);
                return;
            case DragMode.LoopEnd:
                SetLoop(LoopStart, Math.Clamp(Snap(beat, e.Alt), LoopStart + minimum, _length));
                return;
        }
        if (ShowLoop && LoopStrip.Contains(e.Position))
        {
            float x1 = BeatToX(LoopStart), x2 = BeatToX(LoopEnd);
            Cursor = MathF.Abs(e.Position.X - x1) <= EdgeZone || MathF.Abs(e.Position.X - x2) <= EdgeZone ? StandardCursor.SizeWE : StandardCursor.Hand;
        }
        else Cursor = StandardCursor.Hand;
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
        if (_drag is DragMode.NewLoop or DragMode.MoveLoop or DragMode.LoopStart or DragMode.LoopEnd)
        {
            LoopStartParameter?.EndEdit();
            LoopEndParameter?.EndEdit();
        }
        _drag = DragMode.None;
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.WheelDelta == 0f) return;
        if (e.Control) Zoom(Math.Pow(1.25, e.WheelDelta), XToBeat(e.Position.X));
        else SetView(_viewStart - e.WheelDelta * _viewLength * 0.1, _viewLength);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        double current = Math.Max(0.0, PlayPosition);
        double step = e.Shift ? Math.Max(1, BeatsPerBar) : 1.0;
        switch (e.Key)
        {
            case Key.Left:
                RequestPosition(Math.Max(0.0, Math.Ceiling(current / step - 1e-9) * step - step));
                break;
            case Key.Right:
                RequestPosition(Math.Min(_length, Math.Floor(current / step + 1e-9) * step + step));
                break;
            case Key.Home:
                RequestPosition(0.0);
                break;
            case Key.End:
                RequestPosition(_length);
                break;
            case Key.L:
                _loopEnabled = !_loopEnabled;
                LoopChanged?.Invoke(this);
                break;
            case Key.Add or Key.OemPlus:
                Zoom(1.5, PlayPosition >= 0.0 ? PlayPosition : _viewStart + _viewLength * 0.5);
                break;
            case Key.Subtract or Key.OemMinus:
                Zoom(1.0 / 1.5, PlayPosition >= 0.0 ? PlayPosition : _viewStart + _viewLength * 0.5);
                break;
            default:
                return;
        }
        e.Handled = true;
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    // ---------------------------------------------------------------- edits

    private void RequestPosition(double beat)
    {
        beat = Math.Clamp(beat, 0.0, _length);
        if (PlayPositionSource is null) PlayPosition = beat;
        PositionRequested?.Invoke(this, beat);
        // Keep the requested position in view.
        if (beat < _viewStart) SetView(beat, _viewLength);
        else if (beat > _viewStart + _viewLength) SetView(beat - _viewLength, _viewLength);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void SetLoop(double start, double end)
    {
        if (Math.Abs(start - LoopStart) < 1e-9 && Math.Abs(end - LoopEnd) < 1e-9) return;
        // Write the end first when the loop moves right, so a bound parameter never sees start > end.
        if (start > LoopStart)
        {
            LoopEnd = end;
            LoopStart = start;
        }
        else
        {
            LoopStart = start;
            LoopEnd = end;
        }
        LoopChanged?.Invoke(this);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary>What a screen reader says: the play position and the loop.</summary>
    internal string Describe()
    {
        var t = Strings;
        string text = string.Format(CultureInfo.InvariantCulture, t.TimelinePositionFormat, FormatPosition(Math.Max(0.0, PlayPosition)));
        return text + ", " + (_loopEnabled ? string.Format(CultureInfo.InvariantCulture, t.TimelineLoopFormat, FormatPosition(LoopStart), FormatPosition(LoopEnd)) : t.TimelineLoopOff);
    }
}

/// <summary>Peer of a <see cref="TimelineRuler"/>: a group whose value is the play position and the loop.</summary>
/// <param name="owner">The ruler.</param>
public class TimelineRulerAutomationPeer(TimelineRuler owner) : ElementAutomationPeer<TimelineRuler>(owner), IValuePattern
{
    private string? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationTimeline;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => true;

    /// <inheritdoc />
    public string Value => Owner.Describe();

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(string value) => throw new InvalidOperationException("The timeline is edited with its keys.");

    /// <inheritdoc />
    public override object? GetPattern(AutomationPattern pattern) => pattern == AutomationPattern.Value ? this : base.GetPattern(pattern);

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        string text = Value;
        if (text == _reported) return;
        RaisePropertyChanged(AutomationProperty.Value, _reported, text);
        _reported = text;
    }
}
