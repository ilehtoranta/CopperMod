[CmdletBinding()]
param(
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Release",

	[string]$Runtime = "win-x64",

	[switch]$FrameworkDependent,

	[string]$OutputPath
)

$ErrorActionPreference = "Stop"

$copperPadRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $copperPadRoot "src\CopperPad.Gui\CopperPad.Gui.csproj"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
	$OutputPath = Join-Path $copperPadRoot "artifacts\gui\$Runtime"
}

$selfContained = -not $FrameworkDependent.IsPresent
$selfContainedValue = $selfContained.ToString().ToLowerInvariant()

$publishArgs = @(
	"publish",
	$projectPath,
	"-c",
	$Configuration,
	"-r",
	$Runtime,
	"-o",
	$OutputPath,
	"--self-contained:$selfContainedValue",
	"/p:PublishSingleFile=true",
	"/p:IncludeNativeLibrariesForSelfExtract=true"
)

Write-Host "Publishing CopperPad.Gui for $Runtime..."
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
	throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executableName = if ($Runtime.StartsWith("win", [StringComparison]::OrdinalIgnoreCase)) {
	"CopperPad.Gui.exe"
}
else {
	"CopperPad.Gui"
}

$executablePath = Join-Path $OutputPath $executableName
if (-not (Test-Path -LiteralPath $executablePath)) {
	throw "Published executable was not found: $executablePath"
}

Write-Host "Running CopperPad.Gui smoke test..."
$startInfo = [System.Diagnostics.ProcessStartInfo]::new($executablePath)
$startInfo.Arguments = "--smoke-test"
$startInfo.UseShellExecute = $false
$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
	throw "CopperPad.Gui smoke test process could not be started."
}

$process.WaitForExit()
if ($process.ExitCode -ne 0) {
	throw "CopperPad.Gui smoke test failed with exit code $($process.ExitCode). Check the app crash log under the per-user CopperMod\CopperPad app-data folder."
}

if (-not (Test-Path -LiteralPath (Join-Path $OutputPath "README.md"))) {
	throw "Published README.md was not found."
}

if (-not (Test-Path -LiteralPath (Join-Path $OutputPath "LICENSE"))) {
	throw "Published LICENSE was not found."
}

if (-not (Test-Path -LiteralPath (Join-Path $OutputPath "THIRD-PARTY-NOTICES.md"))) {
	throw "Published THIRD-PARTY-NOTICES.md was not found."
}

$zipPath = Join-Path (Join-Path $copperPadRoot "artifacts") "CopperPad.Gui-$Runtime.zip"
Push-Location $OutputPath
try {
	$zipEntries = @(
		$executableName,
		"README.md",
		"LICENSE",
		"THIRD-PARTY-NOTICES.md",
		"third-party"
	)
	Compress-Archive -Path $zipEntries -DestinationPath $zipPath -Force -ErrorAction Stop
}
finally {
	Pop-Location
}

Write-Host "CopperPad.Gui smoke test passed: $executablePath"
Write-Host "CopperPad.Gui release zip: $zipPath"
