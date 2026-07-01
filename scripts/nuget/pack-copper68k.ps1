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
$project = Join-Path $repoRoot "Copper68k\Copper68k.csproj"
$copper68kTestProject = Join-Path $repoRoot "Copper68k.Tests\Copper68k.Tests.csproj"
$amigaTestProject = Join-Path $repoRoot "CopperMod.Amiga.Tests\CopperMod.Amiga.Tests.csproj"
$testFilter = "M68k|M68020|M68040"

if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $packageDir = $OutputDirectory
}
else {
    $packageDir = Join-Path $repoRoot $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

if (-not $NoRestore) {
    Invoke-DotNet -Arguments @("restore", $project)
    Invoke-DotNet -Arguments @("restore", $copper68kTestProject)
    Invoke-DotNet -Arguments @("restore", $amigaTestProject)
}

Invoke-DotNet -Arguments @("build", $project, "-c", $Configuration, "--no-restore")

if (-not $SkipTests) {
    Invoke-DotNet -Arguments @("test", $copper68kTestProject, "-c", $Configuration, "--no-restore")
    Invoke-DotNet -Arguments @("test", $amigaTestProject, "-c", $Configuration, "--no-restore", "--filter", $testFilter)
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

$packagePath = Join-Path $packageDir "Copper68k.$Version.nupkg"
$symbolPackagePath = Join-Path $packageDir "Copper68k.$Version.snupkg"

if (-not (Test-Path $packagePath)) {
    throw "Package was not created at '$packagePath'."
}

if (-not $SkipPackageContentValidation) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $requiredEntries = @(
            "README.md",
            "copper68k-icon.png",
            "lib/net10.0/Copper68k.dll",
            "lib/net10.0/Copper68k.xml"
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

        if ($readmeFirstLine -ne "# Copper68k") {
            throw "Package README starts with '$readmeFirstLine', expected '# Copper68k'."
        }

        $xmlReader = New-Object System.IO.StreamReader($zip.GetEntry("lib/net10.0/Copper68k.xml").Open())
        try {
            $xmlText = $xmlReader.ReadToEnd()
        }
        finally {
            $xmlReader.Dispose()
        }

        foreach ($member in @("T:Copper68k.IM68kBus", "T:Copper68k.IM68kCore", "T:Copper68k.M68kCoreFactory")) {
            if (-not $xmlText.Contains($member)) {
                throw "Package XML documentation is missing '$member'."
            }
        }

        foreach ($implementationType in @("T:Copper68k.M68kInterpreter", "T:Copper68k.M68020Interpreter", "T:Copper68k.M68030Interpreter", "T:Copper68k.M68040Interpreter")) {
            if ($xmlText.Contains($implementationType)) {
                throw "Package XML documentation exposes internal implementation type '$implementationType'."
            }
        }

        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "Package '$packagePath' is missing a nuspec entry."
        }

        $nuspecReader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try {
            [xml] $nuspecXml = $nuspecReader.ReadToEnd()
        }
        finally {
            $nuspecReader.Dispose()
        }

        $metadata = $nuspecXml.package.metadata
        if (-not $metadata.releaseNotes) {
            throw "Package '$packagePath' is missing release notes."
        }

        if ($metadata.repository.type -ne "git" -or -not $metadata.repository.url -or -not $metadata.repository.commit) {
            throw "Package '$packagePath' is missing complete repository metadata."
        }
    }
    finally {
        $zip.Dispose()
    }

    if (Test-Path $symbolPackagePath) {
        $symbolZip = [System.IO.Compression.ZipFile]::OpenRead($symbolPackagePath)
        try {
            if (-not $symbolZip.GetEntry("lib/net10.0/Copper68k.pdb")) {
                throw "Symbol package '$symbolPackagePath' is missing 'lib/net10.0/Copper68k.pdb'."
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
