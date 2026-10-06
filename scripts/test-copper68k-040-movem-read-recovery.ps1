#requires -Version 7.0
[CmdletBinding()]
param([string] $OutputDirectory = 'artifacts/040-movem-read-recovery', [switch] $ValidateReportsOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
function Hash([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifestPath = Join-Path $output 'inputs.json'
$sources = @(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj') + @('scripts/test-copper68k-040-movem-read-recovery.ps1','Copper68k.Tests/Synthetic/SyntheticM68040MovemReadRecoveryTests.cs') | Sort-Object -Unique)
$reports = @('witness-scalar','witness-batch','discovery-scalar','discovery-batch') | ForEach-Object { "68040-movem-read-recovery-$_.json" }
$evidence = @('audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll') + $reports
if (-not $ValidateReportsOnly) {
    if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $identities = @($sources | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $settings = @{ COPPER68K_RUN_040_MOVEM_READ_RECOVERY='1'; COPPER68K_SYNTHETIC_REPORT_DIR=$output }
    $saved = @{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68040MovemReadRecoveryTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    @{ schema=1; reference='MC68040UM 8.2.6, 8.4.6.2/7'; referenceUrl='https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf'; sourceCommit=(& git -C $repo rev-parse HEAD); testExit=$testExit; sources=$identities; evidence=@($evidence | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $output $_))} }) } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath
}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.sources.Count -ne $sources.Count -or
    @(Compare-Object ($manifest.sources.path | Sort-Object) $sources).Count -ne 0 -or
    $manifest.evidence.Count -ne $evidence.Count -or @(Compare-Object ($manifest.evidence.path | Sort-Object) ($evidence | Sort-Object)).Count -ne 0) { throw 'Missing recovery identities' }
foreach ($source in $manifest.sources) { if ((Hash (Join-Path $repo $source.path)) -ne $source.sha256) { throw "Changed recovery source: $($source.path)" } }
foreach ($item in $manifest.evidence) { if ((Hash (Join-Path $output $item.path)) -ne $item.sha256) { throw "Changed recovery evidence: $($item.path)" } }
[xml]$trx=Get-Content (Join-Path $output 'audit.trx') -Raw
$tests=@($trx.TestRun.Results.UnitTestResult)
$prefix='Copper68k.Tests.Synthetic.SyntheticM68040MovemReadRecoveryTests.'
$names=@('ScalarRecoveryMatrix','BatchRecoveryMatrix','FixedLegalSourceRecoveryWitnesses(batch: False)','FixedLegalSourceRecoveryWitnesses(batch: True)','FixedPcSelfReferenceEncodings') | ForEach-Object { $prefix+$_ }
if ($tests.Count -ne 5 -or @(Compare-Object ($tests.testName | Sort-Object) ($names | Sort-Object)).Count -ne 0 -or @($tests.testId | Sort-Object -Unique).Count -ne 5) { throw 'Incomplete recovery execution roster' }
# Expand the architectural inventory independently from the C# generator.
function SourceId([int] $Mode, [int] $Register, [string] $Index='brief-default') { "mode=$Mode/reg=$Register/index=$Index" }
$forms=@(foreach ($mode in @(2,3,5,6)) { foreach ($reg in 0..7) { SourceId $mode $reg } }; foreach ($reg in 0..3) { SourceId 7 $reg })
$full=@(foreach ($bs in @('False','True')) { foreach ($is in @('False','True')) { foreach ($bd in 1..3) { foreach ($iis in @(0,1,2,3,5,6,7)) {
    if ($is -eq 'True' -and $iis -ge 5) { continue }
    "full/bs=$bs/is=$is/bd=$bd/iis=$iis"
} } } })
if ($forms.Count -ne 36 -or $full.Count -ne 66) { throw 'Wrong independent source inventory' }
$canonical=@(SourceId 2 0; SourceId 3 0; SourceId 3 7; SourceId 5 0; SourceId 6 0; SourceId 7 0; SourceId 7 1; SourceId 7 2; SourceId 7 3
    SourceId 6 0 'full/bs=False/is=False/bd=2/iis=2/ix=D1/L/scale=1'
    SourceId 7 3 'full/bs=False/is=False/bd=3/iis=7/ix=D1/L/scale=1')
function AddKeys($Map, [string] $Cohort, [string[]] $Forms, [string[]] $Banks, [string[]] $Traces, [bool] $Witness) {
    foreach ($form in $Forms) { foreach ($width in @(2,4)) { foreach ($bank in $Banks) { foreach ($trace in $Traces) {
        $transfers=if ($Witness) { @(3) } else { @(0,1,2,3) }
        $bytes=if ($Witness) { @($width-1) } else { @(0..($width-1)) }
        foreach ($transfer in $transfers) { foreach ($byte in $bytes) {
            $key="68040/MOVEM/read-recovery/$Cohort/size=$width/$form/bank=$bank/T=$trace/transfer=$transfer/byte=$byte"
            if ($Map.ContainsKey($key)) { throw "Duplicate independent combination: $key" }
            $Map[$key]=if ($Cohort -eq 'status') { 32 } else { 1 }
        } }
    } } } }
}
$witness=@{}; $required=@{}
AddKeys $witness 'witness' $forms @('ISP') @('0000') $true
AddKeys $required 'opcode' $forms @('ISP') @('0000') $false
$structures=@(foreach ($spec in $full) { foreach ($ix in @('D1/W/scale=1','D1/L/scale=1','D1/W/scale=8','A0/W/scale=4')) {
    SourceId 6 1 "$spec/ix=$ix"; SourceId 7 3 "$spec/ix=$ix"
} })
AddKeys $required 'structure' $structures @('ISP') @('0000') $false
AddKeys $required 'status' $canonical @('user','user-M','ISP','MSP') @('0000','8000','4000') $false
$rows=@()
foreach ($name in @('witness-scalar','witness-batch','discovery-scalar','discovery-batch')) {
    $report=Get-Content (Join-Path $output "68040-movem-read-recovery-$name.json") -Raw | ConvertFrom-Json
    $inventory=if ($name.StartsWith('witness')) { $witness } else { $required }
    $expected=if ($name.StartsWith('witness')) { 72 } else { 114912 }
    if ($report.schema -ne 1 -or $report.model -ne '68040' -or $report.group -ne "movem-read-recovery-$name" -or $report.logicalCases -ne $expected -or $report.xunitBatches -ne 1 -or @($report.combinations.psobject.Properties).Count -ne $inventory.Count) { throw "Incomplete recovery report: $name" }
    $sum=@{passing=0;mismatching=0;unsupported=0;untested=0}
    foreach ($combination in $report.combinations.psobject.Properties) {
        if (-not $inventory.ContainsKey($combination.Name)) { throw "Foreign recovery combination: $($combination.Name)" }
        $weight=0
        foreach ($status in $combination.Value.psobject.Properties) {
            if (-not $sum.ContainsKey($status.Name) -or $status.Value -le 0) { throw 'Invalid recovery status/weight' }
            $sum[$status.Name]+=$status.Value; $weight+=$status.Value
        }
        if ($weight -ne $inventory[$combination.Name]) { throw "Changed recovery combination weight: $($combination.Name)" }
    }
    foreach ($status in $sum.Keys) { if ($sum[$status] -ne $report.counts.$status) { throw "Recovery totals disagree: $name/$status" } }
    $rows+=@{group=$name;wholePrograms=$expected;combinations=$inventory.Count;counts=$sum}
}
@{schema=1;rows=$rows;wholePrograms=229968;roadmapComplete=$false;inputManifestSha256=(Hash $manifestPath)} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output 'verification.json')
if (@($tests | Where-Object outcome -ne 'Passed').Count -gt 0 -or $manifest.testExit -ne 0 -or @($rows | Where-Object { $_.counts.mismatching + $_.counts.unsupported + $_.counts.untested -gt 0 }).Count -gt 0) { throw "040 MOVEM recovery fails. Retained evidence: $output" }
Write-Host '040 MOVEM recovery passes 229,968 whole programs across scalar/batch execution; milestone 6 remains open.'
