[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/040-operand-writes')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
. (Join-Path $PSScriptRoot 'copper68k-040-operand-write-keys.ps1')
$groups=@(foreach($matrix in @('opcodes','boundaries','indexed')){foreach($route in @('scalar','batch')){"move-write-fault-$matrix-$route"}})
function Identity([string]$Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing operand-write input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Validate-Selection($Actual,[string[]]$Required){
    $paths=@($Actual.path)
    if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
        @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count){throw 'Incomplete/duplicate operand-write identity selection'}
    foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed operand-write input: $($entry.path)"}}
}
$saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try{
    $inputs=@(@(& git ls-files Copper68k)+@('Copper68k.Tests/Copper68k.Tests.csproj',
        'Copper68k.Tests/Synthetic/SyntheticM68040OperandWriteFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040WritebackProgram.cs',
        'Copper68k.Tests/Synthetic/SyntheticM68040AccessDoubleFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
        'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs',
        'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','Copper68k.Tests/Synthetic/MoveSpecification.cs',
        'Copper68k.Tests/Synthetic/AddressingFixtures.cs','scripts/copper68k-040-operand-write-keys.ps1',
        'scripts/test-copper68k-040-operand-writes.ps1')|Sort-Object -Unique)
    $binaries=@('build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'|
        ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
    $evidence=@(@($groups|ForEach-Object{"68040-$_.json"})+@('operand-writes.trx','run.log')|
        ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw 'Use a fresh operand-write output directory'}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_})
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$output)
        & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68040OperandWriteFaultTests' --logger 'trx;LogFileName=operand-writes.trx' --results-directory $output *> (Join-Path $output 'run.log')
        if($LASTEXITCODE -ne 0){throw "Operand-write execution failed: $output/run.log"}
        foreach($entry in $original){if((Identity $entry.path).sha256 -cne $entry.sha256){throw 'Operand-write source changed during execution'}}
        @{schema=1;profile='m68040-actual-move-write-faults';sourceRevision=(& git rev-parse HEAD);inputs=$original;
            binaries=@($binaries|ForEach-Object{Identity $_});evidence=@($evidence|ForEach-Object{Identity $_})}|
            ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne 'm68040-actual-move-write-faults' -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete operand-write identity profile'}
    Validate-Selection @($id.inputs) $inputs;Validate-Selection @($id.binaries) $binaries;Validate-Selection @($id.evidence) $evidence
    $prefix='Copper68k.Tests.Synthetic.SyntheticM68040OperandWriteFaultTests.'
    $names=@(foreach($method in @('EveryMemoryDestinationMoveOpcodeFaultsAndReturns','EveryLaneSizeValueCcrAndStackBank','FullIndexedAliasesAndTraceContinuation')){
        foreach($batch in @('False','True')){"$prefix${method}(batch: $batch)"}
    })+@(($prefix+'PredecrementFaultCommitsAliasedBaseOnce'),($prefix+'FullIndexedFaultKeepsLatchedOperandAndPendingTrace'),
        ($prefix+'TraceFrameWriteFaultCannotChangeCompletedMoveDestination'),
        ($prefix+'MovePostincrementWriteFaultCreatesPendingWriteback(width: 1, opcode: 4288)'),
        ($prefix+'MovePostincrementWriteFaultCreatesPendingWriteback(width: 2, opcode: 12480)'),
        ($prefix+'MovePostincrementWriteFaultCreatesPendingWriteback(width: 4, opcode: 8384)'))
    [xml]$trx=Get-Content (Join-Path $output 'operand-writes.trx') -Raw
    $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
    if($c.total -ne 12 -or $c.executed -ne 12 -or $c.passed -ne 12 -or $c.failed -ne 0 -or
        $results.Count -ne 12 -or @($results|Where-Object outcome -CNE Passed).Count -ne 0 -or
        @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count){throw 'Incomplete operand-write execution selection'}
    $coverage=@()
    foreach($group in $groups){
        $matrix=$group -replace '^move-write-fault-','' -replace '-(scalar|batch)$',''
        $keys=Get-Copper68k040OperandWriteKeys $matrix
        $cases=if($matrix -ceq 'opcodes'){7350}elseif($matrix -ceq 'boundaries'){9216}else{14256}
        $r=Get-Content (Join-Path $output "68040-$group.json") -Raw|ConvertFrom-Json -AsHashtable
        if($r.schema -ne 1 -or $r.model -cne '68040' -or $r.group -cne $group -or $r.xunitBatches -ne 1 -or
            $r.logicalCases -ne $cases -or $r.counts.passing -ne $cases -or $r.counts.mismatching -ne 0 -or
            $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.failures.Count -ne 0 -or $r.combinations.Count -ne $keys.Count){throw "Incomplete operand-write coverage: $group"}
        foreach($key in $keys.Keys){if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key].passing -ne $keys[$key]){throw "Missing/misweighted operand-write combination: $key"}}
        $coverage+=@{group=$group;scenarios=$cases;combinations=$keys.Count}
    }
    @{schema=1;logicalCases=61644;xunitBatches=6;boundedWitnesses=6;coverage=$coverage;roadmapComplete=$false;
        reference='MC68040UM 8.4.6, table 8-5, 8.4.6.7';scope='Actual rejected MOVE destination writes, format-7 WB1, handler, RTE and following instruction; scalar/batch public accurate CPU';
        remaining='Other integer/read/MOVEM/MOVE16 faults, compiled/cache/MMU fault pipelines, nested WB2/3, physical function-code spaces and hardware timing'}|
        ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
    Write-Host "040 actual MOVE writes: 61644 passing scenarios / 43788 combinations; $output"
}finally{[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved);Pop-Location}
$global:LASTEXITCODE=0
