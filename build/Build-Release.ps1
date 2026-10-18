# 2026/10/18 16:20:00 UTC - PluginUI
# SPDX-FileCopyrightText: 2026 asm8086
# SPDX-License-Identifier: MIT

<#
.SYNOPSIS
    Builds the PluginUI release packages: the DemoSynth VST3 bundle for x64 (x86-64-v3 and x86-64-v2) and ARM64,
    PluginUIEditor, the gallery and the NuGet packages.

.DESCRIPTION
    Stages, run in this order (the release workflow runs Build on one machine per architecture, then Package and
    Validate on an x64 machine):

      Build     Publishes, for each target, the NativeAOT plug-in (and, for x64-v2 / arm64, the gallery and the editor)
                into artifacts/stage/<target>/.
      Package   Builds the x64 VST3 loader (build/vst3shim), assembles the bundles, the zip files, the NuGet packages
                and SHA256SUMS.txt into artifacts/release/.
      Validate  Runs the Steinberg validator on the bundles for the architecture of this machine (x64: both builds,
                through the loader).
      All       Build + Package + Validate (default).

    The universal bundle PluginUI.DemoSynth.vst3 contains:
      Contents/x86_64-win/PluginUI.DemoSynth.vst3            loader: picks the build for the processor
      Contents/x86_64-win/PluginUI.DemoSynth.x86-64-v3.dll   AVX2 / FMA build (Haswell, Zen and later)
      Contents/x86_64-win/PluginUI.DemoSynth.x86-64-v2.dll   build for any x86-64-v2 processor
      Contents/arm64-win/PluginUI.DemoSynth.vst3             ARM64 build

    Requirements (Windows): .NET 10 SDK, Visual Studio with the C++ desktop workload (NativeAOT linker and cl.exe),
    and the ARM64 C++ tools for -Target arm64 on an x64 machine. Without cl.exe, the loader is built with clang and
    lld-link when they are installed.

.EXAMPLE
    pwsh build/Build-Release.ps1                                 # x64-v3 and x64-v2, package, validate
    pwsh build/Build-Release.ps1 -Stage Validate -ValidatePath artifacts/stage/arm64/plugin/PluginUI.DemoSynth.vst3
    pwsh build/Build-Release.ps1 -Target x64-v3,x64-v2,arm64     # also ARM64 (cross-compiled)
    pwsh build/Build-Release.ps1 -Stage Build -Target arm64      # one stage on an ARM64 machine
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Build', 'Package', 'Validate')]
    [string]$Stage = 'All',

    [ValidateSet('x64-v3', 'x64-v2', 'arm64')]
    [string[]]$Target = @('x64-v3', 'x64-v2'),

    [string]$Version,

    [string]$Configuration = 'Release',

    [string]$Artifacts,

    [switch]$SkipNuGet,

    # Validate stage: plug-in files or bundle folders to check instead of the packaged bundles.
    [string[]]$ValidatePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Artifacts) { $Artifacts = Join-Path $Root 'artifacts' }
$StageDir = Join-Path $Artifacts 'stage'
$ReleaseDir = Join-Path $Artifacts 'release'
$PluginName = 'PluginUI.DemoSynth'

if (-not $Version) {
    if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -match '^v(\d+\.\d+\.\d+.*)$') {
        $Version = $Matches[1]
    }
    else {
        [xml]$props = Get-Content (Join-Path $Root 'Directory.Build.props')
        $Version = $props.SelectSingleNode('//VersionPrefix').InnerText
    }
}
Write-Host "PluginUI release $Version ($Stage)"

function Invoke-Native {
    param([string]$Command, [string[]]$Arguments)
    Write-Host "> $Command $($Arguments -join ' ')"
    # Out-Host: the tool output must not become the return value of the calling function.
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
}

function Get-Rid([string]$t) { if ($t -eq 'arm64') { 'win-arm64' } else { 'win-x64' } }

# ------------------------------------------------------------------------------------------------- Build

function Invoke-Build([string]$t) {
    $rid = Get-Rid $t
    $dir = Join-Path $StageDir $t
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    $common = @('-c', $Configuration, '-r', $rid, "-p:Version=$Version")
    if ($t -ne 'arm64') { $common += "-p:IlcInstructionSet=$($t -replace '^x64-', 'x86-64-')" }

    # The plug-in, compiled ahead of time for this target.
    Invoke-Native dotnet (@('publish', (Join-Path $Root "samples/$PluginName")) + $common + @('-o', (Join-Path $dir 'plugin')))
    $vst3 = Join-Path $dir "plugin/$PluginName.vst3"
    if (-not (Test-Path $vst3)) { throw "NativeAOT output not found: $vst3" }

    # The applications, once per architecture (the x64-v2 build runs on every x64 processor).
    if ($t -ne 'x64-v3') {
        Invoke-Native dotnet (@('publish', (Join-Path $Root 'samples/PluginUI.Gallery'), '-p:PublishAot=true') + $common + @('-o', (Join-Path $dir 'gallery')))
        Invoke-Native dotnet @('publish', (Join-Path $Root 'tools/PluginUIEditor'), '-c', $Configuration, '-r', $rid, '--self-contained', 'false',
            "-p:Version=$Version", '-o', (Join-Path $dir 'editor'))
    }
}

# ----------------------------------------------------------------------------------------------- Package

function Build-Loader([string]$outDir) {
    $source = Join-Path $Root 'build/vst3shim/vst3shim.c'
    $dll = Join-Path $outDir 'vst3shim.dll'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    if (Get-Command cl -ErrorAction SilentlyContinue) {
        Push-Location $outDir
        try { Invoke-Native cl @('/nologo', '/O2', '/W4', '/LD', $source, '/Fe:vst3shim.dll', '/link', 'kernel32.lib') }
        finally { Pop-Location }
    }
    elseif ((Get-Command clang -ErrorAction SilentlyContinue) -and (Get-Command lld-link -ErrorAction SilentlyContinue)) {
        $lib = Join-Path $outDir 'kernel32.lib'
        $obj = Join-Path $outDir 'vst3shim.obj'
        Invoke-Native llvm-dlltool @('-m', 'i386:x86-64', '-d', (Join-Path $Root 'build/vst3shim/kernel32.def'), '-l', $lib)
        Invoke-Native clang @('--target=x86_64-pc-windows-msvc', '-O2', '-Wall', '-Wextra', '-DPLUGINUI_SHIM_NO_CRT', '-fno-builtin',
            '-fno-stack-protector', '-mno-stack-arg-probe', '-c', $source, '-o', $obj)
        Invoke-Native lld-link @('/dll', '/nodefaultlib', '/entry:DllMain', "/out:$dll", $obj, $lib)
    }
    else {
        throw 'No C compiler for the VST3 loader: run from a Visual Studio developer shell (cl.exe), or install clang and lld.'
    }
    return $dll
}

function New-Zip([string]$source, [string]$zip) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "  $([IO.Path]::GetFileName($zip))"
}

function Copy-Notices([string]$folder) {
    Copy-Item (Join-Path $Root 'LICENSE') (Join-Path $folder 'LICENSE.txt')
    Copy-Item (Join-Path $Root 'build/THIRD-PARTY-NOTICES.txt') (Join-Path $folder 'THIRD-PARTY-NOTICES.txt')
}

function Write-InstallNotes([string]$folder, [string]$kind) {
    $text = @"
PluginUI $Version - $PluginName ($kind)
https://github.com/asm8086/PluginUI

Installation: copy the folder $PluginName.vst3 to C:\Program Files\Common Files\VST3, then rescan the plug-ins in your DAW.
Requirements: Windows 10 1809 or later.

Builds:
  universal  x64 and ARM64 in one bundle; on x64 a loader picks the x86-64-v3 build (AVX2, FMA: Intel Haswell 2013,
             AMD Zen and later) or the x86-64-v2 build (any other 64-bit processor with SSE4.2).
  x64-v3     x64 processors with AVX2 and FMA only.
  x64-v2     any x64 processor with SSE4.2 (slower DSP than x64-v3).
  arm64      Windows on ARM.

Troubleshooting: the environment variable PLUGINUI_X64_LEVEL=v2 forces the x86-64-v2 build of the universal bundle.
"@
    Set-Content -Path (Join-Path $folder 'README.txt') -Value $text -Encoding utf8
    Copy-Notices $folder
}

function New-Bundle([string]$parent, [hashtable]$files) {
    # $files: relative path inside the bundle -> source file
    $bundle = Join-Path $parent "$PluginName.vst3"
    foreach ($entry in $files.GetEnumerator()) {
        $target = Join-Path $bundle $entry.Key
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item $entry.Value $target
    }
}

function Invoke-Package {
    if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
    $work = Join-Path $Artifacts 'work'
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }

    $built = @{}
    foreach ($t in 'x64-v3', 'x64-v2', 'arm64') {
        $file = Join-Path $StageDir "$t/plugin/$PluginName.vst3"
        if (Test-Path $file) { $built[$t] = $file }
    }
    if ($built.Count -eq 0) { throw "Nothing to package in ${StageDir}: run the Build stage first." }
    Write-Host "Packaging builds: $($built.Keys -join ', ')"

    # Universal bundle: the loader and both x64 builds, plus ARM64 when available.
    if ($built.ContainsKey('x64-v3') -and $built.ContainsKey('x64-v2')) {
        $loader = Build-Loader (Join-Path $work 'loader')
        $files = @{
            "Contents/x86_64-win/$PluginName.vst3"           = $loader
            "Contents/x86_64-win/$PluginName.x86-64-v3.dll" = $built['x64-v3']
            "Contents/x86_64-win/$PluginName.x86-64-v2.dll" = $built['x64-v2']
        }
        if ($built.ContainsKey('arm64')) { $files["Contents/arm64-win/$PluginName.vst3"] = $built['arm64'] }
        else { Write-Warning 'No ARM64 build: the universal bundle is x64 only.' }
        $folder = Join-Path $work 'universal'
        New-Bundle $folder $files
        Write-InstallNotes $folder 'universal'
        New-Zip $folder (Join-Path $ReleaseDir "$PluginName-$Version-vst3-universal.zip")
    }
    else {
        Write-Warning 'The universal bundle needs both x64-v3 and x64-v2 builds.'
    }

    # One bundle per build, without loader.
    foreach ($t in $built.Keys | Sort-Object) {
        $arch = if ($t -eq 'arm64') { 'arm64-win' } else { 'x86_64-win' }
        $folder = Join-Path $work $t
        New-Bundle $folder @{ "Contents/$arch/$PluginName.vst3" = $built[$t] }
        Write-InstallNotes $folder $t
        New-Zip $folder (Join-Path $ReleaseDir "$PluginName-$Version-vst3-$t.zip")
    }

    # Applications.
    foreach ($t in 'x64-v2', 'arm64') {
        $rid = Get-Rid $t
        foreach ($app in @(@{ Dir = 'editor'; Name = 'PluginUIEditor' }, @{ Dir = 'gallery'; Name = 'PluginUI.Gallery' })) {
            $dir = Join-Path $StageDir "$t/$($app.Dir)"
            if (Test-Path $dir) {
                Get-ChildItem $dir -Filter '*.pdb' -Recurse | Remove-Item -Force
                Copy-Notices $dir
                New-Zip $dir (Join-Path $ReleaseDir "$($app.Name)-$Version-$rid.zip")
            }
        }
    }

    # NuGet packages (PluginUI, PluginUI.Design).
    if (-not $SkipNuGet) {
        Invoke-Native dotnet @('pack', (Join-Path $Root 'src/PluginUI/PluginUI.csproj'), '-c', $Configuration, "-p:Version=$Version", '-o', (Join-Path $ReleaseDir 'nuget'))
        Invoke-Native dotnet @('pack', (Join-Path $Root 'src/PluginUI.Design/PluginUI.Design.csproj'), '-c', $Configuration, "-p:Version=$Version", '-o', (Join-Path $ReleaseDir 'nuget'))
    }

    # Checksums of the downloadable files.
    # (the symbol packages go to nuget.org only, they are not release downloads)
    $sums = Get-ChildItem $ReleaseDir -File -Recurse | Where-Object { $_.Name -ne 'SHA256SUMS.txt' -and $_.Extension -ne '.snupkg' } | Sort-Object Name | ForEach-Object {
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
    }
    Set-Content -Path (Join-Path $ReleaseDir 'SHA256SUMS.txt') -Value $sums -Encoding utf8
    Write-Host "Release files in $ReleaseDir"
}

# ---------------------------------------------------------------------------------------------- Validate

function Invoke-Validate {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $validator = Join-Path $Root 'build/ValidatePlugin'
    $work = Join-Path $Artifacts 'work'
    $checks = @()
    if ($ValidatePath) {
        foreach ($p in $ValidatePath) { $checks += @{ Path = $p; Level = $null; Required = $true } }
    }
    elseif ($arch -eq 'Arm64') {
        $checks += @{ Path = (Join-Path $work "arm64/$PluginName.vst3"); Level = $null; Required = $false }
        $checks += @{ Path = (Join-Path $work "universal/$PluginName.vst3"); Level = $null; Required = $false }
    }
    else {
        foreach ($t in 'x64-v3', 'x64-v2') { $checks += @{ Path = (Join-Path $work "$t/$PluginName.vst3"); Level = $null; Required = $false } }
        # The loader: the build chosen for this processor, then the x86-64-v2 build forced.
        $checks += @{ Path = (Join-Path $work "universal/$PluginName.vst3"); Level = $null; Required = $false }
        $checks += @{ Path = (Join-Path $work "universal/$PluginName.vst3"); Level = 'v2'; Required = $false }
    }
    $failed = 0
    $ran = 0
    foreach ($check in $checks) {
        $path = $check.Path
        if (-not (Test-Path $path)) {
            if ($check.Required) { Write-Host "Not found: $path"; $failed++ } else { Write-Warning "Not built, skipped: $path" }
            continue
        }
        if (Test-Path $path -PathType Leaf) {
            # The validator expects a bundle: wrap a single plug-in file into one for this architecture.
            $vstArch = if ($arch -eq 'Arm64') { 'arm64-win' } else { 'x86_64-win' }
            $name = [IO.Path]::GetFileNameWithoutExtension($path)
            $bundle = Join-Path $work "validate/$ran/$name.vst3"
            New-Item -ItemType Directory -Force -Path (Join-Path $bundle "Contents/$vstArch") | Out-Null
            Copy-Item $path (Join-Path $bundle "Contents/$vstArch/$name.vst3") -Force
            $path = $bundle
        }
        if ($check.Level) { $env:PLUGINUI_X64_LEVEL = $check.Level } else { Remove-Item Env:PLUGINUI_X64_LEVEL -ErrorAction SilentlyContinue }
        Write-Host "Validating $path$(if ($check.Level) { " (PLUGINUI_X64_LEVEL=$($check.Level))" })"
        & dotnet run --project $validator -c $Configuration -- $path | Out-Host
        if ($LASTEXITCODE -ne 0) { $failed++ }
        $ran++
    }
    Remove-Item Env:PLUGINUI_X64_LEVEL -ErrorAction SilentlyContinue
    if ($failed -gt 0) { throw "$failed validation(s) failed." }
    if ($ran -eq 0) { throw 'Nothing was validated: build and package first, or give -ValidatePath.' }
    Write-Host "$ran validation(s) passed."
}

# ------------------------------------------------------------------------------------------------- Main

if ($Stage -in 'All', 'Build') { foreach ($t in $Target) { Invoke-Build $t } }
if ($Stage -in 'All', 'Package') { Invoke-Package }
if ($Stage -in 'All', 'Validate') { Invoke-Validate }
