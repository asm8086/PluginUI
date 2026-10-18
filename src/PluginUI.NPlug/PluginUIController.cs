// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using NPlug;
using NPlug.IO;

namespace PluginUI.NPlug;

/// <summary>
/// Base edit controller for plug-ins with a PluginUI editor: implements <see cref="IParameterEditSink"/> on top of
/// NPlug's concurrent edit support, creates the view on Windows only, sends the notes played on the editor to the
/// processor, receives the processor's shared state and provides MIDI learn.
/// </summary>
/// <typeparam name="TModel">Parameter model shared with the processor.</typeparam>
/// <remarks>
/// <para>
/// MIDI learn (<see cref="MidiLearn"/>, offered when <see cref="IsMidiLearnEnabled"/> is set): the controller
/// implements VST3 <c>IMidiLearn</c> and <c>IMidiMapping</c>;
/// learned assignments are saved with the controller state by <see cref="SaveState"/> and read back by
/// <see cref="RestoreState"/>. Derived classes that store their own controller data call the base method first.
/// </para>
/// <para>
/// Override <see cref="OnMessage"/> or <see cref="OnConnect"/> only with a call to the base method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class MyController : PluginUIController&lt;MyModel&gt;
/// {
///     protected override PluginEditorView CreateEditor() =&gt; new MyEditor(this);
/// }
/// </code>
/// </example>
public abstract class PluginUIController<TModel> : AudioController<TModel>, IParameterEditSink, IAudioControllerMidiLearn
    where TModel : AudioProcessorModel, new()
{
    private readonly NPlugMidiLearn _midiLearn;
    private object? _sharedState;

    /// <summary>Creates the controller.</summary>
    protected PluginUIController()
    {
        _midiLearn = new NPlugMidiLearn(() => Handler);
    }

    /// <summary>MIDI learn service (assign MIDI controllers to parameters from the editor's context menu).</summary>
    public NPlugMidiLearn MidiLearn => _midiLearn;

    /// <summary>
    /// When <c>true</c>, the editor's context menus offer MIDI learn. Default <c>false</c>: enable it for plug-ins with an
    /// event input (instruments, MIDI-controlled effects), since hosts send MIDI controllers only to those. Learned
    /// assignments apply whatever this setting.
    /// </summary>
    public bool IsMidiLearnEnabled { get; set; }

    /// <summary>
    /// Object shared by the processor (<see cref="PluginUIProcessor{TModel}.CreateSharedState"/>), or <c>null</c>
    /// until the components are connected (or if the processor shares nothing).
    /// </summary>
    public object? SharedState => _sharedState;

    /// <summary>Raised on the UI thread when <see cref="SharedState"/> becomes available.</summary>
    public event Action? SharedStateChanged;

    /// <summary>Typed access to <see cref="SharedState"/>.</summary>
    /// <typeparam name="T">Type of the shared object.</typeparam>
    /// <param name="state">The shared object.</param>
    /// <returns><c>true</c> if the processor shares an object of this type.</returns>
    public bool TryGetSharedState<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? state) where T : class
    {
        state = _sharedState as T;
        return state is not null;
    }

    /// <summary>Sends a note played on the editor to the processor (UI thread).</summary>
    /// <param name="note">The note event.</param>
    /// <returns><c>false</c> if the processor is not connected.</returns>
    public bool SendNote(UINoteEvent note) => PluginUIMessages.TrySendNote(Host, ConnectionPoint, note);

    /// <summary>Creates the editor view (called on Windows 10 1809+ only).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763")]
    protected abstract PluginEditorView? CreateEditor();

    /// <inheritdoc />
    protected override IAudioPluginView? CreateView()
        => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) ? CreateEditor() : null;

    /// <inheritdoc />
    /// <remarks>Without a host connection (tests, snapshots, standalone use) changes are kept in the model only.</remarks>
    protected override void OnParameterValueChanged(AudioParameter parameter, bool parameterValueChangedFromHost)
    {
        if (!parameterValueChangedFromHost && Handler is null) return;
        base.OnParameterValueChanged(parameter, parameterValueChangedFromHost);
    }

    /// <inheritdoc />
    protected override void OnConnect(IAudioConnectionPoint connectionPoint)
    {
        base.OnConnect(connectionPoint);
        PluginUIMessages.TrySend(Host, ConnectionPoint, PluginUIMessages.SharedStateRequest);
    }

    /// <inheritdoc />
    protected override void OnDisconnect(IAudioConnectionPoint connectionPoint)
    {
        base.OnDisconnect(connectionPoint);
        if (_sharedState is null) return;
        _sharedState = null;
        SharedStateChanged?.Invoke();
    }

    /// <inheritdoc />
    protected override void OnMessage(AudioMessage message)
    {
        if (message.Id == PluginUIMessages.SharedState)
        {
            if (message.Attributes.TryGetInt64(PluginUIMessages.HandleAttribute, out long handle)
                && SharedStateRegistry.Find(handle) is { } state
                && !ReferenceEquals(state, _sharedState))
            {
                _sharedState = state;
                SharedStateChanged?.Invoke();
            }
            return;
        }
        base.OnMessage(message);
    }

    /// <inheritdoc />
    /// <remarks>Writes the learned MIDI assignments; overrides call this first, then write their own data.</remarks>
    protected override void SaveState(PortableBinaryWriter writer)
    {
        base.SaveState(writer);
        _midiLearn.Save(writer);
    }

    /// <inheritdoc />
    /// <remarks>Reads the learned MIDI assignments; overrides call this first, then read their own data.</remarks>
    protected override void RestoreState(PortableBinaryReader reader)
    {
        base.RestoreState(reader);
        _midiLearn.Load(reader);
    }

    /// <inheritdoc />
    /// <remarks>Learned assignments first, then the fixed mappings declared with <c>SetMidiCCMapping</c>.</remarks>
    protected override bool TryGetMidiControllerAssignment(int busIndex, int channel, AudioMidiControllerNumber midiControllerNumber, out AudioParameterId id)
        => _midiLearn.TryGetAssignment(busIndex, channel, midiControllerNumber, out id)
           || base.TryGetMidiControllerAssignment(busIndex, channel, midiControllerNumber, out id);

    /// <summary>Live MIDI controller reported by the host (VST3 <c>IMidiLearn</c>, UI thread).</summary>
    /// <param name="busIndex">Event input bus.</param>
    /// <param name="channel">MIDI channel, 0-based.</param>
    /// <param name="midiCC">Controller number.</param>
    /// <returns><c>true</c> if a learn completed.</returns>
    protected virtual bool OnLiveMidiControllerInput(int busIndex, short channel, AudioMidiControllerNumber midiCC)
        => _midiLearn.OnLiveController(busIndex, channel, midiCC);

    bool IAudioControllerMidiLearn.TryOnLiveMidiControllerInput(int busIndex, short channel, AudioMidiControllerNumber midiCC)
        => OnLiveMidiControllerInput(busIndex, channel, midiCC);

    AudioProcessorModel IParameterEditSink.Model => Model;

    IAudioControllerHandler? IParameterEditSink.ControllerHandler => Handler;

    IMidiLearnService? IParameterEditSink.MidiLearn => IsMidiLearnEnabled ? _midiLearn : null;

    bool IParameterEditSink.TrySendNote(UINoteEvent note) => SendNote(note);

    void IParameterEditSink.BeginEdit(AudioParameter parameter)
    {
        // No host connection yet, or a nested gesture on the same parameter: ignore rather than throw.
        if (Handler is null || IsEditingParameter(parameter)) return;
        BeginEditParameter(parameter);
    }

    void IParameterEditSink.PerformEdit(AudioParameter parameter, double normalizedValue)
    {
        if (Handler is null)
        {
            // Not connected (tests, standalone): just update the model.
            parameter.NormalizedValue = normalizedValue;
            return;
        }
        if (!IsEditingParameter(parameter)) return;
        // Setting the value raises the model event, which NPlug forwards to the host (performEdit).
        parameter.NormalizedValue = normalizedValue;
    }

    void IParameterEditSink.EndEdit(AudioParameter parameter)
    {
        if (IsEditingParameter(parameter)) EndEditParameter(parameter);
    }

    bool IParameterEditSink.IsEditing(AudioParameter parameter) => IsEditingParameter(parameter);
}
