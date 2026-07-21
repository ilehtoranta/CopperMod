param(
    [string]$Cases = "default",
    [string]$Root = (Join-Path $PSScriptRoot "..\third_party\vAmigaTS"),
    [string]$Configuration = "Release",
    [int]$MaxFrames = 180,
    [string]$KickstartRom,
    [switch]$CompareRaw,
    [switch]$DumpRaw,
    [switch]$HardwareSpecialization,
    [switch]$StopOnFirstFailure,
    [switch]$TraceWrites,
    [switch]$TracePresentation,
    [switch]$CaptureBusAccesses,
	[int]$CaseTimeoutSeconds = 120,
	[string]$ProgressPath = (Join-Path $PSScriptRoot "..\TestResults\vamigats-progress.log"),
	[string]$ResultsPath = (Join-Path $PSScriptRoot "..\TestResults\vamigats-results.jsonl"),
    [switch]$Fetch
)

$ErrorActionPreference = "Stop"

if ($Fetch) {
    & (Join-Path $PSScriptRoot "fetch-vamigats.ps1") -Destination $Root
}

$resolvedRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Root)
if (-not (Test-Path -LiteralPath $resolvedRoot)) {
    throw "vAmigaTS root not found: $resolvedRoot. Run scripts\fetch-vamigats.ps1 first or pass -Fetch."
}

$previousRoot = $env:COPPER_AMIGA_VAMIGATS_ROOT
$previousCases = $env:COPPER_AMIGA_VAMIGATS_CASES
$previousMaxFrames = $env:COPPER_AMIGA_VAMIGATS_MAX_FRAMES
$previousKickstartRom = $env:COPPER_AMIGA_VAMIGATS_KICK13_ROM
$previousCompareRaw = $env:COPPER_AMIGA_VAMIGATS_COMPARE_RAW
$previousDumpRaw = $env:COPPER_AMIGA_VAMIGATS_DUMP_RAW
$previousHardwareSpecialization = $env:COPPER_AMIGA_VAMIGATS_HARDWARE_SPECIALIZATION
$previousStopOnFirstFailure = $env:COPPER_AMIGA_VAMIGATS_STOP_ON_FIRST_FAILURE
$previousTraceWrites = $env:COPPER_AMIGA_VAMIGATS_TRACE_WRITES
$previousTracePresentation = $env:COPPER_AMIGA_VAMIGATS_TRACE_PRESENTATION
$previousCaptureBusAccesses = $env:COPPER_AMIGA_TRACE_BUS_ACCESSES
$previousSkipRawOffsetScan = $env:COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN
$previousCaseTimeoutSeconds = $env:COPPER_AMIGA_VAMIGATS_CASE_TIMEOUT_SECONDS
$previousProgressPath = $env:COPPER_AMIGA_VAMIGATS_PROGRESS_PATH
$previousResultsPath = $env:COPPER_AMIGA_VAMIGATS_RESULTS_PATH
$exitCode = 0

try {
    $env:COPPER_AMIGA_VAMIGATS_ROOT = $resolvedRoot
    $env:COPPER_AMIGA_VAMIGATS_CASES = $Cases
    $env:COPPER_AMIGA_VAMIGATS_MAX_FRAMES = $MaxFrames.ToString([Globalization.CultureInfo]::InvariantCulture)
    if ([string]::IsNullOrWhiteSpace($KickstartRom)) {
        $env:COPPER_AMIGA_VAMIGATS_KICK13_ROM = $previousKickstartRom
    } else {
        $resolvedKickstartRom = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($KickstartRom)
        if (-not (Test-Path -LiteralPath $resolvedKickstartRom -PathType Leaf)) {
            throw "Kickstart ROM not found: $resolvedKickstartRom"
        }

        $env:COPPER_AMIGA_VAMIGATS_KICK13_ROM = $resolvedKickstartRom
    }

    $env:COPPER_AMIGA_VAMIGATS_COMPARE_RAW = if ($CompareRaw) { "1" } else { "0" }
    $env:COPPER_AMIGA_VAMIGATS_DUMP_RAW = if ($DumpRaw) { "1" } else { "0" }
    $env:COPPER_AMIGA_VAMIGATS_HARDWARE_SPECIALIZATION = if ($HardwareSpecialization) { "1" } else { "0" }
    $env:COPPER_AMIGA_VAMIGATS_STOP_ON_FIRST_FAILURE = if ($StopOnFirstFailure) { "1" } else { "0" }
    $env:COPPER_AMIGA_VAMIGATS_TRACE_WRITES = if ($TraceWrites) { "1" } else { "0" }
    $env:COPPER_AMIGA_VAMIGATS_TRACE_PRESENTATION = if ($TracePresentation) { "1" } else { "0" }
    $env:COPPER_AMIGA_TRACE_BUS_ACCESSES = if ($CaptureBusAccesses) { "1" } else { "0" }
	$env:COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN = "1"
	$env:COPPER_AMIGA_VAMIGATS_CASE_TIMEOUT_SECONDS = $CaseTimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
	$env:COPPER_AMIGA_VAMIGATS_PROGRESS_PATH = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ProgressPath)
	$env:COPPER_AMIGA_VAMIGATS_RESULTS_PATH = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultsPath)

	Write-Host "vAmigaTS progress: $env:COPPER_AMIGA_VAMIGATS_PROGRESS_PATH"
	Write-Host "vAmigaTS results:  $env:COPPER_AMIGA_VAMIGATS_RESULTS_PATH"
	Write-Host "vAmigaTS per-case timeout: $CaseTimeoutSeconds seconds"

    dotnet test (Join-Path $PSScriptRoot "..\CopperScreen.Tests\CopperScreen.Tests.csproj") `
        -c $Configuration `
        --filter "FullyQualifiedName~VAmigaTsAdfCorpusTests" `
        --logger "console;verbosity=normal"
    $exitCode = $LASTEXITCODE
} finally {
    $env:COPPER_AMIGA_VAMIGATS_ROOT = $previousRoot
    $env:COPPER_AMIGA_VAMIGATS_CASES = $previousCases
    $env:COPPER_AMIGA_VAMIGATS_MAX_FRAMES = $previousMaxFrames
    $env:COPPER_AMIGA_VAMIGATS_KICK13_ROM = $previousKickstartRom
    $env:COPPER_AMIGA_VAMIGATS_COMPARE_RAW = $previousCompareRaw
    $env:COPPER_AMIGA_VAMIGATS_DUMP_RAW = $previousDumpRaw
    $env:COPPER_AMIGA_VAMIGATS_HARDWARE_SPECIALIZATION = $previousHardwareSpecialization
    $env:COPPER_AMIGA_VAMIGATS_STOP_ON_FIRST_FAILURE = $previousStopOnFirstFailure
    $env:COPPER_AMIGA_VAMIGATS_TRACE_WRITES = $previousTraceWrites
    $env:COPPER_AMIGA_VAMIGATS_TRACE_PRESENTATION = $previousTracePresentation
    $env:COPPER_AMIGA_TRACE_BUS_ACCESSES = $previousCaptureBusAccesses
	$env:COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN = $previousSkipRawOffsetScan
	$env:COPPER_AMIGA_VAMIGATS_CASE_TIMEOUT_SECONDS = $previousCaseTimeoutSeconds
	$env:COPPER_AMIGA_VAMIGATS_PROGRESS_PATH = $previousProgressPath
	$env:COPPER_AMIGA_VAMIGATS_RESULTS_PATH = $previousResultsPath
}

if ($exitCode -ne 0) {
    exit $exitCode
}
