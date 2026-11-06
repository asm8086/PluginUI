// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Automation;

/// <summary>
/// Peer of a <see cref="UIElement"/>. Name, help text, identifier, bounds, enabled and focus state come from the
/// element; derived peers give the role and the patterns. Used as is for focusable elements without a dedicated peer
/// (role <see cref="AutomationControlType.Custom"/>).
/// </summary>
/// <param name="owner">Element described.</param>
public class ElementAutomationPeer(UIElement owner) : AutomationPeer
{
    /// <summary>Element described.</summary>
    public UIElement Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public override UIHost? Host => Owner.Host;

    /// <inheritdoc />
    public override bool IsAvailable
    {
        get
        {
            if (Owner.Host is null) return false;
            for (var e = Owner; e is not null; e = e.Parent)
            {
                if (!e.IsVisible || e.AutomationHidden) return false;
            }
            return true;
        }
    }

    /// <summary>Texts of the user interface language of the host.</summary>
    protected UIStrings Strings => Owner.Host?.Strings ?? UIStrings.ForLanguage(UIHost.DefaultLanguage);

    /// <inheritdoc />
    public override AutomationControlType ControlType => AutomationControlType.Custom;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="UIElement.AutomationName"/>, else the name of <see cref="UIElement.AutomationLabeledBy"/>, else
    /// <see cref="GetNameCore"/>, else the tooltip.
    /// </remarks>
    public sealed override string Name
    {
        get
        {
            if (!string.IsNullOrEmpty(Owner.AutomationName)) return Owner.AutomationName;
            if (Owner.AutomationLabeledBy is { } label && !ReferenceEquals(label, Owner))
            {
                string? text = label.GetAutomationPeer()?.Name ?? (label as Label)?.Text;
                if (!string.IsNullOrEmpty(text)) return text;
            }
            string? core = GetNameCore();
            if (!string.IsNullOrEmpty(core)) return core;
            return Owner.GetToolTipText() ?? string.Empty;
        }
    }

    /// <summary>Name given by the element itself (its caption, label or parameter name), or null.</summary>
    protected virtual string? GetNameCore() => Owner is Control { Label: { Length: > 0 } label } ? label : null;

    /// <inheritdoc />
    /// <remarks><see cref="UIElement.AutomationHelpText"/>, else the tooltip when it is not already the name.</remarks>
    public override string? HelpText
    {
        get
        {
            if (!string.IsNullOrEmpty(Owner.AutomationHelpText)) return Owner.AutomationHelpText;
            string? tip = Owner.GetToolTipText();
            return string.IsNullOrEmpty(tip) || tip == Name ? null : tip;
        }
    }

    /// <inheritdoc />
    public override string? AutomationId => Owner.Name;

    /// <inheritdoc />
    public override string? ClassName => Owner.GetType().Name;

    /// <inheritdoc />
    public override bool IsEnabled => Owner.IsEffectivelyEnabled;

    /// <inheritdoc />
    public override bool IsKeyboardFocusable => Owner.Focusable && Owner.IsEffectivelyEnabled;

    /// <inheritdoc />
    public override Rect BoundingRectangle => Owner.Bounds;

    /// <inheritdoc />
    public override AutomationPeer? GetParent()
    {
        for (var e = Owner.Parent; e is not null; e = e.Parent)
        {
            if (e.AutomationHidden) continue;
            if (e.GetAutomationPeer() is { } peer) return peer;
        }
        return Owner.Host?.RootAutomationPeer;
    }

    /// <inheritdoc />
    protected override void CollectChildren(List<AutomationPeer> children)
    {
        foreach (var child in Owner.VisualChildren) CollectElementPeers(child, children);
    }

    /// <summary>
    /// Adds the peer of <paramref name="element"/>, or the peers of its children when it is transparent. Hidden
    /// elements (<see cref="UIElement.IsVisible"/>, <see cref="UIElement.AutomationHidden"/>) are skipped with their subtree.
    /// </summary>
    /// <param name="element">Element to add.</param>
    /// <param name="children">List to fill.</param>
    protected internal static void CollectElementPeers(UIElement element, List<AutomationPeer> children)
    {
        if (!element.IsVisible || element.AutomationHidden) return;
        if (element.GetAutomationPeer() is { } peer)
        {
            children.Add(peer);
            return;
        }
        foreach (var child in element.VisualChildren) CollectElementPeers(child, children);
    }

    /// <inheritdoc />
    /// <remarks>Gives the host window the keyboard focus, then the element.</remarks>
    public override void SetFocus()
    {
        if (Owner.Host is not { } host || !Owner.Focusable) return;
        host.RequestKeyboardFocus();
        if (Owner.Focus()) host.ShowFocusRings = true;
    }
}

/// <summary><see cref="ElementAutomationPeer"/> with a typed <see cref="Owner"/>.</summary>
/// <typeparam name="T">Type of the element.</typeparam>
/// <param name="owner">Element described.</param>
public abstract class ElementAutomationPeer<T>(T owner) : ElementAutomationPeer(owner) where T : UIElement
{
    /// <summary>Element described.</summary>
    public new T Owner => (T)base.Owner;
}
