// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;

namespace PluginUI.Host.Windows;

/// <summary>Sample formats of ASIO channels (ASIOSampleType).</summary>
internal enum AsioSampleType
{
    Int16Msb = 0,
    Int24Msb = 1,
    Int32Msb = 2,
    Float32Msb = 3,
    Float64Msb = 4,
    Int32Msb16 = 8,
    Int32Msb18 = 9,
    Int32Msb20 = 10,
    Int32Msb24 = 11,
    Int16Lsb = 16,
    Int24Lsb = 17,
    Int32Lsb = 18,
    Float32Lsb = 19,
    Float64Lsb = 20,
    Int32Lsb16 = 24,
    Int32Lsb18 = 25,
    Int32Lsb20 = 26,
    Int32Lsb24 = 27,
}

/// <summary>Converts the samples of ASIO channels from and to 32-bit float (no allocation).</summary>
/// <remarks>
/// MSB formats are big-endian, LSB formats little-endian. "Int32Lsb24" and its kind hold a 24-bit value right-justified
/// in 32 bits. Integer outputs are clipped to full scale; NaN becomes silence.
/// </remarks>
internal static unsafe class AsioSamples
{
    /// <summary><c>true</c> for the formats this class converts (not DSD).</summary>
    public static bool IsSupported(AsioSampleType type) => BytesPerSample(type) > 0;

    /// <summary>Bytes of one sample, 0 for an unsupported format.</summary>
    public static int BytesPerSample(AsioSampleType type) => Kind(type) switch
    {
        0 => 2,
        1 => 3,
        2 or 3 or 8 or 9 or 10 or 11 => 4,
        4 => 8,
        _ => 0,
    };

    /// <summary>Text for the user ("Int32LSB24").</summary>
    public static string Describe(AsioSampleType type) => type.ToString().Replace("Lsb", "LSB", StringComparison.Ordinal).Replace("Msb", "MSB", StringComparison.Ordinal);

    // Formats 0-11 are MSB, 16-27 the same kinds in LSB.
    private static int Kind(AsioSampleType type) => (int)type is >= 0 and <= 11 or >= 16 and <= 27 ? (int)type & 15 : -1;

    private static bool Swap(AsioSampleType type) => (int)type < 16 == BitConverter.IsLittleEndian;

    private static int Bits(int kind) => kind switch { 8 => 16, 9 => 18, 10 => 20, 11 => 24, 0 => 16, 1 => 24, _ => 32 };

    /// <summary>Reads <paramref name="count"/> samples of <paramref name="type"/> into floats.</summary>
    public static void ToFloat(byte* source, AsioSampleType type, float* destination, int count)
    {
        int kind = Kind(type);
        bool swap = Swap(type);
        switch (kind)
        {
            case 0:
            {
                var s = (short*)source;
                for (int i = 0; i < count; i++) destination[i] = (swap ? BinaryPrimitives.ReverseEndianness(s[i]) : s[i]) * (1f / 32768f);
                break;
            }
            case 1:
            {
                bool big = (int)type < 16;
                for (int i = 0; i < count; i++, source += 3)
                {
                    int v = big ? (sbyte)source[0] << 16 | source[1] << 8 | source[2] : (sbyte)source[2] << 16 | source[1] << 8 | source[0];
                    destination[i] = v * (1f / 8388608f);
                }
                break;
            }
            case 3:
            {
                var s = (int*)source;
                for (int i = 0; i < count; i++) destination[i] = BitConverter.Int32BitsToSingle(swap ? BinaryPrimitives.ReverseEndianness(s[i]) : s[i]);
                break;
            }
            case 4:
            {
                var s = (long*)source;
                for (int i = 0; i < count; i++) destination[i] = (float)BitConverter.Int64BitsToDouble(swap ? BinaryPrimitives.ReverseEndianness(s[i]) : s[i]);
                break;
            }
            case 2 or 8 or 9 or 10 or 11:
            {
                var s = (int*)source;
                double scale = 1.0 / (1L << (Bits(kind) - 1));
                for (int i = 0; i < count; i++) destination[i] = (float)((swap ? BinaryPrimitives.ReverseEndianness(s[i]) : s[i]) * scale);
                break;
            }
            default:
                new Span<float>(destination, count).Clear();
                break;
        }
    }

    /// <summary>Writes <paramref name="count"/> floats as samples of <paramref name="type"/>.</summary>
    public static void FromFloat(float* source, AsioSampleType type, byte* destination, int count)
    {
        int kind = Kind(type);
        bool swap = Swap(type);
        switch (kind)
        {
            case 3:
            {
                var d = (int*)destination;
                for (int i = 0; i < count; i++)
                {
                    int bits = BitConverter.SingleToInt32Bits(float.IsNaN(source[i]) ? 0f : source[i]);
                    d[i] = swap ? BinaryPrimitives.ReverseEndianness(bits) : bits;
                }
                break;
            }
            case 4:
            {
                var d = (long*)destination;
                for (int i = 0; i < count; i++)
                {
                    long bits = BitConverter.DoubleToInt64Bits(float.IsNaN(source[i]) ? 0.0 : source[i]);
                    d[i] = swap ? BinaryPrimitives.ReverseEndianness(bits) : bits;
                }
                break;
            }
            case 0:
            {
                var d = (short*)destination;
                for (int i = 0; i < count; i++)
                {
                    short v = (short)Scale(source[i], 32767.0);
                    d[i] = swap ? BinaryPrimitives.ReverseEndianness(v) : v;
                }
                break;
            }
            case 1:
            {
                bool big = (int)type < 16;
                for (int i = 0; i < count; i++, destination += 3)
                {
                    int v = Scale(source[i], 8388607.0);
                    if (big)
                    {
                        destination[0] = (byte)(v >> 16);
                        destination[1] = (byte)(v >> 8);
                        destination[2] = (byte)v;
                    }
                    else
                    {
                        destination[0] = (byte)v;
                        destination[1] = (byte)(v >> 8);
                        destination[2] = (byte)(v >> 16);
                    }
                }
                break;
            }
            case 2 or 8 or 9 or 10 or 11:
            {
                var d = (int*)destination;
                double full = (1L << (Bits(kind) - 1)) - 1;
                for (int i = 0; i < count; i++)
                {
                    int v = Scale(source[i], full);
                    d[i] = swap ? BinaryPrimitives.ReverseEndianness(v) : v;
                }
                break;
            }
            default:
                // Unknown format: the bytes of a silent block cannot be known, leave the driver's buffer as it is.
                break;
        }
    }

    private static int Scale(float x, double full)
    {
        if (float.IsNaN(x)) return 0;
        double v = Math.Clamp((double)x, -1.0, 1.0) * full;
        return (int)Math.Round(v);
    }
}
