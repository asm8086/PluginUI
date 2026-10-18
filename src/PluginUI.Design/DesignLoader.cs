// 2026/10/18 17:31:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Design;

/// <summary>Options of <see cref="DesignLoader.Build"/>.</summary>
public sealed class DesignLoadOptions
{
    /// <summary>
    /// Returns the live parameter for a design parameter name (bindings). Unresolved names get a preview
    /// <see cref="LocalParameter"/> when <see cref="CreatePreviewParameters"/> is set, otherwise stay unbound.
    /// </summary>
    public Func<string, IParameter?>? ResolveParameter { get; set; }

    /// <summary>Creates preview parameters for unresolved bindings (default <c>true</c>).</summary>
    public bool CreatePreviewParameters { get; set; } = true;

    /// <summary>Loads images and SVG (required when the design references assets).</summary>
    public IDesignAssetResolver? Assets { get; set; }

    /// <summary>Shows a label in place of elements of unknown type (default <c>true</c>); otherwise they are skipped.</summary>
    public bool PlaceholderForUnknownTypes { get; set; } = true;

    /// <summary>Called for every element created, with its node (tools map elements back to nodes).</summary>
    public Action<UIElement, DesignNode>? ElementCreated { get; set; }

    /// <summary>Resolver matching parameters by <see cref="IParameter.Name"/> (ordinal comparison).</summary>
    /// <param name="parameters">Available parameters, e.g. <c>Parameters.GetSnapshotParameters(includeBypass: true)</c> in an NPlug editor.</param>
    /// <returns>The resolver.</returns>
    public static Func<string, IParameter?> ByName(IEnumerable<IParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var map = new Dictionary<string, IParameter>(StringComparer.Ordinal);
        foreach (var p in parameters) map.TryAdd(p.Name, p);
        return name => map.GetValueOrDefault(name);
    }
}

/// <summary>Result of <see cref="DesignLoader.Build"/>.</summary>
public sealed class DesignBuildResult
{
    internal DesignBuildResult(UIElement root, Dictionary<string, UIElement> elements, Dictionary<string, IParameter> previews, List<string> warnings)
    {
        Root = root;
        Elements = elements;
        PreviewParameters = previews;
        Warnings = warnings;
    }

    /// <summary>The root element.</summary>
    public UIElement Root { get; }

    /// <summary>Named elements.</summary>
    public IReadOnlyDictionary<string, UIElement> Elements { get; }

    /// <summary>Preview parameters created for unresolved bindings, by design parameter name.</summary>
    public IReadOnlyDictionary<string, IParameter> PreviewParameters { get; }

    /// <summary>Problems found while building (unknown types, invalid values, missing assets…).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>A named element.</summary>
    /// <typeparam name="T">Expected type.</typeparam>
    /// <param name="name">Element name.</param>
    /// <returns>The element.</returns>
    /// <exception cref="KeyNotFoundException">No element has this name, or it has another type.</exception>
    public T Get<T>(string name) where T : UIElement
        => Elements.TryGetValue(name, out var e) && e is T typed ? typed : throw new KeyNotFoundException($"No {typeof(T).Name} named '{name}' in the design.");
}

/// <summary>
/// Builds live PluginUI elements from a design, at run time (no generated code needed) or in tools. Compatible with
/// trimming and NativeAOT: element types come from the <see cref="DesignTypeRegistry"/>.
/// </summary>
/// <example>
/// <code>
/// // In a PluginEditorView:
/// protected override UIElement Build()
/// {
///     var design = DesignJson.Read(EmbeddedText("Editor.pluginui"));
///     var result = DesignLoader.Build(design, options: new DesignLoadOptions
///     {
///         ResolveParameter = DesignLoadOptions.ByName(Parameters.GetSnapshotParameters(includeBypass: true)),
///         Assets = new EmbeddedAssetResolver(typeof(MyEditor).Assembly),
///     });
///     result.Get&lt;Button&gt;("reset").Clicked += Reset;
///     return result.Root;
/// }
/// </code>
/// </example>
public static class DesignLoader
{
    /// <summary>Builds the element tree of a design.</summary>
    /// <param name="document">The design.</param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    /// <param name="options">Bindings, assets, callbacks.</param>
    /// <returns>The root element, named elements and diagnostics.</returns>
    public static DesignBuildResult Build(DesignDocument document, DesignTypeRegistry? registry = null, DesignLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        registry ??= DesignTypeRegistry.Default;
        options ??= new DesignLoadOptions();
        var context = new Context(document, registry, options);
        var root = context.Create(document.Root, "root") ?? new Canvas();
        return new DesignBuildResult(root, context.Elements, context.Previews, context.Warnings);
    }

    /// <summary>
    /// Applies a design value to a live element (tools updating an element after a property edit).
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="property">The property.</param>
    /// <param name="value">The design value (<c>null</c> restores the default).</param>
    /// <param name="info">Type information of the element.</param>
    /// <param name="assets">Asset resolver.</param>
    public static void ApplyProperty(UIElement element, DesignTypeInfo info, DesignPropertyInfo property, object? value, IDesignAssetResolver? assets)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(property);
        object? design = value ?? info.DefaultValue(property.Name);
        property.SetValue(element, DesignValues.Materialize(design, property.PropertyType, assets));
    }

    private sealed class Context(DesignDocument document, DesignTypeRegistry registry, DesignLoadOptions options)
    {
        public Dictionary<string, UIElement> Elements { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IParameter> Previews { get; } = new(StringComparer.Ordinal);
        public List<string> Warnings { get; } = new();

        public UIElement? Create(DesignNode node, string path)
        {
            string where = node.Name is null ? $"{path}/{node.Type}" : $"{path}/{node.Name}";
            var info = registry.Find(node.Type);
            if (info is null)
            {
                Warnings.Add($"{where}: unknown element type '{node.Type}'.");
                if (!options.PlaceholderForUnknownTypes) return null;
                var placeholder = new Label("? " + node.Type) { IsDim = true, Name = node.Name };
                ApplyLayoutOnly(placeholder, node);
                options.ElementCreated?.Invoke(placeholder, node);
                return placeholder;
            }

            UIElement element;
            try
            {
                element = info.Create();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Warnings.Add($"{where}: cannot create {node.Type}: {e.Message}");
                return null;
            }
            if (node.Name is not null)
            {
                element.Name = node.Name;
                if (!Elements.TryAdd(node.Name, element)) Warnings.Add($"{where}: duplicate element name '{node.Name}'.");
            }
            foreach (var (name, value) in node.Properties)
            {
                var property = info.Find(name);
                if (property is null || property.IsBinding)
                {
                    Warnings.Add($"{where}: {node.Type} has no property '{name}'.");
                    continue;
                }
                try
                {
                    property.SetValue(element, DesignValues.Materialize(value, property.PropertyType, options.Assets));
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    var inner = e is System.Reflection.TargetInvocationException { InnerException: { } ie } ? ie : e;
                    Warnings.Add($"{where}.{name}: {inner.Message}");
                }
            }
            foreach (var (name, parameterName) in node.Bindings)
            {
                var property = info.Find(name);
                if (property is not { IsBinding: true })
                {
                    Warnings.Add($"{where}: {node.Type} has no parameter property '{name}'.");
                    continue;
                }
                var parameter = Resolve(parameterName, where);
                if (parameter is not null) property.SetValue(element, parameter);
            }
            options.ElementCreated?.Invoke(element, node);
            AddChildren(element, info, node, where);
            return element;
        }

        private static void ApplyLayoutOnly(UIElement element, DesignNode node)
        {
            if (node.Get(nameof(UIElement.Left)) is float left) element.Left = left;
            if (node.Get(nameof(UIElement.Top)) is float top) element.Top = top;
            if (node.Get(nameof(UIElement.Width)) is float width) element.Width = width;
            if (node.Get(nameof(UIElement.Height)) is float height) element.Height = height;
        }

        private IParameter? Resolve(string name, string where)
        {
            if (options.ResolveParameter?.Invoke(name) is { } live) return live;
            if (Previews.TryGetValue(name, out var preview)) return preview;
            if (!options.CreatePreviewParameters)
            {
                Warnings.Add($"{where}: parameter '{name}' is not available.");
                return null;
            }
            var definition = document.FindParameter(name);
            if (definition is null) Warnings.Add($"{where}: parameter '{name}' is not declared in the design (linear 0–1 preview).");
            preview = definition?.CreatePreview() ?? new LocalParameter(name);
            Previews[name] = preview;
            return preview;
        }

        private void AddChildren(UIElement element, DesignTypeInfo info, DesignNode node, string where)
        {
            if (node.Children.Count == 0) return;
            switch (info.Container)
            {
                case DesignContainerKind.Children:
                    foreach (var childNode in node.Children)
                    {
                        if (Create(childNode, where) is { } child) ((Panel)element).Add(child);
                    }
                    break;
                case DesignContainerKind.Content:
                    if (node.Children.Count > 1) Warnings.Add($"{where}: {node.Type} holds a single element; {node.Children.Count - 1} ignored.");
                    var content = Create(node.Children[0], where);
                    if (element is ScrollViewer scroll) scroll.Content = content;
                    else ((ContentPanel)element).Content = content;
                    break;
                case DesignContainerKind.Split:
                    var split = (SplitView)element;
                    var panes = DesignSlots.SplitPanes(node);
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        if (panes[i] is not { } second)
                        {
                            Warnings.Add($"{where}: a split view holds two elements; '{node.Children[i]}' ignored.");
                            continue;
                        }
                        var child = Create(node.Children[i], where);
                        if (second) split.Second = child; else split.First = child;
                    }
                    break;
                case DesignContainerKind.Tabs:
                    var tabs = (TabView)element;
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        var childNode = node.Children[i];
                        string header = childNode.Slot ?? "Tab " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        tabs.Pages.Add(new TabPage(header, Create(childNode, where)));
                    }
                    break;
                default:
                    Warnings.Add($"{where}: {node.Type} cannot hold children; {node.Children.Count} ignored.");
                    break;
            }
        }
    }
}

/// <summary>
/// Converts live element trees into designs: what <see cref="LiveDesignBridge"/> saves from a running plug-in after
/// live editing, and how an existing hand-written editor is imported into PluginUIEditor.
/// </summary>
public static class DesignCapture
{
    /// <summary>Captures an element tree.</summary>
    /// <param name="root">Root element.</param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    /// <param name="warnings">Receives what could not be captured (optional).</param>
    /// <returns>The root node.</returns>
    /// <remarks>
    /// Only properties that differ from a fresh element are recorded. Bound parameters become bindings by
    /// <see cref="IParameter.Name"/>. Loaded images, custom skins and unregistered element types cannot be described
    /// and are reported.
    /// </remarks>
    public static DesignNode CaptureTree(UIElement root, DesignTypeRegistry? registry = null, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        registry ??= DesignTypeRegistry.Default;
        return Capture(root, registry, warnings, "root") ?? new DesignNode("Canvas");
    }

    /// <summary>Captures the tree, size and theme of a host as a complete document.</summary>
    /// <param name="host">The host.</param>
    /// <param name="registry">Element types (default: <see cref="DesignTypeRegistry.Default"/>).</param>
    /// <param name="warnings">Receives what could not be captured (optional).</param>
    /// <returns>The document (parameters are declared from the bound parameters, as linear 0–1 previews when unknown).</returns>
    public static DesignDocument CaptureHost(UIHost host, DesignTypeRegistry? registry = null, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        var document = new DesignDocument
        {
            DesignSize = host.DesignSize,
            Sizing = host.Sizing,
            Theme = ThemeJson.FromTheme(host.Theme),
            Root = CaptureTree(host.Root, registry, warnings),
        };
        // Declare the bound parameters so that the design previews them with their real ranges.
        foreach (var e in host.Root.DescendantsAndSelf())
        {
            var info = (registry ?? DesignTypeRegistry.Default).Find(e);
            if (info is null) continue;
            foreach (var b in info.Bindings)
            {
                if (b.GetValue(e) is IParameter p && document.FindParameter(p.Name) is null) document.Parameters.Add(DescribeParameter(p));
            }
        }
        return document;
    }

    /// <summary>Describes a live parameter as a design parameter (range sampled from its mapping).</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The description.</returns>
    public static DesignParameter DescribeParameter(IParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var d = new DesignParameter { Name = parameter.Name, Unit = parameter.Units };
        double min = parameter.ToPlain(0.0), max = parameter.ToPlain(1.0), mid = parameter.ToPlain(0.5);
        d.Min = Tidy(min);
        d.Max = Tidy(max);
        d.Default = Tidy(parameter.ToPlain(parameter.DefaultNormalizedValue));
        if (parameter.StepCount == 1)
        {
            d.Kind = DesignParameterKind.Toggle;
            d.Default = parameter.DefaultNormalizedValue >= 0.5 ? 1 : 0;
        }
        else if (parameter.StepCount > 1)
        {
            d.Kind = DesignParameterKind.Choice;
            for (int i = 0; i <= parameter.StepCount; i++) d.Choices.Add(parameter.ToDisplayString(i / (double)parameter.StepCount));
            d.Default = Math.Round(parameter.DefaultNormalizedValue * parameter.StepCount);
        }
        else if (min > 0.0 && max > min && Math.Abs(mid - Math.Sqrt(min * max)) < Math.Abs(mid - 0.5 * (min + max)))
        {
            d.Kind = DesignParameterKind.Logarithmic;
        }
        if (d.Kind is DesignParameterKind.Linear or DesignParameterKind.Logarithmic) d.Decimals = DecimalsOf(parameter.ToDisplayString(parameter.DefaultNormalizedValue));
        return d;
    }

    // Removes the noise of exp/log mappings: 19.999999999999996 → 20.
    private static double Tidy(double value)
        => double.IsFinite(value) ? double.Parse(value.ToString("G12", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture) : value;

    // Decimals shown by a parameter's own formatting ("2400" → 0, "0.350" → 3).
    private static int DecimalsOf(string text)
    {
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) return 0;
        int n = 0;
        for (int i = dot + 1; i < text.Length && char.IsAsciiDigit(text[i]); i++) n++;
        return n;
    }

    private static DesignNode? Capture(UIElement element, DesignTypeRegistry registry, ICollection<string>? warnings, string path)
    {
        var info = registry.Find(element);
        string where = element.Name is null ? $"{path}/{element.GetType().Name}" : $"{path}/{element.Name}";
        if (info is null)
        {
            warnings?.Add($"{where}: {element.GetType().FullName} is not registered for designs (skipped with its children).");
            return null;
        }
        var node = new DesignNode(info.Name) { Name = element.Name };
        foreach (var property in info.Properties)
        {
            object? value = property.GetValue(element);
            if (!DesignValues.TryCapture(value, property.PropertyType, out var design))
            {
                warnings?.Add($"{where}.{property.Name}: value set in code (image, skin…) is not part of the design.");
                continue;
            }
            if (!DesignValues.AreEqual(design, info.DefaultValue(property.Name))) node.Properties[property.Name] = design;
        }
        foreach (var binding in info.Bindings)
        {
            if (binding.GetValue(element) is IParameter parameter) node.Bindings[binding.Name] = parameter.Name;
        }
        switch (element)
        {
            case SplitView split:
                if (split.First is { } first && Capture(first, registry, warnings, where) is { } a) { a.Slot = "First"; node.Children.Add(a); }
                if (split.Second is { } second && Capture(second, registry, warnings, where) is { } b) { b.Slot = "Second"; node.Children.Add(b); }
                break;
            case TabView tabs:
                foreach (var page in tabs.Pages)
                {
                    if (page.Content is null) continue;
                    if (Capture(page.Content, registry, warnings, where) is { } child) { child.Slot = page.Header; node.Children.Add(child); }
                }
                break;
            case ScrollViewer scroll:
                if (scroll.Content is { } content && Capture(content, registry, warnings, where) is { } c) node.Children.Add(c);
                break;
            case ContentPanel panel:
                if (panel.Content is { } inner && Capture(inner, registry, warnings, where) is { } d) node.Children.Add(d);
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    if (Capture(child, registry, warnings, where) is { } n) node.Children.Add(n);
                }
                break;
        }
        return node;
    }
}

/// <summary>Placement rules shared by the loader and the code generator, so both build the same tree.</summary>
internal static class DesignSlots
{
    /// <summary>
    /// Pane of each child of a split view: <c>false</c> for First, <c>true</c> for Second, <c>null</c> when it does
    /// not fit (the pane is already taken). A child without slot takes First, then Second.
    /// </summary>
    public static bool?[] SplitPanes(DesignNode split)
    {
        var result = new bool?[split.Children.Count];
        bool first = false, second = false;
        for (int i = 0; i < result.Length; i++)
        {
            bool? pane = split.Children[i].Slot switch
            {
                "First" => false,
                "Second" => true,
                _ => !first ? false : !second ? true : null,
            };
            if (pane == false && !first) { first = true; result[i] = false; }
            else if (pane == true && !second) { second = true; result[i] = true; }
        }
        return result;
    }
}
