// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Design;

/// <summary>
/// A PluginUI design (<c>.pluginui</c> file): the element tree with its property values and parameter bindings, the
/// design size and theme, the parameters used for preview and scaffolding, and the code generation settings.
/// </summary>
/// <remarks>
/// Read and write it with <see cref="DesignJson"/>, build live elements from it with <see cref="DesignLoader"/>,
/// generate C# with <see cref="CSharpDesignGenerator"/>. Property values are kept as typed design values (see
/// <see cref="DesignValues"/>): numbers, enums, colors, geometry, text styles, paints, and asset references
/// (<see cref="DesignAsset"/>, <see cref="SkinDescriptor"/>) that are resolved when the design is built.
/// </remarks>
public sealed class DesignDocument
{
    /// <summary>Current version of the file format.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>File extension, with the dot.</summary>
    public const string FileExtension = ".pluginui";

    /// <summary>
    /// Free comment written first in the file (JSON has no comments), for example a header with a date and a project
    /// name. Kept as is by the editor.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>Format version read from the file (written as <see cref="CurrentFormatVersion"/>).</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Name of the design (window title, default class name).</summary>
    public string Name { get; set; } = "Editor";

    /// <summary>Logical size of the editor at 100 % (DIPs).</summary>
    public Size DesignSize { get; set; } = new(640f, 400f);

    /// <summary>Resizing behavior offered to the host.</summary>
    public SizingMode Sizing { get; set; } = SizingMode.ScaleToFit;

    /// <summary>Theme of the editor.</summary>
    public DesignTheme Theme { get; set; } = new();

    /// <summary>Parameters the controls bind to (preview values, generated model).</summary>
    public List<DesignParameter> Parameters { get; } = new();

    /// <summary>Code generation settings.</summary>
    public CodegenSettings Codegen { get; set; } = new();

    /// <summary>Root element (usually a <see cref="Canvas"/>).</summary>
    public DesignNode Root { get; set; } = new("Canvas");

    /// <summary>Members of the file this version does not know (raw JSON), written back when saving.</summary>
    public OrderedDictionary<string, string> UnknownMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>Finds a parameter by name (ordinal comparison).</summary>
    /// <param name="name">The name.</param>
    /// <returns>The parameter, or <c>null</c>.</returns>
    public DesignParameter? FindParameter(string name) => Parameters.Find(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>Every node of the tree, depth first, root included.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<DesignNode> AllNodes() => Root.DescendantsAndSelf();

    /// <summary>Finds a node by element name.</summary>
    /// <param name="name">The element name.</param>
    /// <returns>The node, or <c>null</c>.</returns>
    public DesignNode? FindNode(string name) => AllNodes().FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Returns a name based on <paramref name="baseName"/> that no node uses yet ("knob", "knob2", "knob3"…).
    /// </summary>
    /// <param name="baseName">Wanted name (a valid C# identifier).</param>
    /// <returns>The unique name.</returns>
    public string UniqueName(string baseName)
    {
        var used = new HashSet<string>(AllNodes().Select(n => n.Name).OfType<string>(), StringComparer.Ordinal);
        if (!used.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            string candidate = baseName + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Asset paths referenced by the design (images, SVG), relative to the design file.</summary>
    /// <returns>The distinct paths, in order of first use.</returns>
    public IReadOnlyList<string> AssetPaths()
    {
        var paths = new List<string>();
        foreach (var node in AllNodes())
        {
            foreach (var value in node.Properties.Values) DesignValues.CollectAssets(value, paths);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>One element of a design.</summary>
public sealed class DesignNode
{
    /// <summary>Creates a node.</summary>
    /// <param name="type">Registered element type name (<see cref="DesignTypeRegistry"/>), e.g. "Knob".</param>
    public DesignNode(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type;
    }

    /// <summary>Element type name.</summary>
    public string Type { get; set; }

    /// <summary>Element name (<see cref="UIElement.Name"/>): the field name in generated code. <c>null</c> for anonymous elements.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Explicitly set property values, by property name, in insertion order. Values are design values
    /// (see <see cref="DesignValues"/>); properties left out keep the element's default.
    /// </summary>
    public OrderedDictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Parameter bindings: property name (e.g. "Parameter", "XParameter") → design parameter name.</summary>
    public OrderedDictionary<string, string> Bindings { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Properties this version could not interpret (unknown property or type), kept as raw JSON so that saving the
    /// document does not lose them.
    /// </summary>
    public OrderedDictionary<string, string> UnknownProperties { get; } = new(StringComparer.Ordinal);

    /// <summary>Members of the file this version does not know (raw JSON), written back when saving.</summary>
    public OrderedDictionary<string, string> UnknownMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Position of this node in its parent when the parent needs one: the tab header in a <see cref="TabView"/>,
    /// "First" or "Second" in a <see cref="SplitView"/>. <c>null</c> otherwise.
    /// </summary>
    public string? Slot { get; set; }

    /// <summary>Child elements.</summary>
    public List<DesignNode> Children { get; } = new();

    /// <summary>Identifier used by tools during a session (not saved).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Value of a property, or <paramref name="fallback"/> when not set.</summary>
    /// <param name="name">Property name.</param>
    /// <param name="fallback">Value returned when the property is not set.</param>
    /// <returns>The value.</returns>
    public object? Get(string name, object? fallback = null) => Properties.TryGetValue(name, out var value) ? value : fallback;

    /// <summary>Sets a property value (design value).</summary>
    /// <param name="name">Property name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This node.</returns>
    public DesignNode Set(string name, object? value)
    {
        Properties[name] = value;
        UnknownProperties.Remove(name);
        return this;
    }

    /// <summary>Binds a parameter property to a design parameter.</summary>
    /// <param name="property">Property of type <see cref="IParameter"/>, e.g. "Parameter".</param>
    /// <param name="parameter">Design parameter name.</param>
    /// <returns>This node.</returns>
    public DesignNode Bind(string property, string parameter)
    {
        Bindings[property] = parameter;
        return this;
    }

    /// <summary>Adds a child.</summary>
    /// <param name="child">The child.</param>
    /// <returns>This node.</returns>
    public DesignNode Add(DesignNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        Children.Add(child);
        return this;
    }

    /// <summary>This node and its descendants, depth first.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<DesignNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var d in child.DescendantsAndSelf()) yield return d;
        }
    }

    /// <summary>Parent of <paramref name="node"/> within this subtree, or <c>null</c>.</summary>
    /// <param name="node">A descendant.</param>
    /// <returns>The parent.</returns>
    public DesignNode? FindParent(DesignNode node)
    {
        foreach (var candidate in DescendantsAndSelf())
        {
            if (candidate.Children.Contains(node)) return candidate;
        }
        return null;
    }

    /// <inheritdoc />
    public override string ToString() => Name is null ? Type : $"{Name} ({Type})";
}

/// <summary>How a design parameter maps its plain value.</summary>
public enum DesignParameterKind
{
    /// <summary>Continuous, linear between <see cref="DesignParameter.Min"/> and <see cref="DesignParameter.Max"/>.</summary>
    Linear,

    /// <summary>Continuous, logarithmic (frequencies, times): min must be positive.</summary>
    Logarithmic,

    /// <summary>Whole numbers between min and max.</summary>
    Integer,

    /// <summary>On / off.</summary>
    Toggle,

    /// <summary>One of <see cref="DesignParameter.Choices"/>.</summary>
    Choice,
}

/// <summary>A parameter of the design: controls bind to it by name.</summary>
/// <remarks>
/// In the editor preview it becomes a <see cref="LocalParameter"/>; at run time bindings resolve to the plug-in's
/// parameters by name (<see cref="DesignLoadOptions.ResolveParameter"/>); scaffolding turns it into an NPlug parameter.
/// </remarks>
public sealed class DesignParameter
{
    /// <summary>Members of the file this version does not know (raw JSON), written back when saving.</summary>
    public OrderedDictionary<string, string> UnknownMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>Name (also the NPlug parameter title in scaffolded projects).</summary>
    public string Name { get; set; } = "Parameter";

    /// <summary>Value mapping.</summary>
    public DesignParameterKind Kind { get; set; } = DesignParameterKind.Linear;

    /// <summary>Minimum plain value.</summary>
    public double Min { get; set; }

    /// <summary>Maximum plain value.</summary>
    public double Max { get; set; } = 1.0;

    /// <summary>Default plain value (for <see cref="DesignParameterKind.Choice"/>: the default index; toggle: 0 or 1).</summary>
    public double Default { get; set; }

    /// <summary>Unit shown after the value ("dB", "Hz", "%").</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Decimals of the displayed value.</summary>
    public int Decimals { get; set; } = 2;

    /// <summary>Labels of a <see cref="DesignParameterKind.Choice"/> parameter.</summary>
    public List<string> Choices { get; } = new();

    /// <summary>Creates the preview parameter.</summary>
    /// <returns>A local parameter with the same mapping.</returns>
    public LocalParameter CreatePreview() => Kind switch
    {
        DesignParameterKind.Toggle => LocalParameter.Toggle(Name, Default >= 0.5),
        DesignParameterKind.Choice => LocalParameter.FromList(Name, Choices.Count > 0 ? Choices : ["—"], (int)Math.Round(Default)),
        DesignParameterKind.Logarithmic when Min > 0.0 && Max > Min
            => new LocalParameter(Name, Min, Max, Math.Clamp(Default, Min, Max), Unit, ParameterScale.Logarithmic(Min, Max), 0, Decimals),
        DesignParameterKind.Integer when Max > Min
            => new LocalParameter(Name, Min, Max, Math.Clamp(Default, Min, Max), Unit, ParameterScale.Linear(Min, Max), (int)Math.Round(Max - Min), 0),
        _ => new LocalParameter(Name, Min, Math.Max(Max, Min + 1e-9), Math.Clamp(Default, Min, Math.Max(Max, Min)), Unit, ParameterScale.Linear(Min, Math.Max(Max, Min + 1e-9)), 0, Decimals),
    };

    /// <summary>Copy of this parameter.</summary>
    /// <returns>The copy.</returns>
    public DesignParameter Clone()
    {
        var copy = new DesignParameter { Name = Name, Kind = Kind, Min = Min, Max = Max, Default = Default, Unit = Unit, Decimals = Decimals };
        copy.Choices.AddRange(Choices);
        foreach (var (key, raw) in UnknownMembers) copy.UnknownMembers[key] = raw;
        return copy;
    }
}

/// <summary>Theme of a design: a base theme, an accent color and individual overrides.</summary>
public sealed class DesignTheme
{
    /// <summary>"Dark" or "Light".</summary>
    public string Base { get; set; } = "Dark";

    /// <summary>Accent color (null = the base theme's).</summary>
    public Color? Accent { get; set; }

    /// <summary>Palette colors overridden by name ("Background", "Surface"…).</summary>
    public OrderedDictionary<string, Color> Palette { get; } = new(StringComparer.Ordinal);

    /// <summary>Typography overridden by name ("Family", "NormalSize"…): string or number.</summary>
    public OrderedDictionary<string, object> Typography { get; } = new(StringComparer.Ordinal);

    /// <summary>Metrics overridden by name ("CornerRadius", "Spacing"…).</summary>
    public OrderedDictionary<string, float> Metrics { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Theme content this version cannot apply, kept as raw JSON and written back: unknown sections (key: section
    /// name) and unknown or invalid overrides (key: "palette.Name", "typography.Name", "metrics.Name").
    /// </summary>
    public OrderedDictionary<string, string> UnknownMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>Builds the theme.</summary>
    /// <returns>The theme.</returns>
    public Theme Create() => ThemeJson.Apply(this);
}

/// <summary>What the generated partial class derives from.</summary>
public enum CodegenTarget
{
    /// <summary>A <c>PluginUI.NPlug.PluginEditorView</c>: generates <c>protected override UIElement Build()</c>.</summary>
    PluginEditorView,

    /// <summary>Any class (standalone application, custom host): generates <c>public UIElement BuildDesign()</c> only.</summary>
    Standalone,
}

/// <summary>Settings of the generated <c>.Designer.cs</c> file.</summary>
public sealed class CodegenSettings
{
    /// <summary>Members of the file this version does not know (raw JSON), written back when saving.</summary>
    public OrderedDictionary<string, string> UnknownMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>Namespace of the generated class.</summary>
    public string Namespace { get; set; } = "MyPlugin.Editor";

    /// <summary>Name of the partial class.</summary>
    public string ClassName { get; set; } = "MyEditor";

    /// <summary>Kind of class.</summary>
    public CodegenTarget Target { get; set; } = CodegenTarget.PluginEditorView;

    /// <summary>Generates the <c>Build()</c> override (plug-in editors); turn off to call <c>BuildDesign()</c> yourself.</summary>
    public bool GenerateBuildOverride { get; set; } = true;

    /// <summary>Accessibility of the fields of named elements: "private", "internal", "protected" or "public".</summary>
    public string FieldAccess { get; set; } = "private";
}
