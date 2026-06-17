param(
    [string] $Configuration = "Release",
    [string] $OutputDirectory = ".\artifacts\packages",
    [string] $Version,
    [switch] $NoRestore,
    [switch] $SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "CopperDisk\CopperDisk.csproj"
$testProject = Join-Path $repoRoot "CopperDisk.Tests\CopperDisk.Tests.csproj"

if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $packageDir = $OutputDirectory
}
else {
    $packageDir = Join-Path $repoRoot $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

if (-not $NoRestore) {
    dotnet restore $project
    dotnet restore $testProject
}

dotnet build $project `
    -c $Configuration `
    --no-restore

if (-not $SkipTests) {
    dotnet test $testProject `
        -c $Configuration `
        --no-restore
}

$packProperties = @("/p:EnablePackageValidation=true")
if ($Version) {
    $packProperties += "/p:PackageVersion=$Version"
}

dotnet pack $project `
    -c $Configuration `
    --no-restore `
    -o $packageDir `
    @packProperties

if (-not $Version) {
    [xml] $projectXml = Get-Content $project
    $Version = ($projectXml.Project.PropertyGroup |
        Where-Object { $_.PackageVersion } |
        Select-Object -First 1).PackageVersion
}

$packagePath = Join-Path $packageDir "CopperDisk.$Version.nupkg"
$symbolPackagePath = Join-Path $packageDir "CopperDisk.$Version.snupkg"

if (-not (Test-Path $packagePath)) {
    throw "Package was not created at '$packagePath'."
}

Write-Host ""
Write-Host "Created package:"
Write-Host "  $packagePath"
if (Test-Path $symbolPackagePath) {
    Write-Host "  $symbolPackagePath"
}
