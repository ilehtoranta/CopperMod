# Host-load policy v2. Native per-CPU accounting avoids WMI provider timestamp lag.
# https://learn.microsoft.com/windows/win32/api/winternl/nf-winternl-ntquerysysteminformation
function Get-G6ProcessorCounters {
    if (-not ('G6NativeCpuTimes' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class G6NativeCpuTimes {
    [StructLayout(LayoutKind.Sequential)]
    private struct Times {
        public long Idle, Kernel, User, Reserved1, Reserved2;
        public uint Reserved3;
    }
    public sealed class Counter { public int Index; public long Idle; public long Time; }
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returned);
    public static Counter[] Read() {
        int size = Marshal.SizeOf(typeof(Times));
        IntPtr buffer = Marshal.AllocHGlobal(size * 64);
        try {
            int returned;
            int status = NtQuerySystemInformation(8, buffer, size * 64, out returned);
            if (status != 0 || returned <= 0 || returned > size * 64 || returned % size != 0)
                throw new InvalidOperationException("Native processor telemetry unavailable: " + status);
            var result = new Counter[returned / size];
            for (int i = 0; i < result.Length; i++) {
                var value = (Times)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(Times));
                // Kernel includes idle. All three use the same CPU-accounting clock.
                result[i] = new Counter { Index=i, Idle=value.Idle, Time=checked(value.Kernel + value.User) };
            }
            return result;
        } finally { Marshal.FreeHGlobal(buffer); }
    }
}
'@
    }
    [G6NativeCpuTimes]::Read()
}

function Get-G6HostSnapshot {
    param($BenchmarkProcess = $null)
    $benchmarkId = if ($null -eq $BenchmarkProcess) { 0 } else { $BenchmarkProcess.Id }
    $cores = @{}
    foreach ($row in Get-G6ProcessorCounters) {
        $cores[[int]$row.Index] = [pscustomobject]@{
            Idle = [decimal]$row.Idle
            Time = [decimal]$row.Time
        }
    }
    $benchmarkSeconds = 0.0
    if ($null -ne $BenchmarkProcess) {
        $benchmarkSeconds = $BenchmarkProcess.TotalProcessorTime.TotalSeconds
    }
    $timeSeconds = [Diagnostics.Stopwatch]::GetTimestamp() / [double][Diagnostics.Stopwatch]::Frequency
    $workloads = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.ProcessId -ne $BenchmarkId -and $_.ProcessId -ne $PID -and (
            $_.Name -match '^(testhost|vstest.console|MSBuild|csc|vbc)(\.exe)?$' -or
            ($_.Name -match '^dotnet(\.exe)?$' -and $_.CommandLine -match '(\s(build|test)\s|vstest\.console|Benchmarks[^\s"]*\.dll)')
        )
    } | ForEach-Object { "$($_.Name)[$($_.ProcessId)]" })
    [pscustomobject]@{ Cores = $cores; BenchmarkSeconds = $benchmarkSeconds; Workloads = $workloads; TimeSeconds = $timeSeconds }
}

function Get-G6HostLoad {
    param($Before, $After, [int]$SelectedCpu, [int[]]$ProtectedCpus)
    if ($Before.Cores.Count -eq 0 -or $Before.Cores.Count -ne $After.Cores.Count) {
        throw 'G6 performance INVALID / RERUN: missing or changed processor telemetry.'
    }
    $busy = @{}
    $seconds = $After.TimeSeconds - $Before.TimeSeconds
    if ($seconds -le 0 -or $seconds -gt 10) { throw 'G6 performance INVALID / RERUN: host telemetry interval must be positive and at most 10 seconds.' }
    foreach ($cpu in $Before.Cores.Keys) {
        if (-not $After.Cores.ContainsKey($cpu)) { throw 'Missing processor telemetry.' }
        $elapsed = $After.Cores[$cpu].Time - $Before.Cores[$cpu].Time
        $idle = $After.Cores[$cpu].Idle - $Before.Cores[$cpu].Idle
        if ($elapsed -le 0 -or $idle -lt 0 -or $idle -gt $elapsed) {
            throw "G6 performance INVALID / RERUN: stale or invalid processor telemetry (cpu=$cpu elapsed=$elapsed idle=$idle)."
        }
        # Idle accounting may be published at different ticks on different
        # cores. Use each CPU's own accounting denominator for utilization,
        # and monotonic wall time for persistence and benchmark CPU subtraction.
        $busy[$cpu] = [math]::Max(0.0, 100.0 * (1.0 - [double]($idle / $elapsed)))
    }
    if (-not $busy.ContainsKey($SelectedCpu) -or $ProtectedCpus.Count -eq 0) { throw 'Missing protected-core telemetry.' }
    $benchmarkPercent = 100.0 * ($After.BenchmarkSeconds - $Before.BenchmarkSeconds) / $seconds
    if ($benchmarkPercent -lt 0 -or $benchmarkPercent -gt 110) { throw 'Invalid pinned benchmark CPU-time delta.' }
    # Benchmark process is pinned to SelectedCpu. Its CPU time must not count
    # as competing work. Affinity masks of other processes prove no placement.
    $selected = [math]::Max(0.0, $busy[$SelectedCpu] - $benchmarkPercent)
    $sibling = 0.0
    foreach ($cpu in $ProtectedCpus) {
        if (-not $busy.ContainsKey($cpu)) { throw 'Missing SMT-sibling telemetry.' }
        if ($cpu -ne $SelectedCpu) { $sibling = [math]::Max($sibling, $busy[$cpu]) }
    }
    $total = ($busy.Values | Measure-Object -Sum).Sum
    [pscustomobject]@{
        Seconds = $seconds
        Selected = $selected
        Sibling = $sibling
        Package = [math]::Max(0.0, ($total - $benchmarkPercent) / $busy.Count)
        Workloads = $After.Workloads
    }
}

function Update-G6HostLoadState {
    param([hashtable]$State, $Load, [double]$CoreLimit = 25,
        [double]$PackageLimit = 25, [double]$SustainedSeconds = 10)
    if ($Load.Workloads.Count) {
        throw "G6 performance INVALID / RERUN: competing test/build/benchmark: $($Load.Workloads -join ', ')"
    }
    foreach ($dimension in @('Selected','Sibling','Package')) {
        $limit = if ($dimension -eq 'Package') { $PackageLimit } else { $CoreLimit }
        if (-not $State.ContainsKey($dimension)) { $State[$dimension] = 0.0 }
        $State[$dimension] = if ($Load.$dimension -ge $limit) { $State[$dimension] + $Load.Seconds } else { 0.0 }
        if ($State[$dimension] -ge $SustainedSeconds) {
            throw "G6 performance INVALID / RERUN: sustained $dimension interference >=$limit% for $($State[$dimension]) seconds."
        }
    }
}

function Write-G6HostLoad {
    param($Load)
    Write-Host ('HOST_LOAD seconds={0:F2} selected-other={1:F1}% sibling={2:F1}% package-other={3:F1}%' -f $Load.Seconds,$Load.Selected,$Load.Sibling,$Load.Package)
}
