param(
    [string]$CorpusRoot = (Join-Path $PSScriptRoot '..\third_party\vAmigaTS'),
    [string]$KickstartRom = 'C:\D-drive\TestData\ROM\Kickstart_13.rom'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repositoryRoot 'CopperScreen.Tests\CopperScreen.Tests.csproj'
$testFilter = 'FullyQualifiedName~SelectedVAmigaTsAdfImagesRunWithoutFatalBootStatusWhenCorpusIsAvailable'
$cycleCases = @(
    'Agnus/Registers/VPOS/cycle01v/cycle01v.adf',
    'Agnus/Registers/VPOS/cycle01vh/cycle01vh.adf',
    'Agnus/Registers/VPOS/cycleD9v/cycleD9v.adf',
    'Agnus/Registers/VPOS/cycleD9vh/cycleD9vh.adf'
)

function Invoke-TimingTest([bool]$Build) {
    $arguments = @('test', $testProject)
    $arguments += if ($Build) { '--no-restore' } else { '--no-build' }
    $arguments += @('--filter', $testFilter, '--logger', 'console;verbosity=minimal')
    $process = Start-Process dotnet -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Timing test failed with exit code $($process.ExitCode)."
    }
}

$env:COPPER_AMIGA_VAMIGATS_ROOT = (Resolve-Path $CorpusRoot).Path
$env:COPPER_AMIGA_VAMIGATS_KICK13_ROM = (Resolve-Path $KickstartRom).Path
$env:COPPER_AMIGA_VAMIGATS_COMPARE_RAW = '1'
$env:COPPER_AMIGA_VAMIGATS_HARDWARE_SPECIALIZATION = '1'
$env:COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN = '1'
$env:COPPER_AMIGA_VAMIGATS_MAX_FRAMES = '450'
$env:COPPER_AMIGA_VAMIGATS_CASES = $cycleCases -join ';'

Invoke-TimingTest -Build $true

$env:COPPER_AMIGA_VAMIGATS_CASES = 'Agnus/Registers/VPOS/cycleD9v/cycleD9v.adf'
foreach ($maxFrames in 447..453) {
    $env:COPPER_AMIGA_VAMIGATS_MAX_FRAMES = [string]$maxFrames
    $captureFrame = $maxFrames + 3
    Invoke-TimingTest -Build $false
    Write-Host "cycleD9v capture frame $captureFrame exact"
}
