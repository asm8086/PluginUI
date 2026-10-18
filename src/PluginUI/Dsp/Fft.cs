// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.InteropServices;

namespace PluginUI.Dsp;

/// <summary>Analysis window shapes.</summary>
public enum WindowFunction
{
    /// <summary>No window.</summary>
    Rectangular,
    /// <summary>Hann (good default).</summary>
    Hann,
    /// <summary>Hamming.</summary>
    Hamming,
    /// <summary>Blackman-Harris (4 terms, low leakage).</summary>
    BlackmanHarris,
}

/// <summary>
/// Radix-2 FFT with precomputed twiddles and a magnitude-spectrum helper for analyzers.
/// One instance per size; not thread-safe (use one per thread).
/// </summary>
/// <remarks>
/// <para>The butterflies run on split real / imaginary arrays with <see cref="Vector{T}"/> (4 doubles per
/// instruction with AVX2, 2 with SSE), and the bit-reversal permutation is fused with the loading of the input.</para>
/// <para><see cref="MagnitudeSpectrum"/> uses the real-input algorithm: the N real samples are packed into an
/// N/2-point complex FFT, then separated, which halves the work. The dB conversion is vectorized too.</para>
/// </remarks>
public sealed class Fft
{
    private readonly int _size;
    private readonly FftCore _half;          // N/2 points: real-input spectra
    private readonly double[] _re, _im;      // N/2 work arrays
    private readonly double[] _splitCos, _splitSin; // W_N^k = exp(-2πik/N), k < N/2
    private FftCore? _full;                  // N points: Forward, created on first use
    private double[] _fullRe = [], _fullIm = [];
    private float[] _window = [];
    private double _windowSum;
    private WindowFunction _windowKind = (WindowFunction)(-1);

    /// <summary>Creates an FFT of <paramref name="size"/> points (power of two, 16..65536).</summary>
    public Fft(int size)
    {
        if (size < 16 || size > 65536 || (size & (size - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(size), "Size must be a power of two between 16 and 65536.");
        _size = size;
        int m = size / 2;
        _half = new FftCore(m);
        _re = new double[m];
        _im = new double[m];
        _splitCos = new double[m];
        _splitSin = new double[m];
        for (int k = 0; k < m; k++)
        {
            double angle = -2.0 * Math.PI * k / size;
            _splitCos[k] = Math.Cos(angle);
            _splitSin[k] = Math.Sin(angle);
        }
    }

    /// <summary>Number of points.</summary>
    public int Size => _size;

    /// <summary>Forward transform in place (length must equal <see cref="Size"/>).</summary>
    public void Forward(Span<Complex> data)
    {
        if (data.Length != _size) throw new ArgumentException("Length must equal Size.", nameof(data));
        if (_full is null)
        {
            _full = new FftCore(_size);
            _fullRe = new double[_size];
            _fullIm = new double[_size];
        }
        int[] reverse = _full.BitReverse;
        for (int i = 0; i < _size; i++)
        {
            int r = reverse[i];
            _fullRe[r] = data[i].Real;
            _fullIm[r] = data[i].Imaginary;
        }
        _full.TransformBitReversed(_fullRe, _fullIm);
        for (int i = 0; i < _size; i++) data[i] = new Complex(_fullRe[i], _fullIm[i]);
    }

    /// <summary>Window coefficients of the given kind for this size.</summary>
    public ReadOnlySpan<float> GetWindow(WindowFunction kind)
    {
        if (kind == _windowKind) return _window;
        _window = new float[_size];
        double sum = 0.0;
        for (int i = 0; i < _size; i++)
        {
            double x = 2.0 * Math.PI * i / (_size - 1);
            _window[i] = (float)(kind switch
            {
                WindowFunction.Hann => 0.5 - 0.5 * Math.Cos(x),
                WindowFunction.Hamming => 0.54 - 0.46 * Math.Cos(x),
                WindowFunction.BlackmanHarris => 0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x),
                _ => 1.0,
            });
            sum += _window[i];
        }
        _windowSum = sum;
        _windowKind = kind;
        return _window;
    }

    /// <summary>
    /// Windowed magnitude spectrum in dBFS (a full-scale sine reads ≈ 0 dB): <paramref name="magnitudesDb"/>
    /// receives <c>Size / 2</c> bins (bin k = k × sampleRate / Size Hz).
    /// </summary>
    public void MagnitudeSpectrum(ReadOnlySpan<float> samples, Span<float> magnitudesDb, WindowFunction window = WindowFunction.Hann, float floorDb = -140f)
    {
        if (samples.Length < _size) throw new ArgumentException("Not enough samples.", nameof(samples));
        if (magnitudesDb.Length < _size / 2) throw new ArgumentException("Destination too small.", nameof(magnitudesDb));
        var w = GetWindow(window);
        int m = _size / 2;

        // Pack z[j] = x[2j] + i·x[2j+1] (windowed), directly in bit-reversed order.
        int[] reverse = _half.BitReverse;
        for (int j = 0; j < m; j++)
        {
            int r = reverse[j];
            _re[r] = samples[2 * j] * w[2 * j];
            _im[r] = samples[2 * j + 1] * w[2 * j + 1];
        }
        _half.TransformBitReversed(_re, _im);

        // Split: with Zc = conj(Z[(m − k) mod m]), 2·X[k] = (Z[k] + Zc) − i·W^k·(Z[k] − Zc).
        // A full-scale sine reads |X| = windowSum / 2, hence the 2 / windowSum amplitude scale (squared, /4 for 2·X).
        double scale = 2.0 / Math.Max(1e-12, _windowSum);
        double powerScale = 0.25 * scale * scale;
        var output = magnitudesDb[..m];
        for (int k = 0; k < m; k++)
        {
            int kc = (m - k) & (m - 1);
            double zr = _re[k], zi = _im[k], cr = _re[kc], ci = -_im[kc];
            double er = zr + cr, ei = zi + ci;   // 2·even part
            double odr = zi - ci, odi = cr - zr; // −i·(Z[k] − Zc) = 2·odd part
            double c = _splitCos[k], s = _splitSin[k];
            double xr = er + (odr * c - odi * s);
            double xi = ei + (odr * s + odi * c);
            output[k] = (float)((xr * xr + xi * xi) * powerScale);
        }
        VectorOps.PowerToDecibels(output, floorDb);
    }
}

/// <summary>
/// Complex radix-2 decimation-in-time FFT kernel on split real / imaginary arrays (input in bit-reversed order).
/// The stages whose butterfly span is at least one vector are computed with <see cref="Vector{T}"/>.
/// </summary>
internal sealed class FftCore
{
    private readonly int _n;
    // Twiddles of the stage that merges blocks of h points: index h + k holds exp(−iπk/h), k < h (h = 1, 2, 4… n/2).
    private readonly double[] _twRe, _twIm;

    public FftCore(int n)
    {
        if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(n));
        _n = n;
        _twRe = new double[n];
        _twIm = new double[n];
        for (int h = 1; h < n; h <<= 1)
        {
            for (int k = 0; k < h; k++)
            {
                double angle = -Math.PI * k / h;
                _twRe[h + k] = Math.Cos(angle);
                _twIm[h + k] = Math.Sin(angle);
            }
        }
        int log2 = BitOperations.Log2((uint)n);
        BitReverse = new int[n];
        for (int i = 0; i < n; i++)
        {
            int r = 0;
            for (int b = 0; b < log2; b++) r |= ((i >> b) & 1) << (log2 - 1 - b);
            BitReverse[i] = r;
        }
    }

    public int Size => _n;

    /// <summary>Destination index of each input sample (write input i at <c>BitReverse[i]</c>).</summary>
    public int[] BitReverse { get; }

    /// <summary>Transforms in place data already stored in bit-reversed order; the output is in natural order.</summary>
    public void TransformBitReversed(Span<double> re, Span<double> im)
    {
        int n = _n;
        if (re.Length < n || im.Length < n) throw new ArgumentException("Arrays too small.");
        re = re[..n];
        im = im[..n];

        // h = 1: twiddle 1.
        for (int i = 0; i + 1 < n; i += 2)
        {
            double ar = re[i], ai = im[i], br = re[i + 1], bi = im[i + 1];
            re[i] = ar + br; im[i] = ai + bi;
            re[i + 1] = ar - br; im[i + 1] = ai - bi;
        }

        // h = 2: twiddles 1 and −i.
        for (int i = 0; i + 3 < n; i += 4)
        {
            double ar = re[i], ai = im[i], br = re[i + 2], bi = im[i + 2];
            re[i] = ar + br; im[i] = ai + bi;
            re[i + 2] = ar - br; im[i + 2] = ai - bi;
            ar = re[i + 1]; ai = im[i + 1];
            double tr = im[i + 3], ti = -re[i + 3]; // −i·b
            re[i + 1] = ar + tr; im[i + 1] = ai + ti;
            re[i + 3] = ar - tr; im[i + 3] = ai - ti;
        }

        for (int h = 4; h < n; h <<= 1)
        {
            if (Vector.IsHardwareAccelerated && h >= Vector<double>.Count) StageVector(re, im, h);
            else StageScalar(re, im, h);
        }
    }

    private void StageScalar(Span<double> re, Span<double> im, int h)
    {
        for (int i = 0; i < _n; i += 2 * h)
        {
            for (int k = 0; k < h; k++)
            {
                int a = i + k, b = a + h;
                double wr = _twRe[h + k], wi = _twIm[h + k];
                double tr = re[b] * wr - im[b] * wi;
                double ti = re[b] * wi + im[b] * wr;
                double ar = re[a], ai = im[a];
                re[a] = ar + tr; im[a] = ai + ti;
                re[b] = ar - tr; im[b] = ai - ti;
            }
        }
    }

    // h is a power of two ≥ Vector<double>.Count, so every block is a whole number of vectors (no remainder).
    private void StageVector(Span<double> re, Span<double> im, int h)
    {
        ref double pr = ref MemoryMarshal.GetReference(re);
        ref double pi = ref MemoryMarshal.GetReference(im);
        ref double wr0 = ref MemoryMarshal.GetArrayDataReference(_twRe);
        ref double wi0 = ref MemoryMarshal.GetArrayDataReference(_twIm);
        int width = Vector<double>.Count;
        for (int i = 0; i < _n; i += 2 * h)
        {
            for (int k = 0; k < h; k += width)
            {
                nuint a = (nuint)(i + k), b = a + (nuint)h, t = (nuint)(h + k);
                var wr = Vector.LoadUnsafe(ref wr0, t);
                var wi = Vector.LoadUnsafe(ref wi0, t);
                var br = Vector.LoadUnsafe(ref pr, b);
                var bi = Vector.LoadUnsafe(ref pi, b);
                var tr = br * wr - bi * wi;
                var ti = br * wi + bi * wr;
                var ar = Vector.LoadUnsafe(ref pr, a);
                var ai = Vector.LoadUnsafe(ref pi, a);
                (ar + tr).StoreUnsafe(ref pr, a);
                (ai + ti).StoreUnsafe(ref pi, a);
                (ar - tr).StoreUnsafe(ref pr, b);
                (ai - ti).StoreUnsafe(ref pi, b);
            }
        }
    }
}
