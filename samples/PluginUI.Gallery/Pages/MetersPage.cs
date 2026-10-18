// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Gallery.Pages;

/// <summary>Meters, scope, analyzer, plot and keyboard, fed by <see cref="DemoAudio"/>.</summary>
internal static class MetersPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();
        c.Caption(L.T("LEVEL METERS", "VU-MÈTRES"), 20, 12);
        c.At(new LevelMeter { Sources = audio.NewPeakSources(), RmsSources = audio.NewRmsSources(), ToolTip = L.T("Peak + RMS, peak hold, click to clear the clip indicator", "Crête + RMS, maintien de crête, clic pour effacer l'écrêtage") }, 20, 36, 54, 240);
        c.At(new LevelMeter { MeterStyle = MeterStyle.Segments, Sources = audio.NewPeakSources() }, 84, 36, 54, 240);
        c.At(new LevelMeter { Orientation = Orientation.Horizontal, Sources = audio.NewPeakSources(), RmsSources = audio.NewRmsSources() }, 160, 36, 260, 40);
        var gr = c.At(new LevelMeter { Mode = MeterMode.GainReduction, ChannelCount = 1, InputUnit = LevelUnit.Decibels, Label = "GR" }, 150, 96, 44, 180);
        gr.SetLevels(6f);
        c.At(new Led { IsOn = true }, 210, 100, 16, 16);
        c.At(new Led { Brightness = 0.5f, Color = Color.FromRgb(0x3DD68C) }, 236, 100, 16, 16);
        c.At(new Led { IsOn = false }, 262, 100, 16, 16);
        c.At(new Led { IsOn = true, Shape = LedShape.Square, Color = Color.FromRgb(0xFF5252) }, 288, 100, 16, 16);
        c.At(new ProgressBar { Value = 0.62, ShowText = true }, 210, 132, 210, 16);
        c.At(new ProgressBar { IsIndeterminate = true }, 210, 156, 210, 8);
        c.At(new FpsCounter(), 330, 98, 90, 18);

        c.Caption("OSCILLOSCOPE", 20, 290);
        c.At(new Oscilloscope { Source = audio.Ring, WindowSamples = 800, Filled = true, Gain = 1.6f }, 20, 314, 400, 150);

        c.Caption(L.T("ANALYZER", "ANALYSEUR"), 440, 12);
        c.At(new SpectrumAnalyzer { Source = audio.Ring, ShowPeakHold = true, FftSize = 4096 }, 440, 36, 420, 200);
        c.At(new SpectrumAnalyzer { Source = audio.Ring, Mode = SpectrumMode.Bars, FftSize = 4096 }, 440, 250, 420, 120);

        c.Caption(L.T("CURVE (EQ)", "COURBE (ÉGALISEUR)"), 440, 384);
        var plot = c.At(new Plot { XMin = 20, XMax = 20000, LogX = true, YMin = -18, YMax = 18 }, 440, 408, 420, 110);
        plot.Add(f => 9 * Math.Exp(-Math.Pow(Math.Log(f / 120) * 1.6, 2)) - 6 * Math.Exp(-Math.Pow(Math.Log(f / 3000) * 2.2, 2)), null, fill: true);
        plot.Add(f => -12 / (1 + Math.Pow(f / 12000, -4)), Color.FromRgb(0xFFB74D));

        c.Caption(L.T("KEYBOARD", "CLAVIER"), 20, 478);
        audio.Keyboard = c.At(new PianoKeyboard { LowestNote = 36, HighestNote = 96 }, 20, 502, 400, 72);
        return c;
    }
}
