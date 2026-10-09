#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SingleStepPath,
    [string] $MusashiPath,
    [string] $WinUaePath,
    [string] $WinUaeGeneratorSource,
    [string] $WinUaeRunnerSource,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $OutputDirectory,
    [string] $ArtifactsPath
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'copper68k-qualification-scope.ps1')
$qualificationScope = Get-Copper68kQualificationScope $repo
if (-not $SingleStepPath -and -not $MusashiPath -and -not $WinUaePath) { throw 'Select at least one reference audit' }
if (-not $WinUaePath -and ($WinUaeGeneratorSource -or $WinUaeRunnerSource)) { throw 'WinUAE source arguments require a Basic input directory' }
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
$build = if ($ArtifactsPath) { [IO.Path]::GetFullPath($ArtifactsPath, $repo) } else { Join-Path $output 'build' }
$models = @('68000','68010','68EC020','68020','68030','68040','68060','A1200')
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function PinnedSource([string]$Path, [string]$Pin) {
    if (-not $Path) { throw "Source checkout required for pin $Pin" }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $head = & git -C $resolved rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $Pin) { throw "Wrong reference source pin: $resolved" }
    & git -C $resolved diff --quiet HEAD --
    if ($LASTEXITCODE -ne 0) { throw "Modified reference source: $resolved" }
    return $resolved
}
function InputFiles([string]$Root, [string]$Pattern) {
    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $Pattern | Sort-Object FullName)
    if ($files.Count -eq 0) { throw "Empty input selection: $Root" }
    @($files | ForEach-Object { [ordered]@{ path=[IO.Path]::GetRelativePath($Root,$_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Hash $_.FullName) } })
}
$jobs = @()
$identities = @()
if ($SingleStepPath) {
    $pin = '64b253116a3de04aaac4346c43680960dc9b67e5'
    $root = PinnedSource $SingleStepPath $pin
    $files = @(InputFiles $root '*.json.bin')
    if ($files.Count -ne 127) { throw 'Incomplete SingleStepTests input selection; expected 127 files' }
    $identities += [ordered]@{ reference='SingleStepTests'; source=$root; pin=$pin; inputs=$files }
    $jobs += @{ name='singlestep'; test='OfficialSingleStepCorpusMatchesInterpreterWhenEnabled'; settings=@{
        COPPER68K_RUN_M68000_SINGLESTEP='1'; COPPER68K_M68000_SINGLESTEP_PATH=$root
        COPPER68K_M68000_SINGLESTEP_FILTER=''; COPPER68K_M68000_SINGLESTEP_LIMIT='0'
        COPPER68K_M68000_SINGLESTEP_BACKEND='interpreter'; COPPER68K_M68000_SINGLESTEP_VALIDATE_CYCLES='0'
        COPPER68K_M68000_SINGLESTEP_INCLUDE_UNVERIFIED='0'; COPPER68K_M68000_SINGLESTEP_AUDIT='1'
        COPPER68K_M68000_SINGLESTEP_SOURCE_REVISION=$pin
    } }
}
if ($MusashiPath) {
    $pin = '72c1d74800f3087b45a0c1a7342601bbed898881'
    $root = PinnedSource $MusashiPath $pin
    $files = @(InputFiles (Join-Path $root 'test') '*.bin')
    foreach ($spec in @(@('mc68000',60),@('mc68040',18))) {
        if (@($files | Where-Object { $_.path.StartsWith("$($spec[0])/",[StringComparison]::Ordinal) }).Count -ne $spec[1]) { throw "Incomplete Musashi selection: $($spec[0])" }
    }
    if ($files.Count -ne 78) { throw 'Unexpected Musashi input selection' }
    $identities += [ordered]@{ reference='Musashi'; source=$root; pin=$pin; inputs=$files }
    $jobs += @{ name='musashi'; test='MusashiIntegerProgramsAcrossSelectedModelsWhenEnabled'; settings=@{
        COPPER68K_RUN_MUSASHI_MODEL_AUDIT='1'; COPPER68K_MUSASHI_MODEL_AUDIT_PATH=$root
    } }
}
if ($WinUaePath) {
    $generatorPin='025b999239800357e95065fe5b9a15ea5b300fa7'
    $runnerPin='7a83745d6c6159bc74ab0471578ffc8bc244e66e'
    $generator=PinnedSource $WinUaeGeneratorSource $generatorPin
    $runner=PinnedSource $WinUaeRunnerSource $runnerPin
    $root=(Resolve-Path -LiteralPath $WinUaePath).Path
    $manifest=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
    $native=Join-Path $root 'm68k_cpu_tester.dll'
    if ($manifest.Schema -ne 1 -or $manifest.Preset -ne 'Basic' -or $manifest.GeneratorCommit -ne $generatorPin -or $manifest.RunnerCommit -ne $runnerPin -or $manifest.NativeLibrarySha256 -ne (Hash $native)) { throw 'Incompatible Basic WinUAE input manifest/native bridge' }
    $identities += [ordered]@{ reference='WinUAE Basic'; generator=$generator; generatorPin=$generatorPin; runner=$runner; runnerPin=$runnerPin; source=$root; manifestSha256=(Hash (Join-Path $root 'manifest.json')); nativeSha256=(Hash $native); inputs=@(InputFiles $root '*.dat') }
    $jobs += @{ name='winuae-basic'; test='WinUaeIntegerFixturesAcrossSelectedModelsWhenEnabled'; settings=@{
        COPPER68K_RUN_WINUAE_MODEL_AUDIT='1'; COPPER68K_WINUAE_MODEL_PATH=$root; COPPER68K_WINUAE_CPUTEST_LIBRARY=$native
    } }
}
New-Item -ItemType Directory -Path $output | Out-Null
[ordered]@{ schema=1; sourceCommit=(& git -C $repo rev-parse HEAD); commandSha256=(Hash $PSCommandPath); inputs=$identities } |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'reference-inputs.json') -Encoding utf8
$records=@()
foreach ($job in $jobs) {
    $reportDir=Join-Path $output $job.name
    New-Item -ItemType Directory -Path $reportDir | Out-Null
    $job.settings.COPPER68K_SYNTHETIC_REPORT_DIR=$reportDir
    $job.settings.COPPER68K_SYNTHETIC_MODELS=$models -join ','
    $saved=@{}
    try {
        foreach ($key in $job.settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$job.settings[$key]) }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path $build --filter "FullyQualifiedName~$($job.test)" --logger 'trx;LogFileName=audit.trx' --results-directory $reportDir
        $exitCode=$LASTEXITCODE
        [xml]$trx=Get-Content -LiteralPath (Join-Path $reportDir 'audit.trx') -Raw
        $counts=$trx.TestRun.ResultSummary.Counters
        if ($exitCode -ne 0) { throw "$($job.name) failed; retained diagnostics: $reportDir" }
        if ([int]$counts.total -ne 1 -or [int]$counts.executed -ne 1 -or [int]$counts.passed -ne 1 -or $trx.TestRun.Results.UnitTestResult.outcome -ne 'Passed') { throw "$($job.name) audit did not execute exactly one passing test" }
        $reportName=switch ($job.name) { 'singlestep' {'singlestep-model-audit.json'} 'musashi' {'musashi-model-audit.json'} 'winuae-basic' {'winuae-model-audit.json'} }
        $reportPath=Join-Path $reportDir $reportName
        $audit=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($audit.mismatching -ne 0) { throw "$($job.name) report has mismatches" }
        switch ($job.name) {
            'singlestep' {
                $inputs=($identities | Where-Object reference -eq SingleStepTests).inputs
                $expected=@($inputs | Where-Object { [IO.Path]::GetFileName($_.path) -notin @('TAS.json.bin','TRAPV.json.bin') })
                if ($audit.schema -ne 1 -or $audit.sourceRevision -ne '64b253116a3de04aaac4346c43680960dc9b67e5' -or $audit.model -ne '68000' -or $audit.backend -ne 'Interpreter' -or $audit.validateCycles -or $audit.filter -or $audit.executed -ne 312500 -or $audit.passing -ne 312500 -or $audit.rows.Count -ne 125 -or @($audit.excluded | Sort-Object -Unique).Count -ne 2 -or @($audit.excluded | Where-Object { $_ -notin @('TAS.json.bin','TRAPV.json.bin') }).Count -ne 0) { throw 'Incomplete SingleStepTests audit' }
                foreach ($file in $expected) {
                    $row=@($audit.rows | Where-Object File -CEQ ([IO.Path]::GetFileName($file.path)))
                    if ($row.Count -ne 1 -or $row[0].Sha256 -ne $file.sha256 -or $row[0].Passing -ne 2500 -or $row[0].Mismatching -ne 0) { throw "SingleStepTests coverage/identity: $($file.path)" }
                }
            }
            'musashi' {
                $inputs=($identities | Where-Object reference -eq Musashi).inputs
                if ($audit.schema -ne 1 -or ($audit.selectedModels -join ',') -ne ($models -join ',') -or $audit.rows.Count -ne 624 -or $audit.passing -ne 536 -or $audit.excluded -ne 88) { throw 'Incomplete Musashi audit' }
                foreach ($model in $models) {
                    $requiredPassing=switch ($model) { '68000' {55} '68010' {55} '68060' {66} default {72} }
                    if (@($audit.rows | Where-Object { $_.Model -ceq $model -and $_.Status -eq 'passing' }).Count -ne $requiredPassing) { throw "Musashi model coverage: $model" }
                    foreach ($file in $inputs) {
                        $row=@($audit.rows | Where-Object { $_.Model -ceq $model -and $_.Program -ceq $file.path })
                        if ($row.Count -ne 1 -or $row[0].Sha256 -ne $file.sha256 -or $row[0].Status -notin @('passing','excluded') -or ($row[0].Status -eq 'passing' -and $row[0].RetiredInstructions -le 0) -or ($row[0].Status -eq 'excluded' -and -not $row[0].Detail)) { throw "Musashi coverage/identity: $model/$($file.path)" }
                    }
                }
            }
            'winuae-basic' {
                if ($audit.schema -ne 2 -or ($audit.selectedModels -join ',') -ne ($models -join ',') -or $audit.rows.Count -ne 1381 -or $audit.passing -ne 1381 -or $audit.unsupported -ne 0 -or $audit.untested -ne 0 -or $audit.executedCases -le 0 -or @($audit.rows | Where-Object { $_.Status -ne 'passing' -or $_.ExecutedCases -le 0 }).Count -ne 0) { throw 'Incomplete WinUAE Basic audit' }
            }
        }
        foreach ($identity in $identities) {
            $inputRoot=if ($identity.reference -eq 'Musashi') { Join-Path $identity.source 'test' } else { $identity.source }
            foreach ($file in $identity.inputs) {
                $path=Join-Path $inputRoot $file.path
                if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Hash $path) -ne $file.sha256) { throw "Reference input changed during audit: $path" }
            }
        }
        $records += [ordered]@{ reference=$job.name; reportSha256=(Hash $reportPath); trxSha256=(Hash (Join-Path $reportDir 'audit.trx')); cpuSha256=(Hash (Join-Path $build 'bin/Copper68k/release/Copper68k.dll')); testSha256=(Hash (Join-Path $build 'bin/Copper68k.Tests/release/Copper68k.Tests.dll')) }
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
}
[ordered]@{ schema=1; inputsSha256=(Hash (Join-Path $output 'reference-inputs.json')); audits=$records; qualificationScope=$qualificationScope; roadmapComplete=$false } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'reference-verification.json') -Encoding utf8
