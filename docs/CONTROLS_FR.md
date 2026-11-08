<!-- 2026/11/08 12:00:00 UTC - PluginUI -->

# Catalogue des éléments

*English version: [CONTROLS.md](CONTROLS.md)*

Tous les types sont dans l'espace de noms `PluginUI`. Les contrôles **à valeur** dérivent de `ValueControl` : liaison à un `IParameter` (`Parameter = …`), geste VST3 complet, réinitialisation par double-clic ou Ctrl+clic (`ResetOnDoubleClick`, `ResetOnControlClick`), saisie de la valeur au clavier pour `Knob` et `Slider` (double-clic sur le texte de la valeur, Alt+clic, double-clic droit ou Entrée, au choix par `TextEntryTriggers` ; nombres toujours avec le point décimal), molette, flèches, Maj pour le réglage fin, bulle de valeur (`ShowValueBubble`), habillage (`Skin`) et style (`Style`). La colonne « Équivalent » indique la classe VSTGUI ou iPlug2 correspondante.

Versions de référence des équivalents : **VSTGUI 4.15.1** (SDK VST 3 3.8.1) et **iPlug2** `master` du 18 octobre 2026 (commit `39ea73a`, après le tag `v1.0.0-beta`).

## Rotatifs

| Élément | Points clés | Équivalent |
|---|---|---|
| `Knob` | Styles `Arc` / `Classic` / `Minimal`, mode bipolaire (`IsBipolar`, `BipolarCenter`), arc de modulation (`ModulationAmount`), graduations, glisser vertical/horizontal, saisie au clavier (double-clic sur la valeur, Alt+clic…), film-strip et SVG via `Skin` | `CKnob`, `CAnimKnob`, `IVKnobControl`, `IBKnobControl`, `ISVGKnobControl` |
| `EndlessKnob` | Encodeur sans fin : événement `Turned(pas)`, pilotage optionnel d'un paramètre par `StepSize`, `Wrap` | — |

## Linéaires

| Élément | Points clés | Équivalent |
|---|---|---|
| `Slider` | Vertical ou horizontal, bipolaire, poignées `Cap`/`Round`/`Line`/`None`, `JumpToClick`, graduations, libellé latéral, `SliderHandleSkin` (poignée bitmap) | `CSlider`, `IVSliderControl`, `IBSliderControl`, `ISVGSliderControl`, `CHorizontalSlider`, `CVerticalSlider` |
| `RangeSlider` | Deux bornes (`Low`/`High`, `LowParameter`/`HighParameter`), écart minimal, glisser de la bande | `IVRangeSliderControl` |
| `MultiSlider` | N barres, dessin à la souris (interpolé, Ctrl : ligne droite), bipolaire, pas, index en surbrillance, `BindParameters` | `IVMultiSliderControl` |
| `XYPad` | Deux paramètres édités dans des gestes simultanés, grille, réticule | `CXYPad`, `IVXYPadControl` |
| `ScrollBar` | `Minimum`/`Maximum`/`ViewportSize`/`Value`, flèches, pages avec répétition, masquage automatique | `CScrollbar` |
| `EnvelopeEditor` | ADSR à points déplaçables, quatre paramètres optionnels, axe temporel en racine carrée | — (bonus) |
| `CurveEditor` | Enveloppe / LFO multi-segments (MSEG) : points déplaçables, ajout et suppression par double-clic, courbure par segment, grille magnétique, curseur de lecture, `Evaluate` exact pour le moteur audio | — (bonus) |

## Boutons et sélecteurs

| Élément | Points clés | Équivalent |
|---|---|---|
| `Button` | Texte et/ou icône (bitmap, SVG, `IconRenderer`), apparences `Normal`/`Accent`/`Flat`/`Outline`, répétition tant que pressé, paramètre momentané | `CKickButton`, `CTextButton`, `IVButtonControl`, `IBButtonControl` (+ `ImageStateSkin`), `ISVGButtonControl` (+ `SvgStateSkin`) |
| `ToggleButton` | `OnText`/`OffText`, LED, couleurs allumé et éteint (`OffColor` : carré d'activation coloré), icône (`Glyph` : `Wave`) ; avec un `FilmStripSkin`, équivalent de `CMovieButton` | `COnOffButton`, `CMovieButton`, `IVToggleControl`, `ITextToggleControl`, `ISVGToggleControl` |
| `CheckBox` | Case animée + texte | `CCheckBox` |
| `Switch` | N positions, vertical/horizontal, sélection directe ou cyclique, film-strip par index d'état | `CHorizontalSwitch`, `IVSwitchControl`, `CVerticalSwitch`, `IBSwitchControl` (+ `FilmStripSkin`), `ISVGSwitchControl` |
| `SlideSwitch` | Interrupteur « pilule » animé | `IVSlideSwitchControl` |
| `SegmentedControl` | Segments en ligne ou colonne, surbrillance glissante | `CSegmentButton`, `IVTabSwitchControl` |
| `RadioGroup` | Liste de boutons radio | `IVRadioButtonControl` |
| `RockerSwitch` | Momentané −1/0/+1, répétition, pas d'un paramètre | `CRockerSwitch` |
| `MultiToggle` | Rangée de N bascules (séquenceur de portes), dessin par glisser, position de lecture | `IVMultiToggleControl` |
| `StepSequencer` | Valeurs par pas (dessin interpolé) + portes, longueur active, groupes de temps, pas joué, liaison à un paramètre par pas (un geste par paramètre touché) | — (bonus) |
| `ModulationMatrix`, `ModulationSlot`, `ModulationTarget` | Matrice de modulation : une ligne par emplacement avec marche / arrêt, menus de source et de destination (destinations groupées en sous-menus), intensité bipolaire glissée à la souris (Maj fin, double-clic 0), suppression ; les sources déposées depuis un `ModulationSourceButton` ajoutent un emplacement ou remplacent une source ; intensité et marche / arrêt liables à des paramètres ; clavier (flèches par cellule, Entrée / Espace, Page préc. / suiv. ou + / −, Inser, Suppr) | — (bonus) |
| `RoutingMatrix` | Grille de routage entrées × sorties (routeur de canaux, connecteur de broches, baie de brassage) : clic ou glisser sur les croisements, `RoutingMode` Free / OneInputPerOutput / OneOutputPerInput, libellés de colonnes tournés quand ils sont étroits, croisements liables à des paramètres (`ParameterOf`) ; flèches et Espace ; une case à cocher par croisement pour les lecteurs d'écran | — (bonus) |
| `NodeEditor`, `NodeEditorNode`, `NodeEditorPort`, `NodeEditorCable` | Éditeur de graphe modulaire (hôte modulaire, baie de brassage, flux du signal) : modules à en-tête titré, entrées à gauche et sorties à droite (couleurs par type, nombre de canaux), câbles en courbes (en pointillés pour le retour d'une boucle, avec une étiquette), badges, modules estompés ou en échec, indicateurs de niveau en direct ; câbles tirés depuis l'un ou l'autre bout, bouts écartés pour les déplacer ou les enlever, seuls les connecteurs compatibles s'allument (`CanConnect`) ; déplacements sur une grille, sélection au rectangle et Ctrl+clic, zoom autour de la souris, déplacement de la vue au bouton du milieu, `ZoomToFit` ; chaque modification est une demande à l'application (`CableRequested`, `CableRemoveRequested`, `DeleteRequested`, `NodeOpened`, `ContextMenuOpening`) ; clavier (Ctrl+flèches pour sélectionner, flèches pour déplacer, Entrée, Suppr, Origine, + / −, Maj+F10) ; les lecteurs d'écran annoncent le module sélectionné et ses câbles | — (bonus) |
| `ModulationSourceButton` | Pastille de source de modulation à glisser sur un contrôle qui accepte la modulation (`AcceptsModulation`, événement `ModulationDropped`) | — (bonus, façon Serum / Vital) |

## Menus

| Élément | Points clés | Équivalent |
|---|---|---|
| `DropDown` | Éléments du paramètre ou liste explicite, sous-menus par séparateur (`"Groupe/Élément"`), molette et flèches, éléments non applicables masqués (`IsItemAvailable`) | `COptionMenu`, `IVMenuButtonControl` |
| `MenuButton` | Menu arbitraire (`MenuItem`), styles hamburger / points / texte + chevron | `IVMenuButtonControl` |
| `ToolBar` | Barre d'outils de boutons construits à partir de `Command` (actif, coché, info-bulle avec le raccourci), 18 icônes vectorielles (`ToolGlyph`), séparateurs, descriptions pour une barre d'état | — (rangées de `COnOffButton` / `IVButtonControl`) |
| `StatusBar` | Champs (fixes, ajustés, étirés ; textes calculés), descriptions de l'entrée de menu ou du bouton survolé, messages temporaires | — |
| `MenuBar` | Barre de menus d'application : clic, Alt+lettre, Alt seul / F10, flèches entre menus, lettres soulignées `&`, raccourcis de toutes les entrées, entrées construites à partir de `Command` (texte, raccourci, états actif et coché). Voir [DESKTOP_FR.md](DESKTOP_FR.md) | — (barre de menus native des enveloppes autonomes) |
| `MenuPopup` | Menu dessiné : coches, raccourcis, séparateurs, sous-menus, clavier, saisie rapide, défilement | `IPopupMenuControl` |
| `NativeMenu` | Menu Win32 natif (`UIHost.UseNativeMenus`) | `COptionMenu` (natif) |
| Menu contextuel du DAW | Clic droit sur un contrôle lié : menu de l'hôte VST3 (`IComponentHandler3`), entrées propres ajoutables | intégré à VSTGUI et iPlug2 |

## Texte

| Élément | Points clés | Équivalent |
|---|---|---|
| `Label` | Une ou plusieurs lignes, alignements, `TextSource` rafraîchi à chaque image (texte temps réel) | `CTextLabel`, `CMultiLineTextLabel`, `ITextControl`, `IRTTextControl`, `IVLabelControl`, `IMultiLineTextControl` |
| `ValueDisplay` | Valeur du paramètre, saisie par double-clic ou Entrée, glisser optionnel | `CParamDisplay`, `ICaptionControl` |
| `NumberBox` | Glisser, molette, saisie, boutons −/+ qui se répètent et accélèrent quand on les maintient, pas dans les unités du paramètre (`PlainStep`), `ButtonLayout` sur les côtés ou empilés | `IVNumberBoxControl` |
| `NumericUpDown` | Boîte numérique : un `NumberBox` avec ▲ / ▼ empilés à droite ; une position d'un paramètre à crans par clic, Haut / Bas et Page préc. / suiv. | — (bonus) |
| `TextBox` | Saisie une ligne : sélection, presse-papiers, mots (Ctrl+flèches), filtre, longueur max., lecture seule, style recherche | `CTextEdit`, `CSearchTextEdit`, `ITextEntryControl` |
| `UIHost.BeginTextEdit` | Saisie sur place au-dessus de n'importe quel rectangle | `IEditableTextControl` |
| `SegmentDisplay` | Afficheur 7 segments vectoriel, segments éteints visibles, point décimal, inclinaison, lueur | `CSpecialDigit` |
| `Hyperlink` | Lien cliquable (navigateur par défaut) | `IURLControl` |
| `BitmapText`, `BitmapFont` | Texte en police bitmap à chasse fixe (LCD, VFD, pixel art), agrandissement entier sans lissage, segments éteints visibles, texte temps réel | `IBTextControl` |
| Bulle de valeur | `ShowValueBubble` sur tout `ValueControl` | `IBubbleControl` |

## Mesure et visualisation

| Élément | Points clés | Équivalent |
|---|---|---|
| `LevelMeter` | 1 à 64 canaux, dégradé / segments / plein, crête + RMS, maintien de crête, indicateur d'écrêtage, échelle en dB, mode réduction de gain, sources `PeakValue` sans verrou, film-strip par `Skin` (une image par niveau, dessinée par canal) | `CVuMeter`, `IVMeterControl`, `IVLEDMeterControl`, `IVPeakAvgMeterControl`, `IBMeterControl` (+ `FilmStripSkin`) |
| `Led` | Luminosité, lueur, formes rondes ou carrées, source par fonction | `ILEDControl` |
| `Oscilloscope` | Lecture d'un `AudioRingBuffer`, déclenchement sur front montant, gain, remplissage, lueur | `IVScopeControl`, `IVDisplayControl` |
| `SpectrumAnalyzer` | FFT (`Dsp.Fft`), axe log 20 Hz–20 kHz, compensation de pente, lissage attaque/relâchement, maintien de crête, mode barres par fraction d'octave | `IVSpectrumAnalyzerControl`, `IVBarGraphSpectrumAnalyzerControl` |
| `Plot` | Fonctions ou listes de points, X logarithmique, remplissage, graduations automatiques (courbe d'égaliseur) | `IVPlotControl` |
| `ProgressBar` | Déterminée (avec pourcentage) ou indéterminée | — |
| `TimelineRuler`, `TimelineMarker` | Règle temporelle en mesures et temps ou en minutes et secondes (`Tempo`) : zoom (Ctrl+molette, + / −) et défilement, curseur de lecture lu côté audio, clic pour demander une position (`PositionRequested`), bande de boucle (tracer, déplacer, redimensionner, double-clic marche / arrêt ; `LoopStartParameter` / `LoopEndParameter` en temps), marqueurs ; positions calées sur la grille visible (Alt = libre) | — (bonus) |
| `FpsCounter` | Images par seconde et durée d'image (débogage) | `IFPSDisplayControl` |
| `HistoryGraph` | Historique défilant d'une valeur (réduction de gain, enveloppe, LFO) : ligne, rempli ou barres, source `PeakValue` sans verrou ou fonction interrogée | `IVDisplayControl` |
| `ParametricEqEditor`, `EqBand` | Égaliseur paramétrique : courbe exacte des biquads RBJ, nœuds déplaçables (fréquence et gain en gestes parallèles), Q à la molette, menu de forme par bande, ajout et suppression, spectre en fond | — (bonus) |
| `FrequencyResponseView`, `ResponseCurve`, `ResponseNode` | Affichage de réponse pour tout filtre : courbes fournies par l'appelant, axe des fréquences logarithmique ou linéaire, axe gauche fixe et axe droit à échelle automatique (phase en multiples de π, retards), spectre d'entrée analysé (recouvrement 50 %, lissage en relâchement, dB en 20·log10 ou 6·log2) et spectre façonné par une réponse sans seconde FFT, nœuds liés aux paramètres, nœuds de bande définis par deux bords avec leurs propres poignées ; au choix, spectres à l'échelle en décibels des courbes (`SpectrumFollowsLeftAxis`, ancrés à un niveau fixe par `SpectrumAnchor`) et axe droit centré sur zéro (`RightAxisCentered`) ; aucune analyse quand l'éditeur est caché | — (bonus) |
| `CrossoverEditor`, `CrossoverSplit`, `CrossoverBand` | Séparateur multibande (2 à 8 bandes) : réponses Linkwitz-Riley des bandes (pentes multiples de 12 dB par octave, −6 dB à chaque séparation), poignées de séparation glissées le long de l'axe des fréquences avec un écart minimal, lignes de gain des bandes, pastilles solo et muet, spectre en fond ; chaque fréquence de séparation, gain, solo et muet peut être lié à un paramètre (gestes d'édition) ; clavier : Page préc. / Page suiv. et Ctrl+flèches sélectionnent, les flèches modifient (Maj pour le pas fin), S, M, Suppr | — (bonus) |
| `CompressorCurve` | Courbe de transfert (seuil, ratio, genou doux), seuil et ratio réglables à la souris, niveau d'entrée en direct | — (bonus) |
| `LoudnessMeter` + `Dsp.LoudnessAnalyzer` | Sonie EBU R128 / BS.1770-4 : momentanée, court terme, intégrée (avec seuils), plage LRA, crête ; cible et tolérance | — (bonus) |
| `Tuner` + `Dsp.PitchDetector` | Accordeur chromatique : détection de hauteur YIN sur un `AudioRingBuffer` (ou `FrequencySource` mesurée par le plug-in), note en dièses ou en bémols, écart en cents sur une aiguille ou une rangée de diodes (`TunerStyle`), vert dans `InTuneCents`, fréquence et référence (la4, `ReferenceParameter` liable), note maintenue puis relâchée ; les lecteurs d'écran n'entendent que les changements de note | — (bonus) |
| `Goniometer`, `CorrelationMeter` | Image stéréo (Lissajous M/S) et corrélation de phase −1…+1 | — (bonus) |
| `Spectrogram` | Spectrogramme défilant, axe de fréquence logarithmique, carte de couleurs | — (bonus) |
| `WaveformView` + `Dsp.WavReader` | Forme d'onde multicanal : zoom et défilement, marqueurs début / fin / boucle liables à des paramètres, sélection, curseur de lecture, dépôt de fichier WAV | — (bonus, échantillonneur) |
| `WavetableView` | Affichage de table d'ondes et réglage de position (un `ValueControl`) : trames empilées en 3D ou la trame courante seule (`WavetableDisplay`), trame interpolée à la position, glisser pour parcourir, `SetFrames`, table de démonstration `CreateMorph` | — (bonus) |

## MIDI

| Élément | Points clés | Équivalent |
|---|---|---|
| `PitchWheel`, `ModWheel` (`WheelControl`) | Molettes de pitch (retour au centre animé dans le même geste) et de modulation | `IWheelControl` |
| `PianoKeyboard` | Étendue configurable, glissando, vélocité selon la hauteur du clic, `NoteOn`/`NoteOff`, `SetNoteState` utilisable depuis n'importe quel thread, notes du processeur par `ActivitySource` (`NoteActivity`, sans verrou), jeu au clavier d'ordinateur ; dans un plugin NPlug, `ConnectKeyboard` envoie les notes au processeur | `KeyboardView`, `IVKeyboardControl` |
| `PatternEditor`, `PatternNote` | Mini piano roll pour des motifs de quelques mesures : clavier et règle des mesures, notes ajoutées, déplacées et allongées sur la grille (Alt = libre), sélection au rectangle et par Ctrl+clic, couloir de vélocité, curseur de lecture (`PlayPositionSource`), défilement des hauteurs à la molette ; édition au clavier (flèches, Maj+flèches pour la durée et l'octave, Ctrl+flèches pour la sélection et la vélocité, Inser, Ctrl+D, Suppr) ; `PatternChanged` une fois par modification, `PreviewNoteOn` / `PreviewNoteOff` pour entendre les notes touchées | — (bonus) |
| `DrumPads`, `DrumPad` | Grille de pads de batterie (4 × 4 par défaut, premier pad en bas à gauche), vélocité selon la hauteur du clic, rangées du clavier d'ordinateur 1234 / QWER / ASDF / ZXCV, flèches et Espace, `PadPressed` / `PadReleased`, pads allumés par `ActivitySource` ou par `Flash` depuis le thread audio ; chaque pad est un bouton pour les lecteurs d'écran | — (bonus) |

## Conteneurs et mise en page

| Élément | Points clés | Équivalent |
|---|---|---|
| `Canvas` | Positionnement absolu (`SetBounds`) : la racine habituelle d'un éditeur | `CViewContainer` |
| `StackPanel`, `WrapPanel`, `DockPanel`, `UniformGrid` | Empilement, retour à la ligne, ancrage, grille régulière | `CRowColumnView` |
| `Grid` | Lignes/colonnes `Auto` / pixels / étoiles, fusion, espacements (`new Grid("Auto,*", "120,*")`) | Flexbox iPlug2 |
| `Border`, `Panel` | Fond (couleur ou dégradé, équivalent de `CGradientView`), bordure, arrondi, ombre portée, marge interne | `CShadowViewContainer`, `IVPanelControl`, `IPanelControl` |
| `GroupBox` | Cadre titré, barre d'accent, repliable | `IVGroupControl` |
| `TabView` | Onglets en haut ou à gauche (ils se resserrent pour tenir dans la bande), indicateur animé, pages désactivables, Ctrl+Tab ; seule la page visible est attachée | `CTabView`, `IVTabbedPagesControl` |
| `ViewSwitcher` | Affiche un seul enfant à la fois, choisi par `SelectedIndex` ou par un paramètre lié (discret : indice du pas ; continu : valeur répartie sur les enfants) ; les vues masquées gardent leur état | `UIViewSwitchContainer` |
| `ScrollViewer` | Défilement vertical et horizontal, barres fines superposées, molette (Maj : horizontal), `ScrollIntoView` | `CScrollView` |
| `SplitView` | Deux volets et séparateur déplaçable | `CSplitView` |
| `ListBox` | Liste virtualisée, sélection simple ou étendue, clavier, recherche par frappe, réordonnancement par glisser | `CListControl`, `CStringList` |
| `TreeView`, `TreeNode` | Arborescence virtualisée : flèches de dépliage, retrait, texte de détail et pastille de couleur par nœud, un nœud sélectionné, clavier (flèches, Gauche / Droite pour replier et déplier, Entrée, saisie rapide), filtre de texte qui garde les ancêtres des nœuds trouvés, `Expanding` pour des enfants chargés à la demande ; un arbre d'éléments pour les lecteurs d'écran | — (bonus) |
| `DataTable` | Colonnes fixes ou étoiles, tri, redimensionnement, ajustement automatique, lignes virtualisées, édition de cellules, colonnes affichées ou masquées depuis le menu de l'en-tête, disposition enregistrée (`SaveLayout` / `RestoreLayout`), menu contextuel des lignes (`RowContextMenu`, touche Menu), cellules dessinées (`CellRenderer`), lecteurs d'écran | `CDataBrowser` |
| `CornerResizer` | Poignée de redimensionnement de l'éditeur (double-clic : 100 %) | `ICornerResizerControl` |
| `Separator`, `Spacer` | Ligne de séparation, espace fixe ou extensible | — |
| `LayeredPanel` | Calques superposés pleine taille, opacité composée par calque, `BringToFront` / `SendToBack` | `CLayeredViewContainer` |

## Dialogues et sélecteurs

| Élément | Points clés | Équivalent |
|---|---|---|
| `ColorSwatch`, `ColorPicker`, `ColorPickerPopup` | Carré TS/V, teinte, alpha, saisie hexadécimale, couleurs récentes, Échap restaure | `CColorChooser`, `IColorPickerControl`, `IVColorSwatchControl` |
| `FilePathBox` | Chemin abrégé au milieu, dialogue natif (fichier, dossier, enregistrement), dépôt de fichiers | `CNewFileSelector`, `IDirBrowseControlBase` |
| `FileDialogs` | `IFileOpenDialog` / `IFileSaveDialog` | — |
| `ABCompareBar`, `ParameterSnapshot` | Comparaison A/B de réglages complets, copie vers l'autre emplacement, restauration par gestes | — (bonus) |
| `PresetBar`, `IPresetProvider`, `PresetList` | Précédent/suivant, menu par banque, Enregistrer / Enregistrer sous / Supprimer, Ctrl+S | `IVBakedPresetManagerControl`, `IVDiskPresetManagerControl` |
| `PresetBrowser` | Navigateur de presets sur un `IPresetProvider` : zone de recherche (chaque mot dans le nom ou la banque), arbre des catégories (tous les presets, favoris, banques imbriquées par « / », nombres), liste avec banque, étoile des favoris (clic ou Ctrl+D) et preset chargé mis en évidence ; double-clic ou Entrée charge, `LoadOnSelect` charge à la sélection ; Ctrl+F va à la recherche, Bas à la liste | — (bonus) |
| `Dialog` | Dialogue modal, boutons par défaut et d'annulation, `Dialog.ShowMessage` | — |
| `AboutBox`, `SplashScreen` | Fenêtre « À propos », écran d'accueil qui s'efface en fondu | `CSplashScreen`, `IAboutBoxControl`, `CAnimationSplashScreen` |

## Images et intégration

| Élément | Points clés | Équivalent |
|---|---|---|
| `ImageView` | Image ajustée (`ImageFit`) ou film-strip animé (boucle, aller-retour) | `CMovieBitmap`, `CAutoAnimation`, `IBitmapControl` |
| `ToastStack`, `Toast` | Zone de notifications au-dessus de l'éditeur : notifications dans un coin (`ToastCorner`), types Info / Success / Warning / Error, bouton d'action (« Annuler »), × pour fermer, minuterie suspendue sous la souris, apparition et effacement en fondu, les plus anciennes fermées au-delà de `MaxVisible` ; `Post` depuis n'importe quel thread ; annoncées aux lecteurs d'écran | — (bonus) |
| `SvgView` | SVG natif, ajustement, rotation | `ISVGControl` |
| `NativeWindowHost` | Incruste une fenêtre Win32 tierce (vidéo, WebView2 créée par l'application), ou fournit une fenêtre conteneur vide pour un contenu qui a besoin d'un parent (`ProvidesContainer` : éditeurs de plug-ins VST3) ; masquée sous les popups sur demande (`HideUnderPopups`) | `CExternalView`, `IPlatformViewControl`, `CExternalControl` |

## Habillages (`Skins`)

| Habillage | Usage |
|---|---|
| `FilmStripSkin` | Film-strip : image choisie selon la valeur ou l'index d'état (`CMultiFrameBitmap`) |
| `ImageStateSkin` | Une image par état : normal, actif, survol, enfoncé, désactivé |
| `SvgRotarySkin` | Rotor SVG tourné selon la valeur, fond et reflet fixes |
| `ImageRotarySkin` | Rotor bitmap tourné selon la valeur, fond et reflet fixes (`IBKnobRotaterControl`) |
| `SvgStateSkin` | Un SVG par état ou par plage de valeur |
| `NineSliceSkin` | Cadre bitmap étirable sans déformation des coins (`CNinePartTiledBitmap`) |
| `SliderHandleSkin` | Poignée (et piste) bitmap pour `Slider` |
| `DelegateSkin` | Dessin libre par une fonction (`ILambdaControl`) |

## Services transverses

Apprentissage MIDI (`UIHost.MidiLearn`, `IMidiLearnService`, `MidiLearnService` : entrées « MIDI Learn » / « Oublier l'affectation » dans le menu contextuel, y compris celui du DAW ; contour du contrôle en attente), langue de l'interface (`UIHost.Language`, tables `UIStrings` anglais / français, extensibles), nombres indépendants des paramètres régionaux (`InvariantNumber`), infobulles (`ToolTip`), menus contextuels (`ContextMenu`), glisser-déposer interne (`UIHost.BeginDrag`) et dépôt de fichiers (`AllowFileDrop`), animations (`UIHost.Animator`, courbes `Easing`), tickers par image, navigation au clavier (Tab, anneau de focus), presse-papiers, thèmes clair et sombre, mode d'édition en direct, rendu PNG hors écran, DSP d'affichage (`Dsp.Fft`, `AudioRingBuffer`, `PeakValue`, `LevelBallistics`).

## Non inclus (volontairement)

Vue web WebView2 intégrée (`IWebViewControl` ; utilisez `NativeWindowHost`), shaders GPU (`IShaderControl`), vue OpenGL (`COpenGLView`), éditeur de code complet (éditeur de texte de VSTGUI), sélecteurs de police et de date (`CFontChooser`). Multi-touch (`IMultiTouchControlBase`) : PluginUI gère la souris, et le toucher à un doigt grâce à l'émulation de la souris par Windows.
