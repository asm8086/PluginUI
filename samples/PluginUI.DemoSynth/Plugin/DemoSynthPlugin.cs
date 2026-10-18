// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using NPlug;

namespace PluginUI.DemoSynth.Plugin;

/// <summary>Declares the plugin classes to the host through the NPlug factory.</summary>
public static class DemoSynthPlugin
{
    /// <summary>Plugin name displayed by the host.</summary>
    public const string Name = "PluginUI DemoSynth";

    /// <summary>Plugin version.</summary>
    public static readonly Version Version = new(0, 1, 0);

    /// <summary>Creates the factory describing the processor and controller classes.</summary>
    /// <returns>The factory.</returns>
    public static AudioPluginFactory GetFactory()
    {
        var factory = new AudioPluginFactory(new AudioPluginFactoryInfo("PluginUI", "https://github.com/asm8086/PluginUI", "asm8086@users.noreply.github.com"));
        factory.RegisterPlugin<DemoSynthProcessor>(new AudioProcessorClassInfo(DemoSynthProcessor.ClassId, Name, AudioProcessorCategory.InstrumentSynthesizer)
        {
            Version = Version,
        });
        factory.RegisterPlugin<DemoSynthController>(new AudioControllerClassInfo(DemoSynthController.ClassId, Name + " Controller")
        {
            Version = Version,
        });
        return factory;
    }

    /// <summary>Runs when the module is loaded (debug proxy or NativeAOT binary) and publishes the factory.</summary>
    [ModuleInitializer]
    internal static void ExportThisPlugin() => AudioPluginFactoryExporter.Instance = GetFactory();
}
