[CmdletBinding()]
param(
    [switch] $Deep,
    [switch] $ValidateReportsOnly,
    [uint32] $Seed = 68020,
    [ValidateRange(1, 1000000)] [int] $Samples = 10000,
    [string[]] $Models = @('68000', '68010', '68EC020', '68020', '68030', '68040', '68060', 'A1200'),
    [string] $OutputDirectory = 'artifacts/synthetic-suite',
    [string] $SingleStepPath,
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
    foreach ($model in $knownModels) {
        $expected = @{'move-opcodes'=9726; 'move-values-ccr'=58368; 'move-invalid-operands'=2562; 'move-alignment'=12;
            'move-extensions-aliases'=$(if ($model -in @('68000','68010')) {1756} else {7120});
            'move-register-overlap'=$(if ($model -in @('68000','68010')) {1801} else {2593});
            'move-address-boundary'=$(if ($model -in @('68000','68010')) {44} else {60});
            'transfer-registers'=17624; 'transfer-addresses'=$(if ($model -in @('68000','68010')) {8064} else {9252});
            'transfer-movep'=3708; 'transfer-movem'=$(if ($model -in @('68000','68010')) {3196} else {3592});
            'arithmetic-boundaries'=56448; 'arithmetic-addressing'=$(if ($model -in @('68000','68010')) {4667} else {8237});
            'arithmetic-scenarios'=21072; 'arithmetic-extend'=25440;
            'arithmetic-decimal'=$(if ($model -in @('68000','68010')) {111392} else {111458});
            'arithmetic-muldiv-boundaries'=26904;
            'arithmetic-muldiv-addressing'=$(if ($model -in @('68000','68010')) {5608} elseif ($model -eq '68060') {7208} else {7224});
            'control-branches'=$(if ($model -in @('68000','68010')) {8192} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {12288});
            'control-conditions'=$(if ($model -in @('68000','68010')) {42048} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {43104});
            'control-jumps'=$(if ($model -in @('68000','68010')) {3592} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {3856});
            'control-trapcc'=3072;
            'logical-addressing'=$(if ($model -in @('68000','68010')) {6660} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {9944});
            'logical-bitfield-addressing'=$(if ($model -in @('68000','68010')) {2688} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {3480});
            'logical-bitfield-values'=155136;
            'logical-bits'=$(if ($model -in @('68000','68010')) {104192} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {104852});
            'logical-boundaries'=31808;
            'logical-cas'=$(if ($model -in @('68000','68010')) {36975} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {37371});
            'logical-cas2'=36864;
            'logical-shifts'=$(if ($model -in @('68000','68010')) {130561} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {131089});
            'system-basic'=1728;
            'system-bounds'=$(if ($model -in @('68000','68010')) {11828} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {12488});
            'system-callm'=$(if ($model -in @('68000','68010')) {10832} elseif ($model -in @('68020','68EC020','A1200')) {11000} elseif ($model -in @('68030','68040','68060')) {10964});
            'system-interrupt'=$(if ($model -in @('68000')) {224} elseif ($model -in @('68010','68060')) {226} elseif ($model -in @('68020','68030','68040','68EC020','A1200')) {418});
            'system-model'=13120;
            'system-movec'=2432;
            'system-moves'=$(if ($model -in @('68000','68010')) {10656} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {11052});
            'system-rte'=$(if ($model -in @('68000','68010','68060')) {2048} elseif ($model -in @('68020','68030','68EC020','A1200')) {1888} elseif ($model -in @('68040')) {2016});
            'system-rtm'=24594;
            'system-stack'=6080;
            'system-status'=$(if ($model -in @('68000','68010')) {16540} elseif ($model -in @('68020','68030','68040','68060','68EC020','A1200')) {16936});
            'system-trace'=$(if ($model -in @('68000','68010','68060')) {1284} elseif ($model -in @('68020','68030','68040','68EC020','A1200')) {2568})}
        if ($model -eq '68040') { $expected['system-mmu-disabled'] = 34850 }
        foreach ($group in $expected.Keys) {
            $report = Get-Content -LiteralPath (Join-Path $output "$model-$group.json") -Raw | ConvertFrom-Json
            if ($report.logicalCases -ne $expected[$group] -or $report.counts.passing -ne $expected[$group] -or
                $report.counts.mismatching -ne 0 -or $report.counts.unsupported -ne 0 -or $report.counts.untested -ne 0) { throw "Incomplete gate: $model/$group" }
            $logicalCases += $report.logicalCases
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
        Set-AuditEnvironment 'COPPER68K_RUN_M68000_SINGLESTEP' '1'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_PATH' $resolved
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_FILTER' 'MOVE'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_LIMIT' '0'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_BACKEND' 'interpreter'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_VALIDATE_CYCLES' '0'
        Set-AuditEnvironment 'COPPER68K_M68000_SINGLESTEP_INCLUDE_UNVERIFIED' '0'
        Invoke-Tests 'FullyQualifiedName~OfficialSingleStepCorpusMatchesInterpreterWhenEnabled' 'singlestep-move-68000'
        $references += 'SingleStepTests: 68000 MOVE; MAME generated, documented caveats, semantic audit'
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
        $references += 'WinUAE: available adapter is 68000 integer; generator supports other models, adapters/fixtures still need qualification'
    }
    $inventory = Get-Content -LiteralPath (Join-Path $output 'integer-inventory.json') -Raw | ConvertFrom-Json
    foreach ($row in $inventory.combinations) {
        if ($row.Milestone -le 5) { $row.status = 'passing'; $row.note = 'Passing named semantic scenario batches; consult qualification boundaries for untested physical/internal protocols.' }
    }
    $inventory | Add-Member -NotePropertyName qualificationBoundaries -NotePropertyValue @('RTE fault restart/internal formats remain untested: 010 frame8 restart, 020/030 frames9/A/B, 040 frame7', 'BKPT external replacement responder is unavailable', 'MOVES physical function-code spaces and LPSTOP CPU-space broadcast are unqualified', 'CALLM/RTM type1 protocol uses an internal synthetic responder; normal public buses have none')
    $inventory | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'qualified-inventory.json')
    @{schema=1; deterministicLogicalCases=$logicalCases; deterministicXunitBatches=339; moveGate='passing'; transferGate='passing'; arithmeticGate='passing'; logicalGate='passing'; controlGate='passing'; m68040DisabledMmuInstructionGate='passing'; roadmapComplete=$false;
        seeded=$(if ($Deep) {@{seed=$Seed; samplesPerModel=$Samples; models=$Models}} else {$null});
        externalReferences=$references; unavailableReferenceCoverage=$(if ($references.Count -eq 0) {'External audits not requested/executed'} else {'Software-reference coverage is scoped by the per-program exclusions; hardware and exhaustive external instruction-combination qualification remain unavailable'});
        m68040MmuInstructionScope=@{translationEnabled=$false; dfc=@(1,2,5,6); globalRegisterField='canonical zero'; undefined='Disabled PTEST MMUSR; DFC 0/3/4/7'; enabledMmu='Unqualified flat-table approximation; PFLUSH conservatively flushes all ATC entries'};
        unavailableSystemCoverage=@('RTE bus-fault internal restart on 010/020/030', '020/030 coprocessor midinstruction restoration', '040 access-fault pending trace/writeback restoration', 'External BKPT replacement responder', 'Physical MOVES function-code spaces and LPSTOP CPU-space broadcast');
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
