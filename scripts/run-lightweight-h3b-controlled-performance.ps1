param(
    [Parameter(Mandatory = $true)]
    [string]$CandidateDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ReferenceDirectory,
    [int]$WarmupFrames = 600,
    [int]$MeasuredFrames = 3600,
    [int]$SamplesPerBuild = 3,
    [int]$LogicalProcessor = 2,
    [int]$ExpectedEfficiencyClass = 1,
    [double]$MinimumActiveFps = 200.0,
    [double]$MinimumInactiveRetentionPercent = 97.0,
    [double]$MinimumActiveRetentionPercent = 70.0,
    [double]$MaximumSpreadPercent = 10.0,
    [int]$PreflightSeconds = 10,
    [int]$CooldownSeconds = 10,
    [double]$CompetingCorePercent = 25.0,
    [double]$CompetingPackagePercent = 25.0,
    [int]$SustainedContentionSeconds = 10
)

$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'run-lightweight-h2-controlled-performance.ps1'
& $gate `
    -CandidateDirectory $CandidateDirectory `
    -ReferenceDirectory $ReferenceDirectory `
    -GateName H3b `
    -ActiveWorkload '--synthetic-blitter-fill' `
    -WarmupFrames $WarmupFrames `
    -MeasuredFrames $MeasuredFrames `
    -SamplesPerBuild $SamplesPerBuild `
    -LogicalProcessor $LogicalProcessor `
    -ExpectedEfficiencyClass $ExpectedEfficiencyClass `
    -MinimumActiveFps $MinimumActiveFps `
    -MinimumInactiveRetentionPercent $MinimumInactiveRetentionPercent `
    -MinimumActiveRetentionPercent $MinimumActiveRetentionPercent `
    -MaximumSpreadPercent $MaximumSpreadPercent `
    -PreflightSeconds $PreflightSeconds `
    -CooldownSeconds $CooldownSeconds `
    -CompetingCorePercent $CompetingCorePercent `
    -CompetingPackagePercent $CompetingPackagePercent `
    -SustainedContentionSeconds $SustainedContentionSeconds
exit $LASTEXITCODE
