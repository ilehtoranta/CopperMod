#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $InputDirectory,
    [string] $NativeLibrary,
    [string] $OutputDirectory = 'artifacts/winuae-long-arithmetic-audit',
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
$settings = @{
    COPPER68K_RUN_WINUAE_LONG_ARITHMETIC_AUDIT = '1'
    COPPER68K_WINUAE_LONG_ARITHMETIC_PATH = $root
    COPPER68K_WINUAE_LONG_ARITHMETIC_LIBRARY = $library
    COPPER68K_SYNTHETIC_REPORT_DIR = $output
}
$saved = @{}
try {
    foreach ($name in $settings.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $settings[$name])
    }
    & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path $build --filter 'FullyQualifiedName~WinUaeLongArithmeticAcrossAdvancedModelsWhenEnabled|FullyQualifiedName~M68kWinUaeLongArithmeticEncodingTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw 'Qualified LongArithmetic audit failed' }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $output 'audit.trx') -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.executed -ne 19 -or [int]$counts.passed -ne 19) { throw 'LongArithmetic audit did not execute its complete test selection' }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'winuae-long-arithmetic-audit.json'))) { throw 'LongArithmetic coverage report is missing' }
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
