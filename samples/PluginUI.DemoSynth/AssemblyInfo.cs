// 2026/10/18 13:39:00 UTC - PluginUI
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// PluginUI (editor and its lock-free helpers such as NoteActivity) targets Windows 10 1809+: so does the plug-in.
[assembly: SupportedOSPlatform("windows10.0.17763")]

[assembly: InternalsVisibleTo("PluginUI.NPlug.Tests")]
