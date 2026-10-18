// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Dsp;

namespace PluginUI.Gallery.Pages;

/// <summary>Controls for instruments: performance wheels, step sequencer, MSEG, sample view, modulation, A/B, LCD.</summary>
internal static class InstrumentPage
{
    public static UIElement Build(DemoAudio audio)
    {
        var c = new Canvas();

        c.Caption(L.T("WHEELS", "MOLETTES"), 20, 12, 120);
        c.At(new PitchWheel { Label = "PITCH" }, 20, 36, 40, 150);
        c.At(new ModWheel { Label = "MOD", Value = 0.35 }, 76, 36, 40, 150);

        c.Caption(L.T("STEP SEQUENCER", "SÉQUENCEUR PAS À PAS"), 140, 12);
        var seq = c.At(new StepSequencer { StepCount = 16, IsBipolar = true, CurrentStepSource = () => audio.CurrentStep }, 140, 36, 400, 150);
        var random = new Random(3);
        for (int i = 0; i < 16; i++)
        {
            seq.SetValue(i, 0.5 + 0.45 * Math.Sin(i * 0.7) * (i % 3 == 0 ? 1 : 0.5));
            seq.SetGate(i, random.NextDouble() > 0.2);
        }

        c.Caption(L.T("LCD (BITMAP FONT)", "LCD (POLICE BITMAP)"), 560, 12, 180);
        var font = Art.PixelFont(Color.FromRgb(0x7CF7D4));
        c.At(new BitmapText { Font = font, Text = "CUTOFF 1.25K", PixelScale = 2, DrawBackground = true, UnlitCharacter = '8', CellCount = 13 }, 560, 36, 170, 34);
        c.At(new BitmapText { Font = font, TextSource = () => "STEP " + InvariantNumber.Format(audio.CurrentStep + 1, "00") + "/16", PixelScale = 2, DrawBackground = true, UnlitCharacter = '8', CellCount = 13 }, 560, 76, 170, 34);
        c.Caption(L.T("ROTATING BITMAP", "BITMAP PIVOTANT"), 750, 12, 130);
        c.At(new Knob { Label = "DRIVE", Value = 0.65, Skin = new ImageRotarySkin(Art.BitmapRotor()) { MinAngle = -150f, MaxAngle = 150f } }, 760, 36, 72, 96);

        c.Caption(L.T("MULTI-SEGMENT ENVELOPE (MSEG)", "ENVELOPPE MULTI-SEGMENTS (MSEG)"), 20, 200);
        var mseg = c.At(new CurveEditor { PlayPositionSource = () => audio.PlayPosition }, 20, 224, 380, 150);
        mseg.SetPoints([new(0, 0, -0.5), new(0.12, 1, 0.6), new(0.45, 0.55), new(0.75, 0.55, 0.4), new(1, 0)]);

        c.Caption(L.T("SAMPLE (DROP A .WAV)", "ÉCHANTILLON (DÉPOSEZ UN .WAV)"), 420, 200);
        var wave = c.At(new WaveformView { AcceptFileDrop = true, ShowLoop = true, PlayPositionSource = () => audio.PlayPosition }, 420, 224, 440, 150);
        wave.Audio = DemoSample();
        wave.StartPosition = 0.04;
        wave.EndPosition = 0.92;

        c.Caption(L.T("DRAG-AND-DROP MODULATION", "MODULATION PAR GLISSER-DÉPOSER"), 20, 390);
        var lfo = new ModulationSource("LFO 1", Color.FromRgb(0xFFB74D), 0);
        var env = new ModulationSource("ENV 2", Color.FromRgb(0xB388FF), 1);
        c.At(new ModulationSourceButton { Source = lfo }, 20, 416, 80, 24);
        c.At(new ModulationSourceButton { Source = env }, 20, 448, 80, 24);
        string[] names = ["CUTOFF", "RESO", "PAN"];
        for (int i = 0; i < names.Length; i++)
        {
            var knob = c.At(new Knob { Label = names[i], Value = 0.4 + 0.15 * i, AcceptsModulation = true, ModulationAmount = i == 0 ? 0.25 : 0.0 }, 120 + i * 80, 408, 70, 92);
            knob.ModulationDropped += (k, source) =>
            {
                var target = (Knob)k;
                target.ModulationColor = source.Color;
                target.ModulationAmount = target.ModulationAmount == 0 ? 0.3 : -target.ModulationAmount;
            };
        }

        c.Caption(L.T("A/B COMPARISON", "COMPARAISON A/B"), 400, 390);
        var a = new LocalParameter("A");
        var b = new LocalParameter("B");
        c.At(new ABCompareBar { Parameters = [a, b] }, 400, 416, 140, 26);
        c.At(new Knob(a) { Label = "TONE" }, 400, 448, 60, 70);
        c.At(new Knob(b) { Label = "BODY" }, 470, 448, 60, 70);

        c.Caption(L.T("MIDI LEARN (RIGHT-CLICK)", "MIDI LEARN (CLIC DROIT)"), 570, 390);
        var learnParam = new LocalParameter(L.T("Cutoff", "Coupure"));
        c.At(new Slider(Orientation.Horizontal) { Parameter = learnParam, Label = "CUTOFF" }, 570, 416, 200, 40);
        return c;
    }

    /// <summary>Synthetic plucked sample (decaying harmonics), stereo.</summary>
    private static AudioData DemoSample()
    {
        const int rate = 48000, length = rate;
        var left = new float[length];
        var right = new float[length];
        var random = new Random(2);
        for (int i = 0; i < length; i++)
        {
            double t = i / (double)rate, env = Math.Exp(-t * 4.0) * Math.Min(1.0, t * 400.0);
            double s = Math.Sin(2 * Math.PI * 196 * t) + 0.5 * Math.Sin(2 * Math.PI * 392 * t + 0.3) * Math.Exp(-t * 3) + 0.25 * Math.Sin(2 * Math.PI * 588 * t);
            left[i] = (float)(0.55 * env * s + (random.NextDouble() - 0.5) * 0.01);
            right[i] = (float)(0.5 * env * s * (1 - 0.2 * Math.Sin(t * 9)));
        }
        return new AudioData([left, right], rate);
    }
}
