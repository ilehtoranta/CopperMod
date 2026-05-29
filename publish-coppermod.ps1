param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$out = ".\artifacts\release\CopperMod"

function Remove-DebugFiles($Path) {
    Get-ChildItem $Path -Recurse -Include *.pdb,*.xml -File |
        Remove-Item -Force
}

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item $out -ItemType Directory | Out-Null

$winOut = Join-Path $out "CopperMod-win-x64-self-contained"
dotnet publish .\CopperMod\CopperMod.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:GenerateDocumentationFile=false `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $winOut
Remove-DebugFiles $winOut
Compress-Archive "$winOut\*" `
    (Join-Path $out "CopperMod-$Version-win-x64-self-contained.zip")

$dotnetOut = Join-Path $out "CopperMod-dotnet"
dotnet publish .\CopperMod\CopperMod.csproj `
    -c Release `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:GenerateDocumentationFile=false `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:UseAppHost=false `
    -o $dotnetOut
Remove-DebugFiles $dotnetOut
Compress-Archive "$dotnetOut\*" `
    (Join-Path $out "CopperMod-$Version-dotnet.zip")

Get-FileHash "$out\*.zip" -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $(Split-Path $_.Path -Leaf)" } |
    Set-Content "$out\SHA256SUMS.txt"
