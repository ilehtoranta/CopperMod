#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/040-operand-read-discovery',
    [switch] $ValidateReportsOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
function Hash([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifestPath = Join-Path $output 'inputs.json'
$sources = @(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj') + @('scripts/test-copper68k-040-operand-read-discovery.ps1','Copper68k.Tests/Synthetic/SyntheticM68040OperandReadFaultDiscoveryTests.cs') | Sort-Object -Unique)
$evidence = @('audit.trx','68040-operand-read-fault-discovery-scalar.json','68040-operand-read-fault-discovery-batch.json','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')
if (-not $ValidateReportsOnly) {
    if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $identities = @($sources | ForEach-Object { @{ path=$_; sha256=(Hash (Join-Path $repo $_)) } })
    $settings = @{ COPPER68K_RUN_040_OPERAND_READ_DISCOVERY='1'; COPPER68K_SYNTHETIC_REPORT_DIR=$output }
    $saved = @{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68040OperandReadFaultDiscoveryTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    @{ schema=1; reference='MC68040UM 8.4.6/8.4.6.2/8.4.6.7'; referenceUrl='https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf'; sourceCommit=(& git -C $repo rev-parse HEAD); testExit=$testExit; sources=$identities; evidence=@($evidence | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $output $_))} }) } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath
}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.sources.Count -ne $sources.Count -or
    @(Compare-Object ($manifest.sources.path | Sort-Object) $sources).Count -ne 0 -or
    $manifest.evidence.Count -ne 5 -or @(Compare-Object ($manifest.evidence.path | Sort-Object) ($evidence | Sort-Object)).Count -ne 0) { throw 'Missing discovery identities' }
foreach ($source in $manifest.sources) { if ((Hash (Join-Path $repo $source.path)) -ne $source.sha256) { throw "Changed discovery source: $($source.path)" } }
foreach ($item in $manifest.evidence) { if ((Hash (Join-Path $output $item.path)) -ne $item.sha256) { throw "Changed discovery evidence: $($item.path)" } }
[xml]$trx=Get-Content (Join-Path $output 'audit.trx') -Raw
$c=$trx.TestRun.ResultSummary.Counters
$tests=@($trx.TestRun.Results.UnitTestResult)
if ($c.executed -ne 3 -or $c.notExecuted -ne 0 -or $tests.Count -ne 3 -or @($tests.testId | Sort-Object -Unique).Count -ne 3) { throw 'Incomplete discovery execution' }
foreach ($name in @('ScalarReadFaultsRequireFormat7','BatchReadFaultsRequireFormat7','FixedManualReadInstructionWitnessesValidateEncodings')) {
    $selected=@($tests | Where-Object { $_.testName.EndsWith('.'+$name) })
    if ($selected.Count -ne 1 -or ($name.StartsWith('Fixed') -and $selected[0].outcome -ne 'Passed')) { throw "Missing/passing fixture witness: $name" }
}
# Independent architectural inventory; never calculate expectations with CPU helpers.
$forms=@(@('MOVE',1,'1010'),@('MOVE',2,'3010'),@('MOVE',4,'2010'),@('MOVEA',2,'3050'),@('MOVEA',4,'2050'),@('ADD',1,'D010'),@('ADD',2,'D050'),@('ADD',4,'D090'),@('CMP',1,'B010'),@('CMP',2,'B050'),@('CMP',4,'B090'),@('TST',1,'4A10'),@('TST',2,'4A50'),@('TST',4,'4A90'),@('MOVEM',2,'4C90'),@('MOVEM',4,'4CD0'))
$required=@{}
foreach ($form in $forms) { foreach ($bank in @('user','user-M','ISP','MSP')) { foreach ($trace in @('0000','8000','4000')) { foreach ($lane in 0..3) { for ($faultByte=0; $faultByte -lt $form[1]; $faultByte++) {
    $key="68040/$($form[0])/operand-read-fault/size=$($form[1])/bank=$bank/T=$trace/lane=$lane/byte=$faultByte"
    $required[$key]=32
} } } } }
$rows=@()
foreach ($route in @('scalar','batch')) {
    $report=Get-Content (Join-Path $output "68040-operand-read-fault-discovery-$route.json") -Raw | ConvertFrom-Json
    if ($report.model -ne '68040' -or $report.group -ne "operand-read-fault-discovery-$route" -or $report.logicalCases -ne 61440 -or $report.xunitBatches -ne 1 -or @($report.combinations.psobject.Properties).Count -ne 1920) { throw "Incomplete discovery coverage: $route" }
    $sum=@{passing=0;mismatching=0;unsupported=0;untested=0}
    foreach ($combination in $report.combinations.psobject.Properties) {
        if (-not $required.ContainsKey($combination.Name)) { throw "Foreign discovery combination: $($combination.Name)" }
        $weight=0
        foreach ($status in $combination.Value.psobject.Properties) {
            if (-not $sum.ContainsKey($status.Name) -or $status.Value -le 0) { throw 'Invalid discovery status/weight' }
            $sum[$status.Name]+=$status.Value; $weight+=$status.Value
        }
        if ($weight -ne $required[$combination.Name]) { throw "Changed discovery combination weight: $($combination.Name)" }
    }
    foreach ($status in $sum.Keys) { if ($sum[$status] -ne $report.counts.$status) { throw "Discovery totals disagree: $route/$status" } }
    $rows+=@{route=$route;logicalCases=61440;combinations=1920;counts=$sum}
}
$mismatches=($rows | ForEach-Object {$_.counts.mismatching} | Measure-Object -Sum).Sum
$unsupported=($rows | ForEach-Object {$_.counts.unsupported} | Measure-Object -Sum).Sum
$untested=($rows | ForEach-Object {$_.counts.untested} | Measure-Object -Sum).Sum
@{schema=1;rows=$rows;logicalCases=122880;fixtureWitnesses=16;roadmapComplete=$false;inputManifestSha256=(Hash $manifestPath)} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output 'verification.json')
if ($mismatches+$unsupported+$untested -gt 0 -or $c.failed -ne 0 -or $manifest.testExit -ne 0) { throw "040 operand-read qualification fails: $mismatches mismatching, $unsupported unsupported, $untested untested cases. Retained reports: $output" }
Write-Host '040 operand-read qualification passes its complete selected inventory.'
