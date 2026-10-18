// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Design.Tests;

/// <summary>Designs and resolvers shared by the tests.</summary>
internal static class Samples
{
    /// <summary>A design exercising every container kind, bindings, assets, styles and paints.</summary>
    public static DesignDocument Synth()
    {
        var d = new DesignDocument { Name = "Synth", DesignSize = new Size(600f, 360f), Sizing = SizingMode.ScaleToFit };
        d.Theme.Accent = Color.FromRgb(0xFFB74D);
        d.Theme.Metrics["CornerRadius"] = 6f;
        d.Codegen.Namespace = "Test.Editor";
        d.Codegen.ClassName = "SynthEditor";
        d.Parameters.Add(new DesignParameter { Name = "Cutoff", Kind = DesignParameterKind.Logarithmic, Min = 20, Max = 20000, Default = 2400, Unit = "Hz", Decimals = 0 });
        d.Parameters.Add(new DesignParameter { Name = "Gain", Min = -48, Max = 12, Default = 0, Unit = "dB", Decimals = 1 });
        var mode = new DesignParameter { Name = "Mode", Kind = DesignParameterKind.Choice, Default = 1 };
        mode.Choices.AddRange(["Low", "Band", "High"]);
        d.Parameters.Add(mode);
        d.Parameters.Add(new DesignParameter { Name = "Bypass", Kind = DesignParameterKind.Toggle });

        var root = new DesignNode("Canvas") { Name = "root" };
        root.Set("Background", new SolidPaint(Color.FromRgb(0x202226)));
        d.Root = root;

        root.Add(new DesignNode("Label") { Name = "title" }
            .Set("Left", 16f).Set("Top", 8f).Set("Width", 200f).Set("Height", 28f)
            .Set("Text", "Synth \"Demo\"\n")
            .Set("TextStyle", new TextStyle("Segoe UI", 18f, FontWeight.SemiBold)));
        root.Add(new DesignNode("Knob") { Name = "cutoff" }
            .Set("Left", 20f).Set("Top", 48f).Set("Width", 76f).Set("Height", 104f)
            .Set("Label", "CUTOFF").Set("IsBipolar", true).Set("BipolarCenter", 0.5)
            .Bind("Parameter", "Cutoff"));
        root.Add(new DesignNode("Knob")
            .Set("Left", 110f).Set("Top", 48f).Set("Width", 76f).Set("Height", 104f)
            .Set("Style", Theme.Dark.DefaultStyle with { Accent = Color.FromRgb(0x66BB6A), Roundness = 0.5f })
            .Bind("Parameter", "Gain"));
        root.Add(new DesignNode("ToggleButton") { Name = "bypass" }
            .Set("Left", 500f).Set("Top", 8f).Set("Width", 84f).Set("Height", 26f)
            .Set("OnText", "BYPASS").Set("OffText", "BYPASS")
            .Bind("Parameter", "Bypass"));
        root.Add(new DesignNode("SegmentedControl")
            .Set("Left", 200f).Set("Top", 60f).Set("Width", 180f).Set("Height", 26f)
            .Set("Items", new[] { "Low", "Band", "High" })
            .Bind("Parameter", "Mode"));

        var grid = new DesignNode("Grid") { Name = "grid" }
            .Set("Left", 16f).Set("Top", 170f).Set("Width", 280f).Set("Height", 120f)
            .Set("Rows", "Auto,*").Set("Columns", "100,2*,*").Set("ColumnSpacing", 4f);
        grid.Add(new DesignNode("Label").Set("Text", "A").Set("Row", 0).Set("Column", 1));
        grid.Add(new DesignNode("Led").Set("Row", 1).Set("Column", 2).Set("Color", new Color(0.1f, 0.2f, 0.3f, 0.4f)));
        root.Add(grid);

        var border = new DesignNode("Border")
            .Set("Left", 310f).Set("Top", 170f).Set("Width", 120f).Set("Height", 60f)
            .Set("Background", new LinearGradientPaint(new Point(0f, 0f), new Point(0f, 1f), [new GradientStop(0f, Color.FromRgb(0x333333)), new GradientStop(1f, Color.FromRgb(0x111111, 0.5f))]))
            .Set("Padding", new Thickness(4f, 2f));
        border.Add(new DesignNode("Label").Set("Text", "in border"));
        root.Add(border);

        var split = new DesignNode("SplitView").Set("Left", 440f).Set("Top", 170f).Set("Width", 150f).Set("Height", 80f);
        split.Add(new DesignNode("Label") { Slot = "Second" }.Set("Text", "right"));
        split.Add(new DesignNode("Label") { Slot = "First" }.Set("Text", "left"));
        root.Add(split);

        var tabs = new DesignNode("TabView") { Name = "tabs" }.Set("Left", 310f).Set("Top", 240f).Set("Width", 280f).Set("Height", 110f);
        tabs.Add(new DesignNode("Label") { Slot = "First tab" }.Set("Text", "page 1"));
        var page2 = new DesignNode("StackPanel") { Slot = "Second tab" }.Set("Spacing", 2f);
        page2.Add(new DesignNode("Slider").Set("Orientation", Orientation.Horizontal).Bind("Parameter", "Gain"));
        tabs.Add(page2);
        root.Add(tabs);

        root.Add(new DesignNode("Knob") { Name = "skinned" }
            .Set("Left", 200f).Set("Top", 96f).Set("Width", 60f).Set("Height", 60f)
            .Set("Skin", new ImageRotarySkinDescriptor { Rotor = "assets/rotor.png", Background = "assets/back.png", MinAngle = -150f, MaxAngle = 150f }));
        root.Add(new DesignNode("ImageView")
            .Set("Left", 270f).Set("Top", 96f).Set("Width", 40f).Set("Height", 40f)
            .Set("Image", new DesignAsset("assets/logo.png")));
        root.Add(new DesignNode("SvgView")
            .Set("Left", 320f).Set("Top", 96f).Set("Width", 40f).Set("Height", 40f)
            .Set("Svg", new DesignAsset("assets/icon.svg", DesignAssetKind.Svg)));
        return d;
    }

    /// <summary>Asset resolver creating images in memory (no WIC decoding needed).</summary>
    public sealed class FakeAssets : IDesignAssetResolver
    {
        public List<string> Requests { get; } = new();

        public ImageSource LoadImage(string path)
        {
            Requests.Add(path);
            return ImageSource.FromPixels(4, 4, new byte[4 * 4 * 4]);
        }

        public SvgImage LoadSvg(string path)
        {
            Requests.Add(path);
            return SvgImage.FromString("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\"/>");
        }
    }
}
