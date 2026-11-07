// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Host.Audio;

/// <summary>What feeds the input of a plug-in in a <see cref="PluginRack"/>.</summary>
/// <remarks>
/// <see cref="Read"/> runs on the audio thread: no allocation. Properties may be changed from the user interface while
/// the source plays (they are read once per block).
/// </remarks>
public abstract class SignalSource
{
    /// <summary>Display name.</summary>
    public abstract string Name { get; }

    /// <summary>Called when the sample rate is known or changes (not on the audio thread).</summary>
    public virtual void Prepare(double sampleRate) { }

    /// <summary>Fills <paramref name="destination"/> (the plug-in's main input channels).</summary>
    /// <param name="destination">Channels to fill.</param>
    /// <param name="deviceInputs">Input channels of the audio device (may be empty).</param>
    public abstract void Read(in AudioBuffers destination, in AudioBuffers deviceInputs);

    /// <summary>Converts decibels to a linear gain.</summary>
    public static float DbToGain(double db) => db <= -150.0 ? 0f : (float)Math.Pow(10.0, db / 20.0);
}

/// <summary>Silent input.</summary>
public sealed class SilenceSource : SignalSource
{
    /// <summary>Shared instance.</summary>
    public static SilenceSource Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "Silence";

    /// <inheritdoc />
    public override void Read(in AudioBuffers destination, in AudioBuffers deviceInputs) => destination.Clear();
}

/// <summary>The input channels of the audio device: device channel <c>c</c> feeds plug-in input <c>c</c> (mono inputs feed every channel).</summary>
public sealed class DeviceInputSource : SignalSource
{
    /// <summary>Gain in decibels.</summary>
    public double GainDb { get; set; }

    /// <inheritdoc />
    public override string Name => "Audio input";

    /// <inheritdoc />
    public override void Read(in AudioBuffers destination, in AudioBuffers deviceInputs)
    {
        if (deviceInputs.ChannelCount == 0)
        {
            destination.Clear();
            return;
        }
        float gain = DbToGain(GainDb);
        for (int c = 0; c < destination.ChannelCount; c++)
        {
            var source = deviceInputs[c % deviceInputs.ChannelCount];
            var target = destination[c];
            for (int i = 0; i < target.Length; i++) target[i] = source[i] * gain;
        }
    }
}

/// <summary>Waveforms of <see cref="TestSignalGenerator"/>.</summary>
public enum TestSignal
{
    /// <summary>Sine at <see cref="TestSignalGenerator.Frequency"/>.</summary>
    Sine,
    /// <summary>Square (band-unlimited).</summary>
    Square,
    /// <summary>Rising sawtooth (band-unlimited).</summary>
    Sawtooth,
    /// <summary>Triangle.</summary>
    Triangle,
    /// <summary>White noise.</summary>
    WhiteNoise,
    /// <summary>Pink noise (−3 dB per octave).</summary>
    PinkNoise,
    /// <summary>Logarithmic sine sweep from <see cref="TestSignalGenerator.Frequency"/> to <see cref="TestSignalGenerator.EndFrequency"/>, repeated.</summary>
    Sweep,
    /// <summary>One-sample impulses every <see cref="TestSignalGenerator.Period"/> seconds.</summary>
    Impulses,
}

/// <summary>Test signals: tones, noises, sweeps and impulses, the same on every channel.</summary>
public sealed class TestSignalGenerator : SignalSource
{
    private double _sampleRate = 48000;
    private double _phase, _sweepTime;
    private long _impulseCountdown;
    private uint _noise = 0x12345678;
    private float _b0, _b1, _b2, _b3, _b4, _b5, _b6;

    /// <summary>Waveform.</summary>
    public TestSignal Signal { get; set; }

    /// <summary>Frequency of tones, start of sweeps (Hz).</summary>
    public double Frequency { get; set; } = 1000.0;

    /// <summary>End of sweeps (Hz).</summary>
    public double EndFrequency { get; set; } = 20000.0;

    /// <summary>Duration of one sweep (seconds).</summary>
    public double SweepSeconds { get; set; } = 5.0;

    /// <summary>Interval between impulses (seconds).</summary>
    public double Period { get; set; } = 1.0;

    /// <summary>Level in dBFS (peak for tones and impulses, RMS for noises).</summary>
    public double LevelDb { get; set; } = -20.0;

    /// <inheritdoc />
    public override string Name => "Test signal";

    /// <inheritdoc />
    public override void Prepare(double sampleRate)
    {
        _sampleRate = sampleRate;
        _phase = 0;
        _sweepTime = 0;
        _impulseCountdown = 0;
    }

    /// <inheritdoc />
    public override void Read(in AudioBuffers destination, in AudioBuffers deviceInputs)
    {
        if (destination.ChannelCount == 0) return;
        var first = destination[0];
        float gain = DbToGain(LevelDb);
        double sr = _sampleRate;
        switch (Signal)
        {
            case TestSignal.Sine or TestSignal.Square or TestSignal.Sawtooth or TestSignal.Triangle:
            {
                double step = Math.Clamp(Frequency, 0.0, sr / 2) / sr;
                var signal = Signal;
                for (int i = 0; i < first.Length; i++)
                {
                    double p = _phase;
                    double v = signal switch
                    {
                        TestSignal.Sine => Math.Sin(2.0 * Math.PI * p),
                        TestSignal.Square => p < 0.5 ? 1.0 : -1.0,
                        TestSignal.Sawtooth => 2.0 * p - 1.0,
                        _ => p < 0.5 ? 4.0 * p - 1.0 : 3.0 - 4.0 * p,
                    };
                    first[i] = (float)v * gain;
                    _phase = p + step;
                    if (_phase >= 1.0) _phase -= 1.0;
                }
                break;
            }
            case TestSignal.WhiteNoise:
            {
                float g = gain * 1.7320508f; // uniform in [-1, 1] has an RMS of 1/√3
                for (int i = 0; i < first.Length; i++) first[i] = NextNoise() * g;
                break;
            }
            case TestSignal.PinkNoise:
            {
                // Paul Kellet's filter of white noise (refined version); about 1 RMS for this white noise.
                for (int i = 0; i < first.Length; i++)
                {
                    float white = NextNoise() * 0.5773503f;
                    _b0 = 0.99886f * _b0 + white * 0.0555179f;
                    _b1 = 0.99332f * _b1 + white * 0.0750759f;
                    _b2 = 0.96900f * _b2 + white * 0.1538520f;
                    _b3 = 0.86650f * _b3 + white * 0.3104856f;
                    _b4 = 0.55000f * _b4 + white * 0.5329522f;
                    _b5 = -0.7616f * _b5 - white * 0.0168980f;
                    float pink = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362f;
                    _b6 = white * 0.115926f;
                    first[i] = pink * gain;
                }
                break;
            }
            case TestSignal.Sweep:
            {
                double f1 = Math.Clamp(Frequency, 1.0, sr / 2), f2 = Math.Clamp(EndFrequency, 1.0, sr / 2);
                double duration = Math.Max(0.1, SweepSeconds);
                double ratio = Math.Log(f2 / f1);
                for (int i = 0; i < first.Length; i++)
                {
                    double f = f1 * Math.Exp(ratio * _sweepTime / duration);
                    first[i] = (float)Math.Sin(2.0 * Math.PI * _phase) * gain;
                    _phase += f / sr;
                    if (_phase >= 1.0) _phase -= Math.Floor(_phase);
                    _sweepTime += 1.0 / sr;
                    if (_sweepTime >= duration)
                    {
                        _sweepTime = 0;
                        _phase = 0;
                    }
                }
                break;
            }
            default:
            {
                long period = Math.Max(1, (long)Math.Round(Period * sr));
                for (int i = 0; i < first.Length; i++)
                {
                    if (_impulseCountdown <= 0)
                    {
                        first[i] = gain;
                        _impulseCountdown = period;
                    }
                    else
                    {
                        first[i] = 0f;
                    }
                    _impulseCountdown--;
                }
                break;
            }
        }
        for (int c = 1; c < destination.ChannelCount; c++) first.CopyTo(destination[c]);
    }

    private float NextNoise()
    {
        // xorshift32: uniform in [-1, 1).
        uint x = _noise;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _noise = x;
        return (int)x * (1f / 2147483648f);
    }
}

/// <summary>Plays an audio file (looped by default), resampled to the device rate when needed.</summary>
public sealed class FileSource : SignalSource
{
    private readonly AudioBuffer _audio;
    private double _position, _step = 1.0;

    /// <summary>Plays <paramref name="audio"/>.</summary>
    public FileSource(AudioBuffer audio, string name)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        FileName = name;
    }

    /// <summary>Loads a WAV file.</summary>
    public static FileSource Load(string path) => new(WavFile.Read(path), System.IO.Path.GetFileName(path));

    /// <summary>File name.</summary>
    public string FileName { get; }

    /// <summary>Audio played.</summary>
    public AudioBuffer Audio => _audio;

    /// <inheritdoc />
    public override string Name => FileName;

    /// <summary>Starts again at the end.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Gain in decibels.</summary>
    public double GainDb { get; set; }

    /// <summary>Paused (silence, position kept).</summary>
    public bool IsPaused { get; set; }

    /// <summary>Position in seconds (in the file).</summary>
    public double PositionSeconds
    {
        get => Volatile.Read(ref _position) / _audio.SampleRate;
        set => Volatile.Write(ref _position, Math.Clamp(value, 0.0, _audio.Duration) * _audio.SampleRate);
    }

    /// <inheritdoc />
    public override void Prepare(double sampleRate) => _step = _audio.SampleRate / sampleRate;

    /// <inheritdoc />
    public override void Read(in AudioBuffers destination, in AudioBuffers deviceInputs)
    {
        int length = _audio.Length, channels = _audio.ChannelCount;
        if (IsPaused || length == 0 || channels == 0)
        {
            destination.Clear();
            return;
        }
        float gain = DbToGain(GainDb);
        double position = _position;
        int frames = destination.Frames;
        for (int c = 0; c < destination.ChannelCount; c++)
        {
            float[] source = _audio.Channels[c % channels];
            var target = destination[c];
            double p = position;
            for (int i = 0; i < frames; i++)
            {
                if (p >= length)
                {
                    if (!Loop)
                    {
                        target[i..].Clear();
                        break;
                    }
                    p -= length;
                }
                int i0 = (int)p;
                float frac = (float)(p - i0);
                float a = source[i0], b = i0 + 1 < length ? source[i0 + 1] : Loop ? source[0] : 0f;
                target[i] = (a + (b - a) * frac) * gain;
                p += _step;
            }
        }
        position += frames * _step;
        if (position >= length) position = Loop ? position % length : length;
        _position = position;
    }
}
