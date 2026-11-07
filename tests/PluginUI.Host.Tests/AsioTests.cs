// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PluginUI.Host.Audio;
using PluginUI.Host.Windows;

namespace PluginUI.Host.Tests;

/// <summary>
/// An ASIO driver object in this process: the vtable of IASIO with managed methods. It records what the host asks and
/// lets the test play the role of the driver's thread (<see cref="Switch"/>).
/// </summary>
internal sealed unsafe class FakeAsio : IDisposable
{
    public const int Inputs = 2, Outputs = 4;
    public static readonly int[] OutputTypes = [18 /* Int32LSB */, 16 /* Int16LSB */, 19 /* Float32LSB */, 27 /* Int32LSB24 */];
    public const int InputType = 17; // Int24LSB

    private static FakeAsio? s_instance;
    private readonly void** _vtable;
    private readonly nint* _object;
    private readonly List<nint> _buffers = [];

    public FakeAsio()
    {
        _vtable = (void**)NativeMemory.AllocZeroed(24, (nuint)sizeof(void*));
        _vtable[2] = (delegate* unmanaged<nint, uint>)&Release;
        _vtable[3] = (delegate* unmanaged<nint, nint, int>)&Init;
        _vtable[4] = (delegate* unmanaged<nint, byte*, void>)&GetDriverName;
        _vtable[5] = (delegate* unmanaged<nint, int>)&GetDriverVersion;
        _vtable[6] = (delegate* unmanaged<nint, byte*, void>)&GetErrorMessage;
        _vtable[7] = (delegate* unmanaged<nint, int>)&Start;
        _vtable[8] = (delegate* unmanaged<nint, int>)&Stop;
        _vtable[9] = (delegate* unmanaged<nint, int*, int*, int>)&GetChannels;
        _vtable[10] = (delegate* unmanaged<nint, int*, int*, int>)&GetLatencies;
        _vtable[11] = (delegate* unmanaged<nint, int*, int*, int*, int*, int>)&GetBufferSize;
        _vtable[12] = (delegate* unmanaged<nint, double, int>)&CanSampleRate;
        _vtable[13] = (delegate* unmanaged<nint, double*, int>)&GetSampleRate;
        _vtable[14] = (delegate* unmanaged<nint, double, int>)&SetSampleRate;
        _vtable[18] = (delegate* unmanaged<nint, int*, int>)&GetChannelInfo;
        _vtable[19] = (delegate* unmanaged<nint, nint*, int, int, nint*, int>)&CreateBuffers;
        _vtable[20] = (delegate* unmanaged<nint, int>)&DisposeBuffers;
        _vtable[21] = (delegate* unmanaged<nint, int>)&ControlPanel;
        _vtable[23] = (delegate* unmanaged<nint, int>)&OutputReady;
        _object = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
        *_object = (nint)_vtable;
        s_instance = this;
    }

    public nint Object => (nint)_object;

    public double Rate { get; set; } = 48000;

    public int BufferSize { get; private set; }

    public int Released, Inits, Starts, Stops, Disposed, Panels, OutputReadies;

    /// <summary>The ASIOBufferInfo array the host passed (isInput, channel, buffer0, buffer1 per channel).</summary>
    public nint* Infos { get; private set; }

    public nint* CallbackTable { get; private set; }

    public byte* Buffer(int channel, int half) => (byte*)Infos[channel * 3 + 1 + half];

    /// <summary>The driver's thread: asks for buffer half <paramref name="half"/>.</summary>
    public void Switch(int half) => ((delegate* unmanaged<int, int, void>)CallbackTable[0])(half, 1);

    public int Message(int selector, int value) => ((delegate* unmanaged<int, int, void*, double*, int>)CallbackTable[2])(selector, value, null, null);

    public void Dispose()
    {
        foreach (var b in _buffers) NativeMemory.Free((void*)b);
        NativeMemory.Free(_object);
        NativeMemory.Free(_vtable);
        if (ReferenceEquals(s_instance, this)) s_instance = null;
    }

    private static FakeAsio F => s_instance!;

    private static int Bytes(int type) => type switch { 16 => 2, 17 => 3, 20 => 8, _ => 4 };

    [UnmanagedCallersOnly] private static uint Release(nint self) => (uint)Interlocked.Increment(ref F.Released);
    [UnmanagedCallersOnly] private static int Init(nint self, nint window) { F.Inits++; return 1; }
    [UnmanagedCallersOnly] private static void GetDriverName(nint self, byte* name) { "Fake ASIO\0"u8.CopyTo(new Span<byte>(name, 32)); }
    [UnmanagedCallersOnly] private static int GetDriverVersion(nint self) => 3;
    [UnmanagedCallersOnly] private static void GetErrorMessage(nint self, byte* text) { text[0] = 0; }
    [UnmanagedCallersOnly] private static int Start(nint self) { F.Starts++; return 0; }
    [UnmanagedCallersOnly] private static int Stop(nint self) { F.Stops++; return 0; }
    [UnmanagedCallersOnly] private static int GetChannels(nint self, int* inputs, int* outputs) { *inputs = Inputs; *outputs = Outputs; return 0; }
    [UnmanagedCallersOnly] private static int GetLatencies(nint self, int* input, int* output) { *input = 64; *output = 96; return 0; }

    [UnmanagedCallersOnly]
    private static int GetBufferSize(nint self, int* min, int* max, int* preferred, int* granularity)
    {
        *min = 32;
        *max = 1024;
        *preferred = 256;
        *granularity = -1;
        return 0;
    }

    [UnmanagedCallersOnly] private static int CanSampleRate(nint self, double rate) => rate is 44100 or 48000 or 96000 ? 0 : -995;
    [UnmanagedCallersOnly] private static int GetSampleRate(nint self, double* rate) { *rate = F.Rate; return 0; }
    [UnmanagedCallersOnly] private static int SetSampleRate(nint self, double rate) { F.Rate = rate; return 0; }

    [UnmanagedCallersOnly]
    private static int GetChannelInfo(nint self, int* info)
    {
        // channel, isInput, isActive, group, type, name[32]
        int channel = info[0];
        bool input = info[1] != 0;
        info[4] = input ? InputType : OutputTypes[channel];
        var name = System.Text.Encoding.ASCII.GetBytes((input ? "Analog In " : "Line Out ") + (channel + 1) + "\0");
        name.CopyTo(new Span<byte>((byte*)(info + 5), 32));
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int CreateBuffers(nint self, nint* infos, int count, int size, nint* callbacks)
    {
        var f = F;
        f.Infos = infos;
        f.CallbackTable = callbacks;
        f.BufferSize = size;
        for (int i = 0; i < count; i++)
        {
            // ASIOBufferInfo: int isInput, int channel (one pointer-sized slot on x64), then the two buffers.
            int isInput = (int)(infos[i * 3] & 0xFFFFFFFF), channel = (int)(infos[i * 3] >> 32);
            int bytes = Bytes(isInput != 0 ? InputType : OutputTypes[channel]) * size;
            for (int half = 0; half < 2; half++)
            {
                var b = (nint)NativeMemory.AllocZeroed((nuint)bytes);
                f._buffers.Add(b);
                infos[i * 3 + 1 + half] = b;
            }
        }
        return 0;
    }

    [UnmanagedCallersOnly] private static int DisposeBuffers(nint self) { F.Disposed++; return 0; }
    [UnmanagedCallersOnly] private static int ControlPanel(nint self) { F.Panels++; return 0; }
    [UnmanagedCallersOnly] private static int OutputReady(nint self) { F.OutputReadies++; return 0; }
}

[NonParallelizable]
public unsafe class AsioTests
{
    /// <summary>Copies input 1 at half level to every output, and records what Prepare received.</summary>
    private sealed class HalfCopy : IAudioRenderer
    {
        public (double Rate, int Frames, int Inputs, int Outputs) Prepared;
        public bool Throw;

        public void Prepare(double sampleRate, int maxFrames, int inputChannels, int outputChannels) => Prepared = (sampleRate, maxFrames, inputChannels, outputChannels);

        public void Render(in AudioBuffers inputs, in AudioBuffers outputs)
        {
            if (Throw) throw new InvalidOperationException("renderer failed");
            for (int c = 0; c < outputs.ChannelCount; c++)
            {
                for (int i = 0; i < outputs.Frames; i++) outputs[c][i] = inputs.ChannelCount > 0 ? inputs[0][i] * 0.5f : 0f;
            }
        }
    }

    [Test]
    public void DriverIsConfiguredAndBuffersAreConverted()
    {
        if (!Environment.Is64BitProcess) Assert.Ignore("ASIO is 64-bit only.");
        using var fake = new FakeAsio();
        var renderer = new HalfCopy();
        using var driver = AsioDriver.FromObject(fake.Object, "Fake", bufferFrames: 100, sampleRate: 96000, inputChannels: -1);
        driver.Start(renderer);

        Assert.That(driver.DriverName, Is.EqualTo("Fake ASIO"));
        Assert.That(driver.Name, Is.EqualTo("ASIO — Fake ASIO"));
        Assert.That(fake.Rate, Is.EqualTo(96000));
        Assert.That(fake.BufferSize, Is.EqualTo(128), "100 samples, powers of two: 128");
        Assert.That(renderer.Prepared, Is.EqualTo((96000.0, 128, 2, 4)));
        Assert.That(driver.OutputLatencySamples, Is.EqualTo(96));
        Assert.That(driver.InputLatencySamples, Is.EqualTo(64));
        Assert.That(driver.SupportedSampleRates, Is.EqualTo(new[] { 44100, 48000, 96000 }));
        Assert.That(driver.OutputChannelNames, Is.EqualTo(new[] { "Line Out 1", "Line Out 2", "Line Out 3", "Line Out 4" }));
        Assert.That(driver.InputChannelNames[1], Is.EqualTo("Analog In 2"));
        Assert.That(driver.SampleFormat, Is.EqualTo("Int32LSB"));
        Assert.That(fake.Starts, Is.EqualTo(1));

        // Input 1, half 1: a 24-bit ramp.
        byte* input = fake.Buffer(0, 1);
        for (int i = 0; i < 128; i++)
        {
            int v = (i - 64) * 65536; // -0.5 .. +0.49
            input[i * 3] = (byte)v;
            input[i * 3 + 1] = (byte)(v >> 8);
            input[i * 3 + 2] = (byte)(v >> 16);
        }
        int readiesBefore = fake.OutputReadies;
        fake.Switch(1);
        Assert.That(fake.OutputReadies, Is.EqualTo(readiesBefore + 1));
        for (int i = 0; i < 128; i += 17)
        {
            double expected = (i - 64) * 65536 / 8388608.0 * 0.5;
            Assert.That(((int*)fake.Buffer(FakeAsio.Inputs + 0, 1))[i] / 2147483648.0, Is.EqualTo(expected).Within(1e-6), "Int32LSB");
            Assert.That(((short*)fake.Buffer(FakeAsio.Inputs + 1, 1))[i] / 32768.0, Is.EqualTo(expected).Within(1e-4), "Int16LSB");
            Assert.That(((float*)fake.Buffer(FakeAsio.Inputs + 2, 1))[i], Is.EqualTo(expected).Within(1e-6), "Float32LSB");
            Assert.That(((int*)fake.Buffer(FakeAsio.Inputs + 3, 1))[i] / 8388608.0, Is.EqualTo(expected).Within(1e-6), "Int32LSB24");
            Assert.That(((int*)fake.Buffer(FakeAsio.Inputs + 0, 0))[i], Is.Zero, "the other half is untouched");
        }

        driver.ShowControlPanel();
        Assert.That(fake.Panels, Is.EqualTo(1));
        driver.Stop();
        Assert.That(fake.Stops, Is.EqualTo(1));
        Assert.That(fake.Disposed, Is.EqualTo(1));
        Assert.That(driver.IsRunning, Is.False);
        driver.Dispose();
        Assert.That(fake.Released, Is.EqualTo(1));
    }

    [Test]
    public void MessagesAreAnsweredAndResetsAreRequested()
    {
        if (!Environment.Is64BitProcess) Assert.Ignore("ASIO is 64-bit only.");
        using var fake = new FakeAsio();
        using var driver = AsioDriver.FromObject(fake.Object, "Fake");
        using var reset = new ManualResetEventSlim();
        driver.ResetRequested += _ => reset.Set();
        driver.Start(new HalfCopy());
        Assert.That(driver.InputChannels, Is.Zero, "no input unless asked");
        Assert.That(fake.BufferSize, Is.EqualTo(256), "the preferred size");
        Assert.That(driver.SampleRate, Is.EqualTo(48000));

        Assert.That(fake.Message(1, 3), Is.EqualTo(1), "kAsioResetRequest is supported");
        Assert.That(fake.Message(1, 7), Is.EqualTo(1), "kAsioSupportsTimeInfo is supported");
        Assert.That(fake.Message(1, 15), Is.Zero, "kAsioOverload is not");
        Assert.That(fake.Message(2, 0), Is.EqualTo(2), "engine version 2");
        Assert.That(fake.Message(3, 0), Is.EqualTo(1));
        Assert.That(reset.Wait(5000), Is.True, "kAsioResetRequest raises ResetRequested");

        // The host starts again, as an application would on ResetRequested.
        driver.Start(new HalfCopy());
        Assert.That(fake.Starts, Is.EqualTo(2));
        Assert.That(fake.Inits, Is.EqualTo(1), "the driver is loaded once");
    }

    [Test]
    public void OnlyOneAsioDriverRunsAndRendererFailuresAreReported()
    {
        if (!Environment.Is64BitProcess) Assert.Ignore("ASIO is 64-bit only.");
        using var fake = new FakeAsio();
        var renderer = new HalfCopy { Throw = true };
        using var driver = AsioDriver.FromObject(fake.Object, "Fake");
        using var failed = new ManualResetEventSlim();
        string? message = null;
        driver.Failed += (_, m) => { message = m; failed.Set(); };
        driver.Start(renderer);

        using var second = AsioDriver.FromObject(fake.Object, "Other");
        Assert.Throws<InvalidOperationException>(() => second.Start(new HalfCopy()));

        var output = (int*)fake.Buffer(0, 0);
        output[5] = 12345;
        fake.Switch(0);
        Assert.That(failed.Wait(5000), Is.True);
        Assert.That(message, Is.EqualTo("renderer failed"));
        Assert.That(output[5], Is.Zero, "silence after a failure");
        driver.Stop();
    }

    [TestCase(0, 256)]
    [TestCase(100, 128)]
    [TestCase(180, 128)]
    [TestCase(190, 256)]
    [TestCase(5000, 1024)]
    [TestCase(1, 32)]
    public void BufferSizesFollowTheDriver(int requested, int expected)
        => Assert.That(Asio.ChooseBufferSize(requested, new AsioBufferSizes(32, 1024, 256, -1)), Is.EqualTo(expected));

    [Test]
    public void BufferSizesWithAStep()
    {
        Assert.That(Asio.ChooseBufferSize(100, new AsioBufferSizes(64, 2048, 512, 32)), Is.EqualTo(96));
        Assert.That(Asio.ChooseBufferSize(100, new AsioBufferSizes(64, 2048, 512, 0)), Is.EqualTo(512), "a fixed size");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(18)]
    [TestCase(19)]
    [TestCase(20)]
    [TestCase(24)]
    [TestCase(25)]
    [TestCase(26)]
    [TestCase(27)]
    public void SampleFormatsRoundTrip(int format)
    {
        var type = (AsioSampleType)format;
        Assert.That(AsioSamples.IsSupported(type), Is.True);
        float[] values = [-1f, -0.5f, -0.123f, 0f, 0.25f, 0.999f, 2f, -3f, float.NaN];
        float[] expected = [-1f, -0.5f, -0.123f, 0f, 0.25f, 0.999f, 1f, -1f, 0f];
        int bits = (format & 15) switch { 0 => 16, 8 => 16, 9 => 18, 10 => 20, 1 or 11 => 24, _ => 32 };
        bool isFloat = (format & 15) is 3 or 4;
        double tolerance = isFloat ? 1e-7 : 1.5 / (1L << (bits - 1));
        var bytes = new byte[values.Length * 8];
        var back = new float[values.Length];
        fixed (float* v = values)
        fixed (byte* b = bytes)
        fixed (float* r = back)
        {
            AsioSamples.FromFloat(v, type, b, values.Length);
            AsioSamples.ToFloat(b, type, r, values.Length);
        }
        for (int i = 0; i < values.Length; i++)
        {
            if (isFloat && i is 6 or 7) continue; // float formats are not clipped
            Assert.That(back[i], Is.EqualTo(expected[i]).Within(tolerance), $"value {values[i]}");
        }
        if (format is 0 or 1 or 2)
        {
            // Big-endian: the most significant byte first (0.25 is positive and not zero there).
            Assert.That(bytes[AsioSamples.BytesPerSample(type) * 4], Is.Not.Zero);
        }
    }

    [Test]
    public void DsdIsNotSupported() => Assert.That(AsioSamples.IsSupported((AsioSampleType)32), Is.False);

    [Test]
    public void NoDriverOrRegistryGivesAnEmptyList()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("The registry is Windows only.");
        Assert.That(Asio.GetDrivers(), Is.Not.Null);
    }
}
