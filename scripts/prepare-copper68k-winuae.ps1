#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $GeneratorSource,
    [Parameter(Mandatory)] [string] $RunnerSource,
    [Parameter(Mandatory)] [string] $VcVars64,
    [ValidateSet('Basic','TraceTraps','TrapBounds','Breakpoints','LongArithmetic','WordDivision','LowPowerStop','Moves','Cas','Cas2','CacheEncodings','Move16','QualifiedBasic')] [string] $Preset = 'Basic',
    [string] $OutputDirectory = 'artifacts/winuae-model-inputs'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$generatorPin = '025b999239800357e95065fe5b9a15ea5b300fa7'
$runnerPin = '7a83745d6c6159bc74ab0471578ffc8bc244e66e'
foreach ($source in @(@{path=$GeneratorSource; pin=$generatorPin}, @{path=$RunnerSource; pin=$runnerPin})) {
    $revision = & git -C $source.path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $revision -ne $source.pin) { throw "Incorrect source revision at $($source.path)" }
    if (& git -C $source.path diff --name-only HEAD) { throw "Modified tracked source at $($source.path)" }
}
$generator = Join-Path (Resolve-Path -LiteralPath $GeneratorSource).Path 'gencpu'
$vendor = Join-Path (Resolve-Path -LiteralPath $RunnerSource).Path 'crates/cputest-runner/vendor'
$vcvars = (Resolve-Path -LiteralPath $VcVars64).Path
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if (Test-Path -LiteralPath $output) { throw "Use a new output directory: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$savedEnvironment = @{}
function Run-Compiler([string[]] $Arguments, [string] $Log) {
    & cl @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $Log" }
}
function Patch-Once([string] $Text, [string] $Before, [string] $After) {
    if (($Text.Split([string[]]@($Before), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Native patch anchor changed: $Before" }
    return $Text.Replace($Before, $After)
}
# Compose only already reviewed corrections. Each original patch and resulting
# source is recorded; Basic and the individual family presets stay unchanged.
$combinedPatches = @()
function Apply-QualifiedPatch([string] $Text, [string] $Name, [int] $RequiredHunks, [int] $SelectedHunks = 0) {
    $path = Join-Path $PSScriptRoot ('winuae/' + $Name)
    $patch = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $hunks = @($patch -split "(?m)^@@`n" | Select-Object -Skip 1)
    if ($hunks.Count -ne $RequiredHunks) { throw "Incorrect combined patch hunk count: $Name" }
    $count = if ($SelectedHunks -eq 0) { $RequiredHunks } else { $SelectedHunks }
    for ($i = 0; $i -lt $count; $i++) {
        $lines = $hunks[$i].TrimEnd("`n").Split("`n")
        $before = (($lines | Where-Object { $_.StartsWith('-') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
        $after = (($lines | Where-Object { $_.StartsWith('+') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
        $Text = Patch-Once $Text $before $after
    }
    [IO.File]::WriteAllText((Join-Path $output $Name), $patch)
    $script:combinedPatches += @{Name=$Name; Sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant(); AppliedHunks=$count; AvailableHunks=$RequiredHunks}
    return $Text
}
try {
    $settings = & $env:COMSPEC /c "`"$vcvars`" >nul && set"
    if ($LASTEXITCODE -ne 0) { throw 'MSVC environment initialization failed' }
    foreach ($setting in $settings) {
        $index = $setting.IndexOf('=')
        if ($index -le 0) { continue }
        $name = $setting.Substring(0, $index)
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $setting.Substring($index + 1))
    }
    $compiler = (& cl 2>&1 | Out-String).Trim()
    Push-Location $generator
    try {
        Run-Compiler @('/nologo','/O2','/EHsc','/w','/I.','/Iinclude','build68k.cpp','/Fe:build68k.exe') (Join-Path $output 'build68k.log')
        & ./build68k.exe table68k | Set-Content -Encoding utf8 cpudefs.cpp
        if ($LASTEXITCODE -ne 0) { throw 'Opcode table generation failed' }
        $cpuGeneratorSource = 'gencpu.cpp'
        if ($Preset -eq 'QualifiedBasic') {
            $source = [IO.File]::ReadAllText((Join-Path $generator 'gencpu.cpp')).Replace("`r`n", "`n")
            foreach ($item in @(
                @('trap-bounds-pc.patch',2), @('breakpoint-pc.patch',1),
                @('long-arithmetic-unimplemented.patch',3), @('word-division-carry.patch',1),
                @('lpstop-fetch-pc.patch',2), @('cas-unimplemented-pc.patch',1),
                @('cas2-compare-alias.patch',2))) {
                $source = Apply-QualifiedPatch $source $item[0] $item[1]
            }
            $cpuGeneratorSource = Join-Path $output 'gencpu-qualified-basic.cpp'
            [IO.File]::WriteAllText($cpuGeneratorSource, $source)
        }
        if ($Preset -in @('TrapBounds','Breakpoints','LongArithmetic','WordDivision','LowPowerStop','Cas','Cas2')) {
            # TrapBounds: following PC (MC68020UM 6.1.4 / MC68040UM 8.2.3).
            # Breakpoints: opcode PC (illegal exception, UM 6.1.5 / 8.2.4).
            # Patch a copy; Basic keeps the pinned source.
            $sourceName = switch ($Preset) {'TrapBounds' {'gencpu-trap-bounds.cpp'} 'Breakpoints' {'gencpu-breakpoints.cpp'} 'LongArithmetic' {'gencpu-long-arithmetic.cpp'} 'WordDivision' {'gencpu-word-division.cpp'} 'LowPowerStop' {'gencpu-lpstop.cpp'} 'Cas' {'gencpu-cas.cpp'} 'Cas2' {'gencpu-cas2.cpp'}}
            $patchName = switch ($Preset) {'TrapBounds' {'trap-bounds-pc.patch'} 'Breakpoints' {'breakpoint-pc.patch'} 'LongArithmetic' {'long-arithmetic-unimplemented.patch'} 'WordDivision' {'word-division-carry.patch'} 'LowPowerStop' {'lpstop-fetch-pc.patch'} 'Cas' {'cas-unimplemented-pc.patch'} 'Cas2' {'cas2-compare-alias.patch'}}
            $patchPath = Join-Path $PSScriptRoot ('winuae/' + $patchName)
            $patch = [IO.File]::ReadAllText($patchPath).Replace("`r`n", "`n")
            $source = [IO.File]::ReadAllText((Join-Path $generator 'gencpu.cpp')).Replace("`r`n", "`n")
            $hunks = @($patch -split "(?m)^@@`n" | Select-Object -Skip 1)
            $requiredHunks = switch ($Preset) {'Breakpoints' {1} 'TrapBounds' {2} 'LongArithmetic' {3} 'WordDivision' {1} 'LowPowerStop' {2} 'Cas' {1} 'Cas2' {2}}
            if ($hunks.Count -ne $requiredHunks) { throw "Incorrect $Preset patch hunk count" }
            foreach ($hunk in $hunks) {
                $lines = $hunk.TrimEnd("`n").Split("`n")
                $before = (($lines | Where-Object { $_.StartsWith('-') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
                $after = (($lines | Where-Object { $_.StartsWith('+') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
                $source = Patch-Once $source $before $after
            }
            $cpuGeneratorSource = Join-Path $output $sourceName
            [IO.File]::WriteAllText($cpuGeneratorSource, $source)
            [IO.File]::WriteAllText((Join-Path $output $patchName), $patch)
        }
        Run-Compiler @('/nologo','/O2','/EHsc','/w','/I.','/Iinclude',$cpuGeneratorSource,'missing.cpp','readcpu.cpp','cpudefs.cpp','/Fe:gencpu_prog.exe') (Join-Path $output 'gencpu-build.log')
        & ./gencpu_prog.exe . *> (Join-Path $output 'gencpu-run.log')
        if ($LASTEXITCODE -ne 0) { throw 'CPU source generation failed' }
        $testerSource = 'cputest.cpp'
        if ($Preset -eq 'QualifiedBasic') {
            $source = [IO.File]::ReadAllText((Join-Path $generator 'cputest.cpp')).Replace("`r`n", "`n")
            # CAS's insertion anchors the original MOVES block: apply it first.
            foreach ($item in @(@('cas-encodings.patch',1), @('moves-encodings.patch',1),
                @('long-arithmetic-encodings.patch',1), @('cas2-overlap-inputs.patch',1),
                @('move16-encodings.patch',5))) {
                $source = Apply-QualifiedPatch $source $item[0] $item[1]
            }
            # Correct scope-00 exception classification only. The second hunk
            # is the narrow preset's family filter and MUST NOT enter this run.
            $source = Apply-QualifiedPatch $source 'cache-encodings.patch' 2 1
            $testerSource = Join-Path $output 'cputest-qualified-basic.cpp'
            [IO.File]::WriteAllText($testerSource, $source)
        }
        if ($Preset -eq 'CacheEncodings') {
            # Correct only the scope-00 architectural exception; select its
            # ILLEGAL inputs before execution. Upstream excludes actual cache
            # handlers; their semantic outcomes remain synthetic coverage.
            $patchPath = Join-Path $PSScriptRoot 'winuae/cache-encodings.patch'
            $patch = [IO.File]::ReadAllText($patchPath).Replace("`r`n", "`n")
            $source = [IO.File]::ReadAllText((Join-Path $generator 'cputest.cpp')).Replace("`r`n", "`n")
            $hunks = @($patch -split "(?m)^@@`n" | Select-Object -Skip 1)
            if ($hunks.Count -ne 2) { throw 'Incorrect CacheEncodings patch hunk count' }
            foreach ($hunk in $hunks) {
                $lines = $hunk.TrimEnd("`n").Split("`n")
                $before = (($lines | Where-Object { $_.StartsWith('-') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
                $after = (($lines | Where-Object { $_.StartsWith('+') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
                $source = Patch-Once $source $before $after
            }
            $testerSource = Join-Path $output 'cputest-cache-encodings.cpp'
            [IO.File]::WriteAllText($testerSource, $source)
            [IO.File]::WriteAllText((Join-Path $output 'cache-encodings.patch'), $patch)
        }
        if ($Preset -eq 'TraceTraps') {
            # The pinned generator retained pending trace on 040/060. Qualify
            # this separate preset against MC68040UM 8.3 / MC68060UM 8.2.6.
            # Keep the original source and every prior Basic input unchanged.
            $patchPath = Join-Path $PSScriptRoot 'winuae/trace-priority.patch'
            $patchLines = [IO.File]::ReadAllLines($patchPath)
            $before = ($patchLines | Where-Object { $_.StartsWith('-') -and -not $_.StartsWith('---') }).Substring(1)
            $after = ($patchLines | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') }).Substring(1)
            $testerSource = Join-Path $output 'cputest-trace.cpp'
            [IO.File]::WriteAllText($testerSource, (Patch-Once ([IO.File]::ReadAllText((Join-Path $generator 'cputest.cpp'))) $before $after))
            [IO.File]::WriteAllText((Join-Path $output 'trace-priority.patch'), ([IO.File]::ReadAllText($patchPath)).Replace("`r`n", "`n"))
        }
        if ($Preset -in @('LongArithmetic','Moves','Cas','Cas2','Move16')) {
            # M68000PM 4-94/98/136/140: reserved extension fields are zero;
            # Dh == Dl with a 64-bit multiply has undefined results. MOVES
            # 6-24/25/26 similarly fixes reserved fields and excludes undefined
            # same-An postincrement/predecrement store values. CAS 4-67 allows
            # only Du/Dc extension fields. Select
            # legal inputs before reference execution, never in the CPU bridge.
            $patchName = switch ($Preset) {'Moves' {'moves-encodings.patch'} 'Cas' {'cas-encodings.patch'} 'Cas2' {'cas2-overlap-inputs.patch'} 'Move16' {'move16-encodings.patch'} default {'long-arithmetic-encodings.patch'}}
            $patch = [IO.File]::ReadAllText((Join-Path $PSScriptRoot ('winuae/' + $patchName))).Replace("`r`n", "`n")
            $source = [IO.File]::ReadAllText((Join-Path $generator 'cputest.cpp')).Replace("`r`n", "`n")
            $hunks = @($patch -split "(?m)^@@`n" | Select-Object -Skip 1)
            $requiredInputHunks = if ($Preset -eq 'Move16') { 5 } else { 1 }
            if ($hunks.Count -ne $requiredInputHunks) { throw "Incorrect $Preset input patch hunk count" }
            foreach ($hunk in $hunks) {
                $lines = $hunk.TrimEnd("`n").Split("`n")
                $before = (($lines | Where-Object { $_.StartsWith('-') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
                $after = (($lines | Where-Object { $_.StartsWith('+') -or $_.StartsWith(' ') }) | ForEach-Object { $_.Substring(1) }) -join "`n"
                $source = Patch-Once $source $before $after
            }
            $testerSource = Join-Path $output $(switch ($Preset) {'Moves' {'cputest-moves.cpp'} 'Cas' {'cputest-cas.cpp'} 'Cas2' {'cputest-cas2.cpp'} 'Move16' {'cputest-move16.cpp'} default {'cputest-long-arithmetic.cpp'}})
            [IO.File]::WriteAllText($testerSource, $source)
            [IO.File]::WriteAllText((Join-Path $output $patchName), $patch)
        }
        $cpp = @('cpudefs.cpp','cpuemu_90_test.cpp','cpuemu_91_test.cpp','cpuemu_92_test.cpp','cpuemu_93_test.cpp','cpuemu_94_test.cpp','cpuemu_95_test.cpp','cputbl_test.cpp',$testerSource,'cputest_support.cpp','disasm.cpp','fpp.cpp','fpp_softfloat.cpp','ini.cpp','newcpu_common.cpp','readcpu.cpp','softfloat/softfloat.cpp','softfloat/softfloat_decimal.cpp','softfloat/softfloat_fpsp.cpp')
        Run-Compiler (@('/nologo','/O2','/EHsc','/w','/I.','/Iinclude','/Icputest','/I../zlib','/DCPUEMU_90','/DCPUEMU_91','/DCPUEMU_92','/DCPUEMU_93','/DCPUEMU_94','/DCPUEMU_95','/DCPU_TESTER') + $cpp + @('/Fe:cputester.exe')) (Join-Path $output 'cputester-build.log')
        if ($Preset -ne 'Basic') { Copy-Item -LiteralPath 'cputester.exe' -Destination $output }
    } finally { Pop-Location }

    $native = [IO.File]::ReadAllText((Join-Path $vendor 'm68k_cpu_tester.c'))
    $native = Patch-Once $native 'cpu_lvl = settings->cpu_level == 6 ? 5 : settings->cpu_level;' 'ccr_mask = 0xff; check_undefined_sr = settings->check_undefined_sr; cpu_lvl = settings->cpu_level == 6 ? 5 : settings->cpu_level;'
    $native = Patch-Once $native 'context->opcode = settings->opcode;' 'context->stop_on_error = !settings->continue_on_error; context->opcode = settings->opcode;'
    $native = Patch-Once $native 'int lvl2 = cpu_lvl;' 'fclose(f); int lvl2 = cpu_lvl;'
    $start = $native.IndexOf('static uae_u8* validate_exception(', [StringComparison]::Ordinal);
    $end = $native.IndexOf('// regs: registers before execution of test code', $start, [StringComparison]::Ordinal);
    if ($start -lt 0 -or $end -le $start) { throw 'Exception-validator patch anchor changed' }
    $native = $native.Substring(0, $start) + "#include `"integer_validation.h`"`n`n" + $native.Substring($end);
    $native = Patch-Once $native 'vbr_zero = calloc(1, 1024);' 'vbr_zero = calloc(1, 1024); copper_defined_sr = 0xffff; copper_frame_checks = 0; copper_masked_cases = 0; last_exception_len = 0;'
    if (($native.Split([string[]]@('sr_undefined_mask & test_ccrignoremask'), [StringSplitOptions]::None).Count - 1) -ne 2) { throw 'SR comparison patch anchors changed' }
    $native = $native.Replace('sr_undefined_mask & test_ccrignoremask', 'sr_undefined_mask & test_ccrignoremask & copper_defined_sr')
    $native = Patch-Once $native 'if ((last_registers.sr & test_ccrignoremask) != (test_regs.sr & test_ccrignoremask))' 'if ((last_registers.sr & test_ccrignoremask & sr_undefined_mask & copper_defined_sr) != (test_regs.sr & test_ccrignoremask & sr_undefined_mask & copper_defined_sr))'
    $native = Patch-Once $native 'printf("%s\n", outbuffer);' '*outbp = 0; printf("%s\n", outbuffer);'
    $native = $native.Replace("*outbp++ = '\n';", "*outbp++ = '\n'; *outbp = 0;")
    $native += @'

const char* M68KTester_last_output(void) { return outbuffer; }
void M68KTester_destroy(M68KTesterContext* context) {
    free(low_memory); low_memory = NULL;
    free(high_memory); high_memory = NULL;
    free(low_memory_temp); low_memory_temp = NULL;
    free(high_memory_temp); high_memory_temp = NULL;
    free(low_memory_back); low_memory_back = NULL;
    free(high_memory_back); high_memory_back = NULL;
    free(vbr_zero); vbr_zero = NULL;
    free(absallocated); absallocated = NULL; test_memory = NULL;
    free(context);
}
'@
    [IO.File]::WriteAllText((Join-Path $output 'winuae-native.c'), $native)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'winuae/msc_dirent.h') -Destination $output
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'winuae/integer_validation.h') -Destination $output
    Push-Location $output
    try {
        Run-Compiler @('/nologo','/O2','/w','/std:c11','/LD','/I.',"/I$vendor","/I$vendor/capstone-stub",'winuae-native.c','/Fe:m68k_cpu_tester.dll','/link','/EXPORT:M68KTester_init','/EXPORT:M68KTester_run_tests','/EXPORT:M68KTester_last_output','/EXPORT:m68k_tester_addressing_mask','/EXPORT:M68KTester_destroy','/EXPORT:M68KTester_set_defined_sr','/EXPORT:M68KTester_frame_checks','/EXPORT:M68KTester_masked_cases') (Join-Path $output 'native-build.log')
    } finally { Pop-Location }

    $profiles = @()
    $baseIni = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'winuae/cputestgen.ini')).Replace("`r`n", "`n")
    if ($Preset -eq 'QualifiedBasic') { $baseIni = $baseIni.Replace('[test=Basic]', '[test=QualifiedBasic]') }
    if ($Preset -eq 'TraceTraps') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=TraceTraps]').Replace('mode=all', 'mode=TRAP').Replace('feature_sr_mask=0x0000', 'feature_sr_mask=0xa000')
    }
    if ($Preset -eq 'TrapBounds') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=TrapBounds]').Replace('mode=all', 'mode=TRAPcc,CHK2.B,CHK2.W,CHK2.L')
    }
    if ($Preset -eq 'Breakpoints') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=Breakpoints]').Replace('mode=all', 'mode=BKPT')
    }
    if ($Preset -eq 'LongArithmetic') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=LongArithmetic]').Replace('mode=all', 'mode=MULL.L,DIVL.L')
    }
    if ($Preset -eq 'WordDivision') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=WordDivision]').Replace('mode=all', 'mode=DIVU.W,DIVS.W')
    }
    if ($Preset -eq 'LowPowerStop') {
        # Request both privilege states even for malformed encodings: Line-F
        # alone does not request the generator's automatic supervisor round.
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=LowPowerStop]').Replace('mode=all', 'mode=LPSTOP').Replace('feature_sr_mask=0x0000', 'feature_sr_mask=0x2000')
    }
    if ($Preset -eq 'Moves') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=Moves]').Replace('mode=all', 'mode=MOVES.B,MOVES.W,MOVES.L').Replace('feature_sr_mask=0x0000', 'feature_sr_mask=0x2000')
    }
    if ($Preset -eq 'Cas2') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=Cas2]').Replace('mode=all', 'mode=CAS2.W,CAS2.L').Replace('feature_sr_mask=0x0000', 'feature_sr_mask=0x2000')
    }
    if ($Preset -eq 'Cas') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=Cas]').Replace('mode=all', 'mode=CAS.B,CAS.W,CAS.L').Replace('feature_sr_mask=0x0000', 'feature_sr_mask=0x2000')
    }
    if ($Preset -eq 'CacheEncodings') {
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=CacheEncodings]').Replace('mode=all', 'mode=ILLEGAL').Replace('feature_sr_mask=0x0000', 'feature_sr_mask=0x2000')
    }
    if ($Preset -eq 'Move16') {
        # The pinned generator runs an initial round plus test_rounds repeats.
        $baseIni = $baseIni.Replace('[test=Basic]', '[test=Move16]').Replace('mode=all', 'mode=MOVE16').Replace('feature_sr_mask=0x0000', 'feature_sr_mask=0x2000').Replace('test_rounds=1','test_rounds=15')
    }
    foreach ($model in @(
        @{id='68000'; cpu='68000'; width=24}, @{id='68010'; cpu='68010'; width=24},
        @{id='68EC020'; cpu='68020'; width=24}, @{id='68020'; cpu='68020'; width=32},
        @{id='68030'; cpu='68030'; width=32}, @{id='68040'; cpu='68040'; width=32},
        @{id='68060'; cpu='68060'; width=32})) {
        if ($Preset -eq 'TraceTraps' -and $model.id -notin @('68040','68060')) { continue }
        if ($Preset -eq 'TrapBounds' -and $model.id -in @('68000','68010')) { continue }
        if ($Preset -eq 'Breakpoints' -and $model.id -eq '68000') { continue }
        if ($Preset -eq 'LongArithmetic' -and $model.id -in @('68000','68010')) { continue }
        if ($Preset -eq 'LowPowerStop' -and $model.id -ne '68060') { continue }
        if ($Preset -eq 'Moves' -and $model.id -eq '68000') { continue }
        if ($Preset -in @('Cas','Cas2') -and $model.id -in @('68000','68010')) { continue }
        if ($Preset -eq 'Move16' -and $model.id -notin @('68040','68060')) { continue }
        $profileRoot = Join-Path $output $model.id
        New-Item -ItemType Directory -Path $profileRoot | Out-Null
        # Every invocation uses a fresh generator output path; stale data cannot fill a gap.
        $prefix = 'copper68k-' + [guid]::NewGuid().ToString('N') + '/'
        $config = $baseIni.Replace("cpu=68000`n", "cpu=$($model.cpu)`n").Replace('path=data/', "path=$prefix")
        if ($Preset -eq 'TrapBounds' -and $model.id -eq '68060') { $config = $config.Replace('mode=TRAPcc,CHK2.B,CHK2.W,CHK2.L', 'mode=TRAPcc') }
        if ($model.width -eq 24) { $config = $config.Replace('cpu_address_space=68020','cpu_address_space=68030') }
        else { $config = $config.Replace('test_high_memory_start=0x00ff8000','test_high_memory_start=0xffff8000').Replace('test_high_memory_end=0x01000000','test_high_memory_end=0xffffffff') }
        [IO.File]::WriteAllText((Join-Path $profileRoot 'cputestgen.ini'), $config)
        [IO.File]::WriteAllText((Join-Path $generator 'cputestgen.ini'), $config)
        Push-Location $generator
        try {
            $generated = $prefix + $model.cpu + '_' + $Preset
            New-Item -ItemType Directory -Path $generated -Force | Out-Null
            & ./cputester.exe *> (Join-Path $profileRoot 'generation.log')
            if ($LASTEXITCODE -ne 0) { throw "Fixture generation failed: $($model.id)" }
            Copy-Item -LiteralPath $generated -Destination (Join-Path $profileRoot $model.cpu) -Recurse
        } finally { Pop-Location }
        $profiles += @{
            Id=$model.id; CpuDirectory=$model.cpu; CpuLevel=$(switch ($model.cpu) {'68000' {0} '68010' {1} '68020' {2} '68030' {3} '68040' {4} '68060' {5}}); AddressBits=$model.width
            Opcodes=@(Get-ChildItem -LiteralPath (Join-Path $profileRoot $model.cpu) -Directory | Select-Object -ExpandProperty Name | Sort-Object)
            Inputs=@(Get-ChildItem -LiteralPath $profileRoot -Recurse -File -Filter '*.dat' | Sort-Object FullName | ForEach-Object {
                @{Path=[IO.Path]::GetRelativePath($profileRoot,$_.FullName).Replace('\','/'); Bytes=$_.Length; Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
            })
        }
        if ($model.id -ne '68EC020') { $index++ }
    }
    @{
        Schema=1; GeneratorCommit=$generatorPin; RunnerCommit=$runnerPin; Profiles=$profiles
        Preset=$Preset
        QualifiedBasicPatches=$combinedPatches
        QualifiedBasicCpuSourceSha256=$(if ($Preset -eq 'QualifiedBasic') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-qualified-basic.cpp')).Hash.ToLowerInvariant()} else {$null})
        QualifiedBasicInputSourceSha256=$(if ($Preset -eq 'QualifiedBasic') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-qualified-basic.cpp')).Hash.ToLowerInvariant()} else {$null})
        QualifiedBasicScope=$(if ($Preset -eq 'QualifiedBasic') {'All Basic families and profiles; reviewed corrections and legal-input rules composed before execution. Ordinary STOP and RTE disagreements retained; no trace correction or narrow cache family filter. MOVE16 retains the broad one-round selection, not the separate sixteen-round qualification.'} else {$null})
        TracePrioritySourceSha256=$(if ($Preset -eq 'TraceTraps') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-trace.cpp')).Hash.ToLowerInvariant()} else {$null})
        TracePriorityPatchSha256=$(if ($Preset -eq 'TraceTraps') {(Get-FileHash -LiteralPath (Join-Path $output 'trace-priority.patch')).Hash.ToLowerInvariant()} else {$null})
        TrapBoundsSourceSha256=$(if ($Preset -eq 'TrapBounds') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-trap-bounds.cpp')).Hash.ToLowerInvariant()} else {$null})
        TrapBoundsPatchSha256=$(if ($Preset -eq 'TrapBounds') {(Get-FileHash -LiteralPath (Join-Path $output 'trap-bounds-pc.patch')).Hash.ToLowerInvariant()} else {$null})
        BreakpointSourceSha256=$(if ($Preset -eq 'Breakpoints') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-breakpoints.cpp')).Hash.ToLowerInvariant()} else {$null})
        BreakpointPatchSha256=$(if ($Preset -eq 'Breakpoints') {(Get-FileHash -LiteralPath (Join-Path $output 'breakpoint-pc.patch')).Hash.ToLowerInvariant()} else {$null})
        LongArithmeticSourceSha256=$(if ($Preset -eq 'LongArithmetic') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-long-arithmetic.cpp')).Hash.ToLowerInvariant()} else {$null})
        LongArithmeticPatchSha256=$(if ($Preset -eq 'LongArithmetic') {(Get-FileHash -LiteralPath (Join-Path $output 'long-arithmetic-encodings.patch')).Hash.ToLowerInvariant()} else {$null})
        LongArithmeticCpuSourceSha256=$(if ($Preset -eq 'LongArithmetic') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-long-arithmetic.cpp')).Hash.ToLowerInvariant()} else {$null})
        LongArithmeticCpuPatchSha256=$(if ($Preset -eq 'LongArithmetic') {(Get-FileHash -LiteralPath (Join-Path $output 'long-arithmetic-unimplemented.patch')).Hash.ToLowerInvariant()} else {$null})
        WordDivisionSourceSha256=$(if ($Preset -eq 'WordDivision') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-word-division.cpp')).Hash.ToLowerInvariant()} else {$null})
        WordDivisionPatchSha256=$(if ($Preset -eq 'WordDivision') {(Get-FileHash -LiteralPath (Join-Path $output 'word-division-carry.patch')).Hash.ToLowerInvariant()} else {$null})
        LowPowerStopSourceSha256=$(if ($Preset -eq 'LowPowerStop') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-lpstop.cpp')).Hash.ToLowerInvariant()} else {$null})
        LowPowerStopPatchSha256=$(if ($Preset -eq 'LowPowerStop') {(Get-FileHash -LiteralPath (Join-Path $output 'lpstop-fetch-pc.patch')).Hash.ToLowerInvariant()} else {$null})
        MovesSourceSha256=$(if ($Preset -eq 'Moves') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-moves.cpp')).Hash.ToLowerInvariant()} else {$null})
        MovesPatchSha256=$(if ($Preset -eq 'Moves') {(Get-FileHash -LiteralPath (Join-Path $output 'moves-encodings.patch')).Hash.ToLowerInvariant()} else {$null})
        Move16SourceSha256=$(if ($Preset -eq 'Move16') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-move16.cpp')).Hash.ToLowerInvariant()} else {$null})
        Move16PatchSha256=$(if ($Preset -eq 'Move16') {(Get-FileHash -LiteralPath (Join-Path $output 'move16-encodings.patch')).Hash.ToLowerInvariant()} else {$null})
        Cas2InputSourceSha256=$(if ($Preset -eq 'Cas2') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-cas2.cpp')).Hash.ToLowerInvariant()} else {$null})
        CacheEncodingsSourceSha256=$(if ($Preset -eq 'CacheEncodings') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-cache-encodings.cpp')).Hash.ToLowerInvariant()} else {$null})
        CacheEncodingsPatchSha256=$(if ($Preset -eq 'CacheEncodings') {(Get-FileHash -LiteralPath (Join-Path $output 'cache-encodings.patch')).Hash.ToLowerInvariant()} else {$null})
        Cas2InputPatchSha256=$(if ($Preset -eq 'Cas2') {(Get-FileHash -LiteralPath (Join-Path $output 'cas2-overlap-inputs.patch')).Hash.ToLowerInvariant()} else {$null})
        Cas2SourceSha256=$(if ($Preset -eq 'Cas2') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-cas2.cpp')).Hash.ToLowerInvariant()} else {$null})
        Cas2PatchSha256=$(if ($Preset -eq 'Cas2') {(Get-FileHash -LiteralPath (Join-Path $output 'cas2-compare-alias.patch')).Hash.ToLowerInvariant()} else {$null})
        CasSourceSha256=$(if ($Preset -eq 'Cas') {(Get-FileHash -LiteralPath (Join-Path $output 'gencpu-cas.cpp')).Hash.ToLowerInvariant()} else {$null})
        CasPatchSha256=$(if ($Preset -eq 'Cas') {(Get-FileHash -LiteralPath (Join-Path $output 'cas-unimplemented-pc.patch')).Hash.ToLowerInvariant()} else {$null})
        CasInputSourceSha256=$(if ($Preset -eq 'Cas') {(Get-FileHash -LiteralPath (Join-Path $output 'cputest-cas.cpp')).Hash.ToLowerInvariant()} else {$null})
        CasInputPatchSha256=$(if ($Preset -eq 'Cas') {(Get-FileHash -LiteralPath (Join-Path $output 'cas-encodings.patch')).Hash.ToLowerInvariant()} else {$null})
        NativeLibrarySha256=(Get-FileHash -LiteralPath (Join-Path $output 'm68k_cpu_tester.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        Compiler=$compiler; GeneratorExecutableSha256=(Get-FileHash -LiteralPath (Join-Path $generator 'cputester.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
        NativeSourceSha256=(Get-FileHash -LiteralPath (Join-Path $output 'winuae-native.c') -Algorithm SHA256).Hash.ToLowerInvariant()
        IntegerValidationSha256=(Get-FileHash -LiteralPath (Join-Path $output 'integer_validation.h') -Algorithm SHA256).Hash.ToLowerInvariant()
        Seed=$(if ($Preset -eq 'Move16') {'Pinned generator xorshift state initialized to 1 per test set; sixteen explicit valid-address rounds'} else {"Pinned generator xorshift state initialized to 1 per test set; one $Preset round"})
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
    Write-Host "Prepared input manifest and native bridge: $output"
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
