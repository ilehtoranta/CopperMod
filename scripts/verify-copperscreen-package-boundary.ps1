[CmdletBinding()]
param(
    [string]$SourceRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('copperscreen-packages-' + [Guid]::NewGuid().ToString('N'))),
    [string]$NativeRom,
    [string]$NativeAdf,
    [string]$NativeScript
)

# Local proof only: never publishes packages or changes a remote repository.
$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Choose a new, empty WorkDirectory.' }
$null = New-Item -ItemType Directory -Path $WorkDirectory
$WorkDirectory = (Resolve-Path -LiteralPath $WorkDirectory).Path
$feed = Join-Path $WorkDirectory 'feed'
$consumer = Join-Path $WorkDirectory 'consumer'
$packages = Join-Path $WorkDirectory 'packages'
$null = New-Item -ItemType Directory -Path $feed, $consumer
$restore = @("-p:RestoreAdditionalProjectSources=$feed", "-p:RestorePackagesPath=$packages")

function Invoke-Dotnet([string]$Label, [string[]]$Arguments) {
    Write-Host $Label
    & dotnet @Arguments *> (Join-Path $WorkDirectory ($Label + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "$Label failed; see $WorkDirectory/$Label.log" }
}

Invoke-Dotnet 'pack-cpu' (@('pack', "$SourceRoot/Copper68k/Copper68k.csproj", '-c', 'Release', '-p:PackageVersion=1.4.1-boundary.1', '-o', $feed) + $restore)
Invoke-Dotnet 'pack-disk' (@('pack', "$SourceRoot/CopperDisk/CopperDisk.csproj", '-c', 'Release', '-p:PackageVersion=2.1.1-boundary.1', '-o', $feed) + $restore)
Invoke-Dotnet 'pack-engine' (@('pack', "$SourceRoot/CopperMod.Amiga.Lightweight/CopperMod.Amiga.Lightweight.csproj", '-c', 'Release', '-p:UseEmulatorPackageDependencies=true', '-p:LightweightDiagnostics=false', '-o', $feed) + $restore)

# Copy the app and its focused tests, never emulator projects or build output.
foreach ($directory in @('CopperScreen', 'CopperScreen.Lightweight.Tests')) {
    $source = Join-Path $SourceRoot $directory
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj|artifacts|\.git)([\\/]|$)') { continue }
        $destination = Join-Path (Join-Path $consumer $directory) $relative
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent)
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
$testSources = Join-Path $consumer 'CopperScreen.Tests'
$null = New-Item -ItemType Directory -Path $testSources
foreach ($file in @('CopperScreenLightweightSessionTests.cs', 'MainWindowPresentationTests.cs', 'FramebufferPresenterTests.cs', 'MiniaudioSampleQueueTests.cs')) {
    Copy-Item -LiteralPath "$SourceRoot/CopperScreen.Tests/$file" -Destination $testSources
}

$native = @($NativeRom, $NativeAdf, $NativeScript)
$provided = @($native | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
if ($provided -ne 0 -and $provided -ne 3) { throw 'Provide all three native replay paths, or none.' }
$names = @('COPPERSCREEN_LIGHTWEIGHT_NATIVE_ROM', 'COPPERSCREEN_LIGHTWEIGHT_NATIVE_ADF', 'COPPERSCREEN_LIGHTWEIGHT_NATIVE_SCRIPT')
$previous = @($names | ForEach-Object { [Environment]::GetEnvironmentVariable($_, 'Process') })
try {
    for ($i = 0; $i -lt 3; $i++) {
        [Environment]::SetEnvironmentVariable($names[$i], $native[$i], 'Process')
    }
    Invoke-Dotnet 'consumer-tests' (@('test', "$consumer/CopperScreen.Lightweight.Tests/CopperScreen.Lightweight.Tests.csproj", '-c', 'Release', '-p:UseEmulatorProjectReferences=false', '--logger', 'trx', '--results-directory', "$WorkDirectory/results") + $restore)
} finally {
    for ($i = 0; $i -lt 3; $i++) { [Environment]::SetEnvironmentVariable($names[$i], $previous[$i], 'Process') }
}

$assets = Get-Content -LiteralPath "$consumer/CopperScreen/obj/project.assets.json" -Raw | ConvertFrom-Json
$libraries = $assets.libraries.PSObject.Properties
if (@($libraries | Where-Object { $_.Value.type -eq 'project' }).Count -ne 0) { throw 'Consumer restored an emulator project, not a package.' }
foreach ($name in @('Copper68k/', 'CopperDisk/', 'CopperMod.Amiga.Lightweight/')) {
    if (@($libraries | Where-Object { $_.Name.StartsWith($name) -and $_.Value.type -eq 'package' }).Count -ne 1) {
        throw "Expected exactly one package dependency for $name"
    }
}
$forbidden = @('CopperMod.Amiga.dll', 'CopperMod.Amiga.CyberGraphics.dll', 'CopperMod.Amiga.Emulator.dll', 'CopperSharp.Sdk.Amiga.Support.dll')
foreach ($file in Get-ChildItem -LiteralPath "$consumer/CopperScreen/bin/Release/net10.0" -File) {
    if ($file.Name -in $forbidden -or $file.Name.StartsWith('CopperStart')) { throw "Forbidden host dependency: $($file.Name)" }
}
Write-Host "PASS: package-only consumer and focused tests. Evidence: $WorkDirectory"
if ($provided -eq 0) { Write-Host 'Native replay was not requested; its two tests are skipped.' }
