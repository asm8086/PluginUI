// 2026/11/08 14:03:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst3;

/// <summary>
/// A VST 3 plug-in instance: component, audio processor and edit controller, connected as a DAW connects them.
/// Created by <see cref="Vst3Module.CreatePlugin(Guid)"/>.
/// </summary>
public sealed unsafe class Vst3Plugin : PluginInstance, IVst3HostCallbacks
{
    // MediaTypes, BusDirections, IoModes, ProcessModes, SymbolicSampleSizes
    private const int Audio = 0, EventMedia = 1, Input = 0, Output = 1, Sample32 = 0;
    private const int MainBus = 0;
    private const uint DefaultActiveFlag = 1;
    private const int RestartIoChanged = 1 << 1, RestartParamValuesChanged = 1 << 2, RestartLatencyChanged = 1 << 3, RestartParamTitlesChanged = 1 << 4;

    private readonly Vst3Module _module;
    private void* _component;
    private void* _processor;
    private void* _controller;
    private void* _componentConnection;
    private void* _controllerConnection;
    private void* _midiMapping;
    private readonly bool _singleComponent;
    private readonly ComponentHandler _handler;
    private PlugFrame? _frame;

    private PluginBus[] _inputs = [];
    private PluginBus[] _outputs = [];
    private int _eventInputs;
    private int _eventOutputs;
    private PluginParameter[] _parameters = [];
    private Dictionary<uint, PluginParameter> _parameterById = new();

    private readonly ParameterChanges _inChanges = new();
    private readonly ParameterChanges _outChanges = new();
    private readonly EventList _inEvents = new();
    private readonly EventList _outEvents = new();
    private readonly ParameterQueue _toUi = new(4096);
    private ProcessDataNative* _data;
    private ProcessContextNative* _context;
    private bool _active;
    private Vst3Editor? _editor;

    internal Vst3Plugin(Vst3Module module, Vst3ClassInfo info)
    {
        _module = module;
        Info = info;
        _handler = new ComponentHandler(this);
        try
        {
            Vst3Module.Step($"creating the component of '{info.Name}'");
            _component = module.CreateInstance(info.ClassId, Tuid.IComponent);
            if (_component is null) throw new Vst3Exception($"Cannot create '{info.Name}'.");
            Vst3Module.Step("initializing the component");
            if (PluginBaseInitialize(_component) != Vst3Result.Ok) throw new Vst3Exception($"'{info.Name}' failed to initialize.");

            _processor = Com.As(_component, Tuid.IAudioProcessor);
            if (_processor is null) throw new Vst3Exception($"'{info.Name}' is not an audio processor.");

            Guid controllerId;
            bool hasControllerClass = ((delegate* unmanaged[MemberFunction]<void*, Guid*, int>)Com.Slot(_component, 5))(_component, &controllerId) == Vst3Result.Ok
                && controllerId != Guid.Empty;
            if (hasControllerClass)
            {
                Vst3Module.Step("creating and initializing the edit controller");
                _controller = module.CreateInstance(controllerId, Tuid.IEditController);
                if (_controller is not null && PluginBaseInitialize(_controller) != Vst3Result.Ok)
                {
                    Com.Release(_controller);
                    _controller = null;
                }
            }
            if (_controller is null)
            {
                _controller = Com.As(_component, Tuid.IEditController);
                _singleComponent = _controller is not null;
            }
            if (_controller is not null)
            {
                Vst3Module.Step("connecting the component and the controller");
                ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_controller, 16))(_controller, _handler.Pointer);
                if (!_singleComponent)
                {
                    _componentConnection = Com.As(_component, Tuid.IConnectionPoint);
                    _controllerConnection = Com.As(_controller, Tuid.IConnectionPoint);
                    if (_componentConnection is not null && _controllerConnection is not null)
                    {
                        ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_componentConnection, 3))(_componentConnection, _controllerConnection);
                        ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_controllerConnection, 3))(_controllerConnection, _componentConnection);
                    }
                }
                // The controller starts from the processor's state.
                Vst3Module.Step("passing the processor state to the controller");
                byte[] componentState = ReadState(_component, 13);
                WriteState(_controller, 5, componentState);
                _midiMapping = Com.As(_controller, Tuid.IMidiMapping);
            }

            Vst3Module.Step("reading the buses");
            ActivateBuses();
            Vst3Module.Step("reading the parameters");
            ReadParameters();
        }
        catch
        {
            Dispose(true);
            throw;
        }
    }

    /// <summary>Class of the plug-in.</summary>
    public Vst3ClassInfo Info { get; }

    /// <inheritdoc />
    public override string Name => Info.Name;

    /// <inheritdoc />
    public override string Vendor => Info.Vendor;

    /// <inheritdoc />
    public override PluginFormat Format => PluginFormat.Vst3;

    /// <inheritdoc />
    public override bool IsInstrument => Info.SubCategories.Contains("Instrument", StringComparison.OrdinalIgnoreCase)
        || (_eventInputs > 0 && _inputs.Length == 0);

    /// <inheritdoc />
    public override IReadOnlyList<PluginBus> InputBuses => _inputs;

    /// <inheritdoc />
    public override IReadOnlyList<PluginBus> OutputBuses => _outputs;

    /// <inheritdoc />
    public override int EventInputCount => _eventInputs;

    /// <inheritdoc />
    public override int EventOutputCount => _eventOutputs;

    /// <inheritdoc />
    public override IReadOnlyList<PluginParameter> Parameters => _parameters;

    /// <inheritdoc />
    public override int LatencySamples => _processor is null ? 0 : (int)((delegate* unmanaged[MemberFunction]<void*, uint>)Com.Slot(_processor, 6))(_processor);

    /// <inheritdoc />
    public override int TailSamples
    {
        get
        {
            if (_processor is null) return 0;
            uint tail = ((delegate* unmanaged[MemberFunction]<void*, uint>)Com.Slot(_processor, 10))(_processor);
            return tail >= int.MaxValue ? int.MaxValue : (int)tail;
        }
    }

    /// <inheritdoc />
    public override bool HasEditor => _controller is not null;

    private int PluginBaseInitialize(void* obj)
        => ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(obj, 3))(obj, _module.HostContext.Pointer);

    // ---------------------------------------------------------------- buses and parameters

    private void ActivateBuses()
    {
        _inputs = ReadBuses(Audio, Input);
        _outputs = ReadBuses(Audio, Output);
        _eventInputs = ((delegate* unmanaged[MemberFunction]<void*, int, int, int>)Com.Slot(_component, 7))(_component, EventMedia, Input);
        for (int i = 0; i < _eventInputs; i++) ActivateBus(EventMedia, Input, i, true);
        _eventOutputs = ((delegate* unmanaged[MemberFunction]<void*, int, int, int>)Com.Slot(_component, 7))(_component, EventMedia, Output);
        for (int i = 0; i < _eventOutputs; i++) ActivateBus(EventMedia, Output, i, true);
    }

    private PluginBus[] ReadBuses(int media, int direction)
    {
        int count = ((delegate* unmanaged[MemberFunction]<void*, int, int, int>)Com.Slot(_component, 7))(_component, media, direction);
        var buses = new List<PluginBus>(count);
        for (int i = 0; i < count; i++)
        {
            BusInfoNative info;
            if (((delegate* unmanaged[MemberFunction]<void*, int, int, int, BusInfoNative*, int>)Com.Slot(_component, 8))(_component, media, direction, i, &info) != Vst3Result.Ok) continue;
            buses.Add(new PluginBus(FixedText.Utf16(info.Name, 128), info.ChannelCount, info.BusType != MainBus, (info.Flags & DefaultActiveFlag) != 0));
            // Every audio bus is activated: auxiliary inputs (side chain) receive silence unless the host fills them.
            ActivateBus(media, direction, i, true);
        }
        return buses.ToArray();
    }

    private void ActivateBus(int media, int direction, int index, bool state)
        => ((delegate* unmanaged[MemberFunction]<void*, int, int, int, byte, int>)Com.Slot(_component, 10))(_component, media, direction, index, state ? (byte)1 : (byte)0);

    private void ReadParameters()
    {
        if (_controller is null)
        {
            _parameters = [];
            _parameterById = new();
            return;
        }
        int count = ((delegate* unmanaged[MemberFunction]<void*, int>)Com.Slot(_controller, 8))(_controller);
        var list = new List<PluginParameter>(count);
        for (int i = 0; i < count; i++)
        {
            ParameterInfoNative p;
            if (((delegate* unmanaged[MemberFunction]<void*, int, ParameterInfoNative*, int>)Com.Slot(_controller, 9))(_controller, i, &p) != Vst3Result.Ok) continue;
            list.Add(new PluginParameter(p.Id, FixedText.Utf16(p.Title, 128), FixedText.Utf16(p.ShortTitle, 128), FixedText.Utf16(p.Units, 128),
                p.StepCount, p.DefaultNormalizedValue, p.UnitId, (ParameterFlags)p.Flags));
        }
        _parameters = list.ToArray();
        _parameterById = _parameters.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Asks the plug-in for a channel layout (each entry: channels of one bus). Must be called before <see cref="PluginInstance.Prepare"/>.</summary>
    /// <returns><c>true</c> if the plug-in accepted it; the buses are read again either way.</returns>
    public bool TrySetChannelLayout(int[] inputChannels, int[] outputChannels)
    {
        ArgumentNullException.ThrowIfNull(inputChannels);
        ArgumentNullException.ThrowIfNull(outputChannels);
        ulong* ins = stackalloc ulong[Math.Max(1, inputChannels.Length)];
        ulong* outs = stackalloc ulong[Math.Max(1, outputChannels.Length)];
        for (int i = 0; i < inputChannels.Length; i++) ins[i] = SpeakerArrangement(inputChannels[i]);
        for (int i = 0; i < outputChannels.Length; i++) outs[i] = SpeakerArrangement(outputChannels[i]);
        bool ok = ((delegate* unmanaged[MemberFunction]<void*, ulong*, int, ulong*, int, int>)Com.Slot(_processor, 3))(_processor, ins, inputChannels.Length, outs, outputChannels.Length) == Vst3Result.Ok;
        _inputs = ReadBuses(Audio, Input);
        _outputs = ReadBuses(Audio, Output);
        return ok;
    }

    /// <summary>VST 3 speaker arrangement for a channel count (mono, stereo, then the first n speakers).</summary>
    public static ulong SpeakerArrangement(int channels) => channels switch
    {
        <= 0 => 0,
        1 => 1UL << 19, // kSpeakerM
        2 => 0b11, // L R
        6 => 0b111111, // 5.1: L R C LFE Ls Rs
        _ => channels >= 64 ? ulong.MaxValue : (1UL << channels) - 1,
    };

    // ---------------------------------------------------------------- parameters

    /// <inheritdoc />
    public override double GetParameter(uint id)
        => _controller is null ? 0.0 : ((delegate* unmanaged[MemberFunction]<void*, uint, double>)Com.Slot(_controller, 14))(_controller, id);

    /// <inheritdoc />
    protected override void OnSetParameterUi(uint id, double normalized)
    {
        if (_controller is not null) ((delegate* unmanaged[MemberFunction]<void*, uint, double, int>)Com.Slot(_controller, 15))(_controller, id, normalized);
    }

    /// <inheritdoc />
    public override string FormatParameter(uint id, double normalized)
    {
        if (_controller is null) return normalized.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        char* text = stackalloc char[128];
        text[0] = '\0';
        int hr = ((delegate* unmanaged[MemberFunction]<void*, uint, double, char*, int>)Com.Slot(_controller, 10))(_controller, id, normalized, text);
        string value = hr == Vst3Result.Ok ? FixedText.Utf16(text, 128) : normalized.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return _parameterById.TryGetValue(id, out var p) && p.Units.Length > 0 && !value.EndsWith(p.Units, StringComparison.OrdinalIgnoreCase)
            ? $"{value} {p.Units}" : value;
    }

    /// <inheritdoc />
    public override bool TryParseParameter(uint id, string text, out double normalized)
    {
        normalized = 0;
        if (_controller is null || text is null) return false;
        string trimmed = text.Trim();
        if (_parameterById.TryGetValue(id, out var p) && p.Units.Length > 0 && trimmed.EndsWith(p.Units, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^p.Units.Length].Trim();
        foreach (string candidate in trimmed == text.Trim() ? [trimmed] : new[] { trimmed, text.Trim() })
        {
            double value;
            fixed (char* s = candidate)
            {
                if (((delegate* unmanaged[MemberFunction]<void*, uint, char*, double*, int>)Com.Slot(_controller, 11))(_controller, id, s, &value) == Vst3Result.Ok
                    && double.IsFinite(value))
                {
                    normalized = Math.Clamp(value, 0.0, 1.0);
                    return true;
                }
            }
        }
        // Fallback: a plain number converted by the plug-in.
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double plain))
        {
            normalized = Math.Clamp(PlainToNormalized(id, plain), 0.0, 1.0);
            return true;
        }
        return false;
    }

    /// <summary>Plain value (Hz, dB…) of a normalized value.</summary>
    public double NormalizedToPlain(uint id, double normalized)
        => _controller is null ? normalized : ((delegate* unmanaged[MemberFunction]<void*, uint, double, double>)Com.Slot(_controller, 12))(_controller, id, normalized);

    /// <summary>Normalized value of a plain value.</summary>
    public double PlainToNormalized(uint id, double plain)
        => _controller is null ? plain : ((delegate* unmanaged[MemberFunction]<void*, uint, double, double>)Com.Slot(_controller, 13))(_controller, id, plain);

    // ---------------------------------------------------------------- processing

    /// <inheritdoc />
    protected override void OnPrepare(double sampleRate, int maxBlockSize, bool realtime)
    {
        Vst3Module.Step("setting up the processing");
        var setup = new ProcessSetup { ProcessMode = realtime ? 0 : 2, SymbolicSampleSize = Sample32, MaxSamplesPerBlock = maxBlockSize, SampleRate = sampleRate };
        if (((delegate* unmanaged[MemberFunction]<void*, int, int>)Com.Slot(_processor, 5))(_processor, Sample32) != Vst3Result.Ok)
            throw new Vst3Exception($"'{Name}' does not process 32-bit samples.");
        if (((delegate* unmanaged[MemberFunction]<void*, ProcessSetup*, int>)Com.Slot(_processor, 7))(_processor, &setup) != Vst3Result.Ok)
            throw new Vst3Exception($"'{Name}' refused the processing setup ({sampleRate} Hz, {maxBlockSize} samples).");
        BuildProcessData(realtime);
        Vst3Module.Step("activating");
        ((delegate* unmanaged[MemberFunction]<void*, byte, int>)Com.Slot(_component, 11))(_component, 1);
        ((delegate* unmanaged[MemberFunction]<void*, byte, int>)Com.Slot(_processor, 8))(_processor, 1);
        _active = true;
    }

    private void BuildProcessData(bool realtime)
    {
        FreeProcessData();
        int ins = _inputs.Length, outs = _outputs.Length;
        int channels = InputChannelCount + OutputChannelCount;
        nuint size = (nuint)(sizeof(ProcessDataNative) + sizeof(ProcessContextNative) + (ins + outs) * sizeof(AudioBusBuffersNative) + channels * sizeof(void*));
        byte* block = (byte*)NativeMemory.AllocZeroed(size);
        _data = (ProcessDataNative*)block;
        _context = (ProcessContextNative*)(block + sizeof(ProcessDataNative));
        var buses = (AudioBusBuffersNative*)(block + sizeof(ProcessDataNative) + sizeof(ProcessContextNative));
        var pointers = (void**)(buses + ins + outs);
        int input = 0, output = 0;
        for (int b = 0; b < ins; b++)
        {
            buses[b].NumChannels = _inputs[b].ChannelCount;
            buses[b].ChannelBuffers = pointers;
            for (int c = 0; c < _inputs[b].ChannelCount; c++) *pointers++ = InputPointer(input++);
        }
        for (int b = 0; b < outs; b++)
        {
            buses[ins + b].NumChannels = _outputs[b].ChannelCount;
            buses[ins + b].ChannelBuffers = pointers;
            for (int c = 0; c < _outputs[b].ChannelCount; c++) *pointers++ = OutputPointer(output++);
        }
        _data->ProcessMode = realtime ? 0 : 2;
        _data->SymbolicSampleSize = Sample32;
        _data->NumInputs = ins;
        _data->NumOutputs = outs;
        _data->Inputs = ins > 0 ? buses : null;
        _data->Outputs = outs > 0 ? buses + ins : null;
        _data->InputParameterChanges = _inChanges.Pointer;
        _data->OutputParameterChanges = _outChanges.Pointer;
        _data->InputEvents = _inEvents.Pointer;
        _data->OutputEvents = _outEvents.Pointer;
        _data->ProcessContext = _context;
    }

    private void FreeProcessData()
    {
        NativeMemory.Free(_data);
        _data = null;
        _context = null;
    }

    /// <inheritdoc />
    public override void AddMidiEvent(in MidiEvent e)
    {
        int offset = Math.Max(0, e.SampleOffset);
        switch (e.Type)
        {
            case 0x80:
            case 0x90:
            {
                bool on = e.Type == 0x90 && e.Data2 > 0;
                var ev = new EventNative
                {
                    SampleOffset = offset,
                    Flags = EventNative.IsLive,
                    Type = on ? EventNative.NoteOnType : EventNative.NoteOffType,
                    Channel = (short)e.Channel,
                    Pitch = e.Data1,
                };
                if (on)
                {
                    ev.Float1 = 0f;              // tuning
                    ev.Float2 = e.Data2 / 127f;  // velocity
                    ev.Int1 = 0;                 // length
                    ev.Int2 = -1;                // noteId
                }
                else
                {
                    // NoteOffEvent: channel, pitch, velocity (28), noteId (32), tuning (36)
                    ev.Float1 = e.Data2 / 127f;
                    *(int*)((byte*)&ev + 32) = -1;
                    *(float*)((byte*)&ev + 36) = 0f;
                }
                _inEvents.Add(ev);
                break;
            }
            case 0xA0:
            {
                var ev = new EventNative { SampleOffset = offset, Flags = EventNative.IsLive, Type = EventNative.PolyPressureType, Channel = (short)e.Channel, Pitch = e.Data1, Float1 = e.Data2 / 127f };
                *(int*)((byte*)&ev + 32) = -1;
                _inEvents.Add(ev);
                break;
            }
            case 0xB0:
                MapController(offset, e.Channel, e.Data1, e.Data2 / 127.0);
                break;
            case 0xD0:
                MapController(offset, e.Channel, 128, e.Data1 / 127.0); // kAfterTouch
                break;
            case 0xE0:
                MapController(offset, e.Channel, 129, ((e.Data2 << 7) | e.Data1) / 16383.0); // kPitchBend
                break;
        }
    }

    /// <summary>Converts an event the plug-in produced to a MIDI message of <see cref="PluginInstance.OutputMidi"/>.</summary>
    private void CollectOutputEvent(ref EventNative ev)
    {
        int offset = ev.SampleOffset;
        static byte Seven(float v) => (byte)Math.Clamp((int)MathF.Round(v * 127f), 0, 127);
        switch (ev.Type)
        {
            case EventNative.NoteOnType:
            {
                byte velocity = Seven(ev.Float2);
                AddOutputMidi(new MidiEvent(offset, (byte)(0x90 | (ev.Channel & 15)), (byte)(ev.Pitch & 0x7F), velocity == 0 ? (byte)1 : velocity));
                break;
            }
            case EventNative.NoteOffType:
                AddOutputMidi(new MidiEvent(offset, (byte)(0x80 | (ev.Channel & 15)), (byte)(ev.Pitch & 0x7F), Seven(ev.Float1)));
                break;
            case EventNative.PolyPressureType:
                AddOutputMidi(new MidiEvent(offset, (byte)(0xA0 | (ev.Channel & 15)), (byte)(ev.Pitch & 0x7F), Seven(ev.Float1)));
                break;
            case EventNative.LegacyMidiCcOutType:
            {
                // controlNumber: 0..127 controllers, 128 channel pressure, 129 pitch bend, 130 program change, 131 poly pressure.
                byte channel = (byte)(ev.CcChannel & 15), value = (byte)(ev.CcValue & 0x7F), value2 = (byte)(ev.CcValue2 & 0x7F);
                int control = ev.CcControlNumber;
                if (control < 128) AddOutputMidi(new MidiEvent(offset, (byte)(0xB0 | channel), (byte)control, value));
                else if (control == 128) AddOutputMidi(new MidiEvent(offset, (byte)(0xD0 | channel), value, 0));
                else if (control == 129) AddOutputMidi(new MidiEvent(offset, (byte)(0xE0 | channel), value, value2));
                else if (control == 130) AddOutputMidi(new MidiEvent(offset, (byte)(0xC0 | channel), value, 0));
                else if (control == 131) AddOutputMidi(new MidiEvent(offset, (byte)(0xA0 | channel), value, value2));
                break;
            }
        }
    }

    private void MapController(int offset, int channel, int controller, double value)
    {
        if (_midiMapping is null) return;
        uint id;
        if (((delegate* unmanaged[MemberFunction]<void*, int, short, short, uint*, int>)Com.Slot(_midiMapping, 3))(_midiMapping, 0, (short)channel, (short)controller, &id) == Vst3Result.Ok)
        {
            _inChanges.Add(id, offset, value);
            _toUi.Enqueue(id, value);
        }
    }

    /// <inheritdoc />
    protected override void OnProcess(int frames, ParameterQueue pending)
    {
        while (pending.TryDequeue(out uint id, out double value)) _inChanges.Add(id, 0, value);

        var ctx = _context;
        ctx->State = ProcessContextNative.TempoValid | ProcessContextNative.TimeSigValid | ProcessContextNative.ProjectTimeMusicValid
            | ProcessContextNative.ContTimeValid | (IsPlaying ? ProcessContextNative.Playing : 0);
        ctx->SampleRate = SampleRate;
        ctx->ProjectTimeSamples = SamplePosition;
        ctx->ContinousTimeSamples = SamplePosition;
        ctx->Tempo = Tempo;
        ctx->ProjectTimeMusic = SamplePosition / SampleRate * Tempo / 60.0;
        ctx->TimeSigNumerator = TimeSignature.Numerator;
        ctx->TimeSigDenominator = TimeSignature.Denominator;

        _data->NumSamples = frames;
        for (int b = 0; b < _data->NumOutputs; b++) _data->Outputs[b].SilenceFlags = 0;
        for (int b = 0; b < _data->NumInputs; b++) _data->Inputs[b].SilenceFlags = 0;

        ((delegate* unmanaged[MemberFunction]<void*, ProcessDataNative*, int>)Com.Slot(_processor, 9))(_processor, _data);

        for (int i = 0; i < _outChanges.Count; i++)
        {
            var queue = _outChanges[i];
            if (queue.Count > 0) _toUi.Enqueue(queue.Id, queue.ValueAt(queue.Count - 1));
        }
        for (int i = 0; i < _outEvents.Count; i++) CollectOutputEvent(ref _outEvents[i]);
        _inChanges.Clear();
        _outChanges.Clear();
        _inEvents.Clear();
        _outEvents.Clear();
    }

    /// <inheritdoc />
    public override void Idle()
    {
        while (_toUi.TryDequeue(out uint id, out double value))
        {
            if (_controller is not null) ((delegate* unmanaged[MemberFunction]<void*, uint, double, int>)Com.Slot(_controller, 15))(_controller, id, value);
            RaiseParameterChanged(id, value);
        }
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        if (!_active) return;
        _active = false;
        ((delegate* unmanaged[MemberFunction]<void*, byte, int>)Com.Slot(_processor, 8))(_processor, 0);
        ((delegate* unmanaged[MemberFunction]<void*, byte, int>)Com.Slot(_component, 11))(_component, 0);
    }

    // ---------------------------------------------------------------- state

    private static byte[] ReadState(void* obj, int slot)
    {
        var stream = new BStream();
        try
        {
            int hr = ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(obj, slot))(obj, stream.Pointer);
            return hr == Vst3Result.Ok ? stream.ToArray() : [];
        }
        finally
        {
            stream.Release();
        }
    }

    private static bool WriteState(void* obj, int slot, ReadOnlySpan<byte> state, string? fileName = null) => WriteState(obj, slot, state, fileName, out _);

    // A project state (graph files) says so in its attributes, as DAWs do; a preset gives its file name.
    private static bool WriteState(void* obj, int slot, ReadOnlySpan<byte> state, string? fileName, out string details)
    {
        var stream = new BStream(state) { FileName = fileName is null ? string.Empty : Path.GetFileNameWithoutExtension(fileName), StateType = fileName is null ? "Project" : null };
        try
        {
            int hr = ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(obj, slot))(obj, stream.Pointer);
            details = $"result 0x{hr:X8}, {stream.BytesRead} of {stream.Length} bytes read"
                + (stream.MissingInterfaces.Count > 0 ? ", asked for " + string.Join(", ", stream.MissingInterfaces) : string.Empty);
            return Accepted(hr, stream.BytesRead);
        }
        finally
        {
            stream.Release();
        }
    }

    /// <summary>
    /// Whether a plug-in took a state. Some plug-ins (Waves shells among them) read the whole state, apply it and still
    /// answer <c>kResultFalse</c>; DAWs ignore that answer, so a "false" after reading is taken as a success. A plug-in
    /// that reads nothing, or answers an error code, refuses the state.
    /// </summary>
    internal static bool Accepted(int result, long bytesRead) => result == Vst3Result.Ok || (result == Vst3Result.False && bytesRead > 0);

    /// <summary>State of the processor component (<c>IComponent::getState</c>).</summary>
    public byte[] GetComponentState() => ReadState(_component, 13);

    /// <summary>State of the edit controller (<c>IEditController::getState</c>), empty when there is none.</summary>
    public byte[] GetControllerState() => _controller is null || _singleComponent ? [] : ReadState(_controller, 7);

    /// <summary>Restores the processor and controller states (as in a .vstpreset).</summary>
    /// <param name="component">State of the processor component.</param>
    /// <param name="controller">State of the edit controller.</param>
    /// <param name="fileName">The preset file the states come from; null for a project state (graph files).</param>
    public void SetComponentAndControllerState(ReadOnlySpan<byte> component, ReadOnlySpan<byte> controller, string? fileName = null)
    {
        if (!component.IsEmpty)
        {
            if (!WriteState(_component, 12, component, fileName, out string details)) throw new Vst3Exception($"'{Name}' refused the state ({details}).");
            if (_controller is not null) WriteState(_controller, 5, component, fileName);
        }
        if (!controller.IsEmpty && _controller is not null && !_singleComponent) WriteState(_controller, 6, controller, fileName);
    }

    /// <inheritdoc />
    /// <remarks>A .vstpreset image (component and controller chunks).</remarks>
    public override string PresetExtension => ".vstpreset";

    /// <inheritdoc />
    public override byte[] GetState() => VstPreset.Write(Info.ClassId, GetComponentState(), GetControllerState());

    /// <inheritdoc />
    public override void SetState(ReadOnlySpan<byte> state) => SetState(state, null);

    private void SetState(ReadOnlySpan<byte> state, string? fileName)
    {
        var preset = VstPreset.Read(state);
        if (preset.ClassId != Info.ClassId) throw new Vst3Exception("The preset belongs to another plug-in.");
        SetComponentAndControllerState(preset.ComponentState, preset.ControllerState, fileName);
    }

    /// <summary>Saves a .vstpreset file (readable by DAWs).</summary>
    public void SavePreset(string path) => File.WriteAllBytes(path, GetState());

    /// <summary>Loads a .vstpreset file.</summary>
    public void LoadPreset(string path) => SetState(File.ReadAllBytes(path), path);

    // ---------------------------------------------------------------- editor

    /// <inheritdoc />
    public override Vst3Editor? CreateEditor()
    {
        if (_controller is null) return null;
        _editor?.Dispose();
        Vst3Module.Step("creating the editor view");
        void* view;
        fixed (byte* name = "editor"u8)
        {
            view = ((delegate* unmanaged[MemberFunction]<void*, byte*, void*>)Com.Slot(_controller, 17))(_controller, name);
        }
        if (view is null) return null;
        _frame ??= new PlugFrame(this);
        _editor = new Vst3Editor(this, view, _frame);
        return _editor;
    }

    internal void EditorClosed(Vst3Editor editor)
    {
        if (ReferenceEquals(_editor, editor)) _editor = null;
    }

    // ---------------------------------------------------------------- host callbacks

    /// <summary>Raised when the plug-in's editor starts a gesture on a parameter.</summary>
    public event Action<uint>? EditBegan;

    /// <summary>Raised when the plug-in's editor ends a gesture on a parameter.</summary>
    public event Action<uint>? EditEnded;

    /// <summary><c>true</c> when the plug-in reported unsaved changes (<c>IComponentHandler2::setDirty</c>).</summary>
    public bool IsDirty { get; set; }

    void IVst3HostCallbacks.BeginEdit(uint id) => EditBegan?.Invoke(id);

    void IVst3HostCallbacks.PerformEdit(uint id, double value)
    {
        // The controller already has the value: the processor gets it with the next block.
        QueueForProcessing(id, value);
        RaiseParameterChanged(id, value);
    }

    void IVst3HostCallbacks.EndEdit(uint id) => EditEnded?.Invoke(id);

    void IVst3HostCallbacks.RestartComponent(int flags)
    {
        if ((flags & (RestartParamTitlesChanged | RestartParamValuesChanged)) != 0) ReadParameters();
        if ((flags & RestartIoChanged) != 0 && !_active)
        {
            _inputs = ReadBuses(Audio, Input);
            _outputs = ReadBuses(Audio, Output);
        }
        if ((flags & (RestartLatencyChanged | RestartIoChanged | RestartParamTitlesChanged | RestartParamValuesChanged)) != 0) RaiseRestarted();
    }

    void IVst3HostCallbacks.SetDirty(bool dirty) => IsDirty = dirty;

    bool IVst3HostCallbacks.ResizeView(void* view, ViewRect* rect) => _editor is { } editor && editor.OnResizeRequested(view, *rect);

    // ---------------------------------------------------------------- lifetime

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        Vst3Module.Step($"releasing '{Info.Name}'");
        _editor?.Dispose();
        _editor = null;
        if (_componentConnection is not null && _controllerConnection is not null)
        {
            ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_componentConnection, 4))(_componentConnection, _controllerConnection);
            ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_controllerConnection, 4))(_controllerConnection, _componentConnection);
        }
        Com.Release(_componentConnection);
        Com.Release(_controllerConnection);
        _componentConnection = _controllerConnection = null;
        Com.Release(_midiMapping);
        _midiMapping = null;
        if (_controller is not null)
        {
            ((delegate* unmanaged[MemberFunction]<void*, void*, int>)Com.Slot(_controller, 16))(_controller, null);
            if (!_singleComponent) ((delegate* unmanaged[MemberFunction]<void*, int>)Com.Slot(_controller, 4))(_controller);
            Com.Release(_controller);
            _controller = null;
        }
        Com.Release(_processor);
        _processor = null;
        if (_component is not null)
        {
            ((delegate* unmanaged[MemberFunction]<void*, int>)Com.Slot(_component, 4))(_component);
            Com.Release(_component);
            _component = null;
        }
        FreeProcessData();
        _frame?.Release();
        _frame = null;
        _handler.Release();
    }
}
