#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$OutputDirectory='artifacts/040-movem-mask-faults',
    [switch]$ValidateReportsOnly,
    [string]$PythonCommand='python',
    [string]$BaselineDirectory
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
if (-not $ValidateReportsOnly) {
    if (Test-Path -LiteralPath $output) { throw "Use a fresh output directory: $output" }
    $sourceRoot=Join-Path $output 'source'
    New-Item -ItemType Directory -Path $sourceRoot | Out-Null
    $sources=@(@(& git -C $repo ls-files 'Copper68k/*.cs' 'Copper68k/*.csproj' 'Copper68k.Tests/*.cs' 'Copper68k.Tests/*.csproj') +
        @('Copper68k.Tests/Synthetic/SyntheticM68040MovemMaskFaultTests.cs') | Sort-Object -Unique)
    if ($sources.Count -eq 0) { throw 'Empty source selection' }
    $identities=@{}
    foreach ($name in $sources) {
        $from=Join-Path $repo $name; $to=Join-Path $sourceRoot $name
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
        Copy-Item -LiteralPath $from -Destination $to
        $identities[$name]=Hash $to
        if ((Hash $from) -ne $identities[$name]) { throw "Source changed while freezing: $name" }
    }
    $protected=@(@('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll') |
        Where-Object { Test-Path -LiteralPath (Join-Path $repo $_) } |
        ForEach-Object { @{path=$_;sha256=(Hash (Join-Path $repo $_))} })
    $selection='FullyQualifiedName~SyntheticM68040MovemMaskFaultTests|FullyQualifiedName~SyntheticM68040MovemWriteRecoveryTests|FullyQualifiedName~FixedManualStoreEncodings|FullyQualifiedName~CanonicalStoreFixturesExecuteWithoutFault'
    $command=@('dotnet','test',(Join-Path $sourceRoot 'Copper68k.Tests/Copper68k.Tests.csproj'),'-c','Release',
        '--artifacts-path',(Join-Path $output 'build'),'--filter',$selection,'--logger','trx;LogFileName=audit.trx','--results-directory',$output)
    $settings=@{COPPER68K_SYNTHETIC_REPORT_DIR=$output;COPPER68K_RUN_040_MOVEM_MASK_FAULT_AUDIT='1';COPPER68K_RUN_040_MOVEM_WRITE_RECOVERY='1'}
    $inputs=@{sourceCommit=(& git -C $repo rev-parse HEAD);sources=$identities;command=$command;settings=$settings;
        producerSha256=(Hash $PSCommandPath);protected=$protected;publication=$false;privateCandidateImport=$false}
    $inputsPath=Join-Path $output 'inputs.json'
    $inputs | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inputsPath -Encoding utf8
    $saved=@{}
    try {
        foreach ($key in $settings.Keys) { $saved[$key]=[Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key,$settings[$key]) }
        $arguments=$command[1..($command.Count-1)]
        & dotnet @arguments *> (Join-Path $output 'execution.log')
        $testExit=$LASTEXITCODE
    } finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) }
    }
    @{exit=$testExit;inputsSha256=(Hash $inputsPath);logSha256=(Hash (Join-Path $output 'execution.log'));
        trxSha256=(Hash (Join-Path $output 'audit.trx'));cpuSha256=(Hash (Join-Path $output 'build/bin/Copper68k/release/Copper68k.dll'));
        testSha256=(Hash (Join-Path $output 'build/bin/Copper68k.Tests/release/Copper68k.Tests.dll'))} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'execution.json') -Encoding utf8
}
$verify=@((Join-Path $PSScriptRoot 'verify-copper68k-040-movem-mask-faults.py'),'--repo',$repo,'--output',$output,'--producer',$PSCommandPath)
if ($BaselineDirectory) { $verify+=@('--baseline',[IO.Path]::GetFullPath($BaselineDirectory,$repo)) }
& $PythonCommand @verify
if ($LASTEXITCODE -ne 0) { throw "040 MOVEM mask fault qualification failed; preserve evidence: $output" }
