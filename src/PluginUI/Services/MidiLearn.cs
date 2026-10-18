// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI;

/// <summary>
/// MIDI learn back-end used by the value controls: when <see cref="UIHost.MidiLearn"/> is set, the right-click menu
/// of a control bound to a parameter offers "MIDI Learn" and "Forget MIDI assignment", and the control is outlined
/// while it waits for a MIDI controller.
/// </summary>
/// <remarks>
/// Implement it on top of your plugin's MIDI mapping (for VST3, <c>IMidiMapping</c> / <c>IMidiLearn</c> of the
/// controller; <c>PluginUI.NPlug</c> provides one), or use <see cref="MidiLearnService"/> for standalone applications
/// and tests. Members are called on the UI thread; raise <see cref="Changed"/> on the UI thread.
/// </remarks>
public interface IMidiLearnService
{
    /// <summary><c>true</c> while <paramref name="parameter"/> waits for a MIDI controller.</summary>
    /// <param name="parameter">A parameter.</param>
    /// <returns><c>true</c> if learning for this parameter.</returns>
    bool IsLearning(IParameter parameter);

    /// <summary>Starts learning for a parameter (cancels any other learn in progress).</summary>
    /// <param name="parameter">The parameter to assign to the next MIDI controller moved.</param>
    void BeginLearn(IParameter parameter);

    /// <summary>Cancels the learn in progress, if any.</summary>
    void CancelLearn();

    /// <summary>Removes the MIDI assignment of a parameter.</summary>
    /// <param name="parameter">A parameter.</param>
    void Forget(IParameter parameter);

    /// <summary>Short description of the assignment (e.g. "CC 74 · Ch 1"), or <c>null</c> if the parameter has none.</summary>
    /// <param name="parameter">A parameter.</param>
    /// <returns>The assignment text, or <c>null</c>.</returns>
    string? GetAssignment(IParameter parameter);

    /// <summary>Raised when a learn starts, ends, or an assignment changes.</summary>
    event Action? Changed;
}

/// <summary>
/// In-memory <see cref="IMidiLearnService"/>: feed it the MIDI controllers received by your application with
/// <see cref="ProcessController"/>; the learning parameter gets assigned, assigned parameters follow the controller.
/// </summary>
/// <example>
/// <code>
/// var midiLearn = new MidiLearnService();
/// host.MidiLearn = midiLearn;
/// // From your MIDI input (marshal to the UI thread with host.Post):
/// host.Post(() => midiLearn.ProcessController(channel, controller, value / 127.0));
/// </code>
/// </example>
public sealed class MidiLearnService : IMidiLearnService
{
    private readonly Dictionary<IParameter, (int Channel, int Controller)> _assignments = new(ReferenceEqualityComparer.Instance);
    private IParameter? _learning;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Parameter currently learning, or <c>null</c>.</summary>
    public IParameter? LearningParameter => _learning;

    /// <summary>Current assignments.</summary>
    public IReadOnlyDictionary<IParameter, (int Channel, int Controller)> Assignments => _assignments;

    /// <inheritdoc />
    public bool IsLearning(IParameter parameter) => ReferenceEquals(_learning, parameter);

    /// <inheritdoc />
    public void BeginLearn(IParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _learning = parameter;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void CancelLearn()
    {
        if (_learning is null) return;
        _learning = null;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Forget(IParameter parameter)
    {
        if (_assignments.Remove(parameter)) Changed?.Invoke();
    }

    /// <inheritdoc />
    public string? GetAssignment(IParameter parameter)
        => _assignments.TryGetValue(parameter, out var a) ? Describe(a.Channel, a.Controller) : null;

    /// <summary>Text of an assignment, e.g. "CC 74 · Ch 1".</summary>
    /// <param name="channel">MIDI channel, 0-based (negative: any channel, not shown).</param>
    /// <param name="controller">Controller number (0..127, or 128/129 for aftertouch and pitch bend).</param>
    /// <returns>The description.</returns>
    /// <remarks>Controller 128 is shown as "Aftertouch" and 129 as "Pitch Bend" (VST3 extended controller numbers).</remarks>
    public static string Describe(int channel, int controller)
    {
        string name = controller switch
        {
            128 => "Aftertouch",
            129 => "Pitch Bend",
            _ => "CC " + InvariantNumber.Format(controller, "0"),
        };
        return channel < 0 ? name : name + " · Ch " + InvariantNumber.Format(channel + 1, "0");
    }

    /// <summary>Assigns a controller to a parameter (replacing other parameters assigned to the same controller).</summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="channel">MIDI channel, 0-based (0..15).</param>
    /// <param name="controller">Controller number (0..127).</param>
    /// <exception cref="ArgumentOutOfRangeException">The channel or the controller is out of range.</exception>
    public void Assign(IParameter parameter, int channel, int controller)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 15);
        ArgumentOutOfRangeException.ThrowIfLessThan(controller, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(controller, 127);
        foreach (var other in _assignments.Where(kv => kv.Value == (channel, controller)).Select(kv => kv.Key).ToList()) _assignments.Remove(other);
        _assignments[parameter] = (channel, controller);
        Changed?.Invoke();
    }

    /// <summary>
    /// Handles a received MIDI controller (UI thread): completes a learn in progress, or moves the parameter assigned
    /// to it (as a complete edit gesture).
    /// </summary>
    /// <param name="channel">MIDI channel, 0-based.</param>
    /// <param name="controller">Controller number.</param>
    /// <param name="value">Controller value normalized to [0, 1].</param>
    /// <returns><c>true</c> if the controller was used.</returns>
    public bool ProcessController(int channel, int controller, double value)
    {
        if (_learning is { } learning)
        {
            _learning = null;
            Assign(learning, channel, controller);
            return true;
        }
        foreach (var (parameter, assignment) in _assignments)
        {
            if (assignment != (channel, controller)) continue;
            parameter.SetValue(Math.Clamp(value, 0.0, 1.0));
            return true;
        }
        return false;
    }
}
