// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Knobs, sliders, XY pad, multi-slider, envelope.</summary>
internal static class RotaryLinearPage
{
    public static UIElement Build()
    {
        var c = new Canvas();
        var gain = new LocalParameter("Gain", -24, 24, 0, "dB", decimals: 1) { Value = 6 };
        var cutoff = new LocalParameter(L.T("Cutoff", "Coupure"), 20, 20000, 1000, "Hz", ParameterScale.Logarithmic(20, 20000), decimals: 0);
        var reso = new LocalParameter(L.T("Resonance", "Résonance"), 0.5, 12, 0.707, "Q", ParameterScale.Logarithmic(0.5, 12));

        c.Caption(L.T("KNOBS", "POTENTIOMÈTRES"), 20, 12);
        c.At(new Knob(gain) { IsBipolar = true, ShowValueBubble = true, ToolTip = L.T("Bipolar, center = 0 dB. Double-click: default value, Alt+click: type a value.", "Bipolaire, centre = 0 dB. Double-clic : valeur par défaut, Alt+clic : saisie.") }, 20, 36, 64, 84);
        c.At(new Knob(cutoff) { KnobStyle = KnobStyle.Classic, ModulationAmount = 0.2, ToolTip = L.T("Classic style with modulation arc", "Style classique avec arc de modulation") }, 100, 36, 64, 84);
        c.At(new Knob(reso) { KnobStyle = KnobStyle.Minimal }, 180, 36, 64, 84);
        c.At(new Knob { Label = "Mix", Value = 0.33, ModulationAmount = -0.15 }, 260, 26, 100, 120);
        c.At(new EndlessKnob { Label = L.T("Encoder", "Encodeur"), ToolTip = L.T("Endless rotation", "Rotation infinie") }, 380, 36, 64, 84);

        c.Caption(L.T("SLIDERS", "CURSEURS"), 20, 160);
        c.At(new Slider { Label = L.T("Level", "Niveau"), Value = 0.65 }, 20, 184, 48, 180);
        c.At(new Slider { Label = "Pan", Value = 0.3, IsBipolar = true, Handle = SliderHandle.Round }, 80, 184, 48, 180);
        c.At(new Slider { Label = L.T("Fill", "Plein"), Value = 0.45, Handle = SliderHandle.None }, 140, 184, 44, 180);
        c.At(new Slider(Orientation.Horizontal) { Label = L.T("Width", "Largeur"), Value = 0.55 }, 210, 194, 260, 28);
        c.At(new Slider(Orientation.Horizontal) { Label = L.T("Steps", "Pas"), StepCount = 4, Value = 0.5, Handle = SliderHandle.Round }, 210, 234, 260, 28);
        c.At(new Slider(Orientation.Horizontal) { Label = L.T("Line", "Ligne"), Value = 0.2, Handle = SliderHandle.Line }, 210, 274, 260, 28);
        c.At(new ScrollBar(Orientation.Horizontal) { Maximum = 100, ViewportSize = 40, Value = 50, ShowArrows = true, AutoHide = false }, 210, 324, 260, 14);

        c.Caption(L.T("RANGES AND XY PAD", "PLAGES ET PAVÉ XY"), 500, 12);
        c.At(new RangeSlider { Label = L.T("Range", "Tessiture"), Low = 0.2, High = 0.7 }, 500, 36, 240, 48);
        c.At(new RangeSlider { Label = L.T("Band", "Bande"), Orientation = Orientation.Vertical, Low = 0.3, High = 0.8 }, 500, 100, 48, 200);
        c.At(new XYPad { Label = L.T("XY filter", "Filtre XY"), X = 0.65, Y = 0.3 }, 570, 100, 200, 220);
        c.At(new ScrollBar { Maximum = 100, ViewportSize = 25, Value = 30 }, 790, 100, 12, 220);

        c.Caption(L.T("SEQUENCES AND ENVELOPE", "SÉQUENCES ET ENVELOPPE"), 20, 390);
        var steps = new MultiSlider(16) { Label = L.T("Steps", "Pas"), StepCount = 8, HighlightedIndex = 5 };
        for (int i = 0; i < 16; i++) steps[i] = (Math.Sin(i * 0.6) + 1) / 2;
        c.At(steps, 20, 414, 330, 160);
        var bipolar = new MultiSlider(12) { Label = L.T("Bipolar", "Bipolaire"), IsBipolar = true, DefaultValue = 0.5 };
        for (int i = 0; i < 12; i++) bipolar[i] = 0.5 + 0.4 * Math.Cos(i * 0.9);
        c.At(bipolar, 370, 414, 200, 160);
        c.At(new EnvelopeEditor { Label = L.T("Amplitude envelope", "Enveloppe d'amplitude") }, 590, 360, 240, 214);
        return c;
    }
}
