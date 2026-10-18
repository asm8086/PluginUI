---
# 2026/10/18 15:48:00 UTC - PluginUI
uid: PluginUI
summary: *content
---
The user interface toolkit: hosting (<xref:PluginUI.UIHost>, <xref:PluginUI.StandaloneWindow>), the element tree
(<xref:PluginUI.UIElement>, layout panels), about eighty controls, themes and skins, Direct2D drawing
(<xref:PluginUI.DrawingContext>), parameters (<xref:PluginUI.IParameter>) and services (clipboard, file dialogs, MIDI learn).

Everything is measured in DIPs (1/96 inch). Elements are created and modified on the UI thread; other threads use
<xref:PluginUI.UIHost.Post(System.Action)> or lock-free sources such as <xref:PluginUI.Dsp.PeakValue>.
See the [element catalog](../../docs/CONTROLS.md) and [Extending PluginUI](../../docs/EXTENDING.md).
