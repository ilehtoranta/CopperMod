param(
    [Parameter(Mandatory = $true)]
    [string]$CandidateDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ReferenceDirectory,
    [ValidateSet('H2', 'H3a', 'H3b', 'H3c', 'H4a')]
    [string]$GateName = 'H2',
    [ValidateSet('--synthetic-display-dma', '--synthetic-blitter', '--synthetic-blitter-fill', '--synthetic-blitter-line', '--synthetic-sprites')]
    [string]$ActiveWorkload = '--synthetic-display-dma',
    [int]$WarmupFrames = 600,
    [int]$MeasuredFrames = 3600,
    [int]$SamplesPerBuild = 3,
    [int]$LogicalProcessor = 2,
    [int]$ExpectedEfficiencyClass = 1,
    [double]$MinimumActiveFps = 200.0,
    [double]$MinimumInactiveRetentionPercent = 97.0,
    [double]$MinimumActiveRetentionPercent = 0.0,
    [double]$MaximumSpreadPercent = 10.0,
    [int]$PreflightSeconds = 10,
    [int]$CooldownSeconds = 10,
    [double]$CompetingCorePercent = 25.0,
    [double]$CompetingPackagePercent = 25.0,
    [int]$SustainedContentionSeconds = 10
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'agnus-g6-host-load.ps1')

$runnerName = 'CopperMod.Amiga.Lightweight.Runner.dll'
$engineName = 'CopperMod.Amiga.Lightweight.dll'
$candidateDirectory = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$referenceDirectory = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
$candidateRunner = Join-Path $candidateDirectory $runnerName
$referenceRunner = Join-Path $referenceDirectory $runnerName
$affinityMask = [int64]1 -shl $LogicalProcessor

foreach ($path in @($candidateRunner, $referenceRunner)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Lightweight runner not found: $path"
    }
}
if ($SamplesPerBuild -ne 3) {
    throw "The formal $GateName gate requires exactly three samples per build."
}
if ($PreflightSeconds -lt $SustainedContentionSeconds) {
    throw 'Formal preflight must cover the sustained-contention window.'
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class LightweightCpuSetProbe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(
        IntPtr information, uint bufferLength, out uint returnedLength,
        IntPtr process, uint flags);

    public sealed class Entry
    {
        public byte LogicalProcessorIndex { get; set; }
        public byte CoreIndex { get; set; }
        public byte EfficiencyClass { get; set; }
        public ushort Group { get; set; }
    }

    public static Entry[] Read()
    {
        uint length;
        GetSystemCpuSetInformation(IntPtr.Zero, 0, out length, IntPtr.Zero, 0);
        if (length == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        IntPtr buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new List<Entry>();
            int offset = 0;
            while (offset < length)
            {
                IntPtr item = IntPtr.Add(buffer, offset);
                int size = Marshal.ReadInt32(item, 0);
                int type = Marshal.ReadInt32(item, 4);
                if (size <= 0) break;
                if (type == 0)
                {
                    result.Add(new Entry {
                        Group = (ushort)Marshal.ReadInt16(item, 12),
                        LogicalProcessorIndex = Marshal.ReadByte(item, 14),
                        CoreIndex = Marshal.ReadByte(item, 15),
                        EfficiencyClass = Marshal.ReadByte(item, 18)
                    });
                }
                offset += size;
            }
            return result.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
'@

$cpuSets = @([LightweightCpuSetProbe]::Read())
if (@($cpuSets | Where-Object Group -ne 0).Count) {
    throw "$GateName performance INVALID / RERUN: multi-group processor telemetry is unsupported."
}
$selectedCpu = $cpuSets |
    Where-Object LogicalProcessorIndex -eq $LogicalProcessor |
    Select-Object -First 1
if ($null -eq $selectedCpu) {
    throw "Logical CPU $LogicalProcessor was not reported by Windows CPU sets."
}
if ($selectedCpu.EfficiencyClass -ne $ExpectedEfficiencyClass) {
    throw "Logical CPU $LogicalProcessor efficiency class is $($selectedCpu.EfficiencyClass), expected $ExpectedEfficiencyClass."
}
$protectedLogicalProcessors = @($cpuSets |
    Where-Object CoreIndex -eq $selectedCpu.CoreIndex |
    ForEach-Object LogicalProcessorIndex)
$protectedAffinityMask = [int64]0
foreach ($processor in $protectedLogicalProcessors) {
    $protectedAffinityMask = $protectedAffinityMask -bor ([int64]1 -shl $processor)
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$candidateRunnerHash = Get-Sha256 $candidateRunner
$referenceRunnerHash = Get-Sha256 $referenceRunner
if ($candidateRunnerHash -ne $referenceRunnerHash) {
    throw "$GateName comparison requires the identical runner assembly in both directories."
}

$powerPlan = (powercfg /getactivescheme) -join ' '
Write-Host "Lightweight $GateName controlled performance: CPU=$LogicalProcessor mask=$affinityMask core=$($selectedCpu.CoreIndex) siblings=$($protectedLogicalProcessors -join ',') protectedMask=$protectedAffinityMask efficiency=$($selectedCpu.EfficiencyClass) priority=Normal"
Write-Host "PowerPlan=$powerPlan"
Write-Host "RunnerSHA256=$candidateRunnerHash"
foreach ($build in @('candidate', 'reference')) {
    $directory = if ($build -eq 'candidate') { $candidateDirectory } else { $referenceDirectory }
    Write-Host "Build=$build Directory=$directory EngineSHA256=$(Get-Sha256 (Join-Path $directory $engineName)) Copper68kSHA256=$(Get-Sha256 (Join-Path $directory 'Copper68k.dll'))"
}
Write-Host "Warmup=$WarmupFrames Measured=$MeasuredFrames SamplesPerBuild=$SamplesPerBuild ActiveWorkload=$ActiveWorkload ActiveThreshold=$MinimumActiveFps FPS InactiveRetention=$MinimumInactiveRetentionPercent% ActiveRetention=$MinimumActiveRetentionPercent% MaxSpread=$MaximumSpreadPercent%"
Write-Host "HostPolicy=v2 telemetry=native-accounting coreOther=$CompetingCorePercent% packageOther=$CompetingPackagePercent% sustained=$SustainedContentionSeconds seconds"

function Assert-CleanPreflight {
    Write-Host "Preflight: sampling core/sibling/package load for $PreflightSeconds second(s)."
    $before = Get-G6HostSnapshot
    $state = @{}
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $PreflightSeconds) {
        Start-Sleep -Seconds 2
        $after = Get-G6HostSnapshot
        $load = Get-G6HostLoad $before $after $LogicalProcessor $protectedLogicalProcessors
        Write-G6HostLoad $load
        Update-G6HostLoadState $state $load $CompetingCorePercent $CompetingPackagePercent $SustainedContentionSeconds
        $before = $after
    }
}

function Convert-ToDouble([string]$Value) {
    $number = 0.0
    if ([double]::TryParse($Value, [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::CurrentCulture, [ref]$number)) {
        return $number
    }
    $normalized = $Value.Replace(',', '.')
    if ([double]::TryParse($normalized, [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return $number
    }
    throw "Cannot parse numeric benchmark value '$Value'."
}

function Get-Field([string]$Line, [string]$Name) {
    $match = [regex]::Match($Line, "(?:^| )$([regex]::Escape($Name))=([^ ]+)")
    if (-not $match.Success) {
        throw "Runner output does not contain '$Name': $Line"
    }
    $match.Groups[1].Value
}

function Invoke-ControlledSample(
    [ValidateSet('candidate', 'reference')][string]$Build,
    [ValidateSet('inactive', 'active')][string]$Cohort,
    [int]$Ordinal) {
    $directory = if ($Build -eq 'candidate') { $candidateDirectory } else { $referenceDirectory }
    $assembly = Join-Path $directory $runnerName
    $workload = if ($Cohort -eq 'active') { $ActiveWorkload } else { '--synthetic-rom-stop' }
    $startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $arguments = @($assembly, $workload, '--warmup', "$WarmupFrames", '--frames', "$MeasuredFrames")
    if ($null -ne $startInfo.ArgumentList) {
        foreach ($argument in $arguments) {
            $startInfo.ArgumentList.Add($argument)
        }
    }
    else {
        # Windows PowerShell 5.1 exposes ProcessStartInfo.ArgumentList through
        # type forwarding but returns null. Every argument here is controlled
        # by this script; quoting each one also preserves workspace spaces.
        $startInfo.Arguments = ($arguments | ForEach-Object {
            '"' + $_.Replace('"', '\"') + '"'
        }) -join ' '
    }

    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $process.ProcessorAffinity = [intptr]$affinityMask
        $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::Normal
        if ([int64]$process.ProcessorAffinity -ne $affinityMask -or
            $process.PriorityClass -ne 'Normal') {
            throw "$GateName performance INVALID / RERUN: affinity or priority confirmation failed."
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $loadState = @{}
        $before = Get-G6HostSnapshot $process
        do {
            $finished = $process.WaitForExit(2000)
            $process.Refresh()
            if (-not $process.HasExited -and
                ([int64]$process.ProcessorAffinity -ne $affinityMask -or
                 $process.PriorityClass -ne 'Normal')) {
                throw "$GateName performance INVALID / RERUN: affinity or priority changed."
            }
            $after = Get-G6HostSnapshot $process
            $load = Get-G6HostLoad $before $after $LogicalProcessor $protectedLogicalProcessors
            Write-G6HostLoad $load
            Update-G6HostLoadState $loadState $load $CompetingCorePercent $CompetingPackagePercent $SustainedContentionSeconds
            $before = $after
        } while (-not $finished)

        $line = @($standardOutput.Result -split "`r?`n" |
            Where-Object { $_.StartsWith('engine=lightweight-a500 ') }) |
            Select-Object -Last 1
        $errorText = $standardError.Result
        if ($process.ExitCode -ne 0) {
            throw "$GateName runner exited $($process.ExitCode): $errorText"
        }
        if (-not $line) {
            throw "Unparseable $GateName runner output: $errorText"
        }
        [pscustomobject]@{
            Build = $Build
            Cohort = $Cohort
            Ordinal = $Ordinal
            Fps = Convert-ToDouble (Get-Field $line 'fps')
            Cycle = Get-Field $line 'cycle'
            Cpu = Get-Field $line 'cpu'
            Hardware = Get-Field $line 'hardware'
            Output = Get-Field $line 'output'
            Pixels = Get-Field $line 'pixels'
            AudioSamples = Get-Field $line 'audioSamples'
            Allocated = Get-Field $line 'allocated'
            Unsupported = Get-Field $line 'unsupported'
        }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
}

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $sorted[[int][Math]::Floor($sorted.Count / 2)]
}

if ($CooldownSeconds -gt 0) {
    Write-Host "Cooldown: $CooldownSeconds second(s)."
    Start-Sleep -Seconds $CooldownSeconds
}
Assert-CleanPreflight

$order = @('candidate', 'reference', 'reference', 'candidate', 'candidate', 'reference')
$allResults = [Collections.Generic.List[object]]::new()
foreach ($cohort in @('inactive', 'active')) {
    $ordinals = @{ candidate = 0; reference = 0 }
    foreach ($build in $order) {
        $ordinals[$build]++
        Write-Host "Running cohort=$cohort build=$build sample=$($ordinals[$build])/$SamplesPerBuild."
        $sample = Invoke-ControlledSample $build $cohort $ordinals[$build]
        $allResults.Add($sample)
        Write-Host ("RESULT cohort={0} build={1} sample={2} fps={3:F2} cycle={4} cpu={5} hardware={6} output={7} pixels={8} audioSamples={9} allocated={10} unsupported={11}" -f
            $sample.Cohort, $sample.Build, $sample.Ordinal, $sample.Fps,
            $sample.Cycle, $sample.Cpu, $sample.Hardware, $sample.Output,
            $sample.Pixels, $sample.AudioSamples, $sample.Allocated,
            $sample.Unsupported)
    }
}

$medians = @{}
foreach ($cohort in @('inactive', 'active')) {
    foreach ($build in @('candidate', 'reference')) {
        $results = @($allResults | Where-Object {
            $_.Cohort -eq $cohort -and $_.Build -eq $build
        })
        $fingerprints = @($results | ForEach-Object {
            "$($_.Cycle)|$($_.Cpu)|$($_.Hardware)|$($_.Output)|$($_.Pixels)|$($_.AudioSamples)|$($_.Allocated)|$($_.Unsupported)"
        } | Select-Object -Unique)
        if ($fingerprints.Count -ne 1) {
            throw "$GateName performance STOP: $cohort/$build fingerprints differ across samples."
        }
        if ($results[0].Allocated -ne '0' -or $results[0].Unsupported -ne 'none') {
            throw "$GateName performance STOP: $cohort/$build allocated or entered unsupported behavior."
        }
        $values = [double[]]@($results | ForEach-Object Fps)
        $median = Get-Median $values
        $minimum = ($values | Measure-Object -Minimum).Minimum
        $maximum = ($values | Measure-Object -Maximum).Maximum
        $spread = 100.0 * ($maximum - $minimum) / $median
        $medians["$cohort/$build"] = $median
        Write-Host ("SUMMARY cohort={0} build={1} samples={2} median={3:F2} spread={4:F2}% frameMs={5:F6}" -f
            $cohort, $build, ($values -join ','), $median, $spread,
            (1000.0 / $median))
        if ($spread -gt $MaximumSpreadPercent) {
            throw "$GateName performance INVALID / RERUN: $cohort/$build spread is $spread%."
        }
    }
}

$inactiveRetention = 100.0 * $medians['inactive/candidate'] / $medians['inactive/reference']
if ($inactiveRetention -lt $MinimumInactiveRetentionPercent) {
    throw "$GateName performance STOP: inactive retention is $inactiveRetention%."
}
if ($medians['active/candidate'] -lt $MinimumActiveFps) {
    throw "$GateName performance STOP: active candidate median is $($medians['active/candidate']) FPS."
}
$activeRetention = 100.0 * $medians['active/candidate'] / $medians['active/reference']
if ($activeRetention -lt $MinimumActiveRetentionPercent) {
    throw "$GateName performance STOP: active retention is $activeRetention%."
}
if (($allResults | Where-Object { $_.Build -eq 'candidate' -and $_.Pixels -ne '142102' }).Count) {
    throw "$GateName performance STOP: candidate did not expose the complete 454x313 PAL raster."
}

Write-Host ("Lightweight {0} controlled performance PASS: inactive retention={1:F2}% active retention={2:F2}% active={3:F2} FPS/{4:F6} ms; no production cutover." -f
    $GateName,
    $inactiveRetention, $activeRetention,
    $medians['active/candidate'],
    (1000.0 / $medians['active/candidate']))
