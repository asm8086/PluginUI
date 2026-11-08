// 2026/11/08 18:01:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Vst2;

/// <summary>Control operations between a host and a bridge.</summary>
internal static class BridgeOperation
{
    public const int Describe = 1;
    public const int Prepare = 2;
    public const int Stop = 3;
    public const int SetParameter = 4;
    public const int FormatParameter = 5;
    public const int ParseParameter = 6;
    public const int GetState = 7;
    public const int SetState = 8;
    public const int Idle = 9;
    public const int Close = 10;
    public const int EditorCreate = 11;
    public const int EditorOpen = 12;
    public const int EditorClose = 13;
}

/// <summary>What a host learns about a plug-in loaded in a bridge.</summary>
internal sealed class BridgeDescription
{
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public bool IsInstrument { get; set; }
    public List<PluginBus> Inputs { get; } = [];
    public List<PluginBus> Outputs { get; } = [];
    public int EventInputs { get; set; }
    public int EventOutputs { get; set; }
    public int Latency { get; set; }
    public int Tail { get; set; }
    public bool HasEditor { get; set; }
    public string PresetExtension { get; set; } = ".fxp";
    public int UniqueId { get; set; }
    public List<PluginParameter> Parameters { get; } = [];

    public static BridgeDescription Of(Vst2Plugin plugin)
    {
        var d = new BridgeDescription
        {
            Name = plugin.Name, Vendor = plugin.Vendor, IsInstrument = plugin.IsInstrument, EventInputs = plugin.EventInputCount,
            EventOutputs = plugin.EventOutputCount, Latency = plugin.LatencySamples, Tail = plugin.TailSamples, HasEditor = plugin.HasEditor,
            PresetExtension = plugin.PresetExtension, UniqueId = plugin.UniqueId,
        };
        d.Inputs.AddRange(plugin.InputBuses);
        d.Outputs.AddRange(plugin.OutputBuses);
        d.Parameters.AddRange(plugin.Parameters);
        return d;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(Name);
        w.Write(Vendor);
        w.Write(IsInstrument);
        WriteBuses(w, Inputs);
        WriteBuses(w, Outputs);
        w.Write(EventInputs);
        w.Write(EventOutputs);
        w.Write(Latency);
        w.Write(Tail);
        w.Write(HasEditor);
        w.Write(PresetExtension);
        w.Write(UniqueId);
        w.Write(Parameters.Count);
        foreach (var p in Parameters)
        {
            w.Write(p.Id);
            w.Write(p.Title);
            w.Write(p.ShortTitle);
            w.Write(p.Units);
            w.Write(p.StepCount);
            w.Write(p.DefaultValue);
            w.Write(p.UnitId);
            w.Write((int)p.Flags);
        }
    }

    public static BridgeDescription Read(BinaryReader r)
    {
        var d = new BridgeDescription { Name = r.ReadString(), Vendor = r.ReadString(), IsInstrument = r.ReadBoolean() };
        ReadBuses(r, d.Inputs);
        ReadBuses(r, d.Outputs);
        d.EventInputs = r.ReadInt32();
        d.EventOutputs = r.ReadInt32();
        d.Latency = r.ReadInt32();
        d.Tail = r.ReadInt32();
        d.HasEditor = r.ReadBoolean();
        d.PresetExtension = r.ReadString();
        d.UniqueId = r.ReadInt32();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            d.Parameters.Add(new PluginParameter(r.ReadUInt32(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadInt32(), r.ReadDouble(), r.ReadInt32(),
                (ParameterFlags)r.ReadInt32()));
        }
        return d;
    }

    private static void WriteBuses(BinaryWriter w, List<PluginBus> buses)
    {
        w.Write(buses.Count);
        foreach (var b in buses)
        {
            w.Write(b.Name);
            w.Write(b.ChannelCount);
            w.Write(b.IsAuxiliary);
            w.Write(b.IsDefaultActive);
        }
    }

    private static void ReadBuses(BinaryReader r, List<PluginBus> buses)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) buses.Add(new PluginBus(r.ReadString(), r.ReadInt32(), r.ReadBoolean(), r.ReadBoolean()));
    }
}

/// <summary>Writes and reads the payloads of control calls.</summary>
internal static class BridgePayload
{
    public static byte[] Write(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) write(w);
        return stream.ToArray();
    }

    public static T Read<T>(ReadOnlySpan<byte> payload, Func<BinaryReader, T> read)
    {
        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8);
        return read(r);
    }
}
