// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PluginUI.Design.Tests;

/// <summary>Runtime loader, tree capture, generated code (compiled and run) and the live bridge.</summary>
public class BuildTests
{
    private static Dictionary<string, IParameter> LiveParameters(DesignDocument d)
        => d.Parameters.ToDictionary(p => p.Name, p => (IParameter)p.CreatePreview());

    [Test]
    public void LoaderBuildsEveryContainerBindingAndAsset()
    {
        var document = Samples.Synth();
        var assets = new Samples.FakeAssets();
        var gain = new LocalParameter("Gain", -48, 12, 0, "dB");
        var created = new List<(UIElement, DesignNode)>();
        var result = DesignLoader.Build(document, options: new DesignLoadOptions
        {
            ResolveParameter = name => name == "Gain" ? gain : null,
            Assets = assets,
            ElementCreated = (e, n) => created.Add((e, n)),
        });
        Assert.That(result.Warnings, Is.Empty);
        var root = (Canvas)result.Root;
        var cutoff = result.Get<Knob>("cutoff");
        var grid = result.Get<Grid>("grid");
        var tabs = result.Get<TabView>("tabs");
        Assert.Multiple(() =>
        {
            Assert.That(root.Name, Is.EqualTo("root"));
            Assert.That(root.Background, Is.EqualTo(new SolidPaint(Color.FromRgb(0x202226))));
            Assert.That(cutoff.Left, Is.EqualTo(20f));
            Assert.That(cutoff.Label, Is.EqualTo("CUTOFF"));
            Assert.That(cutoff.Parameter, Is.SameAs(result.PreviewParameters["Cutoff"]), "unresolved: preview parameter");
            Assert.That(cutoff.Parameter!.ToPlain(0.5), Is.EqualTo(Math.Sqrt(20 * 20000)).Within(1e-6), "logarithmic preview");
            Assert.That(root.Children.OfType<Knob>().Single(k => k.Name is null).Parameter, Is.SameAs(gain), "resolved: live parameter");
            Assert.That(grid.RowDefinitions, Is.EqualTo(new[] { GridLength.Auto, GridLength.OneStar }));
            Assert.That(grid.ColumnDefinitions, Is.EqualTo(new[] { GridLength.Pixels(100f), GridLength.Star(2f), GridLength.OneStar }));
            Assert.That(grid.Children[1], Is.TypeOf<Led>());
            Assert.That(grid.Children[1].Column, Is.EqualTo(2));
            Assert.That(root.Children.OfType<Border>().Single().Content, Is.TypeOf<Label>());
            var split = root.Children.OfType<SplitView>().Single();
            Assert.That(((Label)split.First!).Text, Is.EqualTo("left"), "slots, not order");
            Assert.That(((Label)split.Second!).Text, Is.EqualTo("right"));
            Assert.That(tabs.Pages.Select(p => p.Header), Is.EqualTo(new[] { "First tab", "Second tab" }));
            Assert.That(((StackPanel)tabs.Pages[1].Content!).Children.Single(), Is.TypeOf<Slider>());
            Assert.That(result.Get<Knob>("skinned").Skin, Is.TypeOf<ImageRotarySkin>());
            Assert.That(assets.Requests, Is.EquivalentTo(new[] { "assets/rotor.png", "assets/back.png", "assets/logo.png", "assets/icon.svg" }));
            Assert.That(created, Has.Count.EqualTo(document.AllNodes().Count()));
        });

        var unknown = DesignJson.Read("""{"format":"pluginui","root":{"type":"Canvas","children":[{"type":"Nope","properties":{"Left":5}}]}}""");
        var r2 = DesignLoader.Build(unknown);
        Assert.That(r2.Warnings, Has.Some.Contains("Nope"));
        Assert.That(((Canvas)r2.Root).Children.Single(), Is.TypeOf<Label>());
        Assert.That(() => DesignLoader.Build(Samples.Synth()), Throws.Nothing, "missing asset resolver: reported, not thrown");
        Assert.That(DesignLoader.Build(Samples.Synth()).Warnings, Has.Some.Contains("asset resolver"));
    }

    [Test]
    public void CaptureIsStableAndDescribesBindings()
    {
        var document = Samples.Synth();
        var live = LiveParameters(document);
        var options = new DesignLoadOptions { ResolveParameter = n => live.GetValueOrDefault(n), Assets = new Samples.FakeAssets() };
        var built = DesignLoader.Build(document, options: options).Root;
        var warnings = new List<string>();
        var captured = DesignCapture.CaptureTree(built, warnings: warnings);
        Assert.That(warnings, Has.Count.EqualTo(3), "the skin, the image and the SVG were set from assets");
        string once = Json(captured);
        string twice = Json(DesignCapture.CaptureTree(DesignLoader.Build(Wrap(captured), options: options).Root));
        Assert.That(twice, Is.EqualTo(once));
        Assert.That(captured.DescendantsAndSelf().First(n => n.Name == "cutoff").Bindings["Parameter"], Is.EqualTo("Cutoff"));

        var host = new UIHost(built, document.DesignSize, document.Sizing) { Theme = document.Theme.Create() };
        var full = DesignCapture.CaptureHost(host);
        Assert.Multiple(() =>
        {
            Assert.That(full.DesignSize, Is.EqualTo(document.DesignSize));
            Assert.That(full.Theme.Accent, Is.EqualTo(document.Theme.Accent));
            Assert.That(full.Theme.Metrics["CornerRadius"], Is.EqualTo(6f));
            Assert.That(full.FindParameter("Cutoff")!.Kind, Is.EqualTo(DesignParameterKind.Logarithmic));
            Assert.That(full.FindParameter("Mode")!.Choices, Is.EqualTo(new[] { "Low", "Band", "High" }));
            Assert.That(full.FindParameter("Bypass")!.Kind, Is.EqualTo(DesignParameterKind.Toggle));
            Assert.That(full.FindParameter("Gain")!.Min, Is.EqualTo(-48).Within(1e-9));
        });
    }

    private static DesignDocument Wrap(DesignNode root)
    {
        var d = Samples.Synth();
        d.Root = root;
        return d;
    }

    private static string Json(DesignNode root) => DesignJson.Write(new DesignDocument { Root = root });

    // ---------------------------------------------------------------- generated code

    private static Assembly Compile(string generated, string userPart)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(p => MetadataReference.CreateFromFile(p)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(UIElement).Assembly.Location));
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create("Generated" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(generated, parse), CSharpSyntaxTree.ParseText(userPart, parse)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        var problems = emit.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Id != "CA1416").Select(d => d.ToString()).ToList();
        Assert.That(emit.Success && problems.Count == 0, Is.True, string.Join("\n", problems) + "\n\n" + generated);
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    private const string UserPart = """
        #nullable enable
        namespace Test.Editor;

        public abstract class FakeView
        {
            protected abstract PluginUI.UIElement Build();
            public PluginUI.UIElement CallBuild() => Build();
        }

        public partial class SynthEditor : FakeView
        {
            public System.Func<string, PluginUI.IParameter?> Resolver = _ => null;
            public System.Func<string, PluginUI.ImageSource> Image = _ => throw new System.InvalidOperationException();
            public System.Func<string, PluginUI.SvgImage> Svg = _ => throw new System.InvalidOperationException();
            public int Built;
            partial void OnBuilt(PluginUI.UIElement root) => Built++;
            private partial PluginUI.IParameter? BindParameter(string name) => Resolver(name);
            private partial PluginUI.ImageSource LoadImage(string path) => Image(path);
            private partial PluginUI.SvgImage LoadSvg(string path) => Svg(path);
        }
        """;

    [Test]
    public void GeneratedCodeCompilesAndBuildsTheSameTreeAsTheLoader()
    {
        var document = Samples.Synth();
        var generator = new CSharpDesignGenerator { Timestamp = new DateTime(2026, 10, 18, 14, 24, 0, DateTimeKind.Utc) };
        string code = generator.Generate(document);
        Assert.That(generator.Warnings, Is.Empty);
        Assert.That(code, Does.StartWith("// 2026/10/18 14:24:00 UTC - Synth"));
        Assert.That(code, Does.Contain("private Knob cutoff = null!;").And.Contain("protected override UIElement Build() => BuildDesign();"));
        Assert.That(generator.Generate(document), Is.EqualTo(code), "deterministic");

        var assembly = Compile(code, UserPart);
        var type = assembly.GetType("Test.Editor.SynthEditor")!;
        object editor = Activator.CreateInstance(type)!;
        var live = LiveParameters(document);
        var assets = new Samples.FakeAssets();
        type.GetField("Resolver")!.SetValue(editor, (Func<string, IParameter?>)(n => live.GetValueOrDefault(n)));
        type.GetField("Image")!.SetValue(editor, (Func<string, ImageSource>)assets.LoadImage);
        type.GetField("Svg")!.SetValue(editor, (Func<string, SvgImage>)assets.LoadSvg);
        var generatedRoot = (UIElement)type.GetMethod("CallBuild")!.Invoke(editor, null)!;
        Assert.That(type.GetField("Built")!.GetValue(editor), Is.EqualTo(1), "OnBuilt called once");
        Assert.That(type.GetField("cutoff", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor), Is.InstanceOf<Knob>());

        var loaded = DesignLoader.Build(document, options: new DesignLoadOptions { ResolveParameter = n => live.GetValueOrDefault(n), Assets = new Samples.FakeAssets() }).Root;
        Assert.That(Json(DesignCapture.CaptureTree(generatedRoot)), Is.EqualTo(Json(DesignCapture.CaptureTree(loaded))));
        Assert.That(((Knob)generatedRoot.FindByName("skinned")!).Skin, Is.TypeOf<ImageRotarySkin>());

        var theme = (Theme)type.GetMethod("CreateDesignTheme")!.Invoke(null, null)!;
        Assert.That(ThemeJson.Export(theme), Is.EqualTo(ThemeJson.Export(document.Theme.Create())));
        Assert.That(type.GetField("DesignSize")!.GetValue(null), Is.EqualTo(document.DesignSize));
    }

    [Test]
    public void StandaloneTargetAndInvalidNames()
    {
        var document = new DesignDocument { Name = "App" };
        document.Codegen.Target = CodegenTarget.Standalone;
        document.Codegen.Namespace = "Test.App";
        document.Codegen.ClassName = "MainView";
        document.Codegen.FieldAccess = "internal";
        document.Root.Name = "class"; // keyword: becomes a local
        document.Root.Add(new DesignNode("Label") { Name = "dup" }.Set("Text", "a"));
        document.Root.Add(new DesignNode("Label") { Name = "dup" }.Set("Text", "b"));
        document.Root.Add(new DesignNode("Label") { Name = "has space" });
        var generator = new CSharpDesignGenerator();
        string code = generator.Generate(document);
        Assert.That(generator.Warnings, Has.Count.EqualTo(3));
        Assert.That(code, Does.Contain("public UIElement BuildDesign()").And.Not.Contain("protected override").And.Not.Contain("partial IParameter? BindParameter"));
        Assert.That(code, Does.Contain("internal Label dup = null!;"));
        var assembly = Compile(code, "namespace Test.App; public partial class MainView { }");
        var view = Activator.CreateInstance(assembly.GetType("Test.App.MainView")!)!;
        var root = (Canvas)view.GetType().GetMethod("BuildDesign")!.Invoke(view, null)!;
        Assert.That(root.Children.OfType<Label>().Select(l => l.Text), Is.EqualTo(new string?[] { "a", "b", null }));
        Assert.That(root.Name, Is.EqualTo("class"));
    }

    [Test]
    public void GeneratedCodeSurvivesHostileNamesAndValues()
    {
        var document = new DesignDocument { Name = "Hostile" };
        document.Codegen.Target = CodegenTarget.Standalone;
        document.Codegen.Namespace = "Test.Hostile";
        document.Codegen.ClassName = "HostileView";
        // Names that would hide types used by the generated code, or the class itself.
        document.Root.Add(new DesignNode("Label") { Name = "Color" }.Set("Text", "line\u2028separator\u2029end"));
        document.Root.Add(new DesignNode("Label") { Name = "HostileView" });
        document.Root.Add(new DesignNode("Slider") { Name = "Orientation" }.Set("Orientation", Orientation.Horizontal));
        var grid = new DesignNode("Grid") { Name = "grid" }.Set("Rows", "1*, x, 2*");
        document.Root.Add(grid);
        // Split view: an explicit First, a free child (Second), then a child that does not fit.
        var split = new DesignNode("SplitView") { Name = "split" };
        split.Add(new DesignNode("Label") { Name = "first", Slot = "First" }.Set("Text", "1"));
        split.Add(new DesignNode("Label") { Name = "free" }.Set("Text", "2"));
        split.Add(new DesignNode("Label") { Name = "extra", Slot = "First" }.Set("Text", "3"));
        document.Root.Add(split);

        var generator = new CSharpDesignGenerator();
        string code = generator.Generate(document);
        Assert.That(generator.Warnings, Has.Some.Contains("'Color'").And.Some.Contains("'HostileView'").And.Some.Contains("'Orientation'"));
        Assert.That(generator.Warnings, Has.Some.Contains("'x'").And.Some.Contains("split view holds two"));
        Assert.That(code, Does.Contain("\\u2028").And.Contain("\\u2029"));
        var assembly = Compile(code, "namespace Test.Hostile; public partial class HostileView { }");
        var view = Activator.CreateInstance(assembly.GetType("Test.Hostile.HostileView")!)!;
        var generatedRoot = (UIElement)view.GetType().GetMethod("BuildDesign")!.Invoke(view, null)!;

        var loaded = DesignLoader.Build(document);
        Assert.That(loaded.Warnings, Has.Some.Contains("split view holds two"));
        Assert.That(Json(DesignCapture.CaptureTree(generatedRoot)), Is.EqualTo(Json(DesignCapture.CaptureTree(loaded.Root))), "generator and loader agree");
        var splitView = (SplitView)generatedRoot.FindByName("split")!;
        Assert.That(((Label)splitView.First!).Text, Is.EqualTo("1"));
        Assert.That(((Label)splitView.Second!).Text, Is.EqualTo("2"));
        Assert.That(((Grid)generatedRoot.FindByName("grid")!).RowDefinitions, Has.Count.EqualTo(2));
        Assert.That(((Label)generatedRoot.FindByName("Color")!).Text, Is.EqualTo("line\u2028separator\u2029end"));

        // BindDesignFields points the fields to another tree built from the design (live reload).
        var other = DesignLoader.Build(document).Root;
        view.GetType().GetMethod("BindDesignFields", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [other]);
        Assert.That(view.GetType().GetField("split", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view), Is.SameAs(other.FindByName("split")));

        // Undefined enum values and invalid settings.
        var odd = new DesignDocument { Name = "Odd" };
        odd.Codegen.Namespace = "Test.Odd";
        odd.Codegen.ClassName = "OddView";
        odd.Codegen.Target = CodegenTarget.Standalone;
        odd.Root.Add(new DesignNode("Label") { Name = "label" }.Set("TextAlignment", (HorizontalAlignment)(-1)));
        string oddCode = new CSharpDesignGenerator().Generate(odd);
        Assert.That(oddCode, Does.Contain("(HorizontalAlignment)(-1)"));
        Compile(oddCode, "namespace Test.Odd; public partial class OddView { }");
        odd.Codegen.Namespace = "My Plugin";
        Assert.That(() => new CSharpDesignGenerator().Generate(odd), Throws.InvalidOperationException);
        Assert.That(CSharpDesignGenerator.ValidateCodegen(new CodegenSettings { Namespace = "Knob.Editor", ClassName = "View" }), Is.Not.Null);
        Assert.That(CSharpDesignGenerator.ValidateCodegen(new CodegenSettings { Namespace = "My.Editor", ClassName = "Canvas" }), Is.Not.Null);
        Assert.That(CSharpDesignGenerator.ValidateCodegen(new CodegenSettings { Namespace = "My.Editor", ClassName = "View" }), Is.Null);
        Assert.That(CSharpDesignGenerator.IsElementName("FontWeight", null, "View"), Is.False, "type of a TextStyle field");
        Assert.That(CSharpDesignGenerator.IsElementName("Parameters", null, "View"), Is.False, "member of the scaffolded class");
        Assert.That(CSharpDesignGenerator.IsElementName("knob", null, "View"), Is.True);
        Assert.That(CSharpDesignGenerator.IsElementName("Knob", null, "View"), Is.False);
        Assert.That(CSharpDesignGenerator.IsElementName("View", null, "View"), Is.False);
    }

    // ---------------------------------------------------------------- live bridge

    private static string TempDesign(DesignDocument document)
    {
        string dir = Path.Combine(Path.GetTempPath(), "pluginui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "Editor.pluginui");
        DesignJson.Save(document, path);
        return path;
    }

    private static bool WaitFor(UIHost host, Func<bool> condition, int milliseconds = 4000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            host.ProcessPostedActions();
            if (condition()) return true;
            Thread.Sleep(20);
        }
        host.ProcessPostedActions();
        return condition();
    }

    [Test]
    public void BridgeSavesLayoutAndHotReloads()
    {
        var document = Samples.Synth();
        string path = TempDesign(document);
        var assets = new Samples.FakeAssets();
        DesignBuildResult Rebuild(DesignDocument d) => DesignLoader.Build(d, options: new DesignLoadOptions { Assets = assets });
        var host = new UIHost(new Canvas(), document.DesignSize, document.Sizing);
        using var bridge = new LiveDesignBridge(host, path, Rebuild);
        int reloads = 0;
        bridge.Reloaded += (_, _) => reloads++;
        Assert.That(bridge.Reload(), Is.Not.Null);
        Assert.That(reloads, Is.EqualTo(1));

        // Nothing edited yet: Ctrl+S writes nothing (it must not revert changes made in the designer).
        Assert.That(bridge.Save(), Has.Some.Contains("nothing to save"));

        // Live edit moves the knob (Shift+Right: 10 DIPs, twice) and widens it (Ctrl+Right: 1 DIP, 14 times):
        // Ctrl+S merges its layout, the rest of the file is kept.
        host.UpdateLayout();
        host.IsLiveEditEnabled = true;
        var cutoff = (Knob)host.Root.FindByName("cutoff")!;
        host.LiveEditor.Selected = cutoff;
        for (int i = 0; i < 2; i++) host.DispatchKeyDown(new KeyEventArgs(Key.Right, ModifierKeys.Shift));
        for (int i = 0; i < 14; i++) host.DispatchKeyDown(new KeyEventArgs(Key.Right, ModifierKeys.Control));
        Assert.That(host.LiveEditor.EditedElements, Has.Member(cutoff));
        var warnings = bridge.Save();
        Assert.That(warnings, Is.Empty);
        var saved = DesignJson.Load(path);
        Assert.Multiple(() =>
        {
            Assert.That(saved.FindNode("cutoff")!.Get("Left"), Is.EqualTo(40f));
            Assert.That(saved.FindNode("cutoff")!.Get("Width"), Is.EqualTo(90f));
            Assert.That(saved.FindNode("cutoff")!.Get("Label"), Is.EqualTo("CUTOFF"));
            Assert.That(saved.FindNode("skinned")!.Get("Skin"), Is.TypeOf<ImageRotarySkinDescriptor>(), "assets survive");
            Assert.That(saved.Parameters, Has.Count.EqualTo(4));
        });
        Thread.Sleep(600);
        host.ProcessPostedActions();
        Assert.That(reloads, Is.EqualTo(1), "our own save does not reload");

        // The designer saved a new version: the editor rebuilds from it.
        saved.FindNode("title")!.Set("Text", "Reloaded");
        DesignJson.Save(saved, path);
        Assert.That(WaitFor(host, () => reloads == 2), Is.True, "hot reload");
        Assert.That(((Label)host.Root.FindByName("title")!).Text, Is.EqualTo("Reloaded"));
        Assert.That(((Knob)host.Root.FindByName("cutoff")!).Left, Is.EqualTo(40f));
    }

    [Test]
    public void BridgeCapturesTheWholeTreeWithoutFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pluginui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "New.pluginui");
        var root = new Canvas { Name = "root" };
        root.Add(new Knob { Name = "drive", Left = 10f, Top = 20f, Width = 60f, Height = 80f, Parameter = new LocalParameter("Drive", 0, 10, 2) });
        var host = new UIHost(root, new Size(200f, 120f));
        using var bridge = new LiveDesignBridge(host, path);
        host.IsLiveEditEnabled = true;
        host.DispatchKeyDown(new KeyEventArgs(Key.S, ModifierKeys.Control)); // Ctrl+S in live edit mode
        var saved = DesignJson.Load(path);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Name, Is.EqualTo("New"));
            Assert.That(saved.DesignSize, Is.EqualTo(new Size(200f, 120f)));
            Assert.That(saved.FindNode("drive")!.Get("Top"), Is.EqualTo(20f));
            Assert.That(saved.FindNode("drive")!.Bindings["Parameter"], Is.EqualTo("Drive"));
            Assert.That(saved.FindParameter("Drive")!.Max, Is.EqualTo(10).Within(1e-9));
        });
    }
}
