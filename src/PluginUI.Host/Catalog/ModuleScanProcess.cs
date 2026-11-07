// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUI.Host.Catalog;

/// <summary>
/// Child side of out-of-process scanning: a host executable that receives
/// <c>scan-module --format vst3|vst2 --out &lt;file.json&gt; &lt;module&gt;</c> calls <see cref="Run"/> and exits with its
/// result (see <see cref="OutOfProcessModuleScanner"/>).
/// </summary>
/// <remarks>
/// The result goes to a file rather than to the standard output, which plug-ins sometimes write to. Progress lines on the
/// standard error tell the parent that the scan is alive (a shell module with hundreds of plug-ins takes minutes). On
/// Windows the module is loaded on a single-threaded apartment thread, as in a DAW: plug-ins that initialize COM or OLE
/// for their user interface fail otherwise.
/// </remarks>
public static class ModuleScanProcess
{
    /// <summary>Command name the scanner passes to the child executable.</summary>
    public const string CommandName = "scan-module";

    internal const string ProgressPrefix = "\u0001pluginhost-progress ";
    internal const string ErrorPrefix = "\u0001pluginhost-error ";

    /// <summary>Runs the command; <paramref name="arguments"/> are those after <see cref="CommandName"/>.</summary>
    /// <returns>Exit code: 0 success, 1 wrong arguments, 2 the module could not be described.</returns>
    public static int Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string format = "vst3";
        string? output = null, module = null;
        for (int i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--format" when i + 1 < arguments.Count:
                    format = arguments[++i];
                    break;
                case "--out" when i + 1 < arguments.Count:
                    output = arguments[++i];
                    break;
                default:
                    module ??= arguments[i];
                    break;
            }
        }
        if (module is null || output is null)
        {
            Console.Error.WriteLine(ErrorPrefix + $"Usage: {CommandName} --format vst3|vst2 --out <file.json> <module>");
            return 1;
        }

        int exitCode = 0;
        RunOnUserInterfaceThread(() =>
        {
            try
            {
                var found = new InProcessModuleScanner().Scan(module, format == "vst2" ? PluginFormat.Vst2 : PluginFormat.Vst3,
                    (done, count) => Console.Error.WriteLine(ProgressPrefix + done.ToString(CultureInfo.InvariantCulture) + "/" + count.ToString(CultureInfo.InvariantCulture)));
                File.WriteAllText(output, PluginCatalog.ToJson(found));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ErrorPrefix + ex.Message.ReplaceLineEndings(" "));
                exitCode = 2;
            }
        });
        return exitCode;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a single-threaded apartment thread on Windows (the current thread if it is one),
    /// directly elsewhere.
    /// </summary>
    public static void RunOnUserInterfaceThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!OperatingSystem.IsWindows() || Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }, 16 * 1024 * 1024) { Name = "Plug-in scan" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException(error.Message, error);
    }
}
