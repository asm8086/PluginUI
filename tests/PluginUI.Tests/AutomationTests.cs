// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Automation;

namespace PluginUI.Tests;

/// <summary>Accessibility tree, patterns and events (platform-neutral layer; the Windows bridge only translates).</summary>
public class AutomationTests
{
    private sealed class Spy : IAutomationEventSink
    {
        public List<string> Log { get; } = new();

        public bool IsListening => true;

        public void FocusChanged(AutomationPeer peer) => Log.Add($"focus {peer.ControlType} {peer.Name}");

        public void PropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
            => Log.Add($"{property} {peer.Name}={newValue}");

        public void EventRaised(AutomationPeer peer, AutomationEvent kind) => Log.Add($"{kind} {peer.ControlType} {peer.Name}");

        public void StructureChanged(AutomationPeer peer) => Log.Add("structure");

        public void Announce(AutomationPeer source, string text, AnnouncementPriority priority) => Log.Add($"announce {text}");
    }

    private static (UIHost Host, Canvas Root, Spy Spy) CreateHost(float width = 400f, float height = 300f)
    {
        var root = new Canvas();
        var host = new UIHost(root, new Size(width, height));
        var spy = new Spy();
        host.AutomationSink = spy;
        return (host, root, spy);
    }

    private static T Add<T>(Canvas root, T element, float x, float y, float w, float h) where T : UIElement
    {
        element.SetBounds(x, y, w, h);
        root.Add(element);
        return element;
    }

    private static List<string> Describe(AutomationPeer peer)
        => peer.GetChildren().Select(c => $"{c.ControlType} {c.Name}").ToList();

    /// <summary>Every child names its parent: the tree reads the same from the top and from the leaves.</summary>
    private static void AssertConsistent(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            Assert.That(child.GetParent(), Is.SameAs(peer), $"parent of {child}");
            AssertConsistent(child);
        }
    }

    [Test]
    public void TreeFlattensLayoutPanelsAndNamesControls()
    {
        var (host, root, _) = CreateHost();
        var title = Add(root, new Label("Filter"), 0, 0, 200, 20);
        var stack = Add(root, new StackPanel(), 0, 30, 200, 200);
        stack.Children.Add(new Knob(new LocalParameter("Cutoff", 20, 20000, 1000, "Hz")) { Height = 80 });
        stack.Children.Add(new Button("Save"));
        stack.Children.Add(new CheckBox("Bypass"));
        stack.Children.Add(new DropDown("Low pass", "High pass") { Label = "Type" });
        host.UpdateLayout();

        Assert.That(Describe(host.RootAutomationPeer), Is.EqualTo(new[]
        {
            "Text Filter", "Slider Cutoff", "Button Save", "CheckBox Bypass", "ComboBox Type",
        }));
        AssertConsistent(host.RootAutomationPeer);
        Assert.That(title.GetAutomationPeer()!.IsKeyboardFocusable, Is.False);
    }

    [Test]
    public void NameComesFromAutomationNameThenLabelThenToolTip()
    {
        var (host, root, spy) = CreateHost();
        var caption = Add(root, new Label("Drive"), 0, 0, 60, 20);
        var knob = Add(root, new Knob { AutomationLabeledBy = caption }, 0, 20, 60, 60);
        var icon = Add(root, new Button { ToolTip = "Open preset" }, 100, 0, 24, 24);
        var named = Add(root, new Button("X") { AutomationName = "Close", ToolTip = "Closes the panel" }, 140, 0, 24, 24);
        host.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(knob.GetAutomationPeer()!.Name, Is.EqualTo("Drive"));
            Assert.That(icon.GetAutomationPeer()!.Name, Is.EqualTo("Open preset"));
            Assert.That(icon.GetAutomationPeer()!.HelpText, Is.Null, "the tooltip is not repeated");
            Assert.That(named.GetAutomationPeer()!.Name, Is.EqualTo("Close"));
            Assert.That(named.GetAutomationPeer()!.HelpText, Is.EqualTo("Closes the panel"));
        });

        named.AutomationName = "Close panel";
        Assert.That(spy.Log, Does.Contain("Name Close panel=Close panel"));
    }

    [Test]
    public void HiddenElementsAreLeftOut()
    {
        var (host, root, _) = CreateHost();
        Add(root, new Button("Shown"), 0, 0, 60, 24);
        Add(root, new Button("Hidden") { IsVisible = false }, 0, 30, 60, 24);
        var decoration = Add(root, new StackPanel { AutomationHidden = true }, 0, 60, 60, 60);
        decoration.Children.Add(new Button("Inside"));
        host.UpdateLayout();
        Assert.That(Describe(host.RootAutomationPeer), Is.EqualTo(new[] { "Button Shown" }));
    }

    [Test]
    public void KnobExposesPlainRangeAndFormattedValue()
    {
        var (host, root, spy) = CreateHost();
        var parameter = new LocalParameter("Gain", -24, 24, 0, "dB", decimals: 1);
        var knob = Add(root, new Knob(parameter), 0, 0, 60, 80);
        host.UpdateLayout();
        var peer = knob.GetAutomationPeer()!;
        var range = (IRangeValuePattern)peer.GetPattern(AutomationPattern.RangeValue)!;
        var value = (IValuePattern)peer.GetPattern(AutomationPattern.Value)!;

        Assert.Multiple(() =>
        {
            Assert.That(range.Minimum, Is.EqualTo(-24.0));
            Assert.That(range.Maximum, Is.EqualTo(24.0));
            Assert.That(range.Value, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(value.Value, Is.EqualTo(parameter.ToDisplayStringWithUnits(parameter.NormalizedValue)));
            Assert.That(peer.LocalizedControlType, Is.EqualTo(UIStrings.English.AutomationKnob));
        });

        var edits = new List<string>();
        parameter.EditStarted += _ => edits.Add("begin");
        parameter.EditEnded += _ => edits.Add("end");
        range.SetValue(12.0);
        Assert.That(parameter.PlainValue(), Is.EqualTo(12.0).Within(1e-6));
        Assert.That(edits, Is.EqualTo(new[] { "begin", "end" }), "one complete gesture, as a host expects");
        Assert.That(spy.Log, Has.Some.StartsWith("RangeValue Gain=12"));
        Assert.That(spy.Log, Has.Some.StartsWith("Value Gain="));

        value.SetValue("-6");
        Assert.That(parameter.PlainValue(), Is.EqualTo(-6.0).Within(1e-6));

        knob.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => range.SetValue(3.0));
    }

    [Test]
    public void UnboundSliderUsesPercent()
    {
        var (host, root, _) = CreateHost();
        var slider = Add(root, new Slider(Orientation.Horizontal) { Label = "Mix", Value = 0.25 }, 0, 0, 200, 30);
        host.UpdateLayout();
        var peer = slider.GetAutomationPeer()!;
        var range = (IRangeValuePattern)peer.GetPattern(AutomationPattern.RangeValue)!;
        Assert.Multiple(() =>
        {
            Assert.That(range.Maximum, Is.EqualTo(100.0));
            Assert.That(range.Value, Is.EqualTo(25.0).Within(1e-9));
            Assert.That(peer.Orientation, Is.EqualTo(AutomationOrientation.Horizontal));
        });
        range.SetValue(75);
        Assert.That(slider.Value, Is.EqualTo(0.75).Within(1e-9));
    }

    [Test]
    public void CheckBoxToggles()
    {
        var (host, root, spy) = CreateHost();
        var box = Add(root, new CheckBox("Bypass"), 0, 0, 100, 24);
        host.UpdateLayout();
        var toggle = (ITogglePattern)box.GetAutomationPeer()!.GetPattern(AutomationPattern.Toggle)!;
        Assert.That(toggle.ToggleState, Is.EqualTo(ToggleState.Off));
        toggle.Toggle();
        Assert.Multiple(() =>
        {
            Assert.That(box.IsChecked, Is.True);
            Assert.That(toggle.ToggleState, Is.EqualTo(ToggleState.On));
            Assert.That(spy.Log, Does.Contain("ToggleState Bypass=On"));
            Assert.That(box.GetAutomationPeer()!.GetPattern(AutomationPattern.RangeValue), Is.Null);
        });
    }

    [Test]
    public void ButtonInvokes()
    {
        var (host, root, spy) = CreateHost();
        int clicks = 0;
        var button = Add(root, new Button("Save", () => clicks++), 0, 0, 80, 24);
        host.UpdateLayout();
        ((IInvokePattern)button.GetAutomationPeer()!.GetPattern(AutomationPattern.Invoke)!).Invoke();
        Assert.That(clicks, Is.EqualTo(1));
        Assert.That(spy.Log, Does.Contain("Invoked Button Save"));
    }

    [Test]
    public void RadioGroupExposesOptionsAndFocusesTheSelectedOne()
    {
        var (host, root, spy) = CreateHost();
        var group = Add(root, new RadioGroup("Sine", "Saw", "Square") { Label = "Wave" }, 0, 0, 120, 80);
        host.UpdateLayout();
        var peer = group.GetAutomationPeer()!;
        var options = peer.GetChildren();
        Assert.That(options.Select(o => $"{o.ControlType} {o.Name} {o.PositionInSet}/{o.SizeOfSet}"), Is.EqualTo(new[]
        {
            "RadioButton Sine 1/3", "RadioButton Saw 2/3", "RadioButton Square 3/3",
        }));
        AssertConsistent(peer);

        group.Focus();
        Assert.That(host.GetFocusedAutomationPeer(), Is.SameAs(options[0]));
        ((ISelectionItemPattern)options[2].GetPattern(AutomationPattern.SelectionItem)!).Select();
        Assert.Multiple(() =>
        {
            Assert.That(group.SelectedIndex, Is.EqualTo(2));
            Assert.That(host.GetFocusedAutomationPeer(), Is.SameAs(options[2]));
            Assert.That(spy.Log, Does.Contain("ElementSelected RadioButton Square"));
            Assert.That(spy.Log.Last(), Is.EqualTo("focus RadioButton Square"));
            Assert.That(((ISelectionPattern)peer.GetPattern(AutomationPattern.Selection)!).GetSelection(), Is.EqualTo(new[] { options[2] }));
        });

        // The option under the mouse is found through the element under it.
        var center = options[1].BoundingRectangle.Center;
        Assert.That(host.AutomationPeerAt(center), Is.SameAs(options[1]));
    }

    [Test]
    public void ArrowKeysOnARadioGroupMoveTheAutomationFocus()
    {
        var (host, root, spy) = CreateHost();
        var group = Add(root, new RadioGroup("A", "B", "C"), 0, 0, 120, 80);
        host.UpdateLayout();
        group.Focus();
        spy.Log.Clear();
        host.DispatchKeyDown(new KeyEventArgs(Key.Down, ModifierKeys.None));
        Assert.That(group.SelectedIndex, Is.EqualTo(1));
        Assert.That(spy.Log.Where(l => l.StartsWith("focus", StringComparison.Ordinal)), Is.EqualTo(new[] { "focus RadioButton B" }));
    }

    [Test]
    public void DropDownExpandsAndReportsItsMenu()
    {
        var (host, root, spy) = CreateHost();
        var drop = Add(root, new DropDown("Room", "Hall", "Plate") { Label = "Algorithm" }, 0, 0, 120, 28);
        host.UpdateLayout();
        var peer = drop.GetAutomationPeer()!;
        var expand = (IExpandCollapsePattern)peer.GetPattern(AutomationPattern.ExpandCollapse)!;
        var value = (IValuePattern)peer.GetPattern(AutomationPattern.Value)!;
        Assert.That(value.Value, Is.EqualTo("Room"));

        expand.Expand();
        host.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(expand.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Expanded));
            Assert.That(spy.Log, Does.Contain("ExpandCollapseState Algorithm=Expanded"));
            Assert.That(spy.Log, Does.Contain("MenuOpened Menu "));
        });
        var menu = host.RootAutomationPeer.GetChildren().Last();
        Assert.That(Describe(menu), Is.EqualTo(new[] { "MenuItem Room", "MenuItem Hall", "MenuItem Plate" }));
        Assert.That(((ITogglePattern)menu.GetChildren()[0].GetPattern(AutomationPattern.Toggle)!).ToggleState, Is.EqualTo(ToggleState.On), "the selected entry is checked");
        AssertConsistent(host.RootAutomationPeer);

        ((IInvokePattern)menu.GetChildren()[1].GetPattern(AutomationPattern.Invoke)!).Invoke();
        Assert.Multiple(() =>
        {
            Assert.That(drop.SelectedIndex, Is.EqualTo(1));
            Assert.That(value.Value, Is.EqualTo("Hall"));
            Assert.That(expand.ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Collapsed));
            Assert.That(spy.Log, Does.Contain("MenuClosed Menu "));
        });

        value.SetValue("plate");
        Assert.That(drop.SelectedIndex, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => value.SetValue("Spring"));
    }

    [Test]
    public void MenuBarKeyboardModeMovesTheFocusThroughTitlesAndEntries()
    {
        var (host, root, spy) = CreateHost();
        var file = new MenuItem("&File");
        file.Items.Add(new MenuItem("&Open") { ShortcutText = "Ctrl+O" });
        file.Items.Add(MenuItem.Separator());
        file.Items.Add(new MenuItem("E&xit"));
        var edit = new MenuItem("&Edit");
        edit.Items.Add(new MenuItem("&Undo"));
        var bar = Add(root, new MenuBar(file, edit), 0, 0, 400, 26);
        host.UpdateLayout();

        Assert.That(Describe(bar.GetAutomationPeer()!), Is.EqualTo(new[] { "MenuItem File", "MenuItem Edit" }));
        var titles = bar.GetAutomationPeer()!.GetChildren();
        Assert.That(titles[0].AccessKey, Is.EqualTo("Alt+F"));

        host.DispatchKeyDown(new KeyEventArgs(Key.F10, ModifierKeys.None));
        Assert.That(spy.Log.Last(), Is.EqualTo("focus MenuItem File"));
        host.DispatchKeyDown(new KeyEventArgs(Key.Right, ModifierKeys.None));
        Assert.That(spy.Log.Last(), Is.EqualTo("focus MenuItem Edit"));
        host.DispatchKeyDown(new KeyEventArgs(Key.Left, ModifierKeys.None));

        spy.Log.Clear();
        host.DispatchKeyDown(new KeyEventArgs(Key.Down, ModifierKeys.None));
        host.UpdateLayout();
        Assert.That(spy.Log, Is.EqualTo(new[] { "MenuOpened Menu ", "structure", "focus MenuItem Open" }), "one focus event, on the entry");
        Assert.That(((IExpandCollapsePattern)titles[0].GetPattern(AutomationPattern.ExpandCollapse)!).ExpandCollapseState, Is.EqualTo(ExpandCollapseState.Expanded));

        var menu = host.RootAutomationPeer.GetChildren().Last();
        var entries = menu.GetChildren();
        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(e => e.ControlType), Is.EqualTo(new[] { AutomationControlType.MenuItem, AutomationControlType.Separator, AutomationControlType.MenuItem }));
            Assert.That(entries[0].AcceleratorKey, Is.EqualTo("Ctrl+O"));
            Assert.That(entries[0].AccessKey, Is.EqualTo("O"));
            Assert.That(entries[2].PositionInSet, Is.EqualTo(2));
            Assert.That(entries[2].SizeOfSet, Is.EqualTo(2));
        });

        host.DispatchKeyDown(new KeyEventArgs(Key.Down, ModifierKeys.None));
        Assert.That(spy.Log.Last(), Is.EqualTo("focus MenuItem Exit"), "the separator is skipped");

        host.DispatchKeyDown(new KeyEventArgs(Key.Escape, ModifierKeys.None));
        Assert.That(spy.Log, Does.Contain("MenuClosed Menu "));
        Assert.That(spy.Log.Last(), Is.EqualTo("focus MenuItem File"), "Esc goes back to the title");
    }

    [Test]
    public void ModalDialogHidesTheContentBelow()
    {
        var (host, root, _) = CreateHost();
        Add(root, new Button("Behind"), 0, 0, 80, 24);
        host.UpdateLayout();
        var dialog = Dialog.ShowMessage(host, "Confirm", "Delete the preset?", MessageBoxButtons.YesNo);
        host.UpdateLayout();
        var children = host.RootAutomationPeer.GetChildren();
        Assert.That(children, Has.Count.EqualTo(1));
        var peer = children[0];
        Assert.Multiple(() =>
        {
            Assert.That(peer.Name, Is.EqualTo("Confirm"));
            Assert.That(peer.IsDialog, Is.True);
            Assert.That(peer.LocalizedControlType, Is.EqualTo(UIStrings.English.AutomationDialog));
            Assert.That(peer.GetChildren().Select(c => c.Name), Does.Contain("Delete the preset?"));
        });
        AssertConsistent(host.RootAutomationPeer);
        dialog.Close(null);
    }

    [Test]
    public void TabViewListsItsTabsThenThePage()
    {
        var (host, root, spy) = CreateHost();
        var tabs = Add(root, new TabView(), 0, 0, 300, 200);
        tabs.AddPage("Oscillator", new Button("Sync"));
        tabs.AddPage("Filter", new Button("Reset"));
        host.UpdateLayout();
        var peer = tabs.GetAutomationPeer()!;
        Assert.That(Describe(peer), Is.EqualTo(new[] { "TabItem Oscillator", "TabItem Filter", "Button Sync" }));

        var filter = peer.GetChildren()[1];
        ((ISelectionItemPattern)filter.GetPattern(AutomationPattern.SelectionItem)!).Select();
        Assert.Multiple(() =>
        {
            Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
            Assert.That(Describe(peer), Is.EqualTo(new[] { "TabItem Oscillator", "TabItem Filter", "Button Reset" }));
            Assert.That(spy.Log, Does.Contain("ElementSelected TabItem Filter"));
            Assert.That(filter.BoundingRectangle.Width, Is.GreaterThan(0f));
        });
        AssertConsistent(peer);
    }

    [Test]
    public void ListBoxEntriesAreSelectable()
    {
        var (host, root, _) = CreateHost();
        var list = Add(root, new ListBox(new List<string> { "Init", "Bass", "Lead" }) { MultiSelect = true }, 0, 0, 160, 120);
        host.UpdateLayout();
        var peer = list.GetAutomationPeer()!;
        var items = peer.GetChildren();
        Assert.That(Describe(peer), Is.EqualTo(new[] { "ListItem Init", "ListItem Bass", "ListItem Lead" }));
        ((ISelectionItemPattern)items[1].GetPattern(AutomationPattern.SelectionItem)!).Select();
        ((ISelectionItemPattern)items[2].GetPattern(AutomationPattern.SelectionItem)!).AddToSelection();
        Assert.That(list.SelectedIndices.Order(), Is.EqualTo(new[] { 1, 2 }));
        ((ISelectionItemPattern)items[1].GetPattern(AutomationPattern.SelectionItem)!).RemoveFromSelection();
        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2 }));
        Assert.That(((ISelectionPattern)peer.GetPattern(AutomationPattern.Selection)!).CanSelectMultiple, Is.True);
        list.Focus();
        Assert.That(host.GetFocusedAutomationPeer(), Is.SameAs(items[1]), "the focus is on the current entry (the last one touched)");
    }

    [Test]
    public void TextBoxValueFollowsTheText()
    {
        var (host, root, spy) = CreateHost();
        var box = Add(root, new TextBox { Placeholder = "Preset name" }, 0, 0, 160, 26);
        host.UpdateLayout();
        var peer = box.GetAutomationPeer()!;
        var value = (IValuePattern)peer.GetPattern(AutomationPattern.Value)!;
        Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Edit));
        Assert.That(peer.Name, Is.EqualTo("Preset name"));
        value.SetValue("Warm pad");
        Assert.That(box.Text, Is.EqualTo("Warm pad"));
        Assert.That(spy.Log, Does.Contain("Value Preset name=Warm pad"));
        box.IsReadOnly = true;
        Assert.Throws<InvalidOperationException>(() => value.SetValue("x"));
    }

    [Test]
    public void ToolBarAndStatusBarDescribeTheirParts()
    {
        var (host, root, spy) = CreateHost();
        var bar = Add(root, new ToolBar(), 0, 0, 400, 30);
        var save = bar.Add(new Command("&Save", () => { }) { Gesture = KeyGesture.Parse("Ctrl+S"), Description = "Saves the document" }, ToolGlyph.Save);
        var status = Add(root, new StatusBar(), 0, 270, 400, 30);
        status.Segments.Add(new StatusBarSegment("Ready", float.PositiveInfinity));
        status.Segments.Add(new StatusBarSegment("48 kHz"));
        host.UpdateLayout();

        var savePeer = save.GetAutomationPeer()!;
        Assert.Multiple(() =>
        {
            Assert.That(savePeer.Name, Is.EqualTo("Save"));
            Assert.That(savePeer.AcceleratorKey, Is.EqualTo("Ctrl+S"));
            Assert.That(savePeer.HelpText, Is.Not.Null);
            Assert.That(Describe(status.GetAutomationPeer()!), Is.EqualTo(new[] { "Text Ready", "Text 48 kHz" }));
        });

        status.ShowMessage("Preset saved");
        Assert.That(spy.Log, Does.Contain("announce Preset saved"));
    }

    [Test]
    public void FocusEventsFollowTabNavigation()
    {
        var (host, root, spy) = CreateHost();
        Add(root, new Button("One"), 0, 0, 80, 24);
        Add(root, new Button("Two"), 0, 30, 80, 24);
        host.UpdateLayout();
        host.DispatchKeyDown(new KeyEventArgs(Key.Tab, ModifierKeys.None));
        host.DispatchKeyDown(new KeyEventArgs(Key.Tab, ModifierKeys.None));
        Assert.That(spy.Log.Where(l => l.StartsWith("focus", StringComparison.Ordinal)), Is.EqualTo(new[] { "focus Button One", "focus Button Two" }));
    }

    [Test]
    public void NothingIsReportedWithoutAListener()
    {
        var root = new Canvas();
        var host = new UIHost(root, new Size(200, 100));
        var box = Add(root, new CheckBox("A"), 0, 0, 80, 24);
        host.UpdateLayout();
        Assert.That(box.ExistingAutomationPeer, Is.Null, "peers are created on demand only");
        box.IsChecked = true;
        Assert.That(box.ExistingAutomationPeer, Is.Null);
        host.Announce("ignored");
    }

    [Test]
    public void HighContrastThemeUsesTheSchemeColors()
    {
        var text = Color.FromRgb(0xFFFF00);
        var highlight = Color.FromRgb(0x00FFFF);
        var theme = Theme.CreateHighContrast(Palette.HighContrast(Color.FromRgb(0x000000), text, highlight, Color.FromRgb(0x000000), Color.FromRgb(0x808080), Color.FromRgb(0x000000), Color.FromRgb(0xFF00FF)));
        Assert.Multiple(() =>
        {
            Assert.That(theme.Palette.Text, Is.EqualTo(text));
            Assert.That(theme.Palette.TextDim, Is.EqualTo(text), "no dimmed texts in high contrast");
            Assert.That(theme.Palette.Accent, Is.EqualTo(highlight));
            Assert.That(theme.DefaultStyle.DrawShadow, Is.False);
            Assert.That(theme.DefaultStyle.LabelColor, Is.EqualTo(text));
            Assert.That(Theme.HighContrast.Palette.Background, Is.EqualTo(Color.FromRgb(0x000000)));
        });

        // Away from a Windows high contrast session, the host draws with its own theme.
        var root = new Canvas();
        var host = new UIHost(root, new Size(100, 100)) { Theme = Theme.Light };
        var button = new Button("A");
        root.Add(button);
        Assert.That(host.EffectiveTheme, Is.SameAs(Theme.Light));
        Assert.That(button.Theme, Is.SameAs(Theme.Light));
        Assert.That(host.IsHighContrastActive, Is.False);
    }
}
