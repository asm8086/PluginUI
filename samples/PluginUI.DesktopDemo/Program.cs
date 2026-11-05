// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI;
using PluginUI.Desktop;
using PluginUI.DesktopDemo;

/// <summary>Entry point.</summary>
internal static class Program
{
    // Usage:
    //   BiquadDesigner [file.bqd] [--lang en|fr]                 the application
    //   BiquadDesigner --snapshot <dir> [scale] [--lang en|fr]   renders the window and its menus to PNG files (docs)
    // [STAThread]: the file dialogs, the clipboard and drag and drop are COM/OLE services that need a single-threaded
    // apartment (top-level statements cannot carry the attribute, hence this explicit Main).
    [STAThread]
    private static int Main(string[] args)
    {
        var arguments = new List<string>(args);
        string? forcedLanguage = null;
        int langIndex = arguments.IndexOf("--lang");
        if (langIndex >= 0)
        {
            forcedLanguage = langIndex + 1 < arguments.Count ? arguments[langIndex + 1] : "en";
            arguments.RemoveRange(langIndex, Math.Min(2, arguments.Count - langIndex));
        }

        bool snapshot = arguments.Count >= 1 && arguments[0] == "--snapshot";
        var app = new DesktopApplication("Biquad Designer", company: "asm8086",
            dataFolder: snapshot ? Path.Combine(Path.GetTempPath(), "BiquadDesigner-snapshot") : null);
        var settings = AppSettings<DemoSettings>.Load(app.GetDataPath("settings.json"), DemoJson.Default.DemoSettings);

        // Language: command line, then the View / Language setting, then Windows.
        string language = forcedLanguage ?? settings.Value.Language;
        UIHost.DefaultLanguage = language;
        L.French = UIStrings.ForLanguage(language).LanguageCode.StartsWith("fr", StringComparison.OrdinalIgnoreCase);

        var designer = new DesignerWindow(app, settings);
        if (snapshot)
        {
            string dir = arguments.Count >= 2 ? arguments[1] : "snapshots";
            float scale = arguments.Count >= 3 && float.TryParse(arguments[2], System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1f;
            return Snapshots.Run(designer, dir, scale);
        }

        // A file given on the command line (double-click on a .bqd associated with the program) opens at start.
        if (arguments.Count >= 1 && File.Exists(arguments[0])) designer.Host.Post(() => designer.Session.OpenFile(arguments[0], confirm: false));
        return app.Run(designer.Window);
    }
}

/// <summary>Texts of the demo in the interface language.</summary>
internal static class L
{
    public static bool French { get; set; }

    public static string T(string english, string french) => French ? french : english;
}

/// <summary>Offscreen rendering of the window for the documentation (no window is created).</summary>
internal static class Snapshots
{
    public static int Run(DesignerWindow designer, string dir, float scale)
    {
        Directory.CreateDirectory(dir);
        var host = designer.Host;
        string suffix = L.French ? "-fr" : "";
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "desktop-main" + suffix + ".png"), scale);

        // Edit menu open from the keyboard (mnemonics underlined), after one undoable edit.
        designer.Document.Bands[2].Gain.BeginEdit();
        designer.Document.Bands[2].Gain.PerformEdit(designer.Document.Bands[2].Gain.ToNormalized(6.0));
        designer.Document.Bands[2].Gain.EndEdit();
        designer.MenuBar.OpenMenu(1, fromKeyboard: true);
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "desktop-menu" + suffix + ".png"), scale);
        host.CloseAllPopups();

        // The "save changes?" question.
        var dialog = new Dialog(designer.Window.Title, new DialogText(host.Strings.FormatSaveChangesQuestion(designer.Session.DisplayName)));
        dialog.AddButton(host.Strings.Cancel, null, isCancel: true).AddButton(host.Strings.DontSave, null).AddButton(host.Strings.Save, null, isDefault: true);
        host.ShowPopup(dialog);
        Settle(host);
        host.RenderToPng(Path.Combine(dir, "desktop-save" + suffix + ".png"), scale);
        host.CloseAllPopups();

        Console.WriteLine($"Snapshots written to {Path.GetFullPath(dir)}");
        return 0;
    }

    private static void Settle(UIHost host)
    {
        host.UpdateLayout();
        for (int i = 0; i < 10; i++) host.StepFrame(1.0 / 60.0);
        host.ProcessPostedActions();
    }
}
