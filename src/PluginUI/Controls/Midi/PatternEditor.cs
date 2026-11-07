// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>A note of a <see cref="PatternEditor"/>: pitch, position and length in beats, velocity.</summary>
public sealed class PatternNote
{
    /// <summary>Creates a note.</summary>
    /// <param name="pitch">MIDI note number (0 … 127).</param>
    /// <param name="start">Start in beats from the beginning of the pattern.</param>
    /// <param name="length">Length in beats.</param>
    /// <param name="velocity">Velocity (0 … 1).</param>
    public PatternNote(int pitch, double start, double length, float velocity = 0.8f)
    {
        Pitch = pitch;
        Start = start;
        Length = length;
        Velocity = velocity;
    }

    /// <summary>MIDI note number (0 … 127).</summary>
    public int Pitch { get; set; }

    /// <summary>Start in beats from the beginning of the pattern.</summary>
    public double Start { get; set; }

    /// <summary>Length in beats.</summary>
    public double Length { get; set; }

    /// <summary>End in beats (<see cref="Start"/> + <see cref="Length"/>).</summary>
    public double End => Start + Length;

    /// <summary>Velocity (0 … 1).</summary>
    public float Velocity { get; set; }

    /// <summary>A copy of the note.</summary>
    public PatternNote Clone() => new(Pitch, Start, Length, Velocity);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Pitch} @ {Start} + {Length} ({Velocity:0.00})");
}

/// <summary>
/// Mini piano roll for a pattern of a few bars: notes drawn on a pitch / time grid with a keyboard on the left, a bar
/// ruler, a velocity lane and a play cursor. For the patterns of an arpeggiator, a step sequencer or a drum machine.
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Click an empty cell to add a note (drag right to make it longer); drag a note to move it in
/// time and pitch, its right edge to change its length (Alt = off the grid). Double-click or right-click a note to
/// delete it (right-click on a selected note deletes the selection). Ctrl+click adds a note to the selection or
/// removes it; Shift+drag on an empty area selects with a rectangle. Drag in the velocity lane to set the velocity of
/// the notes under the mouse (only the selected ones when there is a selection). The wheel scrolls the pitches (Shift =
/// an octave); click the keyboard to hear a pitch.</para>
/// <para><b>Keyboard.</b> Ctrl+Left / Ctrl+Right select the previous or next note, Home / End the first or last,
/// Ctrl+A all of them, Esc none. Left / Right move the selection by a grid step, Up / Down by a semitone (Shift = an
/// octave); Shift+Left / Shift+Right shorten or lengthen it; Ctrl+Up / Ctrl+Down change its velocity. Insert adds a
/// note after the selection, Ctrl+D duplicates the selection after itself, Delete removes it.</para>
/// <para><b>Data.</b> A pattern is plug-in state rather than parameters: save <see cref="Notes"/> with the state of the
/// plug-in and read it again after <see cref="PatternChanged"/>, raised once per completed edit. The audio side reads
/// a copy (<see cref="GetNotes"/>), never the live list. <see cref="PreviewNoteOn"/> and <see cref="PreviewNoteOff"/>
/// let the plug-in play the notes the user touches.</para>
/// </remarks>
/// <example>
/// <code>
/// var roll = new PatternEditor { LengthBeats = 8, StepsPerBeat = 4, PlayPositionSource = () => processor.PatternBeat };
/// roll.PatternChanged += r => processor.SetPattern(r.GetNotes());
/// roll.PreviewNoteOn += (_, pitch, velocity) => processor.Audition(pitch, velocity);
/// roll.PreviewNoteOff += (_, pitch) => processor.StopAudition(pitch);
/// </code>
/// </example>
public class PatternEditor : Control
{
    private static readonly bool[] IsBlackInOctave = [false, true, false, true, false, false, true, false, true, false, true, false];
    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private const float RulerHeight = 14f, LaneGap = 4f, ResizeZone = 6f;

    private readonly List<PatternNote> _notes = new();
    private readonly HashSet<PatternNote> _selection = new();
    private double _length = 16.0;
    private int _beatsPerBar = 4, _stepsPerBeat = 4;
    private int _lowest = 48, _visible = 24;
    private double _lastLength = 0.25;

    private enum DragMode { None, Move, Resize, Band, Velocity, Keys }

    private DragMode _drag;
    private Point _anchor;
    private (PatternNote Note, int Pitch, double Start, double Length)[] _origin = [];
    private PatternNote? _dragNote;
    private Rect _band;
    private bool _changed, _creating;
    private int _auditioned = -1;
    private RefreshTicker? _ticker;
    private double _shownPlay = double.NaN;
    private int _reportedCount = -1;

    /// <summary>Creates an empty editor: four bars of 4/4 in sixteenth notes, C3 to B4 visible.</summary>
    public PatternEditor()
    {
        Focusable = true;
        Cursor = StandardCursor.Hand;
    }

    /// <summary>
    /// Notes of the pattern (any order). The editor reads them every frame; after changing the list from code, call
    /// <see cref="UIElement.InvalidateVisual"/>.
    /// </summary>
    [Browsable(false)]
    public IList<PatternNote> Notes => _notes;

    /// <summary>Length of the pattern in beats (1 … 1024; default 16, four bars of 4/4).</summary>
    [Category("Data")]
    public double LengthBeats
    {
        get => _length;
        set { _length = Math.Clamp(value, 1.0, 1024.0); InvalidateVisual(); }
    }

    /// <summary>Beats per bar (1 … 32), for the bar lines and the ruler.</summary>
    [Category("Data")]
    public int BeatsPerBar
    {
        get => _beatsPerBar;
        set { _beatsPerBar = Math.Clamp(value, 1, 32); InvalidateVisual(); }
    }

    /// <summary>Grid steps per beat (1 … 16; default 4, sixteenth notes in 4/4).</summary>
    [Category("Data")]
    public int StepsPerBeat
    {
        get => _stepsPerBeat;
        set { _stepsPerBeat = Math.Clamp(value, 1, 16); InvalidateVisual(); }
    }

    /// <summary>Moves and resizes notes on the grid (hold Alt to place them freely).</summary>
    [Category("Behavior")]
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Lowest visible pitch (bottom row).</summary>
    [Category("Data")]
    public int LowestVisibleNote
    {
        get => _lowest;
        set { _lowest = Math.Clamp(value, 0, 128 - _visible); InvalidateVisual(); }
    }

    /// <summary>Number of visible pitches (rows, 4 … 128).</summary>
    [Category("Data")]
    public int VisibleNoteCount
    {
        get => _visible;
        set
        {
            _visible = Math.Clamp(value, 4, 128);
            _lowest = Math.Clamp(_lowest, 0, 128 - _visible);
            InvalidateVisual();
        }
    }

    /// <summary>Velocity of the notes added with the mouse or Insert (0 … 1).</summary>
    [Category("Behavior")]
    public float DefaultVelocity { get; set; } = 0.8f;

    /// <summary>Shows the velocity lane below the grid.</summary>
    [Category("Appearance")]
    public bool ShowVelocityLane { get; set; } = true;

    /// <summary>Width of the keyboard on the left.</summary>
    [Category("Layout")]
    public float KeyboardWidth { get; set; } = 34f;

    /// <summary>Octave number of middle C (MIDI 60) in the note names: 4 (default) or 3.</summary>
    [Category("Data")]
    public int MiddleCOctave { get; set; } = 4;

    /// <summary>Color of the notes (null = the accent color of the style).</summary>
    [Category("Appearance")]
    public Color? NoteColor { get; set; }

    /// <summary>Play cursor in beats (negative = stopped, hidden).</summary>
    [Category("Value")]
    public double PlayPosition { get; set; } = -1.0;

    /// <summary>Function polled every frame for <see cref="PlayPosition"/> (e.g. a volatile field written by the audio thread).</summary>
    [Browsable(false)]
    public Func<double>? PlayPositionSource { get; set; }

    /// <summary>Selected notes, in time order.</summary>
    [Browsable(false)]
    public IReadOnlyList<PatternNote> SelectedNotes => Sorted(_notes.Where(_selection.Contains));

    /// <summary>Raised once after each completed edit by the user (mouse released, key pressed).</summary>
    public event Action<PatternEditor>? PatternChanged;

    /// <summary>Raised when the selection changed.</summary>
    public event Action<PatternEditor>? SelectionChanged;

    /// <summary>Raised when the user touches a note or a key of the keyboard (pitch, velocity 0 … 1): play it.</summary>
    public event Action<PatternEditor, int, float>? PreviewNoteOn;

    /// <summary>Raised when the note started by <see cref="PreviewNoteOn"/> must stop.</summary>
    public event Action<PatternEditor, int>? PreviewNoteOff;

    /// <inheritdoc />
    protected override Size DefaultSize => new(560f, 260f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new PatternEditorAutomationPeer(this);

    /// <summary>Length of a grid step in beats.</summary>
    public double GridStep => 1.0 / _stepsPerBeat;

    /// <summary>A copy of the notes in time order (then by pitch), for the audio thread or the plug-in state.</summary>
    public PatternNote[] GetNotes() => [.. Sorted(_notes).Select(n => n.Clone())];

    /// <summary>Replaces the notes (copies them) and clears the selection.</summary>
    /// <param name="notes">New notes.</param>
    public void SetNotes(IEnumerable<PatternNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        _notes.Clear();
        _notes.AddRange(notes.Select(n => n.Clone()));
        if (_selection.Count > 0)
        {
            _selection.Clear();
            SelectionChanged?.Invoke(this);
        }
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary><c>true</c> when <paramref name="note"/> is selected.</summary>
    public bool IsSelected(PatternNote note) => _selection.Contains(note);

    /// <summary>Selects the given notes only.</summary>
    public void Select(IEnumerable<PatternNote> notes)
    {
        _selection.Clear();
        foreach (var n in notes) if (_notes.Contains(n)) _selection.Add(n);
        OnSelectionChanged();
    }

    /// <summary>Note name with octave ("C4", "F#2") using <see cref="MiddleCOctave"/>.</summary>
    public string NoteName(int pitch) => SharpNames[((pitch % 12) + 12) % 12] + (pitch / 12 - 5 + MiddleCOctave).ToString(CultureInfo.InvariantCulture);

    private static bool IsBlack(int pitch) => IsBlackInOctave[((pitch % 12) + 12) % 12];

    private static List<PatternNote> Sorted(IEnumerable<PatternNote> notes)
    {
        var list = notes.ToList();
        list.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Pitch.CompareTo(b.Pitch));
        return list;
    }

    // ---------------------------------------------------------------- geometry

    private void Areas(Rect widget, out Rect keys, out Rect grid, out Rect lane, out Rect ruler)
    {
        var r = widget.Deflate(1f);
        float laneHeight = ShowVelocityLane ? MathF.Min(48f, r.Height * 0.22f) : 0f;
        float bottom = r.Bottom - (laneHeight > 0f ? laneHeight + LaneGap : 0f);
        float kw = MathF.Min(KeyboardWidth, r.Width * 0.3f);
        ruler = new Rect(r.X + kw, r.Y, MathF.Max(0f, r.Width - kw), RulerHeight);
        keys = Rect.FromEdges(r.X, r.Y + RulerHeight, r.X + kw, bottom);
        grid = Rect.FromEdges(r.X + kw, r.Y + RulerHeight, r.Right, bottom);
        lane = Rect.FromEdges(grid.X, r.Bottom - laneHeight, grid.Right, r.Bottom);
    }

    private Rect Grid
    {
        get
        {
            Areas(Bounds, out _, out var grid, out _, out _);
            return grid;
        }
    }

    private float RowHeight(Rect grid) => grid.Height / _visible;

    private float BeatToX(double beat, Rect grid) => grid.X + (float)(beat / _length) * grid.Width;

    private double XToBeat(float x, Rect grid) => (x - grid.X) / Math.Max(1f, grid.Width) * _length;

    private float PitchTop(int pitch, Rect grid) => grid.Y + (_lowest + _visible - 1 - pitch) * RowHeight(grid);

    private int YToPitch(float y, Rect grid) => Math.Clamp(_lowest + _visible - 1 - (int)MathF.Floor((y - grid.Y) / MathF.Max(1f, RowHeight(grid))), 0, 127);

    private Rect NoteRect(PatternNote n, Rect grid)
    {
        float top = PitchTop(n.Pitch, grid);
        return Rect.FromEdges(BeatToX(n.Start, grid), top, MathF.Max(BeatToX(n.End, grid), BeatToX(n.Start, grid) + 2f), top + RowHeight(grid));
    }

    private bool InView(PatternNote n) => n.Pitch >= _lowest && n.Pitch < _lowest + _visible;

    private PatternNote? NoteAt(Point p, Rect grid)
    {
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            var n = _notes[i];
            if (InView(n) && NoteRect(n, grid).Contains(p)) return n;
        }
        return null;
    }

    private double Snap(double beat, bool free) => SnapToGrid && !free ? Math.Round(beat / GridStep) * GridStep : beat;

    private double SnapDown(double beat, bool free) => SnapToGrid && !free ? Math.Floor(beat / GridStep + 1e-9) * GridStep : beat;

    private double MinLength => SnapToGrid ? GridStep : GridStep / 8.0;

    // ---------------------------------------------------------------- live updates

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new RefreshTicker(this, null, _ =>
        {
            if (PlayPositionSource is { } source) PlayPosition = source();
            double shown = PlayPosition < 0.0 ? -1.0 : PlayPosition;
            if (shown != _shownPlay)
            {
                _shownPlay = shown;
                InvalidateVisual();
            }
            if (_notes.Count != _reportedCount)
            {
                _reportedCount = _notes.Count;
                _selection.RemoveWhere(n => !_notes.Contains(n));
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
        Areas(widget, out var keys, out var grid, out var lane, out var ruler);
        float rowH = RowHeight(grid);
        var small = Theme.Typography.Small with { Size = MathF.Min(9.5f, MathF.Max(7f, rowH - 2f)), Ellipsis = false, VerticalAlign = VerticalAlignment.Center };
        var accent = NoteColor ?? style.Accent;

        // Rows: black-key rows darker, a line under each B (octave boundary).
        for (int i = 0; i < _visible; i++)
        {
            int pitch = _lowest + i;
            float top = PitchTop(pitch, grid);
            var row = new Rect(grid.X, top, grid.Width, rowH);
            dc.FillRect(row, IsBlack(pitch) ? p.Background.WithAlpha(0.55f) : p.Surface.WithAlpha(0.35f));
            if (pitch % 12 == 0) dc.DrawLine(new Point(grid.X, row.Bottom), new Point(grid.Right, row.Bottom), p.Border, 1f);

            var key = new Rect(keys.X, top, keys.Width, rowH);
            var keyColor = IsBlack(pitch) ? Color.FromRgb(0x2A2C31) : Color.FromRgb(0xDADBDF);
            if (pitch == _auditioned) keyColor = Color.Lerp(keyColor, accent, 0.7f);
            dc.FillRect(Rect.FromEdges(key.X, key.Y + 0.5f, key.Right - 1f, key.Bottom - 0.5f), keyColor);
            if (pitch % 12 == 0 && rowH >= 7f)
            {
                dc.DrawText(NoteName(pitch), Rect.FromEdges(key.X + 3f, key.Y, key.Right - 2f, key.Bottom), small, Color.FromRgb(0x55585F));
            }
        }

        // Columns: steps, beats, bars; ruler with the bar numbers.
        float stepW = (float)(GridStep / _length) * grid.Width;
        int steps = (int)Math.Ceiling(_length / GridStep);
        var rulerText = Theme.Typography.Small with { Size = 9.5f, Ellipsis = false, VerticalAlign = VerticalAlignment.Center };
        for (int s = 0; s <= steps; s++)
        {
            double beat = s * GridStep;
            if (beat > _length + 1e-9) break;
            float x = BeatToX(beat, grid);
            bool isBeat = s % _stepsPerBeat == 0;
            bool isBar = isBeat && (s / _stepsPerBeat) % _beatsPerBar == 0;
            if (!isBeat && stepW < 4f) continue;
            var color = isBar ? p.TextDim.WithAlpha(0.55f) : isBeat ? p.Border.Lighten(0.08f) : p.Border.WithAlpha(0.45f);
            dc.DrawLine(new Point(x, grid.Y), new Point(x, grid.Bottom), color, isBar ? 1.2f : 1f);
            if (isBar && beat < _length - 1e-9)
            {
                int bar = s / _stepsPerBeat / _beatsPerBar + 1;
                dc.DrawText(bar.ToString(CultureInfo.InvariantCulture), new Rect(x + 3f, ruler.Y, 40f, ruler.Height), rulerText, p.TextDim);
                dc.DrawLine(new Point(x, ruler.Y + 3f), new Point(x, ruler.Bottom), p.TextDim.WithAlpha(0.55f), 1f);
            }
        }

        // Notes, with markers on the edges for the notes above or below the visible rows.
        dc.PushClip(grid);
        foreach (var n in _notes)
        {
            bool selected = _selection.Contains(n);
            if (!InView(n))
            {
                float y = n.Pitch >= _lowest + _visible ? grid.Y : grid.Bottom - 3f;
                dc.FillRect(Rect.FromEdges(BeatToX(n.Start, grid), y, BeatToX(n.End, grid), y + 3f), (selected ? p.Text : accent).WithAlpha(0.6f));
                continue;
            }
            var r = NoteRect(n, grid);
            var body = Rect.FromEdges(r.X + 0.5f, r.Y + 1f, r.Right - 0.5f, r.Bottom - 1f);
            float radius = MathF.Min(3f, body.Height * 0.3f);
            var fill = accent.WithAlpha(0.45f + 0.55f * Math.Clamp(n.Velocity, 0f, 1f));
            dc.FillRoundedRect(body, radius, selected ? Color.Lerp(fill, Color.White, 0.25f) : fill);
            dc.DrawRoundedRect(body, radius, selected ? p.Text : accent.Darken(0.35f), selected ? 1.5f : 1f);
            if (selected && body.Width >= 26f && body.Height >= 8f)
            {
                dc.DrawText(NoteName(n.Pitch), Rect.FromEdges(body.X + 3f, body.Y, body.Right - 2f, body.Bottom), small, accent.Contrasting);
            }
        }
        if (_drag == DragMode.Band)
        {
            dc.FillRect(_band, p.Selection.WithAlpha(0.25f));
            dc.DrawRect(_band, p.Selection, 1f);
        }
        dc.PopClip();

        // Velocity lane.
        if (ShowVelocityLane && lane.Height > 4f)
        {
            dc.FillRect(lane, p.Background.WithAlpha(0.6f));
            dc.DrawLine(new Point(lane.X, lane.Y + lane.Height * 0.5f), new Point(lane.Right, lane.Y + lane.Height * 0.5f), p.Border.WithAlpha(0.6f), 1f);
            foreach (var n in _notes)
            {
                bool selected = _selection.Contains(n);
                var bar = VelocityBar(n, lane);
                float h = Math.Clamp(n.Velocity, 0f, 1f) * (lane.Height - 2f);
                var stem = Rect.FromEdges(bar.X, lane.Bottom - h, bar.Right, lane.Bottom);
                dc.FillRect(stem, (selected ? p.Text : accent).WithAlpha(selected ? 0.9f : 0.7f));
                dc.FillRect(new Rect(stem.X, stem.Y, stem.Width, 2f), selected ? p.Text : accent.Lighten(0.3f));
            }
        }

        // Play cursor over everything.
        if (PlayPosition >= 0.0 && PlayPosition <= _length)
        {
            float x = BeatToX(PlayPosition, grid);
            dc.DrawLine(new Point(x, ruler.Y), new Point(x, ShowVelocityLane ? lane.Bottom : grid.Bottom), p.AccentAlt, 1.5f);
        }
    }

    private Rect VelocityBar(PatternNote n, Rect lane)
    {
        float x = BeatToX(n.Start, lane);
        float w = Math.Clamp(BeatToX(n.End, lane) - x - 1f, 3f, 8f);
        return new Rect(x, lane.Y, w, lane.Height);
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        Areas(Bounds, out var keys, out var grid, out var lane, out _);
        var pos = e.Position;
        if (e.Button == MouseButton.Right)
        {
            if (grid.Contains(pos) && NoteAt(pos, grid) is { } hit)
            {
                Delete(_selection.Contains(hit) ? [.. _selection] : [hit]);
                e.Handled = true;
            }
            return;
        }
        if (e.Button != MouseButton.Left) return;
        e.Handled = true;
        _anchor = pos;
        _changed = false;

        if (keys.Contains(pos))
        {
            _drag = DragMode.Keys;
            Audition(YToPitch(pos.Y, grid), DefaultVelocity);
            return;
        }
        if (ShowVelocityLane && lane.Contains(pos))
        {
            _drag = DragMode.Velocity;
            PaintVelocity(pos, lane);
            return;
        }
        if (!grid.Contains(pos)) return;

        var note = NoteAt(pos, grid);
        if (note is null)
        {
            if (e.Shift)
            {
                _drag = DragMode.Band;
                _band = new Rect(pos.X, pos.Y, 0f, 0f);
                if (!e.Control) ClearSelection();
                return;
            }
            // A new note at the cell under the mouse, longer while dragging to the right.
            ClearSelection();
            double start = Math.Clamp(SnapDown(XToBeat(pos.X, grid), e.Alt), 0.0, _length - MinLength);
            double length = Math.Min(_lastLength, _length - start);
            note = new PatternNote(YToPitch(pos.Y, grid), start, Math.Max(MinLength, length), DefaultVelocity);
            _notes.Add(note);
            _selection.Add(note);
            OnSelectionChanged();
            _changed = true;
            BeginDrag(DragMode.Resize, note);
            _creating = true;
            return;
        }
        if (e.ClickCount == 2)
        {
            Delete([note]);
            return;
        }
        if (e.Control)
        {
            if (!_selection.Remove(note)) _selection.Add(note);
            OnSelectionChanged();
            return;
        }
        if (!_selection.Contains(note))
        {
            _selection.Clear();
            _selection.Add(note);
            OnSelectionChanged();
        }
        var r = NoteRect(note, grid);
        bool edge = pos.X >= r.Right - MathF.Min(ResizeZone, r.Width / 3f);
        BeginDrag(edge ? DragMode.Resize : DragMode.Move, note);
    }

    private void BeginDrag(DragMode mode, PatternNote note)
    {
        _drag = mode;
        _dragNote = note;
        _creating = false;
        _origin = [.. _notes.Where(_selection.Contains).Select(n => (n, n.Pitch, n.Start, n.Length))];
        Audition(note.Pitch, note.Velocity);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Areas(Bounds, out _, out var grid, out var lane, out _);
        var pos = e.Position;
        switch (_drag)
        {
            case DragMode.Keys:
            {
                int pitch = YToPitch(pos.Y, grid);
                if (pitch != _auditioned) Audition(pitch, DefaultVelocity);
                return;
            }
            case DragMode.Velocity:
                PaintVelocity(pos, lane);
                return;
            case DragMode.Band:
            {
                _band = Rect.FromEdges(MathF.Min(_anchor.X, pos.X), MathF.Min(_anchor.Y, pos.Y), MathF.Max(_anchor.X, pos.X), MathF.Max(_anchor.Y, pos.Y));
                bool changed = false;
                foreach (var n in _notes)
                {
                    bool inside = InView(n) && NoteRect(n, grid).Intersects(_band);
                    if (inside && _selection.Add(n)) changed = true;
                }
                if (changed) OnSelectionChanged();
                InvalidateVisual();
                return;
            }
            case DragMode.Move:
                MoveSelection(pos, grid, e.Alt);
                return;
            case DragMode.Resize:
                ResizeSelection(pos, grid, e.Alt);
                return;
        }

        if (grid.Contains(pos) && NoteAt(pos, grid) is { } hover)
        {
            var r = NoteRect(hover, grid);
            Cursor = pos.X >= r.Right - MathF.Min(ResizeZone, r.Width / 3f) ? StandardCursor.SizeWE : StandardCursor.SizeAll;
        }
        else
        {
            Cursor = lane.Contains(pos) && ShowVelocityLane ? StandardCursor.SizeNS : StandardCursor.Hand;
        }
    }

    private void MoveSelection(Point pos, Rect grid, bool free)
    {
        double delta = (pos.X - _anchor.X) / Math.Max(1f, grid.Width) * _length;
        delta = SnapToGrid && !free ? Math.Round(delta / GridStep) * GridStep : delta;
        int semitones = (int)MathF.Round((_anchor.Y - pos.Y) / MathF.Max(1f, RowHeight(grid)));
        // Keep the whole selection inside the pattern and the MIDI range.
        foreach (var o in _origin)
        {
            delta = Math.Clamp(delta, -o.Start, _length - o.Length - o.Start);
            semitones = Math.Clamp(semitones, -o.Pitch, 127 - o.Pitch);
        }
        bool changed = false;
        foreach (var o in _origin)
        {
            double start = o.Start + delta;
            int pitch = o.Pitch + semitones;
            if (o.Note.Start != start || o.Note.Pitch != pitch) changed = true;
            o.Note.Start = start;
            o.Note.Pitch = pitch;
        }
        if (!changed) return;
        _changed = true;
        if (_dragNote is { } lead && lead.Pitch != _auditioned) Audition(lead.Pitch, lead.Velocity);
        InvalidateVisual();
    }

    private void ResizeSelection(Point pos, Rect grid, bool free)
    {
        double delta = (pos.X - _anchor.X) / Math.Max(1f, grid.Width) * _length;
        bool changed = false;
        foreach (var o in _origin)
        {
            // A note being drawn ends under the mouse, never shorter than it started.
            double end = _creating ? Math.Max(o.Start + o.Length, XToBeat(pos.X, grid)) : o.Start + o.Length + delta;
            end = SnapToGrid && !free ? Math.Round(end / GridStep) * GridStep : end;
            double length = Math.Clamp(end - o.Start, MinLength, _length - o.Start);
            if (o.Note.Length != length) changed = true;
            o.Note.Length = length;
        }
        if (!changed) return;
        _changed = true;
        if (_dragNote is { } lead) _lastLength = lead.Length;
        InvalidateVisual();
    }

    private void PaintVelocity(Point pos, Rect lane)
    {
        float velocity = Math.Clamp((lane.Bottom - pos.Y) / MathF.Max(1f, lane.Height - 2f), 0.01f, 1f);
        bool onlySelected = _selection.Count > 0;
        foreach (var n in _notes)
        {
            if (onlySelected && !_selection.Contains(n)) continue;
            var bar = VelocityBar(n, lane);
            if (pos.X < bar.X - 1f || pos.X > bar.Right + 1f) continue;
            if (n.Velocity == velocity) continue;
            n.Velocity = velocity;
            _changed = true;
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left) EndDrag();
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        EndDrag();
    }

    private void EndDrag()
    {
        StopAudition();
        if (_drag == DragMode.None) return;
        _drag = DragMode.None;
        _dragNote = null;
        _origin = [];
        if (_changed)
        {
            _changed = false;
            Changed();
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled || e.WheelDelta == 0f) return;
        int step = (e.Shift ? 12 : 2) * Math.Sign(e.WheelDelta);
        LowestVisibleNote = _lowest + step;
        e.Handled = true;
    }

    private void Audition(int pitch, float velocity)
    {
        StopAudition();
        if ((uint)pitch > 127) return;
        _auditioned = pitch;
        PreviewNoteOn?.Invoke(this, pitch, Math.Clamp(velocity, 0.01f, 1f));
        InvalidateVisual();
    }

    private void StopAudition()
    {
        if (_auditioned < 0) return;
        int pitch = _auditioned;
        _auditioned = -1;
        PreviewNoteOff?.Invoke(this, pitch);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled || _drag != DragMode.None) return;
        var selected = SelectedNotes;
        var sorted = Sorted(_notes);
        bool any = selected.Count > 0;
        switch (e.Key)
        {
            case Key.A when e.Control:
                Select(_notes);
                break;
            case Key.Escape when any:
                ClearSelection();
                break;
            case Key.Left when e.Control:
            case Key.Right when e.Control:
            {
                if (sorted.Count == 0) return;
                int index = any ? sorted.IndexOf(e.Key == Key.Right ? selected[^1] : selected[0]) : -1;
                index = e.Key == Key.Right ? Math.Min(sorted.Count - 1, index + 1) : (index < 0 ? 0 : Math.Max(0, index - 1));
                SelectOne(sorted[index]);
                break;
            }
            case Key.Home when sorted.Count > 0:
                SelectOne(sorted[0]);
                break;
            case Key.End when sorted.Count > 0:
                SelectOne(sorted[^1]);
                break;
            case Key.Left or Key.Right when any && e.Shift:
            {
                double step = e.Key == Key.Right ? GridStep : -GridStep;
                foreach (var n in selected) n.Length = Math.Clamp(n.Length + step, MinLength, _length - n.Start);
                _lastLength = selected[^1].Length;
                Changed();
                break;
            }
            case Key.Left or Key.Right when any:
            {
                double step = e.Key == Key.Right ? GridStep : -GridStep;
                foreach (var n in selected) step = Math.Clamp(step, -n.Start, _length - n.End);
                if (step == 0.0) break;
                foreach (var n in selected) n.Start += step;
                Changed();
                break;
            }
            case Key.Up or Key.Down when any && e.Control:
            {
                float step = e.Key == Key.Up ? 0.05f : -0.05f;
                foreach (var n in selected) n.Velocity = Math.Clamp(MathF.Round((n.Velocity + step) * 100f) / 100f, 0.01f, 1f);
                Changed();
                break;
            }
            case Key.Up or Key.Down when any:
            {
                int step = (e.Key == Key.Up ? 1 : -1) * (e.Shift ? 12 : 1);
                foreach (var n in selected) step = Math.Clamp(step, -n.Pitch, 127 - n.Pitch);
                if (step == 0) break;
                foreach (var n in selected) n.Pitch += step;
                Changed();
                break;
            }
            case Key.Insert:
                InsertNote(any ? selected[^1] : null);
                break;
            case Key.D when e.Control && any:
                Duplicate(selected);
                break;
            case Key.Delete or Key.Back when any:
                Delete(selected);
                break;
            default:
                return;
        }
        if (SelectedNotes is { Count: > 0 } now) ScrollIntoView(now[^1]);
        e.Handled = true;
        InvalidateVisual();
    }

    private void InsertNote(PatternNote? after)
    {
        double length = after?.Length ?? _lastLength;
        double start = after is null ? 0.0 : after.End;
        if (start > _length - MinLength) start = 0.0;
        length = Math.Min(length, _length - start);
        int pitch = after?.Pitch ?? Math.Clamp(60, _lowest, _lowest + _visible - 1);
        var note = new PatternNote(pitch, start, length, after?.Velocity ?? DefaultVelocity);
        _notes.Add(note);
        SelectOne(note);
        Changed();
    }

    private void Duplicate(IReadOnlyList<PatternNote> selected)
    {
        double from = selected.Min(n => n.Start), to = selected.Max(n => n.End);
        double span = SnapToGrid ? Math.Ceiling((to - from) / GridStep - 1e-9) * GridStep : to - from;
        if (to - from + span > _length + 1e-9 && from + span >= _length) return;
        var copies = new List<PatternNote>();
        foreach (var n in selected)
        {
            if (n.Start + span >= _length) continue;
            var copy = n.Clone();
            copy.Start += span;
            copy.Length = Math.Min(copy.Length, _length - copy.Start);
            copies.Add(copy);
        }
        if (copies.Count == 0) return;
        _notes.AddRange(copies);
        Select(copies);
        Changed();
    }

    private void Delete(IReadOnlyCollection<PatternNote> notes)
    {
        bool any = false;
        foreach (var n in notes)
        {
            any |= _notes.Remove(n);
            _selection.Remove(n);
        }
        if (!any) return;
        OnSelectionChanged();
        Changed();
    }

    private void SelectOne(PatternNote note)
    {
        _selection.Clear();
        _selection.Add(note);
        OnSelectionChanged();
    }

    private void ClearSelection()
    {
        if (_selection.Count == 0) return;
        _selection.Clear();
        OnSelectionChanged();
    }

    private void ScrollIntoView(PatternNote note)
    {
        if (note.Pitch < _lowest) LowestVisibleNote = note.Pitch;
        else if (note.Pitch >= _lowest + _visible) LowestVisibleNote = note.Pitch - _visible + 1;
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private void Changed()
    {
        PatternChanged?.Invoke(this);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    // ---------------------------------------------------------------- screen readers

    /// <summary>What a screen reader says: the selected note, or how many notes there are and are selected.</summary>
    internal string DescribeSelection()
    {
        var t = Strings;
        var selected = SelectedNotes;
        if (selected.Count == 1)
        {
            var n = selected[0];
            int bar = (int)Math.Floor(n.Start / _beatsPerBar + 1e-9);
            double beat = n.Start - bar * _beatsPerBar + 1.0;
            return string.Format(CultureInfo.InvariantCulture, t.PatternNoteFormat, NoteName(n.Pitch), bar + 1,
                InvariantNumber.Format(beat, "0.##"), InvariantNumber.Format(n.Length, "0.###"), (int)MathF.Round(n.Velocity * 100f));
        }
        if (selected.Count > 1) return string.Format(CultureInfo.InvariantCulture, t.NotesSelectedFormat, selected.Count, _notes.Count);
        return string.Format(CultureInfo.InvariantCulture, t.NoteCountFormat, _notes.Count);
    }
}

/// <summary>
/// Peer of a <see cref="PatternEditor"/>: a group whose value describes the selected note (pitch, position, length,
/// velocity), or the number of notes.
/// </summary>
/// <param name="owner">The editor.</param>
public class PatternEditorAutomationPeer(PatternEditor owner) : ElementAutomationPeer<PatternEditor>(owner), IValuePattern
{
    private string? _reported;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationPatternEditor;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => true;

    /// <inheritdoc />
    public string Value => Owner.DescribeSelection();

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public void SetValue(string value) => throw new InvalidOperationException("The pattern editor is edited with its keys.");

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
