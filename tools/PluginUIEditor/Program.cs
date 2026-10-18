// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace PluginUIEditor;

internal static class Program
{
    /// <summary>Starts the editor; an optional argument is a design file to open.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // Numbers are always written and read with a decimal point, whatever the Windows regional settings: the
        // PropertyGrid and the parameter table format through the current culture.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length >= 3 && args[0] == "--snapshot")
        {
            return Snapshot.Run(args[1], args[2], args.Length >= 4 ? args[3] : null);
        }

        var settings = EditorSettings.Load();
        EditorStrings.Select(settings.Language);
        ValueConverters.Register();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(settings, args.Length > 0 && File.Exists(args[0]) ? args[0] : null));
        return 0;
    }
}

/// <summary>
/// Screenshot mode for documentation and tests: <c>PluginUIEditor --snapshot design.pluginui out.png [en|fr]</c>
/// opens the main window with the design, selects its first named element and saves an image of the window.
/// </summary>
internal static class Snapshot
{
    public static int Run(string design, string output, string? language)
    {
        var settings = new EditorSettings { Language = language ?? "en", IsTransient = true };
        EditorStrings.Select(settings.Language);
        ValueConverters.Register();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new MainForm(settings, File.Exists(design) ? design : null) { StartPosition = FormStartPosition.Manual, Location = System.Drawing.Point.Empty, TopMost = true };
        form.Shown += (_, _) =>
        {
            var timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                form.SelectFirstNamedElement();
                Application.DoEvents();
                // A screen copy includes the native Direct2D child window, which DrawToBitmap cannot capture.
                var bounds = form.RectangleToScreen(form.ClientRectangle);
                using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                form.ForceClose();
            };
            timer.Start();
        };
        Application.Run(form);
        return File.Exists(output) ? 0 : 1;
    }
}
