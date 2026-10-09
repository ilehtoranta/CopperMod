#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $InputDirectory,
    [string] $NativeLibrary,
    [string] $OutputDirectory = 'artifacts/winuae-trap-bounds-audit'
)
# Keep the existing command and its complete selection contract.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'test-copper68k-winuae-qualified-exceptions.ps1') -Preset TrapBounds -InputDirectory $InputDirectory -NativeLibrary $NativeLibrary -OutputDirectory $OutputDirectory
