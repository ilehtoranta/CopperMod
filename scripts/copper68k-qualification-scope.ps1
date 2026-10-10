function Get-Copper68kQualificationScope([string]$Repository) {
    $path = Join-Path $Repository 'docs/COPPER68K_QUALIFICATION_SCOPE.json'
    $scope = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($scope.schema -ne 1 -or $scope.acceptance -cne 'known-coverage-only' -or
        ($scope.approvedModels -join ',') -cne '68000,68010,68EC020,68020,68030,68040,68060,A1200' -or
        $scope.approvedCoverage.Count -eq 0 -or $scope.deferred.Count -eq 0) {
        throw 'Missing or incompatible Copper68k qualification scope'
    }
    $ids = @($scope.deferred | ForEach-Object { $_.id })
    if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { throw 'Duplicate deferred qualification ID' }
    $required = @('010-invalid-format-rte-ccr', '060-ordinary-stop-new-user-sr',
        '030-040-disputed-trace-boundaries', 'broader-architectural-restoration',
        'other-private-continuation-candidates')
    if (($ids | Sort-Object) -join ',' -cne (($required | Sort-Object) -join ',')) {
        throw 'Missing or unexpected deferred qualification area'
    }
    foreach ($item in $scope.deferred) {
        if ([string]::IsNullOrWhiteSpace($item.id) -or $item.status -cne 'deferred' -or
            [string]::IsNullOrWhiteSpace($item.reason) -or [string]::IsNullOrWhiteSpace($item.optIn) -or
            -not (Test-Path -LiteralPath (Join-Path $Repository $item.record) -PathType Leaf)) {
            throw "Invalid deferred qualification entry: $($item.id)"
        }
    }
    [ordered]@{
        policySha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        policy = $scope
    }
}
