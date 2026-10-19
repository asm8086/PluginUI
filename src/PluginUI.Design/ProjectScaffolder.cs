// 2026/10/19 13:37:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;

namespace PluginUI.Design;

/// <summary>Kind of project created by <see cref="ProjectScaffolder"/>.</summary>
public enum ScaffoldKind
{
    /// <summary>VST3 audio effect (stereo in/out, bypass parameter).</summary>
    Effect,

    /// <summary>VST3 instrument (MIDI input, stereo out, on-screen keyboard bridge, MIDI learn).</summary>
    Instrument,

    /// <summary>Standalone Windows application hosting the design in a window.</summary>
    Standalone,
}

/// <summary>Options of <see cref="ProjectScaffolder"/>.</summary>
public sealed class ScaffoldOptions
{
    /// <summary>Project name: a C# identifier, used for the namespace, the assembly and the class names.</summary>
    public string ProjectName { get; set; } = "MyPlugin";

    /// <summary>Kind of project.</summary>
    public ScaffoldKind Kind { get; set; } = ScaffoldKind.Effect;

    /// <summary>Vendor shown by the DAW.</summary>
    public string Vendor { get; set; } = "My Company";

    /// <summary>Vendor web site.</summary>
    public string Url { get; set; } = "https://example.com";

    /// <summary>Vendor e-mail.</summary>
    public string Email { get; set; } = "contact@example.com";

    /// <summary>Plug-in version.</summary>
    public Version Version { get; set; } = new(0, 1, 0);

    /// <summary>Folder of PluginUI (the one containing <c>src</c>), as written in the project file.</summary>
    public string PluginUIRoot { get; set; } = @"$(MSBuildThisFileDirectory)..\PluginUI\";

    /// <summary>
    /// Adds the live design bridge in Debug builds: Ctrl+S in live edit mode saves the layout to the design, and saving
    /// the design in PluginUIEditor reloads the editor in the DAW (default <c>true</c>).
    /// </summary>
    public bool IncludeLiveBridge { get; set; } = true;

    /// <summary>VST3 class identifier of the processor (default: new).</summary>
    public Guid ProcessorId { get; set; } = Guid.NewGuid();

    /// <summary>VST3 class identifier of the controller (default: new).</summary>
    public Guid ControllerId { get; set; } = Guid.NewGuid();

    /// <summary>Timestamp written in the file headers (default: now, UTC).</summary>
    public DateTime? Timestamp { get; set; }
}

/// <summary>
/// Creates a complete, buildable project from a design: an NPlug VST3 effect or instrument (model with the design
/// parameters, processor, controller, factory, editor with its generated part) or a standalone application.
/// </summary>
/// <remarks>
/// The design is copied into the project (<c>Editor\&lt;Name&gt;Editor.pluginui</c>) with its code generation settings
/// pointing to the new editor class; its assets go to <c>Editor\assets</c> and are embedded in the assembly. The result
/// is a set of files (relative path → text) that the caller writes; existing files are never touched here.
/// </remarks>
public static class ProjectScaffolder
{
    /// <summary>Creates the files of a project.</summary>
    /// <param name="design">The design (not modified: a copy is adapted).</param>
    /// <param name="options">Project options.</param>
    /// <returns>Files by relative path (forward slashes).</returns>
    /// <exception cref="ArgumentException">The project name is not a valid C# identifier.</exception>
    public static IReadOnlyDictionary<string, string> Create(DesignDocument design, ScaffoldOptions options)
        => CreateCore(design, options);

    /// <summary>
    /// Checks a project name: it must be a C# identifier, and must not be the name of a type or namespace the
    /// generated code uses (the project name is also its root namespace, which would hide them).
    /// </summary>
    /// <param name="name">Candidate project name.</param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    /// <returns><c>null</c> when the name is usable, otherwise the reason (English).</returns>
    public static string? ValidateProjectName(string name, DesignTypeRegistry? registry = null)
    {
        if (!CSharpDesignGenerator.IsIdentifier(name)) return $"'{name}' is not a valid C# identifier.";
        if (name is "PluginUI" or "NPlug" or "System" or "Microsoft" or "Windows" or "Editor" or "Plugin"
            || CSharpDesignGenerator.ReservedTypeNames(registry ?? DesignTypeRegistry.Default).Contains(name)
            || (name.StartsWith("Audio", StringComparison.Ordinal) && name.Length > 5 && char.IsUpper(name[5])))
        {
            return $"'{name}' is the name of a type or namespace used by the generated code (PluginUI, NPlug): choose another name.";
        }
        return null;
    }

    /// <summary><c>true</c> if an asset path stays inside the design folder (relative, no "..").</summary>
    /// <param name="path">Asset path.</param>
    /// <returns>Whether the asset can be copied into a project.</returns>
    public static bool IsLocalAssetPath(string path)
    {
        string p = DesignAsset.Normalize(path);
        return p.Length > 0 && !Path.IsPathRooted(p) && !p.Contains(':', StringComparison.Ordinal) && !p.Split('/').Contains("..");
    }

    private static IReadOnlyDictionary<string, string> CreateCore(DesignDocument design, ScaffoldOptions options)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(options);
        string name = options.ProjectName;
        if (ValidateProjectName(name) is { } error) throw new ArgumentException(error, nameof(options));
        // Normalized through JSON, like the designer saves it: values the element types do not know become raw
        // content, so the generated code and the hand-written part agree on what is used (assets, bindings).
        design = DesignJson.Read(DesignJson.Write(design));

        var context = new Context(design, options);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string editor = context.EditorClass;
        string designFile = $"Editor/{editor}.pluginui";

        var copy = DesignJson.Read(DesignJson.Write(design));
        copy.Name = options.Kind == ScaffoldKind.Standalone ? name : editor;
        copy.Codegen.Namespace = context.EditorNamespace;
        copy.Codegen.ClassName = editor;
        copy.Codegen.Target = options.Kind == ScaffoldKind.Standalone ? CodegenTarget.Standalone : CodegenTarget.PluginEditorView;
        copy.Codegen.GenerateBuildOverride = true;
        copy.Comment = $"{(options.Timestamp ?? DateTime.UtcNow).ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC - {name}";
        files[designFile] = DesignJson.Write(copy);
        var generator = new CSharpDesignGenerator { SourceName = $"{editor}.pluginui", Timestamp = options.Timestamp };
        files[$"Editor/{editor}.Designer.cs"] = generator.Generate(copy);

        files["Directory.Build.props"] = context.DirectoryBuildProps();
        files["Directory.Build.targets"] = context.DirectoryBuildTargets();
        files["README.md"] = context.Readme();
        files[".gitignore"] = context.Header("#") + "bin/\nobj/\n.vs/\n*.user\n";
        files["Editor/assets/README.md"] = context.Header("md") + "\nImages and SVG files referenced by the design (`assets/...` paths). They are embedded in the assembly.\n";

        if (options.Kind == ScaffoldKind.Standalone)
        {
            files[$"{name}.csproj"] = context.StandaloneProject();
            files["Program.cs"] = context.StandaloneProgram();
            files[$"Editor/{editor}.cs"] = context.StandaloneEditor();
        }
        else
        {
            files[$"{name}.csproj"] = context.PluginProject();
            files[$"Plugin/{name}Model.cs"] = context.Model();
            files[$"Plugin/{name}Processor.cs"] = options.Kind == ScaffoldKind.Instrument ? context.InstrumentProcessor() : context.EffectProcessor();
            files[$"Plugin/{name}Controller.cs"] = context.Controller();
            files[$"Plugin/{name}Plugin.cs"] = context.Factory();
            files[$"Editor/{editor}.cs"] = context.PluginEditor();
            if (copy.Parameters.Any(p => p.Kind == DesignParameterKind.Logarithmic)) files["Plugin/LogRangeParameter.cs"] = context.LogRangeParameter();
        }
        return files;
    }

    /// <summary>Writes the files of <see cref="Create"/> into a folder, then copies the design assets.</summary>
    /// <param name="files">Files by relative path.</param>
    /// <param name="folder">Project folder (created if needed).</param>
    /// <param name="design">The design (for its assets).</param>
    /// <param name="designFolder">Folder of the original design file (asset paths are relative to it), or <c>null</c>.</param>
    /// <returns>Assets that could not be found.</returns>
    /// <exception cref="IOException">The folder already contains one of the files.</exception>
    public static IReadOnlyList<string> Write(IReadOnlyDictionary<string, string> files, string folder, DesignDocument design, string? designFolder)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(design);
        // Everything is checked before the first write: no half-written project.
        var assets = designFolder is null ? [] : design.AssetPaths().Where(IsLocalAssetPath).Select(DesignAsset.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var path in files.Keys.Concat(assets.Select(a => "Editor/" + a)))
        {
            string full = Path.Combine(folder, path);
            if (File.Exists(full)) throw new IOException($"{full} already exists: choose an empty folder.");
        }
        foreach (var (path, content) in files)
        {
            string full = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, new UTF8Encoding(false));
        }
        // The generated code is as recent as its design (the live bridge reloads a design newer than its code).
        foreach (var path in files.Keys.Where(p => p.EndsWith(DesignDocument.FileExtension, StringComparison.Ordinal)))
        {
            string designer = Path.Combine(folder, Path.ChangeExtension(path, ".Designer.cs"));
            if (File.Exists(designer)) File.SetLastWriteTimeUtc(Path.Combine(folder, path), File.GetLastWriteTimeUtc(designer));
        }
        var missing = new List<string>();
        if (designFolder is null) return design.AssetPaths();
        missing.AddRange(design.AssetPaths().Where(a => !IsLocalAssetPath(a)));
        foreach (var asset in assets)
        {
            string source = Path.Combine(designFolder, asset);
            string target = Path.Combine(folder, "Editor", asset);
            if (!File.Exists(source)) { missing.Add(asset); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }
        return missing;
    }

    private sealed class Context(DesignDocument design, ScaffoldOptions options)
    {
        private readonly string _stamp = (options.Timestamp ?? DateTime.UtcNow).ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

        public string Name => options.ProjectName;
        public string EditorClass => Name + (options.Kind == ScaffoldKind.Standalone ? "View" : "Editor");
        public string EditorNamespace => Name + ".Editor";
        private bool Instrument => options.Kind == ScaffoldKind.Instrument;

        public string Header(string kind) => kind switch
        {
            "xml" or "md" => $"<!-- {_stamp} UTC - {Name} -->\n",
            "#" => $"# {_stamp} UTC - {Name}\n",
            _ => $"// {_stamp} UTC - {Name}\n",
        };

        private static string S(string text) => DesignValues.CSharpString(text);

        private static string D(double value) => DesignValues.CSharpDouble(value);

        private string Version3 => $"{options.Version.Major}, {options.Version.Minor}, {Math.Max(0, options.Version.Build)}";

        /// <summary>Model properties: (property name, design parameter), bypass excluded for effects.</summary>
        private IEnumerable<(string Property, DesignParameter Parameter)> ModelParameters()
        {
            var used = new HashSet<string>(StringComparer.Ordinal) { "Model", "ByPassParameter", "Name", "Id" };
            foreach (var p in design.Parameters)
            {
                if (IsBypass(p)) continue;
                string property = Identifier(p.Name);
                while (!used.Add(property)) property += "_";
                yield return (property, p);
            }
        }

        private bool IsBypass(DesignParameter p) => !Instrument && p.Kind == DesignParameterKind.Toggle && p.Name.Equals("Bypass", StringComparison.OrdinalIgnoreCase);

        private static string Identifier(string text)
        {
            var sb = new StringBuilder();
            bool upper = true;
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else
                {
                    upper = true;
                }
            }
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, 'P');
            string id = sb.ToString();
            return CSharpDesignGenerator.IsIdentifier(id) ? id : id + "Parameter";
        }

        public string DirectoryBuildProps() => Header("xml") + """
            <Project>

              <PropertyGroup>
                <OptimizationPreference Condition="'$(OptimizationPreference)' == ''">Speed</OptimizationPreference>
              </PropertyGroup>

            </Project>

            """;

        public string DirectoryBuildTargets() => Header("xml") + """
            <Project>

              <!--
                Evaluated after the project, once its runtime identifier (win-x64 or win-arm64) is known. NativeAOT x64 builds
                target x86-64-v3 (AVX2, FMA: Intel 2013+, AMD 2015+); for older CPUs publish with
                -p:IlcInstructionSet=x86-64-v2. JIT builds (Debug) use every instruction set of the CPU.
              -->
              <PropertyGroup>
                <IlcInstructionSet Condition="'$(IlcInstructionSet)' == '' and $(RuntimeIdentifier.EndsWith('-x64'))">x86-64-v3</IlcInstructionSet>
              </PropertyGroup>

            </Project>

            """;

        private string AssetItems => $$"""
              <ItemGroup>
                <!-- Design assets, embedded with their design path ("assets/knob.png") as resource name. -->
                <EmbeddedResource Include="Editor\assets\**\*" Exclude="Editor\assets\**\*.md" LogicalName="assets/$([System.String]::Copy('%(RecursiveDir)').Replace('\', '/'))%(Filename)%(Extension)" />
                <None Include="Editor\*.pluginui" />{{OtherAssetItems}}
              </ItemGroup>
            """;

        // Design assets outside Editor\assets (e.g. "images/knob.png"): embedded one by one with their design path.
        private string OtherAssetItems => string.Concat(design.AssetPaths()
            .Where(IsLocalAssetPath)
            .Select(DesignAsset.Normalize)
            .Where(a => !a.StartsWith("assets/", StringComparison.Ordinal)) // resource names are case-sensitive
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(a => $"\n    <EmbeddedResource Include=\"Editor\\{System.Security.SecurityElement.Escape(a.Replace('/', '\\'))}\" LogicalName=\"{System.Security.SecurityElement.Escape(a)}\" />"));

        public string PluginProject()
        {
            string bridge = options.IncludeLiveBridge ? """

                  <!-- Debug only: live design bridge (hot reload of the .pluginui file, Ctrl+S from live edit). -->
                  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                    <ProjectReference Include="$(PluginUIRoot)src\PluginUI.Design\PluginUI.Design.csproj" />
                  </ItemGroup>
                """ : string.Empty;
            string define = options.IncludeLiveBridge ? "\n    <DefineConstants Condition=\"'$(Configuration)' == 'Debug'\">$(DefineConstants);LIVE_DESIGN</DefineConstants>" : string.Empty;
            return Header("xml") + $$"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Library</OutputType>
                    <RootNamespace>{{Name}}</RootNamespace>
                    <AssemblyName>{{Name}}</AssemblyName>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <IsPackable>false</IsPackable>
                    <!-- Debug: the NPlug proxy loads this assembly with CoreCLR, its dependencies must sit next to it. -->
                    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
                    <IsAotCompatible>true</IsAotCompatible>
                    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
                    <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())</RuntimeIdentifier>{{define}}
                  </PropertyGroup>

                  <PropertyGroup>
                    <PluginUIRoot Condition="'$(PluginUIRoot)' == ''">{{options.PluginUIRoot}}</PluginUIRoot>
                  </PropertyGroup>

                  <!--
                    PluginUI.NPlug brings the NPlug package and its build files: native debug proxy (Debug), GetPluginFactory
                    export, NativeAOT defaults, .vst3 output. For another NPlug version, add <PackageReference Include="NPlug" />.
                  -->
                  <ItemGroup>
                    <ProjectReference Include="$(PluginUIRoot)src\PluginUI.NPlug\PluginUI.NPlug.csproj" />
                  </ItemGroup>
                {{bridge}}
                {{AssetItems}}

                </Project>

                """;
        }

        public string Model()
        {
            var sb = new StringBuilder();
            sb.Append(Header("cs"));
            sb.Append(CultureInfo.InvariantCulture, $$"""

                using NPlug;

                namespace {{Name}}.Plugin;

                /// <summary>
                /// Parameters of {{Name}}, shared (by value, through the host) between the processor and the controller.
                /// Generated from the design: the parameter titles are the names the editor controls bind to.
                /// </summary>
                /// <remarks>Identifiers follow the declaration order: only append new parameters once presets or automation exist.</remarks>
                public sealed class {{Name}}Model : AudioProcessorModel
                {
                    /// <summary>Declares the parameters.</summary>
                    public {{Name}}Model() : base({{S(Name)}})
                    {

                """);
            if (!Instrument)
            {
                var bypass = design.Parameters.FirstOrDefault(IsBypass);
                sb.Append("        // A bypass parameter is mandatory for a VST3 effect.\n");
                sb.Append(CultureInfo.InvariantCulture, $"        AddByPassParameter({(bypass is null ? string.Empty : S(bypass.Name))});\n");
            }
            foreach (var (property, p) in ModelParameters())
            {
                sb.Append(CultureInfo.InvariantCulture, $"        {property} = AddParameter({Create(p)});\n");
            }
            sb.Append("    }\n");
            foreach (var (property, p) in ModelParameters())
            {
                sb.Append(CultureInfo.InvariantCulture, $"\n    /// <summary>{System.Security.SecurityElement.Escape(DesignValues.CommentText(p.Name))}{(p.Unit.Length > 0 ? " (" + System.Security.SecurityElement.Escape(DesignValues.CommentText(p.Unit)) + ")" : string.Empty)}.</summary>\n");
                sb.Append(CultureInfo.InvariantCulture, $"    public {TypeOf(p)} {property} {{ get; }}\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string TypeOf(DesignParameter p) => p.Kind switch
        {
            DesignParameterKind.Logarithmic => "LogRangeParameter",
            DesignParameterKind.Toggle => "AudioBoolParameter",
            DesignParameterKind.Choice when p.Choices.Count >= 2 => "AudioStringListParameter",
            DesignParameterKind.Choice => "AudioBoolParameter",
            _ => "AudioRangeParameter",
        };

        private static string Create(DesignParameter p)
        {
            string unit = S(p.Unit);
            return p.Kind switch
            {
                DesignParameterKind.Logarithmic => $"new LogRangeParameter({S(p.Name)}, {unit}, {D(Math.Max(p.Min, 1e-9))}, {D(Math.Max(p.Max, p.Min + 1e-9))}, {D(p.Default)}, decimals: {p.Decimals.ToString(CultureInfo.InvariantCulture)})",
                DesignParameterKind.Toggle => $"new AudioBoolParameter({S(p.Name)}, defaultValue: {(p.Default >= 0.5 ? "true" : "false")})",
                DesignParameterKind.Choice when p.Choices.Count >= 2 => $"new AudioStringListParameter({S(p.Name)}, [{string.Join(", ", p.Choices.Select(S))}], selectedItem: {((int)Math.Round(p.Default)).ToString(CultureInfo.InvariantCulture)})",
                DesignParameterKind.Choice => $"new AudioBoolParameter({S(p.Name)})",
                DesignParameterKind.Integer => $"new AudioRangeParameter({S(p.Name)}, units: {unit}, minValue: {D(p.Min)}, maxValue: {D(Math.Max(p.Max, p.Min + 1))}, defaultPlainValue: {D(p.Default)}, stepCount: {((int)Math.Round(Math.Max(1, p.Max - p.Min))).ToString(CultureInfo.InvariantCulture)}) {{ Precision = 0 }}",
                _ => $"new AudioRangeParameter({S(p.Name)}, units: {unit}, minValue: {D(p.Min)}, maxValue: {D(Math.Max(p.Max, p.Min + 1e-9))}, defaultPlainValue: {D(p.Default)}) {{ Precision = {p.Decimals.ToString(CultureInfo.InvariantCulture)} }}",
            };
        }

        public string EffectProcessor() => Header("cs") + $$"""

            using NPlug;
            using PluginUI.NPlug;

            namespace {{Name}}.Plugin;

            /// <summary>
            /// Audio processor of {{Name}}: stereo in, stereo out. Runs on the host audio thread: no allocation, no lock.
            /// Write the DSP in <see cref="ProcessMain"/> (it currently passes the audio through).
            /// </summary>
            public sealed class {{Name}}Processor : PluginUIProcessor<{{Name}}Model>
            {
                /// <summary>VST3 class identifier of the processor.</summary>
                public static readonly Guid ClassId = new("{{options.ProcessorId}}");

                /// <summary>Creates the processor (32-bit float processing).</summary>
                public {{Name}}Processor() : base(AudioSampleSizeSupport.Float32)
                {
                }

                /// <inheritdoc />
                public override Guid ControllerClassId => {{Name}}Controller.ClassId;

                /// <inheritdoc />
                protected override bool Initialize(AudioHostApplication host)
                {
                    AddAudioInput("Input", SpeakerArrangement.SpeakerStereo);
                    AddAudioOutput("Output", SpeakerArrangement.SpeakerStereo);
                    return true;
                }

                /// <inheritdoc />
                protected override void ProcessMain(in AudioProcessData data)
                {
                    // Parameters: Model.<Name>.Value (plain) or .NormalizedValue, updated before each block.
                    var input = data.Input[0];
                    var output = data.Output[0];
                    int channels = Math.Min(input.ChannelCount, output.ChannelCount);
                    for (int c = 0; c < channels; c++)
                    {
                        var source = input.GetChannelSpanAsFloat32(ProcessSetupData, data, c);
                        var target = output.GetChannelSpanAsFloat32(ProcessSetupData, data, c);
                        source[..data.SampleCount].CopyTo(target);
                    }
                }
            }

            """;

        public string InstrumentProcessor() => Header("cs") + $$"""

            using NPlug;
            using PluginUI.NPlug;

            namespace {{Name}}.Plugin;

            /// <summary>
            /// Audio processor of {{Name}}: MIDI in, stereo out. A minimal sine voice shows the note path (host MIDI and the
            /// editor's keyboard); replace it with your synthesis. Runs on the audio thread: no allocation, no lock.
            /// </summary>
            public sealed class {{Name}}Processor : PluginUIProcessor<{{Name}}Model>
            {
                /// <summary>VST3 class identifier of the processor.</summary>
                public static readonly Guid ClassId = new("{{options.ProcessorId}}");

                private double _phase, _increment, _level, _target;
                private int _note = -1;

                /// <summary>Creates the processor (32-bit float processing).</summary>
                public {{Name}}Processor() : base(AudioSampleSizeSupport.Float32)
                {
                }

                /// <inheritdoc />
                public override Guid ControllerClassId => {{Name}}Controller.ClassId;

                /// <inheritdoc />
                protected override bool Initialize(AudioHostApplication host)
                {
                    AddEventInput("MIDI In", 16);
                    AddAudioOutput("Output", SpeakerArrangement.SpeakerStereo);
                    return true;
                }

                /// <inheritdoc />
                protected override bool ProcessByPass(in AudioProcessData data) => false;

                /// <inheritdoc />
                protected override void ProcessEvent(in AudioEvent audioEvent)
                {
                    if (audioEvent.Kind == AudioEventKind.NoteOn) NoteOn(audioEvent.Value.NoteOn.Pitch, audioEvent.Value.NoteOn.Velocity);
                    else if (audioEvent.Kind == AudioEventKind.NoteOff) NoteOff(audioEvent.Value.NoteOff.Pitch);
                }

                private void NoteOn(int pitch, float velocity)
                {
                    if (velocity <= 0f) { NoteOff(pitch); return; }
                    _note = pitch;
                    _increment = 440.0 * Math.Pow(2.0, (pitch - 69) / 12.0) / Math.Max(1.0, ProcessSetupData.SampleRate);
                    _target = 0.2 * velocity;
                }

                private void NoteOff(int pitch)
                {
                    if (pitch == _note) _target = 0.0;
                }

                /// <inheritdoc />
                protected override void ProcessMain(in AudioProcessData data)
                {
                    while (TryDequeueUINote(out var note))
                    {
                        if (note.Kind == UINoteKind.NoteOn) NoteOn(note.Pitch, note.Velocity);
                        else if (note.Kind == UINoteKind.NoteOff) NoteOff(note.Pitch);
                        else _target = 0.0;
                    }
                    var output = data.Output[0];
                    if (output.ChannelCount == 0) return;
                    var left = output.GetChannelSpanAsFloat32(ProcessSetupData, data, 0);
                    for (int i = 0; i < data.SampleCount; i++)
                    {
                        _level += (_target - _level) * 0.002;
                        left[i] = (float)(Math.Sin(2.0 * Math.PI * _phase) * _level);
                        _phase += _increment;
                        if (_phase >= 1.0) _phase -= 1.0;
                    }
                    for (int c = 1; c < output.ChannelCount; c++)
                    {
                        left[..data.SampleCount].CopyTo(output.GetChannelSpanAsFloat32(ProcessSetupData, data, c));
                    }
                }
            }

            """;

        private string InstrumentConstructor => !Instrument ? string.Empty : $$"""

                /// <summary>Creates the controller.</summary>
                public {{Name}}Controller()
                {
                    // The instrument receives MIDI: offer "MIDI Learn" in the context menus of its controls.
                    IsMidiLearnEnabled = true;
                }
            """;

        public string Controller() => Header("cs") + $$"""

            using System.Runtime.Versioning;
            using PluginUI.NPlug;
            using {{EditorNamespace}};

            namespace {{Name}}.Plugin;

            /// <summary>
            /// Edit controller of {{Name}}: exposes the parameters to the host and creates the editor. The VST3 edit protocol,
            /// the note bridge, the shared state and MIDI learn come from <see cref="PluginUIController{TModel}"/>.
            /// </summary>
            public sealed class {{Name}}Controller : PluginUIController<{{Name}}Model>
            {
                /// <summary>VST3 class identifier of the controller.</summary>
                public static readonly Guid ClassId = new("{{options.ControllerId}}");
            {{InstrumentConstructor}}

                /// <inheritdoc />
                [SupportedOSPlatform("windows10.0.17763")]
                protected override PluginEditorView? CreateEditor() => new {{EditorClass}}(this);
            }

            """;

        public string Factory() => Header("cs") + $$"""

            using System.Runtime.CompilerServices;
            using NPlug;

            namespace {{Name}}.Plugin;

            /// <summary>Declares the plug-in classes to the host through the NPlug factory.</summary>
            public static class {{Name}}Plugin
            {
                /// <summary>Plug-in name displayed by the host.</summary>
                public const string Name = {{S(Name)}};

                /// <summary>Creates the factory.</summary>
                /// <returns>The factory.</returns>
                public static AudioPluginFactory GetFactory()
                {
                    var factory = new AudioPluginFactory(new AudioPluginFactoryInfo({{S(options.Vendor)}}, {{S(options.Url)}}, {{S(options.Email)}}));
                    factory.RegisterPlugin<{{Name}}Processor>(new AudioProcessorClassInfo({{Name}}Processor.ClassId, Name, AudioProcessorCategory.{{(Instrument ? "InstrumentSynthesizer" : "Effect")}})
                    {
                        Version = new Version({{Version3}}),
                    });
                    factory.RegisterPlugin<{{Name}}Controller>(new AudioControllerClassInfo({{Name}}Controller.ClassId, Name + " Controller")
                    {
                        Version = new Version({{Version3}}),
                    });
                    return factory;
                }

                /// <summary>Publishes the factory when the module loads (debug proxy or NativeAOT binary).</summary>
                [ModuleInitializer]
                internal static void ExportThisPlugin() => AudioPluginFactoryExporter.Instance = GetFactory();
            }

            """;

        public string PluginEditor()
        {
            bool images = design.AllNodes().Any(n => n.Properties.Values.Any(v => DesignValues.UsesAssetLoader(v, DesignAssetKind.Image)));
            bool svgs = design.AllNodes().Any(n => n.Properties.Values.Any(v => DesignValues.UsesAssetLoader(v, DesignAssetKind.Svg)));
            bool bindings = design.AllNodes().Any(n => n.Bindings.Count > 0);
            var sb = new StringBuilder();
            sb.Append(Header("cs"));
            sb.Append(CultureInfo.InvariantCulture, $$"""

                using System.Runtime.Versioning;
                using PluginUI;
                using PluginUI.NPlug;
                using {{Name}}.Plugin;
                {{(options.IncludeLiveBridge ? "#if LIVE_DESIGN\nusing PluginUI.Design;\n#endif\n" : string.Empty)}}
                namespace {{EditorNamespace}};

                /// <summary>
                /// Editor of {{Name}}. The layout lives in {{EditorClass}}.pluginui (open it with PluginUIEditor), and
                /// {{EditorClass}}.Designer.cs is generated from it: put your code here (events in <see cref="OnBuilt"/>).
                /// </summary>
                [SupportedOSPlatform("windows10.0.17763")]
                public sealed partial class {{EditorClass}} : PluginEditorView
                {
                    private readonly {{Name}}Controller _controller;
                    private readonly Dictionary<string, IParameter> _parameters = new(StringComparer.Ordinal);

                    /// <summary>Creates the editor for a controller.</summary>
                    /// <param name="controller">The controller.</param>
                    public {{EditorClass}}({{Name}}Controller controller)
                        : base(controller, DesignSize, DesignSizing)
                    {
                        _controller = controller;
                        // Controls bind by parameter title (see BindParameter).
                        foreach (var p in Parameters.GetSnapshotParameters(includeBypass: true)) _parameters.TryAdd(p.Name, p);
                    }

                    /// <inheritdoc />
                    protected override void ConfigureHost(UIHost host)
                    {
                        host.Theme = CreateDesignTheme();
                        host.MinSize = new Size(DesignSize.Width * 0.75f, DesignSize.Height * 0.75f);
                        host.MaxSize = new Size(DesignSize.Width * 3f, DesignSize.Height * 3f);
                #if !DEBUG
                        host.LiveEditShortcutEnabled = false;
                #endif
                    }

                    /// <summary>Called once the design is built: wire events, set what the design cannot describe.</summary>
                    partial void OnBuilt(UIElement root)
                    {
                {{(Instrument ? "        // Notes played on an on-screen keyboard go to the processor.\n        foreach (var keyboard in root.DescendantsAndSelf().OfType<PianoKeyboard>()) ConnectKeyboard(keyboard);\n" : "        // Example: ((Button)root.FindByName(\"reset\")!).Clicked += () => { };\n")}}    }

                """);
            if (bindings)
            {
                sb.Append("""

                        /// <summary>Parameter of a design name: the NPlug parameter with the same title.</summary>
                        private partial IParameter? BindParameter(string name) => _parameters.GetValueOrDefault(name);

                    """);
            }
            if (images)
            {
                sb.Append(CultureInfo.InvariantCulture, $$"""

                        /// <summary>Design images are embedded resources named by their path ("assets/knob.png").</summary>
                        private partial ImageSource LoadImage(string path) => ImageSource.FromResource(typeof({{EditorClass}}).Assembly, path.Replace('\\', '/'));

                    """);
            }
            if (svgs)
            {
                sb.Append(CultureInfo.InvariantCulture, $$"""

                        /// <summary>Design SVG files are embedded resources named by their path ("assets/icon.svg").</summary>
                        private partial SvgImage LoadSvg(string path) => SvgImage.FromResource(typeof({{EditorClass}}).Assembly, path.Replace('\\', '/'));

                    """);
            }
            if (options.IncludeLiveBridge)
            {
                sb.Append(CultureInfo.InvariantCulture, $$"""

                    #if LIVE_DESIGN
                        // Debug builds: Ctrl+Shift+E (live edit) then Ctrl+S writes the layout to the design file, and saving
                        // the design in PluginUIEditor rebuilds this editor in the DAW.
                        private LiveDesignBridge? _bridge;

                        private static string DesignFile([System.Runtime.CompilerServices.CallerFilePath] string source = "")
                            => Path.Combine(Path.GetDirectoryName(source)!, "{{EditorClass}}.pluginui");

                        /// <inheritdoc />
                        protected override void OnAttached()
                        {
                            if (!File.Exists(DesignFile())) return; // not on the development machine
                            _bridge = new LiveDesignBridge(Host, DesignFile(), design => DesignLoader.Build(design, options: new DesignLoadOptions
                            {
                                ResolveParameter = name => _parameters.GetValueOrDefault(name),
                                Assets = new FileAssetResolver(Path.GetDirectoryName(DesignFile())!),
                            }));
                            // The rebuilt tree is wired like the generated one: fields first, then OnBuilt.
                            _bridge.Reloaded += (_, result) =>
                            {
                                BindDesignFields(result.Root);
                                OnBuilt(result.Root);
                            };
                            // The design was changed after the code was last generated: show the design.
                            if (File.GetLastWriteTimeUtc(DesignFile()) > File.GetLastWriteTimeUtc(Path.ChangeExtension(DesignFile(), ".Designer.cs"))) _bridge.Reload();
                        }

                        /// <inheritdoc />
                        protected override void OnRemoved()
                        {
                            _bridge?.Dispose();
                            _bridge = null;
                        }
                    #endif

                    """);
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        public string LogRangeParameter() => Header("cs") + $$"""

            using System.Globalization;
            using NPlug;

            namespace {{Name}}.Plugin;

            /// <summary>Continuous parameter with a logarithmic mapping (frequencies, times): equal travel, equal ratios.</summary>
            public sealed class LogRangeParameter : AudioParameter
            {
                private readonly double _logMin;
                private readonly double _logRatio;

                /// <summary>Creates the parameter.</summary>
                public LogRangeParameter(string title, string units, double minValue, double maxValue, double defaultPlainValue, int decimals = 2)
                    : base(title, units, defaultNormalizedValue: Normalize(minValue, maxValue, defaultPlainValue))
                {
                    if (minValue <= 0.0 || maxValue <= minValue) throw new ArgumentException($"Invalid range [{minValue}, {maxValue}] for a logarithmic parameter.");
                    MinValue = minValue;
                    MaxValue = maxValue;
                    Decimals = decimals;
                    _logMin = Math.Log(minValue);
                    _logRatio = Math.Log(maxValue / minValue);
                    NormalizedValue = DefaultNormalizedValue;
                }

                /// <summary>Minimum plain value.</summary>
                public double MinValue { get; }

                /// <summary>Maximum plain value.</summary>
                public double MaxValue { get; }

                /// <summary>Decimals of the text value.</summary>
                public int Decimals { get; }

                /// <summary>Current plain value (allocation-free, audio thread safe).</summary>
                public double Value => ToPlain(NormalizedValue);

                /// <inheritdoc />
                public override double ToPlain(double normalizedValue) => Math.Exp(_logMin + Math.Clamp(normalizedValue, 0.0, 1.0) * _logRatio);

                /// <inheritdoc />
                public override double ToNormalized(double plainValue) => (Math.Log(Math.Clamp(plainValue, MinValue, MaxValue)) - _logMin) / _logRatio;

                /// <inheritdoc />
                public override string ToString(double valueNormalized)
                    => ToPlain(valueNormalized).ToString("F" + Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

                /// <inheritdoc />
                public override double FromString(string plainValueAsString)
                    => double.TryParse(plainValueAsString, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? ToNormalized(value) : NormalizedValue;

                private static double Normalize(double minValue, double maxValue, double plainValue)
                {
                    if (minValue <= 0.0 || maxValue <= minValue) return 0.0;
                    return Math.Log(Math.Clamp(plainValue, minValue, maxValue) / minValue) / Math.Log(maxValue / minValue);
                }
            }

            """;

        public string StandaloneProject() => Header("xml") + $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>{{Name}}</RootNamespace>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsAotCompatible>true</IsAotCompatible>
                <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
                <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())</RuntimeIdentifier>
              </PropertyGroup>

              <PropertyGroup>
                <PluginUIRoot Condition="'$(PluginUIRoot)' == ''">{{options.PluginUIRoot}}</PluginUIRoot>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="$(PluginUIRoot)src\PluginUI\PluginUI.csproj" />
              </ItemGroup>

            {{AssetItems}}

            </Project>

            """;

        public string StandaloneProgram() => Header("cs") + $$"""

            using System.Runtime.Versioning;
            using PluginUI;
            using {{EditorNamespace}};

            [assembly: SupportedOSPlatform("windows10.0.17763")]

            StandaloneWindow.EnablePerMonitorDpiAwareness();
            var view = new {{EditorClass}}();
            var host = new UIHost(view.BuildDesign(), {{EditorClass}}.DesignSize, {{EditorClass}}.DesignSizing) { Theme = {{EditorClass}}.CreateDesignTheme() };
            return new StandaloneWindow(host, {{S(Name)}}).Run();

            """;

        public string StandaloneEditor()
        {
            bool images = design.AllNodes().Any(n => n.Properties.Values.Any(v => DesignValues.UsesAssetLoader(v, DesignAssetKind.Image)));
            bool svgs = design.AllNodes().Any(n => n.Properties.Values.Any(v => DesignValues.UsesAssetLoader(v, DesignAssetKind.Svg)));
            bool bindings = design.AllNodes().Any(n => n.Bindings.Count > 0);
            var sb = new StringBuilder();
            sb.Append(Header("cs"));
            sb.Append(CultureInfo.InvariantCulture, $$"""

                using System.Runtime.Versioning;
                using PluginUI;

                namespace {{EditorNamespace}};

                /// <summary>
                /// Main view of {{Name}}. The layout lives in {{EditorClass}}.pluginui (open it with PluginUIEditor), and
                /// {{EditorClass}}.Designer.cs is generated from it: put your code here.
                /// </summary>
                [SupportedOSPlatform("windows10.0.17763")]
                public sealed partial class {{EditorClass}}
                {
                    /// <summary>Application state the controls bind to, by design parameter name.</summary>
                    public Dictionary<string, IParameter> Parameters { get; } = new(StringComparer.Ordinal)
                    {

                """);
            foreach (var p in design.Parameters) sb.Append(CultureInfo.InvariantCulture, $"        [{S(p.Name)}] = {StandaloneParameter(p)},\n");
            sb.Append("""
                    };

                    /// <summary>Called once the design is built: wire events, set what the design cannot describe.</summary>
                    partial void OnBuilt(UIElement root)
                    {
                    }

                """);
            if (bindings) sb.Append("\n    private partial IParameter? BindParameter(string name) => Parameters.GetValueOrDefault(name);\n");
            if (images) sb.Append(CultureInfo.InvariantCulture, $"\n    private partial ImageSource LoadImage(string path) => ImageSource.FromResource(typeof({EditorClass}).Assembly, path.Replace('\\\\', '/'));\n");
            if (svgs) sb.Append(CultureInfo.InvariantCulture, $"\n    private partial SvgImage LoadSvg(string path) => SvgImage.FromResource(typeof({EditorClass}).Assembly, path.Replace('\\\\', '/'));\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string StandaloneParameter(DesignParameter p) => p.Kind switch
        {
            DesignParameterKind.Toggle => $"LocalParameter.Toggle({S(p.Name)}, {(p.Default >= 0.5 ? "true" : "false")})",
            DesignParameterKind.Choice => $"LocalParameter.FromList({S(p.Name)}, [{string.Join(", ", p.Choices.Select(S))}], {((int)Math.Round(p.Default)).ToString(CultureInfo.InvariantCulture)})",
            DesignParameterKind.Logarithmic => $"new LocalParameter({S(p.Name)}, {D(p.Min)}, {D(p.Max)}, {D(p.Default)}, {S(p.Unit)}, ParameterScale.Logarithmic({D(p.Min)}, {D(p.Max)}), 0, {p.Decimals.ToString(CultureInfo.InvariantCulture)})",
            DesignParameterKind.Integer => $"new LocalParameter({S(p.Name)}, {D(p.Min)}, {D(p.Max)}, {D(p.Default)}, {S(p.Unit)}, null, {((int)Math.Round(Math.Max(1, p.Max - p.Min))).ToString(CultureInfo.InvariantCulture)}, 0)",
            _ => $"new LocalParameter({S(p.Name)}, {D(p.Min)}, {D(p.Max)}, {D(p.Default)}, {S(p.Unit)}, null, 0, {p.Decimals.ToString(CultureInfo.InvariantCulture)})",
        };

        public string Readme()
        {
            string kind = options.Kind switch
            {
                ScaffoldKind.Effect => "VST3 audio effect",
                ScaffoldKind.Instrument => "VST3 instrument",
                _ => "Windows application",
            };
            string build = options.Kind == ScaffoldKind.Standalone
                ? "```\ndotnet run\ndotnet publish -c Release -p:PublishAot=true   # single native executable\n```\n"
                : $"```\ndotnet build -c Debug                   # bin\\Debug\\net10.0\\win-x64\\{Name}.vst3 (win-arm64 on ARM64; debuggable through the NPlug proxy)\ndotnet publish -c Release -r win-x64    # NativeAOT {Name}.vst3 (AVX2); add -p:IlcInstructionSet=x86-64-v2 for older CPUs\ndotnet publish -c Release -r win-arm64  # NativeAOT {Name}.vst3 for Windows on ARM\n```\n\nCopy the `.vst3` to `C:\\Program Files\\Common Files\\VST3` and rescan the plug-ins in the DAW.\n";
            string live = options.IncludeLiveBridge && options.Kind != ScaffoldKind.Standalone
                ? "\n## Live design (Debug builds)\n\n- In the DAW, Ctrl+Shift+E turns on live edit in the editor: move and resize, then **Ctrl+S** writes the layout to the design file.\n- Saving the design in PluginUIEditor rebuilds the editor in the DAW at once.\n- Regenerate the `.Designer.cs` file (PluginUIEditor: *Generate code*) before a release build.\n"
                : string.Empty;
            return Header("md") + $$"""

                # {{Name}}

                {{kind}} written in C# with {{(options.Kind == ScaffoldKind.Standalone ? "" : "[NPlug](https://github.com/xoofx/NPlug) and ")}}PluginUI, created from a PluginUIEditor design.

                ## Files

                | Path | Content |
                |---|---|
                | `Editor/{{EditorClass}}.pluginui` | The design: open it with PluginUIEditor |
                | `Editor/{{EditorClass}}.Designer.cs` | Generated from the design (do not edit) |
                | `Editor/{{EditorClass}}.cs` | Your editor code: events in `OnBuilt` |
                | `Editor/assets/` | Images and SVG of the design (embedded resources) |
                {{(options.Kind == ScaffoldKind.Standalone ? "| `Program.cs` | Window and host |" : $"| `Plugin/{Name}Model.cs` | Parameters (titles = design parameter names) |\n| `Plugin/{Name}Processor.cs` | Audio processing (audio thread) |\n| `Plugin/{Name}Controller.cs` | Edit controller, creates the editor |\n| `Plugin/{Name}Plugin.cs` | Factory: name, vendor, class ids |")}}

                ## Build

                {{build}}
                It expects the PluginUI sources at the path set in the project file (`PluginUIRoot`){{(options.Kind == ScaffoldKind.Standalone ? "" : "; NPlug comes from its NuGet package")}}.
                {{live}}
                """;
        }
    }
}
