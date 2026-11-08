// 2026/11/08 18:01:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst2;

/// <summary>
/// Windows message handling of a bridge and its host. A bridged editor is a child window of a host window in another
/// process: each side must handle the messages the other one sends while it waits, or both would wait for ever.
/// </summary>
internal static unsafe partial class BridgePump
{
    private const uint QsSendMessage = 0x0040;
    private const uint QsAllInput = 0x04FF;
    private const uint MwmoInputAvailable = 0x0004;
    private const uint PmNoRemove = 0x0000;
    private const uint PmRemove = 0x0001;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 0x102;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static partial int PeekMessage(Msg* msg, nint hwnd, uint first, uint last, uint remove);

    [LibraryImport("user32.dll")]
    private static partial int TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint hwnd, nuint id, uint milliseconds, delegate* unmanaged[Stdcall]<nint, uint, nuint, uint, void> callback);

    [LibraryImport("user32.dll")]
    private static partial int KillTimer(nint hwnd, nuint id);

    /// <summary>
    /// Waits for <paramref name="handle"/> up to <paramref name="milliseconds"/> while dispatching every message of this
    /// thread (bridge side: the plug-in's windows live on it). <c>true</c> when the handle was signalled (an auto-reset
    /// event is then reset, as by <see cref="WaitHandle.WaitOne(int)"/>).
    /// </summary>
    public static bool WaitDispatching(WaitHandle handle, int milliseconds)
    {
        long deadline = Environment.TickCount64 + milliseconds;
        nint native = handle.SafeWaitHandle.DangerousGetHandle();
        while (true)
        {
            uint remaining = (uint)Math.Max(0, deadline - Environment.TickCount64);
            uint result = MsgWaitForMultipleObjectsEx(1, &native, remaining, QsAllInput, MwmoInputAvailable);
            if (result == WaitObject0) return true;
            if (result == WaitObject0 + 1)
            {
                Msg msg;
                while (PeekMessage(&msg, 0, 0, 0, PmRemove) != 0)
                {
                    TranslateMessage(&msg);
                    DispatchMessage(&msg);
                }
                if (remaining == 0) return handle.WaitOne(0);
                continue;
            }
            GC.KeepAlive(handle);
            return result != WaitTimeout && handle.WaitOne(0);
        }
    }

    /// <summary>
    /// Waits for <paramref name="handle"/> up to <paramref name="milliseconds"/>, handling only the messages other threads
    /// send to this one (host side: a bridged editor's window sends some to its parent while the host waits).
    /// </summary>
    public static bool WaitHandlingSentMessages(WaitHandle handle, int milliseconds)
    {
        long deadline = Environment.TickCount64 + milliseconds;
        nint native = handle.SafeWaitHandle.DangerousGetHandle();
        while (true)
        {
            uint remaining = (uint)Math.Max(0, deadline - Environment.TickCount64);
            uint result = MsgWaitForMultipleObjectsEx(1, &native, remaining, QsSendMessage, 0);
            if (result == WaitObject0) return true;
            if (result == WaitObject0 + 1)
            {
                // Peeking handles the sent messages; posted ones stay in the queue.
                Msg msg;
                PeekMessage(&msg, 0, 0, 0, PmNoRemove | (QsSendMessage << 16));
                if (remaining == 0) return handle.WaitOne(0);
                continue;
            }
            GC.KeepAlive(handle);
            return result != WaitTimeout && handle.WaitOne(0);
        }
    }

    private static Action? s_tick;

    /// <summary>
    /// Calls <paramref name="tick"/> about every <paramref name="milliseconds"/> from this thread's message loop, including
    /// the loops of the plug-in's own dialog boxes and menus (a thread timer). One timer per thread (the bridge has one).
    /// </summary>
    public static nuint StartTimer(int milliseconds, Action tick)
    {
        s_tick = tick;
        return SetTimer(0, 0, (uint)milliseconds, &OnTimer);
    }

    /// <summary>Stops a timer of <see cref="StartTimer"/>.</summary>
    public static void StopTimer(nuint timer)
    {
        if (timer != 0) KillTimer(0, timer);
        s_tick = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnTimer(nint hwnd, uint message, nuint id, uint time)
    {
        try
        {
            s_tick?.Invoke();
        }
        catch (Exception)
        {
            // Nothing may leave a native callback.
        }
    }
}
