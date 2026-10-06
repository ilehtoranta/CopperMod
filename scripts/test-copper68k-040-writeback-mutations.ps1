[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/040-writeback-mutations')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$programPath='Copper68k.Tests/Synthetic/SyntheticM68040WritebackProgram.cs'
$source=Join-Path $repo $programPath
$prefix='Copper68k.Tests.Synthetic.SyntheticM68040WritebackTests.'
$names=@(($prefix+'HandlerCompletesMemoryAlignedByteAtLaneOne'),($prefix+'HandlerCompletesOverlappingMixedWritebacksInOrder'))
$mutations=@(
    @{name='missing-lane-rotation';anchor='Emit("lane-rotate", slot, [0xe3b8]);';replacement='Emit("lane-rotate", slot, [0x4e71]);';failed=$names;reason='lane-rotate/WB1'},
    @{name='reversed-writebacks';anchor='for (var slot = 1; slot <= 3; slot++)';replacement='for (var slot = 3; slot >= 1; slot--)';failed=@($names[1]);reason='Writeback results differ from independently intended operands'}
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
    if($current -cne $ExpectedHash){throw 'Writeback program changed concurrently; current file left intact'}
    [IO.File]::WriteAllBytes($source,$Bytes)
}
function Evidence-Paths([string]$Directory,[bool]$Mutation){
    $leaves=@('witnesses.trx','run.log','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')
    if($Mutation){$leaves+='mutated-program.cs'}
    return @($leaves|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $Directory $_))})
}
function Check-Execution([string]$Directory,[string[]]$Failed,[string]$Reason=''){
    [xml]$trx=Get-Content (Join-Path $Directory 'witnesses.trx') -Raw
    $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
    $actualFailed=@($results|Where-Object outcome -CEQ Failed|ForEach-Object testName|Sort-Object)
    if($c.total -ne 2 -or $c.executed -ne 2 -or $c.failed -ne $Failed.Count -or $c.passed -ne (2-$Failed.Count) -or
        $results.Count -ne 2 -or @($results|Where-Object{$_.outcome -cnotin @('Passed','Failed')}).Count -ne 0 -or
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
        'Copper68k.Tests/Synthetic/SyntheticM68040WritebackTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
        'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs',
        'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','scripts/test-copper68k-040-writeback-mutations.ps1')|Sort-Object -Unique)
    $normal=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll'|Where-Object{Test-Path -LiteralPath (Join-Path $repo $_)})
    $filter='FullyQualifiedName~HandlerCompletesMemoryAlignedByteAtLaneOne|FullyQualifiedName~HandlerCompletesOverlappingMixedWritebacksInOrder'
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
                [IO.File]::WriteAllText((Join-Path $directory 'mutated-program.cs'),$mutated)
                if((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash){throw 'Writeback program changed before mutation'}
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
        @{schema=1;profile='m68040-writeback-handler-defect-proofs';sourceRevision=(& git rev-parse HEAD);sourceRestored=$true;
            inputs=$original;normalBinaries=$originalNormal;baseline=@((Evidence-Paths $baseline $false)|ForEach-Object{Identity $_});mutations=$rows}|
            ConvertTo-Json -Depth 10|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne 'm68040-writeback-handler-defect-proofs' -or $id.sourceRestored -ne $true -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete writeback mutation profile'}
    Validate-Selection @($id.inputs) $inputs;Validate-Selection @($id.normalBinaries) $normal
    $baseline=Join-Path $output 'baseline';Validate-Selection @($id.baseline) (Evidence-Paths $baseline $false);Check-Execution $baseline @()
    if($id.mutations.Count -ne 2 -or @($id.mutations.name|Select-Object -Unique).Count -ne 2){throw 'Incomplete/duplicate writeback mutations'}
    $text=[IO.File]::ReadAllText($source)
    foreach($mutation in $mutations){
        $row=@($id.mutations|Where-Object name -CEQ $mutation.name)
        if($row.Count -ne 1 -or $row[0].exit -ne 1){throw 'Missing executed writeback mutation'}
        $directory=Join-Path $output $mutation.name;Validate-Selection @($row[0].evidence) (Evidence-Paths $directory $true)
        if([IO.File]::ReadAllText((Join-Path $directory 'mutated-program.cs')) -cne (Mutated-Source $text $mutation)){throw 'Wrong writeback mutation source scope'}
        Check-Execution $directory @($mutation.failed) $mutation.reason
    }
    @{schema=1;mutationProofs=2;sourceRestored=$true;productionCpuMutation=$false;roadmapComplete=$false;
        scope='Handler fixture defects: lost WB1 lane rotation and reversed stage order; no emulator bug or regression retirement claimed'}|
        ConvertTo-Json -Depth 6|Set-Content (Join-Path $output 'summary.json')
    Write-Host 'Both independent writeback witnesses detect their targeted handler defects; original program/CPU/binaries preserved.'
} finally {Pop-Location}
$global:LASTEXITCODE=0
