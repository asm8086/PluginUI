// 2026/10/18 16:20:00 UTC - PluginUI
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
