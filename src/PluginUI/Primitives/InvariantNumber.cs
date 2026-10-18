// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI;

/// <summary>
/// Culture-independent number formatting and parsing used for every number PluginUI displays or reads from the
/// user: the decimal separator is always a point (<c>1.5</c>), whatever the language of the interface
/// (<see cref="UIHost.Language"/>) and the regional settings of the machine (<see cref="CultureInfo.CurrentCulture"/>).
/// </summary>
/// <remarks>
/// <para>Accepted input: optional sign, digits, an optional point and fraction, an optional exponent
/// (<c>-12</c>, <c>0.5</c>, <c>.5</c>, <c>1e3</c>), surrounded by optional spaces. A comma is rejected rather than
/// guessed: <c>1,5</c> could mean one and a half or fifteen depending on the reader, so the text box reports the
/// value as invalid instead of applying a wrong one. Thousands separators are not accepted either.</para>
/// <para>Use these helpers in custom controls and formatters instead of <see cref="double.Parse(string)"/> or
/// <see cref="double.ToString()"/>, which follow the regional settings.</para>
/// </remarks>
/// <example>
/// <code>
/// string text = InvariantNumber.Format(1234.5, "0.0");   // "1234.5" on every machine
/// bool ok = InvariantNumber.TryParse(" -0.25 ", out double v); // true, v = -0.25
/// bool bad = InvariantNumber.TryParse("0,25", out _);         // false: comma rejected
/// </code>
/// </example>
public static class InvariantNumber
{
    private const NumberStyles Styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>The culture used for all numbers: <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public static CultureInfo Culture => CultureInfo.InvariantCulture;

    /// <summary>Parses a number written with a decimal point.</summary>
    /// <param name="text">The text to parse (may be <c>null</c>).</param>
    /// <param name="value">The parsed value, or 0 when the text is not a valid number.</param>
    /// <returns><c>true</c> if <paramref name="text"/> is a finite number in the accepted syntax (see remarks of <see cref="InvariantNumber"/>).</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out double value)
    {
        if (double.TryParse(text, Styles, CultureInfo.InvariantCulture, out value) && double.IsFinite(value)) return true;
        value = 0.0;
        return false;
    }

    /// <summary>Parses a number written with a decimal point.</summary>
    /// <param name="text">The text to parse (may be <c>null</c>).</param>
    /// <param name="value">The parsed value, or 0 when the text is not a valid number.</param>
    /// <returns><c>true</c> if <paramref name="text"/> is a valid finite number.</returns>
    public static bool TryParse(string? text, out double value) => TryParse(text.AsSpan(), out value);

    /// <summary>Formats a number with a decimal point.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">A standard or custom .NET numeric format string (<c>"F2"</c>, <c>"0.##"</c>…); <c>null</c> = shortest round-trip form.</param>
    /// <returns>The formatted text, e.g. <c>"0.50"</c> for <c>Format(0.5, "F2")</c>.</returns>
    public static string Format(double value, string? format = null) => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Formats a number with a fixed count of decimals and a decimal point.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="decimals">Number of decimals (clamped to 0..15).</param>
    /// <returns>The formatted text, e.g. <c>"12.30"</c> for <c>Format(12.3, 2)</c>.</returns>
    public static string Format(double value, int decimals)
        => value.ToString("F" + Math.Clamp(decimals, 0, 15).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
