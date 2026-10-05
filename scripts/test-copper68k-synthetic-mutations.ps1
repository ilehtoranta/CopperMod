[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/synthetic-mutations', [ValidateSet('All','Move','Arithmetic','Logical','Control','Consolidation','Rte040','LowPowerStop')] [string]$Scope = 'All')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if (Test-Path -LiteralPath $output) { $output = Join-Path $output ([guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$advanced = 'Copper68k/M68kAdvancedTimingInterpreter.cs'
$mutations = @(
    @{name='absolute-decode'; file=$advanced; before='(opcode & 0xFFF8) == 0x13D0'; after='(opcode & 0xF1F8) == 0x11D0'; group='EveryLegalMoveOpcode'},
    @{name='extension-length'; file='Copper68k/M68kAdvancedTimingInterpreter.Move.cs'; before='case 0: return unchecked((uint)(int)(short)FetchWord());'; after='case 0: return FetchLong();'; group='EveryLegalMoveOpcode'},
    @{name='index-sign'; file='Copper68k/M68kIntegerSemantics.cs'; before=': unchecked((int)(short)(rawIndex & 0xFFFF));'; after=': unchecked((int)(ushort)(rawIndex & 0xFFFF));'; group='MoveExtensionsAndAliases'},
    @{name='alias-order'; file=$advanced; before=@'
            WriteGeneralRegister(true, sourceRegister, unchecked(address + M68kIntegerSemantics.AddressIncrement(sourceRegister, size)));
            // Resolve the destination after source EA side effects, including An aliasing.
            WriteSized(State.A[(opcode >> 9) & 7], value, size);
'@; after=@'
            WriteSized(State.A[(opcode >> 9) & 7], value, size);
            WriteGeneralRegister(true, sourceRegister, unchecked(address + M68kIntegerSemantics.AddressIncrement(sourceRegister, size)));
'@; group='EveryLegalMoveOpcode'},
    @{name='a7-stride'; file=$advanced; before='WriteGeneralRegister(true, source, State.A[source] + (source == 7 ? 2u : 1u));'; after='WriteGeneralRegister(true, source, State.A[source] + 1u);'; group='EveryLegalMoveOpcode'},
    @{name='move-flags'; file=$advanced; before=@'
        private void SetMoveFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
'@; after=@'
        private void SetMoveFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Zero, false);
'@; group='MoveValueAndConditionCodeBoundaries'}
)
$arithmetic = 'Copper68k/M68kAdvancedTimingInterpreter.Arithmetic.cs'
$mutations += @(
    @{name='arithmetic-overflow'; file=$advanced; before=@'
            var arithmetic = M68kIntegerSemantics.CalculateAddFlags(destination, source, result, size);
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, arithmetic.Overflow);
'@; after=@'
            var arithmetic = M68kIntegerSemantics.CalculateAddFlags(destination, source, result, size);
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, false);
'@; group='ArithmeticBoundariesAndAllConditionCodes'; milestone=3},
    @{name='extend-sticky-zero'; file=$arithmetic; before='        result &= M68kCpuState.Mask(size);'; after='        // Mutant: untruncated result before sticky-zero flags.'; group='ExtendComparisonAndAliases'; milestone=3},
    @{name='decimal-alias-order'; file=$advanced; before=@'
                source = ReadByte(sourceAddress);
                var address = State.A[destinationRegister] - (destinationRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, destinationRegister, address);
                destinationAddress = State.A[destinationRegister];
'@; after=@'
                var address = State.A[destinationRegister] - (destinationRegister == 7 ? 2u : 1u);
                WriteGeneralRegister(true, destinationRegister, address);
                source = ReadByte(State.A[sourceRegister]);
                destinationAddress = State.A[destinationRegister];
'@; group='DecimalAndPacking'; milestone=3},
    @{name='pack-a7-stride'; file=$arithmetic; before=@'
    private byte ReadPredecrementByte(int register)
    {
        var address = unchecked(State.A[register] - (register == 7 ? 2u : 1u));
'@; after=@'
    private byte ReadPredecrementByte(int register)
    {
        var address = unchecked(State.A[register] - 1u);
'@; group='DecimalAndPacking'; milestone=3},
    @{name='060-divide-frame'; file='Copper68k/M68060Interpreter.cs'; before='            if (vector is 5 or 6 or 7 or 9) PushLong(State.LastInstructionProgramCounter);'; after='            // Mutant: missing format-2 instruction address.'; group='MultiplyDivideAddressingAndRegisterAliases'; milestone=3}
)
$mutations += @(
    @{name='bitfield-clear-vc'; file=$advanced; before=@'
            State.SetFlag(M68kCpuState.Zero, field == 0);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
'@; after='            State.SetFlag(M68kCpuState.Zero, field == 0);'; group='BitFieldWidthsOffsetsFlagsAndRegisterAliases'; milestone=4},
    @{name='bitfield-negative-offset'; file=$advanced; before=@'
        private static int FloorDivideBy8(int value)
            => value >> 3;
'@; after=@'
        private static int FloorDivideBy8(int value)
            => value / 8;
'@; group='BitFieldAddressingSignedOffsetsAndFullExtensions'; milestone=4},
    @{name='cas2-dc1-precedence'; file=$advanced; before=@'
                WriteDataRegisterSized(compareRegister2, destination2, size);
                WriteDataRegisterSized(compareRegister1, destination1, size);
'@; after=@'
                WriteDataRegisterSized(compareRegister1, destination1, size);
                WriteDataRegisterSized(compareRegister2, destination2, size);
'@; group='Cas2ComparisonsRegisterFieldsAndAliases'; milestone=4},
    @{name='040-cas2-failed-writeback'; file=$advanced; before='                if (_profile.Model == M68kAcceleratorModel.M68040) WriteSized(address1, destination1, size);'; after='                // Mutant: missing failed-CAS2 writeback.'; group='Cas2ComparisonsRegisterFieldsAndAliases'; milestone=4; model='68040'},
    @{name='chk2-boundary-z'; file=$advanced; before='            State.SetFlag(M68kCpuState.Zero, value == lower || value == upper);'; after='            State.SetFlag(M68kCpuState.Zero, false);'; group='CheckBoundsValuesFlagsFormsAndRegisterAliases'; milestone=5},
    @{name='cmp2-address-width'; file=$advanced; before='            var value = useAddressRegister ? unchecked((int)State.A[register]) : SignExtendForSize(State.D[register], size);'; after='            var value = SignExtendForSize(useAddressRegister ? State.A[register] : State.D[register], size);'; group='CheckBoundsValuesFlagsFormsAndRegisterAliases'; milestone=5},
    @{name='rte-throwaway'; file=$advanced; before='                if (format == 1) continue; // Throwaway frame can select another stack.'; after='                // Mutant: accept throwaway as final frame.'; group='RteFramesPrivilegeStackSelectionAndInvalidFormats'; milestone=5},
    @{name='move16-postincrement'; file='Copper68k/M68kAdvancedTimingInterpreter.System.cs'; before='        if (form == 4 || form < 2) WriteGeneralRegister(true, register, unchecked(State.A[register] + 16));'; after='        if (form == 4 || form < 2) WriteGeneralRegister(true, register, unchecked(State.A[register] + 4));'; group='Move16CacheInstructionsBreakpointsAndLowPowerStop'; milestone=5; model='68040'},
    @{name='cacr-clear-readback'; file=$advanced; before='? value & ~0x0C0Cu : value; // Clear commands always read as zero.'; after='? value : value; // Mutant: clear commands read back set.'; group='MovecControlInventoryMasksPrivilegeAndAllGeneralRegisters'; milestone=5}
)
$mutations += @(
    @{name='000-asl-overflow'; file='Copper68k/M68kCore.cs'; before=@'
            State.SetFlag(M68kCpuState.Overflow, shifted.Overflow);
            return shifted.Value;
'@; after=@'
            State.SetFlag(M68kCpuState.Overflow, false);
            return shifted.Value;
'@; group='ShiftsCountsValuesAndFlags'; milestone=6; model='68000'; legacyTest='AslByteSetsOverflowWhenSignChanges'; legacyFile='Copper68k.Tests/M68kShiftTests.cs'},
    @{name='040-t0-serializers'; file=$advanced; before=@'
                if (_profile.Model == M68kAcceleratorModel.M68040)
                    flow |= traceOpcode is 0x4E71 or 0x4E7A or 0x4E7B ||
'@; after=@'
                if (false)
                    flow |= traceOpcode is 0x4E71 or 0x4E7A or 0x4E7B ||
'@; group='TraceRetirementTakenAndUntakenFlowStopsAndAbortingFaults'; milestone=6; model='68040'},
    @{name='040-throwaway-chain-stop'; file=$advanced; before='                if (format == 1) continue; // Throwaway frame can select another stack.';
      after='                // Mutant: stop before the final chained frame.';
      group='SyntheticM68040ThrowawayTests'; milestone=6; model='68040'; throwaway=$true},
    @{name='040-throwaway-stack-selection'; file=$advanced; before=@'
                State.StatusRegister = restoredStatus;
                if (format == 1) continue; // Throwaway frame can select another stack.
'@; after=@'
                if (format != 1) State.StatusRegister = restoredStatus;
                if (format == 1) continue; // Mutant: retain the old stack selector.
'@; group='SyntheticM68040ThrowawayTests'; milestone=6; model='68040'; throwaway=$true},
    @{name='040-address-error-format'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs'; before='        PushWord(0x200c);'; after='        PushWord(0x000c);';
      group='SyntheticM68040OddReturnTests'; milestone=6; model='68040'; odd=$true},
    @{name='040-address-error-a0'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs'; before='        PushLong(faultAddress & ~1u);'; after='        PushLong(faultAddress);';
      group='SyntheticM68040OddReturnTests'; milestone=6; model='68040'; odd=$true},
    @{name='040-rte-saved-sr-image'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs'; before='        RaiseM68040AddressError(rtePc, target, priorSr);'; after='        RaiseM68040AddressError(rtePc, target, (ushort)(State.StatusRegister | 0x2000));';
      group='SyntheticM68040OddReturnTests'; milestone=6; model='68040'; odd=$true},
    @{name='040-rte-pending-priority'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs'; before='        if (continuation >= 0x2000)';
      after="        if (TryRaiseM68040RteAddressError(pc, priorSr, State.LastInstructionProgramCounter)) return true;`n        if (continuation >= 0x2000)";
      group='SyntheticM68040OddReturnTests'; milestone=6; model='68040'; odd=$true},
    @{name='060-lpstop-linef'; file='Copper68k/M68kAdvancedTimingInterpreter.System.cs'; before=@'
            if (FetchWord() != 0x01c0)
            { RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException); return true; }
'@; after=@'
            if (FetchWord() != 0x01c0)
            { RaiseFormat0Exception(4, pc, M68kInstructionTimingKey.IllegalInstruction); return true; }
'@; group='SyntheticLowPowerStopTests'; milestone=6; model='68060'; lpstop=$true},
    @{name='060-lpstop-privilege-priority'; file='Copper68k/M68kAdvancedTimingInterpreter.System.cs'; before='            if (FetchWord() != 0x01c0)'; after=@'
            if (!State.GetFlag(M68kCpuState.Supervisor))
            { RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation); return true; }
            if (FetchWord() != 0x01c0)
'@; group='SyntheticLowPowerStopTests'; milestone=6; model='68060'; lpstop=$true},
    @{name='060-lpstop-clear-s'; file='Copper68k/M68kAdvancedTimingInterpreter.System.cs'; before='            if ((immediate & M68kCpuState.Supervisor) == 0)';
      after='            if (false)'; group='SyntheticLowPowerStopTests'; milestone=6; model='68060'; lpstop=$true}
)
if ($Scope -eq 'Move') { $mutations = @($mutations | Where-Object { -not $_.milestone }) }
if ($Scope -eq 'Arithmetic') { $mutations = @($mutations | Where-Object { $_.milestone -eq 3 }) }
if ($Scope -eq 'Logical') { $mutations = @($mutations | Where-Object { $_.milestone -eq 4 }) }
if ($Scope -eq 'Control') { $mutations = @($mutations | Where-Object { $_.milestone -eq 5 }) }
if ($Scope -eq 'Consolidation') { $mutations = @($mutations | Where-Object { $_.milestone -eq 6 }) }
if ($Scope -eq 'Rte040') { $mutations = @($mutations | Where-Object { $_.odd }) }
if ($Scope -eq 'LowPowerStop') { $mutations = @($mutations | Where-Object { $_.lpstop }) }
$saved = @{}
foreach ($mutation in $mutations) {
    $path = Join-Path $repo $mutation.file
    if (-not $saved.ContainsKey($path)) { $saved[$path] = [IO.File]::ReadAllBytes($path) }
}
$reportDirectory = [Environment]::GetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR')
$results = @()
Push-Location $repo
try {
    foreach ($mutation in $mutations) {
        $path = Join-Path $repo $mutation.file
        $text = [Text.Encoding]::UTF8.GetString($saved[$path])
        $newline = $(if ($text.Contains("`r`n")) { "`r`n" } else { "`n" })
        $before = $mutation.before.Replace("`r`n", "`n").Replace("`n", $newline)
        $after = $mutation.after.Replace("`r`n", "`n").Replace("`n", $newline)
        if (($text.Split($before, [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Mutation anchor is not unique: $($mutation.name)" }
        $directory = Join-Path $output $mutation.name
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR', $directory)
        try {
            [IO.File]::WriteAllText($path, $text.Replace($before, $after), [Text.UTF8Encoding]::new($false))
            $model = $(if ($mutation.model) { $mutation.model } elseif ($mutation.name -eq '060-divide-frame') { '68060' } else { '68020' })
            $filter = "FullyQualifiedName~$($mutation.group)&DisplayName~$model"
            $legacyPresent = $mutation.legacyTest -and [IO.File]::ReadAllText((Join-Path $repo $mutation.legacyFile)).Contains("void $($mutation.legacyTest)(")
            if ($legacyPresent) { $filter += "|FullyQualifiedName~$($mutation.legacyTest)" }
            & dotnet test Copper68k.Tests/Copper68k.Tests.csproj -c Release --no-restore --filter $filter --logger "trx;LogFileName=mutation.trx" --results-directory $directory *> (Join-Path $directory 'run.log')
            $exitCode = $LASTEXITCODE
            $batches = @(Get-ChildItem -LiteralPath $directory -Filter '*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
            $failures = @($batches | ForEach-Object { $_.failures })
            if ($exitCode -eq 0 -or $batches.Count -eq 0 -or $failures.Count -eq 0) { throw "Mutation survived or failed without executable semantic evidence: $($mutation.name)" }
            if ($mutation.throwaway) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $trx.TestRun.ResultSummary.Counters.failed -ne 2 -or
                    $batches.Count -ne 2 -or @($batches | Where-Object { $_.group -eq 'rte-throwaway-controls' -and $_.logicalCases -eq 82944 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.group -eq 'rte-throwaway-access' -and $_.logicalCases -eq 428544 }).Count -ne 1) {
                    throw 'Throwaway mutation did not execute its complete two-batch selection'
                }
            }
            if ($mutation.odd) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 3 -or $batches.Count -ne 3 -or
                    @($batches | Where-Object { $_.group -eq 'rte-odd-normal' -and $_.logicalCases -eq 69120 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.group -eq 'rte-odd-pending' -and $_.logicalCases -eq 165888 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.group -eq 'address-error-fetch-040' -and $_.logicalCases -eq 2304 }).Count -ne 1) {
                    throw 'Odd-return mutation did not execute its complete three-batch selection'
                }
            }
            if ($mutation.lpstop) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.model -eq '68060' -and $_.group -eq 'system-lpstop-extensions' -and $_.logicalCases -eq 131070 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -eq '68060' -and $_.group -eq 'system-lpstop-values' -and $_.logicalCases -eq 12160 }).Count -ne 1) {
                    throw 'LPSTOP mutation did not execute its complete two-batch selection'
                }
            }
            if ($legacyPresent) {
                [xml]$legacyTrx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if (@($legacyTrx.TestRun.Results.UnitTestResult | Where-Object { $_.testName.EndsWith($mutation.legacyTest) -and $_.outcome -eq 'Failed' }).Count -ne 1) { throw 'Original regression did not detect the same mutation' }
            }
            $results += @{mutation=$mutation.name; file=$mutation.file; sourceSha256=(Get-FileHash -LiteralPath $path).Hash;
                detected=$true; replacementCase=$failures[0].id; diagnostic=$failures[0].reason; originalRegressionDetected=$legacyPresent;
                reports=@($batches | ForEach-Object { @{group=$_.group; logicalCases=$_.logicalCases; counts=$_.counts} })}
            Write-Host "Detected $($mutation.name): $($failures[0].id)"
        }
        finally { [IO.File]::WriteAllBytes($path, $saved[$path]) }
    }
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'mutation-proof.json')
}
finally {
    foreach ($path in $saved.Keys) { [IO.File]::WriteAllBytes($path, $saved[$path]) }
    [Environment]::SetEnvironmentVariable('COPPER68K_SYNTHETIC_REPORT_DIR', $reportDirectory)
    # Rebuild the restored implementation; never leave mutated binaries behind.
    & dotnet build Copper68k.Tests/Copper68k.Tests.csproj -c Release --no-restore *> (Join-Path $output 'restored-build.log')
    $restoredExit = $LASTEXITCODE
    Pop-Location
    if ($restoredExit -ne 0) { throw 'Restored implementation failed to rebuild' }
}
