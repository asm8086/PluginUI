<!-- 2026/10/18 16:20:00 UTC - PluginUI -->

# PluginUIEditor — concepteur visuel

*English version: [EDITOR.md](EDITOR.md)*

PluginUIEditor est une application WinForms qui sert à composer visuellement les éditeurs PluginUI. La surface de conception est un véritable hôte PluginUI (Win32 + Direct2D) : ce que vous voyez est exactement ce que dessine le plugin. Un design s'enregistre dans un fichier `.pluginui` (JSON), que l'éditeur sait transformer en :

| Sortie | Ce que c'est | Destination |
|---|---|---|
| `.pluginui` | Le design lui-même : disposition, propriétés, liaisons aux paramètres, thème, réglages de génération de code | À côté du code de l'éditeur, sous contrôle de version |
| `.Designer.cs` | Du code C# qui construit le même arbre d'éléments, sans réflexion à l'exécution (NativeAOT) | Compilé dans le plugin |
| Chargement à l'exécution | `DesignLoader.Build` lit un `.pluginui` pendant l'exécution (registre compatible AOT, pas de sérialiseur par réflexion) | Habillages, dispositions choisies par l'utilisateur, outils |
| Nouveau projet | Un projet NPlug complet (effet, instrument) ou une application autonome construits autour du design | Un nouveau dossier, prêt à compiler |
| Exports | Images PNG (100 % et 200 %), thème en JSON, ressources utilisées par le design | Documentation, site web, autre projet |
| Lien de design en direct | Lien dans les deux sens entre le fichier de design et l'éditeur ouvert dans le DAW (versions Debug) | Développement |

![PluginUIEditor](images/fr/14-editor.png)

## Sommaire

- [Prérequis et lancement](#prérequis-et-lancement)
- [La fenêtre](#la-fenêtre)
- [Travailler avec les éléments](#travailler-avec-les-éléments)
- [Propriétés, liaisons et ressources](#propriétés-liaisons-et-ressources)
- [Réglages du design et paramètres](#réglages-du-design-et-paramètres)
- [Clavier et souris](#clavier-et-souris)
- [Générer le code](#générer-le-code)
- [Charger un design à l'exécution](#charger-un-design-à-lexécution)
- [Nouveau projet à partir d'un design](#nouveau-projet-à-partir-dun-design)
- [Lien de design en direct](#lien-de-design-en-direct)
- [Éléments personnalisés (extensions)](#éléments-personnalisés-extensions)
- [Options et langue](#options-et-langue)
- [Le format `.pluginui`](#le-format-pluginui)
- [Résumé de l'API de la bibliothèque](#résumé-de-lapi-de-la-bibliothèque)

## Prérequis et lancement

- Windows 10 1809 ou ultérieur, x64, avec le .NET 10 Desktop Runtime.
- Compilation : `dotnet build tools/PluginUIEditor -c Release` (le projet compile aussi sous Linux ou macOS pour l'intégration continue, mais ne s'exécute que sous Windows).
- Lancement : `PluginUIEditor.exe [design.pluginui]`. L'argument facultatif ouvre un design. `samples/Designs/DemoSynth.pluginui` est la disposition du synthétiseur de démonstration, capturée depuis son éditeur ; il est aussi copié à côté de l'exécutable (`samples\`).
- `PluginUIEditor.exe --snapshot design.pluginui sortie.png [en|fr]` ouvre le design, sélectionne son premier élément nommé et enregistre une image de la fenêtre (c'est ainsi qu'est produite la capture de cette page).

## La fenêtre

- **Boîte à outils** (en haut à gauche) : tous les éléments de PluginUI, groupés comme dans [CONTROLS_FR.md](CONTROLS_FR.md) : conteneurs, rotatifs, linéaires, boutons, texte, mesures, instrument, analyse, sélecteurs, décoration. L'info-bulle affiche la documentation XML de l'élément. Les éléments personnalisés des [extensions](#éléments-personnalisés-extensions) ont leurs propres groupes.
- **Structure** (en bas à gauche) : l'arbre des éléments. On y sélectionne, on renomme sur place (F2 ou clic lent), on fait glisser un élément sur un conteneur pour l'y déplacer, ou sur un élément pour le placer avant celui-ci.
- **Surface de conception** (au centre) : le design dessiné par PluginUI au zoom courant, avec l'éditeur en direct de PluginUI : sélection, poignées, grille magnétique et étiquette indiquant le type, le nom, la position et la taille.
- Onglet **Propriétés** : les propriétés de l'élément sélectionné (ou de la racine si rien n'est sélectionné), avec en bas la documentation de la propriété sélectionnée.
- Onglet **Design** : nom, taille, mode de redimensionnement, thème et réglages de génération de code.
- Onglet **Paramètres** : les paramètres auxquels le design se lie (voir [plus bas](#réglages-du-design-et-paramètres)).
- Onglet **Messages** : problèmes rencontrés au chargement ou à la construction du design (types ou propriétés inconnus, ressources manquantes, noms en double), avertissements de la génération de code, résultat du chargement des extensions. Le titre de l'onglet indique le nombre de messages.
- **Barre d'état** : dernière action, nombre d'éléments, zoom.

## Travailler avec les éléments

- **Ajouter** : faites glisser un élément de la boîte à outils sur la surface ; il va dans le conteneur le plus profond sous la souris, au point de dépôt, aligné sur la grille, avec la taille naturelle de l'élément. Un double-clic dans la boîte à outils ajoute l'élément au conteneur sélectionné (ou au conteneur de l'élément sélectionné).
- **Conteneurs** : `Canvas` place ses enfants selon `Left` / `Top`. Les panneaux de mise en page (`StackPanel`, `Grid`, `DockPanel`, `WrapPanel`, `UniformGrid`, `LayeredPanel`) les disposent : réglez `Row`, `Column`, `Dock`, `Margin` et les alignements dans les propriétés. `Border`, `GroupBox` et `ScrollViewer` contiennent un enfant ; `SplitView` en contient deux (emplacements `First` et `Second` ; un élément en trop est ignoré, avec un message) ; chaque enfant d'un `TabView` est un onglet dont le titre est son **emplacement** (`(Slot)` dans les propriétés).
- **Sélectionner, déplacer, redimensionner** sur la surface : un clic sélectionne (Alt+clic sélectionne le parent), un glisser déplace, les huit poignées redimensionnent ; maintenez Alt pendant le glisser pour ignorer la grille. Au clavier : les flèches déplacent (1 DIP, Maj : 10 DIP), Ctrl+flèches redimensionne, Tab parcourt les éléments frères, Échap désélectionne. La nouvelle disposition est enregistrée dans `Left`, `Top`, `Width`, `Height` (ou `Margin` dans les panneaux de mise en page).
- **Ordre** : *Édition ▸ Mettre au premier plan / à l'arrière-plan* change l'ordre de dessin parmi les éléments frères.
- **Presse-papiers** : couper, copier, coller et dupliquer portent sur des sous-arbres entiers. Le presse-papiers contient du JSON (format `pluginui-nodes`) : on peut donc coller des éléments dans un autre design ou une autre instance de l'éditeur. Les éléments collés reçoivent des noms uniques et, sur un canevas, un décalage de 8 DIP. Un conteneur plein (un `SplitView` avec ses deux volets, un `Border` avec son enfant) transmet le collage à son parent.
- **Annuler / rétablir** : chaque modification, y compris les déplacements sur la surface et les saisies dans les tableaux, peut être annulée (300 étapes).
- **Aperçu interactif** (F5) : les contrôles réagissent à la souris et au clavier comme dans le plugin, liés aux paramètres d'aperçu (voir [Paramètres](#réglages-du-design-et-paramètres)) ; les raccourcis d'édition (Suppr, presse-papiers, annuler) reviennent alors aux contrôles du design. F5 de nouveau revient à l'édition.

## Propriétés, liaisons et ressources

L'onglet **Propriétés** liste toutes les propriétés modifiables du type d'élément (publiques, en lecture-écriture, d'un [type pris en charge](#le-format-pluginui)), groupées par catégorie. Une propriété en **gras** diffère de la valeur par défaut de l'élément et est écrite dans le fichier ; la réinitialiser (clic droit ▸ Rétablir ; pour les valeurs saisies en JSON, effacer le texte) la retire du fichier.

- `(Name)` : le nom de l'élément. Il est facultatif, mais il devient un **champ** de la classe générée et la clé qu'utilisent le chargeur et le lien de design en direct : nommez les éléments que votre code utilise. Ce doit être un identifiant C# qui n'est ni le nom d'un type de PluginUI utilisé par le code généré (`Color`, `Knob`, `Orientation`…) ni celui de la classe ; un nom déjà pris reçoit un suffixe numérique.
- Couleurs : `#RRGGBB` ou `#RRGGBBAA`, avec un sélecteur de couleur (`…`).
- `Thickness`, `Point`, `Size`, `Rect` : nombres séparés par des virgules (`8, 4` ou `1, 2, 3, 4`). Les nombres utilisent toujours le point décimal, quels que soient les paramètres régionaux de Windows.
- Images et SVG : un chemin relatif au dossier du fichier de design, avec un sélecteur de fichier (`…`) ; enregistrez d'abord le design. Un fichier choisi hors du dossier du design est copié dans son dossier `assets`, pour que les projets générés puissent l'incorporer.
- Styles de texte, styles vectoriels, peintures, film-strips, habillages et listes se saisissent en JSON compact, par exemple un habillage film-strip :
  `{"kind":"FilmStrip","Image":"assets/knob.png","FrameCount":128,"Orientation":"Vertical"}`.
- **Liaisons** (catégorie *Liaison*) : les propriétés de type `IParameter` (`Parameter`, `XParameter`, `GainParameter`…) se choisissent dans la liste des paramètres du design. Dans le plugin, le code généré ou le chargeur associe chaque nom à un vrai paramètre.
- Les propriétés de types non descriptibles (événements, délégués, objets quelconques) ne sont pas affichées : réglez-les dans le code, dans `OnBuilt` (voir [Générer le code](#générer-le-code)).

## Réglages du design et paramètres

Onglet **Design** :

| Réglage | Signification |
|---|---|
| Nom | Nom du design (entête des fichiers générés, nom de classe par défaut) |
| Taille | Taille logique de l'éditeur à 100 % (DIP), `largeur, hauteur` |
| Redimensionnement | `Fixed` (taille fixe), `ScaleToFit` (zoom proportionnel quand la fenêtre hôte change de taille), `Relayout` (la mise en page s'adapte à la taille) |
| Thème de base / Couleur d'accent | `Dark` ou `Light`, et une couleur d'accent facultative. Les autres surcharges du thème (palette, typographie, métriques) viennent d'un thème importé (*Fichier ▸ Importer un thème*) |
| Espace de noms / Classe | Espace de noms et nom de la classe partielle générée |
| Cible | `PluginEditorView` : la classe générée est l'autre moitié d'un `PluginEditorView` et redéfinit `Build()` ; `Standalone` : seulement une méthode publique `BuildDesign()`, pour n'importe quel hôte |
| Redéfinir Build | Génère `protected override UIElement Build() => BuildDesign();` (cible PluginEditorView) |
| Accès aux champs | Modificateur d'accès des champs des éléments (`private`, `internal`, `protected`, `public`) |

Onglet **Paramètres** : une ligne par paramètre auquel le design se lie, avec son nom, sa nature (`Linear`, `Logarithmic`, `Integer`, `Toggle`, `Choice`), sa plage, sa valeur par défaut, son unité, ses décimales et ses choix (séparés par des virgules). Dans l'éditeur, ces lignes créent des **paramètres d'aperçu** : les potentiomètres affichent des valeurs vraisemblables et réagissent en aperçu interactif. Dans le plugin, les noms sont associés aux vrais paramètres (par leur titre dans les projets générés) : gardez-les identiques aux titres de vos paramètres NPlug. Renommer un paramètre ici met à jour les contrôles qui lui sont liés ; deux paramètres ne peuvent pas porter le même nom. Un design capturé depuis un éditeur en fonctionnement contient déjà ses paramètres.

## Clavier et souris

| Touches | Action |
|---|---|
| Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Maj+S | Nouveau, ouvrir, enregistrer, enregistrer sous |
| Ctrl+G | Générer le code |
| Ctrl+Maj+N | Nouveau projet à partir du design |
| Ctrl+Z / Ctrl+Y (ou Ctrl+Maj+Z) | Annuler / rétablir |
| Ctrl+X / Ctrl+C / Ctrl+V / Ctrl+D | Couper, copier, coller, dupliquer |
| Suppr | Supprimer l'élément sélectionné |
| Alt+Haut | Sélectionner le parent |
| Ctrl+Maj+] / Ctrl+Maj+[ | Premier plan / arrière-plan |
| Flèches, Maj+flèches | Déplacer de 1 / 10 DIP |
| Ctrl+flèches | Redimensionner |
| Tab / Maj+Tab, Échap | Élément frère suivant / précédent, désélectionner |
| Alt+clic, Alt+glisser | Sélectionner le parent, déplacer sans la grille |
| F2 (structure) | Renommer |
| Ctrl+= / Ctrl+- / Ctrl+0 / Ctrl+9 | Zoom avant, zoom arrière, 100 %, ajuster à la fenêtre |
| F5 | Aperçu interactif oui / non |
| F1 | Cette documentation |

Les raccourcis d'édition (annuler, presse-papiers, Suppr) n'agissent sur le design que lorsque la surface ou la structure a le focus ; dans les zones de texte et les tableaux, ils gardent leur rôle habituel. Sur un clavier AZERTY, Ctrl+Maj+] et Ctrl+Maj+[ correspondent aux touches de ces caractères sur un clavier QWERTY : utilisez plutôt le menu *Édition*.

## Générer le code

*Fichier ▸ Générer le code* (Ctrl+G) écrit `<Classe>.Designer.cs`, une moitié de classe partielle. Extrait, pour le synthétiseur de démonstration :

```csharp
// 2026/10/18 14:24:00 UTC - DemoSynth
// <auto-generated> … </auto-generated>
partial class DemoSynthDesign
{
    public static readonly Size DesignSize = new Size(820f, 470f);
    public const SizingMode DesignSizing = SizingMode.ScaleToFit;
    public static Theme CreateDesignTheme() => new Theme("Dark", new Palette(…), new Typography(…), new Metrics(…));

    private Knob cutoff = null!;
    private PianoKeyboard keyboard = null!;

    private UIElement BuildDesign()
    {
        var canvas1 = new Canvas();
        cutoff = new Knob { Name = "cutoff", Width = 76f, Height = 104f, Label = "CUTOFF" };
        cutoff.Parameter = BindParameter("Cutoff");
        …
        OnBuilt(canvas1);
        return canvas1;
    }

    protected override UIElement Build() => BuildDesign();
    partial void OnBuilt(UIElement root);
    private partial IParameter? BindParameter(string name);
}
```

Vous écrivez l'autre moitié :

```csharp
public sealed partial class DemoSynthDesign : PluginEditorView
{
    public DemoSynthDesign(MyController controller) : base(controller, DesignSize, DesignSizing) { }

    protected override void ConfigureHost(UIHost host) => host.Theme = CreateDesignTheme();

    // Événements et tout ce que le design ne peut pas décrire.
    partial void OnBuilt(UIElement root) => ConnectKeyboard(keyboard);

    // Nom de paramètre du design → paramètre du plugin.
    private partial IParameter? BindParameter(string name)
        => Parameters.GetSnapshotParameters(includeBypass: true).FirstOrDefault(p => p.Name == name);
}
```

- La partie générée contient aussi `BindDesignFields(UIElement root)`, qui fait pointer les champs des éléments vers ceux d'un autre arbre construit à partir du même design (utilisé après un rechargement en direct).
- `BindParameter` n'est généré que si le design comporte des liaisons ; `LoadImage(string path)` et `LoadSvg(string path)` seulement s'il utilise des images ou des SVG (implémentez-les avec `ImageSource.FromResource` / `SvgImage.FromResource`, ou laissez un projet généré s'en charger).
- Le code n'utilise que des constructeurs et des affectations de propriétés : aucune réflexion, compatible NativeAOT, et les valeurs sont exactes (les littéraux `float` sont reproduits à l'identique, les couleurs s'écrivent `Color.FromRgb(0xRRGGBB, alpha)`).
- Régénérez après chaque modification du design ; ne modifiez jamais le fichier `.Designer.cs`. Dans un projet créé par l'éditeur, le [lien de design en direct](#lien-de-design-en-direct) reconstruit l'éditeur à partir du fichier de design dans les versions Debug.

## Charger un design à l'exécution

`PluginUI.Design` sait aussi construire un design pendant l'exécution, pour des habillages ou des dispositions choisis par l'utilisateur. Le registre des types d'éléments est explicite (pas d'exploration des assemblies) et le JSON se lit avec `JsonDocument` : cela fonctionne en NativeAOT. Enregistrez vos propres éléments avec `registry.Register<MonControle>()`.

```csharp
protected override UIElement Build()
{
    using var stream = typeof(MyEditor).Assembly.GetManifestResourceStream("assets/Editor.pluginui")!;
    var warnings = new List<string>();
    var design = DesignJson.Read(new StreamReader(stream).ReadToEnd(), warnings: warnings);
    var result = DesignLoader.Build(design, options: new DesignLoadOptions
    {
        ResolveParameter = DesignLoadOptions.ByName(Parameters.GetSnapshotParameters(includeBypass: true)),
        Assets = new EmbeddedAssetResolver(typeof(MyEditor).Assembly),   // ou new FileAssetResolver(dossier)
    });
    result.Get<Button>("reset").Clicked += _ => ResetAll();
    return result.Root;   // result.Warnings liste les types inconnus, les ressources manquantes…
}
```

Les éléments de type inconnu sont remplacés par une étiquette grisée (`PlaceholderForUnknownTypes`) et les propriétés inconnues sont conservées dans le document : un design créé avec une version plus récente ou avec des extensions s'ouvre quand même.

## Nouveau projet à partir d'un design

*Fichier ▸ Nouveau projet à partir du design* (Ctrl+Maj+N) crée un projet complet autour du design courant :

| Champ | Signification |
|---|---|
| Nom du projet | Un identifiant C# : noms du projet, de l'espace de noms et des classes. Ce ne peut pas être le nom d'un type ou d'un espace de noms utilisé par le code généré (`Knob`, `PluginUI`, `NPlug`, `AudioPlugin`…), puisqu'il devient l'espace de noms racine |
| Type | Effet VST3, instrument VST3 (entrée d'événements, clavier à l'écran connecté) ou application Windows |
| Dossier parent | Le projet est créé dans `<dossier>\<nom>` (un dossier existant n'est jamais écrasé) |
| Éditeur, site web, e-mail | Informations de la fabrique du plugin |
| Lien de design en direct | Ajoute le lien aux versions Debug |

Fichiers créés, pour un instrument nommé `MySynth` :

```
MySynth/
├── MySynth.csproj              Plugin NativeAOT (.vst3), références à NPlug et PluginUI
├── Directory.Build.props
├── README.md
├── .gitignore
├── Plugin/                     MySynthModel (paramètres du design), Processor, Controller, Plugin, LogRangeParameter
└── Editor/
    ├── MySynthEditor.pluginui  Le design (à ouvrir avec PluginUIEditor)
    ├── MySynthEditor.Designer.cs
    ├── MySynthEditor.cs        Votre moitié : OnBuilt, BindParameter (par titre de paramètre), lien en direct
    └── assets/                 Images et SVG du design (ressources incorporées « assets/… »)
```

Les dossiers de NPlug et de PluginUI sont pris dans *Outils ▸ Options* et écrits relativement au projet quand c'est possible ; sans eux, le fichier projet attend PluginUI dans `..\PluginUI\` (dossier voisin du projet) et prend les sources de NPlug dans son sous-module (`external\NPlug\src\`), ou à défaut dans `..\NPlugCorrected\src\` ; les propriétés MSBuild `NPlugRoot` et `PluginUIRoot` les remplacent. Les paramètres du design deviennent des paramètres NPlug (linéaire, logarithmique, entier, interrupteur, liste). Le processeur d'un effet laisse passer l'audio et l'instrument joue une sinusoïde monophonique : remplacez-les par votre DSP.

## Lien de design en direct

Dans les versions Debug d'un projet généré (symbole `LIVE_DESIGN`), l'éditeur ouvert dans le DAW est lié à son fichier `.pluginui` :

- **Design → DAW (rechargement à chaud)** : enregistrez le design dans PluginUIEditor et la fenêtre du plugin se reconstruit en un quart de seconde, sans recharger le plugin.
- **DAW → design** : dans le plugin, Ctrl+Maj+E passe en mode d'édition en direct ; déplacez et redimensionnez des éléments, puis Ctrl+S réécrit la disposition (`Left`, `Top`, `Width`, `Height`, `Margin`) des éléments déplacés depuis le dernier enregistrement dans le fichier de design, en les retrouvant par leur nom. Le reste du fichier est conservé, et Ctrl+S sans aucun déplacement n'écrit rien. PluginUIEditor relit le fichier quand vous le rouvrez.
- À l'ouverture de la fenêtre du plugin, si le fichier de design est plus récent que le code généré, un éditeur Debug généré se reconstruit à partir de ce fichier : un `.Designer.cs` pas encore régénéré ne gêne donc pas pendant le développement. Un rechargement applique l'arbre des éléments, le thème et la taille du design, puis fait pointer les champs des éléments vers les nouveaux (`BindDesignFields`) et appelle `OnBuilt` avec la nouvelle racine.

Pour ajouter le lien à un éditeur existant :

```csharp
#if DEBUG
private LiveDesignBridge? _bridge;
protected override void OnAttached() => _bridge = new LiveDesignBridge(Host, @"C:\src\MyPlugin\Editor\MyEditor.pluginui",
    design => DesignLoader.Build(design, options: new DesignLoadOptions { ResolveParameter = … }));
protected override void OnRemoved() => _bridge?.Dispose();
#endif
```

Après un rechargement, les champs et les gestionnaires d'événements désignent encore les anciens éléments : traitez `Reloaded` (qui reçoit le `DesignBuildResult`) pour relier les nouveaux (le code généré contient `BindDesignFields(root)` pour les champs ; les projets générés l'appellent, puis `OnBuilt` avec la nouvelle racine).

## Éléments personnalisés (extensions)

Vos propres éléments (voir [EXTENDING_FR.md](EXTENDING_FR.md)) peuvent apparaître dans la boîte à outils : compilez-les dans une bibliothèque de classes et ajoutez sa `.dll` dans *Outils ▸ Options ▸ Assemblies d'extension* (un chemin par ligne, pris en compte au prochain démarrage). Tout `UIElement` public, non abstrait, doté d'un constructeur public sans paramètre est enregistré ; `[System.ComponentModel.Category("Mes contrôles")]` sur la classe choisit son groupe dans la boîte à outils (par défaut : le nom de l'assembly). Un élément dont le nom est déjà enregistré est ignoré. L'onglet *Messages* indique ce qui a été chargé.

Sans l'extension, un design qui utilise des éléments personnalisés s'ouvre quand même : ils apparaissent comme des emplacements réservés et leurs propriétés sont conservées. Dans votre plugin, enregistrez-les pour le chargeur (`registry.Register<MonControle>()`) ; le code généré n'a besoin de rien de plus, puisqu'il utilise directement les classes.

## Options et langue

*Outils ▸ Options* :

- **Langue de l'interface** : *Automatique* (suit la langue d'affichage de Windows : français pour `fr-*`, anglais sinon), anglais ou français. Les menus, panneaux, catégories, boîtes de dialogue et messages sont traduits, ainsi que les textes dessinés par PluginUI sur la surface (menus contextuels, édition en direct). Les noms des propriétés restent ceux de l'API C#. Les nombres utilisent toujours le point décimal.
- **Grille magnétique** en DIP (0 : aucune).
- **Dossier `src` de NPlug** et **dossier de PluginUI** utilisés par les nouveaux projets.
- **Assemblies d'extension**.

Les réglages sont enregistrés dans `%AppData%\PluginUIEditor\settings.json` avec la liste des fichiers récents.

## Le format `.pluginui`

JSON UTF-8, indenté, dans un ordre stable (facile à comparer). Les nombres utilisent la culture invariante ; les petits tuples numériques tiennent sur une ligne.

```json
{
  "comment": "2026/10/18 14:24:00 UTC - PluginUI",
  "format": "pluginui",
  "version": 1,
  "name": "DemoSynth",
  "size": [820, 470],
  "sizing": "ScaleToFit",
  "theme": { "base": "Dark", "accent": "#FFB74D" },
  "parameters": [
    { "name": "Cutoff", "kind": "Logarithmic", "min": 20, "max": 20000, "default": 2400, "unit": "Hz", "decimals": 2 }
  ],
  "codegen": { "namespace": "PluginUI.DemoSynth.Editor", "class": "DemoSynthDesign", "target": "PluginEditorView",
               "buildOverride": true, "fieldAccess": "private" },
  "root": {
    "type": "Canvas",
    "children": [
      { "type": "Knob", "name": "cutoff",
        "properties": { "Left": 206, "Top": 86, "Width": 76, "Height": 104, "Label": "CUTOFF" },
        "bindings": { "Parameter": "Cutoff" } }
    ]
  }
}
```

| Membre | Contenu |
|---|---|
| `comment` | Texte libre facultatif, écrit en premier (le JSON n'a pas de commentaires) ; les projets générés y mettent une date et le nom du projet |
| `format`, `version` | Toujours `"pluginui"` ; version du format (1). Une version plus récente s'ouvre avec un avertissement ; les membres que cette version ne connaît pas (dans le document, les paramètres, les réglages de génération et les nœuds) sont conservés et réécrits, avec le numéro de version |
| `name`, `size`, `sizing` | Voir [Réglages du design](#réglages-du-design-et-paramètres) |
| `theme` | `base` (`Dark` / `Light`), `accent`, et des objets facultatifs `palette`, `typography`, `metrics` qui remplacent des valeurs isolées (un champ inconnu, une valeur du mauvais type ou une section inconnue n'est pas appliqué, est signalé et reste dans le fichier) |
| `parameters` | `name`, `kind`, `min`, `max`, `default`, `unit`, `decimals`, `choices` |
| `codegen` | `namespace`, `class`, `target`, `buildOverride`, `fieldAccess` |
| `root` | Le nœud racine |
| nœud | `type` (nom de la classe), `name`, `slot` (titre d'onglet, volet de séparateur), `properties` (seulement les valeurs différentes des valeurs par défaut), `bindings` (propriété → nom de paramètre), `children` |

Valeurs des propriétés :

| Type de propriété | JSON |
|---|---|
| `string`, `bool`, nombres, `char`, énumérations | Valeur JSON ; les énumérations par leur nom ; NaN et les infinis en `"NaN"`, `"Infinity"`, `"-Infinity"` |
| `Color` | `"#RRGGBB"`, `"#RRGGBBAA"`, ou `[r, g, b, a]` (0–1) quand un canal ne tombe pas sur la grille 8 bits |
| `Thickness`, `Point`, `Size`, `Rect` | Tableaux de nombres (`8`, `[8, 4]`, `[g, h, d, b]`, `[x, y]`, `[l, h]`, `[x, y, l, h]`) |
| `TextStyle`, `VectorStyle` | Objets avec les membres du record qui diffèrent des valeurs par défaut |
| `Paint` | Une couleur, ou `{"type": "linear" ou "radial", "stops": [[position, couleur], …], …}` |
| Listes de `string`, `float`, `double` | Tableaux |
| `ImageSource`, `SvgImage` | Chemin de la ressource, relatif au fichier de design (`"assets/fond.png"`) |
| `FilmStrip` | `{"Image": "…", "FrameCount": 64, "Orientation": "Vertical"}` |
| `IControlSkin` | `{"kind": "FilmStrip" / "ImageRotary" / "SvgRotary" / "ImageState", …}` avec les chemins d'images et les options de l'habillage |

Le presse-papiers et les fichiers de *nœuds* utilisent le même format de nœud dans `{"format": "pluginui-nodes", "nodes": [ … ]}`. Les thèmes exportés utilisent `{"format": "pluginui-theme", …}` et peuvent être importés dans n'importe quel design.

## Résumé de l'API de la bibliothèque

Tout est dans l'espace de noms `PluginUI.Design` (`src/PluginUI.Design`, compatible AOT, documenté par des commentaires XML) :

| Type | Rôle |
|---|---|
| `DesignDocument`, `DesignNode`, `DesignParameter`, `DesignTheme`, `CodegenSettings` | Le design en mémoire |
| `DesignJson` | `Load` / `Read` / `Save` / `Write` des fichiers `.pluginui` ; `WriteNodes` / `ReadNodes` pour le presse-papiers |
| `DesignTypeRegistry`, `DesignTypeInfo`, `DesignPropertyInfo` | Types et propriétés modifiables ; `CreateDefault()`, `Register<T>()` |
| `DesignLoader`, `DesignLoadOptions`, `DesignBuildResult` | Construire un arbre d'éléments vivant à partir d'un design |
| `DesignCapture` | Capturer un arbre vivant ou tout un `UIHost` sous forme de design (`CaptureHost`) |
| `CSharpDesignGenerator` | Générer le code `.Designer.cs` |
| `ThemeJson` | Exporter / importer des thèmes, thème ↔ thème de design |
| `DesignValues` | Conversions de valeurs (JSON, valeurs de design, valeurs vivantes, C#) |
| `IDesignAssetResolver`, `FileAssetResolver`, `EmbeddedAssetResolver` | Charger les images et les SVG d'un design |
| `LiveDesignBridge` | Lier un éditeur en fonctionnement à son fichier de design |
| `ProjectScaffolder`, `ScaffoldOptions` | Créer un projet à partir d'un design |
