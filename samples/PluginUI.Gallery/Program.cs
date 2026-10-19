// 2026/10/19 22:45:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI;
using PluginUI.Gallery;

// Usage:
//   PluginUI.Gallery [--lang en|fr]                         interactive window (English by default)
//   PluginUI.Gallery --snapshot <dir> [scale] [--lang fr]   renders every page (and a few popups) to PNG files, no window
var arguments = new List<string>(args);
int langIndex = arguments.IndexOf("--lang");
if (langIndex >= 0)
{
    L.French = langIndex + 1 < arguments.Count && arguments[langIndex + 1].StartsWith("fr", StringComparison.OrdinalIgnoreCase);
    arguments.RemoveRange(langIndex, Math.Min(2, arguments.Count - langIndex));
}
// The texts drawn by PluginUI itself (tooltips, dialogs, menus) follow the same language.
UIHost.DefaultLanguage = L.French ? "fr" : "en";

if (arguments.Count >= 1 && arguments[0] == "--snapshot")
{
    string dir = arguments.Count >= 2 ? arguments[1] : "snapshots";
    float scale = arguments.Count >= 3 && float.TryParse(arguments[2], System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1.5f;
    return Snapshots.Run(dir, scale);
}

StandaloneWindow.EnablePerMonitorDpiAwareness();
var app = new GalleryApp();
return new StandaloneWindow(app.Host, L.T("PluginUI — gallery", "PluginUI — galerie")).Run();

/// <summary>Offscreen rendering of the gallery pages (documentation and visual regression checks).</summary>
internal static class Snapshots
{
    public static int Run(string dir, float scale)
    {
        Directory.CreateDirectory(dir);
        var app = new GalleryApp();
        var host = app.Host;
        string[] names = ["1-rotary-linear", "2-buttons-menus", "3-text-pickers", "4-meters-midi", "5-containers-lists", "6-skins-styles", "10-instrument", "11-analysis", "15-response"];
        for (int page = 0; page < names.Length; page++)
        {
            app.Tabs.SelectedIndex = page;
            Settle(app);
            host.RenderToPng(Path.Combine(dir, names[page] + ".png"), scale);
        }

        // Light theme.
        app.SetTheme(light: true);
        app.Tabs.SelectedIndex = 0;
        Settle(app);
        host.RenderToPng(Path.Combine(dir, "7-light-theme.png"), scale);
        app.SetTheme(light: false);

        // Popups: menu and colour picker on the text page, live edit on the first page.
        app.Tabs.SelectedIndex = 1;
        Settle(app);
        var items = new List<MenuItem> { new(L.T("Open…", "Ouvrir…")) { ShortcutText = "Ctrl+O" }, new(L.T("Save", "Enregistrer")) { ShortcutText = "Ctrl+S" }, MenuItem.Separator(), new(L.T("Recent", "Récents")) { IsChecked = true } };
        items[3].Items.Add(new MenuItem("Preset A"));
        host.ShowMenu(items, new Point(440f, 330f));
        host.RenderToPng(Path.Combine(dir, "8-menu.png"), scale);
        host.CloseAllPopups();

        // MIDI learn waiting on the instrument page.
        app.Tabs.SelectedIndex = 6;
        var midi = new MidiLearnService();
        host.MidiLearn = midi;
        if (host.Root.DescendantsAndSelf().OfType<Slider>().LastOrDefault(s => s.Parameter is not null) is { Parameter: { } learn }) midi.BeginLearn(learn);
        Settle(app);
        host.RenderToPng(Path.Combine(dir, "12-midi-learn.png"), scale);
        host.MidiLearn = null;

        app.Tabs.SelectedIndex = 0;
        Settle(app);
        host.IsLiveEditEnabled = true;
        host.LiveEditor.Selected = host.Root.DescendantsAndSelf().OfType<XYPad>().FirstOrDefault();
        host.RenderToPng(Path.Combine(dir, "9-live-edit.png"), scale);
        host.IsLiveEditEnabled = false;

        Console.WriteLine($"Snapshots written to {Path.GetFullPath(dir)}");
        return 0;
    }

    private static void Settle(GalleryApp app)
    {
        app.Host.UpdateLayout();
        for (int i = 0; i < 40; i++)
        {
            app.Audio.Generate(800);
            app.Host.StepFrame(1.0 / 60.0);
        }
        app.Host.ProcessPostedActions();
    }
}
