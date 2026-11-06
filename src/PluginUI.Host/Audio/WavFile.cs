// 2026/11/06 22:50:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;

namespace PluginUI.Host.Audio;

/// <summary>Sample formats of WAV files.</summary>
public enum WavSampleFormat
{
    /// <summary>16-bit integer.</summary>
    Int16,
    /// <summary>24-bit integer.</summary>
    Int24,
    /// <summary>32-bit integer.</summary>
    Int32,
    /// <summary>32-bit float.</summary>
    Float32,
    /// <summary>64-bit float.</summary>
    Float64,
}

/// <summary>Audio held in memory: one float array per channel.</summary>
/// <param name="SampleRate">Sample rate.</param>
/// <param name="Channels">Samples, one array per channel (all the same length).</param>
public sealed record AudioBuffer(double SampleRate, float[][] Channels)
{
    /// <summary>Number of channels.</summary>
    public int ChannelCount => Channels.Length;

    /// <summary>Number of frames.</summary>
    public int Length => Channels.Length == 0 ? 0 : Channels[0].Length;

    /// <summary>Duration in seconds.</summary>
    public double Duration => SampleRate > 0 ? Length / SampleRate : 0.0;

    /// <summary>Silent buffer.</summary>
    public static AudioBuffer Create(double sampleRate, int channels, int length)
        => new(sampleRate, Enumerable.Range(0, channels).Select(_ => new float[length]).ToArray());

    /// <summary>Largest absolute sample.</summary>
    public float Peak()
    {
        float peak = 0f;
        foreach (var channel in Channels)
        {
            foreach (float v in channel) peak = Math.Max(peak, Math.Abs(v));
        }
        return peak;
    }
}

/// <summary>Reads and writes RIFF/WAVE files (PCM 16/24/32 bits, IEEE float 32/64 bits, WAVE_FORMAT_EXTENSIBLE).</summary>
public static class WavFile
{
    private const ushort FormatPcm = 1, FormatFloat = 3, FormatExtensible = 0xFFFE;

    /// <summary>Reads a WAV file.</summary>
    /// <exception cref="FormatException">Not a supported WAV file.</exception>
    public static AudioBuffer Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Reads a WAV image.</summary>
    /// <exception cref="FormatException">Not a supported WAV file.</exception>
    public static AudioBuffer Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8))
            throw new FormatException("Not a RIFF/WAVE file.");
        int position = 12;
        ushort format = 0, channels = 0, bits = 0;
        uint rate = 0;
        ReadOnlySpan<byte> samples = default;
        bool haveFormat = false, haveData = false;
        while (position + 8 <= data.Length)
        {
            var id = data.Slice(position, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            int body = position + 8;
            long available = Math.Min(size, data.Length - body);
            if (id.SequenceEqual("fmt "u8) && available >= 16)
            {
                var fmt = data.Slice(body, (int)available);
                format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                if (format == FormatExtensible && fmt.Length >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]);
                haveFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                samples = data.Slice(body, (int)available);
                haveData = true;
            }
            position = body + (int)Math.Min(size + (size & 1), int.MaxValue - body);
        }
        if (!haveFormat || !haveData) throw new FormatException("The WAV file has no fmt or data chunk.");
        if (channels == 0 || rate == 0) throw new FormatException("Invalid WAV format.");

        WavSampleFormat sampleFormat = (format, bits) switch
        {
            (FormatPcm, 16) => WavSampleFormat.Int16,
            (FormatPcm, 24) => WavSampleFormat.Int24,
            (FormatPcm, 32) => WavSampleFormat.Int32,
            (FormatFloat, 32) => WavSampleFormat.Float32,
            (FormatFloat, 64) => WavSampleFormat.Float64,
            _ => throw new FormatException($"Unsupported WAV sample format (format {format}, {bits} bits)."),
        };
        int bytes = bits / 8;
        int frames = samples.Length / (bytes * channels);
        var result = AudioBuffer.Create(rate, channels, frames);
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                var s = samples.Slice((f * channels + c) * bytes, bytes);
                result.Channels[c][f] = sampleFormat switch
                {
                    WavSampleFormat.Int16 => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    WavSampleFormat.Int24 => ((s[0] | (s[1] << 8) | ((sbyte)s[2] << 16))) / 8388608f,
                    WavSampleFormat.Int32 => (float)(BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648.0),
                    WavSampleFormat.Float32 => BinaryPrimitives.ReadSingleLittleEndian(s),
                    _ => (float)BinaryPrimitives.ReadDoubleLittleEndian(s),
                };
            }
        }
        return result;
    }

    /// <summary>Writes a WAV file.</summary>
    public static void Write(string path, AudioBuffer audio, WavSampleFormat format = WavSampleFormat.Float32)
        => File.WriteAllBytes(path, Write(audio, format));

    /// <summary>WAV image of <paramref name="audio"/>. Integer formats clip at ±1 (TPDF dither for 16 and 24 bits).</summary>
    public static byte[] Write(AudioBuffer audio, WavSampleFormat format = WavSampleFormat.Float32)
    {
        ArgumentNullException.ThrowIfNull(audio);
        int channels = audio.ChannelCount, frames = audio.Length;
        (ushort tag, int bits) = format switch
        {
            WavSampleFormat.Int16 => (FormatPcm, 16),
            WavSampleFormat.Int24 => (FormatPcm, 24),
            WavSampleFormat.Int32 => (FormatPcm, 32),
            WavSampleFormat.Float32 => (FormatFloat, 32),
            _ => (FormatFloat, 64),
        };
        int bytes = bits / 8;
        int dataSize = frames * channels * bytes;
        bool extensible = channels > 2;
        int fmtSize = extensible ? 40 : 16;
        var result = new byte[12 + 8 + fmtSize + (tag == FormatFloat ? 12 : 0) + 8 + dataSize + (dataSize & 1)];
        var span = result.AsSpan();
        int p = 0;
        void Text(string s) { Encoding.ASCII.GetBytes(s, result.AsSpan(p, 4)); p += 4; }
        void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(p), v); p += 4; }
        void U16(ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(p), v); p += 2; }

        Text("RIFF"); U32((uint)(result.Length - 8)); Text("WAVE");
        Text("fmt "); U32((uint)fmtSize);
        U16(extensible ? FormatExtensible : tag); U16((ushort)channels); U32((uint)audio.SampleRate);
        U32((uint)(audio.SampleRate * channels * bytes)); U16((ushort)(channels * bytes)); U16((ushort)bits);
        if (extensible)
        {
            U16(22); U16((ushort)bits); U32(channels >= 32 ? 0 : (uint)((1L << channels) - 1));
            U16(tag); U16(0x0000); U32(0x00100000); U32(0xAA000080); U32(0x719B3800); // KSDATAFORMAT_SUBTYPE GUID tail
        }
        if (tag == FormatFloat)
        {
            Text("fact"); U32(4); U32((uint)frames);
        }
        Text("data"); U32((uint)dataSize);
        var rng = new Random(0x5EED);
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                float v = audio.Channels[c][f];
                var s = span.Slice(p, bytes);
                switch (format)
                {
                    case WavSampleFormat.Int16:
                        BinaryPrimitives.WriteInt16LittleEndian(s, (short)Quantize(v, 32767.0, rng));
                        break;
                    case WavSampleFormat.Int24:
                    {
                        int i = (int)Quantize(v, 8388607.0, rng);
                        s[0] = (byte)i; s[1] = (byte)(i >> 8); s[2] = (byte)(i >> 16);
                        break;
                    }
                    case WavSampleFormat.Int32:
                        BinaryPrimitives.WriteInt32LittleEndian(s, (int)Math.Clamp(Math.Round(v * 2147483647.0), int.MinValue, int.MaxValue));
                        break;
                    case WavSampleFormat.Float32:
                        BinaryPrimitives.WriteSingleLittleEndian(s, v);
                        break;
                    default:
                        BinaryPrimitives.WriteDoubleLittleEndian(s, v);
                        break;
                }
                p += bytes;
            }
        }
        return result;
    }

    private static double Quantize(float v, double scale, Random rng)
    {
        double dither = rng.NextDouble() - rng.NextDouble(); // TPDF, ±1 LSB
        return Math.Clamp(Math.Round(v * scale + dither), -scale - 1, scale);
    }
}
