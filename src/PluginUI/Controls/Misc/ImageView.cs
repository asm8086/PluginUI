// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Displays a bitmap, or plays a film-strip animation (VSTGUI <c>CMovieBitmap</c>): logos, backgrounds,
/// animated indicators. The animation runs on the host frame timer only while <see cref="IsPlaying"/>.
/// </summary>
public class ImageView : Control
{
    private ImageSource? _image;
    private FilmStrip? _strip;
    private int _frame;
    private bool _playing;
    private Player? _player;

    /// <summary>Creates an empty image view.</summary>
    public ImageView()
    {
        Focusable = false;
    }

    /// <summary>Creates a view showing <paramref name="image"/>.</summary>
    public ImageView(ImageSource image) : this() => _image = image;

    /// <summary>Creates a view playing <paramref name="strip"/>.</summary>
    public ImageView(FilmStrip strip) : this() => _strip = strip;

    /// <summary>Static image (ignored when <see cref="FilmStrip"/> is set).</summary>
    [Category("Appearance")]
    public ImageSource? Image { get => _image; set { _image = value; InvalidateMeasure(); } }

    /// <summary>Film-strip to show or animate.</summary>
    [Category("Appearance")]
    public FilmStrip? FilmStrip
    {
        get => _strip;
        set
        {
            _strip = value;
            _frame = 0;
            InvalidateMeasure();
        }
    }

    /// <summary>Fitting mode.</summary>
    [Category("Appearance")]
    public ImageFit Fit { get; set; } = ImageFit.Contain;

    /// <summary>Frame shown when not playing (film-strip only).</summary>
    [Category("Appearance")]
    public int Frame
    {
        get => _frame;
        set
        {
            int count = Math.Max(1, _strip?.FrameCount ?? 1);
            value = ((value % count) + count) % count;
            if (value == _frame) return;
            _frame = value;
            InvalidateVisual();
        }
    }

    /// <summary>Animation speed.</summary>
    [Category("Data")]
    public double FramesPerSecond { get; set; } = 24.0;

    /// <summary>Restarts at the first frame after the last one; otherwise stops on the last frame.</summary>
    [Category("Data")]
    public bool Loop { get; set; } = true;

    /// <summary>Plays back and forth (ping-pong) instead of wrapping.</summary>
    [Category("Data")]
    public bool PingPong { get; set; }

    /// <summary>Starts playing as soon as the view is attached.</summary>
    [Category("Data")]
    public bool AutoPlay { get; set; }

    /// <summary>Smooth (bilinear) scaling; turn off for pixel art.</summary>
    [Category("Appearance")]
    public bool Smooth { get; set; } = true;

    /// <summary><c>true</c> while the film-strip animation runs.</summary>
    [Category("Behavior")]
    public bool IsPlaying
    {
        get => _playing;
        set
        {
            if (_playing == value) return;
            _playing = value;
            if (value) StartPlayer(); else if (_player is not null) Host?.RemoveTicker(_player);
        }
    }

    /// <summary>Raised when a non-looping animation reaches its last frame.</summary>
    public event Action<ImageView>? Completed;

    /// <summary>Starts the animation.</summary>
    public void Play() => IsPlaying = true;

    /// <summary>Stops the animation (keeps the current frame).</summary>
    public void Stop() => IsPlaying = false;

    private Size NaturalSize => _strip?.FrameSize ?? _image?.Size ?? Size.Zero;

    /// <inheritdoc />
    protected override Size DefaultSize => NaturalSize.IsEmpty ? new Size(32f, 32f) : NaturalSize;

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        if (_strip is { } strip)
        {
            dc.DrawFilmStripFrame(strip, _frame, ImageLayout.Fit(strip.FrameSize, widget, Fit));
        }
        else if (_image is { } image)
        {
            dc.DrawImage(image, ImageLayout.Fit(image.Size, widget, Fit), 1f, null, Smooth);
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AutoPlay) _playing = true;
        if (_playing) StartPlayer();
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_player is not null) Host?.RemoveTicker(_player);
        base.OnDetached();
    }

    private void StartPlayer()
    {
        if (Host is null) return;
        _player ??= new Player(this);
        _player.Reset();
        Host.AddTicker(_player);
    }

    private sealed class Player(ImageView owner) : ITickable
    {
        private double _accumulator;
        private int _direction = 1;

        public void Reset() => _accumulator = 0;

        public bool Tick(double elapsedSeconds)
        {
            if (!owner._playing || owner._strip is not { FrameCount: > 1 } strip) return owner._playing;
            _accumulator += elapsedSeconds * Math.Max(0.01, owner.FramesPerSecond);
            int steps = (int)_accumulator;
            if (steps == 0) return true;
            _accumulator -= steps;
            int frame = owner._frame;
            int last = strip.FrameCount - 1;
            for (int i = 0; i < steps; i++)
            {
                frame += _direction;
                if (frame > last || frame < 0)
                {
                    if (owner.PingPong)
                    {
                        _direction = -_direction;
                        frame = Math.Clamp(frame + 2 * _direction, 0, last);
                    }
                    else if (owner.Loop)
                    {
                        frame = 0;
                    }
                    else
                    {
                        owner._frame = last;
                        owner._playing = false;
                        owner.InvalidateVisual();
                        owner.Completed?.Invoke(owner);
                        return false;
                    }
                }
            }
            owner._frame = frame;
            owner.InvalidateVisual();
            return true;
        }
    }
}

/// <summary>Displays an SVG image (fit, rotation, opacity). Native SVG needs Windows 10 1703 or later.</summary>
public class SvgView : Control
{
    private SvgImage? _svg;
    private float _rotation;

    /// <summary>Creates an empty SVG view.</summary>
    public SvgView()
    {
        Focusable = false;
    }

    /// <summary>Creates a view showing <paramref name="svg"/>.</summary>
    public SvgView(SvgImage svg) : this() => _svg = svg;

    /// <summary>SVG document.</summary>
    [Category("Appearance")]
    public SvgImage? Svg { get => _svg; set { _svg = value; InvalidateMeasure(); } }

    /// <summary>Fitting mode.</summary>
    [Category("Appearance")]
    public ImageFit Fit { get; set; } = ImageFit.Contain;

    /// <summary>Rotation in degrees (clockwise) around the center.</summary>
    [Category("Appearance")]
    public float Rotation { get => _rotation; set { _rotation = value; InvalidateVisual(); } }

    /// <inheritdoc />
    protected override Size DefaultSize => _svg is null || _svg.IntrinsicSize.IsEmpty ? new Size(32f, 32f) : _svg.IntrinsicSize;

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        if (_svg is null) return;
        dc.DrawSvg(_svg, ImageLayout.Fit(_svg.IntrinsicSize, widget, Fit), _rotation);
    }
}
