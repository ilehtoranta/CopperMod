#requires -Version 7.0
[CmdletBinding()]
param([string]$ReferenceDirectory='artifacts/reference-winuae-rte-modern',
    [string]$OutputDirectory='artifacts/060-stop-discovery',
    [string]$VcVars='C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat',
    [switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$source=[IO.Path]::GetFullPath($ReferenceDirectory,$repo)
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$pin='5d22d33632646efc3f747f03e82d28353e52722e'
$groups=@('stop-discovery-scalar','stop-discovery-batch')
$fixtures=@('Copper68k.Tests/Synthetic/M68060StopDiscoveryTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
    'Copper68k.Tests/Synthetic/SyntheticExecution.cs','Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs',
    'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','Copper68k.Tests/Synthetic/SyntheticSystemTests.cs',
    'Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040AccessFrameAuditTests.cs',
    'scripts/reference/m68060-stop.cpp','scripts/test-copper68k-060-stop-discovery.ps1',
    'Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
$referenceFiles=@('build68k.cpp','table68k','gencpu.cpp','readcpu.cpp','missing.cpp','od-win32/unicode.cpp','newcpu.cpp')+
    @(& git -C $source ls-files 'include/*' 'od-win32/sysconfig.h')
$generated=@('build68k.exe','cpudefs.cpp','gencpu.exe','cpuemu_33.cpp','winuae-stop-sr.inc','winuae-stop-op.inc','observer.exe')
$evidence=@('discovery.trx')+@($groups|ForEach-Object{"68060-$_.json";"$_.rows";"$_.reference"})
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
    foreach($row in $Rows){if((Hash (Join-Path $Root $row.file)) -cne $row.sha256){throw "Changed $Kind input: $($row.file)"}}
}
if((& git -C $source rev-parse HEAD) -cne $pin -or (& git -C $source status --porcelain)){throw 'Missing or modified pinned WinUAE checkout'}
if(-not $ValidateReportsOnly){
    if(Test-Path -LiteralPath $output){throw "Use fresh outputs: $output"}
    New-Item -ItemType Directory -Path $output|Out-Null
    $savedCompiler=@{}
    $settings=& $env:COMSPEC /c "`"$VcVars`" >nul && set"
    if($LASTEXITCODE -ne 0){throw 'MSVC setup failed'}
    foreach($setting in $settings){$at=$setting.IndexOf('=');if($at -gt 0){$name=$setting.Substring(0,$at);$savedCompiler[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$setting.Substring($at+1))}}
    Push-Location $output
    try {
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" (Join-Path $source 'build68k.cpp') /Fe:build68k.exe *> build68k.log
        if($LASTEXITCODE -ne 0){throw 'Opcode builder failed'}
        Get-Content -LiteralPath (Join-Path $source 'table68k')|& ./build68k.exe|Set-Content -Encoding utf8 cpudefs.cpp
        if($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath cpudefs.cpp -SimpleMatch 'n_defs68k = 232')){throw 'Missing opcode definitions'}
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" (Join-Path $source 'gencpu.cpp') (Join-Path $source 'readcpu.cpp') (Join-Path $source 'missing.cpp') (Join-Path $source 'od-win32/unicode.cpp') cpudefs.cpp user32.lib /Fe:gencpu.exe *> generator-build.log
        if($LASTEXITCODE -ne 0){throw 'Generator build failed'}
        & ./gencpu.exe *> generator-run.log
        if($LASTEXITCODE -ne 0){throw 'Reference generation failed'}
        $native=[IO.File]::ReadAllText((Join-Path $source 'newcpu.cpp'))
        $helpers=@('void REGPARAM2 MakeSR(void)','static void MakeFromSR_x(int t0trace)','void REGPARAM2 MakeFromSR_STOP(void)',
            'static void m68k_set_stop(int stoptype)','void do_cycles_stop(int c)')
        ($helpers|ForEach-Object{FunctionText $native $_}) -join "`n`n"|Set-Content -Encoding utf8 winuae-stop-sr.inc
        FunctionText ([IO.File]::ReadAllText((Join-Path $output 'cpuemu_33.cpp'))) 'uae_u32 REGPARAM2 op_4e72_33_ff(uae_u32 opcode)'|Set-Content -Encoding utf8 winuae-stop-op.inc
        & cl /nologo /O2 /EHsc /W4 "/I$output" (Join-Path $repo 'scripts/reference/m68060-stop.cpp') /Fe:observer.exe *> observer-build.log
        if($LASTEXITCODE -ne 0){throw 'STOP observer build failed'}
    }finally{Pop-Location;foreach($name in $savedCompiler.Keys){[Environment]::SetEnvironmentVariable($name,$savedCompiler[$name])}}
    & dotnet build (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release *> (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw 'STOP fixture build failed'}
    $saved=@{};$settings=@{COPPER68K_RUN_060_STOP_DISCOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output;COPPER68K_060_STOP_EXPORT=$output}
    try{
        foreach($name in $settings.Keys){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$settings[$name])}
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --no-build --no-restore --filter 'FullyQualifiedName~M68060StopDiscoveryTests' --logger 'trx;LogFileName=discovery.trx' --results-directory $output *> (Join-Path $output 'run.log')
    }finally{foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
    foreach($group in $groups){
        & (Join-Path $output 'observer.exe') (Join-Path $output "$group.rows") (Join-Path $output "$group.reference") 163840 *> (Join-Path $output "$group.reference.log")
        if($LASTEXITCODE -notin @(0,1)){throw "STOP reference execution failed: $group"}
    }
    $identity=@{schema=1;reference='WinUAE-060-STOP-discovery';referenceCommit=$pin;sourceCommit=(& git -C $repo rev-parse HEAD);
        softwareReferenceExecuted=$true;traceBoundaryComposed=$true;fullExceptionOracle=$false;architecturallyQualified=$false;roadmapComplete=$false;
        inputs=@();referenceInputs=@();generated=@();evidence=@()}
    foreach($file in (@(& git -C $repo ls-files 'Copper68k/*')+$fixtures)){$identity.inputs+=@{file=$file;sha256=(Hash (Join-Path $repo $file))}}
    foreach($file in $referenceFiles){$identity.referenceInputs+=@{file=$file;sha256=(Hash (Join-Path $source $file))}}
    foreach($file in $generated){$identity.generated+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    foreach($file in $evidence){$identity.evidence+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    $identity|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identities.json')
}
$identity=Get-Content (Join-Path $output 'identities.json') -Raw|ConvertFrom-Json
if($identity.schema -ne 1 -or $identity.reference -cne 'WinUAE-060-STOP-discovery' -or $identity.referenceCommit -cne $pin -or
    $identity.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $identity.softwareReferenceExecuted -ne $true -or $identity.traceBoundaryComposed -ne $true -or
    $identity.fullExceptionOracle -ne $false -or $identity.architecturallyQualified -ne $false -or $identity.roadmapComplete -ne $false){throw 'Incomplete STOP discovery identity'}
Check-Identities $identity.inputs (@(& git -C $repo ls-files 'Copper68k/*')+$fixtures) $repo 'fixture/CPU'
Check-Identities $identity.referenceInputs $referenceFiles $source 'reference'
Check-Identities $identity.generated $generated $output 'generated'
Check-Identities $identity.evidence $evidence $output 'evidence'
[xml]$trx=Get-Content (Join-Path $output 'discovery.trx') -Raw
$c=$trx.TestRun.ResultSummary.Counters
if($c.total -ne 2 -or $c.executed -ne 2 -or $c.notExecuted -ne 0 -or ([int]$c.passed+[int]$c.failed) -ne 2){throw 'STOP discovery must execute both complete routes'}
foreach($method in @('DefinedStatusImagesScalar','DefinedStatusImagesBatch')){
    if(@($trx.TestRun.Results.UnitTestResult|Where-Object{$_.testName -ceq "Copper68k.Tests.Synthetic.M68060StopDiscoveryTests.$method" -and $_.outcome -cin @('Passed','Failed')}).Count -ne 1){throw "Missing STOP execution: $method"}
}
$keys=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
$fixture=[Collections.Generic.List[string]]::new();$index=0
foreach($matrix in @('images','ccr')){foreach($supervisor in @($false,$true)){foreach($master in @(0,0x1000)){foreach($trace in @(0,0x8000)){
foreach($ccr in $(if($matrix -eq 'images'){@(0,31)}else{0..31})){foreach($s in @(0,0x2000)){foreach($m in @(0,0x1000)){foreach($t in @(0,0x8000)){
foreach($ipl in $(if($matrix -eq 'images'){0..7}else{@(0,7)})){foreach($nextCcr in 0..31){
    $sr=$(if($supervisor){0x2700}else{0x0700}) -bor $master -bor $trace -bor $ccr
    $immediate=$s -bor $m -bor $t -bor ($ipl -shl 8) -bor $nextCcr
    $vector=if(-not $supervisor -or -not $s){8}elseif($trace){9}else{0}
    $boundarySr=if($vector -eq 8){$sr}else{$immediate};$pc=if($vector -eq 8){0x1000}else{0x1004}
    $prefix='68060/STOP/{0}/super={1}/M={2:X4}/T={3:X4}/S={4:X4}/newM={5:X4}/newT={6:X4}/IPL={7}' -f $matrix,$supervisor,$master,$trace,$s,$m,$t,$ipl
    foreach($phase in $(if($vector){@('execute')}else{@('execute','inert')})){
        $key="$prefix/$phase";if(-not $keys.ContainsKey($key)){$keys[$key]=0};$keys[$key]++
    }
    $fixture.Add(('{0:X8} {1:X8} {2:X8} {3:X8} {4:X8} {5:X8}' -f $index++,$sr,$immediate,$vector,$boundarySr,$pc))
}}}}}}}}}}
if($fixture.Count -ne 163840 -or $keys.Count -ne 720){throw "Independent STOP selection differs: $($fixture.Count)/$($keys.Count)"}
$mismatches=0;$summaries=@()
foreach($group in $groups){
    $report=Get-Content (Join-Path $output "68060-$group.json") -Raw|ConvertFrom-Json -AsHashtable
    if($report.schema -ne 1 -or $report.model -cne '68060' -or $report.group -cne $group -or $report.xunitBatches -ne 1 -or $report.logicalCases -ne 184320 -or
        $report.counts.unsupported -ne 0 -or $report.counts.untested -ne 0 -or ($report.counts.passing+$report.counts.mismatching) -ne 184320 -or $report.combinations.Count -ne $keys.Count){throw 'Incomplete STOP report'}
    foreach($key in $keys.Keys){if(-not $report.combinations.Contains($key) -or (@($report.combinations[$key].Values)|Measure-Object -Sum).Sum -ne $keys[$key]){throw "Missing/misweighted STOP combination: $key"}}
    $rows=[IO.File]::ReadAllLines((Join-Path $output "$group.rows"));$reference=[IO.File]::ReadAllLines((Join-Path $output "$group.reference"))
    if($rows.Count -ne $fixture.Count -or $reference.Count -ne $fixture.Count){throw 'Incomplete STOP rows'}
    $nativeMismatch=0
    for($i=0;$i -lt $fixture.Count;$i++){
        $fields=$rows[$i].Split(' ');if($fields.Count -ne 27 -or ($fields[0..5] -join ' ') -cne $fixture[$i]){throw "STOP fixture order/selection differs: $i"}
        $numbers=[uint32[]]::new(9);for($n=0;$n -lt 9;$n++){$numbers[$n]=[uint32]::Parse($fields[$n],[Globalization.NumberStyles]::HexNumber)}
        $initial=@('00007800','00004700','A55A0022','A55A0122','A55A0222','A55A0322','A55A0422','A55A0522','A55A0622','00000002',
            '00004000','00004100','00004200','00004300','00004400','00004500','00004600',$(if($numbers[1] -band 0x2000){'00004700'}else{'00007800'}))
        if(($fields[9..26] -join ' ') -cne ($initial -join ' ')){throw "STOP initial registers/stacks differ: $i"}
        $refFields=$reference[$i].Split(' ');if($refFields.Count -ne 6){throw "Malformed STOP reference row: $i"}
        $ref=[uint32[]]::new(6);for($n=0;$n -lt 6;$n++){$ref[$n]=[uint32]::Parse($refFields[$n],[Globalization.NumberStyles]::HexNumber)}
        $match=$numbers[3] -eq $numbers[6] -and $numbers[4] -eq $numbers[7] -and $numbers[5] -eq $numbers[8]
        if($ref.Count -ne 6 -or $ref[0] -ne $i -or $ref[1] -ne $numbers[3] -or $ref[2] -ne $numbers[4] -or $ref[3] -ne $numbers[5] -or
            $ref[4] -ne [int]($numbers[3] -ne 8) -or $ref[5] -ne [int]$match){throw "STOP reference outcome differs: $i"}
        if(-not $match){$nativeMismatch++}
    }
    if($nativeMismatch -ne $report.counts.mismatching){throw "STOP native/comparator mismatch counts differ: group=$group native=$nativeMismatch report=$($report.counts.mismatching) fixtures=$($fixture.Count)"}
    & (Join-Path $output 'observer.exe') (Join-Path $output "$group.rows") (Join-Path $output "$group.replayed") 163840 *> (Join-Path $output "$group.replay.log")
    if($LASTEXITCODE -notin @(0,1) -or (Hash (Join-Path $output "$group.replayed")) -cne (Hash (Join-Path $output "$group.reference"))){throw 'Frozen STOP reference replay differs'}
    $mismatches+=$report.counts.mismatching;$summaries+=@{group=$group;logicalCases=184320;combinations=$keys.Count;counts=$report.counts}
}
@{schema=1;softwareReferenceExecuted=$true;fullExceptionOracle=$false;traceBoundaryComposed=$true;architecturallyQualified=$false;roadmapComplete=$false;
    groups=$summaries;remaining='Ordinary STOP S-clear architectural outcome, full trace/exception/IRQ delivery and broader qualification/consolidation remain required'}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
if($mismatches){throw "STOP software-reference discovery found $mismatches mismatches; not an architectural CPU-fix claim"}
