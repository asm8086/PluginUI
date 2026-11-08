<!-- 2026/11/08 12:00:00 UTC - PluginUI -->

# PluginUI — interface Win32 + Direct2D pour plugins audio en C#

*English version: [README.md](README.md)*

PluginUI est une boîte à outils d'interface utilisateur pour les éditeurs de plugins VST3 écrits en C# / .NET 10. Elle dessine tout en **Direct2D / DirectWrite** dans **une seule fenêtre Win32 enfant** de celle du DAW, n'a aucune dépendance à WinForms, WPF ou un runtime d'interface, et reste **compatible NativeAOT** (le plugin compilé est une DLL native sans .NET installé).

Elle réunit le catalogue d'éléments de **VSTGUI** et d'**iPlug2**, complété d'éléments pour instruments et pour l'analyse (égaliseur, séparateur multibande, affichage de réponse en fréquence, sonie EBU R128, spectrogramme, forme d'onde, accordeur, table d'ondes, MSEG, séquenceur, piano roll, pads de batterie, matrice de modulation avec sources par glisser-déposer, éditeur de graphes modulaires, navigateur de presets, apprentissage MIDI, A/B) : près d'une centaine d'éléments, chacun disponible en dessin vectoriel stylable, en bitmap / film-strip et en SVG. Elle offre aussi les points d'extension pour en créer d'autres.

![Galerie — rotatifs et linéaires](docs/images/fr/1-rotary-linear.png)

| | |
|---|---|
| ![Boutons et menus](docs/images/fr/2-buttons-menus.png) | ![Mesures et MIDI](docs/images/fr/4-meters-midi.png) |
| ![Conteneurs et listes](docs/images/fr/5-containers-lists.png) | ![Habillages et styles](docs/images/fr/6-skins-styles.png) |
| ![Thème clair](docs/images/fr/7-light-theme.png) | ![Mode d'édition en direct](docs/images/fr/9-live-edit.png) |
| ![Instrument](docs/images/fr/10-instrument.png) | ![Mixage et analyse](docs/images/fr/11-analysis.png) |
| ![Réponse en fréquence](docs/images/fr/15-response.png) | ![Séparateur multibande](docs/images/fr/16-multiband.png) |
| ![Éditeur de motif et pads de batterie](docs/images/fr/17-sequencer.png) | ![Navigateur de presets et arborescence](docs/images/fr/18-browser.png) |
| ![Matrices de modulation et de routage](docs/images/fr/19-matrix.png) | ![Règle temporelle, boîtes numériques et notifications](docs/images/fr/20-tools.png) |
| ![Table d'ondes et accordeurs](docs/images/fr/21-synth.png) | ![Éditeur de modules](docs/images/fr/22-modular.png) |

*Captures produites par `PluginUI.Gallery --snapshot` (rendu hors écran). Les cadres barrés des pages « Habillages » et « Conteneurs » sont les SVG, que le moteur de capture (Wine) ne sait pas dessiner ; Windows les affiche nativement.*

## Contenu

```
PluginUI/
├── PluginUI.slnx
├── Directory.Build.props/.targets  Réglages communs : emplacement de NPlug, métadonnées des paquets, jeu d'instructions AVX2 pour NativeAOT x64
├── CodeAnalysis.globalconfig       Règles d'analyse imposées (CA1304 / CA1305 : aucune dépendance aux paramètres régionaux)
├── README.md, README_FR.md         Ce fichier (anglais, français)
├── LICENSE                         Licence MIT
├── CHANGELOG.md                    Historique des versions
├── CONTRIBUTING.md, CONTRIBUTING_FR.md   Comment contribuer (anglais, français)
├── build/                          Script de publication, chargeur VST3 (vst3shim) et outil de validation
├── .github/workflows/              Intégration continue (Linux + Windows), site de documentation, versions
├── docs/                           Chaque guide en anglais (X.md) et en français (X_FR.md)
│   ├── CONTROLS.md                 Catalogue des éléments (avec équivalents VSTGUI / iPlug2)
│   ├── EXTENDING.md                Étendre la bibliothèque : thèmes, habillages, contrôles, conteneurs, popups
│   ├── EDITOR.md                   Guide du concepteur visuel PluginUIEditor et du format .pluginui
│   ├── DESKTOP.md                  Applications de bureau avec PluginUI.Desktop (barre de menus, commandes, documents)
│   ├── ACCESSIBILITY.md            Lecteurs d'écran (UI Automation), clavier, annonces, contraste élevé
│   ├── PLUGIN_HOST.md              Hébergement de plug-ins VST3 : PluginUI.Host et l'outil en ligne de commande pluginhost
│   └── images/                     Captures (anglais ; françaises dans images/fr/)
├── docfx/                          Site de documentation (DocFX) : articles + référence de l'API
├── src/
│   ├── PluginUI/                   La bibliothèque (indépendante de tout format de plugin)
│   │   ├── Primitives/             Point, Size, Rect, Thickness, Color (HSV, hexadécimal), InvariantNumber
│   │   ├── Rendering/              Renderer Direct2D, DrawingContext, Paint (dégradés), PathGeometry,
│   │   │                           TextStyle, ImageSource / FilmStrip / NineSlice (WIC), SvgImage
│   │   ├── Core/                   UIElement (mise en page, rendu, entrées), Panel, Control, ValueControl
│   │   ├── Layout/                 Canvas, StackPanel, Grid, WrapPanel, DockPanel, UniformGrid, Border
│   │   ├── Automation/             Accessibilité : pairs d'automation, motifs, passerelle Windows UI Automation
│   │   ├── Hosting/                UIHost (fenêtre, entrées, DPI, popups, minuteurs), Popup, MenuPopup,
│   │   │                           LiveEditor, StandaloneWindow
│   │   ├── Parameters/             IParameter, LocalParameter (lin., log., puissance, listes), MappedParameter, BandCenterParameter
│   │   ├── Localization/           UIStrings (textes de l'interface en anglais / français), UILanguages
│   │   ├── Theming/                Theme, Palette, Typography, Metrics, VectorStyle
│   │   ├── Skins/                  Film-strip, images par état, SVG, neuf-tranches, délégué
│   │   ├── Animation/              Animator, Easing, ITickable
│   │   ├── Dsp/                    Fft, AudioRingBuffer, PeakValue, LevelBallistics, LoudnessAnalyzer (EBU R128),
│   │   │                           BiquadDesign, StereoAnalysis, CompressorMath, WavReader, WaveformPeaks, VectorOps (SIMD)
│   │   ├── Services/               Presse-papiers, menus natifs, dialogues de fichiers, shell, apprentissage MIDI
│   │   └── Controls/               Rotary, Linear, Buttons, Menus, Text, Meters, Midi, Lists, Pickers, Misc
│   │                               (dont égaliseur, compresseur, sonie, goniomètre, spectrogramme, forme d'onde,
│   │                               MSEG, séquenceur, molettes, A/B, police bitmap, calques)
│   ├── PluginUI.NPlug/             Adaptateur NPlug (VST3) : PluginEditorView, NPlugParameter, PluginUIController,
│   │                               PluginUIProcessor (notes de l'éditeur, état partagé), NPlugMidiLearn
│   ├── PluginUI.Design/            Designs .pluginui : document, JSON, registre de types compatible AOT, chargeur,
│   │                               capture, génération de code C#, thèmes, lien de design en direct, création de projets
│   ├── PluginUI.Desktop/           Applications de bureau : DesktopApplication, DocumentSession, UndoStack,
│   │                               AppSettings (JSON AOT), RecentFiles, MessageDialog
│   ├── PluginUI.Host/              Hôte de plug-ins : modules et instances VST3, liste et recherche des plug-ins, rendu
│   │                               hors temps réel, mesure de réponse, rack en direct et sources de signal, WAV et MIDI
│   ├── PluginUI.Host.Vst2/         Hébergement VST 2.4 : modules et shells, plug-ins, éditeurs, presets .fxp / .fxb
│   └── PluginUI.Host.Windows/      Périphériques Windows de l'hôte : ASIO, sortie et entrée WASAPI, entrée et sortie MIDI
├── tools/PluginUIEditor/           Concepteur visuel WinForms (boîte à outils, propriétés, aperçu, code, nouveaux projets)
├── tools/PluginHost.Cli/           pluginhost : hôte de plug-ins en ligne de commande (scan, liste, traitement, rendu, mesure, presets)
├── tools/PluginHost.Desktop/       Plug-in Host : hôte graphique construit avec PluginUI (liste de plug-ins triable, éditeurs, presets)
├── samples/PluginUI.Gallery/       Application de démonstration (fenêtre autonome + mode capture PNG)
├── samples/PluginUI.DemoSynth/     Instrument VST3 d'exemple : synthé polyphonique avec entrée sidechain
├── samples/PluginUI.DesktopDemo/   Biquad Designer : application de bureau (IL ou NativeAOT) construite avec PluginUI.Desktop
├── samples/Designs/                Designs d'exemple (.pluginui), dont la disposition de DemoSynth
├── tests/PluginUI.Tests/           237 tests NUnit (mise en page, entrées, gestes, popups, contrôles, DSP et SIMD, cultures et langues, conventions de conception, barre de menus, commandes, outils de bureau, accessibilité, tables, fenêtres natives, plein écran)
├── tests/PluginUI.NPlug.Tests/     29 tests NUnit (pont éditeur ↔ processeur, touches de l'hôte, MIDI learn, DSP du synthé, validateur Steinberg)
├── tests/PluginUI.Design.Tests/    26 tests NUnit (format .pluginui, chargeur, code généré compilé par Roslyn, lien en direct, projets, commandes de l'éditeur, extensions)
├── tests/PluginUI.Host.Tests/      81 tests NUnit (hébergement VST 2 d'un plug-in de test en processus, .fxp / .fxb, ASIO par un pilote de test en processus, formats d'échantillons, hébergement VST3 de DemoSynth et de plug-ins de test, paramètres, correspondance MIDI, presets, rendu, mesure, WAV, fichiers MIDI, MIDI émis par les plug-ins, liste des plug-ins, recherche hors processus, rack en direct et sources de signal)
├── tests/PluginUI.Host.TestGain/   Effet VST3 de test avec latence (publiable aussi en .vst3 NativeAOT pour les essais manuels)
└── tests/PluginUI.Host.TestVst2/   Le plug-in VST 2 de test en module natif (.dll / .so NativeAOT pour les essais manuels)
```

Environ 34 000 lignes de C# pour les bibliothèques. Tous les membres publics ont leur documentation XML.

## Prérequis

- Windows 10 1809 (build 17763) ou ultérieur, x64 ou ARM64. SVG natif à partir de 1703.
- Publication NativeAOT par défaut : processeur avec AVX2 et FMA (voir « Performances et SIMD »).
- Visual Studio 2026, SDK .NET **10.0.403 ou ultérieur** (imposé par `global.json` : les SDK antérieurs apportent un compilateur NativeAOT et des packs du runtime touchés par l'avis GHSA-p3jp-9v83-76w9).
- `TerraFX.Interop.Windows` 10.0.26100.6 (NuGet), pour des liaisons Win32 / Direct2D / DirectWrite / WIC sans marshalling, compatibles NativeAOT.
- `PluginUI.NPlug` uniquement : [NPlug](https://github.com/xoofx/NPlug) 0.7.0 ou ultérieur (paquet NuGet `NPlug`, apporté par `PluginUI.NPlug`).
- Paquets NuGet : `PluginUI`, `PluginUI.Design`, `PluginUI.NPlug` et `PluginUI.Desktop`.

## Téléchargements

Chaque [version](https://github.com/asm8086/PluginUI/releases) fournit :

| Fichier | Contenu |
|---|---|
| `PluginUI.DemoSynth-<version>-vst3-universal.zip` | L'instrument de démonstration en un seul bundle VST3 pour x64 et ARM64. Sur x64, un petit chargeur choisit la version x86-64-v3 (AVX2, FMA : Intel Haswell et ultérieurs, AMD Zen et ultérieurs) ou la version x86-64-v2 (SSE4.2 et POPCNT : Intel Nehalem et ultérieurs, AMD Bulldozer et ultérieurs) ; sur un processeur plus ancien, le plugin refuse de se charger au lieu d'arrêter l'hôte |
| `…-vst3-x64-v3.zip`, `…-vst3-x64-v2.zip`, `…-vst3-arm64.zip` | Une seule version par bundle, sans chargeur |
| `PluginUIEditor-<version>-win-x64.zip`, `…-win-arm64.zip` | Le concepteur visuel (nécessite le .NET 10 Desktop Runtime) |
| `PluginUI.Gallery-<version>-win-x64.zip`, `…-win-arm64.zip` | La galerie, compilée en NativeAOT (aucun runtime nécessaire) |
| `BiquadDesigner-<version>-win-x64.zip`, `…-win-arm64.zip` | L'application de bureau de démonstration, compilée en NativeAOT (aucun runtime nécessaire) |
| `PluginHost-<version>-win-x64.zip`, `…-win-arm64.zip` | Plug-in Host, l'hôte graphique VST3 / VST 2 (ASIO, WASAPI, MIDI), NativeAOT (à partir de la 0.3.0) |
| `pluginhost-cli-<version>-win-x64.zip`, `…-win-arm64.zip` | `pluginhost`, l'hôte en ligne de commande (recherche, traitement, rendu, mesure), NativeAOT (à partir de la 0.3.0) |
| `*.nupkg`, `SHA256SUMS.txt` | Paquets NuGet (aussi poussés sur nuget.org une fois la publication activée) et sommes de contrôle |

`build/Build-Release.ps1` produit les mêmes fichiers en local ; voir [CONTRIBUTING_FR.md](CONTRIBUTING_FR.md#publication-des-versions).

## Démarrage rapide

### Dans un plugin NPlug (VST3)

Un plugin est une bibliothèque de classes avec un identifiant d'exécution Windows ; le paquet `PluginUI.NPlug` apporte
PluginUI et NPlug (avec les fichiers de compilation de NPlug : proxy de débogage, export de la fabrique, `.vst3`
NativeAOT) :

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <OutputType>Library</OutputType>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier> <!-- ou win-arm64 -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> <!-- versions Debug chargées par le proxy NPlug -->
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="PluginUI.NPlug" Version="0.4.0" />
</ItemGroup>
```

```csharp
// Contrôleur : le pont beginEdit/performEdit/endEdit est fourni.
public sealed class MyController : PluginUIController<MyModel>
{
    [SupportedOSPlatform("windows10.0.17763")]
    protected override PluginEditorView CreateEditor() => new MyEditor(this);
}

// Éditeur : uniquement la mise en page.
[SupportedOSPlatform("windows10.0.17763")]
public sealed class MyEditor(MyController c) : PluginEditorView(c, new Size(480, 300), SizingMode.ScaleToFit)
{
    protected override UIElement Build()
    {
        var model = ((MyController)Sink).Model;
        var root = new Canvas();
        var gain = new Knob(Parameters[model.Gain]) { Label = "GAIN", ShowValueBubble = true };
        gain.SetBounds(20, 20, 72, 96);
        var meter = new LevelMeter { ChannelCount = 2 };
        meter.SetBounds(400, 20, 50, 200);
        root.Add(gain, meter, new CornerResizer { Left = 460, Top = 280, Width = 18, Height = 18 });
        return root;
    }
}
```

`PluginEditorView` gère tout le protocole de la vue VST3 :

- cycle de vie `attached` / `removed` et fenêtre enfant ;
- `setContentScaleFactor` ou détection du DPI ;
- négociation de taille : `canResize`, `checkSizeConstraint`, `resizeView` puis `onSize` ;
- molette et touches transmises par l'hôte ;
- `findParameter` ;
- menu contextuel du DAW au clic droit sur un contrôle lié.

Les touches que l'hôte transmet vont aux champs de texte et aux menus ouverts ; les autres restent au DAW (voir `ForwardHostKeysToControls`).

### Instruments, notes, état partagé et MIDI learn

`PluginUI.NPlug` fournit aussi ce qu'il faut pour un instrument (VSTi) :

- **Clavier → processeur** : `ConnectKeyboard(keyboard)` dans l'éditeur envoie les notes jouées au processeur par un message VST3. Un processeur dérivé de `PluginUIProcessor<TModel>` les lit sans allocation dans `ProcessMain` avec `TryDequeueUINote`.
- **État partagé** : le processeur renvoie un objet dans `CreateSharedState` (crêtes `PeakValue`, notes `NoteActivity`, tampons) ; le contrôleur le reçoit dans `SharedState`, et l'éditeur y branche ses vumètres et son clavier (`PianoKeyboard.ActivitySource`).
- **MIDI learn** : avec `IsMidiLearnEnabled = true`, le menu contextuel de chaque contrôle lié propose « Apprentissage MIDI » et « Oublier l'affectation MIDI », y compris dans le menu du DAW. Les affectations passent par `IMidiMapping` et sont enregistrées avec l'état du contrôleur. L'apprentissage demande un hôte qui implémente `IMidiLearn` (Cubase, Nuendo…).
- **Comparaison A/B** : `ABCompareBar.Parameters = Parameters.GetSnapshotParameters()`.
- **Entrées audio d'un instrument** : rien n'empêche un instrument d'avoir des entrées audio, par exemple un bus auxiliaire de sidechain.

Le projet `samples/PluginUI.DemoSynth` met tout cela en œuvre (voir son README).

![DemoSynth](docs/images/13-demosynth.png)

### Application autonome

```csharp
StandaloneWindow.EnablePerMonitorDpiAwareness();
var host = new UIHost(root, new Size(640, 400), SizingMode.ScaleToFit);
return new StandaloneWindow(host, "Mon application").Run();
```

### Application de bureau

PluginUI peut aussi être l'interface complète d'une application Windows, compilée en IL (`dotnet run`, débogage) ou en
NativeAOT (`dotnet publish -p:PublishAot=true`, un seul `.exe` natif). La bibliothèque de base fournit la barre de menus
(`MenuBar`, Alt / F10 / lettres soulignées), les barres d'outils et d'état (`ToolBar`, `StatusBar`), les commandes et raccourcis (`Command`, `KeyGesture`, `CommandSet`), les
fenêtres modales (`StandaloneWindow.ShowDialog`), la barre de titre sombre, l'icône, la position mémorisée et le plein
écran (`StandaloneWindow.IsFullScreen`) ; le paquet **PluginUI.Desktop** ajoute l'objet application (avec sa commande de
plein écran, F11), les documents (Nouveau / Ouvrir / Enregistrer / Enregistrer sous,
« enregistrer les modifications ? »), annuler / rétablir, les fichiers récents et des réglages JSON sans réflexion.

```csharp
[STAThread]
static int Main()
{
    var app = new DesktopApplication("Mon outil", company: "Moi");
    var host = new UIHost(root, new Size(1000, 700), SizingMode.Relayout);
    return app.Run(new StandaloneWindow(host, app.Name));
}
```

Guide : [docs/DESKTOP_FR.md](docs/DESKTOP_FR.md). Exemple : `samples/PluginUI.DesktopDemo` (Biquad Designer).

![Biquad Designer](docs/images/fr/desktop-menu.png)

### Accessibilité

Les lecteurs d'écran (Narrateur, NVDA, JAWS) lisent et pilotent les interfaces PluginUI par Windows UI Automation,
dans les applications de bureau comme dans les éditeurs de plug-in : chaque commande a un rôle, un nom (sa légende,
son libellé, le nom de son paramètre ou `AutomationName`), une valeur avec ses unités et les actions de son type
(activer, basculer, régler une valeur, ouvrir une liste déroulante, choisir une option ou un onglet, lancer une entrée
de menu). Le focus est suivi quand le clavier parcourt les commandes et les menus ; `UIHost.Announce` fait prononcer
un message. En mode contraste élevé de Windows, les hôtes dessinent avec les couleurs du jeu de l'utilisateur. Rien à
activer ; un hôte ne coûte rien tant qu'aucun client ne le demande. Guide :
[docs/ACCESSIBILITY_FR.md](docs/ACCESSIBILITY_FR.md).

### Héberger des plug-ins

**PluginUI.Host** charge des plug-ins VST3 (fichiers et bundles, ou une fabrique NPlug dans le même processus) et les
fait fonctionner depuis du code .NET : paramètres avec leurs textes affichés, notes et contrôleurs MIDI, presets,
fenêtres d'éditeur, rendu hors temps réel avec compensation de latence, mesure de réponse. L'outil en ligne de
commande **pluginhost**, construit dessus, conserve les dossiers de plug-ins dans un fichier de réglages et la liste
des plug-ins trouvés entre les lancements (chaque module est ouvert dans un processus séparé), et remplace les scripts
écrits avec Python et Pedalboard :

```
pluginhost scan
pluginhost list --sort vendor
pluginhost process -i sec.wav -o traite.wav -p "Pro-Q 3" --preset brillant.vstpreset -p "Pro-L 2" --set "Gain=+3 dB"
pluginhost render -p "Diva" --midi accords.mid -o nappe.wav
pluginhost measure -p "Pro-Q 3" -o reponse.csv --db 6log2
```

**Plug-in Host** (`tools/PluginHost.Desktop`) est l'hôte graphique, construit avec PluginUI et livré en un seul
exécutable NativeAOT : la liste des plug-ins avec une recherche, des filtres et des colonnes triables et configurables
(marque, catégorie, format, canaux, latence, date d'installation, utilisation récente et fréquente, favoris…), les
détails de chaque plug-in, les recherches avec leur progression, les dossiers de plug-ins, et une fenêtre par plug-in
ouvert avec son propre éditeur ou une table générique des paramètres, les presets, sa source (entrée audio, signal de
test, fichier audio) ou un clavier à l'écran, joué en direct par ASIO ou WASAPI avec entrée et sortie MIDI (y compris le
MIDI produit par les plug-ins). Il partage les dossiers
et la liste des plug-ins avec `pluginhost`.

![Plug-in Host](docs/images/fr/pluginhost-main.png)

Les deux hôtes chargent les plug-ins VST3 et VST 2. Guide : [docs/PLUGIN_HOST_FR.md](docs/PLUGIN_HOST_FR.md).

## Concepts

- **`UIHost`** possède la fenêtre enfant, le moteur de rendu, la mise en page, le routage des entrées (capture implicite, survol, focus, Tab, menus contextuels, glisser-déposer, dépôt de fichiers), les popups, les infobulles, la bulle de valeur, le minuteur d'animation et `Post` (exécuter sur le thread d'interface depuis un autre thread). Il ne lance jamais de boucle de messages : dans un plugin, c'est celle du DAW.
- **Coordonnées** : tout est en DIP (1/96 pouce). `Bounds` est absolu dans l'hôte : pas de transformations à cumuler.
- **Mise en page** en deux passes, comme WPF : `Measure` puis `Arrange`, avec `Width`/`Height`, minimum et maximum, `Margin` et alignements.
- **Modes de taille** :
  - `Fixed` : taille de conception × DPI × `Zoom` ;
  - `ScaleToFit` : zoom proportionnel du dessin, redimensionnable ;
  - `Relayout` : la mise en page s'adapte à la taille.
- **Paramètres** : les contrôles se lient à un `IParameter` (valeurs normalisées 0–1, geste begin / perform / end). Plusieurs gestes peuvent être ouverts en même temps, par exemple un pavé XY avec deux paramètres, ou la molette sur un contrôle pendant qu'on en glisse un autre.
- **Saisie et nombres** : sur `Knob` et `Slider`, un double-clic sur le texte de la valeur ouvre une zone de saisie ; ailleurs, il remet la valeur par défaut. `TextEntryTriggers` choisit les déclencheurs. Tout nombre affiché ou saisi utilise le point décimal, quels que soient la langue et les paramètres régionaux (`InvariantNumber`) ; une virgule est refusée.
- **Langue** : les textes propres à PluginUI (dialogues, menu des presets, infobulles) suivent `UIHost.Language` : *Auto* (langue de Windows), anglais, français, ou toute langue ajoutée par `UIStrings.Register`.
- **Rendu** : les ressources Direct2D sont recréées après une perte de périphérique (`D2DERR_RECREATE_TARGET`), et les bitmaps et SVG sont mis en cache par hôte (`UIHost.EvictImage`). L'opacité est appliquée comme un alpha global, à la manière de VSTGUI (`PushOpacityLayer` pour une vraie composition par calque). Les arcs sont des courbes de Bézier.
- **Robustesse** : aucune exception ne traverse la frontière native, ni dans la procédure de fenêtre, ni dans les minuteurs, ni dans les actions postées (`UIHost.UnhandledException`). La classe de fenêtre a un nom unique par chargement du module et un compteur de références. Les gestes ouverts sont fermés si la capture est perdue, si le contrôle quitte l'arbre ou si l'éditeur se ferme.

## Mode d'édition en direct

`Ctrl+Maj+E` dans l'éditeur ouvert dans le DAW :

- sélection, déplacement et redimensionnement des éléments à la souris ou au clavier ;
- Ctrl+Z annule ;
- **Ctrl+C copie le code C#** correspondant (`SetBounds(...)` ou `Margin`), à recoller dans `Build()`.

Désactivez le raccourci en production avec `host.LiveEditShortcutEnabled = false` (fait dans les versions Release des projets créés par PluginUIEditor).

Avec un `LiveDesignBridge` (versions Debug des projets créés par PluginUIEditor), **Ctrl+S** réécrit la disposition dans le fichier `.pluginui`, et l'enregistrement du design dans PluginUIEditor reconstruit l'éditeur dans le DAW sans recharger le plugin.

## Concepteur visuel : PluginUIEditor

`tools/PluginUIEditor` est une application WinForms pour composer les éditeurs à la souris : boîte à outils de tous les éléments, surface de conception rendue par PluginUI lui-même, grille des propriétés documentée, liaisons aux paramètres, annuler / rétablir, presse-papiers, aperçu interactif, interface en anglais ou en français. Un design s'enregistre en `.pluginui` (JSON) et se transforme en code `.Designer.cs` sans réflexion (NativeAOT), se charge à l'exécution (`DesignLoader`), s'exporte (PNG, thème, ressources) ou sert de point de départ à un nouveau projet d'effet, d'instrument ou d'application. Guide complet : [docs/EDITOR_FR.md](docs/EDITOR_FR.md) ([English](docs/EDITOR.md)).

![PluginUIEditor](docs/images/fr/14-editor.png)

## Performances et SIMD

`Directory.Build.targets` fixe le jeu d'instructions pour tous les projets du dossier :

- Les bibliothèques sont AnyCPU (x64 et ARM64) ; la galerie, l'éditeur et le plugin compilent par défaut pour l'architecture de la machine (`-r win-x64` ou `-r win-arm64` pour choisir).
- En NativeAOT, `IlcInstructionSet` vaut `x86-64-v3` (AVX, AVX2, FMA, BMI1/2). Sans ce réglage, le code AOT est figé sur la base x64, sans AVX ni FMA. Pour viser des processeurs sans AVX2, publiez avec `-p:IlcInstructionSet=x86-64-v2`.
- En JIT, rien à régler : le compilateur utilise ce que le processeur offre.

Le DSP d'affichage est vectorisé avec `Vector<T>` : 8 floats ou 4 doubles par instruction avec AVX2, 4 ou 2 avec SSE.

- **`Fft`** :
  - papillons sur des tableaux réel / imaginaire séparés, avec la permutation par inversion de bits fusionnée au chargement ;
  - `MagnitudeSpectrum` utilise l'algorithme pour signal réel (une FFT complexe de N/2 points, puis séparation), ce qui divise le travail par deux ;
  - conversion en dB vectorisée (`Vector.Log`).
- **`AudioRingBuffer`** : copies en deux segments contigus (copies de blocs) au lieu d'un échantillon à la fois avec masquage d'indice ; moyenne stéréo vectorisée.
- **`PeakValue.PushBlockPeak`**, oscilloscope (min/max par colonne), analyseur (max par colonne) : réductions vectorisées.

Mesures avant / après (médiane, NativeAOT `x86-64-v3`, machine virtuelle de 2 cœurs : valeurs indicatives) :

| Opération | Avant | Après | Gain |
|---|---|---|---|
| Spectre, 2 048 points | 60 µs | 11 µs | × 5,3 |
| Spectre, 8 192 points | 317 µs | 87 µs | × 3,7 |
| Anneau : écriture de 512 échantillons (thread audio) | 0,30 µs | 0,03 µs | × 10 |
| Anneau : copie des 4 096 derniers (interface) | 2,6 µs | 0,14 µs | × 19 |
| Crête d'un bloc de 512 | 2,3 µs | 0,17 µs | × 14 |
| `Fft.Forward` (API `Complex`), 4 096 points | 72 µs | 58 µs | × 1,3 |

Le gain de `Forward` est plus faible parce que la conversion entre `Complex[]` et les tableaux séparés pèse lourd. Les contrôles n'utilisent que `MagnitudeSpectrum`.

Chaque noyau SIMD est vérifié contre une référence scalaire, ou contre la définition de la DFT pour la FFT. Pour couvrir aussi les chemins SSE seul et scalaire :

```powershell
$env:DOTNET_EnableAVX = '0';        dotnet test tests/PluginUI.Tests; Remove-Item Env:DOTNET_EnableAVX
$env:DOTNET_EnableHWIntrinsic = '0'; dotnet test tests/PluginUI.Tests; Remove-Item Env:DOTNET_EnableHWIntrinsic
```

## Galerie

```
dotnet run --project samples/PluginUI.Gallery                       # fenêtre interactive
dotnet run --project samples/PluginUI.Gallery -- --snapshot out 2   # captures PNG à l'échelle 2
```

La galerie montre les familles d'éléments sur neuf pages, propose un thème sombre ou clair et quatre couleurs d'accent, et inclut l'édition en direct. Elle démarre en anglais ; `--lang fr` donne l'interface en français :

```
dotnet run --project samples/PluginUI.Gallery -- --lang fr                    # fenêtre interactive, en français
dotnet run --project samples/PluginUI.Gallery -- --snapshot out 2 --lang fr   # captures PNG françaises à l'échelle 2
```

Les captures françaises de cette documentation sont dans `docs/images/fr/`. La galerie se publie aussi en NativeAOT : `dotnet publish -c Release -r win-x64 -p:PublishAot=true`.

## Site de documentation

La référence de l'API et les articles sont générés avec [DocFX](https://dotnet.github.io/docfx/) :

```
dotnet tool restore
dotnet docfx docfx/docfx.json --serve
```

Le site est produit dans `docfx/_site` et publié sur [GitHub Pages](https://asm8086.github.io/PluginUI/) par le workflow de documentation. Les guides français font aussi partie du site, accessibles par le lien de langue en tête de chaque guide.

## Tests

```
dotnet test tests/PluginUI.Tests
dotnet test tests/PluginUI.NPlug.Tests
dotnet test tests/PluginUI.Design.Tests
dotnet test tests/PluginUI.Host.Tests
```

Les tests n'ouvrent aucune fenêtre : ils simulent les entrées via `UIHost.Dispatch…`. Ils tournent aussi sous Linux (intégration continue).

## État de la validation

Ce qui a été vérifié pendant le développement :

- compilation des projets PluginUI sans avertissement, avec les analyseurs NativeAOT et trimming actifs et les avertissements traités comme des erreurs ;
- 136 tests unitaires (dont un rendu, exécuté sous Windows seulement), y compris les cultures fr-FR / de-DE / en-US / ru-RU, la langue de l'interface, les conventions de conception, et les noyaux SIMD exécutés avec AVX2 / AVX-512, SSE seul et en scalaire ;
- 24 tests du pont NPlug et de DemoSynth (file de notes, codes de touches de l'hôte et saisie au clavier via le DAW, valeurs saisies, MIDI learn et sa persistance, enveloppes, filtre, oscillateurs, polyphonie, sidechain) ;
- 25 tests des designs et de l'éditeur (aller-retour JSON de toutes les valeurs par défaut et du contenu inconnu, registre complet, chargeur, capture, code généré compilé par Roslyn et identique au chargeur, y compris avec des noms et valeurs hostiles, lien en direct, projets générés, commandes de l'éditeur, extensions) ;
- validateur VST3 officiel de Steinberg : 47/47 sur DemoSynth (x86-64-v3, x86-64-v2, bundle universel sur x64 et sur ARM64), et sur les projets d'effet et d'instrument créés par PluginUIEditor ;
- PluginUIEditor exécuté sous Wine et piloté par des événements souris et clavier simulés : glisser-déposer depuis la boîte à outils, sélection, suppression, annuler, déplacement au clavier, copier-coller, aperçu interactif, saisie dans la grille des propriétés et dans le tableau des paramètres, génération de code, création de projet (qui compile), enregistrement ;
- rendu Direct2D / DirectWrite / WIC de toutes les pages de la galerie et de l'éditeur de DemoSynth, hors écran, sous Wine ;
- relecture indépendante du cœur, avec corrections : capture après une boucle modale, gestes, DPI, presse-papiers, réentrance des popups.

Ce qui **n'a pas pu l'être** pendant le développement, faute de Windows réel, et reste à confirmer :

- le comportement interactif dans une fenêtre (procédure de fenêtre, souris, clavier, curseurs, changements de DPI) dans un vrai DAW ;
- le rendu SVG natif ;
- les menus déroulants de PluginUIEditor : sous Wine, ouvrir un menu d'une application WinForms en mode PerMonitorV2 lève une exception dans WinForms (reproduit avec une application de dix lignes), les menus ont donc été testés par leurs raccourcis clavier ;
- les quelques effets que Wine rend mal : dégradés vers le transparent, découpes arrondies par calque.

Faites d'abord un essai dans la galerie, puis dans le plugin DemoSynth (Reaper, Cubase, Bitwig…).

## Licence et contributions

PluginUI est publié sous [licence MIT](LICENSE) — Copyright (c) 2026 asm8086. NPlug est sous licence BSD-2-Clause (Alexandre Mutel). Les contributions sont les bienvenues : voir [CONTRIBUTING_FR.md](CONTRIBUTING_FR.md) et le [CHANGELOG](CHANGELOG.md).
