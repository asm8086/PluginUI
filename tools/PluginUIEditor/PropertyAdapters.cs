// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using PluginUI;
using PluginUI.Design;
using Color = PluginUI.Color;
using Point = PluginUI.Point;
using Rectangle = PluginUI.Rect;
using Size = PluginUI.Size;

namespace PluginUIEditor;

/// <summary>A property descriptor backed by delegates (PropertyGrid adapters).</summary>
internal sealed class DelegateDescriptor : PropertyDescriptor
{
    private readonly Type _type;
    private readonly Func<object?> _get;
    private readonly Action<object?>? _set;
    private readonly Func<bool>? _isSet;
    private readonly Action? _reset;
    private readonly TypeConverter? _converter;
    private readonly UITypeEditor? _editor;

    public DelegateDescriptor(string name, string displayName, string category, string description, Type type,
        Func<object?> get, Action<object?>? set, Func<bool>? isSet = null, Action? reset = null, TypeConverter? converter = null, UITypeEditor? editor = null)
        : base(name, MakeAttributes(category, description, displayName))
    {
        _editor = editor;
        _type = type;
        _get = get;
        _set = set;
        _isSet = isSet;
        _reset = reset;
        _converter = converter;
    }

    private static Attribute[] MakeAttributes(string category, string description, string displayName)
        => [new CategoryAttribute(category), new DescriptionAttribute(description), new DisplayNameAttribute(displayName)];

    // The editor instance carries context (session, asset kind): returned directly instead of an [Editor] type.
    public override object? GetEditor(Type editorBaseType)
        => _editor is not null && editorBaseType == typeof(UITypeEditor) ? _editor : base.GetEditor(editorBaseType);

    public override Type ComponentType => typeof(object);

    public override bool IsReadOnly => _set is null;

    public override Type PropertyType => _type;

    public override TypeConverter Converter => _converter ?? base.Converter;

    public override bool CanResetValue(object component) => _isSet?.Invoke() ?? false;

    public override object? GetValue(object? component) => _get();

    public override void ResetValue(object component) => _reset?.Invoke();

    public override void SetValue(object? component, object? value) => _set?.Invoke(value);

    public override bool ShouldSerializeValue(object component) => _isSet?.Invoke() ?? false;
}

/// <summary>Type descriptor exposing a fixed list of property descriptors.</summary>
internal sealed class AdapterDescriptor(string className, PropertyDescriptorCollection properties) : CustomTypeDescriptor
{
    public override string GetClassName() => className;

    public override string GetComponentName() => className;

    public override PropertyDescriptorCollection GetProperties() => properties;

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => properties;

    public override object GetPropertyOwner(PropertyDescriptor? pd) => this;
}

/// <summary>
/// The properties of a design element for the PropertyGrid: name, slot, every designable property (shown in bold when
/// set, reset with the context menu) and the parameter bindings.
/// </summary>
internal static class NodeAdapter
{
    public static AdapterDescriptor Create(DesignSession session, DesignNode node)
    {
        var t = EditorStrings.Current;
        var info = session.Registry.Find(node.Type);
        var list = new List<PropertyDescriptor>
        {
            new DelegateDescriptor("(Name)", t.ElementName, t.CategoryDesign, t.ElementNameHelp, typeof(string),
                () => node.Name ?? string.Empty, v =>
                {
                    // The PropertyGrid shows the message and restores the previous value.
                    if (!session.Rename(node, v as string)) throw new ArgumentException(t.InvalidElementName);
                }),
        };
        var parent = session.ParentOf(node);
        if (parent is not null && session.ContainerOf(parent) is DesignContainerKind.Tabs or DesignContainerKind.Split)
        {
            list.Add(new DelegateDescriptor("(Slot)", t.SlotName, t.CategoryDesign, t.SlotHelp, typeof(string),
                () => node.Slot ?? string.Empty, v => session.SetSlot(node, v as string)));
        }
        if (info is null) return new AdapterDescriptor(node.Type, new PropertyDescriptorCollection(list.ToArray()));

        foreach (var property in info.Properties)
        {
            list.Add(ValueDescriptor(session, node, info, property));
        }
        foreach (var binding in info.Bindings)
        {
            list.Add(new DelegateDescriptor(binding.Name, binding.Name, t.CategoryBinding, t.BindingHelp + "\n" + XmlDocs.Summary(binding.Property), typeof(string),
                () => node.Bindings.TryGetValue(binding.Name, out var p) ? p : string.Empty,
                v => session.SetBinding(node, binding.Name, v as string),
                () => node.Bindings.ContainsKey(binding.Name),
                () => session.SetBinding(node, binding.Name, null),
                new ChoicesConverter(() => [string.Empty, .. session.Document.Parameters.Select(p => p.Name)])));
        }
        return new AdapterDescriptor(node.Type, new PropertyDescriptorCollection(list.ToArray()));
    }

    private static PropertyDescriptor ValueDescriptor(DesignSession session, DesignNode node, DesignTypeInfo info, DesignPropertyInfo property)
    {
        var t = EditorStrings.Current;
        string category = t.Category(property.Category);
        string description = XmlDocs.Summary(property.Property);
        var type = property.PropertyType;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        object? Current() => node.Properties.TryGetValue(property.Name, out var v) ? v : info.DefaultValue(property.Name);
        bool IsSet() => node.Properties.ContainsKey(property.Name);
        void Reset() => session.SetProperty(node, property.Name, null);

        // Types the grid edits directly (with the converters registered in Program).
        if (underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(Color)
            || underlying == typeof(Thickness) || underlying == typeof(Point) || underlying == typeof(Size) || underlying == typeof(Rectangle))
        {
            return new DelegateDescriptor(property.Name, property.Name, category, description, type,
                () => Current() is double d && underlying == typeof(float) ? (float)d : Current(),
                v => session.SetProperty(node, property.Name, v),
                IsSet, Reset, editor: underlying == typeof(Color) ? new ColorEditor() : null);
        }

        // Asset paths: plain text with a file picker.
        if (underlying == typeof(ImageSource) || underlying == typeof(SvgImage))
        {
            var kind = underlying == typeof(SvgImage) ? DesignAssetKind.Svg : DesignAssetKind.Image;
            return new DelegateDescriptor(property.Name, property.Name, category, description + "\n(assets/… relative to the design file)", typeof(string),
                () => (Current() as DesignAsset)?.Path ?? string.Empty,
                v => session.SetProperty(node, property.Name, string.IsNullOrWhiteSpace(v as string) ? null : new DesignAsset(DesignAsset.Normalize((string)v!), kind)),
                IsSet, Reset, editor: new AssetEditor(session, kind));
        }

        // Everything else (text styles, vector styles, paints, lists, skins, film strips): compact JSON.
        return new DelegateDescriptor(property.Name, property.Name, category, description + "\n(JSON)", typeof(string),
            () => Current() is { } value ? DesignValues.ToJsonString(value) : string.Empty,
            v =>
            {
                string text = (v as string ?? string.Empty).Trim();
                session.SetProperty(node, property.Name, text.Length == 0 ? null : DesignValues.FromJsonString(text, type));
            },
            IsSet, Reset);
    }
}

/// <summary>The design settings (size, theme, code generation) for the PropertyGrid.</summary>
internal static class DesignSettingsAdapter
{
    public static AdapterDescriptor Create(DesignSession session)
    {
        var t = EditorStrings.Current;
        var d = session.Document;
        void Edit(Action a) => session.EditSettings(a);
        var list = new PropertyDescriptor[]
        {
            new DelegateDescriptor("Name", t.DesignName, t.CategoryGeneral, t.DesignNameHelp, typeof(string), () => d.Name, v => Edit(() => d.Name = v as string ?? d.Name)),
            new DelegateDescriptor("Size", t.DesignSize, t.CategoryGeneral, t.DesignSizeHelp, typeof(Size), () => d.DesignSize,
                v => Edit(() => d.DesignSize = v is Size s && s.Width >= 16f && s.Height >= 16f ? s : d.DesignSize)),
            new DelegateDescriptor("Sizing", t.DesignSizing, t.CategoryGeneral, t.DesignSizingHelp, typeof(SizingMode), () => d.Sizing, v => Edit(() => d.Sizing = (SizingMode)v!)),
            new DelegateDescriptor("ThemeBase", t.ThemeBase, t.CategoryTheme, t.ThemeBaseHelp, typeof(string), () => d.Theme.Base,
                v => Edit(() => d.Theme.Base = v as string ?? "Dark"), converter: new ChoicesConverter(() => ["Dark", "Light"], exclusive: true)),
            new DelegateDescriptor("Accent", t.ThemeAccent, t.CategoryTheme, t.ThemeAccentHelp, typeof(Color?), () => d.Theme.Accent,
                v => Edit(() => d.Theme.Accent = v as Color?), () => d.Theme.Accent is not null, () => Edit(() => d.Theme.Accent = null), editor: new ColorEditor()),
            new DelegateDescriptor("Namespace", t.CodegenNamespace, t.CategoryCodegen, t.CodegenNamespaceHelp, typeof(string), () => d.Codegen.Namespace, v =>
                {
                    string text = (v as string ?? string.Empty).Trim();
                    if (CSharpDesignGenerator.ValidateCodegen(new CodegenSettings { Namespace = text, ClassName = "Valid" }, session.Registry) is not null)
                    {
                        throw new ArgumentException(string.Format(System.Globalization.CultureInfo.InvariantCulture, t.InvalidIdentifierFormat, text));
                    }
                    Edit(() => d.Codegen.Namespace = text);
                }),
            new DelegateDescriptor("ClassName", t.CodegenClass, t.CategoryCodegen, t.CodegenClassHelp, typeof(string), () => d.Codegen.ClassName, v =>
                {
                    string text = (v as string ?? string.Empty).Trim();
                    if (CSharpDesignGenerator.ValidateCodegen(new CodegenSettings { Namespace = "Valid", ClassName = text }, session.Registry) is not null)
                    {
                        throw new ArgumentException(string.Format(System.Globalization.CultureInfo.InvariantCulture, t.InvalidIdentifierFormat, text));
                    }
                    Edit(() => d.Codegen.ClassName = text);
                }),
            new DelegateDescriptor("Target", t.CodegenTarget, t.CategoryCodegen, t.CodegenTargetHelp, typeof(CodegenTarget), () => d.Codegen.Target, v => Edit(() => d.Codegen.Target = (CodegenTarget)v!)),
            new DelegateDescriptor("BuildOverride", t.CodegenBuild, t.CategoryCodegen, t.CodegenBuildHelp, typeof(bool), () => d.Codegen.GenerateBuildOverride, v => Edit(() => d.Codegen.GenerateBuildOverride = (bool)v!)),
            new DelegateDescriptor("FieldAccess", t.CodegenAccess, t.CategoryCodegen, t.CodegenAccessHelp, typeof(string), () => d.Codegen.FieldAccess,
                v => Edit(() => d.Codegen.FieldAccess = v as string ?? "private"), converter: new ChoicesConverter(() => ["private", "internal", "protected", "public"], exclusive: true)),
        };
        return new AdapterDescriptor("Design", new PropertyDescriptorCollection(list));
    }
}

/// <summary>String converter offering a list of values.</summary>
internal sealed class ChoicesConverter(Func<IReadOnlyList<string>> values, bool exclusive = false) : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => exclusive;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(values().ToArray());
}

/// <summary>Converters of PluginUI value types: invariant text, decimal point only.</summary>
internal static class ValueConverters
{
    public static void Register()
    {
        TypeDescriptor.AddAttributes(typeof(Color), new TypeConverterAttribute(typeof(ColorConverter)));
        TypeDescriptor.AddAttributes(typeof(Thickness), new TypeConverterAttribute(typeof(ThicknessConverter)));
        TypeDescriptor.AddAttributes(typeof(Point), new TypeConverterAttribute(typeof(PointConverter)));
        TypeDescriptor.AddAttributes(typeof(Size), new TypeConverterAttribute(typeof(SizeConverter)));
        TypeDescriptor.AddAttributes(typeof(Rectangle), new TypeConverterAttribute(typeof(RectConverter)));
    }

    internal static float[] Floats(string text)
    {
        var parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!InvariantNumber.TryParse(parts[i], out double v)) throw new FormatException($"'{parts[i]}' is not a number (use a decimal point).");
            values[i] = (float)v;
        }
        return values;
    }

    internal static string Format(params float[] values) => string.Join(", ", values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));

    public abstract class TextConverter<T> : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            => value is string s ? Parse(s.Trim()) : base.ConvertFrom(context, culture, value);

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
            => destinationType == typeof(string) && value is T v ? Format(v) : base.ConvertTo(context, culture, value, destinationType);

        protected abstract T Parse(string text);

        protected abstract string Format(T value);
    }

    public sealed class ColorConverter : TextConverter<Color>
    {
        protected override Color Parse(string text)
        {
            if (Color.TryParse(text, out var c)) return c;
            var f = Floats(text);
            return f.Length switch { 3 => new Color(f[0], f[1], f[2]), 4 => new Color(f[0], f[1], f[2], f[3]), _ => throw new FormatException("#RRGGBB, #RRGGBBAA or r, g, b, a") };
        }

        protected override string Format(Color value) => value.ToHex();
    }

    public sealed class ThicknessConverter : TextConverter<Thickness>
    {
        protected override Thickness Parse(string text)
        {
            var f = Floats(text);
            return f.Length switch { 1 => new Thickness(f[0]), 2 => new Thickness(f[0], f[1]), 4 => new Thickness(f[0], f[1], f[2], f[3]), _ => throw new FormatException("1, 2 or 4 numbers") };
        }

        protected override string Format(Thickness v) => v.Left == v.Top && v.Top == v.Right && v.Right == v.Bottom ? ValueConverters.Format(v.Left) : ValueConverters.Format(v.Left, v.Top, v.Right, v.Bottom);
    }

    public sealed class PointConverter : TextConverter<Point>
    {
        protected override Point Parse(string text) { var f = Floats(text); return f.Length == 2 ? new Point(f[0], f[1]) : throw new FormatException("x, y"); }

        protected override string Format(Point v) => ValueConverters.Format(v.X, v.Y);
    }

    public sealed class SizeConverter : TextConverter<Size>
    {
        protected override Size Parse(string text) { var f = Floats(text); return f.Length == 2 ? new Size(f[0], f[1]) : throw new FormatException("width, height"); }

        protected override string Format(Size v) => ValueConverters.Format(v.Width, v.Height);
    }

    public sealed class RectConverter : TextConverter<Rectangle>
    {
        protected override Rectangle Parse(string text) { var f = Floats(text); return f.Length == 4 ? new Rectangle(f[0], f[1], f[2], f[3]) : throw new FormatException("x, y, width, height"); }

        protected override string Format(Rectangle v) => ValueConverters.Format(v.X, v.Y, v.Width, v.Height);
    }
}

/// <summary>Color picker (Windows color dialog) with a swatch in the grid.</summary>
internal sealed class ColorEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

    public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

    public override void PaintValue(PaintValueEventArgs e)
    {
        if (e.Value is not Color c) return;
        using var brush = new System.Drawing.SolidBrush(ToDrawing(c));
        e.Graphics.FillRectangle(brush, e.Bounds);
    }

    private static System.Drawing.Color ToDrawing(Color c)
        => System.Drawing.Color.FromArgb((int)MathF.Round(c.A * 255f), (int)MathF.Round(c.R * 255f), (int)MathF.Round(c.G * 255f), (int)MathF.Round(c.B * 255f));

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        var current = value as Color? ?? Color.White;
        using var dialog = new ColorDialog { FullOpen = true, Color = ToDrawing(current) };
        if (dialog.ShowDialog() != DialogResult.OK) return value;
        var picked = dialog.Color;
        return new Color(picked.R / 255f, picked.G / 255f, picked.B / 255f, current.A);
    }
}

/// <summary>File picker for asset paths: copies nothing, stores the path relative to the design folder.</summary>
internal sealed class AssetEditor(DesignSession session, DesignAssetKind kind) : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = kind == DesignAssetKind.Svg ? "SVG (*.svg)|*.svg" : "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            InitialDirectory = Path.Combine(session.AssetFolder, "assets"),
        };
        if (session.FilePath is null)
        {
            MessageBox.Show(EditorStrings.Current.SaveDesignFirst, Application.ProductName, System.Windows.Forms.MessageBoxButtons.OK, MessageBoxIcon.Information);
            return value;
        }
        if (dialog.ShowDialog() != DialogResult.OK) return value;
        string relative = Path.GetRelativePath(session.AssetFolder, dialog.FileName);
        if (!ProjectScaffolder.IsLocalAssetPath(relative))
        {
            // Outside the design folder: copied into its assets folder, so that projects can embed it.
            string folder = Path.Combine(session.AssetFolder, "assets");
            Directory.CreateDirectory(folder);
            string name = Path.GetFileNameWithoutExtension(dialog.FileName), extension = Path.GetExtension(dialog.FileName);
            string target = Path.Combine(folder, name + extension);
            for (int i = 2; File.Exists(target) && !FilesEqual(target, dialog.FileName); i++) target = Path.Combine(folder, name + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + extension);
            if (!File.Exists(target)) File.Copy(dialog.FileName, target);
            relative = Path.GetRelativePath(session.AssetFolder, target);
            MessageBox.Show(string.Format(System.Globalization.CultureInfo.InvariantCulture, EditorStrings.Current.AssetCopiedFormat, DesignAsset.Normalize(relative)),
                Application.ProductName, System.Windows.Forms.MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return DesignAsset.Normalize(relative);
    }

    private static bool FilesEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}

/// <summary>Summaries of the XML documentation files next to the application (PluginUI.xml, PluginUI.Design.xml).</summary>
internal static class XmlDocs
{
    private static readonly Lazy<Dictionary<string, string>> Members = new(Load);

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.xml")
            .Concat(Extensions.Loaded.Select(p => Path.ChangeExtension(p, ".xml")).Where(File.Exists));
        foreach (var file in files)
        {
            try
            {
                var doc = XDocument.Load(file);
                foreach (var member in doc.Descendants("member"))
                {
                    string? name = member.Attribute("name")?.Value;
                    var summary = member.Element("summary");
                    if (name is null || summary is null) continue;
                    map[name] = string.Join(" ", summary.Nodes().Select(Render).Where(x => x.Length > 0)).Trim();
                }
            }
            catch (Exception e) when (e is IOException or System.Xml.XmlException)
            {
                // Not a documentation file.
            }
        }
        return map;
    }

    private static string Render(XNode node) => node switch
    {
        XText text => text.Value.Trim(),
        XElement { Name.LocalName: "see" or "seealso" } see => (see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? see.Value).Split('.', ':').Last(),
        XElement { Name.LocalName: "paramref" or "typeparamref" } p => p.Attribute("name")?.Value ?? string.Empty,
        XElement element => string.Join(" ", element.Nodes().Select(Render)),
        _ => string.Empty,
    };

    public static string TypeSummary(Type type) => Members.Value.TryGetValue("T:" + type.FullName, out var text) ? text : string.Empty;

    public static string Summary(PropertyInfo? property)
    {
        if (property is null) return string.Empty;
        for (var t = property.DeclaringType; t is not null; t = t.BaseType)
        {
            if (Members.Value.TryGetValue($"P:{t.FullName}.{property.Name}", out var text)) return text;
        }
        return string.Empty;
    }
}
