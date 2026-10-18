// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Horizontal piano keyboard: play with the mouse (velocity from the vertical click position, glissando while
/// dragging), with the computer keyboard when focused (QWERTY rows, Z/X shift octaves), and show notes played
/// elsewhere (host MIDI, sequencer) with the thread-safe <see cref="SetNoteState"/>. iPlug2 <c>IVKeyboardControl</c>.
/// </summary>
public class PianoKeyboard : Control
{
    private static readonly bool[] IsBlackInOctave = [false, true, false, true, false, false, true, false, true, false, true, false];
    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    // QWERTY mapping (US layout virtual keys): bottom "piano" row A..L = white keys, W E T Y U O P = black keys.
    private static readonly (Key Key, int Offset)[] QwertyMap =
    [
        (Key.A, 0), (Key.W, 1), (Key.S, 2), (Key.E, 3), (Key.D, 4), (Key.F, 5), (Key.T, 6), (Key.G, 7),
        (Key.Y, 8), (Key.H, 9), (Key.U, 10), (Key.J, 11), (Key.K, 12), (Key.O, 13), (Key.L, 14), (Key.P, 15),
    ];

    private readonly int[] _external = new int[128]; // written from any thread
    private readonly bool[] _local = new bool[128];
    private readonly Dictionary<Key, int> _qwertyDown = new();
    private int _externalDirty;
    private int _mouseNote = -1;
    private int _lowest = 48, _highest = 84;
    private int _qwertyOctave = 4;
    private KeyboardTicker? _ticker;

    /// <summary>Creates a keyboard (C3–C6 with middle C = C4... see <see cref="MiddleCOctave"/>).</summary>
    public PianoKeyboard()
    {
        Focusable = true;
    }

    /// <summary>Lowest note (MIDI number; adjusted down to a white key).</summary>
    [Category("Data")]
    public int LowestNote { get => _lowest; set { _lowest = Math.Clamp(value, 0, 126); if (IsBlack(_lowest)) _lowest--; if (_highest <= _lowest) _highest = Math.Min(127, _lowest + 12); InvalidateVisual(); } }

    /// <summary>Highest note (MIDI number; adjusted up to a white key).</summary>
    [Category("Data")]
    public int HighestNote { get => _highest; set { _highest = Math.Clamp(value, 1, 127); if (IsBlack(_highest)) _highest++; if (_highest <= _lowest) _lowest = Math.Max(0, _highest - 12); InvalidateVisual(); } }

    /// <summary>Octave number of middle C (MIDI 60): 4 (scientific, default) or 3 (Yamaha / many DAWs).</summary>
    [Category("Data")]
    public int MiddleCOctave { get; set; } = 4;

    /// <summary>Labels the C keys with their octave (C3, C4...).</summary>
    [Category("Appearance")]
    public bool ShowOctaveLabels { get; set; } = true;

    /// <summary>Plays notes from the computer keyboard while focused.</summary>
    [Category("Behavior")]
    public bool QwertyEnabled { get; set; } = true;

    /// <summary>Velocity of notes played from the computer keyboard (0..1).</summary>
    [Category("Behavior")]
    public float QwertyVelocity { get; set; } = 0.8f;

    /// <summary>Height of black keys relative to white keys.</summary>
    [Category("Layout")]
    public float BlackKeyHeight { get; set; } = 0.62f;

    /// <summary>Width of black keys relative to white keys.</summary>
    [Category("Layout")]
    public float BlackKeyWidth { get; set; } = 0.6f;

    /// <summary>Highlight color of notes played on this keyboard (null = theme accent).</summary>
    [Category("Appearance")]
    public Color? PlayedColor { get; set; }

    /// <summary>Highlight color of notes set with <see cref="SetNoteState"/> (null = theme alternate accent).</summary>
    [Category("Appearance")]
    public Color? ExternalColor { get; set; }

    /// <summary>
    /// Notes sounding elsewhere (typically the processor's voices, fed from the audio thread), shown like
    /// <see cref="SetNoteState"/> and polled every frame.
    /// </summary>
    [Browsable(false)]
    public NoteActivity? ActivitySource { get; set; }

    /// <summary>Raised when a note starts (note, velocity 0..1).</summary>
    public event Action<PianoKeyboard, int, float>? NoteOn;

    /// <summary>Raised when a note stops.</summary>
    public event Action<PianoKeyboard, int>? NoteOff;

    /// <summary><c>true</c> for black keys.</summary>
    public static bool IsBlack(int note) => IsBlackInOctave[((note % 12) + 12) % 12];

    /// <summary>Note name with octave ("C4", "F#2") using <see cref="MiddleCOctave"/>.</summary>
    public string NoteName(int note) => SharpNames[((note % 12) + 12) % 12] + (note / 12 - 5 + MiddleCOctave).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Shows or hides a note played elsewhere. Thread-safe (callable from the audio or MIDI thread);
    /// the display updates on the next frame.
    /// </summary>
    public void SetNoteState(int note, bool on)
    {
        if ((uint)note > 127) return;
        Volatile.Write(ref _external[note], on ? 1 : 0);
        Interlocked.Exchange(ref _externalDirty, 1);
    }

    /// <summary>Clears every externally set note (thread-safe).</summary>
    public void ClearNoteStates()
    {
        for (int i = 0; i < 128; i++) Volatile.Write(ref _external[i], 0);
        Interlocked.Exchange(ref _externalDirty, 1);
    }

    /// <summary><c>true</c> if a note is held on this keyboard (mouse or computer keyboard).</summary>
    public bool IsNotePlayed(int note) => (uint)note <= 127 && _local[note];

    /// <inheritdoc />
    protected override Size DefaultSize => new(Math.Max(1, WhiteCount) * 16f, 72f);

    private int WhiteCount
    {
        get
        {
            int n = 0;
            for (int i = _lowest; i <= _highest; i++) if (!IsBlack(i)) n++;
            return n;
        }
    }

    private Rect KeyRect(Rect area, int note, float whiteWidth)
    {
        int whiteIndex = 0;
        for (int i = _lowest; i < note; i++) if (!IsBlack(i)) whiteIndex++;
        if (!IsBlack(note)) return new Rect(area.X + whiteIndex * whiteWidth, area.Y, whiteWidth, area.Height);
        float bw = whiteWidth * BlackKeyWidth;
        // Classic offsets: C#/D# and F#/G#/A# are shifted to look like a real keyboard.
        float shift = (note % 12) switch { 1 => -0.1f, 3 => 0.1f, 6 => -0.12f, 8 => 0f, 10 => 0.12f, _ => 0f };
        float cx = area.X + whiteIndex * whiteWidth + shift * bw;
        return new Rect(cx - bw * 0.5f, area.Y, bw, area.Height * BlackKeyHeight);
    }

    /// <summary>Note under a point (black keys first), or -1.</summary>
    public int NoteAt(Point point)
    {
        LayoutParts(EffectiveStyle, out _, out var area, out _);
        if (!area.Contains(point)) return -1;
        float ww = area.Width / Math.Max(1, WhiteCount);
        for (int n = _lowest; n <= _highest; n++)
        {
            if (IsBlack(n) && KeyRect(area, n, ww).Contains(point)) return n;
        }
        for (int n = _lowest; n <= _highest; n++)
        {
            if (!IsBlack(n) && KeyRect(area, n, ww).Contains(point)) return n;
        }
        return -1;
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
        int whites = Math.Max(1, WhiteCount);
        float ww = widget.Width / whites;
        var played = PlayedColor ?? style.Accent;
        var external = ExternalColor ?? p.AccentAlt;
        var whiteKey = Color.FromRgb(0xEDEDEF);
        var blackKey = Color.FromRgb(0x1E1F23);
        var labelStyle = Theme.Typography.Small with { Size = MathF.Min(9.5f, ww * 0.55f), Align = HorizontalAlignment.Center, VerticalAlign = VerticalAlignment.Bottom, Ellipsis = false };
        float radius = MathF.Min(3f, ww * 0.18f);

        dc.FillRect(widget, p.Background.Darken(0.4f));
        for (int n = _lowest; n <= _highest; n++)
        {
            if (IsBlack(n)) continue;
            var r = KeyRect(widget, n, ww);
            var key = Rect.FromEdges(r.X + 0.5f, r.Y, r.Right - 0.5f, r.Bottom);
            var fill = HighlightFor(n, whiteKey, played, external, 0.75f);
            dc.FillRoundedRect(key, radius, Paint.Vertical(fill.Darken(0.06f), fill));
            dc.FillRect(new Rect(key.X, key.Y, key.Width, radius + 1f), fill.Darken(0.06f)); // square top corners
            if (ShowOctaveLabels && n % 12 == 0 && ww >= 9f)
            {
                dc.DrawText(NoteName(n), Rect.FromEdges(key.X, key.Y, key.Right, key.Bottom - 3f), labelStyle, Color.FromRgb(0x6B6E76));
            }
        }
        for (int n = _lowest; n <= _highest; n++)
        {
            if (!IsBlack(n)) continue;
            var r = KeyRect(widget, n, ww);
            var fill = HighlightFor(n, blackKey, played, external, 0.85f);
            dc.FillRoundedRect(new Rect(r.X + 1f, r.Y + 1f, r.Width, r.Height), radius, Color.Black.WithAlpha(0.35f));
            dc.FillRoundedRect(r, radius, Paint.Vertical(fill.Lighten(0.12f), fill));
            if (fill == blackKey)
            {
                var highlight = Rect.FromEdges(r.X + r.Width * 0.18f, r.Y + 1f, r.Right - r.Width * 0.18f, r.Bottom - r.Height * 0.12f);
                dc.FillRoundedRect(highlight, radius * 0.6f, Color.White.WithAlpha(0.05f));
            }
        }
        // Top shadow line (felt strip).
        dc.FillRect(widget.TakeTop(2f), Color.Black.WithAlpha(0.45f));
        if (IsFocused && Host?.ShowFocusRings == true) dc.DrawRect(widget.Inflate(-0.5f), p.Focus, 1f);
    }

    private Color HighlightFor(int note, Color normal, Color played, Color external, float amount)
    {
        if (_local[note]) return Color.Lerp(normal, played, amount);
        if (Volatile.Read(ref _external[note]) != 0 || ActivitySource?.IsOn(note) == true) return Color.Lerp(normal, external, amount);
        return normal;
    }

    // ---------------------------------------------------------------- notes

    private void Start(int note, float velocity)
    {
        if ((uint)note > 127 || _local[note]) return;
        _local[note] = true;
        NoteOn?.Invoke(this, note, Math.Clamp(velocity, 0.01f, 1f));
        InvalidateVisual();
    }

    private void Stop(int note)
    {
        if ((uint)note > 127 || !_local[note]) return;
        _local[note] = false;
        NoteOff?.Invoke(this, note);
        InvalidateVisual();
    }

    /// <summary>Stops every note played on this keyboard.</summary>
    public void ReleaseAll()
    {
        for (int i = 0; i < 128; i++) Stop(i);
        _qwertyDown.Clear();
        _mouseNote = -1;
    }

    private float VelocityAt(Point point, int note)
    {
        LayoutParts(EffectiveStyle, out _, out var area, out _);
        float ww = area.Width / Math.Max(1, WhiteCount);
        var r = KeyRect(area, note, ww);
        return Math.Clamp(0.2f + 0.8f * (point.Y - r.Y) / MathF.Max(1f, r.Height), 0.05f, 1f);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        int note = NoteAt(e.Position);
        if (note < 0) return;
        e.Handled = true;
        IsPressed = true;
        _mouseNote = note;
        Start(note, VelocityAt(e.Position, note));
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mouseNote < 0 || !IsMouseCaptured) return;
        int note = NoteAt(e.Position);
        if (note == _mouseNote) return;
        Stop(_mouseNote);
        _mouseNote = note;
        if (note >= 0) Start(note, VelocityAt(e.Position, note));
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_mouseNote >= 0) Stop(_mouseNote);
        _mouseNote = -1;
        IsPressed = false;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        if (_mouseNote >= 0) Stop(_mouseNote);
        _mouseNote = -1;
        IsPressed = false;
    }

    /// <inheritdoc />
    protected internal override void OnGotFocus()
    {
        base.OnGotFocus();
        if (QwertyEnabled) Host?.RequestKeyboardFocus();
    }

    /// <inheritdoc />
    protected internal override void OnLostFocus()
    {
        base.OnLostFocus();
        ReleaseQwertyNotes();
    }

    private void ReleaseQwertyNotes()
    {
        foreach (var note in _qwertyDown.Values) Stop(note);
        _qwertyDown.Clear();
    }

    // The window lost the keyboard (click in the DAW): the key-ups will never come, release the computer-keyboard notes.
    private void OnHostKeyboardFocusChanged(UIHost host, bool focused)
    {
        if (!focused) ReleaseQwertyNotes();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !QwertyEnabled || !IsEffectivelyEnabled || e.Control || (e.Modifiers & ModifierKeys.Alt) != 0) return;
        if (e.Key == Key.Z) { _qwertyOctave = Math.Max(0, _qwertyOctave - 1); e.Handled = true; return; }
        if (e.Key == Key.X) { _qwertyOctave = Math.Min(9, _qwertyOctave + 1); e.Handled = true; return; }
        foreach (var (key, offset) in QwertyMap)
        {
            if (key != e.Key) continue;
            e.Handled = true;
            if (e.IsRepeat || _qwertyDown.ContainsKey(key)) return;
            int note = (_qwertyOctave + 1) * 12 + offset; // octave 4 → MIDI 60
            if (note > 127) return;
            _qwertyDown[key] = note;
            Start(note, QwertyVelocity);
            return;
        }
    }

    /// <inheritdoc />
    protected internal override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_qwertyDown.Remove(e.Key, out int note))
        {
            Stop(note);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- external notes

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _ticker ??= new KeyboardTicker(this);
        Host!.AddTicker(_ticker);
        Host.KeyboardFocusChanged += OnHostKeyboardFocusChanged;
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        ReleaseAll();
        if (Host is { } host)
        {
            host.KeyboardFocusChanged -= OnHostKeyboardFocusChanged;
            if (_ticker is not null) host.RemoveTicker(_ticker);
        }
        base.OnDetached();
    }

    private sealed class KeyboardTicker(PianoKeyboard owner) : ITickable
    {
        private int _activityVersion = -1;

        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            bool dirty = Interlocked.Exchange(ref owner._externalDirty, 0) != 0;
            if (owner.ActivitySource is { } activity)
            {
                int version = activity.Version;
                if (version != _activityVersion)
                {
                    _activityVersion = version;
                    dirty = true;
                }
            }
            if (dirty) owner.InvalidateVisual();
            return true;
        }
    }
}
