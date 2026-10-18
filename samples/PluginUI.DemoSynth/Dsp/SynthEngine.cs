// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

namespace PluginUI.DemoSynth.Dsp;

/// <summary>Sound settings of the synth, read once per block from the parameters.</summary>
public struct SynthSettings
{
    /// <summary>Oscillator shape: 0 = sawtooth, 1 = square.</summary>
    public float Shape;

    /// <summary>Detune between the two oscillators, in cents.</summary>
    public double DetuneCents;

    /// <summary>Filter cutoff in Hz.</summary>
    public double Cutoff;

    /// <summary>Filter resonance in [0, 1].</summary>
    public double Resonance;

    /// <summary>Filter envelope depth in octaves (negative: closes the filter).</summary>
    public double FilterEnvOctaves;

    /// <summary>Amplitude envelope.</summary>
    public double AmpAttack, AmpDecay, AmpSustain, AmpRelease;

    /// <summary>Filter envelope.</summary>
    public double FilterAttack, FilterDecay, FilterSustain, FilterRelease;

    /// <summary>Pitch bend in semitones.</summary>
    public double PitchBendSemitones;

    /// <summary>Mod wheel in [0, 1] (vibrato depth).</summary>
    public double ModWheel;

    /// <summary>Output gain (linear).</summary>
    public float Volume;

    /// <summary>Side-chain ducking depth in [0, 1].</summary>
    public float Duck;

    /// <summary>Side-chain release time in seconds.</summary>
    public double DuckRelease;

    /// <summary>Default sound (matches the parameter defaults).</summary>
    public static SynthSettings Default => new()
    {
        Shape = 0f, DetuneCents = 8.0, Cutoff = 2400.0, Resonance = 0.2, FilterEnvOctaves = 2.0,
        AmpAttack = 0.005, AmpDecay = 0.3, AmpSustain = 0.7, AmpRelease = 0.35,
        FilterAttack = 0.002, FilterDecay = 0.4, FilterSustain = 0.2, FilterRelease = 0.4,
        PitchBendSemitones = 0.0, ModWheel = 0.0, Volume = 0.35f, Duck = 0f, DuckRelease = 0.15,
    };
}

/// <summary>
/// Polyphonic subtractive synth: 16 voices of two detuned PolyBLEP oscillators, a resonant ZDF low-pass with its own
/// envelope, an amplitude envelope, vibrato from the mod wheel, pitch bend, and ducking from a side-chain input.
/// Allocation-free after construction; every method runs on the audio thread.
/// </summary>
/// <remarks>
/// Events are applied between <see cref="Render"/> calls, so the processor renders the block in segments that end
/// at each event's sample offset (sample-accurate timing). Filter coefficients and pitch are updated every
/// <see cref="ControlInterval"/> samples.
/// </remarks>
public sealed class SynthEngine
{
    /// <summary>Number of voices.</summary>
    public const int VoiceCount = 16;

    /// <summary>Samples between two updates of pitch and filter cutoff.</summary>
    public const int ControlInterval = 16;

    private const double VibratoHz = 5.5;
    private const double VibratoMaxCents = 50.0;
    private const float VoiceGain = 0.25f; // headroom for chords

    private readonly Voice[] _voices = new Voice[VoiceCount];
    private readonly NoteActivity? _activity;
    private SynthSettings _settings = SynthSettings.Default;
    private AdsrSettings _amp, _filter;
    private EnvelopeFollower _follower;
    private double _sampleRate = 48000.0;
    private double _lfoPhase;
    private float _volume, _duck;
    private long _age;

    /// <summary>Creates the engine.</summary>
    /// <param name="activity">Receives the held notes for the editor's keyboard (optional).</param>
    public SynthEngine(NoteActivity? activity = null)
    {
        _activity = activity;
        Setup(_sampleRate);
    }

    /// <summary>Sample rate in Hz.</summary>
    public double SampleRate => _sampleRate;

    /// <summary>Current settings.</summary>
    public ref readonly SynthSettings Settings => ref _settings;

    /// <summary>Number of voices producing sound.</summary>
    public int ActiveVoiceCount
    {
        get
        {
            int n = 0;
            foreach (ref readonly var v in _voices.AsSpan()) if (v.Amp.IsActive) n++;
            return n;
        }
    }

    /// <summary>Gain applied by the side-chain ducking during the last <see cref="Render"/> (1 = none).</summary>
    public float DuckGain { get; private set; } = 1f;

    /// <summary>Prepares for a sample rate and stops every voice.</summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    public void Setup(double sampleRate)
    {
        _sampleRate = sampleRate > 0.0 ? sampleRate : 48000.0;
        Reset();
        Apply(_settings);
        _volume = _settings.Volume;
        _duck = _settings.Duck;
    }

    /// <summary>Stops every voice immediately.</summary>
    public void Reset()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].IsHeld) _activity?.NoteOff(_voices[i].Pitch);
            _voices[i] = default;
        }
        _follower.Reset();
        _lfoPhase = 0.0;
    }

    /// <summary>Applies new settings (once per block).</summary>
    /// <param name="settings">The settings.</param>
    public void Apply(in SynthSettings settings)
    {
        _settings = settings;
        _amp = AdsrSettings.Create(settings.AmpAttack, settings.AmpDecay, settings.AmpSustain, settings.AmpRelease, _sampleRate);
        _filter = AdsrSettings.Create(settings.FilterAttack, settings.FilterDecay, settings.FilterSustain, settings.FilterRelease, _sampleRate);
        _follower.Setup(0.002, settings.DuckRelease, _sampleRate);
    }

    /// <summary>Starts a note (steals a voice when all are busy).</summary>
    /// <param name="pitch">MIDI note number.</param>
    /// <param name="velocity">Velocity in [0, 1]; 0 is a note-off.</param>
    /// <param name="channel">MIDI channel.</param>
    /// <param name="noteId">VST3 note id (−1 when unused).</param>
    public void NoteOn(int pitch, float velocity, int channel = 0, int noteId = -1)
    {
        if ((uint)pitch > 127) return;
        if (velocity <= 0f)
        {
            NoteOff(pitch, channel, noteId);
            return;
        }
        int index = FindVoice(pitch, channel);
        ref var v = ref _voices[index];
        if (v.IsHeld) _activity?.NoteOff(v.Pitch);
        bool restart = !v.Amp.IsActive;
        v.Pitch = pitch;
        v.Channel = channel;
        v.NoteId = noteId;
        v.IsHeld = true;
        v.Age = ++_age;
        v.Gain = VoiceGain * (0.25f + 0.75f * velocity * velocity);
        if (restart)
        {
            // Random-ish start phases (derived from the age) so that chords don't phase-align.
            v.Osc1.Reset((_age * 0.618034) % 1.0);
            v.Osc2.Reset((_age * 0.381966) % 1.0);
            v.Filter.Reset();
        }
        v.ControlCountdown = 0; // pitch and cutoff of the new note from its first sample (also for a stolen voice)
        v.Amp.Trigger();
        v.FilterEnv.Trigger();
        _activity?.NoteOn(pitch);
    }

    /// <summary>Releases a note.</summary>
    /// <param name="pitch">MIDI note number.</param>
    /// <param name="channel">MIDI channel.</param>
    /// <param name="noteId">VST3 note id: when not −1, only the voice started with this id is released.</param>
    public void NoteOff(int pitch, int channel = 0, int noteId = -1)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            ref var v = ref _voices[i];
            if (!v.IsHeld) continue;
            bool match = noteId != -1 && v.NoteId != -1 ? v.NoteId == noteId : v.Pitch == pitch && v.Channel == channel;
            if (!match) continue;
            v.IsHeld = false;
            v.Amp.Release();
            v.FilterEnv.Release();
            _activity?.NoteOff(v.Pitch);
        }
    }

    /// <summary>Releases every held note.</summary>
    public void AllNotesOff()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            ref var v = ref _voices[i];
            if (!v.IsHeld) continue;
            v.IsHeld = false;
            v.Amp.Release();
            v.FilterEnv.Release();
            _activity?.NoteOff(v.Pitch);
        }
    }

    private int FindVoice(int pitch, int channel)
    {
        // Same note retriggered: reuse its voice (no doubling). Then a free voice, then the quietest releasing
        // voice, then the oldest held one.
        int free = -1, releasing = -1, oldest = 0;
        double quietest = double.MaxValue;
        for (int i = 0; i < _voices.Length; i++)
        {
            ref readonly var v = ref _voices[i];
            if (v.Amp.IsActive && v.Pitch == pitch && v.Channel == channel) return i;
            if (!v.Amp.IsActive)
            {
                if (free < 0) free = i;
                continue;
            }
            if (!v.IsHeld && v.Amp.Level < quietest)
            {
                quietest = v.Amp.Level;
                releasing = i;
            }
            if (v.Age < _voices[oldest].Age) oldest = i;
        }
        return free >= 0 ? free : releasing >= 0 ? releasing : oldest;
    }

    /// <summary>Renders a segment (adds nothing to the outputs: they are overwritten).</summary>
    /// <param name="left">Left output.</param>
    /// <param name="right">Right output (same length as <paramref name="left"/>), or empty for a mono output.</param>
    /// <param name="sideLeft">Side-chain left input (empty: no side-chain).</param>
    /// <param name="sideRight">Side-chain right input (empty: mono side-chain or none).</param>
    public void Render(Span<float> left, Span<float> right, ReadOnlySpan<float> sideLeft = default, ReadOnlySpan<float> sideRight = default)
    {
        int n = left.Length;
        bool stereo = right.Length >= n;
        left.Clear();
        if (stereo) right[..n].Clear();

        double lfoIncrement = VibratoHz / _sampleRate;
        double vibratoDepth = _settings.ModWheel * VibratoMaxCents;
        double detuneRatio = Math.Pow(2.0, _settings.DetuneCents / 2400.0); // ± half the detune on each oscillator

        for (int i = 0; i < _voices.Length; i++)
        {
            ref var v = ref _voices[i];
            if (!v.Amp.IsActive) continue;
            double lfoPhase = _lfoPhase;
            for (int s = 0; s < n; s++)
            {
                if (v.ControlCountdown-- <= 0)
                {
                    v.ControlCountdown = ControlInterval - 1;
                    double vibrato = vibratoDepth * Math.Sin(2.0 * Math.PI * lfoPhase);
                    double semitones = v.Pitch - 69 + _settings.PitchBendSemitones + vibrato / 100.0;
                    double frequency = 440.0 * Math.Pow(2.0, semitones / 12.0);
                    v.Increment1 = Math.Min(0.45, frequency / detuneRatio / _sampleRate);
                    v.Increment2 = Math.Min(0.45, frequency * detuneRatio / _sampleRate);
                    double cutoff = _settings.Cutoff * Math.Pow(2.0, _settings.FilterEnvOctaves * v.FilterEnv.Level);
                    v.Filter.SetCoefficients(cutoff, _settings.Resonance, _sampleRate);
                }
                lfoPhase += lfoIncrement;
                if (lfoPhase >= 1.0) lfoPhase -= 1.0;

                float osc = 0.5f * (v.Osc1.Next(v.Increment1, _settings.Shape) + v.Osc2.Next(v.Increment2, _settings.Shape));
                v.FilterEnv.Next(_filter);
                float sample = v.Filter.Process(osc) * v.Amp.Next(_amp) * v.Gain;
                left[s] += sample;
                if (!v.Amp.IsActive)
                {
                    v.FilterEnv.Reset();
                    break;
                }
            }
        }

        _lfoPhase += lfoIncrement * n;
        _lfoPhase -= Math.Floor(_lfoPhase);

        // Output gain (smoothed) and side-chain ducking.
        bool hasSide = sideLeft.Length >= n;
        bool stereoSide = hasSide && sideRight.Length >= n;
        float volumeTarget = _settings.Volume, duckTarget = _settings.Duck;
        const float Smoothing = 0.002f;
        float minGain = 1f;
        for (int s = 0; s < n; s++)
        {
            _volume += (volumeTarget - _volume) * Smoothing;
            _duck += (duckTarget - _duck) * Smoothing;
            float gain = _volume;
            if (hasSide)
            {
                float level = Math.Abs(sideLeft[s]);
                if (stereoSide) level = Math.Max(level, Math.Abs(sideRight[s]));
                double envelope = _follower.Process(level);
                float duckGain = 1f - _duck * (float)Math.Min(1.0, envelope);
                if (duckGain < minGain) minGain = duckGain;
                gain *= duckGain;
            }
            left[s] *= gain;
        }
        if (stereo) left.CopyTo(right); // the voices are centered: both channels are identical
        if (!hasSide) _follower.Reset();
        DuckGain = minGain;
    }

    private struct Voice
    {
        public PolyBlepOscillator Osc1, Osc2;
        public SvfLowPass Filter;
        public Adsr Amp, FilterEnv;
        public double Increment1, Increment2;
        public int Pitch, Channel, NoteId, ControlCountdown;
        public float Gain;
        public bool IsHeld;
        public long Age;
    }
}
