// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;

namespace PluginUI.Dsp;

/// <summary>Decoded audio: one float array per channel (−1..1) and the sample rate.</summary>
/// <param name="Channels">Samples per channel, all of the same length.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
public sealed record AudioData(float[][] Channels, int SampleRate)
{
    /// <summary>Number of frames (samples per channel).</summary>
    public int Length => Channels.Length == 0 ? 0 : Channels[0].Length;

    /// <summary>Duration in seconds.</summary>
    public double Duration => SampleRate <= 0 ? 0.0 : Length / (double)SampleRate;
}

/// <summary>
/// Minimal WAV (RIFF / RIFX-free) reader for previews in <see cref="WaveformView"/>: PCM 8, 16, 24 and 32 bit,
/// IEEE float 32 and 64 bit, WAVE_FORMAT_EXTENSIBLE, any channel count. Not meant for the audio thread
/// (allocates the decoded data).
/// </summary>
public static class WavReader
{
    /// <summary>Reads a WAV file.</summary>
    /// <param name="path">File path.</param>
    /// <returns>The decoded audio.</returns>
    /// <exception cref="InvalidDataException">The file is not a supported WAV file.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static AudioData Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Decodes WAV bytes.</summary>
    /// <param name="data">The complete file content.</param>
    /// <returns>The decoded audio.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported WAV file.</exception>
    public static AudioData Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        ReadOnlySpan<byte> samples = default;
        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            int size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4)), (uint)(data.Length - pos - 8));
            var chunk = data.Slice(pos + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
                if (format == 0xFFFE && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]); // sub-format GUID
            }
            else if (id.SequenceEqual("data"u8))
            {
                samples = chunk;
            }
            pos += 8 + size + (size & 1);
        }
        if (channels <= 0 || sampleRate <= 0 || samples.IsEmpty && format == 0) throw new InvalidDataException("Missing fmt or data chunk.");
        bool isFloat = format == 3;
        if (!(format == 1 || isFloat)) throw new InvalidDataException($"Unsupported WAV encoding (format tag {format}).");
        if (isFloat ? bits is not (32 or 64) : bits is not (8 or 16 or 24 or 32)) throw new InvalidDataException($"Unsupported sample size ({bits} bits).");
        int bytes = bits / 8, frame = bytes * channels;
        int frames = samples.Length / frame;
        var result = new float[channels][];
        for (int c = 0; c < channels; c++) result[c] = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            for (int c = 0; c < channels; c++)
            {
                var s = samples.Slice(i * frame + c * bytes, bytes);
                result[c][i] = (isFloat, bits) switch
                {
                    (true, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
                    (true, _) => (float)BinaryPrimitives.ReadDoubleLittleEndian(s),
                    (false, 8) => (s[0] - 128) / 128f,
                    (false, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (false, 24) => ((s[0] | (s[1] << 8) | (s[2] << 16)) << 8 >> 8) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                };
            }
        }
        return new AudioData(result, sampleRate);
    }
}

/// <summary>
/// Multi-resolution min/max summary of a long signal, so that a waveform of millions of samples is drawn at any zoom
/// level by reading at most a few values per pixel (used by <see cref="WaveformView"/>).
/// </summary>
/// <remarks>
/// Level k stores the minimum and maximum of blocks of <c>BaseBlock × 4^k</c> samples. Building is O(n) and memory is
/// about 2 × n / <see cref="BaseBlock"/> × 4/3 floats.
/// </remarks>
public sealed class WaveformPeaks
{
    /// <summary>Samples per block of the finest level.</summary>
    public const int BaseBlock = 64;

    private readonly float[] _samples;
    private readonly List<(float[] Min, float[] Max, int Block)> _levels = [];

    /// <summary>Builds the summary of one channel.</summary>
    /// <param name="samples">The samples (kept by reference: do not modify them afterwards).</param>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is <c>null</c>.</exception>
    public WaveformPeaks(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _samples = samples;
        int block = BaseBlock;
        int count = (samples.Length + block - 1) / block;
        var min = new float[count];
        var max = new float[count];
        for (int b = 0; b < count; b++)
        {
            var (lo, hi) = VectorOps.MinMax(samples.AsSpan(b * block, Math.Min(block, samples.Length - b * block)));
            min[b] = lo;
            max[b] = hi;
        }
        _levels.Add((min, max, block));
        while (count > 4)
        {
            int next = (count + 3) / 4;
            var nmin = new float[next];
            var nmax = new float[next];
            for (int b = 0; b < next; b++)
            {
                int from = b * 4, len = Math.Min(4, count - from);
                nmin[b] = VectorOps.MinMax(min.AsSpan(from, len)).Min;
                nmax[b] = VectorOps.Max(max.AsSpan(from, len));
            }
            block *= 4;
            _levels.Add((nmin, nmax, block));
            (min, max, count) = (nmin, nmax, next);
        }
    }

    /// <summary>Number of samples summarized.</summary>
    public int Length => _samples.Length;

    /// <summary>Minimum and maximum of the samples in [<paramref name="start"/>, <paramref name="end"/>).</summary>
    /// <param name="start">First sample (clamped).</param>
    /// <param name="end">End sample, exclusive (clamped).</param>
    /// <returns>(min, max), or (0, 0) for an empty range.</returns>
    public (float Min, float Max) Range(long start, long end)
    {
        start = Math.Clamp(start, 0, _samples.Length);
        end = Math.Clamp(end, start, _samples.Length);
        long length = end - start;
        if (length <= 0) return (0f, 0f);
        // Use the coarsest level whose blocks are at most a quarter of the range, plus exact samples at the edges.
        int level = -1;
        for (int k = _levels.Count - 1; k >= 0; k--)
        {
            if (_levels[k].Block * 4L <= length) { level = k; break; }
        }
        if (level < 0) return VectorOps.MinMax(_samples.AsSpan((int)start, (int)length));
        var (mins, maxs, block) = _levels[level];
        long firstBlock = (start + block - 1) / block, lastBlock = end / block; // full blocks [firstBlock, lastBlock)
        // length ≥ 4 blocks guarantees at least 3 full blocks inside the range.
        if (lastBlock <= firstBlock) return VectorOps.MinMax(_samples.AsSpan((int)start, (int)length));
        float lo = VectorOps.MinMax(mins.AsSpan((int)firstBlock, (int)(lastBlock - firstBlock))).Min;
        float hi = VectorOps.Max(maxs.AsSpan((int)firstBlock, (int)(lastBlock - firstBlock)));
        long headEnd = Math.Min(end, firstBlock * block), tailStart = Math.Max(headEnd, lastBlock * block);
        if (headEnd > start) Merge(_samples.AsSpan((int)start, (int)(headEnd - start)), ref lo, ref hi);
        if (end > tailStart) Merge(_samples.AsSpan((int)tailStart, (int)(end - tailStart)), ref lo, ref hi);
        return (lo, hi);
    }

    private static void Merge(ReadOnlySpan<float> span, ref float lo, ref float hi)
    {
        var (a, b) = VectorOps.MinMax(span);
        if (a < lo) lo = a;
        if (b > hi) hi = b;
    }
}
