<!-- 2026/11/06 12:53:00 UTC - PluginUI -->

# Accessibilité

*[English](ACCESSIBILITY.md)*

PluginUI dessine lui-même toutes les commandes dans une seule fenêtre : Windows ne peut donc pas les voir seul. La
bibliothèque les décrit aux technologies d'assistance par **Windows UI Automation** : Narrateur, NVDA et JAWS lisent
le nom, le rôle, la valeur et l'état de chaque commande, suivent le focus clavier et agissent sur les commandes. Cela
fonctionne dans les applications de bureau comme dans les éditeurs de plug-in ouverts dans une DAW. En mode contraste
élevé, l'interface prend les couleurs du jeu de couleurs de Windows.

Il n'y a rien à activer. Un hôte ne coûte rien tant qu'aucun lecteur d'écran ni autre client ne demande l'arbre.

## Ce que reçoivent les lecteurs d'écran

| Élément | Rôle | Ce qui peut être lu et fait |
|---|---|---|
| `Button` | Bouton | Activer |
| `ToggleButton`, `SlideSwitch`, `Switch` à deux positions | Bouton avec un état bascule | Basculer (activé / désactivé) |
| `CheckBox` | Case à cocher | Basculer |
| `Knob`, `Slider`, `ModWheel`, `PitchWheel`, autres `ValueControl` | Curseur (annoncé « bouton rotatif » pour `Knob`) | Plage de valeurs et texte de la valeur (« -6.0 dB ») ; réglage par nombre ou par texte |
| `NumberBox`, `ValueDisplay` | Toupie (spinner) | Comme un curseur |
| `RadioGroup`, `SegmentedControl`, `Switch` à 3 positions ou plus | Groupe de cases d'option | Chaque option, sa position (« 2 sur 3 »), Sélectionner ; le focus est sur l'option choisie |
| `DropDown` | Zone de liste déroulante | Développer / Réduire, entrée choisie, choix par le texte |
| `MenuButton` | Bouton | Développer / Réduire |
| `TextBox` | Zone d'édition | Texte, lecture seule, état « saisie non valide » |
| `Label`, `DialogText`, champs de la barre d'état | Texte | Contenu |
| `ListBox` | Liste d'éléments | Sélectionner, ajouter à la sélection, en retirer ; le focus est sur la ligne courante |
| `TabView` | Onglets | Choisir un onglet ; le contenu de la page suit les onglets |
| `GroupBox` | Groupe nommé par son en-tête | Développer / Réduire s'il est repliable |
| `MenuBar`, menus | Barre de menus, menus, éléments de menu | Développer / Réduire les sous-menus, Activer, coches, touches d'accès (Alt+F) et raccourcis (Ctrl+S) |
| `ToolBar`, `ToolBarButton` | Barre d'outils, boutons | Activer (Basculer pour les commandes cochables), raccourci, description |
| `StatusBar` | Barre d'état avec des textes | Les messages de `ShowMessage` sont annoncés |
| `Dialog`, fenêtres contextuelles modales | Volet annoncé « boîte de dialogue » | Le contenu sous une fenêtre modale est écarté, comme pour la souris |
| `ProgressBar` | Barre de progression | Valeur en lecture seule |
| Autres commandes focalisables | Personnalisé | Nom (libellé ou info-bulle) |

Les panneaux de disposition (`Canvas`, `Grid`, `StackPanel`…) sont transparents : leurs enfants apparaissent à leur
place. Les éléments masqués sont écartés.

Les valeurs des commandes liées à un paramètre sont données dans les unités du paramètre (une coupure de 20 à
20000 Hz, un gain en dB) : la plage vient de `IParameter.ToPlain`, le texte est celui qu'affiche la commande. Une
modification faite par un lecteur d'écran est un geste complet (`BeginEdit` / `PerformEdit` / `EndEdit`) : la DAW
l'enregistre comme une seule étape d'automation.

## Nommer les commandes

Le nom prononcé par un lecteur d'écran vient, dans cet ordre :

1. de `AutomationName`, s'il est renseigné ;
2. de l'élément désigné par `AutomationLabeledBy` (un `Label` placé à côté) ;
3. de la légende de la commande : `Text` des boutons et cases à cocher, `Label`, nom du paramètre lié ;
4. de l'info-bulle.

```csharp
var drive = new Knob(model.Drive);                                // « Drive » (nom du paramètre)
var open = new Button { Svg = folderIcon, ToolTip = "Ouvrir un preset" };  // « Ouvrir un preset »
var close = new Button { Svg = crossIcon, AutomationName = "Fermer" };
var caption = new Label("Caractère");
var mode = new DropDown("Propre", "Chaud") { AutomationLabeledBy = caption };
var logo = new ImageView(logoImage) { AutomationHidden = true };  // décoration
```

`AutomationHelpText` ajoute une description lue après le nom (par défaut, l'info-bulle quand elle n'est pas déjà le
nom). `AutomationHidden` retire un élément et ses descendants de l'arbre. Ces propriétés figurent dans la grille des
propriétés de PluginUIEditor (catégories Texte et Comportement).

Donnez un nom à l'éditeur lui-même : Narrateur et NVDA le lisent quand le focus entre dans la fenêtre du plug-in.

```csharp
protected override void ConfigureHost(UIHost host)   // PluginEditorView
{
    host.AutomationName = "Lumen EQ";
}
```

## Clavier

Les utilisateurs de lecteurs d'écran travaillent au clavier : Tab et Maj+Tab passent d'une commande à l'autre, les
flèches, Page préc. / Page suiv., Début / Fin changent les valeurs, Suppr rétablit la valeur par défaut, Entrée permet
de taper une valeur, Espace bascule. Les menus fonctionnent comme dans Windows (F10, Alt, flèches, Échap). Un lecteur
d'écran peut aussi placer le focus sur une commande (`SetFocus` d'UI Automation) : la fenêtre de l'hôte prend alors le
focus clavier.

Dans une DAW, la fenêtre du plug-in ne reçoit le clavier que lorsqu'elle a le focus. Un clic sur une commande ne prend
pas le focus par défaut (les touches restent à la DAW) ; un utilisateur de lecteur d'écran va dans la fenêtre du
plug-in avec les commandes de la DAW ou du lecteur d'écran. Activez `UIHost.TakeFocusOnClick` si votre plug-in doit le
prendre au clic.

## Annonces

`UIHost.Announce(texte)` demande aux lecteurs d'écran de prononcer un message sans déplacer le focus : preset chargé,
analyse terminée, erreur. `AnnouncementPriority.Important` interrompt la lecture en cours. `StatusBar.ShowMessage`
annonce ses messages (désactivable avec `AnnounceMessages = false`). Les annonces utilisent les notifications d'UI
Automation (Windows 10 1709 et suivants).

## Contraste élevé

Quand Windows est en contraste élevé, les hôtes dessinent avec un thème construit à partir des couleurs du jeu de
l'utilisateur : fond, texte, surbrillance, texte désactivé, liens (`UIHost.CreateSystemHighContrastTheme`). Les textes
ne sont plus atténués, les cadres et les anneaux de focus sont plus épais et il n'y a plus d'ombres. L'hôte suit les
changements du réglage tant qu'il est ouvert.

- `UIHost.FollowSystemHighContrast` (`true` par défaut) désactive ce comportement pour un hôte.
- `UIHost.EffectiveTheme` est le thème utilisé pour dessiner ; `UIHost.IsHighContrastActive` indique si le jeu de
  couleurs s'applique.
- `Theme.HighContrast` et `Theme.CreateHighContrast(Palette.HighContrast(...))` donnent le même aspect à la demande.

Les habillages bitmap et film-strip ne sont pas recolorés. Si votre éditeur repose sur des images, vérifiez-le en
contraste élevé et proposez une variante vectorielle là où les images se lisent mal.

## Décrire une commande personnalisée

Une commande dérivée de `ValueControl` est déjà un curseur. Pour une autre commande personnalisée, redéfinissez
`OnCreateAutomationPeer` et renvoyez un pair (peer) qui donne le rôle et implémente les motifs (patterns). Appelez
`NotifyAutomationStateChanged()` quand l'état lu par les lecteurs d'écran change.

```csharp
public sealed class PanPad : Control
{
    public double Pan { get; private set; }

    protected override AutomationPeer? OnCreateAutomationPeer() => new PanPadPeer(this);

    private void SetPan(double pan)
    {
        Pan = pan;
        InvalidateVisual();
        NotifyAutomationStateChanged();
    }

    private sealed class PanPadPeer(PanPad owner) : ElementAutomationPeer<PanPad>(owner), IRangeValuePattern
    {
        private double _reported = double.NaN;

        public override AutomationControlType ControlType => AutomationControlType.Slider;
        protected override string? GetNameCore() => "Panoramique";

        public double Value => Owner.Pan * 100;
        public double Minimum => -100;
        public double Maximum => 100;
        public double SmallChange => 1;
        public double LargeChange => 10;
        public bool IsReadOnly => !Owner.IsEffectivelyEnabled;
        public void SetValue(double value) => Owner.SetPan(Math.Clamp(value, -100, 100) / 100);

        protected override void OnOwnerStateChanged()
        {
            RaisePropertyChanged(AutomationProperty.RangeValue, _reported, Value);
            _reported = Value;
        }
    }
}
```

Les éléments qu'une commande dessine sans être des éléments (cellules, points, touches) peuvent être exposés comme
enfants : dérivez de `ItemAutomationPeer` et ajoutez-les dans `CollectChildren` ; `OptionsAutomationPeer` en donne un
exemple. Renvoyez `null` depuis `OnCreateAutomationPeer` pour rendre un élément transparent.

## Tester

- **Narrateur** (intégré à Windows) : Ctrl+Win+Entrée le démarre et l'arrête. Parcourez l'éditeur avec Tab et écoutez ;
  Verr. maj + flèches parcourent tous les éléments.
- **NVDA** (gratuit, nvaccess.org) : le lecteur d'écran le plus utilisé avec JAWS. NVDA+Tab lit l'élément qui a le
  focus, NVDA+pavé numérique navigue par objet.
- **Accessibility Insights for Windows** (gratuit, Microsoft) : affiche l'arbre UI Automation, les propriétés et les
  motifs de l'élément sous la souris ou qui a le focus, et lance des vérifications automatiques (FastPass).
- `Inspect.exe` (SDK Windows) affiche le même arbre.

Vérifiez que chaque commande a un nom, que les valeurs sont lues avec leurs unités et que tout l'éditeur s'utilise
sans la souris.

## Limites

- Les zones de texte exposent leur texte comme une valeur : les lecteurs d'écran le lisent, mais la revue caractère
  par caractère et la position du curseur (motif texte d'UI Automation) ne sont pas encore disponibles.
- Les commandes composites (`PresetBar`, `ABCompareBar`, `XYPad`, `ParametricEqEditor`, `EnvelopeEditor`,
  `StepSequencer`, `PianoKeyboard`, graphiques et indicateurs) sont chacune un seul élément, nommé par son libellé ou
  son info-bulle. Donnez-leur un `AutomationName`, ou un pair à vous comme ci-dessus.
- Les menus natifs (`UIHost.UseNativeMenus`) sont des menus Windows : Windows les lit lui-même.
