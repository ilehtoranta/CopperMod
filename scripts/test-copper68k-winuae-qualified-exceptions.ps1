#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('TrapBounds','Breakpoints','WordDivision','LowPowerStop','Moves','Cas','Cas2','CacheEncodings','Move16')] [string] $Preset = 'TrapBounds',
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $InputDirectory,
    [string] $NativeLibrary,
    [string] $OutputDirectory = 'artifacts/winuae-trap-bounds-audit',
    [string] $ArtifactsPath
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = (Resolve-Path -LiteralPath $InputDirectory).Path
if (-not $NativeLibrary) { $NativeLibrary = Join-Path $root 'm68k_cpu_tester.dll' }
$library = (Resolve-Path -LiteralPath $NativeLibrary).Path
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$build = if ($ArtifactsPath) { [IO.Path]::GetFullPath($ArtifactsPath, $repo) } else { Join-Path $output 'build' }
$key = switch ($Preset) {'TrapBounds' {'TRAP_BOUNDS'} 'Breakpoints' {'BREAKPOINT'} 'WordDivision' {'WORD_DIVISION'} 'LowPowerStop' {'LPSTOP'} 'Moves' {'MOVES'} 'Cas' {'CAS'} 'Cas2' {'CAS2'} 'CacheEncodings' {'CACHE_ENCODINGS'}}
$testName = switch ($Preset) {'TrapBounds' {'WinUaeTrapAndBoundsAcrossAdvancedModelsWhenEnabled'} 'Breakpoints' {'WinUaeBreakpointExceptionsAcrossSelectedModelsWhenEnabled'} 'WordDivision' {'WinUaeWordDivisionAcrossSelectedModelsWhenEnabled'} 'LowPowerStop' {'WinUaeLowPowerStopExceptionsWhenEnabled'} 'Moves' {'WinUaeMovesAcrossSelectedModelsWhenEnabled'} 'Cas' {'WinUaeCasAcrossAdvancedModelsWhenEnabled'} 'Cas2' {'WinUaeCas2AcrossAdvancedModelsWhenEnabled'} 'CacheEncodings' {'WinUaeCacheScopeZeroAcrossSelectedModelsWhenEnabled'}}
$reportName = switch ($Preset) {'TrapBounds' {'winuae-trap-bounds-audit.json'} 'Breakpoints' {'winuae-breakpoint-audit.json'} 'WordDivision' {'winuae-word-division-audit.json'} 'LowPowerStop' {'winuae-lpstop-audit.json'} 'Moves' {'winuae-moves-audit.json'} 'Cas' {'winuae-cas-audit.json'} 'Cas2' {'winuae-cas2-audit.json'} 'CacheEncodings' {'winuae-cache-encodings-audit.json'}}
if ($Preset -eq 'Move16') {
    $key = 'MOVE16'
    $testName = 'WinUaeMove16AcrossSupportedModelsWhenEnabled'
    $reportName = 'winuae-move16-audit.json'
}
$filter = "FullyQualifiedName~$testName"
$expectedTests = 1
if ($Preset -eq 'Move16') {
    $filter += '|FullyQualifiedName~M68kWinUaeMove16EncodingTests'
    $expectedTests = 24
}
if ($Preset -eq 'WordDivision') {
    $filter += '|FullyQualifiedName~M68kWinUaeWordDivisionEncodingTests'
    $expectedTests = 15
}
if ($Preset -eq 'LowPowerStop') {
    $filter += '|FullyQualifiedName~M68kWinUaeLowPowerStopEncodingTests'
    $expectedTests = 11
}
if ($Preset -eq 'Moves') {
    $filter += '|FullyQualifiedName~M68kWinUaeMovesEncodingTests'
    $expectedTests = 17
}
if ($Preset -eq 'Cas') {
    $filter += '|FullyQualifiedName~M68kWinUaeCasEncodingTests'
    $expectedTests = 16
}
if ($Preset -eq 'Cas2') {
    $filter += '|FullyQualifiedName~M68kWinUaeCas2EncodingTests'
    $expectedTests = 12
}
if ($Preset -eq 'CacheEncodings') {
    $filter += '|FullyQualifiedName~M68kWinUaeCacheScopeEncodingTests'
    $expectedTests = 13
}
$settings = @{
    ('COPPER68K_RUN_WINUAE_' + $key + '_AUDIT') = '1'
    ('COPPER68K_WINUAE_' + $key + '_PATH') = $root
    ('COPPER68K_WINUAE_' + $key + '_LIBRARY') = $library
    COPPER68K_SYNTHETIC_REPORT_DIR = $output
}
$saved = @{}
try {
    foreach ($name in $settings.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $settings[$name])
    }
    & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path $build --filter $filter --logger 'trx;LogFileName=audit.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw "Qualified $Preset audit failed" }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $output 'audit.trx') -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.executed -ne $expectedTests -or [int]$counts.passed -ne $expectedTests) { throw "$Preset audit did not execute its complete test selection" }
    if (-not (Test-Path -LiteralPath (Join-Path $output $reportName))) { throw "$Preset coverage report is missing" }
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
