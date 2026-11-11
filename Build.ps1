# Builds Tweak Bench.exe (and the small helper Tweak Bench Notifications.exe) with the Roslyn C# compiler against .NET Framework 4.8 (inbox on every Windows 10/11 PC).
#   powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1                          app into this folder
#   powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -OutputDirectory build\out
#   powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -OutputDirectory build\out -Test
# -Test also compiles tests\*.cs with every source folder except src\App (the WPF shell) into TweakBench.Tests.exe
# (in -OutputDirectory, or build\ when building into this folder). Set TB_STAGE_ROOT to compile in a private folder
# (parallel development builds); leftovers of other builds are then left alone.
# Installers are never built here: the 4:00 AM Daily App Installer Queue does that.
param([string]$OutputDirectory = $PSScriptRoot, [switch]$Test)
$ErrorActionPreference = 'Stop'
# Compile in the background without competing with interactive apps (csc inherits the class).
try { [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal' } catch {}

# Shared sources: a staged _shared copy (installer queue) wins over the live App Maintenance folder.
$sharedRoot = @((Join-Path $PSScriptRoot '_shared'), 'C:\dev\App Maintenance\shared') | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Build.Common.ps1') } | Select-Object -First 1
if (-not $sharedRoot) { throw 'Shared app source is missing: C:\dev\App Maintenance\shared' }
. (Join-Path $sharedRoot 'Build.Common.ps1')
$cleanupName = 'Tweak Bench'
$exeName = 'Tweak Bench.exe'

# The inbox csc is C# 5 only. Tweak Bench uses the Roslyn compiler from the VS2022 build tools (build-time only);
# the program it produces needs nothing but .NET Framework 4.8.
function Find-Roslyn {
    $candidates = @($env:TB_ROSLYN_CSC, 'C:\dev\Toolchains\VS2022\MSBuild\Current\Bin\Roslyn\csc.exe')
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($root) { $candidates += @(Get-ChildItem -Path (Join-Path $root 'Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe') -ErrorAction SilentlyContinue | ForEach-Object FullName) }
    }
    $found = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $found) { throw 'The Roslyn C# compiler was not found. Expected C:\dev\Toolchains\VS2022\MSBuild\Current\Bin\Roslyn\csc.exe (or set TB_ROSLYN_CSC to a Roslyn csc.exe, for example from Visual Studio 2022 Build Tools).' }
    return $found
}

$compiler = Find-Roslyn
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll',
    'System.Xml.dll', 'System.Xaml.dll', 'System.Management.dll', 'System.ServiceProcess.dll', 'Microsoft.CSharp.dll',
    'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'WPF\WindowsBase.dll', 'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll'
) | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$common = @('-noconfig', '-nologo', '-langversion:latest', '-nullable-', '-platform:x64', '-optimize+', '-debug-', '-codepage:65001', '-utf8output', '-nowarn:1591')

function Invoke-Roslyn([string]$What, [string[]]$Arguments) {
    & $compiler @common @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$What compilation failed." }
}

function Install-Binary([string]$Candidate, [string]$Target, [string]$Stage) {
    # The replaced file stays in the staging folder; Move-BuildLeftovers sends it to C:\dev\_cleanup next build.
    if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($Candidate, $Target, (Join-Path $Stage 'previous.exe')) } else { [IO.File]::Move($Candidate, $Target) }
    if (-not (Get-ChildItem -LiteralPath $Stage -Force)) { [IO.Directory]::Delete($Stage) }
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# Earlier staging folders (each holds that build's replaced exe) and *-previous-*.exe files are moved
# to C:\dev\_cleanup\Tweak Bench\build-leftovers (never deleted); files still in use wait for the next build.
# TB_STAGE_ROOT (parallel development builds) compiles in a private folder and leaves other builds' folders alone.
if (-not $env:TB_STAGE_ROOT) { Move-BuildLeftovers -AppRoot $PSScriptRoot -App $cleanupName -Folders @($OutputDirectory) }

$src = Join-Path $PSScriptRoot 'src'
function New-Stage {
    $base = if ($env:TB_STAGE_ROOT) { $env:TB_STAGE_ROOT } else { Join-Path $PSScriptRoot 'build' }
    $s = Join-Path $base ('compile-' + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $s -Force | Out-Null; return $s
}

# --- the app ---------------------------------------------------------------------------------------
$stage = New-Stage
$candidate = Join-Path $stage $exeName
$sources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' -File -Recurse | ForEach-Object FullName)
$sources += @(Get-SharedSource @('AppRestart.cs', 'SingleInstance.cs', 'ConfigurationTransfer.cs', 'ReleaseNotes.cs', 'ReleaseAttributes.cs'))
$resources = @(
    ('/resource:' + (Join-Path $PSScriptRoot 'MainWindow.xaml') + ',MainWindow.xaml'),
    ('/resource:' + (Join-Path $PSScriptRoot 'CHANGELOG.md') + ',App.Changelog.md')
)
$png = Join-Path $PSScriptRoot 'assets\TB.png'
if (Test-Path -LiteralPath $png) { $resources += ('/resource:' + $png + ',App.Icon.png') }
$icon = Join-Path $PSScriptRoot 'assets\TB.ico'
$iconArgument = @(); if (Test-Path -LiteralPath $icon) { $iconArgument = @('-win32icon:' + $icon) }
Invoke-Roslyn 'Tweak Bench' (@('-target:winexe', ('-win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('-out:' + $candidate)) + $iconArgument + $resources + $references + $sources)
$target = Join-Path $OutputDirectory $exeName
Install-Binary $candidate $target $stage

# --- the notification helper -----------------------------------------------------------------------
# "Tweak Bench Notifications.exe": no WPF, no window. Core + Notify + helper sources, plus a version/attributes source generated
# here from src\App\Release.cs so its version always equals the app's. It sits next to the app exe (and in the project root).
$releaseText = Get-Content -LiteralPath (Join-Path $src 'App\Release.cs') -Raw
$versionMatch = [regex]::Match($releaseText, 'class Release[^{]*\{\s*public const string Version\s*=\s*"(\d+\.\d+\.\d+)"')
if (-not $versionMatch.Success) { throw 'src\App\Release.cs: Release.Version was not found.' }
$helperVersion = $versionMatch.Groups[1].Value
$helperName = 'Tweak Bench Notifications.exe'
$helperStage = New-Stage
$helperCandidate = Join-Path $helperStage $helperName
$helperAttributes = Join-Path $helperStage 'HelperVersion.g.cs'
@"
using System.Reflection;

[assembly: AssemblyVersion("$helperVersion")]
[assembly: AssemblyFileVersion("$helperVersion")]
[assembly: AssemblyInformationalVersion("$helperVersion")]
"@ | Set-Content -LiteralPath $helperAttributes -Encoding UTF8
$helperSources = @('Core', 'Notify' | ForEach-Object { Get-ChildItem -LiteralPath (Join-Path $src $_) -Filter '*.cs' -File -Recurse | ForEach-Object FullName })
$helperSources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'helper') -Filter '*.cs' -File | ForEach-Object FullName)
$helperSources += $helperAttributes
Invoke-Roslyn 'Tweak Bench Notifications' (@('-target:winexe', ('-win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('-out:' + $helperCandidate)) + $iconArgument + $references + $helperSources)
$helperTarget = Join-Path $OutputDirectory $helperName
Install-Binary $helperCandidate $helperTarget $helperStage

if ($OutputDirectory -ne $PSScriptRoot) {
    foreach ($item in @('assets', 'README.md', 'CHANGELOG.md', 'Tweak Bench.exe.config')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $item) -Destination $OutputDirectory -Recurse -Force }
}
Copy-TransferHelper -Destination $OutputDirectory -App $cleanupName
Write-Output "Built app: $target and $helperTarget. Installer files were not changed."
# Project rule (2026-11-11): the project-root exe is always the latest build, so a build into any other
# folder also rebuilds C:\dev\Windows\Tweak Bench.exe. Queue stages are skipped by the helper itself. TB_STAGE_ROOT builds
# (several agents compiling in parallel) skip it; whoever finishes such a batch builds the root once with plain Build.ps1.
if (-not $env:TB_STAGE_ROOT) { Update-ProjectRoot -AppRoot $PSScriptRoot -OutputDirectory $OutputDirectory -BuildScript $PSCommandPath }

# --- tests -----------------------------------------------------------------------------------------
if ($Test) {
    $stage = New-Stage
    $candidate = Join-Path $stage 'TweakBench.Tests.exe'
    # Every source folder except the WPF app shell is under test; new layers need no Build.ps1 change.
    $sources = @(Get-ChildItem -LiteralPath $src -Directory | Where-Object Name -ne 'App' | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.cs' -File -Recurse | ForEach-Object FullName })
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' -File | ForEach-Object FullName)
    Invoke-Roslyn 'Tests' (@('-target:exe', ('-win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('-out:' + $candidate)) + $references + $sources)
    $testTarget = if ($OutputDirectory -eq $PSScriptRoot) { Join-Path $PSScriptRoot 'build\TweakBench.Tests.exe' } else { Join-Path $OutputDirectory 'TweakBench.Tests.exe' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $testTarget) -Force | Out-Null
    Install-Binary $candidate $testTarget $stage
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Tweak Bench.exe.config') -Destination ($testTarget + '.config') -Force
    Write-Output "Built tests: $testTarget"
}
