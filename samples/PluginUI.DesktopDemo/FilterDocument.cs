// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PluginUI.Dsp;

namespace PluginUI.DesktopDemo;

/// <summary>One biquad section of the design, as parameters that the controls and the graph edit directly.</summary>
internal sealed class Band
{
    public static readonly EqBandType[] Types = [EqBandType.Bell, EqBandType.LowShelf, EqBandType.HighShelf, EqBandType.LowCut, EqBandType.HighCut, EqBandType.Notch, EqBandType.BandPass];

    public Band(int index, Color color, EqBandType type, double frequency, double gain, double q, bool enabled)
    {
        Index = index;
        Color = color;
        string n = (index + 1).ToString(CultureInfo.InvariantCulture);
        Enabled = LocalParameter.Toggle(L.T("Band ", "Bande ") + n, enabled);
        Type = LocalParameter.FromList(L.T("Type ", "Type ") + n, [.. Types.Select(TypeName)], Array.IndexOf(Types, type));
        Frequency = new LocalParameter(L.T("Frequency ", "Fréquence ") + n, defaultValue: frequency, units: "Hz", scale: ParameterScale.Logarithmic(10.0, 22000.0), decimals: 0);
        Gain = new LocalParameter(L.T("Gain ", "Gain ") + n, -24.0, 24.0, gain, "dB", decimals: 1);
        Q = new LocalParameter("Q " + n, defaultValue: q, scale: ParameterScale.Logarithmic(0.1, 24.0), decimals: 2);
    }

    public int Index { get; }

    public Color Color { get; }

    public LocalParameter Enabled { get; }

    public LocalParameter Type { get; }

    public LocalParameter Frequency { get; }

    public LocalParameter Gain { get; }

    public LocalParameter Q { get; }

    public IEnumerable<LocalParameter> Parameters => [Enabled, Type, Frequency, Gain, Q];

    public bool IsEnabled => Enabled.SelectedIndex == 1;

    public EqBandType FilterType => Types[Math.Clamp(Type.SelectedIndex, 0, Types.Length - 1)];

    public bool HasGain => FilterType is EqBandType.Bell or EqBandType.LowShelf or EqBandType.HighShelf;

    public BiquadDesign.Coefficients Coefficients(double sampleRate)
        => IsEnabled ? BiquadDesign.Design(FilterType, Frequency.Value, HasGain ? Gain.Value : 0.0, Q.Value, sampleRate) : BiquadDesign.Coefficients.Identity;

    public static string TypeName(EqBandType type) => type switch
    {
        EqBandType.Bell => L.T("Bell", "Cloche"),
        EqBandType.LowShelf => L.T("Low shelf", "Plateau grave"),
        EqBandType.HighShelf => L.T("High shelf", "Plateau aigu"),
        EqBandType.LowCut => L.T("Low cut", "Coupe-bas"),
        EqBandType.HighCut => L.T("High cut", "Coupe-haut"),
        EqBandType.Notch => L.T("Notch", "Réjection"),
        _ => L.T("Band pass", "Passe-bande"),
    };
}

/// <summary>The document: a cascade of four biquads at a sample rate, saved as JSON (.bqd).</summary>
internal sealed class FilterDocument
{
    public static readonly double[] SampleRates = [44100.0, 48000.0, 88200.0, 96000.0, 192000.0];

    private static readonly Color[] Colors = [Color.FromRgb(0xFF7043), Color.FromRgb(0x26C6DA), Color.FromRgb(0x9CCC65), Color.FromRgb(0xBA68C8)];

    public FilterDocument()
    {
        Bands =
        [
            new Band(0, Colors[0], EqBandType.LowCut, 40.0, 0.0, 0.7071, true),
            new Band(1, Colors[1], EqBandType.Bell, 250.0, -3.0, 1.0, true),
            new Band(2, Colors[2], EqBandType.Bell, 2500.0, 4.0, 1.4, true),
            new Band(3, Colors[3], EqBandType.HighShelf, 9000.0, 2.0, 0.7071, false),
        ];
        SampleRate = LocalParameter.FromList(L.T("Sample rate", "Fréquence d'échantillonnage"), [.. SampleRates.Select(r => (r / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz")], 1);
        foreach (var p in AllParameters) p.Changed += _ => Version++;
    }

    public IReadOnlyList<Band> Bands { get; }

    public LocalParameter SampleRate { get; }

    /// <summary>Incremented on every change (repaints the graph and the coefficients).</summary>
    public int Version { get; private set; }

    public double Fs => SampleRates[Math.Clamp(SampleRate.SelectedIndex, 0, SampleRates.Length - 1)];

    public IEnumerable<LocalParameter> AllParameters => Bands.SelectMany(b => b.Parameters).Append(SampleRate);

    /// <summary>Puts every parameter back to its default value.</summary>
    public void Reset()
    {
        foreach (var p in AllParameters) p.NormalizedValue = p.DefaultNormalizedValue;
    }

    // ---------------------------------------------------------------- response

    public Complex Response(Band band, double hz) => Evaluate(band.Coefficients(Fs), hz, Fs);

    public Complex CascadeResponse(double hz)
    {
        Complex h = Complex.One;
        foreach (var band in Bands) h *= Response(band, hz);
        return h;
    }

    public static double Db(Complex h) => 20.0 * Math.Log10(Math.Max(h.Magnitude, 1e-12));

    /// <summary>Unwrapped phase of the cascade in radians (sum of the phases of each zero and pole factor).</summary>
    public double CascadePhase(double hz)
    {
        double w = 2.0 * Math.PI * hz / Fs, phi = 0.0;
        Complex e = Complex.FromPolarCoordinates(1.0, -w);
        foreach (var band in Bands)
        {
            var k = band.Coefficients(Fs);
            foreach (var r in Roots(k.B1 / k.B0, k.B2 / k.B0)) phi += FactorPhase(r, e, w);
            foreach (var r in Roots(k.A1, k.A2)) phi -= FactorPhase(r, e, w);
        }
        return phi;
    }

    private static Complex Evaluate(in BiquadDesign.Coefficients k, double hz, double fs)
    {
        Complex x = Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * hz / fs), x2 = x * x;
        return (k.B0 + k.B1 * x + k.B2 * x2) / (1.0 + k.A1 * x + k.A2 * x2);
    }

    private static double FactorPhase(Complex r, Complex e, double w) => r.Magnitude <= 1.0
        ? (1.0 - r * e).Phase
        : (-r).Phase - w + (1.0 - Complex.Conjugate(e) / r).Phase;

    // Roots in z of z² + b·z + c.
    private static Complex[] Roots(double b, double c)
    {
        Complex sq = Complex.Sqrt(b * b - 4.0 * c);
        return [(-b + sq) / 2.0, (-b - sq) / 2.0];
    }

    // ---------------------------------------------------------------- coefficients text

    /// <summary>Coefficients of the enabled sections, one block per section (also what Copy puts on the clipboard).</summary>
    public string CoefficientsText()
    {
        var sb = new StringBuilder();
        sb.Append("// fs = ").Append(Fs.ToString("0", CultureInfo.InvariantCulture)).Append(" Hz, a0 = 1").AppendLine();
        foreach (var band in Bands)
        {
            if (!band.IsEnabled) continue;
            var k = band.Coefficients(Fs);
            sb.Append("// ").Append(band.Index + 1).Append(": ").Append(Band.TypeName(band.FilterType)).Append(' ')
              .Append(band.Frequency.Value.ToString("0", CultureInfo.InvariantCulture)).AppendLine(" Hz");
            Line(sb, "b0", k.B0);
            Line(sb, "b1", k.B1);
            Line(sb, "b2", k.B2);
            Line(sb, "a1", k.A1);
            Line(sb, "a2", k.A2);
        }
        return sb.ToString().TrimEnd();
    }

    private static void Line(StringBuilder sb, string name, double value)
        => sb.Append(name).Append(" = ").Append(value.ToString(value < 0 ? "0.000000000000" : " 0.000000000000", CultureInfo.InvariantCulture)).AppendLine();

    // ---------------------------------------------------------------- file

    public bool Load(string path)
    {
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize(stream, DemoJson.Default.FilterFile) ?? throw new InvalidDataException("Empty file.");
        if (file.Format != FilterFile.CurrentFormat) throw new InvalidDataException($"Unknown format '{file.Format}'.");
        int rate = Array.IndexOf(SampleRates, file.SampleRate);
        SampleRate.SelectedIndex = rate >= 0 ? rate : 1;
        for (int i = 0; i < Bands.Count; i++)
        {
            var band = Bands[i];
            var saved = i < file.Bands.Count ? file.Bands[i] : null;
            band.Enabled.SelectedIndex = saved?.Enabled == true ? 1 : 0;
            if (saved is null) continue;
            int type = Array.IndexOf(Band.Types, saved.Type);
            band.Type.SelectedIndex = type >= 0 ? type : 0;
            band.Frequency.Value = saved.Frequency;
            band.Gain.Value = saved.Gain;
            band.Q.Value = saved.Q;
        }
        return true;
    }

    public bool Save(string path)
    {
        var file = new FilterFile
        {
            SampleRate = Fs,
            Bands = [.. Bands.Select(b => new FilterFile.Section { Enabled = b.IsEnabled, Type = b.FilterType, Frequency = Math.Round(b.Frequency.Value, 3), Gain = Math.Round(b.Gain.Value, 3), Q = Math.Round(b.Q.Value, 4) })],
        };
        string temp = path + ".tmp";
        using (var stream = File.Create(temp)) JsonSerializer.Serialize(stream, file, DemoJson.Default.FilterFile);
        File.Move(temp, path, overwrite: true);
        return true;
    }
}

/// <summary>JSON shape of a .bqd file.</summary>
internal sealed class FilterFile
{
    public const string CurrentFormat = "pluginui-biquad-1";

    public string Format { get; set; } = CurrentFormat;

    public double SampleRate { get; set; } = 48000.0;

    public List<Section> Bands { get; set; } = new();

    internal sealed class Section
    {
        public bool Enabled { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter<EqBandType>))]
        public EqBandType Type { get; set; }

        public double Frequency { get; set; }

        public double Gain { get; set; }

        public double Q { get; set; }
    }
}

/// <summary>Settings of the application (View menu, language), saved in %APPDATA%\asm8086\Biquad Designer.</summary>
internal sealed class DemoSettings
{
    public bool ShowPhase { get; set; } = true;

    public bool ShowBands { get; set; } = true;

    public bool LightTheme { get; set; }

    public string Language { get; set; } = "auto";
}

[JsonSerializable(typeof(FilterFile))]
[JsonSerializable(typeof(DemoSettings))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DemoJson : JsonSerializerContext;
