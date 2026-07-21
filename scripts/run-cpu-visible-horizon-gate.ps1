[CmdletBinding()]
param(
    [ValidateSet('All', 'Lemmings', 'HiredGuns', 'ShadowBeast', 'Workbench')]
    [string[]] $Workload = @('All'),
    [ValidateRange(1, 20)]
    [int] $Repeat = 3,
    [switch] $NoBuild,
    [switch] $SkipRegressionTests,
    [switch] $Quick,
    [switch] $DeferredCpuBusBatch,
    [switch] $DeferredCpuChipReadSegments,
    [ValidatePattern('^[0-9A-Fa-f]{8}$')]
    [string] $LemmingsFramebufferChecksum = '03E3F1D1',
    [ValidatePattern('^[0-9A-Fa-f]{8}$')]
    [string] $LemmingsAudioChecksum = 'FA40F11D',
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'CopperMod.sln'
$benchmarkProject = Join-Path $repositoryRoot 'CopperScreen.Benchmarks\CopperScreen.Benchmarks.csproj'
$amigaTestProject = Join-Path $repositoryRoot 'CopperMod.Amiga.Tests\CopperMod.Amiga.Tests.csproj'
$benchmarkDll = Join-Path $repositoryRoot 'CopperScreen.Benchmarks\bin\Release\net10.0\CopperScreen.Benchmarks.dll'
$resultsFile = Join-Path $repositoryRoot 'CopperMod.Amiga\CPU_VISIBLE_HORIZON_BASELINES.md'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\cpu-visible-horizon\$stamp"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

if (-not $NoBuild) {
    $buildLog = Join-Path $OutputDirectory 'build.log'
    & dotnet build $solution -c Release 2>&1 | Tee-Object -FilePath $buildLog
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $benchmarkDll)) {
    throw "Benchmark binary does not exist: $benchmarkDll"
}

$selected = if ($Workload -contains 'All') {
    @('Lemmings', 'HiredGuns', 'ShadowBeast', 'Workbench')
} else {
    $Workload
}

$warmup = if ($Quick) { 20 } else { 600 }
$frames = if ($Quick) { 20 } else { 360 }
$effectiveRepeat = if ($Quick) { 1 } else { $Repeat }
$lemmingsChecksums = if ($Quick) {
    @()
} else {
    @(
        '--expect-framebuffer-checksum', $LemmingsFramebufferChecksum,
        '--expect-audio-checksum', $LemmingsAudioChecksum
    )
}
$common = @(
    '--cpu', 'interpreter',
    '--warmup', $warmup,
    '--frames', $frames,
    '--repeat', $effectiveRepeat,
    '--hardware-specialization',
    '--hardware-profile',
    '--skip-phase-profile'
)
if ($DeferredCpuBusBatch) {
    $common += '--cpu-deferred-bus-batch'
}
if ($DeferredCpuChipReadSegments) {
    $common += '--cpu-deferred-chip-read-segments'
}

$definitions = @{
    Lemmings = @{
        Name = 'Lemmings SR'
        Extra = @('--profile', 'expanded-copperstart.json') + $lemmingsChecksums
    }
    HiredGuns = @{
        Name = 'Hired Guns'
        Extra = @('--profile', 'expanded-copperstart.json')
    }
    ShadowBeast = @{
        Name = 'Shadow of the Beast PNA host-exec'
        Extra = @('--profile', 'a500-kickstart31-host-exec.json')
    }
    Workbench = @{
        Name = 'Workbench 1.3'
        Extra = @('--profile', 'expanded-kickstart13.json')
    }
}

$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$branch = (& git -C $repositoryRoot branch --show-current).Trim()
$gitStatus = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
$dirty = if ($gitStatus.Count -gt 0) { 'yes' } else { 'no' }
$dotnetVersion = (& dotnet --version).Trim()
$runtime = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
$os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
$processor = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name).Trim()
$started = Get-Date -Format 'yyyy-MM-dd HH:mm:ss K'

$metadata = @(
    "Started: $started"
    "Commit: $commit"
    "Branch: $branch"
    "Dirty worktree: $dirty"
    "OS: $os"
    "Processor: $processor"
    "Architecture: $architecture"
    "Runtime: $runtime"
    ".NET SDK: $dotnetVersion"
    "Warmup frames: $warmup"
    "Measured frames: $frames"
    "Repeats: $effectiveRepeat"
    "Deferred CPU bus batch: $DeferredCpuBusBatch"
    "Deferred CPU Chip-read segments: $DeferredCpuChipReadSegments"
    "Lemmings framebuffer checksum gate: 0x$LemmingsFramebufferChecksum"
    "Lemmings audio checksum gate: 0x$LemmingsAudioChecksum"
    "Output: $OutputDirectory"
)
$metadata | Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.txt')
$gitStatus | Set-Content -LiteralPath (Join-Path $OutputDirectory 'git-status.txt')

$regressionLog = Join-Path $OutputDirectory 'focused-regressions.log'
if (-not $SkipRegressionTests) {
    # Keep this list explicit. A bare substring such as "Copper" also matches
    # the CopperMod namespace and consequently selects the entire test assembly.
    $focusedRegressionClasses = @(
        'PhysicalBusLedgerTests',
        'AmigaBusTimingTests',
        'AmigaBitplaneConformanceMatrixTests',
        'AmigaSpriteConformanceMatrixTests',
        'AmigaCopperConformanceMatrixTests',
        'AmigaBlitterConformanceMatrixTests',
        'AmigaDiskControllerConformanceMatrixTests',
        'PaulaConformanceMatrixTests'
    )
    $focusedRegressionFilter = ($focusedRegressionClasses | ForEach-Object {
        "FullyQualifiedName~CopperMod.Amiga.Tests.$_."
    }) -join '|'
    $focusedRegressionFilter | Set-Content -LiteralPath (Join-Path $OutputDirectory 'focused-regressions.filter.txt')
    & dotnet test $amigaTestProject -c Release --no-build --filter $focusedRegressionFilter 2>&1 |
        Tee-Object -FilePath $regressionLog
    if ($LASTEXITCODE -ne 0) {
        throw "Focused regression filter failed with exit code $LASTEXITCODE."
    }
}

$failed = $false
$logs = [System.Collections.Generic.List[string]]::new()
foreach ($key in $selected) {
    $definition = $definitions[$key]
    $safeName = $key.ToLowerInvariant()
    $logPath = Join-Path $OutputDirectory "$safeName.tsv"
    $arguments = @($benchmarkDll, '--only', $definition.Name) + $common + $definition.Extra
    Write-Host "Running $($definition.Name)..."
    & dotnet @arguments 2>&1 | Tee-Object -FilePath $logPath
    if ($LASTEXITCODE -ne 0) {
        $failed = $true
        Write-Error -ErrorAction Continue "$($definition.Name) failed with exit code $LASTEXITCODE."
    }
    $logs.Add($logPath)
}

$append = [System.Collections.Generic.List[string]]::new()
$append.Add('')
$append.Add("## $started")
$append.Add('')
foreach ($line in $metadata) {
    $append.Add("- $line")
}
$append.Add('')
$append.Add('### Git status')
$append.Add('')
$append.Add('```text')
if ($gitStatus.Count -eq 0) {
    $append.Add('(clean)')
} else {
    foreach ($line in $gitStatus) {
        $append.Add($line)
    }
}
$append.Add('```')
$append.Add('')
if (Test-Path -LiteralPath $regressionLog) {
    $append.Add('### Focused regressions')
    $append.Add('')
    $append.Add('```text')
    foreach ($line in Get-Content -LiteralPath $regressionLog) {
        $append.Add($line)
    }
    $append.Add('```')
    $append.Add('')
}
foreach ($log in $logs) {
    $append.Add("### $([System.IO.Path]::GetFileNameWithoutExtension($log))")
    $append.Add('')
    $append.Add('```text')
    foreach ($line in Get-Content -LiteralPath $log) {
        $append.Add($line)
    }
    $append.Add('```')
    $append.Add('')
}
$append | Add-Content -LiteralPath $resultsFile

if ($failed) {
    throw 'One or more CPU-visible horizon baseline workloads failed.'
}

Write-Host "Baseline gate completed. Results appended to $resultsFile"
