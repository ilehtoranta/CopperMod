[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/040-operand-write-mutations')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$programPath='Copper68k/M68040Support.cs'
$source=Join-Path $repo $programPath
$prefix='Copper68k.Tests.Synthetic.SyntheticM68040OperandWriteFaultTests.'
$names=@(($prefix+'MovePostincrementWriteFaultCreatesPendingWriteback(width: 1, opcode: 4288)'),
    ($prefix+'MovePostincrementWriteFaultCreatesPendingWriteback(width: 2, opcode: 12480)'),
    ($prefix+'MovePostincrementWriteFaultCreatesPendingWriteback(width: 4, opcode: 8384)'),
    ($prefix+'PredecrementFaultCommitsAliasedBaseOnce'),($prefix+'FullIndexedFaultKeepsLatchedOperandAndPendingTrace'),($prefix+'TraceFrameWriteFaultCannotChangeCompletedMoveDestination'))
$mutations=@(
    @{name='lost-original-operand-width';anchor='LogicalAddress = operand.Address, ByteCount = 4, WriteValue = operand.Value';replacement='LogicalAddress = operand.Address, ByteCount = 2, WriteValue = operand.Value';failed=@($names[3]);reason='fault entry: A0 expected'},
    @{name='missing-predecrement-completion';anchor='if (mode is 3 or 4)';replacement='if (mode == 3)';failed=@($names[3]);reason='fault entry: A0 expected'},
    @{name='wrong-bus-lanes';anchor='var shift = (int)(fault.LogicalAddress & 3) * 8;';replacement='var shift = 0;';failed=@($names[0],$names[1],$names[2]);reason='Expected'},
    @{name='lost-latched-data';anchor='uint data = fault.WriteValue!.Value;';replacement='uint data = 0;';failed=@($names[0],$names[1],$names[2],$names[3],$names[4]);reason='expected'},
    @{name='missing-trace-continuation';anchor='var trace = (savedSr & 0x8000) != 0;';replacement='var trace = false;';failed=@($names[4]);reason='fault entry: Memory'},
    @{name='missing-negative-flag';anchor='var sign = width == 1 ? 0x80u : width == 2 ? 0x8000u : 0x80000000u;';replacement='var sign = 0u;';failed=@($names[3],$names[4]);reason='fault entry: SR expected'},
    @{name='missing-exception-boundary';anchor='State.ExceptionSequence != ExecutionBoundaryExceptionSequence ||';replacement='';failed=@($names[5]);reason='Expected'}
)
function Identity([string]$Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing writeback mutation input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Validate-Selection($Actual,[string[]]$Required){
    $paths=@($Actual.path)
    if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
        ($paths.Count -gt 0 -and @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count)){throw 'Incomplete/duplicate writeback mutation identity selection'}
    foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed writeback mutation input: $($entry.path)"}}
}
function Mutated-Source([string]$Text,$Mutation){
    if(($Text.Split($Mutation.anchor).Length-1) -ne 1){throw 'Nonunique writeback mutation anchor'}
    return $Text.Replace($Mutation.anchor,$Mutation.replacement)
}
function Restore-Source([byte[]]$Bytes,[string]$ExpectedHash,[string]$OriginalHash){
    $current=(Get-FileHash -LiteralPath $source).Hash
    if($current -ceq $OriginalHash){return}
    if($current -cne $ExpectedHash){throw 'CPU source changed concurrently; current file left intact'}
    [IO.File]::WriteAllBytes($source,$Bytes)
}
function Evidence-Paths([string]$Directory,[bool]$Mutation){
    $leaves=@('witnesses.trx','run.log','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')
    if($Mutation){$leaves+='mutated-cpu.cs'}
    return @($leaves|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $Directory $_))})
}
function Check-Execution([string]$Directory,[string[]]$Failed,[string]$Reason=''){
    [xml]$trx=Get-Content (Join-Path $Directory 'witnesses.trx') -Raw
    $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
    $actualFailed=@($results|Where-Object outcome -CEQ Failed|ForEach-Object testName|Sort-Object)
    if($c.total -ne 6 -or $c.executed -ne 6 -or $c.failed -ne $Failed.Count -or $c.passed -ne (6-$Failed.Count) -or
        $results.Count -ne 6 -or @($results|Where-Object{$_.outcome -cnotin @('Passed','Failed')}).Count -ne 0 -or
        @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count -ne 0 -or
        $actualFailed.Count -ne $Failed.Count -or
        ($Failed.Count -gt 0 -and @(Compare-Object $actualFailed @($Failed|Sort-Object)).Count -ne 0)){throw 'Incomplete writeback mutation execution'}
    foreach($result in @($results|Where-Object outcome -CEQ Failed)){
        if($result.Output.ErrorInfo.Message -notmatch [regex]::Escape($Reason)){throw 'Writeback mutation failed for another reason'}
    }
}
Push-Location $repo
try {
    $inputs=@(@(& git ls-files Copper68k)+@('Copper68k.Tests/Copper68k.Tests.csproj',$programPath,
        'Copper68k.Tests/Synthetic/SyntheticM68040OperandWriteFaultTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
        'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs',
        'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','scripts/test-copper68k-040-operand-write-mutations.ps1')|Sort-Object -Unique)
    $normal=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll'|Where-Object{Test-Path -LiteralPath (Join-Path $repo $_)})
    $filter='FullyQualifiedName~SyntheticM68040OperandWriteFaultTests.MovePostincrement|FullyQualifiedName~SyntheticM68040OperandWriteFaultTests.PredecrementFault|FullyQualifiedName~SyntheticM68040OperandWriteFaultTests.FullIndexedFault|FullyQualifiedName~SyntheticM68040OperandWriteFaultTests.TraceFrameWriteFault'
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw 'Use a fresh writeback mutation directory'}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_});$originalNormal=@($normal|ForEach-Object{Identity $_})
        $bytes=[IO.File]::ReadAllBytes($source);$text=[IO.File]::ReadAllText($source)
        $sourceHash=(Get-FileHash -LiteralPath $source).Hash;$expectedHash=$sourceHash
        $baseline=Join-Path $output 'baseline';New-Item -ItemType Directory -Path $baseline|Out-Null
        & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $baseline 'build') --filter $filter --logger 'trx;LogFileName=witnesses.trx' --results-directory $baseline *> (Join-Path $baseline 'run.log')
        if($LASTEXITCODE -ne 0){throw 'Writeback mutation baseline failed'}
        Check-Execution $baseline @()
        $rows=@()
        try {
            foreach($mutation in $mutations){
                $directory=Join-Path $output $mutation.name;New-Item -ItemType Directory -Path $directory|Out-Null
                $mutated=Mutated-Source $text $mutation
                [IO.File]::WriteAllText((Join-Path $directory 'mutated-cpu.cs'),$mutated)
                if((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash){throw 'CPU source changed before mutation'}
                [IO.File]::WriteAllText($source,$mutated);$expectedHash=(Get-FileHash -LiteralPath $source).Hash
                & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $directory 'build') --filter $filter --logger 'trx;LogFileName=witnesses.trx' --results-directory $directory *> (Join-Path $directory 'run.log')
                $code=$LASTEXITCODE
                Restore-Source $bytes $expectedHash $sourceHash
                if($code -ne 1){throw "Writeback mutation did not fail: $($mutation.name)"}
                Check-Execution $directory @($mutation.failed) $mutation.reason
                $rows+=@{name=$mutation.name;exit=$code;evidence=@((Evidence-Paths $directory $true)|ForEach-Object{Identity $_})}
            }
        } finally {Restore-Source $bytes $expectedHash $sourceHash}
        foreach($entry in @($original)+@($originalNormal)){if((Identity $entry.path).sha256 -cne $entry.sha256){throw 'Writeback mutations changed original source/binaries'}}
        @{schema=1;profile='m68040-operand-write-defect-proofs';sourceRevision=(& git rev-parse HEAD);sourceRestored=$true;
            inputs=$original;normalBinaries=$originalNormal;baseline=@((Evidence-Paths $baseline $false)|ForEach-Object{Identity $_});mutations=$rows}|
            ConvertTo-Json -Depth 10|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne 'm68040-operand-write-defect-proofs' -or $id.sourceRestored -ne $true -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete writeback mutation profile'}
    Validate-Selection @($id.inputs) $inputs;Validate-Selection @($id.normalBinaries) $normal
    $baseline=Join-Path $output 'baseline';Validate-Selection @($id.baseline) (Evidence-Paths $baseline $false);Check-Execution $baseline @()
    if($id.mutations.Count -ne 7 -or @($id.mutations.name|Select-Object -Unique).Count -ne 7){throw 'Incomplete/duplicate writeback mutations'}
    $text=[IO.File]::ReadAllText($source)
    foreach($mutation in $mutations){
        $row=@($id.mutations|Where-Object name -CEQ $mutation.name)
        if($row.Count -ne 1 -or $row[0].exit -ne 1){throw 'Missing executed writeback mutation'}
        $directory=Join-Path $output $mutation.name;Validate-Selection @($row[0].evidence) (Evidence-Paths $directory $true)
        if([IO.File]::ReadAllText((Join-Path $directory 'mutated-cpu.cs')) -cne (Mutated-Source $text $mutation)){throw 'Wrong writeback mutation source scope'}
        Check-Execution $directory @($mutation.failed) $mutation.reason
    }
    @{schema=1;mutationProofs=7;sourceRestored=$true;productionCpuMutation=$true;roadmapComplete=$false;
        scope='CPU defects: original split-operand width, predecrement completion, bus lanes, latched data, CT, N and exception boundary; no regression retired'}|
        ConvertTo-Json -Depth 6|Set-Content (Join-Path $output 'summary.json')
    Write-Host 'Six bounded witnesses detect all seven targeted CPU defects; original CPU source and normal binaries preserved.'
} finally {Pop-Location}
$global:LASTEXITCODE=0
