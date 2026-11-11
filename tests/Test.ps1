# Builds the headless tests into a temporary folder and runs them against an in-memory PC and a temporary data folder.
# Nothing here touches the real PC, the installer queue or C:\dev\Windows\build.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test.ps1 [suite-name-filter]
# Exit code 0 = every test passed.
param([string]$Filter)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('TweakBench-tests-' + [Guid]::NewGuid().ToString('N'))
$out = Join-Path $root 'out'
$data = Join-Path $root 'data'
$buildScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'Build.ps1'
$oldStage = $env:TB_STAGE_ROOT
$oldData = $env:TWEAKBENCH_DATA
$exitCode = 1
try {
    New-Item -ItemType Directory -Path $out, $data -Force | Out-Null
    $env:TB_STAGE_ROOT = Join-Path $root 'stage'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -OutputDirectory $out -Test
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
    $env:TWEAKBENCH_DATA = $data
    $exe = Join-Path $out 'TweakBench.Tests.exe'
    if ($Filter) { & $exe $Filter } else { & $exe }
    $exitCode = $LASTEXITCODE
}
catch {
    Write-Output ("ERROR: " + $_.Exception.Message)
    $exitCode = 1
}
finally {
    $env:TB_STAGE_ROOT = $oldStage
    $env:TWEAKBENCH_DATA = $oldData
    try { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}
exit $exitCode
