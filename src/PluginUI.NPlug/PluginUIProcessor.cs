// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using NPlug;
using NPlug.IO;

namespace PluginUI.NPlug;

/// <summary>
/// Base audio processor for plug-ins with a PluginUI editor: receives the notes played on the editor
/// (<see cref="TryDequeueUINote"/>) and shares an object with the controller (<see cref="CreateSharedState"/>), for
/// meters, note activity or waveforms read by the UI without locks.
/// </summary>
/// <typeparam name="TModel">Parameter model shared with the controller.</typeparam>
/// <remarks>
/// <para>
/// Messages arrive on the UI thread; notes wait in a bounded queue that the audio thread drains at the start of each
/// block, so they play at sample offset 0 of the next block. Override <see cref="OnMessage"/>, <see cref="OnConnect"/>,
/// <see cref="RestoreState"/> or <see cref="Terminate"/> only with a call to the base method.
/// </para>
/// <para>
/// The shared state is a plain object created once per processor instance. Both VST3 components of a plug-in run in
/// the same process, which makes this the simplest way to feed real-time displays: write it from the audio thread
/// with lock-free types (<c>PeakValue</c>, <c>AudioRingBuffer</c>, <see cref="NoteActivity"/>), read it from the UI.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class SynthShared { public NoteActivity Notes { get; } = new(); }
///
/// public sealed class SynthProcessor : PluginUIProcessor&lt;SynthModel&gt;
/// {
///     private readonly SynthShared _shared = new();
///     protected override object? CreateSharedState() =&gt; _shared;
///     protected override void ProcessMain(in AudioProcessData data)
///     {
///         while (TryDequeueUINote(out var note)) { /* start / stop voices */ }
///         ...
///     }
/// }
/// </code>
/// </example>
public abstract class PluginUIProcessor<TModel> : AudioProcessor<TModel>
    where TModel : AudioProcessorModel, new()
{
    private readonly UINoteQueue _uiNotes = new();
    private object? _sharedState;
    private bool _sharedStateCreated;
    private long _sharedHandle;
    private int _stateRestored;

    /// <summary>Creates the processor.</summary>
    /// <param name="sampleSizeSupport">Sample sizes the processor supports.</param>
    protected PluginUIProcessor(AudioSampleSizeSupport sampleSizeSupport) : base(sampleSizeSupport)
    {
    }

    /// <summary>Notes received from the editor, waiting for the audio thread.</summary>
    protected UINoteQueue UINotes => _uiNotes;

    /// <summary>Takes the next note played on the editor (audio thread, no allocation).</summary>
    /// <param name="note">The event.</param>
    /// <returns><c>false</c> when no note is waiting.</returns>
    protected bool TryDequeueUINote(out UINoteEvent note) => _uiNotes.TryDequeue(out note);

    /// <summary>
    /// Creates the object shared with the controller (called on the UI thread when the components connect, once per
    /// initialization). The default returns <c>null</c>: nothing is shared. The processor keeps its own reference to
    /// the object for the audio thread.
    /// </summary>
    /// <returns>The shared object, or <c>null</c>.</returns>
    protected virtual object? CreateSharedState() => null;

    /// <summary>The object shared with the controller, or <c>null</c> before the components connect.</summary>
    protected object? SharedState => _sharedState;

    /// <summary>
    /// <c>true</c> once after the host restored the processor state (project or preset load): the parameters changed
    /// without parameter-change events, so derived processors refresh their DSP settings (audio thread, no allocation).
    /// </summary>
    /// <returns><c>true</c> if a state was restored since the previous call.</returns>
    protected bool TryConsumeStateRestored() => Interlocked.Exchange(ref _stateRestored, 0) != 0;

    /// <inheritdoc />
    protected override void RestoreState(PortableBinaryReader reader)
    {
        base.RestoreState(reader);
        Volatile.Write(ref _stateRestored, 1);
    }

    /// <inheritdoc />
    protected override void OnConnect(IAudioConnectionPoint connectionPoint)
    {
        base.OnConnect(connectionPoint);
        EnsureSharedState();
        AnnounceSharedState(); // the controller may already be connected and waiting
    }

    private void EnsureSharedState()
    {
        if (_sharedStateCreated) return;
        _sharedStateCreated = true;
        _sharedState = CreateSharedState();
        if (_sharedState is not null) _sharedHandle = SharedStateRegistry.Register(_sharedState);
    }

    /// <inheritdoc />
    protected override void OnMessage(AudioMessage message)
    {
        switch (message.Id)
        {
            case PluginUIMessages.Note:
                if (PluginUIMessages.TryReadNote(message.Attributes, out var note)) _uiNotes.TryEnqueue(note);
                break;
            case PluginUIMessages.SharedStateRequest:
                EnsureSharedState();
                AnnounceSharedState();
                break;
            default:
                base.OnMessage(message);
                break;
        }
    }

    /// <inheritdoc />
    protected override void Terminate()
    {
        if (_sharedHandle != 0) SharedStateRegistry.Unregister(_sharedHandle);
        _sharedHandle = 0;
        _sharedState = null;
        _sharedStateCreated = false; // a later initialize/connect creates and announces it again
        base.Terminate();
    }

    private void AnnounceSharedState()
    {
        if (_sharedState is null) return;
        long handle = _sharedHandle;
        PluginUIMessages.TrySend(Host, ConnectionPoint, PluginUIMessages.SharedState, a => a.TrySetInt64(PluginUIMessages.HandleAttribute, handle));
    }
}
