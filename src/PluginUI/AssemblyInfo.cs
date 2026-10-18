// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.Versioning;

// The toolkit renders with Win32 + Direct2D. Layout, parameters, themes and the element tree are platform neutral
// (and unit-tested elsewhere), but every rendering/hosting entry point requires Windows 10 1809 or later.
[assembly: SupportedOSPlatform("windows10.0.17763")]
