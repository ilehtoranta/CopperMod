#requires -Version 7.0
[CmdletBinding()]
param([string] $OutputDirectory='artifacts/extb-consolidation', [switch] $ValidateReportsOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory,$repo)
$pin='4863f449816115b536b21f9faa7c73539ecaabcb'
$legacyFile='Copper68k.Tests/M68020InterpreterTests.cs'
$legacyName='M68020ExecutesM68020OnlyExtbLong'
$advanced='Copper68k/M68kAdvancedTimingInterpreter.cs'
$before='var value = unchecked((uint)(int)(sbyte)(State.D[register] & 0xFF));'
$after='var value = State.D[register] & 0xFF; // Mutation: EXTB zero extension.'
$models=@('68000','68010','68EC020','68020','68030','68040','68060','A1200')
function Hash([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$files=@(& git -C $repo ls-files Copper68k Copper68k.Tests)
$identities=@($files + 'scripts/test-copper68k-extb-consolidation.ps1' | Sort-Object -Unique)
$historical=@(& git -C $repo show "${pin}:$legacyFile") -join "`n"
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($historical)){throw 'Missing pinned historical regression fixture'}
$pattern='(?ms)^\t\[Fact\]\n\tpublic void '+$legacyName+'\(\)\n\t\{.*?^\t\}\n'
$matches=[regex]::Matches($historical,$pattern)
if($matches.Count -ne 1){throw 'Historical regression fixture is not unique'}
$fixture=$matches[0].Value
$manifestPath=Join-Path $output 'inputs.json'
if(-not $ValidateReportsOnly){
    if(Test-Path -LiteralPath $output){throw "Use a fresh output directory: $output"}
    New-Item -ItemType Directory -Path $output | Out-Null
    $sourceIds=@($identities | ForEach-Object {@{path=$_;sha256=(Hash (Join-Path $repo $_))}})
    $normal=@(foreach($file in @('Copper68k/bin/Release/net10.0/Copper68k.dll','Copper68k.Tests/bin/Release/net10.0/Copper68k.Tests.dll')){
        if(Test-Path -LiteralPath (Join-Path $repo $file)){@{path=$file;sha256=(Hash (Join-Path $repo $file))}}
    })
    $fixture | Set-Content -LiteralPath (Join-Path $output 'historical-regression.cs.txt')
    $records=@()
    foreach($kind in @('clean','zero-extension')){
        $owned=Join-Path $output $kind
        New-Item -ItemType Directory -Path $owned | Out-Null
        foreach($file in $files){$dest=Join-Path $owned $file;New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $dest}
        $legacyPath=Join-Path $owned $legacyFile
        $text=[IO.File]::ReadAllText($legacyPath).Replace("`r`n","`n")
        # After retirement, inject only the pinned original fact into this owned copy.
        if(-not $text.Contains("public void $legacyName()")){
            $anchor="public sealed class M68020InterpreterTests`n{`n"
            if($text.Split([string[]]@($anchor),[StringSplitOptions]::None).Count -ne 2){throw 'Historical fixture insertion anchor changed'}
            $text=$text.Replace($anchor,$anchor+$fixture)
        } elseif([regex]::Match($text,$pattern).Value -cne $fixture){throw 'Existing legacy regression differs from pinned fixture'}
        [IO.File]::WriteAllText($legacyPath,$text)
        $production=Join-Path $owned $advanced
        $code=[IO.File]::ReadAllText($production)
        if($code.Split([string[]]@($before),[StringSplitOptions]::None).Count -ne 2){throw 'EXTB mutation target is not unique'}
        if($kind -eq 'zero-extension'){[IO.File]::WriteAllText($production,$code.Replace($before,$after))}
        $saved=[Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
        try{
            [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$owned)
            & dotnet test (Join-Path $owned 'Copper68k.Tests/Copper68k.Tests.csproj') -c Release --artifacts-path (Join-Path $owned 'build') --filter "FullyQualifiedName~$legacyName|FullyQualifiedName~RegisterTransferFamilies" --logger 'trx;LogFileName=audit.trx' --results-directory $owned *> (Join-Path $owned 'execution.log')
            $code=$LASTEXITCODE
        }finally{[Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR',$saved)}
        $paths=@('audit.trx','execution.log',$legacyFile,$advanced,'build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')+@($models | ForEach-Object{"$_-transfer-registers.json"})
        $records+=@{kind=$kind;exit=$code;evidence=@($paths | ForEach-Object{@{path=$_;sha256=(Hash (Join-Path $owned $_))}})}
    }
    foreach($item in $normal){if((Hash (Join-Path $repo $item.path)) -ne $item.sha256){throw 'Normal assembly modified'}}
    @{schema=1;historicalPin=$pin;legacyMethod=$legacyName;sourceCommit=(& git -C $repo rev-parse HEAD);sources=$sourceIds;normalBinaries=$normal;fixtureSha256=(Hash (Join-Path $output 'historical-regression.cs.txt'));runs=$records} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath
}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if($manifest.schema -ne 1 -or $manifest.historicalPin -ne $pin -or $manifest.legacyMethod -ne $legacyName -or
    $manifest.sources.Count -ne $identities.Count -or @(Compare-Object ($manifest.sources.path | Sort-Object) $identities).Count -or
    $manifest.runs.Count -ne 2 -or @(Compare-Object ($manifest.runs.kind | Sort-Object) @('clean','zero-extension')).Count){throw 'Incomplete consolidation identities'}
foreach($item in $manifest.sources){if((Hash (Join-Path $repo $item.path)) -ne $item.sha256){throw "Changed consolidation source: $($item.path)"}}
if((Hash (Join-Path $output 'historical-regression.cs.txt')) -ne $manifest.fixtureSha256 -or
    (Get-Content (Join-Path $output 'historical-regression.cs.txt') -Raw).TrimEnd() -cne $fixture.TrimEnd()){throw 'Changed historical fixture'}
$legacy='Copper68k.Tests.M68020InterpreterTests.'+$legacyName
$names=@($legacy)+@($models | ForEach-Object{'Copper68k.Tests.Synthetic.SyntheticTransferTests.RegisterTransferFamilies(modelId: "'+$_+'")'})
foreach($run in $manifest.runs){
    $owned=Join-Path $output $run.kind
    $paths=@('audit.trx','execution.log',$legacyFile,$advanced,'build/bin/Copper68k/release/Copper68k.dll','build/bin/Copper68k.Tests/release/Copper68k.Tests.dll')+@($models | ForEach-Object{"$_-transfer-registers.json"})
    if($run.evidence.Count -ne $paths.Count -or @(Compare-Object ($run.evidence.path | Sort-Object) ($paths | Sort-Object)).Count){throw 'Incomplete consolidation evidence'}
    foreach($item in $run.evidence){if((Hash (Join-Path $owned $item.path)) -ne $item.sha256){throw "Changed consolidation evidence: $($item.path)"}}
    $expectedCode=[IO.File]::ReadAllText((Join-Path $repo $advanced))
    if($run.kind -eq 'zero-extension'){$expectedCode=$expectedCode.Replace($before,$after)}
    if([IO.File]::ReadAllText((Join-Path $owned $advanced)) -cne $expectedCode){throw 'Wrong production mutation scope'}
    [xml]$trx=Get-Content (Join-Path $owned 'audit.trx') -Raw
    $tests=@($trx.TestRun.Results.UnitTestResult)
    if($tests.Count -ne 9 -or @(Compare-Object ($tests.testName | Sort-Object) ($names | Sort-Object)).Count -or
        @($tests.testId | Sort-Object -Unique).Count -ne 9){throw 'Incomplete consolidation execution roster'}
    $failed=if($run.kind -eq 'clean'){@()}else{@($names | Where-Object{$_ -eq $legacy -or ($_ -notmatch 'modelId: "68000"|modelId: "68010"')})}
    $actualFailed=@($tests | Where-Object outcome -eq Failed | ForEach-Object{$_.testName})
    if((($actualFailed | Sort-Object) -join "`n") -cne (($failed | Sort-Object) -join "`n") -or
        @($tests | Where-Object outcome -notin @('Passed','Failed')).Count -or $run.exit -ne [int]($run.kind -eq 'zero-extension')){throw 'Wrong consolidation failure roster'}
    if($run.kind -eq 'zero-extension'){
        $message=($tests | Where-Object testName -eq $legacy).Output.ErrorInfo.Message
        if($message -notmatch 'Expected: 4294967168' -or $message -notmatch 'Actual:\s+128'){throw 'Wrong historical EXTB mutation diagnostic'}
    }
    foreach($model in $models){
        $r=Get-Content (Join-Path $owned "$model-transfer-registers.json") -Raw | ConvertFrom-Json
        $mismatch=if($run.kind -eq 'zero-extension' -and $model -notin @('68000','68010')){1024}else{0}
        if($r.model -ne $model -or $r.group -ne 'transfer-registers' -or $r.logicalCases -ne 17624 -or $r.xunitBatches -ne 1 -or
            $r.counts.passing -ne 17624-$mismatch -or $r.counts.mismatching -ne $mismatch -or $r.counts.unsupported -ne 0 -or $r.counts.untested -ne 0 -or $r.failures.Count -ne $mismatch){throw "Wrong transfer proof counts: $model/$($run.kind)"}
        $keys=@{}
        foreach($reg in 0..7){
            $keys["$model/MOVEQ/L/imm8->D$reg/all-encodings"] = 411
            foreach($family in @('EXT.W','EXT.L','EXTB.L','SWAP')){$keys["$model/$family/D$reg/boundary-ccr"] = 256}
        }
        foreach($kind in @('40','48','88')){foreach($source in 0..7){foreach($destination in 0..7){$keys["$model/EXG/L/kind=$kind/r$source->r$destination/preserve-flags"] = 32}}}
        if(@($r.combinations.psobject.Properties).Count -ne 232){throw 'Incomplete transfer combination inventory'}
        foreach($combination in $r.combinations.psobject.Properties){
            if(-not $keys.ContainsKey($combination.Name)){throw 'Foreign transfer combination'}
            $bad=if($mismatch -and $combination.Name.Contains('/EXTB.L/')){128}else{0}
            $states=@($combination.Value.psobject.Properties)
            if($states.Count -ne (1+[int]($bad -gt 0)) -or $combination.Value.passing -ne $keys[$combination.Name]-$bad -or
                ($bad -gt 0 -and $combination.Value.mismatching -ne $bad)){throw 'Wrong transfer combination weight/status'}
        }
        if($mismatch){
            if(@($r.failures | Where-Object{$_.status -ne 'mismatching' -or $_.id -notmatch '/EXTB.L/' -or $_.reason -notin @('SR expected 2708, actual 2700, mask=FFFF','SR expected 2718, actual 2710, mask=FFFF')}).Count){throw 'Wrong EXTB mutation diagnostics'}
            $case="$model/EXTB.L/D0/boundary-ccr/op=49C0/v=00000080/ccr=00"
            if(@($r.failures | Where-Object{$_.id -eq $case -and $_.reason -eq 'SR expected 2708, actual 2700, mask=FFFF'}).Count -ne 1){throw 'Missing exact legacy replacement failure'}
        }
    }
}
@{schema=1;historicalRegressionDetected=$true;replacementDetected=$true;cleanExecutions=9;mutationExecutions=9;passingCleanScenarios=140992;mutatedMismatches=6144;retiredMethod=$legacyName;replacementCase='68020/EXTB.L/D0/boundary-ccr/op=49C0/v=00000080/ccr=00';inputManifestSha256=(Hash $manifestPath);roadmapComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'verification.json')
Write-Host 'EXTB consolidation proven: original and exact replacement detect zero extension; all eight clean profiles pass.'
