// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics;
using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

public sealed unsafe partial class UIHost
{
    private const nuint FrameTimerId = 1;

    private readonly List<ITickable> _tickers = new();
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _frameTimerRunning;
    private double _lastTick;

    /// <summary>Interval of the frame timer (animations, meters). Default 16 ms (~60 fps).</summary>
    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(16);

    /// <summary>Raised on every frame-timer tick while animations or tickers are active.</summary>
    public event Action<UIHost, double>? FrameTick;

    /// <summary>
    /// Registers a per-frame callback (meters, scopes, custom animations). It runs on the UI thread until
    /// <see cref="ITickable.Tick"/> returns <c>false</c> or <see cref="RemoveTicker"/> is called.
    /// </summary>
    public void AddTicker(ITickable ticker)
    {
        ArgumentNullException.ThrowIfNull(ticker);
        if (!_tickers.Contains(ticker)) _tickers.Add(ticker);
        EnsureFrameTimer();
    }

    /// <summary>Unregisters a per-frame callback.</summary>
    public void RemoveTicker(ITickable ticker) => _tickers.Remove(ticker);

    /// <summary>Starts the frame timer if it is not running (called by the animator and tickers).</summary>
    public void EnsureFrameTimer()
    {
        if (_frameTimerRunning || _hwnd == HWND.NULL) return;
        _lastTick = _clock.Elapsed.TotalSeconds;
        uint ms = (uint)Math.Clamp(FrameInterval.TotalMilliseconds, 1, 1000);
        _frameTimerRunning = SetTimer(_hwnd, FrameTimerId, ms, null) != 0;
    }

    private void StopFrameTimer()
    {
        if (!_frameTimerRunning) return;
        _frameTimerRunning = false;
        if (_hwnd != HWND.NULL) KillTimer(_hwnd, FrameTimerId);
    }

    private void OnNativeTimer(nuint id)
    {
        if (id == ToolTipTimerId)
        {
            OnToolTipTimer();
            return;
        }
        if (id == PaintRetryTimerId)
        {
            if (_hwnd != HWND.NULL)
            {
                KillTimer(_hwnd, PaintRetryTimerId);
                Invalidate();
            }
            return;
        }
        if (id != FrameTimerId) return;

        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTick, 0.0, 0.1);
        _lastTick = now;
        StepFrame(dt);
        if (!Animator.IsRunning && _tickers.Count == 0 && FrameTick is null) StopFrameTimer();
    }

    /// <summary>Advances animations and tickers by <paramref name="dt"/> seconds (driven by the frame timer; public for tests and snapshots).</summary>
    public void StepFrame(double dt)
    {
        bool animated = false;
        try
        {
            animated = Animator.Step(dt);
        }
        catch (Exception ex)
        {
            Animator.CancelAll();
            ReportException(ex, "Animation");
        }

        if (_tickers.Count > 0)
        {
            foreach (var ticker in _tickers.ToArray())
            {
                bool keep;
                try
                {
                    keep = ticker.Tick(dt);
                }
                catch (Exception ex)
                {
                    ReportException(ex, "Ticker");
                    keep = false;
                }
                if (!keep) _tickers.Remove(ticker);
            }
        }

        try
        {
            FrameTick?.Invoke(this, dt);
        }
        catch (Exception ex)
        {
            ReportException(ex, "FrameTick");
        }

        if (animated) Invalidate();
    }

    // =====================================================================================
    // Cross-thread marshaling
    // =====================================================================================

    /// <summary><c>true</c> on the UI thread (or before the window exists).</summary>
    public bool CheckAccess() => _uiThreadId == 0 || _uiThreadId == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread (thread-safe). Actions posted while detached run after the next attach.
    /// </summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _posted.Enqueue(action);
        var hwnd = _hwnd;
        if (hwnd != HWND.NULL) PostMessageW(hwnd, Win32.WM_APP + 1, 0, 0);
    }

    /// <summary>Runs the posted actions now (UI thread; used by tests and offscreen hosts).</summary>
    public void ProcessPostedActions()
    {
        int budget = 1000; // never starve the message loop
        while (budget-- > 0 && _posted.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ReportException(ex, "Posted action");
            }
        }
        if (!_posted.IsEmpty && _hwnd != HWND.NULL) PostMessageW(_hwnd, Win32.WM_APP + 1, 0, 0);
    }
}
