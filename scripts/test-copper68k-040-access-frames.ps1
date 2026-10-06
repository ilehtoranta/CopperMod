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
    'rte-mixed-epoch-canonical-scalar' = @{cases=1152000; combinations=12096}
    'rte-mixed-epoch-canonical-batch' = @{cases=1152000; combinations=12096}
    'rte-mixed-epoch-structure-scalar' = @{cases=3744000; combinations=628992}
    'rte-mixed-epoch-structure-batch' = @{cases=3744000; combinations=628992}
    'rte-odd-normal' = @{cases=69120; combinations=720}
    'rte-chained-odd-canonical-scalar' = @{cases=82944; combinations=2592}
    'rte-chained-odd-canonical-batch' = @{cases=82944; combinations=2592}
    'rte-chained-odd-structure-scalar' = @{cases=539136; combinations=269568}
    'rte-chained-odd-structure-batch' = @{cases=539136; combinations=269568}
    'rte-chained-odd-access-canonical-scalar' = @{cases=55296; combinations=1728}
    'rte-chained-odd-access-canonical-batch' = @{cases=55296; combinations=1728}
    'rte-chained-odd-access-structure-scalar' = @{cases=359424; combinations=179712}
    'rte-chained-odd-access-structure-batch' = @{cases=359424; combinations=179712}
    'rte-odd-pending' = @{cases=165888; combinations=1296}
    'address-error-fetch-040' = @{cases=2304; combinations=72}
    'rte-validation-physical-direct' = @{cases=162816; combinations=2544}
    'rte-validation-physical-chained' = @{cases=61056; combinations=15264}
    'rte-mixed-fault-canonical-scalar' = @{cases=276480; combinations=3456}
    'rte-mixed-fault-canonical-batch' = @{cases=276480; combinations=3456}
    'rte-mixed-fault-structure-scalar' = @{cases=3556800; combinations=711360}
    'rte-mixed-fault-structure-batch' = @{cases=3556800; combinations=711360}
    'rte-access-entry-double-fault' = @{cases=98304; combinations=3072}
    'rte-access-handler-refault' = @{cases=3840; combinations=120}
    'access-double-fault-dispatch-accurate' = @{cases=1088; combinations=544}
    'access-double-fault-dispatch-v1' = @{cases=1088; combinations=544}
    'access-double-fault-dispatch-v2' = @{cases=1088; combinations=544}
    'rte-validation-batch' = @{cases=129024; combinations=4032}
    'access-fault-batch-dispatch' = @{cases=10368; combinations=5184}
    'rte-repair-boundaries' = @{cases=143424; combinations=540}
    'rte-repair-chained' = @{cases=768960; combinations=45792}
    'rte-retry-trace-boundaries-scalar' = @{cases=92736; combinations=378}
    'rte-retry-trace-boundaries-batch' = @{cases=92736; combinations=378}
    'rte-retry-trace-chained-scalar' = @{cases=1271808; combinations=82944}
    'rte-retry-trace-chained-batch' = @{cases=1271808; combinations=82944}
    'rte-pending-trace-boundaries-scalar' = @{cases=41472; combinations=162}
    'rte-pending-trace-boundaries-batch' = @{cases=41472; combinations=162}
    'rte-pending-trace-chained-scalar' = @{cases=870912; combinations=54432}
    'rte-pending-trace-chained-batch' = @{cases=870912; combinations=54432}
    'rte-user-master-boundaries-scalar' = @{cases=44736; combinations=180}
    'rte-user-master-boundaries-batch' = @{cases=44736; combinations=180}
    'rte-user-master-chained-scalar' = @{cases=714240; combinations=45792}
    'rte-user-master-chained-batch' = @{cases=714240; combinations=45792}
    'rte-cp-vectors-boundaries-scalar' = @{cases=110592; combinations=432}
    'rte-cp-vectors-boundaries-batch' = @{cases=110592; combinations=432}
    'rte-cp-vectors-chained-scalar' = @{cases=2322432; combinations=145152}
    'rte-cp-vectors-chained-batch' = @{cases=2322432; combinations=145152}
    'rte-software-trace-boundaries-scalar' = @{cases=514560; combinations=1296}
    'rte-software-trace-boundaries-batch' = @{cases=514560; combinations=1296}
    'rte-software-trace-chained-scalar' = @{cases=3601920; combinations=145152}
    'rte-software-trace-chained-batch' = @{cases=3601920; combinations=145152}
    'instruction-fault-frame' = @{cases=36864; combinations=576}
    'instruction-fault-restart' = @{cases=79872; combinations=624}
    'handler-prefetch-entry-scalar' = @{cases=196608; combinations=6144}
    'handler-prefetch-entry-batch' = @{cases=196608; combinations=6144}
    'handler-prefetch-executing-scalar' = @{cases=12288; combinations=384}
    'handler-prefetch-executing-batch' = @{cases=12288; combinations=384}
    'handler-prefetch-retention-scalar' = @{cases=7680; combinations=240}
    'handler-prefetch-retention-batch' = @{cases=7680; combinations=240}
    'handler-prefetch-odd-scalar' = @{cases=12288; combinations=384}
    'handler-prefetch-odd-batch' = @{cases=12288; combinations=384}
    'rte-user-fault-scalar' = @{cases=168192; combinations=20832}
    'rte-user-fault-batch' = @{cases=168192; combinations=20832}
    'rte-user-repair-scalar' = @{cases=675328; combinations=8224}
    'rte-user-repair-batch' = @{cases=675328; combinations=8224}
    'rte-user-trace-boundaries-scalar' = @{cases=873984; combinations=2304}
    'rte-user-trace-boundaries-batch' = @{cases=873984; combinations=2304}
    'rte-user-trace-chained-scalar' = @{cases=3502080; combinations=145920}
    'rte-user-trace-chained-batch' = @{cases=3502080; combinations=145920}
    'rte-user-software-trace-boundaries-scalar' = @{cases=1360896; combinations=2592}
    'rte-user-software-trace-boundaries-batch' = @{cases=1360896; combinations=2592}
    'rte-user-software-trace-chained-scalar' = @{cases=6350848; combinations=193536}
    'rte-user-software-trace-chained-batch' = @{cases=6350848; combinations=193536}
    'rte-writeback-canonical-scalar' = @{cases=36864; combinations=1152}
    'rte-writeback-canonical-batch' = @{cases=36864; combinations=1152}
    'rte-writeback-structure-scalar' = @{cases=36864; combinations=18432}
    'rte-writeback-structure-batch' = @{cases=36864; combinations=18432}
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
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --filter 'FullyQualifiedName~SyntheticM68040AccessFrameAuditTests|FullyQualifiedName~M68040AccessFrameFixtureTests|FullyQualifiedName~SyntheticM68040MovemContinuationTests|FullyQualifiedName~SyntheticM68040FpuContinuationTests|FullyQualifiedName~SyntheticM68040ThrowawayTests|FullyQualifiedName~SyntheticM68040OddReturnTests|FullyQualifiedName~SyntheticM68040ChainedOddReturnTests|FullyQualifiedName~SyntheticM68040RteValidationFaultTests|FullyQualifiedName~SyntheticM68040AccessDoubleFaultTests|FullyQualifiedName~SyntheticM68040BatchFaultTests|FullyQualifiedName~SyntheticM68040RteRepairTests|FullyQualifiedName~SyntheticM68040InstructionFaultTests|FullyQualifiedName~SyntheticM68040HandlerPrefetchTests|FullyQualifiedName~SyntheticM68040UserRteFaultTests|FullyQualifiedName~SyntheticM68040WritebackTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output
        $testExit = $LASTEXITCODE
    } finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    $identity = [ordered]@{
        schema=1; reference='MC68040UM'; url='https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf'
        sections=@('2.2.2.1','7.6.1','7.6.3','8.1 figure 8-1','8.2.1','8.2.2','8.2.5','8.2.6','8.3','8.4','8.4.1','8.4.2','8.4.3','8.4.4','8.4.6.2','8.4.6.3','8.4.6.5','8.4.6.7 table 8-6','9.6 table 9-9','9.6.2'); softwareReferenceExecuted=$false
        sourceCommit=(& git -C $repo rev-parse HEAD); cpuCommittedTree=(& git -C $repo rev-parse HEAD:Copper68k)
        cpuSourceFiles=@(& git -C $repo ls-files --cached --others --exclude-standard 'Copper68k/*') | ForEach-Object {
            @{file=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        inputs=@('Copper68k.Tests/Synthetic/SyntheticM68040AccessFrameAuditTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040MovemContinuationTests.cs','scripts/test-copper68k-040-access-frames.ps1','Copper68k.Tests/Synthetic/SyntheticM68040FpuContinuationTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040OddReturnTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040ChainedOddReturnTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040RteValidationFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040AccessDoubleFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040BatchFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040RteRepairTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040SoftwareTraceProgram.cs','Copper68k.Tests/Synthetic/SyntheticM68040InstructionFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040HandlerPrefetchTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040UserRteFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040WritebackTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040WritebackProgram.cs') | ForEach-Object {
            @{file=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        assemblies=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') | ForEach-Object {
            @{file=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        limitations=@('Synthetic frames and supervisor-stack physical map faults, not hardware captures or enabled-MMU access faults',
            'CP context transfer and detailed fault protocol inventory remain untested and fail the gate',
            'Multiple continuation bits are architecturally undefined and excluded',
            'Selected active accurate-batch paths verify counts/callbacks and scalar/batch bus/cycle policy; generic short operand frames do not qualify architectural format-7 data restart',
            'Instruction-fault fixtures use cache-disabled accurate execution and physical-map rejection; speculative deferral, enabled caches/MMU and compiled fetch PC provenance remain unqualified',
            'Legacy repair clears saved trace; separate supervisor and user-tail bridge groups preserve incoming trace, check normal/CM completion and pending CT/CU/CP49-55 priority. Executed integer trace service and successful mixed-epoch throwaway chains have separate coverage; mixed epochs during faults, repair/retry and internal restoration remain required',
            'User-tail fault expectations compose documented throwaway live-SR rules with general supervisor exception entry; unusual combined hardware behavior has not been observed',
            'Mixed-epoch fault entry/bare return and user-tail privilege failure have separate coverage; mixed-epoch repair/retry and original trace suspension/resumption, internal-restoration double faults, cache/MMU/compiled handler-entry prefetch, chained odd-PC SR provenance and physical timing remain unqualified',
            'Direct odd-RTE saved-SR ordering uses documentary WinUAE 5d22d336, not an executed hardware oracle')
    }
    $identity | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'identities.json')
}
# Validate the complete selection even if xUnit reported mismatches, so an early
# failure cannot hide missing reports, empty scopes or stale cardinalities.
$identity = Get-Content -LiteralPath (Join-Path $output 'identities.json') -Raw | ConvertFrom-Json
if ($identity.schema -ne 1 -or $identity.reference -cne 'MC68040UM' -or $identity.softwareReferenceExecuted -ne $false -or
    $identity.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $identity.cpuCommittedTree -notmatch '^[a-f0-9]{40}$' -or
    @($identity.cpuSourceFiles).Count -eq 0 -or @($identity.inputs).Count -ne 17 -or @($identity.inputs.file | Sort-Object -Unique).Count -ne 17 -or
    @($identity.assemblies).Count -ne 2) { throw '040 access-frame input identity is missing or incomplete' }
foreach ($input in $identity.inputs) {
    $path = Join-Path $repo $input.file
    if ($input.file -notin @('Copper68k.Tests/Synthetic/SyntheticM68040AccessFrameAuditTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040MovemContinuationTests.cs','scripts/test-copper68k-040-access-frames.ps1','Copper68k.Tests/Synthetic/SyntheticM68040FpuContinuationTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040OddReturnTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040ChainedOddReturnTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040RteValidationFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040AccessDoubleFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040BatchFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040RteRepairTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040SoftwareTraceProgram.cs','Copper68k.Tests/Synthetic/SyntheticM68040InstructionFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040HandlerPrefetchTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040UserRteFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040WritebackTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040WritebackProgram.cs') -or
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
if ([int]$counters.executed -ne 110 -or [int]$counters.total -ne 110 -or [int]$counters.notExecuted -ne 0) {
    throw '040 access-frame audit did not execute its complete selection (86 batches, 24 fixed examples/witnesses)'
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
            foreach ($form in @('frame-validation-fault','odd-PC-chained-SR-provenance','real-access-fault-entry','writeback-handler','CP-context-transferred-vector')) {
                $expectedCombinations["68040/RTE/format7/$form/bank=$bank"] = 32
            }
        }
    } elseif ($group.StartsWith('rte-writeback-', [StringComparison]::Ordinal)) {
        $structure=$group.Contains('structure',[StringComparison]::Ordinal)
        $matrix=if($structure){'structure'}else{'canonical'}
        foreach($handler in @('ISP','MSP')){foreach($restored in @('user','user-M','ISP','MSP')){foreach($lane in 0..3){
            $prefix="68040/RTE/writebacks/$matrix/handler=$handler/restore=$restored/lane=$lane"
            if(-not $structure){
                foreach($slot in 1..3){foreach($width in @(1,2,4)){foreach($pair in 0..3){
                    $mask=1 -shl ($slot-1)
                    $expectedCombinations["$prefix/valid=$mask/slot$slot-size$width/layout=distinct/T=0000/pair=$pair"]=32
                }}}
            }else{
                foreach($mask in 0..7){foreach($pattern in 0..3){foreach($layout in @('distinct','same','overlap')){
                    foreach($trace in @('0000','8000','4000')){foreach($pair in @(2,3)){
                        $expectedCombinations["$prefix/valid=$mask/pattern$pattern/layout=$layout/T=$trace/pair=$pair"]=2
                    }}
                }}}
            }
        }}}
    } elseif ($group.StartsWith('rte-chained-odd-', [StringComparison]::Ordinal)) {
        $structural=$group.Contains('structure',[StringComparison]::Ordinal)
        $access=$group.Contains('access-',[StringComparison]::Ordinal)
        foreach($start in @('ISP','MSP')){foreach($tail in @('user','user-M','ISP','MSP')){
        foreach($middle in $(if($structural){@('none','user','user-M','ISP','MSP')}else{@('none')})){
        foreach($result in @('user','user-M','ISP','MSP')){foreach($incoming in @(0,0x8000,0x4000)){
        foreach($first in @(0,0x8000,0x4000)){foreach($second in $(if($middle -eq 'none'){@(0)}else{@(0,0x8000,0x4000)})){
        foreach($trace in @(0,0x8000,0x4000)){foreach($target in $(if($structural){@(0x6001,0xff002003u)}else{@(0x6001)})){
        foreach($alignment in $(if($structural){@(0,1)}else{@(0)})){foreach($vbr in $(if($structural){@(0,0x10000)}else{@(0x10000)})){
        foreach($form in $(if($access){@('normal','CM')}else{@('format0','format2','format3')})){
            $path=if($middle -eq 'none'){"$start-$tail"}else{"$start-$middle-$tail"}
            $secondName=if($middle -eq 'none'){'none'}else{'{0:X4}' -f $second}
            $key='68040/RTE/chained-odd/{0}/path={1}/result={2}/incoming={3:X4}/first={4:X4}/second={5}/T={6:X4}/target={7:X8}/align={8}/VBR={9:X8}' -f $form,$path,$result,$incoming,$first,$secondName,$trace,$target,$alignment,$vbr
            $expectedCombinations[$key]=$(if($structural){2}else{32})
        }}}}}}}}}}}}
    } elseif ($group.StartsWith('rte-user-trace-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-user-software-trace-', [StringComparison]::Ordinal)) {
        $userSoftwareTrace = $group.StartsWith('rte-user-software-trace-', [StringComparison]::Ordinal)
        $chained = $group.Contains('-chained-', [StringComparison]::Ordinal)
        foreach ($start in @('ISP','MSP')) {
            foreach ($middle in $(if ($chained) { @('none','user','ISP','MSP') } else { @('none') })) {
                $path = if ($middle -eq 'none') { "$start-user" } else { "$start-$middle-user" }
                foreach ($master in @($false,$true)) {
                    foreach ($incoming in $(if ($chained) { @(0x8000) } else { @(0,0x8000,0x4000) })) {
                        foreach ($alignment in $(if ($chained) { @(0,1) } else { @(0) })) {
                            foreach ($vbr in $(if ($chained) { @(0,0x10000) } else { @(0x10000) })) {
                                foreach ($result in @('user','user-M','ISP','MSP')) {
                                    foreach ($trace in @(0,0x8000,0x4000)) {
                                        foreach ($form in $(if ($userSoftwareTrace) { @('CU-linear','CU-flow','CP49','CP50','CP51','CP52','CP53','CP54','CP55') } else { @('format0','format2','format3','invalid4','invalid15','normal','CM','CT','CU','CP','CP50','CP51','CP52','CP53','CP54','CP55') })) {
                                          foreach ($clear in $(if ($userSoftwareTrace) { @($false,$true) } else { @($false) })) {
                                            $reads = @('0:2')
                                            if ($chained) {
                                                $reads += @('2:4','6:2')
                                                if ($form -notin @('format0','format2','format3','invalid4','invalid15')) { $reads += '12:2' }
                                                if ($form -notin @('format0','format2','format3','invalid4','invalid15','normal')) { $reads += '8:4' }
                                            }
                                            foreach ($read in $reads) {
                                                $parts = $read.Split(':')
                                                for ($byte=0; $byte -lt $(if ($chained) {[int]$parts[1]} else {1}); $byte++) {
                                                    $key = '68040/RTE/user-{0}/{1}/{2}/path={3}/M={4}/incoming={5:X4}/align={6}/VBR={7:X8}/result={8}/T={9:X4}/read={10}:{11}/byte={12}' -f $(if ($userSoftwareTrace) {'software-trace'} else {'trace'}),$(if ($chained) {'structure'} else {'boundaries'}),$form,$path,$master,$incoming,$alignment,$vbr,$result,$trace,$parts[0],$parts[1],$byte
                                                    # Seven stores, two RTEs, entry and following; pending
                                                    # conversion or incoming trace adds one handler return.
                                                    $pending = $form -in @('CT','CU','CP','CP50','CP51','CP52','CP53','CP54','CP55')
                                                    $phases = if ($pending -or $incoming -ne 0) {12} else {11}
                                                    if ($userSoftwareTrace) {
                                                        $key += "/clear=$clear"
                                                        # Literal program transitions; do not call fixture Phases().
                                                        $phases = 14
                                                        if ($form.StartsWith('CU')) { $phases++ }
                                                        if ($form -eq 'CU-flow' -and $trace -ne 0x8000) { $phases += 2 }
                                                        if ($trace -eq 0x8000 -or ($form -eq 'CU-flow' -and $trace -eq 0x4000)) {
                                                            $phases += 5
                                                            if ($clear) { $phases++ }
                                                        }
                                                    }
                                                    $expectedCombinations[$key] = $(if ($chained) {2} else {32}) * $phases
                                                }
                                            }
                                          }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    } elseif ($group.StartsWith('rte-user-fault-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-user-repair-', [StringComparison]::Ordinal)) {
        $repair = $group.StartsWith('rte-user-repair-', [StringComparison]::Ordinal)
        foreach ($matrix in @('boundaries','structure')) {
            foreach ($start in @('ISP','MSP')) {
                foreach ($middle in $(if ($matrix -eq 'structure') { @('none','user','ISP','MSP') } else { @('none') })) {
                    $path = if ($middle -eq 'none') { "$start-user" } else { "$start-$middle-user" }
                    foreach ($master in @($false,$true)) {
                        foreach ($incoming in $(if ($repair) { @(0x8000) } else { @(0,0x8000,0x4000) })) {
                            foreach ($alignment in @(0,1)) { foreach ($vbr in @(0,0x10000)) {
                                foreach ($result in $(if ($repair -and $matrix -eq 'boundaries') { @('user','ISP','MSP') } else { @('ISP') })) {
                                    foreach ($trace in $(if ($repair -and $matrix -eq 'boundaries') { @(0,0x8000,0x4000) } else { @(0) })) {
                                        foreach ($form in @('format0','format2','format3','invalid4','invalid15','normal','CM','CT','CU','CP')) {
                                            $reads = @(@{offset=0; width=2})
                                            if ($matrix -eq 'structure') {
                                                $reads += @(@{offset=2; width=4}, @{offset=6; width=2})
                                                if ($form -in @('normal','CM','CT','CU','CP')) {
                                                    $reads += @{offset=12; width=2}
                                                    if ($form -ne 'normal') { $reads += @{offset=8; width=4} }
                                                }
                                            }
                                            foreach ($read in $reads) {
                                                foreach ($byte in $(if ($matrix -eq 'structure') { 0..($read.width - 1) } else { @(0) })) {
                                                    $key = '68040/RTE/user-{0}/{1}/{2}/path={3}/M={4}/incoming={5:X4}/align={6}/VBR={7:X8}/result={8}/T={9:X4}/read={10}:{11}/byte={12}' -f $(if ($repair) {'repair'} else {'fault'}),$matrix,$form,$path,$master,$incoming,$alignment,$vbr,$result,$trace,$read.offset,$read.width,$byte
                                                    $phases = if (-not $repair) { 3 } elseif ($form -in @('CT','CU','CP')) { 12 } else { 11 }
                                                    $expectedCombinations[$key] = $(if ($matrix -eq 'boundaries') {32} else {2}) * $phases
                                                }
                                            }
                                        }
                                    }
                                }
                            } }
                        }
                    }
                }
            }
        }
    } elseif ($group.StartsWith('handler-prefetch-retention-', [StringComparison]::Ordinal)) {
        $route = if ($group.EndsWith('-scalar', [StringComparison]::Ordinal)) { 'scalar' } else { 'batch' }
        foreach ($mode in @('linear','branch','subroutine','task','map')) {
            foreach ($bank in @('user','ISP','MSP')) { foreach ($offset in @(0,2,4,6)) {
                foreach ($alignment in @(0,1)) { foreach ($vbr in @(0,0x10000)) {
                    $key = '68040/access-error/handler-retention/{0}/route={1}/bank={2}/handler={3}/align={4}/VBR={5:X8}' -f $mode,$route,$bank,$offset,$alignment,$vbr
                    $expectedCombinations[$key] = 32
                } }
            } }
        }
    } elseif ($group.StartsWith('handler-prefetch-', [StringComparison]::Ordinal)) {
        $entry = $group.StartsWith('handler-prefetch-entry-', [StringComparison]::Ordinal)
        $odd = $group.StartsWith('handler-prefetch-odd-', [StringComparison]::Ordinal)
        $route = if ($group.EndsWith('-scalar', [StringComparison]::Ordinal)) { 'scalar' } else { 'batch' }
        foreach ($form in @('opcode','extension','RTE')) {
            foreach ($bank in $(if ($form -eq 'RTE') { @('ISP','MSP') } else { @('user','ISP','MSP') })) {
                foreach ($trace in @(0,0x8000,0x4000)) { foreach ($alignment in @(0,1)) { foreach ($vbr in @(0,0x10000)) {
                    foreach ($offset in $(if ($odd) { @(1,3,5,7) } elseif ($entry) { @(0,2,4,6) } else { @(0) })) {
                        foreach ($byte in $(if ($odd) { @(-1) } elseif ($entry) { 0..15 } else { 0..3 })) {
                            $key = '68040/access-error/handler-prefetch/{0}/route={1}/bank={2}/T={3:X4}/align={4}/VBR={5:X8}/handler={6}/byte={7}' -f $form,$route,$bank,$trace,$alignment,$vbr,$offset,$byte
                            $expectedCombinations[$key] = 32
                        }
                    }
                } } }
            }
        }
    } elseif ($group -in @('instruction-fault-frame','instruction-fault-restart')) {
        $restart = $group -eq 'instruction-fault-restart'
        foreach ($form in @('opcode','extension-low','next-opcode','self-branch')) {
            foreach ($bank in @('user','ISP','MSP')) { foreach ($trace in $(if ($restart) { @(0) } else { @(0,0x8000,0x4000) })) {
                foreach ($alignment in @(0,1)) { foreach ($vbr in @(0,0x10000)) {
                    $values = if (-not $restart -or $form -eq 'self-branch') { @(2L) } elseif ($form -eq 'extension-low') {
                        @(0L,2147483647L,2147483648L,4294967295L)
                    } else { @(0L,127L,4294967168L,4294967295L) }
                    foreach ($value in $values) { foreach ($byte in 0..3) {
                        $key = '68040/access-error/instruction/{0}/bank={1}/T={2:X4}/align={3}/VBR={4:X8}/value={5:X8}/byte={6}' -f $form,$bank,$trace,$alignment,$vbr,$value,$byte
                        $expectedCombinations[$key] = if ($restart) {128} else {64}
                    } }
                } }
            } }
        }
    } elseif ($group -in @('rte-repair-boundaries','rte-repair-chained') -or $group.StartsWith('rte-retry-trace-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-pending-trace-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-user-master-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-cp-vectors-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-software-trace-', [StringComparison]::Ordinal)) {
        $softwareTrace = $group.StartsWith('rte-software-trace-', [StringComparison]::Ordinal)
        $cpVectors = $group.StartsWith('rte-cp-vectors-', [StringComparison]::Ordinal)
        $userMaster = $group.StartsWith('rte-user-master-', [StringComparison]::Ordinal)
        $pendingTrace = $group.StartsWith('rte-pending-trace-', [StringComparison]::Ordinal)
        $keepTrace = $softwareTrace -or $cpVectors -or $userMaster -or $pendingTrace -or $group.StartsWith('rte-retry-trace-', [StringComparison]::Ordinal)
        $chained = $group -eq 'rte-repair-chained' -or $group.StartsWith('rte-retry-trace-chained-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-pending-trace-chained-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-user-master-chained-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-cp-vectors-chained-', [StringComparison]::Ordinal) -or $group.StartsWith('rte-software-trace-chained-', [StringComparison]::Ordinal)
        $paths = if ($chained) { @('ISP-ISP','ISP-ISP-ISP','ISP-MSP-ISP','ISP-MSP','ISP-ISP-MSP','ISP-MSP-MSP',
            'MSP-ISP','MSP-ISP-ISP','MSP-MSP-ISP','MSP-MSP','MSP-ISP-MSP','MSP-MSP-MSP') } else { @('ISP','MSP') }
        foreach ($path in $paths) { foreach ($result in $(if ($cpVectors -or $softwareTrace) { @('user','user-M','ISP','MSP') } elseif ($userMaster) { @('user-M') } else { @('user','ISP','MSP') })) {
            foreach ($incoming in $(if ($chained -and (-not $keepTrace -or $softwareTrace)) { @(0x8000) } else { @(0,0x8000,0x4000) })) {
                foreach ($trace in @(0,0x8000,0x4000)) {
                    foreach ($alignment in $(if ($chained) { @(0,1) } else { @(0) })) {
                        foreach ($vbr in $(if ($chained) { @(0,0x10000) } else { @(0x10000) })) {
                            foreach ($form in $(if ($softwareTrace) { @('CU-linear','CU-flow','CP49','CP50','CP51','CP52','CP53','CP54','CP55') } elseif ($cpVectors) { @('CP50','CP51','CP52','CP53','CP54','CP55') } elseif ($pendingTrace) { @('CT','CU','CP') } elseif ($keepTrace -and -not $userMaster) { @('format0','format2','format3','invalid4','invalid15','normal','CM') } else { @('format0','format2','format3','invalid4','invalid15','normal','CM','CT','CU','CP') })) {
                              foreach ($clear in $(if ($softwareTrace) { @($false,$true) } else { @($false) })) {
                                # Literal read ranges are independent of the fixture iterator.
                                $reads = @('0:2')
                                if ($chained) {
                                    $reads += @('2:4','6:2')
                                    if ($softwareTrace -or $cpVectors -or $form -in @('normal','CM','CT','CU','CP')) { $reads += '12:2' }
                                    if ($softwareTrace -or $cpVectors -or $form -in @('CM','CT','CU','CP')) { $reads += '8:4' }
                                }
                                foreach ($read in $reads) {
                                    $bytes = if ($chained) { [int]$read.Split(':')[1] } else { 1 }
                                    for ($byte=0; $byte -lt $bytes; $byte++) {
                                        $key = '68040/RTE/{0}/{1}/path={2}/result={3}/incoming={4:X4}/T={5:X4}/align={6}/VBR={7:X8}/read={8}/fault-byte={9}' -f $(if ($softwareTrace) {'software-trace'} elseif ($cpVectors) {'cp-vectors'} elseif ($userMaster) {'user-master'} elseif ($pendingTrace) {'pending-trace'} elseif ($keepTrace) {'retry-trace'} else {'repair'}),$form,$path,$result,$incoming,$trace,$alignment,$vbr,$read,$byte
                                        $phases = if ($cpVectors -or $pendingTrace -or ($userMaster -and $form -in @('CT','CU','CP'))) { 8 } elseif ($keepTrace) { $(if ($incoming -eq 0) {7} else {8}) } elseif ($form -in @('CT','CU','CP')) { 9 } else { 8 }
                                        if ($softwareTrace) {
                                            $key += "/clear=$clear"
                                            # Literal program transitions, not fixture Phases().
                                            $phases = 10
                                            if ($form.StartsWith('CU')) { $phases++ }
                                            if ($form -eq 'CU-flow' -and $trace -ne 0x8000) { $phases += 2 }
                                            if ($trace -eq 0x8000 -or ($form -eq 'CU-flow' -and $trace -eq 0x4000)) {
                                                $phases += 5
                                                if ($clear) { $phases++ }
                                            }
                                        }
                                        $ccrs = if ($chained) { 2 } else { 32 }
                                        $expectedCombinations[$key] = $phases * $ccrs
                                    }
                                }
                              }
                            }
                        }
                    }
                }
            }
        } }
    } elseif ($group -eq 'rte-validation-batch') {
        foreach ($cached in @($false,$true)) { foreach ($prefix in @(0,1,3)) { foreach ($bank in @('ISP','MSP')) {
            foreach ($trace in @(0,0x8000,0x4000)) { foreach ($alignment in @(0,1)) { foreach ($vbr in @(0,0x10000)) {
                foreach ($stop in @('cap','before')) {
                    # Fixed validation read ranges, independent of the C# fixture.
                    foreach ($read in @('0:2','2:4','6:2','12:2','8:4')) {
                        for ($byte=0; $byte -lt [int]$read.Split(':')[1]; $byte++) {
                            $key = '68040/RTE/validation-batch/cached={0}/prefix={1}/bank={2}/T={3:X4}/align={4}/VBR={5:X8}/stop={6}/read={7}/byte={8}' -f $cached,$prefix,$bank,$trace,$alignment,$vbr,$stop,$read,$byte
                            $expectedCombinations[$key] = 32
                        }
                    }
                }
            } } }
        } } }
    } elseif ($group -eq 'access-fault-batch-dispatch') {
        foreach ($form in @('load','store','mixed-load','mixed-store','partial-store','self-fetch')) {
            foreach ($prefix in @(0,1,3)) { foreach ($bank in @('ISP','MSP')) { foreach ($alignment in @(0,1)) {
                foreach ($vbr in @(0,0x10000)) { foreach ($stop in @('cap','before','cycle')) {
                    foreach ($outcome in @('entry','fatal-stack','fatal-vector')) { foreach ($byte in 0..3) {
                        $key = '68040/access-fault/batch/{0}/prefix={1}/bank={2}/align={3}/VBR={4:X8}/stop={5}/outcome={6}/byte={7}' -f $form,$prefix,$bank,$alignment,$vbr,$stop,$outcome,$byte
                        $expectedCombinations[$key] = 2
                    } }
                } }
            } } }
        }
    } elseif ($group -in @('rte-access-entry-double-fault','rte-access-handler-refault')) {
        foreach ($bank in @('ISP','MSP')) { foreach ($trace in @(0,0x8000,0x4000)) { foreach ($alignment in @(0,1)) {
            foreach ($vbr in @(0,0x10000)) {
                if ($group -eq 'rte-access-entry-double-fault') {
                    foreach ($first in @(0,8)) { foreach ($second in 0..63) {
                        $key = '68040/RTE/access-entry-double-fault/bank={0}/T={1:X4}/align={2}/VBR={3:X8}/first={4}/second={5}' -f $bank,$trace,$alignment,$vbr,$first,$second
                        $expectedCombinations[$key] = 32
                    } }
                } else {
                    foreach ($read in @(0,2,6,12,8)) {
                        $key = '68040/RTE/access-handler-refault/bank={0}/T={1:X4}/align={2}/VBR={3:X8}/read={4}' -f $bank,$trace,$alignment,$vbr,$read
                        $expectedCombinations[$key] = 32
                    }
                }
            }
        } } }
    } elseif ($group.StartsWith('access-double-fault-dispatch-')) {
        $engine = $group.Substring('access-double-fault-dispatch-'.Length)
        foreach ($form in @('RTE','MOVE.B.read','MOVE.W.read','MOVE.L.read','MOVE.B.write','MOVE.W.write','MOVE.L.write')) {
            foreach ($bank in @('ISP','MSP')) { foreach ($alignment in @(0,1)) { foreach ($stage in @('stack','vector')) {
                $width = if ($stage -eq 'vector') {4} elseif ($form -eq 'RTE') {60} else {8}
                for ($byte=0; $byte -lt $width; $byte++) {
                    $key = '68040/access-double-fault/dispatch={0}/form={1}/bank={2}/align={3}/stage={4}/byte={5}' -f $engine,$form,$bank,$alignment,$stage,$byte
                    $expectedCombinations[$key] = 2
                }
            } } }
        }
    } elseif ($group.StartsWith('rte-mixed-fault-', [StringComparison]::Ordinal)) {
        $structure = $group.Contains('-structure-', [StringComparison]::Ordinal)
        foreach ($start in @('ISP','MSP')) { foreach ($tail in @('user','user-M','ISP','MSP')) {
        foreach ($middle in $(if ($structure) {@('none','user','user-M','ISP','MSP')} else {@('none')})) {
        foreach ($incoming in @(0,0x8000,0x4000)) { foreach ($first in @(0,0x8000,0x4000)) {
        foreach ($second in $(if ($middle -eq 'none') {@(0)} else {@(0,0x8000,0x4000)})) {
        foreach ($final in $(if ($structure) {@(0x8000)} else {@(0,0x8000,0x4000)})) {
        foreach ($alignment in $(if ($structure) {@(0,1)} else {@(0)})) {
        foreach ($vbr in $(if ($structure) {@(0,0x10000)} else {@(0x10000)})) {
        foreach ($form in @('format0','format2','format3','invalid4','invalid15','normal','CM','CT','CU','CP49','CP50','CP51','CP52','CP53','CP54','CP55')) {
            # Literal validation transfer ranges, independent of C# fixtures.
            $reads = @('2:4')
            if ($structure) {
                $reads = @('0:2','2:4','6:2')
                if ($form -in @('normal','CM','CT','CU') -or $form.StartsWith('CP')) {$reads += '12:2'}
                if ($form -in @('CM','CT','CU') -or $form.StartsWith('CP')) {$reads += '8:4'}
            }
            $path = if ($middle -eq 'none') {"$start-$tail"} else {"$start-$middle-$tail"}
            $secondName = if ($middle -eq 'none') {'none'} else {'{0:X4}' -f $second}
            foreach ($read in $reads) { for ($byte=0; $byte -lt $(if ($structure) {[int]$read.Split(':')[1]} else {1}); $byte++) {
                $key = '68040/RTE/mixed-epoch-validation/{0}/{1}/path={2}/incoming={3:X4}/first={4:X4}/second={5}/final={6:X4}/align={7}/VBR={8:X8}/read={9}/fault-byte={10}' -f $(if ($structure) {'structure'} else {'canonical'}),$form,$path,$incoming,$first,$secondName,$final,$alignment,$vbr,$read,$byte
                # Fault entry and bare return; a user tail then attempts RTE
                # in user mode, producing the documented privilege exception.
                $expectedCombinations[$key] = $(if ($structure) {2} else {32}) * $(if ($tail.StartsWith('user')) {3} else {2})
            } }
        } } } } } } } } } }
    } elseif ($group -in @('rte-validation-physical-direct','rte-validation-physical-chained')) {
        $chained = $group -eq 'rte-validation-physical-chained'
        $paths = if ($chained) { @('ISP-ISP','ISP-ISP-ISP','ISP-MSP-ISP','ISP-MSP','ISP-ISP-MSP','ISP-MSP-MSP',
            'MSP-ISP','MSP-ISP-ISP','MSP-MSP-ISP','MSP-MSP','MSP-ISP-MSP','MSP-MSP-MSP') } else { @('ISP','MSP') }
        foreach ($path in $paths) {
            foreach ($trace in @(0,0x8000,0x4000)) {
                foreach ($alignment in @(0,1)) {
                    foreach ($vbr in @(0,0x10000)) {
                        foreach ($form in @('format0','format2','format3','invalid4','invalid15','normal','CM','CT','CU','CP')) {
                            # Independent fixed byte ranges; no production or fixture read plan.
                            $reads = @('0:2','2:4','6:2')
                            if ($form -in @('normal','CM','CT','CU','CP')) { $reads += '12:2' }
                            if ($form -in @('CM','CT','CU','CP')) { $reads += '8:4' }
                            foreach ($read in $reads) {
                                $width = [int]$read.Split(':')[1]
                                for ($byte=0; $byte -lt $width; $byte++) {
                                    $key = '68040/RTE/validation-physical/{0}/path={1}/T={2:X4}/align={3}/VBR={4:X8}/read={5}/fault-byte={6}' -f $form,$path,$trace,$alignment,$vbr,$read,$byte
                                    $expectedCombinations[$key] = if ($chained) { 4 } else { 64 }
                                }
                            }
                        }
                    }
                }
            }
        }
    } elseif ($group -in @('rte-odd-normal','rte-odd-pending','address-error-fetch-040')) {
        if ($group -eq 'address-error-fetch-040') {
            foreach ($bank in @('user','ISP','MSP')) { foreach ($trace in @(0,0x8000,0x4000)) { foreach ($target in @(0x6001,0xff002003u)) {
                foreach ($vbr in @(0,0x10000)) { foreach ($alignment in @(0,1)) {
                    $key = '68040/fetch/odd/bank={0}/T={1:X4}/target={2:X8}/VBR={3:X8}/align={4}' -f $bank,$trace,$target,$vbr,$alignment
                    $expectedCombinations[$key] = 32
                } }
            } } }
        } else {
            $pending = $group -eq 'rte-odd-pending'
            $forms = if ($pending) { @('CT','CU','CP49','CP50','CP51','CP52','CP53','CP54','CP55') } else { @('format0','format2','format3','normal','CM') }
            foreach ($form in $forms) { foreach ($entry in @('ISP','MSP')) { foreach ($result in @('user','ISP','MSP')) { foreach ($trace in @(0,0x8000,0x4000)) {
                foreach ($target in @(0x6001,0xff002003u)) { foreach ($vbr in @(0,0x10000)) { foreach ($alignment in @(0,1)) {
                    $key = '68040/RTE/odd/{0}/entry={1}/result={2}/T={3:X4}/target={4:X8}/VBR={5:X8}/align={6}' -f $form,$entry,$result,$trace,$target,$vbr,$alignment
                    $expectedCombinations[$key] = 32 * $(if ($pending) {4} else {3})
                } } }
            } } } }
        }
    } elseif ($group.StartsWith('rte-mixed-epoch-', [StringComparison]::Ordinal)) {
        $structure = $group.Contains('-structure-', [StringComparison]::Ordinal)
        foreach ($start in @('ISP','MSP')) { foreach ($tail in @('user','user-M','ISP','MSP')) {
        foreach ($middle in $(if ($structure) {@('none','user','user-M','ISP','MSP')} else {@('none')})) {
        foreach ($result in @('user','user-M','ISP','MSP')) { foreach ($incoming in @(0,0x8000,0x4000)) {
        foreach ($first in @(0,0x8000,0x4000)) { foreach ($second in $(if ($middle -eq 'none') {@(0)} else {@(0,0x8000,0x4000)})) {
        foreach ($trace in @(0,0x8000,0x4000)) { foreach ($alignment in $(if ($structure) {@(0,1)} else {@(0)})) {
        foreach ($vbr in $(if ($structure) {@(0,0x10000)} else {@(0x10000)})) {
        foreach ($form in @('format0','format2','format3','normal','CM','CT','CU','CP49','CP50','CP51','CP52','CP53','CP54','CP55')) {
            $secondName = if ($middle -eq 'none') {'none'} else {'{0:X4}' -f $second}
            $key = '68040/RTE/mixed-epoch/{0}/{1}/start={2}/middle={3}/tail={4}/result={5}/incoming={6:X4}/first={7:X4}/second={8}/T={9:X4}/align={10}/VBR={11:X8}' -f $(if ($structure) {'structure'} else {'canonical'}),$form,$start,$middle,$tail,$result,$incoming,$first,$secondName,$trace,$alignment,$vbr
            # Literal executed program: RTE, optional exception return, following
            # operation; CM additionally returns its T1 trace and executes BRA.
            $phases = 2
            if ($incoming -ne 0 -or $form -in @('CT','CU') -or $form.StartsWith('CP')) {$phases++}
            if ($form -eq 'CM') {$phases++; if ($trace -eq 0x8000) {$phases++}}
            $expectedCombinations[$key] = $(if ($structure) {2} else {32}) * $phases
        } } } } } } } } } } }
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
    if ($expectedCombinations.Count -ne $expected[$group].combinations) { throw "$group independent combination selection differs" }
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
$passed = $testExit -eq 0 -and [int]$counters.failed -eq 0 -and [int]$counters.passed -eq 110 -and
    ($totals.mismatching + $totals.unsupported + $totals.untested) -eq 0
@{schema=1; model='68040'; logicalCases=67793440; xunitBatches=86; fixedExamples=24; counts=$totals; passed=$passed; roadmapComplete=$false} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'audit-summary.json')
if (-not $passed) { throw "040 access-frame audit incomplete: $($totals | ConvertTo-Json -Compress)" }
