<!-- 2026/10/18 15:48:00 UTC - PluginUI -->

# Extending PluginUI

*Version française : [EXTENDING_FR.md](EXTENDING_FR.md)*

This guide shows the extension points of the library, from the simplest to the most complete.

| Need | Extension point |
|---|---|
| Change colors or fonts everywhere | `Theme` (`Palette`, `Typography`, `Metrics`) |
| Change the look of a control type | `Theme.WithStyle<T>(VectorStyle)` |
| Change the look of a single control | `control.Style = …` |
| Skin a control with images | `control.Skin = new FilmStripSkin(…)`, `SvgRotarySkin`, `ImageStateSkin`, `NineSliceSkin` |
| Custom drawing for an existing control | `DelegateSkin` or a class implementing `IControlSkin` |
| New value control | derive from `ValueControl` |
| New control without a value | derive from `Control` |
| Decorative element or visualization | derive from `UIElement` |
| New container | derive from `Panel` (`MeasureOverride` / `ArrangeOverride`) |
| Floating window (menu, picker, dialog) | derive from `Popup` |
| Per-frame animation | `ITickable` + `UIHost.AddTicker` |
| Value source other than a plug-in parameter | implement `IParameter` |

## 1. Themes and styles

```csharp
var theme = Theme.Dark
    .WithAccent(Color.Parse("#FF7A1A"))
    .WithStyle<Knob>(Theme.Dark.DefaultStyle with { Roundness = 1f, TrackThickness = 3f })
    .WithStyle<Button>(Theme.Dark.DefaultStyle with { DrawShadow = false });
host.Theme = theme;
```

`VectorStyle` takes up the idea of iPlug2's `IVStyle`: colors (background, fill, accent, frame, shadow, hover), label and value fonts, whether the label and the value are shown, roundness, thicknesses, shadow, fraction of the area taken by the widget. Each control resolves its style in this order: `control.Style`, then the style registered in the theme for its type (or a parent type), then `Theme.DefaultStyle`.

## 2. Skins

A skin replaces the **drawing of the widget**; the label and the value are still drawn by the control (turn them off with `Style = style.Bare()` if the image already contains them).

```csharp
// Vertical film strip of 128 frames (exported from KnobMan, for example).
var strip = new FilmStrip(ImageSource.FromFile("knob_128.png"), 128, Orientation.Vertical);
knob.Skin = new FilmStripSkin(strip);

// SVG knob: rotor turned from -135° to +135°, fixed background and highlight.
knob.Skin = new SvgRotarySkin(SvgImage.FromFile("rotor.svg"), SvgImage.FromFile("base.svg"), SvgImage.FromFile("glass.svg"));

// Bitmap button with states.
toggle.Skin = new ImageStateSkin(ImageSource.FromFile("off.png"), ImageSource.FromFile("on.png"))
{
    Pressed = ImageSource.FromFile("pressed.png"),
};

// Free drawing for a single control.
meter.Skin = new DelegateSkin((DrawingContext dc, Control c, in SkinState s) =>
{
    dc.FillRect(s.Bounds, Color.Black);
    dc.FillRect(s.Bounds.TakeBottom(s.Bounds.Height * (float)s.Value), Color.Parse("#4CAF50"));
});
```

`SkinState` provides the widget area, the normalized value, the discrete state index, and the hover, pressed, enabled and focus states.

Images are decoded once by WIC (`ImageSource.FromFile`, `FromStream`, `FromResource`), then converted to Direct2D bitmaps on demand and cached per host. `ImageSource.PixelsPerDip` (2 for an @2x image) preserves the logical size.

**SVG**: native rendering goes through `ID2D1DeviceContext5` (Windows 10 1703 and later). If the interface is missing, a placeholder frame is drawn. This is the case under Wine.

## 3. A new value control

`ValueControl` provides the binding to an `IParameter`, the gesture protocol (`BeginGesture` / `UpdateGesture` / `EndGesture`), quantization, the default value (double-click or Ctrl+click), the mouse wheel, the arrow keys, fine adjustment (Shift) and the value bubble.

```csharp
/// <summary>Horizontal bar that fills with the value; drag horizontally to edit.</summary>
public sealed class BarControl : ValueControl
{
    private float _startX;
    private double _startValue;

    protected override Size DefaultSize => new(120f, 20f);

    protected override void RenderVector(DrawingContext dc, Rect widget, VectorStyle style)
    {
        float r = CornerRadiusFor(widget, style);
        dc.FillRoundedRect(widget, r, style.Background);
        dc.FillRoundedRect(widget.TakeLeft(widget.Width * (float)Value), r, style.Accent);
        if (IsMouseOver) dc.FillRoundedRect(widget, r, style.Hover);
    }

    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (TryHandleReset(e) || e.Button != MouseButton.Left) return;
        _startX = e.Position.X;
        _startValue = Value;
        IsPressed = true;
        BeginGesture();          // beginEdit
        e.Handled = true;        // the host captures the mouse for this element
    }

    protected internal override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsEditing) return;
        double scale = e.Shift ? FineFactor : 1.0;
        UpdateGesture(_startValue + (e.Position.X - _startX) / DragSensitivity * scale); // performEdit
    }

    protected internal override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        IsPressed = false;
        EndGesture();            // endEdit
        InvalidateVisual();
    }
}
```

Rules to remember:

- `Bounds` is in absolute host DIPs: no transform to apply for drawing or hit-testing a point.
- Set `e.Handled = true` in `OnMouseDown`: the host then captures the mouse for this control until the button is released.
- An open gesture must always be closed. `ValueControl` takes care of it if the capture is lost (`OnCaptureLost`) or if the control leaves the tree (`OnDetached`).
- Call `InvalidateVisual()` after a change of visual state and `InvalidateMeasure()` after a change of natural size.
- For a control bound to several parameters (XY pad, range), call `IParameter.BeginEdit / PerformEdit / EndEdit` yourself and close the gestures in `OnCaptureLost` and `OnDetached`.
- Keep `ValueText` (the value text) and `GetSkinState` consistent so that skins work.

## 4. A new element or container

```csharp
public sealed class Ruler : UIElement
{
    protected override Size MeasureOverride(Size available) => new(available.Width, 18f);

    protected override void OnRender(DrawingContext dc)
    {
        var p = Theme.Palette;
        for (float x = Bounds.X; x < Bounds.Right; x += 10f)
        {
            float h = (x - Bounds.X) % 50f == 0f ? 10f : 5f;
            dc.DrawLine(new Point(x + 0.5f, Bounds.Bottom - h), new Point(x + 0.5f, Bounds.Bottom), p.TextDim, dc.Hairline);
        }
    }
}

/// <summary>Places children on a circle.</summary>
public sealed class RadialPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        foreach (var c in Children) c.Measure(Size.Infinite);
        return available;
    }

    protected override void ArrangeOverride(Rect bounds)
    {
        float radius = MathF.Min(bounds.Width, bounds.Height) * 0.4f;
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            var center = PathGeometry.PointOnCircle(bounds.Center, radius, 360f * i / Children.Count);
            c.Arrange(Rect.FromCenter(center, c.DesiredSize.Width, c.DesiredSize.Height));
        }
    }
}
```

## 5. Popups

Menus, drop-down lists, color pickers and dialogs are `Popup`s drawn inside the editor window, above the main tree. This avoids any additional native window in the DAW. Useful properties: `Anchor` and `Placement` (Below, Above, Right, Left, AtPoint, Center; automatically flipped when there is not enough room), `IsLightDismiss` (closes on a click outside), `IsModal` (dimmed background, input blocked), `CapturesKeyboard`.

```csharp
var popup = new Popup
{
    Anchor = button.Bounds,
    Placement = PopupPlacement.Below,
    Background = Theme.Palette.SurfaceRaised,
    CornerRadius = 6f,
    Padding = 8f,
    Content = new StackPanel(Orientation.Vertical, 6f).Add(new Knob(), new Knob()),
};
host.ShowPopup(popup);
```

`UIHost.ShowMenu` shows a list of `MenuItem`s (submenus, check marks, shortcuts, separators), drawn by `MenuPopup`, or native with `UIHost.UseNativeMenus = true`. `UIHost.BeginTextEdit` opens an in-place text entry.

## 6. Animation and continuous refresh

```csharp
host.Animator.Animate(0f, 1f, 0.25, v => panel.Opacity = v, Easing.CubicOut);

public sealed class Spinner : UIElement, ITickable
{
    private float _angle;
    protected override void OnAttached() => Host!.AddTicker(this);
    protected override void OnDetached() => Host!.RemoveTicker(this);
    public bool Tick(double dt) { _angle = (_angle + (float)dt * 360f) % 360f; InvalidateVisual(); return true; }
    protected override void OnRender(DrawingContext dc)
        => dc.DrawArc(Bounds.Center, 8f, _angle, 270f, Theme.Palette.Accent, 2f, StrokeStyle.Round);
}
```

The frame timer (about 60 frames per second) runs only while an animation or a ticker is active.

## 7. Data coming from the audio thread

The audio thread must never touch the UI. It publishes values without locks, and the UI reads them every frame:

- `PeakValue`: peak of a block (meters, LEDs, gain reduction);
- `AudioRingBuffer`: samples (oscilloscope, spectrum analyzer);
- `NoteActivity`: held notes (editor keyboard, via `PianoKeyboard.ActivitySource`);
- `UIHost.Post(Action)`: run code on the UI thread from any thread.

In an NPlug plug-in, these objects live in the processor's shared state:

```csharp
public sealed class SynthShared
{
    public NoteActivity Notes { get; } = new();
    public PeakValue[] Output { get; } = [new(), new()];
}

public sealed class SynthProcessor : PluginUIProcessor<SynthModel>
{
    private readonly SynthShared _shared = new();
    protected override object? CreateSharedState() => _shared;   // announced to the controller on connection
    // ProcessMain: _shared.Output[0].PushBlockPeak(left); _shared.Notes.NoteOn(pitch); ...
}

// Editor: the state may arrive after Build (the components connect in any order).
controller.SharedStateChanged += () =>
{
    if (controller.TryGetSharedState<SynthShared>(out var shared)) meter.Sources = shared.Output;
};
```

The opposite direction (editor → processor) goes through VST3 messages: `PluginUIController.SendNote` and `PluginEditorView.ConnectKeyboard` for notes, `PluginUIMessages.TrySend` for your own messages (identifiers outside the `PluginUI.` prefix). Messages arrive on the UI thread; never create them on the audio thread.

## 8. Value sources

`IParameter` decouples the controls from the plug-in format. `PluginUI.NPlug` provides `NPlugParameter` (NPlug VST3 parameters). `LocalParameter` is for UI-only state, standalone applications and tests. Any other source (CLAP parameters, MVVM model state, network…) only has to implement `IParameter` and raise `Changed` on the UI thread.

## 9. Live edit mode

`Ctrl+Shift+E` (or `host.IsLiveEditEnabled = true`) turns on the layout editor: a click selects an element, Alt+click selects its parent, drag to move and use the handles to resize. The arrow keys nudge the element (Shift: ×10, Ctrl: resize), Ctrl+Z undoes. **Ctrl+C copies the C# code** of the modified elements (`SetBounds` for the children of a `Canvas`, `Margin`/`Width`/`Height` otherwise), to paste back into the editor code. Name the elements (`Name`) to get code that can be used directly. Turn the shortcut off in production with `host.LiveEditShortcutEnabled = false`.

## 10. Off-screen rendering

`host.RenderToPng("capture.png", scale: 2f)` produces an image of the UI without a window: documentation, previews, visual regression tests.

## 11. Numbers: always a decimal point

Every number displayed or entered by PluginUI uses the **point** as the decimal separator, whatever the interface language and the regional settings of the machine. In your controls and formatters, use `InvariantNumber.Format(…)` / `InvariantNumber.TryParse(…)` (or `CultureInfo.InvariantCulture`), never `double.Parse` or `ToString()` without a culture.

`InvariantNumber.TryParse` rejects the comma ("1,5" could be read as 1.5 or 15) and thousands separators. In an in-place text entry, rejected text leaves the box open with a red frame (`TextBox.IsInvalid`). To get this behavior in your own text entries, use the `UIHost.BeginTextEdit(bounds, text, Func<string, bool> tryCommit, …)` overload: return `false` to reject the text.

The CA1304 and CA1305 analysis rules are **errors** in every project of the solution except the tests (`CodeAnalysis.globalconfig`). A culture-dependent call therefore does not compile.

## 12. UI texts and language

The texts PluginUI displays itself (dialog buttons, preset menu, placeholder texts, tooltips, live edit help) come from a `UIStrings` table:

```csharp
UIHost.DefaultLanguage = UILanguages.Auto;   // default: Windows display language
host.Language = UILanguages.French;           // or for a single editor
UIStrings.Register("de", UIStrings.English with { Cancel = "Abbrechen", Save = "Speichern" });
```

English and French are built in. An unknown language falls back to English, and `"fr-CA"` falls back to `"fr"`. In *Auto* mode, the language is read through Win32, which also works in a NativeAOT plug-in compiled in invariant globalization mode.

In a control, read the texts through the protected `Strings` property (the host's, or that of `DefaultLanguage` as long as the element is not attached). For a computed tooltip, override `GetToolTipText()`. The texts specific to your plug-in (labels, names of a parameter's values, preset bank names) do not go through this table: they are data of your plug-in.

## 13. Design conventions (PluginUIEditor)

The visual designer and the `.pluginui` format (see [EDITOR.md](EDITOR.md)) describe controls by their public properties. For a custom control to be fully usable there:

- **Public parameterless constructor**: this is what the type registry uses (`Register<T>()`); otherwise, provide a factory (`Register(() => new MyControl(…))`).
- **A category for every public settable property**: `[Category("…")]`, one of `Design`, `Layout`, `Appearance`, `Text`, `Value`, `Data`, `Behavior`. On the class, `[Category("My controls")]` chooses its group in the toolbox.
- **`[Browsable(false)]`** for what is not set at design time: delegates (`Func`, `Action`), run-time objects (audio sources, providers), internal state.
- **Property types**: those the format can describe (numbers, strings, enums, `Color`, `Thickness`, styles, images, skins, simple lists, `IParameter` for bindings; full list in [EDITOR.md](EDITOR.md#the-pluginui-format)). Other properties are set in code (`OnBuilt`).
- **Description**: the XML summary (`/// <summary>`) of the property, read from the generated documentation file (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`, `.xml` file next to the DLL). Write it for a user, not for yourself.
- **Default value**: that of a new instance. The editor writes into the design and the generated code only the properties that differ from it; no `[DefaultValue]` is needed.

To see them in the toolbox, add the DLL in *Tools ▸ Options ▸ Extension assemblies* in PluginUIEditor. To load a design that uses them at run time, register them in the registry passed to `DesignLoader.Build`:

```csharp
var registry = DesignTypeRegistry.CreateDefault();
registry.Register<MyMeter>("My controls");
var result = DesignLoader.Build(design, registry, options);
```

The `DesignConventionTests` test checks these rules for every element of the library. Copy it into your projects for your own controls.
