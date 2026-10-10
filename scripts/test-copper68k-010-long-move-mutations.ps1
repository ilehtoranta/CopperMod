[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/010-long-move-mutations')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$source=Join-Path $repo 'Copper68k/M68kCore.cs'
$fixedMethods=@('SourceWordsContinueWithoutRepeatingPostincrement','DescendingWriteRetainsSourceAndCommitsBaseOnce')
$mutations=@(
    @{name='disabled-capture';old='if (!_m68010LongMovePending || (isWrite != (_m68010LongMovePhase >= 2)) ||';new='if (true || (isWrite != (_m68010LongMovePhase >= 2)) ||';failed=2;passed=0},
    @{name='lost-source-high';old='_m68010LongMoveValue = (uint)pending << 16;';new='_m68010LongMoveValue = pending;';failed=1;passed=1},
    @{name='repeated-source-increment';old='if (((opcode >> 3) & 7) == 3) SetAddressRegister(opcode & 7, State.A[opcode & 7] + 4);';new='if (((opcode >> 3) & 7) == 3) SetAddressRegister(opcode & 7, State.A[opcode & 7] + 8);';failed=1;passed=1},
    @{name='wrong-remaining-address';old='pending = ReadWord(unchecked(_m68010LongMoveAddress + 2));';new='pending = ReadWord(unchecked(_m68010LongMoveAddress + 4));';failed=1;passed=1},
    @{name='wrong-descending-commit';old='if (destinationMode == 4) SetAddressRegister(destinationRegister, _m68010LongMoveAddress);';new='if (destinationMode == 4) SetAddressRegister(destinationRegister, _m68010LongMoveAddress + 4);';failed=1;passed=1},
    @{name='lost-completed-output';old='_m68010LongMoveValue = M68010WordMoveResumeFrame.Long(words, 12);';new='_m68010LongMoveValue = M68010WordMoveResumeFrame.Long(words, 12) & 0xffff;';failed=2;passed=0},
    @{name='omitted-register-write';old='WritePlannedEaValue(in destination, value);';new='if (destinationMode != 0 || destinationRegister != 2) WritePlannedEaValue(in destination, value);';failed=1;passed=0;matrix=$true}
)
function Identity([string]$Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing mutation input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Mutated-Source([string]$Text,$Mutation){
    if($Mutation.matrix){
        $start=$Text.IndexOf('protected void ResumeM68010LongMove(')
        $end=$Text.IndexOf('protected ushort[]? CaptureM68010WordMoveResumeFrame(',$start)
        if($start -lt 0 -or $end -le $start){throw 'Missing long continuation scope'}
        $scope=$Text.Substring($start,$end-$start)
        if(($scope.Split($Mutation.old).Length-1) -ne 1){throw 'Nonunique register mutation anchor'}
        return $Text.Substring(0,$start)+$scope.Replace($Mutation.old,$Mutation.new)+$Text.Substring($end)
    }
    if(($Text.Split($Mutation.old).Length-1) -ne 1){throw "Nonunique mutation anchor: $($Mutation.name)"}
    return $Text.Replace($Mutation.old,$Mutation.new)
}
function Validate-Selection($Actual,[string[]]$Required){
    $paths=@($Actual.path)
    if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
        @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count){throw 'Incomplete/duplicate mutation identity selection'}
    foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed mutation evidence: $($entry.path)"}}
}
function Restore-Source([byte[]]$Bytes,[string]$ExpectedHash,[string]$OriginalHash){
    $current=(Get-FileHash -LiteralPath $source).Hash
    if($current -ceq $OriginalHash){return}
    if($current -cne $ExpectedHash){throw 'CPU source changed concurrently; current file left intact instead of overwriting edits'}
    [IO.File]::WriteAllBytes($source,$Bytes)
}
$saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try {
    $inputs=@(@(& git ls-files Copper68k Copper68k.Tests)+@('scripts/test-copper68k-010-long-move-mutations.ps1')|Sort-Object -Unique)
    $normal=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll'|Where-Object{Test-Path -LiteralPath (Join-Path $repo $_)})
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw 'Use a fresh mutation output directory'}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_})
        $originalNormal=@($normal|ForEach-Object{Identity $_})
        $bytes=[IO.File]::ReadAllBytes($source)
        $text=[IO.File]::ReadAllText($source)
        $sourceHash=(Get-FileHash -LiteralPath $source).Hash
        $expectedSourceHash=$sourceHash
        $records=@()
        try {
            foreach($mutation in $mutations){
                $directory=Join-Path $output $mutation.name
                New-Item -ItemType Directory -Path $directory|Out-Null
                $mutated=Mutated-Source $text $mutation
                [IO.File]::WriteAllText((Join-Path $directory 'mutated-M68kCore.cs'),$mutated)
                if((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash){throw 'CPU source changed before mutation; current file left intact'}
                [IO.File]::WriteAllText($source,$mutated)
                $expectedSourceHash=(Get-FileHash -LiteralPath $source).Hash
                $methods=if($mutation.matrix){@('SourceFaultOperandFormsValuesAndCcr')}else{$fixedMethods}
                $filter=($methods|ForEach-Object{"FullyQualifiedName~SyntheticM68010LongMoveRestartTests.$_"}) -join '|'
                [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$directory)
                & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $directory 'build') --filter $filter --logger 'trx;LogFileName=mutation.trx' --results-directory $directory *> (Join-Path $directory 'run.log')
                $code=$LASTEXITCODE
                Restore-Source $bytes $expectedSourceHash $sourceHash
                if($code -ne 1){throw "Mutation did not fail: $($mutation.name)"}
                $selected=@('mutation.trx','mutated-M68kCore.cs','run.log',
                    'build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')
                if($mutation.matrix){$selected+='68010-system-move-long-restart-source.json'}
                $records+=@{name=$mutation.name;exit=$code;evidence=@($selected|ForEach-Object{Identity ([IO.Path]::GetRelativePath($repo,(Join-Path $directory $_)))})}
            }
        } finally {Restore-Source $bytes $expectedSourceHash $sourceHash}
        foreach($entry in @($original)+@($originalNormal)){if((Identity $entry.path).sha256 -cne $entry.sha256){throw "Mutation changed original input: $($entry.path)"}}
        @{schema=1;profile='68010-long-move-seven-mutation-proofs';sourceRevision=(& git rev-parse HEAD);
            inputs=$original;normalBinaries=$originalNormal;sourceRestored=$true;mutations=$records}|
            ConvertTo-Json -Depth 10|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne '68010-long-move-seven-mutation-proofs' -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$' -or $id.sourceRestored -ne $true){throw 'Incomplete mutation profile'}
    Validate-Selection @($id.inputs) $inputs
    Validate-Selection @($id.normalBinaries) $normal
    if($id.mutations.Count -ne 7 -or @($id.mutations.name|Select-Object -Unique).Count -ne 7){throw 'Incomplete/duplicate mutation selection'}
    $text=[IO.File]::ReadAllText($source)
    foreach($mutation in $mutations){
        $row=@($id.mutations|Where-Object name -CEQ $mutation.name)
        if($row.Count -ne 1 -or $row[0].exit -ne 1){throw "Missing executed mutation: $($mutation.name)"}
        $directory=Join-Path $output $mutation.name
        $selected=@('mutation.trx','mutated-M68kCore.cs','run.log',
            'build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')
        if($mutation.matrix){$selected+='68010-system-move-long-restart-source.json'}
        $paths=@($selected|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $directory $_))})
        Validate-Selection @($row[0].evidence) $paths
        if([IO.File]::ReadAllText((Join-Path $directory 'mutated-M68kCore.cs')) -cne (Mutated-Source $text $mutation)){throw 'Mutation scope differs from intended change'}
        [xml]$trx=Get-Content (Join-Path $directory 'mutation.trx') -Raw
        $c=$trx.TestRun.ResultSummary.Counters
        $methods=if($mutation.matrix){@('SourceFaultOperandFormsValuesAndCcr')}else{$fixedMethods}
        $names=@($methods|ForEach-Object{"Copper68k.Tests.Synthetic.SyntheticM68010LongMoveRestartTests.$_"})
        $results=@($trx.TestRun.Results.UnitTestResult)
        if($c.total -ne $names.Count -or $c.executed -ne $names.Count -or $c.failed -ne $mutation.failed -or
            $c.passed -ne $mutation.passed -or $c.notExecuted -ne 0 -or $results.Count -ne $names.Count -or
            @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count -ne 0 -or
            @($results|Where-Object outcome -NOTIN @('Passed','Failed')).Count -ne 0){throw "Wrong mutation test selection/outcomes: $($mutation.name)"}
        $failedMethods=switch($mutation.name){
            {$_ -in @('disabled-capture','lost-completed-output')}{ $fixedMethods }
            'wrong-descending-commit'{@('DescendingWriteRetainsSourceAndCommitsBaseOnce')}
            'omitted-register-write'{@('SourceFaultOperandFormsValuesAndCcr')}
            default{@('SourceWordsContinueWithoutRepeatingPostincrement')}
        }
        $failedNames=@($failedMethods|ForEach-Object{"Copper68k.Tests.Synthetic.SyntheticM68010LongMoveRestartTests.$_"})
        $actualFailures=@($results|Where-Object outcome -CEQ Failed|ForEach-Object testName)
        if(@(Compare-Object ($actualFailures|Sort-Object) ($failedNames|Sort-Object)).Count){throw "Wrong failed mutation method: $($mutation.name)"}
        foreach($counter in @('error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','disconnected','inProgress','pending')){
            if([int]$c.$counter -ne 0){throw "Nonzero mutation terminal counter: $counter"}
        }
        if($mutation.matrix){
            $r=Get-Content (Join-Path $directory '68010-system-move-long-restart-source.json') -Raw|ConvertFrom-Json -AsHashtable
            if($r.schema -ne 1 -or $r.model -cne '68010' -or $r.group -cne 'system-move-long-restart-source' -or
                $r.logicalCases -ne 165888 -or $r.xunitBatches -ne 1 -or $r.counts.passing -ne 147456 -or
                $r.counts.mismatching -ne 18432 -or $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.combinations.Count -ne 648){throw 'Incomplete register mutation coverage'}
            $sources=@('(A0)','(A0)+','-(A0)','d16(A0)','index(A0)','abs.w','abs.l','d16(PC)','index(PC)')
            $destinations=@('(A1)','(A1)+','-(A1)','d16(A1)','index(A1)','abs.w','abs.l','D2','A2')
            foreach($from in $sources){foreach($to in $destinations){foreach($rr in 0..3){foreach($supervisor in @($false,$true)){
                $key="68010/MOVE.L/restart/source/$from/$to/super=$supervisor/RR=$rr"
                $kind=if($to -ceq 'D2'){'mismatching'}else{'passing'}
                if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key][$kind] -ne 256){throw "Misclassified mutation combination: $key"}
            }}}}
        }
    }
    @{schema=1;mutationProofs=7;executedTests=13;fixedTests=12;matrixCases=165888;matrixDetected=18432;
        sourceRestored=$true;normalBinariesUnchanged=$true;roadmapComplete=$false;
        scope='Targeted emulator continuation defects; no historical regression retired or hardware restart qualification'}|
        ConvertTo-Json -Depth 6|Set-Content (Join-Path $output 'summary.json')
    Write-Host "Seven 010 long MOVE mutations qualified; exact test/key outcomes and source restoration verified: $output"
} finally {[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved);Pop-Location}
$global:LASTEXITCODE=0
