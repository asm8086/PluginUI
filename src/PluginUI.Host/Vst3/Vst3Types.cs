// 2026/11/08 13:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst3;

// Binary layout of the VST 3 interfaces used by a host (pluginterfaces of the VST 3 SDK, MIT licensed since 3.8).
// Written from the published interface definitions; natural alignment matches the SDK packing on every platform.

/// <summary>VST 3 result codes (<c>tresult</c>).</summary>
internal static class Vst3Result
{
    public const int Ok = 0;
    public const int True = 0;
    public const int False = 1;
    public static readonly int NoInterface = OperatingSystem.IsWindows() ? unchecked((int)0x80004002) : -1;
    public static readonly int InvalidArgument = OperatingSystem.IsWindows() ? unchecked((int)0x80070057) : 2;
    public static readonly int NotImplemented = OperatingSystem.IsWindows() ? unchecked((int)0x80004001) : 3;
    public static readonly int InternalError = OperatingSystem.IsWindows() ? unchecked((int)0x80004005) : 4;
    public static readonly int NotInitialized = OperatingSystem.IsWindows() ? unchecked((int)0x8000FFFF) : 5;
    public static readonly int OutOfMemory = OperatingSystem.IsWindows() ? unchecked((int)0x8007000E) : 6;
}

/// <summary>
/// 16-byte interface and class identifiers (<c>TUID</c>). Windows uses the COM layout; other platforms store the four
/// 32-bit words big-endian (the SDK's <c>COM_COMPATIBLE</c> switch).
/// </summary>
internal static class Tuid
{
    /// <summary>Identifier from the four words of <c>DECLARE_CLASS_IID</c>.</summary>
    public static Guid From(uint l1, uint l2, uint l3, uint l4)
    {
        Span<byte> b = stackalloc byte[16];
        if (OperatingSystem.IsWindows())
        {
            b[0] = (byte)l1; b[1] = (byte)(l1 >> 8); b[2] = (byte)(l1 >> 16); b[3] = (byte)(l1 >> 24);
            b[4] = (byte)(l2 >> 16); b[5] = (byte)(l2 >> 24); b[6] = (byte)l2; b[7] = (byte)(l2 >> 8);
        }
        else
        {
            b[0] = (byte)(l1 >> 24); b[1] = (byte)(l1 >> 16); b[2] = (byte)(l1 >> 8); b[3] = (byte)l1;
            b[4] = (byte)(l2 >> 24); b[5] = (byte)(l2 >> 16); b[6] = (byte)(l2 >> 8); b[7] = (byte)l2;
        }
        b[8] = (byte)(l3 >> 24); b[9] = (byte)(l3 >> 16); b[10] = (byte)(l3 >> 8); b[11] = (byte)l3;
        b[12] = (byte)(l4 >> 24); b[13] = (byte)(l4 >> 16); b[14] = (byte)(l4 >> 8); b[15] = (byte)l4;
        return MemoryMarshal.Read<Guid>(b);
    }

    /// <summary>The 16 raw bytes, as the plug-in sees them.</summary>
    public static string ToHex(in Guid id)
    {
        Span<byte> b = stackalloc byte[16];
        MemoryMarshal.Write(b, in id);
        return Convert.ToHexString(b);
    }

    /// <summary>Identifier from its 32 hexadecimal digits in raw byte order (as written by <see cref="ToHex"/>).</summary>
    public static Guid FromHex(string hex)
    {
        Span<byte> b = stackalloc byte[16];
        if (hex.Length != 32 || Convert.FromHexString(hex, b, out _, out int written) != System.Buffers.OperationStatus.Done || written != 16)
            throw new FormatException("A VST 3 class identifier has 32 hexadecimal digits.");
        return MemoryMarshal.Read<Guid>(b);
    }

    /// <summary>The name of a known interface, else its 32 hexadecimal digits.</summary>
    public static string NameOf(Guid iid)
    {
        foreach (var field in KnownInterfaces)
        {
            if (field.Iid == iid) return field.Name;
        }
        return ToHex(iid);
    }

    private static (string Name, Guid Iid)[] KnownInterfaces =>
    [
        ("FUnknown", FUnknown), ("IBStream", IBStream), ("ISizeableStream", ISizeableStream), ("IStreamAttributes", IStreamAttributes),
        ("IAttributeList", IAttributeList), ("IMessage", IMessage), ("IHostApplication", IHostApplication),
        ("IComponentHandler", IComponentHandler), ("IComponentHandler2", IComponentHandler2), ("IPlugFrame", IPlugFrame),
    ];

    public static readonly Guid FUnknown = From(0x00000000, 0x00000000, 0xC0000000, 0x00000046);
    public static readonly Guid IPluginBase = From(0x22888DDB, 0x156E45AE, 0x8358B348, 0x08190625);
    public static readonly Guid IPluginFactory = From(0x7A4D811C, 0x52114A1F, 0xAED9D2EE, 0x0B43BF9F);
    public static readonly Guid IPluginFactory2 = From(0x0007B650, 0xF24B4C0B, 0xA464EDB9, 0xF00B2ABB);
    public static readonly Guid IPluginFactory3 = From(0x4555A2AB, 0xC1234E57, 0x9B122910, 0x36878931);
    public static readonly Guid IComponent = From(0xE831FF31, 0xF2D54301, 0x928EBBEE, 0x25697802);
    public static readonly Guid IAudioProcessor = From(0x42043F99, 0xB7DA453C, 0xA569E79D, 0x9AAEC33D);
    public static readonly Guid IEditController = From(0xDCD7BBE3, 0x7742448D, 0xA874AACC, 0x979C759E);
    public static readonly Guid IConnectionPoint = From(0x70A4156F, 0x6E6E4026, 0x989148BF, 0xAA60D8D1);
    public static readonly Guid IPlugView = From(0x5BC32507, 0xD06049EA, 0xA6151B52, 0x2B755B29);
    public static readonly Guid IPlugFrame = From(0x367FAF01, 0xAFA94693, 0x8D4DA2A0, 0xED0882A3);
    public static readonly Guid IPlugViewContentScaleSupport = From(0x65ED9690, 0x8AC44525, 0x8AADEF7A, 0x72EA703F);
    public static readonly Guid IHostApplication = From(0x58E595CC, 0xDB2D4969, 0x8B6AAF8C, 0x36A664E5);
    public static readonly Guid IComponentHandler = From(0x93A0BEA3, 0x0BD045DB, 0x8E890B0C, 0xC1E46AC6);
    public static readonly Guid IComponentHandler2 = From(0xF040B4B3, 0xA36045EC, 0xABCDC045, 0xB4D5A2CC);
    public static readonly Guid IBStream = From(0xC3BF6EA2, 0x30994752, 0x9B6BF990, 0x1EE33E9B);
    public static readonly Guid ISizeableStream = From(0x04F9549E, 0xE02F4E6E, 0x87E86A87, 0x47F4E17F);
    public static readonly Guid IStreamAttributes = From(0xD6CE2FFC, 0xEFAF4B8C, 0x9E74F1BB, 0x12DA44B4);
    public static readonly Guid IParameterChanges = From(0xA4779663, 0x0BB64A56, 0xB44384A8, 0x466FEB9D);
    public static readonly Guid IParamValueQueue = From(0x01263A18, 0xED074F6F, 0x98C9D356, 0x4686F9BA);
    public static readonly Guid IEventList = From(0x3A2C4214, 0x346349FE, 0xB2C4F397, 0xB9695A44);
    public static readonly Guid IMessage = From(0x936F033B, 0xC6C047DB, 0xBB0882F8, 0x13C1E613);
    public static readonly Guid IAttributeList = From(0x1E5F0AEB, 0xCC7F4533, 0xA2544011, 0x38AD5EE4);
    public static readonly Guid IMidiMapping = From(0xDF0FF9F7, 0x49B74669, 0xB63AB732, 0x7ADBF5E5);
    public static readonly Guid IProcessContextRequirements = From(0x2A654303, 0xEF764E3D, 0x95B5FE83, 0x730EF6D0);
}

/// <summary>Fixed-size text fields of the VST 3 structures.</summary>
internal static unsafe class FixedText
{
    /// <summary>Reads a NUL-terminated 8-bit string (UTF-8 in practice).</summary>
    public static string Ascii(byte* text, int capacity)
    {
        int n = 0;
        while (n < capacity && text[n] != 0) n++;
        return System.Text.Encoding.UTF8.GetString(text, n).Trim();
    }

    /// <summary>Reads a NUL-terminated UTF-16 string.</summary>
    public static string Utf16(char* text, int capacity)
    {
        int n = 0;
        while (n < capacity && text[n] != '\0') n++;
        return new string(text, 0, n).Trim();
    }

    /// <summary>Writes a NUL-terminated UTF-16 string, truncated to the capacity.</summary>
    public static void WriteUtf16(char* destination, int capacity, string text)
    {
        int n = Math.Min(text.Length, capacity - 1);
        for (int i = 0; i < n; i++) destination[i] = text[i];
        destination[n] = '\0';
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PFactoryInfo
{
    public fixed byte Vendor[64];
    public fixed byte Url[256];
    public fixed byte Email[128];
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PClassInfo
{
    public Guid Cid;
    public int Cardinality;
    public fixed byte Category[32];
    public fixed byte Name[64];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PClassInfo2
{
    public Guid Cid;
    public int Cardinality;
    public fixed byte Category[32];
    public fixed byte Name[64];
    public uint ClassFlags;
    public fixed byte SubCategories[128];
    public fixed byte Vendor[64];
    public fixed byte Version[64];
    public fixed byte SdkVersion[64];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PClassInfoW
{
    public Guid Cid;
    public int Cardinality;
    public fixed byte Category[32];
    public fixed char Name[64];
    public uint ClassFlags;
    public fixed byte SubCategories[128];
    public fixed char Vendor[64];
    public fixed char Version[64];
    public fixed char SdkVersion[64];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct BusInfoNative
{
    public int MediaType;
    public int Direction;
    public int ChannelCount;
    public fixed char Name[128];
    public int BusType;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessSetup
{
    public int ProcessMode;
    public int SymbolicSampleSize;
    public int MaxSamplesPerBlock;
    public double SampleRate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AudioBusBuffersNative
{
    public int NumChannels;
    public ulong SilenceFlags;
    public void** ChannelBuffers;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ProcessDataNative
{
    public int ProcessMode;
    public int SymbolicSampleSize;
    public int NumSamples;
    public int NumInputs;
    public int NumOutputs;
    public AudioBusBuffersNative* Inputs;
    public AudioBusBuffersNative* Outputs;
    public void* InputParameterChanges;
    public void* OutputParameterChanges;
    public void* InputEvents;
    public void* OutputEvents;
    public ProcessContextNative* ProcessContext;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessContextNative
{
    public uint State;
    public double SampleRate;
    public long ProjectTimeSamples;
    public long SystemTime;
    public long ContinousTimeSamples;
    public double ProjectTimeMusic;
    public double BarPositionMusic;
    public double CycleStartMusic;
    public double CycleEndMusic;
    public double Tempo;
    public int TimeSigNumerator;
    public int TimeSigDenominator;
    public byte ChordKeyNote;
    public byte ChordRootNote;
    public short ChordMask;
    public int SmpteOffsetSubframes;
    public uint FrameRateFramesPerSecond;
    public uint FrameRateFlags;
    public int SamplesToNextClock;

    // ProcessContext::StatesAndFlags
    public const uint Playing = 1 << 1;
    public const uint ProjectTimeMusicValid = 1 << 9;
    public const uint TempoValid = 1 << 10;
    public const uint BarPositionValid = 1 << 11;
    public const uint TimeSigValid = 1 << 13;
    public const uint ContTimeValid = 1 << 17;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ParameterInfoNative
{
    public uint Id;
    public fixed char Title[128];
    public fixed char ShortTitle[128];
    public fixed char Units[128];
    public int StepCount;
    public double DefaultNormalizedValue;
    public int UnitId;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ViewRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

/// <summary>VST 3 event (48 bytes: header, then a union of the event kinds).</summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
internal unsafe struct EventNative
{
    [FieldOffset(0)] public int BusIndex;
    [FieldOffset(4)] public int SampleOffset;
    [FieldOffset(8)] public double PpqPosition;
    [FieldOffset(16)] public ushort Flags;
    [FieldOffset(18)] public ushort Type;

    // NoteOnEvent: channel, pitch, tuning, velocity, length, noteId
    // NoteOffEvent: channel, pitch, velocity, noteId, tuning
    // PolyPressureEvent: channel, pitch, pressure, noteId
    // LegacyMIDICCOutEvent: controlNumber, channel, value, value2
    [FieldOffset(24)] public short Channel;
    [FieldOffset(26)] public short Pitch;
    [FieldOffset(28)] public float Float1;
    [FieldOffset(32)] public float Float2;
    [FieldOffset(36)] public int Int1;
    [FieldOffset(40)] public int Int2;

    [FieldOffset(24)] public byte CcControlNumber;
    [FieldOffset(25)] public sbyte CcChannel;
    [FieldOffset(26)] public sbyte CcValue;
    [FieldOffset(27)] public sbyte CcValue2;

    public const ushort NoteOnType = 0;
    public const ushort NoteOffType = 1;
    public const ushort DataType = 2;
    public const ushort PolyPressureType = 3;
    public const ushort LegacyMidiCcOutType = 65535;

    public const ushort IsLive = 1;
}

/// <summary>Raw vtable calls on VST 3 objects. Every call goes through slot <c>n</c> of the object's first pointer.</summary>
internal static unsafe class Com
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint Slot(void* obj, int index) => (*(nint**)obj)[index];

    public static int QueryInterface(void* obj, in Guid iid, out void* result)
    {
        void* r = null;
        fixed (Guid* p = &iid)
        {
            int hr = ((delegate* unmanaged[MemberFunction]<void*, Guid*, void**, int>)Slot(obj, 0))(obj, p, &r);
            result = hr == Vst3Result.Ok ? r : null;
            return hr;
        }
    }

    public static uint AddRef(void* obj) => ((delegate* unmanaged[MemberFunction]<void*, uint>)Slot(obj, 1))(obj);

    public static uint Release(void* obj) => obj is null ? 0 : ((delegate* unmanaged[MemberFunction]<void*, uint>)Slot(obj, 2))(obj);

    /// <summary>The object as <paramref name="iid"/> (AddRef'd), or null.</summary>
    public static void* As(void* obj, in Guid iid) => obj is not null && QueryInterface(obj, iid, out void* r) == Vst3Result.Ok ? r : null;
}
