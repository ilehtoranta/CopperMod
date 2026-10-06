[CmdletBinding()]
param([switch]$ValidateReportsOnly,[switch]$IncludeHistoricalRegression,[string]$OutputDirectory='artifacts/exg-mutations')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$source=Join-Path $repo 'Copper68k/M68kCore.cs'
$mutations=@(
    @{name='word-truncation';first='SetAddressRegister(left, State.A[right] & 0xffff);';second='SetAddressRegister(right, value & 0xffff);';mismatching=2096;passing=6352},
    @{name='lost-latched-input';first='SetAddressRegister(left, State.A[right]);';second='SetAddressRegister(right, State.A[left]);';mismatching=1568;passing=6880}
)
function Identity([string]$Path){
    $file=Join-Path $repo $Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing EXG mutation input: $Path"}
    return @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Validate-Selection($Actual,[string[]]$Required){
    $paths=@($Actual.path)
    if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
        @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count){throw 'Incomplete/duplicate EXG mutation identity selection'}
    foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed EXG mutation evidence: $($entry.path)"}}
}
function Mutated-Source([string]$Text,$Mutation){
    $start=$Text.IndexOf('if ((opcode & 0xF1F8) == 0xC148)')
    $end=$Text.IndexOf('if ((opcode & 0xF1F8) == 0xC188)',$start)
    if($start -lt 0 -or $end -le $start){throw 'Missing AA exchange scope'}
    $scope=$Text.Substring($start,$end-$start)
    foreach($anchor in @('SetAddressRegister(left, State.A[right]);','SetAddressRegister(right, value);')){
        if(($scope.Split($anchor).Length-1) -ne 1){throw 'Nonunique EXG mutation anchor'}
    }
    $scope=$scope.Replace('SetAddressRegister(left, State.A[right]);',$Mutation.first).Replace('SetAddressRegister(right, value);',$Mutation.second)
    return $Text.Substring(0,$start)+$scope+$Text.Substring($end)
}
function Restore-Source([byte[]]$Bytes,[string]$ExpectedHash,[string]$OriginalHash){
    $current=(Get-FileHash -LiteralPath $source).Hash
    if($current -ceq $OriginalHash){return}
    if($current -cne $ExpectedHash){throw 'CPU source changed concurrently; current file left intact'}
    [IO.File]::WriteAllBytes($source,$Bytes)
}
$saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try {
    $inputs=@(@(& git ls-files Copper68k)+@('Copper68k.Tests/Copper68k.Tests.csproj',
        'Copper68k.Tests/Synthetic/SyntheticExgRegisterTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
        'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs',
        'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','scripts/test-copper68k-exg-mutations.ps1')|Sort-Object -Unique)
    if($IncludeHistoricalRegression){$inputs+='Copper68k.Tests/M68kInterpreterCoreBehaviorTests.cs'}
    $normal=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll'|Where-Object{Test-Path -LiteralPath (Join-Path $repo $_)})
    if(-not $ValidateReportsOnly){
        if(Test-Path -LiteralPath $output){throw 'Use a fresh EXG mutation directory'}
        New-Item -ItemType Directory -Path $output|Out-Null
        $original=@($inputs|ForEach-Object{Identity $_});$originalNormal=@($normal|ForEach-Object{Identity $_})
        $bytes=[IO.File]::ReadAllBytes($source);$text=[IO.File]::ReadAllText($source)
        $sourceHash=(Get-FileHash -LiteralPath $source).Hash;$expectedSourceHash=$sourceHash
        $rows=@()
        try {
            foreach($mutation in $mutations){
                $directory=Join-Path $output $mutation.name
                New-Item -ItemType Directory -Path $directory|Out-Null
                $mutated=Mutated-Source $text $mutation
                [IO.File]::WriteAllText((Join-Path $directory 'mutated-M68kCore.cs'),$mutated)
                if((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash){throw 'CPU source changed before EXG mutation'}
                [IO.File]::WriteAllText($source,$mutated);$expectedSourceHash=(Get-FileHash -LiteralPath $source).Hash
                [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$directory)
                $filter='(FullyQualifiedName~SyntheticExgRegisterTests.WideValuesAliasesAndActiveStackBanks&DisplayName~68000)'
                if($IncludeHistoricalRegression){$filter+='|FullyQualifiedName~ExgAddressRegistersSwapsFullLongValues'}
                & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $directory 'build') --filter $filter --logger 'trx;LogFileName=mutation.trx' --results-directory $directory *> (Join-Path $directory 'run.log')
                $code=$LASTEXITCODE
                Restore-Source $bytes $expectedSourceHash $sourceHash
                if($code -ne 1){throw "EXG mutation did not fail: $($mutation.name)"}
                $paths=@('mutation.trx','run.log','mutated-M68kCore.cs','68000-transfer-exg-wide.json',
                    'build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'|
                    ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $directory $_))})
                $rows+=@{name=$mutation.name;exit=$code;evidence=@($paths|ForEach-Object{Identity $_})}
            }
        } finally {Restore-Source $bytes $expectedSourceHash $sourceHash}
        foreach($entry in @($original)+@($originalNormal)){if((Identity $entry.path).sha256 -cne $entry.sha256){throw 'EXG mutation changed original source/binaries'}}
        @{schema=1;profile='integer-exg-retirement-proofs';includeHistoricalRegression=[bool]$IncludeHistoricalRegression;
            sourceRevision=(& git rev-parse HEAD);sourceRestored=$true;inputs=$original;normalBinaries=$originalNormal;mutations=$rows}|
            ConvertTo-Json -Depth 10|Set-Content (Join-Path $output 'identity.json')
    }
    $id=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
    if($id.schema -ne 1 -or $id.profile -cne 'integer-exg-retirement-proofs' -or $id.includeHistoricalRegression -ne [bool]$IncludeHistoricalRegression -or $id.sourceRestored -ne $true -or $id.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete EXG mutation profile'}
    Validate-Selection @($id.inputs) $inputs;Validate-Selection @($id.normalBinaries) $normal
    if($id.mutations.Count -ne 2 -or @($id.mutations.name|Select-Object -Unique).Count -ne 2){throw 'Incomplete/duplicate EXG mutations'}
    $text=[IO.File]::ReadAllText($source)
    foreach($mutation in $mutations){
        $row=@($id.mutations|Where-Object name -CEQ $mutation.name)
        if($row.Count -ne 1 -or $row[0].exit -ne 1){throw 'Missing executed EXG mutation'}
        $directory=Join-Path $output $mutation.name
        $paths=@('mutation.trx','run.log','mutated-M68kCore.cs','68000-transfer-exg-wide.json',
            'build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'|
            ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $directory $_))})
        Validate-Selection @($row[0].evidence) $paths
        if([IO.File]::ReadAllText((Join-Path $directory 'mutated-M68kCore.cs')) -cne (Mutated-Source $text $mutation)){throw 'Wrong EXG mutation source scope'}
        $names=@('Copper68k.Tests.Synthetic.SyntheticExgRegisterTests.WideValuesAliasesAndActiveStackBanks(modelId: "68000")')
        if($IncludeHistoricalRegression){$names+='Copper68k.Tests.M68kInterpreterCoreBehaviorTests.ExgAddressRegistersSwapsFullLongValues'}
        [xml]$trx=Get-Content (Join-Path $directory 'mutation.trx') -Raw
        $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
        if($c.total -ne $names.Count -or $c.executed -ne $names.Count -or $c.failed -ne $names.Count -or $c.passed -ne 0 -or
            $results.Count -ne $names.Count -or @($results|Where-Object outcome -CNE Failed).Count -ne 0 -or
            @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count -ne 0){throw 'Incomplete EXG mutation execution'}
        $r=Get-Content (Join-Path $directory '68000-transfer-exg-wide.json') -Raw|ConvertFrom-Json -AsHashtable
        if($r.schema -ne 1 -or $r.model -cne '68000' -or $r.group -cne 'transfer-exg-wide' -or $r.logicalCases -ne 8448 -or
            $r.xunitBatches -ne 1 -or $r.counts.mismatching -ne $mutation.mismatching -or $r.counts.passing -ne $mutation.passing -or
            $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.combinations.Count -ne 1008 -or $r.failures.Count -ne $mutation.mismatching){throw 'Incomplete EXG mutation coverage'}
        foreach($bank in @('user','ISP')){foreach($family in @('DD','AA','AD')){
            foreach($scenario in @('registers','boundaries')){
                $bindings=if($scenario -ceq 'registers'){@(foreach($from in 0..7){foreach($to in 0..7){"${from}:$to"}})}else{@('6:2','2:2','7:3','2:7','7:7')}
                $pairs=if($scenario -ceq 'registers'){0..1}else{0..7};$weight=if($scenario -ceq 'registers'){1}else{32}
                foreach($binding in $bindings){$from,$to=$binding.Split(':');foreach($pair in $pairs){
                    $bad=$family -ceq 'AA' -and $(if($mutation.name -ceq 'lost-latched-input'){ $from -cne $to -and $pair -ne 7 }
                        elseif($from -ceq $to){$pair -in @(1,2,4,6)}else{$pair -ne 7})
                    $kind=if($bad){'mismatching'}else{'passing'}
                    $key="68000/EXG.L/$family/bank=$bank/r$from->r$to/$scenario/pair=$pair"
                    if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key][$kind] -ne $weight){throw "Wrong EXG mutation combination: $key"}
                }}
            }
        }}
        $witness='68000/EXG.L/AA/bank=ISP/r6->r2/boundaries/pair=0/op=C54E/ccr=00'
        if(@($r.failures|Where-Object{$_.id -ceq $witness -and $_.status -ceq 'mismatching'}).Count -ne 1){throw 'Historical EXG witness missing'}
    }
    @{schema=1;mutationProofs=2;includeHistoricalRegression=[bool]$IncludeHistoricalRegression;sourceRestored=$true;
        witness='68000/EXG.L/AA/bank=ISP/r6->r2/boundaries/pair=0/op=C54E/ccr=00';roadmapComplete=$false}|
        ConvertTo-Json -Depth 6|Set-Content (Join-Path $output 'summary.json')
    Write-Host "Both EXG mutations detect the complete replacement and historical witness; includeHistorical=$IncludeHistoricalRegression"
} finally {[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved);Pop-Location}
$global:LASTEXITCODE=0
