// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace PluginUI.Host.Desktop;

/// <summary>
/// Processes the open plug-ins in real time with silent input, without an audio device: plug-ins run as in a DAW (their
/// parameters reach the processor, meters and editors that depend on processing work). Replaced by the audio engine when
/// a device is chosen.
/// </summary>
internal sealed class ProcessingPump : IDisposable
{
    public const double SampleRate = 48000.0;
    public const int BlockSize = 512;

    private readonly Lock _gate = new();
    private readonly List<PluginInstance> _plugins = new();
    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>A plug-in threw while processing; it was removed (raised on the processing thread).</summary>
    public event Action<PluginInstance, Exception>? Failed;

    /// <summary>Starts processing a prepared plug-in.</summary>
    public void Add(PluginInstance plugin)
    {
        lock (_gate) _plugins.Add(plugin);
        if (_thread is not null) return;
        _thread = new Thread(Run) { Name = "Plug-in processing", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Stops processing a plug-in; when this returns, the plug-in is not processing any more.</summary>
    public void Remove(PluginInstance plugin)
    {
        lock (_gate) _plugins.Remove(plugin);
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        long blocks = 0;
        var failed = new List<(PluginInstance, Exception)>();
        while (!_stop)
        {
            lock (_gate)
            {
                foreach (var plugin in _plugins)
                {
                    try
                    {
                        for (int c = 0; c < plugin.InputChannelCount; c++) plugin.GetInput(c).Clear();
                        plugin.Process(BlockSize);
                    }
                    catch (Exception ex)
                    {
                        failed.Add((plugin, ex));
                    }
                }
                foreach (var (plugin, _) in failed) _plugins.Remove(plugin);
            }
            foreach (var (plugin, ex) in failed) Failed?.Invoke(plugin, ex);
            failed.Clear();

            blocks++;
            double wait = blocks * BlockSize / SampleRate - clock.Elapsed.TotalSeconds;
            if (wait > 0.002) Thread.Sleep((int)(wait * 1000.0));
            else if (wait < -0.25)
            {
                // Far behind (debugger, sleep): start counting again instead of catching up.
                clock.Restart();
                blocks = 0;
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join();
        _thread = null;
    }
}
