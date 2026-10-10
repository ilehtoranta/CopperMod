#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory='artifacts/020-prefetch-recovery', [switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$sources=@(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj')+@(
    'Copper68k/M68kAdvancedTimingInterpreter.Access020.cs','Copper68k.Tests/Synthetic/SyntheticM68020PrefetchRecoveryTests.cs',
    'scripts/test-copper68k-020-prefetch-recovery.ps1') | Sort-Object -Unique)
$models=@('68EC020','A1200','68020','68030')
$groups=@('prefetch-recovery-witness','prefetch-recovery-matrix','prefetch-version','prefetch-rerun')
$reports=@(foreach ($model in $models) { foreach ($group in $groups) { foreach ($route in @('scalar','batch')) { "$model-$group-$route.json" } } })
$evidence=@('audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')+$reports
$manifestPath=Join-Path $output 'inputs.json'
if (-not $ValidateReportsOnly) {
    if (Test-Path $output) { throw "Use a fresh output directory: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $identities=@($sources | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $protected=@(@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') |
        Where-Object { Test-Path (Join-Path $repo $_) } | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $settings=@{COPPER68K_RUN_020_PREFETCH_RECOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output}; $saved=@{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68020PrefetchRecoveryTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    @{schema=1;sourceCommit=(& git -C $repo rev-parse HEAD);testExit=$testExit;sources=$identities;protected=$protected;
        evidence=@($evidence | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $output $_))} })} | ConvertTo-Json -Depth 10 | Set-Content $manifestPath
}
$manifest=Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.sources.Count -ne $sources.Count -or
    (($manifest.sources.path | Sort-Object) -join '|') -cne (($sources | Sort-Object) -join '|') -or
    $manifest.evidence.Count -ne $evidence.Count -or
    (($manifest.evidence.path | Sort-Object) -join '|') -cne (($evidence | Sort-Object) -join '|')) { throw 'Missing prefetch recovery identities' }
foreach ($item in $manifest.sources+$manifest.protected) { if ((Hash (Join-Path $repo $item.path)) -ne $item.sha256) { throw "Changed source/protected DLL: $($item.path)" } }
foreach ($item in $manifest.evidence) { if ((Hash (Join-Path $output $item.path)) -ne $item.sha256) { throw "Changed recovery evidence: $($item.path)" } }
[xml]$trx=Get-Content (Join-Path $output 'audit.trx') -Raw
$tests=@($trx.TestRun.Results.UnitTestResult)
$prefix='Copper68k.Tests.Synthetic.SyntheticM68020PrefetchRecoveryTests.'
$names=@(($prefix+'ScalarRecoveryMatrix'), ($prefix+'BatchRecoveryMatrix'))
foreach ($method in @('RepairedPipelineRunsWithoutOddBusFetch','IncompatibleVersionPreservesOriginalFrame','UnclearedRerunReentersAddressHandlerWithoutInstructionReplay')) {
    foreach ($route in @('False','True')) { $names+=($prefix+$method+'(batch: '+$route+')') }
}
if ($tests.Count -ne 8 -or @($tests.testId | Sort-Object -Unique).Count -ne 8 -or
    (($tests.testName | Sort-Object) -join '|') -cne (($names | Sort-Object) -join '|') -or
    @($tests | Where-Object outcome -ne 'Passed').Count -ne 0 -or $manifest.testExit -ne 0) { throw 'Incomplete/failed recovery execution roster' }
$rows=@(); $total=0
foreach ($model in $models) { foreach ($group in $groups) { foreach ($route in @('scalar','batch')) {
    $keys=@{}; $weight=if ($group -eq 'prefetch-recovery-matrix') {32} else {1}
    foreach ($bank in @('user','user-M','ISP','MSP')) {
        if ($group -eq 'prefetch-version') { foreach ($revision in 1..15) { $keys[('{0}/prefetch-version/bank={1}/revision={2:X}' -f $model,$bank,$revision)]=1 } }
        elseif ($group -eq 'prefetch-rerun') { $keys["$model/prefetch-rerun/bank=$bank/revision=0"]=1 }
        else { foreach ($trace in @(0,0x8000,0x4000)) { foreach ($target in @(0x6001,0x10006001)) {
            foreach ($sequence in $(if ($trace -eq 0) {@('single-branch','nop-branch')} else {@('single-branch')})) {
                $keys[('{0}/prefetch-recovery/bank={1}/T={2:X4}/target={3:X8}/sequence={4}' -f $model,$bank,$trace,$target,$sequence)]=$weight
            }
        } } }
    }
    $expected=$keys.Count*$weight
    $report=Get-Content (Join-Path $output "$model-$group-$route.json") -Raw | ConvertFrom-Json
    if ($report.schema -ne 1 -or $report.model -ne $model -or $report.group -ne "$group-$route" -or
        $report.logicalCases -ne $expected -or $report.xunitBatches -ne 1 -or @($report.combinations.psobject.Properties).Count -ne $keys.Count -or
        $report.counts.passing -ne $expected -or $report.counts.mismatching+$report.counts.unsupported+$report.counts.untested -ne 0) { throw 'Incomplete recovery report' }
    foreach ($combination in $report.combinations.psobject.Properties) {
        if (-not $keys.ContainsKey($combination.Name) -or @($combination.Value.psobject.Properties).Count -ne 1 -or
            $combination.Value.passing -ne $keys[$combination.Name]) { throw 'Changed recovery combination/weight' }
    }
    $method=switch ($group) {
        prefetch-recovery-matrix { if ($route -eq 'scalar') {'ScalarRecoveryMatrix'} else {'BatchRecoveryMatrix'} }
        prefetch-recovery-witness { 'RepairedPipelineRunsWithoutOddBusFetch' }
        prefetch-version { 'IncompatibleVersionPreservesOriginalFrame' }
        prefetch-rerun { 'UnclearedRerunReentersAddressHandlerWithoutInstructionReplay' }
    }
    $name=$prefix+$method+$(if ($group -ne 'prefetch-recovery-matrix') {'(batch: '+$(if ($route -eq 'scalar') {'False'} else {'True'})+')'} else {''})
    $test=@($tests | Where-Object testName -eq $name)[0]
    $matches=[regex]::Matches([string]$test.Output.StdOut,[regex]::Escape("$model/${group}-${route}: ")+'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)')
    if ($matches.Count -ne 1 -or [int]$matches[0].Groups[2].Value -ne $expected -or [int]$matches[0].Groups[3].Value -ne $keys.Count) { throw 'Missing actual recovery TRX summary' }
    $counts=$matches[0].Groups[1].Value | ConvertFrom-Json
    foreach ($status in @('passing','mismatching','unsupported','untested')) { if ($counts.$status -ne $report.counts.$status) { throw 'Recovery TRX/report disagreement' } }
    $total+=$expected; $rows+=@{model=$model;group="$group-$route";cases=$expected;keys=$keys.Count}
} } }
if ($total -ne 8960 -or $rows.Count -ne 32) { throw 'Recovery inventory changed' }
@{cases=$total;reports=$rows;roadmapComplete=$false} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'verification.json')
Write-Output '8,960 passing prefetch repair/refusal programs / 32 reports / eight executions.'
