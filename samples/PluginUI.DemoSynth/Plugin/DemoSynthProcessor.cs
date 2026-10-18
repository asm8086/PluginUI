// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using NPlug;
using PluginUI.DemoSynth.Dsp;
using PluginUI.NPlug;

namespace PluginUI.DemoSynth.Plugin;

/// <summary>
/// Audio processor of the demo synth: MIDI event input (16 channels), stereo output and an optional stereo side-chain
/// input (auxiliary bus, inactive until the user routes a track to it). Runs on the host audio thread without
/// allocating.
/// </summary>
/// <remarks>
/// Note events are rendered sample-accurately: the block is split at each event's sample offset. Notes played on the
/// editor's keyboard arrive through <see cref="PluginUIProcessor{TModel}.TryDequeueUINote"/> and start at the
/// beginning of the next block.
/// </remarks>
public sealed class DemoSynthProcessor : PluginUIProcessor<DemoSynthModel>
{
    /// <summary>VST3 class identifier of the processor component.</summary>
    public static readonly Guid ClassId = new("3f1f0c55-8d9a-4a43-9a8e-5d1f2b7c6e01");

    private const int SidechainBus = 0;

    private readonly DemoSynthShared _shared = new();
    private readonly SynthEngine _engine;
    private bool _soundInBlock;
    private float[] _sideLeft = [], _sideRight = []; // side-chain copies: the host may process in place

    /// <summary>Creates the processor (32-bit float processing).</summary>
    public DemoSynthProcessor() : base(AudioSampleSizeSupport.Float32)
    {
        _engine = new SynthEngine(_shared.Notes);
    }

    /// <inheritdoc />
    public override Guid ControllerClassId => DemoSynthController.ClassId;

    /// <summary>The synthesis engine (tests).</summary>
    internal SynthEngine Engine => _engine;

    /// <inheritdoc />
    protected override object? CreateSharedState() => _shared;

    /// <inheritdoc />
    protected override bool Initialize(AudioHostApplication host)
    {
        AddEventInput("MIDI In", 16);
        AddAudioOutput("Output", SpeakerArrangement.SpeakerStereo);
        // Instruments may have audio inputs too: an auxiliary bus, off by default, that the DAW shows as a
        // side-chain destination.
        AddAudioInput("Sidechain", SpeakerArrangement.SpeakerStereo, BusType.Auxiliary, (BusFlags)0);
        return true;
    }

    /// <inheritdoc />
    protected override void OnActivate(bool isActive)
    {
        if (isActive)
        {
            int maxBlock = Math.Max(1, ProcessSetupData.MaxSamplesPerBlock);
            _sideLeft = GC.AllocateArray<float>(maxBlock, pinned: true);
            _sideRight = GC.AllocateArray<float>(maxBlock, pinned: true);
            _engine.Setup(ProcessSetupData.SampleRate);
            _engine.Apply(Model.ToSettings());
            TryConsumeStateRestored();
        }
        else
        {
            _engine.Reset();
            _shared.Notes.Clear();
            _sideLeft = _sideRight = [];
        }
        while (TryDequeueUINote(out _)) { } // notes played while inactive are stale
    }

    /// <inheritdoc />
    protected override void ProcessRecalculate(in AudioProcessData data) => _engine.Apply(Model.ToSettings());

    /// <inheritdoc />
    /// <remarks>
    /// Events are handled in <see cref="ProcessMain"/>, at their sample offsets. A call without samples (NPlug then
    /// skips <see cref="ProcessMain"/>) still applies them, so that no note-off is lost.
    /// </remarks>
    protected override void ProcessEvents(in AudioProcessData data)
    {
        if (data.SampleCount <= 0) HandleAllEvents(data);
    }

    private void HandleAllEvents(in AudioProcessData data)
    {
        var events = data.Input.Events;
        int count = events.Count;
        for (int i = 0; i < count; i++)
        {
            if (events.TryGetEvent(i, out var evt)) HandleEvent(evt);
        }
    }

    /// <inheritdoc />
    /// <remarks>No bypass parameter: the processor always renders.</remarks>
    protected override bool ProcessByPass(in AudioProcessData data) => false;

    /// <inheritdoc />
    protected override void ProcessMain(in AudioProcessData data)
    {
        // A project or preset load changes the parameters without parameter-change events.
        if (TryConsumeStateRestored()) _engine.Apply(Model.ToSettings());

        while (TryDequeueUINote(out var note))
        {
            switch (note.Kind)
            {
                case UINoteKind.NoteOn: _engine.NoteOn(note.Pitch, note.Velocity, note.Channel); break;
                case UINoteKind.NoteOff: _engine.NoteOff(note.Pitch, note.Channel); break;
                default: _engine.AllNotesOff(); break;
            }
        }

        if (data.Output.BusCount == 0 || data.Output[0].ChannelCount < 1)
        {
            HandleAllEvents(data); // nothing to render into: keep the voices consistent
            return;
        }
        var output = data.Output[0];
        bool mono = output.ChannelCount < 2;
        var left = output.GetChannelSpanAsFloat32(ProcessSetupData, data, 0);
        var right = mono ? left : output.GetChannelSpanAsFloat32(ProcessSetupData, data, 1);

        // Side-chain: only when the DAW activated the bus and passes buffers.
        scoped ReadOnlySpan<float> sideLeft = default, sideRight = default;
        var inputBuses = GetAudioInputBuses();
        if (data.Input.BusCount > SidechainBus && inputBuses.Length > SidechainBus && inputBuses[SidechainBus].IsActive)
        {
            var side = data.Input[SidechainBus];
            if (side.ChannelCount > 0 && data.SampleCount <= _sideLeft.Length)
            {
                // Copied before rendering: with in-place processing the input and output buffers may be the same.
                int n = data.SampleCount;
                side.GetChannelSpanAsFloat32(ProcessSetupData, data, 0)[..n].CopyTo(_sideLeft);
                sideLeft = _sideLeft.AsSpan(0, n);
                if (side.ChannelCount > 1)
                {
                    side.GetChannelSpanAsFloat32(ProcessSetupData, data, 1)[..n].CopyTo(_sideRight);
                    sideRight = _sideRight.AsSpan(0, n);
                }
            }
        }

        int sampleCount = data.SampleCount;
        int position = 0;
        float duckGain = 1f;
        _soundInBlock = false;
        var events = data.Input.Events;
        int eventCount = events.Count;
        for (int i = 0; i <= eventCount; i++)
        {
            AudioEvent evt = default;
            int offset = sampleCount;
            if (i < eventCount)
            {
                if (!events.TryGetEvent(i, out evt)) continue;
                offset = Math.Clamp(evt.SampleOffset, position, sampleCount);
            }
            if (offset > position)
            {
                _soundInBlock |= _engine.ActiveVoiceCount > 0;
                RenderSegment(left, right, mono, sideLeft, sideRight, position, offset - position);
                duckGain = Math.Min(duckGain, _engine.DuckGain);
                position = offset;
            }
            if (i < eventCount) HandleEvent(evt);
        }

        // Publish the meters (vectorized peak per block).
        _shared.Output[0].PushBlockPeak(left[..sampleCount]);
        _shared.Output[1].PushBlockPeak(right[..sampleCount]);
        if (sideLeft.Length >= sampleCount)
        {
            _shared.Sidechain.PushBlockPeak(sideLeft[..sampleCount]);
            if (sideRight.Length >= sampleCount) _shared.Sidechain.PushBlockPeak(sideRight[..sampleCount]);
        }
        _shared.Ducking.Push(1f - duckGain);
    }

    private void RenderSegment(Span<float> left, Span<float> right, bool mono, ReadOnlySpan<float> sideLeft, ReadOnlySpan<float> sideRight, int start, int count)
    {
        var l = left.Slice(start, count);
        // Mono output bus: the engine renders the left channel only.
        var r = mono ? Span<float>.Empty : right.Slice(start, count);
        _engine.Render(l, r, Slice(sideLeft, start, count), Slice(sideRight, start, count));
    }

    private static ReadOnlySpan<float> Slice(ReadOnlySpan<float> span, int start, int count)
        => span.Length >= start + count ? span.Slice(start, count) : default;

    private void HandleEvent(in AudioEvent evt)
    {
        switch (evt.Kind)
        {
            case AudioEventKind.NoteOn:
                ref readonly var on = ref evt.Value.NoteOn;
                _engine.NoteOn(on.Pitch, on.Velocity, on.Channel, on.NoteId);
                break;
            case AudioEventKind.NoteOff:
                ref readonly var off = ref evt.Value.NoteOff;
                _engine.NoteOff(off.Pitch, off.Channel, off.NoteId);
                break;
        }
    }

    /// <inheritdoc />
    /// <remarks>The output is silent when no voice sounded during the block: a cheap check instead of scanning the buffers.</remarks>
    protected override void PostProcessCheckSilence(in AudioProcessData data)
    {
        if (data.Output.BusCount == 0) return;
        ref var output = ref data.Output[0];
        output.SilenceFlags = _soundInBlock ? 0UL : (1UL << output.ChannelCount) - 1;
    }
}
