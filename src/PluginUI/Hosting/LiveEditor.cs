// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;

namespace PluginUI;

public sealed partial class UIHost
{
    private LiveEditor? _liveEditor;

    /// <summary>
    /// Live layout editing (Ctrl+Shift+E): select elements, move and resize them with the mouse or the keyboard,
    /// then copy the resulting C# code (Ctrl+C) back into the editor source.
    /// </summary>
    public bool IsLiveEditEnabled
    {
        get => _liveEditor?.IsEnabled ?? false;
        set
        {
            if (value == IsLiveEditEnabled) return;
            _liveEditor ??= new LiveEditor(this);
            if (value)
            {
                CancelInteraction();
                CloseAllPopups();
                HideToolTip();
            }
            _liveEditor.IsEnabled = value;
            LiveEditChanged?.Invoke(this, value);
            Invalidate();
        }
    }

    /// <summary>Live editor (created on first use).</summary>
    public LiveEditor LiveEditor => _liveEditor ??= new LiveEditor(this);

    /// <summary>Raised when live edit mode is turned on or off.</summary>
    public event Action<UIHost, bool>? LiveEditChanged;
}

/// <summary>
/// In-place layout editor. Mouse: click selects (Alt+click selects the parent), drag moves, the 8 handles resize.
/// Keyboard: arrows nudge (Shift ×10, Ctrl resizes), Tab cycles siblings, Esc deselects, Ctrl+Z undoes,
/// Ctrl+C copies the C# code of every edited element.
/// </summary>
/// <remarks>
/// Children of a <see cref="Canvas"/> are edited through <see cref="UIElement.Left"/>/<see cref="UIElement.Top"/>/
/// <see cref="UIElement.Width"/>/<see cref="UIElement.Height"/>; other elements through <see cref="UIElement.Margin"/>
/// and their explicit size. Give elements a <see cref="UIElement.Name"/> to get usable generated code.
/// </remarks>
public sealed class LiveEditor
{
    private readonly UIHost _host;
    private readonly List<Snapshot> _undo = new();
    private readonly HashSet<UIElement> _edited = new(ReferenceEqualityComparer.Instance);
    private UIElement? _selected;
    private DragMode _mode;
    private Point _dragStart;
    private Snapshot _dragOrigin;
    private bool _dragChanged;

    private enum DragMode { None, Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    private readonly record struct Snapshot(UIElement Element, float Left, float Top, float Width, float Height, Thickness Margin, Rect Bounds)
    {
        public static Snapshot Of(UIElement e) => new(e, e.Left, e.Top, e.Width, e.Height, e.Margin, e.Bounds);

        public void Restore()
        {
            Element.Left = Left;
            Element.Top = Top;
            Element.Width = Width;
            Element.Height = Height;
            Element.Margin = Margin;
        }
    }

    internal LiveEditor(UIHost host) => _host = host;

    /// <summary><c>true</c> while live editing.</summary>
    public bool IsEnabled { get; internal set; }

    /// <summary>Snap grid in DIPs (0 = none). Hold Alt while dragging to bypass it.</summary>
    public float GridSize { get; set; } = 4f;

    /// <summary>Draws the outline of every element.</summary>
    public bool ShowOutlines { get; set; } = true;

    /// <summary>Selected element.</summary>
    public UIElement? Selected
    {
        get => _selected;
        set
        {
            _selected = value is null || ReferenceEquals(value, _host.Root) || !ReferenceEquals(value.Host, _host) ? null : value;
            SelectionChanged?.Invoke(this, _selected);
            _host.Invalidate();
        }
    }

    /// <summary>Elements modified since live edit started (or since <see cref="ClearEdits"/>).</summary>
    public IReadOnlyCollection<UIElement> EditedElements => _edited;

    /// <summary>Raised when the selection changes.</summary>
    public event Action<LiveEditor, UIElement?>? SelectionChanged;

    /// <summary>Raised after an element was moved or resized.</summary>
    public event Action<LiveEditor, UIElement>? ElementEdited;

    /// <summary>
    /// Raised by Ctrl+S. With a handler (for instance <c>PluginUI.Design.LiveDesignBridge</c>, which saves the layout
    /// to a <c>.pluginui</c> file), the help bar shows the shortcut.
    /// </summary>
    public event Action<LiveEditor>? SaveRequested;

    /// <summary>Shows the help bar at the bottom of the host (default <c>true</c>; designers turn it off).</summary>
    public bool ShowHelpBar { get; set; } = true;

    /// <summary>Esc with nothing selected leaves live edit (default <c>true</c>; designers keep the mode on).</summary>
    public bool ExitOnEscape { get; set; } = true;

    /// <summary>Forgets the edit history.</summary>
    public void ClearEdits()
    {
        _edited.Clear();
        _undo.Clear();
    }

    /// <summary>Undoes the last edit.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        snapshot.Restore();
        ElementEdited?.Invoke(this, snapshot.Element);
        return true;
    }

    // =====================================================================================
    // Code generation
    // =====================================================================================

    /// <summary>C# statements reproducing the layout of every edited element.</summary>
    public string GenerateCode()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// PluginUI live edit — " + DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
        int anonymous = 0;
        foreach (var e in _edited)
        {
            if (e.Host is null) continue;
            string id = e.Name ?? $"/* {e.GetType().Name} #{++anonymous} */ element";
            sb.AppendLine(CodeFor(e, id));
        }
        return sb.ToString();
    }

    private static string F(float v) => float.IsNaN(v) ? "float.NaN" : v.ToString("0.##", CultureInfo.InvariantCulture) + "f";

    private static string CodeFor(UIElement e, string id)
    {
        if (e.Parent is Canvas) return $"{id}.SetBounds({F(e.Left)}, {F(e.Top)}, {F(e.Width)}, {F(e.Height)});";
        var m = e.Margin;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{id}.Margin = new Thickness({F(m.Left)}, {F(m.Top)}, {F(m.Right)}, {F(m.Bottom)});");
        if (!float.IsNaN(e.Width)) sb.Append(CultureInfo.InvariantCulture, $" {id}.Width = {F(e.Width)};");
        if (!float.IsNaN(e.Height)) sb.Append(CultureInfo.InvariantCulture, $" {id}.Height = {F(e.Height)};");
        return sb.ToString();
    }

    // =====================================================================================
    // Input
    // =====================================================================================

    internal bool OnMouseDown(Point p, MouseButton button, int clickCount, ModifierKeys modifiers)
    {
        if (button != MouseButton.Left) return true;
        var handle = _selected is null ? DragMode.None : HandleAt(_selected.Bounds, p);
        if (handle == DragMode.None)
        {
            var hit = HitAny(_host.Root, p);
            if ((modifiers & ModifierKeys.Alt) != 0 && _selected is not null && _selected.Bounds.Contains(p)) hit = _selected.Parent;
            Selected = hit;
            handle = _selected is not null && _selected.Bounds.Contains(p) ? DragMode.Move : DragMode.None;
        }
        _mode = handle;
        if (_mode != DragMode.None && _selected is not null)
        {
            _dragStart = p;
            _dragOrigin = Snapshot.Of(_selected);
            _dragChanged = false;
        }
        return true;
    }

    internal bool OnMouseMove(Point p, ModifierKeys modifiers)
    {
        if (_mode == DragMode.None || _selected is null) { _host.Invalidate(); return true; }
        float dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        bool snap = (modifiers & ModifierKeys.Alt) == 0 && GridSize > 0f;
        if (snap)
        {
            dx = MathF.Round(dx / GridSize) * GridSize;
            dy = MathF.Round(dy / GridSize) * GridSize;
        }
        _dragOrigin.Restore();
        Apply(_selected, _mode, dx, dy, _dragOrigin);
        _dragChanged |= dx != 0f || dy != 0f;
        return true;
    }

    internal bool OnMouseUp(Point p, MouseButton button, ModifierKeys modifiers)
    {
        if (_mode != DragMode.None && _selected is not null && _dragChanged) Commit(_dragOrigin);
        _mode = DragMode.None;
        return true;
    }

    internal bool OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_selected is not null) Selected = null;
                else if (ExitOnEscape) _host.IsLiveEditEnabled = false;
                return true;
            case Key.S when e.Control && SaveRequested is not null:
                SaveRequested(this);
                return true;
            case Key.Z when e.Control:
                Undo();
                return true;
            case Key.C when e.Control:
                Clipboard.SetText(GenerateCode());
                return true;
            case Key.Tab:
                SelectSibling(!e.Shift);
                return true;
            case Key.Left or Key.Right or Key.Up or Key.Down when _selected is not null:
            {
                float step = e.Shift ? 10f : 1f;
                float dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0f;
                float dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0f;
                var origin = Snapshot.Of(_selected);
                Apply(_selected, e.Control ? DragMode.BottomRight : DragMode.Move, dx, dy, origin);
                Commit(origin);
                return true;
            }
        }
        return e.Key != Key.E || !(e.Control && e.Shift);
    }

    internal StandardCursor? GetCursor(Point p)
    {
        if (_selected is null) return StandardCursor.Arrow;
        var mode = _mode != DragMode.None ? _mode : HandleAt(_selected.Bounds, p);
        return mode switch
        {
            DragMode.Move => StandardCursor.SizeAll,
            DragMode.Left or DragMode.Right => StandardCursor.SizeWE,
            DragMode.Top or DragMode.Bottom => StandardCursor.SizeNS,
            DragMode.TopLeft or DragMode.BottomRight => StandardCursor.SizeNWSE,
            DragMode.TopRight or DragMode.BottomLeft => StandardCursor.SizeNESW,
            _ => _selected.Bounds.Contains(p) ? StandardCursor.SizeAll : StandardCursor.Arrow,
        };
    }

    internal void OnElementDetaching(UIElement element)
    {
        if (_selected is not null && element.IsAncestorOf(_selected))
        {
            _selected = null;
            _mode = DragMode.None;
        }
        _edited.Remove(element);
        _undo.RemoveAll(s => ReferenceEquals(s.Element, element));
    }

    private void Commit(Snapshot origin)
    {
        _undo.Add(origin);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _edited.Add(origin.Element);
        ElementEdited?.Invoke(this, origin.Element);
    }

    private static void Apply(UIElement e, DragMode mode, float dx, float dy, Snapshot o)
    {
        bool canvas = e.Parent is Canvas;
        float w0 = float.IsNaN(o.Width) ? o.Bounds.Width : o.Width;
        float h0 = float.IsNaN(o.Height) ? o.Bounds.Height : o.Height;

        void Move(float mx, float my)
        {
            if (canvas) { e.Left = o.Left + mx; e.Top = o.Top + my; }
            else
            {
                var m = o.Margin;
                bool stretchH = e.HorizontalAlignment == HorizontalAlignment.Stretch && float.IsNaN(o.Width);
                bool stretchV = e.VerticalAlignment == VerticalAlignment.Stretch && float.IsNaN(o.Height);
                e.Margin = new Thickness(m.Left + mx, m.Top + my, stretchH ? m.Right : m.Right - mx, stretchV ? m.Bottom : m.Bottom - my);
            }
        }

        switch (mode)
        {
            case DragMode.Move:
                Move(dx, dy);
                break;
            default:
            {
                bool left = mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft;
                bool right = mode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight;
                bool top = mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight;
                bool bottom = mode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight;
                if (left || right) e.Width = MathF.Max(4f, w0 + (right ? dx : -dx));
                if (top || bottom) e.Height = MathF.Max(4f, h0 + (bottom ? dy : -dy));
                float mx = left ? w0 - e.Width : 0f;
                float my = top ? h0 - e.Height : 0f;
                if (mx != 0f || my != 0f) Move(mx, my);
                break;
            }
        }
    }

    private void SelectSibling(bool forward)
    {
        if (_selected?.Parent is not { } parent)
        {
            Selected = _host.Root.VisualChildren.Count > 0 ? _host.Root.VisualChildren[0] : null;
            return;
        }
        var siblings = parent.VisualChildren;
        int index = -1;
        for (int i = 0; i < siblings.Count; i++) if (ReferenceEquals(siblings[i], _selected)) index = i;
        if (siblings.Count == 0) return;
        Selected = siblings[(index + (forward ? 1 : -1) + siblings.Count) % siblings.Count];
    }

    private static UIElement? HitAny(UIElement root, Point p)
    {
        // Like HitTestTree, but ignores IsHitTestVisible / IsEnabled so that decorations can be edited too.
        if (!root.IsVisible) return null;
        var children = root.VisualChildren;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var hit = HitAny(children[i], p);
            if (hit is not null) return hit;
        }
        return root.Bounds.Contains(p) ? root : null;
    }

    private const float HandleSize = 7f;

    private static IEnumerable<(DragMode Mode, Rect Box)> Handles(Rect r)
    {
        float s = HandleSize, h = s * 0.5f;
        yield return (DragMode.TopLeft, new Rect(r.X - h, r.Y - h, s, s));
        yield return (DragMode.Top, new Rect(r.Center.X - h, r.Y - h, s, s));
        yield return (DragMode.TopRight, new Rect(r.Right - h, r.Y - h, s, s));
        yield return (DragMode.Right, new Rect(r.Right - h, r.Center.Y - h, s, s));
        yield return (DragMode.BottomRight, new Rect(r.Right - h, r.Bottom - h, s, s));
        yield return (DragMode.Bottom, new Rect(r.Center.X - h, r.Bottom - h, s, s));
        yield return (DragMode.BottomLeft, new Rect(r.X - h, r.Bottom - h, s, s));
        yield return (DragMode.Left, new Rect(r.X - h, r.Center.Y - h, s, s));
    }

    private static DragMode HandleAt(Rect r, Point p)
    {
        foreach (var (mode, box) in Handles(r))
        {
            if (box.Inflate(2f).Contains(p)) return mode;
        }
        return DragMode.None;
    }

    // =====================================================================================
    // Overlay
    // =====================================================================================

    internal void Render(DrawingContext dc)
    {
        if (!IsEnabled) return;
        var theme = _host.Theme;
        var accent = theme.Palette.Accent;
        var size = _host.LogicalSize;

        if (ShowOutlines)
        {
            var outline = theme.Palette.Text.WithAlpha(0.18f);
            foreach (var e in _host.Root.DescendantsAndSelf())
            {
                if (ReferenceEquals(e, _host.Root) || !e.IsVisible || e.Bounds.IsEmpty) continue;
                dc.DrawRect(e.Bounds, outline, 1f / dc.Scale, StrokeStyle.Dotted);
            }
        }

        var hover = HitAny(_host.Root, _host.MousePosition);
        if (hover is not null && !ReferenceEquals(hover, _selected) && !ReferenceEquals(hover, _host.Root))
        {
            dc.DrawRect(hover.Bounds, accent.WithAlpha(0.5f), 1f);
        }

        if (_selected is { } sel)
        {
            var r = sel.Bounds;
            dc.FillRect(r, accent.WithAlpha(0.08f));
            dc.DrawRect(r, accent, 1.5f);
            foreach (var (_, box) in Handles(r))
            {
                dc.FillRect(box, theme.Palette.Background);
                dc.DrawRect(box, accent, 1f);
            }
            string info = string.Create(CultureInfo.InvariantCulture, $"{sel}  {r.X:0.#}, {r.Y:0.#}  {r.Width:0.#} × {r.Height:0.#}");
            var style = theme.Typography.Small with { Ellipsis = false };
            var textSize = dc.MeasureText(info, style);
            var label = new Rect(r.X, r.Y - textSize.Height - 6f, textSize.Width + 10f, textSize.Height + 4f);
            if (label.Y < 0f) label = label.Offset(0f, r.Height + textSize.Height + 8f);
            label = label.Offset(MathF.Min(0f, size.Width - label.Right), 0f);
            dc.FillRoundedRect(label, 3f, accent);
            dc.DrawText(info, label.Deflate(new Thickness(5f, 2f)), style, accent.Contrasting);
        }

        if (!ShowHelpBar) return;
        var bar = new Rect(0f, size.Height - 20f, size.Width, 20f);
        dc.FillRect(bar, theme.Palette.Background.WithAlpha(0.85f));
        dc.DrawText(SaveRequested is null ? _host.Strings.LiveEditHelp : _host.Strings.LiveEditHelpSave,
            bar.Deflate(new Thickness(8f, 0f)), theme.Typography.Small, accent);
    }
}
