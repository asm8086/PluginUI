// 2026/10/24 14:21:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Dsp;

/// <summary>
/// Lock-free single-producer / single-consumer sample ring buffer that carries audio from the audio thread
/// (<see cref="Write"/>, wait-free, no allocation) to the UI thread (<see cref="CopyLatest"/>, <see cref="Read"/>).
/// Feeds the oscilloscope, the spectrum analyzer and custom visualizations.
/// </summary>
/// <remarks>
/// When the UI does not keep up, the oldest samples are overwritten: visualizations always see the latest audio.
/// Copies are done in at most two contiguous segments (block copies, vectorized by the runtime) rather than sample
/// by sample with index masking.
/// </remarks>
/// <example>
/// <code>
/// var scopeBuffer = new AudioRingBuffer(8192);          // created once, shared with the editor
///
/// // Audio thread, in ProcessMain:
/// scopeBuffer.WriteMono(left[..sampleCount], right[..sampleCount]);
///
/// // Editor:
/// var scope = new Oscilloscope { Source = scopeBuffer };
/// var analyzer = new SpectrumAnalyzer { Source = scopeBuffer };
/// </code>
/// </example>
public sealed class AudioRingBuffer
{
    private readonly float[] _data;
    private readonly int _mask;
    private long _writePosition; // total samples written (monotonic)
    private long _readPosition;

    /// <summary>Creates a buffer holding at least <paramref name="capacity"/> samples (rounded up to a power of two).</summary>
    public AudioRingBuffer(int capacity = 8192)
    {
        if (capacity < 16) capacity = 16;
        int size = 1;
        while (size < capacity) size <<= 1;
        _data = new float[size];
        _mask = size - 1;
    }

    /// <summary>Capacity in samples.</summary>
    public int Capacity => _data.Length;

    /// <summary>Total number of samples written since creation.</summary>
    public long TotalWritten => Volatile.Read(ref _writePosition);

    /// <summary>Audio thread: appends samples (overwrites the oldest when full).</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        long w = _writePosition;
        if (samples.Length > _data.Length) samples = samples[^_data.Length..];
        int start = (int)(w & _mask);
        int first = Math.Min(samples.Length, _data.Length - start);
        samples[..first].CopyTo(_data.AsSpan(start));
        samples[first..].CopyTo(_data);
        Volatile.Write(ref _writePosition, w + samples.Length);
    }

    /// <summary>Audio thread: appends the average of two channels (stereo to mono display).</summary>
    public void WriteMono(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        long w = _writePosition;
        int n = Math.Min(left.Length, right.Length);
        if (n > _data.Length)
        {
            left = left[(n - _data.Length)..n];
            right = right[(n - _data.Length)..n];
            n = _data.Length;
        }
        int start = (int)(w & _mask);
        int first = Math.Min(n, _data.Length - start);
        VectorOps.Average(left[..first], right[..first], _data.AsSpan(start, first));
        VectorOps.Average(left[first..n], right[first..n], _data.AsSpan(0, n - first));
        Volatile.Write(ref _writePosition, w + n);
    }

    /// <summary>UI thread: copies the most recent <c>destination.Length</c> samples (oldest first). Returns the count copied.</summary>
    /// <remarks>
    /// Request at most half the capacity for tear-free copies: if the writer laps the oldest copied samples during
    /// the copy, the copy is retried (up to three times).
    /// </remarks>
    public int CopyLatest(Span<float> destination)
    {
        for (int attempt = 0; ; attempt++)
        {
            long w = Volatile.Read(ref _writePosition);
            int n = (int)Math.Min(Math.Min(destination.Length, _data.Length), w);
            long start = w - n;
            CopyOut(start, destination[..n]);
            // Samples older than (current write position - capacity) may have been overwritten meanwhile.
            if (Volatile.Read(ref _writePosition) - _data.Length <= start || attempt >= 2) return n;
        }
    }

    /// <summary>UI thread: reads the samples written since the previous <see cref="Read"/> (up to the destination size).</summary>
    public int Read(Span<float> destination)
    {
        long w = Volatile.Read(ref _writePosition);
        long r = _readPosition;
        if (w - r > _data.Length) r = w - _data.Length; // overrun: skip to the oldest valid sample
        int n = (int)Math.Min(destination.Length, w - r);
        CopyOut(r, destination[..n]);
        _readPosition = r + n;
        return n;
    }

    // Copies the samples starting at the absolute position (destination.Length ≤ capacity), in two segments.
    private void CopyOut(long position, Span<float> destination)
    {
        int start = (int)(position & _mask);
        int first = Math.Min(destination.Length, _data.Length - start);
        _data.AsSpan(start, first).CopyTo(destination);
        _data.AsSpan(0, destination.Length - first).CopyTo(destination[first..]);
    }

    /// <summary>UI thread: discards the unread samples (the next <see cref="Read"/> returns only newer ones).</summary>
    public void SkipToLatest() => _readPosition = Volatile.Read(ref _writePosition);

    /// <summary>Number of unread samples (for <see cref="Read"/>).</summary>
    public int Available => (int)Math.Min(_data.Length, Volatile.Read(ref _writePosition) - _readPosition);
}
