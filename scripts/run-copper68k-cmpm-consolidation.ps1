param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference='Stop'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$repo=Join-Path $Root 'source'
$files=@(Get-ChildItem (Join-Path $repo 'Copper68k'),(Join-Path $repo 'Copper68k.Tests') -Recurse -File | Where-Object {$_.Extension -in @('.cs','.csproj') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'})
$ids=@($files|ForEach-Object{@{path=[IO.Path]::GetRelativePath($repo,$_.FullName);sha256=(Hash $_.FullName)}})
$saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
try {
 [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$Root)
 & dotnet.exe test (Join-Path $repo 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $Root 'build') --filter 'FullyQualifiedName~M68020CmpmTests|FullyQualifiedName~ExtendComparisonAndAliases' --logger 'trx;LogFileName=audit.trx' --results-directory $Root *> (Join-Path $Root 'execution.log')
 $testExit=$LASTEXITCODE
}finally{[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved)}
foreach($i in $ids){if((Hash (Join-Path $repo $i.path)) -ne $i.sha256){throw 'Source changed during consolidation execution'}}
@{schema=1;testExit=$testExit;sources=$ids;helperSha256=(Hash $PSCommandPath);baseInputsSha256=(Hash (Join-Path $Root 'base-inputs.json'));roadmapComplete=$false}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $Root 'inputs.json')
Write-Output "CMPM consolidation execution exit $testExit; strict audit pending."
