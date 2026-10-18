// 2026/10/18 16:20:00 UTC - PluginUI
// SPDX-FileCopyrightText: 2026 asm8086
// SPDX-License-Identifier: MIT

using NPlug.Validator;

// Usage: ValidatePlugin <plug-in.vst3 | bundle.vst3 folder>
// Exit code 0 when every test of the Steinberg validator passes. The process architecture decides which binary of a
// bundle is tested (Contents\x86_64-win or Contents\arm64-win).
if (args.Length != 1 || !(File.Exists(args[0]) || Directory.Exists(args[0])))
{
    Console.Error.WriteLine("Usage: ValidatePlugin <plug-in.vst3 | bundle.vst3 folder>");
    return 2;
}

string path = Path.GetFullPath(args[0]);
Console.WriteLine($"Validating {path} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
var errors = new StringWriter();
bool passed = AudioPluginValidator.Validate(path, Console.Out, errors);
if (errors.ToString().Length > 0) Console.Error.WriteLine(errors);
Console.WriteLine(passed ? "Validation passed." : "Validation FAILED.");
return passed ? 0 : 1;
