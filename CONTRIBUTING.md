<!-- 2026/10/18 16:20:00 UTC - PluginUI -->

# Contributing to PluginUI

*Version française : [CONTRIBUTING_FR.md](CONTRIBUTING_FR.md)*

Thank you for your interest! Bug reports, questions, ideas and pull requests are welcome.

## Reporting a problem

Open an [issue](https://github.com/asm8086/PluginUI/issues) with:

- what you did, what you expected and what happened (a screenshot helps for drawing problems);
- Windows version, DPI scaling, DAW and version for plug-in problems;
- the smallest code or `.pluginui` design that shows the problem, when you can.

## Getting the sources

```
git clone --recurse-submodules https://github.com/asm8086/PluginUI.git
```

`external/NPlug` is a submodule: the fork [asm8086/NPlug](https://github.com/asm8086/NPlug), branch
`integration/pluginui`, which contains NPlug fixes proposed upstream. If you cloned without
`--recurse-submodules`, run `git submodule update --init`.

## Building and testing

Requirements: Windows 10 1809 or later, the .NET 10 SDK, Visual Studio 2026 (with the *Desktop development with
C++* workload for NativeAOT publishing). The libraries and tests also build and run on Linux and macOS
(`EnableWindowsTargeting`), which is what part of the CI does.

```
dotnet build PluginUI.slnx -c Release
dotnet test PluginUI.slnx -c Release
```

Other useful commands:

| Command | What it does |
|---|---|
| `dotnet run --project samples/PluginUI.Gallery` | Every element in a window (`-- --lang fr` in French) |
| `dotnet run --project tools/PluginUIEditor` | The visual designer |
| `dotnet tool restore` then `dotnet docfx docfx/docfx.json --serve` | The documentation site |
| `pwsh build/Build-Release.ps1` | The release packages, as the release workflow builds them (Windows) |

## Conventions

- **Language**: identifiers, comments and XML documentation in English. User guides exist in English and French
  (`X.md` and `X_FR.md`): update both, or say in the pull request which one needs translating.
- **File header**: each source file starts with a comment holding the date of its last revision
  (`yyyy/MM/dd HH:mm:ss UTC`) and the project name, then the SPDX lines:

  ```csharp
  // 2026/10/18 16:20:00 UTC - PluginUI
  // SPDX-FileCopyrightText: 2026 asm8086
  // SPDX-License-Identifier: MIT
  ```

- **No culture dependency**: numbers are formatted and parsed with the invariant culture (CA1304 / CA1305 are
  errors). A decimal comma is never displayed nor accepted.
- **NativeAOT**: the libraries build with the AOT and trimming analyzers and warnings treated as errors. No
  reflection that the trimmer cannot see, no `System.Text.Json` reflection serializer.
- **Public API**: every public member has XML documentation written for its users.
- **Real-time safety**: code called from the audio thread allocates nothing, takes no lock and does not block.
- **Controls**: follow the design conventions of [EXTENDING.md](docs/EXTENDING.md#13-design-conventions-pluginuieditor)
  so that they work in the visual designer.
- **Tests**: add or update tests in `tests/` with the change. Tests open no window; they drive the host through
  `UIHost.Dispatch…`.
- **Screenshots**: when a change is visible, regenerate the gallery captures (see the README, section *Gallery*).

## NPlug changes

Fixes to NPlug go to its upstream repository first ([xoofx/NPlug](https://github.com/xoofx/NPlug)), then to the
`integration/pluginui` branch of the fork, and the submodule is updated.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Keep the change focused; describe what it does and how you tested it.
3. Make sure `dotnet build` (no warning) and `dotnet test` pass; the CI runs them on Linux and Windows.
4. Add a line to the *Unreleased* section of [CHANGELOG.md](CHANGELOG.md) for user-visible changes.

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).

## Releases

Maintainers publish a release by pushing a tag `vX.Y.Z`. The release workflow builds and tests everything, creates the
VST3 bundle of the demo instrument for x64 (x86-64-v3 and x86-64-v2) and ARM64, the PluginUIEditor and gallery
packages, pushes the NuGet packages (when the repository variable `PUBLISH_NUGET` is `true` and nuget.org trusted publishing is set up) and publishes the GitHub release.
