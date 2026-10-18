// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace PluginUI.Design.Tests;

/// <summary>Registry coverage, value conversions and the .pluginui format.</summary>
public class DocumentTests
{
    private static readonly HashSet<string> NotDesignable = ["AboutBox", "ColorPickerPopup", "MenuPopup", "TextEditPopup", "DialogText"];

    [Test]
    public void EveryPluginUIElementIsRegistered()
    {
        var registry = DesignTypeRegistry.CreateDefault();
        var expected = typeof(UIElement).Assembly.GetExportedTypes()
            .Where(t => typeof(UIElement).IsAssignableFrom(t) && !t.IsAbstract && !NotDesignable.Contains(t.Name))
            .Select(t => t.Name).OrderBy(n => n).ToList();
        var registered = registry.Types.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.That(registered, Is.EquivalentTo(expected));
        Assert.Multiple(() =>
        {
            Assert.That(registry.Find("Knob")!.Container, Is.EqualTo(DesignContainerKind.None));
            Assert.That(registry.Find("Canvas")!.Container, Is.EqualTo(DesignContainerKind.Children));
            Assert.That(registry.Find("Border")!.Container, Is.EqualTo(DesignContainerKind.Content));
            Assert.That(registry.Find("ScrollViewer")!.Container, Is.EqualTo(DesignContainerKind.Content));
            Assert.That(registry.Find("SplitView")!.Container, Is.EqualTo(DesignContainerKind.Split));
            Assert.That(registry.Find("TabView")!.Container, Is.EqualTo(DesignContainerKind.Tabs));
            Assert.That(registry.Find("Knob")!.Bindings.Select(b => b.Name), Is.EquivalentTo(new[] { "Parameter" }));
            Assert.That(registry.Find("XYPad")!.Bindings.Select(b => b.Name), Is.EquivalentTo(new[] { "XParameter", "YParameter" }));
            Assert.That(registry.Find("Grid")!.Find("Rows"), Is.Not.Null);
            Assert.That(registry.Find("Knob")!.Find("Name"), Is.Null, "Name is DesignNode.Name");
            Assert.That(registry.Find("Knob")!.Find("Skin")!.PropertyType, Is.EqualTo(typeof(IControlSkin)));
            Assert.That(registry.Types.Where(t => t.ToolboxCategory is not null).Select(t => t.ToolboxCategory).Distinct(),
                Is.SubsetOf(BuiltInDesignTypes.Categories));
        });
    }

    [Test]
    public void EveryDefaultValueRoundTripsThroughJson()
    {
        // Covers every converter with the real property types and default values of all elements.
        var problems = new List<string>();
        int checkedCount = 0;
        foreach (var info in DesignTypeRegistry.CreateDefault().Types)
        {
            foreach (var p in info.Properties)
            {
                object? value = info.DefaultValue(p.Name);
                if (value is null) continue;
                try
                {
                    string json = DesignValues.ToJsonString(value);
                    object? back = DesignValues.FromJsonString(json, p.PropertyType);
                    if (!DesignValues.AreEqual(value, back)) problems.Add($"{info.Name}.{p.Name}: {json} → {DesignValues.ToJsonString(back)}");
                    _ = DesignValues.ToCSharp(value, p.PropertyType, CSharpDesignGenerator.TypeName);
                    checkedCount++;
                }
                catch (Exception e)
                {
                    problems.Add($"{info.Name}.{p.Name}: {e.GetType().Name} {e.Message}");
                }
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
        Assert.That(checkedCount, Is.GreaterThan(1500));
    }

    [Test]
    public void ValuesUseCompactInvariantForms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignValues.ToJsonString(Color.FromRgb(0x4FC3F7)), Is.EqualTo("\"#4FC3F7\""));
            Assert.That(DesignValues.ToJsonString(Color.FromRgb(0x000000, 0.45f)), Is.EqualTo("[0,0,0,0.45]"), "alpha off the 8-bit grid: exact floats");
            Assert.That(DesignValues.ToJsonString(Color.FromBytes(1, 2, 3, 128)), Is.EqualTo("\"#01020380\""));
            Assert.That(DesignValues.ToJsonString(new Thickness(4f)), Is.EqualTo("4"));
            Assert.That(DesignValues.ToJsonString(new Thickness(1f, 2f, 3f, 4.5f)), Is.EqualTo("[1,2,3,4.5]"));
            Assert.That(DesignValues.ToJsonString(float.NaN), Is.EqualTo("\"NaN\""));
            Assert.That(DesignValues.ToJsonString(0.1f), Is.EqualTo("0.1"), "shortest round-trip form of the float");
            Assert.That(DesignValues.ToJsonString(new TextStyle(Size: 14f)), Is.EqualTo("{\"Size\":14}"), "only fields that differ from the default");
            Assert.That(DesignValues.ToJsonString(MouseButton.Left), Is.EqualTo("\"Left\""));
            Assert.That(DesignValues.FromJsonString("[1,2]", typeof(Thickness)), Is.EqualTo(new Thickness(1f, 2f)));
            Assert.That(DesignValues.FromJsonString("\"#ABC\"", typeof(Color)), Is.EqualTo(Color.FromRgb(0xAABBCC)));
            Assert.That(DesignValues.FromJsonString("null", typeof(Color?)), Is.Null);
            Assert.That(DesignValues.FromJsonString("\"-Infinity\"", typeof(double)), Is.EqualTo(double.NegativeInfinity));
            Assert.That(DesignValues.FromJsonString("\"1.5\"", typeof(float)), Is.EqualTo(1.5f));
            Assert.That(() => DesignValues.FromJsonString("\"1,5\"", typeof(float)), Throws.TypeOf<FormatException>(), "no comma decimals");
            Assert.That(() => DesignValues.FromJsonString("\"x\"", typeof(Color)), Throws.TypeOf<FormatException>());
        });
        var radial = new RadialGradientPaint(new Point(0.5f, 0.4f), 0.6f, 0.5f, [new(0f, Color.White), new(1f, Color.Black)], new Point(0.1f, 0f), false);
        var back = (RadialGradientPaint)DesignValues.FromJsonString(DesignValues.ToJsonString(radial), typeof(Paint))!;
        Assert.That(back.Stops, Is.EqualTo(radial.Stops));
        Assert.That(back with { Stops = radial.Stops }, Is.EqualTo(radial));
    }

    [Test]
    public void DocumentRoundTripIsLosslessAndDeterministic()
    {
        var document = Samples.Synth();
        string json = DesignJson.Write(document);
        var warnings = new List<string>();
        var read = DesignJson.Read(json, warnings: warnings);
        Assert.That(warnings, Is.Empty);
        Assert.That(DesignJson.Write(read), Is.EqualTo(json));
        Assert.Multiple(() =>
        {
            Assert.That(read.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "Cutoff", "Gain", "Mode", "Bypass" }));
            Assert.That(read.Parameters[2].Choices, Is.EqualTo(new[] { "Low", "Band", "High" }));
            Assert.That(read.FindNode("cutoff")!.Bindings["Parameter"], Is.EqualTo("Cutoff"));
            Assert.That(read.FindNode("skinned")!.Get("Skin"), Is.TypeOf<ImageRotarySkinDescriptor>());
            Assert.That(read.AssetPaths(), Is.EqualTo(new[] { "assets/rotor.png", "assets/back.png", "assets/logo.png", "assets/icon.svg" }));
            Assert.That(read.Theme.Accent, Is.EqualTo(Color.FromRgb(0xFFB74D)));
            Assert.That(json, Does.Contain("\"Text\": \"Synth \\\"Demo\\\"\\n\""));
        });
    }

    [Test]
    public void UnknownContentIsKeptAndReported()
    {
        const string json = """
            {
              "format": "pluginui", "version": 1, "name": "X",
              // comments and trailing commas are accepted
              "root": { "type": "Canvas", "children": [
                { "type": "FutureControl", "name": "f", "properties": { "Glow": 3 } },
                { "type": "Knob", "properties": { "Left": 10, "Sparkle": true, "Width": "wide" } },
              ] }
            }
            """;
        var warnings = new List<string>();
        var document = DesignJson.Read(json, warnings: warnings);
        Assert.That(warnings, Has.Some.Contains("FutureControl").And.Some.Contains("Sparkle").And.Some.Contains("Width"));
        string written = DesignJson.Write(document);
        Assert.That(written, Does.Contain("\"Glow\": 3").And.Contain("\"Sparkle\": true").And.Contain("\"Width\": \"wide\""));
        Assert.That(() => DesignJson.Read("{\"root\":{}}"), Throws.TypeOf<FormatException>());

        // Members of a newer format survive a round trip at every level, with the version number.
        const string future = """
            { "format": "pluginui", "version": 2, "name": "F", "futureSection": { "a": [1, 2] },
              "parameters": [ { "name": "P", "kind": "Linear", "min": 0, "max": 1, "default": 0, "futureField": "x" } ],
              "codegen": { "namespace": "N", "class": "C", "futureOption": true },
              "theme": { "base": "Dark", "palette": { "Warning": "#FF0000", "Glow": "#00FF00", "Text": 12 }, "typography": { "Family": 12 } },
              "root": { "type": "Canvas", "events": { "Click": "OnClick" } } }
            """;
        warnings.Clear();
        var newer = DesignJson.Read(future, warnings: warnings);
        Assert.That(warnings, Has.Some.Contains("newer version").And.Some.Contains("futureSection"));
        Assert.That(warnings, Has.Some.Contains("'Glow'").And.Some.Contains("theme.palette.Text").And.Some.Contains("theme.typography.Family"));
        Assert.That(newer.Theme.UnknownMembers.Keys, Is.EquivalentTo(new[] { "palette.Glow", "palette.Text", "typography.Family" }));
        Assert.That(newer.Theme.Palette.Keys, Is.EquivalentTo(new[] { "Warning" }), "invalid theme overrides are skipped, valid ones kept");
        Assert.That(newer.Theme.Create().Palette.Warning, Is.EqualTo(Color.FromRgb(0xFF0000)));
        string again = DesignJson.Write(newer);
        Assert.That(again, Does.Contain("\"version\": 2").And.Contain("\"futureSection\": { \"a\": [1, 2] }").And.Contain("\"futureField\": \"x\"")
            .And.Contain("\"futureOption\": true").And.Contain("\"events\": { \"Click\": \"OnClick\" }"));
        Assert.That(again, Does.Contain("\"Glow\": \"#00FF00\"").And.Contain("\"Text\": 12").And.Contain("\"Family\": 12"), "theme content that cannot be applied is kept");
        Assert.That(DesignJson.Write(DesignJson.Read(again)), Is.EqualTo(again));
        Assert.That(() => DesignJson.Read("not json"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void ThemesRoundTrip()
    {
        var design = Samples.Synth().Theme;
        design.Palette["Danger"] = Color.FromRgb(0xFF0000);
        design.Typography["Family"] = "Inter";
        design.Typography["NormalSize"] = 13f;
        var theme = design.Create();
        Assert.Multiple(() =>
        {
            Assert.That(theme.Palette.Accent, Is.EqualTo(Color.FromRgb(0xFFB74D)));
            Assert.That(theme.Palette.Focus, Is.EqualTo(Color.FromRgb(0xFFB74D).WithAlpha(0.8f)));
            Assert.That(theme.Palette.Danger, Is.EqualTo(Color.FromRgb(0xFF0000)));
            Assert.That(theme.Typography.Family, Is.EqualTo("Inter"));
            Assert.That(theme.Metrics.CornerRadius, Is.EqualTo(6f));
            Assert.That(theme.DefaultStyle.Accent, Is.EqualTo(theme.Palette.Accent), "derived style follows the new palette");
        });
        var imported = ThemeJson.Import(ThemeJson.Export(theme));
        Assert.That(imported.Base, Is.EqualTo("Dark"));
        Assert.That(imported.Accent, Is.EqualTo(design.Accent));
        Assert.That(imported.Palette.Keys, Is.EquivalentTo(new[] { "Danger" }));
        Assert.That(ThemeJson.Export(imported.Create()), Is.EqualTo(ThemeJson.Export(theme)));
        Assert.That(ThemeJson.FromTheme(Theme.Light).Base, Is.EqualTo("Light"));
        Assert.That(ThemeJson.FromTheme(Theme.Light).Palette, Is.Empty);
        Assert.That(() => ThemeJson.Import("{}"), Throws.TypeOf<FormatException>());
    }
}
