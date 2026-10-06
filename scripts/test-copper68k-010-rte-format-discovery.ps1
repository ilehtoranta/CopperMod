#requires -Version 7.0
[CmdletBinding()]
param([string]$ReferenceDirectory='artifacts/reference-winuae-rte-modern',
    [string]$OutputDirectory='artifacts/010-rte-format-discovery',
    [string]$VcVars='C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat',
    [switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$source=[IO.Path]::GetFullPath($ReferenceDirectory,$repo)
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$pin='5d22d33632646efc3f747f03e82d28353e52722e'
$groups=@('rte-format-discovery-scalar','rte-format-discovery-batch')
$fixtures=@('Copper68k.Tests/Synthetic/M68010RteFormatDiscoveryTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
    'Copper68k.Tests/Synthetic/SyntheticExecution.cs','Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs',
    'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','Copper68k.Tests/EnvironmentFactAttribute.cs','Copper68k.Tests/Copper68k.Tests.csproj',
    'scripts/reference/m68010-rte-format.cpp','scripts/test-copper68k-010-rte-format-discovery.ps1',
    'Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
$referenceFiles=@('build68k.cpp','table68k','gencpu.cpp','readcpu.cpp','missing.cpp','od-win32/unicode.cpp','newcpu.cpp')+
    @(& git -C $source ls-files 'include/*' 'od-win32/sysconfig.h')
$generated=@('build68k.exe','cpudefs.cpp','gencpu.exe','cpuemu_0.cpp','cpuemu_11.cpp','winuae-rte-format-sr.inc','winuae-rte-format-op.inc','observer.exe')
$evidence=@('discovery.trx')+@($groups|ForEach-Object{"68010-$_.json";"$_.rows";"$_.reference"})
function Hash($Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function FunctionText([string]$Text,[string]$Signature){
    $matches=[regex]::Matches($Text,[regex]::Escape($Signature)+'\s*\{')
    if($matches.Count -ne 1){throw "Reference definition differs: $Signature"}
    $at=$matches[0].Index;$brace=$Text.IndexOf('{',$at);$depth=1;$end=$brace+1
    while($depth -gt 0 -and $end -lt $Text.Length){if($Text[$end] -eq '{'){$depth++};if($Text[$end] -eq '}'){$depth--};$end++}
    if($depth){throw 'Incomplete reference function'}
    return $Text.Substring($at,$end-$at)
}
function Check-Identities($Rows,$Required,$Root,[string]$Kind){
    if(@($Rows).Count -ne $Required.Count -or @($Rows.file|Select-Object -Unique).Count -ne $Required.Count -or
        @(Compare-Object ($Required|Sort-Object) ($Rows.file|Sort-Object)).Count){throw "Missing/duplicate $Kind identity"}
    foreach($row in $Rows){if($row.sha256 -cnotmatch '^[a-f0-9]{64}$' -or (Hash (Join-Path $Root $row.file)) -cne $row.sha256){throw "Changed $Kind input: $($row.file)"}}
}
if((& git -C $source rev-parse HEAD) -cne $pin -or (& git -C $source status --porcelain)){throw 'Missing or modified pinned WinUAE checkout'}
if(-not $ValidateReportsOnly){
    if(Test-Path -LiteralPath $output){throw "Use fresh outputs: $output"}
    New-Item -ItemType Directory -Path $output|Out-Null
    $sourceFiles=@(& git -C $repo ls-files 'Copper68k/*')+@($fixtures|Where-Object{$_ -notlike '*.dll'})
    $originalInputs=@($sourceFiles|ForEach-Object{@{file=$_;sha256=(Hash (Join-Path $repo $_))}})
    $originalReferenceInputs=@($referenceFiles|ForEach-Object{@{file=$_;sha256=(Hash (Join-Path $source $_))}})
    $savedCompiler=@{}
    $settings=& $env:COMSPEC /c "`"$VcVars`" >nul && set"
    if($LASTEXITCODE -ne 0){throw 'MSVC setup failed'}
    foreach($setting in $settings){$at=$setting.IndexOf('=');if($at -gt 0){$name=$setting.Substring(0,$at);$savedCompiler[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$setting.Substring($at+1))}}
    Push-Location $output
    try{
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" (Join-Path $source 'build68k.cpp') /Fe:build68k.exe *> build68k.log
        if($LASTEXITCODE -ne 0){throw 'Opcode builder failed'}
        Get-Content -LiteralPath (Join-Path $source 'table68k')|& ./build68k.exe|Set-Content -Encoding utf8 cpudefs.cpp
        if($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath cpudefs.cpp -SimpleMatch 'n_defs68k = 232')){throw 'Missing opcode definitions'}
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" (Join-Path $source 'gencpu.cpp') (Join-Path $source 'readcpu.cpp') (Join-Path $source 'missing.cpp') (Join-Path $source 'od-win32/unicode.cpp') cpudefs.cpp user32.lib /Fe:gencpu.exe *> generator-build.log
        if($LASTEXITCODE -ne 0){throw 'Generator build failed'}
        & ./gencpu.exe *> generator-run.log
        if($LASTEXITCODE -ne 0){throw 'Reference generation failed'}
        $native=[IO.File]::ReadAllText((Join-Path $source 'newcpu.cpp'))
        (@('void REGPARAM2 MakeSR(void)','static void MakeFromSR_x(int t0trace)','void REGPARAM2 MakeFromSR_T0(void)','void REGPARAM2 MakeFromSR(void)')|
            ForEach-Object{FunctionText $native $_}) -join "`n`n"|Set-Content -Encoding utf8 winuae-rte-format-sr.inc
        (FunctionText ([IO.File]::ReadAllText((Join-Path $output 'cpuemu_0.cpp'))) 'uae_u32 REGPARAM2 op_4e73_4_ff(uae_u32 opcode)')+"`n`n"+
            (FunctionText ([IO.File]::ReadAllText((Join-Path $output 'cpuemu_11.cpp'))) 'uae_u32 REGPARAM2 op_4e73_11_ff(uae_u32 opcode)')|
            Set-Content -Encoding utf8 winuae-rte-format-op.inc
        & cl /nologo /O2 /EHsc /W4 "/I$output" (Join-Path $repo 'scripts/reference/m68010-rte-format.cpp') /Fe:observer.exe *> observer-build.log
        if($LASTEXITCODE -ne 0){throw 'RTE observer build failed'}
    }finally{Pop-Location;foreach($name in $savedCompiler.Keys){[Environment]::SetEnvironmentVariable($name,$savedCompiler[$name])}}
    & dotnet build (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release *> (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw 'RTE fixture build failed'}
    $saved=@{};$settings=@{COPPER68K_RUN_010_RTE_FORMAT_DISCOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output;COPPER68K_010_RTE_FORMAT_EXPORT=$output}
    try{
        foreach($name in $settings.Keys){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$settings[$name])}
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --no-build --no-restore --filter 'FullyQualifiedName~M68010RteFormatDiscoveryTests' --logger 'trx;LogFileName=discovery.trx' --results-directory $output *> (Join-Path $output 'run.log')
        $testExit=$LASTEXITCODE
        if($testExit -notin @(0,1)){throw "RTE tests did not execute: $testExit"}
    }finally{foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
    foreach($group in $groups){
        & (Join-Path $output 'observer.exe') (Join-Path $output "$group.rows") (Join-Path $output "$group.reference") 573440 *> (Join-Path $output "$group.reference.log")
        if($LASTEXITCODE -notin @(0,1)){throw "RTE reference execution failed: $group"}
    }
    $identity=@{schema=1;reference='WinUAE-010-RTE-format-discovery';referenceCommit=$pin;sourceCommit=(& git -C $repo rev-parse HEAD);
        softwareReferenceExecuted=$true;nativeImplementations=2;nativeLiteralControls=8;exceptionEntryComposed=$true;fullExceptionOracle=$false;architecturallyQualified=$false;roadmapComplete=$false;
        inputs=@();referenceInputs=@();generated=@();evidence=@()}
    Check-Identities $originalInputs $sourceFiles $repo 'fixture/CPU source during execution'
    Check-Identities $originalReferenceInputs $referenceFiles $source 'reference during execution'
    $identity.inputs=$originalInputs+@($fixtures|Where-Object{$_ -like '*.dll'}|ForEach-Object{@{file=$_;sha256=(Hash (Join-Path $repo $_))}})
    $identity.referenceInputs=$originalReferenceInputs
    foreach($file in $generated){$identity.generated+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    foreach($file in $evidence){$identity.evidence+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    $identity|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identities.json')
}
$identity=Get-Content (Join-Path $output 'identities.json') -Raw|ConvertFrom-Json
if($identity.schema -ne 1 -or $identity.reference -cne 'WinUAE-010-RTE-format-discovery' -or $identity.referenceCommit -cne $pin -or
    $identity.sourceCommit -cnotmatch '^[a-f0-9]{40}$' -or $identity.softwareReferenceExecuted -ne $true -or $identity.nativeImplementations -ne 2 -or
    $identity.nativeLiteralControls -ne 8 -or $identity.exceptionEntryComposed -ne $true -or $identity.fullExceptionOracle -ne $false -or
    $identity.architecturallyQualified -ne $false -or $identity.roadmapComplete -ne $false){throw 'Incomplete RTE discovery identity'}
Check-Identities $identity.inputs (@(& git -C $repo ls-files 'Copper68k/*')+$fixtures) $repo 'fixture/CPU'
Check-Identities $identity.referenceInputs $referenceFiles $source 'reference'
Check-Identities $identity.generated $generated $output 'generated'
Check-Identities $identity.evidence $evidence $output 'evidence'
[xml]$trx=Get-Content (Join-Path $output 'discovery.trx') -Raw
$c=$trx.TestRun.ResultSummary.Counters
if($c.total -ne 2 -or $c.executed -ne 2 -or $c.notExecuted -ne 0 -or ([int]$c.passed+[int]$c.failed) -ne 2){throw 'RTE discovery must execute both complete routes'}
foreach($method in @('InvalidFormatWordsAndConditionCodesScalar','InvalidFormatWordsAndConditionCodesBatch')){
    if(@($trx.TestRun.Results.UnitTestResult|Where-Object{$_.testName -ceq "Copper68k.Tests.Synthetic.M68010RteFormatDiscoveryTests.$method" -and $_.outcome -cin @('Passed','Failed')}).Count -ne 1){throw "Missing RTE execution: $method"}
}
$keys=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
$fixtureKeys=[Collections.Generic.List[string]]::new()
$fixture=[Collections.Generic.List[string]]::new();$index=0
foreach($matrix in @('words','ccr')){foreach($format in 0..65535){
if(($format -shr 12) -in @(0,8) -or ($matrix -eq 'ccr' -and ($format -band 4095) -notin @(0,4,0x24,0x3fc,0x7fc,0xfff))){continue}
foreach($supervisor in @($false,$true)){foreach($trace in $(if($matrix -eq 'words'){@(0)}else{@(0,0x8000)})){
foreach($stacked in $(if($matrix -eq 'words'){@(0xa71f)}else{@(0,31,0x2000,0x201f,0x8000,0x801f,0xa000,0xa01f)})){
foreach($target in $(if($matrix -eq 'words'){@([uint32]0xffff6001L)}else{@([uint32]0x6000,[uint32]0x6001,[uint32]0xffff6000L,[uint32]0xffff6001L)})){
    $key='68010/RTE/{0}/format={1:X4}/super={2}/T={3:X4}/stackedSR={4:X4}/target={5:X8}' -f $matrix,$format,$supervisor,$trace,$stacked,$target
    if(-not $keys.TryAdd($key,$(if($matrix -eq 'words'){2}else{32}))){throw "Duplicate RTE combination: $key"}
    foreach($ccr in $(if($matrix -eq 'words'){@(0,31)}else{0..31})){
        $sr=$(if($supervisor){0x2700}else{0x700}) -bor $trace -bor $ccr
        $vector=if($supervisor){14}else{8}
        $boundary=if($supervisor){($sr -band 0xfff1) -bor $(if($format -band 0x8000){8}else{0})}else{$sr}
        $fixture.Add(('{0:X8} {1:X8} {2:X8} {3:X8} {4:X8} {5:X8} {6:X8}' -f $index++,$sr,$stacked,$target,$format,$vector,$boundary))
        $fixtureKeys.Add($key)
    }
}}}}}}
if($fixture.Count -ne 573440 -or $keys.Count -ne 125440){throw "Independent RTE selection differs: $($fixture.Count)/$($keys.Count)"}
$mismatches=0;$summaries=@()
foreach($group in $groups){
    $report=Get-Content (Join-Path $output "68010-$group.json") -Raw|ConvertFrom-Json -AsHashtable
    if($report.schema -ne 1 -or $report.model -cne '68010' -or $report.group -cne $group -or $report.xunitBatches -ne 1 -or $report.logicalCases -ne 573440 -or
        $report.counts.unsupported -ne 0 -or $report.counts.untested -ne 0 -or ($report.counts.passing+$report.counts.mismatching) -ne 573440 -or $report.combinations.Count -ne $keys.Count){throw 'Incomplete RTE report'}
    foreach($key in $keys.Keys){if(-not $report.combinations.Contains($key) -or (@($report.combinations[$key].Values)|Measure-Object -Sum).Sum -ne $keys[$key] -or
        @($report.combinations[$key].Keys|Where-Object{$_ -cnotin @('passing','mismatching')}).Count){throw "Missing/misweighted RTE combination: $key"}}
    $rows=[IO.File]::ReadAllLines((Join-Path $output "$group.rows"));$reference=[IO.File]::ReadAllLines((Join-Path $output "$group.reference"))
    if($rows.Count -ne $fixture.Count -or $reference.Count -ne $fixture.Count){throw 'Incomplete RTE rows'}
    $nativeMismatch=0
    $classified=[Collections.Generic.Dictionary[string,int[]]]::new([StringComparer]::Ordinal)
    for($i=0;$i -lt $fixture.Count;$i++){
        $fields=$rows[$i].Split(' ');if($fields.Count -ne 16 -or ($fields[0..6] -join ' ') -cne $fixture[$i]){throw "RTE fixture order/selection differs: $i"}
        $numbers=[uint32[]]::new(16);for($n=0;$n -lt 16;$n++){$numbers[$n]=[uint32]::Parse($fields[$n],[Globalization.NumberStyles]::HexNumber)}
        if($numbers[13] -ne 0x7800 -or $numbers[14] -ne $(if($numbers[1] -band 0x2000){0x4700}else{0x8000})){throw "RTE initial stack banks differ: $i"}
        if($numbers[15] -ne 1){throw "RTE surrounding architectural state differs: $i"}
        $refFields=$reference[$i].Split(' ');if($refFields.Count -ne 6){throw "Malformed RTE reference row: $i"}
        $ref=[uint32[]]::new(6);for($n=0;$n -lt 6;$n++){$ref[$n]=[uint32]::Parse($refFields[$n],[Globalization.NumberStyles]::HexNumber)}
        $sp=if($numbers[1] -band 0x2000){0x46f8}else{0x7ff8}
        $match=$numbers[5] -eq $numbers[7] -and $numbers[6] -eq $numbers[8] -and $numbers[9] -eq 0x1000 -and $numbers[10] -eq $sp -and $numbers[11] -eq (0x9000+$numbers[5]*16)
        if($ref[0] -ne $i -or $ref[1] -ne $numbers[5] -or $ref[2] -ne $numbers[6] -or $ref[3] -ne 0x1000 -or
            $ref[4] -ne $(if($numbers[1] -band 0x2000){0x4700}else{0x7800}) -or $ref[5] -ne [int]$match -or $numbers[12] -ne [int]$match){throw "RTE reference/comparator outcome differs: $i"}
        if(-not $match){$nativeMismatch++}
        $key=$fixtureKeys[$i]
        if(-not $classified.ContainsKey($key)){$classified[$key]=[int[]]@(0,0)}
        $classified[$key][[int](-not $match)]++
    }
    if($nativeMismatch -ne $report.counts.mismatching){throw 'RTE native/comparator mismatch counts differ'}
    foreach($key in $keys.Keys){
        if([int]$report.combinations[$key].passing -ne $classified[$key][0] -or [int]$report.combinations[$key].mismatching -ne $classified[$key][1]){
            throw "RTE per-combination classifications differ: $key"
        }
    }
    & (Join-Path $output 'observer.exe') (Join-Path $output "$group.rows") (Join-Path $output "$group.replayed") 573440 *> (Join-Path $output "$group.replay.log")
    if($LASTEXITCODE -notin @(0,1) -or (Hash (Join-Path $output "$group.replayed")) -cne (Hash (Join-Path $output "$group.reference"))){throw 'Frozen RTE reference replay differs'}
    $mismatches+=$report.counts.mismatching;$summaries+=@{group=$group;logicalCases=573440;combinations=$keys.Count;surroundingStatePassing=573440;counts=$report.counts}
}
@{schema=1;softwareReferenceExecuted=$true;nativeImplementations=2;nativeLiteralControls=8;fullExceptionOracle=$false;exceptionEntryComposed=$true;
    architecturallyQualified=$false;roadmapComplete=$false;groups=$summaries;
    remaining='Rejected RTE CCR architecture, full native exception/trace frames, long/RMW/foreign 010 restart and broader model qualification/consolidation remain required'}|
    ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
if($mismatches){throw "RTE software-reference discovery found $mismatches mismatches; not an architectural CPU-fix claim"}
