param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\third_party\vAmigaTS")
)

$ErrorActionPreference = "Stop"

$resolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$parent = Split-Path -Parent $resolvedDestination
if (-not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent | Out-Null
}

if (Test-Path -LiteralPath (Join-Path $resolvedDestination ".git")) {
    git -C $resolvedDestination pull --ff-only
} elseif (Test-Path -LiteralPath $resolvedDestination) {
    throw "Destination exists but is not a Git checkout: $resolvedDestination"
} else {
    git clone --depth 1 https://github.com/dirkwhoffmann/vAmigaTS.git $resolvedDestination
}

Write-Host "vAmigaTS root: $resolvedDestination"
Write-Host "Set COPPER_AMIGA_VAMIGATS_ROOT=$resolvedDestination to run the optional corpus tests."
