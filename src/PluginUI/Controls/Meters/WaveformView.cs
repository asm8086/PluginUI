// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>Draggable markers of a <see cref="WaveformView"/>.</summary>
public enum WaveformMarker
{
    /// <summary>No marker.</summary>
    None,
    /// <summary>Sample start.</summary>
    Start,
    /// <summary>Sample end.</summary>
    End,
    /// <summary>Loop start.</summary>
    LoopStart,
    /// <summary>Loop end.</summary>
    LoopEnd,
}

/// <summary>
/// Sample display for samplers and audio editors: one lane per channel, zoom and scroll, start / end and loop
/// markers (draggable, optionally bound to parameters), a selection, a play cursor and WAV file drop.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Wheel scrolls, Ctrl + wheel zooms around the pointer, double-click on the background zooms to
/// fit, dragging a marker moves it, Shift + drag selects a range.</para>
/// <para><b>Performance.</b> A multi-resolution min/max summary (<see cref="WaveformPeaks"/>) is built once per
/// channel, so even very long samples are drawn by reading a few values per pixel; when zoomed in to less than one
/// sample per pixel the samples themselves are drawn as a line.</para>
/// <para><b>Markers.</b> Positions are normalized (0 = first sample, 1 = last). When a marker parameter is set
/// (<see cref="StartParameter"/>…), its normalized value is the position and drags send edit gestures.</para>
/// </remarks>
/// <example>
/// <code>
/// var wave = new WaveformView { AcceptFileDrop = true, ShowLoop = true, StartParameter = Parameters[model.Start] };
/// wave.Audio = WavReader.Read(@"C:\Samples\kick.wav");
/// wave.PlayPositionSource = () => processor.PlayPosition; // normalized, read every frame
/// </code>
/// </example>
public class WaveformView : Control
{
    private AudioData? _audio;
    private WaveformPeaks[] _peaks = [];
    private double _viewStart;
    private double _viewLength;
    private double _start, _end = 1.0, _loopStart = 0.25, _loopEnd = 0.75;
    private WaveformMarker _draggingMarker;
    private bool _selecting;
    private long _selectionAnchor;
    private RefreshTicker? _ticker;
    private double _lastPlay = double.NaN;

    /// <summary>Creates a waveform view.</summary>
    public WaveformView()
    {
        Cursor = StandardCursor.Arrow;
    }

    /// <summary>The audio displayed (null = empty). Setting it zooms to fit and clears the selection.</summary>
    [Browsable(false)]
    public AudioData? Audio
    {
        get => _audio;
        set
        {
            _audio = value;
            _peaks = value is null ? [] : [.. value.Channels.Select(c => new WaveformPeaks(c))];
            SelectionStart = SelectionEnd = 0;
            ZoomToFit();
        }
    }

    /// <summary>Number of frames of <see cref="Audio"/>.</summary>
    [Browsable(false)]
    public long Length => _audio?.Length ?? 0;

    /// <summary>First visible frame.</summary>
    [Browsable(false)]
    public double ViewStart { get => _viewStart; set { _viewStart = Math.Clamp(value, 0.0, Math.Max(0.0, Length - _viewLength)); InvalidateVisual(); } }

    /// <summary>Number of visible frames.</summary>
    [Browsable(false)]
    public double ViewLength
    {
        get => _viewLength;
        set
        {
            _viewLength = Math.Clamp(value, Math.Min(Length, 8.0), Math.Max(1.0, Length));
            ViewStart = _viewStart;
        }
    }

    /// <summary>Sample start (normalized); ignored when <see cref="StartParameter"/> is set.</summary>
    [Category("Value")]
    public double StartPosition { get => Read(StartParameter, _start); set => _start = Math.Clamp(value, 0.0, 1.0); }

    /// <summary>Sample end (normalized).</summary>
    [Category("Value")]
    public double EndPosition { get => Read(EndParameter, _end); set => _end = Math.Clamp(value, 0.0, 1.0); }

    /// <summary>Loop start (normalized).</summary>
    [Category("Value")]
    public double LoopStart { get => Read(LoopStartParameter, _loopStart); set => _loopStart = Math.Clamp(value, 0.0, 1.0); }

    /// <summary>Loop end (normalized).</summary>
    [Category("Value")]
    public double LoopEnd { get => Read(LoopEndParameter, _loopEnd); set => _loopEnd = Math.Clamp(value, 0.0, 1.0); }

    /// <summary>Parameter holding the sample start (normalized value = position).</summary>
    [Category("Value")]
    public IParameter? StartParameter { get; set; }

    /// <summary>Parameter holding the sample end.</summary>
    [Category("Value")]
    public IParameter? EndParameter { get; set; }

    /// <summary>Parameter holding the loop start.</summary>
    [Category("Value")]
    public IParameter? LoopStartParameter { get; set; }

    /// <summary>Parameter holding the loop end.</summary>
    [Category("Value")]
    public IParameter? LoopEndParameter { get; set; }

    /// <summary>Shows (and allows dragging) the start and end markers.</summary>
    [Category("Appearance")]
    public bool ShowStartEnd { get; set; } = true;

    /// <summary>Shows (and allows dragging) the loop markers.</summary>
    [Category("Appearance")]
    public bool ShowLoop { get; set; }

    /// <summary>Waveform color (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? WaveColor { get; set; }

    /// <summary>Accepts audio files dropped from the Explorer (WAV files are decoded with <see cref="WavReader"/>).</summary>
    [Category("Behavior")]
    public bool AcceptFileDrop { get => AllowFileDrop; set => AllowFileDrop = value; }

    /// <summary>Play cursor position (normalized; NaN = hidden).</summary>
    [Category("Value")]
    public double PlayPosition { get; set; } = double.NaN;

    /// <summary>Function polled every frame for the play cursor (e.g. a volatile value written by the audio thread).</summary>
    [Browsable(false)]
    public Func<double>? PlayPositionSource { get; set; }

    /// <summary>First frame of the selection.</summary>
    [Browsable(false)]
    public long SelectionStart { get; set; }

    /// <summary>End frame (exclusive) of the selection; equal to <see cref="SelectionStart"/> when nothing is selected.</summary>
    [Browsable(false)]
    public long SelectionEnd { get; set; }

    /// <summary>Raised after a marker was moved by the user.</summary>
    public event Action<WaveformView, WaveformMarker>? MarkerMoved;

    /// <summary>Raised after the user changed the selection.</summary>
    public event Action<WaveformView>? SelectionChanged;

    /// <summary>Raised after a dropped WAV file was loaded (argument: path).</summary>
    public event Action<WaveformView, string>? AudioLoaded;

    /// <summary>Raised for dropped files that are not WAV, or WAV files that could not be decoded (argument: path).</summary>
    public event Action<WaveformView, string>? FileDropped;

    /// <inheritdoc />
    protected override Size DefaultSize => new(420f, 140f);

    private static double Read(IParameter? p, double local) => p?.NormalizedValue ?? local;

    /// <summary>Shows the whole sample.</summary>
    public void ZoomToFit()
    {
        _viewStart = 0.0;
        _viewLength = Math.Max(1.0, Length);
        InvalidateVisual();
    }

    /// <summary>Zooms by a factor around a frame (factor &lt; 1 zooms in).</summary>
    /// <param name="factor">Ratio between the new and the current visible length.</param>
    /// <param name="aroundFrame">Frame that stays under the same pixel.</param>
    public void Zoom(double factor, double aroundFrame)
    {
        if (Length == 0) return;
        double ratio = (aroundFrame - _viewStart) / Math.Max(1.0, _viewLength);
        ViewLength = _viewLength * factor;
        ViewStart = aroundFrame - ratio * _viewLength;
    }

    private Rect WaveArea(Rect widget) => widget.Deflate(new Thickness(1f, 2f, 1f, 2f));

    private double FrameAt(float x, Rect area) => _viewStart + (x - area.X) / MathF.Max(1f, area.Width) * _viewLength;

    private float XOf(double frame, Rect area) => area.X + (float)((frame - _viewStart) / Math.Max(1.0, _viewLength) * area.Width);

    private double ToFrame(double normalized) => normalized * Math.Max(0, Length - 1);

    // ---------------------------------------------------------------- live updates

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, _ =>
        {
            if (PlayPositionSource is { } source) PlayPosition = source();
            if (!PlayPosition.Equals(_lastPlay)) { _lastPlay = PlayPosition; InvalidateVisual(); }
        }));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        EndMarkerDrag();
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var area = WaveArea(widget);
        if (_audio is null || Length == 0)
        {
            if (AcceptFileDrop) dc.DrawText(Strings.DropAudioFile, area, Theme.Typography.Normal with { Align = HorizontalAlignment.Center }, p.TextDim);
            return;
        }
        dc.PushClip(area);
        var color = WaveColor ?? style.Accent;

        // Regions outside start / end are dimmed, the loop is tinted.
        if (ShowLoop)
        {
            float l0 = XOf(ToFrame(LoopStart), area), l1 = XOf(ToFrame(LoopEnd), area);
            dc.FillRect(Rect.FromEdges(MathF.Min(l0, l1), area.Y, MathF.Max(l0, l1), area.Bottom), p.AccentAlt.WithAlpha(0.10f));
        }
        if (SelectionEnd > SelectionStart)
        {
            float s0 = XOf(SelectionStart, area), s1 = XOf(SelectionEnd, area);
            dc.FillRect(Rect.FromEdges(s0, area.Y, s1, area.Bottom), p.Selection.WithAlpha(0.35f));
        }

        int channels = _peaks.Length;
        float laneH = area.Height / channels;
        int columns = Math.Max(1, (int)area.Width);
        double framesPerPixel = _viewLength / columns;
        for (int ch = 0; ch < channels; ch++)
        {
            var lane = new Rect(area.X, area.Y + ch * laneH, area.Width, laneH);
            float mid = lane.Center.Y, half = laneH * 0.45f;
            dc.DrawLine(new Point(lane.X, mid), new Point(lane.Right, mid), p.Border, 1f);
            if (ch > 0) dc.DrawLine(new Point(lane.X, lane.Y), new Point(lane.Right, lane.Y), p.Border.WithAlpha(0.6f), 1f);
            if (framesPerPixel >= 1.0)
            {
                var peaks = _peaks[ch];
                for (int x = 0; x < columns; x++)
                {
                    long f0 = (long)(_viewStart + x * framesPerPixel), f1 = (long)(_viewStart + (x + 1) * framesPerPixel);
                    var (lo, hi) = peaks.Range(f0, Math.Max(f1, f0 + 1));
                    float px = area.X + x + 0.5f;
                    dc.DrawLine(new Point(px, mid - hi * half), new Point(px, mid - lo * half + 0.5f), color, 1f);
                }
            }
            else
            {
                var samples = _audio.Channels[ch];
                long first = Math.Max(0, (long)Math.Floor(_viewStart) - 1), last = Math.Min(samples.Length - 1, (long)Math.Ceiling(_viewStart + _viewLength) + 1);
                int count = (int)(last - first + 1);
                if (count >= 2)
                {
                    var points = new Point[count];
                    for (int i = 0; i < count; i++) points[i] = new Point(XOf(first + i, area), mid - samples[first + i] * half);
                    dc.DrawPolyline(points, color, 1.5f);
                    if (framesPerPixel < 0.15) foreach (var pt in points) dc.FillCircle(pt, 2f, color);
                }
            }
        }

        // Dim outside start / end, then markers.
        if (ShowStartEnd)
        {
            float s = XOf(ToFrame(StartPosition), area), e = XOf(ToFrame(EndPosition), area);
            if (s > area.X) dc.FillRect(Rect.FromEdges(area.X, area.Y, s, area.Bottom), p.Background.WithAlpha(0.55f));
            if (e < area.Right) dc.FillRect(Rect.FromEdges(e, area.Y, area.Right, area.Bottom), p.Background.WithAlpha(0.55f));
            DrawMarker(dc, area, s, p.Success, "S");
            DrawMarker(dc, area, e, p.Danger, "E");
        }
        if (ShowLoop)
        {
            DrawMarker(dc, area, XOf(ToFrame(LoopStart), area), p.AccentAlt, "L");
            DrawMarker(dc, area, XOf(ToFrame(LoopEnd), area), p.AccentAlt, "L");
        }
        if (!double.IsNaN(PlayPosition))
        {
            float x = XOf(ToFrame(Math.Clamp(PlayPosition, 0.0, 1.0)), area);
            dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), p.Text, 1.5f);
        }
        dc.PopClip();
    }

    private void DrawMarker(DrawingContext dc, Rect area, float x, Color color, string tag)
    {
        x = MathF.Round(x) + 0.5f;
        dc.DrawLine(new Point(x, area.Y), new Point(x, area.Bottom), color, 1.5f);
        var flag = new Rect(x - 6f, area.Y, 12f, 11f);
        dc.FillRoundedRect(flag, 2f, color);
        dc.DrawText(tag, flag, Theme.Typography.Small with { Size = 8f, Align = HorizontalAlignment.Center, Ellipsis = false }, color.Contrasting);
    }

    // ---------------------------------------------------------------- input

    private WaveformMarker HitMarker(Point p)
    {
        if (Length == 0) return WaveformMarker.None;
        var area = WaveArea(Bounds);
        (WaveformMarker Marker, double Position, bool Visible)[] markers =
        [
            (WaveformMarker.Start, StartPosition, ShowStartEnd),
            (WaveformMarker.End, EndPosition, ShowStartEnd),
            (WaveformMarker.LoopStart, LoopStart, ShowLoop),
            (WaveformMarker.LoopEnd, LoopEnd, ShowLoop),
        ];
        var best = WaveformMarker.None;
        float bestDistance = 6f;
        foreach (var (marker, position, visible) in markers)
        {
            if (!visible) continue;
            float d = MathF.Abs(XOf(ToFrame(position), area) - p.X);
            if (d <= bestDistance) { bestDistance = d; best = marker; }
        }
        return best;
    }

    private IParameter? ParameterOf(WaveformMarker marker) => marker switch
    {
        WaveformMarker.Start => StartParameter,
        WaveformMarker.End => EndParameter,
        WaveformMarker.LoopStart => LoopStartParameter,
        WaveformMarker.LoopEnd => LoopEndParameter,
        _ => null,
    };

    /// <summary>Moves a marker as the user would (constrained: start ≤ end, loop start ≤ loop end; bound parameters are edited).</summary>
    /// <param name="marker">The marker.</param>
    /// <param name="position">New normalized position.</param>
    public void MoveMarker(WaveformMarker marker, double position)
    {
        position = Math.Clamp(position, 0.0, 1.0);
        switch (marker)
        {
            case WaveformMarker.Start: position = Math.Min(position, EndPosition); break;
            case WaveformMarker.End: position = Math.Max(position, StartPosition); break;
            case WaveformMarker.LoopStart: position = Math.Min(position, LoopEnd); break;
            case WaveformMarker.LoopEnd: position = Math.Max(position, LoopStart); break;
            default: return;
        }
        if (ParameterOf(marker) is { } p)
        {
            bool own = _draggingMarker != marker;
            if (own) p.BeginEdit();
            p.PerformEdit(position);
            if (own) p.EndEdit();
        }
        else
        {
            switch (marker)
            {
                case WaveformMarker.Start: _start = position; break;
                case WaveformMarker.End: _end = position; break;
                case WaveformMarker.LoopStart: _loopStart = position; break;
                case WaveformMarker.LoopEnd: _loopEnd = position; break;
            }
        }
        MarkerMoved?.Invoke(this, marker);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var area = WaveArea(Bounds);
        if (_draggingMarker != WaveformMarker.None)
        {
            MoveMarker(_draggingMarker, FrameAt(e.Position.X, area) / Math.Max(1.0, Length - 1));
            return;
        }
        if (_selecting)
        {
            long frame = (long)Math.Clamp(FrameAt(e.Position.X, area), 0, Length);
            SelectionStart = Math.Min(_selectionAnchor, frame);
            SelectionEnd = Math.Max(_selectionAnchor, frame);
            SelectionChanged?.Invoke(this);
            InvalidateVisual();
            return;
        }
        Cursor = HitMarker(e.Position) != WaveformMarker.None ? StandardCursor.SizeWE : StandardCursor.Arrow;
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || Length == 0 || !IsEffectivelyEnabled) return;
        var marker = HitMarker(e.Position);
        if (e.ClickCount == 2 && marker == WaveformMarker.None)
        {
            ZoomToFit();
            e.Handled = true;
            return;
        }
        if (marker != WaveformMarker.None && !e.Shift)
        {
            _draggingMarker = marker;
            ParameterOf(marker)?.BeginEdit();
            e.Handled = true;
            return;
        }
        _selecting = true;
        _selectionAnchor = (long)Math.Clamp(FrameAt(e.Position.X, WaveArea(Bounds)), 0, Length);
        SelectionStart = SelectionEnd = _selectionAnchor;
        SelectionChanged?.Invoke(this);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButton.Left) return;
        EndMarkerDrag();
        _selecting = false;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndMarkerDrag();
        _selecting = false;
    }

    private void EndMarkerDrag()
    {
        if (_draggingMarker == WaveformMarker.None) return;
        ParameterOf(_draggingMarker)?.EndEdit();
        _draggingMarker = WaveformMarker.None;
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || Length == 0) return;
        var area = WaveArea(Bounds);
        if (e.Control) Zoom(Math.Pow(0.8, e.WheelDelta), FrameAt(e.Position.X, area));
        else ViewStart = _viewStart - e.WheelDelta * _viewLength * 0.1;
        e.Handled = true;
    }

    /// <inheritdoc />
    protected internal override void OnFilesDropped(FilesDroppedEventArgs e)
    {
        base.OnFilesDropped(e);
        if (!AcceptFileDrop || e.Files.Count == 0) return;
        string path = e.Files[0];
        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wave", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Audio = WavReader.Read(path);
                AudioLoaded?.Invoke(this, path);
                e.Handled = true;
                return;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Unsupported encoding: let the application decode it.
            }
        }
        FileDropped?.Invoke(this, path);
        e.Handled = true;
    }
}
