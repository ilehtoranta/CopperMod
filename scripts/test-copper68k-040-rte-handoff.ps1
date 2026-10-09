#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ReferenceDirectory='artifacts/reference-winuae-rte-modern',
    [string]$OutputDirectory='artifacts/040-rte-handoff',
    [string]$VcVars='C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat',
    [switch]$AccessFrames,
    [switch]$ValidateReportsOnly
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$source=[IO.Path]::GetFullPath($ReferenceDirectory,$repo)
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$pin='5d22d33632646efc3f747f03e82d28353e52722e'
$fixtures=@('Copper68k.Tests/Synthetic/SyntheticM68040ChainedOddReturnTests.cs',
    'Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040AccessFrameAuditTests.cs',
    'Copper68k.Tests/Synthetic/SyntheticMachine.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs',
    'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticMoveTests.cs',
    'scripts/reference/m68040-rte-handoff.cpp','scripts/test-copper68k-040-rte-handoff.ps1',
    'Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
$generated=@('gencpu-test-mode.cpp','cpudefs.cpp','gencpu.exe','cpuemu_94_test.cpp','winuae-sr.inc','winuae-rte.inc','observer.exe')
$groups=[ordered]@{
    'rte-chained-odd-canonical-scalar'=@{cases=82944;combinations=2592;weight=32}
    'rte-chained-odd-canonical-batch'=@{cases=82944;combinations=2592;weight=32}
    'rte-chained-odd-structure-scalar'=@{cases=539136;combinations=269568;weight=2}
    'rte-chained-odd-structure-batch'=@{cases=539136;combinations=269568;weight=2}
}
if($AccessFrames){$groups=[ordered]@{
    'rte-chained-odd-access-canonical-scalar'=@{cases=55296;combinations=1728;weight=32}
    'rte-chained-odd-access-canonical-batch'=@{cases=55296;combinations=1728;weight=32}
    'rte-chained-odd-access-structure-scalar'=@{cases=359424;combinations=179712;weight=2}
    'rte-chained-odd-access-structure-batch'=@{cases=359424;combinations=179712;weight=2}
}}
function Hash($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function FunctionText([string]$text,[string]$signature) {
    $at=$text.IndexOf($signature,[StringComparison]::Ordinal)
    if($at -lt 0 -or $text.IndexOf($signature,$at+1,[StringComparison]::Ordinal) -ge 0){throw "Reference function anchor differs: $signature"}
    $brace=$text.IndexOf('{',$at);$depth=1;$end=$brace+1
    while($depth -gt 0 -and $end -lt $text.Length){if($text[$end] -eq '{'){$depth++};if($text[$end] -eq '}'){$depth--};$end++}
    if($depth -ne 0){throw 'Incomplete reference function'}
    $text.Substring($at,$end-$at)
}
function CombinationKeys([bool]$structural) {
    $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($start in @('ISP','MSP')){foreach($tail in @('user','user-M','ISP','MSP')){
    foreach($middle in $(if($structural){@('none','user','user-M','ISP','MSP')}else{@('none')})){
    foreach($result in @('user','user-M','ISP','MSP')){foreach($incoming in @(0,0x8000,0x4000)){
    foreach($first in @(0,0x8000,0x4000)){foreach($second in $(if($middle -eq 'none'){@(0)}else{@(0,0x8000,0x4000)})){
    foreach($trace in @(0,0x8000,0x4000)){foreach($target in $(if($structural){@(0x6001,0xff002003u)}else{@(0x6001)})){
    foreach($alignment in $(if($structural){@(0,1)}else{@(0)})){foreach($vbr in $(if($structural){@(0,0x10000)}else{@(0x10000)})){
    foreach($form in $(if($AccessFrames){@('normal','CM')}else{@('format0','format2','format3')})){
        $path=if($middle -eq 'none'){"$start-$tail"}else{"$start-$middle-$tail"}
        $secondName=if($middle -eq 'none'){'none'}else{'{0:X4}' -f $second}
        $key='68040/RTE/chained-odd/{0}/path={1}/result={2}/incoming={3:X4}/first={4:X4}/second={5}/T={6:X4}/target={7:X8}/align={8}/VBR={9:X8}' -f $form,$path,$result,$incoming,$first,$secondName,$trace,$target,$alignment,$vbr
        if(-not $keys.Add($key)){throw 'Duplicated independent combination'}
    }}}}}}}}}}}}
    # Avoid PowerShell enumerating this collection into separate return values.
    return ,$keys
}
if(-not $ValidateReportsOnly) {
    if(Test-Path -LiteralPath $output){throw "Use fresh outputs: $output"}
    if((& git -C $source rev-parse HEAD) -cne $pin -or (& git -C $source status --porcelain)){throw 'Missing or modified pinned WinUAE checkout'}
    New-Item -ItemType Directory -Path $output|Out-Null
    $savedCompilerEnvironment=@{}
    $settings=& $env:COMSPEC /c "`"$VcVars`" >nul && set"
    if($LASTEXITCODE -ne 0){throw 'MSVC setup failed'}
    foreach($setting in $settings){$index=$setting.IndexOf('=');if($index -gt 0){$name=$setting.Substring(0,$index);$savedCompilerEnvironment[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$setting.Substring($index+1))}}
    $text=[IO.File]::ReadAllText((Join-Path $source 'gencpu.cpp'))
    if(($text.Split([string[]]@('#define CPU_TESTER 0'),[StringSplitOptions]::None).Count-1) -ne 1){throw 'Reference generator configuration differs'}
    [IO.File]::WriteAllText((Join-Path $output 'gencpu-test-mode.cpp'),$text.Replace('#define CPU_TESTER 0','#define CPU_TESTER 1'))
    Push-Location $output
    try {
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" (Join-Path $source 'build68k.cpp') /Fe:build68k.exe *> build68k.log
        if($LASTEXITCODE -ne 0){throw 'Reference opcode builder failed'}
        Get-Content -LiteralPath (Join-Path $source 'table68k')|& ./build68k.exe|Set-Content -Encoding utf8 cpudefs.cpp
        if($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath cpudefs.cpp -SimpleMatch 'n_defs68k = 232')){throw 'Missing reference opcode definitions'}
        & cl /nologo /O2 /EHsc /w /DWIN32 /DUNICODE /D_UNICODE "/I$source/include" "/I$source/od-win32" "/I$source" gencpu-test-mode.cpp (Join-Path $source 'readcpu.cpp') (Join-Path $source 'missing.cpp') (Join-Path $source 'od-win32/unicode.cpp') cpudefs.cpp user32.lib /Fe:gencpu.exe *> generator-build.log
        if($LASTEXITCODE -ne 0){throw 'Reference generator build failed'}
        & ./gencpu.exe *> generator-run.log
        if($LASTEXITCODE -ne 0){throw 'Reference generation failed'}
        $rte=FunctionText ([IO.File]::ReadAllText((Join-Path $output 'cpuemu_94_test.cpp'))) 'void REGPARAM2 op_4e73_94_test_ff(uae_u32 opcode)'
        if(-not $rte.Contains('oldsr = newsr;') -or -not $rte.Contains('exception3_read_prefetch_68040bug(opcode, newpc, oldsr);')){throw 'Reference handoff structure differs'}
        [IO.File]::WriteAllText((Join-Path $output 'winuae-rte.inc'),$rte)
        $text=[IO.File]::ReadAllText((Join-Path $source 'cputest.cpp'))
        $sr=@('void MakeFromSR_x(int t0trace)','void REGPARAM2 MakeFromSR_T0(void)','void REGPARAM2 MakeFromSR(void)')|ForEach-Object {FunctionText $text $_}
        [IO.File]::WriteAllText((Join-Path $output 'winuae-sr.inc'),($sr -join "`n`n"))
        & cl /nologo /O2 /EHsc /W4 "/I$output" (Join-Path $repo 'scripts/reference/m68040-rte-handoff.cpp') /Fe:observer.exe *> observer-build.log
        if($LASTEXITCODE -ne 0){throw 'Reference observer build failed'}
        $compiler=(& cl 2>&1|Out-String).Trim()
    } finally {
        Pop-Location
        foreach($name in $savedCompilerEnvironment.Keys){[Environment]::SetEnvironmentVariable($name,$savedCompilerEnvironment[$name])}
    }
    & dotnet build (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release *> (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw 'CPU suite build failed'}
    $saved=@{}
    try {
        foreach($name in @('COPPER68K_SYNTHETIC_REPORT_DIR','COPPER68K_040_RTE_HANDOFF_EXPORT')){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$output)}
        $filter=if($AccessFrames){'FullyQualifiedName~ChainedOddAccessFrame'}else{'FullyQualifiedName~ChainedOddShortFrame'}
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --no-build --no-restore --filter $filter --logger 'trx;LogFileName=handoff.trx' --results-directory $output *> (Join-Path $output 'run.log')
        $testExit=$LASTEXITCODE
    } finally {foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
    foreach($group in $groups.Keys) {
        & (Join-Path $output 'observer.exe') (Join-Path $output "$group.rows") (Join-Path $output "$group.reference") $groups[$group].cases (Join-Path $output "$group.reference.json") *> (Join-Path $output "$group.reference.log")
        if($LASTEXITCODE -ne 0){throw "Reference comparison failed: $group"}
    }
    $identity=@{schema=1;sourceCommit=(& git -C $repo rev-parse HEAD);cpuCommittedTree=(& git -C $repo rev-parse HEAD:Copper68k);
        reference='WinUAE-generated-040-RTE-SR-handoff';referenceCommit=$pin;referenceDirectory=$source;
        softwareReferenceExecuted=$true;fullExceptionOracle=$false;hardwareQualified=$false;roadmapComplete=$false;accessFrames=[bool]$AccessFrames;
        compiler=$compiler;compilerFlags='/O2 /EHsc /DWIN32 /DUNICODE /D_UNICODE; observer /W4';
        extraction='Untouched generated op_4e73_94_test_ff and cputest MakeFromSR_x/T0/MakeFromSR; CPU_TESTER configuration only';
        manual='https://www.nxp.com/docs/en/reference-manual/MC68040UMAD.pdf';manualRule='General operation item 3, traced user return saved S bit';inputs=@();referenceInputs=@();generated=@();evidence=@()}
    foreach($file in (@(& git -C $repo ls-files 'Copper68k/*') + $fixtures)){$identity.inputs+=@{file=$file;sha256=(Hash (Join-Path $repo $file))}}
    foreach($file in @('build68k.cpp','table68k','gencpu.cpp','readcpu.cpp','missing.cpp','od-win32/unicode.cpp','cputest.cpp') + @(& git -C $source ls-files 'include/*' 'od-win32/sysconfig.h')){$identity.referenceInputs+=@{file=$file;sha256=(Hash (Join-Path $source $file))}}
    foreach($file in $generated){$identity.generated+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    foreach($file in @('handoff.trx') + @($groups.Keys|ForEach-Object {"68040-$_.json";"$_.rows";"$_.reference";"$_.reference.json"})){$identity.evidence+=@{file=$file;sha256=(Hash (Join-Path $output $file))}}
    $identity|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identities.json')
    if($testExit -ne 0){throw 'Synthetic comparison failed'}
}
$identity=Get-Content (Join-Path $output 'identities.json') -Raw|ConvertFrom-Json
if($identity.schema -ne 1 -or $identity.reference -cne 'WinUAE-generated-040-RTE-SR-handoff' -or $identity.referenceCommit -cne $pin -or
    $identity.softwareReferenceExecuted -ne $true -or $identity.fullExceptionOracle -ne $false -or $identity.hardwareQualified -ne $false -or
    $identity.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $identity.cpuCommittedTree -notmatch '^[a-f0-9]{40}$'){throw 'Incomplete reference identity'}
if($identity.accessFrames -ne [bool]$AccessFrames){throw 'Reference profile differs; specify -AccessFrames for format7 outputs'}
$required=@(& git -C $repo ls-files 'Copper68k/*') + $fixtures
if(@($identity.inputs).Count -ne $required.Count -or @(Compare-Object ($required|Sort-Object) ($identity.inputs.file|Sort-Object)).Count){throw 'Missing fixture/CPU identity'}
foreach($row in $identity.inputs){if((Hash (Join-Path $repo $row.file)) -cne $row.sha256){throw "Changed fixture/CPU input: $($row.file)"}}
if(@($identity.generated).Count -ne $generated.Count -or @(Compare-Object ($generated|Sort-Object) ($identity.generated.file|Sort-Object)).Count){throw 'Missing generated reference identity'}
foreach($row in $identity.generated){if((Hash (Join-Path $output $row.file)) -cne $row.sha256){throw 'Changed generated reference input'}}
$requiredEvidence=@('handoff.trx') + @($groups.Keys|ForEach-Object {"68040-$_.json";"$_.rows";"$_.reference";"$_.reference.json"})
if(@($identity.evidence).Count -ne $requiredEvidence.Count -or @(Compare-Object ($requiredEvidence|Sort-Object) ($identity.evidence.file|Sort-Object)).Count){throw 'Missing reference evidence identity'}
foreach($row in $identity.evidence){if((Hash (Join-Path $output $row.file)) -cne $row.sha256){throw 'Changed reference evidence'}}
$requiredReference=@('build68k.cpp','table68k','gencpu.cpp','readcpu.cpp','missing.cpp','od-win32/unicode.cpp','cputest.cpp') + @(& git -C $source ls-files 'include/*' 'od-win32/sysconfig.h')
if((& git -C $source rev-parse HEAD) -cne $pin -or (& git -C $source status --porcelain) -or @($identity.referenceInputs).Count -ne $requiredReference.Count -or
    @(Compare-Object ($requiredReference|Sort-Object) ($identity.referenceInputs.file|Sort-Object)).Count){throw 'Missing or changed pinned reference source'}
foreach($row in $identity.referenceInputs){if((Hash (Join-Path $source $row.file)) -cne $row.sha256){throw 'Changed reference source input'}}
[xml]$trx=Get-Content (Join-Path $output 'handoff.trx') -Raw
$c=$trx.TestRun.ResultSummary.Counters
if([int]$c.total -ne 4 -or [int]$c.executed -ne 4 -or [int]$c.passed -ne 4 -or [int]$c.failed -ne 0 -or [int]$c.notExecuted -ne 0){throw 'Incomplete/failed handoff selection'}
$total=0
$canonicalKeys=CombinationKeys $false
$structuralKeys=CombinationKeys $true
if($canonicalKeys.Count -ne $(if($AccessFrames){1728}else{2592}) -or $structuralKeys.Count -ne $(if($AccessFrames){179712}else{269568})){throw 'Independent combination enumeration differs'}
foreach($group in $groups.Keys) {
    $r=Get-Content (Join-Path $output "68040-$group.json") -Raw|ConvertFrom-Json -AsHashtable
    $expected=$groups[$group]
    if($r.schema -ne 1 -or $r.model -cne '68040' -or $r.group -cne $group -or $r.xunitBatches -ne 1 -or $r.logicalCases -ne $expected.cases -or
        $r.combinations.Count -ne $expected.combinations -or $r.counts.passing -ne $expected.cases -or $r.counts.mismatching -ne 0 -or $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0){throw "Incomplete handoff coverage: $group"}
    $keys=$canonicalKeys
    if($group.Contains('structure')){$keys=$structuralKeys}
    foreach($key in $r.combinations.Keys){$statuses=$r.combinations[$key];if(-not $keys.Contains($key) -or $statuses.Count -ne 1 -or $statuses.passing -ne $expected.weight){throw "Incomplete combination: $key"}}
    $reference=Get-Content (Join-Path $output "$group.reference.json") -Raw|ConvertFrom-Json
    if($reference.cases -ne $expected.cases -or $reference.mismatches -ne 0 -or $reference.fixedControls -ne 3 -or $reference.fullExceptionOracle -ne $false -or $reference.hardwareQualified -ne $false){throw 'Incomplete reference comparison'}
    $total+=$r.logicalCases
}
@{schema=1;logicalCases=$total;batches=4;passing=$total;mismatching=0;unsupported=0;untested=0;
    softwareReferenceExecuted=$true;referenceCommit=$pin;fullExceptionOracle=$false;hardwareQualified=$false;roadmapComplete=$false;
    accessFrames=[bool]$AccessFrames;scope=$(if($AccessFrames){'Chained odd-PC normal/CM format7 SR handoff, 60-byte consumption and header reads; SSW/EA validation, no CM/writeback replay and address-error frame checked synthetically'}else{'Chained odd-PC format 0/2/3 SR handoff, stack consumption and read order; architectural format-2/S-bit composition'});
    remaining='Format-7 pending/foreign context, original-trace fault/retry, internal/data restart and broader model reference/consolidation'}|
    ConvertTo-Json -Depth 5|Set-Content (Join-Path $output 'qualification-summary.json')
Write-Output "Qualified $total software handoffs in four batches; full exception/hardware qualification and roadmap remain incomplete."
