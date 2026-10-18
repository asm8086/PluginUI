// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Base class of everything in the visual tree: layout (measure/arrange), rendering, hit testing and input.
/// </summary>
/// <remarks>
/// <para><b>Coordinates.</b> <see cref="Bounds"/> are absolute DIPs in host space after layout: rendering and
/// hit testing need no coordinate transforms.</para>
/// <para><b>Layout.</b> WPF-like two-pass layout: <see cref="Measure"/> computes <see cref="DesiredSize"/>
/// from <see cref="MeasureOverride"/>, <see cref="Arrange"/> assigns <see cref="Bounds"/> and calls
/// <see cref="ArrangeOverride"/>. Explicit <see cref="Width"/>/<see cref="Height"/>, min/max sizes,
/// <see cref="Margin"/> and alignments are applied by the base class.</para>
/// <para><b>Extending.</b> Override <see cref="OnRender"/> to draw, <see cref="MeasureOverride"/> to report a
/// natural size, and the <c>On…</c> input methods to react to the mouse and keyboard.</para>
/// <para><b>Threading.</b> UI thread only. Use <see cref="UIHost.Post"/> to marshal work from other threads.</para>
/// </remarks>
public abstract class UIElement
{
    private float _width = float.NaN, _height = float.NaN;
    private float _minWidth, _minHeight, _maxWidth = float.PositiveInfinity, _maxHeight = float.PositiveInfinity;
    private Thickness _margin;
    private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Stretch;
    private VerticalAlignment _verticalAlignment = VerticalAlignment.Stretch;
    private float _left, _top;
    private int _row, _column, _rowSpan = 1, _columnSpan = 1;
    private Side _dock = Side.Top;
    private bool _isVisible = true, _isEnabled = true;
    private float _opacity = 1f;

    // ---------------------------------------------------------------- identity / tree

    /// <summary>Optional name (used by the live editor, <see cref="FindByName"/> and as field name by PluginUIEditor).</summary>
    [Category("Design")]
    public string? Name { get; set; }

    /// <summary>Free user data.</summary>
    [Browsable(false)]
    public object? Tag { get; set; }

    /// <summary>Parent element (null for the root and popups).</summary>
    [Browsable(false)]
    public UIElement? Parent { get; internal set; }

    /// <summary>Host this element is attached to (null when detached).</summary>
    [Browsable(false)]
    public UIHost? Host { get; private set; }

    /// <summary>Child elements in drawing order (back to front).</summary>
    public virtual IReadOnlyList<UIElement> VisualChildren => Array.Empty<UIElement>();

    /// <summary>Theme in effect (host theme, or the default dark theme when detached).</summary>
    public Theme Theme => Host?.Theme ?? Theme.Dark;

    /// <summary>Text measurement (device-accurate when attached, approximate otherwise).</summary>
    public ITextMeasurer TextMeasurer => Host?.TextMeasurer ?? FallbackTextMeasurer.Instance;

    // ---------------------------------------------------------------- layout properties

    /// <summary>Explicit width in DIPs (NaN = automatic).</summary>
    [Category("Layout")]
    public float Width { get => _width; set => SetLayout(ref _width, value); }

    /// <summary>Explicit height in DIPs (NaN = automatic).</summary>
    [Category("Layout")]
    public float Height { get => _height; set => SetLayout(ref _height, value); }

    /// <summary>Minimum width.</summary>
    [Category("Layout")]
    public float MinWidth { get => _minWidth; set => SetLayout(ref _minWidth, value); }

    /// <summary>Minimum height.</summary>
    [Category("Layout")]
    public float MinHeight { get => _minHeight; set => SetLayout(ref _minHeight, value); }

    /// <summary>Maximum width.</summary>
    [Category("Layout")]
    public float MaxWidth { get => _maxWidth; set => SetLayout(ref _maxWidth, value); }

    /// <summary>Maximum height.</summary>
    [Category("Layout")]
    public float MaxHeight { get => _maxHeight; set => SetLayout(ref _maxHeight, value); }

    /// <summary>Outer spacing.</summary>
    [Category("Layout")]
    public Thickness Margin { get => _margin; set { if (_margin != value) { _margin = value; InvalidateMeasure(); } } }

    /// <summary>Horizontal placement in the layout slot.</summary>
    [Category("Layout")]
    public HorizontalAlignment HorizontalAlignment { get => _horizontalAlignment; set { if (_horizontalAlignment != value) { _horizontalAlignment = value; InvalidateMeasure(); } } }

    /// <summary>Vertical placement in the layout slot.</summary>
    [Category("Layout")]
    public VerticalAlignment VerticalAlignment { get => _verticalAlignment; set { if (_verticalAlignment != value) { _verticalAlignment = value; InvalidateMeasure(); } } }

    /// <summary>X position inside a <see cref="Canvas"/>.</summary>
    [Category("Layout")]
    public float Left { get => _left; set => SetLayout(ref _left, value); }

    /// <summary>Y position inside a <see cref="Canvas"/>.</summary>
    [Category("Layout")]
    public float Top { get => _top; set => SetLayout(ref _top, value); }

    /// <summary>Row inside a <see cref="Grid"/>.</summary>
    [Category("Layout")]
    public int Row { get => _row; set { if (_row != value) { _row = value; InvalidateMeasure(); } } }

    /// <summary>Column inside a <see cref="Grid"/>.</summary>
    [Category("Layout")]
    public int Column { get => _column; set { if (_column != value) { _column = value; InvalidateMeasure(); } } }

    /// <summary>Rows spanned inside a <see cref="Grid"/>.</summary>
    [Category("Layout")]
    public int RowSpan { get => _rowSpan; set { if (_rowSpan != value) { _rowSpan = Math.Max(1, value); InvalidateMeasure(); } } }

    /// <summary>Columns spanned inside a <see cref="Grid"/>.</summary>
    [Category("Layout")]
    public int ColumnSpan { get => _columnSpan; set { if (_columnSpan != value) { _columnSpan = Math.Max(1, value); InvalidateMeasure(); } } }

    /// <summary>Docking side inside a <see cref="DockPanel"/>.</summary>
    [Category("Layout")]
    public Side Dock { get => _dock; set { if (_dock != value) { _dock = value; InvalidateMeasure(); } } }

    /// <summary>Size requested by the last <see cref="Measure"/>, margins included.</summary>
    [Browsable(false)]
    public Size DesiredSize { get; private set; }

    /// <summary>Arranged rectangle in host DIPs, margins excluded.</summary>
    [Browsable(false)]
    public Rect Bounds { get; private set; }

    // ---------------------------------------------------------------- appearance / behavior

    /// <summary>Visible and taking layout space when <c>true</c>; collapsed otherwise.</summary>
    [Category("Appearance")]
    public bool IsVisible { get => _isVisible; set { if (_isVisible != value) { _isVisible = value; InvalidateMeasure(); OnVisibilityChanged(); } } }

    /// <summary>Accepts input when <c>true</c>; drawn dimmed otherwise.</summary>
    [Category("Behavior")]
    public bool IsEnabled { get => _isEnabled; set { if (_isEnabled != value) { _isEnabled = value; InvalidateVisual(); } } }

    /// <summary><c>true</c> if this element and all its ancestors are enabled.</summary>
    public bool IsEffectivelyEnabled => _isEnabled && (Parent?.IsEffectivelyEnabled ?? true);

    /// <summary>
    /// Applies <see cref="Opacity"/> through an offscreen layer: the element and its children are drawn opaque, then
    /// blended as one image, so overlapping children do not show through each other. Costs a layer per frame; by
    /// default opacity is a cheaper global alpha (as in VSTGUI). Children of a <see cref="LayeredPanel"/> use layers
    /// automatically (<see cref="LayeredPanel.CompositeLayers"/>).
    /// </summary>
    [Category("Appearance")]
    public bool UseOpacityLayer { get; set; }

    /// <summary>Opacity multiplier (0..1) applied to the element and its children.</summary>
    [Category("Appearance")]
    public float Opacity { get => _opacity; set { value = Math.Clamp(value, 0f, 1f); if (_opacity != value) { _opacity = value; InvalidateVisual(); } } }

    /// <summary>Clips children to <see cref="Bounds"/>.</summary>
    [Category("Layout")]
    public bool ClipToBounds { get; set; }

    /// <summary>Participates in hit testing when <c>true</c>.</summary>
    [Category("Behavior")]
    public bool IsHitTestVisible { get; set; } = true;

    /// <summary>Tooltip text shown after hovering.</summary>
    [Category("Text")]
    public string? ToolTip { get; set; }

    /// <summary>
    /// Tooltip actually shown: <see cref="ToolTip"/> by default. Override to supply a computed or localized text
    /// (for example from <see cref="Strings"/>) when <see cref="ToolTip"/> is not set.
    /// </summary>
    /// <returns>The text to show, or <c>null</c> / empty for no tooltip.</returns>
    protected internal virtual string? GetToolTipText() => ToolTip;

    /// <summary>
    /// Texts of the user interface language in effect (<see cref="UIHost.Strings"/>), or those of
    /// <see cref="UIHost.DefaultLanguage"/> while the element is not attached to a host.
    /// </summary>
    protected UIStrings Strings => Host?.Strings ?? UIStrings.ForLanguage(UIHost.DefaultLanguage);

    /// <summary>Cursor over this element (<see cref="StandardCursor.Default"/> inherits).</summary>
    [Category("Behavior")]
    public StandardCursor Cursor { get; set; }

    /// <summary>Can receive keyboard focus.</summary>
    [Category("Behavior")]
    public bool Focusable { get; set; }

    /// <summary>Context menu shown on right click (unless the click is handled).</summary>
    [Browsable(false)]
    public IList<MenuItem>? ContextMenu { get; set; }

    /// <summary>Accepts files dropped from the shell (raises <see cref="OnFilesDropped"/>).</summary>
    [Category("Behavior")]
    public bool AllowFileDrop { get; set; }

    /// <summary><c>true</c> while the mouse is over this element or one of its descendants.</summary>
    [Browsable(false)]
    public bool IsMouseOver { get; internal set; }

    /// <summary><c>true</c> if this element has keyboard focus.</summary>
    public bool IsFocused => Host is not null && ReferenceEquals(Host.FocusedElement, this);

    /// <summary><c>true</c> if this element holds the mouse capture.</summary>
    public bool IsMouseCaptured => Host is not null && ReferenceEquals(Host.CapturedElement, this);

    // ---------------------------------------------------------------- events

    /// <summary>Raised on mouse button press (after <see cref="OnMouseDown"/>).</summary>
    public event Action<UIElement, MouseEventArgs>? MouseDown;

    /// <summary>Raised on mouse button release.</summary>
    public event Action<UIElement, MouseEventArgs>? MouseUp;

    /// <summary>Raised on mouse move.</summary>
    public event Action<UIElement, MouseEventArgs>? MouseMove;

    /// <summary>Raised on mouse wheel.</summary>
    public event Action<UIElement, MouseEventArgs>? MouseWheel;

    /// <summary>Raised on key press.</summary>
    public event Action<UIElement, KeyEventArgs>? KeyDown;

    // ---------------------------------------------------------------- helpers

    /// <summary>Positions and sizes the element (absolute layout inside a <see cref="Canvas"/>).</summary>
    public void SetBounds(float x, float y, float width, float height)
    {
        _left = x; _top = y; _width = width; _height = height;
        InvalidateMeasure();
    }

    /// <summary>Requests a new layout pass.</summary>
    public void InvalidateMeasure() => Host?.InvalidateLayout();

    /// <summary>Requests a repaint.</summary>
    public void InvalidateVisual() => Host?.Invalidate();

    /// <summary>Captures the mouse: move/up events go to this element until released.</summary>
    public void CaptureMouse() => Host?.CaptureMouse(this);

    /// <summary>Releases the mouse capture held by this element.</summary>
    public void ReleaseMouseCapture() { if (IsMouseCaptured) Host!.ReleaseMouseCapture(); }

    /// <summary>Gives keyboard focus to this element.</summary>
    public bool Focus() => Host?.Focus(this) ?? false;

    /// <summary>Finds a descendant (or this element) by <see cref="Name"/>.</summary>
    public UIElement? FindByName(string name)
    {
        if (Name == name) return this;
        foreach (var child in VisualChildren)
        {
            var found = child.FindByName(name);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Enumerates this element and all descendants (depth first).</summary>
    public IEnumerable<UIElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in VisualChildren)
        {
            foreach (var d in child.DescendantsAndSelf()) yield return d;
        }
    }

    /// <summary><c>true</c> if <paramref name="element"/> is this element or one of its descendants.</summary>
    public bool IsAncestorOf(UIElement? element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, this)) return true;
        }
        return false;
    }

    private void SetLayout(ref float field, float value)
    {
        if (field.Equals(value)) return;
        field = value;
        InvalidateMeasure();
    }

    // ---------------------------------------------------------------- lifecycle

    internal void AttachTo(UIHost host)
    {
        if (ReferenceEquals(Host, host)) return;
        if (Host is not null) DetachFromHost();
        Host = host;
        OnAttached();
        foreach (var child in VisualChildren) child.AttachTo(host);
    }

    internal void DetachFromHost()
    {
        if (Host is null) return;
        foreach (var child in VisualChildren) child.DetachFromHost();
        var host = Host;
        host.OnElementDetaching(this);
        OnDetached();
        Host = null;
        IsMouseOver = false;
    }

    /// <summary>Called when the element joins a host (subscribe to external events here).</summary>
    protected virtual void OnAttached() { }

    /// <summary>Called when the element leaves its host (unsubscribe here).</summary>
    protected virtual void OnDetached() { }

    /// <summary>Called when <see cref="IsVisible"/> changes.</summary>
    protected virtual void OnVisibilityChanged() { }

    // ---------------------------------------------------------------- layout

    /// <summary>Computes <see cref="DesiredSize"/> for the given available size (margins included).</summary>
    public void Measure(Size available)
    {
        if (!_isVisible)
        {
            DesiredSize = Size.Zero;
            return;
        }

        float aw = Math.Max(0f, available.Width - _margin.Horizontal);
        float ah = Math.Max(0f, available.Height - _margin.Vertical);
        if (!float.IsNaN(_width)) aw = _width;
        if (!float.IsNaN(_height)) ah = _height;
        aw = Math.Clamp(aw, _minWidth, Math.Max(_minWidth, _maxWidth));
        ah = Math.Clamp(ah, _minHeight, Math.Max(_minHeight, _maxHeight));

        var desired = MeasureOverride(new Size(aw, ah));
        float w = float.IsNaN(_width) ? desired.Width : _width;
        float h = float.IsNaN(_height) ? desired.Height : _height;
        w = Math.Clamp(w, _minWidth, Math.Max(_minWidth, _maxWidth));
        h = Math.Clamp(h, _minHeight, Math.Max(_minHeight, _maxHeight));
        if (!float.IsFinite(w)) w = 0f;
        if (!float.IsFinite(h)) h = 0f;
        DesiredSize = new Size(w + _margin.Horizontal, h + _margin.Vertical);
    }

    /// <summary>Natural size of the content for the available size (margins excluded).</summary>
    protected virtual Size MeasureOverride(Size available) => Size.Zero;

    /// <summary>Assigns the final layout slot (margins included) and arranges children.</summary>
    public void Arrange(Rect slot)
    {
        if (!_isVisible)
        {
            Bounds = new Rect(slot.X, slot.Y, 0f, 0f);
            return;
        }

        var inner = slot.Deflate(_margin);
        float desiredW = DesiredSize.Width - _margin.Horizontal;
        float desiredH = DesiredSize.Height - _margin.Vertical;

        float w = !float.IsNaN(_width) ? _width : _horizontalAlignment == HorizontalAlignment.Stretch ? inner.Width : MathF.Min(desiredW, inner.Width);
        float h = !float.IsNaN(_height) ? _height : _verticalAlignment == VerticalAlignment.Stretch ? inner.Height : MathF.Min(desiredH, inner.Height);
        w = Math.Clamp(w, _minWidth, Math.Max(_minWidth, _maxWidth));
        h = Math.Clamp(h, _minHeight, Math.Max(_minHeight, _maxHeight));

        float x = _horizontalAlignment switch
        {
            HorizontalAlignment.Center => inner.X + (inner.Width - w) * 0.5f,
            HorizontalAlignment.Right => inner.Right - w,
            _ => inner.X,
        };
        float y = _verticalAlignment switch
        {
            VerticalAlignment.Center => inner.Y + (inner.Height - h) * 0.5f,
            VerticalAlignment.Bottom => inner.Bottom - h,
            _ => inner.Y,
        };

        Bounds = new Rect(x, y, Math.Max(0f, w), Math.Max(0f, h));
        ArrangeOverride(Bounds);
    }

    /// <summary>Arranges children inside the final bounds.</summary>
    protected virtual void ArrangeOverride(Rect bounds) { }

    // ---------------------------------------------------------------- rendering

    internal void RenderTree(DrawingContext dc)
    {
        if (!_isVisible || _opacity <= 0f || Bounds.Width <= 0f && Bounds.Height <= 0f && VisualChildren.Count == 0) return;
        // Skip elements far outside the current clip (shadows/glows may overflow a little).
        if (!Bounds.Inflate(24f).Intersects(dc.ClipBounds)) return;

        float opacity = _opacity * (_isEnabled ? 1f : 0.45f);
        bool layer = opacity < 0.999f;
        if (layer)
        {
            // True composition (offscreen layer) when requested; otherwise the cheaper global alpha.
            if (UseOpacityLayer || Parent is LayeredPanel { CompositeLayers: true }) dc.PushOpacityLayer(opacity);
            else dc.PushOpacity(opacity);
        }

        OnRender(dc);

        var children = VisualChildren;
        if (children.Count > 0)
        {
            if (ClipToBounds) dc.PushClip(Bounds);
            for (int i = 0; i < children.Count; i++) children[i].RenderTree(dc);
            if (ClipToBounds) dc.PopClip();
        }

        OnRenderOverlay(dc);
        if (layer) dc.PopOpacity();
    }

    /// <summary>Draws the element (below its children).</summary>
    protected virtual void OnRender(DrawingContext dc) { }

    /// <summary>Draws above the children (focus rings, overlays...).</summary>
    protected virtual void OnRenderOverlay(DrawingContext dc) { }

    // ---------------------------------------------------------------- hit testing

    /// <summary>Returns <c>true</c> if the point hits this element (children excluded). Default: inside <see cref="Bounds"/>.</summary>
    public virtual bool HitTest(Point point) => Bounds.Contains(point);

    /// <summary>Deepest visible, enabled, hit-testable element under the point.</summary>
    public UIElement? HitTestTree(Point point)
    {
        if (!_isVisible || !IsHitTestVisible) return null;
        if (ClipToBounds && !Bounds.Contains(point)) return null;
        var children = VisualChildren;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var hit = children[i].HitTestTree(point);
            if (hit is not null) return hit;
        }
        return HitTest(point) ? this : null;
    }

    // ---------------------------------------------------------------- input (override to react)

    /// <summary>Mouse button pressed over the element (or captured).</summary>
    protected internal virtual void OnMouseDown(MouseEventArgs e) => MouseDown?.Invoke(this, e);

    /// <summary>Mouse button released.</summary>
    protected internal virtual void OnMouseUp(MouseEventArgs e) => MouseUp?.Invoke(this, e);

    /// <summary>Mouse moved over the element (or captured).</summary>
    protected internal virtual void OnMouseMove(MouseEventArgs e) => MouseMove?.Invoke(this, e);

    /// <summary>Mouse wheel over the element.</summary>
    protected internal virtual void OnMouseWheel(MouseEventArgs e) => MouseWheel?.Invoke(this, e);

    /// <summary>The mouse entered the element.</summary>
    protected internal virtual void OnMouseEnter() => InvalidateVisual();

    /// <summary>The mouse left the element.</summary>
    protected internal virtual void OnMouseLeave() => InvalidateVisual();

    /// <summary>The mouse capture was lost without a button release.</summary>
    protected internal virtual void OnCaptureLost() { }

    /// <summary>Key pressed while focused (bubbles to parents).</summary>
    protected internal virtual void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);

    /// <summary>Key released while focused.</summary>
    protected internal virtual void OnKeyUp(KeyEventArgs e) { }

    /// <summary>Character typed while focused.</summary>
    protected internal virtual void OnTextInput(TextInputEventArgs e) { }

    /// <summary>Received keyboard focus.</summary>
    protected internal virtual void OnGotFocus() => InvalidateVisual();

    /// <summary>Lost keyboard focus.</summary>
    protected internal virtual void OnLostFocus() => InvalidateVisual();

    /// <summary>Files dropped from the shell (requires <see cref="AllowFileDrop"/> on this element or an ancestor).</summary>
    protected internal virtual void OnFilesDropped(FilesDroppedEventArgs e) { }

    /// <summary>Internal drag over this element: set <see cref="DragEventArgs.Accept"/> to accept.</summary>
    protected internal virtual void OnDragOver(DragEventArgs e) { }

    /// <summary>Internal drag left this element.</summary>
    protected internal virtual void OnDragLeave() { }

    /// <summary>Internal drop on this element.</summary>
    protected internal virtual void OnDrop(DragEventArgs e) { }

    /// <inheritdoc />
    public override string ToString() => Name is null ? GetType().Name : $"{GetType().Name} '{Name}'";
}
