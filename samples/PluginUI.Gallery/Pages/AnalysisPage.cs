// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Dsp;

namespace PluginUI.Gallery.Pages;

/// <summary>Mixing and analysis displays: EQ, compressor, loudness, stereo image, spectrogram, history.</summary>
internal static class AnalysisPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();

        c.Caption(L.T("PARAMETRIC EQ (DRAG, WHEEL = Q, RIGHT-CLICK)", "ÉGALISEUR PARAMÉTRIQUE (GLISSER, MOLETTE = Q, CLIC DROIT)"), 20, 12, 460);
        var eq = c.At(new ParametricEqEditor { AllowAddBands = true, SpectrumSource = audio.Ring }, 20, 36, 470, 210);
        eq.Bands.Add(new EqBand(EqBandType.LowCut, 40, 0, 0.7071));
        eq.Bands.Add(new EqBand(EqBandType.LowShelf, 140, 3, 0.7071));
        eq.Bands.Add(new EqBand(EqBandType.Bell, 900, -4.5, 1.8));
        eq.Bands.Add(new EqBand(EqBandType.Bell, 3200, 2.5, 1.0));
        eq.Bands.Add(new EqBand(EqBandType.HighShelf, 9000, 4, 0.7071));

        c.Caption(L.T("COMPRESSOR", "COMPRESSEUR"), 510, 12);
        c.At(new CompressorCurve { InputLevelSource = audio.CompressorInput }, 510, 36, 190, 190);
        c.Caption(L.T("GAIN REDUCTION", "RÉDUCTION DE GAIN"), 720, 12);
        c.At(new HistoryGraph { PeakSource = audio.GainReduction, Minimum = 0, Maximum = 18, FillFromTop = true, Unit = " dB", TraceColor = Color.FromRgb(0xFF7043) }, 720, 36, 150, 90);
        c.Caption(L.T("CORRELATION", "CORRÉLATION"), 720, 140);
        c.At(new CorrelationMeter { LeftSource = audio.Left, RightSource = audio.Right }, 720, 164, 150, 24);

        c.Caption(L.T("EBU R128 LOUDNESS", "SONIE EBU R128"), 20, 262, 200);
        c.At(new LoudnessMeter { Analyzer = audio.Loudness, TargetLufs = -14f }, 20, 286, 200, 200);

        c.Caption(L.T("GONIOMETER", "GONIOMÈTRE"), 240, 262, 200);
        c.At(new Goniometer { LeftSource = audio.Left, RightSource = audio.Right }, 240, 286, 200, 200);

        c.Caption(L.T("SPECTROGRAM", "SPECTROGRAMME"), 460, 262);
        c.At(new Spectrogram { Source = audio.Ring, MinFrequency = 40f, ColumnsPerSecond = 40f }, 460, 286, 410, 200);
        return c;
    }
}
