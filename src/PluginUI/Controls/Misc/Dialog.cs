// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>Button sets of <see cref="Dialog.ShowMessage"/>.</summary>
public enum MessageBoxButtons
{
    /// <summary>OK.</summary>
    OK,
    /// <summary>OK and Cancel.</summary>
    OKCancel,
    /// <summary>Yes and No.</summary>
    YesNo,
    /// <summary>Yes, No and Cancel.</summary>
    YesNoCancel,
}

/// <summary>Result of a message box.</summary>
public enum MessageBoxResult
{
    /// <summary>Closed without choosing.</summary>
    None,
    /// <summary>OK.</summary>
    OK,
    /// <summary>Cancel (also Escape).</summary>
    Cancel,
    /// <summary>Yes.</summary>
    Yes,
    /// <summary>No.</summary>
    No,
}

/// <summary>
/// Modal dialog drawn inside the host window: title, body element and a row of buttons. Enter chooses the default
/// button, Escape the cancel button. Plug-in editors cannot rely on native modal windows (they would block the DAW),
/// so dialogs are popups: the rest of the editor is dimmed and inert while one is open.
/// </summary>
/// <example>
/// <code>
/// Dialog.ShowMessage(host, "Reset preset", "Discard your changes?", MessageBoxButtons.YesNo,
///     r => { if (r == MessageBoxResult.Yes) ResetPreset(); });
/// </code>
/// </example>
public class Dialog : Popup
{
    private readonly StackPanel _buttons;
    private UIElement? _body;
    private object? _cancelResult;
    private bool _hasCancel;

    /// <summary>Creates an empty dialog (add a body with <see cref="Content"/> and buttons with <see cref="AddButton"/>).</summary>
    public Dialog()
    {
        IsModal = true;
        IsLightDismiss = false;
        CapturesKeyboard = true;
        Placement = PopupPlacement.Center;
        Padding = new Thickness(20f, 16f, 20f, 16f);
        _buttons = new StackPanel(Orientation.Horizontal, 8f) { HorizontalAlignment = HorizontalAlignment.Right };
        Children.Add(_buttons);
    }

    /// <summary>Creates a dialog with a title and a body.</summary>
    public Dialog(string? title, UIElement? body = null) : this()
    {
        Title = title;
        Content = body;
    }

    /// <summary>Title (null = none).</summary>
    [Category("Text")]
    public string? Title { get; set; }

    /// <summary>Body element (hides <see cref="ContentPanel.Content"/>: the dialog manages its own children).</summary>
    [Browsable(false)]
    public new UIElement? Content
    {
        get => _body;
        set
        {
            if (ReferenceEquals(_body, value)) return;
            if (_body is not null) Children.Remove(_body);
            _body = value;
            if (value is not null) Children.Insert(0, value);
        }
    }

    /// <summary>Preferred width of the dialog.</summary>
    [Category("Layout")]
    public float DialogWidth { get; set; } = 380f;

    /// <summary>Result chosen by the user (null until a button is chosen).</summary>
    [Browsable(false)]
    public object? Result { get; private set; }

    /// <summary>Raised when a button is chosen (before the dialog closes).</summary>
    public event Action<Dialog, object?>? ResultChosen;

    /// <summary>Captions of the buttons added with <see cref="AddButton"/>, from left to right.</summary>
    public IReadOnlyList<string> ButtonCaptions => [.. _buttons.Children.OfType<DialogButton>().Select(b => b.Caption)];

    /// <summary>Adds a button (fluent). The default button reacts to Enter, the cancel button to Escape.</summary>
    public Dialog AddButton(string text, object? result, bool isDefault = false, bool isCancel = false)
    {
        _buttons.Children.Add(new DialogButton(this, text, result, isDefault) { MinWidth = 84f });
        if (isCancel)
        {
            _cancelResult = result;
            _hasCancel = true;
        }
        return this;
    }

    /// <summary>Chooses a result and closes the dialog.</summary>
    public void Close(object? result)
    {
        Result = result;
        ResultChosen?.Invoke(this, result);
        Close();
    }

    /// <summary>Shows a message box. Returns the dialog (already open).</summary>
    public static Dialog ShowMessage(UIHost host, string title, string message, MessageBoxButtons buttons = MessageBoxButtons.OK, Action<MessageBoxResult>? callback = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        var dialog = new Dialog(title, new DialogText(message));
        var t = host.Strings; // button captions in the host's interface language
        switch (buttons)
        {
            case MessageBoxButtons.OKCancel:
                dialog.AddButton(t.Cancel, MessageBoxResult.Cancel, isCancel: true).AddButton(t.Ok, MessageBoxResult.OK, isDefault: true);
                break;
            case MessageBoxButtons.YesNo:
                dialog.AddButton(t.No, MessageBoxResult.No, isCancel: true).AddButton(t.Yes, MessageBoxResult.Yes, isDefault: true);
                break;
            case MessageBoxButtons.YesNoCancel:
                dialog.AddButton(t.Cancel, MessageBoxResult.Cancel, isCancel: true).AddButton(t.No, MessageBoxResult.No).AddButton(t.Yes, MessageBoxResult.Yes, isDefault: true);
                break;
            default:
                dialog.AddButton(t.Ok, MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
        }
        if (callback is not null) dialog.Closed += d => callback(((Dialog)d).Result is MessageBoxResult r ? r : MessageBoxResult.None);
        host.ShowPopup(dialog);
        return dialog;
    }

    // ---------------------------------------------------------------- layout & rendering

    private TextStyle TitleStyle => Theme.Typography.Large with { Weight = FontWeight.SemiBold };

    private float TitleHeight => string.IsNullOrEmpty(Title) ? 0f : TitleStyle.Size * 1.5f + 10f;

    private bool HasButtons => _buttons.Children.Count > 0;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float width = MathF.Min(DialogWidth, MathF.Max(160f, available.Width - 24f));
        float inner = width - Padding.Horizontal;
        float h = Padding.Vertical + TitleHeight;
        if (_body is not null)
        {
            _body.Measure(new Size(inner, float.PositiveInfinity));
            h += _body.DesiredSize.Height;
            width = MathF.Max(width, MathF.Min(_body.DesiredSize.Width + Padding.Horizontal, available.Width - 24f));
        }
        if (HasButtons)
        {
            _buttons.Measure(new Size(inner, float.PositiveInfinity));
            h += _buttons.DesiredSize.Height + (_body is null ? 0f : 18f);
            width = MathF.Max(width, _buttons.DesiredSize.Width + Padding.Horizontal);
        }
        return new Size(width, h);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Rect bounds)
    {
        var r = bounds.Deflate(Padding);
        float y = r.Y + TitleHeight;
        float buttonsH = HasButtons ? _buttons.DesiredSize.Height : 0f;
        if (_body is not null)
        {
            float bodyBottom = r.Bottom - buttonsH - (HasButtons ? 18f : 0f);
            _body.Arrange(Rect.FromEdges(r.X, y, r.Right, MathF.Max(y, bodyBottom)));
        }
        _buttons.Arrange(Rect.FromEdges(r.X, r.Bottom - buttonsH, r.Right, r.Bottom));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        var r = Bounds;
        const float radius = 10f;
        dc.DrawShadow(r, radius, 24f, new Point(0f, 8f), p.Shadow.WithAlpha(MathF.Min(1f, p.Shadow.A * 2.5f)));
        dc.FillRoundedRect(r, radius, p.SurfaceRaised);
        dc.DrawRoundedRect(r.Inflate(-0.5f), radius, p.Border, 1f);
        if (!string.IsNullOrEmpty(Title))
        {
            var t = r.Deflate(Padding);
            dc.DrawText(Title, new Rect(t.X, t.Y, t.Width, TitleStyle.Size * 1.5f), TitleStyle, p.Text);
        }
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        var host = Host!;
        if (host.IsAttached)
        {
            Opacity = 0f;
            host.Animator.Fade(this, 1f, 0.14);
        }
        host.Post(FocusDefaultButton);
    }

    private void FocusDefaultButton()
    {
        foreach (var child in _buttons.Children)
        {
            if (child is DialogButton { IsDefault: true } b) { b.Focus(); return; }
        }
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(_hasCancel ? _cancelResult : null);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Enter or Key.Space)
        {
            var focused = Host?.FocusedElement as DialogButton;
            if (focused is not null && ReferenceEquals(focused.Owner, this)) { focused.Choose(); e.Handled = true; return; }
            if (e.Key == Key.Enter)
            {
                foreach (var child in _buttons.Children)
                {
                    if (child is DialogButton { IsDefault: true } b) { b.Choose(); e.Handled = true; return; }
                }
            }
        }
        if (e.Key is Key.Left or Key.Right && Host is { } host)
        {
            host.MoveFocus(e.Key == Key.Right);
            host.ShowFocusRings = true;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Push button of a dialog.</summary>
    private sealed class DialogButton : Control
    {
        private readonly string _text;
        private readonly object? _result;

        public string Caption => _text;

        public DialogButton(Dialog owner, string text, object? result, bool isDefault)
        {
            Owner = owner;
            _text = text;
            _result = result;
            IsDefault = isDefault;
        }

        public Dialog Owner { get; }

        public bool IsDefault { get; }

        protected override Size DefaultSize => new(MathF.Max(84f, TextMeasurer.Measure(_text, Theme.Typography.Normal).Width + 28f), 30f);

        public void Choose() => Owner.Close(_result);

        protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
        {
            var p = Theme.Palette;
            float radius = 6f;
            var fill = IsDefault ? (IsPressed ? p.Accent.Darken(0.15f) : IsMouseOver ? p.Accent.Lighten(0.08f) : p.Accent)
                                 : (IsPressed ? p.Surface : IsMouseOver ? p.SurfaceRaised.Lighten(0.06f) : p.SurfaceRaised.Lighten(0.03f));
            dc.FillRoundedRect(widget, radius, fill);
            if (!IsDefault) dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, p.Border, 1f);
            var text = Theme.Typography.Normal with { Align = HorizontalAlignment.Center, Weight = IsDefault ? FontWeight.SemiBold : FontWeight.Normal };
            dc.DrawText(_text, widget, text, IsDefault ? p.Accent.Contrasting : p.Text);
        }

        protected internal override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left) return;
            IsPressed = true;
            e.Handled = true;
            InvalidateVisual();
        }

        protected internal override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool click = IsPressed && Bounds.Contains(e.Position);
            IsPressed = false;
            InvalidateVisual();
            if (click) Choose();
        }

        protected internal override void OnCaptureLost()
        {
            base.OnCaptureLost();
            IsPressed = false;
        }
    }
}

/// <summary>Wrapped text block used by dialogs (and usable anywhere a paragraph is needed).</summary>
public class DialogText : UIElement
{
    /// <summary>Creates a paragraph.</summary>
    public DialogText(string text) => Text = text;

    /// <summary>Text.</summary>
    [Category("Text")]
    public string Text { get; set; }

    /// <summary>Style (null = theme normal, wrapping).</summary>
    [Category("Appearance")]
    public TextStyle? Style { get; set; }

    /// <summary>Color (null = theme dim text).</summary>
    [Category("Value")]
    public Color? Color { get; set; }

    private TextStyle EffectiveStyle => (Style ?? Theme.Typography.Normal) with { Wrap = true, Ellipsis = false, VerticalAlign = VerticalAlignment.Top };

    /// <inheritdoc />
    protected override Size MeasureOverride(Size available)
    {
        float max = float.IsFinite(available.Width) ? available.Width : 420f;
        var size = TextMeasurer.Measure(Text, EffectiveStyle, max);
        return new Size(MathF.Min(size.Width, max), size.Height);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc) => dc.DrawText(Text, Bounds.Inflate(1f, 0f), EffectiveStyle, Color ?? Theme.Palette.TextDim);
}
