// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Globalization;
using PluginUI.Interop;

namespace PluginUI;

/// <summary>
/// Texts that PluginUI itself displays (dialog buttons, preset menu, placeholders, tooltips, live-edit help).
/// One instance per language; select it with <see cref="UIHost.Language"/> (or <see cref="UIHost.DefaultLanguage"/>
/// for every editor), or assign a custom table to <see cref="UIHost.Strings"/>.
/// </summary>
/// <remarks>
/// <para>English (<see cref="English"/>) and French (<see cref="French"/>) are built in. Register other languages
/// with <see cref="Register"/>: start from <see cref="English"/> with a <c>with</c> expression so that texts added in
/// later versions fall back to English instead of being empty.</para>
/// <para>The language only changes texts. Numbers are always formatted and parsed with a decimal point, whatever the
/// language and the regional settings of the machine (see <see cref="InvariantNumber"/>).</para>
/// <para>Texts that belong to your plugin (labels, parameter value names such as "Off" / "On", preset bank names) are
/// not part of this table: they are data of your plugin and stay as you define them.</para>
/// </remarks>
/// <example>
/// <code>
/// // Follow the Windows display language (default), or force one:
/// UIHost.DefaultLanguage = UILanguages.French;
///
/// // Add German, starting from English for any missing text:
/// UIStrings.Register("de", UIStrings.English with { Cancel = "Abbrechen", Save = "Speichern", Delete = "Löschen" });
/// </code>
/// </example>
public sealed record UIStrings
{
    private static readonly Lock RegistryLock = new();
    private static readonly Dictionary<string, UIStrings> Registry = new(StringComparer.OrdinalIgnoreCase);

    static UIStrings()
    {
        Registry["en"] = English;
        Registry["fr"] = French;
    }

    /// <summary>Built-in English texts (also the fallback for unknown languages).</summary>
    public static UIStrings English { get; } = new();

    /// <summary>Built-in French texts.</summary>
    public static UIStrings French { get; } = new()
    {
        LanguageCode = "fr",
        Ok = "OK",
        Cancel = "Annuler",
        Yes = "Oui",
        No = "Non",
        Save = "Enregistrer",
        SaveAs = "Enregistrer sous…",
        Delete = "Supprimer",
        PresetPlaceholder = "Init",
        FileDialogTitle = "Sélectionner",
        NoFile = "Aucun fichier",
        DragToResize = "Faire glisser pour redimensionner",
        VersionFormat = "Version {0}",
        Integrated = "Intégrée",
        ShortTerm = "Court terme",
        Momentary = "Momentanée",
        LoudnessRange = "Plage",
        Peak = "Crête",
        MidiLearn = "Apprentissage MIDI",
        MidiLearnCancel = "Annuler l'apprentissage MIDI",
        MidiForget = "Oublier l'affectation MIDI ({0})",
        MidiLearnWaiting = "Bougez une commande MIDI…",
        CopyAToB = "Copier A → B",
        CopyBToA = "Copier B → A",
        DropAudioFile = "Déposez un fichier audio",
        DeleteBand = "Supprimer la bande",
        BandBell = "Cloche",
        BandLowShelf = "Plateau grave",
        BandHighShelf = "Plateau aigu",
        BandLowCut = "Coupe-bas",
        BandHighCut = "Coupe-haut",
        BandNotch = "Réjection",
        BandBandPass = "Passe-bande",
        Bypass = "Contourner",
        Solo = "Solo",
        Mute = "Muet",
        BandFormat = "Bande {0}",
        CrossoverFormat = "Séparation {0}",
        AutomationCrossoverEditor = "séparateur de bandes",
        AutomationPatternEditor = "éditeur de motif",
        PatternNoteFormat = "{0}, mesure {1}, temps {2}, durée {3} temps, vélocité {4} %",
        NotesSelectedFormat = "{0} notes sur {1} sélectionnées",
        NoteCountFormat = "{0} notes, aucune sélectionnée",
        AutomationDrumPads = "pads de batterie",
        PadFormat = "Pad {0}",
        AllPresets = "Tous les presets",
        Favorites = "Favoris",
        Favorite = "favori",
        CurrentPreset = "chargé",
        SearchPresets = "Rechercher un preset",
        PresetCategories = "Catégories de presets",
        Presets = "Presets",
        NoPresetFound = "Aucun preset trouvé",
        ModulationSourceHeader = "Source",
        ModulationTargetHeader = "Destination",
        ModulationAmountHeader = "Intensité",
        AddModulation = "Ajouter une modulation",
        ModulationNone = "(aucune)",
        ModulationOn = "active",
        ModulationOff = "désactivée",
        ModulationSlotFormat = "Emplacement {0}",
        ModulationSlotCountFormat = "{0} emplacements",
        AutomationModulationMatrix = "matrice de modulation",
        AutomationRoutingMatrix = "matrice de routage",
        KeyNames = KeyNameTables.French,
        Untitled = "Sans titre",
        SaveChangesQuestion = "Voulez-vous enregistrer les modifications de « {0} » ?",
        DontSave = "Ne pas enregistrer",
        UndoFormat = "&Annuler {0}",
        RedoFormat = "&Rétablir {0}",
        UndoPlain = "&Annuler",
        RedoPlain = "&Rétablir",
        MenuFile = "&Fichier",
        MenuEdit = "&Edition",
        EditCut = "Co&uper",
        EditCopy = "&Copier",
        EditPaste = "C&oller",
        EditSelectAll = "&Tout sélectionner",
        MenuView = "&Affichage",
        MenuHelp = "&Aide",
        MenuNew = "&Nouveau",
        MenuOpen = "&Ouvrir…",
        MenuSave = "&Enregistrer",
        MenuSaveAs = "Enregistrer &sous…",
        MenuExit = "&Quitter",
        MenuFullScreen = "&Plein écran",
        MenuAbout = "À &propos de {0}…",
        AllFiles = "Tous les fichiers",
        HintNew = "Crée un nouveau document",
        HintOpen = "Ouvre un document existant",
        HintSave = "Enregistre le document",
        HintSaveAs = "Enregistre le document sous un autre nom",
        HintUndo = "Annule la dernière modification",
        HintRedo = "Rétablit la dernière modification annulée",
        HintExit = "Quitte l'application",
        HintFullScreen = "Affiche la fenêtre sur tout l'écran (Échap pour en sortir)",
        StatusReady = "Prêt",
        AutomationKnob = "bouton rotatif",
        AutomationDialog = "boîte de dialogue",
        AutomationMenu = "Menu",
        AutomationInvalidEntry = "Saisie non valide",
        StatusSaved = "Enregistré : {0}",
        StatusOpened = "Ouvert : {0}",
        RecentFiles = "Fichiers récents",
        ClearRecentFiles = "Effacer la liste",
        NoRecentFiles = "(aucun)",
        FileNotFound = "Le fichier « {0} » est introuvable.",
        LiveEditHelp = "ÉDITION EN DIRECT — clic : sélection · Alt+clic : parent · glisser / poignées : déplacer / redimensionner · flèches : décaler (Ctrl : taille) · Ctrl+Z : annuler · Ctrl+C : copier le code · Échap : quitter",
        LiveEditHelpSave = "ÉDITION EN DIRECT — clic : sélection · glisser / poignées : déplacer / redimensionner · flèches : décaler (Ctrl : taille) · Ctrl+Z : annuler · Ctrl+S : enregistrer le design · Ctrl+C : copier le code · Échap : quitter",
    };

    /// <summary>Two-letter (or IETF) code of the language of this table, e.g. <c>"en"</c>.</summary>
    public string LanguageCode { get; init; } = "en";

    /// <summary>Dialog button "OK".</summary>
    public string Ok { get; init; } = "OK";

    /// <summary>Dialog button "Cancel".</summary>
    public string Cancel { get; init; } = "Cancel";

    /// <summary>Dialog button "Yes".</summary>
    public string Yes { get; init; } = "Yes";

    /// <summary>Dialog button "No".</summary>
    public string No { get; init; } = "No";

    /// <summary>Preset menu entry "Save" (overwrites the current preset).</summary>
    public string Save { get; init; } = "Save";

    /// <summary>Preset menu entry "Save As…" (asks for a new name).</summary>
    public string SaveAs { get; init; } = "Save As…";

    /// <summary>Preset menu entry "Delete".</summary>
    public string Delete { get; init; } = "Delete";

    /// <summary>Name shown by <see cref="PresetBar"/> when no preset is selected.</summary>
    public string PresetPlaceholder { get; init; } = "Init";

    /// <summary>Default title of the file dialogs opened by <see cref="FilePathBox"/>.</summary>
    public string FileDialogTitle { get; init; } = "Select";

    /// <summary>Text shown by an empty <see cref="FilePathBox"/>.</summary>
    public string NoFile { get; init; } = "No file";

    /// <summary>Tooltip of <see cref="CornerResizer"/>.</summary>
    public string DragToResize { get; init; } = "Drag to resize";

    /// <summary>Version line of <see cref="AboutBox"/>; <c>{0}</c> is replaced by the version.</summary>
    public string VersionFormat { get; init; } = "Version {0}";

    /// <summary>Help bar of the live editor (Ctrl+Shift+E).</summary>
    public string LiveEditHelp { get; init; } = "LIVE EDIT — click: select · Alt+click: parent · drag/handles: move/resize · arrows: nudge (Ctrl: size) · Ctrl+Z: undo · Ctrl+C: copy code · Esc: exit";

    /// <summary>Live edit help bar when the layout can be saved to a design file (Ctrl+S).</summary>
    public string LiveEditHelpSave { get; init; } = "LIVE EDIT — click: select · drag/handles: move/resize · arrows: nudge (Ctrl: size) · Ctrl+Z: undo · Ctrl+S: save the design · Ctrl+C: copy code · Esc: exit";

    /// <summary>Label of the integrated loudness in <see cref="LoudnessMeter"/>.</summary>
    public string Integrated { get; init; } = "Integrated";

    /// <summary>Label of the short-term loudness in <see cref="LoudnessMeter"/>.</summary>
    public string ShortTerm { get; init; } = "Short-term";

    /// <summary>Label of the momentary loudness in <see cref="LoudnessMeter"/>.</summary>
    public string Momentary { get; init; } = "Momentary";

    /// <summary>Label of the loudness range (LRA) in <see cref="LoudnessMeter"/>.</summary>
    public string LoudnessRange { get; init; } = "Range";

    /// <summary>Label of the sample peak in <see cref="LoudnessMeter"/>.</summary>
    public string Peak { get; init; } = "Peak";

    /// <summary>Context-menu entry that starts MIDI learn on a control.</summary>
    public string MidiLearn { get; init; } = "MIDI Learn";

    /// <summary>Context-menu entry that cancels a running MIDI learn.</summary>
    public string MidiLearnCancel { get; init; } = "Cancel MIDI Learn";

    /// <summary>Context-menu entry that removes a MIDI assignment; <c>{0}</c> is the assignment (e.g. "CC 74").</summary>
    public string MidiForget { get; init; } = "Forget MIDI assignment ({0})";

    /// <summary>Tooltip shown while a control waits for a MIDI controller.</summary>
    public string MidiLearnWaiting { get; init; } = "Move a MIDI control…";

    /// <summary>A/B compare: copy the A settings to B.</summary>
    public string CopyAToB { get; init; } = "Copy A → B";

    /// <summary>A/B compare: copy the B settings to A.</summary>
    public string CopyBToA { get; init; } = "Copy B → A";

    /// <summary>Hint of an empty <see cref="WaveformView"/> that accepts dropped files.</summary>
    public string DropAudioFile { get; init; } = "Drop an audio file";

    /// <summary>Equalizer: menu entry that deletes a band.</summary>
    public string DeleteBand { get; init; } = "Delete band";

    /// <summary>Equalizer band type: bell.</summary>
    public string BandBell { get; init; } = "Bell";

    /// <summary>Equalizer band type: low shelf.</summary>
    public string BandLowShelf { get; init; } = "Low shelf";

    /// <summary>Equalizer band type: high shelf.</summary>
    public string BandHighShelf { get; init; } = "High shelf";

    /// <summary>Equalizer band type: low cut.</summary>
    public string BandLowCut { get; init; } = "Low cut";

    /// <summary>Equalizer band type: high cut.</summary>
    public string BandHighCut { get; init; } = "High cut";

    /// <summary>Equalizer band type: notch.</summary>
    public string BandNotch { get; init; } = "Notch";

    /// <summary>Equalizer band type: band pass.</summary>
    public string BandBandPass { get; init; } = "Band pass";

    /// <summary>Menu entry that bypasses (disables) an element, e.g. an equalizer band.</summary>
    public string Bypass { get; init; } = "Bypass";

    /// <summary>Plays only this band or channel (crossover bands, mixers).</summary>
    public string Solo { get; init; } = "Solo";

    /// <summary>Silences this band or channel.</summary>
    public string Mute { get; init; } = "Mute";

    /// <summary>Default name of a band ({0}: its number from 1).</summary>
    public string BandFormat { get; init; } = "Band {0}";

    /// <summary>Name of a crossover frequency between two bands ({0}: its number from 1).</summary>
    public string CrossoverFormat { get; init; } = "Crossover {0}";

    /// <summary>Role of a <see cref="CrossoverEditor"/> spoken by screen readers.</summary>
    public string AutomationCrossoverEditor { get; init; } = "crossover editor";

    /// <summary>Role of a <see cref="PatternEditor"/> spoken by screen readers.</summary>
    public string AutomationPatternEditor { get; init; } = "pattern editor";

    /// <summary>
    /// A note of a <see cref="PatternEditor"/> for screen readers ({0}: pitch name, {1}: bar from 1, {2}: beat in the bar
    /// from 1, {3}: length in beats, {4}: velocity in percent).
    /// </summary>
    public string PatternNoteFormat { get; init; } = "{0}, bar {1}, beat {2}, length {3} beats, velocity {4} %";

    /// <summary>Several selected notes ({0}: selected, {1}: total).</summary>
    public string NotesSelectedFormat { get; init; } = "{0} of {1} notes selected";

    /// <summary>Notes of a pattern, none selected ({0}: total).</summary>
    public string NoteCountFormat { get; init; } = "{0} notes, none selected";

    /// <summary>Role of <see cref="DrumPads"/> spoken by screen readers.</summary>
    public string AutomationDrumPads { get; init; } = "drum pads";

    /// <summary>Default name of a drum pad ({0}: its number from 1).</summary>
    public string PadFormat { get; init; } = "Pad {0}";

    /// <summary>Category of a <see cref="PresetBrowser"/> with every preset.</summary>
    public string AllPresets { get; init; } = "All presets";

    /// <summary>Category of a <see cref="PresetBrowser"/> with the favorite presets.</summary>
    public string Favorites { get; init; } = "Favorites";

    /// <summary>State of a favorite preset, spoken by screen readers.</summary>
    public string Favorite { get; init; } = "favorite";

    /// <summary>State of the loaded preset, spoken by screen readers.</summary>
    public string CurrentPreset { get; init; } = "loaded";

    /// <summary>Placeholder of the search box of a <see cref="PresetBrowser"/>.</summary>
    public string SearchPresets { get; init; } = "Search presets";

    /// <summary>Name of the category tree of a <see cref="PresetBrowser"/>.</summary>
    public string PresetCategories { get; init; } = "Preset categories";

    /// <summary>Name of the preset list of a <see cref="PresetBrowser"/>.</summary>
    public string Presets { get; init; } = "Presets";

    /// <summary>Shown when no preset matches the search.</summary>
    public string NoPresetFound { get; init; } = "No preset found";

    /// <summary>Column of the sources of a <see cref="ModulationMatrix"/>.</summary>
    public string ModulationSourceHeader { get; init; } = "Source";

    /// <summary>Column of the targets of a <see cref="ModulationMatrix"/>.</summary>
    public string ModulationTargetHeader { get; init; } = "Target";

    /// <summary>Column of the amounts of a <see cref="ModulationMatrix"/>.</summary>
    public string ModulationAmountHeader { get; init; } = "Amount";

    /// <summary>Last row of a <see cref="ModulationMatrix"/>, which adds a slot.</summary>
    public string AddModulation { get; init; } = "Add modulation";

    /// <summary>Source or target not chosen yet.</summary>
    public string ModulationNone { get; init; } = "(none)";

    /// <summary>State of a modulation slot that modulates.</summary>
    public string ModulationOn { get; init; } = "on";

    /// <summary>State of a bypassed modulation slot.</summary>
    public string ModulationOff { get; init; } = "off";

    /// <summary>A slot of a <see cref="ModulationMatrix"/> ({0}: its number from 1).</summary>
    public string ModulationSlotFormat { get; init; } = "Slot {0}";

    /// <summary>Number of slots of a <see cref="ModulationMatrix"/> ({0}).</summary>
    public string ModulationSlotCountFormat { get; init; } = "{0} slots";

    /// <summary>Role of a <see cref="ModulationMatrix"/> spoken by screen readers.</summary>
    public string AutomationModulationMatrix { get; init; } = "modulation matrix";

    /// <summary>Role of a <see cref="RoutingMatrix"/> spoken by screen readers.</summary>
    public string AutomationRoutingMatrix { get; init; } = "routing matrix";

    /// <summary>
    /// Names of keys in shortcut texts (modifiers <see cref="Key.Control"/>, <see cref="Key.Shift"/>,
    /// <see cref="Key.Alt"/> included). Keys missing from the table use the English names.
    /// </summary>
    public IReadOnlyDictionary<Key, string> KeyNames { get; init; } = KeyNameTables.English;

    /// <summary>Default name of a document that was never saved.</summary>
    public string Untitled { get; init; } = "Untitled";

    /// <summary>Question asked before closing a modified document ({0}: document name).</summary>
    public string SaveChangesQuestion { get; init; } = "Do you want to save the changes to \u201C{0}\u201D?";

    /// <summary>Button that closes a modified document without saving it.</summary>
    public string DontSave { get; init; } = "Don't Save";

    /// <summary>Undo menu entry with the name of the action ({0}), with its mnemonic.</summary>
    public string UndoFormat { get; init; } = "&Undo {0}";

    /// <summary>Redo menu entry with the name of the action ({0}).</summary>
    public string RedoFormat { get; init; } = "&Redo {0}";

    /// <summary>Undo menu entry when nothing can be undone.</summary>
    public string UndoPlain { get; init; } = "&Undo";

    /// <summary>Redo menu entry when nothing can be redone.</summary>
    public string RedoPlain { get; init; } = "&Redo";

    /// <summary>File menu title (with its '&amp;' mnemonic, like the other menu texts).</summary>
    public string MenuFile { get; init; } = "&File";

    /// <summary>Edit menu title.</summary>
    public string MenuEdit { get; init; } = "&Edit";

    /// <summary>Edit / Cut (menus of the text boxes).</summary>
    public string EditCut { get; init; } = "Cu&t";

    /// <summary>Edit / Copy.</summary>
    public string EditCopy { get; init; } = "&Copy";

    /// <summary>Edit / Paste.</summary>
    public string EditPaste { get; init; } = "&Paste";

    /// <summary>Edit / Select All.</summary>
    public string EditSelectAll { get; init; } = "Select &All";

    /// <summary>View menu title.</summary>
    public string MenuView { get; init; } = "&View";

    /// <summary>Help menu title.</summary>
    public string MenuHelp { get; init; } = "&Help";

    /// <summary>File / New.</summary>
    public string MenuNew { get; init; } = "&New";

    /// <summary>File / Open.</summary>
    public string MenuOpen { get; init; } = "&Open…";

    /// <summary>File / Save.</summary>
    public string MenuSave { get; init; } = "&Save";

    /// <summary>File / Save As.</summary>
    public string MenuSaveAs { get; init; } = "Save &As…";

    /// <summary>File / Exit.</summary>
    public string MenuExit { get; init; } = "E&xit";

    /// <summary>View / Full Screen.</summary>
    public string MenuFullScreen { get; init; } = "&Full Screen";

    /// <summary>Help / About ({0}: application name).</summary>
    public string MenuAbout { get; init; } = "&About {0}…";

    /// <summary>Name of the "*.*" file dialog filter.</summary>
    public string AllFiles { get; init; } = "All Files";

    /// <summary>Status bar description of File / New.</summary>
    public string HintNew { get; init; } = "Creates a new document";

    /// <summary>Status bar description of File / Open.</summary>
    public string HintOpen { get; init; } = "Opens an existing document";

    /// <summary>Status bar description of File / Save.</summary>
    public string HintSave { get; init; } = "Saves the document";

    /// <summary>Status bar description of File / Save As.</summary>
    public string HintSaveAs { get; init; } = "Saves the document with a new name";

    /// <summary>Status bar description of Edit / Undo.</summary>
    public string HintUndo { get; init; } = "Undoes the last change";

    /// <summary>Status bar description of Edit / Redo.</summary>
    public string HintRedo { get; init; } = "Redoes the last undone change";

    /// <summary>Status bar description of File / Exit.</summary>
    public string HintExit { get; init; } = "Quits the application";

    /// <summary>Status bar description of View / Full Screen.</summary>
    public string HintFullScreen { get; init; } = "Shows the window on the whole screen (Esc to leave)";

    /// <summary>Idle text of a status bar.</summary>
    public string StatusReady { get; init; } = "Ready";

    /// <summary>Role of a knob spoken by screen readers (instead of "slider").</summary>
    public string AutomationKnob { get; init; } = "knob";

    /// <summary>Role of a dialog spoken by screen readers.</summary>
    public string AutomationDialog { get; init; } = "dialog";

    /// <summary>Name read for a menu button without caption nor tooltip.</summary>
    public string AutomationMenu { get; init; } = "Menu";

    /// <summary>State read for a text box whose entry was refused.</summary>
    public string AutomationInvalidEntry { get; init; } = "Invalid entry";

    /// <summary>Status bar message after saving ({0}: document name).</summary>
    public string StatusSaved { get; init; } = "Saved: {0}";

    /// <summary>Status bar message after opening ({0}: document name).</summary>
    public string StatusOpened { get; init; } = "Opened: {0}";

    /// <summary>Formats <see cref="StatusSaved"/>.</summary>
    public string FormatStatusSaved(string documentName) => string.Format(CultureInfo.InvariantCulture, StatusSaved, documentName);

    /// <summary>Formats <see cref="StatusOpened"/>.</summary>
    public string FormatStatusOpened(string documentName) => string.Format(CultureInfo.InvariantCulture, StatusOpened, documentName);

    /// <summary>Sub-menu listing the recently opened files.</summary>
    public string RecentFiles { get; init; } = "Recent Files";

    /// <summary>Entry that empties the recent files list.</summary>
    public string ClearRecentFiles { get; init; } = "Clear List";

    /// <summary>Placeholder entry of an empty recent files list.</summary>
    public string NoRecentFiles { get; init; } = "(empty)";

    /// <summary>Message shown when a recent file no longer exists ({0}: path).</summary>
    public string FileNotFound { get; init; } = "The file \u201C{0}\u201D cannot be found.";

    /// <summary>Displayed name of a key (<see cref="KeyNames"/>, then English, then the enum name).</summary>
    /// <param name="key">The key.</param>
    /// <returns>The name, e.g. "Ctrl", "Del", "F5", "A".</returns>
    public string KeyName(Key key)
    {
        if (KeyNames.TryGetValue(key, out var name) || KeyNameTables.English.TryGetValue(key, out name)) return name;
        if (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9) return ((char)key).ToString();
        if (key is >= Key.NumPad0 and <= (Key)0x69) return "Num " + (char)('0' + (key - Key.NumPad0));
        return key.ToString();
    }

    /// <summary>Formats <see cref="SaveChangesQuestion"/> with a document name.</summary>
    public string FormatSaveChangesQuestion(string documentName) => string.Format(CultureInfo.InvariantCulture, SaveChangesQuestion, documentName);

    /// <summary>Formats the Undo entry: <see cref="UndoFormat"/> with an action name, <see cref="UndoPlain"/> without.</summary>
    public string FormatUndo(string? actionName) => string.IsNullOrEmpty(actionName) ? UndoPlain : string.Format(CultureInfo.InvariantCulture, UndoFormat, Mnemonics.Escape(actionName));

    /// <summary>Formats the Redo entry: <see cref="RedoFormat"/> with an action name, <see cref="RedoPlain"/> without.</summary>
    public string FormatRedo(string? actionName) => string.IsNullOrEmpty(actionName) ? RedoPlain : string.Format(CultureInfo.InvariantCulture, RedoFormat, Mnemonics.Escape(actionName));

    /// <summary>Formats <see cref="MenuAbout"/> with the application name.</summary>
    public string FormatAbout(string applicationName) => string.Format(CultureInfo.InvariantCulture, MenuAbout, Mnemonics.Escape(applicationName));

    /// <summary>Formats <see cref="FileNotFound"/> with a path.</summary>
    public string FormatFileNotFound(string path) => string.Format(CultureInfo.InvariantCulture, FileNotFound, path);

    /// <summary>Formats <see cref="MidiForget"/> with an assignment.</summary>
    /// <param name="assignment">The assignment text (e.g. "CC 74").</param>
    /// <returns>The menu text.</returns>
    public string FormatMidiForget(string assignment) => string.Format(CultureInfo.InvariantCulture, MidiForget, assignment);

    /// <summary>Formats <see cref="VersionFormat"/> with a version string.</summary>
    /// <param name="version">The version text.</param>
    /// <returns>The formatted line.</returns>
    public string FormatVersion(string version) => string.Format(CultureInfo.InvariantCulture, VersionFormat, version);

    /// <summary>Registers (or replaces) the texts of a language.</summary>
    /// <param name="languageCode">Two-letter ISO 639-1 code (<c>"de"</c>) or IETF tag (<c>"pt-BR"</c>).</param>
    /// <param name="strings">The texts; <see cref="LanguageCode"/> is set to <paramref name="languageCode"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="languageCode"/> is empty, or is <see cref="UILanguages.Auto"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="strings"/> is <c>null</c>.</exception>
    public static void Register(string languageCode, UIStrings strings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        ArgumentNullException.ThrowIfNull(strings);
        if (string.Equals(languageCode, UILanguages.Auto, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("\"auto\" is reserved.", nameof(languageCode));
        lock (RegistryLock) Registry[languageCode.Trim()] = strings with { LanguageCode = languageCode.Trim() };
    }

    /// <summary>Codes of the registered languages (<c>"en"</c>, <c>"fr"</c> and those added with <see cref="Register"/>).</summary>
    public static IReadOnlyList<string> RegisteredLanguages
    {
        get
        {
            lock (RegistryLock) return [.. Registry.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>
    /// Returns the texts for a language code. <see cref="UILanguages.Auto"/> (or <c>null</c>) uses the Windows display
    /// language (<see cref="SystemLanguage"/>). A regional tag falls back to its language (<c>"fr-CA"</c> → <c>"fr"</c>),
    /// and unknown languages fall back to <see cref="English"/>.
    /// </summary>
    /// <param name="languageCode">A language code, <see cref="UILanguages.Auto"/> or <c>null</c>.</param>
    /// <returns>The matching table, never <c>null</c>.</returns>
    public static UIStrings ForLanguage(string? languageCode)
    {
        string code = string.IsNullOrWhiteSpace(languageCode) || string.Equals(languageCode, UILanguages.Auto, StringComparison.OrdinalIgnoreCase)
            ? SystemLanguage
            : languageCode.Trim();
        lock (RegistryLock)
        {
            if (Registry.TryGetValue(code, out var exact)) return exact;
            int dash = code.IndexOfAny(['-', '_']);
            if (dash > 0 && Registry.TryGetValue(code[..dash], out var neutral)) return neutral;
        }
        return English;
    }

    /// <summary>
    /// Two-letter code of the Windows display language of the current user (<c>"en"</c>, <c>"fr"</c>…), read from
    /// Win32 so that it also works in NativeAOT plugins built with invariant globalization. On other systems, the
    /// current UI culture is used; <c>"en"</c> if it cannot be determined.
    /// </summary>
    public static string SystemLanguage
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (FromPrimaryLanguage(Win32.GetUserDefaultUILanguage() & 0x3FF) is { } code) return code;
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                    // Not a real Windows (some test environments): fall through to the culture.
                }
            }
            string name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return string.IsNullOrEmpty(name) || name == "iv" ? "en" : name;
        }
    }

    // Primary language identifiers (winnt.h LANG_*) of the most common display languages.
    private static string? FromPrimaryLanguage(int primary) => primary switch
    {
        0x01 => "ar", 0x02 => "bg", 0x04 => "zh", 0x05 => "cs", 0x06 => "da", 0x07 => "de", 0x08 => "el",
        0x09 => "en", 0x0A => "es", 0x0B => "fi", 0x0C => "fr", 0x0D => "he", 0x0E => "hu", 0x10 => "it",
        0x11 => "ja", 0x12 => "ko", 0x13 => "nl", 0x14 => "nb", 0x15 => "pl", 0x16 => "pt", 0x18 => "ro",
        0x19 => "ru", 0x1A => "hr", 0x1B => "sk", 0x1D => "sv", 0x1F => "tr", 0x22 => "uk", 0x24 => "sl",
        0x25 => "et", 0x26 => "lv", 0x27 => "lt", 0x2A => "vi", 0x03 => "ca",
        _ => null,
    };
}

/// <summary>Language codes accepted by <see cref="UIHost.Language"/> and <see cref="UIStrings.ForLanguage"/>.</summary>
public static class UILanguages
{
    /// <summary>Follow the Windows display language of the user (default).</summary>
    public const string Auto = "auto";

    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>French.</summary>
    public const string French = "fr";
}

// Separate class: its fields are initialized before UIStrings.English and .French read them.
internal static class KeyNameTables
{
    public static readonly Dictionary<Key, string> English = new()
    {
        [Key.Control] = "Ctrl", [Key.Shift] = "Shift", [Key.Alt] = "Alt",
        [Key.Back] = "Backspace", [Key.Tab] = "Tab", [Key.Enter] = "Enter", [Key.Escape] = "Esc", [Key.Space] = "Space",
        [Key.PageUp] = "PgUp", [Key.PageDown] = "PgDn", [Key.End] = "End", [Key.Home] = "Home",
        [Key.Left] = "Left", [Key.Up] = "Up", [Key.Right] = "Right", [Key.Down] = "Down",
        [Key.Insert] = "Ins", [Key.Delete] = "Del",
        [Key.Multiply] = "Num *", [Key.Add] = "Num +", [Key.Subtract] = "Num -", [Key.Decimal] = "Num .", [Key.Divide] = "Num /",
        [Key.OemSemicolon] = ";", [Key.OemPlus] = "+", [Key.OemComma] = ",", [Key.OemMinus] = "-", [Key.OemPeriod] = ".",
    };

    public static readonly Dictionary<Key, string> French = new()
    {
        [Key.Control] = "Ctrl", [Key.Shift] = "Maj", [Key.Alt] = "Alt",
        [Key.Back] = "Retour arrière", [Key.Tab] = "Tab", [Key.Enter] = "Entrée", [Key.Escape] = "Échap", [Key.Space] = "Espace",
        [Key.PageUp] = "Pg.Préc", [Key.PageDown] = "Pg.Suiv", [Key.End] = "Fin", [Key.Home] = "Origine",
        [Key.Left] = "Gauche", [Key.Up] = "Haut", [Key.Right] = "Droite", [Key.Down] = "Bas",
        [Key.Insert] = "Inser", [Key.Delete] = "Suppr",
    };
}
