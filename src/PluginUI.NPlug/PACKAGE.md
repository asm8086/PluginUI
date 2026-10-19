<!-- 2026/10/19 13:37:00 UTC - PluginUI -->
# PluginUI.NPlug

Hosts [PluginUI](https://www.nuget.org/packages/PluginUI) editors in VST3 plug-ins written with
[NPlug](https://github.com/xoofx/NPlug) (C#, .NET 10, NativeAOT).

- `PluginEditorView`: the VST3 editor view. Derive, build the element tree in `Build()` and bind controls with
  `Parameters[model.Gain]`; window lifecycle, content scale, resizing negotiation, keyboard and wheel forwarding and the
  host parameter context menus are handled.
- `PluginUIController<TModel>`: controller base class with the parameter edit bridge (begin / perform / end gestures),
  MIDI learn saved with the controller state, and access to an object shared by the processor.
- `PluginUIProcessor<TModel>`: processor base class that receives the notes played on the editor (keyboard, pads) and
  shares an object with the editor for real-time displays (meters, spectrum, active notes).

This package depends on NPlug and brings its build files: referencing `PluginUI.NPlug` is enough for a plug-in project
(`<OutputType>Library</OutputType>`, `win-x64` or `win-arm64` runtime identifier). `dotnet build` produces a debuggable
`.vst3` loaded through the NPlug proxy; `dotnet publish -c Release` produces the NativeAOT `.vst3`.

Windows 10 1809 or later. Guide and API reference: https://asm8086.github.io/PluginUI/ — source and demo plug-in:
https://github.com/asm8086/PluginUI
