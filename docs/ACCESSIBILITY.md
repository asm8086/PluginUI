<!-- 2026/11/06 12:53:00 UTC - PluginUI -->

# Accessibility

*[Français](ACCESSIBILITY_FR.md)*

PluginUI draws every control itself in one window, so Windows cannot see them on its own. The library describes
them to assistive technologies through **Windows UI Automation**: Narrator, NVDA and JAWS read the name, role, value
and state of each control, follow the keyboard focus and act on the controls. This works in desktop applications and
in plug-in editors inside a DAW. In high contrast mode, the interface uses the colors of the Windows scheme.

There is nothing to turn on. A host costs nothing until a screen reader or another client asks for the tree.

## What screen readers get

| Element | Role | What can be read and done |
|---|---|---|
| `Button` | Button | Invoke |
| `ToggleButton`, `SlideSwitch`, two-position `Switch` | Button with a toggle state | Toggle (on / off) |
| `CheckBox` | Check box | Toggle |
| `Knob`, `Slider`, `ModWheel`, `PitchWheel`, other `ValueControl` | Slider (spoken as "knob" for `Knob`) | Value range and value text ("-6.0 dB"); set by number or by text |
| `NumberBox`, `ValueDisplay` | Spinner | Same as a slider |
| `RadioGroup`, `SegmentedControl`, `Switch` with 3+ positions | Group of radio buttons | Each option, its position ("2 of 3"), Select; the focus is on the selected option |
| `DropDown` | Combo box | Expand / Collapse, selected entry, select by text |
| `MenuButton` | Button | Expand / Collapse |
| `TextBox` | Edit | Text value, read-only state, "invalid entry" state |
| `Label`, `DialogText`, status bar fields | Text | Content |
| `ListBox` | List of list items | Select, add to and remove from the selection; the focus is on the current row |
| `TabView` | Tab with tab items | Select a tab; the page content follows the tabs |
| `GroupBox` | Group named by its header | Expand / Collapse when collapsible |
| `MenuBar`, menus | Menu bar, menus, menu items | Expand / Collapse sub-menus, Invoke, check marks, access keys (Alt+F) and shortcuts (Ctrl+S) |
| `ToolBar`, `ToolBarButton` | Tool bar, buttons | Invoke (Toggle for checkable commands), shortcut, description |
| `StatusBar` | Status bar with texts | Messages of `ShowMessage` are spoken |
| `Dialog`, modal popups | Pane spoken as "dialog" | The content below a modal popup is left out, as for the mouse |
| `ProgressBar` | Progress bar | Read-only value |
| Other focusable controls | Custom | Name (label or tooltip) |

Layout panels (`Canvas`, `Grid`, `StackPanel`…) are transparent: their children appear in their place. Hidden
elements are left out.

Values of controls bound to a parameter are reported in the parameter's units (a cutoff from 20 to 20000 Hz, a gain
in dB): the range uses `IParameter.ToPlain`, the text is the one the control displays. An edit made by a screen reader
is a complete gesture (`BeginEdit` / `PerformEdit` / `EndEdit`), so the DAW records it as one automation step.

## Naming controls

The name a screen reader speaks comes, in this order, from:

1. `AutomationName`, when set;
2. the element named by `AutomationLabeledBy` (a `Label` next to the control);
3. the control's own caption: `Text` of buttons and check boxes, `Label`, the name of the bound parameter;
4. the tooltip.

```csharp
var drive = new Knob(model.Drive);                                // "Drive" (the parameter name)
var open = new Button { Svg = folderIcon, ToolTip = "Open preset" };  // "Open preset"
var close = new Button { Svg = crossIcon, AutomationName = "Close" };
var caption = new Label("Character");
var mode = new DropDown("Clean", "Warm") { AutomationLabeledBy = caption };
var logo = new ImageView(logoImage) { AutomationHidden = true };  // decoration
```

`AutomationHelpText` adds a description read after the name (by default, the tooltip when it is not the name).
`AutomationHidden` removes an element and its descendants from the tree. These properties are in the property grid of
PluginUIEditor (Text and Behavior categories).

Give the editor itself a name: Narrator and NVDA read it when the focus enters the plug-in window.

```csharp
protected override void ConfigureHost(UIHost host)   // PluginEditorView
{
    host.AutomationName = "Lumen EQ";
}
```

## Keyboard

Screen reader users work with the keyboard: Tab and Shift+Tab move between controls, the arrows, Page Up / Page Down,
Home / End change values, Del resets, Enter types a value, Space toggles. Menus work as in Windows (F10, Alt, arrows,
Esc). A screen reader can also move the focus to a control (UI Automation `SetFocus`): the host window then takes the
keyboard focus.

In a DAW, the plug-in window receives the keyboard only when it has the focus. Clicking a control does not take the
focus by default (keys stay with the DAW); a screen reader user moves to the plug-in window with the DAW's or the
screen reader's commands. Set `UIHost.TakeFocusOnClick` if your plug-in should take it on click.

## Announcements

`UIHost.Announce(text)` asks screen readers to speak a message without moving the focus: a preset loaded, an
analysis finished, an error. `AnnouncementPriority.Important` interrupts what is being read. `StatusBar.ShowMessage`
announces its messages (turn off with `AnnounceMessages = false`). Announcements use UI Automation notifications
(Windows 10 1709 and later).

## High contrast

When Windows runs in high contrast, hosts draw with a theme built from the colors of the user's scheme: background,
text, highlight, disabled text, links (`UIHost.CreateSystemHighContrastTheme`). Texts are no longer dimmed, frames and
focus rings are thicker and there are no shadows. The host follows changes of the setting while it is open.

- `UIHost.FollowSystemHighContrast` (default `true`) turns this off for a host.
- `UIHost.EffectiveTheme` is the theme used for drawing; `UIHost.IsHighContrastActive` tells whether the scheme applies.
- `Theme.HighContrast` and `Theme.CreateHighContrast(Palette.HighContrast(...))` give the same look on demand.

Bitmap and film-strip skins are not recolored. If your editor relies on images, check it in high contrast and offer
a vector variant where the images do not read well.

## Describing a custom control

A control derived from `ValueControl` is already a slider. For another custom control, override
`OnCreateAutomationPeer` and return a peer that gives the role and implements the patterns. Call
`NotifyAutomationStateChanged()` when the state that screen readers read changes.

```csharp
public sealed class PanPad : Control
{
    public double Pan { get; private set; }

    protected override AutomationPeer? OnCreateAutomationPeer() => new PanPadPeer(this);

    private void SetPan(double pan)
    {
        Pan = pan;
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private sealed class PanPadPeer(PanPad owner) : ElementAutomationPeer<PanPad>(owner), IRangeValuePattern
    {
        private double _reported = double.NaN;

        public override AutomationControlType ControlType => AutomationControlType.Slider;
        protected override string? GetNameCore() => "Pan";

        public double Value => Owner.Pan * 100;
        public double Minimum => -100;
        public double Maximum => 100;
        public double SmallChange => 1;
        public double LargeChange => 10;
        public bool IsReadOnly => !Owner.IsEffectivelyEnabled;
        public void SetValue(double value) => Owner.SetPan(Math.Clamp(value, -100, 100) / 100);

        protected override void OnOwnerStateChanged()
        {
            RaisePropertyChanged(AutomationProperty.RangeValue, _reported, Value);
            _reported = Value;
        }
    }
}
```

Items a control draws without being elements (cells, points, keys) can be exposed as children: derive from
`ItemAutomationPeer` and add them in `CollectChildren`; see `OptionsAutomationPeer` for an example. Return `null`
from `OnCreateAutomationPeer` to make an element transparent.

## Testing

- **Narrator** (built into Windows): Ctrl+Win+Enter starts and stops it. Tab through the editor and listen; Caps Lock +
  arrows move through all the elements.
- **NVDA** (free, nvaccess.org): the most used screen reader with JAWS. NVDA+Tab reads the focused element,
  NVDA+numpad keys navigate by object.
- **Accessibility Insights for Windows** (free, Microsoft): shows the UI Automation tree, the properties and the patterns of the
  element under the mouse or with the focus, and runs automated checks (FastPass).
- `Inspect.exe` (Windows SDK) shows the same tree.

Check that every control has a name, that the values are read with their units and that the whole editor can be used
without the mouse.

## Limits

- Text boxes expose their text as a value: screen readers read it, but character by character review and the caret
  position (UI Automation text pattern) are not available yet.
- Composite controls (`PresetBar`, `ABCompareBar`, `XYPad`, `ParametricEqEditor`, `EnvelopeEditor`, `StepSequencer`,
  `PianoKeyboard`, graphs and meters) are one element each, named by their label or tooltip. Give them an
  `AutomationName`, or a peer of your own as above.
- Native menus (`UIHost.UseNativeMenus`) are Windows menus and are read by Windows itself.
