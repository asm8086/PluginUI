// 2026/11/06 23:57:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using PluginUI.Host.Catalog;

namespace PluginUI.Host.Tests;

/// <summary>The parent side of out-of-process scanning, with a shell script standing for the child.</summary>
public class ScannerProcessTests
{
    private const string Json = """[{"Format":"Vst3","Name":"Noisy","Vendor":"V","ClassId":"A","Category":"Effect"}]""";

    // Arguments after the prefix: scan-module --format vst3 --out <file> <module>  →  $1 … $6
    private static OutOfProcessModuleScanner Script(string body) => new("/bin/sh", "-c", body, "sh");

    [SetUp]
    public void OnlyWithAShell() => Assume.That(!OperatingSystem.IsWindows(), "uses /bin/sh");

    [Test]
    public void StandardOutputNoiseIsIgnored()
    {
        var found = Script($"echo 'version 1.2 loaded'; echo '#debug'; printf '%s' '{Json}' > \"$5\"").Scan("x.vst3", PluginFormat.Vst3, TimeSpan.FromSeconds(10));
        Assert.That(found.Single().Name, Is.EqualTo("Noisy"));
    }

    [Test]
    public void ErrorLineBecomesTheMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Script("echo 'noise' >&2; printf '\\001pluginhost-error GetPluginFactory returned no factory.\\n' >&2; exit 2")
            .Scan("x.vst3", PluginFormat.Vst3, TimeSpan.FromSeconds(10)));
        Assert.That(ex!.Message, Is.EqualTo("GetPluginFactory returned no factory."));
    }

    [Test]
    public void CrashReportsTheCodeAndTheLastLines()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Script("echo 'Failed initialize the COM library' >&2; exit 3")
            .Scan("x.vst3", PluginFormat.Vst3, TimeSpan.FromSeconds(10)));
        Assert.That(ex!.Message, Does.StartWith("The scanner stopped (code 0x00000003).").And.Contain("Failed initialize the COM library"));
    }

    [Test]
    public void ProgressKeepsALongScanAlive()
    {
        string script = "for i in 1 2 3 4 5 6 7 8; do printf '\\001pluginhost-progress %s/8\\n' $i >&2; sleep 0.25; done; printf '%s' '" + Json + "' > \"$5\"";
        var found = Script(script).Scan("x.vst3", PluginFormat.Vst3, TimeSpan.FromSeconds(1));
        Assert.That(found, Has.Count.EqualTo(1), "two seconds in all, never one second without progress");
    }

    [Test]
    public void SilenceTimesOut()
    {
        var ex = Assert.Throws<TimeoutException>(() => Script("sleep 5").Scan("x.vst3", PluginFormat.Vst3, TimeSpan.FromSeconds(1)));
        Assert.That(ex!.Message, Is.EqualTo("No answer for 1 s."));
    }
}
