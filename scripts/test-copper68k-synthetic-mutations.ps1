[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/synthetic-mutations', [ValidateSet('All','Move','Arithmetic','Logical','Control','Consolidation','Rte040','RteValidationFault','RteRepair','RteRetryTrace','RtePendingTrace','UserRteFault','InstructionFault','HandlerPrefetch','EntryPrefetch','AccessDoubleFault','BatchFault','LowPowerStop','CacheEncodings')] [string]$Scope = 'All')
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
    @{name='move16-postincrement'; file='Copper68k/M68kAdvancedTimingInterpreter.System.cs'; before='        if (form == 4 || form < 2) WriteGeneralRegister(true, register, unchecked(State.A[register] + 16));'; after='        if (form == 4 || form < 2) WriteGeneralRegister(true, register, unchecked(State.A[register] + 4));'; group='Move16TransfersAndBreakpoints'; milestone=5; model='68040'},
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
      after='            if (false)'; group='SyntheticLowPowerStopTests'; milestone=6; model='68060'; lpstop=$true;
      legacyTest='Move16CacheInstructionsBreakpointsAndLowPowerStop'; legacyFile='Copper68k.Tests/Synthetic/SyntheticModelSystemTests.cs'; legacyModel='68060'}
)
$mutations += @(
    @{name='cache-scope-linef'; file=$advanced; before=@'
            if (scope == 0)
            {
                RaiseFormat0Exception(4, pc, M68kInstructionTimingKey.IllegalInstruction);
'@; after=@'
            if (scope == 0)
            {
                RaiseFormat0Exception(11, pc, M68kInstructionTimingKey.LineFException);
'@; group='EveryCacheOpcodeIncludesInvalidScopeAndNeitherCache'; milestone=6; model='68040'; cache=$true},
    @{name='cache-scope-privilege-priority'; file=$advanced; before=@'
            var scope = (opcode >> 3) & 3;
            if (scope == 0)
'@; after=@'
            var scope = (opcode >> 3) & 3;
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            { RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation); return true; }
            if (scope == 0)
'@; group='EveryCacheOpcodeIncludesInvalidScopeAndNeitherCache'; milestone=6; model='68040'; cache=$true},
    @{name='cache-neither-privilege'; file=$advanced; before=@'
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0)
            {
                RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }
            var caches = (opcode >> 6) & 3;
'@; after=@'
            if ((State.StatusRegister & M68kCpuState.Supervisor) == 0 && ((opcode >> 6) & 3) != 0)
            {
                RaiseFormat0Exception(8, pc, M68kInstructionTimingKey.PrivilegeViolation);
                return true;
            }
            var caches = (opcode >> 6) & 3;
'@; group='EveryCacheOpcodeIncludesInvalidScopeAndNeitherCache'; milestone=6; model='68040'; cache=$true},
    @{name='cache-preserve-x'; file=$advanced; before='            var caches = (opcode >> 6) & 3;'; after=@'
            State.SetFlag(M68kCpuState.Extend, true);
            var caches = (opcode >> 6) & 3;
'@; group='EveryCacheOpcodeIncludesInvalidScopeAndNeitherCache'; milestone=6; model='68040'; cache=$true;
      legacyTest='Move16CacheInstructionsAndBreakpoints'; legacyFile='Copper68k.Tests/Synthetic/SyntheticModelSystemTests.cs'; legacyModel='68040'},
    @{name='cache-extension-length'; file=$advanced; before=@'
            _ = FetchWord();
            var scope = (opcode >> 3) & 3;
'@; after=@'
            _ = FetchWord();
            _ = FetchWord();
            var scope = (opcode >> 3) & 3;
'@; group='EveryCacheOpcodeIncludesInvalidScopeAndNeitherCache'; milestone=6; model='68040'; cache=$true;
      legacyTest='Move16CacheInstructionsAndBreakpoints'; legacyFile='Copper68k.Tests/Synthetic/SyntheticModelSystemTests.cs'; legacyModel='68040'}
)
$mutations += @(
    @{name='rte-validation-frame'; file='Copper68k/M68040Support.cs'; before='            PushWord(0x7008);'; after='            PushWord(0x0008);'; group='SyntheticM68040RteValidationFaultTests'; milestone=6; model='68040'; validationFault=$true},
    @{name='rte-validation-width'; file='Copper68k/M68040Support.cs'; before='(fault.ByteCount == 2 ? 0x0040 : 0)'; after='(fault.ByteCount == 2 ? 0 : 0x0040)'; group='SyntheticM68040RteValidationFaultTests'; milestone=6; model='68040'; validationFault=$true},
    @{name='rte-validation-address'; file='Copper68k/M68040Support.cs'; before='            PushLong(fault.LogicalAddress);'; after='            PushLong(fault.LogicalAddress + 1);'; group='SyntheticM68040RteValidationFaultTests'; milestone=6; model='68040'; validationFault=$true},
    @{name='rte-validation-saved-pc'; file='Copper68k/M68040Support.cs'; before='                    RaiseRteValidationAccessFault(fault, stackedProgramCounter);'; after='                    RaiseRteValidationAccessFault(fault, stackedProgramCounter + 2);'; group='SyntheticM68040RteValidationFaultTests'; milestone=6; model='68040'; validationFault=$true},
    @{name='rte-validation-continuation-read'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs'; before='var address = continuation == 0 ? 0u : ReadRteFrameLong(frame + 8);'; after='var address = continuation == 0 ? 0u : ReadLong(frame + 8);'; group='SyntheticM68040RteValidationFaultTests'; milestone=6; model='68040'; validationFault=$true}
)
$doubleFaultMutations = @(
    @{name='040-double-fault-latch'; file='Copper68k/M68040Support.cs'; before='            _accessErrorDoubleFaultHalted = true;'; after='            _accessErrorDoubleFaultHalted = false;'},
    @{name='040-double-fault-reset'; file='Copper68k/M68040Support.cs'; before='            _accessErrorDoubleFaultHalted = false;'; after='            // Mutant: external reset retains fatal latch.'},
    @{name='040-compiled-double-fault-halt'; file='Copper68k/M68kJitCore.cs'; before='                ((M68040Interpreter)_fallback).LatchAccessErrorDoubleFault();'; after='                // Mutant: compiled fatal entry fails to latch HALT.'},
    @{name='040-compiled-master-stack'; file='Copper68k/M68kJitCore.cs'; before=@'
        private void RaiseM68040Format0Exception(int vector, uint stackedProgramCounter, int cycles)
        {
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(vector, stackedProgramCounter, savedStatusRegister);
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~0xc000);
'@; after=@'
        private void RaiseM68040Format0Exception(int vector, uint stackedProgramCounter, int cycles)
        {
            var savedStatusRegister = State.StatusRegister;
            State.RecordException(vector, stackedProgramCounter, savedStatusRegister);
            State.StatusRegister = (ushort)((State.StatusRegister | M68kCpuState.Supervisor) & ~M68kCpuState.Master);
'@}
)
foreach ($size in @('Byte','Word','Long')) {
    $doubleFaultMutations += @{name="040-classic-read-$size"; file='Copper68k/M68kJitCore.cs';
        before="            if (_cpuModel != M68kJitCpuModel.M68000) return Read$size(address);";
        after="            // Mutant: classic $size read bypasses model-aware helper."}
    $cast = if ($size -eq 'Byte') {'(byte)'} elseif ($size -eq 'Word') {'(ushort)'} else {''}
    $doubleFaultMutations += @{name="040-classic-write-$size"; file='Copper68k/M68kJitCore.cs';
        before="            if (_cpuModel != M68kJitCpuModel.M68000) { Write$size(address, ${cast}value); return; }";
        after="            // Mutant: classic $size write bypasses model-aware helper."}
}
foreach ($mutation in $doubleFaultMutations) {
    $mutation.group='WarmDispatchHaltsBothRteFallbackAndOperandFaultEntry'; $mutation.model='68040'
    $mutation.milestone=6; $mutation.doubleFault=$true; $mutations += $mutation
}
$slowFault = @'
                ExecuteInstructionWithTrace();
            }
            catch (M68040MmuFaultException ex)
            {
                if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;
            }
'@
$hotFault = @'
                        exitBlock = true;
                    }
                    else ExecuteHotInstruction(hotInstruction.Kind, opcode);
                }
                catch (M68040MmuFaultException ex)
                {
                    if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;
                    exitBlock = true;
                }
                boundary.AfterInstruction(previousCycle, State.Cycles);
                executedInstructions++;
'@
$modelFault = @'
                        if (!TryExecuteFastModelSpecificInstruction(opcode))
                            throw new InvalidOperationException("The MC68040 FPU hot instruction was not handled.");
                    }
                    else ExecuteHotInstruction(hotInstruction.Kind, opcode);
                }
                catch (M68040MmuFaultException ex)
                {
                    if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;
                    exitBlock = true;
                }
'@
$selfFault = @'
                    opcode = _timedBus.ReadInstructionFetchWordHot(
                        hotInstruction.Address, out cacheHit,
                        out requiresSynchronization, out completedMachineCycle);
                }
                catch (M68040MmuFaultException ex)
                {
                    if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;
                    boundary.AfterInstruction(previousCycle, State.Cycles);
                    executedInstructions++;
                    return true;
                }
'@
$retryFault = @'
        protected override bool TryHandleM68040ExecutionFault(M68040MmuFault fault)
        {
            // The prefetch bus address may cover an extension or the other
            // half of an aligned long. Restart the executing instruction.
            if (!State.M68040Mmu.Enabled && fault.AccessKind == M68kBusAccessKind.CpuInstructionFetch)
                fault = fault with { StackedProgramCounter = ExecutionBoundaryProgramCounter };
            RaiseMmuFault(fault);
            return true;
        }
'@
$batchFaultMutations = @(
    @{name='040-batch-cold-delivery'; file=$advanced; before=$slowFault; after=$slowFault.Replace('if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;', 'throw;'); proofGroup='rte-validation-batch'; proofCombination='cached=False'},
    @{name='040-batch-hot-delivery'; file=$advanced; before=$hotFault; after=$hotFault.Replace('if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;', 'throw;'); proofGroup='access-fault-batch-dispatch'; proofCombination='/batch/load/'},
    @{name='040-batch-model-delivery'; file=$advanced; before=$modelFault; after=$modelFault.Replace('if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;', 'throw;'); proofGroup='access-fault-batch-dispatch'; proofCombination='/batch/mixed-load/'},
    @{name='040-batch-self-delivery'; file=$advanced; before=$selfFault; after=$selfFault.Replace('if (!TryHandleM68040ExecutionFault(ex.Fault)) throw;', 'throw;'); proofGroup='access-fault-batch-dispatch'; proofCombination='/batch/self-fetch/'},
    @{name='040-batch-self-count'; file=$advanced; before=$selfFault; after=$selfFault.Replace('executedInstructions++;', 'executedInstructions += 2;'); proofGroup='access-fault-batch-dispatch'; proofCombination='/batch/self-fetch/'},
    @{name='040-batch-hot-callback'; file=$advanced; before=$hotFault; after=$hotFault.Replace('boundary.AfterInstruction(previousCycle, State.Cycles);', 'if (!exitBlock) boundary.AfterInstruction(previousCycle, State.Cycles);'); proofGroup='access-fault-batch-dispatch'; proofCombination='/batch/load/'},
    @{name='040-batch-retry-partial'; file='Copper68k/M68040Support.cs'; before=$retryFault; after=@'
        protected override bool TryHandleM68040ExecutionFault(M68040MmuFault fault)
        {
            var failedPc = State.LastInstructionProgramCounter;
            RaiseMmuFault(fault);
            if (!State.Halted)
            {
                State.ProgramCounter = failedPc;
                base.ExecuteInstruction();
            }
            return true;
        }
'@; proofGroup='access-fault-batch-dispatch'; proofCombination='/batch/partial-store/'}
)
foreach ($mutation in $batchFaultMutations) {
    $mutation.group='SyntheticM68040BatchFaultTests'; $mutation.model='68040'
    $mutation.milestone=6; $mutation.batchFault=$true; $mutations += $mutation
}
$repairMutations = @(
    @{name='040-repair-short-pc'; file=$advanced; before='                State.ProgramCounter = restoredPc;';
      after='                State.ProgramCounter = restoredPc + 2;'; proofCombination='/repair/format0/'; proofPhase='/retry-RTE'},
    @{name='040-repair-movem-ea'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs';
      before='            if (continuation == 0x1000) _m68040MovemContinuation = (pc, address);';
      after='            if (continuation == 0x1000) _m68040MovemContinuation = (pc, address + 4);'; proofCombination='/repair/CM/'; proofPhase='/following'},
    @{name='040-repair-pending-sr'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs';
      before='            PushWord(sr);'; after='            PushWord(priorSr);'; proofCombination='/repair/CT/'; proofPhase='/retry-RTE'}
)
foreach ($mutation in $repairMutations) {
    $mutation.group='SyntheticM68040RteRepairTests'; $mutation.model='68040'
    $mutation.milestone=6; $mutation.repair=$true; $mutations += $mutation
}
$retryTraceMutations = @(
    @{name='040-retry-trace-restored-bits'; file=$advanced;
      before='            if ((trace & 0x8000) != 0 || flow || exception)';
      after='            if ((State.StatusRegister & 0xc000) != 0 && ((trace & 0x8000) != 0 || flow || exception))';
      proofCombination='/incoming=8000/T=0000/'; proofPhase='/retry-RTE'; proofReason='PC expected'},
    @{name='040-retry-trace-t0-rte'; file=$advanced;
      before='traceOpcode is 0x4E72 or 0x4E73 or 0x4E74 or 0x4E75 or 0x4E77 or 0x007C or 0x027C or 0x0A7C ||';
      after='traceOpcode is 0x4E72 or 0x4E74 or 0x4E75 or 0x4E77 or 0x007C or 0x027C or 0x0A7C ||';
      proofCombination='/incoming=4000/T=0000/'; proofPhase='/retry-RTE'; proofReason='PC expected'},
    @{name='040-retry-trace-movem-handler-consumption'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs';
      before='        if (_m68040MovemContinuation is not { } saved || saved.Pc != State.ProgramCounter) return false;';
      after='        if (_m68040MovemContinuation is not { } saved) return false;';
      proofCombination='/retry-trace/CM/'; proofPhase='/following'; proofReason='D0 expected'}
)
foreach ($mutation in $retryTraceMutations) {
    $mutation.group='RepairWithUntouchedIncomingTrace'; $mutation.model='68040'; $mutation.milestone=6
    $mutation.retryTrace=$true; $mutations += $mutation
}
$pendingTraceMutations = @(
    @{name='040-pending-trace-extra-rte-trace'; file=$advanced;
      before='            if (exception && (_profile.Model is M68kAcceleratorModel.M68040 or M68kAcceleratorModel.M68060 ||';
      after='            if (exception && State.LastExceptionVector is not (9 or 11 or 49) && (_profile.Model is M68kAcceleratorModel.M68040 or M68kAcceleratorModel.M68060 ||';
      proofCombination='/pending-trace/CT/'; proofPhase='/retry-RTE'; proofReason='A7 expected'},
    @{name='040-pending-trace-saved-incoming-sr'; file='Copper68k/M68kAdvancedTimingInterpreter.Rte.cs';
      before='            PushWord(sr);'; after='            PushWord(priorSr);';
      proofCombination='/pending-trace/CT/'; proofPhase='/retry-RTE'; proofReason='Memory'},
    @{name='040-pending-trace-following-bra-suppressed'; file=$advanced;
      before='            if ((trace & 0x8000) != 0 || flow || exception)';
      after='            if (State.ProgramCounter != State.LastInstructionProgramCounter && ((trace & 0x8000) != 0 || flow || exception))';
      proofCombination='/incoming=8000/T=8000/'; proofPhase='/following'; proofReason='PC expected'}
)
foreach ($mutation in $pendingTraceMutations) {
    $mutation.group='PendingRepairWithPreservedIncomingTrace'; $mutation.model='68040'; $mutation.milestone=6
    $mutation.pendingTrace=$true; $mutations += $mutation
}
$instructionMutations = @(
    @{name='040-instruction-short-frame'; before='                    RaiseUnbufferedReadAccessFault(fault, stackedProgramCounter, instruction: true);';
      after='                    RaiseFormat0Exception(VectorBusError, stackedProgramCounter, M68kInstructionTimingKey.IllegalInstruction);'; proofCombination='/instruction/opcode/'},
    @{name='040-instruction-data-tm'; before='(instruction ? 2 : 1)'; after='1'; proofCombination='/instruction/opcode/'},
    @{name='040-instruction-prefetch-pc'; before='                fault = fault with { StackedProgramCounter = ExecutionBoundaryProgramCounter };';
      after='                fault = fault with { StackedProgramCounter = fault.LogicalAddress };'; proofCombination='/instruction/extension-low/'}
)
foreach ($mutation in $instructionMutations) {
    $mutation.file='Copper68k/M68040Support.cs'; $mutation.group='SyntheticM68040InstructionFaultTests'; $mutation.model='68040'
    $mutation.milestone=6; $mutation.instructionFault=$true; $mutation.proofPhase='/fault-entry'; $mutations += $mutation
}
$mutations += @{
    name='040-host-reader-speculative-fetch'; file=$advanced; before=@'
            _codeReader = bus is M68040LogicalBus { HasHostCodeReader: false }
                ? null : bus as IM68kCodeReader;
'@; after='            _codeReader = bus as IM68kCodeReader;';
    group='FetchFaultAfterHandlerExecutionStartsANewException'; model='68040'; milestone=6; handlerPrefetch=$true
}
$entryMutations = @(
    @{name='040-entry-missing-window'; file='Copper68k/M68040Support.cs'; before='                if (_timedBus.PrefetchM68040AccessErrorHandler(State.ProgramCounter))';
      after='                if (State.Stopped) // Mutant: omit handler entry prefetch.'; group='EveryRequired'; reportPrefix='handler-prefetch-entry-'; cases=196608; proofCombination='/byte=0/'; proofReason='Handler-entry prefetch must halt'},
    @{name='040-entry-final-long'; file='Copper68k/M68kTimingEngine.cs'; before='            _exceptionFetchFourth = ReadM68040InstructionLong(unchecked(_exceptionFetchBase + 12), out _);';
      after='            _exceptionFetchFourth = 0; // Mutant: only three entry longs.'; group='EveryRequired'; reportPrefix='handler-prefetch-entry-'; cases=196608; proofCombination='/byte=12/'; proofReason='Handler-entry prefetch must halt'},
    @{name='040-entry-refetch'; file='Copper68k/M68kTimingEngine.cs'; before='            _exceptionFetchValid = true;';
      after='            _exceptionFetchValid = false; // Mutant: discard acquired entry data.'; group='EntryFetchDataIsRetainedUntilConsumedOrItsContextChanges'; reportPrefix='handler-prefetch-retention-'; cases=7680; proofCombination='/handler-retention/linear/'; proofReason='Retained entry value differs'},
    @{name='040-entry-subroutine-context'; file='Copper68k/M68040Support.cs'; before=@'
            _timedBus.ResetInstructionFetchBuffer();
            base.BeginSubroutine(address, stackPointer, returnAddress);
'@; after='            base.BeginSubroutine(address, stackPointer, returnAddress);'; group='EntryFetchDataIsRetainedUntilConsumedOrItsContextChanges'; reportPrefix='handler-prefetch-retention-'; cases=7680; proofCombination='/handler-retention/subroutine/'; proofReason='Entry context invalidation differs'},
    @{name='040-entry-task-context'; file='Copper68k/M68040Support.cs'; before=@'
            State.CopyTaskContextFrom(next);
            _timedBus.ResetInstructionFetchBuffer();
            DiscardInstructionPrefetch();
'@; after='            State.CopyTaskContextFrom(next);'; group='EntryFetchDataIsRetainedUntilConsumedOrItsContextChanges'; reportPrefix='handler-prefetch-retention-'; cases=7680; proofCombination='/handler-retention/task/'; proofReason='Entry context invalidation differs'}
)
foreach ($mutation in $entryMutations) {
    $mutation.model='68040'; $mutation.milestone=6; $mutation.entryPrefetch=$true; $mutations += $mutation
}
$userRteMutations = @(
    @{name='040-user-validation-saved-s'; before=@'
            var savedSr = State.StatusRegister;
            State.RecordException(VectorBusError, instructionPc, savedSr);
'@; after=@'
            var savedSr = (ushort)(State.StatusRegister | M68kCpuState.Supervisor);
            State.RecordException(VectorBusError, instructionPc, savedSr);
'@; proofCombination='/M=False/'; proofReason='saved SR expected'},
    @{name='040-user-validation-supervisor-tm'; before='            var modifier = ((savedSr & M68kCpuState.Supervisor) != 0 ? 4 : 0) | (instruction ? 2 : 1);';
      after='            var modifier = ((savedSr & M68kCpuState.Supervisor) != 0 ? 4 : 0) | (instruction ? 2 : 5);'; proofCombination='/M=False/'; proofReason='Memory'},
    @{name='040-user-validation-forced-isp'; before=@'
            State.RecordException(VectorBusError, instructionPc, savedSr);
            State.StatusRegister = (ushort)((savedSr | M68kCpuState.Supervisor) & ~0xc000);
'@; after=@'
            State.RecordException(VectorBusError, instructionPc, savedSr);
            State.StatusRegister = (ushort)((savedSr | M68kCpuState.Supervisor) & ~0xd000);
'@; proofCombination='/M=True/'; proofReason='SR expected'}
)
foreach ($mutation in $userRteMutations) {
    $mutation.file='Copper68k/M68040Support.cs'; $mutation.model='68040'; $mutation.milestone=6
    $mutation.group='UserTailFaultsAndBareReturnsUseLiveStatus'; $mutation.userRte=$true; $mutations += $mutation
}
if ($Scope -eq 'Move') { $mutations = @($mutations | Where-Object { -not $_.milestone }) }
if ($Scope -eq 'Arithmetic') { $mutations = @($mutations | Where-Object { $_.milestone -eq 3 }) }
if ($Scope -eq 'Logical') { $mutations = @($mutations | Where-Object { $_.milestone -eq 4 }) }
if ($Scope -eq 'Control') { $mutations = @($mutations | Where-Object { $_.milestone -eq 5 }) }
if ($Scope -eq 'Consolidation') { $mutations = @($mutations | Where-Object { $_.milestone -eq 6 }) }
if ($Scope -eq 'Rte040') { $mutations = @($mutations | Where-Object { $_.odd }) }
if ($Scope -eq 'RteValidationFault') { $mutations = @($mutations | Where-Object { $_.validationFault }) }
if ($Scope -eq 'RteRepair') { $mutations = @($mutations | Where-Object { $_.repair }) }
if ($Scope -eq 'RteRetryTrace') { $mutations = @($mutations | Where-Object { $_.retryTrace }) }
if ($Scope -eq 'RtePendingTrace') { $mutations = @($mutations | Where-Object { $_.pendingTrace }) }
if ($Scope -eq 'InstructionFault') { $mutations = @($mutations | Where-Object { $_.instructionFault }) }
if ($Scope -eq 'HandlerPrefetch') { $mutations = @($mutations | Where-Object { $_.handlerPrefetch }) }
if ($Scope -eq 'EntryPrefetch') { $mutations = @($mutations | Where-Object { $_.entryPrefetch }) }
if ($Scope -eq 'UserRteFault') { $mutations = @($mutations | Where-Object { $_.userRte }) }
if ($Scope -eq 'AccessDoubleFault') { $mutations = @($mutations | Where-Object { $_.doubleFault }) }
if ($Scope -eq 'BatchFault') { $mutations = @($mutations | Where-Object { $_.batchFault }) }
if ($Scope -eq 'LowPowerStop') { $mutations = @($mutations | Where-Object { $_.lpstop }) }
if ($Scope -eq 'CacheEncodings') { $mutations = @($mutations | Where-Object { $_.cache }) }
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
            if ($mutation.repair) {
                # Keep the original repair proof's two complete batches; the
                # new retained-trace facts have their own four-route gate.
                $filter = 'FullyQualifiedName~ExecutedRepairRestoresAllCcrAndTraceStates|FullyQualifiedName~ExecutedRepairRetriesEveryValidationReadAfterCommittedThrowaways'
            }
            $legacyPresent = $mutation.legacyTest -and [IO.File]::ReadAllText((Join-Path $repo $mutation.legacyFile)).Contains("void $($mutation.legacyTest)(")
            if ($legacyPresent) {
                $filter += "|FullyQualifiedName~$($mutation.legacyTest)"
                if ($mutation.legacyModel) { $filter += "&DisplayName~$($mutation.legacyModel)" }
            }
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
            if ($mutation.validationFault) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.group -eq 'rte-validation-physical-direct' -and $_.logicalCases -eq 162816 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.group -eq 'rte-validation-physical-chained' -and $_.logicalCases -eq 61056 }).Count -ne 1) {
                    throw 'RTE validation-fault mutation did not execute its complete two-batch selection'
                }
            }
            if ($mutation.handlerPrefetch) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'handler-prefetch-executing-scalar' -and $_.logicalCases -eq 12288 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'handler-prefetch-executing-batch' -and $_.logicalCases -eq 12288 }).Count -ne 1 -or
                    @($failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains('/route=batch/', [StringComparison]::Ordinal) -and
                        $_.reason.StartsWith('Denied cold batch', [StringComparison]::Ordinal) }).Count -eq 0) {
                    throw 'Host-reader mutation omitted complete cases or intended boundary failure'
                }
            }
            if ($mutation.entryPrefetch) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq ($mutation.reportPrefix + 'scalar') -and $_.logicalCases -eq $mutation.cases }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq ($mutation.reportPrefix + 'batch') -and $_.logicalCases -eq $mutation.cases }).Count -ne 1 -or
                    @($failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                        $_.reason.StartsWith($mutation.proofReason, [StringComparison]::Ordinal) }).Count -eq 0) {
                    throw 'Entry-prefetch mutation omitted complete cases or intended semantic failure'
                }
            }
            if ($mutation.userRte) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $trx.TestRun.ResultSummary.Counters.failed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'rte-user-fault-scalar' -and $_.logicalCases -eq 168192 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'rte-user-fault-batch' -and $_.logicalCases -eq 168192 }).Count -ne 1) {
                    throw 'User-tail mutation omitted complete scalar/batch selections'
                }
                foreach ($route in @('scalar','batch')) {
                    $routeFailures = @($batches | Where-Object group -CEQ "rte-user-fault-$route")[0].failures
                    if (@($routeFailures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                        $_.id.EndsWith('/fault-entry', [StringComparison]::Ordinal) -and $_.reason.StartsWith($mutation.proofReason, [StringComparison]::Ordinal) }).Count -eq 0) {
                        throw "User-tail mutation omitted intended $route architectural failure"
                    }
                }
            }
            if ($mutation.instructionFault) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'instruction-fault-frame' -and $_.logicalCases -eq 36864 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'instruction-fault-restart' -and $_.logicalCases -eq 79872 }).Count -ne 1 -or
                    @($failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                        $_.id.EndsWith($mutation.proofPhase, [StringComparison]::Ordinal) }).Count -eq 0) {
                    throw 'Instruction-fault mutation omitted complete cases or intended semantic failure'
                }
            }
            if ($mutation.retryTrace -or $mutation.pendingTrace) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 4 -or $trx.TestRun.ResultSummary.Counters.failed -ne 4 -or $batches.Count -ne 4) {
                    throw 'Retry-trace mutation omitted its complete four-batch selection'
                }
                foreach ($matrix in @('boundaries','chained')) {
                    foreach ($route in @('scalar','batch')) {
                        $prefix = if ($mutation.pendingTrace) {'rte-pending-trace'} else {'rte-retry-trace'}
                        $cases = if ($mutation.pendingTrace) {if ($matrix -eq 'boundaries') {41472} else {870912}} else {if ($matrix -eq 'boundaries') {92736} else {1271808}}
                        $target = @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq "$prefix-$matrix-$route" })
                        if ($target.Count -ne 1 -or $target[0].logicalCases -ne $cases -or
                            @($target[0].failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                                $_.id.EndsWith($mutation.proofPhase, [StringComparison]::Ordinal) -and $_.reason.StartsWith($mutation.proofReason, [StringComparison]::Ordinal) }).Count -eq 0) {
                            throw "Retry-trace mutation omitted intended $matrix/$route semantic failure"
                        }
                    }
                }
            }
            if ($mutation.repair) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'rte-repair-boundaries' -and $_.logicalCases -eq 143424 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'rte-repair-chained' -and $_.logicalCases -eq 768960 }).Count -ne 1) {
                    throw 'RTE repair mutation omitted its complete two-batch selection'
                }
                if (@($failures | Where-Object {
                    $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                    $_.id.EndsWith($mutation.proofPhase, [StringComparison]::Ordinal)
                }).Count -eq 0) { throw "RTE repair mutation did not detect its intended phase: $($mutation.name)" }
            }
            if ($mutation.batchFault) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 2 -or $batches.Count -ne 2 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'rte-validation-batch' -and $_.logicalCases -eq 129024 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -ceq '68040' -and $_.group -ceq 'access-fault-batch-dispatch' -and $_.logicalCases -eq 10368 }).Count -ne 1) {
                    throw 'Batch-fault mutation omitted its complete two-batch selection'
                }
                $target = @($batches | Where-Object group -CEQ $mutation.proofGroup)[0]
                if (@($target.combinations.PSObject.Properties | Where-Object {
                    $_.Name.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and $_.Value.mismatching -gt 0
                }).Count -eq 0) { throw "Batch-fault mutation did not detect its intended path: $($mutation.name)" }
            }
            if ($mutation.doubleFault) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if ($trx.TestRun.ResultSummary.Counters.executed -ne 3 -or $batches.Count -ne 3) { throw 'Double-fault mutation omitted dispatch batches' }
                foreach ($engine in @('accurate','v1','v2')) {
                    if (@($batches | Where-Object { $_.model -eq '68040' -and $_.group -ceq "access-double-fault-dispatch-$engine" -and $_.logicalCases -eq 1088 }).Count -ne 1) {
                        throw "Double-fault mutation omitted complete $engine selection"
                    }
                }
            }
            if ($mutation.lpstop) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                $expectedBatches = 2 + [int][bool]$legacyPresent
                if ($trx.TestRun.ResultSummary.Counters.executed -ne $expectedBatches -or $batches.Count -ne $expectedBatches -or
                    @($batches | Where-Object { $_.model -eq '68060' -and $_.group -eq 'system-lpstop-extensions' -and $_.logicalCases -eq 131070 }).Count -ne 1 -or
                    @($batches | Where-Object { $_.model -eq '68060' -and $_.group -eq 'system-lpstop-values' -and $_.logicalCases -eq 17024 }).Count -ne 1 -or
                    ($legacyPresent -and @($batches | Where-Object { $_.model -eq '68060' -and $_.group -eq 'system-model' -and $_.logicalCases -eq 13120 }).Count -ne 1)) {
                    throw 'LPSTOP mutation did not execute its complete two-batch selection'
                }
            }
            if ($mutation.cache) {
                [xml]$trx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                $expectedBatches = 1 + [int][bool]$legacyPresent
                if ($trx.TestRun.ResultSummary.Counters.executed -ne $expectedBatches -or $batches.Count -ne $expectedBatches -or
                    @($batches | Where-Object { $_.model -eq '68040' -and $_.group -eq 'system-cache-encodings' -and $_.logicalCases -eq 16384 }).Count -ne 1 -or
                    ($legacyPresent -and @($batches | Where-Object { $_.model -eq '68040' -and $_.group -eq 'system-model' -and $_.logicalCases -eq 12800 }).Count -ne 1)) {
                    throw 'Cache mutation did not execute its complete selection'
                }
            }
            if ($legacyPresent) {
                [xml]$legacyTrx = Get-Content -LiteralPath (Join-Path $directory 'mutation.trx') -Raw
                if (@($legacyTrx.TestRun.Results.UnitTestResult | Where-Object { ($_.testName.EndsWith($mutation.legacyTest) -or $_.testName.Contains($mutation.legacyTest + '(')) -and $_.outcome -eq 'Failed' }).Count -ne 1) { throw 'Original regression did not detect the same mutation' }
            }
            $proofFailure = if ($mutation.handlerPrefetch) {
                @($failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains('/route=batch/', [StringComparison]::Ordinal) -and
                    $_.reason.StartsWith('Denied cold batch', [StringComparison]::Ordinal) })[0]
            } elseif ($mutation.entryPrefetch) {
                @($failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                    $_.reason.StartsWith($mutation.proofReason, [StringComparison]::Ordinal) })[0]
            } elseif ($mutation.retryTrace -or $mutation.pendingTrace) {
                @($failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                    $_.id.EndsWith($mutation.proofPhase, [StringComparison]::Ordinal) -and $_.reason.StartsWith($mutation.proofReason, [StringComparison]::Ordinal) })[0]
            } elseif ($mutation.userRte) {
                @($failures | Where-Object { $_.status -ceq 'mismatching' -and $_.id.Contains($mutation.proofCombination, [StringComparison]::Ordinal) -and
                    $_.id.EndsWith('/fault-entry', [StringComparison]::Ordinal) -and $_.reason.StartsWith($mutation.proofReason, [StringComparison]::Ordinal) })[0]
            } else { $failures[0] }
            $results += @{mutation=$mutation.name; file=$mutation.file; sourceSha256=(Get-FileHash -LiteralPath $path).Hash;
                detected=$true; replacementCase=$proofFailure.id; diagnostic=$proofFailure.reason; originalRegressionDetected=$legacyPresent;
                proofGroup=$mutation.proofGroup; proofCombination=$mutation.proofCombination; proofPhase=$mutation.proofPhase;
                reports=@($batches | ForEach-Object { @{group=$_.group; logicalCases=$_.logicalCases; counts=$_.counts} })}
            Write-Host "Detected $($mutation.name): $($proofFailure.id)"
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
