#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PythonPath,
    [string]$OutputDirectory='artifacts/cmpm-consolidation',
    [switch]$ValidateReportsOnly
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$pin='1e1ab44489b68982a1bbc981c45cddf01fec09c5'
$legacy='Copper68k.Tests/M68020CmpmTests.cs'
$cpu='Copper68k/M68kAdvancedTimingInterpreter.cs'
$helpers=@('scripts/test-copper68k-cmpm-consolidation.ps1','scripts/run-copper68k-cmpm-consolidation.ps1','scripts/verify-copper68k-cmpm-consolidation.py','scripts/prove-copper68k-cmpm-consolidation-integrity.py')
$files=@(& git -C $repo ls-files Copper68k Copper68k.Tests | Where-Object {[IO.Path]::GetExtension($_) -in @('.cs','.csproj')})
if($LASTEXITCODE -ne 0 -or $files.Count -eq 0){throw 'Missing source input selection'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$fixture=(@(& git -C $repo show "${pin}:$legacy") -join "`n")+"`n"
if($LASTEXITCODE -ne 0 -or $fixture -notmatch 'AliasedAddressRegisterUsesTheNextOperandAfterSourceIncrement' -or $fixture -notmatch 'ByteStackRegisterUsesTwoByteStrideForBothAliasedOperands'){throw 'Missing pinned historical CMPM witnesses'}
if(-not $ValidateReportsOnly){
    if(Test-Path -LiteralPath $output){throw "Use a fresh output directory: $output"}
    New-Item -ItemType Directory -Path $output | Out-Null
    $repositoryInputs=@($files+$helpers | Sort-Object -Unique | ForEach-Object {@{path=$_;sha256=(Hash (Join-Path $repo $_))}})
    $normal=@{}
    foreach($file in @('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')){
        if(Test-Path -LiteralPath (Join-Path $repo $file)){$normal[$file]=Hash (Join-Path $repo $file)}
    }
    foreach($mode in @('Clean','AliasOrder','ByteStackStride')){
        $owned=Join-Path $output $mode
        foreach($file in $files){
            $target=Join-Path $owned (Join-Path 'source' $file)
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $target
        }
        # Reinstate the exact pinned original class only in the owned audit copy.
        $legacyPath=Join-Path $owned (Join-Path 'source' $legacy)
        [IO.File]::WriteAllText($legacyPath,$fixture)
        $source=Join-Path $owned 'source'
        $ids=@(Get-ChildItem (Join-Path $source 'Copper68k'),(Join-Path $source 'Copper68k.Tests') -Recurse -File |
            Where-Object {$_.Extension -in @('.cs','.csproj')} |
            ForEach-Object {@{path=[IO.Path]::GetRelativePath($source,$_.FullName).Replace('\','/');sha256=(Hash $_.FullName)}})
        $path=Join-Path $source $cpu
        $text=[IO.File]::ReadAllText($path).Replace("`r`n","`n")
        $start=$text.IndexOf('        private void ExecuteCmpmPostIncrement(')
        if($start -lt 0){throw 'Missing unique CMPM mutation target'}
        $end=$text.IndexOf('        private void ExecuteWordBranch(',$start)
        if($end -le $start){throw 'Missing CMPM function boundary'}
        $block=$text.Substring($start,$end-$start)
        if($mode -eq 'AliasOrder'){
            $old='var source = ReadSized(State.A[sourceRegister], size);'
            if($block.Split([string[]]@($old),[StringSplitOptions]::None).Count -ne 2){throw 'Ambiguous source mutation'}
            $block=$block.Replace($old,"var capturedDestination = State.A[destinationRegister]; // intentional old-base alias sampling`n            $old")
            $old='var destination = ReadSized(State.A[destinationRegister], size);'
            if($block.Split([string[]]@($old),[StringSplitOptions]::None).Count -ne 2){throw 'Ambiguous destination mutation'}
            $block=$block.Replace($old,'var destination = ReadSized(capturedDestination, size);')
        }
        if($mode -eq 'ByteStackStride'){
            foreach($register in @('sourceRegister','destinationRegister')){
                $old="size == M68kOperandSize.Byte && $register == 7 ? 2u : (uint)size"
                if($block.Split([string[]]@($old),[StringSplitOptions]::None).Count -ne 2){throw 'Ambiguous stack-stride mutation'}
                $block=$block.Replace($old,"size == M68kOperandSize.Byte && $register == 7 ? 1u : (uint)size")
            }
        }
        if($mode -ne 'Clean'){[IO.File]::WriteAllText($path,$text.Substring(0,$start)+$block+$text.Substring($end))}
        $sha= [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($fixture))
        @{schema=1;sourceCommit=(& git -C $repo rev-parse HEAD);historicalPin=$pin;mode=$mode;sourceIds=$ids;repositoryInputs=$repositoryInputs;normalAssemblies=$normal;historicalFile=$legacy;historicalSha256=[Convert]::ToHexString($sha).ToLowerInvariant();roadmapComplete=$false}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $owned 'base-inputs.json')
        & (Join-Path $PSScriptRoot 'run-copper68k-cmpm-consolidation.ps1') -Root $owned
    }
}
foreach($mode in @('Clean','AliasOrder','ByteStackStride')){
    & $PythonPath (Join-Path $PSScriptRoot 'verify-copper68k-cmpm-consolidation.py') (Join-Path $output $mode) $mode $repo $output
    if($LASTEXITCODE -ne 0){throw "CMPM consolidation verification failed: $mode"}
}
& $PythonPath (Join-Path $PSScriptRoot 'prove-copper68k-cmpm-consolidation-integrity.py') $output $repo $(if($ValidateReportsOnly){'verify'}else{'create'})
if($LASTEXITCODE -ne 0){throw 'CMPM consolidation integrity/proof failed'}
Write-Output 'CMPM alias/stack consolidation proven; all original and replacement witnesses audited, 36 timing cases retained.'
