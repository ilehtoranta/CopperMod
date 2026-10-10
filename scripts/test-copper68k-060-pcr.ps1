[CmdletBinding()]
param([switch]$ValidateReportsOnly, [string]$OutputDirectory = 'artifacts/060-pcr')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if ((Test-Path -LiteralPath $output) -and -not $ValidateReportsOnly) { $output = Join-Path $output ([guid]::NewGuid().ToString('N')) }
$identityPath = Join-Path $output 'identity.json'
$controls = @(0, 1, 2, 3, 0x80, 0x81, 0x82, 0x83)
$groups = [ordered]@{'system-pcr-defined'=102400; 'system-pcr-identification'=172032; 'system-pcr-reserved-policy'=6144; 'system-pcr-reset'=24576}
function Identity([string]$RelativePath) {
    $path = Join-Path $repo $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing PCR input: $RelativePath" }
    return @{path=$RelativePath; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
function Check-Identities($Items) {
    foreach ($item in $Items) { if ((Identity $item.path).sha256 -cne $item.sha256) { throw "PCR input identity changed: $($item.path)" } }
}
function Add-Key($Keys, [string]$Id, [int]$Weight = 1) {
    if (-not $Keys.TryAdd($Id, $Weight)) { throw "Duplicate expected PCR key: $Id" }
}
function Write-Keys($Keys, $Values, $Ccrs) {
    foreach ($initial in $controls) { foreach ($value in $Values) { foreach ($general in 0..15) {
        foreach ($supervisor in @($false, $true)) { foreach ($ccr in $Ccrs) {
            $id = '68060/MOVEC/L/PCR/R{0}/initial={1:X8}/value={2:X8}/super={3}/ccr={4:X2}' -f $general,$initial,$value,$supervisor,$ccr
            if (-not $supervisor) { Add-Key $Keys "$id/privilege" }
            else { Add-Key $Keys "$id/write"; Add-Key $Keys "$id/read" }
        } }
    } } }
}
function Expected-Keys([string]$Group) {
    $keys = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    switch ($Group) {
        'system-pcr-defined' {
            Write-Keys $keys $controls (0..31)
            foreach ($image in $controls) { foreach ($general in 0..15) { foreach ($ccr in 0..31) {
                Add-Key $keys ('68060/MOVEC/L/PCR/read-R{0}/internal={1:X8}/ccr={2:X2}' -f $general,(0x04300000 -bor $image),$ccr)
            } } }
        }
        'system-pcr-identification' {
            $high = [Collections.Generic.HashSet[uint32]]::new()
            foreach ($v in @(0, [uint32]0xffffff00L, [uint32]0x55555500L, [uint32]0xaaaaaa00L)) { [void]$high.Add($v) }
            foreach ($bit in 8..31) { [void]$high.Add([uint32]([uint64]1 -shl $bit)) }
            $values = @($high | Sort-Object | ForEach-Object { $v=$_; $controls | ForEach-Object { [uint32]($v -bor $_) } })
            if ($values.Count -ne 224) { throw 'Empty/incomplete PCR identification selection' }
            Write-Keys $keys $values @(0,31)
        }
        'system-pcr-reserved-policy' { Write-Keys $keys @(4,8,16,32,64,0x7c,[uint32]0xffffff7cL,[uint32]::MaxValue) @(0,31) }
        'system-pcr-reset' {
            foreach ($general in 0..15) { foreach ($supervisor in @($false,$true)) { foreach ($image in $controls) { foreach ($phase in @('write','reset','read')) {
                Add-Key $keys ('68060/MOVEC/L/PCR/reset/R{0}/super={1}/image={2:X8}/{3}' -f $general,$supervisor,$image,$phase) 32
            } } } }
        }
        default { throw "Unknown PCR group: $Group" }
    }
    return ,$keys
}
$savedReports = [Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try {
    $inputPaths = @(& git ls-files Copper68k)
    $inputPaths += @('Copper68k.Tests/Synthetic/SyntheticPcrTests.cs', 'Copper68k.Tests/Synthetic/SyntheticMovecRegisterFixture.cs',
        'Copper68k.Tests/Synthetic/SyntheticMachine.cs', 'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs',
        'Copper68k.Tests/Synthetic/SyntheticExecution.cs', 'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs',
        'Copper68k.Tests/Copper68k.Tests.csproj', 'Copper68k.Tests/M68060InterpreterTests.cs',
        'scripts/test-copper68k-060-pcr.ps1', 'scripts/test-copper68k-synthetic.ps1', 'scripts/test-copper68k-synthetic-mutations.ps1')
    $inputPaths = @($inputPaths | Sort-Object -Unique)
    $binaryPaths = @('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
    $evidencePaths = @($groups.Keys | ForEach-Object { [IO.Path]::GetRelativePath($repo,(Join-Path $output "68060-$_.json")) }) +
        @([IO.Path]::GetRelativePath($repo,(Join-Path $output 'pcr.trx')))
    if (-not $ValidateReportsOnly) {
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        $inputs = @($inputPaths | Sort-Object -Unique | ForEach-Object { Identity $_ })
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR', $output)
        & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --filter 'FullyQualifiedName~SyntheticPcrTests' --logger 'trx;LogFileName=pcr.trx' --results-directory $output *> (Join-Path $output 'run.log')
        if ($LASTEXITCODE -ne 0) { throw "PCR execution failed; see $output/run.log" }
        Check-Identities $inputs
        $binaries = @($binaryPaths | ForEach-Object { Identity $_ })
        @{schema=1; profile='68060-pcr-manual-fields-and-explicit-policy'; sourceRevision=(& git rev-parse HEAD).Trim();
            inputs=$inputs; binaries=@($binaries);
            evidence=@($evidencePaths | ForEach-Object { Identity $_ })} |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $identityPath
    }
    if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) { throw 'Missing PCR identity manifest' }
    $identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
    if ($identity.schema -ne 1 -or $identity.profile -cne '68060-pcr-manual-fields-and-explicit-policy' -or
        $identity.sourceRevision -cnotmatch '^[0-9a-f]{40}$' -or @($identity.inputs).Count -lt 11 -or
        @($identity.binaries).Count -ne 2 -or @($identity.evidence).Count -ne 5) { throw 'Incomplete PCR identity manifest' }
    foreach ($selection in @(@{actual=@($identity.inputs); required=$inputPaths}, @{actual=@($identity.binaries); required=$binaryPaths}, @{actual=@($identity.evidence); required=$evidencePaths})) {
        $names = @($selection.actual | ForEach-Object { $_.path })
        if ($names.Count -ne $selection.required.Count -or @($names | Select-Object -Unique).Count -ne $names.Count -or
            @($names | Where-Object { $_ -cnotin $selection.required }).Count -ne 0) { throw 'PCR input selection differs from required identities' }
        foreach ($item in $selection.actual) { if ($item.sha256 -cnotmatch '^[0-9A-F]{64}$') { throw 'Malformed PCR input hash' } }
    }
    Check-Identities $identity.inputs; Check-Identities $identity.binaries; Check-Identities $identity.evidence
    [xml]$trx = Get-Content -LiteralPath (Join-Path $output 'pcr.trx') -Raw
    $c = $trx.TestRun.ResultSummary.Counters
    if ($c.total -ne 4 -or $c.executed -ne 4 -or $c.passed -ne 4 -or $c.failed -ne 0 -or $c.notExecuted -ne 0) { throw 'Empty or incomplete PCR test selection' }
    $methods = @('PcrDefinedControlsAllRegistersPrivilegeAndCcr','PcrIdentificationAndRevisionWritesAreIgnored',
        'PcrReservedWritesRetainRepositoryProfilePolicy','PcrResetClearsDefinedControlsFromBothStacks')
    foreach ($method in $methods) {
        if (@($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -ceq "Copper68k.Tests.Synthetic.SyntheticPcrTests.$method" -and $_.outcome -ceq 'Passed' }).Count -ne 1) { throw "Missing PCR execution: $method" }
    }
    $coverage = @()
    foreach ($group in $groups.Keys) {
        $report = Get-Content -LiteralPath (Join-Path $output "68060-$group.json") -Raw | ConvertFrom-Json -AsHashtable
        if ($report.schema -ne 1 -or $report.model -cne '68060' -or $report.group -cne $group -or $report.xunitBatches -ne 1 -or
            $report.logicalCases -ne $groups[$group] -or $report.counts.passing -ne $groups[$group] -or
            $report.counts.mismatching -ne 0 -or $report.counts.unsupported -ne 0 -or $report.counts.untested -ne 0 -or $report.failures.Count -ne 0) { throw "Incomplete PCR gate: $group" }
        $keys = Expected-Keys $group
        if ($keys.Count -ne $report.combinations.Count) { throw "PCR combination selection differs: $group" }
        foreach ($key in $keys.Keys) {
            if (-not $report.combinations.Contains($key) -or $report.combinations[$key].Count -ne 1 -or $report.combinations[$key].passing -ne $keys[$key]) {
                throw "Missing/misweighted PCR combination: $key"
            }
        }
        $coverage += @{group=$group; logicalCases=$groups[$group]; combinations=$keys.Count;
            qualification=$(if ($group -eq 'system-pcr-reserved-policy') {'Repository mask policy; architecturally excluded reserved writes'}
                elseif ($group -eq 'system-pcr-reset') {'Manual PCR/SR reset rules plus public API reset convention'} else {'Manual-defined PCR register fields'})}
    }
    @{schema=1; model='68060'; logicalCases=305152; xunitBatches=4; coverage=$coverage; roadmapComplete=$false;
        softwareReferenceAgreement=$false; physicalTimingQualified=$false;
        reference=@{manual='MC68060UM revision 1, section 3.2.2.5 / figure 3-5 / 11.1.2.1.1 / D-22';
            url='https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf'; addendum='MC68060UMAD rev 0.1, 1998-03-25';
            masksetCaveat='MC68060DE rev 4.0 I14/I15 assign PCR bit 5 for particular masks; excluded from defined-field matrix'};
        remaining='Pinned WinUAE bit-6 EDEBUG discrepancy, ordinary STOP S-clear disagreement, FPU/debug physical effects and all broader reference/consolidation requirements remain'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
    Write-Host "PCR qualified fields and explicit policy: 305152 cases / 4 batches; reports at $output"
}
finally {
    [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR', $savedReports)
    Pop-Location
}
