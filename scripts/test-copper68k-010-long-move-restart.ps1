[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/010-long-move-restart')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$groups=[ordered]@{'system-move-long-restart-source'=165888;'system-move-long-restart-destination'=172032;
    'system-move-long-restart-edges'=5120;'system-move-long-restart-a7'=512;'system-move-long-restart-invalid'=640}
function Identity($Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing 010 long MOVE input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash}
}
function Add-Key($Keys,[string]$Key,[int]$Weight){if(-not $Keys.TryAdd($Key,$Weight)){throw "Duplicate 010 long MOVE combination: $Key"}}
function Expected-Keys($Group){
    $keys=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    switch($Group){
        {$_ -in @('system-move-long-restart-source','system-move-long-restart-destination')}{
            $writing=$Group.EndsWith('destination')
            $sources=@('(A0)','(A0)+','-(A0)','d16(A0)','index(A0)','abs.w','abs.l','d16(PC)','index(PC)')
            $destinations=@('(A1)','(A1)+','-(A1)','d16(A1)','index(A1)','abs.w','abs.l')
            if($writing){$sources+=@('D2','A2','immediate')}else{$destinations+=@('D2','A2')}
            foreach($source in $sources){foreach($destination in $destinations){foreach($rr in 0..3){foreach($supervisor in @($false,$true)){
                Add-Key $keys ("68010/MOVE.L/restart/"+$(if($writing){'destination'}else{'source'})+"/$source/$destination/super=$supervisor/RR=$rr") 256
            }}}}
        }
        'system-move-long-restart-edges'{
            foreach($kind in @('copied','nested','alias','prefetched','trace')){foreach($rr in 0..15){foreach($supervisor in @($false,$true)){
                Add-Key $keys "68010/MOVE.L/restart/$kind/super=$supervisor/RR=$rr" 32
            }}}
        }
        'system-move-long-restart-a7'{
            foreach($kind in @('source-post','source-pre','dest-post','dest-pre')){foreach($rr in 0..3){Add-Key $keys "68010/MOVE.L/restart/A7/$kind/RR=$rr" 32}}
        }
        'system-move-long-restart-invalid'{
            foreach($writing in @($false,$true)){foreach($corruption in @('version','opcode','direction','phase','pc','queue-address','queue-count','operand-parity','value','descending')){
                Add-Key $keys "68010/MOVE.L/restart/invalid/$corruption/write=$writing" 32
            }}
        }
        default{throw "Unknown 010 long MOVE selection: $Group"}
    }
    return ,$keys
}
$savedReports=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try{
    $inputs=@((@(& git ls-files Copper68k)+@('Copper68k/M68010LongMoveResumeFrame.cs',
        'Copper68k.Tests/Synthetic/SyntheticM68010LongMoveRestartTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
        'Copper68k.Tests/Synthetic/SyntheticM68010Format8Tests.cs',
        'Copper68k.Tests/Synthetic/AddressingFixtures.cs','Copper68k.Tests/Synthetic/MoveSpecification.cs',
        'Copper68k.Tests/Synthetic/SyntheticExecution.cs','Copper68k.Tests/Synthetic/SyntheticSystemTests.cs',
        'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticMoveTests.cs',
        'Copper68k.Tests/Copper68k.Tests.csproj','scripts/test-copper68k-010-long-move-restart.ps1',
        'scripts/test-copper68k-synthetic.ps1'))|Sort-Object -Unique)
    $binaries=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
    $evidence=@($groups.Keys|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output "68010-$_.json"))})+
        @([IO.Path]::GetRelativePath($repo,(Join-Path $output 'restart.trx')))
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw "Use fresh 010 long MOVE outputs: $output"}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_})
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$output)
        & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --filter 'FullyQualifiedName~SyntheticM68010LongMoveRestartTests' --logger 'trx;LogFileName=restart.trx' --results-directory $output *> (Join-Path $output 'run.log')
        if($LASTEXITCODE -ne 0){throw "010 long MOVE execution failed: $output/run.log"}
        foreach($item in $original){if((Identity $item.path).sha256 -cne $item.sha256){throw '010 long MOVE input changed during execution'}}
        @{schema=1;profile='68010-long-move-private-word-continuation';sourceRevision=(& git rev-parse HEAD);
            inputs=$original;binaries=@($binaries|ForEach-Object{Identity $_});evidence=@($evidence|ForEach-Object{Identity $_})}|
            ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne '68010-long-move-private-word-continuation' -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete 010 long MOVE identity'}
    foreach($selection in @(@{actual=@($id.inputs);required=$inputs},@{actual=@($id.binaries);required=$binaries},@{actual=@($id.evidence);required=$evidence})){
        $actual=@($selection.actual.path)
        if($actual.Count -ne $selection.required.Count -or @($actual|Select-Object -Unique).Count -ne $actual.Count -or
            @(Compare-Object ($actual|Sort-Object) ($selection.required|Sort-Object)).Count){throw 'Incomplete/duplicate 010 long MOVE input selection'}
        foreach($item in $selection.actual){if($item.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $item.path).sha256 -cne $item.sha256){throw "Changed/malformed 010 long MOVE input: $($item.path)"}}
    }
    [xml]$trx=Get-Content (Join-Path $output 'restart.trx') -Raw;$c=$trx.TestRun.ResultSummary.Counters
    if($c.total -ne 7 -or $c.executed -ne 7 -or $c.passed -ne 7 -or $c.failed -ne 0 -or $c.notExecuted -ne 0){throw 'Incomplete 010 long MOVE test selection'}
    foreach($method in @('SourceWordsContinueWithoutRepeatingPostincrement','DescendingWriteRetainsSourceAndCommitsBaseOnce',
        'SourceFaultOperandFormsValuesAndCcr','DestinationFaultOperandFormsValuesAndCcr',
        'CopiedNestedAliasedPrefetchedAndTracedContinuations','UserStackLongOperandsPreserveSupervisorBank',
        'InvalidPrivateContinuationPreservesFrameBeforeFormatError')){
        if(@($trx.TestRun.Results.UnitTestResult|Where-Object{$_.testName -ceq "Copper68k.Tests.Synthetic.SyntheticM68010LongMoveRestartTests.$method" -and $_.outcome -ceq 'Passed'}).Count -ne 1){throw "Missing 010 long MOVE execution: $method"}
    }
    $coverage=@()
    foreach($group in $groups.Keys){
        $r=Get-Content (Join-Path $output "68010-$group.json") -Raw|ConvertFrom-Json -AsHashtable
        if($r.schema -ne 1 -or $r.model -cne '68010' -or $r.group -cne $group -or $r.xunitBatches -ne 1 -or
            $r.logicalCases -ne $groups[$group] -or $r.counts.passing -ne $groups[$group] -or $r.counts.mismatching -ne 0 -or
            $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.failures.Count -ne 0){throw "Incomplete 010 long MOVE gate: $group"}
        $keys=Expected-Keys $group
        if($r.combinations.Count -ne $keys.Count){throw "010 long MOVE combination selection differs: $group"}
        foreach($key in $keys.Keys){if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key].passing -ne $keys[$key]){throw "Missing/misweighted 010 long MOVE combination: $key"}}
        $coverage+=@{group=$group;logicalCases=$groups[$group];combinations=$keys.Count}
    }
    @{schema=1;model='68010';logicalCases=344192;xunitBatches=5;fixedRegressions=2;coverage=$coverage;roadmapComplete=$false;
        reference='MC68000UM 6.3.9.2/6.3.10/6.4; M68000PM MOVE/MOVEA';
        scope='Generated private long-MOVE/MOVEA images, two 16-bit cycles, independent RR choices and original per-word addresses; retained timing policy';
        remaining='No executed external continuation oracle: foreign silicon images, external BERR, RMW, non-MOVE restoration and physical restart timing remain unqualified'}|
        ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
    Write-Host "010 private long MOVE qualified: 344192 scenarios / 5 reports + 2 fixed regressions; $output"
}finally{[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$savedReports);Pop-Location}
$global:LASTEXITCODE=0
