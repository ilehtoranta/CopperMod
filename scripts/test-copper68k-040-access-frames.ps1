#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/040-access-frame-audit',
    [switch] $ValidateReportsOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
$expected = [ordered]@{
    'rte-access-controls' = @{cases=6912; combinations=108}
    'rte-access-normal' = @{cases=9216; combinations=144}
    'rte-access-trace' = @{cases=13824; combinations=144}
    'rte-access-movem-opcodes' = @{cases=120960; combinations=1260}
    'rte-access-movem-full-index' = @{cases=114048; combinations=1188}
    'rte-access-fpu-unimplemented' = @{cases=13824; combinations=144}
    'rte-access-fpu-post' = @{cases=96768; combinations=1008}
    'rte-throwaway-controls' = @{cases=82944; combinations=1296}
    'rte-throwaway-access' = @{cases=428544; combinations=4752}
    'rte-access-remaining-protocols' = @{cases=480; combinations=15}
}
$testExit = 0
if (-not $ValidateReportsOnly) {
    if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $settings = @{ COPPER68K_RUN_040_ACCESS_FRAME_AUDIT='1'; COPPER68K_SYNTHETIC_REPORT_DIR=$output }
    $saved = @{}
    try {
        foreach ($name in $settings.Keys) {
            $saved[$name] = [Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name, $settings[$name])
        }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --filter 'FullyQualifiedName~SyntheticM68040AccessFrameAuditTests|FullyQualifiedName~M68040AccessFrameFixtureTests|FullyQualifiedName~SyntheticM68040MovemContinuationTests|FullyQualifiedName~SyntheticM68040FpuContinuationTests|FullyQualifiedName~SyntheticM68040ThrowawayTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output
        $testExit = $LASTEXITCODE
    } finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    $identity = [ordered]@{
        schema=1; reference='MC68040UM'; url='https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf'
        sections=@('8.4.1','8.4.2','8.4.3','8.4.4','8.4.6.2','8.4.6.7'); softwareReferenceExecuted=$false
        sourceCommit=(& git -C $repo rev-parse HEAD); cpuCommittedTree=(& git -C $repo rev-parse HEAD:Copper68k)
        cpuSourceFiles=@(& git -C $repo ls-files --cached --others --exclude-standard 'Copper68k/*') | ForEach-Object {
            @{file=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        inputs=@('Copper68k.Tests/Synthetic/SyntheticM68040AccessFrameAuditTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040MovemContinuationTests.cs','scripts/test-copper68k-040-access-frames.ps1','Copper68k.Tests/Synthetic/SyntheticM68040FpuContinuationTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs') | ForEach-Object {
            @{file=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        assemblies=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') | ForEach-Object {
            @{file=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        limitations=@('Canonical synthetic frames, not hardware captures or enabled-MMU access faults',
            'CP context transfer and detailed fault protocol inventory remain untested and fail the gate',
            'Multiple continuation bits are architecturally undefined and excluded',
            'Detailed fault validation, odd return PCs and physical timing remain unqualified')
    }
    $identity | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'identities.json')
}
# Validate the complete selection even if xUnit reported mismatches, so an early
# failure cannot hide missing reports, empty scopes or stale cardinalities.
$identity = Get-Content -LiteralPath (Join-Path $output 'identities.json') -Raw | ConvertFrom-Json
if ($identity.schema -ne 1 -or $identity.reference -cne 'MC68040UM' -or $identity.softwareReferenceExecuted -ne $false -or
    $identity.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $identity.cpuCommittedTree -notmatch '^[a-f0-9]{40}$' -or
    @($identity.cpuSourceFiles).Count -eq 0 -or @($identity.inputs).Count -ne 5 -or @($identity.inputs.file | Sort-Object -Unique).Count -ne 5 -or
    @($identity.assemblies).Count -ne 2) { throw '040 access-frame input identity is missing or incomplete' }
foreach ($input in $identity.inputs) {
    $path = Join-Path $repo $input.file
    if ($input.file -notin @('Copper68k.Tests/Synthetic/SyntheticM68040AccessFrameAuditTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040MovemContinuationTests.cs','scripts/test-copper68k-040-access-frames.ps1','Copper68k.Tests/Synthetic/SyntheticM68040FpuContinuationTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs') -or
        $input.sha256 -cne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()) { throw '040 access-frame fixture/command identity differs' }
}
$cpuFiles = @(& git -C $repo ls-files --cached --others --exclude-standard 'Copper68k/*')
if (@($identity.cpuSourceFiles).Count -ne $cpuFiles.Count -or @($identity.cpuSourceFiles.file | Sort-Object -Unique).Count -ne $cpuFiles.Count) { throw '040 access-frame CPU source selection differs' }
foreach ($source in $identity.cpuSourceFiles) {
    if ($source.file -notin $cpuFiles -or $source.sha256 -cne (Get-FileHash -LiteralPath (Join-Path $repo $source.file) -Algorithm SHA256).Hash.ToLowerInvariant()) { throw '040 access-frame CPU source identity differs' }
}
foreach ($assembly in $identity.assemblies) {
    if ($assembly.file -notin @('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') -or
        $assembly.sha256 -cne (Get-FileHash -LiteralPath (Join-Path $repo $assembly.file) -Algorithm SHA256).Hash.ToLowerInvariant()) { throw '040 access-frame assembly identity differs' }
}
[xml]$trx = Get-Content -LiteralPath (Join-Path $output 'audit.trx') -Raw
$counters = $trx.TestRun.ResultSummary.Counters
if ([int]$counters.executed -ne 16 -or [int]$counters.total -ne 16 -or [int]$counters.notExecuted -ne 0) {
    throw '040 access-frame audit did not execute its complete selection (10 batches, 6 fixed examples)'
}
$totals = [ordered]@{passing=0; mismatching=0; unsupported=0; untested=0}
foreach ($group in $expected.Keys) {
    $report = Get-Content -LiteralPath (Join-Path $output "68040-$group.json") -Raw | ConvertFrom-Json
    if ($report.schema -ne 1 -or $report.model -cne '68040' -or $report.group -cne $group -or $report.xunitBatches -ne 1) { throw "$group identity/schema differs" }
    if ($report.logicalCases -ne $expected[$group].cases -or @($report.combinations.PSObject.Properties).Count -ne $expected[$group].combinations) { throw "$group cardinality differs" }
    if (@($report.counts.PSObject.Properties).Count -ne 4) { throw "$group status selection differs" }
    $caseTotal = 0
    foreach ($status in @($totals.Keys)) {
        $value = $report.counts.$status
        if ($null -eq $value -or $value -isnot [long] -and $value -isnot [int] -or $value -lt 0) { throw "$group invalid $status count" }
        $caseTotal += $value; $totals[$status] += $value
    }
    if ($caseTotal -ne $report.logicalCases) { throw "$group status totals differ" }
    $combinationTotals = @{passing=0; mismatching=0; unsupported=0; untested=0}
    $expectedCombinations = @{}
    if ($group -eq 'rte-access-remaining-protocols') {
        foreach ($bank in @('user','ISP','MSP')) {
            foreach ($form in @('frame-validation-fault','odd-user-trace-PC','real-access-fault-entry','writeback-handler','CP-context-transferred-vector')) {
                $expectedCombinations["68040/RTE/format7/$form/bank=$bank"] = 32
            }
        }
    } elseif ($group -in @('rte-throwaway-controls','rte-throwaway-access')) {
        $forms = if ($group -eq 'rte-throwaway-controls') { @('format0','format2','format3') } else { @('normal','CM','CT','CU','CP49','CP50','CP51','CP52','CP53','CP54','CP55') }
        foreach ($start in @('ISP','MSP')) { foreach ($tail in @('user','ISP','MSP')) { foreach ($middle in @('none','user','ISP','MSP')) {
            foreach ($result in @('user','ISP','MSP')) { foreach ($trace in @(0,0x8000,0x4000)) { foreach ($alignment in @(0,1)) { foreach ($form in $forms) {
                $key = '68040/RTE/throwaway/{0}/start={1}/middle={2}/tail={3}/result={4}/T={5:X4}/align={6}' -f $form,$start,$middle,$tail,$result,$trace,$alignment
                $expectedCombinations[$key] = 32 * $(if ($form -in @('CT','CU') -or $form.StartsWith('CP')) {3} else {2})
            } } } }
        } } }
    } elseif ($group -in @('rte-access-fpu-unimplemented','rte-access-fpu-post')) {
        $post = $group -eq 'rte-access-fpu-post'
        foreach ($vector in $(if ($post) {49..55} else {@(11)})) {
            foreach ($bank in @('user','ISP','MSP')) {
                foreach ($trace in @(0,0x8000,0x4000)) {
                    foreach ($vbr in @(0,0x10000)) {
                        foreach ($ea in @(0x5ffa,0x12345678)) {
                            foreach ($ssw in @(0,0x0105,0x0505,0x0905)) {
                                $key = '68040/RTE/format7/{0}/vector={1}/bank={2}/T={3:X4}/VBR={4:X8}/EA={5:X8}/SSW={6:X4}' -f $(if ($post) {'CP'} else {'CU'}),$vector,$bank,$trace,$vbr,$ea,$ssw
                                $expectedCombinations[$key] = 96
                            }
                        }
                    }
                }
            }
        }
    } elseif ($group -in @('rte-access-movem-opcodes','rte-access-movem-full-index')) {
        foreach ($load in @($false,$true)) {
            foreach ($width in @(2,4)) {
                foreach ($mode in 2..7) {
                    foreach ($register in 0..7) {
                        if ($mode -notin @(2,5,6) -and $mode -ne $(if ($load) {3} else {4}) -and -not ($mode -eq 7 -and $register -le $(if ($load) {3} else {1}))) { continue }
                        $indexes=@('brief')
                        $masks=@(1,0x0101,0xffff)
                        if ($group -eq 'rte-access-movem-full-index') {
                            if (-not (($mode -eq 6 -and $register -eq 0) -or ($load -and $mode -eq 7 -and $register -eq 3))) { continue }
                            $indexes=@()
                            $masks=@(0x0101)
                            foreach ($bs in @($false,$true)) { foreach ($suppressed in @($false,$true)) { foreach ($bd in 1..3) { foreach ($iis in @(0,1,2,3,5,6,7)) {
                                if ($suppressed -and $iis -ge 5) { continue }
                                $indexes += "full/bs=$bs/is=$suppressed/bd=$bd/iis=$iis"
                            } } } }
                        }
                        foreach ($index in $indexes) { foreach ($bank in @('user','ISP','MSP')) { foreach ($mask in $masks) {
                            $key = '68040/MOVEM-CM/{0}/{1}/ea={2}:{3}/{4}/bank={5}/mask={6:X4}' -f $(if ($load) {'load'} else {'store'}),$width,$mode,$register,$index,$bank,$mask
                            $expectedCombinations[$key] = 96
                        } } }
                    }
                }
            }
        }
    } else {
        $formats = if ($group -eq 'rte-access-controls') { @(0,2,3) } else { @(7) }
        $protocol = if ($group -eq 'rte-access-trace') { 'CT' } else { 'normal' }
        $phases = if ($protocol -eq 'CT') { 3 } else { 2 }
        foreach ($format in $formats) {
            foreach ($bank in @('user','ISP','MSP')) {
                foreach ($trace in @(0,0x8000,0x4000)) {
                    foreach ($vbr in @(0,0x10000)) {
                        foreach ($ea in @(0x5ffa,0x12345678)) {
                            foreach ($ssw in $(if ($format -eq 7) { @(0,0x0105,0x0505,0x0905) } else { @(0) })) {
                                $key = '68040/RTE/format{0}/{1}/bank={2}/T={3:X4}/VBR={4:X8}/EA={5:X8}/SSW={6:X4}' -f $format,$protocol,$bank,$trace,$vbr,$ea,$ssw
                                $expectedCombinations[$key] = 32 * $phases
                            }
                        }
                    }
                }
            }
        }
    }
    foreach ($combination in $report.combinations.PSObject.Properties) {
        if (-not $expectedCombinations.ContainsKey($combination.Name)) { throw "$group foreign combination: $($combination.Name); first expected: $($expectedCombinations.Keys | Sort-Object | Select-Object -First 1)" }
        $combinationCases = 0
        foreach ($entry in $combination.Value.PSObject.Properties) {
            if ($entry.Name -notin $totals.Keys -or $entry.Value -isnot [long] -and $entry.Value -isnot [int] -or $entry.Value -lt 0) { throw "$group invalid combination status" }
            $combinationTotals[$entry.Name] += $entry.Value
            $combinationCases += $entry.Value
        }
        if ($combinationCases -ne $expectedCombinations[$combination.Name]) { throw "$group combination cardinality differs" }
    }
    foreach ($status in @($totals.Keys)) {
        if ($combinationTotals[$status] -ne $report.counts.$status) { throw "$group combination totals differ" }
    }
}
$passed = $testExit -eq 0 -and [int]$counters.failed -eq 0 -and [int]$counters.passed -eq 16 -and
    ($totals.mismatching + $totals.unsupported + $totals.untested) -eq 0
@{schema=1; model='68040'; logicalCases=887520; xunitBatches=10; fixedExamples=6; counts=$totals; passed=$passed; roadmapComplete=$false} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'audit-summary.json')
if (-not $passed) { throw "040 access-frame audit incomplete: $($totals | ConvertTo-Json -Compress)" }
