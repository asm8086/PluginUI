// 2026/10/24 14:21:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.Versioning;
using NPlug;

namespace PluginUI.NPlug;

/// <summary>
/// VST3 editor view (NPlug <see cref="IAudioPluginView"/>) hosting a PluginUI element tree in a <see cref="UIHost"/>:
/// window lifecycle, content scale, resizing negotiation with the DAW, keyboard and wheel forwarding,
/// host parameter context menus (with MIDI learn), <c>findParameter</c> and notes played on the editor.
/// </summary>
/// <remarks>
/// Derive and build the UI in <see cref="Build"/>, binding controls with <see cref="Parameters"/>:
/// <code>
/// public sealed class MyEditor(MyController controller) : PluginEditorView(controller, new Size(480, 300))
/// {
///     protected override UIElement Build()
///     {
///         var root = new Canvas();
///         var gain = new Knob { Parameter = Parameters[controller.Model.Gain] };
///         gain.SetBounds(20, 20, 64, 84);
///         root.Children.Add(gain);
///         return root;
///     }
/// }
/// </code>
/// The tree is built lazily (first <see cref="Size"/> or attach) and kept between openings of the editor while the
/// view object lives; the DAW usually creates a new view each time the editor opens.
/// </remarks>
[SupportedOSPlatform("windows10.0.17763")]
public abstract class PluginEditorView : IAudioPluginView, IDisposable
{
    private readonly Size _designSize;
    private readonly SizingMode _sizing;
    private UIHost? _host;
    private IAudioPluginFrame? _frame;
    private float? _hostScale;
    private bool _attached;
    private bool _disposed;
    private readonly List<PianoKeyboard> _keyboards = [];

    /// <summary>Creates the view.</summary>
    /// <param name="sink">Edit bridge (the controller).</param>
    /// <param name="designSize">Logical size of the editor at 100 %.</param>
    /// <param name="sizing">Resizing behavior offered to the DAW.</param>
    protected PluginEditorView(IParameterEditSink sink, Size designSize, SizingMode sizing = SizingMode.Fixed)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sink = sink;
        Parameters = new NPlugParameterSet(sink);
        _designSize = designSize;
        _sizing = sizing;
    }

    /// <summary>Edit bridge (the controller).</summary>
    public IParameterEditSink Sink { get; }

    /// <summary>Parameter wrappers to bind controls: <c>knob.Parameter = Parameters[model.Gain]</c>.</summary>
    public NPlugParameterSet Parameters { get; }

    /// <summary>The UI host (created on first use).</summary>
    public UIHost Host => _host ??= CreateHost();

    /// <summary>Builds the element tree (called once per view).</summary>
    protected abstract UIElement Build();

    /// <summary>Configures the host after creation (theme, minimum/maximum size, zoom...).</summary>
    protected virtual void ConfigureHost(UIHost host) { }

    /// <summary>
    /// Adds plug-in entries to the DAW's parameter context menu (right click on a bound control). Return the
    /// items to append (null or empty: host items only).
    /// </summary>
    protected virtual IReadOnlyList<MenuItem>? GetExtraContextMenuItems(NPlugParameter parameter) => null;

    private UIHost CreateHost()
    {
        var host = new UIHost(Build(), _designSize, _sizing);
        host.ResizeRequestHandler = OnHostResizeRequest;
        host.ParameterContextMenuRequested = OnParameterContextMenu;
        host.MidiLearn = Sink.MidiLearn;
        ConfigureHost(host);
        return host;
    }

    // =====================================================================================
    // IAudioPluginView
    // =====================================================================================

    /// <inheritdoc />
    public bool IsPlatformTypeSupported(AudioPluginViewPlatform platform) => platform == AudioPluginViewPlatform.Hwnd;

    /// <inheritdoc />
    public void Attached(nint parent, AudioPluginViewPlatform type)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (type != AudioPluginViewPlatform.Hwnd || parent == 0) return;
        if (_attached) Removed();
        var host = Host;
        var sizeKnownByHost = host.PixelSize;
        host.FollowWindowDpi = _hostScale is null;
        Parameters.StartListening();
        try
        {
            host.AttachToParent(parent, detectDpi: _hostScale is null);
            _attached = true;
        }
        catch
        {
            Parameters.StopListening();
            throw; // NPlug turns the exception into an error code for the DAW
        }
        // The DPI detected at attach time may change the size the DAW got from Size: ask it to follow.
        if (host.PixelSize != sizeKnownByHost && !RequestFrameResize(host.PixelSize.Width, host.PixelSize.Height))
        {
            host.SetPixelSize(sizeKnownByHost.Width, sizeKnownByHost.Height); // refused: match the DAW frame
        }
        OnAttached();
    }

    /// <inheritdoc />
    public void Removed()
    {
        if (!_attached) return;
        _attached = false;
        OnRemoved();
        // Release the notes still held on the editor's keyboards while their NoteOff still reaches the processor.
        foreach (var keyboard in _keyboards) keyboard.ReleaseAll();
        _host?.Detach();
        Parameters.StopListening();
    }

    /// <summary>
    /// Sends the notes played on <paramref name="keyboard"/> (mouse, computer keyboard) to the processor, which reads
    /// them with <see cref="PluginUIProcessor{TModel}.TryDequeueUINote"/>. Notes still held when the editor closes are
    /// released.
    /// </summary>
    /// <param name="keyboard">An on-screen keyboard of this editor.</param>
    /// <param name="channel">MIDI channel of the notes, 0-based.</param>
    protected void ConnectKeyboard(PianoKeyboard keyboard, int channel = 0)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 15);
        if (_keyboards.Contains(keyboard)) return;
        _keyboards.Add(keyboard);
        _keyboardChannels[keyboard] = channel;
        keyboard.NoteOn += OnKeyboardNoteOn;
        keyboard.NoteOff += OnKeyboardNoteOff;
    }

    private readonly Dictionary<PianoKeyboard, int> _keyboardChannels = new(ReferenceEqualityComparer.Instance);

    private void OnKeyboardNoteOn(PianoKeyboard keyboard, int note, float velocity)
        => Sink.TrySendNote(UINoteEvent.On(note, velocity, _keyboardChannels.GetValueOrDefault(keyboard)));

    private void OnKeyboardNoteOff(PianoKeyboard keyboard, int note)
        => Sink.TrySendNote(UINoteEvent.Off(note, _keyboardChannels.GetValueOrDefault(keyboard)));

    /// <summary>Called after the editor window was created.</summary>
    protected virtual void OnAttached() { }

    /// <summary>Called before the editor window is destroyed.</summary>
    protected virtual void OnRemoved() { }

    /// <summary>Called by NPlug when the DAW releases the view (safety net if <c>removed()</c> was skipped).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Removed();
        _disposed = true;
        foreach (var keyboard in _keyboards)
        {
            keyboard.NoteOn -= OnKeyboardNoteOn;
            keyboard.NoteOff -= OnKeyboardNoteOff;
        }
        _keyboards.Clear();
        if (_host is not null) _host.MidiLearn = null; // the controller's service outlives the view
        _host?.Dispose();
        _frame = null;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public bool OnWheel(float distance) => _attached && Host.DispatchWheel(distance);

    /// <summary>
    /// Sends the keys the DAW forwards through <c>IPlugView::onKeyDown</c> to any focused control (arrows, Delete...).
    /// Off by default: only text input, open menus/dialogs and the live-edit shortcut receive them, so that DAW
    /// shortcuts (transport, delete, navigation) keep working after a click on a knob.
    /// </summary>
    public bool ForwardHostKeysToControls { get; set; }

    private bool WantsHostKeys(Key key, ModifierKeys mods) => WantsHostKeys(Host, key, mods, ForwardHostKeysToControls);

    private static bool WantsHostKeys(UIHost host, Key key, ModifierKeys mods, bool forwardToControls)
    {
        if (forwardToControls) return true;
        if (host.FocusedElement is TextBox) return true;
        foreach (var popup in host.Popups)
        {
            if (popup.CapturesKeyboard || popup.IsModal) return true;
        }
        if (host.IsLiveEditEnabled) return true;
        return host.LiveEditShortcutEnabled && key == Key.E && mods == (ModifierKeys.Control | ModifierKeys.Shift);
    }

    /// <inheritdoc />
    public bool OnKeyDown(ushort key, short keyCode, short modifiers)
    {
        if (!_attached) return false;
        return RouteHostKeyDown(Host, key, keyCode, modifiers, ForwardHostKeysToControls);
    }

    /// <summary>
    /// Routes a key sent by the DAW (<c>IPlugView::onKeyDown</c>) into <paramref name="host"/>: the key event, then the
    /// character as text input.
    /// </summary>
    /// <param name="host">The editor host.</param>
    /// <param name="key">VST3 character (0 when none).</param>
    /// <param name="keyCode">VST3 virtual key code.</param>
    /// <param name="modifiers">VST3 modifier flags.</param>
    /// <param name="forwardToControls">See <see cref="ForwardHostKeysToControls"/>.</param>
    /// <returns><see langword="true"/> when the editor consumed the key (the DAW must not use it).</returns>
    internal static bool RouteHostKeyDown(UIHost host, ushort key, short keyCode, short modifiers, bool forwardToControls)
    {
        var mods = MapModifiers(modifiers);
        var k = MapKey(key, keyCode);
        if (!WantsHostKeys(host, k, mods, forwardToControls)) return false;
        bool handled = k != Key.None && host.DispatchKeyDown(new KeyEventArgs(k, mods));
        // The DAW sends the key and its character in one call (no WM_CHAR follows when the key is consumed): a printable
        // character is also delivered as text input, even when the key itself was claimed (a text box claims every key
        // so that the DAW never sees it, and expects the characters through text input). Keypad keys may come without
        // a character. Ctrl+Alt (AltGr) still types; Ctrl or Alt alone is a shortcut.
        char ch = TextCharacter(key, keyCode);
        bool chord = (mods & (ModifierKeys.Control | ModifierKeys.Alt)) is ModifierKeys.Control or ModifierKeys.Alt;
        if (ch != '\0' && !chord && host.FocusedElement is not null)
        {
            handled |= host.DispatchTextInput(ch.ToString());
        }
        return handled;
    }

    /// <inheritdoc />
    public bool OnKeyUp(ushort key, short keyCode, short modifiers)
    {
        if (!_attached) return false;
        var k = MapKey(key, keyCode);
        if (!WantsHostKeys(k, MapModifiers(modifiers))) return false;
        return k != Key.None && Host.DispatchKeyUp(new KeyEventArgs(k, MapModifiers(modifiers)));
    }

    /// <inheritdoc />
    public ViewRectangle Size
    {
        get
        {
            var (w, h) = Host.PixelSize;
            return new ViewRectangle(0, 0, w, h);
        }
    }

    /// <inheritdoc />
    public void OnSize(ViewRectangle newSize)
    {
        // VST3: only resize here (the DAW decided); never call resizeView from this method.
        Host.SetPixelSize(newSize.Right - newSize.Left, newSize.Bottom - newSize.Top);
    }

    /// <inheritdoc />
    public void OnFocus(bool state)
    {
    }

    /// <inheritdoc />
    public void SetFrame(IAudioPluginFrame? frame) => _frame = frame;

    /// <inheritdoc />
    public bool CanResize() => Host.IsResizable;

    /// <inheritdoc />
    public bool CheckSizeConstraint(ref ViewRectangle rect)
    {
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        Host.ConstrainPixelSize(ref w, ref h);
        rect = new ViewRectangle(rect.Left, rect.Top, rect.Left + w, rect.Top + h);
        return true;
    }

    /// <inheritdoc />
    public bool SetContentScaleFactor(float factor)
    {
        if (!(factor > 0f) || factor > 8f) return false;
        _hostScale = factor;
        Host.FollowWindowDpi = false;
        Host.SetDpiScale(factor);
        return true; // the view scales itself
    }

    /// <inheritdoc />
    public bool TryFindParameter(int xPos, int yPos, out AudioParameterId parameterId)
    {
        var host = Host;
        var element = host.HitAt(new Point(xPos / host.Scale, yPos / host.Scale));
        if (FindParameter(element) is { } parameter)
        {
            parameterId = parameter.Id;
            return true;
        }
        parameterId = default;
        return false;
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static NPlugParameter? FindParameter(UIElement? element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (e is ValueControl { Parameter: NPlugParameter p }) return p;
        }
        return null;
    }

    private bool OnHostResizeRequest(int pixelWidth, int pixelHeight) => RequestFrameResize(pixelWidth, pixelHeight);

    private bool RequestFrameResize(int pixelWidth, int pixelHeight)
    {
        if (_frame is null || !_attached)
        {
            // No frame (yet): resize ourselves; the DAW reads Size when it attaches.
            Host.SetPixelSize(pixelWidth, pixelHeight);
            return true;
        }
        // An accepting DAW calls OnSize from within resizeView.
        bool accepted = _frame.ResizeView(this, new ViewRectangle(0, 0, pixelWidth, pixelHeight));
        if (accepted && Host.PixelSize != (pixelWidth, pixelHeight)) Host.SetPixelSize(pixelWidth, pixelHeight);
        return accepted;
    }

    private bool OnParameterContextMenu(IParameter parameter, Point position)
    {
        if (parameter is not NPlugParameter np || Sink.ControllerHandler is not { IsCreateContextMenuSupported: true } handler) return false;
        // PluginUI's own entries (MIDI learn…) followed by the editor's extra entries.
        var extra = new List<MenuItem>(Host.GetParameterMenuItems(np));
        if (GetExtraContextMenuItems(np) is { Count: > 0 } custom)
        {
            if (extra.Count > 0) extra.Add(MenuItem.Separator());
            extra.AddRange(custom);
        }
        using var menu = handler.CreateContextMenu(this, np.Id);
        if (extra is { Count: > 0 })
        {
            int tag = 1;
            foreach (var item in extra)
            {
                if (item.IsSeparator)
                {
                    menu.AddItem(new AudioContextMenuItem(string.Empty, 0, AudioContextMenuItemFlags.IsSeparator), null);
                    continue;
                }
                var flags = AudioContextMenuItemFlags.None;
                if (!item.IsEnabled) flags |= AudioContextMenuItemFlags.IsDisabled;
                if (item.IsChecked) flags |= AudioContextMenuItemFlags.IsChecked;
                var action = item.Action;
                menu.AddItem(new AudioContextMenuItem(item.Text, tag++, flags), action is null ? null : _ => action());
            }
        }
        float s = Host.Scale;
        menu.Popup((int)MathF.Round(position.X * s), (int)MathF.Round(position.Y * s));
        return true;
    }

    private static ModifierKeys MapModifiers(short modifiers)
    {
        // VST3 KeyModifier: kShiftKey = 1, kAlternateKey = 2, kCommandKey = 4 (Ctrl on Windows), kControlKey = 8.
        var m = ModifierKeys.None;
        if ((modifiers & 1) != 0) m |= ModifierKeys.Shift;
        if ((modifiers & 2) != 0) m |= ModifierKeys.Alt;
        if ((modifiers & (4 | 8)) != 0) m |= ModifierKeys.Control;
        return m;
    }

    /// <summary>Maps a VST3 key event (character + VirtualKeyCodes) to a <see cref="Key"/>.</summary>
    /// <summary>The character typed by a host key: the VST3 character when printable, else the keypad character.</summary>
    /// <param name="character">VST3 character (0 when none).</param>
    /// <param name="keyCode">VST3 virtual key code.</param>
    /// <returns>The character, or '\0'.</returns>
    internal static char TextCharacter(ushort character, short keyCode)
    {
        if (character >= 32 && character != 127) return (char)character;
        return keyCode switch
        {
            >= 24 and <= 33 => (char)('0' + keyCode - 24),   // KEY_NUMPAD0…9
            34 => '*',
            35 => '+',
            37 => '-',
            38 => '.',
            39 => '/',
            _ => '\0',
        };
    }

    internal static Key MapKey(ushort character, short keyCode)
    {
        switch (keyCode)
        {
            case 1: return Key.Back;
            case 2: return Key.Tab;
            case 4: return Key.Enter;   // KEY_RETURN
            case 6: return Key.Escape;
            case 7: return Key.Space;
            case 9: return Key.End;
            case 10: return Key.Home;
            case 11: return Key.Left;
            case 12: return Key.Up;
            case 13: return Key.Right;
            case 14: return Key.Down;
            case 15: return Key.PageUp;
            case 16: return Key.PageDown;
            case 19: return Key.Enter;  // KEY_ENTER (keypad)
            case 21: return Key.Insert;
            case 22: return Key.Delete;
            case >= 24 and <= 33: return (Key)((int)Key.NumPad0 + keyCode - 24);
            case 34: return Key.Multiply;
            case 35: return Key.Add;
            case 37: return Key.Subtract;
            case 38: return Key.Decimal;
            case 39: return Key.Divide;
            case >= 40 and <= 51: return (Key)((int)Key.F1 + keyCode - 40);
            case 54: return Key.Shift;    // KEY_SHIFT
            case 55: return Key.Control;  // KEY_CONTROL
            case 56: return Key.Alt;      // KEY_ALT
        }
        if (character is >= 'a' and <= 'z') return (Key)(character - 'a' + 'A');
        if (character is >= 'A' and <= 'Z' or >= '0' and <= '9') return (Key)character;
        if (character == ' ') return Key.Space;
        return Key.None;
    }
}
