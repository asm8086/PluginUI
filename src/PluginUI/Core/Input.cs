// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>Mouse button.</summary>
public enum MouseButton
{
    /// <summary>No button (move events).</summary>
    None,
    /// <summary>Left (primary) button.</summary>
    Left,
    /// <summary>Right (secondary) button.</summary>
    Right,
    /// <summary>Middle button / wheel click.</summary>
    Middle,
}

/// <summary>Keyboard modifiers.</summary>
[Flags]
public enum ModifierKeys
{
    /// <summary>None.</summary>
    None = 0,
    /// <summary>Shift.</summary>
    Shift = 1,
    /// <summary>Control.</summary>
    Control = 2,
    /// <summary>Alt.</summary>
    Alt = 4,
}

/// <summary>Standard cursor shapes.</summary>
public enum StandardCursor
{
    /// <summary>Inherit from the parent element (arrow at the root).</summary>
    Default,
    /// <summary>Arrow.</summary>
    Arrow,
    /// <summary>Hand (links, buttons).</summary>
    Hand,
    /// <summary>Text caret.</summary>
    IBeam,
    /// <summary>Vertical resize / vertical drag.</summary>
    SizeNS,
    /// <summary>Horizontal resize / horizontal drag.</summary>
    SizeWE,
    /// <summary>Move in any direction.</summary>
    SizeAll,
    /// <summary>Diagonal resize (top-left / bottom-right).</summary>
    SizeNWSE,
    /// <summary>Diagonal resize (top-right / bottom-left).</summary>
    SizeNESW,
    /// <summary>Precision cross.</summary>
    Cross,
    /// <summary>Operation not allowed.</summary>
    No,
    /// <summary>Busy.</summary>
    Wait,
}

/// <summary>
/// Keys, with Windows virtual-key values.
/// </summary>
public enum Key
{
    /// <summary>No key.</summary>
    None = 0,
    /// <summary>Backspace.</summary>
    Back = 0x08,
    /// <summary>Tab.</summary>
    Tab = 0x09,
    /// <summary>Enter.</summary>
    Enter = 0x0D,
    /// <summary>Shift.</summary>
    Shift = 0x10,
    /// <summary>Control.</summary>
    Control = 0x11,
    /// <summary>Alt.</summary>
    Alt = 0x12,
    /// <summary>Escape.</summary>
    Escape = 0x1B,
    /// <summary>Space.</summary>
    Space = 0x20,
    /// <summary>Page up.</summary>
    PageUp = 0x21,
    /// <summary>Page down.</summary>
    PageDown = 0x22,
    /// <summary>End.</summary>
    End = 0x23,
    /// <summary>Home.</summary>
    Home = 0x24,
    /// <summary>Left arrow.</summary>
    Left = 0x25,
    /// <summary>Up arrow.</summary>
    Up = 0x26,
    /// <summary>Right arrow.</summary>
    Right = 0x27,
    /// <summary>Down arrow.</summary>
    Down = 0x28,
    /// <summary>Insert.</summary>
    Insert = 0x2D,
    /// <summary>Delete.</summary>
    Delete = 0x2E,
    /// <summary>0.</summary>
    D0 = 0x30, /// <summary>1.</summary>
    D1, /// <summary>2.</summary>
    D2, /// <summary>3.</summary>
    D3, /// <summary>4.</summary>
    D4, /// <summary>5.</summary>
    D5, /// <summary>6.</summary>
    D6, /// <summary>7.</summary>
    D7, /// <summary>8.</summary>
    D8, /// <summary>9.</summary>
    D9,
    /// <summary>A.</summary>
    A = 0x41, /// <summary>B.</summary>
    B, /// <summary>C.</summary>
    C, /// <summary>D.</summary>
    D, /// <summary>E.</summary>
    E, /// <summary>F.</summary>
    F, /// <summary>G.</summary>
    G, /// <summary>H.</summary>
    H, /// <summary>I.</summary>
    I, /// <summary>J.</summary>
    J, /// <summary>K.</summary>
    K, /// <summary>L.</summary>
    L, /// <summary>M.</summary>
    M, /// <summary>N.</summary>
    N, /// <summary>O.</summary>
    O, /// <summary>P.</summary>
    P, /// <summary>Q.</summary>
    Q, /// <summary>R.</summary>
    R, /// <summary>S.</summary>
    S, /// <summary>T.</summary>
    T, /// <summary>U.</summary>
    U, /// <summary>V.</summary>
    V, /// <summary>W.</summary>
    W, /// <summary>X.</summary>
    X, /// <summary>Y.</summary>
    Y, /// <summary>Z.</summary>
    Z,
    /// <summary>Numeric keypad 0.</summary>
    NumPad0 = 0x60,
    /// <summary>Numeric keypad multiply.</summary>
    Multiply = 0x6A,
    /// <summary>Numeric keypad add.</summary>
    Add = 0x6B,
    /// <summary>Numeric keypad subtract.</summary>
    Subtract = 0x6D,
    /// <summary>Numeric keypad decimal.</summary>
    Decimal = 0x6E,
    /// <summary>Numeric keypad divide.</summary>
    Divide = 0x6F,
    /// <summary>F1.</summary>
    F1 = 0x70, /// <summary>F2.</summary>
    F2, /// <summary>F3.</summary>
    F3, /// <summary>F4.</summary>
    F4, /// <summary>F5.</summary>
    F5, /// <summary>F6.</summary>
    F6, /// <summary>F7.</summary>
    F7, /// <summary>F8.</summary>
    F8, /// <summary>F9.</summary>
    F9, /// <summary>F10.</summary>
    F10, /// <summary>F11.</summary>
    F11, /// <summary>F12.</summary>
    F12,
}

/// <summary>Base class of routed input events.</summary>
public abstract class RoutedEventArgs : EventArgs
{
    /// <summary>Set to <c>true</c> to stop the event from bubbling to parent elements.</summary>
    public bool Handled { get; set; }

    /// <summary>Element the event was originally dispatched to.</summary>
    public UIElement? Source { get; internal set; }
}

/// <summary>Mouse event data.</summary>
public sealed class MouseEventArgs : RoutedEventArgs
{
    internal MouseEventArgs(Point position, MouseButton button, ModifierKeys modifiers, int clickCount, float wheelDelta = 0f, bool horizontalWheel = false)
    {
        Position = position;
        Button = button;
        Modifiers = modifiers;
        ClickCount = clickCount;
        WheelDelta = wheelDelta;
        IsHorizontalWheel = horizontalWheel;
    }

    /// <summary>Position in DIPs (host coordinates, same space as <see cref="UIElement.Bounds"/>).</summary>
    public Point Position { get; }

    /// <summary>Button that changed (down/up), or <see cref="MouseButton.None"/> for moves.</summary>
    public MouseButton Button { get; }

    /// <summary>Keyboard modifiers held during the event.</summary>
    public ModifierKeys Modifiers { get; }

    /// <summary>1 for a single click, 2 for a double click.</summary>
    public int ClickCount { get; }

    /// <summary>Wheel rotation in notches (positive = away from the user / to the right).</summary>
    public float WheelDelta { get; }

    /// <summary><c>true</c> for horizontal wheel (tilt) events.</summary>
    public bool IsHorizontalWheel { get; }

    /// <summary>Shift held.</summary>
    public bool Shift => (Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>Control held.</summary>
    public bool Control => (Modifiers & ModifierKeys.Control) != 0;

    /// <summary>Alt held.</summary>
    public bool Alt => (Modifiers & ModifierKeys.Alt) != 0;
}

/// <summary>Keyboard event data.</summary>
public sealed class KeyEventArgs : RoutedEventArgs
{
    /// <summary>Creates key event data.</summary>
    public KeyEventArgs(Key key, ModifierKeys modifiers, bool isRepeat = false)
    {
        Key = key;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
    }

    /// <summary>Key.</summary>
    public Key Key { get; }

    /// <summary>Modifiers held.</summary>
    public ModifierKeys Modifiers { get; }

    /// <summary><c>true</c> for auto-repeat.</summary>
    public bool IsRepeat { get; }

    /// <summary>Shift held.</summary>
    public bool Shift => (Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>Control held.</summary>
    public bool Control => (Modifiers & ModifierKeys.Control) != 0;
}

/// <summary>Character input event data.</summary>
public sealed class TextInputEventArgs : RoutedEventArgs
{
    /// <summary>Creates character input data.</summary>
    public TextInputEventArgs(string text) => Text = text;

    /// <summary>Typed text (usually one character).</summary>
    public string Text { get; }
}

/// <summary>Files dropped from the shell.</summary>
public sealed class FilesDroppedEventArgs : RoutedEventArgs
{
    internal FilesDroppedEventArgs(Point position, IReadOnlyList<string> files)
    {
        Position = position;
        Files = files;
    }

    /// <summary>Drop position in DIPs.</summary>
    public Point Position { get; }

    /// <summary>Full paths of the dropped files.</summary>
    public IReadOnlyList<string> Files { get; }
}

/// <summary>Internal drag-and-drop event data (between elements of the same host).</summary>
public sealed class DragEventArgs : RoutedEventArgs
{
    internal DragEventArgs(Point position, object data, UIElement dragSource)
    {
        Position = position;
        Data = data;
        DragSource = dragSource;
    }

    /// <summary>Pointer position in DIPs.</summary>
    public Point Position { get; }

    /// <summary>Payload supplied by the drag source.</summary>
    public object Data { get; }

    /// <summary>Element that started the drag.</summary>
    public UIElement DragSource { get; }

    /// <summary>Set to <c>true</c> in <see cref="UIElement.OnDragOver"/> to accept a drop here.</summary>
    public bool Accept { get; set; }
}
