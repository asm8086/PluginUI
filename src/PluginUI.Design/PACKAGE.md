<!-- 2026/10/18 16:20:00 UTC - PluginUI -->
# PluginUI.Design

Designs for [PluginUI](https://www.nuget.org/packages/PluginUI) editors: the `.pluginui` file format produced by the
PluginUIEditor visual designer, and what can be done with it from code.

- `DesignJson`: read and write `.pluginui` files (unknown content is preserved).
- `DesignLoader`: build the element tree of a design at run time — AOT-safe explicit type registry, no reflection
  serializer — for user skins and layouts.
- `CSharpDesignGenerator`: generate the `.Designer.cs` code (no reflection, NativeAOT).
- `LiveDesignBridge`: hot-reload a design in a running editor and write live layout edits back to the file.
- `ProjectScaffolder`: create NPlug effect / instrument projects or standalone applications around a design.

Guide: https://asm8086.github.io/PluginUI/docs/EDITOR.html — source: https://github.com/asm8086/PluginUI
