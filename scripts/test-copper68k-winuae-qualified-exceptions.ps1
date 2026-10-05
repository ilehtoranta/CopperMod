#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('TrapBounds','Breakpoints')] [string] $Preset = 'TrapBounds',
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $InputDirectory,
    [string] $NativeLibrary,
    [string] $OutputDirectory = 'artifacts/winuae-trap-bounds-audit'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = (Resolve-Path -LiteralPath $InputDirectory).Path
if (-not $NativeLibrary) { $NativeLibrary = Join-Path $root 'm68k_cpu_tester.dll' }
$library = (Resolve-Path -LiteralPath $NativeLibrary).Path
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$key = if ($Preset -eq 'TrapBounds') {'TRAP_BOUNDS'} else {'BREAKPOINT'}
$testName = if ($Preset -eq 'TrapBounds') {'WinUaeTrapAndBoundsAcrossAdvancedModelsWhenEnabled'} else {'WinUaeBreakpointExceptionsAcrossSelectedModelsWhenEnabled'}
$reportName = if ($Preset -eq 'TrapBounds') {'winuae-trap-bounds-audit.json'} else {'winuae-breakpoint-audit.json'}
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
    & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --filter "FullyQualifiedName~$testName" --logger 'trx;LogFileName=audit.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw "Qualified $Preset audit failed" }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $output 'audit.trx') -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.executed -ne 1 -or [int]$counts.passed -ne 1) { throw "$Preset audit did not execute its required test" }
    if (-not (Test-Path -LiteralPath (Join-Path $output $reportName))) { throw "$Preset coverage report is missing" }
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
