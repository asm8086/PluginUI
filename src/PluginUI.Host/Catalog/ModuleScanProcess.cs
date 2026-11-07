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
/// standard error tell the parent that the scan is alive (a shell module with hundreds of plug-ins takes minutes), and
/// step lines where a plug-in that crashes stopped (<see cref="Vst3.Vst3Module.Trace"/>). The result is written before the
/// module is released; the parent stops a child that does not exit soon after. On
/// Windows the module is loaded on a single-threaded apartment thread, as in a DAW: plug-ins that initialize COM or OLE
/// for their user interface fail otherwise.
/// </remarks>
public static class ModuleScanProcess
{
    /// <summary>Command name the scanner passes to the child executable.</summary>
    public const string CommandName = "scan-module";

    internal const string ProgressPrefix = "\u0001pluginhost-progress ";
    internal const string ErrorPrefix = "\u0001pluginhost-error ";
    internal const string StepPrefix = "\u0001pluginhost-step ";
    internal const string DoneStep = "result written";

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
        Vst3.Vst3Module.Trace = step => Console.Error.WriteLine(StepPrefix + step);
        RunOnUserInterfaceThread(() =>
        {
            try
            {
                void Progress(int done, int count)
                    => Console.Error.WriteLine(ProgressPrefix + done.ToString(CultureInfo.InvariantCulture) + "/" + count.ToString(CultureInfo.InvariantCulture));
                IReadOnlyList<PluginDescription> found;
                Vst3.Vst3Module? loaded = null;
                if (format == "vst2")
                {
                    found = new InProcessModuleScanner().Scan(module, PluginFormat.Vst2, Progress);
                }
                else
                {
                    loaded = Vst3.Vst3Module.Load(module);
                    found = InProcessModuleScanner.DescribeVst3(loaded, Progress);
                }
                // The result is written completely (temporary file, then renamed) before the module is released: some
                // plug-ins crash in ExitDll or keep threads that hang the exit; the parent then stops this process.
                string temporary = output + ".tmp";
                File.WriteAllText(temporary, PluginCatalog.ToJson(found));
                File.Move(temporary, output, overwrite: true);
                Console.Error.WriteLine(StepPrefix + DoneStep);
                loaded?.Dispose();
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
