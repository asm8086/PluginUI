// 2026/11/07 00:51:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

namespace PluginUI.Tests;

/// <summary>
/// Runs the tests in English whatever the display language of the machine: by default PluginUI follows the Windows
/// language (<see cref="UILanguages.Auto"/>), and many tests compare texts with <see cref="UIStrings.English"/>. Tests
/// about languages choose theirs and restore this one.
/// </summary>
[SetUpFixture]
public sealed class TestLanguage
{
    private string _saved = UILanguages.Auto;

    [OneTimeSetUp]
    public void UseEnglish()
    {
        _saved = UIHost.DefaultLanguage;
        UIHost.DefaultLanguage = UILanguages.English;
    }

    [OneTimeTearDown]
    public void Restore() => UIHost.DefaultLanguage = _saved;
}
