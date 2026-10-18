// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace PluginUI;

/// <summary>
/// Preset manager bar: previous / next buttons, the preset name (click to choose from a menu grouped by bank,
/// "*" when modified) and an actions menu (Save, Save As…, Delete). Works with any <see cref="IPresetProvider"/>.
/// </summary>
public class PresetBar : Control
{
    private IPresetProvider? _provider;
    private Zone _hover = Zone.None;
    private Zone _pressed = Zone.None;
    private bool _menuOpen;

    private enum Zone { None, Previous, Next, Name, Actions }

    /// <summary>Creates a preset bar.</summary>
    public PresetBar() { }

    /// <summary>Creates a preset bar over a provider.</summary>
    public PresetBar(IPresetProvider provider) => Provider = provider;

    /// <summary>Preset source.</summary>
    [Browsable(false)]
    public IPresetProvider? Provider
    {
        get => _provider;
        set
        {
            if (ReferenceEquals(_provider, value)) return;
            if (_provider is not null && Host is not null) _provider.Changed -= OnProviderChanged;
            _provider = value;
            if (_provider is not null && Host is not null) _provider.Changed += OnProviderChanged;
            InvalidateVisual();
        }
    }

    /// <summary>Shows the actions (Save, Save As…, Delete) menu button.</summary>
    [Category("Appearance")]
    public bool ShowActions { get; set; } = true;

    /// <summary>Wraps around at the ends of the list with previous / next.</summary>
    [Category("Behavior")]
    public bool Wrap { get; set; } = true;

    /// <summary>Text shown when no preset is loaded (<c>null</c> = localized <see cref="UIStrings.PresetPlaceholder"/>, "Init").</summary>
    [Category("Text")]
    public string? Placeholder { get; set; }

    /// <summary>Extra entries appended to the actions menu.</summary>
    public IList<MenuItem> ExtraActions { get; } = new List<MenuItem>();

    /// <inheritdoc />
    protected override Size DefaultSize => new(240f, Theme.Metrics.ControlHeight + ControlSizing.LabelAndValueHeight(this));

    /// <inheritdoc />
    public override string? ValueText => null;

    private TextStyle NameStyle => Theme.Typography.Normal with { Align = HorizontalAlignment.Center, Weight = FontWeight.Medium };

    private void Zones(Rect widget, out Rect prev, out Rect next, out Rect name, out Rect actions)
    {
        float b = widget.Height;
        prev = new Rect(widget.X, widget.Y, b, b);
        next = new Rect(prev.Right, widget.Y, b, b);
        actions = ShowActions ? new Rect(widget.Right - b, widget.Y, b, b) : new Rect(widget.Right, widget.Y, 0f, b);
        name = Rect.FromEdges(next.Right, widget.Y, actions.X, widget.Bottom);
    }

    private Zone ZoneAt(Point p)
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        if (!widget.Contains(p)) return Zone.None;
        Zones(widget, out var prev, out var next, out var name, out var actions);
        if (prev.Contains(p)) return Zone.Previous;
        if (next.Contains(p)) return Zone.Next;
        if (actions.Contains(p)) return Zone.Actions;
        return name.Contains(p) ? Zone.Name : Zone.None;
    }

    /// <summary>Display name of the current preset.</summary>
    public string CurrentName
    {
        get
        {
            if (_provider is null || _provider.CurrentIndex < 0 || _provider.CurrentIndex >= _provider.Count) return Placeholder ?? Strings.PresetPlaceholder;
            return _provider.GetName(_provider.CurrentIndex);
        }
    }

    /// <inheritdoc />
    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        var p = Theme.Palette;
        bool enabled = IsEffectivelyEnabled && _provider is not null;
        float radius = CornerRadiusFor(widget, style with { Roundness = MathF.Min(style.Roundness, 0.35f) });
        Zones(widget, out var prev, out var next, out var name, out var actions);

        dc.FillRoundedRect(widget, radius, style.Background);
        using (dc.Clip(widget))
        {
            foreach (var (zone, r) in new[] { (Zone.Previous, prev), (Zone.Next, next), (Zone.Actions, actions) })
            {
                if (r.Width <= 0f) continue;
                var fill = _pressed == zone ? style.Pressed : style.Fill;
                dc.FillRoundedRect(r, radius, fill);
                if (zone == Zone.Previous) dc.FillRect(r.TakeRight(radius), fill);
                if (zone == Zone.Next) dc.FillRect(r, fill);
                if (zone == Zone.Actions) dc.FillRect(r.TakeLeft(radius), fill);
                if (_hover == zone && enabled && _pressed != zone) dc.FillRect(r, style.Hover);
            }
            if ((_hover == Zone.Name || _menuOpen) && enabled) dc.FillRect(name, style.Hover);
        }
        float x1 = MathF.Round(prev.Right) + 0.5f, x2 = MathF.Round(next.Right) + 0.5f, x3 = MathF.Round(actions.X) + 0.5f;
        dc.DrawLine(new Point(x1, widget.Y + 1f), new Point(x1, widget.Bottom - 1f), style.Frame);
        dc.DrawLine(new Point(x2, widget.Y + 1f), new Point(x2, widget.Bottom - 1f), style.Frame);
        if (ShowActions) dc.DrawLine(new Point(x3, widget.Y + 1f), new Point(x3, widget.Bottom - 1f), style.Frame);
        dc.DrawRoundedRect(widget.Inflate(-0.5f), radius, _menuOpen ? style.Accent : style.Frame, _menuOpen ? 1.5f : 1f);

        var fg = enabled ? p.Text : p.TextDisabled;
        float g = MathF.Min(9f, widget.Height * 0.38f);
        Glyphs.ChevronSide(dc, prev.Center, g, fg, right: false);
        Glyphs.ChevronSide(dc, next.Center, g, fg, right: true);
        if (ShowActions) Glyphs.Hamburger(dc, actions.Center, g * 1.3f, fg);

        string text = CurrentName + (_provider?.IsModified == true ? " *" : string.Empty);
        var nameArea = name.Deflate(new Thickness(8f, 0f, 18f, 0f));
        dc.DrawText(text, nameArea, NameStyle, enabled ? p.Text : p.TextDisabled);
        if (_provider is not null && _provider.CurrentIndex >= 0 && _provider.GetBank(_provider.CurrentIndex) is { Length: > 0 } bank && name.Width > 160f)
        {
            dc.DrawText(bank, name.Deflate(new Thickness(8f, 0f)), Theme.Typography.Small with { Align = HorizontalAlignment.Left }, p.TextDim);
        }
        Glyphs.Chevron(dc, new Point(name.Right - 11f, name.Center.Y), 7f, enabled ? p.TextDim : p.TextDisabled, up: _menuOpen);
    }

    // ---------------------------------------------------------------- actions

    /// <summary>Loads the previous preset.</summary>
    public void Previous() => Step(-1);

    /// <summary>Loads the next preset.</summary>
    public void Next() => Step(1);

    private void Step(int direction)
    {
        if (_provider is not { Count: > 0 } provider) return;
        int index = provider.CurrentIndex < 0 ? (direction > 0 ? 0 : provider.Count - 1) : provider.CurrentIndex + direction;
        if (Wrap) index = (index % provider.Count + provider.Count) % provider.Count;
        else index = Math.Clamp(index, 0, provider.Count - 1);
        provider.Load(index);
    }

    /// <summary>Opens the preset list menu.</summary>
    public void OpenPresetMenu()
    {
        if (Host is null || _provider is not { Count: > 0 } provider || _menuOpen) return;
        var root = new List<MenuItem>();
        var banks = new Dictionary<string, MenuItem>();
        for (int i = 0; i < provider.Count; i++)
        {
            int index = i;
            var item = new MenuItem(provider.GetName(i), () => provider.Load(index)) { IsChecked = i == provider.CurrentIndex };
            string? bank = provider.GetBank(i);
            if (string.IsNullOrEmpty(bank))
            {
                root.Add(item);
                continue;
            }
            if (!banks.TryGetValue(bank, out var group))
            {
                group = new MenuItem(bank);
                banks[bank] = group;
                root.Add(group);
            }
            group.Items.Add(item);
            if (item.IsChecked) group.IsChecked = true;
        }
        ShowMenuOver(root, Zone.Name);
    }

    /// <summary>Opens the actions menu.</summary>
    public void OpenActionsMenu()
    {
        if (Host is null || _provider is null || _menuOpen) return;
        var provider = _provider;
        int current = provider.CurrentIndex;
        bool hasCurrent = current >= 0 && current < provider.Count;
        var items = new List<MenuItem>
        {
            new(Strings.Save, () => provider.Save(provider.GetName(current))) { IsEnabled = provider.CanSave && hasCurrent && provider.CanDelete(current), ShortcutText = "Ctrl+S" },
            new(Strings.SaveAs, BeginSaveAs) { IsEnabled = provider.CanSave },
            MenuItem.Separator(),
            new(Strings.Delete, () => provider.Delete(current)) { IsEnabled = hasCurrent && provider.CanDelete(current) },
        };
        if (ExtraActions.Count > 0)
        {
            items.Add(MenuItem.Separator());
            foreach (var extra in ExtraActions) items.Add(extra);
        }
        ShowMenuOver(items, Zone.Actions);
    }

    /// <summary>Starts in-place editing of a new preset name; Enter saves.</summary>
    public void BeginSaveAs()
    {
        if (Host is null || _provider is not { CanSave: true } provider) return;
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        Zones(widget, out _, out _, out var name, out _);
        string initial = provider.CurrentIndex >= 0 ? provider.GetName(provider.CurrentIndex) : string.Empty;
        Host.BeginTextEdit(name, initial, text =>
        {
            if (!string.IsNullOrWhiteSpace(text)) provider.Save(text);
        }, style: NameStyle with { Align = HorizontalAlignment.Left });
    }

    private void ShowMenuOver(IList<MenuItem> items, Zone zone)
    {
        LayoutParts(EffectiveStyle, out _, out var widget, out _);
        Zones(widget, out _, out _, out var name, out var actions);
        var anchor = zone == Zone.Actions ? actions : name;
        _menuOpen = true;
        InvalidateVisual();
        Host!.ShowMenu(items, anchor, PopupPlacement.Below, zone == Zone.Name ? name.Width : 0f, () =>
        {
            _menuOpen = false;
            InvalidateVisual();
        });
    }

    // ---------------------------------------------------------------- input

    /// <inheritdoc />
    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled || _provider is null) return;
        var zone = ZoneAt(e.Position);
        if (zone == Zone.None) return;
        e.Handled = true;
        switch (zone)
        {
            case Zone.Name: OpenPresetMenu(); break;
            case Zone.Actions: OpenActionsMenu(); break;
            default: _pressed = zone; InvalidateVisual(); break;
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _pressed;
        _pressed = Zone.None;
        if (pressed != Zone.None && ZoneAt(e.Position) == pressed)
        {
            if (pressed == Zone.Previous) Previous();
            else if (pressed == Zone.Next) Next();
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var zone = ZoneAt(e.Position);
        if (zone != _hover)
        {
            _hover = zone;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected internal override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hover = Zone.None;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost()
    {
        base.OnCaptureLost();
        _pressed = Zone.None;
    }

    /// <inheritdoc />
    protected internal override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        switch (e.Key)
        {
            case Key.Left or Key.Up: Previous(); e.Handled = true; break;
            case Key.Right or Key.Down: Next(); e.Handled = true; break;
            case Key.Enter or Key.Space: OpenPresetMenu(); e.Handled = true; break;
            case Key.S when e.Control:
                if (_provider is { CanSave: true } p && p.CurrentIndex >= 0 && p.CanDelete(p.CurrentIndex)) p.Save(p.GetName(p.CurrentIndex));
                else BeginSaveAs();
                e.Handled = true;
                break;
        }
    }

    // ---------------------------------------------------------------- lifecycle

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        if (_provider is not null) _provider.Changed += OnProviderChanged;
    }

    /// <inheritdoc />
    protected override void OnDetached()
    {
        if (_provider is not null) _provider.Changed -= OnProviderChanged;
        base.OnDetached();
    }

    private void OnProviderChanged(IPresetProvider provider) => InvalidateVisual();
}
