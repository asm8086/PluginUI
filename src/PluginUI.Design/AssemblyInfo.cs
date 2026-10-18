// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using System.Runtime.Versioning;

// Builds PluginUI elements, which target Windows 10 1809+ (documents, JSON and code generation are platform neutral
// and unit-tested on Linux).
[assembly: SupportedOSPlatform("windows10.0.17763")]
