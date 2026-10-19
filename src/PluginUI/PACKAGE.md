<!-- 2026/10/19 13:37:00 UTC - PluginUI -->
# PluginUI

Win32 + Direct2D user interface toolkit for audio plug-in editors written in C# (.NET 10), **NativeAOT-compatible**:
everything is drawn with Direct2D / DirectWrite in a single child window of the host, with no dependency on WinForms,
WPF or any UI runtime.

- About eighty elements (knobs, sliders, buttons, menus, text, meters, analyzers, keyboard, MSEG, sequencers, EQ…),
  each available as styleable vector drawing, bitmap / film strip and SVG.
- Layout panels, themes, skins, popups, tooltips, live layout editing, per-monitor DPI.
- Lock-free audio → UI helpers (`PluginUI.Dsp`): peak values, ring buffers, FFT, loudness (EBU R128), SIMD.
- Parameters with the VST3 begin / perform / end gesture protocol; standalone windows for applications.

Windows 10 1809 or later. Plug-in hosting for [NPlug](https://github.com/xoofx/NPlug) (VST3) lives in the
[PluginUI.NPlug](https://www.nuget.org/packages/PluginUI.NPlug) package.

Documentation, guides and API reference: https://asm8086.github.io/PluginUI/ — source: https://github.com/asm8086/PluginUI
