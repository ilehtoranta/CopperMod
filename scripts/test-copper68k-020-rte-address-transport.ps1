#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory='artifacts/020-rte-address-transport',[switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$Root=[IO.Path]::GetFullPath($OutputDirectory,$repo)
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$tracked=@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj')
if($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0){throw 'CPU/test source inventory unavailable'}
$sources=@($tracked+@('Copper68k.Tests/Synthetic/SyntheticM68020RteAddressTransportTests.cs','scripts/test-copper68k-020-rte-address-transport.ps1')|Sort-Object -Unique)
$reports=@(foreach($model in @('68EC020','A1200','68020','68030')){foreach($phase in @('control','fault')){foreach($route in @('scalar','batch')){"$model-rte-address-$phase-$route.json"}}})
$evidence=@('audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')+$reports
$manifestPath=Join-Path $Root 'inputs.json'
if(-not $ValidateReportsOnly){
    if(Test-Path $Root){throw "Use a fresh output directory: $Root"}
    New-Item -ItemType Directory -Path $Root|Out-Null
    $identities=@($sources|ForEach-Object {@{path=$_;sha256=(Hash (Join-Path $repo $_))}})
    $protected=@(@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')|
        Where-Object {Test-Path (Join-Path $repo $_)}|ForEach-Object {@{path=$_;sha256=(Hash (Join-Path $repo $_))}})
    $settings=@{COPPER68K_RUN_020_RTE_ADDRESS_TRANSPORT='1';COPPER68K_SYNTHETIC_REPORT_DIR=$Root};$saved=@{}
    try{
        foreach($key in $settings.Keys){$saved[$key]=[Environment]::GetEnvironmentVariable($key);[Environment]::SetEnvironmentVariable($key,$settings[$key])}
        & dotnet.exe test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $Root 'build') --filter 'FullyQualifiedName~SyntheticM68020RteAddressTransportTests' --logger 'trx;LogFileName=audit.trx' --results-directory $Root *> (Join-Path $Root 'execution.log')
        $testExit=$LASTEXITCODE
    }finally{foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key])}}
    @{schema=1;sourceCommit=(& git -C $repo rev-parse HEAD);testExit=$testExit;sources=$identities;protected=$protected;
      evidence=@($evidence|ForEach-Object {@{path=$_;sha256=(Hash (Join-Path $Root $_))}})}|ConvertTo-Json -Depth 10|Set-Content $manifestPath
}
$manifest=Get-Content $manifestPath -Raw|ConvertFrom-Json
if($manifest.schema -ne 1 -or $manifest.testExit -ne 0 -or $manifest.sources.Count -ne $sources.Count -or
   (($manifest.sources.path|Sort-Object)-join '|') -cne (($sources|Sort-Object)-join '|') -or
   $manifest.evidence.Count -ne $evidence.Count -or (($manifest.evidence.path|Sort-Object)-join '|') -cne (($evidence|Sort-Object)-join '|')){throw 'Missing/failed address-transport identities'}
foreach($item in $manifest.sources+$manifest.protected){if((Hash (Join-Path $repo $item.path)) -ne $item.sha256){throw 'Changed address-transport source/protected DLL'}}
foreach($item in $manifest.evidence){if((Hash (Join-Path $Root $item.path)) -ne $item.sha256){throw 'Changed address-transport evidence'}}
[xml]$trx=Get-Content (Join-Path $Root 'audit.trx') -Raw;$tests=@($trx.TestRun.Results.UnitTestResult)
$prefix='Copper68k.Tests.Synthetic.SyntheticM68020RteAddressTransportTests.'
$names=@(($prefix+'FaultFreeHighAndWrappedFrames(batch: False)'),($prefix+'FaultFreeHighAndWrappedFrames(batch: True)'),
    ($prefix+'ScalarHighAndWrappedValidationFaults'),($prefix+'BatchHighAndWrappedValidationFaults'))
if($tests.Count -ne 4 -or (($tests.testName|Sort-Object)-join '|') -cne (($names|Sort-Object)-join '|')){throw 'Wrong address-transport execution roster'}
foreach($test in $tests){if($test.outcome -ne 'Passed'){throw 'Wrong address-transport execution outcome'}}
$rows=@();$passing=0;$mismatching=0
foreach($model in @('68EC020','A1200','68020','68030')){foreach($phase in @('control','fault')){foreach($route in @('scalar','batch')){
    $group="rte-address-$phase-$route";$name="$model-$group.json";$report=Get-Content (Join-Path $Root $name) -Raw|ConvertFrom-Json
    $keys=@{}
    foreach($frame in @(0x4700L,0x10004700L,0xfffffbL,0xffffffL,0xfffffffbL,0xffffffffL,0x1000001L)){
        foreach($bank in @('ISP','MSP')){foreach($ccr in 0..31){
            $reads=@(if($phase -eq 'control'){,@(0,0)}else{@(0,2),@(2,4),@(6,2)})
            foreach($read in $reads){$lanes=if($phase -eq 'control'){@(0)}else{@(0..($read[1]-1))};foreach($lane in $lanes){
                $key='{0}/RTE/address/bank={1}/frame={2:X8}/read={3:X}:{4}/byte={5}/ccr={6:X2}' -f $model,$bank,$frame,$read[0],$read[1],$lane,$ccr
                $keys[$key]='passing'
            }}
        }}
    }
    $expected=$keys.Count;$failures=0
    if($report.schema -ne 1 -or $report.model -ne $model -or $report.group -ne $group -or $report.logicalCases -ne $expected -or
       $report.xunitBatches -ne 1 -or $report.counts.mismatching -ne $failures -or $report.counts.passing -ne $expected-$failures -or
       $report.counts.unsupported+$report.counts.untested -ne 0 -or
       (($report.combinations.psobject.Properties.Name|Sort-Object)-join '|') -cne (($keys.Keys|Sort-Object)-join '|')){throw "Incomplete address-transport report: $name"}
    foreach($key in $report.combinations.psobject.Properties){$status=$keys[$key.Name];if(@($key.Value.psobject.Properties).Count -ne 1 -or $key.Value.$status -ne 1){throw 'Wrong address-transport combination weight/outcome'}}
    if($report.failures.Count -ne $failures){throw 'Missing address-transport diagnostics'}
    foreach($failure in $report.failures){if($failure.status -ne 'mismatching' -or $failure.reason -ne 'wrapped validation request did not reject its physical byte exactly once'){throw 'Unrelated address-transport failure'}}
    $actual=[regex]::Matches(($tests.Output.StdOut -join "`n"),[regex]::Escape("$model/${group}: ")+'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)')
    if($actual.Count -ne 1 -or [int]$actual[0].Groups[2].Value -ne $expected -or [int]$actual[0].Groups[3].Value -ne $keys.Count){throw 'Missing actual address-transport TRX summary'}
    $counts=$actual[0].Groups[1].Value|ConvertFrom-Json
    foreach($status in @('passing','mismatching','unsupported','untested')){if($counts.$status -ne $report.counts.$status){throw 'Address-transport TRX/report disagreement'}}
    $passing+=$report.counts.passing;$mismatching+=$report.counts.mismatching
    $rows+=@{path=$name;sha256=(Hash (Join-Path $Root $name));cases=$expected;keys=$keys.Count;passing=$expected-$failures;mismatching=$failures}
}}}
if($passing -ne 32256 -or $mismatching -ne 0 -or $rows.Count -ne 16){throw 'Address-transport inventory changed'}
@{schema=1;fixed=$true;passing=$passing;mismatching=$mismatching;executions=4;reports=$rows;
    inputsSha256=(Hash (Join-Path $Root 'inputs.json'));helperSha256=(Hash $PSCommandPath);
    cpuSha256=(Hash (Join-Path $Root 'build/bin/Copper68k/release/Copper68k.dll'));testSha256=(Hash (Join-Path $Root 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'));
    trxSha256=(Hash (Join-Path $Root 'audit.trx'));roadmapComplete=$false}|ConvertTo-Json -Depth 9|Set-Content (Join-Path $Root 'verification.json')
Write-Output "$passing passing / $mismatching mismatches / 16 reports / four executions."
