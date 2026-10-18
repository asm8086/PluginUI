// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Numerics;
using PluginUI.Interop;
using PluginUI.Rendering;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.D2D1_ANTIALIAS_MODE;
using static TerraFX.Interop.DirectX.D2D1_BITMAP_INTERPOLATION_MODE;
using static TerraFX.Interop.DirectX.D2D1_DRAW_TEXT_OPTIONS;
using static TerraFX.Interop.DirectX.D2D1_EXTEND_MODE;
using static TerraFX.Interop.DirectX.D2D1_FIGURE_BEGIN;
using static TerraFX.Interop.DirectX.D2D1_FIGURE_END;
using static TerraFX.Interop.DirectX.D2D1_FILL_MODE;
using static TerraFX.Interop.DirectX.D2D1_GAMMA;
using static TerraFX.Interop.DirectX.D2D1_LAYER_OPTIONS;
using static TerraFX.Interop.DirectX.D2D1_ARC_SIZE;
using static TerraFX.Interop.DirectX.D2D1_SWEEP_DIRECTION;
using static TerraFX.Interop.DirectX.DWRITE_MEASURING_MODE;
using static TerraFX.Interop.DirectX.DWRITE_PARAGRAPH_ALIGNMENT;
using static TerraFX.Interop.DirectX.DWRITE_TEXT_ALIGNMENT;

namespace PluginUI;

/// <summary>
/// Immediate-mode drawing API handed to <see cref="UIElement.OnRender"/>. All coordinates are DIPs.
/// </summary>
/// <remarks>
/// <para>Angles are degrees measured clockwise from 12 o'clock (knob convention) unless stated otherwise.</para>
/// <para>State changes (clip, opacity, transform) are stacked: pair each <c>Push…</c> with a <c>Pop…</c>,
/// or use the <c>using</c> scopes (<see cref="Clip(Rect)"/>, <see cref="Opacity(float)"/>, <see cref="Transform(Matrix3x2)"/>).</para>
/// </remarks>
public sealed unsafe class DrawingContext
{
    private enum StateKind : byte { AxisClip, Layer, Transform, Alpha }

    private readonly Renderer _renderer;
    private readonly Stack<StateKind> _states = new();
    private readonly Stack<Matrix3x2> _transforms = new();
    private readonly Stack<Rect> _clipBounds = new();
    private readonly Stack<float> _alphas = new();
    private float _alpha = 1f;
    private Matrix3x2 _transform = Matrix3x2.Identity;

    internal DrawingContext(Renderer renderer)
    {
        _renderer = renderer;
    }

    private ID2D1RenderTarget* Target => _renderer.Target;

    /// <summary>Pixels per DIP (for pixel snapping).</summary>
    public float Scale => _renderer.Scale;

    /// <summary><c>true</c> if <see cref="DrawSvg"/> renders natively on this target.</summary>
    public bool SupportsSvg => _renderer.SupportsSvg;

    /// <summary>Current clip rectangle in DIPs (untransformed approximation, used to skip invisible elements).</summary>
    public Rect ClipBounds => _clipBounds.Count > 0 ? _clipBounds.Peek() : new Rect(-1e6f, -1e6f, 2e6f, 2e6f);

    /// <summary>Text measurement on the current device.</summary>
    public ITextMeasurer Text => _renderer;

    internal void BeginFrame()
    {
        _states.Clear();
        _transforms.Clear();
        _clipBounds.Clear();
        _alphas.Clear();
        _alpha = 1f;
        _transform = Matrix3x2.Identity;
        ApplyTransform();
    }

    internal void EndFrame()
    {
        // Unbalanced pushes (exceptions in paint code) must not leak into EndDraw.
        while (_states.Count > 0) PopState();
    }

    // =====================================================================================
    // Brushes
    // =====================================================================================

    private ID2D1Brush* Solid(Color color)
    {
        var rgba = ToD2D(_alpha < 1f ? color with { A = color.A * _alpha } : color);
        _renderer.Brush->SetColor(&rgba);
        return (ID2D1Brush*)_renderer.Brush;
    }

    /// <summary>Creates a brush for a paint; the caller releases it when <paramref name="owned"/> is true.</summary>
    private ID2D1Brush* CreateBrush(Paint paint, Rect bounds, out bool owned)
    {
        owned = false;
        switch (paint)
        {
            case SolidPaint solid:
                return Solid(solid.Color);

            case LinearGradientPaint linear when linear.Stops.Length > 0:
            {
                var stops = CreateStops(linear.Stops);
                if (stops is null) return Solid(linear.Stops[0].Color);
                try
                {
                    var properties = new D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES
                    {
                        startPoint = ToD2D(Resolve(linear.Start, bounds, linear.RelativeToBounds)),
                        endPoint = ToD2D(Resolve(linear.End, bounds, linear.RelativeToBounds)),
                    };
                    ID2D1LinearGradientBrush* brush;
                    if (Target->CreateLinearGradientBrush(&properties, null, stops, &brush).FAILED) return Solid(linear.Stops[0].Color);
                    owned = true;
                    if (_alpha < 1f) brush->SetOpacity(_alpha);
                    return (ID2D1Brush*)brush;
                }
                finally
                {
                    stops->Release();
                }
            }

            case RadialGradientPaint radial when radial.Stops.Length > 0:
            {
                var stops = CreateStops(radial.Stops);
                if (stops is null) return Solid(radial.Stops[0].Color);
                try
                {
                    var center = Resolve(radial.Center, bounds, radial.RelativeToBounds);
                    float rx = radial.RelativeToBounds ? radial.RadiusX * bounds.Width : radial.RadiusX;
                    float ry = radial.RelativeToBounds ? radial.RadiusY * bounds.Height : radial.RadiusY;
                    var offset = radial.RelativeToBounds ? new Point(radial.OriginOffset.X * bounds.Width, radial.OriginOffset.Y * bounds.Height) : radial.OriginOffset;
                    var properties = new D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES
                    {
                        center = ToD2D(center),
                        gradientOriginOffset = ToD2D(offset),
                        radiusX = rx,
                        radiusY = ry,
                    };
                    ID2D1RadialGradientBrush* brush;
                    if (Target->CreateRadialGradientBrush(&properties, null, stops, &brush).FAILED) return Solid(radial.Stops[0].Color);
                    owned = true;
                    if (_alpha < 1f) brush->SetOpacity(_alpha);
                    return (ID2D1Brush*)brush;
                }
                finally
                {
                    stops->Release();
                }
            }

            default:
                return Solid(Color.Transparent);
        }
    }

    private ID2D1GradientStopCollection* CreateStops(GradientStop[] stops)
    {
        int count = stops.Length;
        var native = stackalloc D2D1_GRADIENT_STOP[Math.Min(count, 64)];
        count = Math.Min(count, 64);
        for (int i = 0; i < count; i++)
        {
            native[i] = new D2D1_GRADIENT_STOP { position = stops[i].Offset, color = ToD2D(stops[i].Color) };
        }
        ID2D1GradientStopCollection* collection;
        return Target->CreateGradientStopCollection(native, (uint)count, D2D1_GAMMA_2_2, D2D1_EXTEND_MODE_CLAMP, &collection).SUCCEEDED ? collection : null;
    }

    private static Point Resolve(Point p, Rect bounds, bool relative) => relative ? new Point(bounds.X + p.X * bounds.Width, bounds.Y + p.Y * bounds.Height) : p;

    // =====================================================================================
    // Basic shapes
    // =====================================================================================

    /// <summary>Fills the whole target (ignores clip and transform).</summary>
    public void Clear(Color color)
    {
        var rgba = ToD2D(color);
        Target->Clear(&rgba);
    }

    /// <summary>Fills a rectangle.</summary>
    public void FillRect(Rect rect, Color color)
    {
        if (color.IsTransparent) return;
        var r = ToD2D(rect);
        Target->FillRectangle(&r, Solid(color));
    }

    /// <summary>Fills a rectangle with a paint.</summary>
    public void FillRect(Rect rect, Paint paint)
    {
        var brush = CreateBrush(paint, rect, out bool owned);
        var r = ToD2D(rect);
        Target->FillRectangle(&r, brush);
        if (owned) brush->Release();
    }

    /// <summary>Strokes a rectangle (the stroke is centered on the edges).</summary>
    public void DrawRect(Rect rect, Color color, float thickness = 1f, StrokeStyle? style = null)
    {
        if (color.IsTransparent || thickness <= 0f) return;
        var r = ToD2D(rect);
        Target->DrawRectangle(&r, Solid(color), thickness, _renderer.GetStrokeStyle(style));
    }

    /// <summary>Fills a rounded rectangle.</summary>
    public void FillRoundedRect(Rect rect, float radius, Color color)
    {
        if (color.IsTransparent) return;
        var rr = RoundedRect(rect, radius);
        Target->FillRoundedRectangle(&rr, Solid(color));
    }

    /// <summary>Fills a rounded rectangle with a paint.</summary>
    public void FillRoundedRect(Rect rect, float radius, Paint paint)
    {
        var brush = CreateBrush(paint, rect, out bool owned);
        var rr = RoundedRect(rect, radius);
        Target->FillRoundedRectangle(&rr, brush);
        if (owned) brush->Release();
    }

    /// <summary>Strokes a rounded rectangle.</summary>
    public void DrawRoundedRect(Rect rect, float radius, Color color, float thickness = 1f, StrokeStyle? style = null)
    {
        if (color.IsTransparent || thickness <= 0f) return;
        var rr = RoundedRect(rect, radius);
        Target->DrawRoundedRectangle(&rr, Solid(color), thickness, _renderer.GetStrokeStyle(style));
    }

    /// <summary>Fills an ellipse.</summary>
    public void FillEllipse(Point center, float radiusX, float radiusY, Paint paint)
    {
        var bounds = new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2f, radiusY * 2f);
        var brush = CreateBrush(paint, bounds, out bool owned);
        var e = new D2D1_ELLIPSE { point = ToD2D(center), radiusX = radiusX, radiusY = radiusY };
        Target->FillEllipse(&e, brush);
        if (owned) brush->Release();
    }

    /// <summary>Strokes an ellipse.</summary>
    public void DrawEllipse(Point center, float radiusX, float radiusY, Color color, float thickness = 1f, StrokeStyle? style = null)
    {
        if (color.IsTransparent || thickness <= 0f) return;
        var e = new D2D1_ELLIPSE { point = ToD2D(center), radiusX = radiusX, radiusY = radiusY };
        Target->DrawEllipse(&e, Solid(color), thickness, _renderer.GetStrokeStyle(style));
    }

    /// <summary>Fills a circle.</summary>
    public void FillCircle(Point center, float radius, Color color)
    {
        if (color.IsTransparent) return;
        var e = new D2D1_ELLIPSE { point = ToD2D(center), radiusX = radius, radiusY = radius };
        Target->FillEllipse(&e, Solid(color));
    }

    /// <summary>Fills a circle with a paint.</summary>
    public void FillCircle(Point center, float radius, Paint paint) => FillEllipse(center, radius, radius, paint);

    /// <summary>Strokes a circle.</summary>
    public void DrawCircle(Point center, float radius, Color color, float thickness = 1f, StrokeStyle? style = null)
        => DrawEllipse(center, radius, radius, color, thickness, style);

    /// <summary>Draws a line.</summary>
    public void DrawLine(Point from, Point to, Color color, float thickness = 1f, StrokeStyle? style = null)
    {
        if (color.IsTransparent || thickness <= 0f) return;
        Target->DrawLine(ToD2D(from), ToD2D(to), Solid(color), thickness, _renderer.GetStrokeStyle(style));
    }

    /// <summary>Draws an open polyline.</summary>
    public void DrawPolyline(ReadOnlySpan<Point> points, Color color, float thickness = 1f, StrokeStyle? style = null)
    {
        if (points.Length < 2 || color.IsTransparent) return;
        var geometry = BuildPolyline(points, closed: false);
        if (geometry is null) return;
        Target->DrawGeometry((ID2D1Geometry*)geometry, Solid(color), thickness, _renderer.GetStrokeStyle(style ?? StrokeStyle.Round));
        geometry->Release();
    }

    /// <summary>Fills a closed polygon.</summary>
    public void FillPolygon(ReadOnlySpan<Point> points, Paint paint)
    {
        if (points.Length < 3) return;
        var geometry = BuildPolyline(points, closed: true);
        if (geometry is null) return;
        D2D_RECT_F b;
        geometry->GetBounds(null, &b);
        var brush = CreateBrush(paint, FromD2D(b), out bool owned);
        Target->FillGeometry((ID2D1Geometry*)geometry, brush, null);
        if (owned) brush->Release();
        geometry->Release();
    }

    /// <summary>Strokes a circular arc (angles in degrees clockwise from 12 o'clock).</summary>
    public void DrawArc(Point center, float radius, float startDegrees, float sweepDegrees, Color color, float thickness = 1f, StrokeStyle? style = null)
    {
        if (MathF.Abs(sweepDegrees) < 0.01f || color.IsTransparent) return;
        DrawGeometry(PathGeometry.Arc(center, radius, startDegrees, sweepDegrees), color, thickness, style ?? StrokeStyle.Round);
    }

    /// <summary>Fills a pie slice.</summary>
    public void FillPie(Point center, float radius, float startDegrees, float sweepDegrees, Paint paint)
    {
        if (MathF.Abs(sweepDegrees) < 0.01f) return;
        FillGeometry(PathGeometry.Pie(center, radius, startDegrees, sweepDegrees), paint);
    }

    /// <summary>Fills a path.</summary>
    public void FillGeometry(PathGeometry path, Paint paint)
    {
        var geometry = BuildGeometry(path);
        if (geometry is null) return;
        D2D_RECT_F b;
        geometry->GetBounds(null, &b);
        var brush = CreateBrush(paint, FromD2D(b), out bool owned);
        Target->FillGeometry((ID2D1Geometry*)geometry, brush, null);
        if (owned) brush->Release();
        geometry->Release();
    }

    /// <summary>Strokes a path.</summary>
    public void DrawGeometry(PathGeometry path, Color color, float thickness = 1f, StrokeStyle? style = null)
    {
        if (color.IsTransparent || thickness <= 0f) return;
        var geometry = BuildGeometry(path);
        if (geometry is null) return;
        Target->DrawGeometry((ID2D1Geometry*)geometry, Solid(color), thickness, _renderer.GetStrokeStyle(style));
        geometry->Release();
    }

    /// <summary>
    /// Soft drop shadow under a rounded rectangle, approximated by stacked translucent layers
    /// (cheap, no effect pipeline, works on every render target).
    /// </summary>
    public void DrawShadow(Rect rect, float radius, float blur, Point offset, Color color)
    {
        if (color.IsTransparent || blur <= 0f) { FillRoundedRect(rect.Offset(offset.X, offset.Y), radius, color); return; }
        int steps = Math.Clamp((int)MathF.Ceiling(blur), 2, 10);
        float alpha = color.A / steps;
        for (int i = steps; i >= 1; i--)
        {
            float grow = blur * i / steps;
            FillRoundedRect(rect.Offset(offset.X, offset.Y).Inflate(grow - blur * 0.5f), radius + grow, color.WithAlpha(alpha));
        }
    }

    // =====================================================================================
    // Text
    // =====================================================================================

    /// <summary>Draws text inside a layout rectangle, aligned and clipped according to <paramref name="style"/>.</summary>
    public void DrawText(string? text, Rect layout, TextStyle style, Color color)
    {
        if (string.IsNullOrEmpty(text) || color.IsTransparent || layout.Width <= 0f) return;
        var format = _renderer.GetTextFormat(style);
        format->SetTextAlignment(style.Align switch
        {
            HorizontalAlignment.Center => DWRITE_TEXT_ALIGNMENT_CENTER,
            HorizontalAlignment.Right => DWRITE_TEXT_ALIGNMENT_TRAILING,
            _ => DWRITE_TEXT_ALIGNMENT_LEADING,
        });
        format->SetParagraphAlignment(style.VerticalAlign switch
        {
            VerticalAlignment.Top => DWRITE_PARAGRAPH_ALIGNMENT_NEAR,
            VerticalAlignment.Bottom => DWRITE_PARAGRAPH_ALIGNMENT_FAR,
            _ => DWRITE_PARAGRAPH_ALIGNMENT_CENTER,
        });
        var r = ToD2D(layout);
        fixed (char* chars = text)
        {
            Target->DrawText(chars, (uint)text.Length, format, &r, Solid(color), D2D1_DRAW_TEXT_OPTIONS_CLIP, DWRITE_MEASURING_MODE_NATURAL);
        }
    }

    /// <summary>Measures text.</summary>
    public Size MeasureText(string text, TextStyle style, float maxWidth = float.PositiveInfinity) => _renderer.Measure(text, style, maxWidth);

    // =====================================================================================
    // Images
    // =====================================================================================

    /// <summary>Draws an image (or a pixel region of it) into a destination rectangle.</summary>
    public void DrawImage(ImageSource image, Rect destination, float opacity = 1f, Rect? sourcePixels = null, bool smooth = true)
    {
        var bitmap = _renderer.GetBitmap(image);
        if (bitmap is null) return;
        var dest = ToD2D(destination);
        var mode = smooth ? D2D1_BITMAP_INTERPOLATION_MODE_LINEAR : D2D1_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR;
        if (sourcePixels is Rect src)
        {
            // Source rectangles are expressed in DIPs of the bitmap (pixels / PixelsPerDip).
            float k = 1f / image.PixelsPerDip;
            var s = new D2D_RECT_F { left = src.X * k, top = src.Y * k, right = src.Right * k, bottom = src.Bottom * k };
            Target->DrawBitmap(bitmap, &dest, Math.Clamp(opacity * _alpha, 0f, 1f), mode, &s);
        }
        else
        {
            Target->DrawBitmap(bitmap, &dest, Math.Clamp(opacity * _alpha, 0f, 1f), mode, null);
        }
    }

    /// <summary>Draws one frame of a film-strip.</summary>
    public void DrawFilmStripFrame(FilmStrip strip, int frame, Rect destination, float opacity = 1f)
        => DrawImage(strip.Image, destination, opacity, strip.FramePixels(frame));

    /// <summary>Draws a nine-slice image stretched to the destination.</summary>
    public void DrawNineSlice(NineSlice nine, Rect destination, float opacity = 1f)
    {
        var img = nine.Image;
        float pw = img.PixelWidth, ph = img.PixelHeight, k = 1f / img.PixelsPerDip;
        var ins = nine.Insets;
        float[] sx = [0f, ins.Left, pw - ins.Right, pw];
        float[] sy = [0f, ins.Top, ph - ins.Bottom, ph];
        float[] dx = [destination.X, destination.X + ins.Left * k, destination.Right - ins.Right * k, destination.Right];
        float[] dy = [destination.Y, destination.Y + ins.Top * k, destination.Bottom - ins.Bottom * k, destination.Bottom];
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var src = Rect.FromEdges(sx[col], sy[row], sx[col + 1], sy[row + 1]);
                var dst = Rect.FromEdges(dx[col], dy[row], dx[col + 1], dy[row + 1]);
                if (src.IsEmpty || dst.IsEmpty) continue;
                DrawImage(img, dst, opacity, src);
            }
        }
    }

    /// <summary>
    /// Draws an SVG document fitted (aspect preserved by the document's own rules) into the destination,
    /// optionally rotated around its center. Draws a placeholder when SVG is not supported.
    /// </summary>
    public void DrawSvg(SvgImage svg, Rect destination, float rotationDegrees = 0f, float opacity = 1f)
    {
        var document = _renderer.GetSvgDocument(svg);
        if (document is null)
        {
            DrawSvgPlaceholder(destination);
            return;
        }

        var viewport = new D2D_SIZE_F { width = destination.Width, height = destination.Height };
        document->SetViewportSize(viewport);
        var local = Matrix3x2.CreateTranslation(destination.X, destination.Y);
        if (rotationDegrees != 0f)
        {
            local *= Matrix3x2.CreateRotation(rotationDegrees * MathF.PI / 180f, destination.Center.ToVector());
        }

        // SVG documents ignore the brush alpha: compose the effective opacity with a real layer.
        float effective = Math.Clamp(opacity, 0f, 1f) * _alpha;
        bool layer = effective < 0.999f;
        if (layer) PushGeometryLayer(null, effective);
        PushTransform(local);
        _renderer.SvgContext->DrawSvgDocument(document);
        PopTransform();
        if (layer) PopState();
    }

    private void DrawSvgPlaceholder(Rect r)
    {
        var c = Color.FromRgb(0x808080, 0.5f);
        DrawRect(r.Inflate(-0.5f), c, 1f, StrokeStyle.Dashed);
        DrawLine(r.Location, new Point(r.Right, r.Bottom), c);
        DrawLine(new Point(r.Right, r.Y), new Point(r.X, r.Bottom), c);
    }

    // =====================================================================================
    // State: clip, opacity, transform
    // =====================================================================================

    /// <summary>Restricts drawing to a rectangle until <see cref="PopClip"/>.</summary>
    public void PushClip(Rect rect)
    {
        if (IsAxisAligned(_transform))
        {
            var r = ToD2D(rect);
            Target->PushAxisAlignedClip(&r, D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
            _states.Push(StateKind.AxisClip);
        }
        else
        {
            PushGeometryLayer(BuildRectGeometry(rect), 1f);
        }
        _clipBounds.Push(_clipBounds.Count > 0 ? _clipBounds.Peek().Intersect(rect) : rect);
    }

    /// <summary>Restricts drawing to a rounded rectangle until <see cref="PopClip"/>.</summary>
    public void PushRoundedClip(Rect rect, float radius)
    {
        ID2D1RoundedRectangleGeometry* geometry;
        var rr = RoundedRect(rect, radius);
        if (_renderer.Factory->CreateRoundedRectangleGeometry(&rr, &geometry).FAILED)
        {
            PushClip(rect);
            return;
        }
        PushGeometryLayer((ID2D1Geometry*)geometry, 1f);
        _clipBounds.Push(_clipBounds.Count > 0 ? _clipBounds.Peek().Intersect(rect) : rect);
    }

    /// <summary>Pops the last clip.</summary>
    public void PopClip()
    {
        if (_clipBounds.Count > 0) _clipBounds.Pop();
        PopState();
    }

    /// <summary>
    /// Multiplies the opacity of everything drawn until <see cref="PopOpacity"/> (VSTGUI-style global alpha:
    /// each drawing operation is attenuated, so overlapping shapes of a translucent element show through each
    /// other). Cheap, and supported by every Direct2D implementation.
    /// </summary>
    public void PushOpacity(float opacity)
    {
        _alphas.Push(_alpha);
        _alpha *= Math.Clamp(opacity, 0f, 1f);
        _states.Push(StateKind.Alpha);
    }

    /// <summary>
    /// Composes everything drawn until <see cref="PopOpacity"/> as one group with the given opacity (Direct2D layer):
    /// exact for overlapping content, more expensive than <see cref="PushOpacity"/>.
    /// </summary>
    public void PushOpacityLayer(float opacity) => PushGeometryLayer(null, Math.Clamp(opacity, 0f, 1f));

    /// <summary>Pops the last <see cref="PushOpacity"/> or <see cref="PushOpacityLayer"/>.</summary>
    public void PopOpacity() => PopState();

    /// <summary>Current global alpha multiplier (product of the pushed opacities).</summary>
    public float GlobalAlpha => _alpha;

    /// <summary>Applies a local transform until <see cref="PopTransform"/>.</summary>
    public void PushTransform(Matrix3x2 transform)
    {
        _transforms.Push(_transform);
        _transform = transform * _transform;
        _states.Push(StateKind.Transform);
        ApplyTransform();
    }

    /// <summary>Pops the last transform.</summary>
    public void PopTransform() => PopState();

    /// <summary>Clip scope: <c>using (dc.Clip(r)) { … }</c>.</summary>
    public StateScope Clip(Rect rect) { PushClip(rect); return new StateScope(this, StateScope.Kind.Clip); }

    /// <summary>Opacity scope.</summary>
    public StateScope Opacity(float opacity) { PushOpacity(opacity); return new StateScope(this, StateScope.Kind.Opacity); }

    /// <summary>Transform scope.</summary>
    public StateScope Transform(Matrix3x2 transform) { PushTransform(transform); return new StateScope(this, StateScope.Kind.Transform); }

    /// <summary>Rotation scope around a point (degrees, clockwise).</summary>
    public StateScope Rotate(float degrees, Point center) => Transform(Matrix3x2.CreateRotation(degrees * MathF.PI / 180f, center.ToVector()));

    /// <summary>Snaps a rectangle to device pixels (crisp hairlines).</summary>
    public Rect Snap(Rect rect) => rect.Snap(Scale);

    /// <summary>Thickness of one device pixel in DIPs.</summary>
    public float Hairline => 1f / MathF.Max(0.01f, Scale);

    private void PushGeometryLayer(ID2D1Geometry* mask, float opacity)
    {
        var parameters = new D2D1_LAYER_PARAMETERS
        {
            contentBounds = new D2D_RECT_F { left = float.MinValue, top = float.MinValue, right = float.MaxValue, bottom = float.MaxValue },
            geometricMask = mask,
            maskAntialiasMode = D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = ToD2D(Matrix3x2.Identity),
            opacity = opacity,
            opacityBrush = null,
            layerOptions = D2D1_LAYER_OPTIONS_NONE,
        };
        Target->PushLayer(&parameters, null);
        if (mask is not null) mask->Release(); // the layer keeps its own reference while pushed
        _states.Push(StateKind.Layer);
    }

    private void PopState()
    {
        if (_states.Count == 0) return;
        switch (_states.Pop())
        {
            case StateKind.AxisClip:
                Target->PopAxisAlignedClip();
                break;
            case StateKind.Layer:
                Target->PopLayer();
                break;
            case StateKind.Transform:
                _transform = _transforms.Count > 0 ? _transforms.Pop() : Matrix3x2.Identity;
                ApplyTransform();
                break;
            case StateKind.Alpha:
                _alpha = _alphas.Count > 0 ? _alphas.Pop() : 1f;
                break;
        }
    }

    private void ApplyTransform()
    {
        var m = ToD2D(_transform);
        Target->SetTransform(&m);
    }

    private static bool IsAxisAligned(Matrix3x2 m) => MathF.Abs(m.M12) < 1e-6f && MathF.Abs(m.M21) < 1e-6f;

    // =====================================================================================
    // Geometry building
    // =====================================================================================

    private ID2D1Geometry* BuildRectGeometry(Rect rect)
    {
        ID2D1RectangleGeometry* geometry;
        var r = ToD2D(rect);
        return _renderer.Factory->CreateRectangleGeometry(&r, &geometry).SUCCEEDED ? (ID2D1Geometry*)geometry : null;
    }

    private ID2D1PathGeometry* BuildPolyline(ReadOnlySpan<Point> points, bool closed)
    {
        ID2D1PathGeometry* geometry;
        if (_renderer.Factory->CreatePathGeometry(&geometry).FAILED) return null;
        ID2D1GeometrySink* sink;
        if (geometry->Open(&sink).FAILED) { geometry->Release(); return null; }
        sink->BeginFigure(ToD2D(points[0]), D2D1_FIGURE_BEGIN_FILLED);
        for (int i = 1; i < points.Length; i++) sink->AddLine(ToD2D(points[i]));
        sink->EndFigure(closed ? D2D1_FIGURE_END_CLOSED : D2D1_FIGURE_END_OPEN);
        sink->Close();
        sink->Release();
        return geometry;
    }

    private ID2D1PathGeometry* BuildGeometry(PathGeometry path)
    {
        if (path.IsEmpty) return null;
        ID2D1PathGeometry* geometry;
        if (_renderer.Factory->CreatePathGeometry(&geometry).FAILED) return null;
        ID2D1GeometrySink* sink;
        if (geometry->Open(&sink).FAILED) { geometry->Release(); return null; }
        sink->SetFillMode(path.Winding ? D2D1_FILL_MODE_WINDING : D2D1_FILL_MODE_ALTERNATE);

        bool open = false;
        foreach (var c in path.Commands)
        {
            switch (c.Kind)
            {
                case PathGeometry.CommandKind.Move:
                    if (open) sink->EndFigure(D2D1_FIGURE_END_OPEN);
                    sink->BeginFigure(ToD2D(c.P1), D2D1_FIGURE_BEGIN_FILLED);
                    open = true;
                    break;
                case PathGeometry.CommandKind.Line when open:
                    sink->AddLine(ToD2D(c.P1));
                    break;
                case PathGeometry.CommandKind.Quad when open:
                {
                    var q = new D2D1_QUADRATIC_BEZIER_SEGMENT { point1 = ToD2D(c.P1), point2 = ToD2D(c.P2) };
                    sink->AddQuadraticBezier(&q);
                    break;
                }
                case PathGeometry.CommandKind.Cubic when open:
                {
                    var b = new D2D1_BEZIER_SEGMENT { point1 = ToD2D(c.P1), point2 = ToD2D(c.P2), point3 = ToD2D(c.P3) };
                    sink->AddBezier(&b);
                    break;
                }
                case PathGeometry.CommandKind.Arc when open:
                {
                    var a = new D2D1_ARC_SEGMENT
                    {
                        point = ToD2D(c.P1),
                        size = new D2D_SIZE_F { width = c.Radius.Width, height = c.Radius.Height },
                        rotationAngle = c.Rotation,
                        sweepDirection = c.Clockwise ? D2D1_SWEEP_DIRECTION_CLOCKWISE : D2D1_SWEEP_DIRECTION_COUNTER_CLOCKWISE,
                        arcSize = c.LargeArc ? D2D1_ARC_SIZE_LARGE : D2D1_ARC_SIZE_SMALL,
                    };
                    sink->AddArc(&a);
                    break;
                }
                case PathGeometry.CommandKind.Close when open:
                    sink->EndFigure(D2D1_FIGURE_END_CLOSED);
                    open = false;
                    break;
            }
        }
        if (open) sink->EndFigure(D2D1_FIGURE_END_OPEN);
        sink->Close();
        sink->Release();
        return geometry;
    }

    // =====================================================================================
    // Conversions
    // =====================================================================================

    private static DXGI_RGBA ToD2D(Color c) => new() { r = c.R, g = c.G, b = c.B, a = c.A };

    private static D2D_RECT_F ToD2D(Rect r) => new() { left = r.X, top = r.Y, right = r.Right, bottom = r.Bottom };

    private static Rect FromD2D(D2D_RECT_F r) => Rect.FromEdges(r.left, r.top, r.right, r.bottom);

    private static D2D_POINT_2F ToD2D(Point p) => new() { x = p.X, y = p.Y };

    private static D2D1_ROUNDED_RECT RoundedRect(Rect rect, float radius)
    {
        float max = MathF.Min(rect.Width, rect.Height) * 0.5f;
        float r = Math.Clamp(radius, 0f, max);
        return new D2D1_ROUNDED_RECT { rect = ToD2D(rect), radiusX = r, radiusY = r };
    }

    private static D2D_MATRIX_3X2_F ToD2D(Matrix3x2 m) => new() { _11 = m.M11, _12 = m.M12, _21 = m.M21, _22 = m.M22, _31 = m.M31, _32 = m.M32 };

    /// <summary>
    /// Disposable scope returned by <see cref="Clip(Rect)"/>, <see cref="Opacity(float)"/> and <see cref="Transform(Matrix3x2)"/>.
    /// </summary>
    public readonly struct StateScope : IDisposable
    {
        internal enum Kind : byte { Clip, Opacity, Transform }

        private readonly DrawingContext _context;
        private readonly Kind _kind;

        internal StateScope(DrawingContext context, Kind kind)
        {
            _context = context;
            _kind = kind;
        }

        /// <summary>Pops the state.</summary>
        public void Dispose()
        {
            switch (_kind)
            {
                case Kind.Clip: _context.PopClip(); break;
                case Kind.Opacity: _context.PopOpacity(); break;
                default: _context.PopTransform(); break;
            }
        }
    }
}

internal static class PointExtensions
{
    public static Vector2 ToVector(this Point p) => new(p.X, p.Y);
}
