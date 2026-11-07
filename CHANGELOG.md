<!-- 2026/11/07 00:51:00 UTC - PluginUI -->

# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).
Before 1.0, minor versions may contain breaking changes.

## [Unreleased]

### Added

- **ASIO** in PluginUI.Host.Windows: `AsioDriver` (all the channels of the card or the first n, sample rate and
  buffer chosen among those the driver accepts, `ShowControlPanel`, `ResetRequested` when the control panel changes
  them, 16/24/32-bit integer and 32/64-bit float samples in either byte order, `outputReady`), `Asio.GetDrivers`,
  `Asio.ChooseBufferSize`. Binding adapted from NAudio (MIT); tests run it against an in-process test driver.
  `AudioDriver.ResetRequested`.
- **Plug-in Host**: driver choice (WASAPI, ASIO, none) in the Audio and MIDI dialog; with ASIO the driver, its control
  panel, its inputs and the sample rate; the driver restarts by itself after a change in its control panel.
- **MIDI produced by plug-ins**: `PluginInstance.EventOutputCount` and `OutputMidi` (VST3 event output buses: notes,
  polyphonic pressure, controllers, channel pressure, pitch bend and program changes through `LegacyMIDICCOut`);
  offline chains pass it to the next plug-in and report the last one's through `RenderOptions.MidiOutput`;
  `IMidiSink` and `RackSlot.MidiOut` route it live; `MidiFile.Write` saves events. `pluginhost` gains `--midi-out`
  and `process --midi`. Test plug-in "Test MIDI Echo".
- **MIDI in PluginUI.Host.Windows**: `MidiOutput` (output ports, short and system exclusive messages, all notes off,
  `IMidiSink` without waiting on the audio thread); `MidiInput` gains message times and system exclusive input.
  Buffer handling adapted from NAudio (MIT).
- **Plug-in Host**: MIDI output port in the Audio and MIDI dialog; a "MIDI out" choice in the windows of plug-ins that
  produce MIDI (nowhere, the output port, the other open plug-ins).
- **Live audio** in PluginUI.Host: `AudioDriver` (devices), `NullAudioDriver` (clock without a device),
  `IAudioRenderer`, `AudioBuffers`; `PluginRack` / `RackSlot` (plug-ins played live, each with its source, MIDI queue,
  bypass, mute, gain and meter; slots added and removed while the device runs, the audio thread never waits);
  sources `SilenceSource`, `DeviceInputSource`, `TestSignalGenerator` (sine, square, sawtooth, triangle, white and
  pink noise, sweep, impulses), `FileSource` (looped, resampled); `PeakMeter`, `MidiEventQueue`.
- **PluginUI.Host.Windows** (new library, not published on NuGet yet): `WasapiDriver` (shared mode, event-driven,
  IAudioClient3 low-latency periods, optional input at the same rate, MMCSS "Pro Audio", device loss reported),
  `Wasapi.GetEndpoints`, `MidiInput` (winmm MIDI input ports).
- **Plug-in Host**: plays the open plug-ins on the audio device. Audio and MIDI dialog (output, input, buffer, MIDI
  input port), device in the status bar, output meter; in each plug-in window a source (audio input, test signal,
  audio file), an on-screen keyboard for instruments, bypass, mute and an output meter. Replaces the silent
  processing thread.
- **Plug-in Host** (`tools/PluginHost.Desktop`): graphical plug-in host built with PluginUI (IL or NativeAOT). Plug-in
  list with search, filters (format, instruments or effects, favorites, failures), sort orders (name, vendor,
  category, format, input or output channels, latency, installation date, recently used, most used, favorites,
  folder, status) and columns shown or hidden from the header; details pane; scans in separate processes with their
  progress; plug-in folders dialog; favorites, show in Explorer, copy the path, ignore a module; one window per open
  plug-in with its editor or a generic parameter table (typed values), presets, real-time processing with silent input
  until audio devices arrive. Folders and plug-in list shared with `pluginhost`; interface settings in `gui.json`.
  Resizable editors are fitted inside the window (editors with a fixed aspect ratio answer `checkSizeConstraint` with
  a size that follows the width; a maximized window cut them) and centered in it.
- **DataTable**: `DataColumn.IsVisible`, `CanHide`, `Key` and `CellRenderer`; header menu to show or hide columns
  (`ShowColumnMenu`, `GetColumnMenuItems`, `SetColumnVisible`, `VisibleColumns`, `ColumnsChanged`); `SaveLayout` /
  `RestoreLayout` (widths, visibility, sort); `RowContextMenu` (right click, menu key, Shift+F10); screen readers
  (`DataTableAutomationPeer`, `DataRowAutomationPeer`). `Key.Apps` (menu key).
- **NativeWindowHost**: `ProvidesContainer` creates an empty container window for content that needs a parent window
  (VST3 plug-in editors), with `ContainerCreated` / `ContainerDestroying`; `HideUnderPopups`, `ContainerBackground`,
  `PreferredPixelSize`, `PixelSize`, `PixelSizeChanged`, `PixelsToDips`.
- **PluginUI.Host**: `PluginInstance.IsDisposed`; `Dispose` can be called more than once.
- **PluginUI.Host** (new library, not published on NuGet yet): VST3 plug-in host for .NET, without UI dependency and
  NativeAOT compatible. `Vst3Module` (`.vst3` files and bundles on Windows and Linux, or an NPlug factory in the same
  process with `FromFactory`), `PluginInstance` / `Vst3Plugin` (buses and channel layouts, parameters with display
  texts and typed values, changes from any thread through a lock-free queue, notes and MIDI controllers through the
  plug-in's `IMidiMapping`, transport, latency and tail, `.vstpreset` state, `Vst3Editor` windows, restart requests),
  `VstPreset`, `PluginCatalog` / `PluginScanner` / `HostSettings` / `HostPaths` (plug-in folders in `settings.json`,
  out-of-process incremental scan, plug-in list in `plugins.json` with favorites and usage, text queries and 13 sort
  orders), `PluginLoader`, `OfflineRenderer` (chains, latency compensation, tails, MIDI), `ResponseMeasurer` (impulse
  and FFT, or stepped sine; 20·log10 or 6·log2 decibels), `WavFile`, `MidiFile`. Guide: `docs/PLUGIN_HOST.md`.
- **pluginhost** (`tools/PluginHost.Cli`): command line plug-in host to replace Python and Pedalboard scripts —
  `folders`, `scan`, `list`, `favorite`, `info`, `preset show|save`, `process` (WAV through a chain of plug-ins),
  `render` (MIDI file through an instrument), `measure` (frequency response to CSV, impulse response to WAV).
  Scanning was checked against a real plug-in collection: modules are loaded on a single-threaded apartment thread
  (Melodyne, Modartt), the child process writes its result to a file (sforzando and Waves shells print on the
  standard output), the timeout counts the time without progress (Waves shells), failures give the exit code meaning
  and the last lines the plug-in printed, `.vst3` modules are also looked for in the VST 2 folders, and VST 2 modules
  are not listed until VST 2 hosting is installed. `ModuleScanProcess` is the child side for any host executable.
  `pluginhost` is built without the CET shadow stack (`<CETCompat>false</CETCompat>`), which some plug-ins are not
  compatible with (Pianoteq 6, Organteq 1); a failure names the last step the plug-in reached (`Vst3Module.Trace`), and
  a complete result is kept when a plug-in crashes while closing. The child releases the module after writing its
  result; a child that does not exit within `OutOfProcessModuleScanner.ClosingGrace` (5 s; Waves shells keep threads
  running) is stopped and its result kept.
- **Accessibility (screen readers)**: PluginUI interfaces are exposed to Narrator, NVDA and JAWS through Windows UI
  Automation, in desktop applications and plug-in editors (`WM_GETOBJECT`, source-generated COM, NativeAOT compatible;
  calls from UI Automation threads run on the UI thread). New namespace `PluginUI.Automation`: `AutomationPeer`,
  `ElementAutomationPeer`, `ItemAutomationPeer`, the patterns `IInvokePattern`, `ITogglePattern`, `IRangeValuePattern`,
  `IValuePattern`, `IExpandCollapsePattern`, `ISelectionItemPattern`, `ISelectionPattern`, and peers for buttons,
  toggles, check boxes, knobs and sliders (plain parameter range and displayed text, one gesture per edit), number
  boxes, option groups, drop-downs, menu buttons, text boxes, labels, lists, tabs, group boxes, menu bar and menus
  (access keys, shortcuts, check marks), tool bars, status bars, dialogs and progress bars. Focus events follow Tab,
  option and list navigation, menus and the menu bar keyboard mode; menus report opening and closing, values and states
  report their changes. `UIElement.AutomationName`, `AutomationHelpText`, `AutomationLabeledBy`, `AutomationHidden`,
  `GetAutomationPeer`, `OnCreateAutomationPeer`, `NotifyAutomationStateChanged`; `UIHost.AutomationName`,
  `RootAutomationPeer`, `GetFocusedAutomationPeer`, `Announce` (UI Automation notifications);
  `StatusBar.AnnounceMessages`. Guide: `docs/ACCESSIBILITY.md`.
- **High contrast**: hosts draw with a theme built from the Windows high contrast colors while the mode is on
  (`UIHost.FollowSystemHighContrast`, default on; `EffectiveTheme`, `IsHighContrastActive`, `IsSystemHighContrast`,
  `CreateSystemHighContrastTheme`), with `Theme.HighContrast`, `Theme.CreateHighContrast` and `Palette.HighContrast`.
- **UIStrings**: `AutomationKnob`, `AutomationDialog`, `AutomationMenu`, `AutomationInvalidEntry`.
- **FrequencyResponseView.SpectrumAnchor**: left-axis value at which `SpectrumMaxDb` is drawn when the spectra follow
  the left axis (default: the top, as before). A fixed anchor keeps the spectrum still relative to the curves when the
  decibel range changes; `EffectiveSpectrumMaxDb`.
- **PluginUI.Desktop** (new NuGet package): complete Windows desktop applications with PluginUI as their whole
  interface, compiled as IL or NativeAOT. `DesktopApplication` (DPI awareness, `%APPDATA%` data folder, main window
  placement and recent files saved in `state.json`, application commands, crash report; requires an STA `Main`),
  `DocumentSession` (file path, modified state, title bar, New / Open / Save / Save As commands with their dialogs,
  "save changes?" before New, Open and closing), `UndoStack` (undo / redo with merge keys, saved point, Ctrl+Z / Ctrl+Y
  commands named after the step, `Track(LocalParameter)` records one step per gesture), `AppSettings<T>` (JSON through
  source generation, atomic save, unreadable files kept as `.bad`), `RecentFiles` (sub-menu with `&1`… entries,
  compacted paths), `MessageDialog` (synchronous drawn message boxes, `AskSaveChanges`). Guide: `docs/DESKTOP.md`.
- **MenuBar**: drawn application menu bar. Click and hover between menus, Alt+letter, lone Alt or F10 for the keyboard
  mode (Left / Right / Down / Enter / Escape), shortcuts of every entry run from anywhere in the window (`Command`
  gestures or parsable `ShortcutText`), `Opening` event for dynamic menus, `HandlesAltKey` off in plug-in editors.
  Registered for PluginUIEditor designs.
- **Commands and shortcuts**: `Command` (text, `KeyGesture`, alternate gestures, `CanExecuteWhen`, `IsCheckedWhen`,
  `AllowsRepeat`, `Executed`), `KeyGesture` (parse / format "Ctrl+Shift+S", English and French key names, localized
  display through `UIStrings.KeyNames`), `CommandSet` (routes key presses of a host to its commands), `Mnemonics`
  (`&` markers). `MenuItem(Command)` shows the command's text, shortcut, enabled and checked states when the menu opens;
  `MenuItem.Invoke`, `DisplayText`, `Mnemonic`, `GetShortcutText`.
- **Menus**: drawn menus underline mnemonics when opened from the keyboard (`MenuPopup.ShowMnemonics`), run the entry
  of a unique mnemonic, and raise `NavigateSibling` for Left / Right in a menu bar. Native menus use the same `&`
  notation (literal `&` kept).
- **StandaloneWindow**: `ShowDialog` (modal, owner disabled, nested loop, `DialogResult`, `Close(int)`), `Owner`,
  `StartPosition` (`WindowStartPosition`), `Placement` (`WindowPlacement`, restored only on an existing monitor),
  `DarkTitleBar` (follows the theme by default) and `UpdateTitleBarTheme`, icon from `IconPath` or the executable's
  `<ApplicationIcon>`, `MinimizeBox` / `MaximizeBox`, `Activate`, `RequestClose`, `RunMessageLoopUntil`.
- **UIHost.PreviewKeyUp**; `Popup.ClosedByEscape`; `Key.OemPlus`, `OemMinus`, `OemComma`, `OemPeriod`,
  `OemSemicolon`.
- **UIStrings**: key names, menu texts with mnemonics (File, Edit, View, Help, New, Open, Save, Save As, Exit, About),
  Undo / Redo with the step name, "Untitled", the save-changes question and its Don't Save button, recent files, file
  not found, "All Files" — in English and French.
- **Biquad Designer** (`samples/PluginUI.DesktopDemo`): desktop demo application (four-section RBJ biquad designer with
  draggable response, coefficients, `.bqd` files, undo, recent files, light theme, French interface), built with
  NativeAOT by `build/Build-Release.ps1` and shipped as `BiquadDesigner-<version>-win-<arch>.zip`.

- **ToolBar**, **ToolBarButton**, **ToolBarSeparator**: tool bar of buttons built from `Command` objects (greyed out
  when the command cannot run, pressed while checked, tooltip with the shortcut), 18 vector icons (`ToolGlyph`,
  `ToolGlyphs.Draw`) or a custom `DrawIcon`, `HintChanged` for a status bar. Registered for PluginUIEditor designs.
- **StatusBar** with `StatusBarSegment`: fixed, fitted or stretched fields with live text sources, `Hint` (description
  of the highlighted menu entry or hovered button through `Follow(MenuBar)` / `Follow(ToolBar)`), temporary messages
  (`ShowMessage`). `MenuItem.Description`, `MenuPopup.ItemHighlighted`, `MenuBar.HintChanged`.
- **PluginUI.Desktop**: `DesktopApplication.ExitCommand`, `DocumentSession.Saved` / `Opened`, localized descriptions of
  the document, undo and exit commands; `UIStrings` hints and status texts (Ready, Saved, Opened).
- **Project creation**: new kind `ScaffoldKind.DesktopApplication` (PluginUIEditor: *Desktop application*): menu bar,
  tool bar, status bar, documents saving the parameter values of the design, undo, recent files, settings.

- **MappedParameter**: a view of an `IParameter` through a normalized remapping, optionally driving linked parameters
  in the same gesture (`Link`). `MappedParameter.Ranges(inner, (min, max), …)`: a knob covering several ranges end to
  end, skipping the gaps (for instance a feedback restricted to 0.5 ≤ |FB| ≤ 0.999). The host still sees the inner
  parameter only.
- **BandCenterParameter** and **IBandCenter** / **BandCenters**: the centre of a band given by two edge parameters as
  a parameter of its own (a CENTER knob moves both edges with their ratio, kept for the whole gesture);
  `BandCenters.Geometric` and `BandCenters.Bilinear(sampleRate)` (peak of the bilinear band designs, cos ω0 =
  cos((ω1 + ω2)/2)/cos((ω2 − ω1)/2)). `ResponseNode.BandCenter` puts a band node on that centre.
- **ResponseCurve.EvaluateRange**: optional smallest and largest values over the frequencies of each pixel column;
  the view fills the area between the two envelopes, so a curve with more detail than pixels (a comb with hundreds
  of teeth) is drawn without aliasing. `RangeFillOpacity`.
- **LevelMeter.DecibelConvention**: 20·log10 (default) or 6·log2 for linear inputs and sources.
- **TextBox** edit menu on a right click (Cut, Copy, Paste, Delete, Select All, in English and French;
  `ShowsEditMenu`), `Copy`, `Cut`, `Paste` methods, Ctrl+Insert, Shift+Insert and Shift+Delete. `UIStrings.EditCut`,
  `EditCopy`, `EditPaste`, `EditSelectAll`.
- **ModelState.Load** (PluginUI.NPlug): loads a model state written by another version. NPlug refuses a state with
  fewer values than parameters (every preset and project saved before parameters were appended); here the appended
  parameters take their default value, extra values are ignored, corrupted values are repaired. `PluginUIProcessor`
  and `PluginUIController` use it, with an `UpgradeState(storedParameterCount)` hook to restore the behavior of the
  older version.

### Changed

- `UIElement.Theme` returns the host's `EffectiveTheme` (its high contrast replacement while Windows is in high
  contrast). Code reading `UIHost.Theme` to draw should read `EffectiveTheme`.
- **Project creation**: the standalone application template has an `[STAThread]` `Main` and an `app.manifest`
  (per-monitor DPI).
- **MenuItem**: an `&` followed by a letter or a digit is now a mnemonic marker in drawn menus too (it already was in
  native menus); `&&` writes `&`, any other `&` stays literal. `DropDown`, `PresetBar` and the MIDI learn entries
  escape their data (`Mnemonics.Escape`), so "R&B" still shows as is.
- **Gallery**: explicit `[STAThread] Main` (the file dialogs of `FilePathBox` need a single-threaded apartment).
- **WM_SYSCHAR** following an Alt+key handled by PluginUI is consumed (no beep).
- **BandCenterParameter** displays "250 Hz", "2.50 kHz", "12.7 kHz" (the unit is in the text, `Units` is empty);
  `Decimals` (default 0) applies below 1 kHz.

### Fixed

- **Shortcuts of the menu bar and of a `CommandSet` no longer take the editing keys of the focused element**: a text
  box keeps Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+A, Del, Backspace, the arrows… even when an Edit menu has commands with the
  same shortcuts, as in Windows (new `UIElement.ClaimsKey`, overridden by `TextBox`); function keys and the other
  Ctrl shortcuts still run the commands.
- **TextBox** edit menu: the shortcuts are shown in the interface language ("Suppr" in French instead of "Del").
- **MappedParameter.Ranges** of a discrete parameter: the outer steps are the inner steps inside the ranges
  (`StepCount` follows) instead of the inner step count; documentation example of `Link` corrected.

- **Paste in value editors inside a DAW**: hosts that send Ctrl+letter to `IPlugView::onKeyDown` as its control
  character (Ctrl+V = 0x16) now reach the text box (`PluginEditorView.MapKey`); before, the key went back to the DAW.
- **MenuPopup**: a menu opened with an entry highlighted before its first layout no longer starts scrolled.

## [0.2.0] - 2026-11-04

### Added

- **FrequencyResponseView**, with `ResponseCurve` and `ResponseNode`: frequency-response display for equalizers and
  filters. Any number of curves supplied by the consumer (no filter design inside), logarithmic or linear frequency
  axis, fixed left axis and auto-scaled right axis (decimal or π-multiple ticks, for phases and delays), analyzed input
  spectrum (FFT with 50 % overlap, instant attack, exponential release, 20·log10 or 6·log2 decibels) and the same
  spectrum shaped by a response without a second FFT, draggable nodes bound to parameters (parallel gestures),
  optionally defined by two band edges with their own handles, wheel on the band ratio or on any parameter.
  Registered for PluginUIEditor designs, shown in the gallery (*Response*).
- **FrequencyResponseView**: `SpectrumFollowsLeftAxis` draws the spectra with the decibels per pixel of the left axis
  (`SpectrumMaxDb` at the top), so that the gap between the filtered and the input spectrum matches the response curves;
  `EffectiveSpectrumMinDb`; `RightAxisCentered` keeps the zero of the right axis at mid-height. The analysis pauses
  while the editor is minimized or hidden (`UIHost.IsShownOnScreen`), the unread samples being dropped
  (`AudioRingBuffer.SkipToLatest`).
- **ToggleButton**: `OffColor` fills the off state (a small square with a band color and a grey off color makes a
  colored enable switch) and `Glyph` draws an icon (`ButtonGlyph.Wave`, `Glyphs.Wave`) instead of, or left of, the
  caption.
- **UIHost.IsShownOnScreen**: false while the window is hidden or its top-level window is minimized.
- **DropDown.IsItemAvailable**: hides the items that do not apply in the current context (left out of the menu, skipped
  by the wheel and the arrow keys) without changing the indices of the others.
- **ViewSwitcher**: shows one child at a time, chosen by `SelectedIndex` or by a bound parameter (VSTGUI
  `UIViewSwitchContainer`); registered for PluginUIEditor designs, shown in the gallery (*Containers* ▸ *Effects*).
- **LevelMeter** skins: with `Skin` (for example a `FilmStripSkin`), each channel is drawn by the skin with its level
  as value and its latched clip indicator as state index (iPlug2 `IBMeterControl`); shown in the gallery (*Meters*).
- Element catalog: reference versions of the equivalents (VSTGUI 4.15.1, iPlug2 `master` of 2026-10-18), and the
  equivalents that were missing.
- **PluginUI.NPlug** is published on NuGet. It depends on the `NPlug` package and passes its build files on (debug
  proxy, factory export, NativeAOT `.vst3`), so a plug-in project needs this single package reference.

### Changed

- **Build**: `global.json` requires the .NET SDK 10.0.403 or a later 10.0 band. Earlier SDKs bring
  `Microsoft.DotNet.ILCompiler` and runtime packs below 10.0.14, affected by GHSA-p3jp-9v83-76w9 (CVE-2026-111497, Brotli
  decoder), which NativeAOT links into the published plug-ins and tools; NuGet audit (NU1903) fails their restore.
- **FrequencyResponseView**: the linear frequency axis honours `MinFrequency` and `MaxFrequency` too (it always spanned
  0 Hz to Nyquist); set `MinFrequency = 0` and `MaxFrequency = double.PositiveInfinity` for the former behavior.
- NPlug comes from its NuGet packages: `NPlug` 0.6.2 (with `NPlug.Proxy` 2.0.2, which writes ARM64 plug-ins to
  `Contents/arm64-win`) and `NPlug.Validator` for the tests and the validation tool. The `external/NPlug` submodule,
  the `NPlugRoot` property and the asm8086/NPlug fork are no longer used; 0.1.0-beta.2 used the NPlug 0.6.0 sources.
- Projects generated by PluginUIEditor reference only `PluginUI.NPlug` (NPlug comes with it).
- Tests and validation tool: `NPlug.Validator` 1.4.1, which also validates in-process on Windows ARM64 (the
  workaround for 1.4.0 is removed).

### Fixed

- **Images drawn smaller than their pixel size** (`ImageView`, `AboutBox` logo, `DrawingContext.DrawImage` with
  `smooth`): Direct2D's high-quality cubic interpolation, which prefilters the reduction, replaces bilinear
  interpolation, which reads only four pixels and made a large logo shown small look like nearest neighbour.
- **PresetBar**: a long preset name no longer covers the bank name shown on its left; it moves to the right of the
  bank (trimmed with an ellipsis if needed), and the bank is left out when the name would keep less than 80 points.
- **In-place value editing** (`UIHost.BeginTextEdit`, the text entry of knobs, sliders, value displays, tables and the
  preset bar): the box is focused and its text selected as soon as it opens, instead of after a posted message, so
  that the first typed character replaces the value; typing while the focus is still elsewhere goes to the box.
- **TextBox** with `SelectAllOnFocus`: the click that gives the focus, or the first click after it, keeps the whole
  text selected instead of placing the caret (the next click places it), so that typing replaces the text; a click
  into a focused box takes back the Win32 keyboard focus from the DAW.
- **Edits made in the editor did not reach the processor on Windows x64**: NPlug 0.6.1 calls the host's
  `IComponentHandler::performEdit` with the value declared as `LibVst.ParamValue`, a struct wrapping a `double`. The
  Windows x64 calling convention passes an 8-byte struct in an integer register, while the host reads the `double` from
  a floating-point register, so the host received 0 (or garbage) for every knob, switch and list change: the editor
  showed the change, the audio did not follow, and automation recorded wrong values. Fixed by NPlug 0.6.2, which passes
  these values (and those of `IParamValueQueue::addPoint`, `IProgress::update`, `getParamStringByValue`…) as `double`;
  PluginUI.NPlug now requires it. ARM64 and the System V platforms were not affected.
- **Typing in a text box of a plug-in editor**: the DAW forwards the keys through `IPlugView::onKeyDown` with their
  character; digits and letters were converted to key events that the focused text box claimed, so they never arrived
  as text (only characters without a key equivalent, such as `é` or `&` on an AZERTY keyboard, did), and the numeric
  keypad sent no character at all. The character is now always delivered as text input, the keypad keys included
  (`PluginEditorView.TextCharacter`); AltGr combinations type, Ctrl or Alt alone stay shortcuts.

### Removed

- `ScaffoldOptions.NPlugRoot` and the *NPlug `src` folder* option of PluginUIEditor.

## [0.1.0] - 2026-10-18

First public version.

### Added

- **PluginUI**: Win32 + Direct2D / DirectWrite toolkit for plug-in editors, NativeAOT-compatible.
  - Hosting: `UIHost` (child window, DPI, sizing modes, input routing, popups, tooltips, animation timer, `Post`),
    `StandaloneWindow`.
  - About eighty elements: rotary and linear controls, buttons and selectors, menus, text and value entry, meters and
    analyzers (level, loudness EBU R128, goniometer, correlation, spectrum, spectrogram, oscilloscope, waveform),
    instrument elements (keyboard, wheels, MSEG, step and gate sequencers, drag-and-drop modulation, A/B comparison,
    MIDI learn), containers (canvas, stack, grid, dock, wrap, tabs, split view, scroll viewer, group box, list box,
    data table).
  - Themes (dark, light, accent), vector styles, skins (film strip, state images, SVG, nine-slice, delegate).
  - Live layout editing (Ctrl+Shift+E) with C# code copy.
  - `PluginUI.Dsp`: lock-free audio → UI transfer, FFT, meter ballistics, biquad design, WAV reading, SIMD kernels.
  - English and French interface texts, culture-independent number formatting.
- **PluginUI.NPlug**: VST3 editor view for NPlug (`PluginEditorView`), controller and processor base classes with
  parameter edit bridge, notes from the editor, shared state and MIDI learn.
- **PluginUI.Design**: `.pluginui` design files, AOT-safe run-time loader, C# code generation, live design bridge,
  project scaffolding.
- **PluginUIEditor**: WinForms visual designer (toolbox, property grid, parameters, preview, undo/redo, code
  generation, new projects, exports, extensions), English and French.
- Samples: `PluginUI.Gallery` (every element, `--lang fr`, PNG snapshots) and `PluginUI.DemoSynth` (16-voice VST3
  instrument with side-chain input).
- Documentation in English and French, DocFX site with API reference.
- Release packages for x64 (x86-64-v3 and x86-64-v2) and ARM64, with a VST3 bundle that picks the right x64 build
  for the processor.

[Unreleased]: https://github.com/asm8086/PluginUI/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/asm8086/PluginUI/compare/v0.1.0-beta.3...v0.2.0
[0.1.0]: https://github.com/asm8086/PluginUI/releases/tag/v0.1.0
