[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $OutputDirectory = ".\artifacts\packages",
    [string] $Version,
    [switch] $NoRestore,
    [switch] $SkipTests
)

$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "Copper6510\Copper6510.csproj"
$testProject = Join-Path $repoRoot "Copper6510.Tests\Copper6510.Tests.csproj"

if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $packageDirectory = $OutputDirectory
}
else {
    $packageDirectory = Join-Path $repoRoot $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null

if (-not $NoRestore) {
    Invoke-DotNet -Arguments @("restore", $project)
    Invoke-DotNet -Arguments @("restore", $testProject)
}

Invoke-DotNet -Arguments @("build", $project, "-c", $Configuration, "--no-restore")
if (-not $SkipTests) {
    Invoke-DotNet -Arguments @("test", $testProject, "-c", $Configuration, "--no-restore")
}

$packProperties = @("/p:EnablePackageValidation=true")
if ($Version) {
    $packProperties += "/p:PackageVersion=$Version"
}

Invoke-DotNet -Arguments (@("pack", $project, "-c", $Configuration, "--no-restore", "-o", $packageDirectory) + $packProperties)

if (-not $Version) {
    [xml] $projectXml = Get-Content $project
    $Version = ($projectXml.Project.PropertyGroup |
        Where-Object { $_.PackageVersion } |
        Select-Object -First 1).PackageVersion
}

$packagePath = Join-Path $packageDirectory "Copper6510.$Version.nupkg"
$symbolPackagePath = Join-Path $packageDirectory "Copper6510.$Version.snupkg"
if (-not (Test-Path $packagePath)) {
    throw "Package was not created at '$packagePath'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    foreach ($entryName in @(
        "README.md",
        "copper6510-icon.png",
        "lib/net10.0/Copper6510.dll",
        "lib/net10.0/Copper6510.xml")) {
        if (-not $zip.GetEntry($entryName)) {
            throw "Package '$packagePath' is missing required entry '$entryName'."
        }
    }

    $readmeReader = New-Object System.IO.StreamReader($zip.GetEntry("README.md").Open())
    try {
        if ($readmeReader.ReadLine() -ne "# Copper6510") {
            throw "Package README does not start with '# Copper6510'."
        }
    }
    finally {
        $readmeReader.Dispose()
    }

    $nuspecEntry = $zip.Entries |
        Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if (-not $nuspecEntry) {
        throw "Package '$packagePath' is missing a nuspec entry."
    }

    $nuspecReader = New-Object System.IO.StreamReader($nuspecEntry.Open())
    try {
        [xml] $nuspec = $nuspecReader.ReadToEnd()
    }
    finally {
        $nuspecReader.Dispose()
    }

    $metadata = $nuspec.package.metadata
    if (-not $metadata.releaseNotes) {
        throw "Package '$packagePath' is missing release notes."
    }
    if ($metadata.repository.type -ne "git" -or
        -not $metadata.repository.url -or
        -not $metadata.repository.commit) {
        throw "Package '$packagePath' is missing complete repository metadata."
    }
}
finally {
    $zip.Dispose()
}

if (Test-Path $symbolPackagePath) {
    $symbolZip = [System.IO.Compression.ZipFile]::OpenRead($symbolPackagePath)
    try {
        if (-not $symbolZip.GetEntry("lib/net10.0/Copper6510.pdb")) {
            throw "Symbol package '$symbolPackagePath' is missing 'lib/net10.0/Copper6510.pdb'."
        }
    }
    finally {
        $symbolZip.Dispose()
    }
}

Write-Host "Validated Copper6510 package: $packagePath"
