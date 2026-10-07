#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ReferenceDirectory='artifacts/reference-winuae-rte-modern',
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars='C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat',
    [switch]$ValidateReportsOnly
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$source=[IO.Path]::GetFullPath($ReferenceDirectory,$repo)
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$pin='5d22d33632646efc3f747f03e82d28353e52722e'
function Hash($path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function FunctionText($file,$signature){
    $text=[IO.File]::ReadAllText((Join-Path $source $file))
    $matches=[regex]::Matches($text,[regex]::Escape($signature)+'\s*\{')
    if($matches.Count -ne 1){throw "Missing/nonunique function: $signature"}
    $at=$matches[0].Index;$brace=$text.IndexOf('{',$at);$end=$brace+1;$depth=1
    while($depth -gt 0 -and $end -lt $text.Length){if($text[$end] -eq '{'){$depth++};if($text[$end] -eq '}'){$depth--};$end++}
    if($depth -ne 0){throw 'Incomplete function'}
    $text.Substring($at,$end-$at)
}
if((& git -C $source rev-parse HEAD) -cne $pin -or (& git -C $source status --porcelain)){throw 'Missing/modified pinned reference'}
$groups=[ordered]@{
    'sr.inc'=@('static void activate_trace(void)','void REGPARAM2 MakeSR(void)','static void MakeFromSR_x(int t0trace)','void REGPARAM2 MakeFromSR_T0(void)','static bool internalexception(int nr)','static void exception_check_trace (int nr)','static void do_trace(void)')
    'frame.inc'=@('void Exception_build_stack_frame(uae_u32 oldpc, uae_u32 currpc, uae_u32 ssw, int nr, int format)','void Exception_build_stack_frame_common(uae_u32 oldpc, uae_u32 currpc, uae_u32 ssw, int nr, int vector_nr)','static void Exception_mmu030 (int nr, uaecptr oldpc)')
    'fault.inc'=@('void mmu030_page_fault(uaecptr addr, bool read, int flags, uae_u32 fc)','void mmu030_hardware_bus_error(uaecptr addr, uae_u32 v, bool read, bool ins, int size)')
    'access.inc'=@('STATIC_INLINE uae_u32 get_word_mmu030_state (uaecptr addr)','STATIC_INLINE uae_u32 get_long_mmu030_state (uaecptr addr)','STATIC_INLINE uae_u32 get_iword_mmu030_state (int o)','STATIC_INLINE void put_word_mmu030_state (uaecptr addr, uae_u32 v)')
    'rte.inc'=@('void m68k_do_rte_mmu030 (uaecptr a7)')
    'ops.inc'=@('2010','4e73','30bc','7000','4e71'|ForEach-Object {"uae_u32 REGPARAM2 op_$($_)_32_ff(uae_u32 opcode)"})
    'loop.inc'=@('static void m68k_run_mmu030 (void)')
}
$expected=[ordered]@{}
foreach($group in $groups.Keys){
    $parts=@(foreach($signature in $groups[$group]){
        $file=switch($group){'fault.inc'{'cpummu30.cpp'}'rte.inc'{'cpummu30.cpp'}'access.inc'{'include/cpummu030.h'}'ops.inc'{'cpuemu_32.cpp'}default{'newcpu.cpp'}}
        if($signature.StartsWith('void Exception_build_stack_frame')){$file='newcpu_common.cpp'}
        FunctionText $file $signature
    })
    $expected[$group]=($parts -join "`n`n")+"`n"
}
$header=[IO.File]::ReadAllText((Join-Path $source 'include/cpummu030.h'))
$common=[IO.File]::ReadAllText((Join-Path $source 'include/mmu_common.h'))
$defines=@(($header+"`n"+$common) -split '\r?\n'|Where-Object {$_ -match '^#define (MMU030_|MAX_MMU030_ACCESS)'})
$start=$header.IndexOf('#define ACCESS_CHECK_PUT');$end=$header.IndexOf('STATIC_INLINE uae_u32 state_store_mmu030',$start)
if($start -lt 0 -or $end -lt $start){throw 'Access macro anchors differ'}
$expected['constants.inc']=($defines -join "`n")+"`n"+$header.Substring($start,$end-$start)
$special=FunctionText 'newcpu.cpp' 'static int do_specialties (int cycles)'
$start=$special.IndexOf("`t`tif (spcflags & SPCFLAG_DOTRACE)")
$end=$special.IndexOf("`n`t`tif (spcflags & SPCFLAG_UAEINT)",$start)
if($start -lt 0 -or $end -lt $start){throw 'Trace fragment anchors differ'}
$expected['special-trace.inc']=$special.Substring($start,$end-$start)+"`n"
$fixture=Join-Path $PSScriptRoot 'reference/m68030-return-trace.cpp'
$files=@('observer.cpp','observer.exe','build.log','run.log','errors.log')+@($expected.Keys)
$inputs=@('newcpu.cpp','newcpu_common.cpp','cpummu30.cpp','cpuemu_32.cpp','include/cpummu030.h','include/mmu_common.h')
$before=@($inputs|ForEach-Object {@{file=$_;sha256=(Hash (Join-Path $source $_))}})
if(-not $ValidateReportsOnly){
    if(Test-Path -LiteralPath $output){throw 'Use fresh output directory'}
    New-Item -ItemType Directory -Path $output|Out-Null
    foreach($name in $expected.Keys){[IO.File]::WriteAllText((Join-Path $output $name),$expected[$name])}
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $output 'observer.cpp')
    Push-Location $output
    try{
        & $env:COMSPEC /c "`"$VcVars`" >nul && cl /nologo /O2 /EHsc /W4 observer.cpp /Fe:observer.exe > build.log 2>&1"
        if($LASTEXITCODE -ne 0){throw 'Reference observer build failed'}
        & ./observer.exe > run.log 2> errors.log
        if($LASTEXITCODE -ne 0){throw 'Reference observer execution failed'}
    }finally{Pop-Location}
    foreach($row in $before){if((Hash (Join-Path $source $row.file)) -cne $row.sha256){throw 'Reference changed during execution'}}
    @{schema=1;referenceCommit=$pin;producerSha256=(Hash $PSCommandPath);fixtureSha256=(Hash $fixture);
      referenceInputs=$before;
      outputs=@($files|ForEach-Object {@{file=$_;sha256=(Hash (Join-Path $output $_))}});
      physicalTransport=$true;fullRunLoop=$true;traceSpecialtiesFragment=$true;fullSpecialties=$false;
      enabledMmu=$false;hardwareQualified=$false;architecturalChangedTraceGatePassed=$false;roadmapComplete=$false}|
        ConvertTo-Json -Depth 6|Set-Content (Join-Path $output 'identities.json')
}
$identity=Get-Content (Join-Path $output 'identities.json') -Raw|ConvertFrom-Json
if($identity.schema -ne 1 -or $identity.referenceCommit -cne $pin -or $identity.producerSha256 -cne (Hash $PSCommandPath) -or
    $identity.fixtureSha256 -cne (Hash $fixture) -or $identity.hardwareQualified -ne $false -or
    $identity.architecturalChangedTraceGatePassed -ne $false -or $identity.fullSpecialties -ne $false -or
    $identity.physicalTransport -ne $true -or $identity.fullRunLoop -ne $true -or
    $identity.traceSpecialtiesFragment -ne $true -or $identity.enabledMmu -ne $false -or
    $identity.roadmapComplete -ne $false){throw 'Wrong reference scope/producer'}
if(@($identity.outputs).Count -ne $files.Count -or @($identity.referenceInputs).Count -ne $inputs.Count -or
    @(Compare-Object ($files|Sort-Object) ($identity.outputs.file|Sort-Object)).Count -ne 0 -or
    @(Compare-Object ($inputs|Sort-Object) ($identity.referenceInputs.file|Sort-Object)).Count -ne 0){throw 'Incomplete reference identity inventory'}
foreach($row in $identity.referenceInputs){if((Hash (Join-Path $source $row.file)) -cne $row.sha256){throw 'Changed reference input'}}
foreach($row in $identity.outputs){if((Hash (Join-Path $output $row.file)) -cne $row.sha256){throw 'Changed reference output'}}
foreach($name in $expected.Keys){if([IO.File]::ReadAllText((Join-Path $output $name)) -cne $expected[$name]){throw 'Changed extracted function'}}
if((Hash (Join-Path $output 'observer.cpp')) -cne (Hash $fixture)){throw 'Changed observer'}
if((& git -C $source status --porcelain)){throw 'Reference checkout changed'}
if($ValidateReportsOnly){
    $replay=Join-Path $output ('replay-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $replay|Out-Null
    Push-Location $replay
    try{
        & (Join-Path $output 'observer.exe') > run.log 2> errors.log
        if($LASTEXITCODE -ne 0 -or (Hash 'run.log') -cne (Hash (Join-Path $output 'run.log')) -or
            (Get-Item errors.log).Length -ne 0){throw 'Executed reference replay differs'}
    }finally{Pop-Location}
}
$rows=[IO.File]::ReadAllLines((Join-Path $output 'run.log'))
$required=@('fault=1 0 0 2 1 0 0 0 1006 events=123546','fault=1 0 1 2 1 1 1004 2a 6000 events=1235467','fault=1 1 0 2 1 0 0 0 1006 events=123546','fault=1 1 1 2 1 1 1004 2a 6000 events=1235467','fault=0 0 0 1 1 0 0 0 1006 events=46','fault=0 1 1 1 1 1 1002 0 6000 events=47')
if($rows.Count -ne 6 -or ($rows -join "`n") -cne ($required -join "`n") -or (Get-Item (Join-Path $output 'errors.log')).Length -ne 0){throw 'Missing/changed canonical reference outcomes'}
@{schema=1;canonicalPrograms=4;directControls=2;successfulRecoveries=4;traceAfterFollowingInstruction=2;traceSuppressed=2;
  referenceCommit=$pin;identitySha256=(Hash (Join-Path $output 'identities.json'));hardwareQualified=$false;
  architecturalChangedTraceGatePassed=$false;roadmapComplete=$false}|
    ConvertTo-Json|Set-Content (Join-Path $output 'verification.json')
Write-Host 'Verified four reference-generated fault/frame/RTE recoveries; returned T1 traces after following MOVEQ. Architectural gate remains open.'
