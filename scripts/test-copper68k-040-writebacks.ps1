[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/040-writebacks')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$groups=@('rte-writeback-canonical-scalar','rte-writeback-canonical-batch','rte-writeback-structure-scalar','rte-writeback-structure-batch')
function Identity([string]$Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing writeback input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Validate-Selection($Actual,[string[]]$Required){
    $paths=@($Actual.path)
    if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
        @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count){throw 'Incomplete/duplicate writeback identity selection'}
    foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed writeback input: $($entry.path)"}}
}
function Expected-Keys([bool]$Structure){
    $keys=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    $matrix=if($Structure){'structure'}else{'canonical'}
    foreach($handler in @('ISP','MSP')){foreach($restored in @('user','user-M','ISP','MSP')){foreach($lane in 0..3){
        $prefix="68040/RTE/writebacks/$matrix/handler=$handler/restore=$restored/lane=$lane"
        if(-not $Structure){
            foreach($slot in 1..3){foreach($width in @(1,2,4)){foreach($pair in 0..3){
                $mask=1 -shl ($slot-1)
                if(-not $keys.TryAdd("$prefix/valid=$mask/slot$slot-size$width/layout=distinct/T=0000/pair=$pair",32)){throw 'Duplicate canonical writeback key'}
            }}}
        }else{
            foreach($mask in 0..7){foreach($pattern in 0..3){foreach($layout in @('distinct','same','overlap')){
                foreach($trace in @('0000','8000','4000')){foreach($pair in @(2,3)){
                    if(-not $keys.TryAdd("$prefix/valid=$mask/pattern$pattern/layout=$layout/T=$trace/pair=$pair",2)){throw 'Duplicate structural writeback key'}
                }}
            }}}
        }
    }}}
    return ,$keys
}
$saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try {
    $inputs=@(@(& git ls-files Copper68k)+@('Copper68k.Tests/Copper68k.Tests.csproj',
        'Copper68k.Tests/Synthetic/SyntheticM68040WritebackTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040WritebackProgram.cs',
        'Copper68k.Tests/Synthetic/SyntheticMachine.cs','Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs',
        'Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs','Copper68k.Tests/Synthetic/SyntheticMoveTests.cs',
        'scripts/test-copper68k-040-writebacks.ps1')|Sort-Object -Unique)
    $binaries=@('build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'|
        ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
    $evidence=@(@($groups|ForEach-Object{"68040-$_.json"})+@('writebacks.trx','run.log')|
        ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw 'Use a fresh writeback output directory'}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_})
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$output)
        & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68040WritebackTests' --logger 'trx;LogFileName=writebacks.trx' --results-directory $output *> (Join-Path $output 'run.log')
        if($LASTEXITCODE -ne 0){throw "Writeback execution failed: $output/run.log"}
        foreach($entry in $original){if((Identity $entry.path).sha256 -cne $entry.sha256){throw 'Writeback source changed during execution'}}
        @{schema=1;profile='m68040-supplied-normal-writebacks';sourceRevision=(& git rev-parse HEAD);inputs=$original;
            binaries=@($binaries|ForEach-Object{Identity $_});evidence=@($evidence|ForEach-Object{Identity $_})}|
            ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne 'm68040-supplied-normal-writebacks' -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete writeback identity profile'}
    Validate-Selection @($id.inputs) $inputs;Validate-Selection @($id.binaries) $binaries;Validate-Selection @($id.evidence) $evidence
    $prefix='Copper68k.Tests.Synthetic.SyntheticM68040WritebackTests.'
    $names=@('CanonicalWritebacksAllLanesSizesValuesAndCcrScalar','CanonicalWritebacksAllLanesSizesValuesAndCcrBatch',
        'WritebackValidityOrderOverlapAndRestoredTraceScalar','WritebackValidityOrderOverlapAndRestoredTraceBatch','HandlerUsesFixedReferenceEncodings',
        'HandlerCompletesMemoryAlignedByteAtLaneOne','HandlerCompletesOverlappingMixedWritebacksInOrder'|
        ForEach-Object{$prefix+$_})+
        @(($prefix+'MemoryAlignedFixtureMatchesManualExamples(value: 120, width: 1, lane: 0, expected: 2028139268)'),
          ($prefix+'MemoryAlignedFixtureMatchesManualExamples(value: 120, width: 1, lane: 3, expected: 3521311608)'),
          ($prefix+'MemoryAlignedFixtureMatchesManualExamples(value: 4660, width: 2, lane: 3, expected: 887288594)'),
          ($prefix+'MemoryAlignedFixtureMatchesManualExamples(value: 305419896, width: 4, lane: 1, expected: 2014458966)'),
          ($prefix+'SuppliedHeaderMatchesManualFrameCombinations(mask: 1, ssw: 37, faultAddress: 16896)'),
          ($prefix+'SuppliedHeaderMatchesManualFrameCombinations(mask: 2, ssw: 1089, faultAddress: 16920)'),
          ($prefix+'SuppliedHeaderMatchesManualFrameCombinations(mask: 4, ssw: 261, faultAddress: 17152)'),
          ($prefix+'SuppliedHeaderMatchesManualFrameCombinations(mask: 0, ssw: 261, faultAddress: 17152)'))
    [xml]$trx=Get-Content (Join-Path $output 'writebacks.trx') -Raw
    $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
    if($c.total -ne 15 -or $c.executed -ne 15 -or $c.passed -ne 15 -or $c.failed -ne 0 -or
        $results.Count -ne 15 -or @($results|Where-Object outcome -CNE Passed).Count -ne 0 -or
        @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count){throw 'Incomplete writeback execution selection'}
    $coverage=@()
    foreach($group in $groups){
        $structure=$group.Contains('structure');$keys=Expected-Keys $structure
        $r=Get-Content (Join-Path $output "68040-$group.json") -Raw|ConvertFrom-Json -AsHashtable
        if($r.schema -ne 1 -or $r.model -cne '68040' -or $r.group -cne $group -or $r.xunitBatches -ne 1 -or
            $r.logicalCases -ne 36864 -or $r.counts.passing -ne 36864 -or $r.counts.mismatching -ne 0 -or
            $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.failures.Count -ne 0 -or $r.combinations.Count -ne $keys.Count){throw "Incomplete writeback coverage: $group"}
        foreach($key in $keys.Keys){if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key].passing -ne $keys[$key]){throw "Missing/misweighted writeback combination: $key"}}
        $coverage+=@{group=$group;scenarios=36864;combinations=$keys.Count}
    }
    @{schema=1;logicalCases=147456;xunitBatches=4;fixedExamples=9;boundedWitnesses=2;coverage=$coverage;roadmapComplete=$false;
        reference='MC68040UM 8.4.6.3/5/7';scope='Executed handler for supplied normal byte/word/long format-7 writebacks with translation/cache disabled';
        remaining='Actual data-fault creation, nested WB2/3 faults, cache pushes/MOVE16 lines, physical function-code spaces and hardware/physical timing remain unqualified'}|
        ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
    Write-Host "040 supplied writebacks: 147456 passing program scenarios / 39168 combinations; $output"
} finally {[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved);Pop-Location}
$global:LASTEXITCODE=0
