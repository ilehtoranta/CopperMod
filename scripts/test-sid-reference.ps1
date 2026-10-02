param(
    [string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
$sidRepoRoot = Split-Path -Parent $PSScriptRoot
$sidToolRoot = Join-Path $sidRepoRoot 'third_party/sidplayfp-3.0.2-ucrt64'
$sidExecutable = Join-Path $sidToolRoot 'sidplayfp.exe'
$sidArchive = Join-Path $sidToolRoot 'sidplayfp-3.0.2-ucrt64.zip'
$sidDownloadUrl = 'https://github.com/libsidplayfp/sidplayfp/releases/download/v3.0.2/sidplayfp-3.0.2-ucrt64.zip'
$sidArchiveSha256 = 'dadd0f0ec352c1382c24728193dd293946e40c120a843295169acfb44c247173'
$sidManifest = Get-Content (Join-Path $sidRepoRoot 'CopperMod.Sid.Tests/ConformanceFixtures/manifest.json') -Raw | ConvertFrom-Json
if ($sidManifest.evidence.referenceVersion -ne '3.0.2-ucrt64') {
    throw 'Update the reference downloader to match the conformance manifest version.'
}

if (-not (Test-Path -LiteralPath $sidExecutable -PathType Leaf)) {
    New-Item -ItemType Directory -Path $sidToolRoot -Force | Out-Null
    Invoke-WebRequest -Uri $sidDownloadUrl -OutFile $sidArchive
    if ((Get-FileHash -LiteralPath $sidArchive -Algorithm SHA256).Hash -ine $sidArchiveSha256) {
        throw 'sidplayfp release archive checksum mismatch.'
    }
    Expand-Archive -LiteralPath $sidArchive -DestinationPath $sidToolRoot
}
$sidExecutableSha256 = (Get-FileHash -LiteralPath $sidExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sidExecutableSha256 -ne $sidManifest.evidence.referenceSha256) {
    throw 'sidplayfp executable does not match the pinned conformance reference.'
}

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $sidRepoRoot 'artifacts/sid-reference'
}
$sidResultsPath = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Path $sidResultsPath -Force | Out-Null

# Windows sidplayfp reads an INI beside its executable before the user's INI.
# Keep reference settings isolated from personal playback settings and ROM paths.
$sidIniPath = Join-Path $sidToolRoot 'sidplayfp.ini'
@'
; Isolated defaults for CopperMod external-reference tests.
[SIDPlayfp]
Version=1
[Console]
ANSI=false
[Audio]
Channels=1
[Emulation]
Engine=RESIDFP
C64Model=PAL
CiaModel=MOS6526
SidModel=MOS6581
DigiBoost=false
UseFilter=true
FilterCurve6581=0.5
FilterRange6581=0.5
FilterCurve8580=0.5
CombinedWaveforms=AVERAGE
Old6581Caps=false
PowerOnDelay=0
'@ | Set-Content -LiteralPath $sidIniPath -Encoding ascii
Copy-Item -LiteralPath $sidIniPath -Destination (Join-Path $sidResultsPath 'sidplayfp.ini') -Force
[ordered]@{
    schema = 1
    authority = 'sidplayfp-emulator-comparison'
    recordedUtc = [DateTime]::UtcNow.ToString('o')
    version = $sidManifest.evidence.referenceVersion
    bundledLibsidplayfpVersion = '3.0.1'
    downloadUrl = $sidDownloadUrl
    archiveSha256 = $sidArchiveSha256
    executableSha256 = $sidExecutableSha256
    configurationSha256 = (Get-FileHash -LiteralPath $sidIniPath -Algorithm SHA256).Hash.ToLowerInvariant()
    candidateProfiles = @{
        waveformAndConformance = 'ReferenceMeasured'
        weakSpotAdsrResetAndPolarity = 'Balanced'
        d418Sine = @('Balanced', 'ReferenceMeasured')
    }
    conformanceSampleRate = 48000
    generatedOracleSampleRate = 96000
    notes = 'Compatibility diagnostics; several tests use fitted alignment or level normalization. Passing does not establish cycle-exact hardware equivalence.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sidResultsPath 'reference-provenance.json') -Encoding utf8

$sidEnvironment = @{
    SIDPLAYFP_EXE = $sidExecutable
    SIDPLAYFP_ORACLE_TESTS = '1'
    SIDPLAYFP_8580_ORACLE_TESTS = '1'
    SID_CONFORMANCE_ORACLE_TESTS = '1'
    SID_CONFORMANCE_REPORT_DIR = (Join-Path $sidResultsPath 'conformance')
    SIDPLAYFP_ORACLE_REPORT = (Join-Path $sidResultsPath 'waveforms-6581.csv')
    SIDPLAYFP_8580_ORACLE_REPORT = (Join-Path $sidResultsPath 'waveforms-8580.csv')
    SIDPLAYFP_WEAKSPOT_ORACLE_REPORT = (Join-Path $sidResultsPath 'weakspots.csv')
    SIDPLAYFP_ADSR_RESTART_REPORT = (Join-Path $sidResultsPath 'adsr-restart.csv')
    SIDPLAYFP_ADSR_TRACE_REPORT = (Join-Path $sidResultsPath 'adsr-trace.csv')
    SIDPLAYFP_RESET_TRANSIENT_REPORT = (Join-Path $sidResultsPath 'reset-transient.csv')
    SIDPLAYFP_SINE_REPORT = (Join-Path $sidResultsPath 'd418-sine.csv')
    SIDPLAYFP_SINE_ARTIFACT_DIR = (Join-Path $sidResultsPath 'd418-sine')
    SIDPLAYFP_POLARITY_PROBE_REPORT = (Join-Path $sidResultsPath 'polarity.csv')
    SIDPLAYFP_POLARITY_PROBE_ARTIFACT_DIR = (Join-Path $sidResultsPath 'polarity')
}
$sidPreviousEnvironment = @{}
try {
    foreach ($sidKey in $sidEnvironment.Keys) {
        $sidPreviousEnvironment[$sidKey] = [Environment]::GetEnvironmentVariable($sidKey, 'Process')
        [Environment]::SetEnvironmentVariable($sidKey, $sidEnvironment[$sidKey], 'Process')
    }
    dotnet test (Join-Path $sidRepoRoot 'CopperMod.Sid.Tests/CopperMod.Sid.Tests.csproj') --configuration Release --filter 'FullyQualifiedName~SidPlayFpWaveformOracleTests|FullyQualifiedName~OptionalSidConformanceFixturesCompareAgainstSidPlayFp' --logger 'trx;LogFileName=sid-reference.trx' --results-directory $sidResultsPath
    if ($LASTEXITCODE -ne 0) { throw "SID external reference checks failed. Reports: $sidResultsPath" }
} finally {
    foreach ($sidKey in $sidPreviousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($sidKey, $sidPreviousEnvironment[$sidKey], 'Process')
    }
}
