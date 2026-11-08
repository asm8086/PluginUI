// 2026/11/08 17:00:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT
//
// Compiled into the host tests (hosted through its entry point) and into TestVst2, a real VST 2 module published with
// NativeAOT (dotnet publish -r win-x64 / linux-x64) to test loading .dll / .so files.

using System.Runtime.InteropServices;
using System.Text;
using PluginUI.Host.Vst2;

namespace PluginUI.Host.Tests;

/// <summary>
/// A VST 2 plug-in written in C# in the test process: an entry point, an AEffect and its dispatcher. A gain effect
/// (gain, mode), three programs, chunks, an editor, MIDI echo (notes an octave up), and a shell mode with two plug-ins.
/// </summary>
internal static unsafe class FakeVst2
{
    public const int GainId = 0x466B476E;   // 'FkGn'
    public const int ShellAId = 0x53684141;  // 'ShAA'
    public const int ShellBId = 0x53684242;  // 'ShBB'

    public static bool ShellMode { get; set; }
    public static State? Last;

    public static nint EntryPoint => (nint)(delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint>, AEffect*>)&Entry;

    internal sealed class State
    {
        public delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint> Host;
        public int HostVersion, Id, Program, Opens, StartProcess, EditCloses, ShellIndex, ResizeAnswer = -1;
        public bool IsShell, Active, WantResize;
        public string Name = string.Empty;
        public float[] Params = [0.5f, 0f];
        public string[] ProgramNames = ["Init", "Warm", "Bright"];
        public double Rate, Tempo, Ppq;
        public nint Parent;
        public int TimeFlags;
        public List<(int Delta, byte Status, byte Note, byte Velocity)> Received = [];
        public ERect* Rect;
        public byte* ChunkMemory;
    }

    private static State S(AEffect* e) => (State)GCHandle.FromIntPtr((nint)e->Object).Target!;

    private static void Copy(void* destination, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text + "\0");
        bytes.CopyTo(new Span<byte>(destination, bytes.Length));
    }

    private static string Read(void* text) => Marshal.PtrToStringAnsi((nint)text) ?? string.Empty;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static AEffect* Entry(delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint> host) => Create(host);

    /// <summary>The entry point's work: creates one plug-in.</summary>
    public static AEffect* Create(delegate* unmanaged[Cdecl]<AEffect*, int, int, nint, void*, float, nint> host)
    {
        // Like real plug-ins, ask the host before the object exists (effect = null).
        var st = new State { Host = host, HostVersion = (int)host(null, 1, 0, 0, null, 0) };
        int current = (int)host(null, 2, 0, 0, null, 0);
        if (ShellMode && current == 0)
        {
            st.IsShell = true;
            st.Id = 0x5368656C;
            st.Name = "Fake Shell";
        }
        else if (ShellMode)
        {
            st.Id = current;
            st.Name = current == ShellAId ? "Shell A" : "Shell B";
        }
        else
        {
            st.Id = GainId;
            st.Name = "Fake Gain";
        }
        st.Rect = (ERect*)NativeMemory.AllocZeroed((nuint)sizeof(ERect));
        *st.Rect = new ERect { Top = 0, Left = 0, Bottom = 200, Right = 300 };
        var e = (AEffect*)NativeMemory.AllocZeroed((nuint)sizeof(AEffect));
        e->Magic = AEffect.ExpectedMagic;
        e->Dispatcher = &Dispatcher;
        e->SetParameter = &SetParameter;
        e->GetParameter = &GetParameter;
        e->ProcessReplacing = &ProcessReplacing;
        e->NumPrograms = 3;
        e->NumParams = 2;
        e->NumInputs = 2;
        e->NumOutputs = 2;
        e->Flags = EffectFlags.HasEditor | EffectFlags.CanReplacing | EffectFlags.ProgramChunks;
        e->InitialDelay = 32;
        e->UniqueId = st.Id;
        e->Version = 7;
        e->Object = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(st));
        Last = st;
        return e;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void SetParameter(AEffect* e, int index, float value) => S(e).Params[index] = value;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static float GetParameter(AEffect* e, int index) => S(e).Params[index];

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static nint Dispatcher(AEffect* e, int opcode, int index, nint value, void* ptr, float opt)
    {
        var st = S(e);
        switch (opcode)
        {
            case EffectOpcode.Open: st.Opens++; return 0;
            case EffectOpcode.Close:
                GCHandle.FromIntPtr((nint)e->Object).Free();
                NativeMemory.Free(st.Rect);
                NativeMemory.Free(st.ChunkMemory);
                NativeMemory.Free(e);
                return 1;
            case EffectOpcode.SetProgram: st.Program = (int)value; return 0;
            case EffectOpcode.GetProgram: return st.Program;
            case EffectOpcode.SetProgramName: st.ProgramNames[st.Program] = Read(ptr); return 0;
            case EffectOpcode.GetProgramName: Copy(ptr, st.ProgramNames[st.Program]); return 0;
            case EffectOpcode.GetParamLabel: Copy(ptr, index == 0 ? "dB" : ""); return 0;
            case EffectOpcode.GetParamDisplay:
                Copy(ptr, index == 0 ? (20 * Math.Log10(Math.Max(1e-6, 2 * st.Params[0]))).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : st.Params[1] >= 0.5f ? "On" : "Off");
                return 0;
            case EffectOpcode.GetParamName: Copy(ptr, index == 0 ? "Gain" : "Mode"); return 0;
            case EffectOpcode.SetSampleRate: st.Rate = opt; return 0;
            case EffectOpcode.MainsChanged: st.Active = value != 0; return 0;
            case EffectOpcode.EditGetRect: *(ERect**)ptr = st.Rect; return 1;
            case EffectOpcode.EditOpen: st.Parent = (nint)ptr; return 1;
            case EffectOpcode.EditClose: st.EditCloses++; return 0;
            case EffectOpcode.EditIdle:
                if (st.WantResize)
                {
                    st.WantResize = false;
                    st.ResizeAnswer = (int)st.Host(e, HostOpcode.SizeWindow, 400, 250, null, 0);
                }
                return 0;
            case EffectOpcode.GetChunk:
                NativeMemory.Free(st.ChunkMemory);
                st.ChunkMemory = (byte*)NativeMemory.Alloc(12);
                ((float*)st.ChunkMemory)[0] = st.Params[0];
                ((float*)st.ChunkMemory)[1] = st.Params[1];
                ((int*)st.ChunkMemory)[2] = st.Program;
                *(void**)ptr = st.ChunkMemory;
                return 12;
            case EffectOpcode.SetChunk:
                if (value < 12) return 0;
                st.Params[0] = ((float*)ptr)[0];
                st.Params[1] = ((float*)ptr)[1];
                st.Program = ((int*)ptr)[2];
                return 1;
            case EffectOpcode.ProcessEvents:
            {
                var list = (VstEvents*)ptr;
                var pointers = (VstMidiEvent**)((byte*)list + sizeof(VstEvents));
                for (int i = 0; i < list->NumEvents; i++)
                    st.Received.Add((pointers[i]->DeltaFrames, pointers[i]->Data0, pointers[i]->Data1, pointers[i]->Data2));
                return 1;
            }
            case EffectOpcode.String2Parameter:
                if (ptr is null) return 1;
                string text = Read(ptr).Replace("dB", "", StringComparison.Ordinal).Trim();
                if (index != 0 || !double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out double db)) return 0;
                st.Params[0] = (float)(Math.Pow(10, db / 20) / 2);
                return 1;
            case EffectOpcode.GetProgramNameIndexed: Copy(ptr, st.ProgramNames[index]); return 1;
            case EffectOpcode.GetPlugCategory: return st.IsShell ? (int)Vst2Category.Shell : (int)Vst2Category.Effect;
            case EffectOpcode.GetEffectName: Copy(ptr, st.Name); return 1;
            case EffectOpcode.GetVendorString: Copy(ptr, "Test Vendor"); return 1;
            case EffectOpcode.GetProductString: Copy(ptr, "Fake Product"); return 1;
            case EffectOpcode.GetVendorVersion: return 1234;
            case EffectOpcode.CanDo: return Read(ptr) is "receiveVstMidiEvent" or "sendVstMidiEvent" ? 1 : -1;
            case EffectOpcode.GetTailSize: return 4800;
            case EffectOpcode.GetParameterProperties:
                if (index != 1) return 0;
                var props = (VstParameterProperties*)ptr;
                props->Flags = VstParameterProperties.IsSwitch;
                Copy(props->ShortLabel, "Mod");
                return 1;
            case EffectOpcode.GetVstVersion: return 2400;
            case EffectOpcode.ShellGetNextPlugin:
                if (!st.IsShell || st.ShellIndex >= 2) return 0;
                Copy(ptr, st.ShellIndex == 0 ? "Shell A" : "Shell B");
                return st.ShellIndex++ == 0 ? ShellAId : ShellBId;
            case EffectOpcode.StartProcess: st.StartProcess++; return 0;
            default: return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void ProcessReplacing(AEffect* e, float** inputs, float** outputs, int frames)
    {
        var st = S(e);
        var time = (VstTimeInfo*)st.Host(e, HostOpcode.GetTime, 0, 0, null, 0);
        if (time is not null)
        {
            st.Tempo = time->Tempo;
            st.Ppq = time->PpqPos;
            st.TimeFlags = time->Flags;
        }
        float gain = st.Params[0] * 2f;
        for (int c = 0; c < 2; c++)
        {
            for (int i = 0; i < frames; i++) outputs[c][i] = inputs[c][i] * gain;
        }
        // MIDI echo: each note an octave up.
        byte* block = stackalloc byte[sizeof(VstEvents) + 2 * sizeof(void*) + sizeof(VstMidiEvent)];
        foreach (var (delta, status, note, velocity) in st.Received)
        {
            var list = (VstEvents*)block;
            var ev = (VstMidiEvent*)(block + sizeof(VstEvents) + 2 * sizeof(void*));
            *ev = new VstMidiEvent { Type = VstMidiEvent.MidiType, ByteSize = sizeof(VstMidiEvent), DeltaFrames = delta, Data0 = status, Data1 = (byte)(note + 12), Data2 = velocity };
            list->NumEvents = 1;
            ((VstMidiEvent**)(block + sizeof(VstEvents)))[0] = ev;
            st.Host(e, HostOpcode.ProcessEvents, 0, 0, list, 0);
        }
        st.Received.Clear();
    }
}
