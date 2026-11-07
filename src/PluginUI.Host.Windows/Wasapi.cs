// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using PluginUI.Host.Audio;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Host.Windows;

/// <summary>An audio endpoint of Windows (a speaker, headphones, a microphone, a channel pair of an interface).</summary>
/// <param name="Id">Endpoint identifier (stable across sessions).</param>
/// <param name="Name">Name shown in Windows' sound settings.</param>
/// <param name="IsInput"><c>true</c> for a recording device.</param>
/// <param name="IsDefault">Default device of Windows.</param>
/// <param name="Channels">Channels of the shared-mode format.</param>
/// <param name="SampleRate">Sample rate of the shared-mode format.</param>
public sealed record AudioEndpoint(string Id, string Name, bool IsInput, bool IsDefault, int Channels, int SampleRate)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Lists the WASAPI endpoints.</summary>
public static unsafe class Wasapi
{
    // {a45c254e-df1c-4efd-8020-67d146a850e0}, 14: PKEY_Device_FriendlyName
    private static readonly PROPERTYKEY FriendlyName = new() { fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0), pid = 14 };

    /// <summary>Active output (or input) endpoints, the default one first.</summary>
    public static IReadOnlyList<AudioEndpoint> GetEndpoints(bool input)
    {
        EnsureCom();
        var list = new List<AudioEndpoint>();
        using var enumerator = CreateEnumerator();
        var flow = input ? EDataFlow.eCapture : EDataFlow.eRender;
        string? defaultId = null;
        IMMDevice* device;
        if (enumerator.Get()->GetDefaultAudioEndpoint(flow, ERole.eConsole, &device).SUCCEEDED)
        {
            defaultId = GetId(device);
            device->Release();
        }
        IMMDeviceCollection* collection;
        ThrowIfFailed(enumerator.Get()->EnumAudioEndpoints(flow, (uint)DEVICE.DEVICE_STATE_ACTIVE, &collection), "listing the audio devices");
        try
        {
            uint count;
            collection->GetCount(&count);
            for (uint i = 0; i < count; i++)
            {
                if (collection->Item(i, &device).FAILED) continue;
                try
                {
                    string id = GetId(device);
                    var (channels, rate) = MixFormat(device);
                    list.Add(new AudioEndpoint(id, GetName(device) ?? id, input, id == defaultId, channels, rate));
                }
                catch (COMException)
                {
                    // An endpoint that cannot be opened (in use exclusively, being removed) is left out.
                }
                finally
                {
                    device->Release();
                }
            }
        }
        finally
        {
            collection->Release();
        }
        return [.. list.OrderByDescending(e => e.IsDefault)];
    }

    internal static void EnsureCom()
    {
        // Any apartment will do: an STA thread (user interface) answers RPC_E_CHANGED_MODE and keeps its COM.
        CoInitializeEx(null, (uint)COINIT.COINIT_MULTITHREADED);
    }

    internal static ComPtr<IMMDeviceEnumerator> CreateEnumerator()
    {
        ComPtr<IMMDeviceEnumerator> enumerator = default;
        fixed (Guid* clsid = &CLSID.CLSID_MMDeviceEnumerator)
        {
            ThrowIfFailed(CoCreateInstance(clsid, null, (uint)CLSCTX_ALL, __uuidof<IMMDeviceEnumerator>(), (void**)enumerator.GetAddressOf()),
                "opening the audio device list");
        }
        return enumerator;
    }

    internal static IMMDevice* GetDevice(IMMDeviceEnumerator* enumerator, string? id, bool input)
    {
        IMMDevice* device;
        if (string.IsNullOrEmpty(id))
        {
            ThrowIfFailed(enumerator->GetDefaultAudioEndpoint(input ? EDataFlow.eCapture : EDataFlow.eRender, ERole.eConsole, &device),
                input ? "finding the default input device" : "finding the default output device");
            return device;
        }
        fixed (char* p = id)
        {
            ThrowIfFailed(enumerator->GetDevice(p, &device), "opening the audio device");
        }
        return device;
    }

    internal static string GetId(IMMDevice* device)
    {
        char* id;
        ThrowIfFailed(device->GetId(&id), "reading the device identifier");
        try
        {
            return new string(id);
        }
        finally
        {
            CoTaskMemFree(id);
        }
    }

    internal static string? GetName(IMMDevice* device)
    {
        IPropertyStore* store;
        if (device->OpenPropertyStore((uint)STGM.STGM_READ, &store).FAILED) return null;
        try
        {
            PROPVARIANT value;
            var key = FriendlyName;
            if (store->GetValue(&key, &value).FAILED) return null;
            try
            {
                return value.vt == (ushort)VARENUM.VT_LPWSTR && value.pwszVal is not null ? new string(value.pwszVal) : null;
            }
            finally
            {
                PropVariantClear(&value);
            }
        }
        finally
        {
            store->Release();
        }
    }

    private static (int Channels, int Rate) MixFormat(IMMDevice* device)
    {
        IAudioClient* client;
        ThrowIfFailed(device->Activate(__uuidof<IAudioClient>(), (uint)CLSCTX_ALL, null, (void**)&client), "opening the device");
        try
        {
            WAVEFORMATEX* format;
            ThrowIfFailed(client->GetMixFormat(&format), "reading the device format");
            try
            {
                return (format->nChannels, (int)format->nSamplesPerSec);
            }
            finally
            {
                CoTaskMemFree(format);
            }
        }
        finally
        {
            client->Release();
        }
    }

    internal static void ThrowIfFailed(HRESULT hr, string doing)
    {
        if (hr.FAILED) throw new COMException($"Error while {doing}: {Describe(hr)}.", hr);
    }

    internal static string Describe(HRESULT hr) => (uint)(int)hr switch
    {
        0x88890004 => "the device was removed or changed (AUDCLNT_E_DEVICE_INVALIDATED)",
        0x8889000A => "the device is used by another application in exclusive mode (AUDCLNT_E_DEVICE_IN_USE)",
        0x88890008 => "format not supported (AUDCLNT_E_UNSUPPORTED_FORMAT)",
        0x88890019 => "buffer size not supported (AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED)",
        0x80070005 => "access denied (check the microphone privacy settings of Windows)",
        _ => $"0x{(uint)(int)hr:X8}",
    };
}

/// <summary>Sample formats WASAPI may give in shared mode.</summary>
internal enum SampleFormat
{
    Float32,
    Int16,
    Int24,
    Int32,
}

/// <summary>
/// Audio through WASAPI in shared mode: event-driven output at the smallest period the engine allows (IAudioClient3,
/// Windows 10) or the requested buffer, optional input from a capture endpoint at the same sample rate, on a thread
/// registered with MMCSS as "Pro Audio".
/// </summary>
/// <remarks>
/// The device keeps Windows' shared-mode format (sample rate and channels of the sound settings); samples are converted
/// from and to 32-bit float. When the input and output clocks drift, the input buffer drops or repeats samples to keep a
/// bounded latency. <see cref="AudioDriver.Failed"/> reports a device that was removed or taken.
/// </remarks>
public sealed unsafe class WasapiDriver : AudioDriver
{
    private const long HundredNanosecondsPerSecond = 10_000_000;
    private readonly string? _outputId, _inputId;
    private readonly int _requestedFrames;
    private Thread? _thread;
    private volatile bool _stop;
    private IAudioRenderer? _renderer;
    private Exception? _openError;
    private readonly ManualResetEventSlim _opened = new(false), _go = new(false);
    private string _name = "WASAPI";

    /// <summary>Opens nothing yet; <see cref="Start"/> opens the devices.</summary>
    /// <param name="outputId">Output endpoint (null = default device of Windows).</param>
    /// <param name="inputId">Input endpoint (null = no input; "" = default input device).</param>
    /// <param name="bufferFrames">Requested buffer in samples (0 = the smallest the device allows).</param>
    public WasapiDriver(string? outputId, string? inputId = null, int bufferFrames = 0)
    {
        _outputId = outputId;
        _inputId = inputId;
        _requestedFrames = Math.Max(0, bufferFrames);
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <summary>Name of the input device (null = no input).</summary>
    public string? InputName { get; private set; }

    /// <inheritdoc />
    public override void Start(IAudioRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        Stop();
        _renderer = renderer;
        _stop = false;
        _openError = null;
        _opened.Reset();
        _go.Reset();
        _thread = new Thread(Run, 256 * 1024) { Name = "Audio (WASAPI)", IsBackground = true, Priority = ThreadPriority.Highest };
        _thread.Start();
        _opened.Wait();
        if (_openError is not null)
        {
            _thread.Join();
            _thread = null;
            throw new InvalidOperationException(_openError.Message, _openError);
        }
        try
        {
            renderer.Prepare(SampleRate, BlockSize, InputChannels, OutputChannels);
        }
        catch
        {
            _stop = true;
            _go.Set();
            _thread.Join();
            _thread = null;
            throw;
        }
        IsRunning = true;
        _go.Set();
    }

    /// <inheritdoc />
    public override void Stop()
    {
        if (_thread is null) return;
        _stop = true;
        _go.Set();
        _thread.Join();
        _thread = null;
        IsRunning = false;
    }

    /// <summary>Native resources of a running stream (released together).</summary>
    private sealed class Session
    {
        public IMMDevice* OutDevice, InDevice;
        public IAudioClient* Output, Input;
        public IAudioRenderClient* Render;
        public IAudioCaptureClient* Capture;
        public WAVEFORMATEX* OutFormat, InFormat;
        public HANDLE Ready;
        public float* OutMemory, InMemory, Ring;
        public float** OutPointers, InPointers;
        public int RingCapacity;

        public void Release()
        {
            if (Render is not null) Render->Release();
            if (Capture is not null) Capture->Release();
            if (Output is not null) Output->Release();
            if (Input is not null) Input->Release();
            if (OutDevice is not null) OutDevice->Release();
            if (InDevice is not null) InDevice->Release();
            if (OutFormat is not null) CoTaskMemFree(OutFormat);
            if (InFormat is not null) CoTaskMemFree(InFormat);
            if (Ready != default) CloseHandle(Ready);
            NativeMemory.Free(OutMemory);
            NativeMemory.Free(InMemory);
            NativeMemory.Free(Ring);
            NativeMemory.Free(OutPointers);
            NativeMemory.Free(InPointers);
            Render = null;
            Capture = null;
            Output = Input = null;
            OutDevice = InDevice = null;
            OutFormat = InFormat = null;
            Ready = default;
            OutMemory = InMemory = Ring = null;
            OutPointers = InPointers = null;
        }
    }

    private void Run()
    {
        Wasapi.EnsureCom();
        var s = new Session();
        try
        {
            Open(s);
        }
        catch (Exception ex)
        {
            _openError = ex;
            s.Release();
            _opened.Set();
            return;
        }
        _opened.Set();
        _go.Wait();
        if (_stop)
        {
            s.Release();
            return;
        }
        uint task = 0;
        HANDLE mmcss;
        fixed (char* name = "Pro Audio") mmcss = AvSetMmThreadCharacteristicsW(name, &task);
        try
        {
            Stream(s);
        }
        catch (Exception ex)
        {
            RaiseFailed(ex.Message);
        }
        finally
        {
            if (mmcss != default) AvRevertMmThreadCharacteristics(mmcss);
            s.Release();
        }
    }

    private void Open(Session s)
    {
        using (var enumerator = Wasapi.CreateEnumerator())
        {
            s.OutDevice = Wasapi.GetDevice(enumerator.Get(), _outputId, input: false);
            _name = "WASAPI — " + (Wasapi.GetName(s.OutDevice) ?? "output");
            if (_inputId is not null)
            {
                s.InDevice = Wasapi.GetDevice(enumerator.Get(), _inputId, input: true);
                InputName = Wasapi.GetName(s.InDevice) ?? "input";
            }
        }
        IAudioClient* client;
        WAVEFORMATEX* format;
        Wasapi.ThrowIfFailed(s.OutDevice->Activate(__uuidof<IAudioClient>(), (uint)CLSCTX_ALL, null, (void**)&client), "opening the output device");
        s.Output = client;
        Wasapi.ThrowIfFailed(client->GetMixFormat(&format), "reading the output format");
        s.OutFormat = format;
        FormatOf(format); // throws for an unsupported format
        int outChannels = format->nChannels;
        double rate = format->nSamplesPerSec;
        s.Ready = CreateEventW(null, BOOL.FALSE, BOOL.FALSE, null);
        InitializeOutput(client, format, rate);
        Wasapi.ThrowIfFailed(client->SetEventHandle(s.Ready), "setting the output event");
        uint bufferFrames;
        Wasapi.ThrowIfFailed(client->GetBufferSize(&bufferFrames), "reading the output buffer size");
        IAudioRenderClient* render;
        Wasapi.ThrowIfFailed(client->GetService(__uuidof<IAudioRenderClient>(), (void**)&render), "opening the output stream");
        s.Render = render;
        long latency;
        client->GetStreamLatency(&latency);

        int inChannels = 0;
        if (s.InDevice is not null)
        {
            IAudioClient* inClient;
            WAVEFORMATEX* inFormat;
            Wasapi.ThrowIfFailed(s.InDevice->Activate(__uuidof<IAudioClient>(), (uint)CLSCTX_ALL, null, (void**)&inClient), "opening the input device");
            s.Input = inClient;
            Wasapi.ThrowIfFailed(inClient->GetMixFormat(&inFormat), "reading the input format");
            s.InFormat = inFormat;
            if (inFormat->nSamplesPerSec != format->nSamplesPerSec)
                throw new InvalidOperationException($"The input works at {inFormat->nSamplesPerSec} Hz and the output at {format->nSamplesPerSec} Hz: "
                    + "choose the same sample rate for both in the sound settings of Windows.");
            FormatOf(inFormat);
            inChannels = inFormat->nChannels;
            Wasapi.ThrowIfFailed(inClient->Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, 0, HundredNanosecondsPerSecond / 10, 0, inFormat, null),
                "starting the input device");
            IAudioCaptureClient* capture;
            Wasapi.ThrowIfFailed(inClient->GetService(__uuidof<IAudioCaptureClient>(), (void**)&capture), "opening the input stream");
            s.Capture = capture;
        }

        SampleRate = rate;
        BlockSize = (int)bufferFrames;
        OutputChannels = outChannels;
        InputChannels = inChannels;
        OutputLatencySamples = (int)(latency * rate / HundredNanosecondsPerSecond) + (int)bufferFrames;

        int frames = BlockSize;
        s.OutMemory = (float*)NativeMemory.AllocZeroed((nuint)(outChannels * frames), sizeof(float));
        s.OutPointers = (float**)NativeMemory.Alloc((nuint)Math.Max(1, outChannels), (nuint)sizeof(float*));
        for (int c = 0; c < outChannels; c++) s.OutPointers[c] = s.OutMemory + c * frames;
        if (inChannels > 0)
        {
            s.RingCapacity = Math.Max(4 * frames, (int)rate / 5);
            s.InMemory = (float*)NativeMemory.AllocZeroed((nuint)(inChannels * frames), sizeof(float));
            s.InPointers = (float**)NativeMemory.Alloc((nuint)inChannels, (nuint)sizeof(float*));
            for (int c = 0; c < inChannels; c++) s.InPointers[c] = s.InMemory + c * frames;
            s.Ring = (float*)NativeMemory.AllocZeroed((nuint)(inChannels * s.RingCapacity), sizeof(float));
        }
    }

    private void Stream(Session s)
    {
        IAudioClient* output = s.Output, input = s.Input;
        IAudioRenderClient* render = s.Render;
        IAudioCaptureClient* capture = s.Capture;
        float* ring = s.Ring;
        float** outPointers = s.OutPointers, inPointers = s.InPointers;
        int ringCapacity = s.RingCapacity, ringRead = 0, ringCount = 0;
        uint bufferFrames = (uint)BlockSize;
        int outChannels = OutputChannels, inChannels = InputChannels;
        var outSample = FormatOf(s.OutFormat);
        var inSample = s.InFormat is null ? SampleFormat.Float32 : FormatOf(s.InFormat);

        Wasapi.ThrowIfFailed(output->Start(), "starting the output");
        if (input is not null) Wasapi.ThrowIfFailed(input->Start(), "starting the input");
        try
        {
            while (!_stop)
            {
                if (WaitForSingleObject(s.Ready, 200) != WAIT.WAIT_OBJECT_0) continue;
                if (_stop) break;

                // Input: everything the device captured goes to the ring buffer (the oldest is dropped when full).
                if (capture is not null)
                {
                    uint packet;
                    while (capture->GetNextPacketSize(&packet).SUCCEEDED && packet > 0)
                    {
                        byte* data;
                        uint got, flags;
                        if (capture->GetBuffer(&data, &got, &flags, null, null).FAILED) break;
                        bool silent = (flags & (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                        for (int i = 0; i < got; i++)
                        {
                            int slot = (ringRead + ringCount) % ringCapacity;
                            for (int c = 0; c < inChannels; c++) ring[slot * inChannels + c] = silent ? 0f : ReadSample(data, i * inChannels + c, inSample);
                            if (ringCount < ringCapacity) ringCount++;
                            else ringRead = (ringRead + 1) % ringCapacity;
                        }
                        capture->ReleaseBuffer(got);
                    }
                }

                uint padding;
                var hr = output->GetCurrentPadding(&padding);
                if (hr.FAILED)
                {
                    RaiseFailed(Wasapi.Describe(hr));
                    break;
                }
                int available = (int)(bufferFrames - padding);
                if (available <= 0) continue;

                if (inChannels > 0)
                {
                    // Keep the input latency bounded: at most two blocks waiting.
                    int excess = ringCount - 3 * available;
                    if (excess > 0)
                    {
                        ringRead = (ringRead + excess) % ringCapacity;
                        ringCount -= excess;
                    }
                    int take = Math.Min(available, ringCount);
                    for (int i = 0; i < available; i++)
                    {
                        for (int c = 0; c < inChannels; c++)
                            inPointers[c][i] = i < take ? ring[((ringRead + i) % ringCapacity) * inChannels + c] : 0f;
                    }
                    ringRead = (ringRead + take) % ringCapacity;
                    ringCount -= take;
                }

                var inputs = new AudioBuffers(inPointers, inChannels, available);
                var outputs = new AudioBuffers(outPointers, outChannels, available);
                _renderer!.Render(inputs, outputs);

                byte* target;
                hr = render->GetBuffer((uint)available, &target);
                if (hr.FAILED)
                {
                    RaiseFailed(Wasapi.Describe(hr));
                    break;
                }
                for (int i = 0; i < available; i++)
                {
                    for (int c = 0; c < outChannels; c++) WriteSample(target, i * outChannels + c, outPointers[c][i], outSample);
                }
                render->ReleaseBuffer((uint)available, 0);
            }
        }
        finally
        {
            output->Stop();
            if (input is not null) input->Stop();
        }
    }

    private void InitializeOutput(IAudioClient* output, WAVEFORMATEX* format, double rate)
    {
        // Windows 10: IAudioClient3 gives periods shorter than 10 ms in shared mode (often 2.7 ms at 48 kHz).
        IAudioClient3* client3;
        if (output->QueryInterface(__uuidof<IAudioClient3>(), (void**)&client3).SUCCEEDED)
        {
            try
            {
                uint defaultPeriod, fundamental, minimum, maximum;
                if (client3->GetSharedModeEnginePeriod(format, &defaultPeriod, &fundamental, &minimum, &maximum).SUCCEEDED && fundamental > 0)
                {
                    uint period = minimum;
                    if (_requestedFrames > 0)
                    {
                        uint wanted = (uint)Math.Clamp(_requestedFrames, (int)minimum, (int)maximum);
                        period = Math.Min(maximum, (wanted + fundamental - 1) / fundamental * fundamental);
                    }
                    if (client3->InitializeSharedAudioStream((uint)AUDCLNT.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, period, format, null).SUCCEEDED) return;
                }
            }
            finally
            {
                client3->Release();
            }
        }
        long duration = _requestedFrames > 0 ? (long)(_requestedFrames * HundredNanosecondsPerSecond / rate) : 0; // 0 = engine default (10 ms)
        Wasapi.ThrowIfFailed(output->Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, (uint)AUDCLNT.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, duration, 0, format, null),
            "starting the output device");
    }

    private static SampleFormat FormatOf(WAVEFORMATEX* format)
    {
        bool isFloat = format->wFormatTag == WAVE.WAVE_FORMAT_IEEE_FLOAT
            || format->wFormatTag == WAVE.WAVE_FORMAT_EXTENSIBLE && ((WAVEFORMATEXTENSIBLE*)format)->SubFormat == *(Guid*)__uuidof<KSDATAFORMAT_SUBTYPE_IEEE_FLOAT>();
        if (isFloat && format->wBitsPerSample == 32) return SampleFormat.Float32;
        return format->wBitsPerSample switch
        {
            16 => SampleFormat.Int16,
            24 => SampleFormat.Int24,
            32 => SampleFormat.Int32,
            _ => throw new NotSupportedException($"Unsupported device format ({format->wBitsPerSample}-bit)."),
        };
    }

    private static float ReadSample(byte* data, int index, SampleFormat format) => format switch
    {
        SampleFormat.Float32 => ((float*)data)[index],
        SampleFormat.Int16 => ((short*)data)[index] * (1f / 32768f),
        SampleFormat.Int24 => (data[index * 3] << 8 | data[index * 3 + 1] << 16 | data[index * 3 + 2] << 24) * (1f / 2147483648f),
        _ => ((int*)data)[index] * (1f / 2147483648f),
    };

    private static void WriteSample(byte* data, int index, float value, SampleFormat format)
    {
        value = Math.Clamp(value, -1f, 1f);
        switch (format)
        {
            case SampleFormat.Float32:
                ((float*)data)[index] = value;
                break;
            case SampleFormat.Int16:
                ((short*)data)[index] = (short)MathF.Round(value * 32767f);
                break;
            case SampleFormat.Int24:
            {
                int v = (int)MathF.Round(value * 8388607f);
                data[index * 3] = (byte)v;
                data[index * 3 + 1] = (byte)(v >> 8);
                data[index * 3 + 2] = (byte)(v >> 16);
                break;
            }
            default:
                ((int*)data)[index] = (int)Math.Round(value * 2147483647.0);
                break;
        }
    }

    [DllImport("avrt.dll", ExactSpelling = true)]
    private static extern HANDLE AvSetMmThreadCharacteristicsW(char* taskName, uint* taskIndex);

    [DllImport("avrt.dll", ExactSpelling = true)]
    private static extern BOOL AvRevertMmThreadCharacteristics(HANDLE handle);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _opened.Dispose();
        _go.Dispose();
    }
}
