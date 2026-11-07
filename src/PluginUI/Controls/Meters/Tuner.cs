// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Automation;
using PluginUI.Dsp;

namespace PluginUI;

/// <summary>Look of a <see cref="Tuner"/>.</summary>
public enum TunerStyle
{
    /// <summary>A needle on an arc from −50 to +50 cents.</summary>
    Needle,
    /// <summary>A row of lights around the centre, as on pedal tuners.</summary>
    Strip,
}

/// <summary>
/// Chromatic tuner: the nearest note, how many cents it is off, the frequency and the reference. It analyzes audio with
/// <see cref="PitchDetector"/> (YIN) from an <see cref="AudioRingBuffer"/>, or shows a frequency the plug-in measures
/// itself (<see cref="FrequencySource"/>).
/// </summary>
/// <remarks>
/// The note stays shown for a moment after the signal stops, then the display dims. Within <see cref="InTuneCents"/> the
/// display turns green. The reference (A4, 440 Hz by default) can be bound to a parameter. Screen readers hear the note,
/// the deviation and "in tune" when the note or the in-tune state changes, not at every frame.
/// </remarks>
/// <example>
/// <code>
/// var tuner = new Tuner { Source = processor.InputScope, SampleRate = (float)processor.SampleRate };
/// tuner.ReferenceParameter = Parameters[model.Reference];   // 415 … 466 Hz
/// </code>
/// </example>
public class Tuner : Control
{
    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] FlatNames = ["C", "D♭", "D", "E♭", "E", "F", "G♭", "G", "A♭", "A", "B♭", "B"];

    private PitchDetector? _detector;
    private float[] _window = [];
    private RefreshTicker? _ticker;
    private double _frequency;
    private double _shownCents;
    private double _holdSeconds;
    private long _lastWritten = -1;
    private string? _reportedNote;
    private bool _reportedInTune;

    /// <summary>Creates a tuner.</summary>
    public Tuner()
    {
    }

    /// <summary>Audio to analyze, or <c>null</c> (then <see cref="FrequencySource"/> is used).</summary>
    [Browsable(false)]
    public AudioRingBuffer? Source { get; set; }

    /// <summary>Sample rate of <see cref="Source"/>, in Hz.</summary>
    [Category("Data")]
    public float SampleRate { get; set; } = 48000f;

    /// <summary>Lowest pitch looked for in Hz.</summary>
    [Category("Data")]
    public float MinFrequency { get; set; } = 30f;

    /// <summary>Highest pitch looked for in Hz.</summary>
    [Category("Data")]
    public float MaxFrequency { get; set; } = 2000f;

    /// <summary>Frequency measured elsewhere, polled every frame (Hz; 0 or less = no pitch), used when <see cref="Source"/> is <c>null</c>.</summary>
    [Browsable(false)]
    public Func<double>? FrequencySource { get; set; }

    /// <summary>Frequency of A4 in Hz (400 … 480; default 440).</summary>
    [Category("Value")]
    public double ReferenceHz
    {
        get => ReferenceParameter is { } p ? p.ToPlain(p.NormalizedValue) : _reference;
        set
        {
            value = Math.Clamp(value, 400.0, 480.0);
            if (ReferenceParameter is { } p)
            {
                p.BeginEdit();
                p.PerformEdit(p.ToNormalized(value));
                p.EndEdit();
            }
            else _reference = value;
            InvalidateVisual();
        }
    }

    private double _reference = 440.0;

    /// <summary>Reference parameter (plain value in Hz), or <c>null</c>.</summary>
    [Browsable(false)]
    public IParameter? ReferenceParameter { get; set; }

    /// <summary>Deviation shown as in tune (green), in cents.</summary>
    [Category("Behavior")]
    public float InTuneCents { get; set; } = 3f;

    /// <summary>Look of the display.</summary>
    [Category("Appearance")]
    public TunerStyle Look { get; set; } = TunerStyle.Needle;

    /// <summary>Names the black keys with flats (D♭) instead of sharps (C#).</summary>
    [Category("Appearance")]
    public bool UseFlats { get; set; }

    /// <summary>Octave number of middle C (MIDI 60): 4 (default) or 3.</summary>
    [Category("Data")]
    public int MiddleCOctave { get; set; } = 4;

    /// <summary>Shows the frequency and the reference under the note.</summary>
    [Category("Appearance")]
    public bool ShowFrequency { get; set; } = true;

    /// <summary>Time the last note stays after the signal stops, in seconds.</summary>
    [Category("Behavior")]
    public double HoldSeconds { get; set; } = 0.6;

    /// <summary>Frequency shown in Hz (0 = none).</summary>
    [Browsable(false)]
    public double Frequency => _frequency;

    /// <summary>Nearest note (MIDI number, −1 = none).</summary>
    [Browsable(false)]
    public int Note => _frequency > 0.0 ? (int)Math.Round(MidiOf(_frequency)) : -1;

    /// <summary>Deviation from <see cref="Note"/> in cents (−50 … +50).</summary>
    [Browsable(false)]
    public double Cents => _frequency > 0.0 ? (MidiOf(_frequency) - Note) * 100.0 : 0.0;

    /// <summary><c>true</c> when a note is shown within <see cref="InTuneCents"/>.</summary>
    [Browsable(false)]
    public bool IsInTune => Note >= 0 && Math.Abs(Cents) <= InTuneCents;

    /// <inheritdoc />
    protected override Size DefaultSize => new(260f, 150f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new TunerAutomationPeer(this);

    private double MidiOf(double frequency) => 69.0 + 12.0 * Math.Log2(frequency / ReferenceHz);

    /// <summary>Name of a note with its octave ("A4", "C#2").</summary>
    public string NoteName(int note) => (UseFlats ? FlatNames : SharpNames)[((note % 12) + 12) % 12] + (note / 12 - 5 + MiddleCOctave).ToString(CultureInfo.InvariantCulture);

    /// <summary>Shows a frequency now (tests, or plug-ins that push the value), as a new measurement.</summary>
    public void SetFrequency(double frequency)
    {
        if (frequency > 0.0)
        {
            bool sameNote = _frequency > 0.0 && (int)Math.Round(MidiOf(frequency)) == Note;
            _frequency = frequency;
            double cents = Cents;
            _shownCents = sameNote ? _shownCents + (cents - _shownCents) * 0.5 : cents;
            _holdSeconds = HoldSeconds;
        }
        InvalidateVisual();
        Report();
    }

    // ---------------------------------------------------------------- analysis

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, Analyze));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    /// <summary>One frame: measures (or reads) the pitch, smooths the needle, holds then releases the note.</summary>
    internal void Analyze(double elapsed)
    {
        double measured = 0.0;
        bool fresh = false;
        if (Source is { } source)
        {
            long written = source.TotalWritten;
            if (written != _lastWritten)
            {
                _lastWritten = written;
                if (_detector is null || _detector.SampleRate != SampleRate || _detector.MinFrequency != MinFrequency || Math.Abs(_detector.MaxFrequency - Math.Min(MaxFrequency, SampleRate / 4.0)) > 1e-6)
                {
                    _detector = new PitchDetector(SampleRate, MinFrequency, MaxFrequency);
                    _window = new float[_detector.WindowSize];
                }
                if (source.CopyLatest(_window) == _window.Length)
                {
                    var estimate = _detector.Detect(_window);
                    if (estimate.HasPitch && estimate.Clarity >= 0.8) measured = estimate.Frequency;
                }
                fresh = true;
            }
        }
        else if (FrequencySource is { } read)
        {
            measured = read();
            fresh = true;
        }

        if (fresh && measured > 0.0)
        {
            SetFrequency(measured);
            return;
        }
        if (_frequency > 0.0)
        {
            _holdSeconds -= elapsed;
            if (_holdSeconds <= -0.4)
            {
                _frequency = 0.0;
                Report();
            }
            InvalidateVisual();
        }
    }

    private void Report()
    {
        string? note = Note >= 0 ? NoteName(Note) : null;
        bool inTune = IsInTune;
        if (note == _reportedNote && inTune == _reportedInTune) return;
        _reportedNote = note;
        _reportedInTune = inTune;
        NotifyAutomationStateChanged();
    }

    /// <summary>What a screen reader says: "A4, +3 cents, in tune" or "no note".</summary>
    internal string Describe()
    {
        var t = Strings;
        if (Note < 0) return t.TunerNoPitch;
        int cents = (int)Math.Round(Cents);
        string text = string.Format(CultureInfo.InvariantCulture, t.TunerFormat, NoteName(Note), (cents > 0 ? "+" : "") + cents.ToString(CultureInfo.InvariantCulture));
        return IsInTune ? text + ", " + t.TunerInTune : text;
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        base.LayoutParts(style with { ShowValue = false }, out label, out widget, out value);
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        GraphHelpers.DrawPanel(dc, Theme, widget);
        var area = widget.Deflate(8f);
        bool active = _frequency > 0.0;
        float fade = active ? (_holdSeconds >= 0.0 ? 1f : (float)Math.Clamp(1.0 + _holdSeconds / 0.4, 0.25, 1.0)) : 0.25f;
        bool inTune = IsInTune;
        var green = p.Success;
        var cue = inTune ? green : Math.Abs(_shownCents) <= 15.0 ? p.Warning : p.Danger;
        double shown = Math.Clamp(_shownCents, -50.0, 50.0);
        float infoHeight = ShowFrequency ? 16f : 0f;

        if (Look == TunerStyle.Needle)
        {
            // Arc from −50 to +50 cents over 120°, centred below the note.
            float radius = MathF.Min(area.Width * 0.48f, (area.Height - infoHeight) * 0.95f);
            var center = new Point(area.Center.X, area.Y + radius + 6f);
            for (int c = -50; c <= 50; c += 5)
            {
                float angle = (float)((-90.0 + c * 1.2) * Math.PI / 180.0);
                bool major = c % 10 == 0;
                float inner = radius - (major ? 10f : 6f);
                var color = Math.Abs(c) <= InTuneCents ? green : major ? p.TextDim : p.Border.Lighten(0.1f);
                dc.DrawLine(new Point(center.X + inner * MathF.Cos(angle), center.Y + inner * MathF.Sin(angle)),
                            new Point(center.X + radius * MathF.Cos(angle), center.Y + radius * MathF.Sin(angle)), color, major ? 1.5f : 1f);
            }
            if (active)
            {
                float angle = (float)((-90.0 + shown * 1.2) * Math.PI / 180.0);
                var tip = new Point(center.X + (radius - 2f) * MathF.Cos(angle), center.Y + (radius - 2f) * MathF.Sin(angle));
                var root = new Point(center.X + radius * 0.7f * MathF.Cos(angle), center.Y + radius * 0.7f * MathF.Sin(angle));
                dc.DrawLine(root, tip, cue.WithAlpha(fade), 2.5f, StrokeStyle.Round);
            }
            DrawNote(dc, new Rect(area.X, center.Y - radius * 0.62f, area.Width, radius * 0.5f), cue, fade, active, p);
        }
        else
        {
            // 21 lights, the centre one green.
            const int lights = 21;
            float gap = 3f;
            float lightWidth = (area.Width - gap * (lights - 1)) / lights;
            var row = new Rect(area.X, area.Y + 4f, area.Width, MathF.Min(18f, area.Height * 0.2f));
            int lit = active ? (int)Math.Round(shown / 5.0) + lights / 2 : -1;
            for (int i = 0; i < lights; i++)
            {
                var r = new Rect(row.X + i * (lightWidth + gap), row.Y, lightWidth, row.Height);
                bool centre = i == lights / 2;
                var off = (centre ? green : p.Track).WithAlpha(0.25f);
                bool on = active && (i == lit || inTune && centre);
                dc.FillRoundedRect(r, 2f, on ? (centre ? green : cue).WithAlpha(fade) : off);
            }
            DrawNote(dc, Rect.FromEdges(area.X, row.Bottom + 6f, area.Right, area.Bottom - infoHeight - 2f), cue, fade, active, p);
        }

        if (ShowFrequency)
        {
            var info = Theme.Typography.Small with { Size = 9.5f, VerticalAlign = VerticalAlignment.Center };
            var bottom = new Rect(area.X, area.Bottom - infoHeight, area.Width, infoHeight);
            string hz = active ? InvariantNumber.Format(_frequency, "0.0") + " Hz" : "— Hz";
            dc.DrawText(hz, bottom, info, p.TextDim.WithAlpha(active ? 1f : 0.6f));
            dc.DrawText("A4 = " + InvariantNumber.Format(ReferenceHz, "0.#") + " Hz", bottom, info with { Align = HorizontalAlignment.Right }, p.TextDim);
            if (active)
            {
                int cents = (int)Math.Round(_shownCents);
                dc.DrawText((cents > 0 ? "+" : "") + cents.ToString(CultureInfo.InvariantCulture) + " ¢", bottom, info with { Align = HorizontalAlignment.Center }, cue.WithAlpha(fade));
            }
        }
    }

    private void DrawNote(DrawingContext dc, Rect r, Color cue, float fade, bool active, Palette p)
    {
        float size = Math.Clamp(r.Height * 0.8f, 12f, 48f);
        var big = Theme.Typography.Normal with { Size = size, Weight = FontWeight.SemiBold, Align = HorizontalAlignment.Center, VerticalAlign = VerticalAlignment.Center, Ellipsis = false };
        if (!active)
        {
            dc.DrawText("–", r, big, p.TextDim.WithAlpha(0.5f));
            return;
        }
        string name = NoteName(Note);
        dc.DrawText(name, r, big, (IsInTune ? cue : p.Text).WithAlpha(fade));
    }
}

/// <summary>Peer of a <see cref="Tuner"/>: a read-only value "A4, +3 cents, in tune".</summary>
/// <param name="owner">The tuner.</param>
public class TunerAutomationPeer(Tuner owner) : ElementAutomationPeer<Tuner>(owner), IValuePattern
{
    private string? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationTuner;

    /// <inheritdoc />
    public string Value => Owner.Describe();

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(string value) => throw new InvalidOperationException("A tuner is read-only.");

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
