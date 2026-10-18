<!-- 2026/10/18 15:48:00 UTC - PluginUI -->

# Getting started

This page builds a first PluginUI editor in a VST3 effect written with [NPlug](https://github.com/xoofx/NPlug), then
shows the same toolkit in a standalone window. It assumes Windows 10 1809 or later (x64), the .NET 10 SDK and Visual
Studio 2026 (or any editor plus the `dotnet` command line).

## The fastest way: a project generated from a design

[PluginUIEditor](../../docs/EDITOR.md) creates a complete, buildable project around a design:

1. Build and start the designer: `dotnet run --project tools/PluginUIEditor`.
2. Open `samples/Designs/DemoSynth.pluginui`, or start from an empty design and drop a few knobs on it; add the
   parameters in the *Parameters* tab and bind the knobs to them (*Binding* category of the properties).
3. *File ▸ New project from design*: choose *VST3 effect* or *VST3 instrument* (proposed for a design with a keyboard), a name and a folder.
4. Build it: `dotnet build -c Debug` gives a debuggable `.vst3` (CoreCLR proxy), `dotnet publish -c Release -r win-x64`
   a NativeAOT one. Copy the `.vst3` to `C:\Program Files\Common Files\VST3` and rescan in the DAW.

The project contains the four NPlug classes shown below, the editor split into a generated part (`.Designer.cs`) and
your part, and — in debug builds — the [live design bridge](../../docs/EDITOR.md#live-design-bridge).

## By hand: the four classes of an NPlug plug-in

The project references NPlug (the patched copy, see the [overview](../../README.md#requirements)) and
`PluginUI.NPlug`, which brings `PluginUI`. A generated project or `samples/PluginUI.DemoSynth` shows the complete
`.csproj` (NativeAOT settings, debug proxy, `.vst3` bundle).

**Model** — the parameters, shared by the processor and the controller:

```csharp
using NPlug;

public sealed class GainModel : AudioProcessorModel
{
    public GainModel() : base("Gain")
    {
        AddByPassParameter(); // mandatory for a VST3 effect
        Gain = AddParameter(new AudioRangeParameter("Gain", units: "dB", minValue: -24.0, maxValue: 24.0, defaultPlainValue: 0.0) { Precision = 1 });
    }

    public AudioRangeParameter Gain { get; }
}
```

**Processor** — the audio thread. `PluginUIProcessor<TModel>` adds what the editor needs (notes played on screen,
shared state for meters):

```csharp
using NPlug;
using PluginUI.NPlug;

public sealed class GainProcessor : PluginUIProcessor<GainModel>
{
    public static readonly Guid ClassId = new("1f3a1c62-3a43-4d55-9b0a-5d3f7d9e2a10");

    public GainProcessor() : base(AudioSampleSizeSupport.Float32) { }

    public override Guid ControllerClassId => GainController.ClassId;

    protected override bool Initialize(AudioHostApplication host)
    {
        AddAudioInput("Input", SpeakerArrangement.SpeakerStereo);
        AddAudioOutput("Output", SpeakerArrangement.SpeakerStereo);
        return true;
    }

    protected override void ProcessMain(in AudioProcessData data)
    {
        float gain = MathF.Pow(10f, (float)Model.Gain.Value / 20f);
        var input = data.Input[0];
        var output = data.Output[0];
        for (int c = 0; c < Math.Min(input.ChannelCount, output.ChannelCount); c++)
        {
            var source = input.GetChannelSpanAsFloat32(ProcessSetupData, data, c);
            var target = output.GetChannelSpanAsFloat32(ProcessSetupData, data, c);
            for (int i = 0; i < data.SampleCount; i++) target[i] = source[i] * gain;
        }
    }
}
```

**Controller** — parameter edits from the editor and the host; `PluginUIController<TModel>` provides the
begin / perform / end edit bridge, MIDI learn and the shared state:

```csharp
using System.Runtime.Versioning;
using PluginUI.NPlug;

public sealed class GainController : PluginUIController<GainModel>
{
    public static readonly Guid ClassId = new("5c0d3b8e-8f0e-4c7b-a5f1-2e6b9c4d7a31");

    [SupportedOSPlatform("windows10.0.17763")]
    protected override PluginEditorView? CreateEditor() => new GainEditor(this);
}
```

**Factory** — declares both classes to the host when the module loads:

```csharp
using System.Runtime.CompilerServices;
using NPlug;

public static class GainPlugin
{
    [ModuleInitializer] // the host asks for the factory as soon as the module is loaded
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Plug-in entry point.")]
    internal static void ExportThisPlugin()
    {
        var factory = new AudioPluginFactory(new AudioPluginFactoryInfo("My Company", "https://example.com", "contact@example.com"));
        factory.RegisterPlugin<GainProcessor>(new AudioProcessorClassInfo(GainProcessor.ClassId, "Gain", AudioProcessorCategory.Effect));
        factory.RegisterPlugin<GainController>(new AudioControllerClassInfo(GainController.ClassId, "Gain Controller"));
        AudioPluginFactoryExporter.Instance = factory;
    }
}
```

## The editor

A <xref:PluginUI.NPlug.PluginEditorView> builds its element tree once per view, the first time the host asks for its size or attaches it. Controls bind to
parameters through `Parameters[...]`; everything is positioned in DIPs (1/96 inch) and scaled for the screen DPI:

```csharp
using System.Runtime.Versioning;
using PluginUI;
using PluginUI.NPlug;

[SupportedOSPlatform("windows10.0.17763")]
public sealed class GainEditor(GainController controller)
    : PluginEditorView(controller, new Size(240, 160), SizingMode.ScaleToFit)
{
    protected override UIElement Build()
    {
        var model = controller.Model;
        var root = new Canvas();
        var gain = new Knob(Parameters[model.Gain]) { Label = "GAIN", ShowValueBubble = true };
        gain.SetBounds(24, 24, 72, 96);
        var bypass = new ToggleButton("Bypass") { Parameter = Parameters[model.ByPassParameter!] };
        bypass.SetBounds(120, 56, 90, 28);
        root.Add(gain, bypass, new CornerResizer { Left = 222, Top = 142, Width = 18, Height = 18 });
        return root;
    }
}
```

What the view does for you: the VST3 view life cycle and child window, DPI and content scale, size negotiation with
the host, mouse wheel and keys forwarded by the host, the gestures that make automation and undo work in the DAW, and
the DAW's context menu on a right-click over a bound control.

Next steps:

- browse the [element catalog](../../docs/CONTROLS.md): meters fed from the processor with
  <xref:PluginUI.Dsp.PeakValue>, an on-screen keyboard for instruments, EQ and compressor displays…;
- change the look with a <xref:PluginUI.Theme>, a vector style, or skins made of film strips, bitmaps or SVG
  ([Extending PluginUI](../../docs/EXTENDING.md));
- press **Ctrl+Shift+E** in the plug-in window to move and resize the elements live, and **Ctrl+C** to copy the
  corresponding code; turn the shortcut off in release builds (`host.LiveEditShortcutEnabled = false` in
  `ConfigureHost`), as generated projects do.

## A standalone window

The same elements run in a plain Windows application, without NPlug:

```csharp
using PluginUI;

StandaloneWindow.EnablePerMonitorDpiAwareness();
var root = new Canvas();
root.Add(new Knob(new LocalParameter("Volume", -48, 6, -12, "dB", decimals: 1)) { Label = "VOLUME", Left = 20, Top = 20, Width = 72, Height = 96 });
var host = new UIHost(root, new Size(320, 200), SizingMode.ScaleToFit);
return new StandaloneWindow(host, "My application").Run();
```

<xref:PluginUI.LocalParameter> is a parameter that lives in the application (linear, logarithmic or stepped).
`samples/PluginUI.Gallery` shows every element this way.
