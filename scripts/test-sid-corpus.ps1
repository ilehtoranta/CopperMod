param(
    [Parameter(Mandatory = $true)][string]$CorpusRoot
)
$ErrorActionPreference = 'Stop'
$sidRepoRoot = Split-Path -Parent $PSScriptRoot
$sidCorpusPath = [IO.Path]::GetFullPath($CorpusRoot)
if (-not (Test-Path -LiteralPath $sidCorpusPath -PathType Container)) {
    throw "SID corpus directory is missing: $sidCorpusPath"
}
$sidPreviousCorpus = $env:SID_CORPUS_ROOT
$sidPreviousTests = $env:SID_CORPUS_TESTS
try {
    $env:SID_CORPUS_ROOT = $sidCorpusPath
    $env:SID_CORPUS_TESTS = '1'
    dotnet test (Join-Path $sidRepoRoot 'CopperMod.Sid.Tests/CopperMod.Sid.Tests.csproj') --configuration Release --logger 'trx;LogFileName=sid-corpus.trx' --results-directory (Join-Path $sidRepoRoot 'artifacts/sid-corpus')
    $sidSuiteExitCode = $LASTEXITCODE
    dotnet test (Join-Path $sidRepoRoot 'CopperMod.Tools.Tests/CopperMod.Tools.Tests.csproj') --configuration Release --filter 'FullyQualifiedName~RendersSidFixtureWithExplicitSecondsDespiteUnknownDuration' --logger 'trx;LogFileName=sid-corpus-cli.trx' --results-directory (Join-Path $sidRepoRoot 'artifacts/sid-corpus')
    if ($sidSuiteExitCode -ne 0 -or $LASTEXITCODE -ne 0) { throw 'SID corpus verification failed. See artifacts/sid-corpus.' }
} finally {
    $env:SID_CORPUS_ROOT = $sidPreviousCorpus
    $env:SID_CORPUS_TESTS = $sidPreviousTests
}
