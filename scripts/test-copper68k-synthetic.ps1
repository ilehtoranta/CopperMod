[CmdletBinding()]
param(
    [switch] $Deep,
    [switch] $ValidateReportsOnly,
    [uint32] $Seed = 68020,
    [ValidateRange(1, 1000000)] [int] $Samples = 10000,
    [string[]] $Models = @('68000', '68010', '68EC020', '68020', '68030', '68040', '68060', 'A1200'),
    [string] $OutputDirectory = 'artifacts/synthetic-suite',
    [string] $SingleStepPath,
    [string] $SingleStepFilter = '',
    [string] $MusashiPath,
    [string] $WinUaePath,
    [string] $WinUaeLibrary,
    [string] $WinUaeSourceCommit
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$knownModels = @('68000', '68010', '68EC020', '68020', '68030', '68040', '68060', 'A1200')
if ($Models.Count -eq 0 -or @($Models | Where-Object { $_ -cnotin $knownModels }).Count -gt 0) { throw 'Empty or unknown CPU model selection' }
if ($Seed -eq 0) { throw 'Seed must be nonzero' }
if ([bool]$WinUaePath -ne [bool]$WinUaeLibrary) { throw 'WinUAE audit requires both generated fixtures and native library' }
if ($WinUaePath -and $WinUaeSourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'WinUAE audit requires the pinned generator source commit' }
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if ((Test-Path -LiteralPath $output) -and -not $ValidateReportsOnly) {
    # Each invocation owns a new directory so old successes cannot fill missing reports.
    $output = Join-Path $output ([guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$savedEnvironment = @{}
function Set-AuditEnvironment([string]$Name, [string]$Value) {
    if (-not $savedEnvironment.ContainsKey($Name)) { $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name) }
    [Environment]::SetEnvironmentVariable($Name, $Value)
}
function Get-InputIdentity([string]$Name, [string]$Path, [string]$Pattern) {
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $files = @(Get-ChildItem -LiteralPath $resolved -Recurse -File -Filter $Pattern | Sort-Object FullName)
    if ($files.Count -eq 0) { throw "${Name}: no $Pattern inputs selected at $resolved" }
    $revision = (& git -C $resolved rev-parse HEAD 2>$null)
    $identity = @{
        reference = $Name; path = $resolved; sourceRevision = $revision
        inputs = @($files | ForEach-Object { @{ name = [IO.Path]::GetRelativePath($resolved, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    }
    $identity | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output "$Name-inputs.json")
    return $resolved
}
function Invoke-Tests([string]$Filter, [string]$Name) {
    & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --filter $Filter --logger "trx;LogFileName=$Name.trx" --results-directory $output
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)" }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $output "$Name.trx") -Raw
    if ([int]$trx.TestRun.ResultSummary.Counters.executed -eq 0) { throw "$Name executed no tests" }
}
Push-Location $repo
try {
    Set-AuditEnvironment 'COPPER68K_SYNTHETIC_REPORT_DIR' $output
    Set-AuditEnvironment 'COPPER68K_SYNTHETIC_DEEP' $(if ($Deep) { '1' } else { '0' })
    Set-AuditEnvironment 'COPPER68K_SYNTHETIC_SEED' "$Seed"
    Set-AuditEnvironment 'COPPER68K_SYNTHETIC_SAMPLES' "$Samples"
    Set-AuditEnvironment 'COPPER68K_SYNTHETIC_MODELS' ($Models -join ',')
    if (-not $ValidateReportsOnly) { Invoke-Tests $(if ($Deep) { 'Suite=Synthetic|Suite=SyntheticDeep' } else { 'Suite=Synthetic' }) 'deterministic' }
    $logicalCases = 0
    $logicalBatches = 0
    foreach ($model in $knownModels) {
        $expected = @{'move-opcodes'=9726; 'move-values-ccr'=58368; 'move-invalid-operands'=2562; 'move-alignment'=12;
            'move-extensions-aliases'=$(if ($model -in @('68000','68010')) {1756} else {7120});
            'move-register-overlap'=$(if ($model -in @('68000','68010')) {1801} else {2593});
            'move-address-boundary'=$(if ($model -in @('68000','68010')) {44} else {60});
            'transfer-registers'=17624; 'transfer-addresses'=$(if ($model -in @('68000','68010')) {8064} else {9252});
            'transfer-exg-wide'=$(if ($model -in @('68000','68010','68060')) {8448} else {12672});
            'transfer-moveq-unassigned-words'=131072;
            'transfer-movep'=3708; 'transfer-movem'=$(if ($model -in @('68000','68010')) {3196} else {3592});
            'transfer-movem-invalid-operands'=6400;
            'arithmetic-boundaries'=56448; 'arithmetic-addressing'=$(if ($model -in @('68000','68010')) {4667} else {8237});
            'arithmetic-scenarios'=21072; 'arithmetic-extend'=25440;
            'arithmetic-invalid-operands'=$(if ($model -in @('68000','68010')) {6336} else {5952});
            'arithmetic-quick-invalid-operands'=26624;
            'integer-binary-invalid-operands'=121344;
            'logical-unassigned-c180'=4096;
            'logical-memory-shift-invalid-operands'=11264;
            'arithmetic-muldiv-word-invalid-operands'=16384;
            'arithmetic-muldiv-long-invalid-operands'=4096;
            'arithmetic-decimal'=$(if ($model -in @('68000','68010')) {111392} else {111458});
            'arithmetic-packing-memory'=$(if ($model -in @('68000','68010')) {8192} else {12224});
            'arithmetic-muldiv-boundaries'=26904;
            'arithmetic-muldiv-addressing'=$(if ($model -in @('68000','68010')) {5608} elseif ($model -eq '68060') {7208} else {7224});
            'control-branches'=$(if ($model -in @('68000','68010')) {8192} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {12288});
            'control-conditions'=$(if ($model -in @('68000','68010')) {42048} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {43104});
            'control-jumps'=$(if ($model -in @('68000','68010')) {3592} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {3856});
            'control-trapcc'=3072;
            'control-scc-unassigned-operands'=3072;
            'control-invalid-addresses'=23808;
            'logical-addressing'=$(if ($model -in @('68000','68010')) {6660} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {9944});
            'logical-bitfield-addressing'=$(if ($model -in @('68000','68010')) {2688} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {3480});
            'logical-bitfield-values'=155136;
            'logical-bits'=$(if ($model -in @('68000','68010')) {104192} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {104852});
            'logical-boundaries'=31808;
            'logical-invalid-operands'=13248;
            'logical-unary-invalid-operands'=$(if ($model -in @('68000','68010')) {12544} elseif ($model -eq '68060') {10304} else {10432});
            'logical-bitfield-invalid-operands'=13312;
            'logical-cas-invalid-operands'=3456;
            'logical-cas'=$(if ($model -in @('68000','68010')) {36975} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {37371});
            'logical-cas2'=36864;
            'logical-shifts'=$(if ($model -in @('68000','68010')) {130561} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {131089});
            'system-basic'=1728;
            'system-linef-state-encodings'=$(if ($model -in @('68000','68010')) {65536} elseif ($model -eq '68030') {49856} elseif ($model -eq '68040') {60224} elseif ($model -eq '68060') {61248} else {47616});
            'system-linef-unassigned-fpu-ea'=1536;
            'system-linef-unassigned-fpu-types'=8192;
            'system-trap-trace'=$(if ($model -eq '68040') {10368} elseif ($model -eq '68060') {8384} elseif ($model -in @('68000','68010')) {3968} else {5952});
            'system-chk-invalid-operands'=11264;
            'system-unassigned-4140'=32768;
            'system-unassigned-4e'=4480;
            'system-debug-instructions'=$(if ($model -eq '68060') {512} else {256});
            'system-bounds'=$(if ($model -in @('68000','68010')) {11828} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {12488});
            'system-callm'=$(if ($model -in @('68000','68010')) {10832} elseif ($model -in @('68020','68EC020','A1200')) {11000} elseif ($model -in @('68030','68040','68060')) {10964});
            'system-interrupt'=$(if ($model -in @('68000')) {224} elseif ($model -in @('68010','68060')) {226} elseif ($model -in @('68020','68030','68040','68EC020','A1200')) {418});
            'system-model'=3584;
            'system-cache-encodings'=16384;
            'system-lpstop-values'=17024;
            'system-movec'=2432;
            'system-moves'=$(if ($model -in @('68000','68010')) {10656} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {11052});
            'system-moves-invalid-operands'=7296;
            'system-rte'=$(if ($model -in @('68000','68010','68060')) {2048} elseif ($model -in @('68020','68030','68EC020','A1200')) {1888} elseif ($model -in @('68040')) {2080});
             'system-rtm'=24594;
            'system-stack'=6080;
            'system-status'=$(if ($model -in @('68000','68010')) {16540} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {16936});
            'system-status-invalid-operands'=2432;
            'system-trace'=$(if ($model -in @('68000','68010','68060')) {1284} elseif ($model -in @('68020','68030','68040','68EC020','A1200')) {2568})}
        if ($model -eq '68000') { $expected['system-double-fault'] = 640 }
        if ($model -in @('68040','68060')) { $expected['system-translation-control'] = 72640 }
        if ($model -in @('68040','68060')) {
            foreach ($control in 4..7) { $expected["system-transparent-control-$control"] = 77824 }
            foreach ($control in @('806','807')) { $expected["system-root-control-$control"] = 55296 }
        }
        if ($model -eq '68060') {
            $expected['system-lpstop-extensions'] = 131070
            $expected['system-bus-control'] = 247808
            $expected['system-bus-control-exceptions'] = 65536
            $expected['system-bus-control-state'] = 2064
            $expected['system-bus-control-interrupts'] = 4096
            $expected['system-movec-control-encodings'] = 536832
            $expected['system-pcr-defined'] = 102400
            $expected['system-pcr-identification'] = 172032
            $expected['system-pcr-reserved-policy'] = 6144
            $expected['system-pcr-reset'] = 24576
        }
        if ($model -eq '68030') {
            foreach ($route in @('scalar','batch')) {
                $expected["system-pmmu-privilege-opcodes-$route"] = 512
                $expected["system-pmmu-privilege-status-$route"] = 131072
            }
        }
        if ($model -eq '68010') {
            $expected['system-010-movec-pairs'] = 688128
            $expected['system-010-movec-masks'] = 1007616
            $expected['system-010-movec-reads'] = 48128
            $expected['system-010-movec-encodings'] = 527872
            $expected['system-format8-entry'] = 1024
            $expected['system-format8-rte'] = 4096
            $expected['system-format8-double-fault'] = 256
            $expected['system-move-word-restart-source'] = 62208
            $expected['system-move-word-restart-destination'] = 64512
            $expected['system-move-word-restart-edges'] = 640
            $expected['system-move-word-restart-a7'] = 192
            $expected['system-move-word-restart-invalid'] = 224
            $expected['system-move-long-restart-source'] = 165888
            $expected['system-move-long-restart-destination'] = 172032
            $expected['system-move-long-restart-edges'] = 5120
            $expected['system-move-long-restart-a7'] = 512
            $expected['system-move-long-restart-invalid'] = 640
        }
        if ($model -eq '68040') {
            foreach ($route in @('scalar','batch')) {
                $expected["move-write-fault-opcodes-$route"] = 7350
                $expected["move-write-fault-boundaries-$route"] = 9216
                $expected["move-write-fault-indexed-$route"] = 14256
            }
            foreach ($matrix in @('canonical','structure')) { foreach ($route in @('scalar','batch')) {
                $expected["rte-writeback-$matrix-$route"] = 36864
            } }
            $expected['instruction-fault-frame'] = 36864
            $expected['instruction-fault-restart'] = 79872
            $expected['rte-repair-boundaries'] = 143424
            $expected['rte-repair-chained'] = 768960
            $expected['rte-retry-trace-boundaries-scalar'] = 92736
            $expected['rte-retry-trace-boundaries-batch'] = 92736
            $expected['rte-retry-trace-chained-scalar'] = 1271808
            $expected['rte-retry-trace-chained-batch'] = 1271808
            $expected['rte-pending-trace-boundaries-scalar'] = 41472
            $expected['rte-pending-trace-boundaries-batch'] = 41472
            $expected['rte-pending-trace-chained-scalar'] = 870912
            $expected['rte-pending-trace-chained-batch'] = 870912
            $expected['rte-user-master-boundaries-scalar'] = 44736
            $expected['rte-user-master-boundaries-batch'] = 44736
            $expected['rte-user-master-chained-scalar'] = 714240
            $expected['rte-user-master-chained-batch'] = 714240
            $expected['rte-cp-vectors-boundaries-scalar'] = 110592
            $expected['rte-cp-vectors-boundaries-batch'] = 110592
            $expected['rte-cp-vectors-chained-scalar'] = 2322432
            $expected['rte-cp-vectors-chained-batch'] = 2322432
            $expected['rte-software-trace-boundaries-scalar'] = 514560
            $expected['rte-software-trace-boundaries-batch'] = 514560
            $expected['rte-software-trace-chained-scalar'] = 3601920
            $expected['rte-software-trace-chained-batch'] = 3601920
            $expected['rte-validation-batch'] = 129024
            $expected['access-fault-batch-dispatch'] = 10368
            $expected['rte-access-entry-double-fault'] = 98304
            $expected['rte-access-handler-refault'] = 3840
            $expected['access-double-fault-dispatch-accurate'] = 3584
            $expected['access-double-fault-dispatch-v1'] = 1088
            $expected['access-double-fault-dispatch-v2'] = 1088
            $expected['rte-validation-physical-direct'] = 162816
            $expected['rte-validation-physical-chained'] = 61056
            $expected['rte-mixed-fault-canonical-scalar'] = 276480
            $expected['rte-mixed-fault-canonical-batch'] = 276480
            $expected['rte-mixed-fault-structure-scalar'] = 3556800
            $expected['rte-mixed-fault-structure-batch'] = 3556800
            $expected['rte-odd-normal'] = 69120
            $expected['rte-chained-odd-canonical-scalar'] = 82944
            $expected['rte-chained-odd-canonical-batch'] = 82944
            $expected['rte-chained-odd-structure-scalar'] = 539136
            $expected['rte-chained-odd-structure-batch'] = 539136
            $expected['rte-chained-odd-access-canonical-scalar'] = 55296
            $expected['rte-chained-odd-access-canonical-batch'] = 55296
            $expected['rte-chained-odd-access-structure-scalar'] = 359424
            $expected['rte-chained-odd-access-structure-batch'] = 359424
            $expected['rte-odd-pending'] = 165888
            $expected['address-error-fetch-040'] = 2304
            $expected['rte-throwaway-controls'] = 82944
            $expected['rte-throwaway-access'] = 428544
            $expected['rte-mixed-epoch-canonical-scalar'] = 1152000
            $expected['rte-mixed-epoch-canonical-batch'] = 1152000
            $expected['rte-mixed-epoch-structure-scalar'] = 3744000
            $expected['rte-mixed-epoch-structure-batch'] = 3744000
            $expected['rte-access-fpu-unimplemented'] = 13824
            $expected['rte-access-fpu-post'] = 96768
            $expected['rte-access-controls'] = 6912
            $expected['rte-access-normal'] = 9216
            $expected['rte-access-trace'] = 13824
            $expected['rte-access-movem-opcodes'] = 120960
            $expected['rte-access-movem-full-index'] = 114048
            $expected['handler-prefetch-executing-scalar'] = 12288
            $expected['handler-prefetch-executing-batch'] = 12288
            $expected['handler-prefetch-entry-scalar'] = 196608
            $expected['handler-prefetch-entry-batch'] = 196608
            $expected['handler-prefetch-retention-scalar'] = 7680
            $expected['handler-prefetch-retention-batch'] = 7680
            $expected['handler-prefetch-odd-scalar'] = 12288
            $expected['handler-prefetch-odd-batch'] = 12288
            $expected['rte-user-fault-scalar'] = 168192
            $expected['rte-user-fault-batch'] = 168192
            $expected['rte-user-repair-scalar'] = 675328
            $expected['rte-user-repair-batch'] = 675328
            $expected['rte-user-trace-boundaries-scalar'] = 873984
            $expected['rte-user-trace-boundaries-batch'] = 873984
            $expected['rte-user-trace-chained-scalar'] = 3502080
            $expected['rte-user-trace-chained-batch'] = 3502080
            $expected['rte-user-software-trace-boundaries-scalar'] = 1360896
            $expected['rte-user-software-trace-boundaries-batch'] = 1360896
            $expected['rte-user-software-trace-chained-scalar'] = 6350848
            $expected['rte-user-software-trace-chained-batch'] = 6350848
            $expected['system-mmu-disabled'] = 34850
            $expected['system-linef-pc-restore'] = 6464
        }
        foreach ($group in $expected.Keys) {
            $report = Get-Content -LiteralPath (Join-Path $output "$model-$group.json") -Raw | ConvertFrom-Json
            if ($report.schema -ne 1 -or $report.model -cne $model -or $report.group -cne $group -or
                $report.xunitBatches -ne 1 -or $report.logicalCases -ne $expected[$group] -or $report.counts.passing -ne $expected[$group] -or
                $report.counts.mismatching -ne 0 -or $report.counts.unsupported -ne 0 -or $report.counts.untested -ne 0) { throw "Incomplete gate: $model/$group" }
            $logicalCases += $report.logicalCases
            $logicalBatches += $report.xunitBatches
        }
    }
    if ($Deep) {
        foreach ($model in $Models) {
            foreach ($group in @('move', 'arithmetic', 'logical', 'control')) {
                $report = Get-Content -LiteralPath (Join-Path $output "$model-$group-seeded-$Seed.json") -Raw | ConvertFrom-Json
                if ($report.logicalCases -ne $Samples -or $report.counts.passing -ne $Samples -or
                    $report.counts.mismatching -ne 0 -or $report.counts.unsupported -ne 0 -or $report.counts.untested -ne 0) { throw "Incomplete seeded audit: $model/$group" }
            }
        }
    }
    $references = @()
    if ($SingleStepPath) {
        $resolved = Get-InputIdentity 'singlestep' $SingleStepPath '*.json.bin'
        $pin = '64b253116a3de04aaac4346c43680960dc9b67e5'
        $revision = (& git -C $resolved rev-parse HEAD).Trim()
        if ($revision -ne $pin) { throw 'SingleStepTests source revision does not match adapter pin' }
        & git -C $resolved diff --quiet HEAD -- .
        if ($LASTEXITCODE -ne 0) { throw 'SingleStepTests inputs differ from the pinned source' }
        $fixtures = $(if (Test-Path -LiteralPath (Join-Path $resolved 'v1')) { Join-Path $resolved 'v1' } else { $resolved })
        $allFiles = @(Get-ChildItem -LiteralPath $fixtures -File -Filter '*.json.bin' | Sort-Object Name)
        if ($allFiles.Count -ne 127) { throw 'Incomplete pinned SingleStepTests fixtures; expected 127 files' }
        $selectedFiles = @($allFiles | Where-Object { $_.Name -notin @('TAS.json.bin', 'TRAPV.json.bin') -and
            (!$SingleStepFilter -or $_.Name.Contains($SingleStepFilter, [StringComparison]::OrdinalIgnoreCase)) })
        if ($selectedFiles.Count -eq 0) { throw 'Empty SingleStepTests audit selection' }
        Set-AuditEnvironment 'COPPER68K_RUN_M68000_SINGLESTEP' '1'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_PATH' $resolved
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_FILTER' $SingleStepFilter
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_LIMIT' '0'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_BACKEND' 'interpreter'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_VALIDATE_CYCLES' '0'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_INCLUDE_UNVERIFIED' '0'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_AUDIT' '1'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_SOURCE_REVISION' $pin
        Invoke-Tests 'FullyQualifiedName~OfficialSingleStepCorpusMatchesInterpreterWhenEnabled' 'singlestep-68000'
        $audit = Get-Content -LiteralPath (Join-Path $output 'singlestep-model-audit.json') -Raw | ConvertFrom-Json
        if ($audit.mismatching -ne 0 -or $audit.passing -ne 2500 * $selectedFiles.Count -or
            $audit.rows.Count -ne $selectedFiles.Count -or $audit.sourceRevision -ne $pin) { throw 'Incomplete SingleStepTests audit' }
        foreach ($file in $selectedFiles) {
            $row = @($audit.rows | Where-Object File -CEQ $file.Name)
            if ($row.Count -ne 1 -or $row[0].Passing -ne 2500 -or $row[0].Mismatching -ne 0 -or
                $row[0].Sha256 -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "Incomplete SingleStepTests fixture: $($file.Name)" }
        }
        $references += "SingleStepTests: pinned 68000 semantic audit; $($audit.passing) cases in $($audit.rows.Count) files; filter '$SingleStepFilter'; MAME generated, documented boundary/caveats"
    }
    if ($MusashiPath) {
        $resolved = Get-InputIdentity 'musashi' $MusashiPath '*.bin'
        $revision = (& git -C $resolved rev-parse HEAD).Trim()
        if ($revision -ne '72c1d74800f3087b45a0c1a7342601bbed898881') { throw 'Musashi source revision does not match adapter pin' }
        & git -C $resolved diff --quiet HEAD -- test
        if ($LASTEXITCODE -ne 0) { throw 'Musashi reference inputs differ from the pinned source' }
        if (-not (Test-Path -LiteralPath (Join-Path $resolved 'test/mc68000'))) { throw 'Musashi model audit requires the repository root, not one corpus subfolder' }
        Set-AuditEnvironment 'COPPER68K_RUN_MUSASHI_MODEL_AUDIT' '1'
        Set-AuditEnvironment 'COPPER68K_MUSASHI_MODEL_AUDIT_PATH' $resolved
        Invoke-Tests 'FullyQualifiedName~MusashiIntegerProgramsAcrossSelectedModelsWhenEnabled' 'musashi-model-audit'
        $audit = Get-Content -LiteralPath (Join-Path $output 'musashi-model-audit.json') -Raw | ConvertFrom-Json
        if ($audit.mismatching -ne 0 -or $audit.passing -eq 0 -or $audit.rows.Count -ne 78 * $Models.Count) { throw 'Incomplete Musashi program audit' }
        foreach ($model in $Models) {
            if (@($audit.rows | Where-Object { $_.Model -ceq $model -and $_.Status -eq 'passing' }).Count -eq 0) { throw "Missing passing reference selection for $model" }
        }
        $references += "Musashi: pinned independent integer programs across $($Models -join ','); $($audit.passing) passed, $($audit.excluded) explicitly excluded; software assertions, not hardware qualification"
    }
    if ($WinUaePath) {
        $resolved = Get-InputIdentity 'winuae' $WinUaePath '*.gz'
        @{generatorCommit=$WinUaeSourceCommit} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'winuae-generator.json')
        $library = (Resolve-Path -LiteralPath $WinUaeLibrary).Path
        Get-FileHash -LiteralPath $library -Algorithm SHA256 | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'winuae-native-library.json')
        Set-AuditEnvironment 'COPPER68K_RUN_WINUAE_CPUTEST_M68000' '1'
        Set-AuditEnvironment 'COPPER68K_WINUAE_CPUTEST_M68000_PATH' $resolved
        Set-AuditEnvironment 'COPPER68K_WINUAE_CPUTEST_LIBRARY' $library
        Set-AuditEnvironment 'COPPER68K_WINUAE_CPUTEST_OPCODE' 'all'
        Set-AuditEnvironment 'COPPER68K_WINUAE_CPUTEST_AUDIT' '1'
        Set-AuditEnvironment 'COPPER68K_WINUAE_CPUTEST_AUDIT_OUTPUT' (Join-Path $output 'winuae-audit.tsv')
        Invoke-Tests 'FullyQualifiedName~WinUaeM68000CpuTesterPassesInterpreterWhenEnabled' 'winuae-68000'
        $references += 'WinUAE: this command option audits the legacy 68000 integer adapter; separately pinned multi-model qualified presets use test-copper68k-winuae-qualified-exceptions.ps1 and test-copper68k-winuae-long-arithmetic.ps1'
    }
    $inventory = Get-Content -LiteralPath (Join-Path $output 'integer-inventory.json') -Raw | ConvertFrom-Json
    foreach ($row in $inventory.combinations) {
        if ($row.Milestone -le 5) { $row.status = 'passing'; $row.note = 'Passing named semantic scenario batches; consult qualification boundaries for untested physical/internal protocols.' }
    }
    $inventory | Add-Member -NotePropertyName qualificationBoundaries -NotePropertyValue @('RTE fault restart/internal formats remain untested: 010 non-MOVE/foreign frame8 restart, 020/030 frames9/A/B, 040 frame7 CP context transfer and detailed access-fault restoration', 'BKPT external replacement responder is unavailable', 'MOVES physical function-code spaces and LPSTOP CPU-space broadcast are unqualified', 'CALLM/RTM type1 protocol uses an internal synthetic responder; normal public buses have none', '060 HALT debug-port restart, PULSE PST pins and debug pipeline commands are unavailable')
    $inventory | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'qualified-inventory.json')
    @{schema=1; deterministicLogicalCases=$logicalCases; deterministicXunitBatches=$logicalBatches; moveGate='passing'; transferGate='passing'; arithmeticGate='passing'; logicalGate='passing'; controlGate='passing'; m68040DisabledMmuInstructionGate='passing'; m68000AddressErrorDoubleFaultGate='passing'; m68010Format8StructureGate='passing'; m68010WordMoveRestartGate='passing'; roadmapComplete=$false;
        seeded=$(if ($Deep) {@{seed=$Seed; samplesPerModel=$Samples; models=$Models}} else {$null});
        externalReferences=$references; unavailableReferenceCoverage=$(if ($references.Count -eq 0) {'External audits not requested/executed'} else {'Software-reference coverage is scoped by the per-program exclusions; hardware and exhaustive external instruction-combination qualification remain unavailable'});
        m68040MmuInstructionScope=@{translationEnabled=$false; dfc=@(1,2,5,6); globalRegisterField='canonical zero'; undefined='Disabled PTEST MMUSR; DFC 0/3/4/7'; enabledMmu='Unqualified flat-table approximation; PFLUSH conservatively flushes all ATC entries'};
        m68040HandlerPrefetchScope=@{logicalCases=457728; batches=8; routes=@('accurate scalar','accurate batch'); bus='Physical map with no host code reader, including stable-map generation'; qualified='Four-long access-error entry window, every entry byte, odd handler addresses, retained data, flow/host/task/map invalidation and later instruction faults; denied cold boundaries perform no CPU fetch/state change'; remaining='Other exception-entry paths, host-reader/compiled fetch provenance, enabled caches/MMU, ordinary speculative deferral and physical timing'};
        m68040UserTailScope=@{logicalCases=1687040; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); authority='Composition of MC68040UM 2.2.2.1, 8.1, 8.2.1/5, 8.4.2 and 8.4.6.7; software qualification, no executed hardware reference'; userM=@(0,1); initialBanks=@('ISP','MSP'); middleBanks=@('none','user','ISP','MSP'); ccr='all 32 canonical SR reads, 0/31 structural reads'; forms='0/2/3, invalid 4/15, normal/CM/CT/CU/CP49'; qualified='Live intermediate SR, supervisor exception stack selected by M, user-data TM=1, preserved USP frame and committed throwaways, bare return followed by privilege violation, executed seven-store repair with fresh throwaway bridge and following trace/continuation'; remaining='Internal restoration faults, mixed-epoch trace provenance, physical bus-order/timing and hardware observation of composed user-tail fault behavior'};
        m68040UserTailTraceScope=@{logicalCases=8752128; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); userM=@(0,1); initialBanks=@('ISP','MSP'); middleBanks=@('none','user','ISP','MSP'); restoredBanks=@('user','user-M','ISP','MSP'); canonicalIncoming=@(0,0x8000,0x4000); structuralIncoming=@(0x8000); restoredTrace=@(0,0x8000,0x4000); forms='0/2/3, invalid 4/15, normal/CM/CT/CU/CP49-55'; ccr='all 32 canonical, 0/31 every validation-read byte'; qualified='Seven real stores preserve incoming trace while adding S only to the access return; fresh supervisor throwaway restores user SR, original RTE completion traces independently of restored bits, CT/CU/CP priority suppresses extra trace; handler return, following flow/MOVEM and all stack banks checked'; remaining='mixed-epoch trace provenance, internal restoration and hardware/physical qualification'};
        m68040MixedEpochThrowawayScope=@{logicalCases=9792000; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); incoming=@(0,0x8000,0x4000); firstThrowaway=@(0,0x8000,0x4000); secondThrowaway=@(0,0x8000,0x4000); restoredTrace=@(0,0x8000,0x4000); banks=@('user','user-M','ISP','MSP'); canonicalCcr='all 32'; structuralCcr=@(0,31); qualified='Successful one/two-throwaway chains independently vary instruction, first/second throwaway and restored trace; final SR selects exception bank, pending CT/CU/CP49-55 wins, CM survives an RTE completion trace and following MOVEM/trace return/BRA; all stack banks, guarded memory, original pending vectors and preserved FPU registers checked'; remaining='Mixed epochs during validation faults, handler repair/retry, internal restoration and chained odd-PC saved-SR provenance; data restart/context transfer and broader qualification remain required'; outsideRoadmap='FPU arithmetic, enabled-MMU operation and physical timing'};
        m68040UserTailSoftwareTraceScope=@{logicalCases=15423488; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); userM=@(0,1); initialBanks=@('ISP','MSP'); middleBanks=@('none','user','ISP','MSP'); restoredBanks=@('user','user-M','ISP','MSP'); forms=@('CU-linear','CU-flow','CP49','CP50','CP51','CP52','CP53','CP54','CP55'); canonicalIncoming=@(0,0x8000,0x4000); structuralIncoming=@(0x8000); restoredTrace=@(0,0x8000,0x4000); ccr='all 32 canonical, 0/31 every validation-read byte'; qualified='Seven real user-tail repair stores retain trace; pending CU/CP preserves original event; integer handler tests repaired saved T1/T0, adjusts supplied CU completion PC, converts frame, reads vector 9, calls via stack/RTS, writes marker once when eligible, optionally clears saved trace and really returns; following BRA checks resumed hardware tracing without new hardware exception provenance during software service'; remaining='Mixed-epoch provenance, internal restoration, CP context transfer and hardware/physical qualification'; outsideRoadmap='FPU arithmetic and enabled-MMU operation'};
        m68040RteCpVectorsScope=@{logicalCases=4866048; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); vectors=@(50,51,52,53,54,55); restoredBanks=@('user','user-M','ISP','MSP'); incoming=@(0,0x8000,0x4000); restoredTrace=@(0,0x8000,0x4000); qualified='Original CP vector survives validation faults, three repair stores and incoming trace; format-3 saved SR/PC/EA, pending-context consumption, all stack banks, FPU register preservation, handler return and following traced BRA checked'; ccr='all 32 canonical, 0/31 every validation-read byte'; remaining='mixed-epoch provenance, context transfer and internal restoration'};
        m68040RteSoftwareTraceScope=@{logicalCases=8232960; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); forms=@('CU-linear','CU-flow','CP49','CP50','CP51','CP52','CP53','CP54','CP55'); restoredBanks=@('user','user-M','ISP','MSP'); canonicalIncoming=@(0,0x8000,0x4000); structuralIncoming=@(0x8000); restoredTrace=@(0,0x8000,0x4000); ccr='all 32 canonical, 0/31 every validation-read byte'; qualified='Executed integer handler inspects saved T1/T0, advances emulated CU PC, converts format/address, reads vector 9 and directly calls software trace service; trace handler preserves or clears saved trace and really returns; hardware exception counter does not change for software calls'; remaining='Actual FPU emulation and hardware execution remain outside this integer fixture; mixed-epoch provenance, internal restoration and hardware/physical qualification'};
        m68040RteUserMasterScope=@{logicalCases=1517952; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); restoredSr='S=0,M=1'; forms=@('format0','format2','format3','invalid4','invalid15','normal','CM','CT','CU','CP49'); incoming=@(0,0x8000,0x4000); restoredTrace=@(0,0x8000,0x4000); qualified='User execution retains USP; synchronous completion trace, pending conversion and following trace select MSP while preserving M; all three stack banks, saved SR/PC/EA, pending consumption and handler returns checked'; ccr='all 32 canonical, 0/31 each validation-read byte'; remaining='mixed-epoch provenance, internal restoration'};
        m68040RtePendingTraceScope=@{logicalCases=1824768; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); forms='CT/CU/CP49'; incoming=@(0,0x8000,0x4000); restoredTrace=@(0,0x8000,0x4000); faultTailBanks=@('ISP','MSP'); restoredBanks=@('user','ISP','MSP'); qualified='Three original-frame stores preserve access SR; returned incoming trace cannot create an extra RTE trace on pending conversion; saved SR/PC/EA, format/vector, pending consumption, handler return and following traced BRA are checked'; ccr='all 32 canonical, 0/31 each validation-read byte'; remaining='mixed-epoch provenance, internal restoration and physical/hardware qualification'};
        m68040RteRetryTraceScope=@{logicalCases=2729088; batches=4; translationEnabled=$false; instructionCacheEnabled=$false; routes=@('accurate scalar','one-instruction batch'); faultTailBanks=@('ISP','MSP'); incoming=@(0,0x8000,0x4000); restoredBanks=@('user','ISP','MSP'); restoredTrace=@(0,0x8000,0x4000); forms='0/2/3, invalid 4/15 repaired to 0, normal/CM'; qualified='Three original-frame repair stores leave saved access SR untouched; handler return restores incoming trace; successful original RTE triggers T1/T0 trace with restored SR/PC; trace-handler return and following instruction retain MOVEM EA and stack selection'; ccr='all 32 canonical, 0/31 every chained read byte'; remaining='mixed trace values within consumed throwaways, internal restoration and hardware/physical timing'};
        m68040MixedEpochFaultScope=@{logicalCases=7666560; batches=4; routes=@('accurate scalar','one-instruction batch'); qualified='Independent incoming/first/second trace states; all banks and user M aliases; canonical final trace/CCR and structural every validation-read byte; last committed SR, bare handler return, user privilege failure and retained original pending vector'; remaining='Executed mixed-epoch repair/retry, original instruction trace suspension/resumption, internal restoration and hardware capture'};
        m68040ChainedOddScope=@{logicalCases=1244160; batches=4; routes=@('accurate scalar','one-instruction batch'); forms='0/2/3'; qualified='One/two throwaways, all banks/user M aliases, independent trace epochs, CCR, SR provenance, consumed stacks, frame reads and format-2 address-error image'; referenceCommand='scripts/test-copper68k-040-rte-handoff.ps1'; referenceScope='Untouched generated WinUAE 040 RTE and cputest SR helper handoff; documented frame/S-bit composition'; remaining='Format7 continuations/foreign context, full reference exception entry and hardware qualification'};
        m68040AccessFrameScope='Synthetic normal/CT/CM/CU/CP returns with original pending vectors, every legal MOVEM word and full-index structure, one/two throwaways selecting all stacks, successful mixed-epoch chains and validation fault capture, direct odd-PC returns, supervisor-stack physical RTE validation fault entry/handler return, executed repair/retry after committed supervisor throwaways, and manual-derived user-tail fault/privilege/bridge repair; chained odd-PC saved-SR provenance, internal-restoration faults, mixed-epoch repair/retry and original trace suspension/resumption, CP context transfer, other real fault entry and physical timing remain unqualified';
        m68040RteRepairScope=@{logicalCases=912384; batches=2; translationEnabled=$false; faultTailBanks=@('ISP','MSP'); restoredBanks=@('user','ISP','MSP'); directCcr='all 32'; chainedCcr=@(0,31); incomingTrace='All three direct; T1 for each chained read byte'; restoredTrace=@(0,0x8000,0x4000); phases=@('fault entry','three original-frame repair stores','explicit incoming-trace clear','handler return','original RTE retry','optional pending return','following instruction'); forms='0/2/3, invalid 4/15 repaired to 0, normal/CM/CT/CU/CP49'; remaining='Internal restoration, mixed-epoch trace provenance and other real access faults; normal/CM preserved-trace and user-tail repair are qualified separately'};
        m68040InstructionFaultScope=@{logicalCases=116736; batches=2; translationEnabled=$false; instructionCacheEnabled=$false; engine='accurate public factory plus cached self-branch batch'; forms=@('opcode','extension-low','next-opcode','self-branch'); banks=@('user','ISP','MSP'); ccr='all 32'; trace='All defined incoming states for frame/return; restart trace zero'; fields='Format7 SR, executing PC distinct from prefetch FA, RW/SIZE/TM and invalid writebacks'; phases=@('fault entry','handler return','restart','following'); remaining='Data writebacks/restart, speculative prefetch deferral, compiled instruction-fault PC provenance, cache-enabled fetches and physical timing'};
        m68040RtePhysicalValidationScope=@{logicalCases=223872; batches=2; translationEnabled=$false; banks=@('ISP','MSP'); directCcr='all 32'; chainedCcr=@(0,31); trace=@(0,0x8000,0x4000); alignment=@(0,1); vbr=@(0,0x10000); faults='Each byte of SR/PC/format/SSW/continuation-EA reads; 14 direct/one/two-throwaway supervisor paths'; phases=@('fault entry','handler return'); undefined='EA and invalid writeback/push data; SSW X'; remaining='Internal-restoration/double faults, other instruction faults and software writeback handlers; user-tail validation is qualified separately'};
        m68040AccessDoubleFaultScope=@{logicalCases=106656; batches=5; translationEnabled=$false; banks=@('ISP','MSP'); directCcr='all 32'; dispatchCcr=@(0,31); engines=@('accurate','classic','V2'); faults='Every byte of format-7 stack/vector; later handler validation refaults; warmed RTE fallback and compiled B/W/L operand read/write side exits'; recovery='External reset, including restored host entry; interrupt, task/subroutine entry and halted batches remain inactive'; undefined='Partial stack write order, contents and exact SP'; remaining='Internal restoration, other handler-entry paths and general architectural access-fault restart'};
        m68040BatchFaultScope=@{logicalCases=139392; batches=2; translationEnabled=$false; paths=@('cold','cached','model-specific cached','self-branch cached'); limits=@('instruction cap','boundary denial','cycle deadline'); architectural='Supervisor RTE validation, physical operand-read and unbuffered self-branch format-7 frames, and fatal second-fault halt'; policy='Scalar/batch bus order and machine/native cycle equality'; qualification='Dedicated discovery enumerates independent combinations; BatchFault scope proves four delivery paths, count, callback and retry defects'; remaining='General data format-7 restart, other entry paths and internal-restoration faults'};
        m68060LowPowerStopScope='Every unrecognized second opcode word in both privilege modes; fixed encoding, status, CCR and incoming trace cases across all profiles. Opcode-PC exception frames follow MC68060UM 8.2.4/5; physical broadcast, pins and ordinary STOP S-clear software disagreement remain unqualified';
        cacheInstructionEncodingScope='Every F4xx opcode, all CCRs and both privilege states across eight profiles; scope zero is illegal on 040/060, including neither-cache forms, before privilege effects. Empty fixture caches qualify architectural state and exception outcomes, not physical invalidation, writeback, bus faults or timing';
        m68010Format8Scope='58-byte address-error frame, reserved holes, version validation and tail probe, alignment double-fault halt/reset; marked private word/long-MOVE/MOVEA images resume pending word cycles without replay; non-MOVE/foreign restart/input state, external BERR, RMW and physical timing remain unqualified';
        m68000DoubleFaultScope='Address-error entry/handler faults; reset-only recovery; external BERR/reset-vector faults unavailable through the current public bus API';
        unavailableSystemCoverage=@('010 non-MOVE/external bus-fault restart and 020/030 internal restart', '020/030 coprocessor midinstruction restoration', '040 access-fault CP context transfer, detailed frame-validation/fault entry and writeback-handler qualification', 'External BKPT replacement responder', 'Physical MOVES function-code spaces and LPSTOP CPU-space broadcast', '060 HALT debug-port restart, PULSE PST pins and debug pipeline commands');
        timing='Semantic gate; timing policy and physical qualification remain separate'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
    Write-Host "Synthetic milestones 1-5 semantic gates: $logicalCases logical cases; reports at $output"
}
finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Pop-Location
}
# Report-only validation does not execute a native command; clear an inherited native exit code.
$global:LASTEXITCODE = 0
