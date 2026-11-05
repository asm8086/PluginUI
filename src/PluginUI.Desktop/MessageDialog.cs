// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Desktop;

/// <summary>Answer to <see cref="MessageDialog.AskSaveChanges"/>.</summary>
public enum SaveChangesChoice
{
    /// <summary>Save, then go on.</summary>
    Save,
    /// <summary>Go on without saving.</summary>
    DontSave,
    /// <summary>Stay (Escape or the window was closed).</summary>
    Cancel,
}

/// <summary>
/// Synchronous message boxes for desktop applications: a PluginUI <see cref="Dialog"/> drawn in the window (same look
/// as the rest of the interface), with a nested message loop that returns the answer, like the Win32 MessageBox.
/// </summary>
/// <remarks>Usable from a <see cref="StandaloneWindow.Closing"/> handler. Texts of the buttons follow the host
/// language (<see cref="UIHost.Strings"/>).</remarks>
public static class MessageDialog
{
    /// <summary>Shows a message and waits for the answer.</summary>
    /// <param name="window">Window that shows the dialog (must be shown).</param>
    /// <param name="title">Title.</param>
    /// <param name="message">Message.</param>
    /// <param name="buttons">Buttons.</param>
    /// <returns>The button chosen; <see cref="MessageBoxResult.None"/> if the window is not shown or closed meanwhile.</returns>
    public static MessageBoxResult Show(StandaloneWindow window, string title, string message, MessageBoxButtons buttons = MessageBoxButtons.OK)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowHandle == 0) return MessageBoxResult.None;
        var dialog = Dialog.ShowMessage(window.Host, title, message, buttons);
        return Wait(window, dialog) is MessageBoxResult r ? r : MessageBoxResult.None;
    }

    /// <summary>
    /// Asks whether to save a modified document before closing it, replacing it or quitting:
    /// Save (default, Enter), Don't Save, Cancel (Escape).
    /// </summary>
    /// <param name="window">Window that shows the dialog.</param>
    /// <param name="documentName">Name shown in the question.</param>
    /// <param name="title">Title (application name), or null.</param>
    public static SaveChangesChoice AskSaveChanges(StandaloneWindow window, string documentName, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowHandle == 0) return SaveChangesChoice.Cancel;
        var t = window.Host.Strings;
        var dialog = new Dialog(title, new DialogText(t.FormatSaveChangesQuestion(documentName)));
        dialog.AddButton(t.Cancel, SaveChangesChoice.Cancel, isCancel: true)
              .AddButton(t.DontSave, SaveChangesChoice.DontSave)
              .AddButton(t.Save, SaveChangesChoice.Save, isDefault: true);
        window.Host.ShowPopup(dialog);
        return Wait(window, dialog) is SaveChangesChoice c ? c : SaveChangesChoice.Cancel;
    }

    /// <summary>Waits until a dialog shown in <paramref name="window"/> closes; returns its <see cref="Dialog.Result"/>.</summary>
    public static object? Wait(StandaloneWindow window, Dialog dialog)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(dialog);
        bool closed = !dialog.IsOpen;
        dialog.Closed += _ => closed = true;
        StandaloneWindow.RunMessageLoopUntil(() => closed || window.WindowHandle == 0);
        if (!closed) dialog.Close();
        return dialog.Result;
    }
}
