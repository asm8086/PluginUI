<!-- 2026/10/18 15:48:00 UTC - PluginUI -->

# Étendre PluginUI

*English version: [EXTENDING.md](EXTENDING.md)*

Ce guide montre les points d'extension de la bibliothèque, du plus simple au plus complet.

| Besoin | Point d'extension |
|---|---|
| Changer les couleurs ou les polices partout | `Theme` (`Palette`, `Typography`, `Metrics`) |
| Changer l'aspect d'un type de contrôle | `Theme.WithStyle<T>(VectorStyle)` |
| Changer l'aspect d'un seul contrôle | `control.Style = …` |
| Habiller un contrôle avec des images | `control.Skin = new FilmStripSkin(…)`, `SvgRotarySkin`, `ImageStateSkin`, `NineSliceSkin` |
| Dessin sur mesure d'un contrôle existant | `DelegateSkin` ou une classe implémentant `IControlSkin` |
| Nouveau contrôle à valeur | dériver de `ValueControl` |
| Nouveau contrôle sans valeur | dériver de `Control` |
| Élément décoratif ou visualisation | dériver de `UIElement` |
| Nouveau conteneur | dériver de `Panel` (`MeasureOverride` / `ArrangeOverride`) |
| Fenêtre flottante (menu, sélecteur, dialogue) | dériver de `Popup` |
| Animation par image | `ITickable` + `UIHost.AddTicker` |
| Source de valeur autre qu'un paramètre de plugin | implémenter `IParameter` |

## 1. Thèmes et styles

```csharp
var theme = Theme.Dark
    .WithAccent(Color.Parse("#FF7A1A"))
    .WithStyle<Knob>(Theme.Dark.DefaultStyle with { Roundness = 1f, TrackThickness = 3f })
    .WithStyle<Button>(Theme.Dark.DefaultStyle with { DrawShadow = false });
host.Theme = theme;
```

`VectorStyle` reprend l'idée de `IVStyle` d'iPlug2 : couleurs (fond, remplissage, accent, cadre, ombre, survol), polices du libellé et de la valeur, affichage ou non du libellé et de la valeur, arrondi, épaisseurs, ombre, fraction de la zone occupée par le widget. Chaque contrôle résout son style dans cet ordre : `control.Style`, puis le style enregistré pour son type (ou un type parent) dans le thème, puis `Theme.DefaultStyle`.

## 2. Habillages (skins)

Un habillage remplace le **dessin du widget** ; le libellé et la valeur restent dessinés par le contrôle (désactivez-les avec `Style = style.Bare()` si l'image les contient déjà).

```csharp
// Film-strip vertical de 128 images (exporté de KnobMan, par exemple).
var strip = new FilmStrip(ImageSource.FromFile("knob_128.png"), 128, Orientation.Vertical);
knob.Skin = new FilmStripSkin(strip);

// Potentiomètre SVG : rotor tourné de -135° à +135°, fond et reflet fixes.
knob.Skin = new SvgRotarySkin(SvgImage.FromFile("rotor.svg"), SvgImage.FromFile("base.svg"), SvgImage.FromFile("glass.svg"));

// Bouton bitmap à états.
toggle.Skin = new ImageStateSkin(ImageSource.FromFile("off.png"), ImageSource.FromFile("on.png"))
{
    Pressed = ImageSource.FromFile("pressed.png"),
};

// Dessin libre pour un seul contrôle.
meter.Skin = new DelegateSkin((DrawingContext dc, Control c, in SkinState s) =>
{
    dc.FillRect(s.Bounds, Color.Black);
    dc.FillRect(s.Bounds.TakeBottom(s.Bounds.Height * (float)s.Value), Color.Parse("#4CAF50"));
});
```

`SkinState` fournit la zone du widget, la valeur normalisée, l'index d'état discret, et les états survol, enfoncé, activé et focus.

Les images sont décodées une fois par WIC (`ImageSource.FromFile`, `FromStream`, `FromResource`), puis converties en bitmaps Direct2D à la demande et mises en cache par hôte. `ImageSource.PixelsPerDip` (2 pour une image @2x) garde la taille logique.

**SVG** : le rendu natif passe par `ID2D1DeviceContext5` (Windows 10 1703 et plus). Si l'interface est absente, un cadre de remplacement est dessiné. C'est le cas sous Wine.

## 3. Un nouveau contrôle à valeur

`ValueControl` fournit la liaison à un `IParameter`, le protocole de geste (`BeginGesture` / `UpdateGesture` / `EndGesture`), la quantification, la valeur par défaut (double-clic ou Ctrl+clic), la molette, les flèches, le réglage fin (Maj) et la bulle de valeur.

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

Règles à retenir :

- `Bounds` est en DIP absolus de l'hôte : pas de transformation à appliquer pour dessiner ou tester un point.
- Mettre `e.Handled = true` dans `OnMouseDown` : l'hôte capture alors la souris pour ce contrôle jusqu'au relâchement.
- Un geste ouvert doit toujours être fermé. `ValueControl` s'en charge si la capture est perdue (`OnCaptureLost`) ou si le contrôle quitte l'arbre (`OnDetached`).
- Appeler `InvalidateVisual()` après un changement d'état visuel et `InvalidateMeasure()` après un changement de taille naturelle.
- Pour un contrôle lié à plusieurs paramètres (pavé XY, plage), appeler soi-même `IParameter.BeginEdit / PerformEdit / EndEdit` et fermer les gestes dans `OnCaptureLost` et `OnDetached`.
- Garder `ValueText` (texte de la valeur) et `GetSkinState` cohérents pour que les habillages fonctionnent.

## 4. Un nouvel élément ou conteneur

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

Les menus, listes déroulantes, sélecteurs de couleur et dialogues sont des `Popup` dessinés dans la fenêtre de l'éditeur, au-dessus de l'arbre principal. Cela évite toute fenêtre native supplémentaire dans le DAW. Propriétés utiles : `Anchor` et `Placement` (Below, Above, Right, Left, AtPoint, Center ; inversion automatique s'il n'y a pas la place), `IsLightDismiss` (fermeture au clic extérieur), `IsModal` (fond assombri, entrées bloquées), `CapturesKeyboard`.

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

`UIHost.ShowMenu` affiche une liste de `MenuItem` (sous-menus, coches, raccourcis, séparateurs), dessinée par `MenuPopup` ou native avec `UIHost.UseNativeMenus = true`. `UIHost.BeginTextEdit` ouvre une saisie de texte sur place.

## 6. Animation et rafraîchissement continu

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

Le minuteur d'images (environ 60 images par seconde) ne tourne que si une animation ou un ticker est actif.

## 7. Données venant du thread audio

Le thread audio ne doit jamais toucher l'interface. Il publie des valeurs sans verrou, et l'interface les lit à chaque image :

- `PeakValue` : crête d'un bloc (vumètres, LED, réduction de gain) ;
- `AudioRingBuffer` : échantillons (oscilloscope, analyseur de spectre) ;
- `NoteActivity` : notes tenues (clavier de l'éditeur, via `PianoKeyboard.ActivitySource`) ;
- `UIHost.Post(Action)` : exécuter du code sur le thread de l'interface depuis n'importe quel thread.

Dans un plugin NPlug, ces objets vivent dans l'état partagé du processeur :

```csharp
public sealed class SynthShared
{
    public NoteActivity Notes { get; } = new();
    public PeakValue[] Output { get; } = [new(), new()];
}

public sealed class SynthProcessor : PluginUIProcessor<SynthModel>
{
    private readonly SynthShared _shared = new();
    protected override object? CreateSharedState() => _shared;   // annoncé au contrôleur à la connexion
    // ProcessMain : _shared.Output[0].PushBlockPeak(left); _shared.Notes.NoteOn(pitch); ...
}

// Éditeur : l'état peut arriver après Build (les composants se connectent dans n'importe quel ordre).
controller.SharedStateChanged += () =>
{
    if (controller.TryGetSharedState<SynthShared>(out var shared)) meter.Sources = shared.Output;
};
```

Le sens inverse (éditeur → processeur) passe par des messages VST3 : `PluginUIController.SendNote` et `PluginEditorView.ConnectKeyboard` pour les notes, `PluginUIMessages.TrySend` pour vos propres messages (identifiants hors du préfixe `PluginUI.`). Les messages arrivent sur le thread de l'interface ; ne les créez jamais sur le thread audio.

## 8. Sources de valeurs

`IParameter` découple les contrôles du format de plugin. `PluginUI.NPlug` fournit `NPlugParameter` (paramètres VST3 de NPlug). `LocalParameter` sert pour l'état propre à l'interface, les applications autonomes et les tests. Une autre source (paramètres CLAP, état d'un modèle MVVM, réseau…) n'a qu'à implémenter `IParameter` et lever `Changed` sur le thread de l'interface.

## 9. Mode d'édition en direct

`Ctrl+Maj+E` (ou `host.IsLiveEditEnabled = true`) active l'éditeur de mise en page : un clic sélectionne un élément, Alt+clic sélectionne son parent, on déplace par glisser et on redimensionne par les poignées. Les flèches décalent l'élément (Maj : ×10, Ctrl : redimensionnement), Ctrl+Z annule. **Ctrl+C copie le code C#** des éléments modifiés (`SetBounds` pour les enfants d'un `Canvas`, `Margin`/`Width`/`Height` sinon), à recoller dans le code de l'éditeur. Nommez les éléments (`Name`) pour obtenir un code directement utilisable. Désactivez le raccourci en production avec `host.LiveEditShortcutEnabled = false`.

## 10. Rendu hors écran

`host.RenderToPng("capture.png", scale: 2f)` produit une image de l'interface sans fenêtre : documentation, aperçus, tests visuels de non-régression.

## 11. Nombres : toujours le point décimal

Tout nombre affiché ou saisi par PluginUI utilise le **point** comme séparateur décimal, quels que soient la langue de l'interface et les paramètres régionaux de la machine. Dans vos contrôles et vos formateurs, utilisez `InvariantNumber.Format(…)` / `InvariantNumber.TryParse(…)` (ou `CultureInfo.InvariantCulture`), jamais `double.Parse` ni `ToString()` sans culture.

`InvariantNumber.TryParse` refuse la virgule (« 1,5 » pourrait se lire 1,5 ou 15) et les séparateurs de milliers. Dans une saisie sur place, un texte refusé laisse la zone ouverte avec un cadre rouge (`TextBox.IsInvalid`). Pour obtenir ce comportement dans vos propres saisies, utilisez la surcharge `UIHost.BeginTextEdit(zone, texte, Func<string, bool> essai, …)` : renvoyez `false` pour refuser le texte.

Les règles d'analyse CA1304 et CA1305 sont des **erreurs** dans tous les projets de la solution, sauf les tests (`CodeAnalysis.globalconfig`). Un appel dépendant de la culture ne compile donc pas.

## 12. Textes et langue de l'interface

Les textes que PluginUI affiche lui-même (boutons des dialogues, menu des presets, textes indicatifs, infobulles, aide de l'édition en direct) viennent d'une table `UIStrings` :

```csharp
UIHost.DefaultLanguage = UILanguages.Auto;   // défaut : langue d'affichage de Windows
host.Language = UILanguages.French;           // ou pour un seul éditeur
UIStrings.Register("de", UIStrings.English with { Cancel = "Abbrechen", Save = "Speichern" });
```

L'anglais et le français sont intégrés. Une langue inconnue retombe sur l'anglais, et `"fr-CA"` retombe sur `"fr"`. En mode *Auto*, la langue est lue par Win32, ce qui fonctionne aussi dans un plugin NativeAOT compilé en mode de globalisation invariante.

Dans un contrôle, lisez les textes par la propriété protégée `Strings` (celle de l'hôte, ou de `DefaultLanguage` tant que l'élément n'est pas attaché). Pour une infobulle calculée, surchargez `GetToolTipText()`. Les textes propres à votre plugin (libellés, noms des valeurs d'un paramètre, noms de banques de presets) ne passent pas par cette table : ce sont des données de votre plugin.

## 13. Conventions de conception (PluginUIEditor)

L'éditeur visuel et le format `.pluginui` (voir [EDITOR_FR.md](EDITOR_FR.md)) décrivent les contrôles par leurs propriétés publiques. Pour qu'un contrôle personnalisé y soit pleinement utilisable :

- **Constructeur public sans paramètre** : c'est lui que le registre de types utilise (`Register<T>()`), ou fournissez une fabrique (`Register(() => new MonControle(…))`).
- **Une catégorie pour chaque propriété publique modifiable** : `[Category("…")]` parmi `Design`, `Layout`, `Appearance`, `Text`, `Value`, `Data`, `Behavior`. Sur la classe, `[Category("Mes contrôles")]` choisit son groupe dans la boîte à outils.
- **`[Browsable(false)]`** pour ce qui ne se règle pas à la conception : délégués (`Func`, `Action`), objets d'exécution (sources audio, fournisseurs), état interne.
- **Types de propriétés** : ceux que le format sait décrire (nombres, chaînes, énumérations, `Color`, `Thickness`, styles, images, habillages, listes simples, `IParameter` pour les liaisons ; liste complète dans [EDITOR_FR.md](EDITOR_FR.md#le-format-pluginui)). Les autres propriétés se règlent dans le code (`OnBuilt`).
- **Description** : le résumé XML (`/// <summary>`) de la propriété, lu dans le fichier de documentation généré (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`, fichier `.xml` à côté de la DLL). Écrivez-le pour un utilisateur, pas pour vous.
- **Valeur par défaut** : celle d'une instance neuve. L'éditeur n'écrit dans le design et le code généré que les propriétés qui en diffèrent ; aucun `[DefaultValue]` n'est nécessaire.

Pour les voir dans la boîte à outils, ajoutez la DLL dans *Outils ▸ Options ▸ Assemblies d'extension* de PluginUIEditor. Pour charger à l'exécution un design qui les utilise, enregistrez-les dans le registre passé à `DesignLoader.Build` :

```csharp
var registry = DesignTypeRegistry.CreateDefault();
registry.Register<MonVuMetre>("Mes contrôles");
var result = DesignLoader.Build(design, registry, options);
```

Le test `DesignConventionTests` vérifie ces règles pour tous les éléments de la bibliothèque. Copiez-le dans vos projets pour vos propres contrôles.

