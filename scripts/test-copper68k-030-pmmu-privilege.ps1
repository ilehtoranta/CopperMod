[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/030-pmmu-privilege')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$groups=@(foreach($matrix in @('opcodes','status')){foreach($route in @('scalar','batch')){"system-pmmu-privilege-$matrix-$route"}})
function Identity([string]$Path){
 $file=Join-Path $repo $Path
 if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing PMMU privilege input: $Path"}
 @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Validate-Selection($Actual,[string[]]$Required){
 $paths=@($Actual.path)
 if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
    @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count){throw 'Incomplete/duplicate PMMU privilege identity selection'}
 foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed PMMU privilege input: $($entry.path)"}}
}
function Expected-Keys([string]$Matrix){
 $keys=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
 if($Matrix -ceq 'opcodes'){
  for($word=0xf000;$word -le 0xf1ff;$word++){$keys[('68030/PMMU/privilege/primary={0:X4}/extension=00BE/status=0700' -f $word)]=1}
 }elseif($Matrix -ceq 'status'){
  for($format=0;$format -lt 8;$format++){foreach($extension in @(0,1,0xbe,0x2000,0x4000,0x8000,0xa000,0xffff)){
   for($trace=0;$trace -lt 4;$trace++){for($master=0;$master -lt 2;$master++){for($ipl=0;$ipl -lt 8;$ipl++){
    $word=0xf000+64*$format;$status=16384*$trace+4096*$master+256*$ipl
    $keys[('68030/PMMU/privilege/primary={0:X4}/extension={1:X4}/status={2:X4}' -f $word,$extension,$status)]=32
   }}}
  }}
 }else{throw 'Unknown PMMU privilege matrix'}
 return $keys
}
$prefix='Copper68k.Tests.Synthetic.SyntheticM68030PmmuPrivilegeTests.'
$names=@(($prefix+'UndefinedPmmuWordInUserModeRaisesPrivilegeBeforeLineF'))+
 @(foreach($method in @('EveryCpIdZeroPrimaryWordInUserMode','EveryDefinedUserStatusAndSecondaryWordBoundaries')){foreach($batch in @('False','True')){"$prefix${method}(batch: $batch)"}})+
 @(foreach($model in @('68000','68010','68EC020','68020','68030','68040','68060','A1200')){foreach($super in @('False','True')){foreach($batch in @('False','True')){"${prefix}RuleIsSpecificToUserModeOn68030(modelId: `"$model`", supervisor: $super, batch: $batch)"}}})+
 @(foreach($id in 1..7){foreach($batch in @('False','True')){"${prefix}NonzeroCpIdsKeepTheAbsentCoprocessorLineFRoute(id: $id, batch: $batch)"}})
$saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
Push-Location $repo
try{
 $inputs=@(@(& git ls-files Copper68k)+@('Copper68k.Tests/Copper68k.Tests.csproj',
  'Copper68k.Tests/Synthetic/SyntheticM68030PmmuPrivilegeTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
  'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs',
  'Copper68k.Tests/Synthetic/SyntheticMoveTests.cs','scripts/test-copper68k-030-pmmu-privilege.ps1')|Sort-Object -Unique)
 $binaries=@('build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
 $evidence=@(@($groups|ForEach-Object{"68030-$_.json"})+@('privilege.trx','run.log')|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $output $_))})
 if(-not $ValidateReportsOnly){
  if(Test-Path -LiteralPath $output){throw 'Use a fresh PMMU privilege output directory'}
  New-Item -ItemType Directory -Path $output|Out-Null
  $original=@($inputs|ForEach-Object{Identity $_})
  [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$output)
  & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $output 'build') --filter 'FullyQualifiedName~SyntheticM68030PmmuPrivilegeTests' --logger 'trx;LogFileName=privilege.trx' --results-directory $output *> (Join-Path $output 'run.log')
  if($LASTEXITCODE -ne 0){throw "PMMU privilege execution failed: $output/run.log"}
  foreach($entry in $original){if((Identity $entry.path).sha256 -cne $entry.sha256){throw 'PMMU privilege source changed during execution'}}
  @{schema=1;profile='m68030-cpid0-user-privilege';sourceRevision=(& git rev-parse HEAD);inputs=$original;
    binaries=@($binaries|ForEach-Object{Identity $_});evidence=@($evidence|ForEach-Object{Identity $_})}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'identity.json')
 }
 $identity=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
 if($identity.schema -ne 1 -or $identity.profile -cne 'm68030-cpid0-user-privilege' -or $identity.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete PMMU privilege identity profile'}
 Validate-Selection @($identity.inputs) $inputs;Validate-Selection @($identity.binaries) $binaries;Validate-Selection @($identity.evidence) $evidence
 [xml]$trx=Get-Content (Join-Path $output 'privilege.trx') -Raw
 $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
 if($names.Count -ne 51 -or $c.total -ne 51 -or $c.executed -ne 51 -or $c.passed -ne 51 -or $c.failed -ne 0 -or
   $results.Count -ne 51 -or @($results|Where-Object outcome -CNE Passed).Count -ne 0 -or
   @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count){throw 'Incomplete PMMU privilege execution selection'}
 $coverage=@()
 foreach($group in $groups){
  $matrix=$group -replace '^system-pmmu-privilege-','' -replace '-(scalar|batch)$',''
  $keys=Expected-Keys $matrix;$cases=if($matrix -ceq 'opcodes'){512}else{131072}
  $r=Get-Content (Join-Path $output "68030-$group.json") -Raw|ConvertFrom-Json -AsHashtable
  if($r.schema -ne 1 -or $r.model -cne '68030' -or $r.group -cne $group -or $r.xunitBatches -ne 1 -or
   $r.logicalCases -ne $cases -or $r.counts.passing -ne $cases -or $r.counts.mismatching -ne 0 -or
   $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.failures.Count -ne 0 -or $r.combinations.Count -ne $keys.Count){throw "Incomplete PMMU privilege coverage: $group"}
  foreach($key in $keys.Keys){if(-not $r.combinations.Contains($key) -or $r.combinations[$key].Count -ne 1 -or $r.combinations[$key].passing -ne $keys[$key]){throw "Missing/misweighted PMMU privilege combination: $key"}}
  $coverage+=@{group=$group;scenarios=$cases;combinations=$keys.Count}
 }
 @{schema=1;logicalCases=263168;xunitBatches=4;fixedExecutions=47;coverage=$coverage;roadmapComplete=$false;
  reference='MC68030UM 8.1.5/6, 9.8';scope='CpID-0 user privilege abort, preserved state/stacks, software frame skip/RTE and following sentinel; scalar/batch';
  remaining='Legal supervisor PMMU instruction semantics, enabled translation, physical coprocessor/bus timing and all remaining milestone-6 protocols'}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'summary.json')
 Write-Host "030 PMMU user privilege: 263168 passing scenarios / 9216 combinations; $output"
}finally{[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved);Pop-Location}
$global:LASTEXITCODE=0
