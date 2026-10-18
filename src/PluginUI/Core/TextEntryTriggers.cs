// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// User actions that open the in-place text box of a value control (<see cref="Knob"/>, <see cref="Slider"/>) so
/// that a value can be typed instead of dragged. Combine flags with <c>|</c>; set
/// <see cref="ValueControl.TextEntryTriggers"/> to <see cref="None"/> to disable typing.
/// </summary>
/// <remarks>
/// <para>Typed numbers always use a point as decimal separator (see <see cref="InvariantNumber"/>); a rejected text
/// keeps the box open with a red frame.</para>
/// <para>Double-click has two meanings on a value control: on the value text it opens the text box
/// (<see cref="DoubleClickOnValue"/>), elsewhere it resets to the default value
/// (<see cref="ValueControl.ResetOnDoubleClick"/>). <see cref="DoubleClickAnywhere"/> gives the text box priority
/// everywhere, which is useful when the value text is hidden.</para>
/// </remarks>
[Flags]
public enum TextEntryTriggers
{
    /// <summary>Typing a value is disabled.</summary>
    None = 0,

    /// <summary>Double-click on the value text (below a knob, beside a slider).</summary>
    DoubleClickOnValue = 1,

    /// <summary>Alt + left click anywhere on the control.</summary>
    AltClick = 2,

    /// <summary>Double-click with the right button anywhere on the control.</summary>
    RightDoubleClick = 4,

    /// <summary>Enter key while the control has the keyboard focus.</summary>
    EnterKey = 8,

    /// <summary>
    /// Double-click anywhere on the control opens the text box (takes precedence over
    /// <see cref="ValueControl.ResetOnDoubleClick"/>; Ctrl+click still resets).
    /// </summary>
    DoubleClickAnywhere = 16,

    /// <summary>Default: <see cref="DoubleClickOnValue"/> | <see cref="AltClick"/> | <see cref="RightDoubleClick"/> | <see cref="EnterKey"/>.</summary>
    Default = DoubleClickOnValue | AltClick | RightDoubleClick | EnterKey,
}
