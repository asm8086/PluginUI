// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using NPlug;
using NPlug.IO;

namespace PluginUI.NPlug;

/// <summary>
/// <see cref="IMidiLearnService"/> on top of the VST3 MIDI mapping of a controller: the host turns the assigned MIDI
/// controllers into parameter changes (<c>IMidiMapping</c>), learning listens to the live controllers the host reports
/// (<c>IMidiLearn::onLiveMIDIControllerInput</c>). Created by <see cref="PluginUIController{TModel}"/>, which saves the
/// assignments with the controller state.
/// </summary>
/// <remarks>
/// Learning needs a host that reports live MIDI controllers (VST3 <c>IMidiLearn</c>, e.g. Cubase and Nuendo); in
/// other hosts the learn entry stays pending until cancelled and the host's own MIDI learn remains available.
/// Assignments are per MIDI channel unless <see cref="LearnOnAllChannels"/> is set, and take precedence over the
/// fixed mappings declared with <c>SetMidiCCMapping</c>. All members run on the UI thread.
/// </remarks>
public sealed class NPlugMidiLearn : IMidiLearnService
{
    private const uint StateMagic = 0x4C495550; // "PUIL"
    private const int StateVersion = 1;

    private readonly Func<IAudioControllerHandler?> _handler;
    private readonly Dictionary<(int Channel, int Controller), AudioParameterId> _byController = new();
    private AudioParameterId? _learning;

    /// <summary>Creates the service.</summary>
    /// <param name="handler">Returns the host handler (used to notify mapping changes); may return <c>null</c>.</param>
    public NPlugMidiLearn(Func<IAudioControllerHandler?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>When <c>true</c>, a learned controller applies to every MIDI channel (default: only the channel it came from).</summary>
    public bool LearnOnAllChannels { get; set; }

    /// <summary>Parameter waiting for a controller, or <c>null</c>.</summary>
    public AudioParameterId? LearningParameter => _learning;

    /// <summary>Learned assignments: (channel, controller) → parameter; channel −1 means every channel.</summary>
    public IReadOnlyDictionary<(int Channel, int Controller), AudioParameterId> Assignments => _byController;

    private static bool TryGetId(IParameter parameter, out AudioParameterId id)
    {
        if (parameter is NPlugParameter p)
        {
            // Reading the id touches no Windows API (the class is annotated for its editor-side members).
#pragma warning disable CA1416
            id = p.Id;
#pragma warning restore CA1416
            return true;
        }
        id = default;
        return false;
    }

    /// <inheritdoc />
    public bool IsLearning(IParameter parameter) => _learning is { } id && TryGetId(parameter, out var pid) && pid == id;

    /// <inheritdoc />
    public void BeginLearn(IParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (!TryGetId(parameter, out var id)) return; // not an NPlug parameter: nothing the host could map
        _learning = id;
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
        if (TryGetId(parameter, out var id)) Forget(id);
    }

    /// <summary>Removes every assignment of a parameter.</summary>
    /// <param name="parameter">The parameter id.</param>
    public void Forget(AudioParameterId parameter)
    {
        int removed = 0;
        foreach (var key in _byController.Where(kv => kv.Value == parameter).Select(kv => kv.Key).ToList())
        {
            _byController.Remove(key);
            removed++;
        }
        if (removed > 0) MappingChanged();
    }

    /// <inheritdoc />
    public string? GetAssignment(IParameter parameter)
    {
        if (!TryGetId(parameter, out var id)) return null;
        string? text = null;
        foreach (var ((channel, controller), pid) in _byController.OrderBy(kv => kv.Key.Channel).ThenBy(kv => kv.Key.Controller))
        {
            if (pid != id) continue;
            string one = Describe(channel, controller);
            text = text is null ? one : text + ", " + one;
        }
        return text;
    }

    /// <summary>Text of an assignment ("CC 74 · Ch 1", "Pitch Bend", channel −1 = every channel).</summary>
    /// <param name="channel">MIDI channel, 0-based, or −1.</param>
    /// <param name="controller">Controller number.</param>
    /// <returns>The description.</returns>
    public static string Describe(int channel, int controller)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        string name = controller switch
        {
            (int)AudioMidiControllerNumber.AfterTouch => "Aftertouch",
            (int)AudioMidiControllerNumber.PitchBend => "Pitch Bend",
            _ => "CC " + controller.ToString(invariant),
        };
        return channel < 0 ? name : name + " · Ch " + (channel + 1).ToString(invariant);
    }

    /// <summary>Assigns a controller to a parameter, replacing the previous assignment of that controller.</summary>
    /// <param name="parameter">The parameter id.</param>
    /// <param name="channel">MIDI channel, 0-based, or −1 for every channel.</param>
    /// <param name="controller">Controller number: 0..127, 128 (aftertouch) or 129 (pitch bend).</param>
    /// <exception cref="ArgumentOutOfRangeException">The channel or the controller is out of range.</exception>
    public void Assign(AudioParameterId parameter, int channel, int controller)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 15);
        ArgumentOutOfRangeException.ThrowIfLessThan(controller, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(controller, (int)AudioMidiControllerNumber.PitchBend);
        _byController[(channel, controller)] = parameter;
        MappingChanged();
    }

    /// <summary>Removes every assignment.</summary>
    public void Clear()
    {
        if (_byController.Count == 0) return;
        _byController.Clear();
        MappingChanged();
    }

    /// <summary>
    /// Live controller reported by the host: completes a learn in progress.
    /// </summary>
    /// <param name="busIndex">Event input bus.</param>
    /// <param name="channel">MIDI channel, 0-based.</param>
    /// <param name="controller">Controller number.</param>
    /// <returns><c>true</c> if the controller was used (the assignment changed).</returns>
    public bool OnLiveController(int busIndex, int channel, AudioMidiControllerNumber controller)
    {
        if (_learning is not { } id || busIndex != 0 || (int)controller > (int)AudioMidiControllerNumber.PitchBend || (uint)channel > 15) return false;
        _learning = null;
        int key = LearnOnAllChannels ? -1 : channel;
        // One controller drives one parameter, and a parameter keeps a single learned controller.
        // A learn on every channel also replaces the channel-specific assignments of that controller.
        foreach (var old in _byController.Where(kv => kv.Value == id || (key == -1 && kv.Key.Controller == (int)controller)).Select(kv => kv.Key).ToList())
        {
            _byController.Remove(old);
        }
        _byController[(key, (int)controller)] = id;
        MappingChanged();
        return true;
    }

    /// <summary>Parameter assigned to a controller on a channel (IMidiMapping query).</summary>
    /// <param name="busIndex">Event input bus.</param>
    /// <param name="channel">MIDI channel, 0-based.</param>
    /// <param name="controller">Controller number.</param>
    /// <param name="parameter">The assigned parameter.</param>
    /// <returns><c>true</c> if a learned assignment exists.</returns>
    public bool TryGetAssignment(int busIndex, int channel, AudioMidiControllerNumber controller, out AudioParameterId parameter)
    {
        parameter = default;
        if (busIndex != 0) return false;
        return _byController.TryGetValue((channel, (int)controller), out parameter)
            || _byController.TryGetValue((-1, (int)controller), out parameter);
    }

    /// <summary>Writes the assignments (controller state).</summary>
    /// <param name="writer">The writer.</param>
    public void Save(PortableBinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteUInt32(StateMagic);
        writer.WriteInt32(StateVersion);
        writer.WriteInt32(_byController.Count);
        foreach (var ((channel, controller), id) in _byController)
        {
            writer.WriteInt32(id.Value);
            writer.WriteInt16((short)channel);
            writer.WriteInt16((short)controller);
        }
    }

    /// <summary>
    /// Reads assignments written by <see cref="Save"/>. A state without them (saved by an older version of the plug-in)
    /// leaves the service empty and, when the stream can seek, the stream where it was.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns><c>true</c> if assignments were found.</returns>
    public bool Load(PortableBinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var stream = reader.Stream;
        long start = stream.CanSeek ? stream.Position : -1;
        bool hadAny = _byController.Count > 0;
        _byController.Clear();
        _learning = null;
        try
        {
            if (stream.CanSeek && stream.Length - start < 12) throw new EndOfStreamException();
            if (reader.ReadUInt32() != StateMagic || reader.ReadInt32() != StateVersion) throw new InvalidDataException();
            int count = reader.ReadInt32();
            if (count is < 0 or > 16 * 130 + 130) throw new InvalidDataException();
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                int channel = reader.ReadInt16();
                int controller = reader.ReadInt16();
                if (channel is >= -1 and <= 15 && controller is >= 0 and <= (int)AudioMidiControllerNumber.PitchBend)
                {
                    _byController[(channel, controller)] = new AudioParameterId(id);
                }
            }
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException)
        {
            _byController.Clear();
            if (start >= 0) stream.Position = start;
            if (hadAny) MappingChanged();
            return false;
        }
        MappingChanged();
        return true;
    }

    private void MappingChanged()
    {
        _handler()?.RestartComponent(AudioRestartFlags.MidiCCAssignmentChanged);
        Changed?.Invoke();
    }
}
