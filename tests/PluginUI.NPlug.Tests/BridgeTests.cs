// 2026/10/18 17:31:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using NPlug;
using NPlug.IO;
using PluginUI.NPlug;

namespace PluginUI.NPlug.Tests;

/// <summary>Editor ↔ processor bridge: note queue, note activity, MIDI learn mapping and its persistence.</summary>
public class BridgeTests
{
    // ---------------------------------------------------------------- note queue

    [Test]
    public void NoteQueueIsFifoAndDropsWhenFull()
    {
        var queue = new UINoteQueue(5); // rounded up to 8
        Assert.That(queue.Capacity, Is.EqualTo(8));
        for (int i = 0; i < 8; i++) Assert.That(queue.TryEnqueue(UINoteEvent.On(60 + i, 0.5f)), Is.True);
        Assert.That(queue.TryEnqueue(UINoteEvent.On(1, 1f)), Is.False, "full: note-ons are dropped");
        Assert.That(queue.TryEnqueue(UINoteEvent.Off(61, 3)), Is.True, "note-offs are never lost");
        Assert.That(queue.TryEnqueue(UINoteEvent.Off(61, 3)), Is.True, "duplicate note-off: kept once");
        Assert.That(queue.TryEnqueue(UINoteEvent.Off(127, 15)), Is.True);
        Assert.That(queue.Count, Is.EqualTo(8));
        Assert.That(queue.TryDequeue(out var first), Is.True);
        Assert.That(first, Is.EqualTo(UINoteEvent.On(60, 0.5f)));
        Assert.That(queue.TryEnqueue(UINoteEvent.On(2, 1f)), Is.False, "note-ons wait until the backlog of note-offs is delivered");
        for (int i = 1; i < 8; i++)
        {
            Assert.That(queue.TryDequeue(out var e), Is.True);
            Assert.That(e, Is.EqualTo(UINoteEvent.On(60 + i, 0.5f)), "queued events first, in order");
        }
        Assert.That(queue.TryDequeue(out var off1), Is.True);
        Assert.That(queue.TryDequeue(out var off2), Is.True);
        Assert.That(new[] { off1, off2 }, Is.EquivalentTo(new[] { UINoteEvent.Off(61, 3), UINoteEvent.Off(127, 15) }));
        Assert.That(queue.TryDequeue(out _), Is.False);
        Assert.That(queue.TryEnqueue(UINoteEvent.On(2, 1f)), Is.True, "backlog drained: note-ons accepted again");
        Assert.That(queue.TryDequeue(out var on), Is.True);
        Assert.That(on.Pitch, Is.EqualTo(2));

        for (int i = 0; i < 8; i++) queue.TryEnqueue(UINoteEvent.On(i, 1f));
        Assert.That(queue.TryEnqueue(UINoteEvent.AllOff), Is.True, "all notes off survives an overflow");
        for (int i = 0; i < 8; i++) queue.TryDequeue(out _);
        Assert.That(queue.TryDequeue(out var all), Is.True);
        Assert.That(all.Kind, Is.EqualTo(UINoteKind.AllNotesOff));
    }

    [Test]
    public void NoteQueueKeepsOrderAcrossThreads()
    {
        var queue = new UINoteQueue(64);
        const int Total = 200_000;
        var producer = Task.Run(() =>
        {
            for (int i = 0; i < Total;)
            {
                if (queue.TryEnqueue(new UINoteEvent(UINoteKind.NoteOn, i & 127, i, 0))) i++;
                else Thread.SpinWait(8);
            }
        });
        int expected = 0;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (expected < Total && DateTime.UtcNow < deadline)
        {
            if (!queue.TryDequeue(out var e)) continue;
            Assert.That(e.Velocity, Is.EqualTo(expected), "order and content");
            expected++;
        }
        producer.Wait();
        Assert.That(expected, Is.EqualTo(Total));
    }

    [Test]
    public void NoteActivityCountsOverlappingNotes()
    {
        var activity = new NoteActivity();
        int v0 = activity.Version;
        activity.NoteOn(60);
        activity.NoteOn(60);
        activity.NoteOff(60);
        Assert.That(activity.IsOn(60), Is.True, "one note-on still pending");
        activity.NoteOff(60);
        activity.NoteOff(60); // unmatched: ignored
        Assert.That(activity.IsOn(60), Is.False);
        activity.NoteOn(200); // out of range: ignored
        Assert.That(activity.Version, Is.Not.EqualTo(v0));
        activity.NoteOn(10);
        activity.Clear();
        Assert.That(activity.IsOn(10), Is.False);
    }

    // ---------------------------------------------------------------- MIDI learn

    // No host handler in unit tests: the service tolerates null.
    private static NPlugMidiLearn CreateLearn() => new(() => null);

    private sealed class FakeSink(AudioProcessorModel model) : IParameterEditSink
    {
        public AudioProcessorModel Model { get; } = model;
        public IAudioControllerHandler? ControllerHandler => null;
        public void BeginEdit(AudioParameter parameter) { }
        public void PerformEdit(AudioParameter parameter, double normalizedValue) => parameter.NormalizedValue = normalizedValue;
        public void EndEdit(AudioParameter parameter) { }
        public bool IsEditing(AudioParameter parameter) => false;
    }

    [Test]
    public void LearnedAssignmentsArePerChannelUnlessOmni()
    {
        var learn = CreateLearn();
        int changes = 0;
        learn.Changed += () => changes++;
        learn.Assign(new AudioParameterId(3), 0, 74);
        learn.Assign(new AudioParameterId(5), -1, (int)AudioMidiControllerNumber.PitchBend);
        Assert.Multiple(() =>
        {
            Assert.That(learn.TryGetAssignment(0, 0, (AudioMidiControllerNumber)74, out var id), Is.True);
            Assert.That(id.Value, Is.EqualTo(3));
            Assert.That(learn.TryGetAssignment(0, 1, (AudioMidiControllerNumber)74, out _), Is.False, "other channel");
            Assert.That(learn.TryGetAssignment(1, 0, (AudioMidiControllerNumber)74, out _), Is.False, "other bus");
            Assert.That(learn.TryGetAssignment(0, 9, AudioMidiControllerNumber.PitchBend, out var bend), Is.True, "omni");
            Assert.That(bend.Value, Is.EqualTo(5));
            Assert.That(changes, Is.EqualTo(2));
        });
        learn.Forget(new AudioParameterId(3));
        Assert.That(learn.TryGetAssignment(0, 0, (AudioMidiControllerNumber)74, out _), Is.False);
        Assert.That(() => learn.Assign(new AudioParameterId(1), 16, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => learn.Assign(new AudioParameterId(1), 0, 130), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void LiveControllerCompletesALearnOnly()
    {
        var learn = CreateLearn();
        Assert.That(learn.OnLiveController(0, 2, (AudioMidiControllerNumber)20), Is.False, "nothing learning");
        Assert.That(learn.Assignments, Is.Empty);

        var model = new DemoSynth.Plugin.DemoSynthModel();
        model.Initialize();
        var parameters = new NPlugParameterSet(new FakeSink(model));
        var cutoff = parameters[model.Cutoff];
        var reso = parameters[model.Resonance];
        learn.BeginLearn(cutoff);
        Assert.That(learn.IsLearning(cutoff), Is.True);
        Assert.That(learn.IsLearning(reso), Is.False);
        Assert.That(learn.OnLiveController(1, 2, (AudioMidiControllerNumber)20), Is.False, "second event bus: ignored");
        Assert.That(learn.OnLiveController(0, 2, (AudioMidiControllerNumber)20), Is.True);
        Assert.That(learn.IsLearning(cutoff), Is.False);
        Assert.That(learn.GetAssignment(cutoff), Is.EqualTo("CC 20 · Ch 3"));
        Assert.That(learn.TryGetAssignment(0, 2, (AudioMidiControllerNumber)20, out var id) && id == model.Cutoff.Id, Is.True);

        // Learning again moves the parameter to the new controller; omni learning ignores the channel.
        learn.LearnOnAllChannels = true;
        learn.BeginLearn(cutoff);
        learn.OnLiveController(0, 5, AudioMidiControllerNumber.ModWheel);
        Assert.That(learn.GetAssignment(cutoff), Is.EqualTo("CC 1"));
        Assert.That(learn.TryGetAssignment(0, 2, (AudioMidiControllerNumber)20, out _), Is.False);
        learn.Forget(cutoff);
        Assert.That(learn.GetAssignment(cutoff), Is.Null);

        // An omni learn replaces channel-specific assignments of the same controller.
        learn.Assign(reso.Id, 4, 30);
        learn.BeginLearn(cutoff);
        learn.OnLiveController(0, 9, (AudioMidiControllerNumber)30);
        Assert.That(learn.TryGetAssignment(0, 4, (AudioMidiControllerNumber)30, out var onFour) && onFour == model.Cutoff.Id, Is.True);
        Assert.That(learn.GetAssignment(reso), Is.Null);
        learn.Clear();

        // A/B snapshot list: every editable parameter, in order.
        var snapshot = parameters.GetSnapshotParameters();
        Assert.That(snapshot.Select(p => p.Parameter), Is.EqualTo(Enumerable.Range(0, model.ParameterCount).Select(model.GetParameterByIndex)));
    }

    [Test]
    public void AssignmentsSurviveSaveAndLoad()
    {
        var learn = CreateLearn();
        learn.Assign(new AudioParameterId(7), 3, 11);
        learn.Assign(new AudioParameterId(8), -1, (int)AudioMidiControllerNumber.AfterTouch);
        using var stream = new MemoryStream();
        using (var writer = new PortableBinaryWriter(stream, owned: false)) learn.Save(writer);
        stream.WriteByte(0x5A); // data written after ours by a derived controller
        stream.Position = 0;

        var restored = CreateLearn();
        using var reader = new PortableBinaryReader(stream, owned: false);
        Assert.That(restored.Load(reader), Is.True);
        Assert.That(restored.Assignments, Is.EquivalentTo(learn.Assignments));
        Assert.That(stream.ReadByte(), Is.EqualTo(0x5A), "the stream stops right after the assignments");
        Assert.That(NPlugMidiLearn.Describe(3, 11), Is.EqualTo("CC 11 · Ch 4"));
        Assert.That(NPlugMidiLearn.Describe(-1, 128), Is.EqualTo("Aftertouch"));
        Assert.That(NPlugMidiLearn.Describe(0, 129), Is.EqualTo("Pitch Bend · Ch 1"));
    }

    [Test]
    public void OlderStatesWithoutAssignmentsAreLeftUntouched()
    {
        var learn = CreateLearn();
        learn.Assign(new AudioParameterId(1), 0, 1);
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
        using var reader = new PortableBinaryReader(stream, owned: false);
        Assert.That(learn.Load(reader), Is.False);
        Assert.That(learn.Assignments, Is.Empty, "a loaded state replaces the assignments");
        Assert.That(stream.Position, Is.Zero, "the stream is rewound for the derived controller");
        using var empty = new PortableBinaryReader(new MemoryStream(), owned: true);
        Assert.That(learn.Load(empty), Is.False);
    }

    [Test]
    public void MessageNamesArePrefixed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PluginUIMessages.Note, Does.StartWith("PluginUI."));
            Assert.That(PluginUIMessages.SharedState, Does.StartWith("PluginUI."));
            Assert.That(PluginUIMessages.SharedStateRequest, Does.StartWith("PluginUI."));
            Assert.That(PluginUIMessages.TrySend(null, null, "x"), Is.False, "no host: nothing sent, no exception");
        });
    }
    [Test]
    public void HostKeyCodesMapToKeys()
    {
        // VST3 VirtualKeyCodes: KEY_BACK = 1 … KEY_F1 = 40 … KEY_F12 = 51, KEY_SHIFT = 54, KEY_CONTROL = 55, KEY_ALT = 56.
        Assert.Multiple(() =>
        {
            Assert.That(PluginEditorView.MapKey(0, 1), Is.EqualTo(Key.Back));
            Assert.That(PluginEditorView.MapKey(0, 22), Is.EqualTo(Key.Delete));
            Assert.That(PluginEditorView.MapKey(0, 40), Is.EqualTo(Key.F1));
            Assert.That(PluginEditorView.MapKey(0, 51), Is.EqualTo(Key.F12));
            Assert.That(PluginEditorView.MapKey(0, 54), Is.EqualTo(Key.Shift));
            Assert.That(PluginEditorView.MapKey(0, 55), Is.EqualTo(Key.Control));
            Assert.That(PluginEditorView.MapKey(0, 56), Is.EqualTo(Key.Alt));
            Assert.That(PluginEditorView.MapKey(0, 57), Is.EqualTo(Key.None), "KEY_EQUALS without a character");
            Assert.That(PluginEditorView.MapKey('a', 0), Is.EqualTo(Key.A));
        });
    }

    [Test]
    public void TypedValuesUseTheDecimalPoint()
    {
        var model = new DemoSynth.Plugin.DemoSynthModel();
        model.Initialize();
        var cutoff = new NPlugParameterSet(new FakeSink(model))[model.Cutoff];
        Assert.Multiple(() =>
        {
            Assert.That(cutoff.TryParse("1000.5 Hz", out var n) && Math.Abs(model.Cutoff.ToPlain(n) - 1000.5) < 0.01, Is.True);
            Assert.That(cutoff.TryParse("1000,5", out _), Is.False, "decimal comma");
        });
    }
}
