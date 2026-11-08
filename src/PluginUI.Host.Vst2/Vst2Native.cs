// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT
//
// The binary interface of VST 2.4 plug-ins, written from its public description (structure layouts, operation codes
// and flags). VST is a trademark of Steinberg Media Technologies GmbH; no part of the VST SDK is used.

using System.Runtime.InteropServices;

namespace PluginUI.Host.Vst2;

/// <summary>The plug-in object a VST 2 entry point returns (natural alignment; 192 bytes in a 64-bit process).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AEffect
{
    /// <summary>'VstP'.</summary>
    public const int ExpectedMagic = 0x56737450;

    public int Magic;
    public delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint> Dispatcher;
    /// <summary>Accumulating process (before 2.4).</summary>
    public delegate* unmanaged[Cdecl]<AEffect*, float**, float**, int, void> Process;
    public delegate* unmanaged[Cdecl]<AEffect*, int, float, void> SetParameter;
    public delegate* unmanaged[Cdecl]<AEffect*, int, float> GetParameter;
    public int NumPrograms;
    public int NumParams;
    public int NumInputs;
    public int NumOutputs;
    public int Flags;
    public nint Reserved1;
    public nint Reserved2;
    public int InitialDelay;
    public int RealQualities;
    public int OffQualities;
    public float IoRatio;
    public void* Object;
    /// <summary>Free for the host: a handle to the <see cref="Vst2Plugin"/>.</summary>
    public void* User;
    public int UniqueId;
    public int Version;
    public delegate* unmanaged[Cdecl]<AEffect*, float**, float**, int, void> ProcessReplacing;
    public delegate* unmanaged[Cdecl]<AEffect*, double**, double**, int, void> ProcessDoubleReplacing;
    public fixed byte Future[56];
}

/// <summary>AEffect flags.</summary>
internal static class EffectFlags
{
    public const int HasEditor = 1 << 0;
    public const int CanReplacing = 1 << 4;
    public const int ProgramChunks = 1 << 5;
    public const int IsSynth = 1 << 8;
    public const int NoSoundInStop = 1 << 9;
    public const int CanDoubleReplacing = 1 << 12;
}

/// <summary>Operations the host asks of the plug-in (dispatcher).</summary>
internal static class EffectOpcode
{
    public const int Open = 0, Close = 1, SetProgram = 2, GetProgram = 3, SetProgramName = 4, GetProgramName = 5;
    public const int GetParamLabel = 6, GetParamDisplay = 7, GetParamName = 8;
    public const int SetSampleRate = 10, SetBlockSize = 11, MainsChanged = 12;
    public const int EditGetRect = 13, EditOpen = 14, EditClose = 15, EditIdle = 19;
    public const int GetChunk = 23, SetChunk = 24;
    public const int ProcessEvents = 25, CanBeAutomated = 26, String2Parameter = 27, GetProgramNameIndexed = 29;
    public const int GetInputProperties = 33, GetOutputProperties = 34, GetPlugCategory = 35;
    public const int SetBypass = 44, GetEffectName = 45, GetVendorString = 47, GetProductString = 48, GetVendorVersion = 49;
    public const int CanDo = 51, GetTailSize = 52, GetParameterProperties = 56, GetVstVersion = 58;
    public const int BeginSetProgram = 67, EndSetProgram = 68, ShellGetNextPlugin = 70, StartProcess = 71, StopProcess = 72;
    public const int BeginLoadBank = 75, BeginLoadProgram = 76, SetProcessPrecision = 77;
}

/// <summary>Requests of the plug-in to the host (audioMaster callback).</summary>
internal static class HostOpcode
{
    public const int Automate = 0, Version = 1, CurrentId = 2, Idle = 3, GetTime = 7, ProcessEvents = 8, IoChanged = 13;
    public const int SizeWindow = 15, GetSampleRate = 16, GetBlockSize = 17, GetInputLatency = 18, GetOutputLatency = 19;
    public const int GetCurrentProcessLevel = 23, GetAutomationState = 24;
    public const int GetVendorString = 32, GetProductString = 33, GetVendorVersion = 34, CanDo = 37, GetLanguage = 38;
    public const int GetDirectory = 41, UpdateDisplay = 42, BeginEdit = 43, EndEdit = 44;
}

/// <summary>Plug-in categories (effGetPlugCategory).</summary>
public enum Vst2Category
{
    /// <summary>Not reported.</summary>
    Unknown = 0,
    /// <summary>Effect.</summary>
    Effect = 1,
    /// <summary>Instrument.</summary>
    Synth = 2,
    /// <summary>Analyzer.</summary>
    Analysis = 3,
    /// <summary>Mastering.</summary>
    Mastering = 4,
    /// <summary>Spatializer.</summary>
    Spatializer = 5,
    /// <summary>Delay, reverb.</summary>
    RoomFx = 6,
    /// <summary>Surround effect.</summary>
    SurroundFx = 7,
    /// <summary>Restoration.</summary>
    Restoration = 8,
    /// <summary>Offline processing.</summary>
    OfflineProcess = 9,
    /// <summary>A module that holds several plug-ins.</summary>
    Shell = 10,
    /// <summary>Generator (tone, noise).</summary>
    Generator = 11,
}

/// <summary>Header of a list of events (the pointers follow it).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VstEvents
{
    public int NumEvents;
    public nint Reserved;
    // VstEvent* events[NumEvents] follows (declared with 2 entries).
}

/// <summary>A MIDI event (32 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VstMidiEvent
{
    public const int MidiType = 1, SysexType = 6, IsRealtime = 1;

    public int Type;
    public int ByteSize;
    public int DeltaFrames;
    public int Flags;
    public int NoteLength;
    public int NoteOffset;
    public byte Data0, Data1, Data2, Data3;
    public sbyte Detune;
    public byte NoteOffVelocity;
    public byte Reserved1, Reserved2;
}

/// <summary>Position and tempo given to the plug-in (audioMasterGetTime).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VstTimeInfo
{
    public const int TransportChanged = 1, TransportPlaying = 2;
    public const int NanosValid = 1 << 8, PpqPosValid = 1 << 9, TempoValid = 1 << 10, BarsValid = 1 << 11, TimeSigValid = 1 << 13;

    public double SamplePos;
    public double SampleRate;
    public double NanoSeconds;
    public double PpqPos;
    public double Tempo;
    public double BarStartPos;
    public double CycleStartPos;
    public double CycleEndPos;
    public int TimeSigNumerator;
    public int TimeSigDenominator;
    public int SmpteOffset;
    public int SmpteFrameRate;
    public int SamplesToNextClock;
    public int Flags;
}

/// <summary>Editor rectangle.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ERect
{
    public short Top, Left, Bottom, Right;
}

/// <summary>Properties of a parameter (effGetParameterProperties).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VstParameterProperties
{
    public const int IsSwitch = 1, UsesIntegerMinMax = 2, UsesFloatStep = 4, UsesIntStep = 8;

    public float StepFloat, SmallStepFloat, LargeStepFloat;
    public fixed byte Label[64];
    public int Flags;
    public int MinInteger, MaxInteger, StepInteger, LargeStepInteger;
    public fixed byte ShortLabel[8];
    public short DisplayIndex, Category, NumParametersInCategory, Reserved;
    public fixed byte CategoryLabel[24];
    public fixed byte Future[16];
}

/// <summary>Properties of an input or output channel (effGetInputProperties / effGetOutputProperties).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VstPinProperties
{
    public const int IsActive = 1, IsStereo = 2;

    public fixed byte Label[64];
    public int Flags;
    public int ArrangementType;
    public fixed byte ShortLabel[8];
    public fixed byte Future[48];
}

/// <summary>Information given before a preset is loaded (effBeginLoadBank / effBeginLoadProgram).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VstPatchChunkInfo
{
    public int Version;
    public int PluginUniqueId;
    public int PluginVersion;
    public int NumElements;
    public fixed byte Future[48];
}
