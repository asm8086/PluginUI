---
# 2026/10/18 15:48:00 UTC - PluginUI
_layout: landing
---

# PluginUI

**Win32 + Direct2D user interfaces for audio plug-ins written in C#.**

PluginUI draws everything with Direct2D / DirectWrite in a single child window of the host, has no dependency on
WinForms, WPF or any UI runtime, and stays **NativeAOT-compatible**: a plug-in compiles to a native DLL that needs no
.NET installation. It brings together the element catalog of VSTGUI and iPlug2 — about eighty elements, each available
as styleable vector drawing, bitmap / film strip and SVG — with instrument and analysis elements, and the extension
points to create more.

![Gallery](../docs/images/1-rotary-linear.png)

- [Overview](../README.md) — what is in the box, requirements, concepts, performance ([français](../README_FR.md)).
- [Getting started](articles/getting-started.md) — a first editor in an NPlug VST3 plug-in, or in a standalone window.
- [Element catalog](../docs/CONTROLS.md) — every element, with its VSTGUI / iPlug2 equivalent.
- [Extending PluginUI](../docs/EXTENDING.md) — themes, skins, custom controls and containers, popups.
- [Visual designer](../docs/EDITOR.md) — PluginUIEditor and the `.pluginui` format.
- [API reference](xref:PluginUI) — `PluginUI`, `PluginUI.Dsp`, `PluginUI.NPlug`, `PluginUI.Design`.

The source code, issues and releases are on [GitHub](https://github.com/asm8086/PluginUI). MIT license.
