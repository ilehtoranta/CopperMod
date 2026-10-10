[CmdletBinding()]
param([switch]$ValidateReportsOnly,[switch]$IncludeHistoricalRegression,[string]$OutputDirectory='artifacts/exg-wide')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$models=@('68000','68010','68EC020','68020','68030','68040','68060','A1200')
function Identity([string]$Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing EXG input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Validate-Selection($Actual,[string[]]$Required){
    $paths=@($Actual.path)
    if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
        @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count){throw 'Incomplete/duplicate EXG identity selection'}
    foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed EXG input: $($entry.path)"}}
}
function Expected-Keys([string]$Model){
    $keys=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    $banks=if($Model -in @('68EC020','68020','68030','68040','A1200')){@('user','ISP','MSP')}else{@('user','ISP')}
    foreach($bank in $banks){foreach($family in @('DD','AA','AD')){
        foreach($source in 0..7){foreach($destination in 0..7){foreach($pair in 0..1){
            if(-not $keys.TryAdd("$Model/EXG.L/$family/bank=$bank/r$source->r$destination/registers/pair=$pair",1)){throw 'Duplicate EXG register key'}
        }}}
        foreach($registers in @('6:2','2:2','7:3','2:7','7:7')){foreach($pair in 0..7){
            $source,$destination=$registers.Split(':')
            if(-not $keys.TryAdd("$Model/EXG.L/$family/bank=$bank/r$source->r$destination/boundaries/pair=$pair",32)){throw 'Duplicate EXG value key'}
        }}
    }}
    return ,$keys
}
$saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try {
    $inputs=@(@(& git ls-files Copper68k)+@('Copper68k.Tests/Copper68k.Tests.csproj',
        'Copper68k.Tests/Synthetic/SyntheticExgRegisterTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
        'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs',
        'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','scripts/test-copper68k-exg.ps1')|
        Sort-Object -Unique)
    if($IncludeHistoricalRegression){$inputs+='Copper68k.Tests/M68kInterpreterCoreBehaviorTests.cs'}
    $binaries=@('build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'|
        ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
    $evidence=@(@($models|ForEach-Object{"$_-transfer-exg-wide.json"})+@('exg.trx','run.log')|
        ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw 'Use a fresh EXG output directory'}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_})
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$output)
        $filter='FullyQualifiedName~SyntheticExgRegisterTests'
        if($IncludeHistoricalRegression){$filter+='|FullyQualifiedName~ExgAddressRegistersSwapsFullLongValues'}
        & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $output 'build') --filter $filter --logger 'trx;LogFileName=exg.trx' --results-directory $output *> (Join-Path $output 'run.log')
        if($LASTEXITCODE -ne 0){throw "EXG execution failed: $output/run.log"}
        foreach($entry in $original){if((Identity $entry.path).sha256 -cne $entry.sha256){throw 'EXG source changed during execution'}}
        @{schema=1;profile='integer-exg-wide';includeHistoricalRegression=[bool]$IncludeHistoricalRegression;
            sourceRevision=(& git rev-parse HEAD);inputs=$original;binaries=@($binaries|ForEach-Object{Identity $_});
            evidence=@($evidence|ForEach-Object{Identity $_})}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne 'integer-exg-wide' -or $id.includeHistoricalRegression -ne [bool]$IncludeHistoricalRegression -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete EXG identity profile'}
    Validate-Selection @($id.inputs) $inputs
    Validate-Selection @($id.binaries) $binaries
    Validate-Selection @($id.evidence) $evidence
    $prefix='Copper68k.Tests.Synthetic.SyntheticExgRegisterTests.'
    $names=@($models|ForEach-Object{$prefix+'WideValuesAliasesAndActiveStackBanks(modelId: "'+$_+'")'})+
        @(($prefix+'FixtureEncodingMatchesFixedExamples(kind: 72, source: 6, destination: 2, expected: 50510)'),
          ($prefix+'FixtureEncodingMatchesFixedExamples(kind: 64, source: 0, destination: 0, expected: 49472)'),
          ($prefix+'FixtureEncodingMatchesFixedExamples(kind: 136, source: 7, destination: 3, expected: 51087)'))
    if($IncludeHistoricalRegression){$names+='Copper68k.Tests.M68kInterpreterCoreBehaviorTests.ExgAddressRegistersSwapsFullLongValues'}
    [xml]$trx=Get-Content (Join-Path $output 'exg.trx') -Raw
    $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
    if($c.total -ne $names.Count -or $c.executed -ne $names.Count -or $c.passed -ne $names.Count -or $c.failed -ne 0 -or
        $results.Count -ne $names.Count -or @($results|Where-Object outcome -CNE Passed).Count -ne 0 -or
        @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count -ne 0){throw 'Incomplete EXG test selection'}
    $coverage=@()
    foreach($model in $models){
        $keys=Expected-Keys $model
        $cases=if($model -in @('68000','68010','68060')){8448}else{12672}
        $r=Get-Content (Join-Path $output "$model-transfer-exg-wide.json") -Raw|ConvertFrom-Json -AsHashtable
        if($r.schema -ne 1 -or $r.model -cne $model -or $r.group -cne 'transfer-exg-wide' -or $r.xunitBatches -ne 1 -or
            $r.logicalCases -ne $cases -or $r.counts.passing -ne $cases -or $r.counts.mismatching -ne 0 -or
            $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.failures.Count -ne 0 -or $r.combinations.Count -ne $keys.Count){throw "Incomplete EXG coverage: $model"}
        foreach($key in $keys.Keys){if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key].passing -ne $keys[$key]){throw "Missing/misweighted EXG combination: $key"}}
        $coverage+=@{model=$model;logicalCases=$cases;combinations=$keys.Count}
    }
    @{schema=1;logicalCases=88704;xunitBatches=8;fixedExamples=3;coverage=$coverage;roadmapComplete=$false;
        reference='M68000PM EXG, 4-105';scope='Architectural long register values, all bindings, CCR, aliases and active stack banks; following NOP; no timing qualification'}|
        ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
    Write-Host "EXG wide-register gate: 88704 passing scenarios / 10584 combinations across eight profiles; $output"
} finally {[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved);Pop-Location}
$global:LASTEXITCODE=0
