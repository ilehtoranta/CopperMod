#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory='artifacts/020-rte-faults', [switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$sources=@(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj')+
    @('Copper68k/M68kAdvancedTimingInterpreter.Rte020.cs','Copper68k.Tests/Synthetic/SyntheticM68020RteFaultTests.cs',
      'scripts/test-copper68k-020-rte-faults.ps1') | Sort-Object -Unique)
$models=@('68EC020','A1200','68020','68030'); $banks=@('user','user-M','ISP','MSP')
$groups=@('rte-validation-fault','rte-load-fault','rte-validation-matrix','rte-load-matrix','rte-validation-ccr','rte-load-ccr',
    'rte-handler-buffer','rte-handler-alias','rte-handler-refault','rte-handler-version','rte-entry-halt','rte-header-normal','rte-header-chained')
$reports=@(foreach($model in $models){foreach($group in $groups){foreach($route in @('scalar','batch')){"$model-$group-$route.json"}}})
$evidence=@('audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')+$reports
$manifestPath=Join-Path $output 'inputs.json'
if(-not $ValidateReportsOnly){
    if(Test-Path $output){throw "Use a fresh output directory: $output"}
    New-Item -ItemType Directory -Path $output | Out-Null
    $identities=@($sources | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $protected=@(@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') |
        Where-Object {Test-Path (Join-Path $repo $_)} | ForEach-Object {@{path=$_;sha256=(Hash (Join-Path $repo $_))}})
    $settings=@{COPPER68K_RUN_020_RTE_FAULTS='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output};$saved=@{}
    try{
        foreach($key in $settings.Keys){$saved[$key]=[Environment]::GetEnvironmentVariable($key);[Environment]::SetEnvironmentVariable($key,$settings[$key])}
        & dotnet.exe test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68020RteFaultTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    }finally{foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key])}}
    @{schema=1;sourceCommit=(& git -C $repo rev-parse HEAD);testExit=$testExit;sources=$identities;protected=$protected;
      evidence=@($evidence | ForEach-Object {@{path=$_;sha256=(Hash (Join-Path $output $_))}})} | ConvertTo-Json -Depth 10 | Set-Content $manifestPath
}
$manifest=Get-Content $manifestPath -Raw | ConvertFrom-Json
if($manifest.schema -ne 1 -or $manifest.sources.Count -ne $sources.Count -or
    (($manifest.sources.path|Sort-Object)-join '|') -cne (($sources|Sort-Object)-join '|') -or
    $manifest.evidence.Count -ne $evidence.Count -or
    (($manifest.evidence.path|Sort-Object)-join '|') -cne (($evidence|Sort-Object)-join '|')){throw 'Missing RTE fault identities'}
foreach($item in $manifest.sources+$manifest.protected){if((Hash (Join-Path $repo $item.path)) -ne $item.sha256){throw "Changed source/protected DLL: $($item.path)"}}
foreach($item in $manifest.evidence){if((Hash (Join-Path $output $item.path)) -ne $item.sha256){throw "Changed RTE evidence: $($item.path)"}}
[xml]$trx=Get-Content (Join-Path $output 'audit.trx') -Raw
$tests=@($trx.TestRun.Results.UnitTestResult)
$prefix='Copper68k.Tests.Synthetic.SyntheticM68020RteFaultTests.'
$names=@(foreach($method in @('ScalarFrameFaultMatrix','BatchFrameFaultMatrix','ScalarFrameFaultCcrBoundaries','BatchFrameFaultCcrBoundaries')){$prefix+$method})
foreach($route in @('False','True')){
    foreach($method in @('ValidationFaultPreservesFrameAndExplicitRteResumes','StateLoadFaultHaltsWithoutWritingMemory','FaultDuringValidationExceptionEntryHalts')){$names+=($prefix+$method+'(batch: '+$route+')')}
    foreach($mode in @('buffer','alias','refault','version')){$names+=($prefix+'HandlerControls(batch: '+$route+', mode: "'+$mode+'")')}
    foreach($chained in @('False','True')){$names+=($prefix+'HeaderFaultsInNormalFramesAndThrowawayChains(batch: '+$route+', chained: '+$chained+')')}
}
if($tests.Count -ne 22 -or @($tests.testId|Sort-Object -Unique).Count -ne 22 -or
    (($tests.testName|Sort-Object)-join '|') -cne (($names|Sort-Object)-join '|') -or
    @($tests|Where-Object outcome -ne 'Passed').Count -ne 0 -or $manifest.testExit -ne 0){throw 'Incomplete/failed RTE execution roster'}
# Requests are enumerated independently of the test/production read helpers.
$validation=@(@(0,2,1),@(2,4,1),@(6,2,1),@(0x36,2,1),@(0x5a,2,1))
$loading=@(@(8,2,1),@(10,2,1),@(0x14,4,1),@(0x24,4,1),@(12,2,1),@(14,2,1))
for($offset=0x10;$offset -lt 92;$offset+=2){$loading+=,@($offset,2,$(if($offset -in @(0x36,0x5a)){2}else{1}))}
$rows=@();$total=0
foreach($model in $models){foreach($group in $groups){foreach($route in @('scalar','batch')){
    $keys=@{};$weight=1;$load=$group.StartsWith('rte-load-');$phase=if($load){'loading'}else{'validation'}
    if($group.EndsWith('-fault')){foreach($bank in $banks){$keys[('{0}/RTE/{1}/bank={2}/read={3:X}' -f $model,$phase,$bank,$(if($load){8}else{0x5a}))]=1}}
    elseif($group.EndsWith('-matrix') -or $group.EndsWith('-ccr')){
        $ccrs=$group.EndsWith('-ccr')
        $requests=@(if($ccrs){,@($(if($load){8}else{0}),2,1)}elseif($load){$loading}else{$validation})
        foreach($bank in $banks){foreach($trace in @(0,0x8000,0x4000)){foreach($read in $requests){for($lane=0;$lane -lt $read[1];$lane++){
            $keys[('{0}/RTE/{1}/bank={2}/T={3:X4}/read={4:X}:{5}:{6}/byte={7}' -f $model,$phase,$bank,$trace,$read[0],$read[1],$read[2],$lane)]=$weight
        }}}}
    }elseif($group.StartsWith('rte-handler-')){
        $mode=$group.Substring(12);$requests=@(if($mode -eq 'buffer'){$validation[0..2]}else{,@($(if($mode -eq 'version'){0x36}else{0x5a}),2,1)})
        foreach($bank in $banks){foreach($read in $requests){for($lane=0;$lane -lt $read[1];$lane++){
            $keys[('{0}/RTE/handler={1}/bank={2}/read={3:X}:{4}:{5}/byte={6}' -f $model,$mode,$bank,$read[0],$read[1],$read[2],$lane)]=1
        }}}
    }elseif($group -eq 'rte-entry-halt'){
        foreach($bank in $banks){foreach($kind in @('write','vector')){
            $offsets=if($kind -eq 'vector'){@(0)}else{@(0..45|ForEach-Object {$_*2})};$width=if($kind -eq 'vector'){4}else{2}
            foreach($offset in $offsets){for($lane=0;$lane -lt $width;$lane++){$keys[('{0}/RTE/entry-halt/bank={1}/kind={2}/offset={3:X}/byte={4}' -f $model,$bank,$kind,$offset,$lane)]=1}}
        }}
    }else{
        $weight=2
        foreach($start in @('ISP','MSP')){$tails=if($group -eq 'rte-header-chained'){$banks}else{@($start)}
            foreach($tail in $tails){foreach($restored in $banks){foreach($format in @(0,2)){foreach($read in $validation[0..2]){for($lane=0;$lane -lt $read[1];$lane++){
                $keys[('{0}/RTE/header/start={1}/tail={2}/restore={3}/format={4}/read={5:X}:{6}/byte={7}' -f $model,$start,$tail,$restored,$format,$read[0],$read[1],$lane)]=$weight
            }}}}}
        }
    }
    $ccrValues=if($group.EndsWith('-ccr')){0..31}elseif($group.StartsWith('rte-header-')){@(0,31)}else{@(31)}
    $expanded=@{}
    foreach($key in $keys.Keys){foreach($ccr in $ccrValues){$expanded[('{0}/ccr={1:X2}' -f $key,$ccr)]=1}}
    $keys=$expanded;$weight=1
    $expected=$keys.Count
    $report=Get-Content (Join-Path $output "$model-$group-$route.json") -Raw | ConvertFrom-Json
    if($report.schema -ne 1 -or $report.model -ne $model -or $report.group -ne "$group-$route" -or
        $report.logicalCases -ne $expected -or $report.xunitBatches -ne 1 -or @($report.combinations.psobject.Properties).Count -ne $keys.Count -or
        $report.counts.passing -ne $expected -or $report.counts.mismatching+$report.counts.unsupported+$report.counts.untested -ne 0){throw "Incomplete RTE report: $model/$group/$route"}
    foreach($combination in $report.combinations.psobject.Properties){if(-not $keys.ContainsKey($combination.Name) -or
        @($combination.Value.psobject.Properties).Count -ne 1 -or $combination.Value.passing -ne $keys[$combination.Name]){throw 'Changed RTE combination/weight'}}
    $flag=if($route -eq 'scalar'){'False'}else{'True'}
    $method=switch -Regex ($group){
        '-matrix$' {$(if($route -eq 'scalar'){'Scalar'}else{'Batch'})+'FrameFaultMatrix';break}
        '-ccr$' {$(if($route -eq 'scalar'){'Scalar'}else{'Batch'})+'FrameFaultCcrBoundaries';break}
        '^rte-validation-fault$' {'ValidationFaultPreservesFrameAndExplicitRteResumes(batch: '+$flag+')';break}
        '^rte-load-fault$' {'StateLoadFaultHaltsWithoutWritingMemory(batch: '+$flag+')';break}
        '^rte-handler-' {'HandlerControls(batch: '+$flag+', mode: "'+$mode+'")';break}
        '^rte-entry-halt$' {'FaultDuringValidationExceptionEntryHalts(batch: '+$flag+')';break}
        default {'HeaderFaultsInNormalFramesAndThrowawayChains(batch: '+$flag+', chained: '+$(if($group -eq 'rte-header-chained'){'True'}else{'False'})+')'}
    }
    $test=@($tests|Where-Object testName -eq ($prefix+$method))[0]
    $matches=[regex]::Matches([string]$test.Output.StdOut,[regex]::Escape("$model/${group}-${route}: ")+'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)')
    if($matches.Count -ne 1 -or [int]$matches[0].Groups[2].Value -ne $expected -or [int]$matches[0].Groups[3].Value -ne $keys.Count){throw 'Missing actual RTE TRX summary'}
    $counts=$matches[0].Groups[1].Value|ConvertFrom-Json
    foreach($status in @('passing','mismatching','unsupported','untested')){if($counts.$status -ne $report.counts.$status){throw 'RTE TRX/report disagreement'}}
    $total+=$expected;$rows+=@{model=$model;group="$group-$route";cases=$expected;keys=$keys.Count}
}}}
if($total -ne 36096 -or $rows.Count -ne 104){throw 'RTE inventory changed'}
@{cases=$total;reports=$rows;roadmapComplete=$false}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $output 'verification.json')
Write-Output '36,096 passing RTE fault programs / 104 reports / 22 executions.'
