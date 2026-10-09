[CmdletBinding()]
param([switch]$ValidateReportsOnly,[string]$OutputDirectory='artifacts/030-pmmu-privilege-mutations')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$programPath='Copper68k/M68kAdvancedTimingInterpreter.cs';$source=Join-Path $repo $programPath
$prefix='Copper68k.Tests.Synthetic.SyntheticM68030PmmuPrivilegeTests.'
function Model-Name([string]$Model,[string]$Supervisor,[string]$Batch){"${prefix}RuleIsSpecificToUserModeOn68030(modelId: `"$Model`", supervisor: $Supervisor, batch: $Batch)"}
$names=@(($prefix+'UndefinedPmmuWordInUserModeRaisesPrivilegeBeforeLineF'))+
 @(foreach($model in @('68000','68010','68EC020','68020','68030','68040','68060','A1200')){foreach($super in @('False','True')){foreach($batch in @('False','True')){Model-Name $model $super $batch}}})+
 @(foreach($id in 1..7){foreach($batch in @('False','True')){"${prefix}NonzeroCpIdsKeepTheAbsentCoprocessorLineFRoute(id: $id, batch: $batch)"}})
$modeAnchor="(opcode & 0x0E00) == 0 &&`n                        (State.StatusRegister & M68kCpuState.Supervisor) == 0)"
$mutations=@(
 @{name='missing-priority';failed=@($names[0]) + @(foreach($batch in @('False','True')){Model-Name '68030' 'False' $batch});reason='expected privilege vector 8, actual 11'},
 @{name='wrong-cpid';anchor='(opcode & 0x0E00) == 0 &&';replacement='';failed=@($names|Where-Object{$_ -match 'NonzeroCpIds.*id: (1|2|5|6|7),'});reason='PC expected'},
 @{name='wrong-model';anchor='_profile.Model == M68kAcceleratorModel.M68030 &&';replacement='';failed=@(foreach($model in @('68EC020','68020','68040','68060','A1200')){foreach($batch in @('False','True')){Model-Name $model 'False' $batch}});reason='PC expected'},
 @{name='wrong-mode';anchor=$modeAnchor;replacement=$modeAnchor.Replace('== 0)','!= 0)');failed=@($names[0]) + @(foreach($super in @('False','True')){foreach($batch in @('False','True')){Model-Name '68030' $super $batch}});reason='PC expected'}
)
function Identity([string]$Path){
 $file=Join-Path $repo $Path
 if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing PMMU mutation input: $Path"}
 @{path=$Path;sha256=(Get-FileHash -LiteralPath $file).Hash}
}
function Validate-Selection($Actual,[string[]]$Required){
 $paths=@($Actual.path)
 if($paths.Count -ne $Required.Count -or @($paths|Select-Object -Unique).Count -ne $paths.Count -or
    ($paths.Count -gt 0 -and @(Compare-Object ($paths|Sort-Object) ($Required|Sort-Object)).Count)){throw 'Incomplete/duplicate PMMU mutation identity selection'}
 foreach($entry in $Actual){if($entry.sha256 -cnotmatch '^[A-F0-9]{64}$' -or (Identity $entry.path).sha256 -cne $entry.sha256){throw "Changed PMMU mutation input: $($entry.path)"}}
}
function Mutated-Source([string]$Text,$Mutation){
 if($Mutation.name -ceq 'missing-priority'){
  $start=$Text.IndexOf('                    // MC68030UM 8.1.5/6',[StringComparison]::Ordinal)
  $end=$Text.IndexOf('                    // MC68020UM 7.5.2.2/3',$start,[StringComparison]::Ordinal)
  if($start -lt 0 -or $end -le $start){throw 'Missing PMMU priority region'}
  return $Text.Remove($start,$end-$start)
 }
 if(($Text.Split($Mutation.anchor).Length-1) -ne 1){throw 'Nonunique PMMU mutation anchor'}
 return $Text.Replace($Mutation.anchor,$Mutation.replacement)
}
function Restore-Source([byte[]]$Bytes,[string]$ExpectedHash,[string]$OriginalHash){
 $current=(Get-FileHash -LiteralPath $source).Hash
 if($current -ceq $OriginalHash){return}
 if($current -cne $ExpectedHash){throw 'CPU source changed concurrently; current file left intact'}
 [IO.File]::WriteAllBytes($source,$Bytes)
}
function Evidence-Paths([string]$Directory,[bool]$Mutation){
 $leaves=@('controls.trx','run.log','build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')
 if($Mutation){$leaves+='mutated-cpu.cs'}
 @($leaves|ForEach-Object{[IO.Path]::GetRelativePath($repo,(Join-Path $Directory $_))})
}
function Check-Execution([string]$Directory,[string[]]$Failed,[string]$Reason=''){
 [xml]$trx=Get-Content (Join-Path $Directory 'controls.trx') -Raw
 $c=$trx.TestRun.ResultSummary.Counters;$results=@($trx.TestRun.Results.UnitTestResult)
 if($names.Count -ne 47 -or $c.total -ne 47 -or $c.executed -ne 47 -or $c.failed -ne $Failed.Count -or $c.passed -ne 47-$Failed.Count -or
  $results.Count -ne 47 -or @($results|Where-Object {$_.outcome -notin @('Passed','Failed')}).Count -ne 0 -or
  @(Compare-Object ($results.testName|Sort-Object) ($names|Sort-Object)).Count){throw 'Incomplete PMMU mutation execution selection'}
 $failedResults=@($results|Where-Object outcome -CEQ Failed)
 if($Failed.Count -gt 0 -and @(Compare-Object ($failedResults.testName|Sort-Object) ($Failed|Sort-Object)).Count){throw 'Wrong PMMU mutation failed methods'}
 if($Failed.Count -eq 0 -and $failedResults.Count -ne 0){throw 'Failed PMMU baseline'}
 if($Reason -and -not @($failedResults|Where-Object {$_.Output.ErrorInfo.Message.Contains($Reason,[StringComparison]::Ordinal)}).Count){throw 'Wrong PMMU mutation failure reason'}
}
Push-Location $repo
try{
 $inputs=@(@(& git ls-files Copper68k)+@('Copper68k.Tests/Copper68k.Tests.csproj',
  'Copper68k.Tests/Synthetic/SyntheticM68030PmmuPrivilegeTests.cs','Copper68k.Tests/Synthetic/SyntheticMachine.cs',
  'Copper68k.Tests/Synthetic/ArchitecturalExpectation.cs','Copper68k.Tests/Synthetic/SyntheticExecution.cs',
  'scripts/test-copper68k-030-pmmu-privilege-mutations.ps1')|Sort-Object -Unique)
 $normal=@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')
 $filter='FullyQualifiedName~SyntheticM68030PmmuPrivilegeTests.UndefinedPmmuWord|FullyQualifiedName~SyntheticM68030PmmuPrivilegeTests.RuleIsSpecific|FullyQualifiedName~SyntheticM68030PmmuPrivilegeTests.NonzeroCpIds'
 if(-not $ValidateReportsOnly){
  if(Test-Path -LiteralPath $output){throw 'Use a fresh PMMU mutation directory'}
  New-Item -ItemType Directory -Path $output|Out-Null
  $original=@($inputs|ForEach-Object{Identity $_});$originalNormal=@($normal|ForEach-Object{Identity $_})
  $bytes=[IO.File]::ReadAllBytes($source);$text=[IO.File]::ReadAllText($source).Replace("`r`n","`n")
  $sourceHash=(Get-FileHash -LiteralPath $source).Hash;$expectedHash=$sourceHash
  $baseline=Join-Path $output 'baseline';New-Item -ItemType Directory -Path $baseline|Out-Null
  & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $baseline 'build') --filter $filter --logger 'trx;LogFileName=controls.trx' --results-directory $baseline *> (Join-Path $baseline 'run.log')
  if($LASTEXITCODE -ne 0){throw 'PMMU mutation baseline failed'}
  Check-Execution $baseline @();$rows=@()
  try{
   foreach($mutation in $mutations){
    $directory=Join-Path $output $mutation.name;New-Item -ItemType Directory -Path $directory|Out-Null
    $mutated=Mutated-Source $text $mutation
    [IO.File]::WriteAllText((Join-Path $directory 'mutated-cpu.cs'),$mutated)
    if((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash){throw 'CPU source changed before mutation'}
    [IO.File]::WriteAllText($source,$mutated);$expectedHash=(Get-FileHash -LiteralPath $source).Hash
    & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --artifacts-path (Join-Path $directory 'build') --filter $filter --logger 'trx;LogFileName=controls.trx' --results-directory $directory *> (Join-Path $directory 'run.log')
    $code=$LASTEXITCODE;Restore-Source $bytes $expectedHash $sourceHash
    if($code -ne 1){throw "PMMU mutation did not fail: $($mutation.name)"}
    Check-Execution $directory @($mutation.failed) $mutation.reason
    $rows+=@{name=$mutation.name;exit=$code;evidence=@((Evidence-Paths $directory $true)|ForEach-Object{Identity $_})}
   }
  }finally{Restore-Source $bytes $expectedHash $sourceHash}
  foreach($entry in @($original)+@($originalNormal)){if((Identity $entry.path).sha256 -cne $entry.sha256){throw 'PMMU mutations changed original source/binaries'}}
  @{schema=1;profile='m68030-pmmu-privilege-defect-proofs';sourceRevision=(& git rev-parse HEAD);sourceRestored=$true;
   inputs=$original;normalBinaries=$originalNormal;baseline=@((Evidence-Paths $baseline $false)|ForEach-Object{Identity $_});mutations=$rows}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $output 'identity.json')
 }
 $identity=Get-Content (Join-Path $output 'identity.json') -Raw|ConvertFrom-Json
 if($identity.schema -ne 1 -or $identity.profile -cne 'm68030-pmmu-privilege-defect-proofs' -or $identity.sourceRestored -ne $true -or $identity.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Incomplete PMMU mutation profile'}
 Validate-Selection @($identity.inputs) $inputs;Validate-Selection @($identity.normalBinaries) $normal
 $baseline=Join-Path $output 'baseline';Validate-Selection @($identity.baseline) (Evidence-Paths $baseline $false);Check-Execution $baseline @()
 if($identity.mutations.Count -ne 4 -or @($identity.mutations.name|Select-Object -Unique).Count -ne 4){throw 'Incomplete/duplicate PMMU mutations'}
 $text=[IO.File]::ReadAllText($source).Replace("`r`n","`n")
 foreach($mutation in $mutations){
  $row=@($identity.mutations|Where-Object name -CEQ $mutation.name)
  if($row.Count -ne 1 -or $row[0].exit -ne 1){throw 'Incorrect PMMU mutation result selection/status'}
  $directory=Join-Path $output $mutation.name
  Validate-Selection @($row[0].evidence) (Evidence-Paths $directory $true)
  if([IO.File]::ReadAllText((Join-Path $directory 'mutated-cpu.cs')) -cne (Mutated-Source $text $mutation)){throw 'Recorded PMMU mutation source differs'}
  Check-Execution $directory @($mutation.failed) $mutation.reason
 }
 @{schema=1;sourceRestored=$true;productionCpuMutation=$true;mutationProofs=4;executions=235;roadmapComplete=$false;scope='CpID-0 priority, CpID/model/mode boundaries; no regression retired'}|ConvertTo-Json|Set-Content (Join-Path $output 'summary.json')
 Write-Host '47 controls detect all four PMMU privilege CPU defects; original CPU bytes and normal assemblies preserved.'
}finally{Pop-Location}
$global:LASTEXITCODE=0
