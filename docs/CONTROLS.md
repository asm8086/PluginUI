<!-- 2026/10/19 20:27:00 UTC - PluginUI -->

# Element catalog

*Version française : [CONTROLS_FR.md](CONTROLS_FR.md)*

All types are in the `PluginUI` namespace. **Value** controls derive from `ValueControl`: binding to an `IParameter` (`Parameter = …`), full VST3 gesture, reset on double-click or Ctrl+click (`ResetOnDoubleClick`, `ResetOnControlClick`), keyboard value entry for `Knob` and `Slider` (double-click on the value text, Alt+click, right double-click or Enter, chosen with `TextEntryTriggers`; numbers always use a decimal point), mouse wheel, arrow keys, Shift for fine adjustment, value bubble (`ShowValueBubble`), skin (`Skin`) and style (`Style`). The "Equivalent" column gives the corresponding VSTGUI or iPlug2 class.

Reference versions for the equivalents: **VSTGUI 4.15.1** (VST 3 SDK 3.8.1) and **iPlug2** `master` of 18 October 2026 (commit `39ea73a`, after tag `v1.0.0-beta`).

## Rotary

| Element | Key points | Equivalent |
|---|---|---|
| `Knob` | `Arc` / `Classic` / `Minimal` styles, bipolar mode (`IsBipolar`, `BipolarCenter`), modulation arc (`ModulationAmount`), tick marks, vertical/horizontal drag, keyboard entry (double-click on the value, Alt+click…), film strip and SVG via `Skin` | `CKnob`, `CAnimKnob`, `IVKnobControl`, `IBKnobControl`, `ISVGKnobControl` |
| `EndlessKnob` | Endless encoder: `Turned(steps)` event, optional parameter control with `StepSize`, `Wrap` | — |

## Linear

| Element | Key points | Equivalent |
|---|---|---|
| `Slider` | Vertical or horizontal, bipolar, `Cap`/`Round`/`Line`/`None` handles, `JumpToClick`, tick marks, side label, `SliderHandleSkin` (bitmap handle) | `CSlider`, `IVSliderControl`, `IBSliderControl`, `ISVGSliderControl`, `CHorizontalSlider`, `CVerticalSlider` |
| `RangeSlider` | Two bounds (`Low`/`High`, `LowParameter`/`HighParameter`), minimum gap, dragging of the band | `IVRangeSliderControl` |
| `MultiSlider` | N bars, drawing with the mouse (interpolated, Ctrl: straight line), bipolar, step, highlighted index, `BindParameters` | `IVMultiSliderControl` |
| `XYPad` | Two parameters edited in simultaneous gestures, grid, crosshair | `CXYPad`, `IVXYPadControl` |
| `ScrollBar` | `Minimum`/`Maximum`/`ViewportSize`/`Value`, arrows, paging with auto-repeat, auto-hide | `CScrollbar` |
| `EnvelopeEditor` | ADSR with draggable points, four optional parameters, square-root time axis | — (bonus) |
| `CurveEditor` | Multi-segment envelope / LFO (MSEG): draggable points, adding and removing by double-click, per-segment curvature, snap grid, playhead, exact `Evaluate` for the audio engine | — (bonus) |

## Buttons and selectors

| Element | Key points | Equivalent |
|---|---|---|
| `Button` | Text and/or icon (bitmap, SVG, `IconRenderer`), `Normal`/`Accent`/`Flat`/`Outline` appearances, repeat while pressed, momentary parameter | `CKickButton`, `CTextButton`, `IVButtonControl`, `IBButtonControl` (+ `ImageStateSkin`), `ISVGButtonControl` (+ `SvgStateSkin`) |
| `ToggleButton` | `OnText`/`OffText`, LED, state color; with a `FilmStripSkin`, equivalent to `CMovieButton` | `COnOffButton`, `CMovieButton`, `IVToggleControl`, `ITextToggleControl`, `ISVGToggleControl` |
| `CheckBox` | Animated box + text | `CCheckBox` |
| `Switch` | N positions, vertical/horizontal, direct or cyclic selection, film strip by state index | `CHorizontalSwitch`, `IVSwitchControl`, `CVerticalSwitch`, `IBSwitchControl` (+ `FilmStripSkin`), `ISVGSwitchControl` |
| `SlideSwitch` | Animated "pill" switch | `IVSlideSwitchControl` |
| `SegmentedControl` | Segments in a row or column, sliding highlight | `CSegmentButton`, `IVTabSwitchControl` |
| `RadioGroup` | List of radio buttons | `IVRadioButtonControl` |
| `RockerSwitch` | Momentary −1/0/+1, auto-repeat, parameter stepping | `CRockerSwitch` |
| `MultiToggle` | Row of N toggles (gate sequencer), drawing by dragging, playback position | `IVMultiToggleControl` |
| `StepSequencer` | Per-step values (interpolated drawing) + gates, active length, beat groups, playing step, binding to one parameter per step (one gesture per parameter touched) | — (bonus) |
| `ModulationSourceButton` | Modulation source chip to drag onto a control that accepts modulation (`AcceptsModulation`, `ModulationDropped` event) | — (bonus, Serum / Vital style) |

## Menus

| Element | Key points | Equivalent |
|---|---|---|
| `DropDown` | Items from the parameter or an explicit list, submenus via a separator (`"Group/Item"`), mouse wheel and arrows, unavailable items hidden (`IsItemAvailable`) | `COptionMenu`, `IVMenuButtonControl` |
| `MenuButton` | Arbitrary menu (`MenuItem`), hamburger / dots / text + chevron styles | `IVMenuButtonControl` |
| `MenuPopup` | Drawn menu: check marks, shortcuts, separators, submenus, keyboard, type-ahead, scrolling | `IPopupMenuControl` |
| `NativeMenu` | Native Win32 menu (`UIHost.UseNativeMenus`) | `COptionMenu` (native) |
| DAW context menu | Right-click on a bound control: VST3 host menu (`IComponentHandler3`), custom entries can be added | built into VSTGUI and iPlug2 |

## Text

| Element | Key points | Equivalent |
|---|---|---|
| `Label` | One or more lines, alignments, `TextSource` refreshed every frame (real-time text) | `CTextLabel`, `CMultiLineTextLabel`, `ITextControl`, `IRTTextControl`, `IVLabelControl`, `IMultiLineTextControl` |
| `ValueDisplay` | Parameter value, entry by double-click or Enter, optional dragging | `CParamDisplay`, `ICaptionControl` |
| `NumberBox` | Drag, mouse wheel, text entry, −/+ buttons | `IVNumberBoxControl` |
| `TextBox` | Single-line entry: selection, clipboard, words (Ctrl+arrows), filter, max. length, read-only, search style | `CTextEdit`, `CSearchTextEdit`, `ITextEntryControl` |
| `UIHost.BeginTextEdit` | In-place text entry over any rectangle | `IEditableTextControl` |
| `SegmentDisplay` | Vector 7-segment display, unlit segments visible, decimal point, slant, glow | `CSpecialDigit` |
| `Hyperlink` | Clickable link (default browser) | `IURLControl` |
| `BitmapText`, `BitmapFont` | Text in a fixed-width bitmap font (LCD, VFD, pixel art), integer scaling without smoothing, unlit segments visible, real-time text | `IBTextControl` |
| Value bubble | `ShowValueBubble` on any `ValueControl` | `IBubbleControl` |

## Metering and visualization

| Element | Key points | Equivalent |
|---|---|---|
| `LevelMeter` | 1 to 64 channels, gradient / segments / solid, peak + RMS, peak hold, clip indicator, dB scale, gain reduction mode, lock-free `PeakValue` sources, film strip via `Skin` (one frame per level, drawn per channel) | `CVuMeter`, `IVMeterControl`, `IVLEDMeterControl`, `IVPeakAvgMeterControl`, `IBMeterControl` (+ `FilmStripSkin`) |
| `Led` | Brightness, glow, round or square shapes, function-based source | `ILEDControl` |
| `Oscilloscope` | Reads an `AudioRingBuffer`, rising-edge trigger, gain, fill, glow | `IVScopeControl`, `IVDisplayControl` |
| `SpectrumAnalyzer` | FFT (`Dsp.Fft`), log axis 20 Hz–20 kHz, slope compensation, attack/release smoothing, peak hold, fractional-octave bar mode | `IVSpectrumAnalyzerControl`, `IVBarGraphSpectrumAnalyzerControl` |
| `Plot` | Functions or point lists, logarithmic X, fill, automatic tick marks (equalizer curve) | `IVPlotControl` |
| `ProgressBar` | Determinate (with percentage) or indeterminate | — |
| `FpsCounter` | Frames per second and frame time (debugging) | `IFPSDisplayControl` |
| `HistoryGraph` | Scrolling history of a value (gain reduction, envelope, LFO): line, filled or bars, lock-free `PeakValue` source or polled function | `IVDisplayControl` |
| `ParametricEqEditor`, `EqBand` | Parametric equalizer: exact curve of the RBJ biquads, draggable nodes (frequency and gain in parallel gestures), Q with the mouse wheel, shape menu per band, adding and removing bands, spectrum in the background | — (bonus) |
| `FrequencyResponseView`, `ResponseCurve`, `ResponseNode` | Response display for any filter: consumer-supplied curves, log or linear frequency axis, fixed left axis and auto-scaled right axis (phase in multiples of π, delays), analyzed input spectrum (50 % overlap, release smoothing, 20·log10 or 6·log2 dB) and the spectrum shaped by a response without a second FFT, nodes bound to parameters, band nodes defined by two edges with their own handles | — (bonus) |
| `CompressorCurve` | Transfer curve (threshold, ratio, soft knee), threshold and ratio adjustable with the mouse, live input level | — (bonus) |
| `LoudnessMeter` + `Dsp.LoudnessAnalyzer` | EBU R128 / BS.1770-4 loudness: momentary, short-term, integrated (with gating), LRA range, peak; target and tolerance | — (bonus) |
| `Goniometer`, `CorrelationMeter` | Stereo image (M/S Lissajous) and phase correlation −1…+1 | — (bonus) |
| `Spectrogram` | Scrolling spectrogram, logarithmic frequency axis, color map | — (bonus) |
| `WaveformView` + `Dsp.WavReader` | Multichannel waveform: zoom and scrolling, start / end / loop markers that can be bound to parameters, selection, playhead, WAV file drop | — (bonus, sampler) |

## MIDI

| Element | Key points | Equivalent |
|---|---|---|
| `PitchWheel`, `ModWheel` (`WheelControl`) | Pitch wheel (animated return to center within the same gesture) and modulation wheel | `IWheelControl` |
| `PianoKeyboard` | Configurable range, glissando, velocity from the vertical click position, `NoteOn`/`NoteOff`, `SetNoteState` callable from any thread, processor notes via `ActivitySource` (`NoteActivity`, lock-free), playing from the computer keyboard; in an NPlug plug-in, `ConnectKeyboard` sends the notes to the processor | `KeyboardView`, `IVKeyboardControl` |

## Containers and layout

| Element | Key points | Equivalent |
|---|---|---|
| `Canvas` | Absolute positioning (`SetBounds`): the usual root of an editor | `CViewContainer` |
| `StackPanel`, `WrapPanel`, `DockPanel`, `UniformGrid` | Stacking, wrapping, docking, regular grid | `CRowColumnView` |
| `Grid` | `Auto` / pixel / star rows and columns, spanning, spacing (`new Grid("Auto,*", "120,*")`) | iPlug2 Flexbox |
| `Border`, `Panel` | Background (color or gradient, equivalent to `CGradientView`), border, rounded corners, drop shadow, padding | `CShadowViewContainer`, `IVPanelControl`, `IPanelControl` |
| `GroupBox` | Titled frame, accent bar, collapsible | `IVGroupControl` |
| `TabView` | Tabs on top or on the left, animated indicator, pages that can be disabled, Ctrl+Tab; only the visible page is attached | `CTabView`, `IVTabbedPagesControl` |
| `ViewSwitcher` | Shows one child at a time, chosen by `SelectedIndex` or a bound parameter (discrete: step index; continuous: value spread over the children); hidden views keep their state | `UIViewSwitchContainer` |
| `ScrollViewer` | Vertical and horizontal scrolling, thin overlay scroll bars, mouse wheel (Shift: horizontal), `ScrollIntoView` | `CScrollView` |
| `SplitView` | Two panes and a movable splitter | `CSplitView` |
| `ListBox` | Virtualized list, single or extended selection, keyboard, type-ahead search, reordering by dragging | `CListControl`, `CStringList` |
| `DataTable` | Fixed or star columns, sorting, resizing, auto-fit, virtualized rows, cell editing | `CDataBrowser` |
| `CornerResizer` | Editor resize grip (double-click: 100 %) | `ICornerResizerControl` |
| `Separator`, `Spacer` | Separator line, fixed or stretchable space | — |
| `LayeredPanel` | Full-size stacked layers, composited opacity per layer, `BringToFront` / `SendToBack` | `CLayeredViewContainer` |

## Dialogs and pickers

| Element | Key points | Equivalent |
|---|---|---|
| `ColorSwatch`, `ColorPicker`, `ColorPickerPopup` | S/V square, hue, alpha, hexadecimal entry, recent colors, Esc restores | `CColorChooser`, `IColorPickerControl`, `IVColorSwatchControl` |
| `FilePathBox` | Path shortened in the middle, native dialog (file, folder, save), file drop | `CNewFileSelector`, `IDirBrowseControlBase` |
| `FileDialogs` | `IFileOpenDialog` / `IFileSaveDialog` | — |
| `ABCompareBar`, `ParameterSnapshot` | A/B comparison of complete settings, copy to the other slot, restore through gestures | — (bonus) |
| `PresetBar`, `IPresetProvider`, `PresetList` | Previous/next, menu by bank, Save / Save as / Delete, Ctrl+S | `IVBakedPresetManagerControl`, `IVDiskPresetManagerControl` |
| `Dialog` | Modal dialog, default and cancel buttons, `Dialog.ShowMessage` | — |
| `AboutBox`, `SplashScreen` | "About" window, splash screen that fades out | `CSplashScreen`, `IAboutBoxControl`, `CAnimationSplashScreen` |

## Images and integration

| Element | Key points | Equivalent |
|---|---|---|
| `ImageView` | Fitted image (`ImageFit`) or animated film strip (loop, ping-pong) | `CMovieBitmap`, `CAutoAnimation`, `IBitmapControl` |
| `SvgView` | Native SVG, fitting, rotation | `ISVGControl` |
| `NativeWindowHost` | Embeds a third-party Win32 window (video, WebView2 created by the application) | `CExternalView`, `IPlatformViewControl`, `CExternalControl` |

## Skins (`Skins`)

| Skin | Use |
|---|---|
| `FilmStripSkin` | Film strip: frame chosen from the value or the state index (`CMultiFrameBitmap`) |
| `ImageStateSkin` | One image per state: normal, on, hover, pressed, disabled |
| `SvgRotarySkin` | SVG rotor turned according to the value, fixed background and highlight |
| `ImageRotarySkin` | Bitmap rotor turned according to the value, fixed background and highlight (`IBKnobRotaterControl`) |
| `SvgStateSkin` | One SVG per state or per value range |
| `NineSliceSkin` | Stretchable bitmap frame without distorting the corners (`CNinePartTiledBitmap`) |
| `SliderHandleSkin` | Bitmap handle (and track) for `Slider` |
| `DelegateSkin` | Free drawing by a function (`ILambdaControl`) |

## Cross-cutting services

MIDI learn (`UIHost.MidiLearn`, `IMidiLearnService`, `MidiLearnService`: "MIDI Learn" / "Forget MIDI assignment" entries in the context menu, including the DAW's; outline around the control waiting for MIDI), interface language (`UIHost.Language`, English / French `UIStrings` tables, extensible), culture-independent numbers (`InvariantNumber`), tooltips (`ToolTip`), context menus (`ContextMenu`), internal drag and drop (`UIHost.BeginDrag`) and file drop (`AllowFileDrop`), animations (`UIHost.Animator`, `Easing` curves), per-frame tickers, keyboard navigation (Tab, focus ring), clipboard, light and dark themes, live edit mode, off-screen PNG rendering, display DSP (`Dsp.Fft`, `AudioRingBuffer`, `PeakValue`, `LevelBallistics`).

## Not included (by design)

Built-in WebView2 web view (`IWebViewControl`; use `NativeWindowHost`), GPU shaders (`IShaderControl`), OpenGL view (`COpenGLView`), full code editor (VSTGUI text editor), font and date pickers (`CFontChooser`). Multi-touch (`IMultiTouchControlBase`): PluginUI handles the mouse, and single-finger touch through Windows' mouse emulation.
