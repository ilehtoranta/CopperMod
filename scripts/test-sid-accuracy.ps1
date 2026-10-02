param(
    [Parameter(Mandatory = $true)][string]$EvidenceRoot
)
$ErrorActionPreference = 'Stop'
$sidRepoRoot = Split-Path -Parent $PSScriptRoot
$sidEvidencePath = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not (Test-Path -LiteralPath (Join-Path $sidEvidencePath 'manifest.json'))) {
    throw "Required cycle-indexed SID hardware evidence is missing: $sidEvidencePath"
}
$sidPreviousRequired = $env:SID_ACCURACY_REQUIRED
$sidPreviousEvidence = $env:SID_HARDWARE_EVIDENCE_ROOT
try {
    $env:SID_ACCURACY_REQUIRED = '1'
    $env:SID_HARDWARE_EVIDENCE_ROOT = $sidEvidencePath
    dotnet test (Join-Path $sidRepoRoot 'CopperMod.Sid.Core.Tests/CopperMod.Sid.Core.Tests.csproj') --configuration Release --logger 'trx;LogFileName=sid-hardware.trx' --results-directory (Join-Path $sidRepoRoot 'artifacts/sid-accuracy')
    if ($LASTEXITCODE -ne 0) { throw 'SID core/hardware evidence gate failed.' }
    dotnet test (Join-Path $sidRepoRoot 'CopperMod.Sid.Tests/CopperMod.Sid.Tests.csproj') --configuration Release --logger 'trx;LogFileName=sid-external.trx' --results-directory (Join-Path $sidRepoRoot 'artifacts/sid-accuracy')
    if ($LASTEXITCODE -ne 0) { throw 'SID external reference/capture gate failed.' }
} finally {
    $env:SID_ACCURACY_REQUIRED = $sidPreviousRequired
    $env:SID_HARDWARE_EVIDENCE_ROOT = $sidPreviousEvidence
}
