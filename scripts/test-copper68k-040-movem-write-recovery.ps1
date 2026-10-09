#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory='artifacts/040-movem-write-recovery', [switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifestPath=Join-Path $output 'inputs.json'
$sources=@(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj') + @(
    'Copper68k.Tests/Synthetic/SyntheticM68040MovemWriteRecoveryTests.cs','scripts/test-copper68k-040-movem-write-recovery.ps1') | Sort-Object -Unique)
$reports=@(foreach ($group in @('movem-write-recovery','movem-write-recovery-witness','movem-write-pointer-alias')) { foreach ($route in @('scalar','batch')) { "68040-$group-$route.json" } })
$evidence=@('audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')+$reports
if (-not $ValidateReportsOnly) {
    if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $identities=@($sources | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $protected=@(@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') |
        Where-Object { Test-Path -LiteralPath (Join-Path $repo $_) } | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $settings=@{COPPER68K_RUN_040_MOVEM_WRITE_RECOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output}; $saved=@{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68040MovemWriteRecoveryTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    @{schema=1;reference='MC68040UM 8.4.6.2/5/7';referenceUrl='https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf';
        sourceCommit=(& git -C $repo rev-parse HEAD);testExit=$testExit;sources=$identities;protected=$protected;
        evidence=@($evidence | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $output $_))} })} |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath
}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.sources.Count -ne $sources.Count -or
    @(Compare-Object ($manifest.sources.path | Sort-Object) $sources).Count -ne 0 -or $manifest.evidence.Count -ne $evidence.Count -or
    @(Compare-Object ($manifest.evidence.path | Sort-Object) ($evidence | Sort-Object)).Count -ne 0) { throw 'Missing recovery identities' }
foreach ($item in $manifest.sources) { if ((Hash (Join-Path $repo $item.path)) -ne $item.sha256) { throw "Changed recovery source: $($item.path)" } }
foreach ($item in $manifest.evidence) { if ((Hash (Join-Path $output $item.path)) -ne $item.sha256) { throw "Changed recovery evidence: $($item.path)" } }
foreach ($item in $manifest.protected) { if ((Hash (Join-Path $repo $item.path)) -ne $item.sha256) { throw "Normal DLL changed: $($item.path)" } }
[xml]$trx=Get-Content (Join-Path $output 'audit.trx') -Raw
$tests=@($trx.TestRun.Results.UnitTestResult)
$prefix='Copper68k.Tests.Synthetic.SyntheticM68040MovemWriteRecoveryTests.'
$names=@('ScalarRecoveryMatrix','BatchRecoveryMatrix','EveryCanonicalStoreFormRunsHandlerAndResumes(batch: False)',
    'EveryCanonicalStoreFormRunsHandlerAndResumes(batch: True)','SelfOverwrittenIndirectPointerIsNotResolvedAgain(batch: False)',
    'SelfOverwrittenIndirectPointerIsNotResolvedAgain(batch: True)') | ForEach-Object { $prefix+$_ }
if ($tests.Count -ne 6 -or $trx.TestRun.ResultSummary.Counters.executed -ne 6 -or
    @($tests.testId | Sort-Object -Unique).Count -ne 6 -or
    @(Compare-Object ($tests.testName | Sort-Object) ($names | Sort-Object)).Count -ne 0) { throw 'Incomplete recovery execution roster' }
if (@($tests | Where-Object { $_.outcome -notin @('Passed','Failed') }).Count -ne 0) { throw 'Unexecuted recovery test' }
function AddKeys([hashtable]$Map,[string]$Cohort,[string[]]$Forms,[string[]]$Banks,[string[]]$Traces,[int]$Weight,[bool]$Witness) {
    foreach ($form in $Forms) { foreach ($bank in $Banks) { foreach ($trace in $Traces) { foreach ($width in @(2,4)) {
        $transfers=if ($Witness) {@(3)} else {@(0,1,2,3)}
        $bytes=if ($Witness) {@($width-1)} else {@(0..($width-1))}
        foreach ($transfer in $transfers) { foreach ($byte in $bytes) {
            $key="68040/MOVEM/write-fault/$Cohort/size=$width/$form/bank=$bank/T=$trace/transfer=$transfer/byte=$byte"
            if ($Map.ContainsKey($key)) { throw "Duplicate independent key: $key" }; $Map[$key]=$Weight
        } }
    } } } }
}
# Independently enumerate the architectural store forms and full-format fields.
$forms=@(foreach ($mode in @(2,4,5,6,7)) { for ($register=0; $register -lt $(if ($mode -eq 7) {2} else {8}); $register++) { "mode=$mode/reg=$register" } })
$canonical=@('mode=2/reg=0','mode=4/reg=0','mode=4/reg=7','mode=5/reg=0','mode=6/reg=0','mode=7/reg=0','mode=7/reg=1')
$fullForms=@(foreach ($bs in @('False','True')) { foreach ($is in @('False','True')) { foreach ($bd in 1..3) { foreach ($iis in @(0,1,2,3,5,6,7)) {
    if ($is -eq 'True' -and $iis -ge 5) { continue }
    foreach ($ix in @('D1/W/scale=1','D1/L/scale=1','D1/W/scale=8','A0/W/scale=4')) {
        "mode=6/reg=1/index=full/bs=$bs/is=$is/bd=$bd/iis=$iis/ix=$ix/pointer-alias=False"
    }
} } } })
if ($forms.Count -ne 34 -or $fullForms.Count -ne 264) { throw 'Invalid independent form inventory' }
$required=@{}; $witness=@{}; $alias=@{}
AddKeys $required recovery-opcode $forms @('ISP') @('0000') 1 $false
AddKeys $required recovery-status $canonical @('user','user-M','ISP','MSP') @('0000','8000','4000') 32 $false
AddKeys $required recovery-structure $fullForms @('ISP') @('0000') 1 $false
AddKeys $witness recovery-witness $forms @('ISP') @('0000') 1 $true
AddKeys $alias pointer-alias @('mode=6/reg=0/index=full/bs=False/is=False/bd=2/iis=1/ix=D1/L/scale=1/pointer-alias=True') @('user','user-M','ISP','MSP') @('0000','8000','4000') 1 $true
$rows=@()
foreach ($group in @('movem-write-recovery','movem-write-recovery-witness','movem-write-pointer-alias')) { foreach ($route in @('scalar','batch')) {
    $inventory=if ($group -eq 'movem-write-recovery') {$required} elseif ($group -eq 'movem-write-recovery-witness') {$witness} else {$alias}
    $expected=if ($group -eq 'movem-write-recovery') {71664} elseif ($group -eq 'movem-write-recovery-witness') {68} else {24}
    $report=Get-Content (Join-Path $output "68040-$group-$route.json") -Raw | ConvertFrom-Json
    if ($report.schema -ne 1 -or $report.model -ne '68040' -or $report.group -ne "$group-$route" -or
        $report.logicalCases -ne $expected -or $report.xunitBatches -ne 1 -or @($report.combinations.psobject.Properties).Count -ne $inventory.Count) { throw "Incomplete recovery report: $group/$route" }
    $sum=@{passing=0;mismatching=0;unsupported=0;untested=0}
    foreach ($combination in $report.combinations.psobject.Properties) {
        if (-not $inventory.ContainsKey($combination.Name)) { throw "Foreign recovery combination: $($combination.Name)" }
        $weight=0
        foreach ($status in $combination.Value.psobject.Properties) {
            if (-not $sum.ContainsKey($status.Name) -or $status.Value -le 0) { throw 'Invalid recovery status/weight' }
            $sum[$status.Name]+=$status.Value; $weight+=$status.Value
        }
        if ($weight -ne $inventory[$combination.Name]) { throw "Changed recovery combination weight: $($combination.Name)" }
    }
    foreach ($status in $sum.Keys) { if ($sum[$status] -ne $report.counts.$status) { throw "Recovery totals disagree: $group/$route/$status" } }
    $testName=if ($group -eq 'movem-write-recovery') {if ($route -eq 'scalar') {'ScalarRecoveryMatrix'} else {'BatchRecoveryMatrix'}} else {
        $(if ($group -eq 'movem-write-recovery-witness') {'EveryCanonicalStoreFormRunsHandlerAndResumes'} else {'SelfOverwrittenIndirectPointerIsNotResolvedAgain'})+
        '(batch: '+$(if ($route -eq 'scalar') {'False'} else {'True'})+')'
    }
    $test=@($tests | Where-Object testName -eq ($prefix+$testName))[0]
    if (($sum.mismatching+$sum.unsupported+$sum.untested -eq 0) -ne ($test.outcome -eq 'Passed')) { throw 'Recovery TRX/report outcome disagreement' }
    $rows += [pscustomobject]@{group=$group;route=$route;logicalCases=$report.logicalCases;counts=$sum;keys=$inventory.Count}
} }
@{schema=1;rows=$rows;wholePrograms=143512;roadmapComplete=$false;inputManifestSha256=(Hash $manifestPath)} |
    ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output 'verification.json')
$rows | Format-Table | Out-Host
if (@($tests | Where-Object outcome -ne 'Passed').Count -ne 0 -or $manifest.testExit -ne 0 -or
    @($rows | Where-Object {$_.counts.mismatching+$_.counts.unsupported+$_.counts.untested -gt 0}).Count -ne 0) { throw "040 MOVEM write recovery fails; retain evidence: $output" }
