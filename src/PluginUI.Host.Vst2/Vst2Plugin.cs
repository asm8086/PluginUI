// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PluginUI.Host.Vst2;

/// <summary>
/// A VST 2 plug-in instance (VST 2.4 interface; 2.0 to 2.3 plug-ins too). Created by
/// <see cref="Vst2Module.CreatePlugin"/> or <see cref="Vst2Hosting.Load"/>.
/// </summary>
/// <remarks>
/// Parameters are identified by their index. VST 2 has a single object for processing and editing: parameter changes
/// are given to the plug-in right away, from the calling thread, as VST 2 hosts do.
/// </remarks>
public sealed unsafe class Vst2Plugin : PluginInstance
{
    private const int MaxEvents = 1024;
    private const int TextCapacity = 256;

    [ThreadStatic] private static Vst2Plugin? t_loading;
    [ThreadStatic] private static int t_shellId;
    [ThreadStatic] private static Vst2Plugin? t_processing;
    [ThreadStatic] private static int t_processLevel;

    private static readonly long s_clockStart = Stopwatch.GetTimestamp();

    private readonly Vst2Module _module;
    private readonly string _hostName;
    private AEffect* _effect;
    private GCHandle _self;
    private PluginBus[] _inputs = [];
    private PluginBus[] _outputs = [];
    private PluginParameter[] _parameters = [];
    private string _name = string.Empty;
    private int _eventInputs, _eventOutputs;

    // Native blocks: events to the plug-in, time info, channel pointers.
    private byte* _events;
    private int _eventCount;
    private VstTimeInfo* _time;
    private float** _inPointers;
    private float** _outPointers;
    private float* _spare;
    private int _pointerSlots;
    private bool _active, _realtime, _wasPlaying;
    private volatile bool _restart, _displayChanged;
    private readonly ParameterQueue _toUi = new(4096);
    private Vst2Editor? _editor;

    internal Vst2Plugin(Vst2Module module, int shellId, string hostName)
    {
        _module = module;
        _hostName = hostName;
        ShellId = shellId;
        try
        {
            Vst2Module.Step($"creating the plug-in of '{System.IO.Path.GetFileName(module.Path)}'");
            t_loading = this;
            t_shellId = shellId;
            try
            {
                _effect = module.Entry(&HostCallback);
            }
            finally
            {
                t_loading = null;
                t_shellId = 0;
            }
            if (_effect is null) throw new Vst2Exception($"'{System.IO.Path.GetFileName(module.Path)}' did not create its plug-in.");
            if (_effect->Magic != AEffect.ExpectedMagic) throw new Vst2Exception($"'{System.IO.Path.GetFileName(module.Path)}' returned an object that is not a VST 2 plug-in.");
            _self = GCHandle.Alloc(this);
            _effect->User = (void*)GCHandle.ToIntPtr(_self);
            AllocateEvents();
            Vst2Module.Step("opening the plug-in");
            Dispatch(EffectOpcode.Open);

            Category = (Vst2Category)(int)Dispatch(EffectOpcode.GetPlugCategory);
            VstVersion = (int)Dispatch(EffectOpcode.GetVstVersion);
            _name = Text(EffectOpcode.GetEffectName);
            Product = Text(EffectOpcode.GetProductString);
            Vendor = Text(EffectOpcode.GetVendorString);
            VendorVersion = (int)Dispatch(EffectOpcode.GetVendorVersion);
            if (_name.Length == 0) _name = Product.Length > 0 ? Product : System.IO.Path.GetFileNameWithoutExtension(module.Path);
            bool synth = (_effect->Flags & EffectFlags.IsSynth) != 0 || Category == Vst2Category.Synth;
            _eventInputs = synth || CanDo("receiveVstMidiEvent") || CanDo("receiveVstEvents") ? 1 : 0;
            _eventOutputs = CanDo("sendVstMidiEvent") || CanDo("sendVstEvents") ? 1 : 0;
            Vst2Module.Step("reading the channels and the parameters");
            ReadBuses();
            ReadParameters();
        }
        catch
        {
            Dispose(true);
            throw;
        }
    }

    /// <summary>Path of the module.</summary>
    public string ModulePath => _module.Path;

    /// <summary>Identifier given to a shell module to create this plug-in (0 for an ordinary module).</summary>
    public int ShellId { get; }

    /// <summary>Unique identifier of the plug-in (its presets carry it).</summary>
    public int UniqueId => _effect->UniqueId;

    /// <summary>Version of the plug-in (AEffect.version).</summary>
    public int PluginVersion => _effect->Version;

    /// <summary>VST version the plug-in implements (2400 for 2.4; 0 or 1 for VST 1).</summary>
    public int VstVersion { get; }

    /// <summary>Category the plug-in reports.</summary>
    public Vst2Category Category { get; }

    /// <summary>Product name.</summary>
    public string Product { get; } = string.Empty;

    /// <summary>Vendor version.</summary>
    public int VendorVersion { get; }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Vendor { get; } = string.Empty;

    /// <inheritdoc />
    public override PluginFormat Format => PluginFormat.Vst2;

    /// <inheritdoc />
    public override bool IsInstrument => (_effect->Flags & EffectFlags.IsSynth) != 0 || Category is Vst2Category.Synth;

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
    public override int LatencySamples => _effect->InitialDelay;

    /// <inheritdoc />
    public override int TailSamples
    {
        get
        {
            // 0: the plug-in does not say; 1: no tail.
            nint tail = Dispatch(EffectOpcode.GetTailSize);
            return tail > 1 ? (int)Math.Min(tail, int.MaxValue) : 0;
        }
    }

    /// <inheritdoc />
    public override bool HasEditor => (_effect->Flags & EffectFlags.HasEditor) != 0;

    /// <summary><c>true</c> when the plug-in saves its state as an opaque chunk (else as parameter values).</summary>
    public bool UsesChunks => (_effect->Flags & EffectFlags.ProgramChunks) != 0;

    /// <summary><c>true</c> when the plug-in can process 64-bit samples.</summary>
    public bool CanProcessDouble => (_effect->Flags & EffectFlags.CanDoubleReplacing) != 0;

    /// <inheritdoc />
    public override string PresetExtension => UsesChunks ? ".fxb" : ".fxp";

    /// <summary>Raised when the plug-in's editor starts a gesture on a parameter (UI thread).</summary>
    public event Action<uint>? EditBegan;

    /// <summary>Raised when the plug-in's editor ends a gesture on a parameter (UI thread).</summary>
    public event Action<uint>? EditEnded;

    // ---------------------------------------------------------------- dispatcher

    internal nint Dispatch(int opcode, int index = 0, nint value = 0, void* ptr = null, float opt = 0f)
        => _effect is null ? 0 : _effect->Dispatcher(_effect, opcode, index, value, ptr, opt);

    private string Text(int opcode, int index = 0)
    {
        byte* text = stackalloc byte[TextCapacity];
        new Span<byte>(text, TextCapacity).Clear();
        Dispatch(opcode, index, 0, text);
        return ReadString(text, TextCapacity);
    }

    internal static string ReadString(byte* text, int capacity)
    {
        var span = new ReadOnlySpan<byte>(text, capacity);
        int end = span.IndexOf((byte)0);
        // Plug-ins write ANSI text: Latin-1 keeps every byte and suits the usual names.
        return Encoding.Latin1.GetString(end < 0 ? span : span[..end]).Trim();
    }

    /// <summary>Asks the plug-in whether it can do something ("receiveVstMidiEvent", "bypass").</summary>
    public bool CanDo(string feature)
    {
        byte[] text = Encoding.ASCII.GetBytes(feature + "\0");
        fixed (byte* t = text) return Dispatch(EffectOpcode.CanDo, 0, 0, t) > 0;
    }

    // ---------------------------------------------------------------- description

    private void ReadBuses()
    {
        _inputs = Buses(Math.Max(0, _effect->NumInputs), true);
        _outputs = Buses(Math.Max(0, _effect->NumOutputs), false);
    }

    /// <summary>The first two channels form the main bus; the next ones auxiliary buses of two (side chain, more outputs).</summary>
    private PluginBus[] Buses(int channels, bool input)
    {
        var buses = new List<PluginBus>();
        string kind = input ? "Input" : "Output";
        int main = Math.Min(2, channels);
        if (main > 0) buses.Add(new PluginBus(kind, main, false, true));
        for (int first = main; first < channels; first += 2)
        {
            int count = Math.Min(2, channels - first);
            string label = PinLabel(first, input);
            buses.Add(new PluginBus(label.Length > 0 ? label : count == 2 ? $"{kind} {first + 1}-{first + 2}" : $"{kind} {first + 1}", count, true, true));
        }
        return [.. buses];
    }

    private string PinLabel(int channel, bool input)
    {
        VstPinProperties pin = default;
        if (Dispatch(input ? EffectOpcode.GetInputProperties : EffectOpcode.GetOutputProperties, channel, 0, &pin) == 0) return string.Empty;
        return ReadString(pin.Label, 64);
    }

    private void ReadParameters()
    {
        int count = Math.Max(0, _effect->NumParams);
        var list = new PluginParameter[count];
        for (int i = 0; i < count; i++)
        {
            string title = Text(EffectOpcode.GetParamName, i);
            string units = Text(EffectOpcode.GetParamLabel, i);
            int steps = 0;
            string shortTitle = string.Empty;
            VstParameterProperties props = default;
            if (Dispatch(EffectOpcode.GetParameterProperties, i, 0, &props) != 0)
            {
                if ((props.Flags & VstParameterProperties.IsSwitch) != 0) steps = 1;
                else if ((props.Flags & VstParameterProperties.UsesIntegerMinMax) != 0 && props.MaxInteger > props.MinInteger) steps = props.MaxInteger - props.MinInteger;
                shortTitle = ReadString(props.ShortLabel, 8);
                if (title.Length == 0) title = ReadString(props.Label, 64);
            }
            if (title.Length == 0) title = $"Parameter {i + 1}";
            var flags = ParameterFlags.CanAutomate;
            if (steps > 0) flags |= ParameterFlags.IsList;
            list[i] = new PluginParameter((uint)i, title, shortTitle.Length > 0 ? shortTitle : title, units, steps, _effect->GetParameter(_effect, i), 0, flags);
        }
        _parameters = list;
    }

    internal IReadOnlyList<Vst2ShellPlugin> ReadShellPlugins()
    {
        var list = new List<Vst2ShellPlugin>();
        byte* name = stackalloc byte[TextCapacity];
        for (int guard = 0; guard < 10000; guard++)
        {
            new Span<byte>(name, TextCapacity).Clear();
            int id = (int)Dispatch(EffectOpcode.ShellGetNextPlugin, 0, 0, name);
            if (id == 0) break;
            list.Add(new Vst2ShellPlugin(id, ReadString(name, TextCapacity)));
        }
        return list;
    }

    // ---------------------------------------------------------------- programs

    /// <summary>Number of programs (presets inside the plug-in).</summary>
    public int ProgramCount => Math.Max(0, _effect->NumPrograms);

    /// <summary>Current program.</summary>
    public int CurrentProgram
    {
        get => (int)Dispatch(EffectOpcode.GetProgram);
        set
        {
            if (value < 0 || value >= ProgramCount) throw new ArgumentOutOfRangeException(nameof(value));
            Dispatch(EffectOpcode.BeginSetProgram);
            Dispatch(EffectOpcode.SetProgram, 0, value);
            Dispatch(EffectOpcode.EndSetProgram);
            _displayChanged = true;
        }
    }

    /// <summary>Name of a program.</summary>
    public string GetProgramName(int index)
    {
        byte* text = stackalloc byte[TextCapacity];
        new Span<byte>(text, TextCapacity).Clear();
        if (Dispatch(EffectOpcode.GetProgramNameIndexed, index, -1, text) == 0 && index == CurrentProgram)
        {
            new Span<byte>(text, TextCapacity).Clear();
            Dispatch(EffectOpcode.GetProgramName, 0, 0, text);
        }
        return ReadString(text, TextCapacity);
    }

    private void SetProgramName(string name)
    {
        byte[] text = new byte[25];
        Encoding.Latin1.GetBytes(name.AsSpan(0, Math.Min(name.Length, 24)), text);
        fixed (byte* t = text) Dispatch(EffectOpcode.SetProgramName, 0, 0, t);
    }

    // ---------------------------------------------------------------- parameters

    /// <inheritdoc />
    public override double GetParameter(uint id) => id < (uint)_effect->NumParams ? _effect->GetParameter(_effect, (int)id) : 0.0;

    /// <inheritdoc />
    protected override void OnSetParameterUi(uint id, double normalized)
    {
        if (id < (uint)_effect->NumParams) _effect->SetParameter(_effect, (int)id, (float)normalized);
    }

    private string Display(uint id)
    {
        string value = Text(EffectOpcode.GetParamDisplay, (int)id);
        string label = Text(EffectOpcode.GetParamLabel, (int)id);
        return label.Length > 0 && value.Length > 0 ? value + " " + label : value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// VST 2 plug-ins display only their current value: another value is shown by setting it for a moment, which is
    /// not done while the plug-in processes (the number is shown instead).
    /// </remarks>
    public override string FormatParameter(uint id, double normalized)
    {
        if (id >= (uint)_effect->NumParams) return string.Empty;
        float current = _effect->GetParameter(_effect, (int)id);
        if (Math.Abs(current - normalized) < 1e-6) return Display(id);
        if (_active) return normalized.ToString("0.###", CultureInfo.InvariantCulture);
        _effect->SetParameter(_effect, (int)id, (float)normalized);
        string text = Display(id);
        _effect->SetParameter(_effect, (int)id, current);
        return text;
    }

    /// <inheritdoc />
    public override bool TryParseParameter(uint id, string text, out double normalized)
    {
        normalized = 0;
        if (id >= (uint)_effect->NumParams || string.IsNullOrWhiteSpace(text)) return false;
        float current = _effect->GetParameter(_effect, (int)id);
        byte[] bytes = Encoding.Latin1.GetBytes(text.Trim() + "\0");
        nint accepted;
        fixed (byte* t = bytes) accepted = Dispatch(EffectOpcode.String2Parameter, (int)id, 0, t);
        if (accepted != 0)
        {
            normalized = _effect->GetParameter(_effect, (int)id);
            _effect->SetParameter(_effect, (int)id, current);
            return true;
        }
        // The plug-in cannot parse text: a number between 0 and 1 is the normalized value.
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v is >= 0 and <= 1)
        {
            normalized = v;
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- processing

    private void AllocateEvents()
    {
        // Header (16 bytes), the pointers, then the events themselves.
        int header = sizeof(VstEvents) + MaxEvents * sizeof(void*);
        _events = (byte*)NativeMemory.AllocZeroed((nuint)(header + MaxEvents * sizeof(VstMidiEvent)));
        var pointers = (VstMidiEvent**)(_events + sizeof(VstEvents));
        var events = (VstMidiEvent*)(_events + header);
        for (int i = 0; i < MaxEvents; i++) pointers[i] = events + i;
        _time = (VstTimeInfo*)NativeMemory.AllocZeroed((nuint)sizeof(VstTimeInfo));
    }

    /// <inheritdoc />
    protected override void OnPrepare(double sampleRate, int maxBlockSize, bool realtime)
    {
        Vst2Module.Step("setting up the processing");
        FreePointers();
        Dispatch(EffectOpcode.SetSampleRate, 0, 0, null, (float)sampleRate);
        Dispatch(EffectOpcode.SetBlockSize, 0, maxBlockSize);
        Dispatch(EffectOpcode.SetProcessPrecision, 0, 0 /* 32-bit */);
        // Plug-ins may read or write every channel they declared, and some touch the first two of each side anyway:
        // the missing ones get a spare buffer.
        int ins = Math.Max(0, _effect->NumInputs), outs = Math.Max(0, _effect->NumOutputs);
        _pointerSlots = Math.Max(2, Math.Max(ins, outs));
        _inPointers = (float**)NativeMemory.AllocZeroed((nuint)_pointerSlots, (nuint)sizeof(float*));
        _outPointers = (float**)NativeMemory.AllocZeroed((nuint)_pointerSlots, (nuint)sizeof(float*));
        _spare = (float*)NativeMemory.AllocZeroed((nuint)(2 * maxBlockSize), sizeof(float));
        for (int c = 0; c < _pointerSlots; c++)
        {
            _inPointers[c] = c < InputChannelCount ? InputPointer(c) : _spare;
            _outPointers[c] = c < OutputChannelCount ? OutputPointer(c) : _spare + maxBlockSize;
        }
        _realtime = realtime;
        Vst2Module.Step("activating");
        Dispatch(EffectOpcode.MainsChanged, 0, 1);
        Dispatch(EffectOpcode.StartProcess);
        _active = true;
    }

    /// <inheritdoc />
    public override void AddMidiEvent(in MidiEvent e)
    {
        if (e.Status < 0x80 || e.Status >= 0xF0 || _eventCount >= MaxEvents) return;
        var ev = (VstMidiEvent*)(_events + sizeof(VstEvents) + MaxEvents * sizeof(void*)) + _eventCount++;
        *ev = new VstMidiEvent
        {
            Type = VstMidiEvent.MidiType,
            ByteSize = sizeof(VstMidiEvent),
            DeltaFrames = Math.Max(0, e.SampleOffset),
            Flags = _realtime ? VstMidiEvent.IsRealtime : 0,
            Data0 = e.Status,
            Data1 = (byte)(e.Data1 & 0x7F),
            Data2 = (byte)(e.Data2 & 0x7F),
            NoteOffVelocity = e.Type == 0x80 ? (byte)(e.Data2 & 0x7F) : (byte)0,
        };
    }

    /// <inheritdoc />
    protected override void OnProcess(int frames, ParameterQueue pending)
    {
        // The values were given to the plug-in when they were set.
        while (pending.TryDequeue(out _, out _))
        {
        }
        var previous = t_processing;
        int previousLevel = t_processLevel;
        t_processing = this;
        t_processLevel = _realtime ? 2 : 4; // kVstProcessLevelRealtime, kVstProcessLevelOffline
        try
        {
            if (_eventCount > 0)
            {
                ((VstEvents*)_events)->NumEvents = _eventCount;
                Dispatch(EffectOpcode.ProcessEvents, 0, 0, _events);
            }
            if ((_effect->Flags & EffectFlags.CanReplacing) != 0 && _effect->ProcessReplacing is not null)
            {
                _effect->ProcessReplacing(_effect, _inPointers, _outPointers, frames);
            }
            else if (_effect->Process is not null)
            {
                // VST 1 plug-ins add to the outputs.
                for (int c = 0; c < OutputChannelCount; c++) new Span<float>(OutputPointer(c), frames).Clear();
                _effect->Process(_effect, _inPointers, _outPointers, frames);
            }
        }
        finally
        {
            _eventCount = 0;
            t_processing = previous;
            t_processLevel = previousLevel;
        }
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        if (!_active) return;
        _active = false;
        Dispatch(EffectOpcode.StopProcess);
        Dispatch(EffectOpcode.MainsChanged, 0, 0);
    }

    /// <inheritdoc />
    public override void Idle()
    {
        while (_toUi.TryDequeue(out uint id, out double value)) RaiseParameterChanged(id, value);
        if (_displayChanged)
        {
            _displayChanged = false;
            ReadParameters();
            RaiseRestarted();
        }
        if (_restart)
        {
            _restart = false;
            if (!_active) ReadBuses();
            RaiseRestarted();
        }
        if (_editor is { IsAttached: true }) Dispatch(EffectOpcode.EditIdle);
    }

    private void FreePointers()
    {
        NativeMemory.Free(_inPointers);
        NativeMemory.Free(_outPointers);
        NativeMemory.Free(_spare);
        _inPointers = _outPointers = null;
        _spare = null;
    }

    private void FillTime()
    {
        var t = _time;
        double rate = SampleRate > 0 ? SampleRate : 44100.0;
        double beats = SamplePosition / rate * Tempo / 60.0;
        double beatsPerBar = TimeSignature.Numerator * 4.0 / Math.Max(1, TimeSignature.Denominator);
        t->SamplePos = SamplePosition;
        t->SampleRate = rate;
        t->NanoSeconds = Stopwatch.GetElapsedTime(s_clockStart).TotalNanoseconds;
        t->PpqPos = beats;
        t->Tempo = Tempo;
        t->BarStartPos = beatsPerBar > 0 ? Math.Floor(beats / beatsPerBar) * beatsPerBar : 0;
        t->CycleStartPos = t->CycleEndPos = 0;
        t->TimeSigNumerator = TimeSignature.Numerator;
        t->TimeSigDenominator = TimeSignature.Denominator;
        t->SmpteOffset = t->SmpteFrameRate = t->SamplesToNextClock = 0;
        int flags = VstTimeInfo.NanosValid | VstTimeInfo.PpqPosValid | VstTimeInfo.TempoValid | VstTimeInfo.BarsValid | VstTimeInfo.TimeSigValid;
        if (IsPlaying) flags |= VstTimeInfo.TransportPlaying;
        if (IsPlaying != _wasPlaying) flags |= VstTimeInfo.TransportChanged;
        _wasPlaying = IsPlaying;
        t->Flags = flags;
    }

    private void CollectOutputEvents(VstEvents* list)
    {
        if (list is null) return;
        var pointers = (VstMidiEvent**)((byte*)list + sizeof(VstEvents));
        for (int i = 0; i < list->NumEvents; i++)
        {
            var ev = pointers[i];
            if (ev is null || ev->Type != VstMidiEvent.MidiType) continue;
            byte status = ev->Data0;
            if (status < 0x80 || status >= 0xF0) continue;
            AddOutputMidi(new MidiEvent(Math.Max(0, ev->DeltaFrames), status, (byte)(ev->Data1 & 0x7F), (byte)(ev->Data2 & 0x7F)));
        }
    }

    // ---------------------------------------------------------------- state

    /// <inheritdoc />
    /// <remarks>An .fxb bank chunk for plug-ins that save chunks, else an .fxp of the current program.</remarks>
    public override byte[] GetState() => GetPreset().Write();

    /// <summary>The state as a preset: a bank chunk for plug-ins that save chunks, else the current program's values.</summary>
    public FxPreset GetPreset()
    {
        if (UsesChunks)
        {
            void* data = null;
            nint size = Dispatch(EffectOpcode.GetChunk, 0 /* bank */, 0, &data);
            if (size > 0 && data is not null)
            {
                return new FxPreset
                {
                    Kind = FxPresetKind.BankChunk, PluginId = UniqueId, PluginVersion = PluginVersion, ProgramCount = Math.Max(1, ProgramCount),
                    CurrentProgram = Math.Max(0, CurrentProgram), Chunk = new ReadOnlySpan<byte>(data, (int)size).ToArray(),
                };
            }
        }
        var values = new float[Math.Max(0, _effect->NumParams)];
        for (int i = 0; i < values.Length; i++) values[i] = _effect->GetParameter(_effect, i);
        string name = ProgramCount > 0 ? GetProgramName(Math.Max(0, CurrentProgram)) : string.Empty;
        return new FxPreset { Kind = FxPresetKind.ProgramParameters, PluginId = UniqueId, PluginVersion = PluginVersion, Programs = [new FxProgram(name, values)] };
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The data is not a preset of this plug-in, or the plug-in refused it.</exception>
    public override void SetState(ReadOnlySpan<byte> state) => SetPreset(FxPreset.Read(state));

    /// <summary>Loads a preset (.fxp program or .fxb bank).</summary>
    /// <exception cref="InvalidDataException">The preset belongs to another plug-in, or the plug-in refused it.</exception>
    public void SetPreset(FxPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.PluginId != UniqueId)
            throw new InvalidDataException($"This preset belongs to another plug-in ({FxPreset.FourCc(preset.PluginId)}, not {FxPreset.FourCc(UniqueId)}).");
        var info = new VstPatchChunkInfo
        {
            Version = 1, PluginUniqueId = preset.PluginId, PluginVersion = preset.PluginVersion,
            NumElements = preset.IsBank ? Math.Max(preset.ProgramCount, preset.Programs.Count) : preset.Programs.Count > 0 ? preset.Programs[0].Parameters.Length : 0,
        };
        if (Dispatch(preset.IsBank ? EffectOpcode.BeginLoadBank : EffectOpcode.BeginLoadProgram, 0, 0, &info) == -1)
            throw new InvalidDataException($"{Name} refused this preset (made by another version of the plug-in).");
        switch (preset.Kind)
        {
            case FxPresetKind.ProgramParameters:
                LoadProgram(preset.Programs[0]);
                break;
            case FxPresetKind.ProgramChunk:
            case FxPresetKind.BankChunk:
                Dispatch(EffectOpcode.BeginSetProgram);
                fixed (byte* data = preset.Chunk)
                {
                    Dispatch(EffectOpcode.SetChunk, preset.IsBank ? 0 : 1, preset.Chunk.Length, data);
                }
                Dispatch(EffectOpcode.EndSetProgram);
                if (preset.Kind == FxPresetKind.ProgramChunk && preset.ProgramName.Length > 0) SetProgramName(preset.ProgramName);
                break;
            case FxPresetKind.BankParameters:
                for (int p = 0; p < Math.Min(preset.Programs.Count, ProgramCount); p++)
                {
                    Dispatch(EffectOpcode.SetProgram, 0, p);
                    LoadProgram(preset.Programs[p]);
                }
                if (ProgramCount > 0) CurrentProgram = Math.Clamp(preset.CurrentProgram, 0, ProgramCount - 1);
                break;
        }
        _displayChanged = true;
        for (uint i = 0; i < (uint)_parameters.Length; i++) _toUi.Enqueue(i, GetParameter(i));
    }

    private void LoadProgram(FxProgram program)
    {
        Dispatch(EffectOpcode.BeginSetProgram);
        if (program.Name.Length > 0 && ProgramCount > 0) SetProgramName(program.Name);
        for (int i = 0; i < Math.Min(program.Parameters.Length, _effect->NumParams); i++) _effect->SetParameter(_effect, i, program.Parameters[i]);
        Dispatch(EffectOpcode.EndSetProgram);
    }

    // ---------------------------------------------------------------- editor

    /// <inheritdoc />
    public override Vst2Editor? CreateEditor()
    {
        if (!HasEditor) return null;
        _editor?.Dispose();
        _editor = new Vst2Editor(this);
        return _editor;
    }

    internal void EditorClosed(Vst2Editor editor)
    {
        if (ReferenceEquals(_editor, editor)) _editor = null;
    }

    // ---------------------------------------------------------------- host callback

    [UnmanagedCallersOnly]
    private static nint HostCallback(AEffect* effect, int opcode, int index, nint value, void* ptr, float opt)
    {
        try
        {
            Vst2Plugin? plugin = effect is null || effect->User is null ? t_loading : GCHandle.FromIntPtr((nint)effect->User).Target as Vst2Plugin;
            switch (opcode)
            {
                case HostOpcode.Version:
                    return 2400;
                case HostOpcode.CurrentId:
                    return plugin is not null && ReferenceEquals(plugin, t_loading) ? t_shellId : plugin?.ShellId ?? 0;
                case HostOpcode.Idle:
                    return 0;
                case HostOpcode.GetSampleRate:
                    return (nint)(plugin is { SampleRate: > 0 } ? plugin.SampleRate : 44100);
                case HostOpcode.GetBlockSize:
                    return plugin is { MaxBlockSize: > 0 } ? plugin.MaxBlockSize : 512;
                case HostOpcode.GetInputLatency:
                case HostOpcode.GetOutputLatency:
                    return 0;
                case HostOpcode.GetCurrentProcessLevel:
                    return t_processLevel != 0 ? t_processLevel : 1; // 1 = user (interface) thread
                case HostOpcode.GetAutomationState:
                    return 1; // off
                case HostOpcode.GetVendorString:
                    return CopyString(ptr, "PluginUI", 64);
                case HostOpcode.GetProductString:
                    return CopyString(ptr, plugin?._hostName ?? "PluginUI Host", 64);
                case HostOpcode.GetVendorVersion:
                    return 1;
                case HostOpcode.CanDo:
                    return ptr is not null && HostCanDo(ReadString((byte*)ptr, 64)) ? 1 : 0;
                case HostOpcode.GetLanguage:
                    return 1; // English
                case HostOpcode.GetDirectory:
                    return plugin is null ? 0 : (nint)plugin._module.Directory;
            }
            if (plugin is null || plugin._effect is null) return 0;
            switch (opcode)
            {
                case HostOpcode.Automate:
                    plugin._toUi.Enqueue((uint)index, opt);
                    return 0;
                case HostOpcode.BeginEdit:
                    plugin.EditBegan?.Invoke((uint)index);
                    return 1;
                case HostOpcode.EndEdit:
                    plugin.EditEnded?.Invoke((uint)index);
                    return 1;
                case HostOpcode.GetTime:
                    plugin.FillTime();
                    return (nint)plugin._time;
                case HostOpcode.ProcessEvents:
                    // Only during the plug-in's processing: the messages belong to that block.
                    if (ReferenceEquals(t_processing, plugin)) plugin.CollectOutputEvents((VstEvents*)ptr);
                    return 1;
                case HostOpcode.IoChanged:
                    plugin._restart = true;
                    return 1;
                case HostOpcode.UpdateDisplay:
                    plugin._displayChanged = true;
                    return 1;
                case HostOpcode.SizeWindow:
                    return plugin._editor is { } editor && editor.OnResizeRequested(index, (int)value) ? 1 : 0;
                default:
                    return 0;
            }
        }
        catch
        {
            // Never let an exception reach the plug-in.
            return 0;
        }
    }

    private static bool HostCanDo(string feature) => feature is "sendVstEvents" or "sendVstMidiEvent" or "sendVstTimeInfo"
        or "receiveVstEvents" or "receiveVstMidiEvent" or "sizeWindow" or "startStopProcess" or "shellCategory"
        or "acceptIOChanges" or "sendVstMidiEventFlagIsRealtime" or "supplyIdle";

    private static nint CopyString(void* destination, string text, int capacity)
    {
        if (destination is null) return 0;
        var d = new Span<byte>(destination, capacity);
        d.Clear();
        int n = Encoding.ASCII.GetBytes(text.AsSpan(0, Math.Min(text.Length, capacity - 1)), d);
        return n > 0 ? 1 : 0;
    }

    // ---------------------------------------------------------------- lifetime

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _editor?.Dispose();
        _editor = null;
        if (_effect is not null)
        {
            Vst2Module.Step($"closing '{_name}'");
            Dispatch(EffectOpcode.Close);
            _effect = null;
        }
        if (_self.IsAllocated) _self.Free();
        FreePointers();
        NativeMemory.Free(_events);
        _events = null;
        NativeMemory.Free(_time);
        _time = null;
        _module.Release();
    }
}
