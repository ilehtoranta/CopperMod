#requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory='artifacts/040-mixed-retry-discovery', [switch]$ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$manifest=Join-Path $output 'identities.json'
$fixtures=@('Copper68k.Tests/Synthetic/M68040MixedEpochRetryDiscoveryTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040RteRepairTests.cs',
    'Copper68k.Tests/Synthetic/SyntheticM68040ThrowawayTests.cs','Copper68k.Tests/Synthetic/SyntheticM68040RteValidationFaultTests.cs',
    'Copper68k.Tests/Synthetic/SyntheticMachine.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs','Copper68k.Tests/Synthetic/SyntheticMoveTests.cs',
    'scripts/test-copper68k-040-mixed-retry-discovery.ps1','Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
if(-not $ValidateReportsOnly) {
    if(Test-Path -LiteralPath $output){throw "Use fresh outputs: $output"}
    New-Item -ItemType Directory -Path $output|Out-Null
    & dotnet build (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release *> (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw 'Discovery build failed'}
    $identity=@{schema=1;sourceCommit=(& git -C $repo rev-parse HEAD);cpuCommittedTree=(& git -C $repo rev-parse HEAD:Copper68k);
        reference='MC68040UM';url='https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf';sections=@('8.2.6','8.4.2','8.4.6.7');
        expectation='Original-instruction trace deferral composed with committed throwaways and validation retry; independently unqualified';
        softwareReferenceExecuted=$false;architecturallyQualified=$false;inputs=@()}
    $files=@(& git -C $repo ls-files 'Copper68k/*') + $fixtures
    foreach($file in $files){$identity.inputs+=@{file=$file;sha256=(Get-FileHash (Join-Path $repo $file)).Hash.ToLowerInvariant()}}
    $identity|ConvertTo-Json -Depth 8|Set-Content $manifest
    $settings=@{COPPER68K_RUN_040_MIXED_RETRY_DISCOVERY='1';COPPER68K_SYNTHETIC_REPORT_DIR=$output};$saved=@{}
    try {
        foreach($name in $settings.Keys){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,$settings[$name])}
        & dotnet test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --no-build --no-restore --filter 'FullyQualifiedName~M68040MixedEpochRetryDiscoveryTests' --logger 'trx;LogFileName=discovery.trx' --results-directory $output *> (Join-Path $output 'run.log')
        $testExit=$LASTEXITCODE
    } finally {foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
}
if(-not (Test-Path -LiteralPath $manifest)){throw 'Missing discovery identities'}
$identity=Get-Content $manifest -Raw|ConvertFrom-Json
if($identity.schema -ne 1 -or $identity.reference -cne 'MC68040UM' -or $identity.architecturallyQualified -ne $false -or
    $identity.sourceCommit -notmatch '^[0-9a-f]{40}$' -or $identity.cpuCommittedTree -notmatch '^[0-9a-f]{40}$'){throw 'Incomplete discovery identities'}
$requiredInputs=@(& git -C $repo ls-files 'Copper68k/*') + $fixtures
if(@($identity.inputs).Count -ne $requiredInputs.Count -or @(Compare-Object ($requiredInputs|Sort-Object) ($identity.inputs.file|Sort-Object)).Count){throw 'Missing or unexpected discovery fixture identity'}
foreach($row in $identity.inputs){if((Get-FileHash (Join-Path $repo $row.file)).Hash.ToLowerInvariant() -cne $row.sha256){throw "Changed discovery input: $($row.file)"}}
[xml]$trx=Get-Content (Join-Path $output 'discovery.trx') -Raw
$c=$trx.TestRun.ResultSummary.Counters
if([int]$c.total -ne 2 -or [int]$c.executed -ne 2 -or [int]$c.notExecuted -ne 0){throw 'Discovery must execute both complete selections without skips'}
$expected=@{}
foreach($start in @('ISP','MSP')){foreach($tail in @('ISP','MSP')){foreach($middle in @('none','user','user-M','ISP','MSP')){
foreach($incoming in @(0,0x8000,0x4000)){foreach($first in @(0,0x8000,0x4000)){
foreach($second in $(if($middle -eq 'none'){@(0)}else{@(0,0x8000,0x4000)})){
foreach($result in @('user','user-M','ISP','MSP')){foreach($trace in @(0,0x8000,0x4000)){foreach($form in @('format0','normal','CM')){
    $path=if($middle -eq 'none'){"$start-$tail"}else{"$start-$middle-$tail"}
    $secondName=if($middle -eq 'none'){'none'}else{'{0:X4}' -f $second}
    $key='68040/RTE/mixed-retry-discovery/{0}/path={1}/result={2}/incoming={3:X4}/first={4:X4}/second={5}/T={6:X4}/align=0/VBR=00010000/read=2:4/fault-byte=0' -f $form,$path,$result,$incoming,$first,$secondName,$trace
    # Two CCR values, seven phases, plus trace return for original T1/T0.
    $expected[$key]=2 * $(if($incoming -eq 0){7}else{8})
}}}}}}}}}
if($expected.Count -ne 16848 -or ($expected.Values|Measure-Object -Sum).Sum -ne 258336){throw 'Independent discovery enumeration differs'}
$totals=@{passing=0;mismatching=0;unsupported=0;untested=0};$reports=@()
foreach($route in @('scalar','batch')) {
    $group="rte-mixed-retry-discovery-$route"
    $r=Get-Content (Join-Path $output "68040-$group.json") -Raw|ConvertFrom-Json -AsHashtable
    if($r.model -cne '68040' -or $r.group -cne $group -or $r.logicalCases -ne 258336 -or $r.xunitBatches -ne 1 -or $r.combinations.Count -ne 16848){throw 'Incomplete discovery report'}
    $sums=@{passing=0;mismatching=0;unsupported=0;untested=0}
    foreach($key in $r.combinations.Keys) {
        if(-not $expected.ContainsKey($key)){throw "Unexpected combination: $key"}
        $sum=0
        foreach($status in $r.combinations[$key].Keys){if(-not $sums.ContainsKey($status)){throw 'Unknown coverage status'};$n=[long]$r.combinations[$key][$status];if($n -lt 0){throw 'Negative coverage count'};$sum+=$n;$sums[$status]+=$n}
        if($sum -ne $expected[$key]){throw "Incomplete combination: $key"}
    }
    foreach($status in $sums.Keys){if($r.counts[$status] -ne $sums[$status]){throw 'Coverage totals differ'};$totals[$status]+=$sums[$status]}
    $reports+=@{group=$group;logicalCases=$r.logicalCases;combinations=$r.combinations.Count;counts=$r.counts}
}
$passed=([int]$c.failed -eq 0 -and [int]$c.passed -eq 2 -and ($totals.mismatching+$totals.unsupported+$totals.untested) -eq 0)
if(-not $ValidateReportsOnly -and (($testExit -eq 0) -ne $passed)){throw 'Test exit and coverage disagree'}
@{schema=1;logicalCases=516672;reports=$reports;counts=$totals;passed=$passed;architecturallyQualified=$false;roadmapComplete=$false;expectation=$identity.expectation}|
    ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'discovery-summary.json')
if(-not $passed){throw "Original-trace discovery remains mismatching or incomplete: $($totals|ConvertTo-Json -Compress)"}
