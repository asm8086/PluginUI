// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Bitmap, film-strip, nine-slice, SVG and delegate skins; vector style variants.</summary>
internal static class SkinsPage
{
    public static UIElement Build()
    {
        var c = new Canvas();
        var bare = Theme.Dark.DefaultStyle;

        c.Caption(L.T("FILM STRIP (61 FRAMES, @2x)", "FILM-STRIP (61 IMAGES, @2x)"), 20, 12);
        var strip = Art.KnobStrip();
        for (int i = 0; i < 4; i++)
        {
            c.At(new Knob { Label = L.T("Strip ", "Bande ") + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Value = 0.15 + i * 0.25, Skin = new FilmStripSkin(strip) }, 20 + i * 84, 36, 72, 96);
        }

        c.Caption(L.T("SVG (NATIVE DIRECT2D)", "SVG (NATIF DIRECT2D)"), 380, 12);
        var rotor = SvgImage.FromString(Art.RotorSvg);
        c.At(new Knob { Label = "SVG", Value = 0.6, Skin = new SvgRotarySkin(rotor) }, 380, 36, 72, 96);
        c.At(new SvgView(SvgImage.FromString(Art.LogoSvg)) { ToolTip = "SvgView" }, 470, 48, 64, 64);

        c.Caption(L.T("STATE IMAGES AND NINE-SLICE", "IMAGES PAR ÉTAT ET NEUF-TRANCHES"), 20, 150);
        var padSkin = new ImageStateSkin(Art.PadButton(false), Art.PadButton(true));
        for (int i = 0; i < 4; i++)
        {
            c.At(new ToggleButton { Skin = padSkin, IsOn = i % 2 == 0, Style = bare.Bare() }, 20 + i * 56, 176, 48, 48);
        }
        c.At(new Button(L.T("Nine-slice", "Neuf-tranches")) { Skin = new NineSliceSkin(Art.Frame(false), null, Art.Frame(true)) }, 260, 182, 150, 36);
        c.At(new Button("Large") { Skin = new NineSliceSkin(Art.Frame(false), null, Art.Frame(true)) }, 420, 176, 120, 48);

        c.Caption(L.T("DELEGATE SKIN", "HABILLAGE PAR DÉLÉGUÉ"), 20, 250);
        var bars = new DelegateSkin((DrawingContext dc, Control ctl, in SkinState s) =>
        {
            int n = 12;
            float h = (s.Bounds.Height - (n - 1) * 3f) / n;
            int lit = (int)Math.Round(s.Value * n);
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(s.Bounds.X, s.Bounds.Bottom - (i + 1) * (h + 3f) + 3f, s.Bounds.Width, h);
                var color = i >= 10 ? Color.FromRgb(0xFF5252) : i >= 8 ? Color.FromRgb(0xFFC107) : Color.FromRgb(0x3DD68C);
                dc.FillRoundedRect(r, 2f, i < lit ? color : color.WithAlpha(0.15f));
            }
        });
        c.At(new Slider { Label = L.T("Delegate", "Délégué"), Value = 0.75, Skin = bars }, 20, 276, 48, 200);
        c.At(new Slider { Label = L.T("Delegate", "Délégué"), Value = 0.4, Skin = bars }, 80, 276, 48, 200);

        c.Caption(L.T("VECTOR STYLE VARIANTS", "VARIANTES DE STYLE VECTORIEL"), 180, 250);
        var styles = new (string Name, VectorStyle Style)[]
        {
            (L.T("Default", "Défaut"), bare),
            (L.T("Round", "Rond"), bare with { Roundness = 1f, TrackThickness = 6f }),
            ("Orange", bare.WithAccent(Color.FromRgb(0xFF9800))),
            (L.T("No frame", "Sans cadre"), bare with { DrawFrame = false, DrawShadow = false }),
            (L.T("Thin green", "Vert fin"), bare.WithAccent(Color.FromRgb(0x66BB6A)) with { TrackThickness = 2f }),
            (L.T("Pink", "Rose"), bare.WithAccent(Color.FromRgb(0xEC407A)) with { WidgetFraction = 0.8f }),
        };
        for (int i = 0; i < styles.Length; i++)
        {
            c.At(new Knob { Label = styles[i].Name, Value = 0.2 + i * 0.12, Style = styles[i].Style }, 180 + i * 84, 276, 72, 96);
            c.At(new Button(styles[i].Name) { Style = styles[i].Style }, 180 + i * 84, 390, 76, 28);
        }
        return c;
    }
}
