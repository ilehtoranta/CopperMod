[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,
    [Parameter(Mandatory = $true)]
    [string]$UnitPrefix,
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$ExpectedNative,
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$ExpectedFocused,
    [ValidateRange(0, 2147483647)]
    [int]$ExpectedFullScaffold = 0
)

# Read-only functional regression evidence audit. This neither runs tests nor
# evaluates the independently controlled G6/G7 host-throughput gates.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-TestRows([string]$FileName) {
    $trxPath = Join-Path $ResultsDirectory $FileName
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $rows = @($trx.TestRun.Results.UnitTestResult)
    if ($rows.Count -ne [int]$trx.TestRun.ResultSummary.Counters.total) {
        throw "TRX row/summary mismatch: $trxPath"
    }
    return $rows
}

function Assert-Gate([string]$Name, [object[]]$Rows, [int]$Expected) {
    $nonPassing = @($Rows | Where-Object { $_.outcome -ne 'Passed' })
    [pscustomobject]@{
        Gate = $Name
        Count = $Rows.Count
        NonPassing = $nonPassing.Count
    } | ConvertTo-Json -Compress
    if ($Rows.Count -ne $Expected -or $nonPassing.Count -ne 0) {
        throw "Unexpected $Name result; expected $Expected passing cases."
    }
}

$nativeRows = @(Read-TestRows "$UnitPrefix-native-green.trx")
$supportRows = @(Read-TestRows "$UnitPrefix-support-green.trx")
$focusedRows = @(Read-TestRows "$UnitPrefix-focused-green.trx")
Assert-Gate 'NativeGraphics' $nativeRows $ExpectedNative
Assert-Gate 'Supporting union' $supportRows 339
Assert-Gate 'Focused unit plus branch' $focusedRows $ExpectedFocused

$bitmapRows = @($nativeRows | Where-Object {
    $_.testName -match 'NativeGraphicsBitMap(ExecAbiTests|PublicAbiTests|OwnershipTests|SpanTests|DimensionTests|AllocationFlagsTests)|NativeGraphics(SinglePlane|TwoPlane|FourPlane|EightPlane)BitMapAllocator|NativeGraphicsCodeBuildAuditsAllSignedWordBranches'
})
Assert-Gate 'All bitmap plus branch' $bitmapRows 424

$combinedRows = @($nativeRows | Where-Object {
    ($_.testName -like '*NativeGraphics*' -and $_.testName -match 'ColorMap|Raster|Region') -or
        $_.testName -like '*NativeGraphicsCodeBuildAuditsAllSignedWordBranches*'
})
Assert-Gate 'Combined Region ColorMap Raster plus branch' $combinedRows 688

$dbufRows = @($supportRows | Where-Object {
    $_.testName -match 'DBufInfo|DoubleBuffer|NativeGraphicsCodeBuildAuditsAllSignedWordBranches'
})
Assert-Gate 'DBufInfo DoubleBuffer plus branch' $dbufRows 153

$broaderRows = @($supportRows | Where-Object {
    $_.testName -like '*GraphicsLibraryPortableScaffoldTests*' -and
        $_.testName -match 'Region|ColorMap|Palette|Raster'
})
Assert-Gate 'Broader scaffold' $broaderRows 178

$providerRows = @($supportRows | Where-Object {
    $_.testName -match 'AllocBitMapAppliesTheEcsWidthLimitButLeavesOcsCompatibilityUnchanged|AllocBitMapUsesReadableFriendStrideAndRejectsMalformedFriend|GraphicsServicesLeavesRtgFriendBitMapAllocationToTheProvider|GraphicsServicesLeavesFriendlessBitMapAllocationToAnActiveProvider|NativeOverlayRoutesOwnedBitMapAllocationAndTailChainsForeignFree|NativeOverlayChainsUnavailableInterleavedAllocationAfterPortableRollback|BitmapLifecycleVectorsUseClassicDAndARegisterAbi|DirectGraphicsServicesBitMapLifecycleHelpersUsePortableBoundary'
})
Assert-Gate 'Provider friend controls' $providerRows 8

$copperRows = @($nativeRows | Where-Object {
    $_.testName -match 'UCopperList|NativeGraphicsCodeBuildAuditsAllSignedWordBranches'
})
Assert-Gate 'User-Copper plus branch' $copperRows 76

if ($ExpectedFullScaffold -ne 0) {
    $scaffoldRows = @(Read-TestRows "$UnitPrefix-scaffold-green.trx")
    Assert-Gate 'Full portable scaffold' $scaffoldRows $ExpectedFullScaffold
}
