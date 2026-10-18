// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Single-line text input: caret, selection (mouse, Shift+keys, double-click word), clipboard (Ctrl+A/C/X/V),
/// word jumps (Ctrl+arrows), placeholder, filter, maximum length, read-only mode and an optional search look
/// (magnifier and clear button).
/// </summary>
/// <remarks>
/// A plug-in window normally leaves the keyboard to the DAW; a text box takes the Win32 keyboard focus only while
/// it is focused (<see cref="UIHost.RequestKeyboardFocus"/>), so typing does not trigger DAW shortcuts.
/// </remarks>
public class TextBox : Control
{
    private bool _isInvalid;
    private string _text = string.Empty;
    private int _caret;
    private int _anchor;
    private float _scroll;
    private bool _selecting;
    private CaretBlinker? _blinker;
    private bool _caretVisible = true;

    /// <summary>Creates a text box.</summary>
    public TextBox()
    {
        Cursor = StandardCursor.IBeam;
    }

    /// <summary>Text.</summary>
    [Category("Text")]
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (MaxLength > 0 && value.Length > MaxLength) value = value[..MaxLength];
            if (_text == value) return;
            _text = value;
            _caret = Math.Min(_caret, _text.Length);
            _anchor = Math.Min(_anchor, _text.Length);
            IsInvalid = false; // the user is correcting the text
            TextChanged?.Invoke(this, _text);
            InvalidateVisual();
        }
    }

    /// <summary>Text shown dimmed while empty.</summary>
    [Category("Text")]
    public string? Placeholder { get; set; }

    /// <summary>
    /// Marks the content as invalid: the frame is drawn in the palette's <see cref="Palette.Danger"/> color.
    /// Set by in-place editors (<see cref="UIHost.BeginTextEdit(Rect, string, Func{string, bool}, Action?, TextStyle?, Func{char, bool}?)"/>)
    /// when a typed value is rejected; cleared automatically as soon as the text changes.
    /// </summary>
    [Category("Behavior")]
    public bool IsInvalid { get => _isInvalid; set { if (_isInvalid == value) return; _isInvalid = value; InvalidateVisual(); } }

    /// <summary>Maximum number of characters (0 = unlimited).</summary>
    [Category("Behavior")]
    public int MaxLength { get; set; }

    /// <summary>Prevents editing (selection and copy remain possible).</summary>
    [Category("Behavior")]
    public bool IsReadOnly { get; set; }

    /// <summary>Accepts or rejects typed characters (e.g. digits only).</summary>
    [Browsable(false)]
    public Func<char, bool>? CharacterFilter { get; set; }

    /// <summary>Search look: magnifier on the left, clear button on the right.</summary>
    [Category("Behavior")]
    public bool IsSearchBox { get; set; }

    /// <summary>Selects everything when the box receives the focus.</summary>
    [Category("Behavior")]
    public bool SelectAllOnFocus { get; set; }

    /// <summary>Text style (null = theme normal text, left aligned).</summary>
    [Category("Text")]
    public TextStyle? TextStyle { get; set; }

    /// <summary>Raised on every text change.</summary>
    public event Action<TextBox, string>? TextChanged;

    /// <summary>Raised when Enter is pressed (or the focus leaves after an edit when <see cref="CommitOnFocusLost"/>).</summary>
    public event Action<TextBox, string>? Committed;

    /// <summary>Raised when Escape is pressed.</summary>
    public event Action<TextBox>? Cancelled;

    /// <summary>Raises <see cref="Committed"/> when the focus leaves.</summary>
    [Category("Behavior")]
    public bool CommitOnFocusLost { get; set; } = true;

    /// <summary>Caret index.</summary>
    [Category("Behavior")]
    public int CaretIndex { get => _caret; set { _caret = _anchor = Math.Clamp(value, 0, _text.Length); InvalidateVisual(); } }

    /// <summary>Start of the selection.</summary>
    public int SelectionStart => Math.Min(_caret, _anchor);

    /// <summary>Length of the selection.</summary>
    public int SelectionLength => Math.Abs(_caret - _anchor);

    /// <summary>Selected text.</summary>
    public string SelectedText => _text.Substring(SelectionStart, SelectionLength);

    /// <summary>Selects a range.</summary>
    public void Select(int start, int length)
    {
        _anchor = Math.Clamp(start, 0, _text.Length);
        _caret = Math.Clamp(start + length, 0, _text.Length);
        InvalidateVisual();
    }

    /// <summary>Selects all the text.</summary>
    public void SelectAll() => Select(0, _text.Length);

    /// <inheritdoc />
    protected override Size DefaultSize => new(140f, Theme.Metrics.ControlHeight);

    private TextStyle EffectiveTextStyle => (TextStyle ?? Theme.Typography.Normal) with { Wrap = false, Ellipsis = false };

    private Rect TextArea
    {
        get
        {
            var r = Bounds.Deflate(new Thickness(6f, 0f));
            if (IsSearchBox)
            {
                r = Rect.FromEdges(r.X + 16f, r.Y, r.Right - (_text.Length > 0 ? 16f : 0f), r.Bottom);
            }
            return r;
        }
    }

    private Rect ClearButtonRect => new(Bounds.Right - 20f, Bounds.Y, 18f, Bounds.Height);

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.35f) });
        dc.FillRoundedRect(widget, radius, style.Background);
        var frame = IsInvalid ? Theme.Palette.Danger : IsFocused ? style.Accent : IsMouseOver ? style.Frame.Lighten(0.15f) : style.Frame;
        dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, frame, IsFocused || IsInvalid ? 1.5f : 1f);

        if (IsSearchBox)
        {
            var c = new Point(widget.X + 12f, widget.Center.Y - 1f);
            dc.DrawCircle(c, 4f, p.TextDim, 1.4f);
            dc.DrawLine(new Point(c.X + 3f, c.Y + 3f), new Point(c.X + 6.5f, c.Y + 6.5f), p.TextDim, 1.4f, StrokeStyle.Round);
            if (_text.Length > 0)
            {
                var x = ClearButtonRect.Center;
                dc.DrawLine(new Point(x.X - 3.5f, x.Y - 3.5f), new Point(x.X + 3.5f, x.Y + 3.5f), p.TextDim, 1.4f, StrokeStyle.Round);
                dc.DrawLine(new Point(x.X - 3.5f, x.Y + 3.5f), new Point(x.X + 3.5f, x.Y - 3.5f), p.TextDim, 1.4f, StrokeStyle.Round);
            }
        }

        var area = TextArea;
        var ts = EffectiveTextStyle;
        UpdateScroll(area, ts);
        using (dc.Clip(area))
        {
            if (_text.Length == 0)
            {
                if (!string.IsNullOrEmpty(Placeholder) && !IsFocused) dc.DrawText(Placeholder, area, ts, p.TextDisabled);
            }
            else
            {
                if (SelectionLength > 0 && IsFocused)
                {
                    float x0 = TextMeasurer.CaretX(_text, ts, SelectionStart), x1 = TextMeasurer.CaretX(_text, ts, SelectionStart + SelectionLength);
                    float h = ts.Size * 1.45f;
                    dc.FillRect(new Rect(area.X + x0 - _scroll, area.Center.Y - h * 0.5f, x1 - x0, h), Host?.HasKeyboardFocus == false ? p.Selection.Fade(0.5f) : p.Selection);
                }
                var layout = new Rect(area.X - _scroll, area.Y, MathF.Max(area.Width + _scroll, TextMeasurer.Measure(_text, ts).Width + 4f), area.Height);
                dc.DrawText(_text, layout, ts with { Align = HorizontalAlignment.Left }, IsReadOnly ? p.TextDim : p.Text);
            }
            if (IsFocused && _caretVisible && !IsReadOnly && Host?.HasKeyboardFocus != false)
            {
                float cx = MathF.Round(area.X + TextMeasurer.CaretX(_text, ts, _caret) - _scroll) + 0.5f;
                float h = ts.Size * 1.3f;
                dc.DrawLine(new Point(cx, area.Center.Y - h * 0.5f), new Point(cx, area.Center.Y + h * 0.5f), p.Text, 1f);
            }
        }
    }

    private void UpdateScroll(Rect area, TextStyle ts)
    {
        float caretX = TextMeasurer.CaretX(_text, ts, _caret);
        float textW = TextMeasurer.Measure(_text, ts).Width;
        if (caretX - _scroll > area.Width - 2f) _scroll = caretX - area.Width + 2f;
        if (caretX - _scroll < 0f) _scroll = caretX;
        _scroll = Math.Clamp(_scroll, 0f, MathF.Max(0f, textW - area.Width + 2f));
    }

    // ---------------------------------------------------------------- mouse

    private int IndexAt(Point p) => TextMeasurer.HitTest(_text, EffectiveTextStyle, p.X - TextArea.X + _scroll);

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left) return;
        e.Handled = true;
        if (IsSearchBox && _text.Length > 0 && ClearButtonRect.Contains(e.Position) && !IsReadOnly)
        {
            Text = string.Empty;
            _caret = _anchor = 0;
            return;
        }
        Focus();
        int index = IndexAt(e.Position);
        if (e.ClickCount == 2)
        {
            SelectWordAt(index);
            return;
        }
        _caret = index;
        if (!e.Shift) _anchor = index;
        _selecting = true;
        ResetBlink();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting || !IsMouseCaptured) return;
        int index = IndexAt(e.Position);
        if (index != _caret)
        {
            _caret = index;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _selecting = false;
    }

    private void SelectWordAt(int index)
    {
        int start = index, end = index;
        while (start > 0 && char.IsLetterOrDigit(_text[start - 1])) start--;
        while (end < _text.Length && char.IsLetterOrDigit(_text[end])) end++;
        Select(start, end - start);
    }

    // ---------------------------------------------------------------- focus & caret

    /// <inheritdoc />
    protected internal override void OnGotFocus()
    {
        base.OnGotFocus();
        Host?.RequestKeyboardFocus();
        if (SelectAllOnFocus) SelectAll();
        _blinker ??= new CaretBlinker(this);
        ResetBlink();
        Host?.AddTicker(_blinker);
    }

    /// <inheritdoc />
    protected internal override void OnLostFocus()
    {
        base.OnLostFocus();
        if (_blinker is not null) Host?.RemoveTicker(_blinker);
        _anchor = _caret;
        if (CommitOnFocusLost) Committed?.Invoke(this, _text);
    }

    private void ResetBlink()
    {
        _caretVisible = true;
        _blinker?.Reset();
        InvalidateVisual();
    }

    private sealed class CaretBlinker(TextBox owner) : ITickable
    {
        private double _t;

        public void Reset() => _t = 0;

        public bool Tick(double elapsedSeconds)
        {
            if (!owner.IsFocused) return false;
            _t += elapsedSeconds;
            bool visible = _t % 1.06 < 0.53;
            if (visible != owner._caretVisible)
            {
                owner._caretVisible = visible;
                owner.InvalidateVisual();
            }
            return true;
        }
    }

    // ---------------------------------------------------------------- keyboard

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool shift = e.Shift, ctrl = e.Control;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Left: MoveCaret(ctrl ? WordLeft(_caret) : (SelectionLength > 0 && !shift ? SelectionStart : _caret - 1), shift); break;
            case Key.Right: MoveCaret(ctrl ? WordRight(_caret) : (SelectionLength > 0 && !shift ? SelectionStart + SelectionLength : _caret + 1), shift); break;
            case Key.Home: MoveCaret(0, shift); break;
            case Key.End: MoveCaret(_text.Length, shift); break;
            case Key.Back:
                if (IsReadOnly) break;
                if (SelectionLength == 0) _anchor = ctrl ? WordLeft(_caret) : Math.Max(0, _caret - 1);
                ReplaceSelection(string.Empty);
                break;
            case Key.Delete:
                if (IsReadOnly) break;
                if (SelectionLength == 0) _anchor = ctrl ? WordRight(_caret) : Math.Min(_text.Length, _caret + 1);
                ReplaceSelection(string.Empty);
                break;
            case Key.A when ctrl: SelectAll(); break;
            case Key.C when ctrl:
                if (SelectionLength > 0) Clipboard.SetText(SelectedText);
                break;
            case Key.X when ctrl:
                if (SelectionLength > 0 && !IsReadOnly) { Clipboard.SetText(SelectedText); ReplaceSelection(string.Empty); }
                break;
            case Key.V when ctrl:
                if (!IsReadOnly && Clipboard.GetText() is { } paste) Insert(paste.Replace("\r", string.Empty).Replace('\n', ' '));
                break;
            case Key.Enter:
                Committed?.Invoke(this, _text);
                break;
            case Key.Escape:
                Cancelled?.Invoke(this);
                break;
            case Key.Tab:
                e.Handled = false; // focus navigation
                break;
            default:
                // Printable keys arrive through OnTextInput; claim them so the DAW never sees them.
                e.Handled = !ctrl && (e.Modifiers & ModifierKeys.Alt) == 0;
                break;
        }
        ResetBlink();
    }

    /// <inheritdoc />
    protected internal override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || IsReadOnly) return;
        e.Handled = true;
        foreach (char c in e.Text)
        {
            if (char.IsControl(c)) return;
        }
        Insert(e.Text);
    }

    /// <summary>Inserts text at the caret (replacing the selection), applying the filter and maximum length.</summary>
    public void Insert(string text)
    {
        if (CharacterFilter is not null) text = new string(text.Where(CharacterFilter).ToArray());
        if (MaxLength > 0) text = text[..Math.Min(text.Length, Math.Max(0, MaxLength - (_text.Length - SelectionLength)))];
        if (text.Length == 0) return;
        ReplaceSelection(text);
    }

    private void ReplaceSelection(string text)
    {
        int start = SelectionStart;
        string updated = _text.Remove(start, SelectionLength).Insert(start, text);
        _caret = _anchor = start + text.Length;
        Text = updated;
        ResetBlink();
    }

    private void MoveCaret(int index, bool extend)
    {
        _caret = Math.Clamp(index, 0, _text.Length);
        if (!extend) _anchor = _caret;
        InvalidateVisual();
    }

    private int WordLeft(int i)
    {
        i = Math.Max(0, i - 1);
        while (i > 0 && !char.IsLetterOrDigit(_text[i])) i--;
        while (i > 0 && char.IsLetterOrDigit(_text[i - 1])) i--;
        return i;
    }

    private int WordRight(int i)
    {
        while (i < _text.Length && char.IsLetterOrDigit(_text[i])) i++;
        while (i < _text.Length && !char.IsLetterOrDigit(_text[i])) i++;
        return i;
    }
}

/// <summary>
/// Popup hosting a <see cref="TextBox"/> exactly over a rectangle: in-place editing of labels, values and list items.
/// Shown by <see cref="UIHost.BeginTextEdit(Rect, string, Func{string, bool}, Action?, TextStyle?, Func{char, bool}?)"/>.
/// </summary>
public sealed class TextEditPopup : Popup
{
    private readonly Func<string, bool> _tryCommit;
    private readonly Action? _cancel;
    private bool _done;

    internal TextEditPopup(Rect bounds, string text, TextStyle? style, Func<string, bool> tryCommit, Action? cancel, Func<char, bool>? filter)
    {
        _tryCommit = tryCommit;
        _cancel = cancel;
        Anchor = bounds;
        Placement = PopupPlacement.AtPoint;
        MinPopupWidth = bounds.Width;
        CapturesKeyboard = true;
        ConsumesOutsideClick = false;
        Box = new TextBox
        {
            Text = text,
            TextStyle = style,
            CharacterFilter = filter,
            Width = bounds.Width,
            Height = bounds.Height,
            SelectAllOnFocus = true,
            CommitOnFocusLost = false,
        };
        Box.Committed += (_, t) => Finish(t);
        Box.Cancelled += _ => Finish(null);
        Content = Box;
    }

    /// <summary>The editing box.</summary>
    public TextBox Box { get; }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.Post(() => Box.Focus());
    }

    private void Finish(string? text)
    {
        if (_done) return;
        if (text is null)
        {
            _done = true;
            Close();
            _cancel?.Invoke();
            return;
        }
        if (!_tryCommit(text))
        {
            // Rejected (e.g. "1,5" for a number): stay open, show the error and let the user correct it.
            Box.IsInvalid = true;
            Box.SelectAll();
            return;
        }
        _done = true;
        Close();
    }

    /// <inheritdoc />
    protected internal override void OnClosed()
    {
        // Light dismiss (click outside) commits the edit, or cancels it if the text is rejected.
        if (!_done)
        {
            _done = true;
            if (!_tryCommit(Box.Text)) _cancel?.Invoke();
        }
        base.OnClosed();
    }
}

public sealed partial class UIHost
{
    /// <summary>
    /// Shows a text box over <paramref name="bounds"/> for in-place editing. Enter or a click outside commits,
    /// Escape cancels.
    /// </summary>
    /// <param name="bounds">Area covered by the editor (DIP).</param>
    /// <param name="text">Initial text (selected).</param>
    /// <param name="commit">Called with the final text when the user commits.</param>
    /// <param name="cancel">Called when the user cancels (Escape), or <c>null</c>.</param>
    /// <param name="style">Text style, or <c>null</c> for the theme's default.</param>
    /// <param name="filter">Character filter (return <c>false</c> to reject a typed character), or <c>null</c>.</param>
    /// <returns>The popup, already shown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <c>null</c>.</exception>
    public TextEditPopup BeginTextEdit(Rect bounds, string text, Action<string> commit, Action? cancel = null, TextStyle? style = null, Func<char, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return BeginTextEdit(bounds, text, t => { commit(t); return true; }, cancel, style, filter);
    }

    /// <summary>
    /// Shows a text box over <paramref name="bounds"/> for in-place editing with validation: when
    /// <paramref name="tryCommit"/> returns <c>false</c> the editor stays open, its frame turns red
    /// (<see cref="TextBox.IsInvalid"/>) and the text is selected for correction. Escape cancels; a click outside
    /// commits a valid text and cancels an invalid one.
    /// </summary>
    /// <param name="bounds">Area covered by the editor (DIP).</param>
    /// <param name="text">Initial text (selected).</param>
    /// <param name="tryCommit">Applies the text and returns <c>true</c>, or returns <c>false</c> to reject it.</param>
    /// <param name="cancel">Called when the edit is cancelled, or <c>null</c>.</param>
    /// <param name="style">Text style, or <c>null</c> for the theme's default.</param>
    /// <param name="filter">Character filter (return <c>false</c> to reject a typed character), or <c>null</c>.</param>
    /// <returns>The popup, already shown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tryCommit"/> is <c>null</c>.</exception>
    /// <example>
    /// <code>
    /// host.BeginTextEdit(area, "0.5", text =>
    /// {
    ///     if (!InvariantNumber.TryParse(text, out double v)) return false; // "0,5" is refused
    ///     myValue = v;
    ///     return true;
    /// });
    /// </code>
    /// </example>
    public TextEditPopup BeginTextEdit(Rect bounds, string text, Func<string, bool> tryCommit, Action? cancel = null, TextStyle? style = null, Func<char, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(tryCommit);
        var popup = new TextEditPopup(bounds, text ?? string.Empty, style, tryCommit, cancel, filter);
        ShowPopup(popup);
        return popup;
    }
}
