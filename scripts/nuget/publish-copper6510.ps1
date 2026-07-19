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

function Invoke-DotNet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$packageFile = Get-Item -LiteralPath (Resolve-Path -LiteralPath $PackagePath)
if ($packageFile.Extension -ne ".nupkg" -or
    $packageFile.Name.EndsWith(".symbols.nupkg", [StringComparison]::OrdinalIgnoreCase) -or
    -not $packageFile.Name.StartsWith("Copper6510.", [StringComparison]::OrdinalIgnoreCase)) {
    throw "PackagePath must point to a Copper6510 main .nupkg package."
}

if (-not $ApiKey -and -not $WhatIfPreference) {
    throw "NuGet API key is required. Pass -ApiKey or set the NUGET_API_KEY environment variable."
}
if (-not $ApiKey) {
    $ApiKey = "WHATIF"
}

$arguments = @(
    "nuget",
    "push",
    $packageFile.FullName,
    "--api-key",
    $ApiKey,
    "--source",
    $Source)
if (-not $NoSkipDuplicate) {
    $arguments += "--skip-duplicate"
}

if ($PSCmdlet.ShouldProcess($packageFile.FullName, "Publish Copper6510 package to $Source")) {
    Invoke-DotNet -Arguments $arguments
}

if (-not $NoSymbols) {
    $symbolPackagePath = [System.IO.Path]::ChangeExtension($packageFile.FullName, ".snupkg")
    if (Test-Path $symbolPackagePath) {
        $symbolArguments = @(
            "nuget",
            "push",
            $symbolPackagePath,
            "--api-key",
            $ApiKey,
            "--source",
            $Source)
        if (-not $NoSkipDuplicate) {
            $symbolArguments += "--skip-duplicate"
        }

        if ($PSCmdlet.ShouldProcess($symbolPackagePath, "Publish Copper6510 symbol package to $Source")) {
            Invoke-DotNet -Arguments $symbolArguments
        }
    }
}
