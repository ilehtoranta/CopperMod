#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory='artifacts/020-address-frame-discovery', [switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$sourceNames=@(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj')+@(
    'Copper68k.Tests/Synthetic/SyntheticM68020AddressFrameDiscoveryTests.cs','scripts/test-copper68k-020-address-frame-discovery.ps1') | Sort-Object -Unique)
$models=@('68EC020','A1200','68020','68030')
$groups=@('address-frame-controls','address-frame-discovery-scalar','address-frame-discovery-batch')
$reportNames=@(foreach ($model in $models) { foreach ($group in $groups) { "$model-$group.json" } })
$evidenceNames=@('audit.trx','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')+$reportNames
$manifestPath=Join-Path $output 'inputs.json'
if (-not $ValidateReportsOnly) {
    if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $sources=@($sourceNames | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $protected=@(@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') |
        Where-Object { Test-Path (Join-Path $repo $_) } | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $settings=@{COPPER68K_RUN_020_ADDRESS_FRAME_DISCOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output}; $saved=@{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68020AddressFrameDiscoveryTests' --logger 'trx;LogFileName=audit.trx' --results-directory $output *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    } finally { foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) } }
    @{schema=1;reference='MC68020UM 6.1.3; MC68030UM 8.1.3';sourceCommit=(& git -C $repo rev-parse HEAD);testExit=$testExit;
        sources=$sources;protected=$protected;evidence=@($evidenceNames | ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $output $_))} })} |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath
}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.sources.Count -ne $sourceNames.Count -or
    (($manifest.sources.path | Sort-Object) -join '|') -cne (($sourceNames | Sort-Object) -join '|') -or
    $manifest.evidence.Count -ne $evidenceNames.Count -or
    (($manifest.evidence.path | Sort-Object) -join '|') -cne (($evidenceNames | Sort-Object) -join '|')) { throw 'Missing frozen address-frame identities' }
foreach ($item in $manifest.sources+$manifest.protected) { if ((Hash (Join-Path $repo $item.path)) -ne $item.sha256) { throw "Changed source/protected DLL: $($item.path)" } }
foreach ($item in $manifest.evidence) { if ((Hash (Join-Path $output $item.path)) -ne $item.sha256) { throw "Changed evidence: $($item.path)" } }
[xml]$trx=Get-Content (Join-Path $output 'audit.trx') -Raw
$tests=@($trx.TestRun.Results.UnitTestResult)
$prefix='Copper68k.Tests.Synthetic.SyntheticM68020AddressFrameDiscoveryTests.'
$names=@($models | ForEach-Object { $prefix+'FaultFreeFixturesPreserveBanksRegistersAndMemory(modelId: "'+$_+'")' })+@(
    ($prefix+'ScalarOddInstructionFetchRequiresBusFaultFrame'),($prefix+'BatchOddInstructionFetchRequiresBusFaultFrame'))
if ($tests.Count -ne 6 -or @($tests.testId | Sort-Object -Unique).Count -ne 6 -or
    (($tests.testName | Sort-Object) -join '|') -cne (($names | Sort-Object) -join '|') -or
    @($tests | Where-Object { $_.outcome -notin @('Passed','Failed') }).Count -ne 0) { throw 'Incomplete address-frame execution roster' }
$allPassing=$true; $rows=@()
foreach ($model in $models) { foreach ($group in $groups) {
    $report=Get-Content (Join-Path $output "$model-$group.json") -Raw | ConvertFrom-Json
    $control=$group -eq 'address-frame-controls'; $expected=if ($control) {128} else {768}; $keys=@{}
    foreach ($bank in @('user','user-M','ISP','MSP')) {
        if ($control) { $keys["$model/NOP/address-frame-control/bank=$bank"]=32 }
        else { foreach ($trace in @(0,0x8000,0x4000)) { foreach ($target in @(0x6001,0x10006001)) {
            $keys[('{0}/address-error/odd-prefetch/bank={1}/T={2:X4}/target={3:X8}' -f $model,$bank,$trace,$target)]=32
        } } }
    }
    if ($report.schema -ne 1 -or $report.model -ne $model -or $report.group -ne $group -or
        $report.logicalCases -ne $expected -or $report.xunitBatches -ne 1 -or
        @($report.combinations.psobject.Properties).Count -ne $keys.Count) { throw "Incomplete address-frame report: $model/$group" }
    $sum=@{passing=0;mismatching=0;unsupported=0;untested=0}
    foreach ($combination in $report.combinations.psobject.Properties) {
        if (-not $keys.ContainsKey($combination.Name)) { throw 'Foreign address-frame combination' }
        $weight=0
        foreach ($status in $combination.Value.psobject.Properties) {
            if (-not $sum.ContainsKey($status.Name) -or $status.Value -le 0) { throw 'Invalid address-frame weight/status' }
            $weight+=$status.Value; $sum[$status.Name]+=$status.Value
        }
        if ($weight -ne $keys[$combination.Name]) { throw 'Changed address-frame weight' }
    }
    foreach ($status in $sum.Keys) { if ($sum[$status] -ne $report.counts.$status) { throw 'Address-frame totals differ' } }
    $testName=if ($control) { $prefix+'FaultFreeFixturesPreserveBanksRegistersAndMemory(modelId: "'+$model+'")' }
        elseif ($group.EndsWith('scalar')) { $prefix+'ScalarOddInstructionFetchRequiresBusFaultFrame' }
        else { $prefix+'BatchOddInstructionFetchRequiresBusFaultFrame' }
    $test=@($tests | Where-Object testName -eq $testName)[0]
    $stdout=[string]$test.Output.StdOut
    $pattern=[regex]::Escape("$model/${group}: ")+'(\{[^\r\n]+\}); logical cases=(\d+), xUnit batches=1, combinations=(\d+)'
    $matches=[regex]::Matches($stdout,$pattern)
    if ($matches.Count -ne 1 -or [int]$matches[0].Groups[2].Value -ne $expected -or
        [int]$matches[0].Groups[3].Value -ne $keys.Count) { throw 'Missing actual TRX address-frame summary' }
    $actualCounts=$matches[0].Groups[1].Value | ConvertFrom-Json
    foreach ($status in $sum.Keys) { if ($actualCounts.$status -ne $sum[$status]) { throw 'TRX/report address-frame counts differ' } }
    if ($control -and ($sum.passing -ne 128 -or $test.outcome -ne 'Passed')) { throw 'Invalid fault-free address-frame controls' }
    if ($sum.passing -ne $expected) { $allPassing=$false }
    $rows+=@{model=$model;group=$group;cases=$expected;counts=$sum}
} }
$rows | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'verification.json')
if (-not $allPassing -or $manifest.testExit -ne 0 -or @($tests | Where-Object outcome -ne 'Passed').Count -ne 0) {
    throw '020/030 address-frame gate has required mismatches or unsupported/untested cases'
}
Write-Output '6,144 address-frame cases and 512 controls pass.'
