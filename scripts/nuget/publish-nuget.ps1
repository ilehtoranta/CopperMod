[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [string] $Version = "1.0.0",
    [string] $PackageDir = ".\artifacts\packages",
    [string] $Source = "https://api.nuget.org/v3/index.json",
    [string] $ApiKey = $env:NUGET_API_KEY,
    [switch] $NoSymbols,
    [switch] $SkipTests
)

$ErrorActionPreference = "Stop"
$cmdlet = $PSCmdlet
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if ([System.IO.Path]::IsPathRooted($PackageDir)) {
    $resolvedPackageDir = $PackageDir
}
else {
    $resolvedPackageDir = Join-Path $repoRoot $PackageDir
}

New-Item -ItemType Directory -Force -Path $resolvedPackageDir | Out-Null

if (-not $ApiKey -and -not $WhatIfPreference) {
    $ApiKey = Read-Host "NuGet API key"
}

if (-not $ApiKey) {
    $ApiKey = "WHATIF"
}

if (-not $SkipTests) {
    dotnet test (Join-Path $repoRoot "CopperMod.sln") -c Release
}

$packageProjects = @(
    "CopperMod.Abstractions\CopperMod.Abstractions.csproj",
    "CopperMod.Med\CopperMod.Med.csproj",
    "CopperMod.ProTracker\CopperMod.ProTracker.csproj",
    "CopperMod.Sid\CopperMod.Sid.csproj"
)

foreach ($project in $packageProjects) {
    dotnet pack (Join-Path $repoRoot $project) `
        -c Release `
        -o $resolvedPackageDir `
        --no-restore `
        "/p:PackageVersion=$Version"
}

& (Join-Path $repoRoot "scripts\nuget\pack-copper68k.ps1") `
    -Configuration Release `
    -OutputDirectory $resolvedPackageDir `
    -Version $Version `
    -NoRestore `
    -SkipTests

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

    $arguments += "--skip-duplicate"

    if ($cmdlet.ShouldProcess($Path, "Publish $Label to $Source")) {
        & dotnet @arguments
    }
}

$packages = @(
    "CopperMod.Abstractions.$Version.nupkg",
    "CopperMod.Med.$Version.nupkg",
    "CopperMod.ProTracker.$Version.nupkg",
    "CopperMod.Sid.$Version.nupkg"
)

foreach ($package in $packages) {
    $path = Join-Path $resolvedPackageDir $package

    if (-not (Test-Path $path)) {
        throw "Package not found: $path"
    }

    Invoke-NuGetPush -Path $path -Label $package

    if (-not $NoSymbols) {
        $symbolPath = [System.IO.Path]::ChangeExtension($path, ".snupkg")
        if (Test-Path $symbolPath) {
            Invoke-NuGetPush -Path $symbolPath -Label "$package symbols"
        }
    }
}

$copper68kPackagePath = Join-Path $resolvedPackageDir "Copper68k.$Version.nupkg"
if (-not (Test-Path $copper68kPackagePath)) {
    throw "Package not found: $copper68kPackagePath"
}

$copper68kPublishArguments = @{
    PackagePath = $copper68kPackagePath
    ApiKey = $ApiKey
    Source = $Source
    WhatIf = $WhatIfPreference
}

if ($NoSymbols) {
    $copper68kPublishArguments.NoSymbols = $true
}

& (Join-Path $repoRoot "scripts\nuget\publish-copper68k.ps1") @copper68kPublishArguments
