// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// "About" dialog: logo, product name, version, a free text and a clickable URL. A click anywhere (outside the link)
/// or Escape closes it.
/// </summary>
public class AboutBox : Dialog
{
    private readonly AboutContent _content;

    /// <summary>Creates an about box.</summary>
    public AboutBox(string productName, string? version = null)
    {
        _content = new AboutContent(this) { ProductName = productName, Version = version };
        Content = _content;
        DialogWidth = 340f;
        Padding = new Thickness(24f, 24f, 24f, 20f);
        IsLightDismiss = true;
    }

    /// <summary>Product name.</summary>
    [Category("Text")]
    public string ProductName { get => _content.ProductName; set => _content.ProductName = value; }

    /// <summary>Version text (e.g. "1.2.0").</summary>
    [Category("Text")]
    public string? Version { get => _content.Version; set => _content.Version = value; }

    /// <summary>Free text (credits, copyright); wraps.</summary>
    [Category("Text")]
    public string? Text { get => _content.Text; set => _content.Text = value; }

    /// <summary>Web site opened when the link is clicked.</summary>
    [Category("Text")]
    public string? Url { get => _content.Url; set => _content.Url = value; }

    /// <summary>Bitmap logo (takes precedence over <see cref="SvgLogo"/>).</summary>
    [Category("Appearance")]
    public ImageSource? Logo { get => _content.Logo; set => _content.Logo = value; }

    /// <summary>SVG logo.</summary>
    [Category("Appearance")]
    public SvgImage? SvgLogo { get => _content.SvgLogo; set => _content.SvgLogo = value; }

    /// <summary>Logo size in DIPs.</summary>
    [Category("Layout")]
    public float LogoSize { get => _content.LogoSize; set => _content.LogoSize = value; }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left) return;
        e.Handled = true;
        Close(null);
    }

    private sealed class AboutContent(AboutBox owner) : UIElement
    {
        private bool _linkHot;

        public string ProductName { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string? Text { get; set; }
        public string? Url { get; set; }
        public ImageSource? Logo { get; set; }
        public SvgImage? SvgLogo { get; set; }
        public float LogoSize { get; set; } = 64f;

        private bool HasLogo => Logo is not null || SvgLogo is not null;
        private TextStyle NameStyle => Theme.Typography.Title with { Align = HorizontalAlignment.Center, Size = Theme.Typography.TitleSize + 2f };
        private TextStyle VersionStyle => Theme.Typography.Small with { Align = HorizontalAlignment.Center };
        private TextStyle BodyStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Center, Wrap = true, Ellipsis = false, VerticalAlign = VerticalAlignment.Top };
        private TextStyle LinkStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Center };

        private float BodyHeight(float width) => string.IsNullOrEmpty(Text) ? 0f : TextMeasurer.Measure(Text, BodyStyle, width).Height;

        protected override Size MeasureOverride(Size available)
        {
            float w = float.IsFinite(available.Width) ? available.Width : 300f;
            float h = (HasLogo ? LogoSize + 14f : 0f) + NameStyle.Size * 1.4f;
            if (!string.IsNullOrEmpty(Version)) h += VersionStyle.Size * 1.6f;
            if (!string.IsNullOrEmpty(Text)) h += 14f + BodyHeight(w);
            if (!string.IsNullOrEmpty(Url)) h += 12f + LinkStyle.Size * 1.5f;
            return new Size(w, h);
        }

        private Rect LinkRect
        {
            get
            {
                if (string.IsNullOrEmpty(Url)) return Rect.Empty;
                float h = LinkStyle.Size * 1.5f;
                float w = TextMeasurer.Measure(Url, LinkStyle).Width + 8f;
                return new Rect(Bounds.Center.X - w * 0.5f, Bounds.Bottom - h, w, h);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var p = Theme.Palette;
            var r = Bounds;
            float y = r.Y;
            if (HasLogo)
            {
                var logo = new Rect(r.Center.X - LogoSize * 0.5f, y, LogoSize, LogoSize);
                if (Logo is not null) dc.DrawImage(Logo, ImageLayout.Fit(Logo.Size, logo, ImageFit.Contain));
                else dc.DrawSvg(SvgLogo!, ImageLayout.Fit(SvgLogo!.IntrinsicSize, logo, ImageFit.Contain));
                y += LogoSize + 14f;
            }
            float nameH = NameStyle.Size * 1.4f;
            dc.DrawText(ProductName, new Rect(r.X, y, r.Width, nameH), NameStyle, p.Text);
            y += nameH;
            if (!string.IsNullOrEmpty(Version))
            {
                float vh = VersionStyle.Size * 1.6f;
                dc.DrawText(Strings.FormatVersion(Version), new Rect(r.X, y, r.Width, vh), VersionStyle, p.TextDim);
                y += vh;
            }
            if (!string.IsNullOrEmpty(Text))
            {
                y += 14f;
                float bh = BodyHeight(r.Width);
                dc.DrawText(Text, new Rect(r.X, y, r.Width, bh + 2f), BodyStyle, p.TextDim);
            }
            if (!string.IsNullOrEmpty(Url))
            {
                var link = LinkRect;
                var color = _linkHot ? p.Accent.Lighten(0.2f) : p.Accent;
                dc.DrawText(Url, link, LinkStyle, color);
                if (_linkHot)
                {
                    float w = TextMeasurer.Measure(Url, LinkStyle).Width;
                    float uy = link.Center.Y + LinkStyle.Size * 0.62f;
                    dc.DrawLine(new Point(link.Center.X - w * 0.5f, uy), new Point(link.Center.X + w * 0.5f, uy), color, 1f);
                }
            }
        }

        protected internal override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = LinkRect.Contains(e.Position);
            if (hot == _linkHot) return;
            _linkHot = hot;
            Cursor = hot ? StandardCursor.Hand : StandardCursor.Default;
            InvalidateVisual();
        }

        protected internal override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _linkHot = false;
            Cursor = StandardCursor.Default;
        }

        protected internal override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left) return;
            e.Handled = true;
            if (LinkRect.Contains(e.Position) && Url is { Length: > 0 } url)
            {
                Shell.Open(url);
                return;
            }
            owner.Close(null);
        }
    }
}

/// <summary>
/// Full-editor splash overlay (logo, title, subtitle) shown at startup; fades out after <see cref="Duration"/> or on
/// click. Show it with <see cref="UIHost.ShowPopup"/>.
/// </summary>
public class SplashScreen : Popup
{
    private bool _closing;

    /// <summary>Creates a splash screen.</summary>
    public SplashScreen(string? title = null, string? subtitle = null)
    {
        Title = title;
        Subtitle = subtitle;
        IsModal = true;
        IsLightDismiss = false;
        CapturesKeyboard = true;
        Placement = PopupPlacement.Center;
    }

    /// <summary>Big title.</summary>
    [Category("Text")]
    public string? Title { get; set; }

    /// <summary>Smaller text under the title (version, loading status...).</summary>
    [Category("Text")]
    public string? Subtitle { get; set; }

    /// <summary>Bitmap shown centered above the title (or as a background with <see cref="ImageAsBackground"/>).</summary>
    [Category("Appearance")]
    public ImageSource? Image { get; set; }

    /// <summary>SVG logo shown centered above the title.</summary>
    [Category("Appearance")]
    public SvgImage? SvgLogo { get; set; }

    /// <summary>Stretches <see cref="Image"/> over the whole editor.</summary>
    [Category("Appearance")]
    public bool ImageAsBackground { get; set; }

    /// <summary>Background (null = theme background).</summary>
    [Category("Appearance")]
    public Paint? Fill { get; set; }

    /// <summary>Seconds before fading out (0 = stays until clicked).</summary>
    [Category("Data")]
    public double Duration { get; set; } = 2.0;

    /// <summary>Fade-out duration in seconds.</summary>
    [Category("Data")]
    public double FadeDuration { get; set; } = 0.45;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available) => available;

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _closing = false;
        Opacity = 1f;
        if (Duration > 0 && Host is { IsAttached: true } host) host.Animator.Start(Duration, _ => { }, Easing.Linear, Dismiss);
    }

    /// <summary>Fades out and closes.</summary>
    public void Dismiss()
    {
        if (_closing || Host is null) return;
        _closing = true;
        if (Host.IsAttached) Host.Animator.Fade(this, 0f, FadeDuration, Close);
        else Close();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds;
        dc.FillRect(r, Fill ?? (Paint)Paint.Vertical(p.Surface, p.Background));
        if (Image is not null && ImageAsBackground) dc.DrawImage(Image, r);

        float titleH = Title is null ? 0f : Theme.Typography.TitleSize * 2.2f;
        float subtitleH = Subtitle is null ? 0f : Theme.Typography.NormalSize * 1.8f;
        Size logo = SvgLogo is not null ? FitLogo(SvgLogo.IntrinsicSize, r) : Image is not null && !ImageAsBackground ? FitLogo(Image.Size, r) : Size.Zero;
        float total = logo.Height + (logo.IsEmpty ? 0f : 18f) + titleH + subtitleH;
        float y = r.Center.Y - total * 0.5f;
        if (!logo.IsEmpty)
        {
            var lr = new Rect(r.Center.X - logo.Width * 0.5f, y, logo.Width, logo.Height);
            if (SvgLogo is not null) dc.DrawSvg(SvgLogo, lr); else dc.DrawImage(Image!, lr);
            y += logo.Height + 18f;
        }
        if (Title is not null)
        {
            dc.DrawText(Title, new Rect(r.X, y, r.Width, titleH), Theme.Typography.Title with { Size = Theme.Typography.TitleSize * 1.6f, Align = HorizontalAlignment.Center, Weight = FontWeight.Bold }, p.Text);
            y += titleH;
        }
        if (Subtitle is not null)
        {
            dc.DrawText(Subtitle, new Rect(r.X, y, r.Width, subtitleH), Theme.Typography.Normal with { Align = HorizontalAlignment.Center }, p.TextDim);
        }
    }

    private static Size FitLogo(Size natural, Rect area)
    {
        if (natural.IsEmpty) return Size.Zero;
        float k = MathF.Min(1f, MathF.Min(area.Width * 0.5f / natural.Width, area.Height * 0.4f / natural.Height));
        return new Size(natural.Width * k, natural.Height * k);
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        e.Handled = true;
        Dismiss();
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        Dismiss();
    }
}
