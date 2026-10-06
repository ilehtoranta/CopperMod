#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/040-movem-write-discovery',
    [switch] $ValidateReportsOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
function Hash([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifestPath = Join-Path $output 'inputs.json'
$sources = @(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj') + @(
    'Copper68k.Tests/Synthetic/SyntheticM68040MovemWriteFaultDiscoveryTests.cs',
    'scripts/test-copper68k-040-movem-write-discovery.ps1') | Sort-Object -Unique)
$evidence = @('audit.trx','68040-movem-write-fault-discovery-scalar.json','68040-movem-write-fault-discovery-batch.json',
    '68040-movem-write-fixture-control-scalar.json','68040-movem-write-fixture-control-batch.json',
    'build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')
if (-not $ValidateReportsOnly) {
    if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $identities = @($sources | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $protected = @(@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') |
        Where-Object { Test-Path -LiteralPath (Join-Path $repo $_) } | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $settings = @{COPPER68K_RUN_040_MOVEM_WRITE_DISCOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output}
    $saved = @{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68040MovemWriteFaultDiscoveryTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    @{schema=1;reference='MC68040UM 8.4.6.2/5/7';referenceUrl='https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf';
        sourceCommit=(& git -C $repo rev-parse HEAD);testExit=$testExit;sources=$identities;protected=$protected;
        evidence=@($evidence | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $output $_))} })} |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.sources.Count -ne $sources.Count -or
    @(Compare-Object ($manifest.sources.path | Sort-Object) $sources).Count -ne 0 -or
    $manifest.evidence.Count -ne $evidence.Count -or
    @(Compare-Object ($manifest.evidence.path | Sort-Object) ($evidence | Sort-Object)).Count -ne 0) { throw 'Missing discovery identities' }
foreach ($source in $manifest.sources) { if ((Hash (Join-Path $repo $source.path)) -ne $source.sha256) { throw "Changed source: $($source.path)" } }
foreach ($item in $manifest.evidence) { if ((Hash (Join-Path $output $item.path)) -ne $item.sha256) { throw "Changed evidence: $($item.path)" } }
foreach ($item in $manifest.protected) { if ((Hash (Join-Path $repo $item.path)) -ne $item.sha256) { throw "Normal DLL changed: $($item.path)" } }
[xml]$trx = Get-Content (Join-Path $output 'audit.trx') -Raw
$tests = @($trx.TestRun.Results.UnitTestResult)
if ($tests.Count -ne 5 -or $trx.TestRun.ResultSummary.Counters.executed -ne 5 -or
    @($tests.testId | Sort-Object -Unique).Count -ne 5) { throw 'Incomplete discovery execution' }
foreach ($name in @('FixedManualStoreEncodings','ScalarStoresRequireFormat7','BatchStoresRequireFormat7')) {
    $selection = @($tests | Where-Object { $_.testName.EndsWith('.'+$name) })
    if ($selection.Count -ne 1 -or $selection[0].outcome -notin @('Passed','Failed') -or
        ($name.StartsWith('Fixed') -and $selection[0].outcome -ne 'Passed')) { throw "Missing/passing fixture witness: $name" }
}
# Independent opcode/form roster. Each opcode key has one case; status keys
# contain all 32 CCRs. Never accept a reduced, empty or foreign selection.
$required = @{}
function AddKeys([string]$Cohort, [int]$Mode, [int]$Register, [string]$Bank, [string]$Trace, [int]$Weight) {
    foreach ($width in @(2,4)) { foreach ($transfer in 0..3) { for ($byte=0; $byte -lt $width; $byte++) {
        $required["68040/MOVEM/write-fault/$Cohort/size=$width/mode=$Mode/reg=$Register/bank=$Bank/T=$Trace/transfer=$transfer/byte=$byte"]=$Weight
    } } }
}
foreach ($mode in @(2,4,5,6,7)) { for ($register=0; $register -lt $(if ($mode -eq 7) {2} else {8}); $register++) { AddKeys opcode $mode $register ISP 0000 1 } }
foreach ($mode in @(2,4,5,6,7)) { foreach ($register in $(if ($mode -eq 7) {@(0,1)} else {@(0)})) {
    foreach ($bank in @('user','user-M','ISP','MSP')) { foreach ($trace in @('0000','8000','4000')) { AddKeys status $mode $register $bank $trace 32 } }
} }
$rows = @()
foreach ($route in @('scalar','batch')) {
    $control = Get-Content (Join-Path $output "68040-movem-write-fixture-control-$route.json") -Raw | ConvertFrom-Json
    $controlTest = @($tests | Where-Object { $_.testName.EndsWith('.CanonicalStoreFixturesExecuteWithoutFault(batch: '+ $(if ($route -eq 'scalar') {'False'} else {'True'}) +')') })
    if ($controlTest.Count -ne 1 -or $controlTest[0].outcome -ne 'Passed' -or $control.schema -ne 1 -or
        $control.model -ne '68040' -or $control.group -ne "movem-write-fixture-control-$route" -or
        $control.logicalCases -ne 68 -or $control.xunitBatches -ne 1 -or $control.counts.passing -ne 68 -or
        $control.counts.mismatching+$control.counts.unsupported+$control.counts.untested -ne 0 -or
        @($control.combinations.psobject.Properties).Count -ne 68) { throw "Missing/passing fixture controls: $route" }
    foreach ($mode in @(2,4,5,6,7)) { for ($register=0; $register -lt $(if ($mode -eq 7) {2} else {8}); $register++) { foreach ($width in @(2,4)) {
        $key="68040/MOVEM/write-fault/fixture/size=$width/mode=$mode/reg=$register/bank=ISP/T=0000/transfer=3/byte=0"
        if ($control.combinations.$key.passing -ne 1 -or @($control.combinations.$key.psobject.Properties).Count -ne 1) { throw "Wrong fixture control key/weight: $key" }
    } } }
    $report = Get-Content (Join-Path $output "68040-movem-write-fault-discovery-$route.json") -Raw | ConvertFrom-Json
    if ($report.schema -ne 1 -or $report.model -ne '68040' -or $report.group -ne "movem-write-fault-discovery-$route" -or
        $report.logicalCases -ne 56112 -or $report.xunitBatches -ne 1 -or @($report.combinations.psobject.Properties).Count -ne 2544) { throw "Incomplete coverage: $route" }
    $sum = @{passing=0;mismatching=0;unsupported=0;untested=0}
    foreach ($combination in $report.combinations.psobject.Properties) {
        if (-not $required.ContainsKey($combination.Name)) { throw "Foreign combination: $($combination.Name)" }
        $weight=0
        foreach ($status in $combination.Value.psobject.Properties) {
            if (-not $sum.ContainsKey($status.Name) -or $status.Value -le 0) { throw 'Invalid status/weight' }
            $sum[$status.Name]+=$status.Value; $weight+=$status.Value
        }
        if ($weight -ne $required[$combination.Name]) { throw "Changed combination weight: $($combination.Name)" }
    }
    foreach ($status in $sum.Keys) { if ($sum[$status] -ne $report.counts.$status) { throw 'Report totals differ from combinations' } }
    $test = @($tests | Where-Object { $_.testName.EndsWith('.'+ $(if ($route -eq 'scalar') {'ScalarStoresRequireFormat7'} else {'BatchStoresRequireFormat7'})) })[0]
    $nonpassing=$sum.mismatching+$sum.unsupported+$sum.untested
    if (($nonpassing -eq 0) -ne ($test.outcome -eq 'Passed')) { throw 'TRX/report outcome disagreement' }
    $rows += [pscustomobject]@{route=$route;logicalCases=$report.logicalCases;passing=$sum.passing;mismatching=$sum.mismatching;unsupported=$sum.unsupported;untested=$sum.untested}
}
$rows | Format-Table | Out-Host
if (@($rows | Where-Object { $_.passing -ne 56112 }).Count -ne 0 -or $manifest.testExit -ne 0) {
    throw 'MOVEM write-fault acceptance failed; retain the original failing evidence. No gap is closed.'
}
