// 2026/11/05 15:08:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;
using PluginUI.Desktop;

namespace PluginUI.Tests;

/// <summary>Commands, shortcuts, mnemonics and the menu bar (core), and the PluginUI.Desktop helpers.</summary>
public class DesktopTests
{
    // ---------------------------------------------------------------- key gestures

    [Test]
    public void KeyGestureParsesAndFormats()
    {
        var g = KeyGesture.Parse("ctrl+shift+s");
        Assert.Multiple(() =>
        {
            Assert.That(g.Key, Is.EqualTo(Key.S));
            Assert.That(g.Modifiers, Is.EqualTo(ModifierKeys.Control | ModifierKeys.Shift));
            Assert.That(g.ToString(), Is.EqualTo("Ctrl+Shift+S"));
            Assert.That(g.ToDisplayString(UIStrings.French), Is.EqualTo("Ctrl+Maj+S"));
            Assert.That(KeyGesture.Parse("Ctrl++").Key, Is.EqualTo(Key.OemPlus));
            Assert.That(KeyGesture.Parse("Ctrl+Plus"), Is.EqualTo(KeyGesture.Parse("Ctrl++")));
            Assert.That(KeyGesture.Parse("F5"), Is.EqualTo(new KeyGesture(Key.F5)));
            Assert.That(KeyGesture.Parse("Maj+Suppr"), Is.EqualTo(new KeyGesture(Key.Delete, ModifierKeys.Shift)));
            Assert.That(KeyGesture.Parse("Alt+F4").ToString(), Is.EqualTo("Alt+F4"));
            Assert.That(new KeyGesture(Key.Delete).ToDisplayString(UIStrings.French), Is.EqualTo("Suppr"));
            Assert.That(KeyGesture.Parse(KeyGesture.Parse("Ctrl+-").ToString()).Key, Is.EqualTo(Key.OemMinus));
        });
    }

    [Test]
    public void KeyGestureRejectsInvalidText()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeyGesture.TryParse("", out _), Is.False);
            Assert.That(KeyGesture.TryParse("Ctrl", out _), Is.False);
            Assert.That(KeyGesture.TryParse("Ctrl+Shift", out _), Is.False);
            Assert.That(KeyGesture.TryParse("Ctrl+Banana", out _), Is.False);
            Assert.That(KeyGesture.TryParse("S+Ctrl", out _), Is.False);
            Assert.Throws<FormatException>(() => KeyGesture.Parse("Hyper+Q"));
        });
    }

    [Test]
    public void KeyGestureMatchesExactModifiers()
    {
        var save = KeyGesture.Parse("Ctrl+S");
        Assert.Multiple(() =>
        {
            Assert.That(save.Matches(new KeyEventArgs(Key.S, ModifierKeys.Control)), Is.True);
            Assert.That(save.Matches(new KeyEventArgs(Key.S, ModifierKeys.Control | ModifierKeys.Shift)), Is.False);
            Assert.That(save.Matches(new KeyEventArgs(Key.S, ModifierKeys.None)), Is.False);
        });
    }

    // ---------------------------------------------------------------- mnemonics

    [Test]
    public void MnemonicsAreParsedEscapedAndConverted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Mnemonics.Parse("Save &As…", out int i), Is.EqualTo("Save As…"));
            Assert.That(i, Is.EqualTo(5));
            Assert.That(Mnemonics.Parse("R&&D", out i), Is.EqualTo("R&D"));
            Assert.That(i, Is.EqualTo(-1));
            Assert.That(Mnemonics.Parse("Drum & Bass", out i), Is.EqualTo("Drum & Bass"));
            Assert.That(i, Is.EqualTo(-1));
            Assert.That(Mnemonics.Get("&Edition"), Is.EqualTo('E'));
            Assert.That(Mnemonics.Strip(Mnemonics.Escape("R&B")), Is.EqualTo("R&B"));
            Assert.That(Mnemonics.Get(Mnemonics.Escape("R&B")), Is.EqualTo('\0'));
            Assert.That(Mnemonics.ToWin32("Drum & Bass"), Is.EqualTo("Drum && Bass"));
            Assert.That(Mnemonics.ToWin32("Save &As"), Is.EqualTo("Save &As"));
            Assert.That(Mnemonics.ToWin32("R&&D"), Is.EqualTo("R&&D"));
        });
    }

    // ---------------------------------------------------------------- commands

    [Test]
    public void MenuItemReflectsItsCommand()
    {
        bool can = false, ran = false;
        var command = new Command("&Save", () => ran = true, KeyGesture.Parse("Ctrl+S")) { CanExecuteWhen = () => can };
        var item = new MenuItem(command);
        Assert.Multiple(() =>
        {
            Assert.That(item.Text, Is.EqualTo("&Save"));
            Assert.That(item.DisplayText, Is.EqualTo("Save"));
            Assert.That(item.Mnemonic, Is.EqualTo('S'));
            Assert.That(item.ShortcutText, Is.EqualTo("Ctrl+S"));
            Assert.That(item.GetShortcutText(UIStrings.French), Is.EqualTo("Ctrl+S"));
            Assert.That(item.IsEnabled, Is.False);
            Assert.That(item.Invoke(), Is.False);
        });
        can = true;
        Assert.Multiple(() =>
        {
            Assert.That(item.IsEnabled, Is.True);
            Assert.That(item.Invoke(), Is.True);
            Assert.That(ran, Is.True);
        });
        command.Text = "&Enregistrer";
        Assert.That(item.DisplayText, Is.EqualTo("Enregistrer"), "the entry follows the command text");
    }

    [Test]
    public void CheckableCommandDrivesTheCheckMark()
    {
        bool grid = true;
        var command = new Command("Grid", () => grid = !grid) { IsCheckedWhen = () => grid };
        var item = new MenuItem(command);
        Assert.That(command.IsCheckable, Is.True);
        Assert.That(item.IsChecked, Is.True);
        item.Invoke();
        Assert.That(item.IsChecked, Is.False);
        var plain = new MenuItem(new Command("Plain")) { IsChecked = true };
        Assert.That(plain.IsChecked, Is.True, "a non-checkable command leaves the entry's own check mark");
    }

    [Test]
    public void CommandSetRunsShortcutsFromTheHost()
    {
        using var host = new UIHost(new Canvas(), new Size(400f, 300f));
        int saves = 0, undos = 0;
        bool canSave = true;
        var commands = new CommandSet
        {
            new Command("Save", () => saves++, KeyGesture.Parse("Ctrl+S")) { CanExecuteWhen = () => canSave },
            new Command("Undo", () => undos++, KeyGesture.Parse("Ctrl+Z")) { AllowsRepeat = true },
        };
        commands[1].AlternateGestures.Add(KeyGesture.Parse("Alt+Back"));
        commands.Attach(host);
        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.S, ModifierKeys.Control)), Is.True);
        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.S, ModifierKeys.Control, isRepeat: true)), Is.False, "Save does not repeat");
        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.Z, ModifierKeys.Control, isRepeat: true)), Is.True, "Undo repeats");
        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.Back, ModifierKeys.Alt)), Is.True, "alternate gesture");
        canSave = false;
        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.S, ModifierKeys.Control)), Is.False, "a disabled command lets the key through");
        commands.Detach();
        Assert.That(host.DispatchKeyDown(new KeyEventArgs(Key.Z, ModifierKeys.Control)), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(undos, Is.EqualTo(2));
        });
    }

    // ---------------------------------------------------------------- menu bar

    private sealed class BarFixture : IDisposable
    {
        public BarFixture()
        {
            File.Items.Add(new MenuItem(New));
            File.Items.Add(new MenuItem("&Open…", () => Opened++) { ShortcutText = "Ctrl+O" });
            File.Items.Add(MenuItem.Separator());
            File.Items.Add(new MenuItem("E&xit", () => Exited++));
            Edit.Items.Add(new MenuItem("&Undo", () => Undone++) { ShortcutText = "Ctrl+Z" });
            Edit.Items.Add(new MenuItem("&Paste", () => Pasted++));
            Edit.Items.Add(new MenuItem("&Preferences", () => { }));
            Bar = new MenuBar(File, Edit, new MenuItem("&Help", () => Helped++)) { Dock = Side.Top };
            var root = new DockPanel();
            root.Children.Add(Bar);
            root.Children.Add(new Canvas());
            Host = new UIHost(root, new Size(600f, 400f));
            Host.UpdateLayout();
        }

        public Command New { get; } = new("&New", null, KeyGesture.Parse("Ctrl+N"));

        public MenuItem File { get; } = new("&File");

        public MenuItem Edit { get; } = new("&Edit");

        public MenuBar Bar { get; }

        public UIHost Host { get; }

        public int Opened, Exited, Undone, Pasted, Helped;

        public MenuPopup? OpenPopup => Host.Popups.OfType<MenuPopup>().LastOrDefault();

        public void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => Host.DispatchKeyDown(new KeyEventArgs(key, modifiers));

        public void Release(Key key, ModifierKeys modifiers = ModifierKeys.None) => Host.DispatchKeyUp(new KeyEventArgs(key, modifiers));

        public void Dispose() => Host.Dispose();
    }

    [Test]
    public void MenuBarOpensWithAltAndLetter()
    {
        using var f = new BarFixture();
        f.Press(Key.Alt, ModifierKeys.Alt);
        f.Press(Key.E, ModifierKeys.Alt);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(1));
        Assert.That(f.OpenPopup!.ShowMnemonics, Is.True);
        Assert.That(f.OpenPopup.HighlightedIndex, Is.EqualTo(0), "opened from the keyboard: first entry highlighted");
        f.Release(Key.E, ModifierKeys.Alt);
        f.Release(Key.Alt);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(1), "releasing Alt after Alt+letter keeps the menu open");
    }

    [Test]
    public void LoneAltEntersKeyboardModeAndArrowsNavigate()
    {
        using var f = new BarFixture();
        f.Press(Key.Alt, ModifierKeys.Alt);
        f.Release(Key.Alt);
        Assert.That(f.Bar.IsActive, Is.True);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(-1));
        f.Press(Key.Right);
        f.Press(Key.Down);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(1));
        // Right on an entry without sub-menu goes to the next title; Left comes back.
        f.Press(Key.Left);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(0));
        f.Press(Key.Escape);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(-1));
        Assert.That(f.Bar.IsActive, Is.True, "Escape in a menu goes back to the titles");
        f.Press(Key.Escape);
        Assert.That(f.Bar.IsActive, Is.False);
    }

    [Test]
    public void AltClickDoesNotActivateTheBar()
    {
        using var f = new BarFixture();
        f.Press(Key.Alt, ModifierKeys.Alt);
        f.Host.DispatchMouseDown(new Point(300f, 200f), MouseButton.Left, 1, ModifierKeys.Alt);
        f.Host.DispatchMouseUp(new Point(300f, 200f), MouseButton.Left, ModifierKeys.Alt);
        f.Release(Key.Alt);
        Assert.That(f.Bar.IsActive, Is.False);
    }

    [Test]
    public void F10TogglesTheKeyboardMode()
    {
        using var f = new BarFixture();
        f.Press(Key.F10);
        Assert.That(f.Bar.IsActive, Is.True);
        f.Press(Key.H);
        Assert.That(f.Helped, Is.EqualTo(1), "a title without sub-entries runs like a button");
        Assert.That(f.Bar.IsActive, Is.False);
        f.Press(Key.F10);
        f.Press(Key.F10);
        Assert.That(f.Bar.IsActive, Is.False);
    }

    [Test]
    public void MenuBarRunsShortcutsOfNestedEntries()
    {
        using var f = new BarFixture();
        int news = 0;
        f.New.Executed += _ => news++;
        f.Press(Key.N, ModifierKeys.Control);
        f.Press(Key.O, ModifierKeys.Control);
        f.Press(Key.Z, ModifierKeys.Control);
        Assert.Multiple(() =>
        {
            Assert.That(news, Is.EqualTo(1));
            Assert.That(f.Opened, Is.EqualTo(1), "parsed ShortcutText");
            Assert.That(f.Undone, Is.EqualTo(1));
        });
        f.Bar.HandlesShortcuts = false;
        f.Press(Key.O, ModifierKeys.Control);
        Assert.That(f.Opened, Is.EqualTo(1));
    }

    [Test]
    public void MnemonicInsideAMenuRunsAUniqueEntry()
    {
        using var f = new BarFixture();
        f.Bar.OpenMenu(0, fromKeyboard: true);
        f.Press(Key.X);
        Assert.That(f.Exited, Is.EqualTo(1));
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(-1));
        // Two entries share P: the first press only highlights.
        f.Bar.OpenMenu(1, fromKeyboard: true);
        f.Press(Key.P);
        Assert.That(f.Pasted, Is.EqualTo(0));
        Assert.That(f.OpenPopup!.HighlightedIndex, Is.EqualTo(1));
        f.Press(Key.Enter);
        Assert.That(f.Pasted, Is.EqualTo(1));
    }

    [Test]
    public void ClickingTitlesOpensAndHoverSwitches()
    {
        using var f = new BarFixture();
        var file = f.Bar.ItemRect(0).Center;
        var edit = f.Bar.ItemRect(1).Center;
        f.Host.DispatchMouseDown(file, MouseButton.Left, 1, ModifierKeys.None);
        f.Host.DispatchMouseUp(file, MouseButton.Left, ModifierKeys.None);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(0));
        Assert.That(f.OpenPopup!.ShowMnemonics, Is.False);
        f.Host.DispatchMouseMove(edit, ModifierKeys.None);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(1));
        // Clicking the open title closes its menu (and does not reopen it).
        f.Host.DispatchMouseDown(edit, MouseButton.Left, 1, ModifierKeys.None);
        f.Host.DispatchMouseUp(edit, MouseButton.Left, ModifierKeys.None);
        Assert.That(f.Bar.OpenIndex, Is.EqualTo(-1));
    }

    [Test]
    public void OpeningEventRebuildsTheMenu()
    {
        using var f = new BarFixture();
        f.Bar.Opening += (_, top) =>
        {
            if (!ReferenceEquals(top, f.Edit)) return;
            top.Items.Clear();
            top.Items.Add(new MenuItem("&Only"));
        };
        f.Bar.OpenMenu(1);
        Assert.That(f.OpenPopup!.Items, Has.Count.EqualTo(1));
    }

    // ---------------------------------------------------------------- tool bar and status bar

    private sealed class ChromeFixture : IDisposable
    {
        public ChromeFixture()
        {
            Save = new Command("&Save", () => Saves++, KeyGesture.Parse("Ctrl+S")) { CanExecuteWhen = () => CanSave, Description = "Saves the document" };
            Grid = new Command("&Grid", () => GridOn = !GridOn) { IsCheckedWhen = () => GridOn };
            var file = new MenuItem("&File");
            file.Items.Add(new MenuItem(Save));
            file.Items.Add(new MenuItem("&Close") { Description = "Closes it" });
            Menu = new MenuBar(file) { Dock = Side.Top };
            Tools = new ToolBar { Dock = Side.Top };
            SaveButton = Tools.Add(Save, ToolGlyph.Save);
            Tools.AddSeparator();
            GridButton = Tools.Add(Grid, ToolGlyph.Grid);
            Status = new StatusBar { Dock = Side.Bottom };
            Status.Segments.Add(new StatusBarSegment("Ready", float.PositiveInfinity));
            Status.Segments.Add(new StatusBarSegment(() => "48 kHz", 80f));
            Status.Follow(Menu);
            Status.Follow(Tools);
            var root = new DockPanel();
            root.Children.Add(Menu);
            root.Children.Add(Tools);
            root.Children.Add(Status);
            root.Children.Add(new Canvas());
            Host = new UIHost(root, new Size(600f, 400f));
            Host.UpdateLayout();
        }

        public Command Save { get; }

        public Command Grid { get; }

        public MenuBar Menu { get; }

        public ToolBar Tools { get; }

        public ToolBarButton SaveButton { get; }

        public ToolBarButton GridButton { get; }

        public StatusBar Status { get; }

        public UIHost Host { get; }

        public int Saves;

        public bool CanSave = true, GridOn;

        public void Click(UIElement element)
        {
            var c = element.Bounds.Center;
            Host.DispatchMouseMove(c, ModifierKeys.None);
            Host.DispatchMouseDown(c, MouseButton.Left, 1, ModifierKeys.None);
            Host.DispatchMouseUp(c, MouseButton.Left, ModifierKeys.None);
        }

        public void Dispose() => Host.Dispose();
    }

    [Test]
    public void ToolBarButtonsRunTheirCommands()
    {
        using var f = new ChromeFixture();
        Assert.That(f.Tools.Children, Has.Count.EqualTo(3));
        Assert.That(f.SaveButton.Bounds.Width, Is.GreaterThan(0f));
        Assert.That(f.GridButton.Bounds.X, Is.GreaterThan(f.SaveButton.Bounds.Right));
        f.Click(f.SaveButton);
        f.Click(f.GridButton);
        Assert.Multiple(() =>
        {
            Assert.That(f.Saves, Is.EqualTo(1));
            Assert.That(f.GridOn, Is.True);
            Assert.That(f.SaveButton.GetToolTipText(), Is.EqualTo("Save (Ctrl+S)"));
        });
        f.CanSave = false;
        Assert.That(f.SaveButton.CanRun, Is.False);
        f.Click(f.SaveButton);
        Assert.That(f.Saves, Is.EqualTo(1), "a disabled command does not run");
        // Pressing then releasing outside the button cancels.
        f.CanSave = true;
        var c = f.SaveButton.Bounds.Center;
        f.Host.DispatchMouseDown(c, MouseButton.Left, 1, ModifierKeys.None);
        f.Host.DispatchMouseUp(new Point(c.X + 200f, c.Y + 200f), MouseButton.Left, ModifierKeys.None);
        Assert.That(f.Saves, Is.EqualTo(1));
    }

    [Test]
    public void StatusBarShowsHintsAndMessages()
    {
        using var f = new ChromeFixture();
        Assert.That(f.Status.DisplayedText(0), Is.EqualTo("Ready"));
        Assert.That(f.Status.DisplayedText(1), Is.EqualTo("48 kHz"));
        var bounds = f.Status.SegmentBounds();
        Assert.That(bounds[1].Width, Is.EqualTo(80f));
        Assert.That(bounds[0].Width + bounds[1].Width, Is.EqualTo(f.Status.Bounds.Width).Within(0.01f));

        // Tool bar hover.
        f.Host.DispatchMouseMove(f.SaveButton.Bounds.Center, ModifierKeys.None);
        Assert.That(f.Status.DisplayedText(0), Is.EqualTo("Saves the document"));
        f.Host.DispatchMouseMove(new Point(300f, 200f), ModifierKeys.None);
        Assert.That(f.Status.Hint, Is.Null);

        // Menu highlight, then closing the menu clears the hint.
        f.Menu.OpenMenu(0, fromKeyboard: true);
        Assert.That(f.Status.DisplayedText(0), Is.EqualTo("Saves the document"));
        f.Host.DispatchKeyDown(new KeyEventArgs(Key.Down, ModifierKeys.None));
        Assert.That(f.Status.DisplayedText(0), Is.EqualTo("Closes it"));
        f.Menu.CloseMenu();
        Assert.That(f.Status.Hint, Is.Null);

        // Temporary message.
        f.Status.ShowMessage("Saved: a.bqd", seconds: 1.0);
        Assert.That(f.Status.DisplayedText(0), Is.EqualTo("Saved: a.bqd"));
        for (int i = 0; i < 70; i++) f.Host.StepFrame(1.0 / 60.0);
        Assert.That(f.Status.Message, Is.Null);
        Assert.That(f.Status.DisplayedText(0), Is.EqualTo("Ready"));
    }

    [Test]
    public void CommandDescriptionsReachTheMenusAndTheStrings()
    {
        var undo = new UndoStack { Strings = UIStrings.French };
        Assert.That(undo.UndoCommand.Description, Is.EqualTo("Annule la dernière modification"));
        Assert.That(new MenuItem(undo.RedoCommand).Description, Is.EqualTo("Rétablit la dernière modification annulée"));
        Assert.That(new MenuItem(undo.RedoCommand) { Description = "x" }.Description, Is.EqualTo("x"));
        Assert.That(UIStrings.French.FormatStatusSaved("a.bqd"), Is.EqualTo("Enregistré : a.bqd"));
    }

    // ---------------------------------------------------------------- undo stack

    [Test]
    public void UndoStackUndoesRedoesAndNamesSteps()
    {
        var undo = new UndoStack();
        int value = 0;
        undo.Do("Set 1", () => value = 1, () => value = 0);
        undo.Do("Set 2", () => value = 2, () => value = 1);
        Assert.That(value, Is.EqualTo(2));
        Assert.That(undo.UndoCommand.Text, Is.EqualTo("&Undo Set 2"));
        Assert.That(undo.UndoCommand.CanExecute(), Is.True);
        Assert.That(undo.RedoCommand.CanExecute(), Is.False);
        undo.Undo();
        Assert.That(value, Is.EqualTo(1));
        Assert.That(undo.RedoName, Is.EqualTo("Set 2"));
        undo.Strings = UIStrings.French;
        Assert.That(undo.RedoCommand.Text, Is.EqualTo("&Rétablir Set 2"));
        undo.Redo();
        Assert.That(value, Is.EqualTo(2));
        undo.Undo();
        undo.Do("Set 3", () => value = 3, () => value = 1);
        Assert.That(undo.CanRedo, Is.False, "a new step drops the redo history");
        Assert.That(undo.RedoCommand.Text, Is.EqualTo("&Rétablir"));
    }

    [Test]
    public void UndoStackMergesAGestureAndTracksTheSavedPoint()
    {
        var undo = new UndoStack();
        double gain = 0;
        Assert.That(undo.IsDirty, Is.False);
        for (int i = 1; i <= 5; i++)
        {
            double before = gain, after = i;
            undo.Do("Gain", () => gain = after, () => gain = before, mergeKey: "gain");
        }
        undo.EndMerge();
        Assert.That(undo.UndoCount, Is.EqualTo(1));
        Assert.That(undo.IsDirty, Is.True);
        undo.MarkSaved();
        Assert.That(undo.IsDirty, Is.False);
        undo.Undo();
        Assert.That(gain, Is.EqualTo(0));
        Assert.That(undo.IsDirty, Is.True);
        undo.Redo();
        Assert.That(gain, Is.EqualTo(5));
        Assert.That(undo.IsDirty, Is.False, "back at the saved point");
        double b2 = gain;
        undo.Do("Gain", () => gain = 6, () => gain = b2, mergeKey: "gain");
        Assert.That(undo.UndoCount, Is.EqualTo(2), "EndMerge started a new step");
    }

    [Test]
    public void UndoStackKeepsItsLimit()
    {
        var undo = new UndoStack { Limit = 3 };
        for (int i = 0; i < 10; i++) undo.Push("Step " + i, () => { }, () => { });
        Assert.That(undo.UndoCount, Is.EqualTo(3));
        Assert.That(undo.UndoName, Is.EqualTo("Step 9"));
    }

    [Test]
    public void TrackedParameterRecordsOneStepPerGesture()
    {
        var undo = new UndoStack();
        var p = new LocalParameter("Cutoff", 0, 100, 50);
        using var tracking = undo.Track(p);
        p.BeginEdit();
        p.PerformEdit(0.6);
        p.PerformEdit(0.7);
        p.PerformEdit(0.8);
        p.EndEdit();
        Assert.That(undo.UndoCount, Is.EqualTo(1));
        Assert.That(undo.UndoName, Is.EqualTo("Cutoff"));
        p.NormalizedValue = 0.2; // one-shot change (typed value, program)
        Assert.That(undo.UndoCount, Is.EqualTo(2));
        undo.Undo();
        Assert.That(p.NormalizedValue, Is.EqualTo(0.8).Within(1e-12));
        undo.Undo();
        Assert.That(p.NormalizedValue, Is.EqualTo(0.5).Within(1e-12));
        Assert.That(undo.UndoCount, Is.EqualTo(0), "undo itself records nothing");
        undo.Redo();
        Assert.That(p.NormalizedValue, Is.EqualTo(0.8).Within(1e-12));
        p.BeginEdit();
        p.EndEdit();
        Assert.That(undo.UndoCount, Is.EqualTo(1), "a gesture without change records nothing");
    }

    // ---------------------------------------------------------------- recent files

    [Test]
    public void RecentFilesKeepsNewestFirstWithoutDuplicates()
    {
        var recent = new RecentFiles { MaxCount = 3 };
        string dir = Path.Combine(Path.GetTempPath(), "pluginui-recent");
        string a = Path.Combine(dir, "a.bqd"), b = Path.Combine(dir, "b.bqd"), c = Path.Combine(dir, "c.bqd"), d = Path.Combine(dir, "d.bqd");
        recent.Add(a);
        recent.Add(b);
        recent.Add(a);
        Assert.That(recent.Items, Is.EqualTo(new[] { a, b }));
        recent.Add(c);
        recent.Add(d);
        Assert.That(recent.Items, Is.EqualTo(new[] { d, c, a }));
        Assert.That(recent.Remove(c), Is.True);
        var menu = new MenuItem("&Recent");
        string? opened = null;
        recent.FillMenu(menu, p => opened = p);
        Assert.That(menu.Items, Has.Count.EqualTo(4)); // 2 files, separator, Clear
        Assert.That(menu.Items[0].Text, Does.StartWith("&1 "));
        Assert.That(menu.Items[1].Mnemonic, Is.EqualTo('2'));
        menu.Items[1].Invoke();
        Assert.That(opened, Is.EqualTo(a));
        menu.Items[3].Invoke();
        Assert.That(recent.Items, Is.Empty);
        recent.FillMenu(menu, _ => { });
        Assert.That(menu.Items.Single().IsEnabled, Is.False);
    }

    [Test]
    public void RecentFilesCompactsLongPaths()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        string path = root + string.Join(sep, "Users", "someone", "Documents", "Audio", "Filters", "Projects", "Mastering", "warm-shelf.bqd");
        string compact = RecentFiles.Compact(path, 40);
        Assert.Multiple(() =>
        {
            Assert.That(compact.Length, Is.LessThanOrEqualTo(40));
            Assert.That(compact, Does.StartWith(root + "…"));
            Assert.That(compact, Does.EndWith("Mastering" + sep + "warm-shelf.bqd"));
            Assert.That(RecentFiles.Compact("short.bqd"), Is.EqualTo("short.bqd"));
        });
    }

    // ---------------------------------------------------------------- settings and application state

    [Test]
    public void AppSettingsRoundTripAndRecover()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pluginui-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            var settings = AppSettings<TestSettings>.Load(path, TestJson.Default.TestSettings);
            Assert.That(settings.LoadedFromFile, Is.False);
            Assert.That(settings.Value.Zoom, Is.EqualTo(1.0));
            settings.Value.Zoom = 1.5;
            settings.Value.Name = "Warm";
            Assert.That(settings.Save(), Is.True);
            var again = AppSettings<TestSettings>.Load(path, TestJson.Default.TestSettings);
            Assert.That(again.LoadedFromFile, Is.True);
            Assert.That(again.Value.Zoom, Is.EqualTo(1.5));
            Assert.That(again.Value.Name, Is.EqualTo("Warm"));
            File.WriteAllText(path, "{ not json");
            var broken = AppSettings<TestSettings>.Load(path, TestJson.Default.TestSettings);
            Assert.That(broken.LoadedFromFile, Is.False);
            Assert.That(File.Exists(path + ".bad"), Is.True);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void DesktopApplicationSavesItsState() => OnThread(ApartmentState.STA, SavesItsState);

    /// <summary>Runs on a thread of the given apartment on Windows (like a [STAThread] Main); directly elsewhere.</summary>
    internal static void OnThread(ApartmentState apartment, Action body)
    {
        if (!OperatingSystem.IsWindows())
        {
            body();
            return;
        }
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(apartment);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void SavesItsState()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pluginui-app-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = new DesktopApplication("Test App", "asm8086", dataFolder: dir);
            string file = Path.Combine(dir, "doc.bqd");
            app.RecentFiles.Add(file);
            app.MainWindowPlacement = new WindowPlacement(100, 80, 900, 600, IsMaximized: true);
            Assert.That(app.SaveState(), Is.True);
            var reloaded = new DesktopApplication("Test App", "asm8086", dataFolder: dir);
            Assert.That(reloaded.RecentFiles.Items, Is.EqualTo(new[] { file }));
            Assert.That(reloaded.MainWindowPlacement, Is.EqualTo(new WindowPlacement(100, 80, 900, 600, true)));
            Assert.That(reloaded.GetDataPath("x.json"), Is.EqualTo(Path.Combine(dir, "x.json")));
            Assert.That(DesktopApplication.DefaultDataFolder("A:B", "Co"), Does.EndWith(Path.Combine("Co", "A_B")).Or.EndWith(Path.Combine("Co", "A:B")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

public class DesktopApplicationApartmentTests
{
    [Test]
    public void DesktopApplicationRefusesAMultithreadedApartmentOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Apartments exist on Windows only.");
        Exception? thrown = null;
        DesktopTests.OnThread(ApartmentState.MTA, () =>
        {
            try { _ = new DesktopApplication("Test App", dataFolder: Path.GetTempPath()); }
            catch (InvalidOperationException ex) { thrown = ex; }
        });
        Assert.That(thrown?.Message, Does.Contain("STAThread"));
    }
}

public sealed class TestSettings
{
    public double Zoom { get; set; } = 1.0;

    public string? Name { get; set; }
}

[JsonSerializable(typeof(TestSettings))]
internal sealed partial class TestJson : JsonSerializerContext;
