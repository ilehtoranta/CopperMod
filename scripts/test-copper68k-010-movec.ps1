[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/010-movec')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$groups=[ordered]@{'system-010-movec-pairs'=688128;'system-010-movec-masks'=1007616;'system-010-movec-reads'=48128;'system-010-movec-encodings'=527872}
$names=@{0='SFC';1='DFC';2048='USP';2049='VBR'}
$initial=@{0=1;1=5;2048=0x7800;2049=0x400}
function Identity($Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing 010 MOVEC input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash}
}
function Add-Key($Keys,[string]$Key,[int]$Weight){if(-not $Keys.TryAdd($Key,$Weight)){throw "Duplicate 010 MOVEC combination: $Key"}}
function Write-Keys($Keys,$Controls,$Values,$Initials,[bool]$Pairs){
    foreach($control in $Controls){foreach($image in $Initials[$control]){foreach($value in $Values){foreach($general in 0..15){
    foreach($destination in $(if($Pairs){0..15}else{@($general)})){foreach($supervisor in @($false,$true)){
        $key='68010/MOVEC/L/{0}/R{1}/initial={2:X8}/value={3:X8}/super={4}' -f $names[$control],$general,$image,$value,$supervisor
        if($Pairs){$key+="/read-R$destination"}
        if($supervisor){Add-Key $Keys "$key/write" 32;Add-Key $Keys "$key/read" 32}else{Add-Key $Keys "$key/privilege" 32}
    }}}}}}
}
function Expected-Keys($Group){
    $keys=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    switch($Group){
        'system-010-movec-pairs'{Write-Keys $keys @(0,1,0x800,0x801) @([uint32]0,[uint32]7,[uint32]0x400,[uint32]0x12345678,[uint32]0xfffffffdL,[uint32]0xfffffffeL,[uint32]::MaxValue) $initial $true}
        'system-010-movec-masks'{
            $values=[Collections.Generic.HashSet[uint32]]::new();foreach($v in 0..7){[void]$values.Add($v)}
            foreach($bit in 3..31){[void]$values.Add([uint32]([uint64]1 -shl $bit))}
            foreach($v in @([uint32]0xfffffff8L,[uint32]::MaxValue,[uint32]0xaaaaaaabL,[uint32]0x55555554)){[void]$values.Add($v)}
            if($values.Count -ne 41){throw 'Incomplete 010 function-code samples'}
            Write-Keys $keys @(0,1) @($values|Sort-Object) @{0=0..7;1=0..7} $false
        }
        'system-010-movec-reads'{
            $addresses=[Collections.Generic.HashSet[uint32]]::new();foreach($bit in 0..31){[void]$addresses.Add([uint32]([uint64]1 -shl $bit))}
            foreach($v in @(0,7,0x400,0x4800,0x12345678,[uint32]::MaxValue,[uint32]0xaaaaaaaaL,[uint32]0x55555555)){[void]$addresses.Add($v)}
            if($addresses.Count -ne 39){throw 'Incomplete 010 address-control samples'}
            foreach($control in @(0,1,0x800,0x801)){foreach($value in $(if($control -lt 2){0..7}else{@($addresses|Sort-Object)})){foreach($general in 0..15){
                Add-Key $keys ('68010/MOVEC/L/{0}/read-R{1}/internal={2:X8}' -f $names[$control],$general,[uint32]$value) 32
            }}}
        }
        'system-010-movec-encodings'{
            foreach($control in 0..4095){foreach($general in 0..15){foreach($store in @($false,$true)){
                foreach($supervisor in $(if($names.ContainsKey($control)){@($false)}else{@($false,$true)})){
                    Add-Key $keys ('68010/MOVEC/L/control={0:X3}/R{1}/store={2}/super={3}' -f $control,$general,$store,$supervisor) $(if($names.ContainsKey($control)){32}else{2})
                }
            }}}
        }
        default{throw "Unknown 010 MOVEC selection: $Group"}
    }
    return ,$keys
}
$savedReports=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try{
    $inputs=@(& git ls-files Copper68k)+@('Copper68k.Tests/Synthetic/SyntheticM68010MovecTests.cs','Copper68k.Tests/Synthetic/SyntheticMovecRegisterFixture.cs',
        'Copper68k.Tests/Synthetic/SyntheticMachine.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs','Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs',
        'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','Copper68k.Tests/M68010InterpreterTests.cs','Copper68k.Tests/Copper68k.Tests.csproj',
        'scripts/test-copper68k-010-movec.ps1','scripts/test-copper68k-synthetic.ps1','scripts/test-copper68k-synthetic-mutations.ps1')
    $binaries=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
    $evidence=@($groups.Keys|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output "68010-$_.json"))})+
        @([IO.Path]::GetRelativePath($repo,(Join-Path $output 'movec.trx')))
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw "Use fresh 010 MOVEC outputs: $output"}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_})
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$output)
        & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --filter 'FullyQualifiedName~SyntheticM68010MovecTests' --logger 'trx;LogFileName=movec.trx' --results-directory $output *> (Join-Path $output 'run.log')
        if($LASTEXITCODE -ne 0){throw "010 MOVEC execution failed: $output/run.log"}
        foreach($item in $original){if((Identity $item.path).sha256 -cne $item.sha256){throw '010 MOVEC input changed during execution'}}
        @{schema=1;profile='68010-movec-manual-registers-and-encodings';sourceRevision=(& git rev-parse HEAD);
            inputs=$original;binaries=@($binaries|ForEach-Object{Identity $_});evidence=@($evidence|ForEach-Object{Identity $_})}|
            ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne '68010-movec-manual-registers-and-encodings' -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete 010 MOVEC identity'}
    foreach($selection in @(@{actual=@($id.inputs);required=$inputs},@{actual=@($id.binaries);required=$binaries},@{actual=@($id.evidence);required=$evidence})){
        $actual=@($selection.actual.path)
        if($actual.Count -ne $selection.required.Count -or @($actual|Select-Object -Unique).Count -ne $actual.Count -or
            @(Compare-Object ($actual|Sort-Object) ($selection.required|Sort-Object)).Count){throw 'Incomplete/duplicate 010 MOVEC input selection'}
        foreach($item in $selection.actual){if($item.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $item.path).sha256 -cne $item.sha256){throw "Changed/malformed 010 MOVEC input: $($item.path)"}}
    }
    [xml]$trx=Get-Content (Join-Path $output 'movec.trx') -Raw;$c=$trx.TestRun.ResultSummary.Counters
    if($c.total -ne 4 -or $c.executed -ne 4 -or $c.passed -ne 4 -or $c.failed -ne 0 -or $c.notExecuted -ne 0){throw 'Incomplete 010 MOVEC test selection'}
    foreach($method in @('Movec010EveryLegalControlRegisterPairPrivilegeAndCcr','Movec010FunctionCodeMasksFromEveryInitialImage',
        'Movec010RawControlImagesPreserveAllBitsAndActiveStack','Movec010UndefinedSelectorsAndLegalUserFormsHaveExactFrames')){
        if(@($trx.TestRun.Results.UnitTestResult|Where-Object{$_.testName -ceq "Copper68k.Tests.Synthetic.SyntheticM68010MovecTests.$method" -and $_.outcome -ceq 'Passed'}).Count -ne 1){throw "Missing 010 MOVEC execution: $method"}
    }
    $coverage=@()
    foreach($group in $groups.Keys){
        $r=Get-Content (Join-Path $output "68010-$group.json") -Raw|ConvertFrom-Json -AsHashtable
        if($r.schema -ne 1 -or $r.model -cne '68010' -or $r.group -cne $group -or $r.xunitBatches -ne 1 -or
            $r.logicalCases -ne $groups[$group] -or $r.counts.passing -ne $groups[$group] -or $r.counts.mismatching -ne 0 -or
            $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.failures.Count -ne 0){throw "Incomplete 010 MOVEC gate: $group"}
        $keys=Expected-Keys $group
        if($r.combinations.Count -ne $keys.Count){throw "010 MOVEC combination selection differs: $group"}
        foreach($key in $keys.Keys){if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key].passing -ne $keys[$key]){throw "Missing/misweighted 010 MOVEC combination: $key"}}
        $coverage+=@{group=$group;logicalCases=$groups[$group];combinations=$keys.Count}
    }
    @{schema=1;model='68010';logicalCases=2271744;xunitBatches=4;coverage=$coverage;roadmapComplete=$false;
        reference='MC68000UM figure 2-3 and 6.3.6/7; M68000PM MOVEC 6-22/23';
        scope='Nontraced IPL7 register/encoding semantics; no function-code bus spaces, restart, prefetch or physical timing oracle';
        remaining='Advanced 010/020/030 restoration, 040 CM/fault lifetime, 060 STOP/PCR disagreement, broader audits and consolidation remain'}|
        ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
    Write-Host "010 MOVEC qualified: 2271744 cases / 4 tests; $output"
}finally{[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$savedReports);Pop-Location}
