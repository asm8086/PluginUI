// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT
//
// The binding of the ASIO driver interface (the slots of its vtable, creating the driver with its class id as the
// interface id, the answers to the driver's messages, outputReady) follows NAudio's AsioDriver and AsioDriverExt (MIT,
// Copyright 2008-2026 Mark Heath; see build/THIRD-PARTY-NOTICES.txt). ASIO is a trademark of Steinberg Media
// Technologies GmbH; no part of the ASIO SDK is used.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PluginUI.Host.Audio;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Host.Windows;

/// <summary>An installed ASIO driver.</summary>
/// <param name="Name">Name under which the driver registered (HKLM\SOFTWARE\ASIO).</param>
/// <param name="ClassId">COM class of the driver.</param>
public sealed record AsioDriverInfo(string Name, Guid ClassId)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Buffer sizes an ASIO driver accepts.</summary>
/// <param name="Minimum">Smallest buffer, in samples.</param>
/// <param name="Maximum">Largest buffer.</param>
/// <param name="Preferred">The driver's choice (set in its control panel).</param>
/// <param name="Granularity">Step between sizes; -1 = powers of two; 0 = only <paramref name="Preferred"/>.</param>
public readonly record struct AsioBufferSizes(int Minimum, int Maximum, int Preferred, int Granularity);

/// <summary>The ASIO drivers installed on the computer.</summary>
public static class Asio
{
    /// <summary>The installed drivers, sorted by name (empty when there is none).</summary>
    public static IReadOnlyList<AsioDriverInfo> GetDrivers()
    {
        var list = new List<AsioDriverInfo>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASIO");
            if (key is null) return list;
            foreach (string name in key.GetSubKeyNames())
            {
                using var driver = key.OpenSubKey(name);
                if (driver?.GetValue("CLSID") is string clsid && Guid.TryParse(clsid, out var id)) list.Add(new AsioDriverInfo(name, id));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // An unreadable key: no driver to offer.
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>The sample rates offered to the user; the driver tells which it accepts.</summary>
    public static IReadOnlyList<int> CommonSampleRates { get; } = [44100, 48000, 88200, 96000, 176400, 192000];

    /// <summary>
    /// The buffer size used for a request: <paramref name="requested"/> (0 = the driver's preferred size) moved to the
    /// nearest size the driver accepts.
    /// </summary>
    public static int ChooseBufferSize(int requested, AsioBufferSizes sizes)
    {
        int min = Math.Max(1, sizes.Minimum), max = Math.Max(min, sizes.Maximum);
        int preferred = Math.Clamp(sizes.Preferred, min, max);
        if (requested <= 0 || sizes.Granularity == 0) return preferred;
        requested = Math.Clamp(requested, min, max);
        if (sizes.Granularity > 0)
        {
            int steps = (int)Math.Round((requested - min) / (double)sizes.Granularity);
            return Math.Min(max, min + steps * sizes.Granularity);
        }
        // Powers of two between the limits, the nearest by ratio.
        int best = preferred;
        double bestDistance = double.MaxValue;
        for (int size = 1; size > 0 && size <= max; size <<= 1)
        {
            if (size < min) continue;
            double distance = Math.Abs(Math.Log2(size / (double)requested));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = size;
            }
        }
        return best;
    }
}

/// <summary>
/// Plays an <see cref="IAudioRenderer"/> through an ASIO driver: the sound card directly, at the latency and sample rate
/// chosen in its driver, with all its channels.
/// </summary>
/// <remarks>
/// <para>Create, start, stop and dispose the driver on one STA thread with a message loop (the user interface thread):
/// ASIO drivers are COM objects of that apartment, and many post messages to it. Only one ASIO driver runs at a time
/// in a process (the ASIO callbacks carry no context). 64-bit processes only.</para>
/// <para>The driver calls the renderer on its own thread. When its settings change (buffer size or sample rate in its
/// control panel), <see cref="AudioDriver.ResetRequested"/> asks for a new <see cref="Start"/>.</para>
/// </remarks>
public sealed unsafe class AsioDriver : AudioDriver
{
    private const int AseOk = 0, AseSuccess = 0x3f4847a0, AseNotPresent = -1000;
    private static AsioDriver? s_current;

    private readonly string _driverName;
    private readonly Guid _classId;
    private readonly int _requestedFrames, _requestedInputs, _requestedOutputs;
    private readonly double _requestedRate;
    private nint _object;
    private VTable _vt;
    private bool _ownsObject, _initialized;
    private IAudioRenderer? _renderer;

    // Running stream (native, allocated in Start, freed in Stop).
    private BufferInfo* _infos;
    private Callbacks* _callbacks;
    private float* _floatMemory;
    private float** _inPointers, _outPointers;
    private AsioSampleType[] _inTypes = [], _outTypes = [];
    private int _inCount, _outCount;
    private bool _outputReady;
    private volatile bool _running;
    private int _inCallback, _failed, _resetRequested;

    /// <summary>Opens nothing yet; <see cref="Start"/> loads the driver.</summary>
    /// <param name="driverName">Name of an installed driver (<see cref="Asio.GetDrivers"/>).</param>
    /// <param name="bufferFrames">Buffer in samples (0 = the size chosen in the driver's control panel).</param>
    /// <param name="sampleRate">Sample rate (0 = the driver's current rate).</param>
    /// <param name="inputChannels">Input channels: 0 = none, -1 = all, n = the first n.</param>
    /// <param name="outputChannels">Output channels: -1 = all, n = the first n.</param>
    /// <exception cref="ArgumentException">No driver has this name.</exception>
    public AsioDriver(string driverName, int bufferFrames = 0, double sampleRate = 0, int inputChannels = 0, int outputChannels = -1)
        : this(driverName, bufferFrames, sampleRate, inputChannels, outputChannels, registered: true)
    {
        var info = Asio.GetDrivers().FirstOrDefault(d => string.Equals(d.Name, driverName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No ASIO driver named '{driverName}' is installed.", nameof(driverName));
        _classId = info.ClassId;
    }

    private AsioDriver(string driverName, int bufferFrames, double sampleRate, int inputChannels, int outputChannels, bool registered)
    {
        _ = registered;
        ArgumentException.ThrowIfNullOrEmpty(driverName);
        _driverName = driverName;
        _requestedFrames = Math.Max(0, bufferFrames);
        _requestedRate = Math.Max(0, sampleRate);
        _requestedInputs = inputChannels;
        _requestedOutputs = outputChannels;
        DriverName = driverName;
    }

    /// <summary>Wraps a driver object that is already created (tests): the object is released by <see cref="Dispose(bool)"/>.</summary>
    internal static AsioDriver FromObject(nint driverObject, string name, int bufferFrames = 0, double sampleRate = 0, int inputChannels = 0, int outputChannels = -1)
    {
        var driver = new AsioDriver(name, bufferFrames, sampleRate, inputChannels, outputChannels, registered: false) { _object = driverObject, _ownsObject = true };
        driver._vt = new VTable(driverObject);
        return driver;
    }

    /// <summary>Window that owns the driver's dialogs (the main window; 0 = none).</summary>
    public nint WindowHandle { get; init; }

    /// <inheritdoc />
    public override string Name => "ASIO — " + DriverName;

    /// <summary>Name under which the driver is registered (<see cref="AsioDriverInfo.Name"/>).</summary>
    public string RegisteredName => _driverName;

    /// <summary>Name the driver gives itself (the registered name until it is loaded).</summary>
    public string DriverName { get; private set; }

    /// <summary>Version of the driver.</summary>
    public int DriverVersion { get; private set; }

    /// <summary>Names of all the input channels of the device.</summary>
    public IReadOnlyList<string> InputChannelNames { get; private set; } = [];

    /// <summary>Names of all the output channels of the device.</summary>
    public IReadOnlyList<string> OutputChannelNames { get; private set; } = [];

    /// <summary>Input latency reported by the driver, in samples (0 when unknown).</summary>
    public int InputLatencySamples { get; private set; }

    /// <summary>Buffer sizes the driver accepts.</summary>
    public AsioBufferSizes BufferSizes { get; private set; }

    /// <summary>Sample rates of <see cref="Asio.CommonSampleRates"/> the driver accepts.</summary>
    public IReadOnlyList<int> SupportedSampleRates { get; private set; } = [];

    /// <summary>Format of the samples of the first output channel ("Int32LSB").</summary>
    public string SampleFormat { get; private set; } = string.Empty;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The driver cannot be loaded or started, or another ASIO driver runs.</exception>
    /// <exception cref="NotSupportedException">The device uses a sample format that is not supported (DSD).</exception>
    public override void Start(IAudioRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        Stop();
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("ASIO is supported in 64-bit processes only.");
        var other = Interlocked.CompareExchange(ref s_current, this, null);
        if (other is not null && !ReferenceEquals(other, this)) throw new InvalidOperationException($"Another ASIO driver ({other.DriverName}) is running.");
        try
        {
            Load();
            Configure();
            CreateBuffers();
            renderer.Prepare(SampleRate, BlockSize, InputChannels, OutputChannels);
            _renderer = renderer;
            _failed = 0;
            _resetRequested = 0;
            _running = true;
            Check(_vt.Start(_object), "start");
            IsRunning = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    /// <inheritdoc />
    public override void Stop()
    {
        if (_running || _infos is not null)
        {
            _running = false;
            if (_object != 0)
            {
                _vt.Stop(_object);
                // The specification says no callback runs after stop returns; make sure before freeing the buffers.
                var wait = new SpinWait();
                while (Volatile.Read(ref _inCallback) != 0) wait.SpinOnce();
                if (_infos is not null) _vt.DisposeBuffers(_object);
            }
            FreeStream();
        }
        _renderer = null;
        IsRunning = false;
        Interlocked.CompareExchange(ref s_current, null, this);
    }

    /// <summary>Opens the driver's control panel (buffer size, clock, routing of the card).</summary>
    /// <remarks>Changes made there usually raise <see cref="AudioDriver.ResetRequested"/>.</remarks>
    /// <exception cref="InvalidOperationException">The driver is not loaded (not started yet) or refused.</exception>
    public void ShowControlPanel()
    {
        if (_object == 0) throw new InvalidOperationException("The ASIO driver is not loaded.");
        Check(_vt.ControlPanel(_object), "controlPanel");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_object != 0)
        {
            if (_ownsObject) _vt.Release(_object);
            _object = 0;
            _initialized = false;
        }
    }

    private void Load()
    {
        if (_object == 0) Create();
        if (_initialized) return;
        if (_vt.Init(_object, WindowHandle) == 0)
        {
            string message = ErrorMessage();
            throw new InvalidOperationException($"The ASIO driver '{_driverName}' could not be started{(message.Length > 0 ? ": " + message : " (is the device connected, or used by another application?)")}.");
        }
        _initialized = true;
    }

    private void Create()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("ASIO drivers must be opened on an STA thread with a message loop (the user interface thread).");
        Guid clsid = _classId;
        void* driver;
        // ASIO drivers expect their class id as the interface id too.
        HRESULT hr = CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &clsid, &driver);
        if (FAILED(hr) || driver is null) throw new InvalidOperationException($"The ASIO driver '{_driverName}' could not be loaded (0x{hr.Value:X8}).");
        _object = (nint)driver;
        _ownsObject = true;
        _vt = new VTable(_object);
    }

    private void Configure()
    {
        byte* text = stackalloc byte[256];
        new Span<byte>(text, 256).Clear();
        _vt.GetDriverName(_object, text);
        string name = ReadString(text, 256);
        if (name.Length > 0) DriverName = name;
        DriverVersion = _vt.GetDriverVersion(_object);

        int inputs, outputs;
        Check(_vt.GetChannels(_object, &inputs, &outputs), "getChannels");
        if (outputs <= 0) throw new InvalidOperationException($"The ASIO driver '{DriverName}' has no output.");
        InputChannelNames = ChannelNames(inputs, true, out var inTypes);
        OutputChannelNames = ChannelNames(outputs, false, out var outTypes);

        _inCount = _requestedInputs < 0 ? inputs : Math.Min(_requestedInputs, inputs);
        _outCount = _requestedOutputs < 0 ? outputs : Math.Clamp(_requestedOutputs, 1, outputs);
        _inTypes = inTypes[.._inCount];
        _outTypes = outTypes[.._outCount];
        foreach (var type in _inTypes.Concat(_outTypes))
        {
            if (!AsioSamples.IsSupported(type)) throw new NotSupportedException($"The ASIO driver '{DriverName}' uses the sample format {(int)type}, which is not supported.");
        }
        SampleFormat = AsioSamples.Describe(_outTypes[0]);

        var rates = new List<int>();
        foreach (int rate in Asio.CommonSampleRates)
        {
            if (_vt.CanSampleRate(_object, rate) == AseOk) rates.Add(rate);
        }
        SupportedSampleRates = rates;
        if (_requestedRate > 0)
        {
            double current;
            if (_vt.GetSampleRate(_object, &current) != AseOk || Math.Abs(current - _requestedRate) > 0.5)
            {
                if (_vt.CanSampleRate(_object, _requestedRate) != AseOk) throw new InvalidOperationException($"The ASIO driver '{DriverName}' does not accept {_requestedRate} Hz.");
                Check(_vt.SetSampleRate(_object, _requestedRate), "setSampleRate");
            }
        }
        double sampleRate = 0;
        if (_vt.GetSampleRate(_object, &sampleRate) != AseOk || sampleRate <= 0)
        {
            // Some drivers report no rate until one is set.
            sampleRate = rates.Contains(48000) ? 48000 : rates.Count > 0 ? rates[0] : 44100;
            Check(_vt.SetSampleRate(_object, sampleRate), "setSampleRate");
        }
        SampleRate = sampleRate;

        int min, max, preferred, granularity;
        Check(_vt.GetBufferSize(_object, &min, &max, &preferred, &granularity), "getBufferSize");
        BufferSizes = new AsioBufferSizes(min, max, preferred, granularity);
        BlockSize = Asio.ChooseBufferSize(_requestedFrames, BufferSizes);
        InputChannels = _inCount;
        OutputChannels = _outCount;
    }

    private string[] ChannelNames(int count, bool input, out AsioSampleType[] types)
    {
        var names = new string[count];
        types = new AsioSampleType[count];
        for (int i = 0; i < count; i++)
        {
            var info = new ChannelInfo { Channel = i, IsInput = input ? 1 : 0 };
            Check(_vt.GetChannelInfo(_object, &info), "getChannelInfo");
            names[i] = ReadString(info.Name, 32) is { Length: > 0 } n ? n : (input ? "In " : "Out ") + (i + 1);
            types[i] = (AsioSampleType)info.Type;
        }
        return names;
    }

    private void CreateBuffers()
    {
        int total = _inCount + _outCount, frames = BlockSize;
        _infos = (BufferInfo*)NativeMemory.AllocZeroed((nuint)total, (nuint)sizeof(BufferInfo));
        for (int i = 0; i < total; i++)
        {
            _infos[i].IsInput = i < _inCount ? 1 : 0;
            _infos[i].Channel = i < _inCount ? i : i - _inCount;
        }
        _callbacks = (Callbacks*)NativeMemory.AllocZeroed((nuint)sizeof(Callbacks));
        _callbacks->BufferSwitch = (delegate* unmanaged<int, int, void>)&OnBufferSwitch;
        _callbacks->SampleRateDidChange = (delegate* unmanaged<double, void>)&OnSampleRateDidChange;
        _callbacks->Message = (delegate* unmanaged<int, int, void*, double*, int>)&OnMessage;
        _callbacks->BufferSwitchTimeInfo = (delegate* unmanaged<void*, int, int, void*>)&OnBufferSwitchTimeInfo;
        _floatMemory = (float*)NativeMemory.AllocZeroed((nuint)(Math.Max(1, total) * frames), sizeof(float));
        _inPointers = (float**)NativeMemory.Alloc((nuint)Math.Max(1, _inCount), (nuint)sizeof(float*));
        _outPointers = (float**)NativeMemory.Alloc((nuint)Math.Max(1, _outCount), (nuint)sizeof(float*));
        for (int c = 0; c < _inCount; c++) _inPointers[c] = _floatMemory + c * frames;
        for (int c = 0; c < _outCount; c++) _outPointers[c] = _floatMemory + (_inCount + c) * frames;

        int result = _vt.CreateBuffers(_object, _infos, total, frames, _callbacks);
        if (result != AseOk)
        {
            // The driver keeps nothing after a failure: free here without disposeBuffers.
            FreeStream();
            Check(result, "createBuffers");
        }

        int inLatency, outLatency;
        if (_vt.GetLatencies(_object, &inLatency, &outLatency) == AseOk)
        {
            InputLatencySamples = inLatency;
            OutputLatencySamples = outLatency;
        }
        _outputReady = _vt.OutputReady(_object) == AseOk;
    }

    private void FreeStream()
    {
        NativeMemory.Free(_infos);
        NativeMemory.Free(_callbacks);
        NativeMemory.Free(_floatMemory);
        NativeMemory.Free(_inPointers);
        NativeMemory.Free(_outPointers);
        _infos = null;
        _callbacks = null;
        _floatMemory = null;
        _inPointers = _outPointers = null;
    }

    /// <summary>Driver thread: converts the inputs, renders, converts the outputs.</summary>
    private void Process(int index)
    {
        Interlocked.Increment(ref _inCallback);
        try
        {
            if (!_running || _infos is null) return;
            int frames = BlockSize;
            index &= 1;
            for (int c = 0; c < _inCount; c++)
                AsioSamples.ToFloat(index == 0 ? (byte*)_infos[c].Buffer0 : (byte*)_infos[c].Buffer1, _inTypes[c], _inPointers[c], frames);
            var inputs = new AudioBuffers(_inCount > 0 ? _inPointers : null, _inCount, frames);
            var outputs = new AudioBuffers(_outPointers, _outCount, frames);
            try
            {
                if (Volatile.Read(ref _failed) == 0) _renderer!.Render(inputs, outputs);
                else outputs.Clear();
            }
            catch (Exception ex)
            {
                outputs.Clear();
                if (Interlocked.Exchange(ref _failed, 1) == 0)
                {
                    string message = ex.Message;
                    ThreadPool.UnsafeQueueUserWorkItem(_ => RaiseFailed(message), null);
                }
            }
            for (int c = 0; c < _outCount; c++)
            {
                var info = &_infos[_inCount + c];
                AsioSamples.FromFloat(_outPointers[c], _outTypes[c], index == 0 ? (byte*)info->Buffer0 : (byte*)info->Buffer1, frames);
            }
            if (_outputReady) _vt.OutputReady(_object);
        }
        finally
        {
            Interlocked.Decrement(ref _inCallback);
        }
    }

    private void RequestReset()
    {
        if (Interlocked.Exchange(ref _resetRequested, 1) == 0) ThreadPool.UnsafeQueueUserWorkItem(_ => RaiseResetRequested(), null);
    }

    [UnmanagedCallersOnly]
    private static void OnBufferSwitch(int index, int directProcess)
    {
        try
        {
            Volatile.Read(ref s_current)?.Process(index);
        }
        catch
        {
            // Never let an exception reach the driver.
        }
    }

    [UnmanagedCallersOnly]
    private static void* OnBufferSwitchTimeInfo(void* time, int index, int directProcess)
    {
        try
        {
            Volatile.Read(ref s_current)?.Process(index);
        }
        catch
        {
            // Never let an exception reach the driver.
        }
        return time;
    }

    [UnmanagedCallersOnly]
    private static void OnSampleRateDidChange(double rate)
    {
        try
        {
            Volatile.Read(ref s_current)?.RequestReset();
        }
        catch
        {
            // Never let an exception reach the driver.
        }
    }

    [UnmanagedCallersOnly]
    private static int OnMessage(int selector, int value, void* message, double* opt)
    {
        try
        {
            return HandleMessage(selector, value);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Answers the driver's messages (asioMessage).</summary>
    internal static int HandleMessage(int selector, int value)
    {
        const int SelectorSupported = 1, EngineVersion = 2, ResetRequest = 3, BufferSizeChange = 4, ResyncRequest = 5,
            LatenciesChanged = 6, SupportsTimeInfo = 7;
        switch (selector)
        {
            case SelectorSupported:
                return value is EngineVersion or ResetRequest or BufferSizeChange or ResyncRequest or LatenciesChanged or SupportsTimeInfo ? 1 : 0;
            case EngineVersion:
                return 2;
            case ResetRequest:
            case BufferSizeChange:
                // The driver must be stopped and started again, from the thread that owns it.
                Volatile.Read(ref s_current)?.RequestReset();
                return 1;
            case ResyncRequest:
            case LatenciesChanged:
                return 1;
            case SupportsTimeInfo:
                return 1;
            default:
                return 0;
        }
    }

    private void Check(int result, string method)
    {
        if (result is AseOk or AseSuccess) return;
        string message = ErrorMessage();
        throw new InvalidOperationException($"The ASIO driver '{DriverName}' failed in {method}: {(message.Length > 0 ? message : ErrorName(result))}.");
    }

    private string ErrorMessage()
    {
        if (_object == 0) return string.Empty;
        byte* text = stackalloc byte[256];
        new Span<byte>(text, 256).Clear();
        _vt.GetErrorMessage(_object, text);
        return ReadString(text, 256);
    }

    private static string ErrorName(int error) => error switch
    {
        AseNotPresent => "the device is not present",
        -999 => "hardware malfunction",
        -998 => "invalid parameter",
        -997 => "invalid mode",
        -996 => "the sample position does not advance",
        -995 => "no clock",
        -994 => "not enough memory",
        _ => $"error {error}",
    };

    private static string ReadString(byte* text, int capacity)
    {
        int length = new ReadOnlySpan<byte>(text, capacity).IndexOf((byte)0);
        if (length < 0) length = capacity;
        // Drivers write ANSI text: Latin-1 keeps every byte and is right for the usual names.
        return Encoding.Latin1.GetString(text, length).Trim();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferInfo
    {
        public int IsInput;
        public int Channel;
        public void* Buffer0;
        public void* Buffer1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ChannelInfo
    {
        public int Channel;
        public int IsInput;
        public int IsActive;
        public int Group;
        public int Type;
        public fixed byte Name[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Callbacks
    {
        public delegate* unmanaged<int, int, void> BufferSwitch;
        public delegate* unmanaged<double, void> SampleRateDidChange;
        public delegate* unmanaged<int, int, void*, double*, int> Message;
        public delegate* unmanaged<void*, int, int, void*> BufferSwitchTimeInfo;
    }

    /// <summary>
    /// The IASIO vtable: IUnknown, then the driver methods. On x64 and ARM64 the C++ member convention is the platform's
    /// default convention, with the object as first argument; ASIO's "long" is 32 bits on Windows.
    /// </summary>
    private readonly struct VTable
    {
        public readonly delegate* unmanaged<nint, uint> Release;                                    // 2
        public readonly delegate* unmanaged<nint, nint, int> Init;                                  // 3  init(sysHandle)
        public readonly delegate* unmanaged<nint, byte*, void> GetDriverName;                       // 4
        public readonly delegate* unmanaged<nint, int> GetDriverVersion;                            // 5
        public readonly delegate* unmanaged<nint, byte*, void> GetErrorMessage;                     // 6
        public readonly delegate* unmanaged<nint, int> Start;                                       // 7
        public readonly delegate* unmanaged<nint, int> Stop;                                        // 8
        public readonly delegate* unmanaged<nint, int*, int*, int> GetChannels;                     // 9
        public readonly delegate* unmanaged<nint, int*, int*, int> GetLatencies;                    // 10
        public readonly delegate* unmanaged<nint, int*, int*, int*, int*, int> GetBufferSize;       // 11
        public readonly delegate* unmanaged<nint, double, int> CanSampleRate;                       // 12
        public readonly delegate* unmanaged<nint, double*, int> GetSampleRate;                      // 13
        public readonly delegate* unmanaged<nint, double, int> SetSampleRate;                       // 14
        public readonly delegate* unmanaged<nint, ChannelInfo*, int> GetChannelInfo;                // 18
        public readonly delegate* unmanaged<nint, BufferInfo*, int, int, Callbacks*, int> CreateBuffers; // 19
        public readonly delegate* unmanaged<nint, int> DisposeBuffers;                              // 20
        public readonly delegate* unmanaged<nint, int> ControlPanel;                                // 21
        public readonly delegate* unmanaged<nint, int> OutputReady;                                 // 23

        public VTable(nint driver)
        {
            var slots = *(void***)driver;
            Release = (delegate* unmanaged<nint, uint>)slots[2];
            Init = (delegate* unmanaged<nint, nint, int>)slots[3];
            GetDriverName = (delegate* unmanaged<nint, byte*, void>)slots[4];
            GetDriverVersion = (delegate* unmanaged<nint, int>)slots[5];
            GetErrorMessage = (delegate* unmanaged<nint, byte*, void>)slots[6];
            Start = (delegate* unmanaged<nint, int>)slots[7];
            Stop = (delegate* unmanaged<nint, int>)slots[8];
            GetChannels = (delegate* unmanaged<nint, int*, int*, int>)slots[9];
            GetLatencies = (delegate* unmanaged<nint, int*, int*, int>)slots[10];
            GetBufferSize = (delegate* unmanaged<nint, int*, int*, int*, int*, int>)slots[11];
            CanSampleRate = (delegate* unmanaged<nint, double, int>)slots[12];
            GetSampleRate = (delegate* unmanaged<nint, double*, int>)slots[13];
            SetSampleRate = (delegate* unmanaged<nint, double, int>)slots[14];
            GetChannelInfo = (delegate* unmanaged<nint, ChannelInfo*, int>)slots[18];
            CreateBuffers = (delegate* unmanaged<nint, BufferInfo*, int, int, Callbacks*, int>)slots[19];
            DisposeBuffers = (delegate* unmanaged<nint, int>)slots[20];
            ControlPanel = (delegate* unmanaged<nint, int>)slots[21];
            OutputReady = (delegate* unmanaged<nint, int>)slots[23];
        }
    }
}
