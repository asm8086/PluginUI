// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUIEditor;

/// <summary>
/// Texts of the editor in one language. English and French are built in; the language is chosen in
/// Tools ▸ Options (Auto follows the Windows display language). Numbers always use the decimal point.
/// </summary>
internal sealed record EditorStrings
{
    public static EditorStrings English { get; } = new();

    public static EditorStrings French { get; } = new()
    {
        Language = "fr",
        MenuFile = "&Fichier", MenuNew = "&Nouveau", MenuOpen = "&Ouvrir…", MenuOpenRecent = "Fichiers &récents", MenuSave = "&Enregistrer",
        MenuSaveAs = "Enregistrer &sous…", MenuImportTheme = "&Importer un thème…", MenuExport = "E&xporter",
        MenuExportPng1 = "Image PNG (100 %)…", MenuExportPng2 = "Image PNG (200 %)…", MenuExportTheme = "Thème (JSON)…",
        MenuExportAssets = "Ressources (images, SVG)…", MenuGenerateCode = "&Générer le code (.Designer.cs)…",
        MenuNewProject = "Nouveau &projet à partir du design…", MenuExit = "&Quitter",
        MenuEdit = "&Édition", MenuUndo = "&Annuler", MenuRedo = "&Rétablir", MenuCut = "Co&uper", MenuCopy = "&Copier",
        MenuPaste = "C&oller", MenuDuplicate = "&Dupliquer", MenuDelete = "&Supprimer", MenuBringToFront = "Mettre au &premier plan",
        MenuSendToBack = "Mettre à l'a&rrière-plan", MenuSelectParent = "Sélectionner le &parent",
        MenuView = "&Affichage", MenuZoomIn = "Zoom &avant", MenuZoomOut = "Zoom a&rrière", MenuZoom100 = "Zoom &100 %", MenuZoomFit = "&Ajuster à la fenêtre", ToolFit = "Ajuster",
        MenuPreview = "&Aperçu interactif", MenuShowOutlines = "&Contours des éléments",
        MenuTools = "&Outils", MenuOptions = "&Options…", MenuHelp = "&Aide", MenuDocumentation = "&Documentation", MenuAbout = "À &propos…",
        PanelToolbox = "Boîte à outils", PanelOutline = "Structure", PanelProperties = "Propriétés", PanelDesign = "Design",
        PanelParameters = "Paramètres", PanelMessages = "Messages",
        ColumnName = "Nom", ColumnKind = "Type", ColumnMin = "Min", ColumnMax = "Max", ColumnDefault = "Défaut", ColumnUnit = "Unité",
        ColumnDecimals = "Décimales", ColumnChoices = "Choix (séparés par des virgules)",
        Untitled = "Sans titre", SaveChangesFormat = "Enregistrer les modifications de {0} ?", OpenFailedFormat = "Impossible d'ouvrir {0} :\n{1}",
        SaveFailedFormat = "Impossible d'enregistrer {0} :\n{1}", CodeGeneratedFormat = "Code généré : {0}", ExportDoneFormat = "Exporté : {0}",
        ProjectCreatedFormat = "Projet créé dans {0}", MissingAssetsFormat = "Ressources introuvables : {0}", Ready = "Prêt",
        ElementsFormat = "{0} éléments", ZoomFormat = "{0} %", PreviewStatus = "Aperçu : les contrôles réagissent à la souris (F5 pour revenir à l'édition)",
        DesignFilter = "Designs PluginUI (*.pluginui)|*.pluginui|Tous les fichiers (*.*)|*.*", CSharpFilter = "Fichiers C# (*.cs)|*.cs",
        PngFilter = "Images PNG (*.png)|*.png", ThemeFilter = "Thèmes PluginUI (*.json)|*.json", ChooseFolder = "Choisissez un dossier",
        UnsupportedPaste = "Le presse-papiers ne contient pas d'éléments PluginUI.", CannotHoldChildren = "{0} ne peut pas contenir d'éléments.",
        DropHint = "Glissez un élément de la boîte à outils, ou double-cliquez dessus.",
        OptionsTitle = "Options", OptionLanguage = "Langue de l'interface", LanguageAuto = "Automatique (langue de Windows)",
        OptionGrid = "Grille magnétique (DIP, 0 = aucune)", OptionNPlugRoot = "Dossier src de NPlug (nouveaux projets)",
        OptionPluginUIRoot = "Dossier de PluginUI (nouveaux projets)", Ok = "OK", Cancel = "Annuler", Browse = "Parcourir…",
        NewProjectTitle = "Nouveau projet", ProjectName = "Nom du projet (identifiant C#)", ProjectKind = "Type", KindEffect = "Effet VST3",
        KindInstrument = "Instrument VST3", KindStandalone = "Application Windows", ProjectFolder = "Dossier parent", Vendor = "Éditeur",
        VendorUrl = "Site web", VendorEmail = "E-mail", LiveBridge = "Lien de design en direct dans les versions Debug", Create = "Créer",
        InvalidProjectName = "Le nom du projet doit être un identifiant C# (lettres, chiffres, _).",
        OptionExtensions = "Assemblies d'extension (un chemin .dll par ligne ; pris en compte au prochain démarrage)",
        ExtensionLoadedFormat = "Extension {0} : {1} élément(s) ajouté(s) à la boîte à outils.", ExtensionMissingFormat = "Extension introuvable : {0}",
        ExtensionFailedFormat = "Impossible de charger l'extension {0} : {1}", ExtensionDuplicateFormat = "Élément ignoré : {0} porte le nom d'un élément déjà enregistré.",
        DuplicateParameterFormat = "Un paramètre s'appelle déjà « {0} ».", InvalidIdentifierFormat = "« {0} » n'est pas utilisable : il faut un identifiant C# qui ne soit pas le nom d'un type de PluginUI.",
        AssetCopiedFormat = "Image copiée dans le dossier du design : {0}", SaveDesignFirst = "Enregistrez d'abord le design : les chemins des ressources sont relatifs à son dossier.",
        InvalidElementName = "Le nom d'un élément doit être un identifiant C# (lettres, chiffres, _) qui n'est pas un nom de type de PluginUI : c'est un champ du code généré.",
        AboutTitle = "À propos de PluginUIEditor",
        AboutText = "PluginUIEditor {0}\n\nConcepteur visuel d'interfaces PluginUI (Win32 + Direct2D) pour plugins audio en C#.\n\nLicence MIT — https://github.com/asm8086/PluginUI",
        CategoryDesign = "Conception", CategoryLayout = "Disposition", CategoryAppearance = "Apparence", CategoryText = "Texte",
        CategoryValue = "Valeur", CategoryData = "Données", CategoryBehavior = "Comportement", CategoryBinding = "Liaison",
        CategoryGeneral = "Général", CategoryTheme = "Thème", CategoryCodegen = "Génération de code",
        ToolboxContainers = "Conteneurs", ToolboxRotary = "Rotatifs", ToolboxLinear = "Linéaires", ToolboxButtons = "Boutons",
        ToolboxText = "Texte", ToolboxMeters = "Mesures", ToolboxInstrument = "Instrument", ToolboxAnalysis = "Analyse",
        ToolboxPickers = "Sélecteurs", ToolboxDecoration = "Décoration",
        DesignName = "Nom", DesignNameHelp = "Nom du design (titre, nom de classe par défaut).",
        DesignSize = "Taille", DesignSizeHelp = "Taille logique de l'éditeur à 100 % (DIP), « largeur, hauteur ».",
        DesignSizing = "Redimensionnement", DesignSizingHelp = "Fixed : taille fixe ; ScaleToFit : zoom proportionnel ; Relayout : la mise en page s'adapte.",
        ThemeBase = "Thème de base", ThemeBaseHelp = "Dark ou Light.", ThemeAccent = "Couleur d'accent", ThemeAccentHelp = "Couleur d'accent (#RRGGBB) ; vide = celle du thème.",
        CodegenNamespace = "Espace de noms", CodegenNamespaceHelp = "Espace de noms de la classe générée.",
        CodegenClass = "Classe", CodegenClassHelp = "Nom de la classe partielle générée.",
        CodegenTarget = "Cible", CodegenTargetHelp = "PluginEditorView : génère Build() ; Standalone : BuildDesign() seulement.",
        CodegenBuild = "Générer Build()", CodegenBuildHelp = "Génère protected override UIElement Build() (éditeurs de plugins).",
        CodegenAccess = "Accès des champs", CodegenAccessHelp = "private, internal, protected ou public.",
        SlotName = "Emplacement", SlotHelp = "En-tête de l'onglet (TabView) ou First / Second (SplitView).",
        ElementName = "(Name)", ElementNameHelp = "Nom de l'élément : nom du champ dans le code généré. Vide = élément anonyme.",
        BindingHelp = "Paramètre du design lié à cette propriété.",
    };

    public string Language { get; init; } = "en";

    public string MenuFile { get; init; } = "&File";
    public string MenuNew { get; init; } = "&New";
    public string MenuOpen { get; init; } = "&Open…";
    public string MenuOpenRecent { get; init; } = "Open &recent";
    public string MenuSave { get; init; } = "&Save";
    public string MenuSaveAs { get; init; } = "Save &as…";
    public string MenuImportTheme { get; init; } = "&Import theme…";
    public string MenuExport { get; init; } = "E&xport";
    public string MenuExportPng1 { get; init; } = "PNG image (100 %)…";
    public string MenuExportPng2 { get; init; } = "PNG image (200 %)…";
    public string MenuExportTheme { get; init; } = "Theme (JSON)…";
    public string MenuExportAssets { get; init; } = "Assets (images, SVG)…";
    public string MenuGenerateCode { get; init; } = "&Generate code (.Designer.cs)…";
    public string MenuNewProject { get; init; } = "New &project from design…";
    public string MenuExit { get; init; } = "E&xit";
    public string MenuEdit { get; init; } = "&Edit";
    public string MenuUndo { get; init; } = "&Undo";
    public string MenuRedo { get; init; } = "&Redo";
    public string MenuCut { get; init; } = "Cu&t";
    public string MenuCopy { get; init; } = "&Copy";
    public string MenuPaste { get; init; } = "&Paste";
    public string MenuDuplicate { get; init; } = "&Duplicate";
    public string MenuDelete { get; init; } = "De&lete";
    public string MenuBringToFront { get; init; } = "Bring to &front";
    public string MenuSendToBack { get; init; } = "Send to &back";
    public string MenuSelectParent { get; init; } = "Select &parent";
    public string MenuView { get; init; } = "&View";
    public string MenuZoomIn { get; init; } = "Zoom &in";
    public string MenuZoomOut { get; init; } = "Zoom &out";
    public string MenuZoom100 { get; init; } = "Zoom &100 %";
    public string MenuZoomFit { get; init; } = "Zoom to &fit";
    public string ToolFit { get; init; } = "Fit";
    public string MenuPreview { get; init; } = "Interactive &preview";
    public string MenuShowOutlines { get; init; } = "Element &outlines";
    public string MenuTools { get; init; } = "&Tools";
    public string MenuOptions { get; init; } = "&Options…";
    public string MenuHelp { get; init; } = "&Help";
    public string MenuDocumentation { get; init; } = "&Documentation";
    public string MenuAbout { get; init; } = "&About…";

    public string PanelToolbox { get; init; } = "Toolbox";
    public string PanelOutline { get; init; } = "Outline";
    public string PanelProperties { get; init; } = "Properties";
    public string PanelDesign { get; init; } = "Design";
    public string PanelParameters { get; init; } = "Parameters";
    public string PanelMessages { get; init; } = "Messages";

    public string ColumnName { get; init; } = "Name";
    public string ColumnKind { get; init; } = "Kind";
    public string ColumnMin { get; init; } = "Min";
    public string ColumnMax { get; init; } = "Max";
    public string ColumnDefault { get; init; } = "Default";
    public string ColumnUnit { get; init; } = "Unit";
    public string ColumnDecimals { get; init; } = "Decimals";
    public string ColumnChoices { get; init; } = "Choices (comma separated)";

    public string Untitled { get; init; } = "Untitled";
    public string SaveChangesFormat { get; init; } = "Save the changes to {0}?";
    public string OpenFailedFormat { get; init; } = "Cannot open {0}:\n{1}";
    public string SaveFailedFormat { get; init; } = "Cannot save {0}:\n{1}";
    public string CodeGeneratedFormat { get; init; } = "Code generated: {0}";
    public string ExportDoneFormat { get; init; } = "Exported: {0}";
    public string ProjectCreatedFormat { get; init; } = "Project created in {0}";
    public string MissingAssetsFormat { get; init; } = "Assets not found: {0}";
    public string Ready { get; init; } = "Ready";
    public string ElementsFormat { get; init; } = "{0} elements";
    public string ZoomFormat { get; init; } = "{0} %";
    public string PreviewStatus { get; init; } = "Preview: controls react to the mouse (F5 to go back to editing)";
    public string DesignFilter { get; init; } = "PluginUI designs (*.pluginui)|*.pluginui|All files (*.*)|*.*";
    public string CSharpFilter { get; init; } = "C# files (*.cs)|*.cs";
    public string PngFilter { get; init; } = "PNG images (*.png)|*.png";
    public string ThemeFilter { get; init; } = "PluginUI themes (*.json)|*.json";
    public string ChooseFolder { get; init; } = "Choose a folder";
    public string UnsupportedPaste { get; init; } = "The clipboard holds no PluginUI elements.";
    public string CannotHoldChildren { get; init; } = "{0} cannot hold elements.";
    public string DropHint { get; init; } = "Drag an element from the toolbox, or double-click it.";

    public string OptionsTitle { get; init; } = "Options";
    public string OptionLanguage { get; init; } = "Interface language";
    public string LanguageAuto { get; init; } = "Automatic (Windows language)";
    public string OptionGrid { get; init; } = "Snap grid (DIPs, 0 = none)";
    public string OptionNPlugRoot { get; init; } = "NPlug src folder (new projects)";
    public string OptionPluginUIRoot { get; init; } = "PluginUI folder (new projects)";
    public string Ok { get; init; } = "OK";
    public string Cancel { get; init; } = "Cancel";
    public string Browse { get; init; } = "Browse…";

    public string NewProjectTitle { get; init; } = "New project";
    public string ProjectName { get; init; } = "Project name (C# identifier)";
    public string ProjectKind { get; init; } = "Kind";
    public string KindEffect { get; init; } = "VST3 effect";
    public string KindInstrument { get; init; } = "VST3 instrument";
    public string KindStandalone { get; init; } = "Windows application";
    public string ProjectFolder { get; init; } = "Parent folder";
    public string Vendor { get; init; } = "Vendor";
    public string VendorUrl { get; init; } = "Web site";
    public string VendorEmail { get; init; } = "E-mail";
    public string LiveBridge { get; init; } = "Live design bridge in Debug builds";
    public string Create { get; init; } = "Create";
    public string InvalidProjectName { get; init; } = "The project name must be a C# identifier (letters, digits, _).";
    public string OptionExtensions { get; init; } = "Extension assemblies (one .dll path per line; applied at next start)";
    public string ExtensionLoadedFormat { get; init; } = "Extension {0}: {1} element(s) added to the toolbox.";
    public string ExtensionMissingFormat { get; init; } = "Extension not found: {0}";
    public string ExtensionFailedFormat { get; init; } = "Cannot load the extension {0}: {1}";
    public string ExtensionDuplicateFormat { get; init; } = "Element skipped: {0} has the name of an element already registered.";
    public string DuplicateParameterFormat { get; init; } = "A parameter is already named \"{0}\".";
    public string InvalidIdentifierFormat { get; init; } = "\"{0}\" cannot be used: a C# identifier that is not the name of a PluginUI type is required.";
    public string AssetCopiedFormat { get; init; } = "Image copied into the design folder: {0}";
    public string SaveDesignFirst { get; init; } = "Save the design first: asset paths are relative to its folder.";
    public string InvalidElementName { get; init; } = "An element name must be a C# identifier (letters, digits, _) that is not a PluginUI type name: it becomes a field of the generated code.";

    public string AboutTitle { get; init; } = "About PluginUIEditor";
    public string AboutText { get; init; } = "PluginUIEditor {0}\n\nVisual designer for PluginUI (Win32 + Direct2D) audio plug-in editors in C#.\n\nMIT license — https://github.com/asm8086/PluginUI";

    public string CategoryDesign { get; init; } = "Design";
    public string CategoryLayout { get; init; } = "Layout";
    public string CategoryAppearance { get; init; } = "Appearance";
    public string CategoryText { get; init; } = "Text";
    public string CategoryValue { get; init; } = "Value";
    public string CategoryData { get; init; } = "Data";
    public string CategoryBehavior { get; init; } = "Behavior";
    public string CategoryBinding { get; init; } = "Binding";
    public string CategoryGeneral { get; init; } = "General";
    public string CategoryTheme { get; init; } = "Theme";
    public string CategoryCodegen { get; init; } = "Code generation";

    public string ToolboxContainers { get; init; } = "Containers";
    public string ToolboxRotary { get; init; } = "Rotary";
    public string ToolboxLinear { get; init; } = "Linear";
    public string ToolboxButtons { get; init; } = "Buttons";
    public string ToolboxText { get; init; } = "Text";
    public string ToolboxMeters { get; init; } = "Meters";
    public string ToolboxInstrument { get; init; } = "Instrument";
    public string ToolboxAnalysis { get; init; } = "Analysis";
    public string ToolboxPickers { get; init; } = "Pickers";
    public string ToolboxDecoration { get; init; } = "Decoration";

    public string DesignName { get; init; } = "Name";
    public string DesignNameHelp { get; init; } = "Name of the design (title, default class name).";
    public string DesignSize { get; init; } = "Size";
    public string DesignSizeHelp { get; init; } = "Logical size of the editor at 100 % (DIPs), \"width, height\".";
    public string DesignSizing { get; init; } = "Sizing";
    public string DesignSizingHelp { get; init; } = "Fixed: fixed size; ScaleToFit: proportional zoom; Relayout: the layout adapts.";
    public string ThemeBase { get; init; } = "Base theme";
    public string ThemeBaseHelp { get; init; } = "Dark or Light.";
    public string ThemeAccent { get; init; } = "Accent color";
    public string ThemeAccentHelp { get; init; } = "Accent color (#RRGGBB); empty = the base theme's.";
    public string CodegenNamespace { get; init; } = "Namespace";
    public string CodegenNamespaceHelp { get; init; } = "Namespace of the generated class.";
    public string CodegenClass { get; init; } = "Class";
    public string CodegenClassHelp { get; init; } = "Name of the generated partial class.";
    public string CodegenTarget { get; init; } = "Target";
    public string CodegenTargetHelp { get; init; } = "PluginEditorView: generates Build(); Standalone: BuildDesign() only.";
    public string CodegenBuild { get; init; } = "Generate Build()";
    public string CodegenBuildHelp { get; init; } = "Generates protected override UIElement Build() (plug-in editors).";
    public string CodegenAccess { get; init; } = "Field access";
    public string CodegenAccessHelp { get; init; } = "private, internal, protected or public.";
    public string SlotName { get; init; } = "Slot";
    public string SlotHelp { get; init; } = "Tab header (TabView) or First / Second (SplitView).";
    public string ElementName { get; init; } = "(Name)";
    public string ElementNameHelp { get; init; } = "Element name: the field name in generated code. Empty = anonymous element.";
    public string BindingHelp { get; init; } = "Design parameter bound to this property.";

    /// <summary>Current texts.</summary>
    public static EditorStrings Current { get; private set; } = English;

    /// <summary>Selects the language: "auto", "en" or "fr".</summary>
    public static void Select(string? language)
    {
        string code = string.IsNullOrEmpty(language) || language == "auto" ? PluginUI.UIStrings.SystemLanguage : language;
        Current = code.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? French : English;
        // The preview of PluginUI's own texts (dialogs, menus) follows the editor language.
        PluginUI.UIHost.DefaultLanguage = Current.Language;
    }

    public string Category(string category) => category switch
    {
        "Design" => CategoryDesign,
        "Layout" => CategoryLayout,
        "Appearance" => CategoryAppearance,
        "Text" => CategoryText,
        "Value" => CategoryValue,
        "Data" => CategoryData,
        "Behavior" => CategoryBehavior,
        "Binding" => CategoryBinding,
        _ => category,
    };

    public string Toolbox(string category) => category switch
    {
        "Containers" => ToolboxContainers,
        "Rotary" => ToolboxRotary,
        "Linear" => ToolboxLinear,
        "Buttons" => ToolboxButtons,
        "Text" => ToolboxText,
        "Meters" => ToolboxMeters,
        "Instrument" => ToolboxInstrument,
        "Analysis" => ToolboxAnalysis,
        "Pickers" => ToolboxPickers,
        "Decoration" => ToolboxDecoration,
        _ => category,
    };
}
