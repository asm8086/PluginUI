// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// Value a control can be bound to: a plug-in parameter (through an adapter such as PluginUI.NPlug)
/// or a local value (<see cref="LocalParameter"/>).
/// </summary>
/// <remarks>
/// <para>Values are normalized to [0, 1], as in VST3. <see cref="StepCount"/> &gt; 0 means discrete values
/// (<c>StepCount + 1</c> states: 1 = on/off).</para>
/// <para>Edits follow the gesture protocol: <see cref="BeginEdit"/>, any number of <see cref="PerformEdit"/>,
/// <see cref="EndEdit"/>. Several parameters may be edited at the same time.</para>
/// <para><see cref="Changed"/> must be raised on the UI thread, for every change (user, host automation, preset).</para>
/// </remarks>
public interface IParameter
{
    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>Unit label (Hz, dB, %, ms...).</summary>
    string Units { get; }

    /// <summary>0 = continuous; otherwise number of steps (states − 1).</summary>
    int StepCount { get; }

    /// <summary>Default normalized value.</summary>
    double DefaultNormalizedValue { get; }

    /// <summary>Current normalized value.</summary>
    double NormalizedValue { get; }

    /// <summary><c>true</c> between <see cref="BeginEdit"/> and <see cref="EndEdit"/>.</summary>
    bool IsEditing { get; }

    /// <summary>Text for a normalized value (without units).</summary>
    string ToDisplayString(double normalized);

    /// <summary>Parses user text (with or without units) into a normalized value.</summary>
    bool TryParse(string text, out double normalized);

    /// <summary>Normalized → plain value.</summary>
    double ToPlain(double normalized);

    /// <summary>Plain → normalized value.</summary>
    double ToNormalized(double plain);

    /// <summary>Raised after every value change.</summary>
    event Action<IParameter>? Changed;

    /// <summary>Starts an edit gesture.</summary>
    void BeginEdit();

    /// <summary>Sets the value during a gesture.</summary>
    void PerformEdit(double normalized);

    /// <summary>Ends the edit gesture.</summary>
    void EndEdit();
}

/// <summary>Helpers for <see cref="IParameter"/>.</summary>
public static class ParameterExtensions
{
    /// <summary>Single-step edit: begin, perform, end.</summary>
    public static void SetValue(this IParameter parameter, double normalized)
    {
        bool owns = !parameter.IsEditing;
        if (owns) parameter.BeginEdit();
        parameter.PerformEdit(Math.Clamp(normalized, 0.0, 1.0));
        if (owns) parameter.EndEdit();
    }

    /// <summary>Display text with units.</summary>
    public static string ToDisplayStringWithUnits(this IParameter parameter, double normalized)
    {
        var text = parameter.ToDisplayString(normalized);
        return string.IsNullOrEmpty(parameter.Units) ? text : $"{text} {parameter.Units}";
    }

    /// <summary>Current plain value.</summary>
    public static double PlainValue(this IParameter parameter) => parameter.ToPlain(parameter.NormalizedValue);

    /// <summary>Index of the current discrete state (0 for continuous parameters).</summary>
    public static int StateIndex(this IParameter parameter)
        => parameter.StepCount <= 0 ? 0 : (int)Math.Round(Math.Clamp(parameter.NormalizedValue, 0.0, 1.0) * parameter.StepCount);
}
