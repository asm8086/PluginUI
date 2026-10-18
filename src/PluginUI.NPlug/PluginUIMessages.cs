// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using NPlug;

namespace PluginUI.NPlug;

/// <summary>
/// Messages exchanged by <see cref="PluginUIController{TModel}"/> and <see cref="PluginUIProcessor{TModel}"/>
/// through the VST3 connection (<c>IConnectionPoint::notify</c>, always on the UI thread).
/// </summary>
/// <remarks>
/// Message identifiers start with <c>PluginUI.</c>: use another prefix for your own messages. Unknown messages are
/// left to the derived classes (override <c>OnMessage</c> and call the base method).
/// </remarks>
public static class PluginUIMessages
{
    /// <summary>A note played on the editor (attributes <c>kind</c>, <c>pitch</c>, <c>velocity</c>, <c>channel</c>).</summary>
    public const string Note = "PluginUI.Note";

    /// <summary>The processor announces its shared state (attribute <c>handle</c>).</summary>
    public const string SharedState = "PluginUI.SharedState";

    /// <summary>The controller asks the processor to announce its shared state.</summary>
    public const string SharedStateRequest = "PluginUI.SharedStateRequest";

    internal const string KindAttribute = "kind";
    internal const string PitchAttribute = "pitch";
    internal const string VelocityAttribute = "velocity";
    internal const string ChannelAttribute = "channel";
    internal const string HandleAttribute = "handle";

    /// <summary>Creates a message, lets <paramref name="fill"/> set its attributes and sends it to <paramref name="peer"/>.</summary>
    /// <param name="host">Host application of the sending component (creates the message).</param>
    /// <param name="peer">Connection point of the other component.</param>
    /// <param name="id">Message identifier.</param>
    /// <param name="fill">Sets the attributes (may be <c>null</c>).</param>
    /// <returns><c>false</c> if there is no host, no connection or the host could not create the message.</returns>
    public static bool TrySend(AudioHostApplication? host, IAudioConnectionPoint? peer, string id, Action<AudioAttributeList>? fill = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (host is null || peer is null || !host.TryCreateMessage(id, out var message)) return false;
        try
        {
            fill?.Invoke(message.Attributes);
            peer.Notify(message);
            return true;
        }
        finally
        {
            message.Dispose();
        }
    }

    internal static bool TrySendNote(AudioHostApplication? host, IAudioConnectionPoint? peer, UINoteEvent note)
        => TrySend(host, peer, Note, a =>
        {
            a.TrySetInt32(KindAttribute, (int)note.Kind);
            a.TrySetInt32(PitchAttribute, note.Pitch);
            a.TrySetFloat64(VelocityAttribute, note.Velocity);
            a.TrySetInt32(ChannelAttribute, note.Channel);
        });

    internal static bool TryReadNote(AudioAttributeList attributes, out UINoteEvent note)
    {
        note = default;
        if (!attributes.TryGetInt32(KindAttribute, out int kind) || (uint)kind > (uint)UINoteKind.AllNotesOff) return false;
        attributes.TryGetInt32(PitchAttribute, out int pitch);
        attributes.TryGetFloat64(VelocityAttribute, out double velocity);
        attributes.TryGetInt32(ChannelAttribute, out int channel);
        if ((UINoteKind)kind != UINoteKind.AllNotesOff && (uint)pitch > 127) return false;
        note = new UINoteEvent((UINoteKind)kind, pitch, (float)Math.Clamp(velocity, 0.0, 1.0), Math.Clamp(channel, 0, 15));
        return true;
    }
}

/// <summary>
/// Process-wide table of the objects processors share with their controllers. Only a number travels in the message,
/// so the exchange works through hosts that copy or proxy messages; both components of a VST3 plug-in live in the
/// same module and process.
/// </summary>
internal static class SharedStateRegistry
{
    private static readonly ConcurrentDictionary<long, object> Objects = new();
    private static long _next = Environment.TickCount64 << 16; // avoids reusing handles of a previous load of the module

    public static long Register(object state)
    {
        long handle = Interlocked.Increment(ref _next);
        Objects[handle] = state;
        return handle;
    }

    public static object? Find(long handle) => Objects.TryGetValue(handle, out var state) ? state : null;

    public static void Unregister(long handle) => Objects.TryRemove(handle, out _);
}
