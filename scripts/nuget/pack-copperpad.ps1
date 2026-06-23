param(
    [string] $Configuration = "Release",
    [string] $OutputDirectory = ".\artifacts\packages",
    [string] $Version,
    [switch] $NoRestore,
    [switch] $SkipTests,
    [switch] $SkipPackageContentValidation
)

$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    param(
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "CopperPad\CopperPad.csproj"
$testProject = Join-Path $repoRoot "CopperPad.Tests\CopperPad.Tests.csproj"

if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $packageDir = $OutputDirectory
}
else {
    $packageDir = Join-Path $repoRoot $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

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

$packArguments = @("pack", $project, "-c", $Configuration, "--no-restore", "-o", $packageDir) + $packProperties
Invoke-DotNet -Arguments $packArguments

if (-not $Version) {
    [xml] $projectXml = Get-Content $project
    $Version = ($projectXml.Project.PropertyGroup |
        Where-Object { $_.PackageVersion } |
        Select-Object -First 1).PackageVersion
}

$packagePath = Join-Path $packageDir "CopperPad.$Version.nupkg"
$symbolPackagePath = Join-Path $packageDir "CopperPad.$Version.snupkg"

if (-not (Test-Path $packagePath)) {
    throw "Package was not created at '$packagePath'."
}

if (-not $SkipPackageContentValidation) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $requiredEntries = @(
            "README.md",
            "lib/net10.0/CopperPad.dll",
            "lib/net10.0/CopperPad.xml"
        )

        foreach ($entryName in $requiredEntries) {
            if (-not $zip.GetEntry($entryName)) {
                throw "Package '$packagePath' is missing required entry '$entryName'."
            }
        }

        $readmeReader = New-Object System.IO.StreamReader($zip.GetEntry("README.md").Open())
        try {
            $readmeFirstLine = $readmeReader.ReadLine()
        }
        finally {
            $readmeReader.Dispose()
        }

        if ($readmeFirstLine -ne "# CopperPad") {
            throw "Package README starts with '$readmeFirstLine', expected '# CopperPad'."
        }

        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "Package '$packagePath' is missing a nuspec entry."
        }
    }
    finally {
        $zip.Dispose()
    }

    if (Test-Path $symbolPackagePath) {
        $symbolZip = [System.IO.Compression.ZipFile]::OpenRead($symbolPackagePath)
        try {
            if (-not $symbolZip.GetEntry("lib/net10.0/CopperPad.pdb")) {
                throw "Symbol package '$symbolPackagePath' is missing 'lib/net10.0/CopperPad.pdb'."
            }
        }
        finally {
            $symbolZip.Dispose()
        }
    }
}

Write-Host ""
Write-Host "Created package:"
Write-Host "  $packagePath"
if (Test-Path $symbolPackagePath) {
    Write-Host "  $symbolPackagePath"
}
