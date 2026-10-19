// 2026/10/19 17:23:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>How a <see cref="LevelMeter"/> draws its bars.</summary>
public enum MeterStyle
{
    /// <summary>Continuous bar colored by a green → yellow → red gradient.</summary>
    Gradient,
    /// <summary>LED segments (unlit segments drawn dim).</summary>
    Segments,
    /// <summary>Continuous bar in a single color (accent).</summary>
    Solid,
}

/// <summary>What a <see cref="LevelMeter"/> measures.</summary>
public enum MeterMode
{
    /// <summary>Signal level: fills from the bottom (or left).</summary>
    Level,
    /// <summary>Gain reduction (positive dB): fills from the top (or right), between 0 and <see cref="LevelMeter.MaxReductionDb"/>.</summary>
    GainReduction,
}

/// <summary>Unit of the values passed to <see cref="LevelMeter.SetLevels"/>.</summary>
public enum LevelUnit
{
    /// <summary>Linear gain (1.0 = 0 dBFS).</summary>
    Linear,
    /// <summary>Decibels.</summary>
    Decibels,
}

/// <summary>
/// Multi-channel level meter: vertical or horizontal, dB scale with marks, gradient / LED segments / solid bars,
/// peak-hold markers, latched clip indicators (click to reset), optional RMS + peak dual display, gain-reduction mode.
/// </summary>
/// <remarks>
/// <para>Feed it on the UI thread with <see cref="SetLevels"/> (and <see cref="SetRmsLevels"/>), or give it
/// <see cref="PeakValue"/> <see cref="Sources"/> written by the audio thread: they are polled on every frame.</para>
/// <para>Ballistics (instant attack, dB/s release, peak hold) come from <see cref="LevelBallistics"/>.</para>
/// </remarks>
public class LevelMeter : Control
{
    private static readonly float[] DefaultMarks = [6f, 3f, 0f, -3f, -6f, -12f, -18f, -24f, -30f, -36f, -48f, -60f, -72f, -90f];

    private LevelBallistics[] _peak = [];
    private LevelBallistics[] _rms = [];
    private float[] _peakInput = [];
    private float[] _rmsInput = [];
    private bool[] _clipped = [];
    private bool _hasRms;
    private MeterTicker? _ticker;
    private LinearGradientPaint? _gradient;
    private Rect _gradientRect;
    private float _minDb = -60f, _maxDb = 6f;

    /// <summary>Creates a stereo meter.</summary>
    public LevelMeter()
    {
        Focusable = false;
        ChannelCount = 2;
    }

    /// <summary>Bar orientation.</summary>
    [Category("Layout")]
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Drawing style.</summary>
    [Category("Appearance")]
    public MeterStyle MeterStyle { get; set; } = MeterStyle.Gradient;

    /// <summary>Level or gain-reduction meter.</summary>
    [Category("Data")]
    public MeterMode Mode { get; set; } = MeterMode.Level;

    /// <summary>Unit of the values passed to <see cref="SetLevels"/>.</summary>
    [Category("Text")]
    public LevelUnit InputUnit { get; set; } = LevelUnit.Linear;

    /// <summary>Number of channels.</summary>
    [Category("Data")]
    public int ChannelCount
    {
        get => _peak.Length;
        set
        {
            value = Math.Clamp(value, 1, 64);
            if (value == _peak.Length) return;
            _peak = new LevelBallistics[value];
            _rms = new LevelBallistics[value];
            for (int i = 0; i < value; i++)
            {
                _peak[i] = new LevelBallistics { FloorDb = FloorDb };
                _rms[i] = new LevelBallistics { FloorDb = FloorDb, ReleaseDbPerSecond = 12f, AttackSeconds = 0.12f, HoldSeconds = 0f };
            }
            _peakInput = new float[value];
            _rmsInput = new float[value];
            Array.Fill(_peakInput, float.NegativeInfinity);
            Array.Fill(_rmsInput, float.NegativeInfinity);
            _clipped = new bool[value];
            ApplyBallistics();
            InvalidateVisual();
        }
    }

    /// <summary>Bottom of the scale in dB.</summary>
    [Category("Data")]
    public float MinDb { get => _minDb; set { _minDb = value; _gradient = null; ApplyBallistics(); InvalidateVisual(); } }

    /// <summary>Top of the scale in dB.</summary>
    [Category("Data")]
    public float MaxDb { get => _maxDb; set { _maxDb = value; _gradient = null; InvalidateVisual(); } }

    /// <summary>Full scale of the gain-reduction mode (dB).</summary>
    [Category("Data")]
    public float MaxReductionDb { get; set; } = 24f;

    /// <summary>Level at which the clip indicator latches (dB).</summary>
    [Category("Data")]
    public float ClipDb { get; set; }

    /// <summary>Level where the gradient turns yellow (dB).</summary>
    [Category("Data")]
    public float WarningDb { get; set; } = -18f;

    /// <summary>Level where the gradient turns red (dB).</summary>
    [Category("Data")]
    public float DangerDb { get; set; } = -6f;

    /// <summary>Low-level color (null = green).</summary>
    [Category("Appearance")]
    public Color? LowColor { get; set; }

    /// <summary>Warning color (null = theme warning).</summary>
    [Category("Appearance")]
    public Color? WarningColor { get; set; }

    /// <summary>High-level color (null = theme danger).</summary>
    [Category("Appearance")]
    public Color? DangerColor { get; set; }

    /// <summary>Draws peak-hold markers.</summary>
    [Category("Appearance")]
    public bool ShowPeakHold { get; set; } = true;

    /// <summary>Draws latched clip indicators above the bars (click to reset).</summary>
    [Category("Appearance")]
    public bool ShowClipIndicator { get; set; } = true;

    /// <summary>Draws the dB scale (marks and labels) beside the bars.</summary>
    [Category("Appearance")]
    public bool ShowScale { get; set; } = true;

    /// <summary>Scale marks in dB (null = automatic set).</summary>
    [Category("Appearance")]
    public IReadOnlyList<float>? ScaleMarks { get; set; }

    /// <summary>LED segment length in DIPs (segment style).</summary>
    [Category("Layout")]
    public float SegmentLength { get; set; } = 3f;

    /// <summary>Gap between LED segments in DIPs.</summary>
    [Category("Layout")]
    public float SegmentGap { get; set; } = 1f;

    /// <summary>Gap between channel bars in DIPs.</summary>
    [Category("Layout")]
    public float ChannelGap { get; set; } = 2f;

    /// <summary>Peak release speed in dB per second.</summary>
    [Category("Data")]
    public float ReleaseDbPerSecond { get => _peak.Length > 0 ? _peak[0].ReleaseDbPerSecond : 24f; set { foreach (var b in _peak) b.ReleaseDbPerSecond = value; } }

    /// <summary>Peak-hold time in seconds.</summary>
    [Category("Data")]
    public float HoldSeconds { get => _peak.Length > 0 ? _peak[0].HoldSeconds : 1.5f; set { foreach (var b in _peak) b.HoldSeconds = value; } }

    /// <summary>Audio-thread level sources polled every frame (one per channel; null = use <see cref="SetLevels"/>).</summary>
    [Browsable(false)]
    public IReadOnlyList<PeakValue>? Sources { get; set; }

    /// <summary>Optional audio-thread RMS sources (one per channel).</summary>
    [Browsable(false)]
    public IReadOnlyList<PeakValue>? RmsSources { get; set; }

    /// <summary>Raised when the user resets the clip indicators.</summary>
    public event Action<LevelMeter>? ClipReset;

    private float FloorDb => Mode == MeterMode.GainReduction ? 0f : _minDb;

    private void ApplyBallistics()
    {
        foreach (var b in _peak) b.FloorDb = FloorDb;
        foreach (var b in _rms) b.FloorDb = FloorDb;
    }

    /// <summary>Peak level (dB) currently displayed for a channel.</summary>
    public float GetDisplayedLevel(int channel) => _peak[channel].LevelDb;

    /// <summary><c>true</c> if the channel's clip indicator is latched.</summary>
    public bool IsClipped(int channel) => _clipped[channel];

    /// <summary>Resets the clip indicators.</summary>
    public void ResetClip()
    {
        Array.Clear(_clipped);
        ClipReset?.Invoke(this);
        InvalidateVisual();
    }

    /// <summary>Resets levels, holds and clip indicators.</summary>
    public void Reset()
    {
        foreach (var b in _peak) b.Reset();
        foreach (var b in _rms) b.Reset();
        Array.Fill(_peakInput, float.NegativeInfinity);
        Array.Fill(_rmsInput, float.NegativeInfinity);
        Array.Clear(_clipped);
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the current peak levels (UI thread), one per channel, in <see cref="InputUnit"/>.
    /// In gain-reduction mode, pass the reduction in positive dB (or linear gain ≤ 1).
    /// </summary>
    public void SetLevels(params float[] levels) => SetInputs(levels, _peakInput, _peak, checkClip: true);

    /// <summary>Sets the current RMS levels (enables the dual peak/RMS display).</summary>
    public void SetRmsLevels(params float[] levels)
    {
        _hasRms = true;
        SetInputs(levels, _rmsInput, _rms, checkClip: false);
    }

    private float ToDb(float v)
    {
        if (Mode == MeterMode.GainReduction)
        {
            return InputUnit == LevelUnit.Decibels ? MathF.Abs(v) : -Decibels.FromGain(MathF.Max(1e-6f, MathF.Abs(v)));
        }
        return InputUnit == LevelUnit.Decibels ? v : Decibels.FromGain(MathF.Abs(v), -200f);
    }

    private void SetInputs(ReadOnlySpan<float> levels, float[] inputs, LevelBallistics[] ballistics, bool checkClip)
    {
        if (levels.Length > ChannelCount) ChannelCount = levels.Length;
        for (int i = 0; i < levels.Length; i++)
        {
            float db = ToDb(levels[i]);
            inputs[i] = db;
            if (checkClip && Mode == MeterMode.Level && db >= ClipDb) _clipped[i] = true;
            // Instant attack even before the first frame tick (snapshots, detached meters).
            if (db > ballistics[i].LevelDb) ballistics[i].Update(db, 0.0);
        }
        InvalidateVisual();
        if (Host is not null && _ticker is not null) Host.AddTicker(_ticker);
    }

    private bool Step(double dt)
    {
        bool changed = false;
        int n = ChannelCount;
        if (Sources is { } sources)
        {
            for (int i = 0; i < Math.Min(n, sources.Count); i++)
            {
                float db = ToDb(sources[i].ReadAndReset());
                _peakInput[i] = db;
                if (Mode == MeterMode.Level && db >= ClipDb && !_clipped[i]) { _clipped[i] = true; changed = true; }
            }
        }
        if (RmsSources is { } rmsSources)
        {
            _hasRms = true;
            for (int i = 0; i < Math.Min(n, rmsSources.Count); i++) _rmsInput[i] = ToDb(rmsSources[i].ReadAndReset());
        }
        for (int i = 0; i < n; i++)
        {
            changed |= _peak[i].Update(_peakInput[i], dt);
            if (_hasRms) changed |= _rms[i].Update(_rmsInput[i], dt);
        }
        if (changed) InvalidateVisual();
        return true;
    }

    // ---------------------------------------------------------------- lifecycle

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _ticker ??= new MeterTicker(this);
        Host!.AddTicker(_ticker);
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    private sealed class MeterTicker(LevelMeter owner) : ITickable
    {
        public bool Tick(double elapsedSeconds) => owner.Host is not null && owner.Step(elapsedSeconds);
    }

    // ---------------------------------------------------------------- layout & rendering

    /// <inheritdoc />
    protected override Size DefaultSize => Orientation == Orientation.Vertical ? new Size(ShowScale ? 44f : 18f, 160f) : new Size(180f, ShowScale ? 30f : 12f);

    private bool Vertical => Orientation == Orientation.Vertical;

    private float ScaleThickness => !ShowScale ? 0f : Vertical ? 24f : 13f;

    // With a skin, the clip state goes to the skin (SkinState.StateIndex) instead of a drawn strip.
    private float ClipThickness => ShowClipIndicator && Mode == MeterMode.Level && Skin is null ? 5f : 0f;

    private void Areas(Rect widget, out Rect bars, out Rect scale, out Rect clip)
    {
        if (Vertical)
        {
            scale = ShowScale ? widget.TakeRight(ScaleThickness) : Rect.Empty;
            var rest = Rect.FromEdges(widget.X, widget.Y, widget.Right - ScaleThickness, widget.Bottom);
            clip = ClipThickness > 0f ? rest.TakeTop(ClipThickness) : Rect.Empty;
            bars = Rect.FromEdges(rest.X, rest.Y + (ClipThickness > 0f ? ClipThickness + 2f : 0f), rest.Right, rest.Bottom);
            if (ShowScale) scale = Rect.FromEdges(scale.X, bars.Y, scale.Right, bars.Bottom);
        }
        else
        {
            scale = ShowScale ? widget.TakeBottom(ScaleThickness) : Rect.Empty;
            var rest = Rect.FromEdges(widget.X, widget.Y, widget.Right, widget.Bottom - ScaleThickness);
            clip = ClipThickness > 0f ? rest.TakeRight(ClipThickness) : Rect.Empty;
            bars = Rect.FromEdges(rest.X, rest.Y, rest.Right - (ClipThickness > 0f ? ClipThickness + 2f : 0f), rest.Bottom);
            if (ShowScale) scale = Rect.FromEdges(bars.X, scale.Y, bars.Right, scale.Bottom);
        }
    }

    private Rect ChannelRect(Rect bars, int channel)
    {
        int n = ChannelCount;
        if (Vertical)
        {
            float w = (bars.Width - ChannelGap * (n - 1)) / n;
            return new Rect(bars.X + channel * (w + ChannelGap), bars.Y, MathF.Max(1f, w), bars.Height);
        }
        float h = (bars.Height - ChannelGap * (n - 1)) / n;
        return new Rect(bars.X, bars.Y + channel * (h + ChannelGap), bars.Width, MathF.Max(1f, h));
    }

    /// <summary>Position of a dB value along the bar, 0 (empty end) → 1 (full end).</summary>
    protected float Fraction(float db)
    {
        if (Mode == MeterMode.GainReduction) return Math.Clamp(db / MathF.Max(0.1f, MaxReductionDb), 0f, 1f);
        return Math.Clamp((db - _minDb) / MathF.Max(0.1f, _maxDb - _minDb), 0f, 1f);
    }

    private Color Low => LowColor ?? Color.FromRgb(0x3DD68C);
    private Color Warn => WarningColor ?? Theme.Palette.Warning;
    private Color Danger => DangerColor ?? Theme.Palette.Danger;

    /// <summary>Color of a level (gradient zones).</summary>
    protected Color ColorForDb(float db)
    {
        if (MeterStyle == MeterStyle.Solid || Mode == MeterMode.GainReduction) return EffectiveStyle.Accent;
        if (db >= DangerDb) return Danger;
        if (db >= WarningDb) return Color.Lerp(Warn, Danger, Math.Clamp((db - WarningDb) / MathF.Max(0.1f, DangerDb - WarningDb), 0f, 1f) * 0.35f);
        return Low;
    }

    // Sub-rectangle of a channel bar between two fractions (0 = empty end).
    private Rect BarSpan(Rect bar, float from, float to)
    {
        bool reversed = Mode == MeterMode.GainReduction;
        if (Vertical)
        {
            float a = reversed ? bar.Y + bar.Height * from : bar.Bottom - bar.Height * to;
            float b = reversed ? bar.Y + bar.Height * to : bar.Bottom - bar.Height * from;
            return Rect.FromEdges(bar.X, a, bar.Right, b);
        }
        float l = reversed ? bar.Right - bar.Width * to : bar.X + bar.Width * from;
        float r = reversed ? bar.Right - bar.Width * from : bar.X + bar.Width * to;
        return Rect.FromEdges(l, bar.Y, r, bar.Bottom);
    }

    private LinearGradientPaint GradientFor(Rect bars)
    {
        if (_gradient is not null && _gradientRect == bars) return _gradient;
        float fw = Fraction(WarningDb), fd = Fraction(DangerDb);
        var stops = new GradientStop[]
        {
            new(0f, Low.Darken(0.15f)),
            new(MathF.Max(0f, fw - 0.06f), Low),
            new(fw, Warn),
            new(MathF.Max(fw, fd - 0.02f), Color.Lerp(Warn, Danger, 0.35f)),
            new(fd, Danger),
            new(1f, Danger),
        };
        var start = Vertical ? new Point(bars.X, bars.Bottom) : new Point(bars.X, bars.Y);
        var end = Vertical ? new Point(bars.X, bars.Y) : new Point(bars.Right, bars.Y);
        _gradient = new LinearGradientPaint(start, end, stops, RelativeToBounds: false);
        _gradientRect = bars;
        return _gradient;
    }

    /// <summary>
    /// Draws the meter. With a <see cref="Control.Skin"/> (for example a <see cref="FilmStripSkin"/> whose frame follows
    /// the level), the skin draws each channel in its own bar area: <see cref="SkinState.Value"/> is the channel level
    /// mapped to [0, 1] over <see cref="MinDb"/>…<see cref="MaxDb"/> (or the reduction range in gain-reduction mode) and
    /// <see cref="SkinState.StateIndex"/> is 1 while the channel's clip indicator is latched. The scale is still drawn.
    /// </summary>
    /// <param name="dc">Drawing context.</param>
    protected override void OnRender(DrawingContext dc)
    {
        if (Skin is null)
        {
            base.OnRender(dc);
            return;
        }
        var style = EffectiveStyle;
        LayoutParts(style, out var label, out var widget, out var value);
        var states = ChannelSkinStates(widget, out var scale);
        foreach (var state in states) Skin.Render(dc, this, state);
        if (!scale.IsEmpty) RenderScale(dc, scale, Theme.Palette);
        if (!label.IsEmpty) RenderLabel(dc, label, style);
        if (!value.IsEmpty) RenderValue(dc, value, style);
    }

    /// <summary>Skin state of each channel: bar area, level in [0, 1], 1 when the clip indicator is latched.</summary>
    internal SkinState[] ChannelSkinStates(Rect widget, out Rect scale)
    {
        Areas(widget, out var bars, out scale, out _);
        var states = new SkinState[ChannelCount];
        for (int ch = 0; ch < states.Length; ch++)
        {
            states[ch] = new SkinState(ChannelRect(bars, ch), Fraction(_peak[ch].LevelDb), _clipped[ch] ? 1 : 0, IsMouseOver, IsPressed, IsEffectivelyEnabled, IsFocused);
        }
        return states;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        Areas(widget, out var bars, out var scale, out var clip);
        var track = p.Background.Darken(0.35f);
        bool reduction = Mode == MeterMode.GainReduction;

        for (int ch = 0; ch < ChannelCount; ch++)
        {
            var bar = ChannelRect(bars, ch);
            dc.FillRect(bar, track);
            float level = _peak[ch].LevelDb;
            float f = Fraction(level);

            if (MeterStyle == MeterStyle.Segments && !reduction) RenderSegments(dc, bar, f, _hasRms ? Fraction(_rms[ch].LevelDb) : -1f);
            else
            {
                if (_hasRms && !reduction)
                {
                    // Peak bar dimmed (track-colored veil above the RMS level), RMS bar bright.
                    float fr = MathF.Min(f, Fraction(_rms[ch].LevelDb));
                    RenderFill(dc, bars, BarSpan(bar, 0f, f), 1f);
                    dc.FillRect(BarSpan(bar, fr, f), track.WithAlpha(0.6f));
                }
                else
                {
                    RenderFill(dc, bars, BarSpan(bar, 0f, f), 1f);
                }
            }

            if (ShowPeakHold && !reduction)
            {
                float hold = _peak[ch].HoldDb;
                if (hold > _minDb + 0.01f)
                {
                    float fh = Fraction(hold);
                    var marker = Vertical
                        ? new Rect(bar.X, MathF.Round(bar.Bottom - bar.Height * fh) - 1f, bar.Width, 2f)
                        : new Rect(MathF.Round(bar.X + bar.Width * fh) - 1f, bar.Y, 2f, bar.Height);
                    dc.FillRect(marker, ColorForDb(hold).Lighten(0.2f));
                }
            }

            if (!clip.IsEmpty)
            {
                var c = Vertical
                    ? new Rect(bar.X, clip.Y, bar.Width, clip.Height)
                    : new Rect(clip.X, bar.Y, clip.Width, bar.Height);
                dc.FillRect(c, _clipped[ch] ? Danger : track.Lighten(0.06f));
            }
        }

        if (!scale.IsEmpty) RenderScale(dc, scale, p);
    }

    private void RenderFill(DrawingContext dc, Rect bars, Rect fill, float opacity)
    {
        if (fill.IsEmpty) return;
        if (MeterStyle == MeterStyle.Gradient && Mode == MeterMode.Level)
        {
            if (opacity < 1f)
            {
                dc.PushOpacity(opacity);
                dc.FillRect(fill, GradientFor(bars));
                dc.PopOpacity();
            }
            else dc.FillRect(fill, GradientFor(bars));
        }
        else
        {
            dc.FillRect(fill, EffectiveStyle.Accent.Fade(opacity));
        }
    }

    private void RenderSegments(DrawingContext dc, Rect bar, float fraction, float rmsFraction)
    {
        float length = Vertical ? bar.Height : bar.Width;
        float pitch = MathF.Max(1.5f, SegmentLength + SegmentGap);
        int count = Math.Max(1, (int)((length + SegmentGap) / pitch));
        float seg = (length - SegmentGap * (count - 1)) / count;
        for (int i = 0; i < count; i++)
        {
            float f0 = i * (seg + SegmentGap) / length, f1 = (i * (seg + SegmentGap) + seg) / length;
            float centerDb = _minDb + (f0 + f1) * 0.5f * (_maxDb - _minDb);
            var color = ColorForDb(centerDb);
            bool lit = f1 <= fraction + 0.5f * seg / length;
            if (rmsFraction >= 0f && lit && f1 > rmsFraction + 0.5f * seg / length) color = color.Fade(0.5f);
            dc.FillRect(BarSpan(bar, f0, f1), lit ? color : color.WithAlpha(0.09f));
        }
    }

    private void RenderScale(DrawingContext dc, Rect scale, Palette p)
    {
        var marks = ScaleMarks ?? AutoMarks();
        var style = Theme.Typography.Small with { Size = 9f, Align = Vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center, Ellipsis = false };
        var tick = p.TextDim.WithAlpha(0.6f);
        float lastPos = float.NaN;
        foreach (float db in marks)
        {
            float f = Fraction(db);
            if (Mode == MeterMode.Level && (db < _minDb - 0.01f || db > _maxDb + 0.01f)) continue;
            if (Mode == MeterMode.GainReduction && (db < 0f || db > MaxReductionDb)) continue;
            string text = Mode == MeterMode.GainReduction ? db.ToString("0", CultureInfo.InvariantCulture)
                : db > 0f ? "+" + db.ToString("0", CultureInfo.InvariantCulture) : db.ToString("0", CultureInfo.InvariantCulture);
            if (Vertical)
            {
                float y = Mode == MeterMode.GainReduction ? scale.Y + scale.Height * f : scale.Bottom - scale.Height * f;
                if (!float.IsNaN(lastPos) && MathF.Abs(lastPos - y) < 10f) continue;
                lastPos = y;
                // No tick mark beside vertical labels: it would read as a minus sign.
                dc.DrawText(text, new Rect(scale.X + 3f, y - 6f, scale.Width - 3f, 12f), style, p.TextDim);
            }
            else
            {
                float x = Mode == MeterMode.GainReduction ? scale.Right - scale.Width * f : scale.X + scale.Width * f;
                if (!float.IsNaN(lastPos) && MathF.Abs(lastPos - x) < 22f) continue;
                lastPos = x;
                float xx = MathF.Round(x) + 0.5f;
                dc.DrawLine(new Point(xx, scale.Y + 1f), new Point(xx, scale.Y + 3.5f), tick, 1f);
                dc.DrawText(text, new Rect(x - 14f, scale.Y + 3f, 28f, scale.Height - 3f), style, p.TextDim);
            }
        }
    }

    private IReadOnlyList<float> AutoMarks()
    {
        if (Mode == MeterMode.GainReduction) return [0f, 3f, 6f, 9f, 12f, 18f, 24f, 30f, 36f, 48f];
        return DefaultMarks;
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        // Click anywhere on the meter resets the clip indicators and peak holds.
        if (_clipped.Any(c => c) || ShowPeakHold)
        {
            ResetClip();
            foreach (var b in _peak)
            {
                float level = b.LevelDb;
                b.Reset();
                b.Update(level, 0.0);
            }
            e.Handled = true;
        }
    }
}
