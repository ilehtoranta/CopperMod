[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [string] $ApiKey = $env:NUGET_API_KEY,
    [string] $Source = "https://api.nuget.org/v3/index.json",
    [switch] $NoSymbols,
    [switch] $NoSkipDuplicate
)

$ErrorActionPreference = "Stop"
$cmdlet = $PSCmdlet

$resolvedPackage = Resolve-Path -LiteralPath $PackagePath
$packageFile = Get-Item -LiteralPath $resolvedPackage.Path

if ($packageFile.Extension -ne ".nupkg") {
    throw "PackagePath must point to a .nupkg file."
}

if ($packageFile.Name.EndsWith(".symbols.nupkg", [StringComparison]::OrdinalIgnoreCase)) {
    throw "PackagePath must point to the main .nupkg package, not a symbols package."
}

if (-not $packageFile.Name.StartsWith("Copper68k.", [StringComparison]::OrdinalIgnoreCase)) {
    throw "PackagePath must point to a Copper68k package."
}

if (-not $ApiKey -and -not $WhatIfPreference) {
    throw "NuGet API key is required. Pass -ApiKey or set the NUGET_API_KEY environment variable."
}

if (-not $ApiKey) {
    $ApiKey = "WHATIF"
}

function Invoke-NuGetPush {
    param(
        [string] $Path,
        [string] $Label
    )

    $arguments = @(
        "nuget",
        "push",
        $Path,
        "--api-key",
        $ApiKey,
        "--source",
        $Source
    )

    if ($Path.EndsWith(".nupkg", [StringComparison]::OrdinalIgnoreCase)) {
        $arguments += "--no-symbols"
    }

    if (-not $NoSkipDuplicate) {
        $arguments += "--skip-duplicate"
    }

    if ($cmdlet.ShouldProcess($Path, "Publish $Label to $Source")) {
        & dotnet @arguments
    }
}

Invoke-NuGetPush -Path $packageFile.FullName -Label "Copper68k package"

if (-not $NoSymbols) {
    $symbolPackagePath = [System.IO.Path]::ChangeExtension($packageFile.FullName, ".snupkg")
    if (Test-Path $symbolPackagePath) {
        Invoke-NuGetPush -Path $symbolPackagePath -Label "Copper68k symbol package"
    }
    else {
        Write-Host "No matching symbol package found at '$symbolPackagePath'."
    }
}
