// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Host.Catalog;
using PluginUI.Host.Vst2;

namespace PluginUI.Host.Bridge;

/// <summary>
/// The 32-bit bridge: started by a 64-bit host, never by the user.
/// <code>
///   PluginHost.Bridge32 bridge &lt;channel&gt; &lt;host process id&gt; &lt;module&gt; &lt;plug-in or -&gt; &lt;host name&gt;
///   PluginHost.Bridge32 scan-module --format vst2 --out &lt;file.json&gt; &lt;module&gt;
/// </code>
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Vst2Hosting.Register();
        int scan = Array.IndexOf(args, ModuleScanProcess.CommandName);
        if (scan >= 0) return ModuleScanProcess.Run(args[(scan + 1)..]);
        int bridge = Array.IndexOf(args, Vst2BridgeServer.CommandName);
        if (bridge >= 0) return Vst2BridgeServer.Run(args[(bridge + 1)..]);
        Console.Error.WriteLine("PluginHost.Bridge32 runs 32-bit VST 2 plug-ins for Plug-in Host and pluginhost; it is started by them.");
        return 1;
    }
}
