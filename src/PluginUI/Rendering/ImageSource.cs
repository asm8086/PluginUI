// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Reflection;
using PluginUI.Interop;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

/// <summary>
/// Raster image decoded once (PNG, JPEG, BMP, GIF, TIFF... through WIC) into premultiplied BGRA pixels.
/// Device bitmaps are created lazily by each renderer and rebuilt after a device loss.
/// </summary>
/// <remarks>
/// <see cref="PixelsPerDip"/> describes the image density: an image exported at 2× for HiDPI
/// (e.g. "knob@2x.png") should use 2 so that its DIP size matches the 1× design.
/// </remarks>
public sealed class ImageSource
{
    private readonly byte[] _pixels;

    private ImageSource(int width, int height, byte[] pixels, float pixelsPerDip)
    {
        PixelWidth = width;
        PixelHeight = height;
        _pixels = pixels;
        PixelsPerDip = pixelsPerDip <= 0f ? 1f : pixelsPerDip;
    }

    /// <summary>Width in pixels.</summary>
    public int PixelWidth { get; }

    /// <summary>Height in pixels.</summary>
    public int PixelHeight { get; }

    /// <summary>Image density (pixels per DIP).</summary>
    public float PixelsPerDip { get; }

    /// <summary>Natural size in DIPs.</summary>
    public Size Size => new(PixelWidth / PixelsPerDip, PixelHeight / PixelsPerDip);

    /// <summary>Premultiplied BGRA pixels, row-major, stride = 4 × width.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>Decodes an image file.</summary>
    public static ImageSource FromFile(string path, float pixelsPerDip = 1f) => FromBytes(File.ReadAllBytes(path), pixelsPerDip);

    /// <summary>Decodes an image stream.</summary>
    public static ImageSource FromStream(Stream stream, float pixelsPerDip = 1f)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return FromBytes(memory.ToArray(), pixelsPerDip);
    }

    /// <summary>Decodes an embedded resource of <paramref name="assembly"/>.</summary>
    public static ImageSource FromResource(Assembly assembly, string resourceName, float pixelsPerDip = 1f)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found in {assembly.GetName().Name}.");
        return FromStream(stream, pixelsPerDip);
    }

    /// <summary>Wraps raw premultiplied BGRA pixels (copied).</summary>
    public static ImageSource FromPixels(int width, int height, ReadOnlySpan<byte> premultipliedBgra, float pixelsPerDip = 1f)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (premultipliedBgra.Length < width * height * 4) throw new ArgumentException("Pixel buffer too small.", nameof(premultipliedBgra));
        return new ImageSource(width, height, premultipliedBgra[..(width * height * 4)].ToArray(), pixelsPerDip);
    }

    /// <summary>Decodes an encoded image (any WIC-supported format).</summary>
    public static unsafe ImageSource FromBytes(byte[] encoded, float pixelsPerDip = 1f)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        using var com = new ComScope();

        IWICImagingFactory* factory = null;
        IWICStream* stream = null;
        IWICBitmapDecoder* decoder = null;
        IWICBitmapFrameDecode* frame = null;
        IWICFormatConverter* converter = null;
        try
        {
            Guid clsid = CLSID.CLSID_WICImagingFactory;
            Com.ThrowIfFailed(CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, __uuidof<IWICImagingFactory>(), (void**)&factory), "CoCreateInstance(WICImagingFactory)");
            Com.ThrowIfFailed(factory->CreateStream(&stream), "IWICImagingFactory.CreateStream");
            fixed (byte* data = encoded)
            {
                Com.ThrowIfFailed(stream->InitializeFromMemory(data, (uint)encoded.Length), "IWICStream.InitializeFromMemory");
                Com.ThrowIfFailed(factory->CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnLoad, &decoder), "CreateDecoderFromStream");
                Com.ThrowIfFailed(decoder->GetFrame(0, &frame), "IWICBitmapDecoder.GetFrame");
                Com.ThrowIfFailed(factory->CreateFormatConverter(&converter), "CreateFormatConverter");
                Guid format = GUID.GUID_WICPixelFormat32bppPBGRA;
                Com.ThrowIfFailed(converter->Initialize((IWICBitmapSource*)frame, &format, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0.0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom), "IWICFormatConverter.Initialize");

                uint width, height;
                Com.ThrowIfFailed(converter->GetSize(&width, &height), "GetSize");
                var pixels = new byte[checked((int)(width * height * 4))];
                fixed (byte* p = pixels)
                {
                    Com.ThrowIfFailed(converter->CopyPixels(null, width * 4, (uint)pixels.Length, p), "CopyPixels");
                }
                return new ImageSource((int)width, (int)height, pixels, pixelsPerDip);
            }
        }
        finally
        {
            Com.Release(ref converter);
            Com.Release(ref frame);
            Com.Release(ref decoder);
            Com.Release(ref stream);
            Com.Release(ref factory);
        }
    }
}

/// <summary>
/// Vertical or horizontal strip of equally sized animation frames (classic "film-strip" knobs and switches).
/// </summary>
/// <param name="Image">The strip image.</param>
/// <param name="FrameCount">Number of frames in the strip.</param>
/// <param name="Orientation">Frames stacked vertically (most common) or horizontally.</param>
public sealed record FilmStrip(ImageSource Image, int FrameCount, Orientation Orientation = Orientation.Vertical)
{
    /// <summary>Size of one frame in DIPs.</summary>
    public Size FrameSize => Orientation == Orientation.Vertical
        ? new Size(Image.Size.Width, Image.Size.Height / Math.Max(1, FrameCount))
        : new Size(Image.Size.Width / Math.Max(1, FrameCount), Image.Size.Height);

    /// <summary>Source rectangle of a frame, in pixels.</summary>
    public Rect FramePixels(int index)
    {
        int count = Math.Max(1, FrameCount);
        index = Math.Clamp(index, 0, count - 1);
        if (Orientation == Orientation.Vertical)
        {
            float h = (float)Image.PixelHeight / count;
            return new Rect(0f, index * h, Image.PixelWidth, h);
        }
        float w = (float)Image.PixelWidth / count;
        return new Rect(index * w, 0f, w, Image.PixelHeight);
    }

    /// <summary>Frame index for a normalized value.</summary>
    public int FrameForValue(double normalized) => (int)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * (Math.Max(1, FrameCount) - 1));
}

/// <summary>
/// Image drawn with fixed corners and stretched edges/center (9-patch), for scalable panels and buttons.
/// </summary>
/// <param name="Image">Source image.</param>
/// <param name="Insets">Size of the fixed borders, in image pixels.</param>
public sealed record NineSlice(ImageSource Image, Thickness Insets);

/// <summary>
/// Initializes COM for the current thread if needed and balances the call.
/// </summary>
internal readonly unsafe struct ComScope : IDisposable
{
    private readonly bool _uninitialize;

    public ComScope()
    {
        HRESULT hr = CoInitializeEx(null, (uint)COINIT.COINIT_APARTMENTTHREADED);
        // S_OK / S_FALSE: we own a reference. RPC_E_CHANGED_MODE: already initialized as MTA, usable as is.
        _uninitialize = hr.Value is 0 or 1;
    }

    public void Dispose()
    {
        if (_uninitialize) CoUninitialize();
    }
}
