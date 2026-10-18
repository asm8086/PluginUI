// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Interop;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D2D1_ALPHA_MODE;
using static TerraFX.Interop.DirectX.D2D1_CAP_STYLE;
using static TerraFX.Interop.DirectX.D2D1_DASH_STYLE;
using static TerraFX.Interop.DirectX.D2D1_FACTORY_TYPE;
using static TerraFX.Interop.DirectX.D2D1_FEATURE_LEVEL;
using static TerraFX.Interop.DirectX.D2D1_LINE_JOIN;
using static TerraFX.Interop.DirectX.D2D1_PRESENT_OPTIONS;
using static TerraFX.Interop.DirectX.D2D1_RENDER_TARGET_TYPE;
using static TerraFX.Interop.DirectX.D2D1_RENDER_TARGET_USAGE;
using static TerraFX.Interop.DirectX.D2D1_TEXT_ANTIALIAS_MODE;
using static TerraFX.Interop.DirectX.DWRITE_FACTORY_TYPE;
using static TerraFX.Interop.DirectX.DWRITE_FONT_STRETCH;
using static TerraFX.Interop.DirectX.DWRITE_FONT_STYLE;
using static TerraFX.Interop.DirectX.DWRITE_TRIMMING_GRANULARITY;
using static TerraFX.Interop.DirectX.DWRITE_WORD_WRAPPING;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI.Rendering;

/// <summary>
/// Owns every Direct2D / DirectWrite object of one host: factories, render target (window or offscreen bitmap),
/// and the caches of device-dependent (brushes, bitmaps, SVG documents) and device-independent resources
/// (stroke styles, text formats).
/// </summary>
/// <remarks>
/// Device-dependent resources are rebuilt after <c>D2DERR_RECREATE_TARGET</c>; <see cref="DeviceGeneration"/>
/// increments each time. Everything is released by <see cref="Dispose"/>: nothing is shared between hosts.
/// </remarks>
internal sealed unsafe class Renderer : ITextMeasurer, IDisposable
{
    private readonly record struct FormatKey(string Family, float Size, FontWeight Weight, bool Italic, bool Wrap, bool Ellipsis);

    private sealed class FormatEntry
    {
        public IDWriteTextFormat* Format;
        public IDWriteInlineObject* EllipsisSign;
    }

    private ID2D1Factory* _factory;
    private IDWriteFactory* _dwrite;

    private ID2D1RenderTarget* _target;
    private ID2D1HwndRenderTarget* _hwndTarget;   // same object as _target in window mode
    private ID2D1DeviceContext5* _svgContext;     // null when SVG is not supported
    private ID2D1SolidColorBrush* _brush;

    // Offscreen mode
    private IWICImagingFactory* _wic;
    private IWICBitmap* _wicBitmap;

    private HWND _hwnd;
    private uint _pixelWidth = 1, _pixelHeight = 1;
    private float _dpi = 96f;
    private bool _offscreen;

    private readonly Dictionary<StrokeStyle, nint> _strokes = new();
    private readonly Dictionary<FormatKey, FormatEntry> _formats = new();
    private readonly Dictionary<ImageSource, nint> _bitmaps = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SvgImage, nint> _svgDocuments = new(ReferenceEqualityComparer.Instance);

    public Renderer()
    {
        try
        {
            ID2D1Factory* factory;
            Com.ThrowIfFailed(DirectX.D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof<ID2D1Factory>(), (void**)&factory), "D2D1CreateFactory");
            _factory = factory;
            IDWriteFactory* dwrite;
            Com.ThrowIfFailed(DirectX.DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof<IDWriteFactory>(), (IUnknown**)&dwrite), "DWriteCreateFactory");
            _dwrite = dwrite;
            Context = new DrawingContext(this);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Drawing API bound to this renderer (valid during <see cref="Render"/>).</summary>
    public DrawingContext Context { get; } = null!;

    /// <summary>Incremented each time device resources are discarded.</summary>
    public int DeviceGeneration { get; private set; }

    /// <summary><c>true</c> if native SVG rendering is available on the current target.</summary>
    public bool SupportsSvg => _svgContext is not null;

    /// <summary>Pixels per DIP of the current target.</summary>
    public float Scale => _dpi / 96f;

    internal ID2D1Factory* Factory => _factory;
    internal IDWriteFactory* DWrite => _dwrite;
    internal ID2D1RenderTarget* Target => _target;
    internal ID2D1DeviceContext5* SvgContext => _svgContext;
    internal ID2D1SolidColorBrush* Brush => _brush;

    // =====================================================================================
    // Targets
    // =====================================================================================

    /// <summary>Binds the renderer to a window; the render target is created on the first frame.</summary>
    public void AttachWindow(HWND hwnd, uint pixelWidth, uint pixelHeight, float scale)
    {
        DiscardDeviceResources();
        _offscreen = false;
        _hwnd = hwnd;
        _pixelWidth = Math.Max(1u, pixelWidth);
        _pixelHeight = Math.Max(1u, pixelHeight);
        _dpi = 96f * scale;
    }

    /// <summary>Creates an offscreen bitmap target (snapshots, tests).</summary>
    public void CreateOffscreen(uint pixelWidth, uint pixelHeight, float scale)
    {
        DiscardDeviceResources();
        Com.Release(ref _wicBitmap);
        _offscreen = true;
        _hwnd = HWND.NULL;
        _pixelWidth = Math.Max(1u, pixelWidth);
        _pixelHeight = Math.Max(1u, pixelHeight);
        _dpi = 96f * scale;

        if (_wic is null)
        {
            IWICImagingFactory* wic;
            Guid clsid = CLSID.CLSID_WICImagingFactory;
            Com.ThrowIfFailed(CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, __uuidof<IWICImagingFactory>(), (void**)&wic), "CoCreateInstance(WICImagingFactory)");
            _wic = wic;
        }

        IWICBitmap* bitmap;
        Guid format = GUID.GUID_WICPixelFormat32bppPBGRA;
        Com.ThrowIfFailed(_wic->CreateBitmap(_pixelWidth, _pixelHeight, &format, WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, &bitmap), "IWICImagingFactory.CreateBitmap");
        _wicBitmap = bitmap;
    }

    /// <summary>Follows a client-area size change (pixels).</summary>
    public void Resize(uint pixelWidth, uint pixelHeight)
    {
        _pixelWidth = Math.Max(1u, pixelWidth);
        _pixelHeight = Math.Max(1u, pixelHeight);
        if (_hwndTarget is not null)
        {
            var size = new D2D_SIZE_U { width = _pixelWidth, height = _pixelHeight };
            if (_hwndTarget->Resize(&size).FAILED) DiscardDeviceResources();
        }
    }

    /// <summary>Changes the DIP-to-pixel scale (96 × scale DPI).</summary>
    public void SetScale(float scale)
    {
        _dpi = 96f * scale;
        if (_target is not null) _target->SetDpi(_dpi, _dpi);
    }

    /// <summary>
    /// Renders one frame. Returns <c>false</c> if the frame could not be presented (device lost, no target):
    /// the caller must schedule another frame.
    /// </summary>
    public bool Render(Action<DrawingContext> paint)
    {
        if (!_offscreen && _hwnd == HWND.NULL) return true;
        if (!EnsureDeviceResources()) return false;

        _target->BeginDraw();
        bool presented = true;
        try
        {
            Context.BeginFrame();
            paint(Context);
        }
        finally
        {
            Context.EndFrame();
            HRESULT hr = _target->EndDraw(null, null);
            if (hr.Value == Win32.D2DERR_RECREATE_TARGET || hr.FAILED)
            {
                DiscardDeviceResources();
                presented = false;
            }
        }
        return presented;
    }

    /// <summary>Encodes the offscreen bitmap as PNG.</summary>
    public void SavePng(string path)
    {
        if (!_offscreen || _wicBitmap is null) throw new InvalidOperationException("No offscreen target.");
        IWICStream* stream = null;
        IWICBitmapEncoder* encoder = null;
        IWICBitmapFrameEncode* frame = null;
        try
        {
            Com.ThrowIfFailed(_wic->CreateStream(&stream), "CreateStream");
            fixed (char* p = Path.GetFullPath(path))
            {
                Com.ThrowIfFailed(stream->InitializeFromFilename(p, GENERIC_WRITE), "InitializeFromFilename");
            }
            Guid png = GUID.GUID_ContainerFormatPng;
            Com.ThrowIfFailed(_wic->CreateEncoder(&png, null, &encoder), "CreateEncoder");
            Com.ThrowIfFailed(encoder->Initialize((IStream*)stream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache), "Encoder.Initialize");
            Com.ThrowIfFailed(encoder->CreateNewFrame(&frame, null), "CreateNewFrame");
            Com.ThrowIfFailed(frame->Initialize(null), "Frame.Initialize");
            Com.ThrowIfFailed(frame->SetSize(_pixelWidth, _pixelHeight), "SetSize");
            Guid format = GUID.GUID_WICPixelFormat32bppPBGRA;
            Com.ThrowIfFailed(frame->SetPixelFormat(&format), "SetPixelFormat");
            Com.ThrowIfFailed(frame->WriteSource((IWICBitmapSource*)_wicBitmap, null), "WriteSource");
            Com.ThrowIfFailed(frame->Commit(), "Frame.Commit");
            Com.ThrowIfFailed(encoder->Commit(), "Encoder.Commit");
        }
        finally
        {
            Com.Release(ref frame);
            Com.Release(ref encoder);
            Com.Release(ref stream);
        }
    }

    // =====================================================================================
    // Device resources
    // =====================================================================================

    private bool EnsureDeviceResources()
    {
        if (_target is not null) return true;

        var properties = new D2D1_RENDER_TARGET_PROPERTIES
        {
            type = D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = _offscreen ? DXGI_FORMAT_B8G8R8A8_UNORM : DXGI_FORMAT_UNKNOWN,
                alphaMode = _offscreen ? D2D1_ALPHA_MODE_PREMULTIPLIED : D2D1_ALPHA_MODE_UNKNOWN,
            },
            dpiX = _dpi,
            dpiY = _dpi,
            usage = D2D1_RENDER_TARGET_USAGE_NONE,
            minLevel = D2D1_FEATURE_LEVEL_DEFAULT,
        };

        if (_offscreen)
        {
            ID2D1RenderTarget* target;
            if (_factory->CreateWicBitmapRenderTarget(_wicBitmap, &properties, &target).FAILED) return false;
            _target = target;
            _target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        }
        else
        {
            var hwndProperties = new D2D1_HWND_RENDER_TARGET_PROPERTIES
            {
                hwnd = _hwnd,
                pixelSize = new D2D_SIZE_U { width = _pixelWidth, height = _pixelHeight },
                presentOptions = D2D1_PRESENT_OPTIONS_NONE,
            };
            ID2D1HwndRenderTarget* hwndTarget;
            if (_factory->CreateHwndRenderTarget(&properties, &hwndProperties, &hwndTarget).FAILED) return false;
            _hwndTarget = hwndTarget;
            _target = (ID2D1RenderTarget*)hwndTarget;
        }

        // Optional: native SVG needs ID2D1DeviceContext5 (Windows 10 1703+).
        ID2D1DeviceContext5* svg;
        if (_target->QueryInterface(__uuidof<ID2D1DeviceContext5>(), (void**)&svg).SUCCEEDED) _svgContext = svg;

        var white = new DXGI_RGBA { r = 1f, g = 1f, b = 1f, a = 1f };
        ID2D1SolidColorBrush* brush;
        if (_target->CreateSolidColorBrush(&white, null, &brush).FAILED)
        {
            DiscardDeviceResources();
            return false;
        }
        _brush = brush;
        return true;
    }

    /// <summary>Releases the device-dependent resources (target, brush, bitmaps, SVG documents).</summary>
    public void DiscardDeviceResources()
    {
        foreach (var bitmap in _bitmaps.Values) ((IUnknown*)bitmap)->Release();
        _bitmaps.Clear();
        foreach (var document in _svgDocuments.Values) ((IUnknown*)document)->Release();
        _svgDocuments.Clear();
        Com.Release(ref _brush);
        Com.Release(ref _svgContext);
        if (_target is not null)
        {
            _target->Release();
            _target = null;
            _hwndTarget = null;
        }
        DeviceGeneration++;
    }

    /// <summary>Device bitmap for an image (cached until the device is lost).</summary>
    internal ID2D1Bitmap* GetBitmap(ImageSource image)
    {
        if (_bitmaps.TryGetValue(image, out var cached)) return (ID2D1Bitmap*)cached;
        if (_target is null) return null;

        var properties = new D2D1_BITMAP_PROPERTIES
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED },
            dpiX = 96f * image.PixelsPerDip,
            dpiY = 96f * image.PixelsPerDip,
        };
        var size = new D2D_SIZE_U { width = (uint)image.PixelWidth, height = (uint)image.PixelHeight };
        ID2D1Bitmap* bitmap;
        fixed (byte* pixels = image.Pixels)
        {
            if (_target->CreateBitmap(size, pixels, (uint)image.PixelWidth * 4, &properties, &bitmap).FAILED) return null;
        }
        _bitmaps[image] = (nint)bitmap;
        return bitmap;
    }

    /// <summary>Releases the cached device resources of an image or SVG that is no longer used.</summary>
    public void Evict(object source)
    {
        if (source is ImageSource image && _bitmaps.Remove(image, out var bitmap)) ((IUnknown*)bitmap)->Release();
        if (source is SvgImage svg && _svgDocuments.Remove(svg, out var document)) ((IUnknown*)document)->Release();
    }

    /// <summary>Device SVG document (cached until the device is lost), or null if unsupported.</summary>
    internal ID2D1SvgDocument* GetSvgDocument(SvgImage svg)
    {
        if (_svgContext is null) return null;
        if (_svgDocuments.TryGetValue(svg, out var cached)) return (ID2D1SvgDocument*)cached;

        IStream* stream;
        fixed (byte* data = svg.Data)
        {
            stream = SHCreateMemStream(data, (uint)svg.Data.Length);
        }
        if (stream is null) return null;
        try
        {
            ID2D1SvgDocument* document;
            var viewport = new D2D_SIZE_F { width = svg.IntrinsicSize.Width, height = svg.IntrinsicSize.Height };
            if (_svgContext->CreateSvgDocument(stream, viewport, &document).FAILED) return null;
            _svgDocuments[svg] = (nint)document;
            return document;
        }
        finally
        {
            stream->Release();
        }
    }

    // =====================================================================================
    // Device-independent resources
    // =====================================================================================

    internal ID2D1StrokeStyle* GetStrokeStyle(StrokeStyle? style)
    {
        style ??= StrokeStyle.Solid;
        if (_strokes.TryGetValue(style, out var cached)) return (ID2D1StrokeStyle*)cached;

        static D2D1_CAP_STYLE Cap(LineCap cap) => cap switch
        {
            LineCap.Round => D2D1_CAP_STYLE_ROUND,
            LineCap.Square => D2D1_CAP_STYLE_SQUARE,
            _ => D2D1_CAP_STYLE_FLAT,
        };

        var properties = new D2D1_STROKE_STYLE_PROPERTIES
        {
            startCap = Cap(style.StartCap),
            endCap = Cap(style.EndCap),
            dashCap = Cap(style.StartCap == LineCap.Round ? LineCap.Round : LineCap.Flat),
            lineJoin = style.Join switch { LineJoin.Round => D2D1_LINE_JOIN_ROUND, LineJoin.Bevel => D2D1_LINE_JOIN_BEVEL, _ => D2D1_LINE_JOIN_MITER },
            miterLimit = 10f,
            dashStyle = style.Dashes is { Length: > 0 } ? D2D1_DASH_STYLE_CUSTOM : D2D1_DASH_STYLE_SOLID,
            dashOffset = style.DashOffset,
        };

        ID2D1StrokeStyle* stroke;
        if (style.Dashes is { Length: > 0 } dashes)
        {
            fixed (float* d = dashes)
            {
                Com.ThrowIfFailed(_factory->CreateStrokeStyle(&properties, d, (uint)dashes.Length, &stroke), "CreateStrokeStyle");
            }
        }
        else
        {
            Com.ThrowIfFailed(_factory->CreateStrokeStyle(&properties, null, 0, &stroke), "CreateStrokeStyle");
        }
        _strokes[style] = (nint)stroke;
        return stroke;
    }

    internal IDWriteTextFormat* GetTextFormat(TextStyle style)
    {
        var key = new FormatKey(style.Family, style.Size, style.Weight, style.Italic, style.Wrap, style.Ellipsis);
        if (_formats.TryGetValue(key, out var entry)) return entry.Format;

        entry = new FormatEntry();
        IDWriteTextFormat* format;
        fixed (char* family = style.Family)
        fixed (char* locale = "en-us")
        {
            Com.ThrowIfFailed(_dwrite->CreateTextFormat(family, null, (DWRITE_FONT_WEIGHT)(int)style.Weight,
                style.Italic ? DWRITE_FONT_STYLE_ITALIC : DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL,
                Math.Max(1f, style.Size), locale, &format), "CreateTextFormat");
        }
        entry.Format = format;
        format->SetWordWrapping(style.Wrap ? DWRITE_WORD_WRAPPING_WRAP : DWRITE_WORD_WRAPPING_NO_WRAP);
        if (style.Ellipsis)
        {
            IDWriteInlineObject* sign;
            if (_dwrite->CreateEllipsisTrimmingSign(format, &sign).SUCCEEDED)
            {
                entry.EllipsisSign = sign;
                var trimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY_CHARACTER };
                format->SetTrimming(&trimming, sign);
            }
        }
        _formats[key] = entry;
        return format;
    }

    // =====================================================================================
    // ITextMeasurer
    // =====================================================================================

    private IDWriteTextLayout* CreateLayout(string text, TextStyle style, float maxWidth, float maxHeight)
    {
        var format = GetTextFormat(style);
        IDWriteTextLayout* layout;
        fixed (char* chars = text)
        {
            if (_dwrite->CreateTextLayout(chars, (uint)text.Length, format, maxWidth, maxHeight, &layout).FAILED) return null;
        }
        return layout;
    }

    public Size Measure(string text, TextStyle style, float maxWidth = float.PositiveInfinity)
    {
        text ??= string.Empty;
        float width = float.IsFinite(maxWidth) && maxWidth > 0f ? maxWidth : 100000f;
        var layout = CreateLayout(text.Length == 0 ? " " : text, style with { Ellipsis = false }, width, 100000f);
        if (layout is null) return FallbackTextMeasurer.Instance.Measure(text, style, maxWidth);
        try
        {
            DWRITE_TEXT_METRICS metrics;
            layout->GetMetrics(&metrics);
            float w = text.Length == 0 ? 0f : metrics.widthIncludingTrailingWhitespace;
            return new Size(MathF.Ceiling(w), MathF.Ceiling(metrics.height));
        }
        finally
        {
            layout->Release();
        }
    }

    public float CaretX(string text, TextStyle style, int index)
    {
        text ??= string.Empty;
        if (text.Length == 0) return 0f;
        index = Math.Clamp(index, 0, text.Length);
        var layout = CreateLayout(text, style with { Wrap = false, Ellipsis = false }, 100000f, 100000f);
        if (layout is null) return FallbackTextMeasurer.Instance.CaretX(text, style, index);
        try
        {
            float x, y;
            DWRITE_HIT_TEST_METRICS metrics;
            bool trailing = index == text.Length;
            layout->HitTestTextPosition((uint)(trailing ? index - 1 : index), trailing ? BOOL.TRUE : BOOL.FALSE, &x, &y, &metrics);
            return x;
        }
        finally
        {
            layout->Release();
        }
    }

    public int HitTest(string text, TextStyle style, float x)
    {
        text ??= string.Empty;
        if (text.Length == 0) return 0;
        var layout = CreateLayout(text, style with { Wrap = false, Ellipsis = false }, 100000f, 100000f);
        if (layout is null) return FallbackTextMeasurer.Instance.HitTest(text, style, x);
        try
        {
            BOOL trailing, inside;
            DWRITE_HIT_TEST_METRICS metrics;
            layout->HitTestPoint(x, 1f, &trailing, &inside, &metrics);
            int index = (int)metrics.textPosition + (trailing != BOOL.FALSE ? 1 : 0);
            return Math.Clamp(index, 0, text.Length);
        }
        finally
        {
            layout->Release();
        }
    }

    // =====================================================================================

    public void Dispose()
    {
        DiscardDeviceResources();
        foreach (var stroke in _strokes.Values) ((IUnknown*)stroke)->Release();
        _strokes.Clear();
        foreach (var entry in _formats.Values)
        {
            Com.Release(ref entry.EllipsisSign);
            Com.Release(ref entry.Format);
        }
        _formats.Clear();
        Com.Release(ref _wicBitmap);
        Com.Release(ref _wic);
        Com.Release(ref _dwrite);
        Com.Release(ref _factory);
        _hwnd = HWND.NULL;
    }
}
