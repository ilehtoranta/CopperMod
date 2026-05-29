param(
    [string]$Version = "1.0.0",
    [switch]$TagOnly
)

$ErrorActionPreference = "Stop"
$tag = "coppermod-v$Version"
$out = ".\artifacts\release\CopperMod"

function Get-ReleaseAssets {
    if (-not (Test-Path $out)) {
        throw "Release artifacts were not found at '$out'. Run .\publish-coppermod.ps1 -Version $Version first."
    }

    $zipAssets = @(Get-ChildItem $out -Filter *.zip -File | ForEach-Object { $_.FullName })
    if ($zipAssets.Count -eq 0) {
        throw "No zip assets were found at '$out'. Run .\publish-coppermod.ps1 -Version $Version first."
    }

    $checksum = Join-Path $out "SHA256SUMS.txt"
    if (-not (Test-Path $checksum)) {
        throw "Missing checksum file '$checksum'. Run .\publish-coppermod.ps1 -Version $Version first."
    }

    return $zipAssets + $checksum
}

function Ensure-TagPushed {
    $localTag = git tag --list $tag
    if ($localTag) {
        Write-Host "Tag $tag already exists locally."
    }
    else {
        git tag $tag
    }

    $remoteTag = git ls-remote --tags origin "refs/tags/$tag"
    if ($remoteTag) {
        Write-Host "Tag $tag already exists on origin."
    }
    else {
        git push origin $tag
    }
}

function Write-ManualInstructions($assets) {
    Write-Host ""
    Write-Host "Create the GitHub release manually:"
    Write-Host "  1. Open https://github.com/ilehtoranta/CopperMod/releases/new"
    Write-Host "  2. Choose tag: $tag"
    Write-Host "  3. Title: CopperMod $Version"
    Write-Host "  4. Upload these files:"
    foreach ($asset in $assets) {
        Write-Host "     $asset"
    }
}

$assets = Get-ReleaseAssets
if ($TagOnly) {
    Ensure-TagPushed
    Write-ManualInstructions $assets
    exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    Write-Host "GitHub CLI 'gh' was not found."
    Write-Host "Install it with: winget install --id GitHub.cli -e"
    Write-Host "Or rerun with -TagOnly and create the release in GitHub's web UI."
    Write-ManualInstructions $assets
    exit 1
}

Ensure-TagPushed

gh release create $tag @assets `
    --title "CopperMod $Version" `
    --notes "CopperMod binary release."
