// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.ComponentModel;
using PluginUI.Automation;

namespace PluginUI;

/// <summary>Kind of a <see cref="Toast"/>: its color, its icon and how urgently screen readers speak it.</summary>
public enum ToastKind
{
    /// <summary>Information.</summary>
    Info,
    /// <summary>Something went well (preset saved, export finished).</summary>
    Success,
    /// <summary>Something needs attention (sample rate changed, a file is missing).</summary>
    Warning,
    /// <summary>Something failed (spoken immediately by screen readers).</summary>
    Error,
}

/// <summary>Corner of a <see cref="ToastStack"/> where toasts appear.</summary>
public enum ToastCorner
{
    /// <summary>Bottom right (default).</summary>
    BottomRight,
    /// <summary>Bottom left.</summary>
    BottomLeft,
    /// <summary>Bottom, centred.</summary>
    BottomCenter,
    /// <summary>Top right.</summary>
    TopRight,
    /// <summary>Top left.</summary>
    TopLeft,
    /// <summary>Top, centred.</summary>
    TopCenter,
}

/// <summary>A notification shown by a <see cref="ToastStack"/>.</summary>
public sealed class Toast
{
    internal Toast(string message, ToastKind kind, TimeSpan duration, string? actionText, Action? action)
    {
        Message = message;
        Kind = kind;
        Duration = duration;
        ActionText = actionText;
        Action = action;
    }

    /// <summary>Text.</summary>
    public string Message { get; }

    /// <summary>Kind.</summary>
    public ToastKind Kind { get; }

    /// <summary>Time shown before it fades out (<see cref="TimeSpan.Zero"/> = until closed).</summary>
    public TimeSpan Duration { get; }

    /// <summary>Text of the action button ("Undo", "Show"), or <c>null</c>.</summary>
    public string? ActionText { get; }

    /// <summary>Action run by the button (the toast closes afterwards), or <c>null</c>.</summary>
    public Action? Action { get; }

    /// <summary><c>true</c> once it was closed.</summary>
    public bool IsDismissed { get; internal set; }

    internal double Age;
    internal float Opacity;
    internal bool Closing;
    internal Rect Bounds;
}

/// <summary>
/// Notification area: short messages ("Preset saved", "Sample rate changed to 96 kHz") that appear in a corner, stack,
/// fade out after a few seconds and can carry an action ("Undo"). Place it last in the root panel, over the whole
/// editor: only the toasts take the mouse, everything else goes through to the controls below.
/// </summary>
/// <remarks>
/// <para>The timer pauses while the mouse is over a toast. × closes a toast; its action button runs the action and
/// closes it. Each toast is announced to screen readers (errors immediately).</para>
/// <para><see cref="Show"/> runs on the UI thread; <see cref="Post"/> can be called from any thread (audio thread,
/// file loading task) and shows the toast at the next frame.</para>
/// </remarks>
/// <example>
/// <code>
/// var toasts = new ToastStack();
/// root.Children.Add(toasts);                         // last, over the whole editor
/// toasts.Show("Preset saved", ToastKind.Success);
/// toasts.Show("3 notes deleted", actionText: "Undo", action: undo.Undo);
/// processor.SampleRateChanged += rate => toasts.Post($"Sample rate: {rate / 1000} kHz", ToastKind.Warning);
/// </code>
/// </example>
public class ToastStack : Control
{
    private const float Gap = 8f, Edge = 12f, CloseSize = 16f;

    private readonly List<Toast> _toasts = new();
    private readonly ConcurrentQueue<(string Message, ToastKind Kind)> _posted = new();
    private ToastTicker? _ticker;
    private Toast? _hover;
    private int _hoverPart; // 1 = close, 2 = action

    /// <summary>Creates an empty notification area.</summary>
    public ToastStack()
    {
        Cursor = StandardCursor.Arrow;
    }

    /// <summary>Corner where toasts appear (the newest is nearest the corner).</summary>
    [Category("Layout")]
    public ToastCorner Corner { get; set; } = ToastCorner.BottomRight;

    /// <summary>Width of a toast.</summary>
    [Category("Layout")]
    public float ToastWidth { get; set; } = 300f;

    /// <summary>Largest number of toasts shown at once (the oldest close first).</summary>
    [Category("Behavior")]
    public int MaxVisible { get; set; } = 4;

    /// <summary>Time a toast stays when <see cref="Show"/> is not given one.</summary>
    [Category("Behavior")]
    public double DefaultSeconds { get; set; } = 4.0;

    /// <summary>Toasts shown, oldest first.</summary>
    [Browsable(false)]
    public IReadOnlyList<Toast> Toasts => _toasts;

    /// <summary>Raised when a toast closes (by itself, × or its action).</summary>
    public event Action<ToastStack, Toast>? Dismissed;

    /// <inheritdoc />
    protected override Size DefaultSize => new(400f, 300f);

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new ToastStackAutomationPeer(this);

    /// <summary>Shows a toast (UI thread).</summary>
    /// <param name="message">Text.</param>
    /// <param name="kind">Kind.</param>
    /// <param name="duration">Time shown (null = <see cref="DefaultSeconds"/>, <see cref="TimeSpan.Zero"/> = until closed).</param>
    /// <param name="actionText">Text of an action button, or <c>null</c>.</param>
    /// <param name="action">Action of the button.</param>
    /// <returns>The toast.</returns>
    public Toast Show(string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null, string? actionText = null, Action? action = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var toast = new Toast(message, kind, duration ?? TimeSpan.FromSeconds(DefaultSeconds), action is null ? null : actionText, action);
        _toasts.Add(toast);
        while (_toasts.Count(t => !t.Closing) > Math.Max(1, MaxVisible)) Dismiss(_toasts.First(t => !t.Closing));
        Host?.Announce(message, kind == ToastKind.Error ? AnnouncementPriority.Important : AnnouncementPriority.Polite);
        InvalidateVisual();
        NotifyAutomationStateChanged();
        return toast;
    }

    /// <summary>Shows a toast from any thread at the next frame.</summary>
    /// <param name="message">Text.</param>
    /// <param name="kind">Kind.</param>
    public void Post(string message, ToastKind kind = ToastKind.Info)
    {
        ArgumentNullException.ThrowIfNull(message);
        _posted.Enqueue((message, kind));
    }

    /// <summary>Closes a toast (it fades out).</summary>
    public void Dismiss(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        if (toast.Closing || !_toasts.Contains(toast)) return;
        toast.Closing = true;
        toast.IsDismissed = true;
        Dismissed?.Invoke(this, toast);
        if (Host is null) _toasts.Remove(toast);
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    /// <summary>Closes every toast.</summary>
    public void DismissAll()
    {
        foreach (var t in _toasts.ToArray()) Dismiss(t);
    }

    /// <summary>Runs the action of a toast and closes it.</summary>
    public void InvokeAction(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        if (toast.Closing) return;
        Dismiss(toast);
        toast.Action?.Invoke();
    }

    // ---------------------------------------------------------------- layout

    private TextStyle MessageStyle => Theme.Typography.Normal with { Wrap = true, Ellipsis = false, VerticalAlign = VerticalAlignment.Center };

    private float ActionWidth(Toast t) => t.ActionText is { } text ? TextMeasurer.Measure(text, Theme.Typography.Normal with { Weight = FontWeight.SemiBold }).Width + 18f : 0f;

    private void LayoutToasts()
    {
        var area = Bounds.Deflate(Edge);
        float width = MathF.Min(ToastWidth, area.Width);
        bool top = Corner is ToastCorner.TopLeft or ToastCorner.TopRight or ToastCorner.TopCenter;
        float x = Corner switch
        {
            ToastCorner.BottomLeft or ToastCorner.TopLeft => area.X,
            ToastCorner.BottomCenter or ToastCorner.TopCenter => area.Center.X - width * 0.5f,
            _ => area.Right - width,
        };
        float y = top ? area.Y : area.Bottom;
        // Newest nearest the corner.
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            float textWidth = width - 40f - CloseSize - 10f - ActionWidth(t);
            float height = MathF.Max(40f, TextMeasurer.Measure(t.Message, MessageStyle, MathF.Max(20f, textWidth)).Height + 20f);
            t.Bounds = top ? new Rect(x, y, width, height) : new Rect(x, y - height, width, height);
            y = top ? y + height + Gap : y - height - Gap;
        }
    }

    private static Rect CloseRect(Toast t) => new(t.Bounds.Right - CloseSize - 8f, t.Bounds.Center.Y - CloseSize * 0.5f, CloseSize, CloseSize);

    private Rect ActionRect(Toast t)
    {
        float w = ActionWidth(t);
        return w <= 0f ? Rect.Empty : new Rect(CloseRect(t).X - 6f - w, t.Bounds.Center.Y - 12f, w, 24f);
    }

    /// <summary>Bounds of a toast (host DIPs).</summary>
    internal Rect ToastBounds(Toast toast)
    {
        LayoutToasts();
        return toast.Bounds;
    }

    /// <summary>Bounds of a toast's action button (host DIPs).</summary>
    internal Rect ActionBounds(Toast toast)
    {
        LayoutToasts();
        return ActionRect(toast);
    }

    /// <inheritdoc />
    public override bool HitTest(Point point)
    {
        LayoutToasts();
        foreach (var t in _toasts) if (!t.Closing && t.Bounds.Contains(point)) return true;
        return false;
    }

    private (Toast? Toast, int Part) PartAt(Point p)
    {
        LayoutToasts();
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            if (t.Closing || !t.Bounds.Contains(p)) continue;
            if (CloseRect(t).Inflate(3f).Contains(p)) return (t, 1);
            if (ActionRect(t).Contains(p)) return (t, 2);
            return (t, 0);
        }
        return (null, 0);
    }

    // ---------------------------------------------------------------- rendering

    /// <inheritdoc />
    protected override void LayoutParts(VectorStyle style, out Rect label, out Rect widget, out Rect value)
    {
        label = Rect.Empty;
        value = Rect.Empty;
        widget = Bounds;
    }

    private Color KindColor(ToastKind kind, Palette p) => kind switch
    {
        ToastKind.Success => p.Success,
        ToastKind.Warning => p.Warning,
        ToastKind.Error => p.Danger,
        _ => p.Accent,
    };

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        if (_toasts.Count == 0) return;
        var p = Theme.Palette;
        LayoutToasts();
        var message = MessageStyle;
        var actionStyle = Theme.Typography.Normal with { Weight = FontWeight.SemiBold, Align = HorizontalAlignment.Center, VerticalAlign = VerticalAlignment.Center };
        foreach (var t in _toasts)
        {
            float alpha = Math.Clamp(t.Opacity, 0f, 1f);
            if (alpha <= 0f) continue;
            using (dc.Opacity(alpha))
            {
                var r = t.Bounds;
                var color = KindColor(t.Kind, p);
                dc.FillRoundedRect(r.Offset(0f, 2f), 8f, p.Shadow.WithAlpha(0.35f));
                dc.FillRoundedRect(r, 8f, p.SurfaceRaised);
                dc.DrawRoundedRect(r.Inflate(-0.5f), 8f, p.Border, 1f);
                using (dc.Clip(r)) dc.FillRect(new Rect(r.X, r.Y, 4f, r.Height), color);

                // Icon: a disc with i, ✓, ! or ×.
                var c = new Point(r.X + 22f, r.Center.Y);
                dc.FillCircle(c, 9f, color);
                var ink = color.Contrasting;
                switch (t.Kind)
                {
                    case ToastKind.Success:
                        dc.DrawPolyline([new Point(c.X - 4f, c.Y), new Point(c.X - 1f, c.Y + 3f), new Point(c.X + 4.5f, c.Y - 3.5f)], ink, 1.8f, StrokeStyle.Round);
                        break;
                    case ToastKind.Error:
                        dc.DrawLine(new Point(c.X - 3.5f, c.Y - 3.5f), new Point(c.X + 3.5f, c.Y + 3.5f), ink, 1.8f, StrokeStyle.Round);
                        dc.DrawLine(new Point(c.X - 3.5f, c.Y + 3.5f), new Point(c.X + 3.5f, c.Y - 3.5f), ink, 1.8f, StrokeStyle.Round);
                        break;
                    case ToastKind.Warning:
                        dc.DrawLine(new Point(c.X, c.Y - 4.5f), new Point(c.X, c.Y + 1f), ink, 1.8f, StrokeStyle.Round);
                        dc.FillCircle(new Point(c.X, c.Y + 4f), 1.1f, ink);
                        break;
                    default:
                        dc.DrawLine(new Point(c.X, c.Y - 1f), new Point(c.X, c.Y + 4.5f), ink, 1.8f, StrokeStyle.Round);
                        dc.FillCircle(new Point(c.X, c.Y - 4f), 1.1f, ink);
                        break;
                }

                var close = CloseRect(t);
                var action = ActionRect(t);
                float textRight = (action.IsEmpty ? close.X : action.X) - 6f;
                dc.DrawText(t.Message, Rect.FromEdges(r.X + 40f, r.Y + 4f, textRight, r.Bottom - 4f), message, p.Text);
                if (!action.IsEmpty)
                {
                    bool hot = ReferenceEquals(_hover, t) && _hoverPart == 2;
                    if (hot) dc.FillRoundedRect(action, 4f, color.WithAlpha(0.18f));
                    dc.DrawText(t.ActionText!, action, actionStyle, color);
                }
                var xColor = ReferenceEquals(_hover, t) && _hoverPart == 1 ? p.Text : p.TextDim;
                var cc = close.Center;
                dc.DrawLine(new Point(cc.X - 4f, cc.Y - 4f), new Point(cc.X + 4f, cc.Y + 4f), xColor, 1.4f);
                dc.DrawLine(new Point(cc.X - 4f, cc.Y + 4f), new Point(cc.X + 4f, cc.Y - 4f), xColor, 1.4f);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnRenderOverlay(DrawingContext dc)
    {
        // No focus ring: the toasts are not keyboard targets (screen readers reach them through the peer).
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (toast, part) = PartAt(e.Position);
        Cursor = part is 1 or 2 ? StandardCursor.Hand : StandardCursor.Arrow;
        if (!ReferenceEquals(toast, _hover) || part != _hoverPart)
        {
            _hover = toast;
            _hoverPart = part;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = null;
        _hoverPart = 0;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left) return;
        var (toast, part) = PartAt(e.Position);
        if (toast is null) return;
        e.Handled = true;
        if (part == 1) Dismiss(toast);
        else if (part == 2) InvokeAction(toast);
    }

    // ---------------------------------------------------------------- animation

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Host!.AddTicker(_ticker ??= new ToastTicker(this));
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_ticker is not null) Host?.RemoveTicker(_ticker);
        base.OnDetached();
    }

    /// <summary>Advances the timers and the fades (one frame).</summary>
    internal void Advance(double seconds)
    {
        while (_posted.TryDequeue(out var posted)) Show(posted.Message, posted.Kind);
        if (_toasts.Count == 0) return;
        bool dirty = false;
        foreach (var t in _toasts.ToArray())
        {
            if (t.Closing)
            {
                t.Opacity -= (float)(seconds / 0.2);
                if (t.Opacity <= 0f) _toasts.Remove(t);
                dirty = true;
                continue;
            }
            if (t.Opacity < 1f)
            {
                t.Opacity = MathF.Min(1f, t.Opacity + (float)(seconds / 0.15));
                dirty = true;
            }
            // The timer pauses while the mouse is over the toast.
            if (!ReferenceEquals(_hover, t)) t.Age += seconds;
            if (t.Duration > TimeSpan.Zero && t.Age >= t.Duration.TotalSeconds) Dismiss(t);
        }
        if (dirty) InvalidateVisual();
    }

    private sealed class ToastTicker(ToastStack owner) : ITickable
    {
        public bool Tick(double elapsedSeconds)
        {
            if (owner.Host is null) return false;
            owner.Advance(elapsedSeconds);
            return true;
        }
    }
}

/// <summary>Peer of a <see cref="ToastStack"/>: a group of notifications, each a text with its action as a button.</summary>
/// <param name="owner">The notification area.</param>
public class ToastStackAutomationPeer(ToastStack owner) : ElementAutomationPeer<ToastStack>(owner)
{
    private readonly Dictionary<Toast, ToastAutomationPeer> _items = new();

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Group;

    /// <inheritdoc />
    public override string? LocalizedControlType => Strings.AutomationNotifications;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Owner.Toasts.Count == 0 ? Rect.Empty : Owner.Bounds;

    /// <summary>Peer of a toast.</summary>
    public ToastAutomationPeer GetToast(Toast toast)
    {
        if (!_items.TryGetValue(toast, out var peer)) _items[toast] = peer = new ToastAutomationPeer(this, toast);
        return peer;
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        foreach (var t in Owner.Toasts) if (!t.IsDismissed) children.Add(GetToast(t));
        foreach (var gone in _items.Keys.Where(t => !Owner.Toasts.Contains(t)).ToArray()) _items.Remove(gone);
    }

    /// <inheritdoc />
    protected internal override void OnOwnerStateChanged() => Host?.NotifyAutomationStructureChanged();
}

/// <summary>Peer of a <see cref="Toast"/>: a button when it has an action (invoking it runs the action), text otherwise.</summary>
public sealed class ToastAutomationPeer : ItemAutomationPeer, IInvokePattern
{
    private readonly ToastStackAutomationPeer _stack;

    internal ToastAutomationPeer(ToastStackAutomationPeer stack, Toast toast) : base(stack)
    {
        _stack = stack;
        Toast = toast;
    }

    /// <summary>The toast.</summary>
    public Toast Toast { get; }

    /// <inheritdoc />
    protected override bool Exists => !Toast.IsDismissed && _stack.Owner.Toasts.Contains(Toast);

    /// <inheritdoc />
    public override AutomationControlType ControlType => Toast.Action is null ? AutomationControlType.Text : AutomationControlType.Button;

    /// <inheritdoc />
    public override string Name => Toast.ActionText is { } action ? Toast.Message + ", " + action : Toast.Message;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Exists ? _stack.Owner.ToastBounds(Toast) : Rect.Empty;

    /// <inheritdoc />
    public override object? GetPattern(AutomationPattern pattern) => pattern == AutomationPattern.Invoke && Toast.Action is null ? null : base.GetPattern(pattern);

    /// <inheritdoc />
    public void Invoke()
    {
        ThrowIfNotEnabled();
        if (Toast.Action is null) throw new InvalidOperationException("The notification has no action.");
        _stack.Owner.InvokeAction(Toast);
    }
}
