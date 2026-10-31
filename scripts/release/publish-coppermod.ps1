param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.1.0')

# Build local archives; publishing and tagging are separate operations.
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release\CopperMod'))
$out = [IO.Path]::GetFullPath((Join-Path $releaseRoot $Version))
if (-not $out.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must stay inside artifacts/release/CopperMod.'
}
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -Path $out -ItemType Directory -Force | Out-Null
$project = Join-Path $repoRoot 'CopperMod\CopperMod.csproj'

function Copy-DependencyNotices([string]$Destination) {
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot 'CopperMod\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
    $cache = @($assets.packageFolders.Keys)[0]
    $noticeRoot = Join-Path $Destination 'licenses'
    New-Item -ItemType Directory -Force -Path $noticeRoot | Out-Null
    $index = foreach ($entry in $assets.libraries.GetEnumerator() | Sort-Object Key) {
        if ($entry.Value.type -ne 'package') { continue }
        $packageRoot = Join-Path $cache $entry.Value.path
        $notice = Join-Path $noticeRoot ($entry.Key.Replace('/', '-'))
        $licenseFiles = Get-ChildItem -LiteralPath $packageRoot -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD-PARTY-NOTICES)(\.|$)' }
        if ($licenseFiles) {
            New-Item -ItemType Directory -Force -Path $notice | Out-Null
            $licenseFiles | Copy-Item -Destination $notice
        }
        $nuspec = Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' -File | Select-Object -First 1
        if ($nuspec) {
            [xml]$spec = Get-Content -LiteralPath $nuspec.FullName -Raw
            "$($entry.Key) | $($spec.package.metadata.authors) | $($spec.package.metadata.license.InnerText) | $($spec.package.metadata.projectUrl)"
        }
    }
    $index | Set-Content -LiteralPath (Join-Path $Destination 'DEPENDENCIES.txt')
    $runtimeRoot = Join-Path $cache 'microsoft.netcore.app.runtime.win-x64\10.0.13'
    if (Test-Path -LiteralPath $runtimeRoot) {
        $notice = Join-Path $noticeRoot 'dotnet-runtime-10.0.13'
        New-Item -ItemType Directory -Force -Path $notice | Out-Null
        Copy-Item -LiteralPath (Join-Path $runtimeRoot 'LICENSE.TXT'), (Join-Path $runtimeRoot 'THIRD-PARTY-NOTICES.TXT') -Destination $notice
    }
}

foreach ($flavor in 'win-x64-self-contained', 'dotnet') {
    $destination = Join-Path $out "CopperMod-$flavor"
    $arguments = @('publish', $project, '-c', 'Release', "-p:Version=$Version",
        '-p:DebugType=None', '-p:DebugSymbols=false', '-p:GenerateDocumentationFile=false',
        '-p:PublishTrimmed=false', '-o', $destination)
    if ($flavor -eq 'win-x64-self-contained') {
        $arguments += @('-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:RuntimeFrameworkVersion=10.0.13')
    }
    else {
        $arguments += @('--self-contained', 'false', '-p:PublishSingleFile=false', '-p:UseAppHost=false')
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $flavor." }
    Get-ChildItem -LiteralPath $destination -Recurse -File |
        Where-Object Extension -in '.pdb', '.xml' | Remove-Item -Force
    foreach ($document in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md') {
        Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\release-notes\CopperMod-1.1.0.md') -Destination (Join-Path $destination 'RELEASE-NOTES.md')
    Copy-DependencyNotices $destination
    $files = Get-ChildItem -LiteralPath $destination -Recurse -File
    $forbidden = @($files | Where-Object {
        $_.Extension -in '.rom', '.bin', '.adf', '.hdf', '.ipf', '.sam', '.tfx', '.wav' -or
        $_.Name -match 'CopperStart|SystemRom|DiskBoot|^TFMX(_|$)'
    })
    if ($forbidden.Count) { throw "Private ROM, media or replay binary found in release: $($forbidden.FullName -join ', ')" }
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath (Join-Path $out "CopperMod-$Version-$flavor.zip")
}
Get-FileHash (Join-Path $out '*.zip') -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $(Split-Path $_.Path -Leaf)" } |
    Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt')
Write-Host "Prepared release archives: $out"
