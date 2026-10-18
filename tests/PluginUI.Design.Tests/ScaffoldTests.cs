// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Design.Tests;

/// <summary>Project scaffolding (the generated projects are also built and validated outside the unit tests).</summary>
public class ScaffoldTests
{
    private static readonly DateTime Stamp = new(2026, 10, 18, 14, 24, 0, DateTimeKind.Utc);

    [Test]
    public void EffectProjectContainsEveryPart()
    {
        var design = Samples.Synth();
        var files = ProjectScaffolder.Create(design, new ScaffoldOptions { ProjectName = "Filtr", Kind = ScaffoldKind.Effect, Timestamp = Stamp });
        Assert.That(files.Keys, Is.SupersetOf(new[]
        {
            "Filtr.csproj", "Directory.Build.props", "README.md", ".gitignore",
            "Plugin/FiltrModel.cs", "Plugin/FiltrProcessor.cs", "Plugin/FiltrController.cs", "Plugin/FiltrPlugin.cs", "Plugin/LogRangeParameter.cs",
            "Editor/FiltrEditor.cs", "Editor/FiltrEditor.Designer.cs", "Editor/FiltrEditor.pluginui",
        }));
        string model = files["Plugin/FiltrModel.cs"];
        Assert.Multiple(() =>
        {
            Assert.That(model, Does.Contain("AddByPassParameter(\"Bypass\");"), "the design's Bypass toggle becomes the VST3 bypass");
            Assert.That(model, Does.Contain("Cutoff = AddParameter(new LogRangeParameter(\"Cutoff\", \"Hz\", 20.0, 20000.0, 2400.0, decimals: 0));"));
            Assert.That(model, Does.Contain("new AudioStringListParameter(\"Mode\", [\"Low\", \"Band\", \"High\"], selectedItem: 1)"));
            Assert.That(model, Does.Not.Contain("Bypass = AddParameter"));
            Assert.That(files["Filtr.csproj"], Does.Contain("LIVE_DESIGN").And.Contain("PluginUI.Design.csproj").And.Contain("LogicalName=\"assets/$([System.String]::Copy('%(RecursiveDir)')"));
            Assert.That(files["Editor/FiltrEditor.cs"], Does.Contain("private partial IParameter? BindParameter(string name)").And.Contain("LoadImage").And.Contain("LoadSvg").And.Contain("LiveDesignBridge"));
            Assert.That(files["Editor/FiltrEditor.Designer.cs"], Does.Contain("namespace Filtr.Editor;").And.Contain("partial class FiltrEditor"));
            Assert.That(files.Values, Has.All.StartsWith("// 2026/10/18 14:24:00 UTC - Filtr").Or.StartsWith("<!-- 2026/10/18 14:24:00 UTC - Filtr").Or.StartsWith("# 2026/10/18").Or.StartsWith("{"));
        });
        var copy = DesignJson.Read(files["Editor/FiltrEditor.pluginui"]);
        Assert.That(copy.Codegen.ClassName, Is.EqualTo("FiltrEditor"));
        Assert.That(design.Codegen.ClassName, Is.EqualTo("SynthEditor"), "the original design is not modified");
    }

    [Test]
    public void InstrumentAndStandaloneVariants()
    {
        var design = Samples.Synth();
        var synth = ProjectScaffolder.Create(design, new ScaffoldOptions { ProjectName = "Synthy", Kind = ScaffoldKind.Instrument, IncludeLiveBridge = false, Timestamp = Stamp });
        Assert.That(synth["Plugin/SynthyModel.cs"], Does.Not.Contain("AddByPassParameter").And.Contain("Bypass = AddParameter(new AudioBoolParameter(\"Bypass\""));
        Assert.That(synth["Plugin/SynthyController.cs"], Does.Contain("IsMidiLearnEnabled = true;"));
        Assert.That(synth["Plugin/SynthyProcessor.cs"], Does.Contain("AddEventInput").And.Contain("TryDequeueUINote"));
        Assert.That(synth["Editor/SynthyEditor.cs"], Does.Contain("ConnectKeyboard").And.Not.Contain("LiveDesignBridge"));
        Assert.That(synth["Synthy.csproj"], Does.Not.Contain("LIVE_DESIGN"));
        Assert.That(synth["Plugin/SynthyPlugin.cs"], Does.Contain("AudioProcessorCategory.InstrumentSynthesizer"));

        var app = ProjectScaffolder.Create(design, new ScaffoldOptions { ProjectName = "Viewer", Kind = ScaffoldKind.Standalone, Timestamp = Stamp });
        Assert.That(app.Keys, Does.Contain("Program.cs").And.Contain("Editor/ViewerView.cs").And.Not.Contain("Plugin/ViewerModel.cs"));
        Assert.That(app["Editor/ViewerView.Designer.cs"], Does.Contain("public UIElement BuildDesign()"));
        Assert.That(app["Editor/ViewerView.cs"], Does.Contain("[\"Cutoff\"] = new LocalParameter(\"Cutoff\", 20.0, 20000.0, 2400.0, \"Hz\", ParameterScale.Logarithmic(20.0, 20000.0), 0, 0)"));

        Assert.That(() => ProjectScaffolder.Create(design, new ScaffoldOptions { ProjectName = "my plugin" }), Throws.ArgumentException);
        // The project name is the root namespace: it must not hide the types the generated code uses.
        foreach (var bad in new[] { "Goniometer", "Knob", "Color", "PluginUI", "NPlug", "AudioPlugin", "Editor" })
        {
            Assert.That(ProjectScaffolder.ValidateProjectName(bad), Is.Not.Null, bad);
        }
        Assert.That(ProjectScaffolder.ValidateProjectName("Audiophile"), Is.Null);
        Assert.That(ProjectScaffolder.ValidateProjectName("MySynth"), Is.Null);
    }

    [Test]
    public void WritingRefusesToOverwrite()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pluginui-tests", Guid.NewGuid().ToString("N"));
        var design = Samples.Synth();
        var files = ProjectScaffolder.Create(design, new ScaffoldOptions { ProjectName = "W", Timestamp = Stamp });
        var missing = ProjectScaffolder.Write(files, dir, design, designFolder: dir);
        Assert.That(missing, Has.Count.EqualTo(4), "the sample assets do not exist on disk");
        Assert.That(File.Exists(Path.Combine(dir, "Editor", "WEditor.Designer.cs")), Is.True);
        Assert.That(() => ProjectScaffolder.Write(files, dir, design, null), Throws.TypeOf<IOException>());

        // Assets in sub-folders keep their path as resource name; paths leaving the design folder are refused.
        var nested = new DesignDocument { Name = "N" };
        nested.Root.Add(new DesignNode("ImageView") { Name = "a" }.Set("Image", new DesignAsset("assets/knobs/big.png", DesignAssetKind.Image)));
        nested.Root.Add(new DesignNode("ImageView") { Name = "b" }.Set("Image", new DesignAsset("images/logo.png", DesignAssetKind.Image)));
        nested.Root.Add(new DesignNode("ImageView") { Name = "c" }.Set("Image", new DesignAsset("../outside.png", DesignAssetKind.Image)));
        string source = Path.Combine(Path.GetTempPath(), "pluginui-tests", Guid.NewGuid().ToString("N"));
        foreach (var asset in new[] { "assets/knobs/big.png", "images/logo.png" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(source, asset))!);
            File.WriteAllBytes(Path.Combine(source, asset), [1, 2, 3]);
        }
        var nestedFiles = ProjectScaffolder.Create(nested, new ScaffoldOptions { ProjectName = "Nested", Timestamp = Stamp });
        string project = nestedFiles["Nested.csproj"];
        Assert.That(project, Does.Contain("%(RecursiveDir)").And.Contain("<EmbeddedResource Include=\"Editor\\images\\logo.png\" LogicalName=\"images/logo.png\" />"));
        Assert.That(project, Does.Not.Contain("outside"));
        string target = Path.Combine(source, "out");
        var notCopied = ProjectScaffolder.Write(nestedFiles, target, nested, source);
        Assert.That(notCopied, Is.EqualTo(new[] { "../outside.png" }));
        Assert.That(File.Exists(Path.Combine(target, "Editor", "assets", "knobs", "big.png")), Is.True);
        Assert.That(File.Exists(Path.Combine(target, "Editor", "images", "logo.png")), Is.True);
        Assert.That(File.Exists(Path.Combine(source, "outside.png")), Is.False);
    }
}
