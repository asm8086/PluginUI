// 2026/11/06 12:53:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace PluginUI;

public sealed unsafe partial class UIHost
{
    private const uint SPI_GETHIGHCONTRAST = 0x0042;
    private const uint HCF_HIGHCONTRASTON = 0x00000001;
    private const int COLOR_WINDOW = 5, COLOR_WINDOWTEXT = 8, COLOR_HIGHLIGHT = 13, COLOR_HIGHLIGHTTEXT = 14,
        COLOR_BTNFACE = 15, COLOR_GRAYTEXT = 17, COLOR_HOTLIGHT = 26;

    private Theme? _highContrastTheme;
    private bool _followSystemHighContrast = true;
    private long _highContrastCheckedAt = long.MinValue;

    /// <summary>
    /// Draws with the colors of the Windows high contrast scheme while it is on (default <c>true</c>), instead of
    /// <see cref="Theme"/>. Bitmap skins are kept; vector controls, texts and focus rings follow the scheme.
    /// </summary>
    public bool FollowSystemHighContrast
    {
        get => _followSystemHighContrast;
        set
        {
            if (_followSystemHighContrast == value) return;
            _followSystemHighContrast = value;
            CheckHighContrast(force: true);
        }
    }

    /// <summary>Theme actually used for drawing: <see cref="Theme"/>, or the high contrast theme while it applies.</summary>
    public Theme EffectiveTheme => _highContrastTheme ?? _theme;

    /// <summary><c>true</c> while the drawing follows the system high contrast scheme.</summary>
    public bool IsHighContrastActive => _highContrastTheme is not null;

    /// <summary><c>true</c> when Windows runs in high contrast (false elsewhere and when it cannot be read).</summary>
    public static bool IsSystemHighContrast
    {
        get
        {
            try
            {
                var hc = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
                return SystemParametersInfoW(SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) != 0 && (hc.dwFlags & HCF_HIGHCONTRASTON) != 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>High contrast theme built from the current Windows system colors.</summary>
    public static Theme CreateSystemHighContrastTheme()
    {
        try
        {
            static Color Sys(int index)
            {
                uint c = GetSysColor(index); // 0x00BBGGRR
                return Color.FromRgb(((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF));
            }
            return Theme.CreateHighContrast(Palette.HighContrast(
                window: Sys(COLOR_WINDOW),
                windowText: Sys(COLOR_WINDOWTEXT),
                highlight: Sys(COLOR_HIGHLIGHT),
                highlightText: Sys(COLOR_HIGHLIGHTTEXT),
                grayText: Sys(COLOR_GRAYTEXT),
                buttonFace: Sys(COLOR_BTNFACE),
                hotLight: Sys(COLOR_HOTLIGHT)));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Theme.HighContrast;
        }
    }

    /// <summary>Follows the high contrast setting (at most once a second unless forced: plug-in windows get no broadcast).</summary>
    private void CheckHighContrast(bool force)
    {
        long now = Environment.TickCount64;
        if (!force && now - _highContrastCheckedAt < 1000) return;
        _highContrastCheckedAt = now;
        bool wanted = _followSystemHighContrast && IsSystemHighContrast;
        Theme? next = wanted ? CreateSystemHighContrastTheme() : null;
        if (next is null && _highContrastTheme is null) return;
        if (next is not null && _highContrastTheme is not null && next.Palette == _highContrastTheme.Palette) return;
        _highContrastTheme = next;
        InvalidateLayout();
    }
}
