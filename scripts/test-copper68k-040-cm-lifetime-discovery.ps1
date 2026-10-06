#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ReferenceDirectory='artifacts/reference-winuae-rte-modern',
    [string]$OutputDirectory='artifacts/040-cm-lifetime-discovery',
    [string]$VcVars='C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat',
    [switch]$MmuExceptionEntry,
    [switch]$ValidateReportsOnly
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$source=[IO.Path]::GetFullPath($ReferenceDirectory,$repo)
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$pin='5d22d33632646efc3f747f03e82d28353e52722e'
$fixtures=@('Copper68k.Tests/Synthetic/M68040CmLifetimeDiscoveryTests.cs',
    'Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040AccessFrameAuditTests.cs',
    'Copper68k.Tests/Synthetic/SyntheticMachine.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs',
    'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticMoveTests.cs',
    'scripts/reference/m68040-cm-lifetime.cpp','scripts/test-copper68k-040-cm-lifetime-discovery.ps1',
    'Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
$generated=@('build68k.exe','cpudefs.cpp','gencpu.exe','cpuemu_31.cpp','winuae-cm-sr.inc','winuae-cm-rte-helper.inc','winuae-cm-ops.inc','observer.exe')
if($MmuExceptionEntry){$generated=@('build68k.exe','cpudefs.cpp','gencpu.exe','cpuemu_31.cpp','winuae-cm-native-sr.inc','winuae-cm-exception.inc','winuae-cm-rte-helper.inc','winuae-cm-ops.inc','observer.exe')}
$groups=@('rte-cm-lifetime-discovery-scalar','rte-cm-lifetime-discovery-batch')
$evidence=@('discovery.trx') + @($groups|ForEach-Object {"68040-$_.json";"$_.rows";"$_.reference";"$_.reference.json"})
if($MmuExceptionEntry){$evidence+=@($groups|ForEach-Object {"$_.frames"})}
function Hash($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function FunctionText([string]$text,[string]$signature) {
    # Match definitions, excluding same-signature forward declarations.
    $matches=[regex]::Matches($text,[regex]::Escape($signature)+'\s*\{')
    if($matches.Count -ne 1){throw "Reference function anchor differs: $signature"}
    $at=$matches[0].Index
    $brace=$text.IndexOf('{',$at);$depth=1;$end=$brace+1
    while($depth -gt 0 -and $end -lt $text.Length){if($text[$end] -eq '{'){$depth++};if($text[$end] -eq '}'){$depth--};$end++}
    if($depth -ne 0){throw 'Incomplete reference function'}
    $text.Substring($at,$end-$at)
}
function CheckIdentities($rows,$required,$root,[string]$kind) {
    if(@($rows).Count -ne $required.Count -or @(Compare-Object ($required|Sort-Object) ($rows.file|Sort-Object)).Count){throw "Missing $kind identity"}
    foreach($row in $rows){if((Hash (Join-Path $root $row.file)) -cne $row.sha256){throw "Changed $kind input: $($row.file)"}}
}
$referenceFiles=@('build68k.cpp','table68k','gencpu.cpp','readcpu.cpp','missing.cpp','od-win32/unicode.cpp','cputest.cpp','cpummu.cpp','newcpu.cpp','newcpu_common.cpp') + @(& git -C $source ls-files 'include/*' 'od-win32/sysconfig.h')
if((& git -C $source rev-parse HEAD) -cne $pin -or (& git -C $source status --porcelain)){throw 'Missing or modified pinned WinUAE checkout'}
if(-not $ValidateReportsOnly) {
    if(Test-Path -LiteralPath $output){throw "Use fresh outputs: $output"}
    New-Item -ItemType Directory -Path $output|Out-Null
    $savedCompiler=@{}
    $settings=& $env:COMSPEC /c "`"$VcVars`" >nul && set"
    if($LASTEXITCODE -ne 0){throw 'MSVC setup failed'}
    foreach($setting in $settings){$index=$setting.IndexOf('=');if($index -gt 0){$name=$setting.Substring(0,$index);$savedCompiler[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$setting.Substring($index+1))}}
    Push-Location $output
    try {
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" (Join-Path $source 'build68k.cpp') /Fe:build68k.exe *> build68k.log
        if($LASTEXITCODE -ne 0){throw 'Reference opcode builder failed'}
        Get-Content -LiteralPath (Join-Path $source 'table68k')|& ./build68k.exe|Set-Content -Encoding utf8 cpudefs.cpp
        if($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath cpudefs.cpp -SimpleMatch 'n_defs68k = 232')){throw 'Missing reference opcode definitions'}
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" (Join-Path $source 'gencpu.cpp') (Join-Path $source 'readcpu.cpp') (Join-Path $source 'missing.cpp') (Join-Path $source 'od-win32/unicode.cpp') cpudefs.cpp user32.lib /Fe:gencpu.exe *> generator-build.log
        if($LASTEXITCODE -ne 0){throw 'Reference generator build failed'}
        & ./gencpu.exe *> generator-run.log
        if($LASTEXITCODE -ne 0){throw 'Reference generation failed'}
        if($MmuExceptionEntry) {
            $text=[IO.File]::ReadAllText((Join-Path $source 'newcpu.cpp'))
            $sr=@('void REGPARAM2 MakeSR(void)','static void MakeFromSR_x(int t0trace)','void REGPARAM2 MakeFromSR_T0(void)','void REGPARAM2 MakeFromSR(void)')|ForEach-Object {FunctionText $text $_}
            [IO.File]::WriteAllText((Join-Path $output 'winuae-cm-native-sr.inc'),($sr -join "`n`n"))
            $exceptions=@(FunctionText ([IO.File]::ReadAllText((Join-Path $source 'newcpu_common.cpp'))) 'void Exception_build_stack_frame(uae_u32 oldpc, uae_u32 currpc, uae_u32 ssw, int nr, int format)')
            foreach($signature in @('static bool internalexception(int nr)','static void exception_check_trace (int nr)','static void exception_debug (int nr)','void fill_prefetch (void)','static void Exception_mmu (int nr, uaecptr oldpc)','static void ExceptionX (int nr, uaecptr address, uaecptr oldpc)','void REGPARAM2 Exception(int nr)','static void exception3f(uae_u32 opcode, uaecptr addr, bool writeaccess, bool instructionaccess, bool notinstruction, uaecptr pc, int size, int fc, uae_u16 secondarysr)','static void exception3_read_special(uae_u32 opcode, uaecptr addr, int size, int fc)','void exception3_read_prefetch_68040bug(uae_u32 opcode, uaecptr addr, uae_u16 secondarysr)')){$exceptions+=FunctionText $text $signature}
            [IO.File]::WriteAllText((Join-Path $output 'winuae-cm-exception.inc'),($exceptions -join "`n`n"))
        } else {
            $text=[IO.File]::ReadAllText((Join-Path $source 'cputest.cpp'))
            $sr=@('void REGPARAM2 MakeSR(void)','void MakeFromSR_x(int t0trace)','void REGPARAM2 MakeFromSR_T0(void)','void REGPARAM2 MakeFromSR(void)')|ForEach-Object {FunctionText $text $_}
            [IO.File]::WriteAllText((Join-Path $output 'winuae-cm-sr.inc'),($sr -join "`n`n"))
        }
        [IO.File]::WriteAllText((Join-Path $output 'winuae-cm-rte-helper.inc'),(FunctionText ([IO.File]::ReadAllText((Join-Path $source 'cpummu.cpp'))) 'void m68k_do_rte_mmu040 (uaecptr a7)'))
        $text=[IO.File]::ReadAllText((Join-Path $output 'cpuemu_31.cpp'))
        $ops=@('4e73','30bc','217c','4cfa','4e71')|ForEach-Object {FunctionText $text "uae_u32 REGPARAM2 op_$($_)_31_ff(uae_u32 opcode)"}
        [IO.File]::WriteAllText((Join-Path $output 'winuae-cm-ops.inc'),($ops -join "`n`n"))
        [string[]]$profileFlags=if($MmuExceptionEntry){@('/DWINUAE_EXECUTED_MMU_EXCEPTION')}else{@()}
        & cl /nologo /O2 /EHsc /W4 @profileFlags "/I$output" (Join-Path $repo 'scripts/reference/m68040-cm-lifetime.cpp') /Fe:observer.exe *> observer-build.log
        if($LASTEXITCODE -ne 0){throw 'Reference observer build failed'}
        $compiler=(& cl 2>&1|Out-String).Trim()
    } finally {Pop-Location;foreach($name in $savedCompiler.Keys){[Environment]::SetEnvironmentVariable($name,$savedCompiler[$name])}}
    & dotnet build (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release *> (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw 'CPU discovery build failed'}
    $settings=@{COPPER68K_RUN_040_CM_LIFETIME_DISCOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output;COPPER68K_040_CM_LIFETIME_EXPORT=$output};$saved=@{}
    try {
        foreach($name in $settings.Keys){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$settings[$name])}
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --no-build --no-restore --filter 'FullyQualifiedName~M68040CmLifetimeDiscoveryTests' --logger 'trx;LogFileName=discovery.trx' --results-directory $output *> (Join-Path $output 'run.log')
        $testExit=$LASTEXITCODE
    } finally {foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
    foreach($group in $groups) {
        [string[]]$frameArguments=if($MmuExceptionEntry){@((Join-Path $output "$group.frames"))}else{@()}
        & (Join-Path $output 'observer.exe') (Join-Path $output "$group.rows") (Join-Path $output "$group.reference") 4096 (Join-Path $output "$group.reference.json") @frameArguments *> (Join-Path $output "$group.reference.log")
        if($LASTEXITCODE -notin @(0,1)){throw "Reference execution failed: $group"}
    }
    $identity=@{schema=1;sourceCommit=(& git -C $repo rev-parse HEAD);cpuCommittedTree=(& git -C $repo rev-parse HEAD:Copper68k);
        reference='WinUAE-MMU-040-CM-lifetime-discovery';referenceCommit=$pin;referenceDirectory=$source;
        softwareReferenceExecuted=$true;composedExceptionBoundary=(-not $MmuExceptionEntry);mmuExceptionEntryExecuted=[bool]$MmuExceptionEntry;fullExceptionOracle=$false;architecturallyQualified=$false;hardwareQualified=$false;roadmapComplete=$false;
        compiler=$compiler;compilerFlags=('/O2 /EHsc /DWIN32 /DUNICODE /D_UNICODE; observer /W4 '+($profileFlags -join ' '));
        extraction=$(if($MmuExceptionEntry){'Untouched full generated _31 RTE/MOVE/MOVEM/NOP, cpummu CM helper, newcpu SR/exception3/ExceptionX/Exception_mmu and newcpu_common frame builder; original CPU_TESTER=0'}else{'Untouched full generated _31 RTE/MOVE/MOVEM/NOP, cpummu m68k_do_rte_mmu040 and cputest SR helpers; original CPU_TESTER=0'});
        caveats=$(if($MmuExceptionEntry){'Physical transport; executed scoped MMU exception entry/frame writes and original fill_prefetch compatibility-disabled return; no cache/translation/run-loop/trace/IRQ/timing/hardware oracle; unavailable paths fail'}else{'Physical memory transport; manual odd-PC exception composition; no full Exception_mmu, enabled MMU, trace, IRQ, timing or hardware oracle'});
        inputs=@();referenceInputs=@();generated=@();evidence=@()}
    foreach($file in (@(& git -C $repo ls-files 'Copper68k/*')+$fixtures)){$identity.inputs+=@{file=$file;sha256=(Hash (Join-Path $repo $file))}}
    foreach($file in $referenceFiles){$identity.referenceInputs+=@{file=$file;sha256=(Hash (Join-Path $source $file))}}
    foreach($file in $generated){$identity.generated+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    foreach($file in $evidence){$identity.evidence+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    $identity|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identities.json')
}
$identity=Get-Content (Join-Path $output 'identities.json') -Raw|ConvertFrom-Json
if($identity.schema -ne 1 -or $identity.reference -cne 'WinUAE-MMU-040-CM-lifetime-discovery' -or $identity.referenceCommit -cne $pin -or
    $identity.softwareReferenceExecuted -ne $true -or $identity.fullExceptionOracle -ne $false -or
    $identity.architecturallyQualified -ne $false -or $identity.hardwareQualified -ne $false -or $identity.roadmapComplete -ne $false -or
    $identity.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $identity.cpuCommittedTree -notmatch '^[a-f0-9]{40}$'){throw 'Incomplete CM discovery identity'}
if($identity.mmuExceptionEntryExecuted -ne [bool]$MmuExceptionEntry -or $identity.composedExceptionBoundary -ne (-not $MmuExceptionEntry)){throw 'CM exception profile differs; specify -MmuExceptionEntry for executed-entry outputs'}
CheckIdentities $identity.inputs (@(& git -C $repo ls-files 'Copper68k/*')+$fixtures) $repo 'fixture/CPU'
CheckIdentities $identity.referenceInputs $referenceFiles $source 'reference source'
CheckIdentities $identity.generated $generated $output 'generated reference'
CheckIdentities $identity.evidence $evidence $output 'evidence'
[xml]$trx=Get-Content (Join-Path $output 'discovery.trx') -Raw
$c=$trx.TestRun.ResultSummary.Counters
if([int]$c.total -ne 2 -or [int]$c.executed -ne 2 -or [int]$c.notExecuted -ne 0 -or ([int]$c.passed+[int]$c.failed) -ne 2){throw 'Discovery must execute both complete selections without skips'}
$keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$fixtureRows=[Collections.Generic.List[string]]::new()
$index=0
foreach($start in @('ISP','MSP')){foreach($result in @('user','user-M','ISP','MSP')){foreach($odd in @(0,1)){foreach($cm in @(0,1)){
foreach($alignment in @(0,1)){foreach($vbr in @(0,0x10000)){
    $prefix='68040/RTE/cm-lifetime/{0}/entry={1}/result={2}/odd={3}/align={4}/VBR={5:X8}' -f $(if($cm){'CM'}else{'normal'}),$start,$result,$odd,$alignment,$vbr
    foreach($phase in $(if($odd){@('initial-RTE','repair-SR','repair-PC','handler-RTE','MOVEM','sentinel')}else{@('initial-RTE','MOVEM','sentinel')})){
        if(-not $keys.Add("$prefix/phase=$phase")){throw 'Duplicated independent combination'}
    }
    foreach($ccr in 0..31){$values=@($index,$(if($start -eq 'MSP'){1}else{0}),[array]::IndexOf(@('user','user-M','ISP','MSP'),$result),$odd,$cm,$alignment,$vbr,$ccr);$fixtureRows.Add(($values|ForEach-Object {'{0:X}' -f $_}) -join ' ');$index++}
}}}}}}
if($keys.Count -ne 576 -or $fixtureRows.Count -ne 4096){throw 'Independent CM discovery enumeration differs'}
$replay=Join-Path $output ('reference-recheck-'+[guid]::NewGuid().ToString('N'))
if($ValidateReportsOnly){New-Item -ItemType Directory $replay|Out-Null}
$totals=@{passing=0;mismatching=0;unsupported=0;untested=0};$reports=@();$passedBatches=0;$frameMismatches=0;$frameComparisons=0
foreach($group in $groups) {
    $r=Get-Content (Join-Path $output "68040-$group.json") -Raw|ConvertFrom-Json -AsHashtable
    if($r.schema -ne 1 -or $r.model -cne '68040' -or $r.group -cne $group -or $r.xunitBatches -ne 1 -or $r.logicalCases -ne 18432 -or $r.combinations.Count -ne 576){throw 'Incomplete CM discovery report'}
    $sums=@{passing=0;mismatching=0;unsupported=0;untested=0}
    foreach($key in $r.combinations.Keys) {
        if(-not $keys.Contains($key)){throw "Unexpected combination: $key"}
        $weight=0
        foreach($status in $r.combinations[$key].Keys){if(-not $sums.ContainsKey($status)){throw 'Unknown coverage status'};$n=[long]$r.combinations[$key][$status];if($n -lt 0){throw 'Negative coverage count'};$weight+=$n;$sums[$status]+=$n}
        if($weight -ne 32){throw "Incomplete CCR combination: $key"}
    }
    foreach($status in $sums.Keys){if($sums[$status] -ne $r.counts[$status]){throw 'Coverage totals differ'};$totals[$status]+=$sums[$status]}
    $lines=[IO.File]::ReadAllLines((Join-Path $output "$group.rows"))
    if($lines.Count -ne 4096){throw 'Missing CPU reference rows'}
    for($n=0;$n -lt $lines.Count;$n++) {
        $split=$lines[$n].Split(' | ',[StringSplitOptions]::None)
        if($split.Count -ne 2 -or $split[0] -cne $fixtureRows[$n]){throw 'Missing/reordered reference fixture combination'}
    }
    if($ValidateReportsOnly) {
        [string[]]$frameArguments=if($MmuExceptionEntry){@((Join-Path $output "$group.frames"))}else{@()}
        & (Join-Path $output 'observer.exe') (Join-Path $output "$group.rows") (Join-Path $replay "$group.reference") 4096 (Join-Path $replay "$group.reference.json") @frameArguments *> (Join-Path $replay "$group.log")
        if($LASTEXITCODE -notin @(0,1) -or (Hash (Join-Path $replay "$group.reference")) -cne (Hash (Join-Path $output "$group.reference")) -or
            (Hash (Join-Path $replay "$group.reference.json")) -cne (Hash (Join-Path $output "$group.reference.json"))){throw 'Recorded reference differs from executed replay'}
    }
    $reference=Get-Content (Join-Path $output "$group.reference.json") -Raw|ConvertFrom-Json
    if($reference.rows -ne 4096 -or $reference.phases -ne 18432 -or $reference.fixedControls -ne 4 -or $reference.composedExceptionBoundary -ne (-not $MmuExceptionEntry) -or $reference.mmuExceptionEntryExecuted -ne [bool]$MmuExceptionEntry -or
        $reference.fullExceptionOracle -ne $false -or $reference.hardwareQualified -ne $false -or $reference.passing -ne $sums.passing -or
        $reference.mismatching -ne $sums.mismatching -or $reference.untested -ne $sums.untested -or $sums.unsupported -ne 0){throw 'CPU/reference phase classifications differ'}
    if($MmuExceptionEntry) {
        $frameLines=[IO.File]::ReadAllLines((Join-Path $output "$group.frames"))
        if($frameLines.Count -ne 4096 -or $reference.frameComparisons -ne 2048 -or $reference.PSObject.Properties.Name -cnotcontains 'frameMismatches' -or $reference.frameMismatches -lt 0 -or $reference.frameMismatches -gt 4096){throw 'Missing or invalid exception frame comparison'}
        $frameMismatches+=$reference.frameMismatches
        $frameComparisons+=$reference.frameComparisons
    } elseif($reference.frameMismatches -ne 0 -or $reference.frameComparisons -ne 0){throw 'Unexpected composed-profile frame comparison'}
    if(($sums.mismatching+$sums.unsupported+$sums.untested) -eq 0){$passedBatches++}
    $reports+=@{group=$group;logicalCases=18432;combinations=576;rows=4096;counts=$r.counts}
}
if([int]$c.passed -ne $passedBatches -or [int]$c.failed -ne (2-$passedBatches)){throw 'xUnit and discovery classifications differ'}
$passed=$passedBatches -eq 2 -and $frameMismatches -eq 0
if(-not $ValidateReportsOnly -and (($testExit -eq 0) -ne ($passedBatches -eq 2))){throw 'Test exit and discovery classifications differ'}
@{schema=1;logicalCases=36864;batches=2;reports=$reports;counts=$totals;passed=$passed;softwareReferenceExecuted=$true;
    architecturallyQualified=$false;fullExceptionOracle=$false;hardwareQualified=$false;roadmapComplete=$false;
    referenceCommit=$pin;composedExceptionBoundary=(-not $MmuExceptionEntry);mmuExceptionEntryExecuted=[bool]$MmuExceptionEntry;frameMismatches=$frameMismatches;frameComparisons=$frameComparisons;caveats=$identity.caveats}|
    ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'discovery-summary.json')
if(-not $passed){throw "CM lifetime software discovery remains mismatching or incomplete: $($totals|ConvertTo-Json -Compress); frame mismatches=$frameMismatches"}
