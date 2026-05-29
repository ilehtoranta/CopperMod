param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$out = ".\artifacts\release\CopperScreen"

function Remove-DebugFiles($Path) {
    Get-ChildItem $Path -Recurse -Include *.pdb,*.xml -File |
        Remove-Item -Force
}

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item $out -ItemType Directory | Out-Null

$winOut = Join-Path $out "CopperScreen-win-x64-self-contained"
dotnet publish .\CopperScreen\CopperScreen.csproj `
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
    (Join-Path $out "CopperScreen-$Version-win-x64-self-contained.zip")

$dotnetOut = Join-Path $out "CopperScreen-dotnet"
dotnet publish .\CopperScreen\CopperScreen.csproj `
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
    (Join-Path $out "CopperScreen-$Version-dotnet.zip")

Get-FileHash "$out\*.zip" -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $(Split-Path $_.Path -Leaf)" } |
    Set-Content "$out\SHA256SUMS.txt"
