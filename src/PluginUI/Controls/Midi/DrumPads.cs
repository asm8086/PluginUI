// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Globalization;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>A pad of <see cref="DrumPads"/>: the MIDI note it plays, a name and a color.</summary>
public sealed class DrumPad
{
    /// <summary>Creates a pad.</summary>
    /// <param name="note">MIDI note number played by the pad.</param>
    /// <param name="name">Name shown on the pad (null = "Pad n" in the language of the host).</param>
    public DrumPad(int note, string? name = null)
    {
        Note = note;
        Name = name;
    }

    /// <summary>MIDI note number played by the pad (0 … 127).</summary>
    public int Note { get; set; }

    /// <summary>Name shown on the pad (null = "Pad n" in the language of the host).</summary>
    public string? Name { get; set; }

    /// <summary>Color of the pad (null = the accent color of the style).</summary>
    public Color? Color { get; set; }
}

/// <summary>
/// Grid of drum pads (4 × 4 by default, the first pad at the bottom left as on hardware drum machines): play them with
/// the mouse (velocity from the height of the click), with the computer keyboard, and light them when the notes are
/// played elsewhere (sequencer, host MIDI).
/// </summary>
/// <remarks>
/// <para><b>Mouse.</b> Press a pad to play it: the higher the click on the pad, the louder (<see cref="VelocitySensitive"/>);
/// the pad also becomes the selected pad, the one a sample editor next to it would show.</para>
/// <para><b>Keyboard.</b> The arrows move the selection, Space or Enter plays the selected pad. With
/// <see cref="QwertyEnabled"/>, the keys 1 2 3 4 / Q W E R / A S D F / Z X C V play the pads as they are laid out on
/// the screen, from the top row (US layout virtual keys).</para>
/// <para><b>Display.</b> <see cref="ActivitySource"/> lights the pads whose notes sound, and <see cref="Flash"/> (any
/// thread) makes a pad glow briefly, for one-shot triggers from the audio thread.</para>
/// </remarks>
/// <example>
/// <code>
/// var pads = new DrumPads { ActivitySource = processor.Voices };
/// pads.Pads[0].Name = "Kick";
/// pads.PadPressed += (p, index, velocity) => processor.Trigger(p.Pads[index].Note, velocity);
/// pads.PadReleased += (p, index) => processor.Release(p.Pads[index].Note);
/// </code>
/// </example>
public class DrumPads : Control
{
    // Computer keyboard rows, top to bottom, as the pads appear on the screen.
    private static readonly Key[][] QwertyRows =
    [
        [Key.D1, Key.D2, Key.D3, Key.D4],
        [Key.Q, Key.W, Key.E, Key.R],
        [Key.A, Key.S, Key.D, Key.F],
        [Key.Z, Key.X, Key.C, Key.V],
    ];

    private int _columns = 4, _rows = 4;
    private int _selected;
    private int _mousePad = -1;
    private readonly HashSet<int> _held = new();
    private readonly Dictionary<Key, int> _keysDown = new();
    private readonly int[] _pendingFlash = new int[128]; // written from any thread: velocity × 1000 + 1
    private float[] _glow = new float[16];
    private PadsTicker? _ticker;

    /// <summary>Creates 16 pads on the notes 36 to 51 (General MIDI drums, from the kick).</summary>
    public DrumPads()
    {
        Focusable = true;
        Cursor = StandardCursor.Hand;
        EnsurePads();
    }

    /// <summary>Pads, from the first one (bottom left when <see cref="FirstPadAtBottom"/>).</summary>
    [Browsable(false)]
    public IList<DrumPad> Pads { get; } = new List<DrumPad>();

    /// <summary>Number of columns (1 … 8). Pads are added on the following notes, or removed, to fill the grid.</summary>
    [Category("Layout")]
    public int Columns
    {
        get => _columns;
        set { _columns = Math.Clamp(value, 1, 8); EnsurePads(); }
    }

    /// <summary>Number of rows (1 … 8).</summary>
    [Category("Layout")]
    public int Rows
    {
        get => _rows;
        set { _rows = Math.Clamp(value, 1, 8); EnsurePads(); }
    }

    /// <summary>Puts the first pad at the bottom left, as on hardware drum machines (<c>false</c> = top left).</summary>
    [Category("Layout")]
    public bool FirstPadAtBottom { get; set; } = true;

    /// <summary>Takes the velocity from the height of the click on the pad (otherwise <see cref="DefaultVelocity"/>).</summary>
    [Category("Behavior")]
    public bool VelocitySensitive { get; set; } = true;

    /// <summary>Velocity of the pads played from the keyboard, or with the mouse when not velocity sensitive (0 … 1).</summary>
    [Category("Behavior")]
    public float DefaultVelocity { get; set; } = 0.8f;

    /// <summary>Plays the pads from the computer keyboard while focused.</summary>
    [Category("Behavior")]
    public bool QwertyEnabled { get; set; } = true;

    /// <summary>Shows the note name under the pad name.</summary>
    [Category("Appearance")]
    public bool ShowNoteNames { get; set; } = true;

    /// <summary>Octave number of middle C (MIDI 60) in the note names: 4 (default) or 3.</summary>
    [Category("Data")]
    public int MiddleCOctave { get; set; } = 4;

    /// <summary>Notes sounding elsewhere (the processor's voices, fed from the audio thread), lighting their pads.</summary>
    [Browsable(false)]
    public NoteActivity? ActivitySource { get; set; }

    /// <summary>Selected pad (the last one played or chosen with the arrows).</summary>
    [Category("Value")]
    public int SelectedPad
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, Pads.Count - 1);
            if (value == _selected) return;
            _selected = value;
            SelectionChanged?.Invoke(this, value);
            InvalidateVisual();
            NotifyAutomationStateChanged();
        }
    }

    /// <summary>Raised when a pad is pressed (pad index, velocity 0 … 1).</summary>
    public event Action<DrumPads, int, float>? PadPressed;

    /// <summary>Raised when a pad is released (pad index).</summary>
    public event Action<DrumPads, int>? PadReleased;

    /// <summary>Raised when <see cref="SelectedPad"/> changed.</summary>
    public event Action<DrumPads, int>? SelectionChanged;

    /// <inheritdoc />
    protected override Size DefaultSize => new(_columns * 64f, _rows * 64f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new DrumPadsAutomationPeer(this);

    /// <summary><c>true</c> while pad <paramref name="pad"/> is held on this control (mouse or keyboard).</summary>
    public bool IsPadHeld(int pad) => _held.Contains(pad);

    /// <summary>
    /// Makes the pads playing <paramref name="note"/> glow briefly (velocity 0 … 1). Thread-safe: call it from the audio
    /// thread when a one-shot is triggered; the display updates on the next frame.
    /// </summary>
    public void Flash(int note, float velocity = 1f)
    {
        if ((uint)note > 127) return;
        Interlocked.Exchange(ref _pendingFlash[note], (int)(Math.Clamp(velocity, 0f, 1f) * 1000f) + 1);
    }

    /// <summary>Name of a pad: its <see cref="DrumPad.Name"/> or "Pad n".</summary>
    public string PadName(int pad) => Pads[pad].Name is { Length: > 0 } name ? name : string.Format(CultureInfo.InvariantCulture, Strings.PadFormat, pad + 1);

    /// <summary>Note name with octave ("C2", "F#2") using <see cref="MiddleCOctave"/>.</summary>
    public string NoteName(int note)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return names[((note % 12) + 12) % 12] + (note / 12 - 5 + MiddleCOctave).ToString(CultureInfo.InvariantCulture);
    }

    private void EnsurePads()
    {
        int count = _columns * _rows;
        while (Pads.Count > count) Pads.RemoveAt(Pads.Count - 1);
        while (Pads.Count < count)
        {
            int note = Pads.Count == 0 ? 36 : Math.Min(127, Pads[^1].Note + 1);
            Pads.Add(new DrumPad(note));
        }
        if (_glow.Length < count) Array.Resize(ref _glow, count);
        _selected = Math.Clamp(_selected, 0, count - 1);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- geometry

    private int Count => Math.Min(Pads.Count, _columns * _rows);

    /// <summary>Column and row on the screen (row 0 at the top) of a pad.</summary>
    private (int Column, int Row) CellOf(int pad)
    {
        int column = pad % _columns, line = pad / _columns;
        return (column, FirstPadAtBottom ? _rows - 1 - line : line);
    }

    private int PadAtCell(int column, int row)
    {
        int line = FirstPadAtBottom ? _rows - 1 - row : row;
        int pad = line * _columns + column;
        return column >= 0 && column < _columns && row >= 0 && row < _rows && pad < Count ? pad : -1;
    }

    internal Rect PadRect(int pad) => PadRect(pad, Bounds);

    private Rect PadRect(int pad, Rect widget)
    {
        var area = widget.Deflate(2f);
        float gap = MathF.Min(6f, MathF.Min(area.Width / _columns, area.Height / _rows) * 0.1f);
        float w = (area.Width - gap * (_columns - 1)) / _columns;
        float h = (area.Height - gap * (_rows - 1)) / _rows;
        var (column, row) = CellOf(pad);
        return new Rect(area.X + column * (w + gap), area.Y + row * (h + gap), w, h);
    }

    /// <summary>Pad under a point, or −1.</summary>
    public int PadAt(Point point)
    {
        for (int i = 0; i < Count; i++) if (PadRect(i).Contains(point)) return i;
        return -1;
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        var nameStyle = Theme.Typography.Small with { Size = 10.5f, Align = HorizontalAlignment.Center, VerticalAlign = VerticalAlignment.Center, Ellipsis = true };
        var noteStyle = nameStyle with { Size = 9f };
        var numberStyle = Theme.Typography.Small with { Size = 8.5f, Ellipsis = false };
        for (int i = 0; i < Count; i++)
        {
            var r = PadRect(i, widget);
            var color = Pads[i].Color ?? style.Accent;
            bool held = _held.Contains(i);
            bool sounding = ActivitySource?.IsOn(Pads[i].Note) == true;
            float glow = MathF.Max(i < _glow.Length ? _glow[i] : 0f, held ? 1f : sounding ? 0.6f : 0f);
            float radius = MathF.Min(6f, MathF.Min(r.Width, r.Height) * 0.12f);

            var face = Color.Lerp(p.SurfaceRaised, color, 0.75f * glow);
            dc.FillRoundedRect(r.Offset(0f, 1.5f), radius, Color.Black.WithAlpha(0.35f));
            dc.FillRoundedRect(r, radius, Paint.Vertical(face.Lighten(0.06f), face.Darken(0.08f)));
            dc.FillRect(Rect.FromEdges(r.X + radius, r.Bottom - 3f, r.Right - radius, r.Bottom - 1f), color.WithAlpha(0.8f));
            bool selected = i == _selected;
            dc.DrawRoundedRect(r, radius, selected ? color : p.Border, selected ? 1.6f : 1f);

            var text = glow > 0.5f ? face.Contrasting : p.Text;
            bool showNote = ShowNoteNames && r.Height >= 40f;
            float mid = r.Y + r.Height * (showNote ? 0.44f : 0.5f);
            dc.DrawText(PadName(i), Rect.FromEdges(r.X + 4f, mid - 8f, r.Right - 4f, mid + 8f), nameStyle, text);
            if (showNote) dc.DrawText(NoteName(Pads[i].Note), Rect.FromEdges(r.X + 4f, mid + 7f, r.Right - 4f, mid + 20f), noteStyle, text.WithAlpha(0.65f));
            if (r.Width >= 36f)
                dc.DrawText((i + 1).ToString(CultureInfo.InvariantCulture), new Rect(r.X + 4f, r.Y + 2f, 20f, 11f), numberStyle, text.WithAlpha(0.5f));
        }
    }

    // ---------------------------------------------------------------- playing

    private void Press(int pad, float velocity)
    {
        if (pad < 0 || pad >= Count || !_held.Add(pad)) return;
        SelectedPad = pad;
        PadPressed?.Invoke(this, pad, Math.Clamp(velocity, 0.01f, 1f));
        InvalidateVisual();
    }

    private void Release(int pad)
    {
        if (!_held.Remove(pad)) return;
        if (pad < _glow.Length) _glow[pad] = 0.6f; // a short tail after the release
        PadReleased?.Invoke(this, pad);
        InvalidateVisual();
    }

    /// <summary>Releases every pad held on this control.</summary>
    public void ReleaseAll()
    {
        foreach (int pad in _held.ToArray()) Release(pad);
        _keysDown.Clear();
        _mousePad = -1;
    }

    /// <summary>Plays a pad and releases it (screen readers, tests).</summary>
    internal void Tap(int pad)
    {
        Press(pad, DefaultVelocity);
        Release(pad);
    }

    // ---------------------------------------------------------------- mouse

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        int pad = PadAt(e.Position);
        if (pad < 0) return;
        e.Handled = true;
        IsPressed = true;
        _mousePad = pad;
        float velocity = DefaultVelocity;
        if (VelocitySensitive)
        {
            var r = PadRect(pad);
            velocity = 0.25f + 0.75f * Math.Clamp((r.Bottom - e.Position.Y) / MathF.Max(1f, r.Height), 0f, 1f);
        }
        Press(pad, velocity);
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_mousePad >= 0) Release(_mousePad);
        _mousePad = -1;
        IsPressed = false;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        if (_mousePad >= 0) Release(_mousePad);
        _mousePad = -1;
        IsPressed = false;
    }

    // ---------------------------------------------------------------- keyboard

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
        ReleaseKeys();
    }

    private void ReleaseKeys()
    {
        foreach (int pad in _keysDown.Values) Release(pad);
        _keysDown.Clear();
    }

    // The window lost the keyboard (click in the DAW): the key-ups will never come.
    private void OnHostKeyboardFocusChanged(UIHost host, bool focused)
    {
        if (!focused) ReleaseKeys();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Control || (e.Modifiers & ModifierKeys.Alt) != 0) return;
        var (column, row) = CellOf(_selected);
        switch (e.Key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down:
            {
                int c = column + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0);
                int r = row + (e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0);
                int pad = PadAtCell(Math.Clamp(c, 0, _columns - 1), Math.Clamp(r, 0, _rows - 1));
                if (pad >= 0) SelectedPad = pad;
                e.Handled = true;
                return;
            }
            case Key.Space or Key.Enter:
                e.Handled = true;
                if (!e.IsRepeat && !_keysDown.ContainsKey(e.Key))
                {
                    _keysDown[e.Key] = _selected;
                    Press(_selected, DefaultVelocity);
                }
                return;
        }
        if (!QwertyEnabled) return;
        for (int r = 0; r < Math.Min(_rows, QwertyRows.Length); r++)
        {
            int c = Array.IndexOf(QwertyRows[r], e.Key);
            if (c < 0) continue;
            int pad = PadAtCell(c, r);
            if (pad < 0) return;
            e.Handled = true;
            if (e.IsRepeat || _keysDown.ContainsKey(e.Key)) return;
            _keysDown[e.Key] = pad;
            Press(pad, DefaultVelocity);
            return;
        }
    }

    /// <inheritdoc />
    protected internal override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_keysDown.Remove(e.Key, out int pad))
        {
            Release(pad);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- live updates

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _ticker ??= new PadsTicker(this);
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

    private sealed class PadsTicker(DrumPads owner) : ITickable
    {
        private int _activityVersion = -1;

        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            bool dirty = false;
            int count = owner.Count;
            // Glows decay over about a quarter of a second.
            float decay = (float)elapsedSeconds / 0.25f;
            for (int i = 0; i < count; i++)
            {
                if (owner._glow[i] > 0f)
                {
                    owner._glow[i] = MathF.Max(0f, owner._glow[i] - decay);
                    dirty = true;
                }
            }
            for (int note = 0; note < 128; note++)
            {
                if (Volatile.Read(ref owner._pendingFlash[note]) == 0) continue;
                int flash = Interlocked.Exchange(ref owner._pendingFlash[note], 0);
                if (flash == 0) continue;
                float velocity = (flash - 1) / 1000f;
                for (int i = 0; i < count; i++)
                {
                    if (owner.Pads[i].Note == note) owner._glow[i] = MathF.Max(owner._glow[i], 0.4f + 0.6f * velocity);
                }
                dirty = true;
            }
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

/// <summary>
/// Peer of <see cref="DrumPads"/>: a group of buttons, one per pad (invoking one plays the pad); the focus is on the
/// selected pad.
/// </summary>
/// <param name="owner">The pads.</param>
public class DrumPadsAutomationPeer(DrumPads owner) : ElementAutomationPeer<DrumPads>(owner)
{
    private readonly List<DrumPadAutomationPeer> _pads = new();
    private int _reported = -1;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationDrumPads;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => false;

    /// <summary>Peer of pad <paramref name="index"/>.</summary>
    /// <param name="index">Pad index.</param>
    public AutomationPeer GetPad(int index)
    {
        while (_pads.Count <= index) _pads.Add(new DrumPadAutomationPeer(this, _pads.Count));
        return _pads[index];
    }

    internal int PadCount => Owner.Pads.Count;

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        for (int i = 0; i < PadCount; i++) children.Add(GetPad(i));
    }

    /// <inheritdoc />
    public override AutomationPeer GetFocusTarget()
    {
        int selected = Owner.SelectedPad;
        return selected >= 0 && selected < PadCount ? GetPad(selected) : this;
    }

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged()
    {
        int selected = Owner.SelectedPad;
        if (selected == _reported) return;
        _reported = selected;
        Host?.UpdateAutomationFocus();
    }
}

/// <summary>Peer of one pad of <see cref="DrumPads"/>: a button named after the pad and its note.</summary>
public sealed class DrumPadAutomationPeer : ItemAutomationPeer, IInvokePattern
{
    private readonly DrumPadsAutomationPeer _group;

    internal DrumPadAutomationPeer(DrumPadsAutomationPeer group, int index) : base(group)
    {
        _group = group;
        Index = index;
    }

    /// <summary>Pad index.</summary>
    public int Index { get; }

    /// <inheritdoc />
    protected override bool Exists => Index < _group.PadCount;

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Button;

    /// <inheritdoc />
    public override string Name => Exists ? _group.Owner.PadName(Index) + ", " + _group.Owner.NoteName(_group.Owner.Pads[Index].Note) : string.Empty;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => _group.IsEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Exists ? _group.Owner.PadRect(Index) : Rect.Empty;

    /// <inheritdoc />
    public override int PositionInSet => Index + 1;

    /// <inheritdoc />
    public override int SizeOfSet => _group.PadCount;

    /// <inheritdoc />
    public void Invoke()
    {
        ThrowIfNotEnabled();
        _group.Owner.SelectedPad = Index;
        _group.Owner.Tap(Index);
    }

    /// <inheritdoc />
    public override void SetFocus()
    {
        _group.Owner.SelectedPad = Index;
        _group.Owner.Focus();
    }
}
