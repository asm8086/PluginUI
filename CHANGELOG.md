<!-- 2026/10/18 16:20:00 UTC - PluginUI -->

# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).
Before 1.0, minor versions may contain breaking changes.

## [Unreleased]

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

[Unreleased]: https://github.com/asm8086/PluginUI/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/asm8086/PluginUI/releases/tag/v0.1.0
