// 2026/11/06 02:49:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PluginUI;

/// <summary>
/// Keyboard shortcut: a key plus modifiers, such as Ctrl+Shift+S. Parsed from and formatted to the usual Windows
/// notation; the displayed form follows the host language (Ctrl+Maj+S in French).
/// </summary>
/// <param name="Key">Main key (not a modifier key).</param>
/// <param name="Modifiers">Modifiers that must be held, exactly.</param>
/// <example>
/// <code>
/// var save = KeyGesture.Parse("Ctrl+S");
/// bool hit = save.Matches(e); // in a KeyDown handler
/// </code>
/// </example>
public readonly record struct KeyGesture(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    /// <summary><c>true</c> when the gesture names a key.</summary>
    public bool IsEmpty => Key == Key.None;

    /// <summary><c>true</c> when <paramref name="e"/> is this gesture (same key, exactly the same modifiers).</summary>
    public bool Matches(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return !IsEmpty && e.Key == Key && e.Modifiers == Modifiers;
    }

    /// <summary>Parses "Ctrl+Shift+S", "Alt+F4", "F5", "Ctrl+Plus", "Del"… (case-insensitive, English or French names).</summary>
    /// <exception cref="FormatException">The text is not a gesture.</exception>
    public static KeyGesture Parse(string text)
        => TryParse(text, out var gesture) ? gesture : throw new FormatException($"'{text}' is not a key gesture.");

    /// <summary>Tries to parse a gesture (see <see cref="Parse"/>).</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out KeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        var key = Key.None;
        // "Ctrl++" names the plus key: split by hand so that a trailing '+' stays a key.
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '+' || i == start) continue;
            parts.Add(text[start..i]);
            start = i + 1;
        }
        if (start < text.Length) parts.Add(text[start..]);
        for (int i = 0; i < parts.Count; i++)
        {
            string part = parts[i].Trim();
            bool last = i == parts.Count - 1;
            if (TryParseModifier(part, out var m) && !last)
            {
                mods |= m;
                continue;
            }
            if (!last || !TryParseKey(part, out key)) return false;
        }
        if (key is Key.None or Key.Shift or Key.Control or Key.Alt) return false;
        gesture = new KeyGesture(key, mods);
        return true;
    }

    /// <summary>Invariant notation, such as "Ctrl+Shift+S" (the form <see cref="Parse"/> reads back).</summary>
    public override string ToString() => Format(UIStrings.English);

    /// <summary>Displayed notation in a language, such as "Ctrl+Maj+S" for <see cref="UIStrings.French"/>.</summary>
    public string ToDisplayString(UIStrings? strings = null) => Format(strings ?? UIStrings.English);

    private string Format(UIStrings strings)
    {
        if (IsEmpty) return string.Empty;
        var sb = new StringBuilder();
        if ((Modifiers & ModifierKeys.Control) != 0) sb.Append(strings.KeyName(Key.Control)).Append('+');
        if ((Modifiers & ModifierKeys.Shift) != 0) sb.Append(strings.KeyName(Key.Shift)).Append('+');
        if ((Modifiers & ModifierKeys.Alt) != 0) sb.Append(strings.KeyName(Key.Alt)).Append('+');
        sb.Append(strings.KeyName(Key));
        return sb.ToString();
    }

    private static bool TryParseModifier(string part, out ModifierKeys modifier)
    {
        modifier = part.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" or "CTL" => ModifierKeys.Control,
            "SHIFT" or "MAJ" => ModifierKeys.Shift,
            "ALT" => ModifierKeys.Alt,
            _ => ModifierKeys.None,
        };
        return modifier != ModifierKeys.None;
    }

    private static bool TryParseKey(string part, out Key key)
    {
        key = Key.None;
        if (part.Length == 1)
        {
            char c = char.ToUpperInvariant(part[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { key = (Key)c; return true; }
            key = c switch
            {
                '+' => Key.OemPlus,
                '-' => Key.OemMinus,
                ',' => Key.OemComma,
                '.' => Key.OemPeriod,
                ' ' => Key.Space,
                _ => Key.None,
            };
            return key != Key.None;
        }
        string upper = part.ToUpperInvariant();
        if (upper.Length is 2 or 3 && upper[0] == 'F' && int.TryParse(upper.AsSpan(1), out int f) && f is >= 1 and <= 12)
        {
            key = Key.F1 + (f - 1);
            return true;
        }
        key = upper switch
        {
            "PLUS" => Key.OemPlus,
            "MINUS" => Key.OemMinus,
            "COMMA" or "VIRGULE" => Key.OemComma,
            "PERIOD" or "POINT" => Key.OemPeriod,
            "DEL" or "DELETE" or "SUPPR" => Key.Delete,
            "INS" or "INSERT" or "INSER" => Key.Insert,
            "ESC" or "ESCAPE" or "ÉCHAP" or "ECHAP" => Key.Escape,
            "ENTER" or "RETURN" or "ENTRÉE" or "ENTREE" => Key.Enter,
            "SPACE" or "ESPACE" => Key.Space,
            "TAB" => Key.Tab,
            "BACKSPACE" or "BACK" or "RETOUR ARRIÈRE" => Key.Back,
            "HOME" or "ORIGINE" => Key.Home,
            "END" or "FIN" => Key.End,
            "PGUP" or "PAGEUP" or "PG.PRÉC" or "PGPRÉC" => Key.PageUp,
            "PGDN" or "PAGEDOWN" or "PG.SUIV" or "PGSUIV" => Key.PageDown,
            "LEFT" or "GAUCHE" => Key.Left,
            "RIGHT" or "DROITE" => Key.Right,
            "UP" or "HAUT" => Key.Up,
            "DOWN" or "BAS" => Key.Down,
            "NUM+" or "ADD" => Key.Add,
            "NUM-" or "SUBTRACT" => Key.Subtract,
            "NUM*" or "MULTIPLY" => Key.Multiply,
            "NUM/" or "DIVIDE" => Key.Divide,
            _ => Key.None,
        };
        if (key == Key.None && Enum.TryParse(part, ignoreCase: true, out Key named) && Enum.IsDefined(named)) key = named;
        return key != Key.None;
    }
}

/// <summary>
/// An action of the application with its label, shortcut and state, shared by menus, buttons and the keyboard:
/// "Save", "Undo", "Show grid"… A <see cref="MenuItem"/> built from a command shows its text, shortcut, enabled and
/// checked states as they are when the menu opens.
/// </summary>
/// <example>
/// <code>
/// var undo = new Command("&amp;Undo", stack.Undo, KeyGesture.Parse("Ctrl+Z")) { CanExecuteWhen = () => stack.CanUndo };
/// commands.Add(undo);                 // Ctrl+Z now runs it
/// editMenu.Items.Add(new MenuItem(undo));
/// </code>
/// </example>
public class Command
{
    private readonly Action? _execute;

    /// <summary>Creates a command.</summary>
    /// <param name="text">Label, with an optional mnemonic ("&amp;Save").</param>
    /// <param name="execute">Action run by <see cref="Execute"/> (null: only <see cref="Executed"/> handlers run).</param>
    /// <param name="gesture">Keyboard shortcut, or null.</param>
    public Command(string text, Action? execute = null, KeyGesture? gesture = null)
    {
        Text = text ?? string.Empty;
        _execute = execute;
        Gesture = gesture;
    }

    /// <summary>Label, with an optional mnemonic ("&amp;Save"). Menus show it without the '&amp;'.</summary>
    public string Text { get; set; }

    /// <summary>Keyboard shortcut, or null.</summary>
    public KeyGesture? Gesture { get; set; }

    /// <summary>Other shortcuts that also run the command (Ctrl+Shift+Z for Redo…); menus show only <see cref="Gesture"/>.</summary>
    public IList<KeyGesture> AlternateGestures { get; } = new List<KeyGesture>();

    /// <summary>Tooltip or status bar description.</summary>
    public string? Description { get; set; }

    /// <summary>Static enabled flag (combined with <see cref="CanExecuteWhen"/>).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Dynamic condition, evaluated when a menu opens or the shortcut is pressed.</summary>
    public Func<bool>? CanExecuteWhen { get; set; }

    /// <summary>Marks the command as a toggle: menus show a check mark when <see cref="IsChecked"/>.</summary>
    public bool IsCheckable { get; set; }

    /// <summary>Static checked state (overridden by <see cref="IsCheckedWhen"/>).</summary>
    public bool IsChecked
    {
        get => IsCheckedWhen?.Invoke() ?? _isChecked;
        set => _isChecked = value;
    }

    private bool _isChecked;

    /// <summary>Dynamic checked state; setting it makes the command checkable.</summary>
    public Func<bool>? IsCheckedWhen
    {
        get => _isCheckedWhen;
        set
        {
            _isCheckedWhen = value;
            if (value is not null) IsCheckable = true;
        }
    }

    private Func<bool>? _isCheckedWhen;

    /// <summary>Lets the shortcut repeat while the key is held (Undo, Redo, Zoom in…). Off by default.</summary>
    public bool AllowsRepeat { get; set; }

    /// <summary>Free user data.</summary>
    public object? Tag { get; set; }

    /// <summary>Label without its mnemonic marker ("&amp;Save" → "Save").</summary>
    public string DisplayText => Mnemonics.Strip(Text);

    /// <summary>Raised after the command ran.</summary>
    public event Action<Command>? Executed;

    /// <summary><c>true</c> when a key press is <see cref="Gesture"/> or one of the <see cref="AlternateGestures"/>.</summary>
    public bool Matches(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Gesture is { } g && g.Matches(e)) return true;
        foreach (var alternate in AlternateGestures)
        {
            if (alternate.Matches(e)) return true;
        }
        return false;
    }

    /// <summary><c>true</c> when the command can run now.</summary>
    public bool CanExecute() => IsEnabled && (CanExecuteWhen?.Invoke() ?? true);

    /// <summary>Runs the command if it can run. Returns <c>true</c> if it ran.</summary>
    public bool Execute()
    {
        if (!CanExecute()) return false;
        _execute?.Invoke();
        Executed?.Invoke(this);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Gesture is { } g ? $"{DisplayText} ({g})" : DisplayText;
}

/// <summary>
/// The commands of a window, routed from the keyboard: once attached to a <see cref="UIHost"/>, a key press matching
/// a command's <see cref="Command.Gesture"/> runs it (before the focused element sees the key).
/// </summary>
/// <remarks>Disabled commands let the key through, so a text box can still receive Ctrl+Z when there is nothing to
/// undo at the application level. Keys claimed by the focused element (<see cref="UIElement.ClaimsKey"/>: Ctrl+C,
/// Del… in a text box) go to it first.</remarks>
public sealed class CommandSet : List<Command>
{
    private UIHost? _host;

    /// <summary>Routes the host's key presses to the commands (replaces a previous attachment).</summary>
    public void Attach(UIHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Detach();
        _host = host;
        host.PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Stops routing key presses.</summary>
    public void Detach()
    {
        if (_host is null) return;
        _host.PreviewKeyDown -= OnPreviewKeyDown;
        _host = null;
    }

    /// <summary>Command whose shortcut is <paramref name="gesture"/>, or null.</summary>
    public Command? Find(KeyGesture gesture)
    {
        foreach (var command in this)
        {
            if (command.Gesture == gesture || command.AlternateGestures.Contains(gesture)) return command;
        }
        return null;
    }

    /// <summary>Runs the enabled command matching a key press. Returns <c>true</c> if one ran.</summary>
    public bool TryExecute(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        foreach (var command in this)
        {
            if (command.Matches(e) && command.CanExecute())
            {
                command.Execute();
                return true;
            }
        }
        return false;
    }

    private void OnPreviewKeyDown(UIHost host, KeyEventArgs e)
    {
        if (e.Handled || e.IsRepeat && !AllowsRepeat(e)) return;
        // The focused element goes first for the keys it edits with (a text box and Ctrl+C, Del…).
        if (host.FocusedElement?.ClaimsKey(e) == true) return;
        if (TryExecute(e)) e.Handled = true;
    }

    // Auto-repeat makes sense for Undo/Redo-like shortcuts held down, not for Save or Open.
    private bool AllowsRepeat(KeyEventArgs e) => Find(new KeyGesture(e.Key, e.Modifiers)) is { AllowsRepeat: true };
}

/// <summary>Helpers for '&amp;' mnemonic markers in menu texts ("&amp;File", "Save &amp;As…", "R&amp;&amp;D").</summary>
/// <remarks>'&amp;' followed by a letter or a digit marks the mnemonic; "&amp;&amp;" is a literal '&amp;'; any other
/// '&amp;' (as in "Drum &amp; Bass") stays literal.</remarks>
public static class Mnemonics
{
    /// <summary>Text without the markers.</summary>
    public static string Strip(string? text) => Parse(text, out _);

    /// <summary>Text without the markers and the index of the mnemonic character in it (-1 if none).</summary>
    public static string Parse(string? text, out int mnemonicIndex)
    {
        mnemonicIndex = -1;
        if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0) return text ?? string.Empty;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '&' && i + 1 < text.Length)
            {
                char next = text[i + 1];
                if (next == '&') { sb.Append('&'); i++; continue; }
                if (char.IsLetterOrDigit(next))
                {
                    if (mnemonicIndex < 0) mnemonicIndex = sb.Length;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Mnemonic character in upper case, or '\0'.</summary>
    public static char Get(string? text)
    {
        string plain = Parse(text, out int index);
        return index < 0 ? '\0' : char.ToUpperInvariant(plain[index]);
    }

    /// <summary>Escapes user data (a preset name such as "R&amp;B") so that no character becomes a mnemonic.</summary>
    public static string Escape(string? text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("&", "&&", StringComparison.Ordinal);

    /// <summary>Win32 menu notation: same markers, with every literal '&amp;' doubled.</summary>
    internal static string ToWin32(string? text)
    {
        string plain = Parse(text, out int index);
        var sb = new StringBuilder(plain.Length + 2);
        for (int i = 0; i < plain.Length; i++)
        {
            if (i == index) sb.Append('&');
            sb.Append(plain[i] == '&' ? "&&" : plain[i]);
        }
        return sb.ToString();
    }
}
