// 2026/11/05 14:06:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.Versioning;

// Desktop applications built on PluginUI, which targets Windows 10 1809+ (the undo stack, recent files and settings
// are platform neutral and unit-tested on Linux).
[assembly: SupportedOSPlatform("windows10.0.17763")]
