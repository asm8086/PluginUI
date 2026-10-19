// 2026/10/19 20:27:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace PluginUI.Design;

/// <summary>How an element holds child elements in a design.</summary>
public enum DesignContainerKind
{
    /// <summary>No children.</summary>
    None,

    /// <summary>Any number of children (<see cref="Panel.Children"/>).</summary>
    Children,

    /// <summary>A single child (<see cref="ContentPanel.Content"/>, <see cref="ScrollViewer.Content"/>).</summary>
    Content,

    /// <summary>Two children: <see cref="SplitView.First"/> and <see cref="SplitView.Second"/> (node slot "First" / "Second").</summary>
    Split,

    /// <summary>Tab pages of a <see cref="TabView"/> (node slot: the tab header).</summary>
    Tabs,
}

/// <summary>A designable property of an element type.</summary>
public sealed class DesignPropertyInfo
{
    private readonly Func<UIElement, object?> _get;
    private readonly Action<UIElement, object?> _set;

    internal DesignPropertyInfo(string name, Type type, string category, Func<UIElement, object?> get, Action<UIElement, object?> set, PropertyInfo? property)
    {
        Name = name;
        PropertyType = type;
        Category = category;
        _get = get;
        _set = set;
        Property = property;
    }

    /// <summary>Property name (C# name and JSON key).</summary>
    public string Name { get; }

    /// <summary>Property type.</summary>
    public Type PropertyType { get; }

    /// <summary>Category: Design, Layout, Appearance, Text, Value, Data, Behavior (or Binding for parameters).</summary>
    public string Category { get; }

    /// <summary>The reflected property (<c>null</c> for virtual properties such as the rows of a <see cref="Grid"/>).</summary>
    public PropertyInfo? Property { get; }

    /// <summary><c>true</c> for properties of type <see cref="IParameter"/>, set through bindings.</summary>
    public bool IsBinding => PropertyType == typeof(IParameter);

    /// <summary>Reads the live value.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The value.</returns>
    public object? GetValue(UIElement element) => _get(element);

    /// <summary>Assigns a live value.</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value.</param>
    public void SetValue(UIElement element, object? value) => _set(element, value);

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {PropertyType.Name}";
}

/// <summary>A designable element type: factory, properties, container kind, toolbox category.</summary>
public sealed class DesignTypeInfo
{
    private readonly Func<UIElement> _factory;
    private readonly Dictionary<string, DesignPropertyInfo> _byName;
    private Dictionary<string, object?>? _defaults;

    internal DesignTypeInfo(Type type, Func<UIElement> factory, string? toolboxCategory, DesignContainerKind container,
        IReadOnlyList<DesignPropertyInfo> properties, IReadOnlyList<DesignPropertyInfo> bindings, IReadOnlyList<string> unsupported)
    {
        Type = type;
        _factory = factory;
        ToolboxCategory = toolboxCategory;
        Container = container;
        Properties = properties;
        Bindings = bindings;
        UnsupportedProperties = unsupported;
        _byName = properties.Concat(bindings).ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    /// <summary>Type name used in designs ("Knob").</summary>
    public string Name => Type.Name;

    /// <summary>The element type.</summary>
    public Type Type { get; }

    /// <summary>Toolbox category ("Rotary", "Meters"…); <c>null</c> for types the toolbox does not offer.</summary>
    public string? ToolboxCategory { get; }

    /// <summary>How the element holds children.</summary>
    public DesignContainerKind Container { get; }

    /// <summary>Designable properties (value properties), in declaration order (base classes first).</summary>
    public IReadOnlyList<DesignPropertyInfo> Properties { get; }

    /// <summary>Parameter properties (<see cref="IParameter"/>), set through <see cref="DesignNode.Bindings"/>.</summary>
    public IReadOnlyList<DesignPropertyInfo> Bindings { get; }

    /// <summary>Public settable properties whose type a design cannot describe (set them in code, e.g. in <c>OnBuilt</c>).</summary>
    public IReadOnlyList<string> UnsupportedProperties { get; }

    /// <summary>Creates an element.</summary>
    /// <returns>The new element.</returns>
    public UIElement Create() => _factory();

    /// <summary>Finds a property or binding by name.</summary>
    /// <param name="name">Property name.</param>
    /// <returns>The property, or <c>null</c>.</returns>
    public DesignPropertyInfo? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// Default design value of a property (read from a freshly created element; asset properties default to
    /// <c>null</c>).
    /// </summary>
    /// <param name="name">Property name.</param>
    /// <returns>The default value.</returns>
    public object? DefaultValue(string name)
    {
        if (_defaults is null)
        {
            var element = Create();
            var defaults = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var p in Properties)
            {
                defaults[p.Name] = DesignValues.TryCapture(p.GetValue(element), p.PropertyType, out var v) ? v : null;
            }
            _defaults = defaults;
        }
        return _defaults.GetValueOrDefault(name);
    }

    /// <summary>Natural size of a new element (its measured default size), used when the designer drops it on a canvas.</summary>
    /// <returns>The size in DIPs.</returns>
    public Size DefaultElementSize()
    {
        var element = Create();
        element.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        var size = element.DesiredSize;
        return new Size(size.Width > 0f && float.IsFinite(size.Width) ? size.Width : 80f, size.Height > 0f && float.IsFinite(size.Height) ? size.Height : 40f);
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The element types a design may use. Types are registered explicitly with their static type, which keeps the
/// reflection used to discover properties compatible with trimming and NativeAOT: no type is ever looked up by
/// name in an assembly.
/// </summary>
/// <example>
/// <code>
/// var registry = DesignTypeRegistry.CreateDefault();   // every PluginUI element
/// registry.Register&lt;MyMeter&gt;("My controls");        // a custom control
/// </code>
/// </example>
public sealed class DesignTypeRegistry
{
    private const DynamicallyAccessedMemberTypes ElementMembers = DynamicallyAccessedMemberTypes.PublicProperties;

    private static readonly string[] AllowedCategories = ["Design", "Layout", "Appearance", "Text", "Value", "Data", "Behavior"];

    private readonly Dictionary<string, DesignTypeInfo> _types = new(StringComparer.Ordinal);

    /// <summary>Registered types, in registration order.</summary>
    public IReadOnlyCollection<DesignTypeInfo> Types => _types.Values;

    private static readonly Lazy<DesignTypeRegistry> SharedDefault = new(CreateDefault);

    /// <summary>A shared registry with every PluginUI element (do not register custom types in it: use <see cref="CreateDefault"/>).</summary>
    public static DesignTypeRegistry Default => SharedDefault.Value;

    /// <summary>Creates a registry with every PluginUI element.</summary>
    /// <returns>The registry.</returns>
    public static DesignTypeRegistry CreateDefault()
    {
        var registry = new DesignTypeRegistry();
        BuiltInDesignTypes.RegisterAll(registry);
        return registry;
    }

    /// <summary>Registers an element type with a parameterless constructor.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="toolboxCategory">Toolbox category (<c>null</c>: loadable but not offered by the toolbox).</param>
    /// <returns>The type information.</returns>
    public DesignTypeInfo Register<[DynamicallyAccessedMembers(ElementMembers)] T>(string? toolboxCategory = null) where T : UIElement, new()
        => Register(() => new T(), toolboxCategory);

    /// <summary>Registers an element type created by a factory.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="factory">Creates an element with default settings.</param>
    /// <param name="toolboxCategory">Toolbox category (<c>null</c>: loadable but not offered by the toolbox).</param>
    /// <returns>The type information.</returns>
    /// <remarks>Registering a type name twice replaces the first registration.</remarks>
    public DesignTypeInfo Register<[DynamicallyAccessedMembers(ElementMembers)] T>(Func<T> factory, string? toolboxCategory = null) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(factory);
        var properties = new List<DesignPropertyInfo>();
        var bindings = new List<DesignPropertyInfo>();
        var unsupported = new List<string>();
        var container = ContainerKindOf(typeof(T));

        // Base class properties first (GetProperties returns the most derived first).
        var reflected = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true } && p.SetMethod is { IsPublic: true })
            .OrderBy(p => Depth(p.DeclaringType!))
            .ThenBy(p => p.MetadataToken)
            .ToList();
        foreach (var p in reflected)
        {
            if (p.Name == nameof(UIElement.Name)) continue; // DesignNode.Name
            if (typeof(UIElement).IsAssignableFrom(p.PropertyType)) continue; // structure (Content, First, Second)
            if (p.PropertyType == typeof(IParameter))
            {
                bindings.Add(new DesignPropertyInfo(p.Name, p.PropertyType, "Binding", e => p.GetValue(e), (e, v) => p.SetValue(e, v), p));
                continue;
            }
            if (p.GetCustomAttribute<BrowsableAttribute>(inherit: true) is { Browsable: false }) continue;
            string category = p.GetCustomAttribute<CategoryAttribute>(inherit: true)?.Category ?? "Behavior";
            if (!AllowedCategories.Contains(category)) category = "Behavior";
            if (!DesignValues.IsSupported(p.PropertyType))
            {
                unsupported.Add(p.Name);
                continue;
            }
            properties.Add(new DesignPropertyInfo(p.Name, p.PropertyType, category, e => p.GetValue(e), (e, v) => p.SetValue(e, v), p));
        }

        if (typeof(Grid).IsAssignableFrom(typeof(T)))
        {
            properties.Add(new DesignPropertyInfo("Rows", typeof(string), "Layout", e => FormatLengths(((Grid)e).RowDefinitions), (e, v) => ParseLengths(((Grid)e).RowDefinitions, v as string), null));
            properties.Add(new DesignPropertyInfo("Columns", typeof(string), "Layout", e => FormatLengths(((Grid)e).ColumnDefinitions), (e, v) => ParseLengths(((Grid)e).ColumnDefinitions, v as string), null));
        }

        var info = new DesignTypeInfo(typeof(T), () => factory(), toolboxCategory, container, properties, bindings, unsupported);
        _types[info.Name] = info;
        return info;
    }

    private static int Depth(Type type)
    {
        int depth = 0;
        for (var t = type.BaseType; t is not null; t = t.BaseType) depth++;
        return depth;
    }

    private static DesignContainerKind ContainerKindOf(Type type)
    {
        if (typeof(SplitView).IsAssignableFrom(type)) return DesignContainerKind.Split;
        if (typeof(TabView).IsAssignableFrom(type)) return DesignContainerKind.Tabs;
        if (typeof(ScrollViewer).IsAssignableFrom(type) || typeof(ContentPanel).IsAssignableFrom(type)) return DesignContainerKind.Content;
        if (typeof(Panel).IsAssignableFrom(type)) return DesignContainerKind.Children;
        return DesignContainerKind.None;
    }

    internal static string FormatLengths(List<GridLength> lengths) => string.Join(",", lengths.Select(l => l.Unit switch
    {
        GridUnit.Auto => "Auto",
        GridUnit.Star => l.Value == 1f ? "*" : l.Value.ToString("R", CultureInfo.InvariantCulture) + "*",
        _ => l.Value.ToString("R", CultureInfo.InvariantCulture),
    }));

    internal static void ParseLengths(List<GridLength> lengths, string? text)
    {
        lengths.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;
        var invalid = new List<string>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Invalid entries are skipped (as in generated code) and reported once the valid ones are added.
            try
            {
                lengths.Add(GridLength.Parse(part));
            }
            catch (FormatException)
            {
                invalid.Add(part);
            }
        }
        if (invalid.Count > 0) throw new FormatException($"Invalid grid length(s) skipped: {string.Join(", ", invalid)}.");
    }

    /// <summary>Type information by name.</summary>
    /// <param name="name">Type name ("Knob").</param>
    /// <returns>The information, or <c>null</c> when the type is not registered.</returns>
    public DesignTypeInfo? Find(string name) => _types.GetValueOrDefault(name);

    /// <summary>Type information of an element.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The information of its exact type, or <c>null</c>.</returns>
    public DesignTypeInfo? Find(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return _types.TryGetValue(element.GetType().Name, out var info) && info.Type == element.GetType() ? info : null;
    }
}

/// <summary>Registration of every PluginUI element, with toolbox categories.</summary>
public static class BuiltInDesignTypes
{
    /// <summary>Toolbox categories, in display order.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        ["Containers", "Rotary", "Linear", "Buttons", "Text", "Meters", "Instrument", "Analysis", "Pickers", "Decoration"];

    /// <summary>Registers the PluginUI elements.</summary>
    /// <param name="registry">The registry.</param>
    public static void RegisterAll(DesignTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        const string Containers = "Containers", Rotary = "Rotary", Linear = "Linear", Buttons = "Buttons", Text = "Text",
            Meters = "Meters", Instrument = "Instrument", Analysis = "Analysis", Pickers = "Pickers", Decoration = "Decoration";

        registry.Register<Canvas>(Containers);
        registry.Register<StackPanel>(Containers);
        registry.Register<WrapPanel>(Containers);
        registry.Register<UniformGrid>(Containers);
        registry.Register<Grid>(Containers);
        registry.Register<DockPanel>(Containers);
        registry.Register<LayeredPanel>(Containers);
        registry.Register<Border>(Containers);
        registry.Register<GroupBox>(Containers);
        registry.Register<ScrollViewer>(Containers);
        registry.Register<SplitView>(Containers);
        registry.Register<TabView>(Containers);
        registry.Register<ViewSwitcher>(Containers);
        registry.Register<Panel>();
        registry.Register<ContentPanel>();

        registry.Register<Knob>(Rotary);
        registry.Register<EndlessKnob>(Rotary);

        registry.Register<Slider>(Linear);
        registry.Register<RangeSlider>(Linear);
        registry.Register(() => new MultiSlider(), Linear);
        registry.Register<XYPad>(Linear);
        registry.Register<ScrollBar>(Linear);
        registry.Register<EnvelopeEditor>(Linear);
        registry.Register<CurveEditor>(Linear);

        registry.Register<Button>(Buttons);
        registry.Register<ToggleButton>(Buttons);
        registry.Register<CheckBox>(Buttons);
        registry.Register<Switch>(Buttons);
        registry.Register<SlideSwitch>(Buttons);
        registry.Register<RockerSwitch>(Buttons);
        registry.Register<RadioGroup>(Buttons);
        registry.Register<SegmentedControl>(Buttons);
        registry.Register(() => new MultiToggle(), Buttons);
        registry.Register<DropDown>(Buttons);
        registry.Register<MenuButton>(Buttons);

        registry.Register<Label>(Text);
        registry.Register<ValueDisplay>(Text);
        registry.Register<NumberBox>(Text);
        registry.Register<TextBox>(Text);
        registry.Register<SegmentDisplay>(Text);
        registry.Register<BitmapText>(Text);
        registry.Register<Hyperlink>(Text);

        registry.Register<LevelMeter>(Meters);
        registry.Register<Led>(Meters);
        registry.Register<ProgressBar>(Meters);
        registry.Register<SpectrumAnalyzer>(Meters);
        registry.Register<Oscilloscope>(Meters);
        registry.Register<Plot>(Meters);
        registry.Register<HistoryGraph>(Meters);
        registry.Register<FpsCounter>(Meters);

        registry.Register<PianoKeyboard>(Instrument);
        registry.Register<PitchWheel>(Instrument);
        registry.Register<ModWheel>(Instrument);
        registry.Register<WheelControl>();
        registry.Register<StepSequencer>(Instrument);
        registry.Register<ModulationSourceButton>(Instrument);
        registry.Register<WaveformView>(Instrument);

        registry.Register<ParametricEqEditor>(Analysis);
        registry.Register<FrequencyResponseView>(Analysis);
        registry.Register<CompressorCurve>(Analysis);
        registry.Register<LoudnessMeter>(Analysis);
        registry.Register<Goniometer>(Analysis);
        registry.Register<CorrelationMeter>(Analysis);
        registry.Register<Spectrogram>(Analysis);

        registry.Register<ColorPicker>(Pickers);
        registry.Register<ColorSwatch>(Pickers);
        registry.Register<FilePathBox>(Pickers);
        registry.Register<PresetBar>(Pickers);
        registry.Register<ABCompareBar>(Pickers);
        registry.Register<ListBox>(Pickers);
        registry.Register<DataTable>(Pickers);

        registry.Register<ImageView>(Decoration);
        registry.Register<SvgView>(Decoration);
        registry.Register<Separator>(Decoration);
        registry.Register<Spacer>(Decoration);
        registry.Register<CornerResizer>(Decoration);

        // Loadable, never placed from the toolbox (created by other elements, or needing a native window).
        registry.Register<Popup>();
        registry.Register<Dialog>();
        registry.Register(() => new SplashScreen());
        registry.Register<NativeWindowHost>();
    }
}
